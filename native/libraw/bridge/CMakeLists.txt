cmake_minimum_required(VERSION 3.24)
project(HappyPhotonLibRawBridge VERSION 1.0 LANGUAGES C CXX)

option(HPLR_BUILD_TESTS "Build bridge tests" ON)
option(HPLR_USE_REENTRANT "Link the reentrant LibRaw variant" ${WIN32})

find_package(libraw CONFIG REQUIRED)
set(HPLR_LIBRAW_TARGET libraw::raw)
if(HPLR_USE_REENTRANT)
  set(HPLR_LIBRAW_TARGET libraw::raw_r)
endif()

add_library(happyphoton_libraw_bridge SHARED
  src/bridge_core.cpp
  src/bridge_lifecycle.cpp
  src/bridge_facts.cpp
  src/bridge_images.cpp
  src/layout_asserts.cpp)
target_compile_features(happyphoton_libraw_bridge PRIVATE cxx_std_20)
target_compile_definitions(happyphoton_libraw_bridge PRIVATE HPLR_BUILDING)
if(HPLR_USE_REENTRANT)
  target_compile_definitions(happyphoton_libraw_bridge PRIVATE HPLR_LIBRAW_REENTRANT)
endif()
target_include_directories(happyphoton_libraw_bridge PUBLIC include PRIVATE src)
target_link_libraries(happyphoton_libraw_bridge PRIVATE ${HPLR_LIBRAW_TARGET})
set_target_properties(happyphoton_libraw_bridge PROPERTIES
  CXX_VISIBILITY_PRESET hidden C_VISIBILITY_PRESET hidden
  OUTPUT_NAME happyphoton_libraw_bridge)

if(HPLR_BUILD_TESTS)
  target_compile_definitions(happyphoton_libraw_bridge PRIVATE HPLR_TESTING)
  enable_testing()
  add_subdirectory(tests)
endif()
