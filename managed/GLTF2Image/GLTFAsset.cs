using System;
using System.Threading.Tasks;

namespace GLTF2Image
{
    public sealed class GLTFAsset : IDisposable, IAsyncDisposable
    {
        private readonly Renderer _renderer;
        internal ReadOnlyMemory<byte> _data;
        internal readonly bool _keepLoadedForMultipleRenders;

        internal nint _handle;

        internal bool IsLoaded => _handle != 0;

        internal GLTFAsset(Renderer renderer, ReadOnlyMemory<byte> data, bool keepLoadedForMultipleRenders)
        {
            _renderer = renderer;
            _data = data;
            _keepLoadedForMultipleRenders = keepLoadedForMultipleRenders;
        }

        ~GLTFAsset()
        {
            Dispose();
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().Wait();
        }

        public async ValueTask DisposeAsync()
        {
            if (IsLoaded)
            {
                await _renderer.DestroyGLTFAssetAsync(this);
            }
            GC.SuppressFinalize(this);
        }
    }
}