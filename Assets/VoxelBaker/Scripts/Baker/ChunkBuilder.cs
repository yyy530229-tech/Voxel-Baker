using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Baker
{
    public static class ChunkBuilder
    {
        public static void BuildChunksAndLODs(
            Vector3Int gridDimensions,
            float voxelSize,
            Vector3 localOrigin,
            int chunkSize,
            VoxelCell[,,] grid,
            List<VoxelChunkData> outChunks,
            out PackedVoxelGPU[] outInitialVisibleVoxels,
            List<VoxelLODData> outLODs)
        {
            outChunks.Clear();
            outLODs.Clear();

            int gx = gridDimensions.x;
            int gy = gridDimensions.y;
            int gz = gridDimensions.z;

            int chunkCountX = Mathf.CeilToInt((float)gx / chunkSize);
            int chunkCountY = Mathf.CeilToInt((float)gy / chunkSize);
            int chunkCountZ = Mathf.CeilToInt((float)gz / chunkSize);

            List<PackedVoxelGPU> visibleList = new List<PackedVoxelGPU>();
            int nextChunkId = 0;

            for (int cx = 0; cx < chunkCountX; cx++)
            {
                for (int cy = 0; cy < chunkCountY; cy++)
                {
                    for (int cz = 0; cz < chunkCountZ; cz++)
                    {
                        int minX = cx * chunkSize;
                        int minY = cy * chunkSize;
                        int minZ = cz * chunkSize;

                        int maxX = Mathf.Min(minX + chunkSize, gx);
                        int maxY = Mathf.Min(minY + chunkSize, gy);
                        int maxZ = Mathf.Min(minZ + chunkSize, gz);

                        List<VoxelCell> chunkCells = new List<VoxelCell>();
                        List<int> visibleIndices = new List<int>();

                        for (int x = minX; x < maxX; x++)
                        {
                            for (int y = minY; y < maxY; y++)
                            {
                                for (int z = minZ; z < maxZ; z++)
                                {
                                    if (grid[x, y, z].isOccupied)
                                    {
                                        int cellIndex = chunkCells.Count;
                                        VoxelCell cell = grid[x, y, z];
                                        chunkCells.Add(cell);

                                        // 如果有暴露的面，加入初始可见集合
                                        if (cell.faceMask != VoxelFaceMask.None)
                                        {
                                            visibleIndices.Add(cellIndex);

                                            // 打包 GPU 实例
                                            PackedVoxelGPU gpuVoxel = new PackedVoxelGPU
                                            {
                                                packedPosition = PackedVoxelGPU.PackPosition(x, y, z),
                                                packedAttributes = PackedVoxelGPU.PackAttributes(cell.paletteIndex, cell.layer, cell.ao, cell.faceMask),
                                                colorRGBA = PackedVoxelGPU.ColorToUInt(cell.customColor),
                                                voxelMeta = (uint)(cellIndex & 0xFFFF) | ((uint)(nextChunkId & 0xFFFF) << 16)
                                            };
                                            visibleList.Add(gpuVoxel);
                                        }
                                    }
                                }
                            }
                        }

                        if (chunkCells.Count > 0)
                        {
                            Vector3 chunkMinLocal = localOrigin + new Vector3(minX * voxelSize, minY * voxelSize, minZ * voxelSize);
                            Vector3 chunkMaxLocal = localOrigin + new Vector3(maxX * voxelSize, maxY * voxelSize, maxZ * voxelSize);
                            Bounds b = new Bounds((chunkMinLocal + chunkMaxLocal) * 0.5f, chunkMaxLocal - chunkMinLocal);

                            VoxelChunkData chunkData = new VoxelChunkData
                            {
                                chunkId = nextChunkId++,
                                chunkCoord = new Vector3Int(cx, cy, cz),
                                minGridPos = new Vector3Int(minX, minY, minZ),
                                chunkSize = chunkSize,
                                localBounds = b,
                                cells = chunkCells.ToArray(),
                                initialVisibleCellIndices = visibleIndices
                            };

                            outChunks.Add(chunkData);
                        }
                    }
                }
            }

            outInitialVisibleVoxels = visibleList.ToArray();

            // 构建 LOD1 (2x2x2 下采样)
            BuildLOD(grid, gridDimensions, voxelSize, localOrigin, 2, 1, outLODs);
            // 构建 LOD2 (4x4x4 下采样)
            BuildLOD(grid, gridDimensions, voxelSize, localOrigin, 4, 2, outLODs);
        }

        private static void BuildLOD(
            VoxelCell[,,] grid,
            Vector3Int gridDimensions,
            float baseVoxelSize,
            Vector3 localOrigin,
            int downscaleFactor,
            int lodLevel,
            List<VoxelLODData> outLODs)
        {
            int gx = gridDimensions.x;
            int gy = gridDimensions.y;
            int gz = gridDimensions.z;

            int lodGx = Mathf.CeilToInt((float)gx / downscaleFactor);
            int lodGy = Mathf.CeilToInt((float)gy / downscaleFactor);
            int lodGz = Mathf.CeilToInt((float)gz / downscaleFactor);

            float lodVoxelSize = baseVoxelSize * downscaleFactor;
            List<PackedVoxelGPU> lodVisible = new List<PackedVoxelGPU>();

            for (int lx = 0; lx < lodGx; lx++)
            {
                for (int ly = 0; ly < lodGy; ly++)
                {
                    for (int lz = 0; lz < lodGz; lz++)
                    {
                        int startX = lx * downscaleFactor;
                        int startY = ly * downscaleFactor;
                        int startZ = lz * downscaleFactor;

                        int occupiedCount = 0;
                        int rSum = 0, gSum = 0, bSum = 0;
                        ushort firstPalette = 0;
                        bool hasVisible = false;

                        for (int dx = 0; dx < downscaleFactor; dx++)
                        {
                            for (int dy = 0; dy < downscaleFactor; dy++)
                            {
                                for (int dz = 0; dz < downscaleFactor; dz++)
                                {
                                    int ox = startX + dx;
                                    int oy = startY + dy;
                                    int oz = startZ + dz;
                                    if (ox < gx && oy < gy && oz < gz)
                                    {
                                        if (grid[ox, oy, oz].isOccupied)
                                        {
                                            occupiedCount++;
                                            Color32 c = grid[ox, oy, oz].customColor;
                                            rSum += c.r;
                                            gSum += c.g;
                                            bSum += c.b;
                                            firstPalette = grid[ox, oy, oz].paletteIndex;
                                            if (grid[ox, oy, oz].faceMask != VoxelFaceMask.None)
                                            {
                                                hasVisible = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // 若该下采样格子包含体素且在表面可见
                        if (occupiedCount > 0 && hasVisible)
                        {
                            Color32 avgCol = new Color32(
                                (byte)(rSum / occupiedCount),
                                (byte)(gSum / occupiedCount),
                                (byte)(bSum / occupiedCount),
                                255
                            );

                            PackedVoxelGPU lodVoxel = new PackedVoxelGPU
                            {
                                packedPosition = PackedVoxelGPU.PackPosition(lx * downscaleFactor, ly * downscaleFactor, lz * downscaleFactor),
                                packedAttributes = PackedVoxelGPU.PackAttributes(firstPalette, VoxelLayerType.OuterSurface, 200, VoxelFaceMask.AllFaces),
                                colorRGBA = PackedVoxelGPU.ColorToUInt(avgCol),
                                voxelMeta = (uint)lodLevel
                            };
                            lodVisible.Add(lodVoxel);
                        }
                    }
                }
            }

            VoxelLODData lodData = new VoxelLODData
            {
                lodLevel = lodLevel,
                voxelSize = lodVoxelSize,
                dimensions = new Vector3Int(lodGx, lodGy, lodGz),
                visibleVoxels = lodVisible.ToArray()
            };
            outLODs.Add(lodData);
        }
    }
}
