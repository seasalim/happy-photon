#include "happyphoton_libraw_bridge.h"

#include <cstdint>
#include <filesystem>
#include <iomanip>
#include <iostream>
#include <limits>
#include <string_view>

#ifdef __APPLE__
#include <mach-o/dyld.h>
#endif

namespace {

constexpr uint32_t zlib_capability = UINT32_C(1) << 6;
constexpr uint32_t jpeg_capability = UINT32_C(1) << 7;
int failures;

#define CHECK(value) do { if (!(value)) { \
    std::cerr << __FILE__ << ':' << __LINE__ << " check failed: " #value "\n"; \
    ++failures; } } while (false)

struct Error {
    uint8_t text[256]{};
    hplr_error value{HPLR_ABI_VERSION, sizeof(hplr_error), 0, 0,
                     text, sizeof(text), 0};
};

template<typename T> T output() {
    T value{};
    value.abi_version = HPLR_ABI_VERSION;
    value.struct_size = sizeof(T);
    return value;
}

hplr_output_config configuration(bool linear) {
    auto value = output<hplr_output_config>();
    value.output_bits = linear ? 16 : 8;
    value.output_color = 1;
    value.gamma_power = linear ? 1.0 : 1.0 / 2.4;
    value.gamma_slope = linear ? 1.0 : 12.92;
    value.no_auto_bright = linear ? 1 : 0;
    value.use_camera_wb = 1;
    value.use_camera_matrix = 1;
    return value;
}

#ifdef __APPLE__
void check_loaded_images(const std::filesystem::path &executable, bool staged,
                         const std::filesystem::path &expected_libraw_directory) {
    const auto staging = std::filesystem::weakly_canonical(executable).parent_path();
    const auto expected_libraw = staged
        ? staging
        : std::filesystem::weakly_canonical(expected_libraw_directory);
    bool bridge_found = false;
    bool libraw_found = false;
    for (uint32_t index = 0; index < _dyld_image_count(); ++index) {
        const char *name = _dyld_get_image_name(index);
        if (!name) continue;
        const auto image = std::filesystem::weakly_canonical(name);
        if (image.filename() == "libhappyphoton_libraw_bridge.dylib") {
            bridge_found = true;
            if (staged) CHECK(image.parent_path() == staging);
        } else if (image.filename().string().starts_with("libraw.") &&
                   image.extension() == ".dylib") {
            libraw_found = true;
            if (staged) {
                CHECK(image.filename() == "libraw.25.dylib");
            }
            CHECK(image.parent_path() == expected_libraw);
        }
    }
    CHECK(libraw_found);
    if (staged) CHECK(bridge_found);
}
#endif

void decode(const std::filesystem::path &fixture, bool linear) {
    const auto utf8 = fixture.u8string();
    Error error;
    hplr_handle handle{};
    CHECK(hplr_open_utf8(reinterpret_cast<const uint8_t *>(utf8.data()),
          static_cast<uint32_t>(utf8.size()), &handle, &error.value) == HPLR_OK);
    CHECK(hplr_unpack(handle, &error.value) == HPLR_OK);
    auto config = configuration(linear);
    CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_OK);
    CHECK(hplr_process(handle, &error.value) == HPLR_OK);
    auto image = output<hplr_image_descriptor>();
    CHECK(hplr_make_processed_image(handle, &image, &error.value) == HPLR_OK);
    CHECK(image.bits_per_sample == static_cast<uint32_t>(config.output_bits));
    CHECK(image.channels == 3 && image.byte_length > 0);
    CHECK(hplr_free_image(image.allocation, &error.value) == HPLR_OK);
    CHECK(hplr_close(handle, &error.value) == HPLR_OK);
}

hplr_camera_facts camera_facts(const std::filesystem::path &fixture) {
    const auto utf8 = fixture.u8string();
    Error error;
    hplr_handle handle{};
    CHECK(hplr_open_utf8(reinterpret_cast<const uint8_t *>(utf8.data()),
          static_cast<uint32_t>(utf8.size()), &handle, &error.value) == HPLR_OK);
    CHECK(hplr_unpack(handle, &error.value) == HPLR_OK);
    auto facts = output<hplr_camera_facts>();
    CHECK(hplr_get_camera_facts(handle, &facts, &error.value) == HPLR_OK);
    CHECK(facts.pre_multiplier_count == 0 ||
          facts.pre_multiplier_count == facts.multiplier_count);
    CHECK(facts.camera_from_xyz_rows == 0 ||
          facts.camera_from_xyz_rows == facts.multiplier_count);
    CHECK(facts.camera_from_xyz_columns ==
          (facts.camera_from_xyz_rows ? 3u : 0u));
    CHECK(facts.linear_max_count == 0 ||
          facts.linear_max_count == facts.multiplier_count);
    CHECK(hplr_close(handle, &error.value) == HPLR_OK);
    return facts;
}

template<typename T>
void print_values(const T *values, uint32_t count) {
    std::cout << '[';
    for (uint32_t index = 0; index < count; ++index) {
        if (index) std::cout << ',';
        std::cout << values[index];
    }
    std::cout << ']';
}

void print_camera_facts(const hplr_camera_facts &facts) {
    std::cout << ",\"camera_facts\":{\"pre_mul_count\":"
              << facts.pre_multiplier_count << ",\"pre_mul\":";
    print_values(facts.pre_multipliers, facts.pre_multiplier_count);
    std::cout << ",\"camera_from_xyz_rows\":" << facts.camera_from_xyz_rows
              << ",\"camera_from_xyz_columns\":" << facts.camera_from_xyz_columns
              << ",\"camera_from_xyz\":";
    print_values(facts.camera_from_xyz,
                 facts.camera_from_xyz_rows * facts.camera_from_xyz_columns);
    std::cout << ",\"linear_max_count\":" << facts.linear_max_count
              << ",\"linear_max\":";
    print_values(facts.linear_max, facts.linear_max_count);
    std::cout << '}';
}

} // namespace

int main(int argc, char **argv) {
    const bool staged = argc == 3 && std::string_view(argv[1]) == "--staged";
    const bool build_tree = argc == 4 &&
        std::string_view(argv[1]) == "--build-libraw-dir";
    if (!staged && !build_tree) return 2;
#ifdef __APPLE__
    check_loaded_images(argv[0], staged, build_tree ? argv[2] : "");
#endif
    CHECK(hplr_abi_version() == HPLR_ABI_VERSION);
    Error error;
    auto runtime = output<hplr_runtime_info>();
    CHECK(hplr_runtime(&runtime, &error.value) == HPLR_OK);
    CHECK(runtime.libraw_version_number == 0x001602);
    CHECK((runtime.capabilities & jpeg_capability) != 0);
    CHECK((runtime.capabilities & zlib_capability) != 0);
    CHECK(runtime.thread_safe_variant == HPLR_EXPECT_REENTRANT);
    const auto fixture = argv[staged ? 2 : 3];
    const auto facts = camera_facts(fixture);
    decode(fixture, true);
    decode(fixture, false);
    if (!failures) {
        std::cout << std::setprecision(std::numeric_limits<float>::max_digits10);
        std::cout << "{\"bridge_abi\":" << hplr_abi_version()
                  << ",\"libraw_version\":" << runtime.libraw_version_number
                  << ",\"capabilities\":" << runtime.capabilities
                  << ",\"thread_safe\":"
                  << (runtime.thread_safe_variant ? "true" : "false");
        print_camera_facts(facts);
        std::cout << "}\n";
    }
    return failures ? 1 : 0;
}
