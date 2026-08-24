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

void copy_pre_multipliers(const libraw_colordata_t &color, uint32_t count,
                          hplr_camera_facts &out) {
    for (uint32_t channel = 0; channel < count; ++channel)
        if (!std::isfinite(color.pre_mul[channel]) || color.pre_mul[channel] <= 0)
            return;
    out.pre_multiplier_count = count;
    for (uint32_t channel = 0; channel < count; ++channel)
        out.pre_multipliers[channel] = color.pre_mul[channel];
}

void copy_camera_from_xyz(const libraw_colordata_t &color, uint32_t count,
                          hplr_camera_facts &out) {
    bool nonzero = false;
    for (uint32_t row = 0; row < count; ++row)
        for (uint32_t column = 0; column < 3; ++column) {
            const auto value = color.cam_xyz[row][column];
            if (!std::isfinite(value)) return;
            nonzero |= value != 0;
        }
    if (!nonzero) return;
    out.camera_from_xyz_rows = count;
    out.camera_from_xyz_columns = 3;
    for (uint32_t row = 0; row < count; ++row)
        for (uint32_t column = 0; column < 3; ++column)
            out.camera_from_xyz[row * 3 + column] = color.cam_xyz[row][column];
}

void copy_linear_max(const libraw_colordata_t &color, uint32_t count,
                     hplr_camera_facts &out) {
    for (uint32_t channel = 0; channel < count; ++channel)
        if (!color.linear_max[channel]) return;
    out.linear_max_count = count;
    for (uint32_t channel = 0; channel < count; ++channel)
        out.linear_max[channel] = color.linear_max[channel];
}

bool has_lens_identity(const libraw_makernotes_lens_t &lens) {
    return lens.LensID || lens.Lens[0] || lens.LensFormat || lens.LensMount ||
        lens.CamID || lens.CameraFormat || lens.CameraMount || lens.FocalType ||
        lens.FocalUnits || lens.FocalLengthIn35mmFormat || lens.MinFocal ||
        lens.MaxFocal || lens.MaxAp4MinFocal || lens.MaxAp4MaxFocal ||
        lens.MinAp4MinFocal || lens.MinAp4MaxFocal || lens.MaxAp || lens.MinAp ||
        lens.CurFocal || lens.CurAp || lens.MaxAp4CurFocal || lens.MinAp4CurFocal ||
        lens.MinFocusDistance || lens.FocusRangeIndex || lens.LensFStops ||
        lens.TeleconverterID || lens.Teleconverter[0] || lens.AdapterID ||
        lens.Adapter[0] || lens.AttachmentID || lens.Attachment[0];
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
        copy_pre_multipliers(color, count, *out_value);
        copy_camera_from_xyz(color, count, *out_value);
        copy_linear_max(color, count, *out_value);
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

extern "C" int32_t HPLR_CALL hplr_get_lens_identity(hplr_handle handle,
    hplr_lens_identity *out_value, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        if (!prepare(out_value, error)) return HPLR_E_ABI;
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        if (!usable(operation->get(), error)) return static_cast<int32_t>(HPLR_E_STATE);
        const auto &lens = operation->get().raw.imgdata.lens.makernotes;
        if (!has_lens_identity(lens)) return static_cast<int32_t>(HPLR_ABSENT);
        out_value->present = 1;
        out_value->lens_id = lens.LensID;
        out_value->camera_id = lens.CamID;
        out_value->teleconverter_id = lens.TeleconverterID;
        out_value->adapter_id = lens.AdapterID;
        out_value->attachment_id = lens.AttachmentID;
        out_value->lens_format = lens.LensFormat;
        out_value->lens_mount = lens.LensMount;
        out_value->camera_format = lens.CameraFormat;
        out_value->camera_mount = lens.CameraMount;
        out_value->focal_type = lens.FocalType;
        out_value->focal_units = lens.FocalUnits;
        out_value->min_focal = lens.MinFocal;
        out_value->max_focal = lens.MaxFocal;
        out_value->max_aperture_at_min_focal = lens.MaxAp4MinFocal;
        out_value->max_aperture_at_max_focal = lens.MaxAp4MaxFocal;
        out_value->min_aperture_at_min_focal = lens.MinAp4MinFocal;
        out_value->min_aperture_at_max_focal = lens.MinAp4MaxFocal;
        out_value->max_aperture = lens.MaxAp;
        out_value->min_aperture = lens.MinAp;
        out_value->current_focal = lens.CurFocal;
        out_value->current_aperture = lens.CurAp;
        out_value->max_aperture_at_current_focal = lens.MaxAp4CurFocal;
        out_value->min_aperture_at_current_focal = lens.MinAp4CurFocal;
        out_value->min_focus_distance = lens.MinFocusDistance;
        out_value->focus_range_index = lens.FocusRangeIndex;
        out_value->lens_f_stops = lens.LensFStops;
        out_value->focal_length_35mm = lens.FocalLengthIn35mmFormat;
        out_value->lens_length = hplr::copy_native_text(
            lens.Lens, sizeof(lens.Lens), out_value->lens, HPLR_TEXT_CAPACITY);
        out_value->teleconverter_length = hplr::copy_native_text(
            lens.Teleconverter, sizeof(lens.Teleconverter),
            out_value->teleconverter, HPLR_TEXT_CAPACITY);
        out_value->adapter_length = hplr::copy_native_text(
            lens.Adapter, sizeof(lens.Adapter), out_value->adapter, HPLR_TEXT_CAPACITY);
        out_value->attachment_length = hplr::copy_native_text(
            lens.Attachment, sizeof(lens.Attachment),
            out_value->attachment, HPLR_TEXT_CAPACITY);
        return static_cast<int32_t>(HPLR_OK);
    });
}

#if defined(HPLR_TESTING)
extern "C" HPLR_API int32_t HPLR_CALL hplr_test_set_lens_identity(hplr_handle handle,
    uint32_t present, hplr_error *error) {
    return hplr::boundary(error, [&]() -> int32_t {
        std::unique_ptr<hplr::Operation> operation;
        auto status = hplr::begin(handle, operation, error);
        if (status != HPLR_OK) return status;
        auto &lens = operation->get().raw.imgdata.lens.makernotes;
        std::memset(&lens, 0, sizeof(lens));
        if (!present) return static_cast<int32_t>(HPLR_OK);
        lens.LensID = UINT64_C(0x0123456789ABCDEF);
        lens.CamID = UINT64_C(0x1020304050607080);
        lens.TeleconverterID = UINT64_C(0x1112131415161718);
        lens.AdapterID = UINT64_C(0x2122232425262728);
        lens.AttachmentID = UINT64_C(0x3132333435363738);
        lens.LensFormat = 1; lens.LensMount = 2;
        lens.CameraFormat = 3; lens.CameraMount = 4;
        lens.FocalType = 2; lens.FocalUnits = 5;
        lens.MinFocal = 24; lens.MaxFocal = 70;
        lens.MaxAp4MinFocal = 2.8f; lens.MaxAp4MaxFocal = 4;
        lens.MinAp4MinFocal = 16; lens.MinAp4MaxFocal = 22;
        lens.MaxAp = 2.8f; lens.MinAp = 22;
        lens.CurFocal = 35; lens.CurAp = 5.6f;
        lens.MaxAp4CurFocal = 3.2f; lens.MinAp4CurFocal = 20;
        lens.MinFocusDistance = 0.3f; lens.FocusRangeIndex = 6;
        lens.LensFStops = 7; lens.FocalLengthIn35mmFormat = 52;
        const char invalid[] = {'L', static_cast<char>(0xff), 'X', 0};
        std::memcpy(lens.Lens, invalid, sizeof(invalid));
        std::memcpy(lens.Teleconverter, "TC", 3);
        std::memcpy(lens.Adapter, "AD", 3);
        std::memcpy(lens.Attachment, "AT", 3);
        return static_cast<int32_t>(HPLR_OK);
    });
}
#endif

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
