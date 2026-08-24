#ifndef HAPPY_PHOTON_LIBRAW_BRIDGE_H
#define HAPPY_PHOTON_LIBRAW_BRIDGE_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#  if defined(HPLR_BUILDING)
#    define HPLR_API __declspec(dllexport)
#  else
#    define HPLR_API __declspec(dllimport)
#  endif
#  define HPLR_CALL __cdecl
#else
#  define HPLR_API __attribute__((visibility("default")))
#  define HPLR_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

#pragma pack(push, 8)

#define HPLR_ABI_VERSION UINT32_C(4)
#define HPLR_CBLACK_COUNT UINT32_C(4104)
#define HPLR_TEXT_CAPACITY UINT32_C(128)

typedef uint64_t hplr_handle;
typedef uint64_t hplr_lease;
typedef uint64_t hplr_allocation;

typedef enum hplr_status {
    HPLR_OK = 0,
    HPLR_ABSENT = 1,
    HPLR_UNAVAILABLE = 2,
    HPLR_E_ABI = -1000,
    HPLR_E_ARGUMENT = -1001,
    HPLR_E_HANDLE = -1002,
    HPLR_E_STATE = -1003,
    HPLR_E_BUSY = -1004,
    HPLR_E_OWNERSHIP = -1005,
    HPLR_E_RESOURCE = -1006,
    HPLR_E_INTERNAL = -1007,
    HPLR_E_LIBRAW = -1008
} hplr_status;

typedef enum hplr_error_class {
    HPLR_ERROR_NONE = 0,
    HPLR_ERROR_LIBRAW = 1,
    HPLR_ERROR_ABI = 2,
    HPLR_ERROR_PROGRAMMING = 3,
    HPLR_ERROR_BRIDGE = 4
} hplr_error_class;

/* Error text is UTF-8, never NUL-terminated by contract, and is truncated to
   text_capacity bytes. The caller owns text and the error value. */
typedef struct hplr_error {
    uint32_t abi_version;
    uint32_t struct_size;
    int32_t error_class;
    int32_t native_code;
    uint8_t *text;
    uint32_t text_capacity;
    uint32_t text_length;
} hplr_error;

typedef struct hplr_runtime_info {
    uint32_t abi_version;
    uint32_t struct_size;
    uint32_t libraw_version_number;
    uint32_t capabilities;
    uint32_t thread_safe_variant;
    uint32_t version_string_length;
    uint8_t version_string[HPLR_TEXT_CAPACITY];
} hplr_runtime_info;

typedef struct hplr_dimensions {
    uint32_t abi_version;
    uint32_t struct_size;
    uint32_t raw_width;
    uint32_t raw_height;
    uint32_t visible_width;
    uint32_t visible_height;
    uint32_t output_width;
    uint32_t output_height;
    int32_t orientation;
    uint32_t reserved;
} hplr_dimensions;

typedef struct hplr_sensor_identity {
    uint32_t abi_version;
    uint32_t struct_size;
    int32_t colors;
    uint32_t filters;
    uint32_t dng_version;
    uint32_t xtrans_count;
    int8_t xtrans[36];
    uint32_t cdesc_length;
    uint8_t cdesc[5];
    uint8_t reserved[3];
} hplr_sensor_identity;

typedef struct hplr_gps_facts {
    uint32_t parsed;
    uint32_t coordinate_present;
    double latitude;
    double longitude;
    uint32_t altitude_present;
    float altitude;
} hplr_gps_facts;

/* Fixed arrays contain sanitized UTF-8. Invalid native bytes are replaced by
   U+FFFD. A zero length means metadata is absent. */
typedef struct hplr_metadata {
    uint32_t abi_version;
    uint32_t struct_size;
    uint32_t make_length;
    uint8_t make[HPLR_TEXT_CAPACITY];
    uint32_t model_length;
    uint8_t model[HPLR_TEXT_CAPACITY];
    uint32_t normalized_make_length;
    uint8_t normalized_make[HPLR_TEXT_CAPACITY];
    uint32_t normalized_model_length;
    uint8_t normalized_model[HPLR_TEXT_CAPACITY];
    uint32_t lens_length;
    uint8_t lens[HPLR_TEXT_CAPACITY];
    uint32_t iso_present;
    float iso;
    uint32_t shutter_present;
    float shutter;
    uint32_t aperture_present;
    float aperture;
    uint32_t focal_length_present;
    float focal_length;
    uint32_t focal_length_35mm_present;
    float focal_length_35mm;
    uint32_t timestamp_present;
    int64_t timestamp;
    int32_t orientation;
    uint32_t reserved;
    hplr_gps_facts gps;
} hplr_metadata;

typedef struct hplr_camera_facts {
    uint32_t abi_version;
    uint32_t struct_size;
    uint32_t multiplier_count;
    float multipliers[4];
    /* camera_to_srgb maps camera channels (columns) to sRGB (rows). It has
       three rows at a fixed stride of four elements. */
    uint32_t matrix_rows;
    uint32_t matrix_columns;
    float camera_to_srgb[12];
    uint32_t pre_multiplier_count;
    float pre_multipliers[4];
    /* camera_from_xyz maps XYZ (columns) to camera channels (rows). It is
       tightly packed with a stride of three elements. */
    uint32_t camera_from_xyz_rows;
    uint32_t camera_from_xyz_columns;
    float camera_from_xyz[12];
    uint32_t linear_max_count;
    uint32_t linear_max[4];
} hplr_camera_facts;

typedef struct hplr_fuji_facts {
    uint32_t abi_version;
    uint32_t struct_size;
    uint32_t present;
    float exposure_midpoint_shift;
    uint32_t dynamic_range;
    uint32_t dynamic_range_setting;
    uint32_t development_dynamic_range;
    uint32_t auto_dynamic_range;
} hplr_fuji_facts;

typedef struct hplr_lens_identity {
    uint32_t abi_version;
    uint32_t struct_size;
    uint32_t present;
    uint32_t reserved;
    uint64_t lens_id;
    uint64_t camera_id;
    uint64_t teleconverter_id;
    uint64_t adapter_id;
    uint64_t attachment_id;
    uint32_t lens_format;
    uint32_t lens_mount;
    uint32_t camera_format;
    uint32_t camera_mount;
    int32_t focal_type;
    uint32_t focal_units;
    float min_focal;
    float max_focal;
    float max_aperture_at_min_focal;
    float max_aperture_at_max_focal;
    float min_aperture_at_min_focal;
    float min_aperture_at_max_focal;
    float max_aperture;
    float min_aperture;
    float current_focal;
    float current_aperture;
    float max_aperture_at_current_focal;
    float min_aperture_at_current_focal;
    float min_focus_distance;
    float focus_range_index;
    float lens_f_stops;
    float focal_length_35mm;
    uint32_t lens_length;
    uint8_t lens[HPLR_TEXT_CAPACITY];
    uint32_t teleconverter_length;
    uint8_t teleconverter[HPLR_TEXT_CAPACITY];
    uint32_t adapter_length;
    uint8_t adapter[HPLR_TEXT_CAPACITY];
    uint32_t attachment_length;
    uint8_t attachment[HPLR_TEXT_CAPACITY];
} hplr_lens_identity;

/* The data pointer aliases LibRaw's preserved rawdata snapshot. Managed writes
   made before release are consumed by the following process call. It remains
   valid only until the matching lease is released. Process, recycle, and close
   reject while the lease is live. */
typedef struct hplr_mosaic_descriptor {
    uint32_t abi_version;
    uint32_t struct_size;
    uint16_t *data;
    uint64_t byte_length;
    uint32_t raw_pitch;
    uint32_t raw_width;
    uint32_t raw_height;
    uint32_t visible_width;
    uint32_t visible_height;
    uint32_t top_margin;
    uint32_t left_margin;
    uint32_t black;
    uint32_t maximum;
    uint32_t cblack_count;
    uint32_t repeating_rows;
    uint32_t repeating_columns;
    uint32_t cblack[HPLR_CBLACK_COUNT];
    hplr_lease lease;
} hplr_mosaic_descriptor;

typedef struct hplr_output_config {
    uint32_t abi_version;
    uint32_t struct_size;
    int32_t output_bits;
    int32_t output_color;
    double gamma_power;
    double gamma_slope;
    int32_t no_auto_bright;
    int32_t half_size;
    int32_t highlight_mode;
    int32_t fbdd_noise_reduction;
    int32_t use_camera_wb;
    int32_t use_auto_wb;
    float user_mul[4];
    int32_t use_camera_matrix;
    int32_t reserved;
    int32_t user_sat;
    uint32_t user_qual_present;
    int32_t user_qual;
    uint32_t cropbox_present;
    uint32_t cropbox[4];
} hplr_output_config;

/* Thumbnail and processed allocations are owned by the returned allocation
   token. data remains valid until exactly one successful hplr_free_image call. */
typedef struct hplr_image_descriptor {
    uint32_t abi_version;
    uint32_t struct_size;
    const uint8_t *data;
    uint64_t byte_length;
    uint32_t width;
    uint32_t height;
    uint32_t bits_per_sample;
    uint32_t channels;
    int32_t format;
    uint32_t reserved;
    hplr_allocation allocation;
} hplr_image_descriptor;

#pragma pack(pop)

HPLR_API uint32_t HPLR_CALL hplr_abi_version(void);
HPLR_API int32_t HPLR_CALL hplr_runtime(hplr_runtime_info *out_info, hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_open_utf8(const uint8_t *path, uint32_t path_length,
                                           hplr_handle *out_handle, hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_close(hplr_handle handle, hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_unpack(hplr_handle handle, hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_recycle(hplr_handle handle, hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_get_dimensions(hplr_handle handle, hplr_dimensions *out_value,
                                                hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_get_sensor_identity(hplr_handle handle,
                                                     hplr_sensor_identity *out_value,
                                                     hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_get_metadata(hplr_handle handle, hplr_metadata *out_value,
                                              hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_get_camera_facts(hplr_handle handle,
                                                  hplr_camera_facts *out_value,
                                                  hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_get_fuji_facts(hplr_handle handle, hplr_fuji_facts *out_value,
                                                hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_get_lens_identity(hplr_handle handle,
                                                   hplr_lens_identity *out_value,
                                                   hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_borrow_mosaic(hplr_handle handle,
                                               hplr_mosaic_descriptor *out_value,
                                               hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_release_mosaic(hplr_lease lease, hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_unpack_thumbnail(hplr_handle handle, hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_make_thumbnail(hplr_handle handle,
                                                hplr_image_descriptor *out_value,
                                                hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_configure_output(hplr_handle handle,
                                                  const hplr_output_config *config,
                                                  hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_process(hplr_handle handle, hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_make_processed_image(hplr_handle handle,
                                                      hplr_image_descriptor *out_value,
                                                      hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_free_image(hplr_allocation allocation, hplr_error *error);

#ifdef __cplusplus
}
#endif
#endif
