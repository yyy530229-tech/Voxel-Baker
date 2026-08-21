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
        public float voxelSize = 0.1f;
        public bool fillInteriorSolid = true;
        public VoxelInteriorProfile interiorProfile;
        public int chunkSize = 16;
        public string assetName = "NewVoxelAsset";
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

            onProgress?.Invoke(0.05f, "Phase 1/9: Analyzing Mesh...");
            MeshAnalysisReport report = MeshAnalyzer.Analyze(settings.sourceMesh, settings.materials, settings.voxelSize);

            // 确定包围盒与带 1 格边距的局部原点及尺寸
            Bounds bounds = settings.sourceMesh.bounds;
            float voxelSize = settings.voxelSize > 0 ? settings.voxelSize : report.recommendedVoxelSize;
            
            // 边缘留出 1 格空隙用于外部 Flood Fill 连通
            Vector3 minBounds = bounds.min - Vector3.one * voxelSize;
            Vector3 maxBounds = bounds.max + Vector3.one * voxelSize;
            Vector3 localOrigin = minBounds;

            int gx = Mathf.Max(3, Mathf.CeilToInt((maxBounds.x - minBounds.x) / voxelSize));
            int gy = Mathf.Max(3, Mathf.CeilToInt((maxBounds.y - minBounds.y) / voxelSize));
            int gz = Mathf.Max(3, Mathf.CeilToInt((maxBounds.z - minBounds.z) / voxelSize));
            Vector3Int gridDimensions = new Vector3Int(gx, gy, gz);

            VoxelCell[,,] grid = new VoxelCell[gx, gy, gz];
            SurfaceHitInfo[,,] surfaceHits = new SurfaceHitInfo[gx, gy, gz];
            Vector3Int[,,] nearestSurfaceCoords = new Vector3Int[gx, gy, gz];

            VoxelPalette palette = ScriptableObject.CreateInstance<VoxelPalette>();
            palette.name = $"{settings.assetName}_Palette";

            // Phase 2: Surface Voxelization
            onProgress?.Invoke(0.20f, "Phase 2/9: Voxelizing Surface Mesh...");
            SurfaceVoxelizer.VoxelizeSurface(settings.sourceMesh, settings.materials, voxelSize, localOrigin, gridDimensions, grid, surfaceHits);

            // Phase 3: Solid Voxelization (Boundary 3D Flood Fill)
            onProgress?.Invoke(0.35f, "Phase 3/9: Solving Solid Interior (3D Flood Fill)...");
            SolidVoxelizer.VoxelizeSolid(gridDimensions, grid, settings.fillInteriorSolid);

            // Phase 4: Distance Field & Visual Layers
            onProgress?.Invoke(0.50f, "Phase 4/9: Computing Surface Distance Field & Layers...");
            DistanceFieldSolver.ComputeDistanceField(gridDimensions, grid, nearestSurfaceCoords);

            // Phase 5: Surface Appearance Sampling & Palette Generation
            onProgress?.Invoke(0.65f, "Phase 5/9: Sampling Textures & Generating Palette...");
            AppearanceSampler.SampleSurfaceAppearance(settings.sourceMesh, settings.materials, gridDimensions, grid, surfaceHits, palette);

            // Phase 6: Interior Color & Profile Generation
            onProgress?.Invoke(0.75f, "Phase 6/9: Generating Interior Structure & Profile...");
            InteriorGenerator.GenerateInterior(gridDimensions, grid, nearestSurfaceCoords, settings.interiorProfile, palette);

            // Phase 7: AO & 6-Face Mask Baking
            onProgress?.Invoke(0.85f, "Phase 7/9: Baking Ambient Occlusion & Face Masks...");
            AOFaceMaskBaker.BakeAOAndFaceMask(gridDimensions, grid);

            // Phase 8: Chunks, Initial Visible Set, and LODs
            onProgress?.Invoke(0.92f, "Phase 8/9: Spatial Chunk Partitioning & LOD Building...");
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

            // Phase 9: Assemble Asset & Statistics
            onProgress?.Invoke(0.98f, "Phase 9/9: Packaging VoxelAsset...");
            VoxelAsset asset = ScriptableObject.CreateInstance<VoxelAsset>();
            asset.name = settings.assetName;
            asset.sourceModelName = settings.sourceMesh.name;
            asset.boundsCenter = bounds.center;
            asset.boundsSize = bounds.size;
            asset.gridDimensions = gridDimensions;
            asset.voxelSize = voxelSize;
            asset.localOrigin = localOrigin;
            asset.chunkSize = settings.chunkSize;
            asset.palette = palette;
            asset.paletteTexture = palette.CreatePaletteTexture();
            asset.chunks = chunks;
            asset.initialVisibleVoxels = initialVisibleVoxels;
            asset.lods = lods;
            asset.interiorProfile = settings.interiorProfile;

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
            asset.totalVisibleVoxels = initialVisibleVoxels.Length;
            sw.Stop();
            asset.bakeDurationSeconds = (float)sw.Elapsed.TotalSeconds;

            onProgress?.Invoke(1.0f, $"Bake Complete in {asset.bakeDurationSeconds:F2}s! Total Voxels: {totalOccupied:N0}, Initial Visible: {initialVisibleVoxels.Length:N0}");
            UnityEngine.Debug.Log($"[VoxelBaker] Successfully baked '{asset.name}': {totalOccupied:N0} voxels ({totalSurface:N0} surface, {totalInterior:N0} interior), {chunks.Count} chunks, {initialVisibleVoxels.Length:N0} initial visible GPU instances in {asset.bakeDurationSeconds:F2}s.");

            return asset;
        }
    }
}
