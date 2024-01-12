# On Ubuntu 22.04, make sure the following are installed:
# cmake
# clang
# libc++-14-dev
# libc++abi-14-dev
# ninja-build

# On Windows, run from "x64 Native Tools Command Prompt for VS 2022"

import os
import subprocess

if os.name == 'nt':
    build_variant = 'win-x64'
else:
    build_variant = 'linux-x64'

print('Building SwiftShader...')
swiftshader_dir = os.path.dirname(os.path.realpath(__file__)) + '/swiftshader'
os.chdir(swiftshader_dir)
subprocess.run(['git', 'apply', '../swiftshader_no_download_commit_hook.patch'])
swiftshader_build_dir = swiftshader_dir + '/out/' + build_variant
if not os.path.exists(swiftshader_build_dir):
    os.makedirs(swiftshader_build_dir)
os.chdir(swiftshader_build_dir)
subprocess.run(['cmake', '../..',
    '-GNinja',
    '-DCMAKE_BUILD_TYPE=Release',
    '-DSWIFTSHADER_BUILD_WSI_XCB=FALSE',
    '-DSWIFTSHADER_BUILD_WSI_WAYLAND=FALSE',
    '-DSWIFTSHADER_BUILD_TESTS=FALSE',
    '-DREACTOR_BACKEND=Subzero'])
subprocess.run(['ninja'])
os.environ["SWIFTSHADER_LD_LIBRARY_PATH"] = swiftshader_build_dir

print('Building Filament...')
filament_dir = os.path.dirname(os.path.realpath(__file__)) + '/filament'
os.chdir(filament_dir)
subprocess.run(['git', 'apply', '../filament_use_swiftshader_relative_path.patch'])
filament_build_dir = filament_dir + '/out/' + build_variant
if not os.path.exists(filament_build_dir):
    os.makedirs(filament_build_dir)
os.chdir(filament_build_dir)
subprocess.run(['cmake', '../..',
    '-GNinja',
    '-DCMAKE_BUILD_TYPE=Release',
    '-DFILAMENT_SUPPORTS_VULKAN=ON',
    '-DFILAMENT_SUPPORTS_OPENGL=OFF',
    '-DFILAMENT_SUPPORTS_METAL=OFF',
    '-DFILAMENT_SUPPORTS_XCB=OFF',
    '-DFILAMENT_SUPPORTS_XLIB=OFF',
    '-DFILAMENT_SKIP_SAMPLES=ON',
    '-DFILAMENT_SKIP_SDL2=ON',
    '-DUSE_STATIC_CRT=OFF'])
subprocess.run(['ninja'])

filament_sdk_dir = filament_build_dir + '/sdk'
print('Installing Filament to ' + filament_sdk_dir)
subprocess.run(['cmake', '--install', '.', '--prefix', filament_sdk_dir])

print('Building gltf2image-native...')
gltf2image_dir = os.path.dirname(os.path.realpath(__file__)) + '/gltf2image'
os.chdir(gltf2image_dir)
gltf2image_build_dir = gltf2image_dir + '/out/' + build_variant
if not os.path.exists(gltf2image_build_dir):
    os.makedirs(gltf2image_build_dir)
os.chdir(gltf2image_build_dir)
subprocess.run(['cmake', '../..',
    '-GNinja',
    '-DCMAKE_BUILD_TYPE=Release'])
subprocess.run(['ninja'])