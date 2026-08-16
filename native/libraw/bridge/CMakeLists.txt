cmake_minimum_required(VERSION 3.24)
project(HappyPhotonLibRawBridge VERSION 1.0 LANGUAGES C CXX)

option(HPLR_BUILD_TESTS "Build bridge tests" ON)
option(HPLR_USE_REENTRANT "Link the reentrant LibRaw variant" ${WIN32})
option(HPLR_EXPECT_LCMS "Require LCMS functional validation" OFF)
option(HPLR_EXPECT_OPENMP "Require OpenMP validation" OFF)

find_package(libraw CONFIG REQUIRED)
set(HPLR_LIBRAW_TARGET libraw::raw)
if(HPLR_USE_REENTRANT)
  set(HPLR_LIBRAW_TARGET libraw::raw_r)
endif()

set(HPLR_BRIDGE_SOURCES
  src/bridge_core.cpp
  src/bridge_lifecycle.cpp
  src/bridge_facts.cpp
  src/bridge_images.cpp
  src/layout_asserts.cpp)

function(hplr_configure_bridge target output_name)
  target_compile_features(${target} PRIVATE cxx_std_20)
  target_compile_definitions(${target} PRIVATE HPLR_BUILDING)
  if(HPLR_USE_REENTRANT)
    target_compile_definitions(${target} PRIVATE HPLR_LIBRAW_REENTRANT)
  endif()
  target_include_directories(${target} PUBLIC include PRIVATE src)
  target_link_libraries(${target} PRIVATE ${HPLR_LIBRAW_TARGET})
  set_target_properties(${target} PROPERTIES
    CXX_VISIBILITY_PRESET hidden C_VISIBILITY_PRESET hidden
    OUTPUT_NAME ${output_name}
    BUILD_RPATH_USE_ORIGIN ON
    INSTALL_RPATH "$<$<PLATFORM_ID:Linux>:\$ORIGIN>$<$<PLATFORM_ID:Darwin>:@loader_path>"
    BUILD_WITH_INSTALL_RPATH ON
    INSTALL_NAME_DIR "@loader_path")
endfunction()

add_library(happyphoton_libraw_bridge SHARED ${HPLR_BRIDGE_SOURCES})
hplr_configure_bridge(happyphoton_libraw_bridge happyphoton_libraw_bridge)

if(HPLR_BUILD_TESTS)
  add_library(happyphoton_libraw_bridge_test SHARED ${HPLR_BRIDGE_SOURCES})
  hplr_configure_bridge(happyphoton_libraw_bridge_test happyphoton_libraw_bridge_test)
  target_compile_definitions(happyphoton_libraw_bridge_test PRIVATE HPLR_TESTING)
  enable_testing()
  add_subdirectory(tests)
endif()
