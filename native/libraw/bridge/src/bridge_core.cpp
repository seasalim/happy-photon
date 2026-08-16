#include "bridge_internal.hpp"

#include <algorithm>
#include <cstring>
#include <limits>
#include <thread>

namespace hplr {

std::mutex registry_mutex;
std::unordered_map<hplr_handle, std::shared_ptr<Context>> handles;
std::unordered_map<hplr_allocation, std::unique_ptr<Allocation>> allocations;
std::atomic<uint64_t> next_token{1};
#if !defined(HPLR_LIBRAW_REENTRANT)
std::mutex libraw_mutex;
#endif

Allocation::~Allocation() {
    if (image) {
#if defined(HPLR_LIBRAW_REENTRANT)
        LibRaw::dcraw_clear_mem(image);
#else
        std::lock_guard<std::mutex> lock(libraw_mutex);
        LibRaw::dcraw_clear_mem(image);
#endif
    }
    if (registered)
        if (auto context = owner.lock()) context->allocations.fetch_sub(1);
}
#if defined(HPLR_TESTING)
std::atomic<bool> fail_next_allocation{false};
#endif

bool valid_error(hplr_error *error) {
    return error && error->abi_version == HPLR_ABI_VERSION &&
           error->struct_size == sizeof(hplr_error) &&
           (!error->text_capacity || error->text);
}

void clear_error(hplr_error *error) {
    error->error_class = HPLR_ERROR_NONE;
    error->native_code = 0;
    error->text_length = 0;
}

int32_t fail(hplr_error *error, hplr_status status, hplr_error_class error_class,
             int native_code, const char *message) {
    if (valid_error(error)) {
        error->error_class = error_class;
        error->native_code = native_code;
        const size_t length = message ? std::strlen(message) : 0;
        error->text_length = static_cast<uint32_t>(std::min<size_t>(length,
            error->text_capacity));
        if (error->text_length)
            std::memcpy(error->text, message, error->text_length);
    }
    return status;
}

int32_t libraw_fail(hplr_error *error, int code) {
    return fail(error, HPLR_E_LIBRAW, HPLR_ERROR_LIBRAW, code,
                libraw_strerror(code));
}

bool valid_header(const void *value, uint32_t abi, uint32_t size,
                  uint32_t expected_size, hplr_error *error) {
    if (!value) {
        fail(error, HPLR_E_ARGUMENT, HPLR_ERROR_PROGRAMMING, 0,
             "required output or value is null");
        return false;
    }
    if (abi != HPLR_ABI_VERSION || size != expected_size) {
        fail(error, HPLR_E_ABI, HPLR_ERROR_ABI, 0,
             "unsupported ABI version or structure size");
        return false;
    }
    return true;
}

std::shared_ptr<Context> find_context(hplr_handle handle) {
    std::lock_guard<std::mutex> lock(registry_mutex);
    auto found = handles.find(handle);
    return found == handles.end() ? nullptr : found->second;
}

int32_t begin(hplr_handle handle, std::unique_ptr<Operation> &operation,
              hplr_error *error) {
    if (!handle)
        return fail(error, HPLR_E_HANDLE, HPLR_ERROR_PROGRAMMING, 0,
                    "handle token is zero");
    auto context = find_context(handle);
    if (!context)
        return fail(error, HPLR_E_HANDLE, HPLR_ERROR_PROGRAMMING, 0,
                    "handle token is unknown or closed");
    operation = std::make_unique<Operation>(std::move(context));
    if (!operation->try_lock())
        return fail(error, HPLR_E_BUSY, HPLR_ERROR_PROGRAMMING, 0,
                    "handle already has an operation in progress");
    return HPLR_OK;
}

uint64_t issue_token() {
    const auto token = next_token.fetch_add(1, std::memory_order_relaxed);
    if (!token || token == std::numeric_limits<uint64_t>::max())
        throw std::bad_alloc();
    return token;
}

static bool continuation(uint8_t value) { return (value & 0xc0) == 0x80; }

uint32_t copy_native_text(const char *source, size_t capacity, uint8_t *destination,
                          uint32_t destination_capacity) {
    if (!source || !destination_capacity) return 0;
    size_t length = 0;
    while (length < capacity && source[length]) ++length;
    uint32_t written = 0;
    size_t offset = 0;
    while (offset < length && written < destination_capacity) {
        const auto first = static_cast<uint8_t>(source[offset]);
        size_t count = 1;
        bool valid = first < 0x80;
        if (first >= 0xc2 && first <= 0xdf) count = 2;
        else if (first >= 0xe0 && first <= 0xef) count = 3;
        else if (first >= 0xf0 && first <= 0xf4) count = 4;
        if (count > 1 && offset + count <= length) {
            valid = true;
            for (size_t i = 1; i < count; ++i) valid &= continuation(
                static_cast<uint8_t>(source[offset + i]));
            if (count == 3) {
                const auto second = static_cast<uint8_t>(source[offset + 1]);
                valid &= !(first == 0xe0 && second < 0xa0) &&
                         !(first == 0xed && second >= 0xa0);
            } else if (count == 4) {
                const auto second = static_cast<uint8_t>(source[offset + 1]);
                valid &= !(first == 0xf0 && second < 0x90) &&
                         !(first == 0xf4 && second >= 0x90);
            }
        }
        if (valid) {
            if (written + count > destination_capacity) break;
            std::memcpy(destination + written, source + offset, count);
            written += static_cast<uint32_t>(count);
            offset += count;
        } else {
            constexpr uint8_t replacement[] = {0xef, 0xbf, 0xbd};
            if (written + sizeof(replacement) > destination_capacity) break;
            std::memcpy(destination + written, replacement, sizeof(replacement));
            written += sizeof(replacement);
            ++offset;
        }
    }
    return written;
}

int32_t validate_image(const libraw_processed_image_t *image, bool thumbnail,
                       hplr_error *error) {
    if (!image || !image->data_size)
        return fail(error, HPLR_E_INTERNAL, HPLR_ERROR_BRIDGE, 0,
                    "LibRaw returned an empty image descriptor");
    if (image->type == LIBRAW_IMAGE_BITMAP) {
        if (!image->width || !image->height || !image->colors ||
            (image->bits != 8 && image->bits != 16))
            return fail(error, HPLR_E_INTERNAL, HPLR_ERROR_BRIDGE, 0,
                        "LibRaw returned impossible bitmap dimensions");
        const uint64_t bytes = static_cast<uint64_t>(image->width) * image->height *
            image->colors * (image->bits / 8u);
        if (bytes != image->data_size)
            return fail(error, HPLR_E_INTERNAL, HPLR_ERROR_BRIDGE, 0,
                        "LibRaw bitmap length does not match its shape");
    } else if (!thumbnail) {
        return fail(error, HPLR_E_INTERNAL, HPLR_ERROR_BRIDGE, 0,
                    "processed output is not a bitmap");
    }
    return HPLR_OK;
}

} // namespace hplr

extern "C" uint32_t HPLR_CALL hplr_abi_version(void) { return HPLR_ABI_VERSION; }

extern "C" int32_t HPLR_CALL hplr_runtime(hplr_runtime_info *out_info,
                                            hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        if (!out_info)
            return hplr::fail(error, HPLR_E_ARGUMENT, HPLR_ERROR_PROGRAMMING, 0,
                              "runtime output is null");
        if (!hplr::valid_header(out_info, out_info->abi_version,
                out_info->struct_size, sizeof(*out_info), error)) return HPLR_E_ABI;
        out_info->libraw_version_number = static_cast<uint32_t>(LibRaw::versionNumber());
        out_info->capabilities = LibRaw::capabilities();
#if defined(HPLR_LIBRAW_REENTRANT)
        out_info->thread_safe_variant = 1;
#else
        out_info->thread_safe_variant = 0;
#endif
        out_info->version_string_length = hplr::copy_native_text(LibRaw::version(),
            HPLR_TEXT_CAPACITY, out_info->version_string, HPLR_TEXT_CAPACITY);
        return static_cast<int32_t>(HPLR_OK);
    });
}

#if defined(HPLR_TESTING)
#include "../tests/test_hooks.h"

extern "C" void HPLR_CALL hplr_test_fail_next_image_allocation(void) {
    hplr::fail_next_allocation.store(true);
}

extern "C" int32_t HPLR_CALL hplr_test_get_params(hplr_handle handle,
    hplr_test_params *out_value, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        if (!out_value)
            return hplr::fail(error, HPLR_E_ARGUMENT, HPLR_ERROR_PROGRAMMING, 0,
                              "test params output is null");
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        const auto &params = operation->get().raw.imgdata.params;
        std::copy(std::begin(params.gamm), std::end(params.gamm),
                  std::begin(out_value->gamma));
        std::copy(std::begin(params.user_mul), std::end(params.user_mul),
                  std::begin(out_value->user_mul));
        out_value->output_bits = params.output_bps;
        out_value->output_color = params.output_color;
        out_value->no_auto_bright = params.no_auto_bright;
        out_value->half_size = params.half_size;
        out_value->highlight = params.highlight;
        out_value->fbdd = params.fbdd_noiserd;
        out_value->use_camera_wb = params.use_camera_wb;
        out_value->use_auto_wb = params.use_auto_wb;
        out_value->use_camera_matrix = params.use_camera_matrix;
        out_value->user_flip = params.user_flip;
        out_value->use_fuji_rotate = params.use_fuji_rotate;
        out_value->use_p1_correction = params.use_p1_correction;
        return static_cast<int32_t>(HPLR_OK);
    });
}

extern "C" int32_t HPLR_CALL hplr_test_hold_operation(hplr_handle handle,
    uint32_t milliseconds, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        std::this_thread::sleep_for(std::chrono::milliseconds(milliseconds));
        return static_cast<int32_t>(HPLR_OK);
    });
}

extern "C" int32_t HPLR_CALL hplr_test_set_foveon(hplr_handle handle,
    uint32_t enabled, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        operation->get().raw.imgdata.rawdata.iparams.is_foveon = enabled ? 1u : 0u;
        return static_cast<int32_t>(HPLR_OK);
    });
}

extern "C" int32_t HPLR_CALL hplr_test_hold_libraw(hplr_handle handle,
    uint32_t milliseconds, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        hplr::invoke_libraw([&] {
            std::this_thread::sleep_for(std::chrono::milliseconds(milliseconds));
            return 0;
        });
        return static_cast<int32_t>(HPLR_OK);
    });
}
#endif
