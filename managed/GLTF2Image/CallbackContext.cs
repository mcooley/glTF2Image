using System;
using System.Threading;
using System.Threading.Tasks;

namespace GLTF2Image
{
    internal sealed class CallbackContext<T>
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
}
