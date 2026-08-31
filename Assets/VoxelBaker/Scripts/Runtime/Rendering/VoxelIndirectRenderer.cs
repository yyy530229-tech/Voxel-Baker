using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelBaker.Data;

namespace VoxelBaker.Runtime.Rendering
{
    public class VoxelIndirectRenderer : IVoxelRenderer
    {
        private VoxelAsset _asset;
        private Material _material;
        private MaterialPropertyBlock _matProps;
        private Transform _transform;
        private Mesh _unitCubeMesh;

        private GraphicsBuffer _voxelBuffer;
        private GraphicsBuffer _argsBuffer;
        private uint[] _argsData = new uint[5] { 0, 0, 0, 0, 0 };

        private int _capacity = 0;
        private int _currentInstanceCount = 0;
        private bool _isInitialized = false;

        private static readonly int PropVoxelBuffer = Shader.PropertyToID("_VoxelBuffer");
        private static readonly int PropVoxelSize = Shader.PropertyToID("_VoxelSize");
        private static readonly int PropLocalOrigin = Shader.PropertyToID("_LocalOrigin");
        private static readonly int PropPaletteTex = Shader.PropertyToID("_PaletteTex");

        // 逐帧强制把 BevelRoundness 锁死为 1.0。
        // 单单改 Shader 里的默认值是不够的：已存在的 Material 资源会保留旧序列化值 (0.95)，
        // 那 5% 的内缩就是相邻体素之间真实的物理缝隙。
        private static readonly int PropBevelRoundness = Shader.PropertyToID("_BevelRoundness");

        //
        // 「细腻感」参数同样逐帧下发。
        // 这三个属性是后加的，已存在的 Material 资源里没有它们，Unity 会回落 Shader 默认值；
        // 但为了让美术在这一个文件里就能调、且保证 Scene/Game 视图与烘焙预览完全一致，
        // 这里统一在运行时覆盖。想改数值只改这三个常量即可。
        //
        // EdgeRoundWidth  : 棱边高光的宽度（占面半宽的比例）。0 = 完全直角。
        // EdgeRoundAmount : 棱边处法线外翻的强度。0.45 ≈ 24°，够亮又不糊。
        //                  ↑ 0.70 会在旋转时形成"沿棱的高光窄线 + 暗带"，是屏幕像素抖动主因之一。
        // ColorJitter     : 每颗积木的批次色差幅度。0 = 完全平铺（推荐）。
        //                  ↑ 任何非零值都会成为旋转时"沙化走样"的种子, 因为批次内像素的采样
        //                    相位每帧变化, 着色抖动 → 看起来在"流"。
        //
        public const float EdgeRoundWidth = 0.20f;
        public const float EdgeRoundAmount = 0.0f;

        // 上一版 0.035 让面"发糊"——参考图每块都是干净平色。
        // 减到 0.012，只留极轻的批次色差。
        // ↑ 0.012 → 0.0：任何 batch 色差都是旋转"沙化"的种子，旋转时每像素的批次归属
        //   每帧都在抖，看起来就像流沙。彻底关掉,牺牲一点点"塑料不平"的质感，换"稳"。
        public const float ColorJitter = 0.0f;

        // Blinn-Phong 高光：参考图里塑料积木的顶面有明显的白色小亮点。
        // Power 64 让高光更集中（更塑料），0.65 强度合适。
        // 强度 0.65 → 0.38：0.65 叠加在已经接近 1.0 的漫反射上必然顶穿（死白来源之一）。
        // 幂 64 → 32：幂越大高光越窄 = 空间频率越高 = 旋转时爬得越厉害。
        // 32 仍读作"塑料的小亮点"，但频带宽度翻倍，摩尔纹显著减轻。
        // 临时归零用于定位远看条纹元凶: 若关掉后远看条纹消失, 则高光是元凶之一。
        // 定位完会从此阈值往上回加 (典型 0.12~0.20 之间找"既能看出塑料感又不产生条纹"的点)。
        public const float SpecularStrength = 0.0f;
        public const float SpecularPower = 18f;

        // 顶/侧/底面明暗差 —— 「积木感」最关键的一个参数。
        //
        // 0.22 是上轮调出的"看起来像积木但不刺眼"的甜点。远看旋转摩尔纹根治后回填。
        public const float FaceShade = 0.22f;
        public const float AOStrength = 0.65f;

        //
        // 排查探针（0 = 关闭，正常运行时务必保持 0）。
        // 完整语义见 VoxelLit.shader 里 _DebugMode 的注释。
        //
        // 排查顺序：
        //   1 品红      —— 先跑这个！屏幕没变品红 = 你看到的画面根本不是这个 shader 画的，
        //                  前面所有 shader 修改自然全部无效（此时该去查渲染路径，不是查参数）
        //   2 纯 albedo —— 排除一切光照项
        //   3 法线可视化 —— 法线若有硬断裂，这里会直接显示为颜色跳变
        //   4 关假圆角
        //   5 关高光
        //   6 关描深
        //
        public const float DebugMode = 0f;

        private static readonly int PropEdgeRoundWidth = Shader.PropertyToID("_EdgeRoundWidth");
        private static readonly int PropEdgeRoundAmount = Shader.PropertyToID("_EdgeRoundAmount");
        private static readonly int PropColorJitter = Shader.PropertyToID("_ColorJitter");
        private static readonly int PropSpecularStrength = Shader.PropertyToID("_SpecularStrength");
        private static readonly int PropSpecularPower = Shader.PropertyToID("_SpecularPower");
        private static readonly int PropFaceShade = Shader.PropertyToID("_FaceShade");
        private static readonly int PropAOStrength = Shader.PropertyToID("_AOStrength");
        private static readonly int PropDebugMode = Shader.PropertyToID("_DebugMode");

        public void Initialize(VoxelAsset asset, Material voxelMaterial, Transform rootTransform)
        {
            _asset = asset;
            _material = voxelMaterial;
            _transform = rootTransform;
            _matProps = new MaterialPropertyBlock();

            _unitCubeMesh = CreateUnitCubeMesh();

            int initialCount = asset != null && asset.initialVisibleVoxels != null ? asset.initialVisibleVoxels.Length : 1024;
            // 预留 2 倍容量，以容纳破坏暴露出的内部体素
            _capacity = Mathf.Max(2048, initialCount * 2);

            _voxelBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _capacity, Marshal.SizeOf<PackedVoxelGPU>());
            _argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, sizeof(uint) * 5);

            _argsData[0] = _unitCubeMesh.GetIndexCount(0);
            _argsData[1] = 0; // Instance count
            _argsData[2] = _unitCubeMesh.GetIndexStart(0);
            _argsData[3] = _unitCubeMesh.GetBaseVertex(0);
            _argsData[4] = 0; // Start instance

            _argsBuffer.SetData(_argsData);

            if (asset != null && asset.initialVisibleVoxels != null)
            {
                UpdateVisibleInstances(asset.initialVisibleVoxels, asset.initialVisibleVoxels.Length);
            }

            _isInitialized = true;
        }

        public void UpdateVisibleInstances(PackedVoxelGPU[] activeInstances, int count)
        {
            if (activeInstances == null || count <= 0)
            {
                _currentInstanceCount = 0;
                _argsData[1] = 0;
                _argsBuffer?.SetData(_argsData);
                return;
            }

            if (count > _capacity)
            {
                _capacity = Mathf.Max(count + 2048, (int)(_capacity * 1.5f));
                _voxelBuffer?.Release();
                _voxelBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _capacity, Marshal.SizeOf<PackedVoxelGPU>());
            }

            _voxelBuffer.SetData(activeInstances, 0, 0, count);
            _currentInstanceCount = count;

            _argsData[1] = (uint)count;
            _argsBuffer.SetData(_argsData);
        }

        private static readonly int PropObjectToWorldMatrix = Shader.PropertyToID("_ObjectToWorldMatrix");

        // 历史注记: 这里曾有一个 Render() 方法, 在 Update() 里直接 Graphics.DrawMeshInstancedIndirect
        // 提交绘制。那条路径完全绕过 URP 管线, RenderScale/MSAA 全部无效, 是远处摩尔纹的根因,
        // 已连根移除 —— 唯一的绘制入口现在是 RenderToCommandBuffer(CommandBuffer),
        // 由 VoxelIndirectRenderFeature 在管线内调用。请勿再恢复 Graphics 直提交路径。

        /// <summary>
        /// 把间接绘制写入 RendererFeature 传入的 CommandBuffer (由 VoxelIndirectRenderFeature 每帧调用)。
        /// 关键: 走 URP 管线内的 CommandBuffer 让 DrawCall 进入带 MSAA + RenderScale 的相机 RT,
        /// 摩尔纹才会被硬件抗锯齿打散。之前在 Update() 里直接 Graphics.DrawMeshInstancedIndirect,
        /// DrawCall 完全绕过 URP → RenderScale/MSAA 无效 → 远处体素与屏幕像素干涉产生摩尔纹。
        ///
        /// 注意: CommandBuffer.DrawMeshInstancedIndirect 只有 7 参数重载
        /// (mesh, submeshIndex, material, shaderPass, bufferWithArgs, argsOffset, properties),
        /// 不含 bounds / ShadowCastingMode / 探针 —— culling 由 RendererFeature 的相机处理,
        /// 实时阴影如需要后续补 ShadowCaster Pass。
        /// </summary>
        public void RenderToCommandBuffer(CommandBuffer cmd)
        {
            if (!_isInitialized || _currentInstanceCount == 0 || _material == null || _unitCubeMesh == null || cmd == null)
                return;

            // 每帧 MaterialPropertyBlock 重新下发, 保证与运行时参数一致。
            _matProps.SetBuffer(PropVoxelBuffer, _voxelBuffer);
            _matProps.SetFloat(PropVoxelSize, _asset != null ? _asset.voxelSize : 0.1f);
            _matProps.SetVector(PropLocalOrigin, _asset != null ? (Vector4)_asset.localOrigin : Vector4.zero);
            _matProps.SetMatrix(PropObjectToWorldMatrix, _transform != null ? _transform.localToWorldMatrix : Matrix4x4.identity);
            _matProps.SetFloat(PropBevelRoundness, 1.0f);
            _matProps.SetFloat(PropEdgeRoundWidth, EdgeRoundWidth);
            _matProps.SetFloat(PropEdgeRoundAmount, EdgeRoundAmount);
            _matProps.SetFloat(PropColorJitter, ColorJitter);
            _matProps.SetFloat(PropSpecularStrength, SpecularStrength);
            _matProps.SetFloat(PropSpecularPower, SpecularPower);
            _matProps.SetFloat(PropFaceShade, FaceShade);
            _matProps.SetFloat(PropAOStrength, AOStrength);
            _matProps.SetFloat(PropDebugMode, DebugMode);
            if (_asset != null && _asset.paletteTexture != null)
                _matProps.SetTexture(PropPaletteTex, _asset.paletteTexture);

            // 7 参数重载: Pass 0 = ForwardLit (UniversalForward)。
            // VoxelLit.shader 内 Pass 1 是 ShadowCaster, 这里只画正向光照。
            cmd.DrawMeshInstancedIndirect(
                _unitCubeMesh,
                0,          // submeshIndex
                _material,
                0,          // shaderPass = ForwardLit
                _argsBuffer,
                0,          // argsOffset
                _matProps);
        }

        public void Release()
        {
            _voxelBuffer?.Release();
            _voxelBuffer = null;

            _argsBuffer?.Release();
            _argsBuffer = null;

            if (_unitCubeMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_unitCubeMesh);
                _unitCubeMesh = null;
            }

            _isInitialized = false;
        }

        public void Dispose()
        {
            Release();
        }

        // 乐高积木倒角宽度 (相对单位立方体边长比例)
        //
        // 设为 0 = 干净立方体（推荐）。
        //
        // 倒角原本是给"乐高砖"做棱边高光的，但代价很大：
        //   · 每块的三角形数从 12 暴涨到 ~150
        //   · 编辑器 Scene 视图线框 overlay 会把倒角面密密麻麻地叠在表面上
        //   · 旋转时相邻块的倒角条带（法线为 ±0.707 的斜面）会互相 z-fight / 穿透
        //   · 沟槽宽度 = 2 × ChamferWidth，0.030 就已经有 6% 暗沟
        //
        // "立体感"完全靠 Shader 内的 _FaceShade 面朝向明暗 + K-Means 平色块 +
        // 顶点法线光照来打，不需要在网格几何上刻倒角。干净立方体反而更"细腻"。
        public const float ChamferWidth = 0.000f;

        private static Mesh CreateUnitCubeMesh()
        {
            // 倒角立方体 (Chamfered Cube / 乐高砖块)：
            // 倒角立方体 (Chamfered Cube / 乐高砖块)：
            // 由 6 个面平面 + 12 个棱倒角平面 + 8 个角倒角平面 (共 26 个半空间) 相交而成。
            // 平面法线朝外，n·p ≤ d 表示内部。三平面交点中满足全部半空间的点即为顶点，
            // 再按平面分组、极角排序、扇形三角化，得到带平直倒角棱边的 LEGO 砖块。
            //
            // ChamferWidth = 0 时退化为干净的单位立方体：12 三角面、6 法线，
            // 编辑器线框 overlay 不再叠满每个方块，旋转时也无 z-fight。
            float h = 0.5f;
            float c = ChamferWidth;

            var planes = new List<(Vector3 n, float d)>();

            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };

            // 6 个面
            for (int a = 0; a < 3; a++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    planes.Add((axes[a] * s, h));
                }
            }

            // 12 个棱倒角 (沿轴 i 的棱，位于另外两轴 ±h 处)
            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3;
                int k = (i + 2) % 3;
                for (int sj = -1; sj <= 1; sj += 2)
                {
                    for (int sk = -1; sk <= 1; sk += 2)
                    {
                        planes.Add((axes[j] * sj + axes[k] * sk, 2f * h - c));
                    }
                }
            }

            // 8 个角倒角 (斜切三角形面)
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        planes.Add((new Vector3(sx, sy, sz), 3f * h - 2f * c));
                    }
                }
            }

            // 枚举所有三平面交点，保留满足全部半空间的顶点
            var verts = new List<Vector3>();
            int pCount = planes.Count;
            for (int a = 0; a < pCount; a++)
            {
                for (int b = a + 1; b < pCount; b++)
                {
                    for (int cc = b + 1; cc < pCount; cc++)
                    {
                        Vector3 p = IntersectThreePlanes(planes[a].n, planes[a].d, planes[b].n, planes[b].d, planes[cc].n, planes[cc].d);
                        if (float.IsNaN(p.x)) continue;
                        if (SatisfiesAllPlanes(p, planes))
                        {
                            bool dup = false;
                            for (int i = 0; i < verts.Count; i++)
                            {
                                if ((verts[i] - p).sqrMagnitude < 1e-10f) { dup = true; break; }
                            }
                            if (!dup) verts.Add(p);
                        }
                    }
                }
            }

            // 按平面分组构建多边形并三角化
            var meshVerts = new System.Collections.Generic.List<Vector3>();
            var meshNormals = new System.Collections.Generic.List<Vector3>();
            var meshTris = new System.Collections.Generic.List<int>();

            for (int pi = 0; pi < pCount; pi++)
            {
                Vector3 n = planes[pi].n;
                float d = planes[pi].d;
                float nLen = n.magnitude;
                Vector3 nNorm = n / nLen;

                var faceVerts = new System.Collections.Generic.List<int>();
                for (int i = 0; i < verts.Count; i++)
                {
                    if (Mathf.Abs(Vector3.Dot(n, verts[i]) - d) < 1e-4f * Mathf.Max(1f, d))
                    {
                        faceVerts.Add(i);
                    }
                }

                if (faceVerts.Count < 3) continue;

                // 计算面中心与面内 2D 基 (用于极角排序)
                Vector3 centroid = Vector3.zero;
                for (int i = 0; i < faceVerts.Count; i++) centroid += verts[faceVerts[i]];
                centroid /= faceVerts.Count;

                Vector3 t1 = Vector3.Cross(nNorm, Mathf.Abs(nNorm.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                Vector3 t2 = Vector3.Cross(nNorm, t1).normalized;

                faceVerts.Sort((ia, ib) =>
                {
                    float angA = Mathf.Atan2(Vector3.Dot(verts[ia] - centroid, t2), Vector3.Dot(verts[ia] - centroid, t1));
                    float angB = Mathf.Atan2(Vector3.Dot(verts[ib] - centroid, t2), Vector3.Dot(verts[ib] - centroid, t1));
                    return angA.CompareTo(angB);
                });

                // 扇形三角化
                int baseIdx = meshVerts.Count;
                for (int i = 0; i < faceVerts.Count; i++)
                {
                    meshVerts.Add(verts[faceVerts[i]]);
                    meshNormals.Add(nNorm);
                }

                for (int k = 1; k < faceVerts.Count - 1; k++)
                {
                    int i0 = baseIdx;
                    int i1 = baseIdx + k;
                    int i2 = baseIdx + k + 1;

                    // 校正绕序：保证三角形法线与面法线同向
                    Vector3 triN = Vector3.Cross(meshVerts[i1] - meshVerts[i0], meshVerts[i2] - meshVerts[i0]);
                    if (Vector3.Dot(triN, nNorm) < 0f)
                    {
                        int tmp = i1;
                        i1 = i2;
                        i2 = tmp;
                    }

                    meshTris.Add(i0);
                    meshTris.Add(i1);
                    meshTris.Add(i2);
                }
            }

            Mesh mesh = new Mesh { name = "ChamferedUnitCube" };
            mesh.vertices = meshVerts.ToArray();
            mesh.normals = meshNormals.ToArray();
            mesh.triangles = meshTris.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>求解三个平面 n_i·p = d_i 的交点 (克莱默法则)</summary>
        private static Vector3 IntersectThreePlanes(Vector3 n1, float d1, Vector3 n2, float d2, Vector3 n3, float d3)
        {
            float det = Vector3.Dot(n1, Vector3.Cross(n2, n3));
            if (Mathf.Abs(det) < 1e-8f) return new Vector3(float.NaN, float.NaN, float.NaN);
            return (d1 * Vector3.Cross(n2, n3) + d2 * Vector3.Cross(n3, n1) + d3 * Vector3.Cross(n1, n2)) / det;
        }

        /// <summary>判断点是否满足全部半空间 n·p ≤ d</summary>
        private static bool SatisfiesAllPlanes(Vector3 p, List<(Vector3 n, float d)> planes)
        {
            for (int i = 0; i < planes.Count; i++)
            {
                if (Vector3.Dot(planes[i].n, p) > planes[i].d + 1e-4f)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
