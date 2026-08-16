add_executable(hplr_native_tests native_tests.cpp)
target_compile_features(hplr_native_tests PRIVATE cxx_std_20)
target_include_directories(hplr_native_tests PRIVATE ../include .)
target_link_libraries(hplr_native_tests PRIVATE happyphoton_libraw_bridge)
target_compile_definitions(hplr_native_tests PRIVATE
  HPLR_EXPECT_REENTRANT=$<BOOL:${HPLR_USE_REENTRANT}>)

add_executable(hplr_c_header_smoke c_header_smoke.c)
target_include_directories(hplr_c_header_smoke PRIVATE ../include)
target_link_libraries(hplr_c_header_smoke PRIVATE happyphoton_libraw_bridge)

add_test(NAME hplr_c_header_smoke COMMAND hplr_c_header_smoke)
add_test(NAME hplr_native_tests COMMAND hplr_native_tests
  "${CMAKE_SOURCE_DIR}/../../../Tests/assets/canon-eos-350d.cr2")

foreach(test_name hplr_c_header_smoke hplr_native_tests)
  set_property(TEST ${test_name} PROPERTY ENVIRONMENT_MODIFICATION
    "PATH=path_list_prepend:$<TARGET_FILE_DIR:happyphoton_libraw_bridge>"
    "PATH=path_list_prepend:$<TARGET_FILE_DIR:${HPLR_LIBRAW_TARGET}>")
endforeach()
