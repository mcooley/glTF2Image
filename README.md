# glTF2Image
glTF2Image is a package for rendering screenshots or thumbnail images of 3D models. It provides an easy-to-use .NET API around [Filament](https://google.github.io/filament/) and [SwiftShader](https://swiftshader.googlesource.com/SwiftShader). It is suitable for web servers that don't have a GPU.

## Example
```csharp
// Create a Renderer instance.
await using var renderer = await Renderer.CreateAsync();

// Load the model.
await using var model = renderer.CreateGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "Avocado.glb")));

// Load another gltf model defining the lights and camera.
await using var lightsAndCamera = renderer.CreateGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "avocado_lights_and_camera.gltf")));

// Render the scene.
int width = 576;
int height = 324;
var data = await renderer.RenderAsync(576, 324, new[] { model, lightsAndCamera });

// We currently have a pixel buffer in RGBA format. Use your favorite library to encode this as a PNG.
// For this example, we'll use ImageSharp.
var image = Image.LoadPixelData<Rgba32>(data, width, height);
await image.SaveAsPngAsync("avocado.png");
```

Result:

![Rendered image of an avocado](docs/images/avocado.png)

## Preparing your model
glTF2Image accepts models in glTF or glb (glTF binary) format.

Your scene must have exactly one camera to tell glTF2Image what to render. If your model does not define a camera, you can add a separate glTF file with a camera, like the example above.

To validate your model, download a [recent Filament release](https://github.com/google/filament/releases) and use the gltf_viewer tool to ensure that Filament renders your model as expected. Be sure to uncheck the "Scene > Scale to unit cube" option, since glTF2Image will not scale your model.

Filament has good support for many glTF features and extensions. There are a small number of limitations:
- Only one directional light is [supported](https://github.com/google/filament/blob/c93aa4c90df7a814a076ebc8d92cc94d4fa96910/filament/include/filament/LightManager.h#L89)

## Requirements
- .NET 8 or later
- Windows or Linux
- x64

## Building from source on Windows
Windows CI uses Visual Studio 2022 with the v143 14.44 x64/x86 build tools (MSVC 19.44). Install that toolset with Visual Studio Installer and make sure Python, CMake, and Ninja are available.

From a fresh Command Prompt in the repository root, select the x64 toolset before building. Adjust the Visual Studio installation path for your edition:

```bat
call "%ProgramFiles%\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" -vcvars_ver=14.44
cl
git submodule update --init
python native\build-native.py release
```

`cl` should report `19.44` for x64. Selecting `14.44` rather than a full version allows servicing updates.
