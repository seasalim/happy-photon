#include <libraw/libraw.h>

#if HPLR_EXPECT_LCMS
#include <lcms2.h>
#endif

#include <chrono>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>

namespace {

constexpr uint32_t zlib_capability = UINT32_C(1) << 6;
constexpr uint32_t jpeg_capability = UINT32_C(1) << 7;

#if HPLR_EXPECT_LCMS
class Profile {
public:
    Profile() {
        handle_ = cmsCreate_sRGBProfile();
        path_ = std::filesystem::temp_directory_path() / "hplr-validation-srgb.icc";
        if (!handle_ || !cmsSaveProfileToFile(handle_, path_.string().c_str()))
            throw std::runtime_error("could not create LCMS validation profile");
    }
    ~Profile() {
        if (handle_) cmsCloseProfile(handle_);
        std::error_code ignored;
        std::filesystem::remove(path_, ignored);
    }
    const std::string path() const { return path_.string(); }
private:
    cmsHPROFILE handle_{};
    std::filesystem::path path_;
};
#endif

uint64_t process_raw(const char *fixture) {
    LibRaw raw;
    if (raw.open_file(fixture) != LIBRAW_SUCCESS ||
        raw.unpack() != LIBRAW_SUCCESS) return 0;
    raw.imgdata.params.output_bps = 8;
    raw.imgdata.params.output_color = 1;
    raw.imgdata.params.use_camera_wb = 1;
#if HPLR_EXPECT_LCMS
    Profile profile;
    auto profile_path = profile.path();
    raw.imgdata.params.output_profile = profile_path.data();
#endif
    if (raw.dcraw_process() != LIBRAW_SUCCESS) return 0;
    int error = LIBRAW_SUCCESS;
    auto *image = raw.dcraw_make_mem_image(&error);
    if (!image || error != LIBRAW_SUCCESS) return 0;
    uint64_t checksum = 1469598103934665603ull;
    for (size_t i = 0; i < image->data_size; ++i)
        checksum = (checksum ^ image->data[i]) * 1099511628211ull;
    LibRaw::dcraw_clear_mem(image);
    return checksum;
}

} // namespace

int main(int argc, char **argv) {
    if (argc != 2) return 2;
    if (LibRaw::versionNumber() != 0x001602) return 3;
    const auto capabilities = LibRaw::capabilities();
    if ((capabilities & jpeg_capability) == 0 ||
        (capabilities & zlib_capability) == 0) return 4;
    const auto started = std::chrono::steady_clock::now();
    const auto checksum = process_raw(argv[1]);
    const auto elapsed = std::chrono::duration<double, std::milli>(
        std::chrono::steady_clock::now() - started).count();
    if (!checksum) return 5;
    std::cout << "{\"version\":" << LibRaw::versionNumber()
              << ",\"capabilities\":" << capabilities
              << ",\"lcms\":" << (HPLR_EXPECT_LCMS ? "true" : "false")
              << ",\"openmp\":" << (HPLR_EXPECT_OPENMP ? "true" : "false")
              << ",\"elapsed_ms\":" << elapsed
              << ",\"checksum\":" << checksum << "}\n";
    return 0;
}
