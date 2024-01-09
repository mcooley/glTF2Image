# Required to build on Ubuntu 22.04:
# cmake
# clang
# libc++-14-dev
# libc++abi-14-dev
# ninja-build

import os
import subprocess

print('Building SwiftShader...')
swiftshader_build_dir = os.path.dirname(os.path.realpath(__file__)) + '/swiftshader/build'
os.chdir(swiftshader_build_dir)
subprocess.run(['git', 'apply', '../../swiftshader_no_download_commit_hook.patch'])
subprocess.run(['cmake', '..',
    '-GNinja',
    '-DCMAKE_BUILD_TYPE=Release',
    '-DSWIFTSHADER_BUILD_WSI_XCB=FALSE',
    '-DSWIFTSHADER_BUILD_WSI_WAYLAND=FALSE',
    '-DSWIFTSHADER_BUILD_TESTS=FALSE',
    '-DREACTOR_BACKEND=Subzero'])
subprocess.run(['ninja'])
os.environ["SWIFTSHADER_LD_LIBRARY_PATH"] = swiftshader_build_dir

print('Building Filament...')
filament_build_dir = os.path.dirname(os.path.realpath(__file__)) + '/filament/out'
if not os.path.exists(filament_build_dir):
    os.mkdirs(filament_build_dir)
os.chdir(filament_build_dir)
subprocess.run(['git', 'apply', '../../filament_use_swiftshader_relative_path.patch'])
subprocess.run(['cmake', '..',
    '-GNinja',
    '-DCMAKE_BUILD_TYPE=Release',
    '-DFILAMENT_SUPPORTS_OPENGL=OFF',
    '-DFILAMENT_SUPPORTS_METAL=OFF',
    '-DFILAMENT_SUPPORTS_XCB=OFF',
    '-DFILAMENT_SUPPORTS_XLIB=OFF',
    '-DFILAMENT_SKIP_SAMPLES=ON',
    '-DFILAMENT_SKIP_SDL2=ON',
    '-DFILAMENT_USE_SWIFTSHADER=ON'])
subprocess.run(['ninja'])

filament_sdk_dir = filament_build_dir + '/sdk'
print('Installing Filament to ' + filament_sdk_dir)
subprocess.run(['cmake', '--install', '.', '--prefix', filament_sdk_dir])
os.environ["FILAMENT_SDK_PATH"] = filament_sdk_dir