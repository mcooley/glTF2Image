using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace GLTF2Image.Tests
{
    public class RendererTests : IDisposable
    {
        public RendererTests()
        {
            Renderer.Logger = NullLogger.Instance;
        }

        public void Dispose()
        {
            Renderer.Logger = null;
        }

        [Fact]
        public async Task LoadGLTFAsset_InvalidData_ThrowsException()
        {
            using var renderer = await Renderer.CreateAsync();

            await Assert.ThrowsAsync<InvalidSceneException>(async () => await renderer.LoadGLTFAssetAsync(Array.Empty<byte>()));
            await Assert.ThrowsAsync<InvalidSceneException>(async () => await renderer.LoadGLTFAssetAsync(Encoding.UTF8.GetBytes(@"not json")));
            await Assert.ThrowsAsync<InvalidSceneException>(async () => await renderer.LoadGLTFAssetAsync(Encoding.UTF8.GetBytes(@"{ ""invalid"": ""format"" }")));
        }

        [Fact]
        public async Task RenderAsync_NoCamerasFound_ThrowsException()
        {
            using var renderer = await Renderer.CreateAsync();
            using var redTriangleUnlit = await renderer.LoadGLTFAssetAsync(File.ReadAllBytes(Path.Join(TestDataPath, "red_triangle_unlit.gltf")));

            await Assert.ThrowsAsync<InvalidSceneException>(async () => await renderer.RenderAsync(100, 100, new[] { redTriangleUnlit }));
        }

        [Fact]
        public async Task RenderAsync_MultipleCamerasFound_ThrowsException()
        {
            using var rm = await Renderer.CreateAsync();
            using var redTriangleUnlit = await rm.LoadGLTFAssetAsync(File.ReadAllBytes(Path.Join(TestDataPath, "red_triangle_unlit.gltf")));
            using var orthographicCamera1 = await rm.LoadGLTFAssetAsync(File.ReadAllBytes(Path.Join(TestDataPath, "orthographic_camera.gltf")));
            using var orthographicCamera2 = await rm.LoadGLTFAssetAsync(File.ReadAllBytes(Path.Join(TestDataPath, "orthographic_camera.gltf")));

            await Assert.ThrowsAsync<InvalidSceneException>(async () => await rm.RenderAsync(100, 100, new[] { redTriangleUnlit, orthographicCamera1, orthographicCamera2 }));
        }

        [Fact]
        public async Task GLTFAsset_DisposeAssetBeforeRenderFinishes_Succeeds()
        {
            using var renderer = await Renderer.CreateAsync();
            var redTriangleUnlit = await renderer.LoadGLTFAssetAsync(File.ReadAllBytes(Path.Join(TestDataPath, "red_triangle_unlit.gltf")));
            var orthographicCamera = await renderer.LoadGLTFAssetAsync(File.ReadAllBytes(Path.Join(TestDataPath, "orthographic_camera.gltf")));

            var dataTask = renderer.RenderAsync(100, 100, new[] { redTriangleUnlit, orthographicCamera });

            redTriangleUnlit.Dispose();
            orthographicCamera.Dispose();

            var data = await dataTask;
            var inTrianglePixel = GetPixelColor(data.Span, 100, 100, 60, 40);
            Assert.Equal(255, inTrianglePixel.A);
        }

        [Fact]
        public async Task RenderAsync_1000Times_Succeeds()
        {
            using var renderer = await Renderer.CreateAsync();
            var orthographicCamera = await renderer.LoadGLTFAssetAsync(File.ReadAllBytes(Path.Join(TestDataPath, "orthographic_camera.gltf")));

            var tasks = new List<Task<Memory<byte>>>();
            for (int i = 0; i < 1000; i++)
            {
                using var redTriangleUnlit = await renderer.LoadGLTFAssetAsync(File.ReadAllBytes(Path.Join(TestDataPath, "red_triangle_unlit.gltf")));

                tasks.Add(renderer.RenderAsync(100, 100, new[] { orthographicCamera, redTriangleUnlit }));
            }

            await Task.WhenAll(tasks);
        }

        [Fact]
        public async Task RenderAsync_CallerProvidedOutputArrayTooSmall_ThrowsException()
        {
            using var renderer = await Renderer.CreateAsync();
            using var redTriangleUnlit = await renderer.LoadGLTFAssetAsync(File.ReadAllBytes(Path.Join(TestDataPath, "red_triangle_unlit.gltf")));
            using var orthographicCamera = await renderer.LoadGLTFAssetAsync(File.ReadAllBytes(Path.Join(TestDataPath, "orthographic_camera.gltf")));

            await Assert.ThrowsAsync<ArgumentException>(async () => await renderer.RenderAsync(100, 100, new[] { redTriangleUnlit, orthographicCamera }, new byte[40000 - 1]));
        }

        [Fact]
        public async Task RenderAsync_CallerProvidedOutputArrayTooLarge_ThrowsException()
        {
            using var renderer = await Renderer.CreateAsync();
            using var redTriangleUnlit = await renderer.LoadGLTFAssetAsync(File.ReadAllBytes(Path.Join(TestDataPath, "red_triangle_unlit.gltf")));
            using var orthographicCamera = await renderer.LoadGLTFAssetAsync(File.ReadAllBytes(Path.Join(TestDataPath, "orthographic_camera.gltf")));

            await Assert.ThrowsAsync<ArgumentException>(async () => await renderer.RenderAsync(100, 100, new[] { redTriangleUnlit, orthographicCamera }, new byte[40000 + 1]));
        }

        [Fact]
        public async Task RenderAsync_CallerProvidedOutputArray_DoesNotAllocateNewArray()
        {
            using var renderer = await Renderer.CreateAsync();
            using var redTriangleUnlit = await renderer.LoadGLTFAssetAsync(File.ReadAllBytes(Path.Join(TestDataPath, "red_triangle_unlit.gltf")));
            using var orthographicCamera = await renderer.LoadGLTFAssetAsync(File.ReadAllBytes(Path.Join(TestDataPath, "orthographic_camera.gltf")));

            byte[] userArray = new byte[40000 + 1];
            Memory<byte> userMemory = new Memory<byte>(userArray, 0, 40000); // Verify that it's OK for the underlying array to be too big as long as the Memory passed to RenderAsync is the right size
            Memory<byte> returnedData = await renderer.RenderAsync(100, 100, new[] { redTriangleUnlit, orthographicCamera }, userMemory);

            Assert.True(returnedData.Span.Overlaps(userArray));
        }

        [Fact]
        public async Task RenderAsync_TestTriangle_PixelColorIsRed()
        {
            using var renderer = await Renderer.CreateAsync();
            using var redTriangleUnlit = await renderer.LoadGLTFAssetAsync(File.ReadAllBytes(Path.Join(TestDataPath, "red_triangle_unlit.gltf")));
            using var orthographicCamera = await renderer.LoadGLTFAssetAsync(File.ReadAllBytes(Path.Join(TestDataPath, "orthographic_camera.gltf")));

            var data = await renderer.RenderAsync(100, 100, new[] { redTriangleUnlit, orthographicCamera });

            var inTrianglePixel = GetPixelColor(data.Span, 100, 100, 60, 40);
            var outOfTrianglePixel = GetPixelColor(data.Span, 100, 100, 25, 75);

            Assert.Equal(255, inTrianglePixel.A);
            Assert.True(inTrianglePixel.R > 240);
            Assert.True(inTrianglePixel.G < 30);
            Assert.True(inTrianglePixel.B < 30);

            Assert.Equal(0, outOfTrianglePixel.A);
        }

        private sealed class MockLogger : ILogger
        {
            public event Action<LogLevel, string>? LogReceived;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                LogReceived?.Invoke(logLevel, formatter(state, exception));
            }
        }

        [Fact]
        public async Task CreateAsync_Logger_ReceivesInfoMessages()
        {
            var logger = new MockLogger();
            Renderer.Logger = logger;

            bool gotExpectedMessage = false;
            logger.LogReceived += (level, message) => {
                if (level == LogLevel.Information && message.Contains("Vulkan device driver: SwiftShader driver"))
                {
                    gotExpectedMessage = true;
                }
            };

            using var renderer = await Renderer.CreateAsync();

            Assert.True(gotExpectedMessage);
        }

        private static string TestDataPath => Path.Join(Path.GetDirectoryName(typeof(RendererTests).Assembly.Location), "TestData");

        private static Color GetPixelColor(ReadOnlySpan<byte> rgbaPixelData, int width, int height, int x, int y)
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
