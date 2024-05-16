using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GLTF2Image
{
    internal static class NativeMethods
    {
        [DllImport("gltf2image_native")]
        public static extern uint createRenderManager(out nint renderManager);

        [DllImport("gltf2image_native")]
        public static extern uint destroyRenderManager(nint renderManager);

        [DllImport("gltf2image_native")]
        public static unsafe extern uint loadGLTFAsset(nint renderManager, byte* data, uint size, out nint gltfAsset);

        [DllImport("gltf2image_native")]
        public static extern uint destroyGLTFAsset(nint renderManager, nint gltfAsset);

        [DllImport("gltf2image_native")]
        public static extern uint createJob(nint renderManager, uint width, uint height, out nint job);

        [DllImport("gltf2image_native")]
        public static extern uint destroyJob(nint renderManager, nint job);

        [DllImport("gltf2image_native")]
        public static extern uint addAsset(nint job, nint gltfAsset);

        [DllImport("gltf2image_native")]
        public static unsafe extern uint renderJob(nint renderManager, nint job, delegate* unmanaged<IntPtr, uint, uint, IntPtr, void> callback, IntPtr user);

        public static void ThrowIfNativeApiFailed(uint nativeApiResult)
        {
            if (nativeApiResult != 0)
            {
                ThrowNativeApiException(nativeApiResult);
            }
        }

        private static void ThrowNativeApiException(uint nativeApiResult)
        {
            switch (nativeApiResult)
            {
                case 1: // UnknownError
                    throw new Exception("Unknown error in gltf2image_native");
                case 2: // InvalidScene_CouldNotLoadAsset
                    throw new InvalidSceneException("Could not load asset");
                default:
                    Debug.Fail("ApiResult was not handled");
                    throw new Exception("Unknown error in gltf2image_native");
            }
        }
    }
}
