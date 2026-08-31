using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Baker
{
    /// <summary>
    /// 实时预览的入参快照。
    ///
    /// 全部是普通值类型字段 —— 没有 Mesh、没有 Material、没有 ScriptableObject。
    /// 这样它才能被整个丢到 ThreadPool 上跑而不用加任何锁。
    /// </summary>
    public class VoxelPreviewRequest
    {
        public MeshSnapshot Snapshot;

        public float TargetModelHeight = 3f;
        public int TargetVoxelBudget = 6000;

        /// <summary>大于 0 时表示用户手动指定了体素尺寸，跳过预算求解。</summary>
        public float ManualVoxelSize = 0f;

        public VoxelFillStrategy FillStrategy = VoxelFillStrategy.SurfaceShellOnly;
        public int ShellThickness = 2;

        public bool AntiAliasing = true;
        public int SmoothingIterations = 1;
        public int SupersampleRate = 3;

        public int PaletteColorCount = 24;
        public float PaletteTolerance = 24f;

        /// <summary>预览网格长轴上限。拖拽中用小值求快，松手后用大值求准。</summary>
        public int MaxGridDimension = 96;

        /// <summary>是否跑块数标定探针。拖拽时关掉可以再快一个数量级。</summary>
        public bool AccurateBudget = true;

        /// <summary>
        /// 协作式取消令牌。
        /// 参数被再次改动时旧的构建就没意义了，构建器会在阶段之间检查这个令牌提前退出，
        /// 避免连续拖拽时在后台堆积一堆跑一半的体素化任务。
        /// </summary>
        public System.Threading.CancellationToken CancelToken = System.Threading.CancellationToken.None;

        public VoxelPreviewRequest Clone()
        {
            return (VoxelPreviewRequest)MemberwiseClone();
        }
    }

    /// <summary>
    /// 预览构建结果。同样是纯数据 —— 顶点数组要在主线程上才能灌进 Mesh。
    /// </summary>
    public class VoxelPreviewResult
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Color32[] Colors;

        /// <summary>
        /// 每个顶点在其所属立方体内部的局部坐标，范围 [-0.5, 0.5]。
        /// 预览用的是合并后的单个 Mesh，positionOS 已经是整体模型空间了，
        /// 没法再反推"这个像素离自己那块积木的边缘有多远"，
        /// 所以边缘描深需要的立方体内局部坐标必须单独塞进 UV1 传过去。
        /// </summary>
        public Vector3[] CubeLocal;

        public int[] Triangles;

        public Vector3Int GridDimensions;
        public float VoxelSize;
        public int TotalVoxels;
        public int VisibleVoxels;
        public int PaletteColorCount;

        /// <summary>网格长轴被 MaxGridDimension 限流过 —— 此时块数会低于预算。</summary>
        public bool GridWasCapped;

        public double BuildMilliseconds;
        public string ErrorMessage;

        /// <summary>本次构建被更新的请求顶掉了，结果应当丢弃。</summary>
        public bool WasCancelled;

        public bool IsValid =>
            string.IsNullOrEmpty(ErrorMessage) && Vertices != null && Vertices.Length > 0;
    }

    /// <summary>
    /// 参数驱动的体素化预览构建器。
    ///
    /// 与正式烘焙走的是完全相同的求解器（同一个 SurfaceVoxelizer、同一个量化器、同一个 AO 烘焙器），
    /// 只是中间产物不落成 ScriptableObject 资产，而是直接拼成一张可渲染的合并网格。
    /// 所以预览里看到的块数、轮廓、配色，就是点"烘焙"之后拿到的东西。
    ///
    /// 全部方法不触碰任何 UnityEngine 对象，可安全地在后台线程调用。
    /// 唯一的例外是 Debug.Log —— 但那只在异常回退路径上。
    /// </summary>
    public static class VoxelPreviewBuilder
    {
        //
        // 6 个面的顶点局部偏移，单位立方体（分量 ±0.5）。
        // 绕序沿用 VoxelScenePreview 里已验证了的那套表：
        // 右手叉积 (v1-v0)×(v2-v0) 指向面法线外侧，配 Cull Back 正面朝外。
        //
        private static readonly Vector3[][] FaceCorners = new Vector3[][]
        {
            // +X
            new Vector3[] { new Vector3( 0.5f, -0.5f, -0.5f), new Vector3( 0.5f,  0.5f, -0.5f), new Vector3( 0.5f,  0.5f,  0.5f), new Vector3( 0.5f, -0.5f,  0.5f) },
            // -X
            new Vector3[] { new Vector3(-0.5f, -0.5f,  0.5f), new Vector3(-0.5f,  0.5f,  0.5f), new Vector3(-0.5f,  0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f) },
            // +Y
            new Vector3[] { new Vector3(-0.5f,  0.5f, -0.5f), new Vector3(-0.5f,  0.5f,  0.5f), new Vector3( 0.5f,  0.5f,  0.5f), new Vector3( 0.5f,  0.5f, -0.5f) },
            // -Y
            new Vector3[] { new Vector3(-0.5f, -0.5f,  0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3( 0.5f, -0.5f, -0.5f), new Vector3( 0.5f, -0.5f,  0.5f) },
            // +Z
            new Vector3[] { new Vector3( 0.5f, -0.5f,  0.5f), new Vector3( 0.5f,  0.5f,  0.5f), new Vector3(-0.5f,  0.5f,  0.5f), new Vector3(-0.5f, -0.5f,  0.5f) },
            // -Z
            new Vector3[] { new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f,  0.5f, -0.5f), new Vector3( 0.5f,  0.5f, -0.5f), new Vector3( 0.5f, -0.5f, -0.5f) }
        };

        private static readonly Vector3[] FaceNormals = new Vector3[]
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };

        /// <summary>
        /// 执行体素化预览。线程安全，可直接丢给 ThreadPool。
        /// 任何异常都会被兜住并写进 ErrorMessage，不会把后台线程炸掉拖垮编辑器。
        /// </summary>
        public static VoxelPreviewResult Build(VoxelPreviewRequest request)
        {
            VoxelPreviewResult result = new VoxelPreviewResult();
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                BuildInternal(request, result);
            }
            catch (OperationCanceledException)
            {
                // 被更新的请求顶掉了，不是错误 —— 静默丢弃，让新结果接管
                result.Vertices = null;
                result.WasCancelled = true;
            }
            catch (Exception e)
            {
                result.ErrorMessage = e.Message;
                result.Vertices = null;
            }

            sw.Stop();
            result.BuildMilliseconds = sw.Elapsed.TotalMilliseconds;
            return result;
        }

        private static void ThrowIfCancelled(VoxelPreviewRequest request)
        {
            if (request.CancelToken.CanBeCanceled && request.CancelToken.IsCancellationRequested)
                throw new OperationCanceledException(request.CancelToken);
        }

        private static void BuildInternal(VoxelPreviewRequest request, VoxelPreviewResult result)
        {
            MeshSnapshot snapshot = request.Snapshot;
            if (snapshot == null || snapshot.VertexCount == 0)
            {
                result.ErrorMessage = "没有可预览的模型";
                return;
            }

            // 1. 归一化尺度 —— 与 VoxelBakerCore.Bake 完全一致
            Bounds rawBounds = snapshot.Bounds;
            float rawMaxDim = Mathf.Max(rawBounds.size.x, Mathf.Max(rawBounds.size.y, rawBounds.size.z));
            float targetH = request.TargetModelHeight > 0 ? request.TargetModelHeight : 3f;
            float meshScale = (rawMaxDim > 0.0001f) ? (targetH / rawMaxDim) : 1f;
            Bounds scaledBounds = new Bounds(rawBounds.center * meshScale, rawBounds.size * meshScale);

            // 2. 求解体素尺寸
            float voxelSize = request.ManualVoxelSize;
            if (voxelSize <= 0.0001f)
            {
                voxelSize = VoxelBudgetSolver.SolveVoxelSize(
                    snapshot,
                    meshScale,
                    scaledBounds,
                    request.TargetVoxelBudget,
                    request.FillStrategy,
                    request.ShellThickness,
                    request.AntiAliasing,
                    request.SmoothingIterations,
                    request.SupersampleRate,
                    request.AccurateBudget);
            }

            // 3. 网格尺寸（留出 1 格边界给外部 Flood Fill，与 Bake 一致）
            Vector3 minB = scaledBounds.min - Vector3.one * voxelSize;
            Vector3 maxB = scaledBounds.max + Vector3.one * voxelSize;
            Vector3 origin = minB;

            int gx = Mathf.Max(3, Mathf.CeilToInt((maxB.x - minB.x) / voxelSize));
            int gy = Mathf.Max(3, Mathf.CeilToInt((maxB.y - minB.y) / voxelSize));
            int gz = Mathf.Max(3, Mathf.CeilToInt((maxB.z - minB.z) / voxelSize));

            int maxDim = Mathf.Max(gx, Mathf.Max(gy, gz));
            if (maxDim > request.MaxGridDimension)
            {
                result.GridWasCapped = true;
                float k = maxDim / (float)request.MaxGridDimension;
                voxelSize *= k;
                minB = scaledBounds.min - Vector3.one * voxelSize;
                origin = minB;
                maxB = scaledBounds.max + Vector3.one * voxelSize;
                gx = Mathf.Max(3, Mathf.CeilToInt((maxB.x - minB.x) / voxelSize));
                gy = Mathf.Max(3, Mathf.CeilToInt((maxB.y - minB.y) / voxelSize));
                gz = Mathf.Max(3, Mathf.CeilToInt((maxB.z - minB.z) / voxelSize));
            }

            // 分配几十 MB 的体素网格之前先验一次票，能省下最贵的一次分配
            ThrowIfCancelled(request);

            Vector3Int dims = new Vector3Int(gx, gy, gz);

            VoxelCell[,,] grid = new VoxelCell[gx, gy, gz];
            SurfaceHitInfo[,,] hits = new SurfaceHitInfo[gx, gy, gz];

            // 4. 表面体素化（整条管线里最重的一步）
            SurfaceVoxelizer.VoxelizeSurface(
                snapshot, meshScale, voxelSize, origin, dims, grid, hits, request.SupersampleRate);
            ThrowIfCancelled(request);

            // 5. 形态学抗锯齿平滑
            if (request.AntiAliasing)
            {
                MorphologySmoother.Smooth(dims, grid, request.SmoothingIterations, true);
            }
            ThrowIfCancelled(request);

            // 6. 内部填充（仅非单层壳策略需要）
            if (request.FillStrategy != VoxelFillStrategy.SurfaceShellOnly)
            {
                SolidVoxelizer.VoxelizeSolid(dims, grid, true);

                Vector3Int[,,] nearest = new Vector3Int[gx, gy, gz];
                DistanceFieldSolver.ComputeDistanceField(dims, grid, nearest);

                // 预览不传内部剖面配置 —— 那是个 ScriptableObject，不能跨线程读。
                // 传 null 会走 NearestSurfaceMaterial 默认策略，几何结果一致，只是内部配色不同。
                InteriorGenerator.GenerateInterior(
                    dims, grid, nearest, null, null, request.FillStrategy, request.ShellThickness);
            }
            ThrowIfCancelled(request);

            // 7. 外观采样（palette 传 null：预览不需要调色板索引，只要颜色）
            AppearanceSampler.SampleSurfaceAppearance(
                snapshot, dims, grid, hits, null, request.AntiAliasing);
            ThrowIfCancelled(request);

            // 8. K-Means 平色量化 —— 与正式烘焙同一套算法、同一组参数
            List<Color32> palette = new List<Color32>();
            VoxelColorQuantizer.QuantizeSurfaceColors(
                dims, grid, palette, request.PaletteColorCount, request.PaletteTolerance);

            // 9. AO 与暴露面掩码。
            //    直接复用正式烘焙的 AO 烘焙器：这样预览里的暗角、以及"哪些面被剔除"，
            //    和最终资产里的处理完全同源，不会出现"预览好看、烘焙变样"。
            AOFaceMaskBaker.BakeAOAndFaceMask(dims, grid);

            // 10. 拼装合并网格
            AssembleMesh(grid, dims, origin, voxelSize, result);
            result.GridDimensions = dims;
            result.VoxelSize = voxelSize;
            result.PaletteColorCount = palette.Count > 0 ? palette.Count : CountDistinctColors(grid, dims);
        }

        private static void AssembleMesh(
            VoxelCell[,,] grid,
            Vector3Int dims,
            Vector3 origin,
            float voxelSize,
            VoxelPreviewResult result)
        {
            int gx = dims.x, gy = dims.y, gz = dims.z;

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Color32> colors = new List<Color32>();
            List<Vector3> cubeLocal = new List<Vector3>();
            List<int> tris = new List<int>();

            int totalVoxels = 0;
            int visibleVoxels = 0;

            for (int x = 0; x < gx; x++)
            {
                for (int y = 0; y < gy; y++)
                {
                    for (int z = 0; z < gz; z++)
                    {
                        VoxelCell cell = grid[x, y, z];
                        if (!cell.isOccupied) continue;

                        totalVoxels++;

                        byte mask = (byte)cell.faceMask;
                        if (mask == 0) continue; // 完全被包住的内部块，不可见

                        visibleVoxels++;

                        Vector3 center = origin + new Vector3(
                            (x + 0.5f) * voxelSize,
                            (y + 0.5f) * voxelSize,
                            (z + 0.5f) * voxelSize);

                        //
                        // AO 直接烘进顶点色。
                        // 正式管线里 AO 是在 shader 里乘上去的，但那依赖实例化属性；
                        // 合并网格没有这些属性，所以在 CPU 侧先乘好，视觉效果等价。
                        //
                        float ao = cell.ao / 255f;
                        ao = 1f - (1f - ao) * 0.65f; // 对应 VoxelLit 的 _AOStrength = 0.65

                        Color32 c = cell.customColor;
                        Color32 shaded = new Color32(
                            (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * ao), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * ao), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * ao), 0, 255),
                            255);

                        for (int f = 0; f < 6; f++)
                        {
                            if ((mask & (1 << f)) == 0) continue;

                            int vStart = verts.Count;
                            Vector3[] corners = FaceCorners[f];
                            Vector3 fn = FaceNormals[f];

                            for (int k = 0; k < 4; k++)
                            {
                                // corners[k] 是单位立方体坐标，乘 voxelSize 得世界偏移，
                                // 原样保留则是 shader 要的立方体内局部坐标。一份数据两用。
                                verts.Add(center + corners[k] * voxelSize);
                                normals.Add(fn);
                                colors.Add(shaded);
                                cubeLocal.Add(corners[k]);
                            }

                            tris.Add(vStart + 0);
                            tris.Add(vStart + 1);
                            tris.Add(vStart + 2);
                            tris.Add(vStart + 0);
                            tris.Add(vStart + 2);
                            tris.Add(vStart + 3);
                        }
                    }
                }
            }

            result.Vertices = verts.ToArray();
            result.Normals = normals.ToArray();
            result.Colors = colors.ToArray();
            result.CubeLocal = cubeLocal.ToArray();
            result.Triangles = tris.ToArray();
            result.TotalVoxels = totalVoxels;
            result.VisibleVoxels = visibleVoxels;
        }

        private static int CountDistinctColors(VoxelCell[,,] grid, Vector3Int dims)
        {
            HashSet<uint> seen = new HashSet<uint>();
            int gx = dims.x, gy = dims.y, gz = dims.z;

            for (int x = 0; x < gx; x++)
                for (int y = 0; y < gy; y++)
                    for (int z = 0; z < gz; z++)
                    {
                        if (!grid[x, y, z].isOccupied) continue;
                        Color32 c = grid[x, y, z].customColor;
                        uint key = (uint)(c.r | (c.g << 8) | (c.b << 16));
                        seen.Add(key);
                        if (seen.Count > 4096) return seen.Count;
                    }

            return seen.Count;
        }
    }
}
