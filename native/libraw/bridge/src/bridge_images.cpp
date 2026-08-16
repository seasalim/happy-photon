#include "bridge_internal.hpp"

#include <cstring>

namespace {

int32_t make_image(const std::shared_ptr<hplr::Context> &context, bool thumbnail,
                   hplr_image_descriptor *out_value, hplr_error *error) {
#if defined(HPLR_TESTING)
    if (hplr::fail_next_allocation.exchange(false))
        return hplr::fail(error, HPLR_E_RESOURCE, HPLR_ERROR_BRIDGE, 0,
                          "injected allocation failure");
#endif
    int code = LIBRAW_SUCCESS;
    auto image = hplr::invoke_libraw([&] {
        return thumbnail ? context->raw.dcraw_make_mem_thumb(&code)
                         : context->raw.dcraw_make_mem_image(&code);
    });
    if (!image) return hplr::libraw_fail(error, code);
    auto allocation = std::make_unique<hplr::Allocation>();
    allocation->image = image;
    allocation->owner = context;
    auto status = hplr::validate_image(image, thumbnail, error);
    if (status != HPLR_OK) return status;
    const auto token = hplr::issue_token();
    out_value->data = image->data;
    out_value->byte_length = image->data_size;
    out_value->width = image->width;
    out_value->height = image->height;
    out_value->bits_per_sample = image->bits;
    out_value->channels = image->colors;
    out_value->format = image->type;
    out_value->allocation = token;
    std::lock_guard<std::mutex> lock(hplr::registry_mutex);
    hplr::allocations.emplace(token, std::move(allocation));
    context->allocations.fetch_add(1, std::memory_order_release);
    hplr::allocations.at(token)->registered = true;
    return HPLR_OK;
}

bool prepare(hplr_image_descriptor *value, hplr_error *error) {
    if (!value) {
        hplr::fail(error, HPLR_E_ARGUMENT, HPLR_ERROR_PROGRAMMING, 0,
                   "image output is null");
        return false;
    }
    if (!hplr::valid_header(value, value->abi_version, value->struct_size,
                            sizeof(*value), error)) return false;
    const auto abi = value->abi_version;
    const auto size = value->struct_size;
    std::memset(value, 0, sizeof(*value));
    value->abi_version = abi;
    value->struct_size = size;
    return true;
}

} // namespace

extern "C" int32_t HPLR_CALL hplr_unpack_thumbnail(hplr_handle handle,
    hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        if (operation->get().state == hplr::State::Recycled)
            return hplr::fail(error, HPLR_E_STATE, HPLR_ERROR_PROGRAMMING, 0,
                              "thumbnail unpack is unavailable after recycle");
        const int code = hplr::invoke_libraw([&] {
            return operation->get().raw.unpack_thumb();
        });
        if (code != LIBRAW_SUCCESS) return hplr::libraw_fail(error, code);
        return static_cast<int32_t>(HPLR_OK);
    });
}

extern "C" int32_t HPLR_CALL hplr_make_thumbnail(hplr_handle handle,
    hplr_image_descriptor *out_value, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        if (!prepare(out_value, error)) return HPLR_E_ABI;
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        if (operation->get().state == hplr::State::Recycled)
            return hplr::fail(error, HPLR_E_STATE, HPLR_ERROR_PROGRAMMING, 0,
                              "thumbnail is unavailable after recycle");
        return make_image(operation->shared(), true, out_value, error);
    });
}

extern "C" int32_t HPLR_CALL hplr_make_processed_image(hplr_handle handle,
    hplr_image_descriptor *out_value, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        if (!prepare(out_value, error)) return HPLR_E_ABI;
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        if (operation->get().state != hplr::State::Processed)
            return hplr::fail(error, HPLR_E_STATE, HPLR_ERROR_PROGRAMMING, 0,
                              "processed image requires successful processing");
        return make_image(operation->shared(), false, out_value, error);
    });
}

extern "C" int32_t HPLR_CALL hplr_free_image(hplr_allocation allocation,
    hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        if (!allocation)
            return hplr::fail(error, HPLR_E_OWNERSHIP, HPLR_ERROR_PROGRAMMING, 0,
                              "image allocation token is zero");
        std::unique_ptr<hplr::Allocation> released;
        {
            std::lock_guard<std::mutex> lock(hplr::registry_mutex);
            auto found = hplr::allocations.find(allocation);
            if (found == hplr::allocations.end())
                return hplr::fail(error, HPLR_E_OWNERSHIP,
                                  HPLR_ERROR_PROGRAMMING, 0,
                                  "image allocation is unknown or freed");
            released = std::move(found->second);
            hplr::allocations.erase(found);
        }
        return static_cast<int32_t>(HPLR_OK);
    });
}
