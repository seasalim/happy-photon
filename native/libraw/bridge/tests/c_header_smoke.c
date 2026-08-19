#include "happyphoton_libraw_bridge.h"
#include <stdint.h>

int main(void) {
    uint8_t text[64];
    hplr_error error = {HPLR_ABI_VERSION, sizeof(hplr_error), 0, 0,
                        text, sizeof(text), 0};
    hplr_camera_facts facts = {0};
    hplr_mosaic_descriptor mosaic = {0};
    uint16_t *mutable_samples = mosaic.data;
    facts.abi_version = HPLR_ABI_VERSION;
    facts.struct_size = sizeof(hplr_camera_facts);
    facts.pre_multiplier_count = 4;
    facts.pre_multipliers[3] = 1.25f;
    facts.camera_from_xyz_rows = 4;
    facts.camera_from_xyz_columns = 3;
    facts.camera_from_xyz[11] = 0.75f;
    facts.linear_max_count = 4;
    facts.linear_max[3] = UINT32_C(65535);
    if (hplr_abi_version() != HPLR_ABI_VERSION || mutable_samples != 0) return 1;
    if (hplr_close(UINT64_C(0xfedcba9876543210), &error) != HPLR_E_HANDLE) return 2;
    if (error.error_class != HPLR_ERROR_PROGRAMMING) return 3;
    if (facts.pre_multiplier_count != 4 || facts.pre_multipliers[3] != 1.25f ||
        facts.camera_from_xyz_rows != 4 || facts.camera_from_xyz_columns != 3 ||
        facts.camera_from_xyz[11] != 0.75f || facts.linear_max_count != 4 ||
        facts.linear_max[3] != UINT32_C(65535)) return 4;
    return 0;
}
