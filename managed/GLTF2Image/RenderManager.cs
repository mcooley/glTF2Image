using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GLTF2Image
{
    public sealed class RenderManager : IDisposable, IAsyncDisposable
    {
        internal nint _handle;
        private WorkQueue _workQueue = new();

        private sealed class WorkQueue : IDisposable
        {
            private Thread _workerThread;
            private ConcurrentQueue<Action?> _queue = new();
            private ManualResetEvent _event = new(false);

            public WorkQueue()
            {
                _workerThread = new(ProcessItems);
                _workerThread.Name = "RenderManager work queue";
                _workerThread.Start();
            }

            public void Add(Action work)
            {
                _queue.Enqueue(work);
                _event.Set();
            }

            public Task RunAsync(Action work)
            {
                CallbackDetails<bool> callbackDetails = new();

                Add(() =>
                {
                    try
                    {
                        work();
                        callbackDetails.SetResult(true);
                    }
                    catch (Exception e)
                    {
                        callbackDetails.SetException(e);
                    }
                });

                return callbackDetails.Task;
            }

            public Task<T> RunAsync<T>(Func<T> work)
            {
                CallbackDetails<T> callbackDetails = new();

                Add(() =>
                {
                    try
                    {
                        callbackDetails.SetResult(work());
                    }
                    catch (Exception e)
                    {
                        callbackDetails.SetException(e);
                    }
                });

                return callbackDetails.Task;
            }

            public void Dispose()
            {
                _queue.Enqueue(null);
                _event.Set();
            }

            private void ProcessItems()
            {
                while (true)
                {
                    if (_queue.TryDequeue(out Action? work))
                    {
                        if (work == null)
                        {
                            break;
                        }
                        else
                        {
                            work();
                        }
                    }

                    _event.WaitOne();
                }
            }
        }

        private sealed class CallbackDetails<T>
        {
            private readonly TaskCompletionSource<T> _taskCompletionSource = new();
            private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;

            public Task<T> Task => _taskCompletionSource.Task;

            public void SetException(Exception exception)
            {
                if (_synchronizationContext != null)
                {
                    _synchronizationContext.Post((object? _) => _taskCompletionSource.SetException(exception), null);
                }
                else
                {
                    _ = System.Threading.Tasks.Task.Run(() => _taskCompletionSource.SetException(exception));
                }
            }

            public void SetResult(T result)
            {
                if (_synchronizationContext != null)
                {
                    _synchronizationContext.Post((object? _) => _taskCompletionSource.SetResult(result), null);
                }
                else
                {
                    _ = System.Threading.Tasks.Task.Run(() => _taskCompletionSource.SetResult(result));
                }
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

        public Task<byte[]> RenderAsync(uint width, uint height, IList<GLTFAsset> assets)
        {
            CallbackDetails<byte[]> callbackDetails = new();
            GCHandle callbackDetailsHandle = GCHandle.Alloc(callbackDetails);

            nint[] handles = new nint[assets.Count];
            for (int i = 0; i < assets.Count; i++)
            {
                handles[i] = assets[i]._handle;
            }

            _workQueue.Add(() =>
            {
                unsafe
                {
                    uint nativeApiResult = NativeMethods.render(_handle, width, height, handles, (uint)handles.Length, &RenderCallback, GCHandle.ToIntPtr(callbackDetailsHandle));
                    if (nativeApiResult != 0)
                    {
                        callbackDetails.SetException(NativeMethods.GetNativeApiException(nativeApiResult));
                    }
                }
            });

            return callbackDetails.Task;
        }

        [UnmanagedCallersOnly]
        public static void RenderCallback(uint nativeApiResult, nint data, uint width, uint height, nint user)
        {
            GCHandle callbackDetailsHandle = GCHandle.FromIntPtr(user);
            CallbackDetails<byte[]> callbackDetails = (CallbackDetails<byte[]>)callbackDetailsHandle.Target!;
            callbackDetailsHandle.Free();

            if (nativeApiResult != 0)
            {
                callbackDetails.SetException(NativeMethods.GetNativeApiException(nativeApiResult));
            }
            else
            {
                uint size = 4 * width * height;
                byte[] managedArray = new byte[size];
                Marshal.Copy(data, managedArray, 0, (int)size);

                callbackDetails.SetResult(managedArray);
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