using System.Numerics;

using Veldrid;
using Veldrid.StartupUtilities;
using Veldrid.Sdl2;

#pragma warning disable

namespace Application
{
    public static class Test
    {
        private static Sdl2Window window;
        private static GraphicsDevice device;

        private static BindableShader shader;
        private static BindableShaderResources resources;

        private static Texture texture;
        private static TextureView textureView;
        
        private static CommandList list;
        private static Pipeline pipeline;
        

        static float time;

        public static void Main()
        {
            WindowCreateInfo ci = new(1920 / 2, 1080 / 2, 1920 / 4, 1080 / 4, WindowState.Normal, "Reflection Demo");

            GraphicsDeviceOptions opt = new(false, PixelFormat.R16_UNorm, false);

            window = VeldridStartup.CreateWindow(ci);
            
            device = VeldridStartup.CreateGraphicsDevice(window, opt, GraphicsBackend.OpenGLES);

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


        static BindableShader CompileShader(GraphicsDevice device)
        {
            bool flipVertexY = device.BackendType == GraphicsBackend.Vulkan;

            string shaderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", "cube.hlsl");
            string shaderCode = File.ReadAllText(shaderPath);

            var compiledSPIRV = ShaderCompiler.Compile(shaderCode, [ ("vert", ShaderStages.Vertex), ("frag", ShaderStages.Fragment) ], flipVertexY);
            
            using var context = new SPIRVCross.NET.Context();

            var shaderDescription = ShaderCompiler.Reflect(context, compiledSPIRV);
            var crossCompiledShaders = ShaderCompiler.CrossCompile(context, device.BackendType, compiledSPIRV);

            return new BindableShader(shaderDescription, crossCompiledShaders, device);
        }


        static void CreateResources(ResourceFactory factory)
        {
            texture = TextureUtils.Create2D<RgbaByte>(TextureData, device, TextureUsage.Sampled);
            textureView = factory.CreateTextureView(texture);

            bool flipVertexY = device.BackendType == GraphicsBackend.Vulkan;

            shader = CompileShader(device);

            resources = shader.CreateResources(device);

            resources.SetTexture("SurfaceTexture", texture);

            GraphicsPipelineDescription description = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.DepthOnlyLessEqual,
                RasterizerStateDescription.Default,
                PrimitiveTopology.TriangleList,
                shader.shaderSet,
                shader.resourceLayout,
                device.MainSwapchain.Framebuffer.OutputDescription
            );

            description.RasterizerState.FrontFace = FrontFace.Clockwise;

            pipeline = factory.CreateGraphicsPipeline(description);
            list = factory.CreateCommandList();
        }

        static void Draw()
        {
            list.Begin();
            list.SetFramebuffer(device.MainSwapchain.Framebuffer);
            list.ClearColorTarget(0, RgbaFloat.Blue);
            list.ClearDepthStencil(1f);
            list.SetPipeline(pipeline);

            Matrix4x4 FOV = Matrix4x4.CreatePerspectiveFieldOfView(
                1.0f,
                (float)window.Width / window.Height,
                0.5f,
                100f);

            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 3, 3), Vector3.Zero, Vector3.UnitY);
            Matrix4x4 rot = Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, time);

            Cube.SetDrawData(device, list, shader);

            resources.SetVector("BaseColor", Vector4.One);
            resources.SetVectorArray("AdditionalColors", AdditionalColors);
            resources.SetFloat("MinValue", 0.25f);
            resources.SetVector("ExtraColor", -Vector4.One * 0.5f);
            resources.SetMatrix("MVP", rot * view * FOV);
        
            resources.Bind(device.ResourceFactory, list);

            Cube.Draw(list);
    
            list.End();

            device.SubmitCommands(list);
            device.SwapBuffers(device.MainSwapchain);
            device.WaitForIdle();
        }


        private static RgbaByte[,] TextureData = new RgbaByte[2, 2]
        {
            { RgbaByte.White, RgbaByte.Black },  
            { RgbaByte.Black, RgbaByte.White },
        };

        private static Vector4[] AdditionalColors;

        private static Mesh Cube = Mesh.CreateCube(Vector3.One);
    }
}