using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Baker
{
    public static class SolidVoxelizer
    {
        private static readonly Vector3Int[] Directions6 = new Vector3Int[]
        {
            new Vector3Int( 1,  0,  0),
            new Vector3Int(-1,  0,  0),
            new Vector3Int( 0,  1,  0),
            new Vector3Int( 0, -1,  0),
            new Vector3Int( 0,  0,  1),
            new Vector3Int( 0,  0, -1)
        };

        public static void VoxelizeSolid(
            Vector3Int gridDimensions,
            VoxelCell[,,] grid,
            bool fillInterior = true)
        {
            if (!fillInterior) return;

            int gx = gridDimensions.x;
            int gy = gridDimensions.y;
            int gz = gridDimensions.z;

            bool[,,] isOutside = new bool[gx, gy, gz];
            Queue<Vector3Int> queue = new Queue<Vector3Int>(gx * gy);

            // 1. 将网格外部 6 个包围面上的非表面体素加入泛洪队列
            for (int x = 0; x < gx; x++)
            {
                for (int y = 0; y < gy; y++)
                {
                    TryEnqueueBoundary(x, y, 0, grid, isOutside, queue);
                    TryEnqueueBoundary(x, y, gz - 1, grid, isOutside, queue);
                }
            }

            for (int x = 0; x < gx; x++)
            {
                for (int z = 0; z < gz; z++)
                {
                    TryEnqueueBoundary(x, 0, z, grid, isOutside, queue);
                    TryEnqueueBoundary(x, gy - 1, z, grid, isOutside, queue);
                }
            }

            for (int y = 0; y < gy; y++)
            {
                for (int z = 0; z < gz; z++)
                {
                    TryEnqueueBoundary(0, y, z, grid, isOutside, queue);
                    TryEnqueueBoundary(gx - 1, y, z, grid, isOutside, queue);
                }
            }

            // 2. 3D BFS 边界泛洪
            while (queue.Count > 0)
            {
                Vector3Int curr = queue.Dequeue();

                for (int d = 0; d < 6; d++)
                {
                    Vector3Int n = curr + Directions6[d];
                    if (n.x >= 0 && n.x < gx && n.y >= 0 && n.y < gy && n.z >= 0 && n.z < gz)
                    {
                        // 如果未被标记为Outside且不是表面体素
                        if (!isOutside[n.x, n.y, n.z] && !grid[n.x, n.y, n.z].isOccupied)
                        {
                            isOutside[n.x, n.y, n.z] = true;
                            queue.Enqueue(n);
                        }
                    }
                }
            }

            // 3. 所有既不是表面又未连通到外界的体素，全部标记为内部实体 (Interior)
            for (int x = 0; x < gx; x++)
            {
                for (int y = 0; y < gy; y++)
                {
                    for (int z = 0; z < gz; z++)
                    {
                        if (!grid[x, y, z].isOccupied && !isOutside[x, y, z])
                        {
                            grid[x, y, z].gridPos = new Vector3Int(x, y, z);
                            grid[x, y, z].isOccupied = true;
                            grid[x, y, z].layer = VoxelLayerType.Interior;
                            grid[x, y, z].distanceToSurface = 1; // 稍后由 DistanceFieldSolver 精确计算
                            grid[x, y, z].isAlive = true;
                            grid[x, y, z].initialHP = 1;
                            grid[x, y, z].currentHP = 1;
                        }
                    }
                }
            }
        }

        private static void TryEnqueueBoundary(int x, int y, int z, VoxelCell[,,] grid, bool[,,] isOutside, Queue<Vector3Int> queue)
        {
            if (!isOutside[x, y, z] && !grid[x, y, z].isOccupied)
            {
                isOutside[x, y, z] = true;
                queue.Enqueue(new Vector3Int(x, y, z));
            }
        }
    }
}
