#include "bridge_internal.hpp"

#include <algorithm>
#include <climits>
#include <cstring>
#include <string>
#include <vector>
#if defined(_WIN32)
#include <windows.h>
#endif

namespace {

bool validate_utf8(const uint8_t *value, uint32_t length) {
    if (!value || !length || std::find(value, value + length, uint8_t{0}) != value + length)
        return false;
    uint32_t offset = 0;
    while (offset < length) {
        const uint8_t first = value[offset++];
        if (first < 0x80) continue;
        uint32_t count;
        if (first >= 0xc2 && first <= 0xdf) count = 1;
        else if (first >= 0xe0 && first <= 0xef) count = 2;
        else if (first >= 0xf0 && first <= 0xf4) count = 3;
        else return false;
        if (offset + count > length) return false;
        const uint8_t second = value[offset];
        if ((first == 0xe0 && second < 0xa0) || (first == 0xed && second >= 0xa0) ||
            (first == 0xf0 && second < 0x90) || (first == 0xf4 && second >= 0x90))
            return false;
        while (count--) if ((value[offset++] & 0xc0) != 0x80) return false;
    }
    return true;
}

int open_path(LibRaw &raw, const uint8_t *path, uint32_t length) {
#if defined(_WIN32)
    const int needed = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
        reinterpret_cast<const char *>(path), static_cast<int>(length), nullptr, 0);
    if (!needed) return LIBRAW_IO_ERROR;
    std::wstring wide(static_cast<size_t>(needed), L'\0');
    if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
        reinterpret_cast<const char *>(path), static_cast<int>(length), wide.data(),
        needed) != needed) return LIBRAW_IO_ERROR;
    wide.push_back(L'\0');
    return hplr::invoke_libraw([&] { return libraw_open_wfile(&raw.imgdata, wide.c_str()); });
#else
    std::string native(reinterpret_cast<const char *>(path), length);
    return hplr::invoke_libraw([&] { return raw.open_file(native.c_str()); });
#endif
}

int32_t require_no_lease(hplr::Context &context, hplr_error *error) {
    if (context.leases.load(std::memory_order_acquire))
        return hplr::fail(error, HPLR_E_BUSY, HPLR_ERROR_PROGRAMMING, 0,
                          "mosaic lease must be released first");
    return HPLR_OK;
}

int32_t require_no_allocation(hplr::Context &context, hplr_error *error) {
    if (context.allocations.load(std::memory_order_acquire))
        return hplr::fail(error, HPLR_E_BUSY, HPLR_ERROR_PROGRAMMING, 0,
                          "owned image allocation must be freed first");
    return HPLR_OK;
}

bool named_quality(int32_t value) {
    switch (value) {
    case 0:
    case 1:
    case 2:
    case 3:
    case 4:
    case 11:
    case 12:
        return true;
    default:
        return false;
    }
}

int32_t validate_crop(const hplr_output_config &config, const hplr::Context &context,
                      hplr_error *error) {
    constexpr uint32_t xtrans_filters = 9;
    if (!config.cropbox_present) return HPLR_OK;
    const auto &sizes = context.raw.imgdata.rawdata.sizes;
    const auto &rawdata = context.raw.imgdata.rawdata;
    const auto &identity = rawdata.iparams;
    const auto x = config.cropbox[0];
    const auto y = config.cropbox[1];
    const auto width = config.cropbox[2];
    const auto height = config.cropbox[3];
    if (!width || !height || x > INT_MAX || y > INT_MAX || width > INT_MAX ||
        height > INT_MAX || static_cast<uint64_t>(x) + width > sizes.width ||
        static_cast<uint64_t>(y) + height > sizes.height || rawdata.ioparams.fuji_width ||
        (identity.filters == xtrans_filters && (x % 6 || y % 6)) ||
        (identity.filters == 1 && (x % 16 || y % 16)))
        return hplr::fail(error, HPLR_E_ARGUMENT, HPLR_ERROR_PROGRAMMING, 0,
                          "crop must be a verbatim in-bounds visible-frame region");
    return HPLR_OK;
}

} // namespace

extern "C" int32_t HPLR_CALL hplr_open_utf8(const uint8_t *path,
    uint32_t path_length, hplr_handle *out_handle, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        if (!out_handle || !validate_utf8(path, path_length))
            return hplr::fail(error, HPLR_E_ARGUMENT, HPLR_ERROR_PROGRAMMING, 0,
                              "path must be non-empty well-formed UTF-8 without NUL");
        *out_handle = 0;
        auto context = std::make_shared<hplr::Context>();
        const int code = open_path(context->raw, path, path_length);
        if (code != LIBRAW_SUCCESS) return hplr::libraw_fail(error, code);
        const auto token = hplr::issue_token();
        {
            std::lock_guard<std::mutex> lock(hplr::registry_mutex);
            hplr::handles.emplace(token, std::move(context));
        }
        *out_handle = token;
        return static_cast<int32_t>(HPLR_OK);
    });
}

extern "C" int32_t HPLR_CALL hplr_close(hplr_handle handle, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        if ((status = require_no_lease(operation->get(), error)) != HPLR_OK) return status;
        if ((status = require_no_allocation(operation->get(), error)) != HPLR_OK) return status;
        std::lock_guard<std::mutex> lock(hplr::registry_mutex);
        auto found = hplr::handles.find(handle);
        if (found == hplr::handles.end() || found->second != operation->shared())
            return hplr::fail(error, HPLR_E_HANDLE, HPLR_ERROR_PROGRAMMING, 0,
                              "handle token is already closed");
        hplr::handles.erase(found);
        return static_cast<int32_t>(HPLR_OK);
    });
}

extern "C" int32_t HPLR_CALL hplr_unpack(hplr_handle handle, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        auto &context = operation->get();
        if (context.state != hplr::State::Open)
            return hplr::fail(error, HPLR_E_STATE, HPLR_ERROR_PROGRAMMING, 0,
                              "unpack requires an open handle");
        const int code = hplr::invoke_libraw([&] { return context.raw.unpack(); });
        if (code != LIBRAW_SUCCESS) return hplr::libraw_fail(error, code);
        context.state = hplr::State::Unpacked;
        return static_cast<int32_t>(HPLR_OK);
    });
}

extern "C" int32_t HPLR_CALL hplr_recycle(hplr_handle handle, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        if ((status = require_no_lease(operation->get(), error)) != HPLR_OK) return status;
        hplr::invoke_libraw([&] { operation->get().raw.recycle(); return 0; });
        operation->get().state = hplr::State::Recycled;
        return static_cast<int32_t>(HPLR_OK);
    });
}

extern "C" int32_t HPLR_CALL hplr_configure_output(hplr_handle handle,
    const hplr_output_config *config, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        if (!config)
            return hplr::fail(error, HPLR_E_ARGUMENT, HPLR_ERROR_PROGRAMMING, 0,
                              "output configuration is null");
        if (!hplr::valid_header(config, config->abi_version, config->struct_size,
                sizeof(*config), error)) return HPLR_E_ABI;
        if ((config->output_bits != 8 && config->output_bits != 16) ||
            config->output_color < 0 || config->output_color > 8 ||
            config->highlight_mode < 0 || config->fbdd_noise_reduction < 0 ||
            config->fbdd_noise_reduction > 2 || config->gamma_power <= 0 ||
            config->gamma_slope <= 0 || config->user_sat < 0 ||
            config->user_sat > UINT16_MAX || config->user_qual_present > 1 ||
            config->cropbox_present > 1 ||
            (config->user_qual_present && !named_quality(config->user_qual)))
            return hplr::fail(error, HPLR_E_ARGUMENT, HPLR_ERROR_PROGRAMMING, 0,
                              "output configuration contains an invalid value");
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        if (operation->get().state != hplr::State::Unpacked)
            return hplr::fail(error, HPLR_E_STATE, HPLR_ERROR_PROGRAMMING, 0,
                              "configuration requires successful unpack");
        if ((status = validate_crop(*config, operation->get(), error)) != HPLR_OK)
            return status;
        auto &params = operation->get().raw.imgdata.params;
        params.output_bps = config->output_bits;
        params.output_color = config->output_color;
        params.gamm[0] = config->gamma_power;
        params.gamm[1] = config->gamma_slope;
        params.no_auto_bright = config->no_auto_bright;
        params.half_size = config->half_size;
        params.highlight = config->highlight_mode;
        params.fbdd_noiserd = config->fbdd_noise_reduction;
        params.use_camera_wb = config->use_camera_wb;
        params.use_auto_wb = config->use_auto_wb;
        std::copy(std::begin(config->user_mul), std::end(config->user_mul),
                  std::begin(params.user_mul));
        params.use_camera_matrix = config->use_camera_matrix;
        params.user_sat = config->user_sat > 0 ? config->user_sat : -1;
        params.user_qual = config->user_qual_present ? config->user_qual : -1;
        if (config->cropbox_present) {
            std::copy(std::begin(config->cropbox), std::end(config->cropbox),
                      std::begin(params.cropbox));
        } else {
            params.cropbox[0] = params.cropbox[1] = 0;
            params.cropbox[2] = params.cropbox[3] = UINT_MAX;
        }
        return static_cast<int32_t>(HPLR_OK);
    });
}

extern "C" int32_t HPLR_CALL hplr_process(hplr_handle handle, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        if ((status = require_no_lease(operation->get(), error)) != HPLR_OK) return status;
        if (operation->get().state != hplr::State::Unpacked)
            return hplr::fail(error, HPLR_E_STATE, HPLR_ERROR_PROGRAMMING, 0,
                              "process requires successful unpack");
        const int code = hplr::invoke_libraw([&] { return operation->get().raw.dcraw_process(); });
        if (code != LIBRAW_SUCCESS) return hplr::libraw_fail(error, code);
        operation->get().state = hplr::State::Processed;
        return static_cast<int32_t>(HPLR_OK);
    });
}
