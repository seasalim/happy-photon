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
HPLR_FIELD(libraw_rawdata_t, color, sizeof(libraw_colordata_t));

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

HPLR_FIELD(libraw_processed_image_t, type, 4);
HPLR_FIELD(libraw_processed_image_t, height, 2);
HPLR_FIELD(libraw_processed_image_t, width, 2);
HPLR_FIELD(libraw_processed_image_t, colors, 2);
HPLR_FIELD(libraw_processed_image_t, bits, 2);
HPLR_FIELD(libraw_processed_image_t, data_size, 4);
HPLR_FIELD(libraw_processed_image_t, data, 1);

static_assert(sizeof(hplr_error) == 32);
static_assert(offsetof(hplr_error, text) == 16);
static_assert(sizeof(hplr_runtime_info) == 152);
static_assert(sizeof(hplr_dimensions) == 40);
static_assert(sizeof(hplr_sensor_identity) == 72);
static_assert(offsetof(hplr_sensor_identity, xtrans) == 24);
static_assert(sizeof(hplr_gps_facts) == 32);
static_assert(offsetof(hplr_gps_facts, latitude) == 8);
static_assert(sizeof(hplr_metadata) == 760);
static_assert(offsetof(hplr_metadata, make) == 12);
static_assert(offsetof(hplr_metadata, timestamp) == 712);
static_assert(offsetof(hplr_metadata, gps) == 728);
static_assert(sizeof(hplr_camera_facts) == 180);
static_assert(offsetof(hplr_camera_facts, abi_version) == 0);
static_assert(offsetof(hplr_camera_facts, struct_size) == 4);
static_assert(offsetof(hplr_camera_facts, multiplier_count) == 8);
static_assert(offsetof(hplr_camera_facts, multipliers) == 12);
static_assert(offsetof(hplr_camera_facts, matrix_rows) == 28);
static_assert(offsetof(hplr_camera_facts, matrix_columns) == 32);
static_assert(offsetof(hplr_camera_facts, camera_to_srgb) == 36);
static_assert(offsetof(hplr_camera_facts, pre_multiplier_count) == 84);
static_assert(offsetof(hplr_camera_facts, pre_multipliers) == 88);
static_assert(offsetof(hplr_camera_facts, camera_from_xyz_rows) == 104);
static_assert(offsetof(hplr_camera_facts, camera_from_xyz_columns) == 108);
static_assert(offsetof(hplr_camera_facts, camera_from_xyz) == 112);
static_assert(offsetof(hplr_camera_facts, linear_max_count) == 160);
static_assert(offsetof(hplr_camera_facts, linear_max) == 164);
static_assert(sizeof(hplr_fuji_facts) == 32);
static_assert(offsetof(hplr_fuji_facts, exposure_midpoint_shift) == 12);
static_assert(sizeof(hplr_output_config) == 80);
static_assert(offsetof(hplr_output_config, gamma_power) == 16);
static_assert(offsetof(hplr_output_config, user_mul) == 56);
static_assert(sizeof(hplr_image_descriptor) == 56);
static_assert(offsetof(hplr_image_descriptor, data) == 8);
static_assert(offsetof(hplr_image_descriptor, allocation) == 48);
static_assert(offsetof(hplr_mosaic_descriptor, data) == 8);
static_assert(offsetof(hplr_mosaic_descriptor, cblack) == 72);
static_assert(sizeof(hplr_mosaic_descriptor) == 16496);
static_assert(offsetof(hplr_mosaic_descriptor, lease) == 16488);

#undef HPLR_FIELD
