# glTF2Image
glTF2Image is a package for rendering screenshots or thumbnail images of 3D models. It provides an easy-to-use .NET API around [Filament](https://google.github.io/filament/) and [SwiftShader](https://swiftshader.googlesource.com/SwiftShader). It is suitable for web servers that don't have a GPU.

## Example
```csharp
// Create a RenderManager instance.
using var renderManager = new RenderManager();

// Load the model.
using var model = renderManager.LoadGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "Avocado.glb")));

// Load another gltf model defining the lights and camera.
using var lightsAndCamera = renderManager.LoadGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "avocado_lights_and_camera.gltf")));

// Render the scene.
int width = 576;
int height = 324;

using var renderJob = renderManager.CreateJob((uint)width, (uint)height);
renderJob.AddAsset(model);
renderJob.AddAsset(lightsAndCamera);
var data = await renderManager.RenderJobAsync(renderJob);

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
- .NET 6 or later
- Windows or Linux
- x64
