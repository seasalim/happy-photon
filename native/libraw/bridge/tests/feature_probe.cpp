#include <libraw/libraw.h>

#if HPLR_EXPECT_LCMS
#include <lcms2.h>
#endif

#include <chrono>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <limits>
#include <string>

namespace {

constexpr uint32_t zlib_capability = UINT32_C(1) << 6;
constexpr uint32_t jpeg_capability = UINT32_C(1) << 7;

struct CameraFacts {
    uint32_t count{};
    uint32_t pre_mul_count{};
    float pre_mul[4]{};
    uint32_t camera_from_xyz_rows{};
    uint32_t camera_from_xyz_columns{};
    float camera_from_xyz[12]{};
    uint32_t linear_max_count{};
    uint32_t linear_max[4]{};
};

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

void read_camera_facts(const LibRaw &raw, CameraFacts &facts) {
    const auto &color = raw.imgdata.color;
    facts.count = 3;
    if (std::isfinite(color.cam_mul[3]) && color.cam_mul[3] > 0 &&
        (std::abs(color.rgb_cam[0][3]) > 0 || std::abs(color.rgb_cam[1][3]) > 0 ||
         std::abs(color.rgb_cam[2][3]) > 0)) facts.count = 4;

    bool pre_mul_valid = true;
    for (uint32_t channel = 0; channel < facts.count; ++channel)
        pre_mul_valid &= std::isfinite(color.pre_mul[channel]) && color.pre_mul[channel] > 0;
    if (pre_mul_valid) {
        facts.pre_mul_count = facts.count;
        for (uint32_t channel = 0; channel < facts.count; ++channel)
            facts.pre_mul[channel] = color.pre_mul[channel];
    }

    bool matrix_valid = true;
    bool matrix_nonzero = false;
    for (uint32_t channel = 0; channel < facts.count; ++channel) {
        for (uint32_t column = 0; column < 3; ++column) {
            const auto value = color.cam_xyz[channel][column];
            matrix_valid &= std::isfinite(value);
            if (std::isfinite(value)) matrix_nonzero |= value != 0;
        }
    }
    if (matrix_valid && matrix_nonzero) {
        facts.camera_from_xyz_rows = facts.count;
        facts.camera_from_xyz_columns = 3;
        for (uint32_t row = 0; row < facts.count; ++row)
            for (uint32_t column = 0; column < 3; ++column)
                facts.camera_from_xyz[row * 3 + column] = color.cam_xyz[row][column];
    }

    bool linear_max_valid = true;
    for (uint32_t channel = 0; channel < facts.count; ++channel)
        linear_max_valid &= color.linear_max[channel] != 0;
    if (linear_max_valid) {
        facts.linear_max_count = facts.count;
        for (uint32_t channel = 0; channel < facts.count; ++channel)
            facts.linear_max[channel] = color.linear_max[channel];
    }
}

uint64_t process_raw(const char *fixture, CameraFacts &facts) {
    LibRaw raw;
    if (raw.open_file(fixture) != LIBRAW_SUCCESS ||
        raw.unpack() != LIBRAW_SUCCESS) return 0;
    read_camera_facts(raw, facts);
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

template<typename T>
void print_values(const T *values, uint32_t count) {
    std::cout << '[';
    for (uint32_t index = 0; index < count; ++index) {
        if (index) std::cout << ',';
        std::cout << values[index];
    }
    std::cout << ']';
}

void print_camera_facts(const CameraFacts &facts) {
    std::cout << ",\"camera_facts\":{\"pre_mul_count\":" << facts.pre_mul_count
              << ",\"pre_mul\":";
    print_values(facts.pre_mul, facts.pre_mul_count);
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
    if (argc != 2) return 2;
    if (LibRaw::versionNumber() != 0x001602) return 3;
    const auto capabilities = LibRaw::capabilities();
    if ((capabilities & jpeg_capability) == 0 ||
        (capabilities & zlib_capability) == 0) return 4;
    const auto started = std::chrono::steady_clock::now();
    CameraFacts facts;
    const auto checksum = process_raw(argv[1], facts);
    const auto elapsed = std::chrono::duration<double, std::milli>(
        std::chrono::steady_clock::now() - started).count();
    if (!checksum) return 5;
    std::cout << std::setprecision(std::numeric_limits<float>::max_digits10);
    std::cout << "{\"version\":" << LibRaw::versionNumber()
              << ",\"capabilities\":" << capabilities
              << ",\"lcms\":" << (HPLR_EXPECT_LCMS ? "true" : "false")
              << ",\"openmp\":" << (HPLR_EXPECT_OPENMP ? "true" : "false")
              << ",\"elapsed_ms\":" << elapsed
              << ",\"checksum\":" << checksum;
    print_camera_facts(facts);
    std::cout << "}\n";
    return 0;
}
