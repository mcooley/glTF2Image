cmake_minimum_required(VERSION 3.19)
project(gltf2image_native)

if(MSVC)
    if (CMAKE_BUILD_TYPE STREQUAL "Debug")
        set(FILAMENT_ROOT ${CMAKE_CURRENT_LIST_DIR}/../filament/out/win-x64-debug/sdk)
    else()
        set(FILAMENT_ROOT ${CMAKE_CURRENT_LIST_DIR}/../filament/out/win-x64/sdk)
    endif()
else()
    if (CMAKE_BUILD_TYPE STREQUAL "Debug")
        set(FILAMENT_ROOT ${CMAKE_CURRENT_LIST_DIR}/../filament/out/linux-x64-debug/sdk)
    else()
        set(FILAMENT_ROOT ${CMAKE_CURRENT_LIST_DIR}/../filament/out/linux-x64/sdk)
    endif()
endif()

set(FILAMENT_LIBS_DIR ${FILAMENT_ROOT}/lib/x86_64/)
set(FILAMENT_INCLUDE_DIR ${FILAMENT_ROOT}/include)

set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_INTERPROCEDURAL_OPTIMIZATION TRUE)

if(MSVC)
    # By default, filament disables STL runtime checks in Debug. We need the same setting to link against it
    set(CMAKE_CXX_FLAGS_DEBUG "${CMAKE_CXX_FLAGS_DEBUG} /D_ITERATOR_DEBUG_LEVEL=0")
else()
    set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -fvisibility=hidden -stdlib=libc++")
endif()

add_library(gltf2image_renderer STATIC RenderManager.cpp)
set_target_properties(gltf2image_renderer PROPERTIES POSITION_INDEPENDENT_CODE ON)
target_include_directories(gltf2image_renderer PUBLIC ${FILAMENT_INCLUDE_DIR})

find_library(FILAMENT_BACKEND backend ${FILAMENT_LIBS_DIR})
find_library(FILAMENT_BLUEVK bluevk ${FILAMENT_LIBS_DIR})
find_library(FILAMENT_DRACODEC dracodec ${FILAMENT_LIBS_DIR})
find_library(FILAMENT_FILABRIDGE filabridge ${FILAMENT_LIBS_DIR})
find_library(FILAMENT_FILAFLAT filaflat ${FILAMENT_LIBS_DIR})
find_library(FILAMENT_FILAMENT filament ${FILAMENT_LIBS_DIR})
find_library(FILAMENT_GEOMETRY geometry ${FILAMENT_LIBS_DIR})
find_library(FILAMENT_GLTFIO_CORE gltfio_core ${FILAMENT_LIBS_DIR})
find_library(FILAMENT_IBL ibl ${FILAMENT_LIBS_DIR})
find_library(FILAMENT_MESHOPTIMIZER meshoptimizer ${FILAMENT_LIBS_DIR})
find_library(FILAMENT_SMOL_V smol-v ${FILAMENT_LIBS_DIR})
find_library(FILAMENT_STB stb ${FILAMENT_LIBS_DIR})
find_library(FILAMENT_UBERARCHIVE uberarchive ${FILAMENT_LIBS_DIR})
find_library(FILAMENT_UBERZLIB uberzlib ${FILAMENT_LIBS_DIR})
find_library(FILAMENT_UTILS utils ${FILAMENT_LIBS_DIR})
find_library(FILAMENT_ZSTD zstd ${FILAMENT_LIBS_DIR})

target_link_libraries(gltf2image_renderer PRIVATE
    ${FILAMENT_BACKEND} ${FILAMENT_BLUEVK} ${FILAMENT_DRACODEC}
    ${FILAMENT_FILABRIDGE} ${FILAMENT_FILAFLAT} ${FILAMENT_FILAMENT}
    ${FILAMENT_GEOMETRY} ${FILAMENT_GLTFIO_CORE} ${FILAMENT_IBL}
    ${FILAMENT_MESHOPTIMIZER} ${FILAMENT_SMOL_V} ${FILAMENT_STB}
    ${FILAMENT_UBERARCHIVE} ${FILAMENT_UBERZLIB} ${FILAMENT_UTILS} ${FILAMENT_ZSTD})

if(WIN32)
    target_link_libraries(gltf2image_renderer PRIVATE shlwapi opengl32)
endif()

add_library(gltf2image_native SHARED GLTFRenderAPI.cpp LogAPI.cpp)
target_link_libraries(gltf2image_native PRIVATE gltf2image_renderer)

if(NOT MSVC)
    target_link_options(gltf2image_native PRIVATE -static-libgcc -static-libstdc++)
    target_link_options(gltf2image_native PRIVATE "LINKER:--exclude-libs=ALL")
endif()

enable_testing()
add_executable(asset_teardown_test tests/AssetTeardown.cpp)
target_link_libraries(asset_teardown_test PRIVATE gltf2image_renderer)
add_test(NAME RenderedAssetTeardown COMMAND asset_teardown_test
    WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/../../managed/GLTF2Image.Tests/TestData")
set_tests_properties(RenderedAssetTeardown PROPERTIES TIMEOUT 60)
