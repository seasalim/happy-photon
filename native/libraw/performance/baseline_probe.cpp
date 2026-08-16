#include <libraw/libraw.h>

#include "process_memory.hpp"

#include <chrono>
#include <filesystem>
#include <iomanip>
#include <iostream>
#include <memory>
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

void check(int code) {
    if (code != LIBRAW_SUCCESS) throw std::runtime_error(libraw_strerror(code));
}

DecodeResult decode_image(const std::filesystem::path &fixture, bool linear) {
    LibRaw raw;
    check(raw.open_file(fixture.string().c_str()));
    check(raw.unpack());
    auto &params = raw.imgdata.params;
    params.output_color = 1;
    params.use_camera_wb = 1;
    params.use_auto_wb = 0;
    params.use_camera_matrix = 1;
    params.output_bps = linear ? 16 : 8;
    params.gamm[0] = linear ? 1.0 : 1.0 / 2.4;
    params.gamm[1] = linear ? 1.0 : 12.92;
    params.no_auto_bright = linear ? 1 : 0;
    params.half_size = linear ? 1 : 0;
    check(raw.dcraw_process());
    int error{};
    std::unique_ptr<libraw_processed_image_t, decltype(&LibRaw::dcraw_clear_mem)> image(
        raw.dcraw_make_mem_image(&error), &LibRaw::dcraw_clear_mem);
    check(error);
    if (!image || image->type != LIBRAW_IMAGE_BITMAP)
        throw std::runtime_error("LibRaw did not return a bitmap");
    return {image->width, image->height, image->bits, image->colors,
            image->data_size, hplr::performance::checksum(image->data, image->data_size)};
}

} // namespace

int main(int argc, char **argv) {
    try {
        if (argc != 4) return 2;
        if (LibRaw::versionNumber() != 0x001501)
            throw std::runtime_error("baseline runtime is not LibRaw 0.21.1");
        const std::string configuration = argv[2];
        const bool linear = configuration == "linear16-preview";
        if (!linear && configuration != "srgb8-full") return 2;
        const auto host_baseline = hplr::performance::current_process_bytes();
        decode_image(argv[1], linear);
        const auto start = std::chrono::steady_clock::now();
        const auto result = decode_image(argv[1], linear);
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
                  << ",\"Width\":" << result.width << ",\"Height\":" << result.height
                  << ",\"Bits\":" << result.bits << ",\"Channels\":" << result.channels
                  << ",\"Bytes\":" << result.bytes << ",\"Checksum\":\""
                  << std::hex << std::uppercase << result.checksum << "\"}\n";
        return 0;
    } catch (const std::exception &error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
