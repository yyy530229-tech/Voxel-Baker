using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ModelConverter
{
    #region glTF 2.0 Data Structures

    [Serializable]
    public class GltfRoot
    {
        public GltfAsset asset;
        public int scene = 0;
        public List<GltfScene> scenes;
        public List<GltfNode> nodes;
        public List<GltfMesh> meshes;
        public List<GltfAccessor> accessors;
        public List<GltfBufferView> bufferViews;
        public List<GltfBuffer> buffers;
        public List<GltfMaterial> materials;
        public List<GltfTexture> textures;
        public List<GltfImage> images;
    }

    [Serializable] public class GltfAsset { public string version; public string generator; }
    [Serializable] public class GltfScene { public string name; public List<int> nodes; }

    [Serializable]
    public class GltfNode
    {
        public string name = "Node";
        public int mesh = -1;
        public List<int> children;
        public float[] translation; // [x, y, z]
        public float[] rotation;    // [x, y, z, w]
        public float[] scale;       // [x, y, z]
        public float[] matrix;      // 4x4 matrix
    }

    [Serializable]
    public class GltfMesh
    {
        public string name = "Mesh";
        public List<GltfPrimitive> primitives;
    }

    [Serializable]
    public class GltfPrimitive
    {
        public GltfAttributes attributes;
        public int indices = -1;
        public int material = -1;
        public int mode = 4; // 4 = TRIANGLES
    }

    [Serializable]
    public class GltfAttributes
    {
        public int POSITION = -1;
        public int NORMAL = -1;
        public int TEXCOORD_0 = -1;
        public int TEXCOORD_1 = -1;
        public int COLOR_0 = -1;
        public int JOINTS_0 = -1;
        public int WEIGHTS_0 = -1;
    }

    [Serializable]
    public class GltfAccessor
    {
        public int bufferView = -1;
        public int byteOffset = 0;
        public int componentType; // 5120(BYTE), 5121(UBYTE), 5122(SHORT), 5123(USHORT), 5125(UINT), 5126(FLOAT)
        public int count;
        public string type; // "SCALAR", "VEC2", "VEC3", "VEC4", "MAT4"
        public float[] min;
        public float[] max;
    }

    [Serializable]
    public class GltfBufferView
    {
        public int buffer = 0;
        public int byteOffset = 0;
        public int byteLength = 0;
        public int byteStride = 0;
        public int target = 0;
    }

    [Serializable]
    public class GltfBuffer
    {
        public int byteLength = 0;
        public string uri;
    }

    [Serializable]
    public class GltfMaterial
    {
        public string name = "Material";
        public GltfPbr pbrMetallicRoughness;
        public float[] emissiveFactor;
        public bool doubleSided = false;
    }

    [Serializable]
    public class GltfPbr
    {
        public float[] baseColorFactor = new float[] { 1f, 1f, 1f, 1f };
        public float metallicFactor = 0f;
        public float roughnessFactor = 0.5f;
        public GltfTextureInfo baseColorTexture;
    }

    [Serializable] public class GltfTextureInfo { public int index = -1; public int texCoord = 0; }
    [Serializable] public class GltfTexture { public int sampler = -1; public int source = -1; public string name; }
    [Serializable] public class GltfImage { public int bufferView = -1; public string mimeType; public string uri; public string name; }

    #endregion

    /// <summary>
    /// 纯 C# GLB (Binary glTF 2.0) 解析器
    /// 支持解析顶点位置、法线、UV、顶点色、三角形索引、子网格、PBR 材质及嵌入纹理图片！
    /// </summary>
    public static class GLBParser
    {
        private const uint GLB_MAGIC = 0x46546C67; // "glTF"
        private const uint CHUNK_TYPE_JSON = 0x4E4F534A; // "JSON"
        private const uint CHUNK_TYPE_BIN = 0x004E4942; // "BIN\0"

        public class ParsedModelData
        {
            public string modelName;
            public List<ParsedMeshData> meshes = new List<ParsedMeshData>();
            public List<ParsedMaterialData> materials = new List<ParsedMaterialData>();
            public List<ParsedTextureData> textures = new List<ParsedTextureData>();
            public List<GltfNode> nodes = new List<GltfNode>();
        }

        public class ParsedMeshData
        {
            public string meshName;
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector2[] uvs;
            public Color[] colors;
            public List<int[]> subMeshIndices = new List<int[]>();
            public List<int> subMeshMaterialIndices = new List<int>();
            public Mesh unityMesh;
        }

        public class ParsedMaterialData
        {
            public string materialName;
            public Color baseColor = Color.white;
            public float metallic = 0.0f;
            public float roughness = 0.5f;
            public Color emissionColor = Color.black;
            public int diffuseTextureIndex = -1;
        }

        public class ParsedTextureData
        {
            public string name;
            public byte[] rawImageData;
            public string mimeType; // "image/png", "image/jpeg"
        }

        public static ParsedModelData LoadGLB(byte[] glbBytes, string defaultName = "Model")
        {
            if (glbBytes == null || glbBytes.Length < 12)
            {
                throw new Exception("无效的 GLB 文件流：文件过短！");
            }

            using (MemoryStream ms = new MemoryStream(glbBytes))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                // 1. 读取 Header (12 字节)
                uint magic = reader.ReadUInt32();
                if (magic != GLB_MAGIC)
                {
                    throw new Exception($"无效的 GLB 魔数: 0x{magic:X8}, 期望为 0x{GLB_MAGIC:X8} (glTF)");
                }

                uint version = reader.ReadUInt32();
                uint totalLength = reader.ReadUInt32();

                string jsonContent = null;
                byte[] binaryData = null;

                // 2. 依次读取 Chunk
                while (ms.Position < totalLength && ms.Position < glbBytes.Length)
                {
                    uint chunkLength = reader.ReadUInt32();
                    uint chunkType = reader.ReadUInt32();

                    if (chunkType == CHUNK_TYPE_JSON)
                    {
                        byte[] jsonBytes = reader.ReadBytes((int)chunkLength);
                        jsonContent = Encoding.UTF8.GetString(jsonBytes);
                    }
                    else if (chunkType == CHUNK_TYPE_BIN)
                    {
                        binaryData = reader.ReadBytes((int)chunkLength);
                    }
                    else
                    {
                        // 忽略其它扩展 chunk
                        reader.ReadBytes((int)chunkLength);
                    }
                }

                if (string.IsNullOrEmpty(jsonContent))
                {
                    throw new Exception("GLB 文件中未找到 JSON 元数据 Chunk！");
                }

                GltfRoot root = JsonUtility.FromJson<GltfRoot>(jsonContent);
                if (root == null)
                {
                    throw new Exception("GLB JSON 反序列化失败！");
                }

                return ExtractModelData(root, binaryData, defaultName);
            }
        }

        private static ParsedModelData ExtractModelData(GltfRoot root, byte[] binData, string defaultName)
        {
            ParsedModelData model = new ParsedModelData { modelName = defaultName };

            // 1. 提取材质与贴图
            if (root.materials != null)
            {
                for (int i = 0; i < root.materials.Count; i++)
                {
                    var gm = root.materials[i];
                    ParsedMaterialData pm = new ParsedMaterialData
                    {
                        materialName = string.IsNullOrEmpty(gm.name) ? $"Material_{i}" : gm.name
                    };

                    if (gm.pbrMetallicRoughness != null)
                    {
                        var pbr = gm.pbrMetallicRoughness;
                        if (pbr.baseColorFactor != null && pbr.baseColorFactor.Length >= 3)
                        {
                            float a = pbr.baseColorFactor.Length > 3 ? pbr.baseColorFactor[3] : 1f;
                            pm.baseColor = new Color(pbr.baseColorFactor[0], pbr.baseColorFactor[1], pbr.baseColorFactor[2], a);
                        }
                        pm.metallic = pbr.metallicFactor;
                        pm.roughness = pbr.roughnessFactor;
                        if (pbr.baseColorTexture != null)
                        {
                            pm.diffuseTextureIndex = pbr.baseColorTexture.index;
                        }
                    }

                    if (gm.emissiveFactor != null && gm.emissiveFactor.Length >= 3)
                    {
                        pm.emissionColor = new Color(gm.emissiveFactor[0], gm.emissiveFactor[1], gm.emissiveFactor[2], 1f);
                    }

                    model.materials.Add(pm);
                }
            }

            // 2. 提取嵌入图像
            if (root.images != null && binData != null)
            {
                for (int i = 0; i < root.images.Count; i++)
                {
                    var img = root.images[i];
                    if (img.bufferView >= 0 && img.bufferView < root.bufferViews.Count)
                    {
                        var bv = root.bufferViews[img.bufferView];
                        byte[] imgBytes = new byte[bv.byteLength];
                        Array.Copy(binData, bv.byteOffset, imgBytes, 0, bv.byteLength);

                        model.textures.Add(new ParsedTextureData
                        {
                            name = string.IsNullOrEmpty(img.name) ? $"Texture_{i}" : img.name,
                            rawImageData = imgBytes,
                            mimeType = img.mimeType
                        });
                    }
                }
            }

            // 3. 提取网格与顶点几何体 (Meshes & Primitives)
            if (root.meshes != null && binData != null)
            {
                for (int m = 0; m < root.meshes.Count; m++)
                {
                    var gMesh = root.meshes[m];
                    ParsedMeshData pMesh = new ParsedMeshData
                    {
                        meshName = string.IsNullOrEmpty(gMesh.name) ? $"{defaultName}_Mesh_{m}" : gMesh.name
                    };

                    List<Vector3> allVertices = new List<Vector3>();
                    List<Vector3> allNormals = new List<Vector3>();
                    List<Vector2> allUVs = new List<Vector2>();
                    List<Color> allColors = new List<Color>();

                    if (gMesh.primitives != null)
                    {
                        for (int p = 0; p < gMesh.primitives.Count; p++)
                        {
                            var prim = gMesh.primitives[p];
                            int vertBaseOffset = allVertices.Count;

                            // POSITION
                            Vector3[] pos = ReadAccessorAsVector3(root, binData, prim.attributes.POSITION);
                            // glTF (Right-handed, Y-up) -> Unity (Left-handed, Y-up): 翻转 X 轴
                            if (pos != null)
                            {
                                for (int i = 0; i < pos.Length; i++)
                                {
                                    pos[i] = new Vector3(-pos[i].x, pos[i].y, pos[i].z);
                                }
                                allVertices.AddRange(pos);
                            }

                            // NORMAL
                            Vector3[] norms = ReadAccessorAsVector3(root, binData, prim.attributes.NORMAL);
                            if (norms != null)
                            {
                                for (int i = 0; i < norms.Length; i++)
                                {
                                    norms[i] = new Vector3(-norms[i].x, norms[i].y, norms[i].z);
                                }
                                allNormals.AddRange(norms);
                            }
                            else if (pos != null)
                            {
                                for (int i = 0; i < pos.Length; i++) allNormals.Add(Vector3.up);
                            }

                            // UV (TEXCOORD_0)
                            Vector2[] uvs = ReadAccessorAsVector2(root, binData, prim.attributes.TEXCOORD_0);
                            if (uvs != null)
                            {
                                for (int i = 0; i < uvs.Length; i++)
                                {
                                    uvs[i] = new Vector2(uvs[i].x, 1f - uvs[i].y); // glTF UV 坐标系 Y 轴翻转
                                }
                                allUVs.AddRange(uvs);
                            }
                            else if (pos != null)
                            {
                                for (int i = 0; i < pos.Length; i++) allUVs.Add(Vector2.zero);
                            }

                            // COLOR_0
                            Color[] colors = ReadAccessorAsColor(root, binData, prim.attributes.COLOR_0);
                            if (colors != null)
                            {
                                allColors.AddRange(colors);
                            }
                            else if (pos != null)
                            {
                                for (int i = 0; i < pos.Length; i++) allColors.Add(Color.white);
                            }

                            // INDICES (Triangles)
                            int[] indices = ReadAccessorAsIndices(root, binData, prim.indices, pos != null ? pos.Length : 0);
                            if (indices != null)
                            {
                                int[] remappedIndices = new int[indices.Length];
                                for (int i = 0; i < indices.Length; i += 3)
                                {
                                    // 坐标系翻转后需调换三角形面片顶点绕序 (Winding Order)
                                    remappedIndices[i] = vertBaseOffset + indices[i];
                                    remappedIndices[i + 1] = vertBaseOffset + indices[i + 2];
                                    remappedIndices[i + 2] = vertBaseOffset + indices[i + 1];
                                }
                                pMesh.subMeshIndices.Add(remappedIndices);
                                pMesh.subMeshMaterialIndices.Add(prim.material);
                            }
                        }
                    }

                    pMesh.vertices = allVertices.ToArray();
                    pMesh.normals = allNormals.ToArray();
                    pMesh.uvs = allUVs.ToArray();
                    pMesh.colors = allColors.ToArray();

                    // 构建 Unity 运行态 Mesh
                    Mesh uMesh = new Mesh { name = pMesh.meshName };
                    if (pMesh.vertices.Length > 65535)
                    {
                        uMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    }
                    uMesh.vertices = pMesh.vertices;
                    if (pMesh.normals.Length == pMesh.vertices.Length) uMesh.normals = pMesh.normals;
                    if (pMesh.uvs.Length == pMesh.vertices.Length) uMesh.uv = pMesh.uvs;
                    if (pMesh.colors.Length == pMesh.vertices.Length) uMesh.colors = pMesh.colors;

                    uMesh.subMeshCount = pMesh.subMeshIndices.Count;
                    for (int s = 0; s < pMesh.subMeshIndices.Count; s++)
                    {
                        uMesh.SetTriangles(pMesh.subMeshIndices[s], s);
                    }

                    if (pMesh.normals.Length == 0) uMesh.RecalculateNormals();
                    uMesh.RecalculateBounds();
                    pMesh.unityMesh = uMesh;

                    model.meshes.Add(pMesh);
                }
            }

            if (root.nodes != null)
            {
                model.nodes = root.nodes;
            }

            return model;
        }

        #region Buffer Reading Helpers

        private static Vector3[] ReadAccessorAsVector3(GltfRoot root, byte[] binData, int accessorIndex)
        {
            if (accessorIndex < 0 || accessorIndex >= root.accessors.Count) return null;
            var acc = root.accessors[accessorIndex];
            if (acc.bufferView < 0 || acc.bufferView >= root.bufferViews.Count) return null;
            var bv = root.bufferViews[acc.bufferView];

            Vector3[] res = new Vector3[acc.count];
            int start = bv.byteOffset + acc.byteOffset;
            int stride = bv.byteStride > 0 ? bv.byteStride : 12; // 3 * float(4)

            for (int i = 0; i < acc.count; i++)
            {
                int offset = start + i * stride;
                float x = BitConverter.ToSingle(binData, offset);
                float y = BitConverter.ToSingle(binData, offset + 4);
                float z = BitConverter.ToSingle(binData, offset + 8);
                res[i] = new Vector3(x, y, z);
            }
            return res;
        }

        private static Vector2[] ReadAccessorAsVector2(GltfRoot root, byte[] binData, int accessorIndex)
        {
            if (accessorIndex < 0 || accessorIndex >= root.accessors.Count) return null;
            var acc = root.accessors[accessorIndex];
            if (acc.bufferView < 0 || acc.bufferView >= root.bufferViews.Count) return null;
            var bv = root.bufferViews[acc.bufferView];

            Vector2[] res = new Vector2[acc.count];
            int start = bv.byteOffset + acc.byteOffset;
            int stride = bv.byteStride > 0 ? bv.byteStride : 8; // 2 * float(4)

            for (int i = 0; i < acc.count; i++)
            {
                int offset = start + i * stride;
                float x = BitConverter.ToSingle(binData, offset);
                float y = BitConverter.ToSingle(binData, offset + 4);
                res[i] = new Vector2(x, y);
            }
            return res;
        }

        private static Color[] ReadAccessorAsColor(GltfRoot root, byte[] binData, int accessorIndex)
        {
            if (accessorIndex < 0 || accessorIndex >= root.accessors.Count) return null;
            var acc = root.accessors[accessorIndex];
            if (acc.bufferView < 0 || acc.bufferView >= root.bufferViews.Count) return null;
            var bv = root.bufferViews[acc.bufferView];

            Color[] res = new Color[acc.count];
            int start = bv.byteOffset + acc.byteOffset;

            if (acc.componentType == 5126) // FLOAT
            {
                int stride = bv.byteStride > 0 ? bv.byteStride : (acc.type == "VEC3" ? 12 : 16);
                for (int i = 0; i < acc.count; i++)
                {
                    int offset = start + i * stride;
                    float r = BitConverter.ToSingle(binData, offset);
                    float g = BitConverter.ToSingle(binData, offset + 4);
                    float b = BitConverter.ToSingle(binData, offset + 8);
                    float a = (acc.type == "VEC4") ? BitConverter.ToSingle(binData, offset + 12) : 1f;
                    res[i] = new Color(r, g, b, a);
                }
            }
            else if (acc.componentType == 5121) // UNSIGNED_BYTE
            {
                int stride = bv.byteStride > 0 ? bv.byteStride : (acc.type == "VEC3" ? 3 : 4);
                for (int i = 0; i < acc.count; i++)
                {
                    int offset = start + i * stride;
                    float r = binData[offset] / 255f;
                    float g = binData[offset + 1] / 255f;
                    float b = binData[offset + 2] / 255f;
                    float a = (acc.type == "VEC4") ? (binData[offset + 3] / 255f) : 1f;
                    res[i] = new Color(r, g, b, a);
                }
            }
            return res;
        }

        private static int[] ReadAccessorAsIndices(GltfRoot root, byte[] binData, int accessorIndex, int vertexCount)
        {
            if (accessorIndex < 0)
            {
                // 无索引缓冲区，自动生成连续拓扑
                int[] autoIndices = new int[vertexCount];
                for (int i = 0; i < vertexCount; i++) autoIndices[i] = i;
                return autoIndices;
            }

            if (accessorIndex >= root.accessors.Count) return null;
            var acc = root.accessors[accessorIndex];
            if (acc.bufferView < 0 || acc.bufferView >= root.bufferViews.Count) return null;
            var bv = root.bufferViews[acc.bufferView];

            int[] res = new int[acc.count];
            int start = bv.byteOffset + acc.byteOffset;

            if (acc.componentType == 5123) // UNSIGNED_SHORT
            {
                for (int i = 0; i < acc.count; i++)
                {
                    res[i] = BitConverter.ToUInt16(binData, start + i * 2);
                }
            }
            else if (acc.componentType == 5125) // UNSIGNED_INT
            {
                for (int i = 0; i < acc.count; i++)
                {
                    res[i] = (int)BitConverter.ToUInt32(binData, start + i * 4);
                }
            }
            else if (acc.componentType == 5121) // UNSIGNED_BYTE
            {
                for (int i = 0; i < acc.count; i++)
                {
                    res[i] = binData[start + i];
                }
            }
            return res;
        }

        #endregion
    }
}
