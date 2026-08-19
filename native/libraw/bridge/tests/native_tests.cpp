#include "happyphoton_libraw_bridge.h"
#include "test_hooks.h"

#include <atomic>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

namespace {

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

hplr_handle open(const std::filesystem::path &path) {
    const auto utf8 = path.u8string();
    hplr_handle handle{};
    Error error;
    CHECK(hplr_open_utf8(reinterpret_cast<const uint8_t *>(utf8.data()),
          static_cast<uint32_t>(utf8.size()), &handle, &error.value) == HPLR_OK);
    return handle;
}

hplr_output_config linear(bool half = false) {
    auto value = output<hplr_output_config>();
    value.output_bits = 16;
    value.output_color = 1;
    value.gamma_power = 1;
    value.gamma_slope = 1;
    value.no_auto_bright = 1;
    value.half_size = half;
    value.highlight_mode = 0;
    value.fbdd_noise_reduction = 0;
    value.use_camera_wb = 1;
    value.use_auto_wb = 0;
    value.user_mul[0] = value.user_mul[1] = value.user_mul[2] =
        value.user_mul[3] = 0;
    value.use_camera_matrix = 1;
    return value;
}

hplr_output_config srgb8() {
    auto value = linear();
    value.output_bits = 8;
    value.gamma_power = 1.0 / 2.4;
    value.gamma_slope = 12.92;
    value.no_auto_bright = 0;
    return value;
}

void invalid_inputs() {
    Error error;
    hplr_handle handle{};
    const uint8_t nul[] = {'x', 0, 'y'};
    CHECK(hplr_open_utf8(nul, 3, &handle, &error.value) == HPLR_E_ARGUMENT);
    const uint8_t malformed[] = {0xf0, 0x80, 0x80, 0x80};
    CHECK(hplr_open_utf8(malformed, 4, &handle, &error.value) == HPLR_E_ARGUMENT);
    CHECK(hplr_close(UINT64_C(0x99999999), &error.value) == HPLR_E_HANDLE);
    CHECK(hplr_release_mosaic(UINT64_C(0x88888888), &error.value) == HPLR_E_OWNERSHIP);
    auto config = linear();
    config.abi_version = 77;
    CHECK(hplr_configure_output(UINT64_C(1), &config, &error.value) == HPLR_E_ABI);
    config = linear();
    config.struct_size--;
    CHECK(hplr_configure_output(UINT64_C(1), &config, &error.value) == HPLR_E_ABI);
}

void runtime_and_unicode(const std::filesystem::path &fixture) {
    Error error;
    auto runtime = output<hplr_runtime_info>();
    CHECK(hplr_runtime(&runtime, &error.value) == HPLR_OK);
    CHECK(runtime.libraw_version_number == 0x001602);
    CHECK(runtime.version_string_length > 0);
    CHECK(runtime.thread_safe_variant == HPLR_EXPECT_REENTRANT);

    const auto directory = std::filesystem::temp_directory_path() / u8"hplr-写真";
    std::filesystem::create_directories(directory);
    const auto copy = directory / u8"カメラ.cr2";
    std::filesystem::copy_file(fixture, copy,
        std::filesystem::copy_options::overwrite_existing);
    const auto handle = open(copy);
    CHECK(handle != 0);
    CHECK(hplr_close(handle, &error.value) == HPLR_OK);
    CHECK(hplr_close(handle, &error.value) == HPLR_E_HANDLE);
    std::filesystem::remove_all(directory);
}

void facts_and_lifetime(const std::filesystem::path &fixture) {
    Error error;
    const auto handle = open(fixture);
    auto dimensions = output<hplr_dimensions>();
    auto sensor = output<hplr_sensor_identity>();
    auto metadata = output<hplr_metadata>();
    auto camera = output<hplr_camera_facts>();
    auto fuji = output<hplr_fuji_facts>();
    CHECK(hplr_get_dimensions(handle, &dimensions, &error.value) == HPLR_OK);
    CHECK(dimensions.raw_width > dimensions.visible_width);
    CHECK(hplr_get_sensor_identity(handle, &sensor, &error.value) == HPLR_OK);
    CHECK(sensor.colors >= 3 && sensor.xtrans_count == 36);
    CHECK(hplr_get_metadata(handle, &metadata, &error.value) == HPLR_OK);
    CHECK(metadata.make_length > 0 && metadata.model_length > 0);
    CHECK(hplr_get_camera_facts(handle, &camera, &error.value) == HPLR_OK);
    CHECK(camera.multiplier_count == 3 || camera.multiplier_count == 4);
    CHECK(hplr_get_fuji_facts(handle, &fuji, &error.value) == HPLR_ABSENT);

    auto mosaic = output<hplr_mosaic_descriptor>();
    CHECK(hplr_borrow_mosaic(handle, &mosaic, &error.value) == HPLR_E_STATE);
    const auto thumb_status = hplr_unpack_thumbnail(handle, &error.value);
    if (thumb_status == HPLR_OK) {
        auto thumbnail = output<hplr_image_descriptor>();
        CHECK(hplr_make_thumbnail(handle, &thumbnail, &error.value) == HPLR_OK);
        CHECK(thumbnail.data && thumbnail.byte_length > 0);
        CHECK(hplr_free_image(thumbnail.allocation, &error.value) == HPLR_OK);
        CHECK(hplr_free_image(thumbnail.allocation, &error.value) == HPLR_E_OWNERSHIP);
    }
    CHECK(hplr_unpack(handle, &error.value) == HPLR_OK);
    camera = output<hplr_camera_facts>();
    CHECK(hplr_get_camera_facts(handle, &camera, &error.value) == HPLR_OK);
    CHECK(camera.pre_multiplier_count == 0 ||
          camera.pre_multiplier_count == camera.multiplier_count);
    CHECK(camera.camera_from_xyz_rows == 0 ||
          camera.camera_from_xyz_rows == camera.multiplier_count);
    CHECK(camera.camera_from_xyz_columns ==
          (camera.camera_from_xyz_rows ? 3u : 0u));
    CHECK(camera.linear_max_count == 0 ||
          camera.linear_max_count == camera.multiplier_count);
    for (uint32_t channel = 0; channel < camera.pre_multiplier_count; ++channel) {
        CHECK(std::isfinite(camera.pre_multipliers[channel]));
        CHECK(camera.pre_multipliers[channel] > 0);
    }
    for (uint32_t channel = 0; channel < camera.linear_max_count; ++channel)
        CHECK(camera.linear_max[channel] > 0);
    for (uint32_t row = 0; row < camera.camera_from_xyz_rows; ++row)
        for (uint32_t column = 0; column < camera.camera_from_xyz_columns; ++column)
            CHECK(std::isfinite(camera.camera_from_xyz[row * 3 + column]));
    CHECK(hplr_borrow_mosaic(handle, &mosaic, &error.value) == HPLR_OK);
    CHECK(mosaic.data && mosaic.byte_length ==
        static_cast<uint64_t>(mosaic.raw_pitch) * mosaic.raw_height);
    CHECK(mosaic.cblack_count == HPLR_CBLACK_COUNT);
    auto second_mosaic = output<hplr_mosaic_descriptor>();
    CHECK(hplr_borrow_mosaic(handle, &second_mosaic, &error.value) == HPLR_E_BUSY);
    auto config = linear();
    CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_OK);
    CHECK(hplr_process(handle, &error.value) == HPLR_E_BUSY);
    CHECK(hplr_close(handle, &error.value) == HPLR_E_BUSY);
    CHECK(hplr_release_mosaic(mosaic.lease, &error.value) == HPLR_OK);
    CHECK(hplr_release_mosaic(mosaic.lease, &error.value) == HPLR_E_OWNERSHIP);
    CHECK(hplr_process(handle, &error.value) == HPLR_OK);
    mosaic = output<hplr_mosaic_descriptor>();
    CHECK(hplr_borrow_mosaic(handle, &mosaic, &error.value) == HPLR_E_STATE);
    auto processed = output<hplr_image_descriptor>();
    CHECK(hplr_make_processed_image(handle, &processed, &error.value) == HPLR_OK);
    CHECK(processed.bits_per_sample == 16 && processed.channels == 3);
    CHECK(hplr_free_image(processed.allocation, &error.value) == HPLR_OK);
    CHECK(hplr_close(handle, &error.value) == HPLR_OK);
}

void abi_v3_configuration(const std::filesystem::path &fixture) {
    Error error;
    const auto handle = open(fixture);
    CHECK(hplr_unpack(handle, &error.value) == HPLR_OK);
    auto dimensions = output<hplr_dimensions>();
    CHECK(hplr_get_dimensions(handle, &dimensions, &error.value) == HPLR_OK);
    hplr_test_params defaults{};
    CHECK(hplr_test_get_params(handle, &defaults, &error.value) == HPLR_OK);

    auto config = linear();
    CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_OK);
    hplr_test_params absent{};
    CHECK(hplr_test_get_params(handle, &absent, &error.value) == HPLR_OK);
    CHECK(absent.user_sat == defaults.user_sat);
    CHECK(absent.user_qual == defaults.user_qual);
    for (uint32_t index = 0; index < 4; ++index)
        CHECK(absent.cropbox[index] == defaults.cropbox[index]);

    config.user_sat = 65535;
    config.user_qual_present = 1;
    config.user_qual = 0;
    config.cropbox_present = 1;
    config.cropbox[0] = 7;
    config.cropbox[1] = 9;
    config.cropbox[2] = 64;
    config.cropbox[3] = 48;
    CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_OK);
    hplr_test_params present{};
    CHECK(hplr_test_get_params(handle, &present, &error.value) == HPLR_OK);
    CHECK(present.user_sat == 65535 && present.user_qual == 0);
    for (uint32_t index = 0; index < 4; ++index)
        CHECK(present.cropbox[index] == config.cropbox[index]);

    config = linear();
    CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_OK);
    hplr_test_params reset{};
    CHECK(hplr_test_get_params(handle, &reset, &error.value) == HPLR_OK);
    CHECK(reset.user_sat == defaults.user_sat && reset.user_qual == defaults.user_qual);
    for (uint32_t index = 0; index < 4; ++index)
        CHECK(reset.cropbox[index] == defaults.cropbox[index]);

    for (const int32_t quality : {0, 1, 2, 3, 4, 11, 12}) {
        config = linear();
        config.user_qual_present = 1;
        config.user_qual = quality;
        CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_OK);
    }

    config.user_sat = -1;
    CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_E_ARGUMENT);
    config.user_sat = 65536;
    CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_E_ARGUMENT);
    config = linear();
    config.user_qual_present = 1;
    config.user_qual = 5;
    CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_E_ARGUMENT);
    config = linear();
    config.cropbox_present = 1;
    config.cropbox[0] = UINT32_MAX;
    config.cropbox[2] = 1;
    config.cropbox[3] = 1;
    CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_E_ARGUMENT);
    config.cropbox[0] = 0;
    config.cropbox[2] = 0;
    CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_E_ARGUMENT);
    config.cropbox[0] = dimensions.visible_width;
    config.cropbox[2] = 1;
    CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_E_ARGUMENT);

    config = linear();
    config.cropbox_present = 1;
    config.cropbox[0] = 1;
    config.cropbox[2] = config.cropbox[3] = 16;
    CHECK(hplr_test_set_crop_class(handle, 1, 0, &error.value) == HPLR_OK);
    CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_E_ARGUMENT);
    config.cropbox[0] = 16;
    CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_OK);
    CHECK(hplr_test_set_crop_class(handle, 9, 0, &error.value) == HPLR_OK);
    config.cropbox[0] = 1;
    CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_E_ARGUMENT);
    config.cropbox[0] = 6;
    CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_OK);
    CHECK(hplr_test_set_crop_class(handle, 9, 1, &error.value) == HPLR_OK);
    CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_E_ARGUMENT);
    CHECK(hplr_close(handle, &error.value) == HPLR_OK);
}

void optional_camera_facts(const std::filesystem::path &fixture) {
    constexpr uint32_t all_facts = HPLR_TEST_PRE_MULTIPLIERS |
        HPLR_TEST_CAMERA_FROM_XYZ | HPLR_TEST_LINEAR_MAX;
    Error error;
    const auto handle = open(fixture);
    CHECK(hplr_test_set_camera_facts(handle, all_facts, &error.value) == HPLR_OK);
    auto camera = output<hplr_camera_facts>();
    CHECK(hplr_get_camera_facts(handle, &camera, &error.value) == HPLR_OK);
    CHECK(camera.multiplier_count == 4);
    CHECK(camera.matrix_rows == 3 && camera.matrix_columns == 4);
    CHECK(camera.pre_multiplier_count == 4);
    CHECK(camera.camera_from_xyz_rows == 4 && camera.camera_from_xyz_columns == 3);
    CHECK(camera.linear_max_count == 4);
    for (uint32_t channel = 0; channel < 4; ++channel) {
        CHECK(camera.multipliers[channel] == static_cast<float>(10 + channel));
        CHECK(camera.pre_multipliers[channel] == static_cast<float>(20 + channel));
        CHECK(camera.linear_max[channel] == 1000 + channel);
        for (uint32_t row = 0; row < 3; ++row)
            CHECK(camera.camera_to_srgb[row * 4 + channel] ==
                static_cast<float>(300 + row * 10 + channel));
        for (uint32_t column = 0; column < 3; ++column)
            CHECK(camera.camera_from_xyz[channel * 3 + column] ==
                static_cast<float>(200 + channel * 10 + column));
    }

    for (const auto missing : {HPLR_TEST_PRE_MULTIPLIERS,
                               HPLR_TEST_CAMERA_FROM_XYZ,
                               HPLR_TEST_LINEAR_MAX}) {
        CHECK(hplr_test_set_camera_facts(handle, all_facts ^ missing,
              &error.value) == HPLR_OK);
        camera = output<hplr_camera_facts>();
        CHECK(hplr_get_camera_facts(handle, &camera, &error.value) == HPLR_OK);
        CHECK(camera.multiplier_count == 4);
        CHECK(camera.pre_multiplier_count ==
            (missing == HPLR_TEST_PRE_MULTIPLIERS ? 0 : 4));
        CHECK(camera.camera_from_xyz_rows ==
            (missing == HPLR_TEST_CAMERA_FROM_XYZ ? 0 : 4));
        CHECK(camera.camera_from_xyz_columns ==
            (missing == HPLR_TEST_CAMERA_FROM_XYZ ? 0 : 3));
        CHECK(camera.linear_max_count ==
            (missing == HPLR_TEST_LINEAR_MAX ? 0 : 4));
    }
    CHECK(hplr_close(handle, &error.value) == HPLR_OK);
}

void configuration_and_failures(const std::filesystem::path &fixture) {
    for (const auto config : {linear(true), srgb8()}) {
        Error error;
        const auto handle = open(fixture);
        CHECK(hplr_unpack(handle, &error.value) == HPLR_OK);
        hplr_test_params before{};
        CHECK(hplr_test_get_params(handle, &before, &error.value) == HPLR_OK);
        CHECK(hplr_configure_output(handle, &config, &error.value) == HPLR_OK);
        hplr_test_params after{};
        CHECK(hplr_test_get_params(handle, &after, &error.value) == HPLR_OK);
        CHECK(after.output_bits == config.output_bits);
        CHECK(after.gamma[0] == config.gamma_power && after.gamma[1] == config.gamma_slope);
        CHECK(after.use_camera_wb == 1 && after.use_auto_wb == 0);
        CHECK(after.user_flip == before.user_flip);
        CHECK(after.use_fuji_rotate == before.use_fuji_rotate);
        CHECK(after.use_p1_correction == before.use_p1_correction);
        CHECK(hplr_process(handle, &error.value) == HPLR_OK);
        hplr_test_fail_next_image_allocation();
        auto image = output<hplr_image_descriptor>();
        CHECK(hplr_make_processed_image(handle, &image, &error.value) == HPLR_E_RESOURCE);
        CHECK(error.value.error_class == HPLR_ERROR_BRIDGE);
        CHECK(hplr_make_processed_image(handle, &image, &error.value) == HPLR_OK);
        CHECK(image.bits_per_sample == static_cast<uint32_t>(config.output_bits));
        CHECK(hplr_close(handle, &error.value) == HPLR_E_BUSY);
        CHECK(hplr_free_image(image.allocation, &error.value) == HPLR_OK);
        CHECK(hplr_close(handle, &error.value) == HPLR_OK);
    }
}

void concurrent_rejection(const std::filesystem::path &fixture) {
    Error worker_error;
    Error caller_error;
    const auto handle = open(fixture);
    std::atomic<bool> started{false};
    std::thread worker([&] {
        started = true;
        CHECK(hplr_test_hold_operation(handle, 100, &worker_error.value) == HPLR_OK);
    });
    while (!started.load()) std::this_thread::yield();
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
    auto dimensions = output<hplr_dimensions>();
    CHECK(hplr_get_dimensions(handle, &dimensions, &caller_error.value) == HPLR_E_BUSY);
    CHECK(hplr_close(handle, &caller_error.value) == HPLR_E_BUSY);
    worker.join();
    CHECK(hplr_close(handle, &caller_error.value) == HPLR_OK);
}

void unsupported_mosaic(const std::filesystem::path &fixture) {
    Error error;
    const auto handle = open(fixture);
    CHECK(hplr_unpack(handle, &error.value) == HPLR_OK);
    CHECK(hplr_test_set_foveon(handle, 1, &error.value) == HPLR_OK);
    auto mosaic = output<hplr_mosaic_descriptor>();
    CHECK(hplr_borrow_mosaic(handle, &mosaic, &error.value) == HPLR_UNAVAILABLE);
    CHECK(error.value.error_class == HPLR_ERROR_NONE);
    CHECK(hplr_test_set_foveon(handle, 0, &error.value) == HPLR_OK);
    CHECK(hplr_close(handle, &error.value) == HPLR_OK);
}

void variant_serialization(const std::filesystem::path &fixture) {
    Error first_error;
    Error second_error;
    const auto first = open(fixture);
    const auto second = open(fixture);
    const auto started = std::chrono::steady_clock::now();
    std::thread one([&] { CHECK(hplr_test_hold_libraw(first, 100, &first_error.value) == HPLR_OK); });
    std::thread two([&] { CHECK(hplr_test_hold_libraw(second, 100, &second_error.value) == HPLR_OK); });
    one.join();
    two.join();
    const auto elapsed = std::chrono::steady_clock::now() - started;
#if HPLR_EXPECT_REENTRANT
    CHECK(elapsed < std::chrono::milliseconds(180));
#else
    CHECK(elapsed >= std::chrono::milliseconds(190));
#endif
    CHECK(hplr_close(first, &first_error.value) == HPLR_OK);
    CHECK(hplr_close(second, &second_error.value) == HPLR_OK);
}

} // namespace

int main(int argc, char **argv) {
    if (argc != 2) return 2;
    const std::filesystem::path fixture = argv[1];
    invalid_inputs();
    runtime_and_unicode(fixture);
    facts_and_lifetime(fixture);
    optional_camera_facts(fixture);
    configuration_and_failures(fixture);
    abi_v3_configuration(fixture);
    concurrent_rejection(fixture);
    unsupported_mosaic(fixture);
    variant_serialization(fixture);
    return failures ? 1 : 0;
}
