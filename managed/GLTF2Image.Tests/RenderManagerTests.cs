using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace GLTF2Image.Tests
{
    public class RenderManagerTests
    {
        [Fact]
        public async Task RenderTestTriangle_PixelColorIsRed()
        {
            using var rm = new RenderManager();
            using var redTriangleUnlit = rm.LoadGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "red_triangle_unlit.gltf")));
            using var orthographicCamera = rm.LoadGLTFAsset(File.ReadAllBytes(Path.Join(TestDataPath, "orthographic_camera.gltf")));

            using var renderJob = rm.CreateJob(100, 100);
            renderJob.AddAsset(redTriangleUnlit);
            renderJob.AddAsset(orthographicCamera);
            var data = await rm.RenderJobAsync(renderJob);

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