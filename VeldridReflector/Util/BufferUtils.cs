using System.Runtime.InteropServices;
using Veldrid;

namespace Application
{
    public static class BufferUtils
    {
        private static DeviceBuffer? _emptyBuffer;
        public static DeviceBuffer GetEmptyBuffer(GraphicsDevice device)
        {
            if (_emptyBuffer != null)
                return _emptyBuffer;

            _emptyBuffer = device.ResourceFactory.CreateBuffer(new BufferDescription(1, BufferUsage.StructuredBufferReadOnly));

            return _emptyBuffer;
        }

        private static DeviceBuffer? _emptyRWBuffer;
        public static DeviceBuffer GetEmptyRWBuffer(GraphicsDevice device)
        {
            if (_emptyRWBuffer != null)
                return _emptyRWBuffer;

            _emptyRWBuffer = device.ResourceFactory.CreateBuffer(new BufferDescription(1, BufferUsage.StructuredBufferReadWrite));

            return _emptyRWBuffer;
        }
    }
}