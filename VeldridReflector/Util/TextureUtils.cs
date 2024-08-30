using System.Runtime.InteropServices;
using Veldrid;

namespace Application
{
    public static class TextureUtils
    {
        public static unsafe Texture Create2D<T>(T[,] pixels, GraphicsDevice device, TextureUsage usage, PixelFormat format = PixelFormat.R8_G8_B8_A8_UNorm) where T : unmanaged
        {
            fixed (T* texDataPtr = pixels)
                return Create2D(texDataPtr, (uint)pixels.GetLength(0), (uint)pixels.GetLength(1), device, usage, format);
        }


        public static unsafe Texture Create2D<T>(T* pixels, uint pixWidth, uint pixHeight, GraphicsDevice device, TextureUsage usage, PixelFormat format = PixelFormat.R8_G8_B8_A8_UNorm) where T : unmanaged
        {
            Texture texture = device.ResourceFactory.CreateTexture(new TextureDescription(
                pixWidth, pixHeight, 1, 1, 1, format, usage, TextureType.Texture2D));

            Texture staging = device.ResourceFactory.CreateTexture(new TextureDescription(
                pixWidth, pixHeight, 1, 1, 1, format, TextureUsage.Staging, TextureType.Texture2D));

            device.UpdateTexture(
                staging, (nint)pixels, (uint)(pixWidth * pixHeight * sizeof(T)),
                0, 0, 0, pixWidth, pixHeight, 1, 0, 0);

            CommandList cl = device.ResourceFactory.CreateCommandList();
            cl.Begin();
            cl.CopyTexture(staging, texture);
            cl.End();
            device.SubmitCommands(cl);

            return texture;
        }


        private static Texture? _emptyTex;
        public static Texture GetEmptyTexture(GraphicsDevice device)
        {
            if (_emptyTex != null)
                return _emptyTex;

            TextureDescription desc = TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled);
            _emptyTex = device.ResourceFactory.CreateTexture(desc);

            return _emptyTex;
        }

        private static Texture? _emptyRWTex;
        public static Texture GetEmptyRWTexture(GraphicsDevice device)
        {
            if (_emptyRWTex != null)
                return _emptyRWTex;

            TextureDescription desc = TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Storage);
            _emptyRWTex = device.ResourceFactory.CreateTexture(desc);

            return _emptyRWTex;
        }
    }
}