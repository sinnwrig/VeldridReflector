using System.Numerics;

using Veldrid;
using Veldrid.StartupUtilities;
using Veldrid.Sdl2;

#pragma warning disable

namespace Application
{
    public static class Test
    {
        private static Sdl2Window _window;
        private static GraphicsDevice _device;

        private static DeviceBuffer _vertexBuffer;
        private static DeviceBuffer _indexBuffer;

        private static BindableResources _resources;
        private static ResourceSet _resourceSet;

        private static Texture _surfaceTexture;
        private static TextureView _surfaceTextureView;
        
        private static CommandList _cl;
        private static Pipeline _pipeline;
        

        private static float _ticks;



        public static void Main()
        {
            WindowCreateInfo ci = new();
            ci.X = 1920 / 2;
            ci.Y = 1080 / 2;
            ci.WindowWidth = 1920 / 4;
            ci.WindowHeight = 1080 / 4;

            GraphicsDeviceOptions opt = new();
            opt.HasMainSwapchain = true;
            opt.SyncToVerticalBlank = true;
            opt.SwapchainDepthFormat = PixelFormat.R16_UNorm;

            _window = VeldridStartup.CreateWindow(ci);
            _device = VeldridStartup.CreateGraphicsDevice(_window, opt, GraphicsBackend.Vulkan);

            CreateResources(_device.ResourceFactory);

            System.Diagnostics.Stopwatch sw = new();
            sw.Start(); 

            _window.Resized += () => _device.ResizeMainWindow((uint)_window.Width, (uint)_window.Height);

            while (_window.Exists)
            {
                _window.PumpEvents();

                float dt = (float)sw.Elapsed.TotalSeconds;

                sw.Restart();

                Draw(dt);
            }
        }


        static uint v4Size = sizeof(float) * 4;


        static void CreateResources(ResourceFactory factory)
        {
            _vertexBuffer = factory.CreateBuffer(new BufferDescription((uint)CubeVertices.Length * (v4Size * 2), BufferUsage.VertexBuffer));

            _device.UpdateBuffer(_vertexBuffer, 0, CubeVertices);
            _device.UpdateBuffer(_vertexBuffer, (uint)CubeVertices.Length * v4Size, CubeUVs);

            _indexBuffer = factory.CreateBuffer(new BufferDescription(sizeof(ushort) * (uint)Indices.Length, BufferUsage.IndexBuffer));
            _device.UpdateBuffer(_indexBuffer, 0, Indices);

            _surfaceTexture = TextureUtils.Create2D<Color>(TextureData, _device, TextureUsage.Sampled);

            _surfaceTextureView = factory.CreateTextureView(_surfaceTexture);

            var result = ShaderCompiler.Compile(ShaderCode.sourceCode, [ ("vert", ShaderStages.Vertex), ("frag", ShaderStages.Fragment) ], _device.BackendType);
            
            var reflectedDescrition  = ShaderReflector.Reflect(_device, result);
            
            _resources = new BindableResources(reflectedDescrition, _device);

            _resources.SetTexture(null, "SurfaceTexture", _surfaceTexture);
            _resources.SetSampler(null, "SurfaceSampler", _device.PointSampler);

            _resourceSet = _resources.CreateResourceSet(_device);

            GraphicsPipelineDescription description = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.DepthOnlyLessEqual,
                RasterizerStateDescription.Default,
                PrimitiveTopology.TriangleList,
                reflectedDescrition.shaderSet,
                reflectedDescrition.layout,
                _device.MainSwapchain.Framebuffer.OutputDescription
            );

            _pipeline = factory.CreateGraphicsPipeline(description);
            _cl = factory.CreateCommandList();
        }

        static void Draw(float deltaSeconds)
        {
            _ticks += deltaSeconds * 1000f;
            _cl.Begin();

            Matrix4x4 FOV = Matrix4x4.CreatePerspectiveFieldOfView(
                1.0f,
                (float)_window.Width / _window.Height,
                0.5f,
                100f);

            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 2, 3), Vector3.Zero, Vector3.UnitY);
            Matrix4x4 rot = Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, _ticks / 1000f);

            _resources.SetMatrix(_cl, "MVP", rot * view * FOV);
            _resources.SetVector(_cl, "BaseColor", new Vector4(1.0f, 1.0f, 1.0f, 1.0f));

            _cl.SetFramebuffer(_device.MainSwapchain.Framebuffer);
            _cl.ClearColorTarget(0, RgbaFloat.CornflowerBlue);
            _cl.ClearDepthStencil(1f);
            _cl.SetPipeline(_pipeline);
            
            int posLocation = _resources.description.GetInputLocation(new StageInput("POSITION", 0));

            if (posLocation >= 0)
                _cl.SetVertexBuffer((uint)posLocation, _vertexBuffer, 0);

            int uvLocation = _resources.description.GetInputLocation(new StageInput("TEXCOORD", 0));

            if (uvLocation >= 0)
                _cl.SetVertexBuffer((uint)uvLocation, _vertexBuffer, (uint)CubeVertices.Length * v4Size);

            _cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            _cl.SetGraphicsResourceSet(0, _resourceSet);
            _cl.DrawIndexed(36, 1, 0, 0, 0);
            _cl.End();

            _device.SubmitCommands(_cl);
            _device.SwapBuffers(_device.MainSwapchain);
            _device.WaitForIdle();
        }


        private static Color[,] TextureData = new Color[2, 2]
        {
            { Color.White, Color.Black },  
            { Color.Black, Color.White },
        };

        private static Vector4[] CubeVertices = 
        [
            // Top
            new Vector4(-0.5f, +0.5f, -0.5f, 0.0f), 
            new Vector4(+0.5f, +0.5f, -0.5f, 0.0f), 
            new Vector4(+0.5f, +0.5f, +0.5f, 0.0f), 
            new Vector4(-0.5f, +0.5f, +0.5f, 0.0f), 

            // Bottom                                                             
            new Vector4(-0.5f, -0.5f, +0.5f, 0.0f),
            new Vector4(+0.5f, -0.5f, +0.5f, 0.0f),
            new Vector4(+0.5f, -0.5f, -0.5f, 0.0f),
            new Vector4(-0.5f, -0.5f, -0.5f, 0.0f),
                
            // Left                                                               
            new Vector4(-0.5f, +0.5f, -0.5f, 0.0f),
            new Vector4(-0.5f, +0.5f, +0.5f, 0.0f),
            new Vector4(-0.5f, -0.5f, +0.5f, 0.0f),
            new Vector4(-0.5f, -0.5f, -0.5f, 0.0f),
                
            // Right                                                              
            new Vector4(+0.5f, +0.5f, +0.5f, 0.0f),
            new Vector4(+0.5f, +0.5f, -0.5f, 0.0f),
            new Vector4(+0.5f, -0.5f, -0.5f, 0.0f),
            new Vector4(+0.5f, -0.5f, +0.5f, 0.0f),
                
            // Back                                                               
            new Vector4(+0.5f, +0.5f, -0.5f, 0.0f),
            new Vector4(-0.5f, +0.5f, -0.5f, 0.0f),
            new Vector4(-0.5f, -0.5f, -0.5f, 0.0f),
            new Vector4(+0.5f, -0.5f, -0.5f, 0.0f),
                
            // Front                                                              
            new Vector4(-0.5f, +0.5f, +0.5f, 0.0f),
            new Vector4(+0.5f, +0.5f, +0.5f, 0.0f),
            new Vector4(+0.5f, -0.5f, +0.5f, 0.0f),
            new Vector4(-0.5f, -0.5f, +0.5f, 0.0f),
        ];

        private static Vector4[] CubeUVs =
        [
            // Top
            new Vector4(0, 0, 0, 0),
            new Vector4(1, 0, 0, 0),
            new Vector4(1, 1, 0, 0),
            new Vector4(0, 1, 0, 0),

            // Bottom                                                             
            new Vector4(0, 0, 0, 0),
            new Vector4(1, 0, 0, 0),
            new Vector4(1, 1, 0, 0),
            new Vector4(0, 1, 0, 0),
                
            // Left                                                               
            new Vector4(0, 0, 0, 0),
            new Vector4(1, 0, 0, 0),
            new Vector4(1, 1, 0, 0),
            new Vector4(0, 1, 0, 0),
                
            // Right                                                              
            new Vector4(0, 0, 0, 0),
            new Vector4(1, 0, 0, 0),
            new Vector4(1, 1, 0, 0),
            new Vector4(0, 1, 0, 0),
                
            // Back                                                               
            new Vector4(0, 0, 0, 0),
            new Vector4(1, 0, 0, 0),
            new Vector4(1, 1, 0, 0),
            new Vector4(0, 1, 0, 0),
                
            // Front                                                              
            new Vector4(0, 0, 0, 0),
            new Vector4(1, 0, 0, 0),
            new Vector4(1, 1, 0, 0),
            new Vector4(0, 1, 0, 0),
        ];

        private static ushort[] Indices = 
        [
            0,1,2, 0,2,3,
            4,5,6, 4,6,7,
            8,9,10, 8,10,11,
            12,13,14, 12,14,15,
            16,17,18, 16,18,19,
            20,21,22, 20,22,23,
        ];
    }
}