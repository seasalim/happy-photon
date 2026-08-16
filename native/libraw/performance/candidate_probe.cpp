#include "process_memory.hpp"
#include "happyphoton_libraw_bridge.h"

#include <chrono>
#include <filesystem>
#include <iomanip>
#include <iostream>
#include <stdexcept>
#include <string>

namespace {

struct DecodeResult {
    uint32_t width{};
    uint32_t height{};
    uint32_t bits{};
    uint32_t channels{};
    uint64_t bytes{};
    uint64_t checksum{};
};

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

void check(int32_t status, const Error &error) {
    if (status != HPLR_OK)
        throw std::runtime_error(std::string(
            reinterpret_cast<const char *>(error.text), error.value.text_length));
}

DecodeResult decode(const std::filesystem::path &fixture, bool linear) {
    const auto utf8 = fixture.u8string();
    Error error;
    hplr_handle handle{};
    check(hplr_open_utf8(reinterpret_cast<const uint8_t *>(utf8.data()),
        static_cast<uint32_t>(utf8.size()), &handle, &error.value), error);
    try {
        check(hplr_unpack(handle, &error.value), error);
        auto config = output<hplr_output_config>();
        config.output_bits = linear ? 16 : 8;
        config.output_color = 1;
        config.gamma_power = linear ? 1.0 : 1.0 / 2.4;
        config.gamma_slope = linear ? 1.0 : 12.92;
        config.no_auto_bright = linear ? 1 : 0;
        config.half_size = linear ? 1 : 0;
        config.use_camera_wb = 1;
        config.use_camera_matrix = 1;
        check(hplr_configure_output(handle, &config, &error.value), error);
        check(hplr_process(handle, &error.value), error);
        auto image = output<hplr_image_descriptor>();
        check(hplr_make_processed_image(handle, &image, &error.value), error);
        const DecodeResult result{image.width, image.height, image.bits_per_sample,
            image.channels, image.byte_length,
            hplr::performance::checksum(image.data, image.byte_length)};
        check(hplr_free_image(image.allocation, &error.value), error);
        check(hplr_close(handle, &error.value), error);
        return result;
    } catch (...) {
        hplr_close(handle, &error.value);
        throw;
    }
}

} // namespace

int main(int argc, char **argv) {
    try {
        if (argc != 4) return 2;
        Error error;
        auto runtime = output<hplr_runtime_info>();
        check(hplr_runtime(&runtime, &error.value), error);
        if (runtime.libraw_version_number != 0x001602)
            throw std::runtime_error("candidate runtime is not LibRaw 0.22.2");
        const std::string configuration = argv[2];
        const bool linear = configuration == "linear16-preview";
        if (!linear && configuration != "srgb8-full") return 2;
        const auto host_baseline = hplr::performance::current_process_bytes();
        decode(argv[1], linear);
        const auto start = std::chrono::steady_clock::now();
        const auto image = decode(argv[1], linear);
        const auto elapsed = std::chrono::duration<double, std::milli>(
            std::chrono::steady_clock::now() - start).count();
        const auto peak = hplr::performance::peak_process_bytes();
        std::cout << std::setprecision(12)
                  << "{\"Configuration\":\"" << configuration
                  << "\",\"Sample\":" << std::stoi(argv[3])
                  << ",\"ElapsedMilliseconds\":" << elapsed
                  << ",\"HostBaselineBytes\":" << host_baseline
                  << ",\"PeakProcessBytes\":" << peak
                  << ",\"PeakAboveHostBytes\":" << (peak > host_baseline ? peak - host_baseline : 0)
                  << ",\"Width\":" << image.width << ",\"Height\":" << image.height
                  << ",\"Bits\":" << image.bits << ",\"Channels\":" << image.channels
                  << ",\"Bytes\":" << image.bytes << ",\"Checksum\":\""
                  << std::hex << std::uppercase << image.checksum << "\"}\n";
        return 0;
    } catch (const std::exception &error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
