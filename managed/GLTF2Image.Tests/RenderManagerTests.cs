using Xunit;

namespace GLTF2Image.Tests
{
    public class RenderManagerTests
    {
        [Fact]
        public void Init_DoesNotCrash()
        {
            using var rm = new RenderManager();
        }
    }
}