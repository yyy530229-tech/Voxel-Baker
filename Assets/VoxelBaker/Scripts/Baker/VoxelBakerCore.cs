using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Baker
{
    public class VoxelBakeSettings
    {
        public Mesh sourceMesh;
        public Material[] materials;

        // 尺度与尺寸控制
        public float targetModelHeight = 3.0f; // 标准高度（米）
        public bool autoCalculateVoxelSize = true; // 是否基于目标块数预算自动推导体素尺寸
        public int targetVoxelBudget = 6000; // 目标体素总块数预算 (1,000 ~ 50,000)
        public float voxelSize = 0f; // 自定义体素尺寸 (米)

        // 乐高式平色块外观
        // 24 色 ≈ 一套 LEGO 基础色板。色数越多越像"贴了贴图的模型"，
        // 越难呈现乐高那种干净利落的纯色积木感。
        public int paletteColorCount = 24; // 目标调色板颜色数 (量化后表面呈现的纯色块数量)
        public float paletteTolerance = 24f; // 颜色聚类初始容差 (通道差值)

        //
        // 结构与填充策略
        //
        // 默认从 SolidCore 改为 SurfaceShellOnly —— 这是"低块数 + 高清"最关键的一步。
        //
        // 同一个 6000 块预算下，三种策略的差别（3m 高熊猫，表面积 ≈ 12m²）：
        //   SolidCore        h = (V/6000)^(1/3) ≈ 0.078m → 长轴 38 格，可见块仅 ~2000
        //   ThickHollowShell h = √(2S/6000)     ≈ 0.067m → 长轴 44 格，可见块 ~3000
        //   SurfaceShellOnly h = √(S/6000)      ≈ 0.045m → 长轴 67 格，可见块 6000
        //
        // SolidCore 把三分之二的预算埋在永远看不见的内部实心里，
        // 表面分辨率被白白砍掉 40%。单层壳把每一块都花在刀刃上。
        //
        public VoxelFillStrategy fillStrategy = VoxelFillStrategy.SurfaceShellOnly;
        public bool fillInteriorSolid
        {
            get => fillStrategy == VoxelFillStrategy.SolidCore;
            set => fillStrategy = value ? VoxelFillStrategy.SolidCore : VoxelFillStrategy.SurfaceShellOnly;
        }
        public int shellThickness = 2; // 厚壳层模式下的壳厚度 (层)
        public VoxelInteriorProfile interiorProfile;

        // 抗锯齿与细腻度画质
        //
        // 「低块数 + 高清」的关键是：把块数预算全部花在刀刃上，
        // 用更精确的表面覆盖率 + 形态学平滑把阶梯状轮廓修干净，
        // 而不是靠堆块数去硬磨。
        //
        public bool enableAntiAliasing = true; // 是否开启形态学抗锯齿平滑 (闭运算填孔 + 开运算去毛刺)

        // 平滑迭代等级。默认 1，不是 2。
        //
        // 每多迭代一次就多抹掉一格宽的凹凸 —— 2 次在 67 格长轴上等于吃掉 3% 的细节，
        // 耳朵、爪子这类细小结构会先被糊掉。
        // 用户要的是"细腻"而不是"抹平"，阶梯感本来就是体素风格的一部分，
        // 所以这里只做 1 次柔和闭运算：填掉单格的锯齿缺口，但保住细小结构。
        public int smoothingIterations = 1;

        public int supersampleRate = 3; // 空间超采样 (1=整格单点, 2=2x2x2=8子盒, 3=3x3x3=27子盒最细腻)

        // 空间分块与资产
        public int chunkSize = 16;
        public string assetName = "NewVoxelAsset";
        public int maxGridDimension = 140; // 安全阀防 OOM
    }

    public static class VoxelBakerCore
    {
        public static VoxelAsset Bake(VoxelBakeSettings settings, Action<float, string> onProgress = null)
        {
            if (settings.sourceMesh == null)
            {
                UnityEngine.Debug.LogError("[VoxelBaker] Source Mesh cannot be null!");
                return null;
            }

            Stopwatch sw = Stopwatch.StartNew();

            onProgress?.Invoke(0.05f, "Phase 1/9: Analyzing Mesh Geometry...");
            Bounds rawBounds = settings.sourceMesh.bounds;
            float rawMaxDim = Mathf.Max(rawBounds.size.x, Mathf.Max(rawBounds.size.y, rawBounds.size.z));
            float targetH = settings.targetModelHeight > 0 ? settings.targetModelHeight : 3.0f;
            float meshScale = (rawMaxDim > 0.0001f) ? (targetH / rawMaxDim) : 1.0f;

            Bounds scaledBounds = new Bounds(rawBounds.center * meshScale, rawBounds.size * meshScale);

            //
            // 把 Mesh 与材质一次性冻结为纯数据快照（主线程）。
            // 之后所有求解器 —— 包括预算求解器 —— 都只吃这份快照，不再触碰 UnityEngine 对象。
            // 这样整条烘焙链路与实时预览链路共用完全相同的求解器代码，
            // 既保证预览与最终结果一致，也让预览能原样搬到后台线程跑。
            //
            MeshSnapshot snapshot = MeshSnapshot.Capture(settings.sourceMesh, settings.materials);
            if (snapshot == null)
            {
                UnityEngine.Debug.LogError("[VoxelBaker] Source Mesh 快照捕获失败，烘焙中止！");
                return null;
            }

            // 1. 求解体素尺寸
            float voxelSize = settings.voxelSize;
            if (settings.autoCalculateVoxelSize || voxelSize <= 0.0001f)
            {
                voxelSize = VoxelBudgetSolver.SolveVoxelSize(
                    snapshot,
                    meshScale,
                    scaledBounds,
                    settings.targetVoxelBudget,
                    settings.fillStrategy,
                    settings.shellThickness,
                    settings.enableAntiAliasing,
                    settings.smoothingIterations,
                    settings.supersampleRate,
                    true // 精确标定：多跑 1~2 次探针，把块数误差压到 ±5%
                );
            }

            // 边缘留出 1 格空隙用于外部 Flood Fill 连通
            Vector3 minBounds = scaledBounds.min - Vector3.one * voxelSize;
            Vector3 maxBounds = scaledBounds.max + Vector3.one * voxelSize;
            Vector3 localOrigin = minBounds;

            int gx = Mathf.Max(3, Mathf.CeilToInt((maxBounds.x - minBounds.x) / voxelSize));
            int gy = Mathf.Max(3, Mathf.CeilToInt((maxBounds.y - minBounds.y) / voxelSize));
            int gz = Mathf.Max(3, Mathf.CeilToInt((maxBounds.z - minBounds.z) / voxelSize));

            // 安全阀限制
            int maxDim = Mathf.Max(gx, Mathf.Max(gy, gz));
            if (maxDim > settings.maxGridDimension)
            {
                float scale = (float)settings.maxGridDimension / maxDim;
                voxelSize /= scale;
                gx = Mathf.Max(3, Mathf.CeilToInt((maxBounds.x - minBounds.x) / voxelSize));
                gy = Mathf.Max(3, Mathf.CeilToInt((maxBounds.y - minBounds.y) / voxelSize));
                gz = Mathf.Max(3, Mathf.CeilToInt((maxBounds.z - minBounds.z) / voxelSize));
            }

            Vector3Int gridDimensions = new Vector3Int(gx, gy, gz);
            UnityEngine.Debug.Log($"[VoxelBakerCore] 烘焙启动: grid={gx}x{gy}x{gz}, voxelSize={voxelSize:F4}m, 预算={settings.targetVoxelBudget}, 模式={settings.fillStrategy}");

            VoxelCell[,,] grid = new VoxelCell[gx, gy, gz];
            SurfaceHitInfo[,,] surfaceHits = new SurfaceHitInfo[gx, gy, gz];
            Vector3Int[,,] nearestSurfaceCoords = new Vector3Int[gx, gy, gz];

            VoxelPalette palette = ScriptableObject.CreateInstance<VoxelPalette>();
            palette.name = $"{settings.assetName}_Palette";
            palette.ClearLookupCache();

            // Phase 2: Surface Voxelization (2x2x2 supersampled)
            onProgress?.Invoke(0.18f, "Phase 2/9: Voxelizing Surface Mesh (Supersampled)...");
            SurfaceVoxelizer.VoxelizeSurface(
                snapshot,
                meshScale,
                voxelSize,
                localOrigin,
                gridDimensions,
                grid,
                surfaceHits,
                settings.supersampleRate
            );

            // Phase 2.5: Anti-Aliasing Morphology Smoothing (消除大锯齿)
            if (settings.enableAntiAliasing)
            {
                onProgress?.Invoke(0.28f, "Phase 3/9: Anti-Aliasing Morphology Smoothing (消锯齿)...");
                MorphologySmoother.Smooth(gridDimensions, grid, settings.smoothingIterations, true);
            }

            // Phase 3: Solid Voxelization (3D Flood Fill)
            if (settings.fillStrategy != VoxelFillStrategy.SurfaceShellOnly)
            {
                onProgress?.Invoke(0.38f, "Phase 4/9: Solving Solid Interior (3D Flood Fill)...");
                SolidVoxelizer.VoxelizeSolid(gridDimensions, grid, true);
            }

            // Phase 4: Distance Field
            onProgress?.Invoke(0.50f, "Phase 5/9: Computing Surface Distance Field...");
            DistanceFieldSolver.ComputeDistanceField(gridDimensions, grid, nearestSurfaceCoords);

            // Phase 5: Surface Appearance Sampling with MSAA & O(1) Fast Palette
            onProgress?.Invoke(0.64f, "Phase 6/9: Fast MSAA Appearance Sampling (O(1) Palette)...");
            AppearanceSampler.SampleSurfaceAppearance(
                snapshot,
                gridDimensions,
                grid,
                surfaceHits,
                palette,
                settings.enableAntiAliasing
            );

            // Phase 5.5: K-Means 颜色量化压平 (乐高式纯色块，消除色斑噪声)
            onProgress?.Invoke(0.70f, "Phase 6.5/9: K-Means Flattening to LEGO Flat Colors...");
            VoxelColorQuantizer.QuantizeSurfaceColors(
                gridDimensions,
                grid,
                palette,
                settings.paletteColorCount,
                settings.paletteTolerance
            );

            // Phase 6: Interior Generation (with Shell Thickness / Hollow)
            onProgress?.Invoke(0.76f, "Phase 7/9: Generating Interior Structure & Shell Layers...");
            InteriorGenerator.GenerateInterior(
                gridDimensions,
                grid,
                nearestSurfaceCoords,
                settings.interiorProfile,
                palette,
                settings.fillStrategy,
                settings.shellThickness
            );

            // Phase 7: AO & Face Masks
            onProgress?.Invoke(0.85f, "Phase 8/9: Baking Ambient Occlusion & Face Masks...");
            AOFaceMaskBaker.BakeAOAndFaceMask(gridDimensions, grid);

            // Phase 8: Chunks, Initial Visible Set, and LODs
            onProgress?.Invoke(0.93f, "Phase 9/9: Building Chunks & GPU Visibility...");
            List<VoxelChunkData> chunks = new List<VoxelChunkData>();
            PackedVoxelGPU[] initialVisibleVoxels;
            List<VoxelLODData> lods = new List<VoxelLODData>();

            ChunkBuilder.BuildChunksAndLODs(
                gridDimensions,
                voxelSize,
                localOrigin,
                settings.chunkSize,
                grid,
                chunks,
                out initialVisibleVoxels,
                lods
            );

            // Phase 9: Assemble Asset
            VoxelAsset asset = ScriptableObject.CreateInstance<VoxelAsset>();
            asset.name = settings.assetName;
            asset.sourceModelName = settings.sourceMesh.name;
            asset.boundsCenter = scaledBounds.center;
            asset.boundsSize = scaledBounds.size;
            asset.gridDimensions = gridDimensions;
            asset.voxelSize = voxelSize;
            asset.localOrigin = localOrigin;
            asset.palette = palette;
            asset.chunks = chunks;
            asset.initialVisibleVoxels = initialVisibleVoxels;
            asset.lods = lods;

            // 统计指标
            int totalOccupied = 0;
            int totalSurface = 0;
            int totalInterior = 0;

            for (int x = 0; x < gx; x++)
            {
                for (int y = 0; y < gy; y++)
                {
                    for (int z = 0; z < gz; z++)
                    {
                        if (grid[x, y, z].isOccupied)
                        {
                            totalOccupied++;
                            if (grid[x, y, z].layer == VoxelLayerType.OuterSurface) totalSurface++;
                            else totalInterior++;
                        }
                    }
                }
            }

            asset.totalOccupiedVoxels = totalOccupied;
            asset.totalSurfaceVoxels = totalSurface;
            asset.totalInteriorVoxels = totalInterior;
            asset.totalVisibleVoxels = initialVisibleVoxels != null ? initialVisibleVoxels.Length : 0;
            asset.bakeDurationSeconds = (float)sw.Elapsed.TotalSeconds;

            sw.Stop();

            int longestAxis = Mathf.Max(gx, Mathf.Max(gy, gz));
            float budgetError = settings.targetVoxelBudget > 0
                ? (totalOccupied - settings.targetVoxelBudget) * 100f / settings.targetVoxelBudget
                : 0f;
            float visibleRatio = totalOccupied > 0 ? totalSurface * 100f / totalOccupied : 0f;

            UnityEngine.Debug.Log(
                $"[VoxelBakerCore] ✓ 烘焙成功！\n" +
                $"  · 网格        : {gx}×{gy}×{gz}（长轴 {longestAxis} 格，voxelSize={voxelSize:F4}m）\n" +
                $"  · 总块数      : {totalOccupied:N0}（目标 {settings.targetVoxelBudget:N0}，偏差 {budgetError:+#;-#;0}%）\n" +
                $"  · 可见占比    : {totalSurface:N0}/{totalOccupied:N0} = {visibleRatio:F0}%（内部被埋的块: {totalInterior:N0}）\n" +
                $"  · 首帧可见    : {asset.totalVisibleVoxels:N0}\n" +
                $"  · 调色板      : {palette.Count} 色\n" +
                $"  · 填充策略    : {settings.fillStrategy}（壳厚 {settings.shellThickness}）\n" +
                $"  · 耗时        : {asset.bakeDurationSeconds:F2}s");

            if (visibleRatio < 60f && settings.fillStrategy == VoxelFillStrategy.SolidCore)
            {
                UnityEngine.Debug.LogWarning(
                    $"[VoxelBakerCore] 有 {100f - visibleRatio:F0}% 的块埋在看不见的内部。" +
                    $"同样的预算改成「单层壳」能把表面分辨率提高约 {Mathf.Sqrt(totalOccupied / (float)Mathf.Max(1, totalSurface)):F2}×。");
            }

            return asset;
        }

    }
}
