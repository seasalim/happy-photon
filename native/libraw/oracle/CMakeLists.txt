cmake_minimum_required(VERSION 3.24)
project(HappyPhotonLibRawOracle LANGUAGES CXX)

option(HPLR_BUILD_PARITY_ORACLE "Build the Windows ABI parity extractor" ON)
option(HPLR_USE_REENTRANT "Link the reentrant LibRaw variant" ON)

find_package(libraw CONFIG REQUIRED)
set(HPLR_LIBRAW_TARGET libraw::raw)
if(HPLR_USE_REENTRANT)
  set(HPLR_LIBRAW_TARGET libraw::raw_r)
endif()

add_executable(hplr_baseline_performance ../performance/baseline_probe.cpp)
target_compile_features(hplr_baseline_performance PRIVATE cxx_std_20)
target_include_directories(hplr_baseline_performance PRIVATE ../performance)
target_link_libraries(hplr_baseline_performance PRIVATE ${HPLR_LIBRAW_TARGET})
if(WIN32)
  target_link_libraries(hplr_baseline_performance PRIVATE psapi)
endif()
set_target_properties(hplr_baseline_performance PROPERTIES
  BUILD_WITH_INSTALL_RPATH ON
  INSTALL_RPATH "$<$<PLATFORM_ID:Linux>:\$ORIGIN>$<$<PLATFORM_ID:Darwin>:@loader_path>")

if(HPLR_BUILD_PARITY_ORACLE)
  if(NOT WIN32)
    message(FATAL_ERROR "The ABI parity oracle is Windows-only")
  endif()
  add_executable(hplr_abi23_oracle oracle.cpp)
  target_compile_features(hplr_abi23_oracle PRIVATE cxx_std_20)
  target_link_libraries(hplr_abi23_oracle PRIVATE libraw::raw_r bcrypt)
endif()
