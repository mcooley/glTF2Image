# On Ubuntu 22.04, make sure the following are installed:
# cmake
# clang
# libc++-14-dev
# libc++abi-14-dev
# ninja-build

# On Windows, run from "x64 Native Tools Command Prompt for VS 2022"

import glob
import os
import shutil
import subprocess
import sys

def apply_patch(patch_file):
    reverse_result = subprocess.run(['git', 'apply', '--ignore-whitespace', '--reverse', '--check', patch_file], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if reverse_result.returncode != 0:
        print('Applying patch ' + patch_file)
        subprocess.run(['git', 'apply', '--ignore-whitespace', patch_file], check=True)
    else:
        print('Looks like ' + patch_file + ' was already applied, not trying to apply it again')

def copy_matching_files(source_glob, target_dir):
    file_list = glob.glob(source_glob)
    for file_path in file_list:
        if os.path.isfile(file_path):
            shutil.copy(file_path, target_dir)

def ensure_directory_exists(directory_path):
    if not os.path.exists(directory_path):
        os.makedirs(directory_path)

def split_debug_symbols(binary_path):
    result = subprocess.run(['readelf', '-S', binary_path], capture_output=True, text=True)
    if '.debug_info' not in result.stdout:
        print(f"The file {binary_path} has already had debug symbols stripped.")
        return

    dbg_path = binary_path[:-3] + '.dbg'
    if os.path.exists(dbg_path):
        os.remove(dbg_path)
    subprocess.run(['objcopy', '--only-keep-debug', binary_path, dbg_path], check=True)
    subprocess.run(['objcopy', '--strip-debug', binary_path], check=True)
    subprocess.run(['objcopy', '--add-gnu-debuglink=' + dbg_path, binary_path], check=True)

if os.name == 'nt':
    build_os = 'win'
else:
    build_os = 'linux'

is_debug = False
if len(sys.argv) > 1 and sys.argv[1].lower() == 'debug':
    is_debug = True

if build_os == 'win':
    if is_debug:
        build_variant = 'win-x64-debug'
    else:
        build_variant = 'win-x64'
else:
    if is_debug:
        build_variant = 'linux-x64-debug'
    else:
        build_variant = 'linux-x64'

if is_debug:
    build_type = 'Debug'
else:
    build_type = 'RelWithDebInfo'

if build_os == 'linux':
    print('Using Clang...')
    os.environ["CC"] = '/usr/bin/clang'
    os.environ["CXX"] = '/usr/bin/clang++'

native_dir = os.path.dirname(os.path.realpath(__file__))
native_out_dir = native_dir + '/out/' + build_variant
ensure_directory_exists(native_out_dir)

print('Building SwiftShader...')
swiftshader_dir = native_dir + '/swiftshader'
os.chdir(swiftshader_dir)
apply_patch('../swiftshader_patches/0001-static-link-c-libraries.patch')
swiftshader_build_dir = swiftshader_dir + '/out/' + build_variant
ensure_directory_exists(swiftshader_build_dir)
os.chdir(swiftshader_build_dir)
subprocess.run(['cmake', '../..',
    '-GNinja',

    # We're always compiling SwiftShader in release mode, even in debug builds. We haven't needed to debug SwiftShader
    # yet. In debug, SwiftShader pops up a "wait for debugger" dialog (see DEBUGGER_WAIT_DIALOG) which disrupts
    # automated tests, and we'd need to patch that if we wanted to use Debug here.
    '-DCMAKE_BUILD_TYPE=RelWithDebInfo',

    '-DSWIFTSHADER_BUILD_WSI_XCB=FALSE',
    '-DSWIFTSHADER_BUILD_WSI_WAYLAND=FALSE',
    '-DSWIFTSHADER_BUILD_TESTS=FALSE',
    '-DREACTOR_BACKEND=Subzero'], check=True)
subprocess.run(['ninja'], check=True)
if build_os == 'linux' and not is_debug:
    split_debug_symbols('libvk_swiftshader.so')
copy_matching_files(swiftshader_build_dir + '/*vk_swiftshader.*', native_out_dir)

print('Building Filament...')
filament_dir = native_dir + '/filament'
os.chdir(filament_dir)
apply_patch('../filament_patches/0001-use-swiftshader-relative-path.patch')
filament_build_dir = filament_dir + '/out/' + build_variant
ensure_directory_exists(filament_build_dir)
os.chdir(filament_build_dir)
subprocess.run(['cmake', '../..',
    '-GNinja',
    f'-DCMAKE_BUILD_TYPE={build_type}',
    '-DFILAMENT_SUPPORTS_VULKAN=ON',
    '-DFILAMENT_SUPPORTS_OPENGL=OFF',
    '-DFILAMENT_SUPPORTS_METAL=OFF',
    '-DFILAMENT_SUPPORTS_XCB=OFF',
    '-DFILAMENT_SUPPORTS_XLIB=OFF',
    '-DFILAMENT_SKIP_SAMPLES=ON',
    '-DFILAMENT_SKIP_SDL2=ON',
    '-DFILAMENT_ENABLE_MATDBG=OFF',
    '-DUSE_STATIC_CRT=OFF'], check=True)
subprocess.run(['ninja'], check=True)

filament_sdk_dir = filament_build_dir + '/sdk'
print('Installing Filament to ' + filament_sdk_dir)
subprocess.run(['cmake', '--install', '.', '--prefix', filament_sdk_dir], check=True)

print('Building gltf2image-native...')
gltf2image_dir = native_dir + '/gltf2image'
os.chdir(gltf2image_dir)
gltf2image_build_dir = gltf2image_dir + '/out/' + build_variant
ensure_directory_exists(gltf2image_build_dir)
os.chdir(gltf2image_build_dir)
subprocess.run(['cmake', '../..',
    '-GNinja',
    f'-DCMAKE_BUILD_TYPE={build_type}'], check=True)
subprocess.run(['ninja'], check=True)
if build_os == 'linux' and not is_debug:
    split_debug_symbols('libgltf2image_native.so')
copy_matching_files(gltf2image_build_dir + '/*gltf2image_native.*', native_out_dir)
