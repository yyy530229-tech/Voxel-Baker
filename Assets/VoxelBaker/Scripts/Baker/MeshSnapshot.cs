using System.Collections.Generic;
using UnityEngine;

namespace VoxelBaker.Baker
{
    /// <summary>
    /// 贴图的 CPU 像素快照。
    ///
    /// 存在的唯一理由：Texture2D.GetPixelBilinear 只能在主线程调用，
    /// 后台线程一碰就会抛 "get_pixel can only be called from the main thread"。
    /// 这里把像素提前拷成普通 Color32[]，采样逻辑用纯 C# 重写，
    /// 于是整条体素化管线得以脱离 UnityEngine 对象、在 ThreadPool 上自由运行。
    /// </summary>
    public sealed class TextureSnapshot
    {
        public readonly int Width;
        public readonly int Height;
        public readonly Color32[] Pixels;

        public TextureSnapshot(int width, int height, Color32[] pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public bool IsValid => Pixels != null && Pixels.Length > 0 && Width > 0 && Height > 0;

        public int ByteSize => Pixels != null ? Pixels.Length * 4 : 0;

        private static int Wrap(int v, int size)
        {
            v %= size;
            return v < 0 ? v + size : v;
        }

        /// <summary>
        /// 与 Texture2D.GetPixelBilinear + Repeat 环绕模式等价的双线性采样。
        /// 纹素中心对齐 (u*width - 0.5)，和 Unity 原生实现保持一致，
        /// 否则预览颜色和最终烘焙结果会有半像素偏移。
        /// </summary>
        public Color SampleBilinear(float u, float v)
        {
            if (!IsValid) return Color.white;

            float fx = u * Width - 0.5f;
            float fy = v * Height - 0.5f;

            int x0 = Mathf.FloorToInt(fx);
            int y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0;
            float ty = fy - y0;

            int xa = Wrap(x0, Width);
            int xb = Wrap(x0 + 1, Width);
            int ya = Wrap(y0, Height);
            int yb = Wrap(y0 + 1, Height);

            Color c00 = Pixels[ya * Width + xa];
            Color c10 = Pixels[ya * Width + xb];
            Color c01 = Pixels[yb * Width + xa];
            Color c11 = Pixels[yb * Width + xb];

            Color top = Color.LerpUnclamped(c00, c10, tx);
            Color bottom = Color.LerpUnclamped(c01, c11, tx);
            return Color.LerpUnclamped(top, bottom, ty);
        }
    }

    /// <summary>
    /// 单个子网格的冻结数据：三角形索引 + 该子网格的材质基色 + 贴图快照。
    /// </summary>
    public sealed class SubMeshSnapshot
    {
        public int[] Triangles = System.Array.Empty<int>();
        public Color BaseColor = Color.white;
        public TextureSnapshot Texture;

        public bool HasTexture => Texture != null && Texture.IsValid;
    }

    /// <summary>
    /// Mesh + 材质在主线程上的一次性冻结快照。
    ///
    /// Capture() 必须在主线程调用（内部会读 Mesh 属性、Material 颜色、RenderTexture）。
    /// 一旦 Capture 完成，得到的这个对象就是纯数据，可以放心丢给任意后台线程。
    /// 这是"预览不卡编辑器"整个方案的地基。
    /// </summary>
    public sealed class MeshSnapshot
    {
        public Vector3[] Vertices = System.Array.Empty<Vector3>();
        public Vector2[] UVs = System.Array.Empty<Vector2>();
        public Color32[] Colors = System.Array.Empty<Color32>();
        public Vector3[] Normals = System.Array.Empty<Vector3>();
        public SubMeshSnapshot[] SubMeshes = System.Array.Empty<SubMeshSnapshot>();
        public Bounds Bounds;

        public bool HasUV { get; private set; }
        public bool HasColor { get; private set; }
        public bool HasNormal { get; private set; }

        public int VertexCount => Vertices != null ? Vertices.Length : 0;

        //
        // 所有子网格三角形索引的扁平化视图。
        // VoxelBudgetSolver 做面积/体积积分时只关心三角形集合本身，不关心子网格归属，
        // 所以这里惰性拼一张总表，避免每个求解器各写一遍嵌套循环。
        //
        private int[] _allTriangles;

        public int[] AllTriangles
        {
            get
            {
                if (_allTriangles != null) return _allTriangles;

                int total = 0;
                for (int i = 0; i < SubMeshes.Length; i++)
                    total += SubMeshes[i].Triangles != null ? SubMeshes[i].Triangles.Length : 0;

                int[] merged = new int[total];
                int offset = 0;
                for (int i = 0; i < SubMeshes.Length; i++)
                {
                    int[] tris = SubMeshes[i].Triangles;
                    if (tris == null || tris.Length == 0) continue;
                    System.Array.Copy(tris, 0, merged, offset, tris.Length);
                    offset += tris.Length;
                }

                _allTriangles = merged;
                return merged;
            }
        }

        //
        // 贴图快照缓存
        //
        // 4096×4096 的贴图读一次要 60MB+ 和几百毫秒，
        // 但同一张贴图在整个预览会话里内容是不变的，
        // 所以按 instanceID 缓存，只有第一次取像素有开销，之后零成本。
        //
        private static readonly Dictionary<int, TextureSnapshot> TextureCache =
            new Dictionary<int, TextureSnapshot>();

        private const long TextureCacheByteBudget = 96L * 1024 * 1024; // 96MB

        public static void ClearTextureCache()
        {
            TextureCache.Clear();
        }

        public static int CachedTextureCount => TextureCache.Count;

        /// <summary>主线程调用：把 Mesh 与材质冻结为纯数据快照。</summary>
        public static MeshSnapshot Capture(Mesh mesh, Material[] materials)
        {
            if (mesh == null) return null;

            MeshSnapshot snap = new MeshSnapshot();

            snap.Vertices = mesh.vertices;
            snap.UVs = mesh.uv;
            snap.Colors = mesh.colors32;
            snap.Normals = mesh.normals;
            snap.Bounds = mesh.bounds;

            int vCount = snap.Vertices != null ? snap.Vertices.Length : 0;
            snap.HasUV = snap.UVs != null && snap.UVs.Length == vCount && vCount > 0;
            snap.HasColor = snap.Colors != null && snap.Colors.Length == vCount && vCount > 0;
            snap.HasNormal = snap.Normals != null && snap.Normals.Length == vCount && vCount > 0;

            int subCount = mesh.subMeshCount;
            snap.SubMeshes = new SubMeshSnapshot[subCount];

            for (int i = 0; i < subCount; i++)
            {
                SubMeshSnapshot sub = new SubMeshSnapshot();
                sub.Triangles = mesh.GetTriangles(i);

                Material mat = (materials != null && i < materials.Length) ? materials[i] : null;
                sub.BaseColor = SampleMaterialBaseColor(mat);
                sub.Texture = CaptureMaterialTexture(mat);

                snap.SubMeshes[i] = sub;
            }

            return snap;
        }

        private static Color SampleMaterialBaseColor(Material mat)
        {
            if (mat == null) return Color.white;

            // 优先 URP 的 _BaseColor，回退到内置管线的 _Color
            if (mat.HasProperty(ShaderPropertyIds.BaseColor))
                return mat.GetColor(ShaderPropertyIds.BaseColor);
            if (mat.HasProperty(ShaderPropertyIds.Color))
                return mat.GetColor(ShaderPropertyIds.Color);

            return Color.white;
        }

        private static class ShaderPropertyIds
        {
            public static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
            public static readonly int Color = Shader.PropertyToID("_Color");
            public static readonly int MainTex = Shader.PropertyToID("_MainTex");
        }

        private static TextureSnapshot CaptureMaterialTexture(Material mat)
        {
            if (mat == null) return null;

            Texture2D tex = null;
            if (mat.HasProperty(ShaderPropertyIds.MainTex))
                tex = mat.GetTexture(ShaderPropertyIds.MainTex) as Texture2D;

            if (tex == null) return null;

            int id = tex.GetInstanceID();
            if (TextureCache.TryGetValue(id, out TextureSnapshot cached))
                return cached;

            TextureSnapshot snapshot = ReadTexturePixels(tex);
            if (snapshot == null || !snapshot.IsValid) return null;

            // 超出预算时整体清空。预览场景里贴图数量有限，
            // 全清比做 LRU 简单得多，且不会留下陈旧数据。
            long total = snapshot.ByteSize;
            foreach (var kv in TextureCache) total += kv.Value.ByteSize;
            if (total > TextureCacheByteBudget)
                TextureCache.Clear();

            TextureCache[id] = snapshot;
            return snapshot;
        }

        /// <summary>
        /// 经 RenderTexture 离屏拷贝读回像素，并降采样到长边不超过 512。
        ///
        /// 两个原因：
        /// 1. 直接 GetPixels32 对 import 设置里没勾 Read/Write 的贴图会直接抛异常，
        ///    RenderTexture 路径则对任何贴图都成立。
        /// 2. 预览只需要低频颜色信息，512 已经远超体素分辨率所需，
        ///    降采样能把 4K 贴图的 60MB 读取压到 1MB，且读回速度快一个数量级。
        /// </summary>
        private static TextureSnapshot ReadTexturePixels(Texture2D source)
        {
            const int maxSize = 512;

            int srcW = Mathf.Max(1, source.width);
            int srcH = Mathf.Max(1, source.height);

            int w = srcW;
            int h = srcH;

            if (srcW > maxSize || srcH > maxSize)
            {
                float aspect = (float)srcW / srcH;
                if (srcW >= srcH)
                {
                    w = maxSize;
                    h = Mathf.Max(1, Mathf.RoundToInt(maxSize / aspect));
                }
                else
                {
                    h = maxSize;
                    w = Mathf.Max(1, Mathf.RoundToInt(maxSize * aspect));
                }
            }

            RenderTexture rt = null;
            Texture2D readback = null;
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, rt);

                RenderTexture.active = rt;
                readback = new Texture2D(w, h, TextureFormat.RGBA32, false);
                readback.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                Color32[] pixels = readback.GetPixels32();
                // 第二个参数必须传 false:传 true 会把贴图设为不可读,再调 GetPixels32 立刻抛
                // "texture data is either not readable" 异常 (Unity 2022.3 已复现)。
                readback.Apply(false, false);

                return new TextureSnapshot(w, h, pixels);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MeshSnapshot] 贴图 '{source.name}' 像素读取失败，该子网格将回退为纯材质色。\n{e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (readback != null) Object.DestroyImmediate(readback);
            }
        }
    }
}
