using System;

namespace GLTF2Image
{
    public class RenderJob : IDisposable
    {
        private RenderManager _renderManager;
        internal nint _handle;

        internal RenderJob(RenderManager renderManager, nint handle)
        {
            _renderManager = renderManager;
            _handle = handle;
        }

        ~RenderJob()
        {
            Dispose();
        }

        public void AddAsset(GLTFAsset asset)
        {
            NativeMethods.ThrowIfNativeApiFailed(NativeMethods.addAsset(_handle, asset._handle));
        }

        public void Dispose()
        {
            if (_handle != 0)
            {
                NativeMethods.ThrowIfNativeApiFailed(NativeMethods.destroyJob(_renderManager._handle, _handle));
                _handle = 0;
                GC.SuppressFinalize(this);
            }
        }
    }
}