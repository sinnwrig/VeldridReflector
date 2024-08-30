using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Veldrid;

namespace Application
{
    public class Mesh
    {
        public Vector4[] vertices = [];
        public Vector4[] uv = [];
        public Vector4[] normals = [];
        public RgbaByte[] colors = [];
        public ushort[] indices = [];

        public bool HasUV => uv.Length > 0;
        public bool HasNormals => normals.Length > 0;
        public bool HasColors => colors.Length > 0;

        public IndexFormat IndexFormat => IndexFormat.UInt16;

        public PrimitiveTopology meshTopology = PrimitiveTopology.TriangleList;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.

        DeviceBuffer vertexBuffer;
        DeviceBuffer indexBuffer;

#pragma warning restore CS8618

        private uint uvStart;
        private uint normalsStart;
        private uint colorsStart;
        private uint bufferLength;

        private bool uploaded;


        private void GetResourceOffsets()
        {
            uint byte4Size = sizeof(byte) * 4;
            uint vec4Size = sizeof(float) * 4;

            uint vertLen = (uint)vertices.Length;

            uvStart = vertLen * vec4Size; // Where vertices end
            normalsStart = uvStart + (HasUV ? vertLen * vec4Size : 0); // Where uvs ends
            colorsStart = normalsStart + (HasNormals ? vertLen * vec4Size : 0); // Where normals end
            bufferLength = colorsStart + (HasColors ? vertLen * byte4Size : 0); // Where colors end
        }

        public void Upload(GraphicsDevice device)
        {
            if (uploaded == true)
                return;

            uploaded = true;

            if (vertices == null || vertices.Length == 0)
                throw new InvalidOperationException($"Mesh has no vertices");

            if (indices == null || indices.Length == 0)
                throw new InvalidOperationException($"Mesh has no indices");


            int indexLength = indices.Length;

            switch (meshTopology)
            {
                case PrimitiveTopology.TriangleList:
                    if (indexLength % 3 != 0)
                        throw new InvalidOperationException($"Triangle List mesh doesn't have the right amount of indices. Has: {indexLength}. Should be a multiple of 3");
                    break;
                case PrimitiveTopology.TriangleStrip:
                    if (indexLength < 3)
                        throw new InvalidOperationException($"Triangle Strip mesh doesn't have the right amount of indices. Has: {indexLength}. Should have at least 3");
                    break;

                case PrimitiveTopology.LineList:
                    if (indexLength % 2 != 0)
                        throw new InvalidOperationException($"Line List mesh doesn't have the right amount of indices. Has: {indexLength}. Should be a multiple of 2");
                    break;

                case PrimitiveTopology.LineStrip:
                    if (indexLength < 2)
                        throw new InvalidOperationException($"Line Strip mesh doesn't have the right amount of indices. Has: {indexLength}. Should have at least 2");
                    break;
            }

            indexBuffer = device.ResourceFactory.CreateBuffer(new BufferDescription((uint)indices.Length * sizeof(ushort), BufferUsage.IndexBuffer));

            device.UpdateBuffer(indexBuffer, 0, indices);

            GetResourceOffsets();

            // Vertex buffer upload
            vertexBuffer = device.ResourceFactory.CreateBuffer(new BufferDescription((uint)bufferLength, BufferUsage.VertexBuffer));

            device.UpdateBuffer(vertexBuffer, 0, vertices);

            if (HasUV)
                device.UpdateBuffer(vertexBuffer, uvStart, uv);

            if (HasNormals)
                device.UpdateBuffer(vertexBuffer, normalsStart, normals);

            if (HasColors)
                device.UpdateBuffer(vertexBuffer, colorsStart, colors);
        }

        public void SetDrawData(GraphicsDevice device, CommandList commandList, BindableShader shader)
        {
            Upload(device);

            commandList.SetIndexBuffer(indexBuffer, IndexFormat);

            shader.BindVertexBuffer(commandList, "POSITION0", vertexBuffer, 0);
            shader.BindVertexBuffer(commandList, "TEXCOORD0", vertexBuffer, uvStart);
            shader.BindVertexBuffer(commandList, "NORMAL0", vertexBuffer, normalsStart);
            shader.BindVertexBuffer(commandList, "COLOR0", vertexBuffer, colorsStart);
        }

        public void Draw(CommandList commandList)
        {
            commandList.DrawIndexed((uint)indices.Length, 1, 0, 0, 0);
        }

        private static Mesh? fullscreenQuad;
        public static Mesh GetFullscreenQuad()
        {
            if (fullscreenQuad != null)
                return fullscreenQuad;

            fullscreenQuad = new()
            {
                vertices = [
                    new Vector4(-1, -1, 0, 0),
                    new Vector4(1, -1, 0, 0),
                    new Vector4(-1, 1, 0, 0),
                    new Vector4(1, 1, 0, 0),
                ],

                uv = [
                    new Vector4(0, 0, 0, 0),
                    new Vector4(1, 0, 0, 0),
                    new Vector4(0, 1, 0, 0),
                    new Vector4(1, 1, 0, 0),
                ],

                indices = [0, 2, 1, 2, 3, 1]
            };

            return fullscreenQuad;
        }

        public static Mesh CreateCube(Vector3 size)
        {
            Mesh mesh = new Mesh();
            float x = size.X / 2f;
            float y = size.Y / 2f;
            float z = size.Z / 2f;

            Vector4[] vertices =
            {
                // Front face
                new(-x, -y, z, 0),
                new( x, -y, z, 0),
                new( x,  y, z, 0),
                new(-x,  y, z, 0),
                
                // Back face
                new(-x, -y, -z, 0),
                new( x, -y, -z, 0),
                new( x,  y, -z, 0),
                new(-x,  y, -z, 0),
                
                // Left face
                new(-x, -y, -z, 0),
                new(-x,  y, -z, 0),
                new(-x,  y,  z, 0),
                new(-x, -y,  z, 0),
                
                // Right face
                new(x, -y,  z, 0),
                new(x,  y,  z, 0),
                new(x,  y, -z, 0),
                new(x, -y, -z, 0),
                
                // Top face
                new(-x, y,  z, 0),
                new( x, y,  z, 0),
                new( x, y, -z, 0),
                new(-x, y, -z, 0),
                
                // Bottom face
                new(-x, -y, -z, 0),
                new( x, -y, -z, 0),
                new( x, -y,  z, 0),
                new(-x, -y,  z, 0)
            };

            Vector4[] uvs =
            {
                // Front face
                new(0, 0, 0, 0),
                new(1, 0, 0, 0),
                new(1, 1, 0, 0),
                new(0, 1, 0, 0),

                // Back face
                new(1, 0, 0, 0),
                new(0, 0, 0, 0),
                new(0, 1, 0, 0),
                new(1, 1, 0, 0),

                // Left face
                new(0, 0, 0, 0),
                new(1, 0, 0, 0),
                new(1, 1, 0, 0),
                new(0, 1, 0, 0),

                // Right face
                new(1, 0, 0, 0),
                new(1, 1, 0, 0),
                new(0, 1, 0, 0),
                new(0, 0, 0, 0),

                // Top face
                new(0, 1, 0, 0),
                new(1, 1, 0, 0),
                new(1, 0, 0, 0),
                new(0, 0, 0, 0),

                // Bottom face
                new(0, 0, 0, 0),
                new(1, 0, 0, 0),
                new(1, 1, 0, 0),
                new(0, 1, 0, 0)
            };

            ushort[] indices =
            {
                1, 0, 2, 0, 3, 2,       // Front face
                5, 6, 4, 6, 7, 4,       // Back face
                9, 10, 8, 10, 11, 8,    // Left face
                13, 14, 12, 14, 15, 12, // Right face
                17, 16, 18, 16, 19, 18, // Top face
                21, 20, 22, 20, 23, 22  // Bottom face
            };

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.indices = indices;

            return mesh;
        }

        public static Mesh CreateCylinder(float radius, float length, int sliceCount)
        {
            Mesh mesh = new Mesh();

            List<Vector4> vertices = new List<Vector4>();
            List<Vector4> uvs = new List<Vector4>();
            List<ushort> indices = new List<ushort>();

            float halfLength = length / 2.0f;

            // Create the vertices and UVs for the top and bottom circles
            for (int i = 0; i <= sliceCount; i++)
            {
                float angle = 2 * MathF.PI * i / sliceCount;
                float x = radius * MathF.Cos(angle);
                float z = radius * MathF.Sin(angle);

                // Top circle
                vertices.Add(new Vector4(x, halfLength, z, 0));
                uvs.Add(new Vector4((float)i / sliceCount, 1, 0, 0));

                // Bottom circle
                vertices.Add(new Vector4(x, -halfLength, z, 0));
                uvs.Add(new Vector4((float)i / sliceCount, 0, 0, 0));
            }

            // Add the center vertices for the top and bottom circles
            vertices.Add(new Vector4(0, halfLength, 0, 0));
            uvs.Add(new Vector4(0.5f, 1, 0, 0));
            vertices.Add(new Vector4(0, -halfLength, 0, 0));
            uvs.Add(new Vector4(0.5f, 0, 0, 0));

            int topCenterIndex = vertices.Count - 2;
            int bottomCenterIndex = vertices.Count - 1;

            // Create the indices for the sides of the cylinder
            for (int i = 0; i < sliceCount; i++)
            {
                int top1 = i * 2;
                int top2 = top1 + 2;
                int bottom1 = top1 + 1;
                int bottom2 = top2 + 1;

                if (i == sliceCount - 1)
                {
                    top2 = 0;
                    bottom2 = 1;
                }

                indices.Add((ushort)top1);
                indices.Add((ushort)bottom1);
                indices.Add((ushort)top2);

                indices.Add((ushort)bottom1);
                indices.Add((ushort)bottom2);
                indices.Add((ushort)top2);
            }

            // Create the indices for the top and bottom circles
            for (int i = 0; i < sliceCount; i++)
            {
                int top1 = i * 2;
                int top2 = (i == sliceCount - 1) ? 0 : top1 + 2;
                int bottom1 = top1 + 1;
                int bottom2 = (i == sliceCount - 1) ? 1 : bottom1 + 2;

                // Top circle
                indices.Add((ushort)top1);
                indices.Add((ushort)top2);
                indices.Add((ushort)topCenterIndex);

                // Bottom circle
                indices.Add((ushort)bottom2);
                indices.Add((ushort)bottom1);
                indices.Add((ushort)bottomCenterIndex);
            }

            mesh.vertices = vertices.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.indices = indices.ToArray();

            return mesh;
        }

        public static Mesh CreateTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            Mesh mesh = new()
            {
                vertices = [new(a, 0), new(b, 0), new(c, 0)],
                indices = [0, 1, 2]
            };

            return mesh;
        }
    }
}