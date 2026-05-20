using System;
using System.Threading.Tasks;

namespace GLTF2Image
{
    public sealed class GLTFAsset : IDisposable, IAsyncDisposable
    {
        private readonly Renderer _renderer;
        internal ReadOnlyMemory<byte> _data;
        internal bool _keepLoadedWhenNoPendingRender;

        internal nint _handle;

        // Number of in-flight render submissions that have loaded this asset's entities into a live Filament Scene
        // and are still waiting for cleanup to run. Mutated only on the engine thread (the work queue worker).
        internal int _pendingRenderCount;

        internal GLTFAsset(Renderer renderer, ReadOnlyMemory<byte> data, bool keepLoadedForMultipleRenders)
        {
            _renderer = renderer;
            _data = data;
            _keepLoadedWhenNoPendingRender = keepLoadedForMultipleRenders;
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
            _keepLoadedWhenNoPendingRender = false;
            await _renderer.DestroyGLTFAssetAsync(this);
            GC.SuppressFinalize(this);
        }
    }
}