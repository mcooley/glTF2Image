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
        private ConcurrentQueue<Action?> _highPriorityQueue = new();
        private AutoResetEvent _event = new(false);

        public WorkQueue()
        {
            _workerThread = new(ProcessItems);
            _workerThread.Name = "GLTF2Image work queue";
            _workerThread.Start();
        }

        public void Add(Action work, bool highPriority = false)
        {
            (highPriority ? _highPriorityQueue : _queue).Enqueue(work);
            _event.Set();
        }

        public Task RunAsync(Action work, bool highPriority = false)
        {
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

            Add(() =>
            {
                try
                {
                    work();
                    completion.SetResult();
                }
                catch (Exception e)
                {
                    completion.SetException(e);
                }
            },
            highPriority);

            return completion.Task;
        }

        public Task<T> RunAsync<T>(Func<T> work, bool highPriority = false)
        {
            TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

            Add(() =>
            {
                try
                {
                    completion.SetResult(work());
                }
                catch (Exception e)
                {
                    completion.SetException(e);
                }
            },
            highPriority);

            return completion.Task;
        }

        public void Dispose()
        {
            _queue.Enqueue(null);
            _event.Set();
        }

        private void ProcessItems()
        {
            bool hasMoreWork = true;
            while (hasMoreWork)
            {
                _event.WaitOne();

                while (TryDequeue(out Action? work))
                {
                    if (work == null)
                    {
                        hasMoreWork = false;
                        break;
                    }
                    else
                    {
                        work();
                    }
                }
            }

            _event.Dispose();
        }

        private bool TryDequeue(out Action? work)
        {
            if (_highPriorityQueue.TryDequeue(out work))
            {
                return true;
            }

            return _queue.TryDequeue(out work);
        }
    }
}
