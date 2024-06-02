using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GLTF2Image
{
    public sealed class RenderManager : IDisposable, IAsyncDisposable
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

        private RenderManager()
        {
        }

        ~RenderManager()
        {
            Dispose();
        }

        public static async Task<RenderManager> CreateAsync()
        {
            RenderManager renderManager = new();
            renderManager._handle = await renderManager._workQueue.RunAsync(() =>
            {
                nint handle;
                NativeMethods.ThrowIfNativeApiFailed(NativeMethods.createRenderManager(out handle));
                return handle;
            });

            return renderManager;
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
            private readonly byte[] _result;
            private GCHandle _pinnedResult;
            private readonly CallbackContext<byte[]> _callback = new();

            public Task<byte[]> Task => _callback.Task;

            public RenderResult(uint width, uint height)
            {
                _result = new byte[width * height * 4]; // TODO consider renting this from a pool instead
                _pinnedResult = GCHandle.Alloc(_result, GCHandleType.Pinned);
            }

            public unsafe byte* ResultBufferAddress()
            {
                if (_pinnedResult == default)
                {
                    throw new InvalidOperationException("Result is no longer pinned");
                }

                return (byte*)_pinnedResult.AddrOfPinnedObject();
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

                // Unpin _result
                result._pinnedResult.Free();
                result._pinnedResult = default;

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
        }

        public Task<byte[]> RenderAsync(uint width, uint height, IList<GLTFAsset> assets)
        {
            nint[] handles = new nint[assets.Count];
            for (int i = 0; i < assets.Count; i++)
            {
                handles[i] = assets[i]._handle;
            }

            RenderResult result = new(width, height);

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
            Task disposeRenderManagerTask = _workQueue.RunAsync(() =>
            {
                if (_handle != 0)
                {
                    NativeMethods.ThrowIfNativeApiFailed(NativeMethods.destroyRenderManager(_handle));
                    _handle = 0;
                    GC.SuppressFinalize(this);
                }
            });
            _workQueue.Dispose();
            await disposeRenderManagerTask;
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