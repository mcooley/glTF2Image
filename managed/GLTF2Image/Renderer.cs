﻿using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GLTF2Image
{
    public sealed class Renderer : IDisposable, IAsyncDisposable
    {
        internal nint _handle;
        private WorkQueue _workQueue = new();

        private static ILogger? _logger;
        public static ILogger? Logger
        {
            get => _logger;
            set
            {
                if (_logger == null && value != null)
                {
                    _logger = value;
                    unsafe
                    {
                        NativeMethods.ThrowIfNativeApiFailed(NativeMethods.setLogCallback(&LogCallback, 0));
                    }
                }
                else if (_logger != null && value == null)
                {
                    unsafe
                    {
                        NativeMethods.ThrowIfNativeApiFailed(NativeMethods.setLogCallback(null, 0));
                    }
                    _logger = null;
                }
                else
                {
                    _logger = value;
                }
            }
        }

        [UnmanagedCallersOnly]
        private static unsafe void LogCallback(NativeMethods.LogLevel level, byte* message, nint user)
        {
            LogLevel logLevel = (LogLevel)level;
            if (Logger?.IsEnabled(logLevel) ?? false)
            {
                string logMessage = Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(message));
                Logger.Log(logLevel, logMessage);
            }
        }

        private Renderer()
        {
        }

        ~Renderer()
        {
            Dispose();
        }

        public static async Task<Renderer> CreateAsync()
        {
            Renderer renderer = new();
            renderer._handle = await renderer._workQueue.RunAsync(() =>
            {
                nint handle;
                NativeMethods.ThrowIfNativeApiFailed(NativeMethods.createRenderManager(out handle));
                return handle;
            });

            return renderer;
        }

        public Task<GLTFAsset> LoadGLTFAssetAsync(ReadOnlyMemory<byte> data)
        {
            return _workQueue.RunAsync(() =>
            {
                nint assetHandle;
                using (var dataPin = data.Pin())
                {
                    unsafe
                    {
                        NativeMethods.ThrowIfNativeApiFailed(NativeMethods.loadGLTFAsset(_handle, (byte*)dataPin.Pointer, (uint)data.Length, out assetHandle));
                    }
                }
                return new GLTFAsset(this, assetHandle);
            });
        }

        private sealed class RenderResult
        {
            private readonly Memory<byte> _result;
            private MemoryHandle _pinnedResult;
            private readonly CallbackContext<Memory<byte>> _callback = new();

            public Task<Memory<byte>> Task => _callback.Task;

            public RenderResult(uint width, uint height)
            {
                _result = new byte[GetRequiredResultLength(width, height)];
                _pinnedResult = _result.Pin();
            }

            public RenderResult(uint width, uint height, Memory<byte> result)
            {
                int requiredLength = GetRequiredResultLength(width, height);
                if (result.Length != requiredLength)
                {
                    throw new ArgumentException($"Result memory must be {requiredLength} bytes");
                }

                _result = result;
                _pinnedResult = _result.Pin();
            }

            public unsafe byte* ResultBufferAddress()
            {
                return (byte*)_pinnedResult.Pointer;
            }

            public uint ResultBufferLength()
            {
                return (uint)_result.Length;
            }

            public GCHandle ToGCHandle()
            {
                return GCHandle.Alloc(this);
            }

            public static RenderResult FromGCHandle(GCHandle handle)
            {
                RenderResult result = (RenderResult)handle.Target!;
                handle.Free();

                result._pinnedResult.Dispose();

                return result;
            }

            public void SetException(Exception exception)
            {
                _callback.SetException(exception);
            }

            public void SetComplete()
            {
                _callback.SetResult(_result);
            }

            private static int GetRequiredResultLength(uint width, uint height)
            {
                return (int)(4 * width * height);
            }
        }

        public Task<Memory<byte>> RenderAsync(uint width, uint height, IList<GLTFAsset> assets)
        {
            return RenderAsync(width, height, assets, new RenderResult(width, height));
        }

        public Task<Memory<byte>> RenderAsync(uint width, uint height, IList<GLTFAsset> assets, Memory<byte> outputMemory)
        {
            return RenderAsync(width, height, assets, new RenderResult(width, height, outputMemory));
        }

        private Task<Memory<byte>> RenderAsync(uint width, uint height, IList<GLTFAsset> assets, RenderResult result)
        {
            nint[] handles = new nint[assets.Count];
            for (int i = 0; i < assets.Count; i++)
            {
                handles[i] = assets[i]._handle;
            }

            _workQueue.Add(() =>
            {
                unsafe
                {
                    uint nativeApiResult = NativeMethods.render(
                        _handle,
                        width,
                        height,
                        handles,
                        (uint)handles.Length,
                        result.ResultBufferAddress(),
                        result.ResultBufferLength(),
                        &RenderCallback,
                        GCHandle.ToIntPtr(result.ToGCHandle()));
                    if (nativeApiResult != 0)
                    {
                        result.SetException(NativeMethods.GetNativeApiException(nativeApiResult));
                    }
                }
            });

            return result.Task;
        }

        [UnmanagedCallersOnly]
        private static void RenderCallback(uint nativeApiResult, nint user)
        {
            GCHandle resultHandle = GCHandle.FromIntPtr(user);
            RenderResult result = RenderResult.FromGCHandle(resultHandle);

            if (nativeApiResult != 0)
            {
                result.SetException(NativeMethods.GetNativeApiException(nativeApiResult));
            }
            else
            {
                result.SetComplete();
            }
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().Wait();
        }

        public async ValueTask DisposeAsync()
        {
            Task disposeRendererTask = _workQueue.RunAsync(() =>
            {
                if (_handle != 0)
                {
                    NativeMethods.ThrowIfNativeApiFailed(NativeMethods.destroyRenderManager(_handle));
                    _handle = 0;
                    GC.SuppressFinalize(this);
                }
            });
            _workQueue.Dispose();
            await disposeRendererTask;
        }

        internal async Task DestroyGLTFAssetAsync(GLTFAsset gltfAsset)
        {
            await _workQueue.RunAsync(() =>
            {
                if (gltfAsset._handle != 0)
                {
                    NativeMethods.ThrowIfNativeApiFailed(NativeMethods.destroyGLTFAsset(_handle, gltfAsset._handle));
                    gltfAsset._handle = 0;
                }
            });
        }
    }
}