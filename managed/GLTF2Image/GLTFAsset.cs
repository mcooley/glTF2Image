using System;
using System.Threading.Tasks;

namespace GLTF2Image
{
    public sealed class GLTFAsset : IDisposable, IAsyncDisposable
    {
        private RenderManager _renderManager;
        internal nint _handle;

        internal GLTFAsset(RenderManager renderManager, nint handle)
        {
            _renderManager = renderManager;
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
            await _renderManager.DestroyGLTFAssetAsync(this);
            GC.SuppressFinalize(this);
        }
    }
}