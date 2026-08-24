#include "happyphoton_libraw_bridge.h"
#include <libraw/libraw.h>

#include <cstddef>
#include <cstdint>
#include <type_traits>

static_assert(LIBRAW_MAJOR_VERSION == 0);
static_assert(LIBRAW_MINOR_VERSION == 22);
static_assert(LIBRAW_PATCH_VERSION == 2);
static_assert(LIBRAW_CBLACK_SIZE == HPLR_CBLACK_COUNT);

#define HPLR_FIELD(type, field, bytes) \
    static_assert(sizeof(((type *)nullptr)->field) == (bytes)); \
    static_assert(offsetof(type, field) + sizeof(((type *)nullptr)->field) <= sizeof(type))

HPLR_FIELD(libraw_data_t, sizes, sizeof(libraw_image_sizes_t));
HPLR_FIELD(libraw_data_t, idata, sizeof(libraw_iparams_t));
HPLR_FIELD(libraw_data_t, lens, sizeof(libraw_lensinfo_t));
HPLR_FIELD(libraw_data_t, makernotes, sizeof(libraw_makernotes_t));
HPLR_FIELD(libraw_data_t, params, sizeof(libraw_output_params_t));
HPLR_FIELD(libraw_data_t, color, sizeof(libraw_colordata_t));
HPLR_FIELD(libraw_data_t, other, sizeof(libraw_imgother_t));
HPLR_FIELD(libraw_data_t, rawdata, sizeof(libraw_rawdata_t));

HPLR_FIELD(libraw_iparams_t, make, 64);
HPLR_FIELD(libraw_iparams_t, model, 64);
HPLR_FIELD(libraw_iparams_t, normalized_make, 64);
HPLR_FIELD(libraw_iparams_t, normalized_model, 64);
HPLR_FIELD(libraw_iparams_t, dng_version, 4);
HPLR_FIELD(libraw_iparams_t, is_foveon, 4);
HPLR_FIELD(libraw_iparams_t, colors, 4);
HPLR_FIELD(libraw_iparams_t, filters, 4);
HPLR_FIELD(libraw_iparams_t, xtrans, 36);
HPLR_FIELD(libraw_iparams_t, cdesc, 5);

HPLR_FIELD(libraw_image_sizes_t, raw_height, 2);
HPLR_FIELD(libraw_image_sizes_t, raw_width, 2);
HPLR_FIELD(libraw_image_sizes_t, height, 2);
HPLR_FIELD(libraw_image_sizes_t, width, 2);
HPLR_FIELD(libraw_image_sizes_t, top_margin, 2);
HPLR_FIELD(libraw_image_sizes_t, left_margin, 2);
HPLR_FIELD(libraw_image_sizes_t, iheight, 2);
HPLR_FIELD(libraw_image_sizes_t, iwidth, 2);
HPLR_FIELD(libraw_image_sizes_t, raw_pitch, 4);
HPLR_FIELD(libraw_image_sizes_t, flip, 4);

HPLR_FIELD(libraw_rawdata_t, raw_image, sizeof(void *));
HPLR_FIELD(libraw_rawdata_t, color4_image, sizeof(void *));
HPLR_FIELD(libraw_rawdata_t, color3_image, sizeof(void *));
HPLR_FIELD(libraw_rawdata_t, float_image, sizeof(void *));
HPLR_FIELD(libraw_rawdata_t, float3_image, sizeof(void *));
HPLR_FIELD(libraw_rawdata_t, float4_image, sizeof(void *));
HPLR_FIELD(libraw_rawdata_t, iparams, sizeof(libraw_iparams_t));
HPLR_FIELD(libraw_rawdata_t, sizes, sizeof(libraw_image_sizes_t));
HPLR_FIELD(libraw_rawdata_t, ioparams, sizeof(libraw_internal_output_params_t));
HPLR_FIELD(libraw_rawdata_t, color, sizeof(libraw_colordata_t));
HPLR_FIELD(libraw_internal_output_params_t, fuji_width, 2);

HPLR_FIELD(libraw_colordata_t, cblack, 4104 * 4);
HPLR_FIELD(libraw_colordata_t, black, 4);
HPLR_FIELD(libraw_colordata_t, maximum, 4);
HPLR_FIELD(libraw_colordata_t, linear_max, 4 * 4);
HPLR_FIELD(libraw_colordata_t, cam_mul, 4 * 4);
HPLR_FIELD(libraw_colordata_t, pre_mul, 4 * 4);
HPLR_FIELD(libraw_colordata_t, rgb_cam, 3 * 4 * 4);
HPLR_FIELD(libraw_colordata_t, cam_xyz, 4 * 3 * 4);

HPLR_FIELD(libraw_imgother_t, iso_speed, 4);
HPLR_FIELD(libraw_imgother_t, shutter, 4);
HPLR_FIELD(libraw_imgother_t, aperture, 4);
HPLR_FIELD(libraw_imgother_t, focal_len, 4);
HPLR_FIELD(libraw_imgother_t, timestamp, sizeof(time_t));
HPLR_FIELD(libraw_imgother_t, parsed_gps, sizeof(libraw_gps_info_t));
HPLR_FIELD(libraw_gps_info_t, latitude, 12);
HPLR_FIELD(libraw_gps_info_t, longitude, 12);
HPLR_FIELD(libraw_gps_info_t, altitude, 4);
HPLR_FIELD(libraw_gps_info_t, altref, 1);
HPLR_FIELD(libraw_gps_info_t, latref, 1);
HPLR_FIELD(libraw_gps_info_t, longref, 1);
HPLR_FIELD(libraw_gps_info_t, gpsparsed, 1);

HPLR_FIELD(libraw_lensinfo_t, Lens, 128);
HPLR_FIELD(libraw_lensinfo_t, FocalLengthIn35mmFormat, 2);
HPLR_FIELD(libraw_lensinfo_t, makernotes, sizeof(libraw_makernotes_lens_t));
HPLR_FIELD(libraw_makernotes_lens_t, LensID, 8);
HPLR_FIELD(libraw_makernotes_lens_t, Lens, 128);
HPLR_FIELD(libraw_makernotes_lens_t, LensFormat, 2);
HPLR_FIELD(libraw_makernotes_lens_t, LensMount, 2);
HPLR_FIELD(libraw_makernotes_lens_t, CamID, 8);
HPLR_FIELD(libraw_makernotes_lens_t, CameraFormat, 2);
HPLR_FIELD(libraw_makernotes_lens_t, CameraMount, 2);
HPLR_FIELD(libraw_makernotes_lens_t, FocalType, 2);
HPLR_FIELD(libraw_makernotes_lens_t, MinFocal, 4);
HPLR_FIELD(libraw_makernotes_lens_t, MaxFocal, 4);
HPLR_FIELD(libraw_makernotes_lens_t, MaxAp4MinFocal, 4);
HPLR_FIELD(libraw_makernotes_lens_t, MaxAp4MaxFocal, 4);
HPLR_FIELD(libraw_makernotes_lens_t, MinAp4MinFocal, 4);
HPLR_FIELD(libraw_makernotes_lens_t, MinAp4MaxFocal, 4);
HPLR_FIELD(libraw_makernotes_lens_t, MaxAp, 4);
HPLR_FIELD(libraw_makernotes_lens_t, MinAp, 4);
HPLR_FIELD(libraw_makernotes_lens_t, CurFocal, 4);
HPLR_FIELD(libraw_makernotes_lens_t, CurAp, 4);
HPLR_FIELD(libraw_makernotes_lens_t, MaxAp4CurFocal, 4);
HPLR_FIELD(libraw_makernotes_lens_t, MinAp4CurFocal, 4);
HPLR_FIELD(libraw_makernotes_lens_t, MinFocusDistance, 4);
HPLR_FIELD(libraw_makernotes_lens_t, FocusRangeIndex, 4);
HPLR_FIELD(libraw_makernotes_lens_t, LensFStops, 4);
HPLR_FIELD(libraw_makernotes_lens_t, TeleconverterID, 8);
HPLR_FIELD(libraw_makernotes_lens_t, Teleconverter, 128);
HPLR_FIELD(libraw_makernotes_lens_t, AdapterID, 8);
HPLR_FIELD(libraw_makernotes_lens_t, Adapter, 128);
HPLR_FIELD(libraw_makernotes_lens_t, AttachmentID, 8);
HPLR_FIELD(libraw_makernotes_lens_t, Attachment, 128);
HPLR_FIELD(libraw_makernotes_lens_t, FocalUnits, 2);
HPLR_FIELD(libraw_makernotes_lens_t, FocalLengthIn35mmFormat, 4);
HPLR_FIELD(libraw_makernotes_t, fuji, sizeof(libraw_fuji_info_t));
HPLR_FIELD(libraw_fuji_info_t, ExpoMidPointShift, 4);
HPLR_FIELD(libraw_fuji_info_t, DynamicRange, 2);
HPLR_FIELD(libraw_fuji_info_t, DynamicRangeSetting, 2);
HPLR_FIELD(libraw_fuji_info_t, DevelopmentDynamicRange, 2);
HPLR_FIELD(libraw_fuji_info_t, AutoDynamicRange, 2);

HPLR_FIELD(libraw_output_params_t, gamm, 6 * 8);
HPLR_FIELD(libraw_output_params_t, user_mul, 4 * 4);
HPLR_FIELD(libraw_output_params_t, half_size, 4);
HPLR_FIELD(libraw_output_params_t, highlight, 4);
HPLR_FIELD(libraw_output_params_t, use_auto_wb, 4);
HPLR_FIELD(libraw_output_params_t, use_camera_wb, 4);
HPLR_FIELD(libraw_output_params_t, use_camera_matrix, 4);
HPLR_FIELD(libraw_output_params_t, output_color, 4);
HPLR_FIELD(libraw_output_params_t, output_bps, 4);
HPLR_FIELD(libraw_output_params_t, no_auto_bright, 4);
HPLR_FIELD(libraw_output_params_t, fbdd_noiserd, 4);
HPLR_FIELD(libraw_output_params_t, user_sat, 4);
HPLR_FIELD(libraw_output_params_t, user_qual, 4);
HPLR_FIELD(libraw_output_params_t, cropbox, 4 * 4);

HPLR_FIELD(libraw_processed_image_t, type, 4);
HPLR_FIELD(libraw_processed_image_t, height, 2);
HPLR_FIELD(libraw_processed_image_t, width, 2);
HPLR_FIELD(libraw_processed_image_t, colors, 2);
HPLR_FIELD(libraw_processed_image_t, bits, 2);
HPLR_FIELD(libraw_processed_image_t, data_size, 4);
HPLR_FIELD(libraw_processed_image_t, data, 1);

#define HPLR_LAYOUT(type, bytes) static_assert(sizeof(type) == (bytes))
#define HPLR_OFFSET(type, field, offset) static_assert(offsetof(type, field) == (offset))

HPLR_LAYOUT(hplr_error, 32);
HPLR_OFFSET(hplr_error, abi_version, 0); HPLR_OFFSET(hplr_error, struct_size, 4);
HPLR_OFFSET(hplr_error, error_class, 8); HPLR_OFFSET(hplr_error, native_code, 12);
HPLR_OFFSET(hplr_error, text, 16); HPLR_OFFSET(hplr_error, text_capacity, 24);
HPLR_OFFSET(hplr_error, text_length, 28);

HPLR_LAYOUT(hplr_runtime_info, 152);
HPLR_OFFSET(hplr_runtime_info, abi_version, 0); HPLR_OFFSET(hplr_runtime_info, struct_size, 4);
HPLR_OFFSET(hplr_runtime_info, libraw_version_number, 8); HPLR_OFFSET(hplr_runtime_info, capabilities, 12);
HPLR_OFFSET(hplr_runtime_info, thread_safe_variant, 16); HPLR_OFFSET(hplr_runtime_info, version_string_length, 20);
HPLR_OFFSET(hplr_runtime_info, version_string, 24);

HPLR_LAYOUT(hplr_dimensions, 40);
HPLR_OFFSET(hplr_dimensions, abi_version, 0); HPLR_OFFSET(hplr_dimensions, struct_size, 4);
HPLR_OFFSET(hplr_dimensions, raw_width, 8); HPLR_OFFSET(hplr_dimensions, raw_height, 12);
HPLR_OFFSET(hplr_dimensions, visible_width, 16); HPLR_OFFSET(hplr_dimensions, visible_height, 20);
HPLR_OFFSET(hplr_dimensions, output_width, 24); HPLR_OFFSET(hplr_dimensions, output_height, 28);
HPLR_OFFSET(hplr_dimensions, orientation, 32); HPLR_OFFSET(hplr_dimensions, reserved, 36);

HPLR_LAYOUT(hplr_sensor_identity, 72);
HPLR_OFFSET(hplr_sensor_identity, abi_version, 0); HPLR_OFFSET(hplr_sensor_identity, struct_size, 4);
HPLR_OFFSET(hplr_sensor_identity, colors, 8); HPLR_OFFSET(hplr_sensor_identity, filters, 12);
HPLR_OFFSET(hplr_sensor_identity, dng_version, 16); HPLR_OFFSET(hplr_sensor_identity, xtrans_count, 20);
HPLR_OFFSET(hplr_sensor_identity, xtrans, 24); HPLR_OFFSET(hplr_sensor_identity, cdesc_length, 60);
HPLR_OFFSET(hplr_sensor_identity, cdesc, 64); HPLR_OFFSET(hplr_sensor_identity, reserved, 69);

HPLR_LAYOUT(hplr_gps_facts, 32);
HPLR_OFFSET(hplr_gps_facts, parsed, 0); HPLR_OFFSET(hplr_gps_facts, coordinate_present, 4);
HPLR_OFFSET(hplr_gps_facts, latitude, 8); HPLR_OFFSET(hplr_gps_facts, longitude, 16);
HPLR_OFFSET(hplr_gps_facts, altitude_present, 24); HPLR_OFFSET(hplr_gps_facts, altitude, 28);

HPLR_LAYOUT(hplr_metadata, 760);
HPLR_OFFSET(hplr_metadata, abi_version, 0); HPLR_OFFSET(hplr_metadata, struct_size, 4);
HPLR_OFFSET(hplr_metadata, make_length, 8); HPLR_OFFSET(hplr_metadata, make, 12);
HPLR_OFFSET(hplr_metadata, model_length, 140); HPLR_OFFSET(hplr_metadata, model, 144);
HPLR_OFFSET(hplr_metadata, normalized_make_length, 272); HPLR_OFFSET(hplr_metadata, normalized_make, 276);
HPLR_OFFSET(hplr_metadata, normalized_model_length, 404); HPLR_OFFSET(hplr_metadata, normalized_model, 408);
HPLR_OFFSET(hplr_metadata, lens_length, 536); HPLR_OFFSET(hplr_metadata, lens, 540);
HPLR_OFFSET(hplr_metadata, iso_present, 668); HPLR_OFFSET(hplr_metadata, iso, 672);
HPLR_OFFSET(hplr_metadata, shutter_present, 676); HPLR_OFFSET(hplr_metadata, shutter, 680);
HPLR_OFFSET(hplr_metadata, aperture_present, 684); HPLR_OFFSET(hplr_metadata, aperture, 688);
HPLR_OFFSET(hplr_metadata, focal_length_present, 692); HPLR_OFFSET(hplr_metadata, focal_length, 696);
HPLR_OFFSET(hplr_metadata, focal_length_35mm_present, 700); HPLR_OFFSET(hplr_metadata, focal_length_35mm, 704);
HPLR_OFFSET(hplr_metadata, timestamp_present, 708); HPLR_OFFSET(hplr_metadata, timestamp, 712);
HPLR_OFFSET(hplr_metadata, orientation, 720); HPLR_OFFSET(hplr_metadata, reserved, 724);
HPLR_OFFSET(hplr_metadata, gps, 728);

HPLR_LAYOUT(hplr_camera_facts, 180);
HPLR_OFFSET(hplr_camera_facts, abi_version, 0); HPLR_OFFSET(hplr_camera_facts, struct_size, 4);
HPLR_OFFSET(hplr_camera_facts, multiplier_count, 8); HPLR_OFFSET(hplr_camera_facts, multipliers, 12);
HPLR_OFFSET(hplr_camera_facts, matrix_rows, 28); HPLR_OFFSET(hplr_camera_facts, matrix_columns, 32);
HPLR_OFFSET(hplr_camera_facts, camera_to_srgb, 36); HPLR_OFFSET(hplr_camera_facts, pre_multiplier_count, 84);
HPLR_OFFSET(hplr_camera_facts, pre_multipliers, 88); HPLR_OFFSET(hplr_camera_facts, camera_from_xyz_rows, 104);
HPLR_OFFSET(hplr_camera_facts, camera_from_xyz_columns, 108); HPLR_OFFSET(hplr_camera_facts, camera_from_xyz, 112);
HPLR_OFFSET(hplr_camera_facts, linear_max_count, 160); HPLR_OFFSET(hplr_camera_facts, linear_max, 164);

HPLR_LAYOUT(hplr_fuji_facts, 32);
HPLR_OFFSET(hplr_fuji_facts, abi_version, 0); HPLR_OFFSET(hplr_fuji_facts, struct_size, 4);
HPLR_OFFSET(hplr_fuji_facts, present, 8); HPLR_OFFSET(hplr_fuji_facts, exposure_midpoint_shift, 12);
HPLR_OFFSET(hplr_fuji_facts, dynamic_range, 16); HPLR_OFFSET(hplr_fuji_facts, dynamic_range_setting, 20);
HPLR_OFFSET(hplr_fuji_facts, development_dynamic_range, 24); HPLR_OFFSET(hplr_fuji_facts, auto_dynamic_range, 28);

HPLR_LAYOUT(hplr_lens_identity, 672);
HPLR_OFFSET(hplr_lens_identity, abi_version, 0); HPLR_OFFSET(hplr_lens_identity, struct_size, 4);
HPLR_OFFSET(hplr_lens_identity, present, 8); HPLR_OFFSET(hplr_lens_identity, reserved, 12);
HPLR_OFFSET(hplr_lens_identity, lens_id, 16); HPLR_OFFSET(hplr_lens_identity, camera_id, 24);
HPLR_OFFSET(hplr_lens_identity, teleconverter_id, 32); HPLR_OFFSET(hplr_lens_identity, adapter_id, 40);
HPLR_OFFSET(hplr_lens_identity, attachment_id, 48); HPLR_OFFSET(hplr_lens_identity, lens_format, 56);
HPLR_OFFSET(hplr_lens_identity, lens_mount, 60); HPLR_OFFSET(hplr_lens_identity, camera_format, 64);
HPLR_OFFSET(hplr_lens_identity, camera_mount, 68); HPLR_OFFSET(hplr_lens_identity, focal_type, 72);
HPLR_OFFSET(hplr_lens_identity, focal_units, 76); HPLR_OFFSET(hplr_lens_identity, min_focal, 80);
HPLR_OFFSET(hplr_lens_identity, max_focal, 84); HPLR_OFFSET(hplr_lens_identity, max_aperture_at_min_focal, 88);
HPLR_OFFSET(hplr_lens_identity, max_aperture_at_max_focal, 92); HPLR_OFFSET(hplr_lens_identity, min_aperture_at_min_focal, 96);
HPLR_OFFSET(hplr_lens_identity, min_aperture_at_max_focal, 100); HPLR_OFFSET(hplr_lens_identity, max_aperture, 104);
HPLR_OFFSET(hplr_lens_identity, min_aperture, 108); HPLR_OFFSET(hplr_lens_identity, current_focal, 112);
HPLR_OFFSET(hplr_lens_identity, current_aperture, 116); HPLR_OFFSET(hplr_lens_identity, max_aperture_at_current_focal, 120);
HPLR_OFFSET(hplr_lens_identity, min_aperture_at_current_focal, 124); HPLR_OFFSET(hplr_lens_identity, min_focus_distance, 128);
HPLR_OFFSET(hplr_lens_identity, focus_range_index, 132); HPLR_OFFSET(hplr_lens_identity, lens_f_stops, 136);
HPLR_OFFSET(hplr_lens_identity, focal_length_35mm, 140); HPLR_OFFSET(hplr_lens_identity, lens_length, 144);
HPLR_OFFSET(hplr_lens_identity, lens, 148); HPLR_OFFSET(hplr_lens_identity, teleconverter_length, 276);
HPLR_OFFSET(hplr_lens_identity, teleconverter, 280); HPLR_OFFSET(hplr_lens_identity, adapter_length, 408);
HPLR_OFFSET(hplr_lens_identity, adapter, 412); HPLR_OFFSET(hplr_lens_identity, attachment_length, 540);
HPLR_OFFSET(hplr_lens_identity, attachment, 544);

HPLR_LAYOUT(hplr_mosaic_descriptor, 16496);
HPLR_OFFSET(hplr_mosaic_descriptor, abi_version, 0); HPLR_OFFSET(hplr_mosaic_descriptor, struct_size, 4);
HPLR_OFFSET(hplr_mosaic_descriptor, data, 8); HPLR_OFFSET(hplr_mosaic_descriptor, byte_length, 16);
HPLR_OFFSET(hplr_mosaic_descriptor, raw_pitch, 24); HPLR_OFFSET(hplr_mosaic_descriptor, raw_width, 28);
HPLR_OFFSET(hplr_mosaic_descriptor, raw_height, 32); HPLR_OFFSET(hplr_mosaic_descriptor, visible_width, 36);
HPLR_OFFSET(hplr_mosaic_descriptor, visible_height, 40); HPLR_OFFSET(hplr_mosaic_descriptor, top_margin, 44);
HPLR_OFFSET(hplr_mosaic_descriptor, left_margin, 48); HPLR_OFFSET(hplr_mosaic_descriptor, black, 52);
HPLR_OFFSET(hplr_mosaic_descriptor, maximum, 56); HPLR_OFFSET(hplr_mosaic_descriptor, cblack_count, 60);
HPLR_OFFSET(hplr_mosaic_descriptor, repeating_rows, 64); HPLR_OFFSET(hplr_mosaic_descriptor, repeating_columns, 68);
HPLR_OFFSET(hplr_mosaic_descriptor, cblack, 72); HPLR_OFFSET(hplr_mosaic_descriptor, lease, 16488);

HPLR_LAYOUT(hplr_output_config, 112);
HPLR_OFFSET(hplr_output_config, abi_version, 0); HPLR_OFFSET(hplr_output_config, struct_size, 4);
HPLR_OFFSET(hplr_output_config, output_bits, 8); HPLR_OFFSET(hplr_output_config, output_color, 12);
HPLR_OFFSET(hplr_output_config, gamma_power, 16); HPLR_OFFSET(hplr_output_config, gamma_slope, 24);
HPLR_OFFSET(hplr_output_config, no_auto_bright, 32); HPLR_OFFSET(hplr_output_config, half_size, 36);
HPLR_OFFSET(hplr_output_config, highlight_mode, 40); HPLR_OFFSET(hplr_output_config, fbdd_noise_reduction, 44);
HPLR_OFFSET(hplr_output_config, use_camera_wb, 48); HPLR_OFFSET(hplr_output_config, use_auto_wb, 52);
HPLR_OFFSET(hplr_output_config, user_mul, 56); HPLR_OFFSET(hplr_output_config, use_camera_matrix, 72);
HPLR_OFFSET(hplr_output_config, reserved, 76); HPLR_OFFSET(hplr_output_config, user_sat, 80);
HPLR_OFFSET(hplr_output_config, user_qual_present, 84); HPLR_OFFSET(hplr_output_config, user_qual, 88);
HPLR_OFFSET(hplr_output_config, cropbox_present, 92); HPLR_OFFSET(hplr_output_config, cropbox, 96);

HPLR_LAYOUT(hplr_image_descriptor, 56);
HPLR_OFFSET(hplr_image_descriptor, abi_version, 0); HPLR_OFFSET(hplr_image_descriptor, struct_size, 4);
HPLR_OFFSET(hplr_image_descriptor, data, 8); HPLR_OFFSET(hplr_image_descriptor, byte_length, 16);
HPLR_OFFSET(hplr_image_descriptor, width, 24); HPLR_OFFSET(hplr_image_descriptor, height, 28);
HPLR_OFFSET(hplr_image_descriptor, bits_per_sample, 32); HPLR_OFFSET(hplr_image_descriptor, channels, 36);
HPLR_OFFSET(hplr_image_descriptor, format, 40); HPLR_OFFSET(hplr_image_descriptor, reserved, 44);
HPLR_OFFSET(hplr_image_descriptor, allocation, 48);

#undef HPLR_FIELD
#undef HPLR_LAYOUT
#undef HPLR_OFFSET
