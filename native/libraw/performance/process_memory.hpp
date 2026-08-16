#pragma once

#include <cstdint>
#include <stdexcept>
#include <string_view>

#if defined(_WIN32)
#include <windows.h>
#include <psapi.h>
#elif defined(__APPLE__)
#include <mach/mach.h>
#include <sys/resource.h>
#else
#include <sys/resource.h>
#include <unistd.h>
#include <fstream>
#endif

namespace hplr::performance {

inline uint64_t current_process_bytes() {
#if defined(_WIN32)
    PROCESS_MEMORY_COUNTERS_EX counters{};
    counters.cb = sizeof(counters);
    if (!GetProcessMemoryInfo(GetCurrentProcess(),
            reinterpret_cast<PROCESS_MEMORY_COUNTERS *>(&counters), sizeof(counters)))
        throw std::runtime_error("GetProcessMemoryInfo failed");
    return counters.PrivateUsage;
#elif defined(__APPLE__)
    mach_task_basic_info_data_t info{};
    mach_msg_type_number_t count = MACH_TASK_BASIC_INFO_COUNT;
    if (task_info(mach_task_self(), MACH_TASK_BASIC_INFO,
            reinterpret_cast<task_info_t>(&info), &count) != KERN_SUCCESS)
        throw std::runtime_error("task_info failed");
    return info.resident_size;
#else
    std::ifstream statm("/proc/self/statm");
    uint64_t total_pages{}, resident_pages{};
    if (!(statm >> total_pages >> resident_pages))
        throw std::runtime_error("cannot read /proc/self/statm");
    return resident_pages * static_cast<uint64_t>(sysconf(_SC_PAGESIZE));
#endif
}

inline uint64_t peak_process_bytes() {
#if defined(_WIN32)
    PROCESS_MEMORY_COUNTERS counters{};
    counters.cb = sizeof(counters);
    if (!GetProcessMemoryInfo(GetCurrentProcess(), &counters, sizeof(counters)))
        throw std::runtime_error("GetProcessMemoryInfo failed");
    return counters.PeakPagefileUsage;
#else
    rusage usage{};
    if (getrusage(RUSAGE_SELF, &usage) != 0)
        throw std::runtime_error("getrusage failed");
#if defined(__APPLE__)
    return static_cast<uint64_t>(usage.ru_maxrss);
#else
    return static_cast<uint64_t>(usage.ru_maxrss) * 1024;
#endif
#endif
}

inline constexpr std::string_view memory_metric() {
#if defined(_WIN32)
    return "peak-private-commit";
#else
    return "peak-resident-set";
#endif
}

inline uint64_t checksum(const uint8_t *data, uint64_t length) {
    uint64_t value = UINT64_C(14695981039346656037);
    for (uint64_t index = 0; index < length; ++index) {
        value ^= data[index];
        value *= UINT64_C(1099511628211);
    }
    return value;
}

} // namespace hplr::performance
