﻿using System;
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

        public RenderJob CreateJob(uint width, uint height)
        {
            nint jobHandle;
            NativeMethods.ThrowIfNativeApiFailed(NativeMethods.createJob(_handle, width, height, out jobHandle));
            return new RenderJob(this, jobHandle);
        }

        public Task<byte[]> RenderJobAsync(RenderJob job)
        {
            TaskCompletionSource<byte[]> taskCompletionSource = new();
            GCHandle completionSourceHandle = GCHandle.Alloc(taskCompletionSource);
            unsafe
            {
                NativeMethods.ThrowIfNativeApiFailed(NativeMethods.renderJob(_handle, job._handle, &RenderJobCallback, GCHandle.ToIntPtr(completionSourceHandle)));
            }
            return taskCompletionSource.Task;
        }

        [UnmanagedCallersOnly]
        public static void RenderJobCallback(IntPtr data, uint width, uint height, IntPtr user)
        {
            GCHandle taskCompletionSourceHandle = GCHandle.FromIntPtr(user);
            TaskCompletionSource<byte[]> taskCompletionSource = (TaskCompletionSource<byte[]>)taskCompletionSourceHandle.Target!;
            taskCompletionSourceHandle.Free();
            
            uint size = 4 * width * height;
            byte[] managedArray = new byte[size];
            Marshal.Copy(data, managedArray, 0, (int)size);

            // Resume on a threadpool thread.
            Task.Run(() => taskCompletionSource.SetResult(managedArray));
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