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
            public readonly byte[] _result; // TODO private
            private readonly CallbackContext<byte[]> _callback = new();

            public Task<byte[]> Task => _callback.Task;

            public RenderResult(uint width, uint height)
            {
                _result = new byte[width * height * 4]; // TODO rent this from a pool instead
            }

            internal void SetException(Exception exception)
            {
                _callback.SetException(exception);
            }

            internal void MarkComplete()
            {
                _callback.SetResult(_result);
            }
        }

        public Task<byte[]> RenderAsync(uint width, uint height, IList<GLTFAsset> assets)
        {
            RenderResult result = new(width, height);
            GCHandle resultHandle = GCHandle.Alloc(result);

            nint[] handles = new nint[assets.Count];
            for (int i = 0; i < assets.Count; i++)
            {
                handles[i] = assets[i]._handle;
            }

            _workQueue.Add(() =>
            {
                unsafe
                {
                    var handle = GCHandle.Alloc(result._result, GCHandleType.Pinned); // TODO need to unpin
                    uint nativeApiResult = NativeMethods.render(
                        _handle,
                        width,
                        height,
                        handles,
                        (uint)handles.Length,
                        (byte*)handle.AddrOfPinnedObject(),
                        (uint)result._result.Length,
                        &RenderCallback,
                        GCHandle.ToIntPtr(resultHandle));
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
            RenderResult result = (RenderResult)resultHandle.Target!;
            resultHandle.Free();

            if (nativeApiResult != 0)
            {
                result.SetException(NativeMethods.GetNativeApiException(nativeApiResult));
            }
            else
            {
                result.MarkComplete();
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