cmake_minimum_required(VERSION 3.25)

cmake_policy(SET CMP0091 NEW)

project(CreoToolkitNative LANGUAGES CXX)

if(NOT MSVC)
  message(FATAL_ERROR "CreoToolkit native targets require MSVC.")
endif()

set(CTK_EXPECTED_MSVC_VERSION "" CACHE STRING
  "Optional exact MSVC_VERSION required by the native build entry point")
if(CTK_EXPECTED_MSVC_VERSION AND
   NOT MSVC_VERSION EQUAL CTK_EXPECTED_MSVC_VERSION)
  message(FATAL_ERROR
    "Native compiler mismatch: expected MSVC_VERSION=${CTK_EXPECTED_MSVC_VERSION}, "
    "actual MSVC_VERSION=${MSVC_VERSION}.")
endif()
message(STATUS "Native MSVC_VERSION=${MSVC_VERSION}")

if(DEFINED ENV{CREO_ROOT} AND NOT CREO_ROOT)
  set(CREO_ROOT "$ENV{CREO_ROOT}" CACHE PATH "Creo Toolkit API root" FORCE)
else()
  set(CREO_ROOT "" CACHE PATH "Creo Toolkit API root")
endif()
if(NOT CREO_ROOT OR NOT EXISTS "${CREO_ROOT}")
  message(FATAL_ERROR
    "CREO_ROOT is not set or does not exist.\n"
    "  Pass -DCREO_ROOT=<path> or set the CREO_ROOT environment variable.\n"
    "  Example: cmake -DCREO_ROOT=\"C:/PTC/Creo 4.0/Common Files/protoolkit\" ..\n"
    "  Or use CMakeUserPresets.json — see CMakePresets.json for the template.")
endif()

set(CREO_INC "${CREO_ROOT}/protoolkit/includes" CACHE PATH "Creo Toolkit include directory")
set(CREO_APPLINC "${CREO_ROOT}/protoolkit/protk_appls/includes" CACHE PATH "Creo Toolkit application include directory")
set(CREO_LIBDIR "${CREO_ROOT}/protoolkit/x86e_win64/obj" CACHE PATH "Creo Toolkit x64 library directory")

set(CTK_ROOT "${CMAKE_CURRENT_SOURCE_DIR}")
set(CTK_NATIVE_DIR "${CTK_ROOT}/src/CreoToolkit.NativeAbi")
set(CTK_NATIVE_HOST_DIR "${CTK_ROOT}/src/CreoToolkit.NativeHost")
set(CTK_GATE_DIR "${CTK_ROOT}/tests/CreoToolkit.CreoGate")
set(CTK_NATIVE_TESTS_DIR "${CTK_ROOT}/tests/CreoToolkit.NativeAbi.Tests")

set(CTK_WINLIBS
  kernel32.lib
  user32.lib
  wsock32.lib
  advapi32.lib
  mpr.lib
  winspool.lib
  netapi32.lib
  psapi.lib
  gdi32.lib
  shell32.lib
  comdlg32.lib
  ole32.lib
  ws2_32.lib
)

if(NOT CMAKE_DUMPBIN)
  set(CMAKE_DUMPBIN dumpbin)
endif()

function(ctk_set_output_dirs target dir)
  foreach(config DEBUG RELEASE RELWITHDEBINFO MINSIZEREL)
    set_target_properties(${target} PROPERTIES
      RUNTIME_OUTPUT_DIRECTORY_${config} "${dir}"
      LIBRARY_OUTPUT_DIRECTORY_${config} "${dir}"
      ARCHIVE_OUTPUT_DIRECTORY_${config} "${dir}"
      PDB_OUTPUT_DIRECTORY_${config} "${dir}"
    )
  endforeach()
endfunction()

function(ctk_common_compile target warning_level)
  target_compile_options(${target} PRIVATE /utf-8 /${warning_level} /EHsc /GS /fp:precise)
  target_compile_definitions(${target} PRIVATE _CRT_SECURE_NO_WARNINGS)
endfunction()

function(ctk_dynamic_runtime target)
  set_property(TARGET ${target} PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreadedDLL")
endfunction()

function(ctk_debug_dynamic_runtime target)
  set_property(TARGET ${target} PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreadedDebugDLL")
endfunction()

function(ctk_static_runtime target)
  set_property(TARGET ${target} PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded")
endfunction()

function(ctk_creo_dll_link target toolkit_lib)
  target_link_options(${target} PRIVATE /NODEFAULTLIB:libcmd.lib)
  target_link_libraries(${target} PRIVATE
    "${CREO_LIBDIR}/${toolkit_lib}"
    "${CREO_LIBDIR}/ucore.lib"
    "${CREO_LIBDIR}/udata.lib"
    ${CTK_WINLIBS}
  )
endfunction()

# 测试 & 探针 target(非生产)在 tests/ 子目录,发布包不含 tests/。
# EXISTS 守卫: 公开发行版无 tests/ 时静默跳过,只 build NativeHost。
if(EXISTS "${CTK_NATIVE_TESTS_DIR}/CMakeLists.txt")
  add_subdirectory(tests/CreoToolkit.NativeAbi.Tests)
endif()

# JSONL is the bootstrap evidence source of truth. ETW is opt-in (restore the
# TraceLogging twin with -DCTK_ENABLE_ETW=ON).
option(CTK_ENABLE_ETW "Compile NativeHost ETW TraceLogging twin of JSONL" OFF)

set(_ctk_native_host_sources
  "${CTK_NATIVE_HOST_DIR}/src/host_entry.cpp"
  "${CTK_NATIVE_HOST_DIR}/src/clr_host_loader.cpp"
  "${CTK_NATIVE_HOST_DIR}/src/host_log_paths.cpp"
  "${CTK_NATIVE_HOST_DIR}/src/toolkit_touch.cpp"
  "${CTK_NATIVE_DIR}/src/ctk_buffer.cpp"
  "${CTK_NATIVE_DIR}/src/ctk_handle.cpp"
  "${CTK_NATIVE_DIR}/src/ctk_ops_misc.cpp"
  "${CTK_NATIVE_DIR}/src/ctk_app.cpp"
  "${CTK_NATIVE_DIR}/exports.def"   # Pro* 符号直通导出;L2 直绑通路(见方案 §6.2b)
)
if(CTK_ENABLE_ETW)
  list(APPEND _ctk_native_host_sources
    "${CTK_NATIVE_HOST_DIR}/src/ctk_etw_provider.cpp")
endif()
add_library(CreoToolkit.NativeHost SHARED ${_ctk_native_host_sources})
set(_CTK_NETFX_UM "C:/Program Files (x86)/Windows Kits/NETFXSDK/4.8/Include/um")
set(_CTK_NETFX_LIB "C:/Program Files (x86)/Windows Kits/NETFXSDK/4.8/Lib/um/x64")
target_include_directories(CreoToolkit.NativeHost PRIVATE
  "${CREO_INC}"
  "${CTK_NATIVE_DIR}/include"
  "${CTK_NATIVE_HOST_DIR}/src"
)
if(EXISTS "${_CTK_NETFX_UM}/metahost.h")
  target_include_directories(CreoToolkit.NativeHost PRIVATE "${_CTK_NETFX_UM}")
endif()
if(EXISTS "${_CTK_NETFX_LIB}/mscoree.lib")
  target_link_directories(CreoToolkit.NativeHost PRIVATE "${_CTK_NETFX_LIB}")
endif()
target_compile_definitions(CreoToolkit.NativeHost PRIVATE
  PRO_MACHINE=36
  PRO_OS=4
  CTK_OPS_NO_ENTRY_STUBS
  PRO_USE_VAR_ARGS
)
if(CTK_ENABLE_ETW)
  target_compile_definitions(CreoToolkit.NativeHost PRIVATE CTK_ENABLE_ETW)
endif()
ctk_common_compile(CreoToolkit.NativeHost W3)
ctk_dynamic_runtime(CreoToolkit.NativeHost)
ctk_creo_dll_link(CreoToolkit.NativeHost protk_dllmd_NU.lib)
target_link_libraries(CreoToolkit.NativeHost PRIVATE mscoree.lib)
ctk_set_output_dirs(CreoToolkit.NativeHost "${CTK_NATIVE_HOST_DIR}/out")

# CreoGate 门测试载体在 tests/ 子目录;发布包不含 tests/。
# EXISTS 守卫: 公开发行版自动跳过,不影响 NativeHost build。
if(EXISTS "${CTK_GATE_DIR}/sync/sync_entry.cpp")
  add_library(CreoGate.Sync SHARED
    "${CTK_GATE_DIR}/sync/sync_entry.cpp"
    "${CTK_GATE_DIR}/core/gate_core.cpp"
    "${CTK_GATE_DIR}/core/gate_ops.cpp"
    "${CTK_GATE_DIR}/creo/ctk_creo_producers.cpp"
    "${CTK_NATIVE_DIR}/src/ctk_buffer.cpp"
    "${CTK_NATIVE_DIR}/src/ctk_handle.cpp"
    "${CTK_NATIVE_DIR}/src/ctk_ops_misc.cpp"
  )
  target_include_directories(CreoGate.Sync PRIVATE
    "${CTK_GATE_DIR}/core"
    "${CTK_GATE_DIR}/creo"
    "${CTK_NATIVE_DIR}/include"
    "${CREO_INC}"
    "${CREO_APPLINC}"
  )
  target_compile_definitions(CreoGate.Sync PRIVATE
    PRO_MACHINE=36
    PRO_OS=4
    CTK_OPS_NO_ENTRY_STUBS
  )
  ctk_common_compile(CreoGate.Sync W3)
  ctk_dynamic_runtime(CreoGate.Sync)
  ctk_creo_dll_link(CreoGate.Sync protk_dllmd_NU.lib)
  target_link_options(CreoGate.Sync PRIVATE /SUBSYSTEM:CONSOLE)
  ctk_set_output_dirs(CreoGate.Sync "${CTK_GATE_DIR}")

  add_executable(CreoGate.Async
    "${CTK_GATE_DIR}/async/async_main.cpp"
    "${CTK_GATE_DIR}/core/gate_core.cpp"
    "${CTK_GATE_DIR}/creo/ctk_creo_producers.cpp"
    "${CTK_NATIVE_DIR}/src/ctk_buffer.cpp"
    "${CTK_NATIVE_DIR}/src/ctk_handle.cpp"
  )
  target_include_directories(CreoGate.Async PRIVATE
    "${CTK_GATE_DIR}/core"
    "${CTK_GATE_DIR}/creo"
    "${CTK_NATIVE_DIR}/include"
    "${CREO_INC}"
    "${CREO_APPLINC}"
  )
  target_compile_definitions(CreoGate.Async PRIVATE
    PRO_MACHINE=36
    PRO_OS=4
    _WSTDIO_DEFINED
  )
  ctk_common_compile(CreoGate.Async W3)
  ctk_static_runtime(CreoGate.Async)
  target_link_libraries(CreoGate.Async PRIVATE
    "${CREO_LIBDIR}/protoolkit_NU.lib"
    "${CREO_LIBDIR}/ucore.lib"
    "${CREO_LIBDIR}/udata.lib"
    libcmt.lib
    ${CTK_WINLIBS}
  )
  target_link_options(CreoGate.Async PRIVATE /SUBSYSTEM:CONSOLE)
  ctk_set_output_dirs(CreoGate.Async "${CTK_GATE_DIR}")

  add_custom_target(creo-gate
    DEPENDS CreoGate.Sync CreoGate.Async
  )
endif()

# native-all 聚合 target 只在 tests 子目标齐全时启用。
if(TARGET native-probes AND
   TARGET native-app-test AND
   TARGET native-host-log-paths-test AND
   TARGET native-host-log-v41-test AND
   TARGET creo-gate)
  add_custom_target(native-all
    DEPENDS
      native-probes
      native-app-test
      native-host-log-paths-test
      native-host-log-v41-test
      CreoToolkit.NativeHost
      creo-gate
  )
endif()
