#include "happyphoton_libraw_bridge.h"
#include <stdint.h>

int main(void) {
    uint8_t text[64];
    hplr_error error = {HPLR_ABI_VERSION, sizeof(hplr_error), 0, 0,
                        text, sizeof(text), 0};
    if (hplr_abi_version() != HPLR_ABI_VERSION) return 1;
    if (hplr_close(UINT64_C(0xfedcba9876543210), &error) != HPLR_E_HANDLE) return 2;
    return error.error_class == HPLR_ERROR_PROGRAMMING ? 0 : 3;
}
