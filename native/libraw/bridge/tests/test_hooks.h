#pragma once
#include "happyphoton_libraw_bridge.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct hplr_test_params {
    double gamma[6];
    float user_mul[4];
    int32_t output_bits;
    int32_t output_color;
    int32_t no_auto_bright;
    int32_t half_size;
    int32_t highlight;
    int32_t fbdd;
    int32_t use_camera_wb;
    int32_t use_auto_wb;
    int32_t use_camera_matrix;
    int32_t user_flip;
    int32_t use_fuji_rotate;
    int32_t use_p1_correction;
} hplr_test_params;

HPLR_API void HPLR_CALL hplr_test_fail_next_image_allocation(void);
HPLR_API int32_t HPLR_CALL hplr_test_get_params(hplr_handle handle,
                                                 hplr_test_params *out_value,
                                                 hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_test_hold_operation(hplr_handle handle,
                                                    uint32_t milliseconds,
                                                    hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_test_set_foveon(hplr_handle handle,
                                                uint32_t enabled,
                                                hplr_error *error);
HPLR_API int32_t HPLR_CALL hplr_test_hold_libraw(hplr_handle handle,
                                                 uint32_t milliseconds,
                                                 hplr_error *error);

#ifdef __cplusplus
}
#endif
