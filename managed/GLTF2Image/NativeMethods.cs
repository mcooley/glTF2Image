using System;
using System.Runtime.InteropServices;

namespace GLTF2Image
{
    internal static class NativeMethods
    {
        [DllImport("gltf2image_native")]
        public static extern nint createRenderManager();

        [DllImport("gltf2image_native")]
        public static extern void destroyRenderManager(nint renderManager);

        [DllImport("gltf2image_native")]
        public static unsafe extern IntPtr loadGLTFAsset(nint renderManager, byte* data, uint size);

        [DllImport("gltf2image_native")]
        public static extern void destroyGLTFAsset(nint renderManager, nint gltfAsset);

        [DllImport("gltf2image_native")]
        public static extern nint createJob(nint renderManager, uint width, uint height);

        [DllImport("gltf2image_native")]
        public static extern void destroyJob(nint renderManager, nint job);

        [DllImport("gltf2image_native")]
        public static extern void addAsset(nint job, nint gltfAsset);

        [DllImport("gltf2image_native")]
        public static unsafe extern void renderJob(nint renderManager, nint job, delegate* unmanaged<IntPtr, uint, uint, IntPtr, void> callback, IntPtr user);
    }
}
