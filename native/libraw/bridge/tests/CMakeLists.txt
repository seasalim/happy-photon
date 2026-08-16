add_executable(hplr_native_tests native_tests.cpp)
target_compile_features(hplr_native_tests PRIVATE cxx_std_20)
target_include_directories(hplr_native_tests PRIVATE ../include .)
target_link_libraries(hplr_native_tests PRIVATE happyphoton_libraw_bridge_test)
target_compile_definitions(hplr_native_tests PRIVATE
  HPLR_EXPECT_REENTRANT=$<BOOL:${HPLR_USE_REENTRANT}>)

add_executable(hplr_c_header_smoke c_header_smoke.c)
target_include_directories(hplr_c_header_smoke PRIVATE ../include)
target_link_libraries(hplr_c_header_smoke PRIVATE happyphoton_libraw_bridge)

add_executable(hplr_candidate_smoke candidate_smoke.cpp)
target_compile_features(hplr_candidate_smoke PRIVATE cxx_std_20)
target_include_directories(hplr_candidate_smoke PRIVATE ../include)
target_link_libraries(hplr_candidate_smoke PRIVATE happyphoton_libraw_bridge)
target_compile_definitions(hplr_candidate_smoke PRIVATE
  HPLR_EXPECT_REENTRANT=$<BOOL:${HPLR_USE_REENTRANT}>)
set_target_properties(hplr_candidate_smoke PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
  BUILD_WITH_INSTALL_RPATH ON
  INSTALL_RPATH "$<$<PLATFORM_ID:Linux>:\$ORIGIN>$<$<PLATFORM_ID:Darwin>:@loader_path>")

add_executable(hplr_feature_probe feature_probe.cpp)
target_compile_features(hplr_feature_probe PRIVATE cxx_std_20)
target_link_libraries(hplr_feature_probe PRIVATE ${HPLR_LIBRAW_TARGET})
target_compile_definitions(hplr_feature_probe PRIVATE
  HPLR_EXPECT_LCMS=$<BOOL:${HPLR_EXPECT_LCMS}>
  HPLR_EXPECT_OPENMP=$<BOOL:${HPLR_EXPECT_OPENMP}>)
set_target_properties(hplr_feature_probe PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
  BUILD_WITH_INSTALL_RPATH ON
  INSTALL_RPATH "$<$<PLATFORM_ID:Linux>:\$ORIGIN>$<$<PLATFORM_ID:Darwin>:@loader_path>")
if(HPLR_EXPECT_LCMS)
  find_package(lcms2 CONFIG REQUIRED)
  target_link_libraries(hplr_feature_probe PRIVATE lcms2::lcms2)
endif()

add_executable(hplr_candidate_performance ../../performance/candidate_probe.cpp)
target_compile_features(hplr_candidate_performance PRIVATE cxx_std_20)
target_include_directories(hplr_candidate_performance PRIVATE
  ../include ../../performance)
target_link_libraries(hplr_candidate_performance PRIVATE happyphoton_libraw_bridge)
if(WIN32)
  target_link_libraries(hplr_candidate_performance PRIVATE psapi)
endif()
set_target_properties(hplr_candidate_performance PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
  BUILD_WITH_INSTALL_RPATH ON
  INSTALL_RPATH "$<$<PLATFORM_ID:Linux>:\$ORIGIN>$<$<PLATFORM_ID:Darwin>:@loader_path>")

add_test(NAME hplr_c_header_smoke COMMAND hplr_c_header_smoke)
add_test(NAME hplr_native_tests COMMAND hplr_native_tests
  "${CMAKE_SOURCE_DIR}/../../../Tests/assets/canon-eos-350d.cr2")
add_test(NAME hplr_candidate_smoke COMMAND hplr_candidate_smoke
  --build-libraw-dir "$<TARGET_FILE_DIR:${HPLR_LIBRAW_TARGET}>"
  "${CMAKE_SOURCE_DIR}/../../../Tests/assets/canon-eos-350d.cr2")
add_test(NAME hplr_feature_probe COMMAND hplr_feature_probe
  "${CMAKE_SOURCE_DIR}/../../../Tests/assets/canon-eos-350d.cr2")

foreach(test_name hplr_c_header_smoke hplr_candidate_smoke)
  set_property(TEST ${test_name} PROPERTY ENVIRONMENT_MODIFICATION
    "PATH=path_list_prepend:$<TARGET_FILE_DIR:happyphoton_libraw_bridge>"
    "PATH=path_list_prepend:$<TARGET_FILE_DIR:${HPLR_LIBRAW_TARGET}>"
    "LD_LIBRARY_PATH=path_list_prepend:$<TARGET_FILE_DIR:happyphoton_libraw_bridge>"
    "LD_LIBRARY_PATH=path_list_prepend:$<TARGET_FILE_DIR:${HPLR_LIBRAW_TARGET}>"
    "DYLD_LIBRARY_PATH=path_list_prepend:$<TARGET_FILE_DIR:happyphoton_libraw_bridge>"
    "DYLD_LIBRARY_PATH=path_list_prepend:$<TARGET_FILE_DIR:${HPLR_LIBRAW_TARGET}>")
endforeach()

set_property(TEST hplr_native_tests PROPERTY ENVIRONMENT_MODIFICATION
  "PATH=path_list_prepend:$<TARGET_FILE_DIR:happyphoton_libraw_bridge_test>"
  "PATH=path_list_prepend:$<TARGET_FILE_DIR:${HPLR_LIBRAW_TARGET}>"
  "LD_LIBRARY_PATH=path_list_prepend:$<TARGET_FILE_DIR:happyphoton_libraw_bridge_test>"
  "LD_LIBRARY_PATH=path_list_prepend:$<TARGET_FILE_DIR:${HPLR_LIBRAW_TARGET}>"
  "DYLD_LIBRARY_PATH=path_list_prepend:$<TARGET_FILE_DIR:happyphoton_libraw_bridge_test>"
  "DYLD_LIBRARY_PATH=path_list_prepend:$<TARGET_FILE_DIR:${HPLR_LIBRAW_TARGET}>")
set_property(TEST hplr_feature_probe PROPERTY ENVIRONMENT_MODIFICATION
  "PATH=path_list_prepend:$<TARGET_FILE_DIR:${HPLR_LIBRAW_TARGET}>"
  "LD_LIBRARY_PATH=path_list_prepend:$<TARGET_FILE_DIR:${HPLR_LIBRAW_TARGET}>"
  "DYLD_LIBRARY_PATH=path_list_prepend:$<TARGET_FILE_DIR:${HPLR_LIBRAW_TARGET}>")
