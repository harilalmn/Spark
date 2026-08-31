# spark_occt — Spark's C ABI over OpenCascade (ADR-0020).
#
# Build it with scripts/build-native.ps1 rather than by hand; that script knows where vcpkg is and
# what to copy where. This file is deliberately plain: one shared library, one dependency, and a
# smoke test that is a C program rather than a C++ one so that the header's "no C++" claim is
# checked by a compiler and not by a reviewer.

cmake_minimum_required(VERSION 3.21)

project(spark_occt VERSION 0.1.0 LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "" FORCE)
endif()

find_package(OpenCASCADE CONFIG REQUIRED)

add_library(spark_occt SHARED
    src/spark_occt.cpp
    src/spark_occt_import.cpp
    src/spark_occt_read.cpp)

target_include_directories(spark_occt
    PUBLIC $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src ${OpenCASCADE_INCLUDE_DIR})

# The modules we actually reach into, and no more. The list is short because the ABI is small,
# which is D17's second reason for keeping the shim hand-written: this line is the thing that has
# to survive an OpenCascade upgrade.
target_link_libraries(spark_occt PRIVATE
    TKernel
    TKMath
    TKG2d
    TKG3d
    TKGeomBase
    TKGeomAlgo
    TKBRep
    TKTopAlgo
    TKPrim
    TKBO
    TKBool
    TKFillet
    TKOffset
    TKMesh
    TKShHealing)

target_compile_definitions(spark_occt PRIVATE SPARK_OCCT_BUILD=1)

if(MSVC)
    # /EHsc because OpenCascade throws, and this library's whole job is to stop it crossing the
    # ABI. /W4 without /WX: OpenCascade's own headers are not warning-clean and we do not get to
    # fix them, so warnings here are read rather than enforced.
    target_compile_options(spark_occt PRIVATE /EHsc /W4 /bigobj)
else()
    target_compile_options(spark_occt PRIVATE -Wall -Wextra)
endif()

set_target_properties(spark_occt PROPERTIES
    OUTPUT_NAME spark_occt
    PREFIX ""
    C_VISIBILITY_PRESET hidden)

# ------------------------------------------------------------------------------------------------
# The smoke test
#
# It is a C program. If this stops compiling, the header has grown C++ in it, which is the one
# thing ADR-0020's shim must never do — and a comment saying so would not have noticed.
# ------------------------------------------------------------------------------------------------

enable_testing()

add_executable(spark_occt_smoke test/smoke.c)
target_link_libraries(spark_occt_smoke PRIVATE spark_occt)
target_include_directories(spark_occt_smoke PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/include)

add_test(NAME smoke COMMAND spark_occt_smoke)
