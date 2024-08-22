using System.Runtime.InteropServices;
using Veldrid;

namespace Application
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Color
    {
        public byte r, g, b, a;

        public Color(byte r, byte g, byte b, byte a = 255)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public Color(float r, float g, float b, float a = 1.0f)
        {
            this.r = (byte)(Math.Clamp(r, 0, 1) * 255);
            this.g = (byte)(Math.Clamp(g, 0, 1) * 255);
            this.b = (byte)(Math.Clamp(b, 0, 1) * 255);
            this.a = (byte)(Math.Clamp(a, 0, 1) * 255);
        }


        public static readonly Color White = new Color(1f, 1f, 1f, 1f);
        public static readonly Color Black = new Color(0f, 0f, 0f, 1f);
        public static readonly Color Gray = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        public static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);
    }

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
    }
}