using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace GLTF2Image.Tests
{
    public class RenderManagerTests
    {
        [Fact]
        public async Task LoadGLTFAsset_InvalidData_ThrowsException()
        {
            using var rm = await RenderManager.CreateAsync();

            Assert.Throws<InvalidSceneException>(() => rm.LoadGLTFAsset(Array.Empty<byte>()));
            Assert.Throws<InvalidSceneException>(() => rm.LoadGLTFAsset(Encoding.UTF8.GetBytes(@"not json")));

            // TODO enable this once https://github.com/google/filament/issues/7868 is fixed
            // Assert.Throws<InvalidSceneException>(() => rm.LoadGLTFAsset(Encoding.UTF8.GetBytes(@"{ ""invalid"": ""format"" }")));
        }

        [Fact]
        public async Task RenderAsync_NoCamerasFound_ThrowsException()
        {
            using var rm = await RenderManager.CreateAsync();
            using var redTriangleUnlit = rm.LoadGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "red_triangle_unlit.gltf")));

            await Assert.ThrowsAsync<InvalidSceneException>(async () => await rm.RenderAsync(100, 100, new[] { redTriangleUnlit }));
        }

        [Fact]
        public async Task RenderAsync_MultipleCamerasFound_ThrowsException()
        {
            using var rm = await RenderManager.CreateAsync();
            using var redTriangleUnlit = rm.LoadGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "red_triangle_unlit.gltf")));
            using var orthographicCamera1 = rm.LoadGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "orthographic_camera.gltf")));
            using var orthographicCamera2 = rm.LoadGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "orthographic_camera.gltf")));

            await Assert.ThrowsAsync<InvalidSceneException>(async () => await rm.RenderAsync(100, 100, new[] { redTriangleUnlit, orthographicCamera1, orthographicCamera2 }));
        }

        [Fact]
        public async Task GLTFAsset_DisposeAssetBeforeRenderFinishes_Succeeds()
        {
            using var rm = await RenderManager.CreateAsync();
            var redTriangleUnlit = rm.LoadGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "red_triangle_unlit.gltf")));
            var orthographicCamera = rm.LoadGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "orthographic_camera.gltf")));

            var dataTask = rm.RenderAsync(100, 100, new[] { redTriangleUnlit, orthographicCamera });

            redTriangleUnlit.Dispose();
            orthographicCamera.Dispose();

            var data = await dataTask;
            var inTrianglePixel = GetPixelColor(data, 100, 100, 60, 40);
            Assert.Equal(255, inTrianglePixel.A);
        }

        [Fact]
        public async Task RenderAsync_1000Times_Succeeds()
        {
            using var rm = await RenderManager.CreateAsync();
            var orthographicCamera = rm.LoadGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "orthographic_camera.gltf")));

            var tasks = new List<Task<byte[]>>();
            for (int i = 0; i < 1000; i++)
            {
                using var redTriangleUnlit = rm.LoadGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "red_triangle_unlit.gltf")));

                tasks.Add(rm.RenderAsync(100, 100, new[] { orthographicCamera, redTriangleUnlit }));
            }

            await Task.WhenAll(tasks);
        }

        [Fact]
        public async Task RenderAsync_TestTriangle_PixelColorIsRed()
        {
            using var rm = await RenderManager.CreateAsync();
            using var redTriangleUnlit = rm.LoadGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "red_triangle_unlit.gltf")));
            using var orthographicCamera = rm.LoadGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "orthographic_camera.gltf")));

            var data = await rm.RenderAsync(100, 100, new[] { redTriangleUnlit, orthographicCamera });

            var inTrianglePixel = GetPixelColor(data, 100, 100, 60, 40);
            var outOfTrianglePixel = GetPixelColor(data, 100, 100, 25, 75);

            Assert.Equal(255, inTrianglePixel.A);
            Assert.True(inTrianglePixel.R > 240);
            Assert.True(inTrianglePixel.G < 30);
            Assert.True(inTrianglePixel.B < 30);

            Assert.Equal(0, outOfTrianglePixel.A);
        }

        private static string TestDataPath => Path.Join(Path.GetDirectoryName(typeof(RenderManagerTests).Assembly.Location), "TestData");

        private static Color GetPixelColor(byte[] rgbaPixelData, int width, int height, int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                throw new ArgumentOutOfRangeException("x or y is outside the image bounds.");
            }

            int index = ((y * width) + x) * 4;

            byte r = rgbaPixelData[index];
            byte g = rgbaPixelData[index + 1];
            byte b = rgbaPixelData[index + 2];
            byte a = rgbaPixelData[index + 3];

            return Color.FromArgb(a, r, g, b);
        }
    }
}