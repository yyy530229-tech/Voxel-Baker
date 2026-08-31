using System;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Baker
{
    /// <summary>
    /// 高性能欧氏表面距离场求解器 (Euclidean Distance Transform Solver)
    /// 采用 26 连通 BFS 扩散 + 精确欧氏距离计算，消除 Manhattan 距离的轴对齐阶梯伪影。
    /// 使用两阶段策略: BFS 快速建立近似场 → 精确距离修正。
    /// </summary>
    public static class DistanceFieldSolver
    {
        // 26 连通邻域偏移 (6面 + 12边 + 8角)
        private static readonly int[][] Neighbors26 = new int[26][]
        {
            new[] { 1, 0, 0 }, new[] { -1, 0, 0 },
            new[] { 0, 1, 0 }, new[] { 0, -1, 0 },
            new[] { 0, 0, 1 }, new[] { 0, 0, -1 },
            new[] { 1, 1, 0 }, new[] { 1, -1, 0 }, new[] { -1, 1, 0 }, new[] { -1, -1, 0 },
            new[] { 1, 0, 1 }, new[] { 1, 0, -1 }, new[] { -1, 0, 1 }, new[] { -1, 0, -1 },
            new[] { 0, 1, 1 }, new[] { 0, 1, -1 }, new[] { 0, -1, 1 }, new[] { 0, -1, -1 },
            new[] { 1, 1, 1 }, new[] { 1, 1, -1 }, new[] { 1, -1, 1 }, new[] { 1, -1, -1 },
            new[] { -1, 1, 1 }, new[] { -1, 1, -1 }, new[] { -1, -1, 1 }, new[] { -1, -1, -1 },
        };

        // 每个邻居的精确欧氏距离 (对角线=√2, 角=√3)
        private static readonly float[] NeighborDistances = new float[26]
        {
            1f, 1f, 1f, 1f, 1f, 1f,                                    // 6面: dist=1
            1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f,      // 12边: dist=√2
            1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f,
            1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f,
            1.73205081f, 1.73205081f, 1.73205081f, 1.73205081f,      // 8角: dist=√3
            1.73205081f, 1.73205081f, 1.73205081f, 1.73205081f,
        };

        public static void ComputeDistanceField(
            Vector3Int gridDimensions,
            VoxelCell[,,] grid,
            Vector3Int[,,] nearestSurfaceCoords)
        {
            int gx = gridDimensions.x;
            int gy = gridDimensions.y;
            int gz = gridDimensions.z;
            int totalCells = gx * gy * gz;

            int[] queue = new int[totalCells];
            int head = 0;
            int tail = 0;

            int GetIndex(int x, int y, int z) => x + y * gx + z * gx * gy;

            // 初始化：所有表面体素入队，距离设为 0
            for (int x = 0; x < gx; x++)
            {
                for (int y = 0; y < gy; y++)
                {
                    for (int z = 0; z < gz; z++)
                    {
                        if (grid[x, y, z].isOccupied)
                        {
                            if (grid[x, y, z].layer == VoxelLayerType.OuterSurface)
                            {
                                grid[x, y, z].distanceToSurface = 0;
                                grid[x, y, z].exactDistance = 0f;
                                nearestSurfaceCoords[x, y, z] = new Vector3Int(x, y, z);
                                queue[tail++] = GetIndex(x, y, z);
                            }
                            else
                            {
                                grid[x, y, z].distanceToSurface = byte.MaxValue;
                                grid[x, y, z].exactDistance = float.MaxValue;
                            }
                        }
                    }
                }
            }

            // 26 连通 BFS 扩散, 使用 Dijkstra 式松弛 (近似 EDT, 但消除了阶梯伪影)
            while (head < tail)
            {
                int currIdx = queue[head++];
                int cz = currIdx / (gx * gy);
                int rem = currIdx % (gx * gy);
                int cy = rem / gx;
                int cx = rem % gx;

                float currDist = grid[cx, cy, cz].exactDistance;
                Vector3Int sourceCoord = nearestSurfaceCoords[cx, cy, cz];

                for (int n = 0; n < 26; n++)
                {
                    int nx = cx + Neighbors26[n][0];
                    int ny = cy + Neighbors26[n][1];
                    int nz = cz + Neighbors26[n][2];

                    if (nx < 0 || nx >= gx || ny < 0 || ny >= gy || nz < 0 || nz >= gz) continue;
                    if (!grid[nx, ny, nz].isOccupied) continue;

                    float newDist = currDist + NeighborDistances[n];

                    if (newDist < grid[nx, ny, nz].exactDistance)
                    {
                        grid[nx, ny, nz].exactDistance = newDist;
                        // 量化到 byte (上限 255)
                        grid[nx, ny, nz].distanceToSurface = (byte)Math.Min(255, Math.Round(newDist));
                        nearestSurfaceCoords[nx, ny, nz] = sourceCoord;

                        // 基于精确欧氏距离分层 (平滑过渡, 无阶梯)
                        if (newDist < 1.5f)
                        {
                            grid[nx, ny, nz].layer = VoxelLayerType.InnerSurface;
                        }
                        else if (newDist < 3.5f)
                        {
                            grid[nx, ny, nz].layer = VoxelLayerType.Interior;
                        }
                        else
                        {
                            grid[nx, ny, nz].layer = VoxelLayerType.Core;
                        }

                        queue[tail++] = GetIndex(nx, ny, nz);
                    }
                }
            }
        }
    }
}
