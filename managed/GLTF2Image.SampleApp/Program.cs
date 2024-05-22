using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GLTF2Image.SampleApp
{
    internal class Program
    {
        private static string TestDataPath => Path.Join(Path.GetDirectoryName(typeof(Program).Assembly.Location), "TestData");

        static async Task Main(string[] args)
        {
            // Create a RenderManager instance.
            using var renderManager = await RenderManager.CreateAsync();

            // Load the model.
            using var model = renderManager.LoadGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "Avocado.glb")));
            
            // Load another gltf model defining the lights and camera.
            using var lightsAndCamera = renderManager.LoadGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "avocado_lights_and_camera.gltf")));

            // Render the scene.
            int width = 576;
            int height = 324;
            var data = await renderManager.RenderAsync(576, 324, new[] { model, lightsAndCamera });

            // We currently have a pixel buffer in RGBA format. Use your favorite library to encode this as a PNG.
            // For this example, we'll use ImageSharp.
            var image = Image.LoadPixelData<Rgba32>(data, width, height);
            await image.SaveAsPngAsync("avocado.png");
        }
    }
}
