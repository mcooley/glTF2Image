# On Ubuntu 24.04 (x64 or arm64), install these packages from the Ubuntu repositories:
# cmake
# clang-18, libc++-18-dev, libc++abi-18-dev
# ninja-build
# Run with CC=/usr/bin/clang-18 CXX=/usr/bin/clang++-18.

# On Windows, install the VS 2022 v143 14.44 build tools for the host architecture (x64/x86 or ARM64).
# In a fresh Command Prompt, run vcvars64.bat -vcvars_ver=14.44 (x64) or vcvarsarm64.bat -vcvars_ver=14.44
# (arm64) from the VS installation's VC\Auxiliary\Build directory, then run this script. cl should report
# 19.44 for the host architecture.

import glob
import os
import platform
import re
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

host_machine = platform.machine().lower()
if host_machine in ('x86_64', 'amd64'):
    build_arch = 'x64'
elif host_machine in ('aarch64', 'arm64'):
    build_arch = 'arm64'
else:
    sys.exit(f'Unsupported host architecture: {platform.machine()}')

if build_os == 'win':
    msvc_target_arch = os.environ.get('VSCMD_ARG_TGT_ARCH')
    if msvc_target_arch and msvc_target_arch.lower() != build_arch:
        sys.exit(f'The MSVC environment targets {msvc_target_arch}, but the host architecture is {build_arch}. '
                 f'Initialize the MSVC environment for {build_arch} before running this script.')

is_debug = False
if len(sys.argv) > 1 and sys.argv[1].lower() == 'debug':
    is_debug = True

build_variant = f'{build_os}-{build_arch}'
if is_debug:
    build_variant += '-debug'

# SwiftShader's Subzero JIT backend has no AArch64 target, so arm64 builds use the bundled LLVM backend instead.
if build_arch == 'x64':
    swiftshader_reactor_backend = 'Subzero'
else:
    swiftshader_reactor_backend = 'LLVM'

if is_debug:
    build_type = 'Debug'
else:
    build_type = 'RelWithDebInfo'

if build_os == 'linux':
    for variable, example in [('CC', '/usr/bin/clang-18'), ('CXX', '/usr/bin/clang++-18')]:
        compiler = os.environ.get(variable)
        if not compiler:
            sys.exit(f'Set {variable} to a Clang 18 compiler executable (for example, {example}).')
        try:
            version = subprocess.run([compiler, '--version'], check=True, capture_output=True, text=True)
        except (OSError, subprocess.CalledProcessError) as error:
            sys.exit(f'Could not verify {variable}={compiler!r}: {error}')
        if not re.search(r'\bclang version 18\.', version.stdout):
            sys.exit(f'{variable}={compiler!r} is not Clang 18 (expected, for example, {example}).')

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
    f'-DREACTOR_BACKEND={swiftshader_reactor_backend}'], check=True)
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
    '-DFILAMENT_BUILD_TESTING=OFF',
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
