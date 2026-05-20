using Microsoft.Extensions.Logging;
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

        public GLTFAsset CreateGLTFAsset(ReadOnlyMemory<byte> data, bool keepLoadedForMultipleRenders = false)
        {
            return new GLTFAsset(this, data, keepLoadedForMultipleRenders);
        }

        private sealed class RenderResult
        {
            private readonly Renderer _renderer;
            private readonly IList<GLTFAsset> _assets;
            private readonly Memory<byte> _result;
            private MemoryHandle _pinnedResult;
            private readonly CallbackContext<Memory<byte>> _callback = new();

            public Task<Memory<byte>> Task => _callback.Task;

            public RenderResult(Renderer renderer, IList<GLTFAsset> assets, uint width, uint height)
            {
                _renderer = renderer;
                _assets = assets;
                _result = new byte[GetRequiredResultLength(width, height)];
                _pinnedResult = _result.Pin();
            }

            public RenderResult(Renderer renderer, IList<GLTFAsset> assets, uint width, uint height, Memory<byte> result)
            {
                _renderer = renderer;
                _assets = assets;
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

            public void SetComplete(nint renderResources)
            {
                _renderer._workQueue.Add(
                    () =>
                    {
                        try
                        {
                            NativeMethods.ThrowIfNativeApiFailed(NativeMethods.destroyRenderResources(_renderer._handle, renderResources));
                            UnloadAssets();
                        }
                        catch (Exception ex)
                        {
                            _pinnedResult.Dispose();
                            _callback.SetException(ex);
                        }
                    },
                    highPriority: true); // Jump ahead of other rendering tasks to free GPU/CPU resources promptly.

                _callback.SetResult(_result);
            }

            private void UnloadAssets()
            {
                // Assets will be unloaded eventually when the GLTFAsset object is destroyed, but
                // it can be more efficient to unload eagerly if we know the asset won't be needed
                // again. Skip assets the caller marked as persistent or that aren't currently
                // loaded (the caller may have disposed them while the render was in flight).
                for (int i = 0; i < _assets.Count; i++)
                {
                    if (_assets[i].IsLoaded && !_assets[i]._keepLoadedForMultipleRenders)
                    {
                        NativeMethods.ThrowIfNativeApiFailed(NativeMethods.destroyGLTFAsset(_renderer._handle, _assets[i]._handle));
                        _assets[i]._handle = 0;
                    }
                }
            }

            private static int GetRequiredResultLength(uint width, uint height)
            {
                return (int)(4 * width * height);
            }
        }

        public Task<Memory<byte>> RenderAsync(uint width, uint height, IList<GLTFAsset> assets)
        {
            return RenderAsync(width, height, assets, new RenderResult(this, assets, width, height));
        }

        public Task<Memory<byte>> RenderAsync(uint width, uint height, IList<GLTFAsset> assets, Memory<byte> outputMemory)
        {
            return RenderAsync(width, height, assets, new RenderResult(this, assets, width, height, outputMemory));
        }

        private Task<Memory<byte>> RenderAsync(uint width, uint height, IList<GLTFAsset> assets, RenderResult result)
        {
            _workQueue.Add(() =>
            {
                // Load assets
                nint[] handles = new nint[assets.Count];
                for (int i = 0; i < assets.Count; i++)
                {
                    if (!assets[i].IsLoaded)
                    {
                        nint assetHandle;
                        using (var dataPin = assets[i]._data.Pin())
                        {
                            unsafe
                            {
                                uint nativeApiResult = NativeMethods.loadGLTFAsset(_handle, (byte*)dataPin.Pointer, (uint)assets[i]._data.Length, out assetHandle);
                                if (nativeApiResult != 0)
                                {
                                    result.SetSynchronousException(NativeMethods.GetNativeApiException(nativeApiResult));
                                    return;
                                }
                            }
                        }
                        assets[i]._handle = assetHandle;
                    }

                    handles[i] = assets[i]._handle;
                }

                // Submit render. On success, ownership of per-render resources and the pinned output
                // buffer transfers to the render completion callback, which will schedule cleanup
                // (destroyRenderResources + asset unload + Task completion) on the engine thread
                // after readPixels has populated the buffer. On synchronous failure, the native side
                // has already torn down any partially-allocated resources; we only need to surface
                // the exception.
                unsafe
                {
                    GCHandle resultHandle = result.ToGCHandle();
                    uint nativeApiResult = NativeMethods.render(
                        _handle,
                        width,
                        height,
                        handles,
                        (uint)handles.Length,
                        result.ResultBufferAddress(),
                        result.ResultBufferLength(),
                        &RenderCallback,
                        GCHandle.ToIntPtr(resultHandle));
                    if (nativeApiResult != 0)
                    {
                        RenderResult unpinnedResult = RenderResult.FromGCHandle(resultHandle); // Ensure the GCHandle is freed to avoid leaks, even in error cases
                        unpinnedResult.SetException(NativeMethods.GetNativeApiException(nativeApiResult));
                        return;
                    }
                }
            });

            return result.Task;
        }

        [UnmanagedCallersOnly]
        private static void RenderCallback(uint nativeApiResult, nint renderResources, nint user)
        {
            GCHandle resultHandle = GCHandle.FromIntPtr(user);
            RenderResult result = RenderResult.FromGCHandle(resultHandle);

            if (nativeApiResult != 0)
            {
                result.SetException(NativeMethods.GetNativeApiException(nativeApiResult));
            }
            else
            {
                result.SetComplete(renderResources);
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
                if (gltfAsset.IsLoaded)
                {
                    NativeMethods.ThrowIfNativeApiFailed(NativeMethods.destroyGLTFAsset(_handle, gltfAsset._handle));
                    gltfAsset._handle = 0;
                }
            },
            highPriority: true); // Jump ahead of other rendering tasks to avoid holding onto memory for too long
        }
    }
}