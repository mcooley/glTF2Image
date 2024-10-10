using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace GLTF2Image
{
    internal sealed class WorkQueue : IDisposable
    {
        private Thread _workerThread;
        private ConcurrentQueue<Action?> _queue = new();
        private AutoResetEvent _event = new(false);

        public WorkQueue()
        {
            _workerThread = new(ProcessItems);
            _workerThread.Name = "GLTF2Image work queue";
            _workerThread.Start();
        }

        public void Add(Action work)
        {
            _queue.Enqueue(work);
            _event.Set();
        }

        public Task RunAsync(Action work)
        {
            CallbackContext<bool> callbackContext = new();

            Add(() =>
            {
                try
                {
                    work();
                    callbackContext.SetResult(true);
                }
                catch (Exception e)
                {
                    callbackContext.SetException(e);
                }
            });

            return callbackContext.Task;
        }

        public Task<T> RunAsync<T>(Func<T> work)
        {
            CallbackContext<T> callbackContext = new();

            Add(() =>
            {
                try
                {
                    callbackContext.SetResult(work());
                }
                catch (Exception e)
                {
                    callbackContext.SetException(e);
                }
            });

            return callbackContext.Task;
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
                _event.WaitOne();

                while (_queue.TryDequeue(out Action? work))
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
            }
        }
    }
}
