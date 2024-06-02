using System;
using System.Threading.Tasks;

namespace GLTF2Image
{
    public sealed class GLTFAsset : IDisposable, IAsyncDisposable
    {
        private Renderer _renderer;
        internal nint _handle;

        internal GLTFAsset(Renderer renderer, nint handle)
        {
            _renderer = renderer;
            _handle = handle;
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
            await _renderer.DestroyGLTFAssetAsync(this);
            GC.SuppressFinalize(this);
        }
    }
}