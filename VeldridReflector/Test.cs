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
            device = VeldridStartup.CreateGraphicsDevice(window, opt, GraphicsBackend.Direct3D11);

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

            var compiledSPIRV = ShaderCompiler.Compile(shaderCode, [ ("vert", ShaderStages.Vertex), ("frag", ShaderStages.Fragment) ], flipVertexY);
            
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


        public static Matrix4x4[,,] GetRubiksCubeMatrices()
        {
            // Array to hold the 27 matrices
            Matrix4x4[,,] matrices = new Matrix4x4[3, 3, 3];

            // Size of each small cube
            float cubeSize = 1.0f;
            float halfCubeSize = cubeSize / 2.0f;
            float spacing = 1.025f; // Space between cubes to avoid overlap

            // Loop to create the 3x3x3 grid
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        // Calculate the position of each small cube
                        Vector3 position = new Vector3(x * spacing, y * spacing, z * spacing);
                        
                        // Create the transformation matrix for this position
                        Matrix4x4 matrix = Matrix4x4.CreateTranslation(position) * 
                            Matrix4x4.CreateFromQuaternion(Quaternion.Identity);
                        
                        // Store the matrix in the array
                        matrices[x + 1, y + 1, z + 1] = matrix;
                    }
                }
            }

            return matrices;
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
        
            resources.Bind(device.ResourceFactory, list);

            Matrix4x4[,,] mats = GetRubiksCubeMatrices();

            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    for (int z = 0; z < 3; z++)
                    {
                        resources.SetVector("BaseColor", GetCubeColor(x, y, z));
                        resources.SetMatrix("MVP", mats[x, y, z] * rot * view * FOV);

                        resources.UpdateBuffer(list, "_Globals");

                        Cube.Draw(list);
                    }
                }
            }

            list.End();

            device.SubmitCommands(list);
            device.SwapBuffers(device.MainSwapchain);
            device.WaitForIdle();
        }


        private static Vector4 GetCubeColor(int x, int y, int z)
        {
            // Determine the color based on the cube's position in the Rubik's Cube
            // Each cube can have up to 3 colors based on its location

            // Calculate a color value based on position
            // Using a simple hash function to create a pseudo-random color based on coordinates
            int hash = (x * 31 + y * 17 + z * 7) % 6;

            switch (hash)
            {
                default: return new Vector4(.9f, 0.1f, 0.2f, 1.0f);
                case 1: return new Vector4(0.0f, 1.0f, 0.0f, 1.0f);
                case 2: return new Vector4(0.0f, 0.25f, 1.0f, 1.0f);
                case 3: return new Vector4(1.0f, 1.0f, 0.0f, 1.0f);
                case 4: return new Vector4(0.0f, 1.0f, 1.0f, 1.0f);
                case 5: return new Vector4(.95f, .9f, .8f, 1.0f);
            }
        }
        

        private static Mesh Cube = Mesh.CreateCube(Vector3.One);
    }
}