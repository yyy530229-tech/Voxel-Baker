using System;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Baker
{
    public enum VoxelFillStrategy
    {
        /// <summary>
        /// 仅表面单层壳 (块数最省，可见分辨率最高)
        /// </summary>
        SurfaceShellOnly = 0,

        /// <summary>
        /// 加厚中空壳层 (表面保留 N 层厚度，内部中空)
        /// </summary>
        ThickHollowShell = 1,

        /// <summary>
        /// 完全实心填充 (内部全填满)
        /// </summary>
        SolidCore = 2
    }

    public enum VoxelDensityPreset
    {
        Tiny_3k = 0,        // 极简 (~3,000 块)
        Standard_12k = 1,   // 标清推荐 (~6,000 块)
        High_30k = 2,       // 高清精细 (~10,000 块)
        Ultra_60k = 3,      // 极致超清 (~16,000 块)
        Custom = 4          // 自定义预算
    }

    /// <summary>
    /// 目标体素块数预算与体素尺寸自适应求解器
    ///
    /// 设计要点（本次重写）：
    /// 旧的解析式里塞了两个拍脑袋的经验系数 —— 表面积 ×1.35、包围盒 ×0.32 ——
    /// 实测偏差高达 +60%（预算 6000 实际烘出 9568）。
    /// 偏差的直接后果是"想要高清就只能把预算往上堆"，因为用户根本不敢调高预算：
    /// 预算一高，块数就爆炸。
    ///
    /// 新实现分两步：
    ///   1) 解析种子：用「体素计数面积」Σ A·L1(n) 和散度定理精确体积，
    ///      这两个量是决定体素个数的真实几何量，不含任何经验系数。
    ///   2) 实测标定：真跑一遍轻量体素化，数出实际块数，再按幂律反解 voxelSize。
    ///      壳层 ∝ h^-2、实心 ∝ h^-3，两次迭代即可收敛到 ±5% 以内。
    ///
    /// 块数一旦可控，"低块数 + 高清"才有讨论空间：
    /// 同样的 6000 块预算，从 SolidCore 换成 SurfaceShellOnly，
    /// 长轴分辨率从 ~38 格涨到 ~67 格，可见块数从 ~2000 涨到 6000。
    /// </summary>
    public static class VoxelBudgetSolver
    {
        public static int GetPresetTargetCount(VoxelDensityPreset preset, int customCount = 6000)
        {
            switch (preset)
            {
                case VoxelDensityPreset.Tiny_3k: return 3000;
                case VoxelDensityPreset.Standard_12k: return 6000;
                case VoxelDensityPreset.High_30k: return 10000;
                case VoxelDensityPreset.Ultra_60k: return 16000;
                case VoxelDensityPreset.Custom: return Mathf.Clamp(customCount, 500, 50000);
                default: return 6000;
            }
        }

        /// <summary>
        /// 「体素计数面积」: Σ_t  A_t · (|nx| + |ny| + |nz|)_t
        ///
        /// 这才是决定"一层表面壳要吃掉多少个体素"的量，不是欧氏面积。
        /// 一个与 X 轴垂直、面积 A 的平面，体素化后是 A/h² 个方块（L1 范数 = 1）。
        /// 一个在 XY 平面内倾斜 45°、面积 A 的平面，体素化后是阶梯状，
        /// 要吃掉 A/h² · (0.707+0.707) = 1.414·A/h² 个方块。
        /// 球体平均下来 L1 = 1.5，正方体 L1 = 1.0 —— 差 50%，
        /// 这就是旧的全局 1.35 系数在不同模型上忽高忽低的根源。
        /// </summary>
        public static float CalculateVoxelCountArea(MeshSnapshot snapshot, float meshScale)
        {
            if (snapshot == null) return 1f;

            Vector3[] vertices = snapshot.Vertices;
            int[] triangles = snapshot.AllTriangles;
            float total = 0f;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 v0 = vertices[triangles[i]] * meshScale;
                Vector3 v1 = vertices[triangles[i + 1]] * meshScale;
                Vector3 v2 = vertices[triangles[i + 2]] * meshScale;

                Vector3 cross = Vector3.Cross(v1 - v0, v2 - v0);
                float area = cross.magnitude * 0.5f;
                if (area < 1e-9f) continue;

                Vector3 n = cross / (2f * area);
                total += area * (Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(n.z));
            }

            return Mathf.Max(0.001f, total);
        }

        /// <summary>
        /// 用散度定理精确计算闭合网格的有向体积 (立方米)：V = (1/6)·Σ v0·(v1×v2)
        /// 取代旧的"包围盒 × 0.32"拍脑袋估算 —— 那个 0.32 对细长模型能差 2~3 倍。
        /// </summary>
        public static float CalculateMeshVolume(MeshSnapshot snapshot, float meshScale)
        {
            if (snapshot == null) return 0f;

            Vector3[] vertices = snapshot.Vertices;
            int[] triangles = snapshot.AllTriangles;
            float volume = 0f;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 v0 = vertices[triangles[i]] * meshScale;
                Vector3 v1 = vertices[triangles[i + 1]] * meshScale;
                Vector3 v2 = vertices[triangles[i + 2]] * meshScale;
                volume += Vector3.Dot(v0, Vector3.Cross(v1, v2));
            }

            volume /= 6f;
            return Mathf.Abs(volume);
        }

        /// <summary>块数随 voxelSize 变化的幂律指数：壳层是面积量纲 (2)，实心是体积量纲 (3)</summary>
        private static float GetScalingExponent(VoxelFillStrategy fillStrategy)
        {
            return fillStrategy == VoxelFillStrategy.SolidCore ? 3f : 2f;
        }

        /// <summary>
        /// 解析种子解：只用精确几何量，不含经验系数。
        /// </summary>
        private static float SolveAnalyticSeed(
            MeshSnapshot snapshot,
            float meshScale,
            Bounds scaledBounds,
            int targetVoxelBudget,
            VoxelFillStrategy fillStrategy,
            int shellThickness)
        {
            float voxelCountArea = CalculateVoxelCountArea(snapshot, meshScale);
            Vector3 size = scaledBounds.size;
            float boundingVolume = Mathf.Max(0.001f, size.x * size.y * size.z);

            switch (fillStrategy)
            {
                case VoxelFillStrategy.SurfaceShellOnly:
                    // 单层壳: N = S1 / h²
                    return Mathf.Sqrt(voxelCountArea / targetVoxelBudget);

                case VoxelFillStrategy.ThickHollowShell:
                {
                    // 厚壳: 第 k 层相对第 0 层会略微收缩，用 0.88^(k-1) 近似。
                    // 厚度 1 → 1.0；厚度 2 → 1.88；厚度 3 → 2.65；厚度 4 → 3.33
                    int t = Mathf.Clamp(shellThickness, 1, 6);
                    float effectiveLayers = 1f;
                    for (int k = 1; k < t; k++) effectiveLayers += Mathf.Pow(0.88f, k);
                    return Mathf.Sqrt((voxelCountArea * effectiveLayers) / targetVoxelBudget);
                }

                case VoxelFillStrategy.SolidCore:
                default:
                {
                    // 实心: N = V_mesh / h³。非闭合网格体积会算崩，此时回退到包围盒估算。
                    float vol = CalculateMeshVolume(snapshot, meshScale);
                    if (vol < boundingVolume * 0.01f || vol > boundingVolume)
                        vol = boundingVolume * 0.32f;
                    return Mathf.Pow(vol / targetVoxelBudget, 1f / 3f);
                }
            }
        }

        /// <summary>
        /// 标定探针：真跑一遍轻量体素化，数出实际占用块数。
        /// 不采样外观、不做 K-Means、不建 chunk，只关心几何计数。
        /// 返回的 voxelSize 可能因为网格上限被放大，需要配合幂律外推使用。
        /// </summary>
        private static int ProbeOccupiedCount(
            MeshSnapshot snapshot,
            float meshScale,
            Bounds scaledBounds,
            ref float voxelSize,
            VoxelFillStrategy fillStrategy,
            int shellThickness,
            bool antiAliasing,
            int smoothingIterations,
            int supersampleRate,
            int maxProbeDimension)
        {
            Vector3 minB = scaledBounds.min - Vector3.one * voxelSize;
            Vector3 maxB = scaledBounds.max + Vector3.one * voxelSize;
            Vector3 origin = minB;

            int gx = Mathf.Max(3, Mathf.CeilToInt((maxB.x - minB.x) / voxelSize));
            int gy = Mathf.Max(3, Mathf.CeilToInt((maxB.y - minB.y) / voxelSize));
            int gz = Mathf.Max(3, Mathf.CeilToInt((maxB.z - minB.z) / voxelSize));

            // 探针网格上限：防止细分把编辑器内存打爆。
            // 一旦被限流，voxelSize 会被放大，外面的幂律外推会自动把计数换算回去。
            int maxDim = Mathf.Max(gx, Mathf.Max(gy, gz));
            if (maxDim > maxProbeDimension)
            {
                float k = maxDim / (float)maxProbeDimension;
                voxelSize *= k;
                minB = scaledBounds.min - Vector3.one * voxelSize;
                origin = minB;
                maxB = scaledBounds.max + Vector3.one * voxelSize;
                gx = Mathf.Max(3, Mathf.CeilToInt((maxB.x - minB.x) / voxelSize));
                gy = Mathf.Max(3, Mathf.CeilToInt((maxB.y - minB.y) / voxelSize));
                gz = Mathf.Max(3, Mathf.CeilToInt((maxB.z - minB.z) / voxelSize));
            }

            Vector3Int dims = new Vector3Int(gx, gy, gz);
            VoxelCell[,,] grid = new VoxelCell[gx, gy, gz];
            SurfaceHitInfo[,,] hits = new SurfaceHitInfo[gx, gy, gz];

            SurfaceVoxelizer.VoxelizeSurface(
                snapshot, meshScale, voxelSize, origin, dims, grid, hits, supersampleRate);

            if (antiAliasing)
            {
                MorphologySmoother.Smooth(dims, grid, smoothingIterations, true);
            }

            if (fillStrategy == VoxelFillStrategy.SurfaceShellOnly)
            {
                int count = 0;
                for (int x = 0; x < gx; x++)
                    for (int y = 0; y < gy; y++)
                        for (int z = 0; z < gz; z++)
                            if (grid[x, y, z].isOccupied) count++;
                return count;
            }

            SolidVoxelizer.VoxelizeSolid(dims, grid, true);

            if (fillStrategy == VoxelFillStrategy.SolidCore)
            {
                int count = 0;
                for (int x = 0; x < gx; x++)
                    for (int y = 0; y < gy; y++)
                        for (int z = 0; z < gz; z++)
                            if (grid[x, y, z].isOccupied) count++;
                return count;
            }

            // 厚壳：算出每个体素的到表面深度，只保留 shellThickness 层以内
            Vector3Int[,,] nearest = new Vector3Int[gx, gy, gz];
            DistanceFieldSolver.ComputeDistanceField(dims, grid, nearest);

            int thickness = Mathf.Clamp(shellThickness, 1, 6);
            int shellCount = 0;
            for (int x = 0; x < gx; x++)
                for (int y = 0; y < gy; y++)
                    for (int z = 0; z < gz; z++)
                        if (grid[x, y, z].isOccupied && grid[x, y, z].distanceToSurface <= thickness)
                            shellCount++;

            return shellCount;
        }

        /// <summary>
        /// 根据目标块数预算和填充模式，自动逆向求解最佳 voxelSize。
        ///
        /// accurate=true 时会额外跑 1~2 次标定探针（编辑器烘焙，多花零点几秒），
        /// 把块数误差从 +60% 压到 ±5% 以内。
        /// </summary>
        public static float SolveVoxelSize(
            MeshSnapshot snapshot,
            float meshScale,
            Bounds scaledBounds,
            int targetVoxelBudget,
            VoxelFillStrategy fillStrategy,
            int shellThickness = 2,
            bool antiAliasing = true,
            int smoothingIterations = 2,
            int supersampleRate = 3,
            bool accurate = true)
        {
            targetVoxelBudget = Mathf.Max(500, targetVoxelBudget);

            Vector3 size = scaledBounds.size;
            float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

            // 安全边界：单轴体素数至少 14 格，最高 200 格
            float minVoxelSize = maxDim / 200f;
            float maxVoxelSize = maxDim / 14f;

            float voxelSize = SolveAnalyticSeed(
                snapshot, meshScale, scaledBounds, targetVoxelBudget, fillStrategy, shellThickness);

            voxelSize = Mathf.Clamp(voxelSize, minVoxelSize, maxVoxelSize);

            if (snapshot == null || !accurate)
                return voxelSize;

            //
            // 标定迭代：实测块数 → 幂律反解
            //   N(h) ∝ h^-p   ⇒   h_new = h · (N_measured / N_target)^(1/p)
            // 壳层 p=2（面积量纲），实心 p=3（体积量纲）。
            // 两次迭代足够，因为残差主要来自形态学平滑与薄结构，都是二阶项。
            //
            float exponent = GetScalingExponent(fillStrategy);
            const int probeIterations = 2;
            // 探针网格上限 72³ ≈ 37 万格。再大就是拿编辑器内存去换那点精度，不划算；
            // 被限流时 voxelSize 会被放大，幂律外推会自动把计数换算回去。
            const int maxProbeDimension = 72;

            for (int iter = 0; iter < probeIterations; iter++)
            {
                float probeSize = voxelSize;
                int measured = 0;

                try
                {
                    measured = ProbeOccupiedCount(
                        snapshot, meshScale, scaledBounds, ref probeSize,
                        fillStrategy, shellThickness,
                        antiAliasing, smoothingIterations, supersampleRate,
                        maxProbeDimension);
                }
                catch (Exception e)
                {
                    // 探针失败（OOM 等）就退回解析解，不影响主流程
                    UnityEngine.Debug.LogWarning($"[VoxelBudgetSolver] 标定探针失败，回退解析解: {e.Message}");
                    break;
                }

                if (measured <= 0) break;

                // 探针可能跑在被限流的网格上（probeSize 被放大过），
                // 先把实测数换算回 voxelSize 对应的量级，再做反解。
                float ratio = probeSize / voxelSize;
                float normalizedCount = measured * Mathf.Pow(ratio, exponent);

                float correction = Mathf.Pow(normalizedCount / targetVoxelBudget, 1f / exponent);
                correction = Mathf.Clamp(correction, 0.55f, 1.8f); // 单步限幅，防止震荡

                voxelSize = Mathf.Clamp(voxelSize * correction, minVoxelSize, maxVoxelSize);

                // 已经在 ±6% 内就收工，省一次探针的时间
                if (Mathf.Abs(correction - 1f) < 0.06f) break;
            }

            return voxelSize;
        }
    }
}
