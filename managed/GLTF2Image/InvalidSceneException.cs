using System;

namespace GLTF2Image
{
    public class InvalidSceneException : Exception
    {
        internal InvalidSceneException(string message) : base(message) { }
    }
}
