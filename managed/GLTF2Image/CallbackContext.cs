using System;
using System.Threading.Tasks;

namespace GLTF2Image
{
    internal sealed class CallbackContext<T>
    {
        private readonly TaskCompletionSource<T> _taskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> Task => _taskCompletionSource.Task;

        public void SetException(Exception exception)
        {
            _taskCompletionSource.SetException(exception);
        }

        public void SetResult(T result)
        {
            _taskCompletionSource.SetResult(result);
        }
    }
}
