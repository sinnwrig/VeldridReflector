using System.Drawing;
using System.Numerics;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

#pragma warning disable

namespace Application
{
    public static class Test
    {
        private static Sdl2Window window;
        private static GraphicsDevice device;

        private static BindableShader shader;
        private static BindableResourceSet resources;

        private static Texture texture;
        private static TextureView textureView;

        private static CommandList list;
        private static Pipeline pipeline;


        static float time;

        public static void Main()
        {

            WindowCreateInfo ci = new(1920 / 2, 1080 / 2, 1920 / 4, 1080 / 4, WindowState.Normal, "Reflection Demo");
            window = VeldridStartup.CreateWindow(ci);

            GraphicsDeviceOptions opt = new(false, PixelFormat.R16_UNorm, false);
            device = VeldridStartup.CreateGraphicsDevice(window, opt, GraphicsBackend.Vulkan);

            window.Resized += () => device.ResizeMainWindow((uint)window.Width, (uint)window.Height);

            CreateResources(device.ResourceFactory);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            int fpsCounter = 0;
            float counterSeconds = 0;

            while (window.Exists)
            {
                window.PumpEvents();

                float dt = (float)watch.Elapsed.TotalSeconds;

                counterSeconds += dt;
                time += dt;

                if (counterSeconds < 1.0f)
                {
                    fpsCounter++;
                }
                else
                {
                    Console.Write($"\rFPS : {fpsCounter}");
                    counterSeconds = 0;
                    fpsCounter = 0;
                }

                watch.Restart();

                Draw();
            }
        }


        static ShaderDescription[] CompileShader(GraphicsDevice device, out ReflectedResourceInfo resources)
        {
            bool flipVertexY = device.BackendType == GraphicsBackend.Vulkan;

            string shaderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", "cube.hlsl");
            string shaderCode = File.ReadAllText(shaderPath);

            var compiledSPIRV = ShaderCompiler.Compile(shaderCode, [("vert", ShaderStages.Vertex), ("frag", ShaderStages.Fragment)], flipVertexY);

            using var context = new SPIRVCross.NET.Context();

            resources = ShaderCompiler.Reflect(context, compiledSPIRV);

            return ShaderCompiler.CrossCompile(context, device.BackendType, compiledSPIRV);
        }


        static void CreateResources(ResourceFactory factory)
        {
            RgbaByte[,] texData = new RgbaByte[24, 24];

            for (int i = 0; i < texData.GetLength(0); i++)
            {
                for (int j = 0; j < texData.GetLength(1); j++)
                {
                    texData[i, j] = i == 0 || j == 0 || i == texData.GetLength(0) - 1 || j == texData.GetLength(1) - 1 ?
                        RgbaByte.Black : RgbaByte.White;
                }
            }

            texture = TextureUtils.Create2D<RgbaByte>(texData, device, TextureUsage.Sampled);
            textureView = factory.CreateTextureView(texture);

            bool flipVertexY = device.BackendType == GraphicsBackend.Vulkan;

            ShaderDescription[] shaders = CompileShader(device, out ReflectedResourceInfo resourceInfo);

            BindableShaderDescription pipelineDescription =
                new(resourceInfo.vertexInputs, resourceInfo.uniforms, resourceInfo.stages);

            shader = new BindableShader(pipelineDescription, shaders, device);

            resources = shader.CreateResources(device);
            resources.SetTexture("SurfaceTexture", texture);

            GraphicsPipelineDescription description = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.DepthOnlyLessEqual,
                RasterizerStateDescription.Default,
                PrimitiveTopology.TriangleList,
                shader.shaderSet,
                shader.resourceLayout,
                device.SwapchainFramebuffer.OutputDescription
            );

            description.RasterizerState.FrontFace = FrontFace.Clockwise;

            pipeline = factory.CreateGraphicsPipeline(description);
            list = factory.CreateCommandList();
        }

        static void Draw()
        {
            list.Begin();
            list.SetFramebuffer(device.MainSwapchain.Framebuffer);
            list.ClearColorTarget(0, RgbaFloat.Black);
            list.ClearDepthStencil(1f);
            list.SetPipeline(pipeline);

            Matrix4x4 FOV = Matrix4x4.CreatePerspectiveFieldOfView(
                1.0f,
                (float)window.Width / window.Height,
                0.5f,
                100f);

            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 5, 5), Vector3.Zero, Vector3.UnitY);
            Matrix4x4 rot = Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, time);

            Cube.SetDrawData(device, list, shader);

            resources.SetVector("VData", Vector4.One);
            resources.SetVector("PData", Vector4.One);

            resources.Bind(device.ResourceFactory, list);

            // Size of each small cube
            float cubeSize = 1.0f;
            float halfCubeSize = cubeSize / 2.0f;
            float spacing = 1.025f; // Space between cubes to avoid overlap

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        // Calculate the position of each small cube
                        Vector3 position = new Vector3(x * spacing, y * spacing, z * spacing);

                        // Create the transformation matrix for this position
                        Matrix4x4 matrix = Matrix4x4.CreateTranslation(position);

                        resources.SetVector("BaseColor", GetCubeColor(x, y, z));
                        resources.SetMatrix("MVP", matrix * rot * view * FOV);
                        resources.UpdateBuffer(list, "_Globals");

                        Cube.Draw(list);
                    }
                }
            }

            Matrix4x4 matrix2 = Matrix4x4.CreateScale(cubeSize * 3) * Matrix4x4.CreateTranslation(Vector3.Zero);

            resources.SetVector("BaseColor", new Vector4(0, 0.25f, 0.25f, 1));
            resources.SetMatrix("MVP", matrix2 * rot * view * FOV);

            resources.UpdateBuffer(list, "_Globals");
            Cube.Draw(list);

            list.End();

            device.SubmitCommands(list);
            device.SwapBuffers(device.MainSwapchain);
            device.WaitForIdle();
        }


        private static Vector4 GetCubeColor(int x, int y, int z)
        {
            float offset = x * 0.2f + y * 0.5f + z * 0.6f;

            return new Vector4(HSVToRGB(new Vector3((time + offset) % 6.0f, 1, 1)), 1.0f);
        }

        public static Vector3 HSVToRGB(Vector3 c)
        {
            // Equivalent of vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0) in GLSL
            Vector4 K = new Vector4(1.0f, 2.0f / 3.0f, 1.0f / 3.0f, 3.0f);

            // Equivalent of abs(fract(c.xxx + K.xyz) * 6.0 - K.www)
            Vector3 p = Vector3.Abs(Fract(new Vector3(c.X) + new Vector3(K.Y, K.Z, K.W)) * 6.0f - new Vector3(K.W));

            // Equivalent of return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y)
            return c.Z * Vector3.Lerp(new Vector3(K.X), Vector3.Clamp(p - new Vector3(K.X), Vector3.Zero, Vector3.One), c.Y);
        }


        public static Vector3 Fract(Vector3 v)
            => v - new Vector3(MathF.Floor(v.X), MathF.Floor(v.Y), MathF.Floor(v.Z));

        private static Mesh Cube = Mesh.CreateCube(Vector3.One);
    }
}