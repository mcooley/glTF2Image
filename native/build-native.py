# On Ubuntu 22.04, make sure the following are installed:
# cmake
# clang
# libc++-14-dev
# libc++abi-14-dev
# ninja-build

# On Windows, run from "x64 Native Tools Command Prompt for VS 2022"

import os
import subprocess

def run(command):
    result = subprocess.run(command)
    if result.returncode != 0:
        raise Exception('Command exited with nonzero status')

def apply_patch(patch_file):
    reverse_result = subprocess.run(['git', 'apply', '--ignore-whitespace', '--reverse', '--check', patch_file], stdout = subprocess.DEVNULL, stderr = subprocess.DEVNULL)
    if reverse_result.returncode != 0:
        print('Applying patch ' + patch_file)
        run(['git', 'apply', '--ignore-whitespace', patch_file])
    else:
        print('Looks like ' + patch_file + ' was already applied, not trying to apply it again')

if os.name == 'nt':
    build_variant = 'win-x64'
else:
    build_variant = 'linux-x64'

if build_variant == 'linux-x64':
    print('Using Clang...')
    os.environ["CC"] = '/usr/bin/clang'
    os.environ["CXX"] = '/usr/bin/clang++'

print('Building SwiftShader...')
swiftshader_dir = os.path.dirname(os.path.realpath(__file__)) + '/swiftshader'
os.chdir(swiftshader_dir)
apply_patch('../swiftshader_patches/0001-no-download-commit-hook.patch')
apply_patch('../swiftshader_patches/0002-static-link-c-libraries.patch')
swiftshader_build_dir = swiftshader_dir + '/out/' + build_variant
if not os.path.exists(swiftshader_build_dir):
    os.makedirs(swiftshader_build_dir)
os.chdir(swiftshader_build_dir)
run(['cmake', '../..',
    '-GNinja',
    '-DCMAKE_BUILD_TYPE=Release',
    '-DSWIFTSHADER_BUILD_WSI_XCB=FALSE',
    '-DSWIFTSHADER_BUILD_WSI_WAYLAND=FALSE',
    '-DSWIFTSHADER_BUILD_TESTS=FALSE',
    '-DREACTOR_BACKEND=Subzero'])
run(['ninja'])
os.environ["SWIFTSHADER_LD_LIBRARY_PATH"] = swiftshader_build_dir

print('Building Filament...')
filament_dir = os.path.dirname(os.path.realpath(__file__)) + '/filament'
os.chdir(filament_dir)
apply_patch('../filament_patches/0001-use-swiftshader-relative-path.patch')
filament_build_dir = filament_dir + '/out/' + build_variant
if not os.path.exists(filament_build_dir):
    os.makedirs(filament_build_dir)
os.chdir(filament_build_dir)
run(['cmake', '../..',
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
run(['ninja'])

filament_sdk_dir = filament_build_dir + '/sdk'
print('Installing Filament to ' + filament_sdk_dir)
run(['cmake', '--install', '.', '--prefix', filament_sdk_dir])

print('Building gltf2image-native...')
gltf2image_dir = os.path.dirname(os.path.realpath(__file__)) + '/gltf2image'
os.chdir(gltf2image_dir)
gltf2image_build_dir = gltf2image_dir + '/out/' + build_variant
if not os.path.exists(gltf2image_build_dir):
    os.makedirs(gltf2image_build_dir)
os.chdir(gltf2image_build_dir)
run(['cmake', '../..',
    '-GNinja',
    '-DCMAKE_BUILD_TYPE=Release'])
run(['ninja'])