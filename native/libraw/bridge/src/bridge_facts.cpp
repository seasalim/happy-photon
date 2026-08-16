#include "bridge_internal.hpp"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace {

template<typename T>
bool prepare(T *value, hplr_error *error) {
    if (!value) {
        hplr::fail(error, HPLR_E_ARGUMENT, HPLR_ERROR_PROGRAMMING, 0,
                   "output value is null");
        return false;
    }
    if (!hplr::valid_header(value, value->abi_version, value->struct_size,
                            sizeof(T), error)) return false;
    const uint32_t abi = value->abi_version;
    const uint32_t size = value->struct_size;
    std::memset(value, 0, sizeof(T));
    value->abi_version = abi;
    value->struct_size = size;
    return true;
}

bool usable(hplr::Context &context, hplr_error *error) {
    if (context.state == hplr::State::Recycled) {
        hplr::fail(error, HPLR_E_STATE, HPLR_ERROR_PROGRAMMING, 0,
                   "facts are unavailable after recycle");
        return false;
    }
    return true;
}

void coordinate(const libraw_gps_info_t &gps, hplr_gps_facts &out) {
    out.parsed = gps.gpsparsed ? 1u : 0u;
    if (!out.parsed) return;
    const bool lat_ref = gps.latref == 'N' || gps.latref == 'S';
    const bool lon_ref = gps.longref == 'E' || gps.longref == 'W';
    bool finite = lat_ref && lon_ref;
    for (float value : gps.latitude) finite &= std::isfinite(value);
    for (float value : gps.longitude) finite &= std::isfinite(value);
    if (finite) {
        out.latitude = std::abs(gps.latitude[0]) + std::abs(gps.latitude[1]) / 60.0 +
            std::abs(gps.latitude[2]) / 3600.0;
        out.longitude = std::abs(gps.longitude[0]) + std::abs(gps.longitude[1]) / 60.0 +
            std::abs(gps.longitude[2]) / 3600.0;
        if (gps.latref == 'S') out.latitude = -out.latitude;
        if (gps.longref == 'W') out.longitude = -out.longitude;
        out.coordinate_present = std::abs(out.latitude) <= 90 &&
            std::abs(out.longitude) <= 180;
    }
    if (std::isfinite(gps.altitude) && (out.coordinate_present || gps.altitude != 0)) {
        out.altitude_present = 1;
        out.altitude = gps.altref == 1 ? -std::abs(gps.altitude) : gps.altitude;
    }
}

bool is_fuji(const char *make) {
    constexpr char expected[] = "FUJIFILM";
    for (size_t index = 0; index < sizeof(expected) - 1; ++index) {
        char value = make[index];
        if (value >= 'a' && value <= 'z') value -= 'a' - 'A';
        if (value != expected[index]) return false;
    }
    return true;
}

} // namespace

extern "C" int32_t HPLR_CALL hplr_get_dimensions(hplr_handle handle,
    hplr_dimensions *out_value, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        if (!prepare(out_value, error)) return HPLR_E_ABI;
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        if (!usable(operation->get(), error)) return static_cast<int32_t>(HPLR_E_STATE);
        const auto &sizes = operation->get().raw.imgdata.sizes;
        out_value->raw_width = sizes.raw_width;
        out_value->raw_height = sizes.raw_height;
        out_value->visible_width = sizes.width;
        out_value->visible_height = sizes.height;
        out_value->output_width = sizes.iwidth;
        out_value->output_height = sizes.iheight;
        out_value->orientation = sizes.flip;
        return static_cast<int32_t>(HPLR_OK);
    });
}

extern "C" int32_t HPLR_CALL hplr_get_sensor_identity(hplr_handle handle,
    hplr_sensor_identity *out_value, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        if (!prepare(out_value, error)) return HPLR_E_ABI;
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        if (!usable(operation->get(), error)) return static_cast<int32_t>(HPLR_E_STATE);
        const auto &identity = operation->get().raw.imgdata.idata;
        out_value->colors = identity.colors;
        out_value->filters = identity.filters;
        out_value->dng_version = identity.dng_version;
        out_value->xtrans_count = 36;
        std::memcpy(out_value->xtrans, identity.xtrans, 36);
        out_value->cdesc_length = static_cast<uint32_t>(
            strnlen(identity.cdesc, sizeof(identity.cdesc)));
        std::memcpy(out_value->cdesc, identity.cdesc, out_value->cdesc_length);
        return static_cast<int32_t>(HPLR_OK);
    });
}

extern "C" int32_t HPLR_CALL hplr_get_metadata(hplr_handle handle,
    hplr_metadata *out_value, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        if (!prepare(out_value, error)) return HPLR_E_ABI;
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        if (!usable(operation->get(), error)) return static_cast<int32_t>(HPLR_E_STATE);
        const auto &data = operation->get().raw.imgdata;
        out_value->make_length = hplr::copy_native_text(data.idata.make,
            sizeof(data.idata.make), out_value->make, HPLR_TEXT_CAPACITY);
        out_value->model_length = hplr::copy_native_text(data.idata.model,
            sizeof(data.idata.model), out_value->model, HPLR_TEXT_CAPACITY);
        out_value->normalized_make_length = hplr::copy_native_text(
            data.idata.normalized_make, sizeof(data.idata.normalized_make),
            out_value->normalized_make, HPLR_TEXT_CAPACITY);
        out_value->normalized_model_length = hplr::copy_native_text(
            data.idata.normalized_model, sizeof(data.idata.normalized_model),
            out_value->normalized_model, HPLR_TEXT_CAPACITY);
        out_value->lens_length = hplr::copy_native_text(data.lens.Lens,
            sizeof(data.lens.Lens), out_value->lens, HPLR_TEXT_CAPACITY);
        out_value->iso_present = std::isfinite(data.other.iso_speed) && data.other.iso_speed > 0;
        out_value->iso = data.other.iso_speed;
        out_value->shutter_present = std::isfinite(data.other.shutter) && data.other.shutter > 0;
        out_value->shutter = data.other.shutter;
        out_value->aperture_present = std::isfinite(data.other.aperture) && data.other.aperture > 0;
        out_value->aperture = data.other.aperture;
        out_value->focal_length_present = std::isfinite(data.other.focal_len) && data.other.focal_len > 0;
        out_value->focal_length = data.other.focal_len;
        const auto equivalent = data.lens.FocalLengthIn35mmFormat
            ? static_cast<float>(data.lens.FocalLengthIn35mmFormat)
            : data.lens.makernotes.FocalLengthIn35mmFormat;
        out_value->focal_length_35mm_present = equivalent > 0;
        out_value->focal_length_35mm = equivalent;
        out_value->timestamp_present = data.other.timestamp > 0;
        out_value->timestamp = static_cast<int64_t>(data.other.timestamp);
        out_value->orientation = data.sizes.flip;
        coordinate(data.other.parsed_gps, out_value->gps);
        return static_cast<int32_t>(HPLR_OK);
    });
}

extern "C" int32_t HPLR_CALL hplr_get_camera_facts(hplr_handle handle,
    hplr_camera_facts *out_value, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        if (!prepare(out_value, error)) return HPLR_E_ABI;
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        if (!usable(operation->get(), error)) return static_cast<int32_t>(HPLR_E_STATE);
        const auto &color = operation->get().raw.imgdata.color;
        uint32_t count = 3;
        if (std::isfinite(color.cam_mul[3]) && color.cam_mul[3] > 0 &&
            (std::abs(color.rgb_cam[0][3]) > 0 || std::abs(color.rgb_cam[1][3]) > 0 ||
             std::abs(color.rgb_cam[2][3]) > 0)) count = 4;
        for (uint32_t column = 0; column < count; ++column) {
            if (!std::isfinite(color.cam_mul[column]) || color.cam_mul[column] <= 0)
                return static_cast<int32_t>(HPLR_ABSENT);
            out_value->multipliers[column] = color.cam_mul[column];
        }
        out_value->multiplier_count = count;
        out_value->matrix_rows = 3;
        out_value->matrix_columns = count;
        for (uint32_t row = 0; row < 3; ++row)
            for (uint32_t column = 0; column < count; ++column)
                out_value->camera_to_srgb[row * 4 + column] = color.rgb_cam[row][column];
        return static_cast<int32_t>(HPLR_OK);
    });
}

extern "C" int32_t HPLR_CALL hplr_get_fuji_facts(hplr_handle handle,
    hplr_fuji_facts *out_value, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        if (!prepare(out_value, error)) return HPLR_E_ABI;
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        if (!usable(operation->get(), error)) return static_cast<int32_t>(HPLR_E_STATE);
        const auto &data = operation->get().raw.imgdata;
        if (!is_fuji(data.idata.make)) return static_cast<int32_t>(HPLR_ABSENT);
        const auto &fuji = data.makernotes.fuji;
        out_value->present = 1;
        out_value->exposure_midpoint_shift = fuji.ExpoMidPointShift;
        out_value->dynamic_range = fuji.DynamicRange;
        out_value->dynamic_range_setting = fuji.DynamicRangeSetting;
        out_value->development_dynamic_range = fuji.DevelopmentDynamicRange;
        out_value->auto_dynamic_range = fuji.AutoDynamicRange;
        return static_cast<int32_t>(HPLR_OK);
    });
}

extern "C" int32_t HPLR_CALL hplr_borrow_mosaic(hplr_handle handle,
    hplr_mosaic_descriptor *out_value, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        if (!prepare(out_value, error)) return HPLR_E_ABI;
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        auto &context = operation->get();
        if (context.state != hplr::State::Unpacked)
            return hplr::fail(error, HPLR_E_STATE, HPLR_ERROR_PROGRAMMING, 0,
                              "mosaic borrow requires successful unpack");
        const auto &rawdata = context.raw.imgdata.rawdata;
        if (!rawdata.raw_image || rawdata.color3_image || rawdata.color4_image ||
            rawdata.float_image || rawdata.float3_image || rawdata.float4_image ||
            rawdata.iparams.is_foveon)
            return static_cast<int32_t>(HPLR_UNAVAILABLE);
        const auto &sizes = rawdata.sizes;
        const uint64_t extent = static_cast<uint64_t>(sizes.raw_pitch) * sizes.raw_height;
        if (!sizes.raw_pitch || sizes.raw_pitch < static_cast<uint32_t>(sizes.raw_width) * 2u ||
            !sizes.raw_height || extent > SIZE_MAX)
            return hplr::fail(error, HPLR_E_INTERNAL, HPLR_ERROR_BRIDGE, 0,
                              "raw mosaic extent is impossible");
        if (context.leases.load(std::memory_order_acquire))
            return hplr::fail(error, HPLR_E_BUSY, HPLR_ERROR_PROGRAMMING, 0,
                              "handle already has a mosaic lease");
        const auto lease = hplr::issue_token();
        context.active_lease.store(lease, std::memory_order_release);
        context.leases.store(1, std::memory_order_release);
        out_value->data = rawdata.raw_image;
        out_value->byte_length = extent;
        out_value->raw_pitch = sizes.raw_pitch;
        out_value->raw_width = sizes.raw_width;
        out_value->raw_height = sizes.raw_height;
        out_value->visible_width = sizes.width;
        out_value->visible_height = sizes.height;
        out_value->top_margin = sizes.top_margin;
        out_value->left_margin = sizes.left_margin;
        out_value->black = rawdata.color.black;
        out_value->maximum = rawdata.color.maximum;
        out_value->cblack_count = HPLR_CBLACK_COUNT;
        out_value->repeating_rows = rawdata.color.cblack[4];
        out_value->repeating_columns = rawdata.color.cblack[5];
        std::copy(std::begin(rawdata.color.cblack), std::end(rawdata.color.cblack),
                  std::begin(out_value->cblack));
        out_value->lease = lease;
        return static_cast<int32_t>(HPLR_OK);
    });
}

extern "C" int32_t HPLR_CALL hplr_release_mosaic(hplr_lease lease,
    hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        if (!lease)
            return hplr::fail(error, HPLR_E_OWNERSHIP, HPLR_ERROR_PROGRAMMING, 0,
                              "mosaic lease token is zero");
        std::lock_guard<std::mutex> lock(hplr::registry_mutex);
        for (auto &[token, context] : hplr::handles) {
            if (context->active_lease.load(std::memory_order_acquire) == lease) {
                context->active_lease.store(0, std::memory_order_release);
                context->leases.store(0, std::memory_order_release);
                return static_cast<int32_t>(HPLR_OK);
            }
        }
        return hplr::fail(error, HPLR_E_OWNERSHIP, HPLR_ERROR_PROGRAMMING, 0,
                          "mosaic lease token is unknown or released");
    });
}
