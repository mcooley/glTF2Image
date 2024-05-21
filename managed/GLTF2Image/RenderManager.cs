using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GLTF2Image
{
    public class RenderManager : IDisposable
    {
        internal nint _handle;

        public RenderManager()
        {
            NativeMethods.ThrowIfNativeApiFailed(NativeMethods.createRenderManager(out _handle));
        }

        ~RenderManager()
        {
            Dispose();
        }

        public GLTFAsset LoadGLTFAsset(ReadOnlySpan<byte> data)
        {
            nint assetHandle;
            unsafe
            {
                fixed (byte* pData = data)
                {
                    NativeMethods.ThrowIfNativeApiFailed(NativeMethods.loadGLTFAsset(_handle, pData, (uint)data.Length, out assetHandle));
                }
            }
            return new GLTFAsset(this, assetHandle);
        }

        public Task<byte[]> RenderAsync(uint width, uint height, IList<GLTFAsset> assets)
        {
            TaskCompletionSource<byte[]> taskCompletionSource = new();
            GCHandle completionSourceHandle = GCHandle.Alloc(taskCompletionSource);
            nint[] handles = new nint[assets.Count];
            for (int i = 0; i < assets.Count; i++)
            {
                handles[i] = assets[i]._handle;
            }

            unsafe
            {
                NativeMethods.ThrowIfNativeApiFailed(NativeMethods.render(_handle, width, height, handles, (uint)handles.Length, &RenderJobCallback, GCHandle.ToIntPtr(completionSourceHandle)));
            }
            return taskCompletionSource.Task;
        }

        [UnmanagedCallersOnly]
        public static void RenderJobCallback(uint nativeApiResult, IntPtr data, uint width, uint height, IntPtr user)
        {
            GCHandle taskCompletionSourceHandle = GCHandle.FromIntPtr(user);
            TaskCompletionSource<byte[]> taskCompletionSource = (TaskCompletionSource<byte[]>)taskCompletionSourceHandle.Target!;
            taskCompletionSourceHandle.Free();

            if (nativeApiResult != 0)
            {
                // Resume on a threadpool thread.
                Task.Run(() => taskCompletionSource.SetException(NativeMethods.GetNativeApiException(nativeApiResult)));
            }
            else
            {
                uint size = 4 * width * height;
                byte[] managedArray = new byte[size];
                Marshal.Copy(data, managedArray, 0, (int)size);

                // Resume on a threadpool thread.
                Task.Run(() => taskCompletionSource.SetResult(managedArray));
            }
        }

        public void Dispose()
        {
            if (_handle != 0)
            {
                NativeMethods.ThrowIfNativeApiFailed(NativeMethods.destroyRenderManager(_handle));
                _handle = 0;
                GC.SuppressFinalize(this);
            }
        }
    }
}