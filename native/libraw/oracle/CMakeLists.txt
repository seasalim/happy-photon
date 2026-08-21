cmake_minimum_required(VERSION 3.24)
project(HappyPhotonLibRawOracle LANGUAGES CXX)

find_package(libraw CONFIG REQUIRED)

if(NOT WIN32)
  message(FATAL_ERROR "The ABI parity oracle is Windows-only")
endif()
add_executable(hplr_abi23_oracle oracle.cpp)
target_compile_features(hplr_abi23_oracle PRIVATE cxx_std_20)
target_link_libraries(hplr_abi23_oracle PRIVATE libraw::raw_r bcrypt)
