#include <libraw/libraw.h>
#include <windows.h>
#include <bcrypt.h>

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>

namespace {

constexpr char ExpectedHash[] =
    "F500C0732FEB21B188D5B52CEA05FD824D5B3C8016EB2CA68D8312ACC9F914B9";

std::string hex(const std::vector<unsigned char> &bytes) {
    std::ostringstream result;
    result << std::uppercase << std::hex << std::setfill('0');
    for (const auto value : bytes) result << std::setw(2) << static_cast<int>(value);
    return result.str();
}

std::string sha256(const std::filesystem::path &path) {
    std::ifstream input(path, std::ios::binary);
    if (!input) throw std::runtime_error("cannot open runtime for hashing");
    BCRYPT_ALG_HANDLE algorithm{};
    BCRYPT_HASH_HANDLE hash{};
    DWORD object_size{}, result_size{};
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) ||
        BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH,
            reinterpret_cast<PUCHAR>(&object_size), sizeof(object_size), &result_size, 0))
        throw std::runtime_error("cannot initialize SHA-256");
    std::vector<unsigned char> object(object_size), digest(32), buffer(65536);
    if (BCryptCreateHash(algorithm, &hash, object.data(), object_size, nullptr, 0, 0))
        throw std::runtime_error("cannot create SHA-256 state");
    while (input) {
        input.read(reinterpret_cast<char *>(buffer.data()), buffer.size());
        if (input.gcount() && BCryptHashData(hash, buffer.data(),
            static_cast<ULONG>(input.gcount()), 0)) throw std::runtime_error("hash failed");
    }
    if (BCryptFinishHash(hash, digest.data(), digest.size(), 0))
        throw std::runtime_error("cannot finish SHA-256");
    BCryptDestroyHash(hash);
    BCryptCloseAlgorithmProvider(algorithm, 0);
    return hex(digest);
}

std::filesystem::path verify_runtime() {
    const auto module = GetModuleHandleW(L"raw_r.dll");
    if (!module) throw std::runtime_error("raw_r.dll is not loaded");
    std::wstring module_buffer(32768, L'\0');
    const auto length = GetModuleFileNameW(module, module_buffer.data(),
        static_cast<DWORD>(module_buffer.size()));
    if (!length) throw std::runtime_error("cannot resolve raw_r.dll path");
    module_buffer.resize(length);
    const auto module_path = std::filesystem::canonical(module_buffer);
    std::wstring executable_buffer(32768, L'\0');
    const auto executable_length = GetModuleFileNameW(nullptr, executable_buffer.data(),
        static_cast<DWORD>(executable_buffer.size()));
    executable_buffer.resize(executable_length);
    if (module_path.parent_path() != std::filesystem::canonical(
            std::filesystem::path(executable_buffer).parent_path()))
        throw std::runtime_error("raw_r.dll did not load from the isolated directory");
    if (sha256(module_path) != ExpectedHash)
        throw std::runtime_error("raw_r.dll does not match the audited Sdcb hash");
    if (LibRaw::versionNumber() != 0x001501)
        throw std::runtime_error("raw_r.dll is not LibRaw 0.21.1");
    return module_path;
}

std::string escape(const char *value, size_t capacity) {
    std::ostringstream output;
    for (size_t index = 0; index < capacity && value[index]; ++index) {
        const unsigned char byte = value[index];
        if (byte == '\\' || byte == '"') output << '\\' << byte;
        else if (byte >= 0x20 && byte < 0x7f) output << byte;
        else output << "\\uFFFD";
    }
    return output.str();
}

bool is_fuji_make(const char *value) {
    constexpr char expected[] = "FUJIFILM";
    for (size_t index = 0; index < sizeof(expected) - 1; ++index) {
        char byte = value[index];
        if (byte >= 'a' && byte <= 'z') byte -= 'a' - 'A';
        if (byte != expected[index]) return false;
    }
    return true;
}

template<typename T>
void array(std::ostream &output, const T *values, size_t count) {
    output << '[';
    for (size_t index = 0; index < count; ++index) {
        if (index) output << ',';
        output << +values[index];
    }
    output << ']';
}

void write_facts(const std::filesystem::path &fixture,
                 const std::filesystem::path &output_path) {
    LibRaw raw;
    const int open_code = raw.open_file(fixture.c_str());
    if (open_code != LIBRAW_SUCCESS) throw std::runtime_error(libraw_strerror(open_code));
    const int unpack_code = raw.unpack();
    if (unpack_code != LIBRAW_SUCCESS) throw std::runtime_error(libraw_strerror(unpack_code));
    const auto &data = raw.imgdata;
    const auto &snapshot = data.rawdata;
    const auto &sizes = snapshot.sizes;
    const auto &identity = snapshot.iparams;
    const auto &color = snapshot.color;
    const auto block_rows = std::min<unsigned>(color.cblack[4], 64);
    const auto block_columns = std::min<unsigned>(color.cblack[5], 64);
    const auto cblack_values = std::min<size_t>(LIBRAW_CBLACK_SIZE,
        6 + static_cast<size_t>(block_rows) * block_columns);
    std::ofstream output(output_path, std::ios::binary);
    output << std::setprecision(9);
    output << "{\n  \"fixture\":\"" << fixture.filename().string() << "\",\n";
    output << "  \"oracle\":{\"isolated\":true,\"module\":\"raw_r.dll\","
        "\"sha256\":\"" << ExpectedHash << "\",\"version\":\"0.21.1\"},\n";
    output << "  \"extent\":" << static_cast<uint64_t>(sizes.raw_pitch) * sizes.raw_height << ",\n";
    output << "  \"raw_pitch\":" << sizes.raw_pitch << ",\n";
    output << "  \"dimensions\":{" << "\"raw_width\":" << sizes.raw_width
        << ",\"raw_height\":" << sizes.raw_height << ",\"width\":" << sizes.width
        << ",\"height\":" << sizes.height << ",\"top_margin\":" << sizes.top_margin
        << ",\"left_margin\":" << sizes.left_margin << "},\n";
    output << "  \"sensor\":{\"colors\":" << identity.colors << ",\"filters\":"
        << identity.filters << ",\"dng_version\":" << identity.dng_version
        << ",\"xtrans\":"; array(output, &identity.xtrans[0][0], 36);
    output << ",\"cdesc\":\"" << escape(identity.cdesc, sizeof(identity.cdesc)) << "\"},\n";
    output << "  \"black\":" << color.black << ",\"maximum\":" << color.maximum << ",\n";
    output << "  \"cblack\":{\"count\":" << LIBRAW_CBLACK_SIZE
        << ",\"block_rows\":" << block_rows << ",\"block_columns\":"
        << block_columns << ",\"values\":"; array(output, color.cblack, cblack_values);
    const size_t camera_columns = color.cam_mul[3] > 0 &&
        (color.rgb_cam[0][3] != 0 || color.rgb_cam[1][3] != 0 ||
         color.rgb_cam[2][3] != 0) ? 4 : 3;
    output << "},\n  \"camera\":{\"multiplier_count\":" << camera_columns
        << ",\"multipliers\":";
    array(output, color.cam_mul, camera_columns);
    output << ",\"matrix_rows\":3,\"matrix_columns\":" << camera_columns
        << ",\"matrix\":[";
    for (int row = 0; row < 3; ++row) {
        if (row) output << ',';
        array(output, color.rgb_cam[row], camera_columns);
    }
    const auto &fuji = data.makernotes.fuji;
    const bool is_fuji = is_fuji_make(data.idata.make);
    output << "]},\n  \"fuji\":{\"present\":" << (is_fuji ? "true" : "false")
        << ",\"exposure_midpoint_shift\":" << fuji.ExpoMidPointShift
        << ",\"dynamic_range\":" << fuji.DynamicRange
        << ",\"dynamic_range_setting\":" << fuji.DynamicRangeSetting
        << ",\"development_dynamic_range\":" << fuji.DevelopmentDynamicRange
        << ",\"auto_dynamic_range\":" << fuji.AutoDynamicRange << "},\n";
    output << "  \"service\":{\"focal_length_35mm\":"
        << data.lens.FocalLengthIn35mmFormat << ",\"gps_parsed\":"
        << +data.other.parsed_gps.gpsparsed << "}\n}\n";
}

} // namespace

int wmain(int argc, wchar_t **argv) {
    try {
        if (argc < 3) return 2;
        verify_runtime();
        const std::filesystem::path output_directory = argv[1];
        std::filesystem::create_directories(output_directory);
        for (int index = 2; index < argc; ++index) {
            const std::filesystem::path fixture = argv[index];
            write_facts(fixture, output_directory / (fixture.filename().string() + ".json"));
        }
        return 0;
    } catch (const std::exception &exception) {
        std::cerr << exception.what() << '\n';
        return 1;
    }
}
