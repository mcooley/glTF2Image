using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GLTF2Image
{
    internal static partial class NativeMethods
    {
        [LibraryImport("gltf2image_native")]
        public static unsafe partial uint createRenderManager(out nint renderManager);

        [LibraryImport("gltf2image_native")]
        public static partial uint destroyRenderManager(nint renderManager);

        [LibraryImport("gltf2image_native")]
        public static unsafe partial uint loadGLTFAsset(nint renderManager, byte* data, uint size, out nint gltfAsset);

        [LibraryImport("gltf2image_native")]
        public static partial uint destroyGLTFAsset(nint renderManager, nint gltfAsset);

        [LibraryImport("gltf2image_native")]
        public static unsafe partial uint render(
            nint renderManager,
            uint width,
            uint height,
            nint[] gltfAssets,
            uint gltfAssetsCount,
            byte* output,
            uint outputLength,
            delegate* unmanaged<uint, nint, nint, void> callback,
            nint user);

        [LibraryImport("gltf2image_native")]
        public static unsafe partial uint destroyTexture(nint renderManager, nint texture);

        public enum LogLevel : uint
        {
            Verbose = 0,
            Debug = 1,
            Info = 2,
            Warning = 3,
            Error = 4,
        }

        [LibraryImport("gltf2image_native")]
        public static unsafe partial uint setLogCallback(delegate* unmanaged<LogLevel, byte*, nint, void> callback, nint user);

        public static void ThrowIfNativeApiFailed(uint nativeApiResult)
        {
            if (nativeApiResult != 0)
            {
                throw GetNativeApiException(nativeApiResult);
            }
        }

        public static Exception GetNativeApiException(uint nativeApiResult)
        {
            switch (nativeApiResult)
            {
                case 1: // UnknownError
                    return new Exception("Unknown error in gltf2image_native");
                case 2: // InvalidScene_CouldNotLoadAsset
                    return new InvalidSceneException("Could not load asset");
                case 3: // InvalidScene_NoCamerasFound
                    return new InvalidSceneException("No cameras were found");
                case 4: // InvalidScene_TooManyCameras
                    return new InvalidSceneException("Multiple cameras were found. The scene must have exactly one camera");
                case 5: // WrongThread
                    return new InvalidOperationException("API was called from the wrong thread");
                case 6: // PixelBufferWrongSize
                    return new InvalidOperationException("Pixel buffer was wrong size");
                default:
                    Debug.Fail("ApiResult was not handled");
                    return new Exception("Unknown error in gltf2image_native");
            }
        }
    }
}
