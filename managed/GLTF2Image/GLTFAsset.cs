using System;

namespace GLTF2Image
{
    public class GLTFAsset : IDisposable
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
            if (_handle != 0)
            {
                NativeMethods.destroyGLTFAsset(_renderManager._handle, _handle);
                _handle = 0;
                GC.SuppressFinalize(this);
            }
        }
    }
}