using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Baker
{
    public static class DistanceFieldSolver
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

        public static void ComputeDistanceField(
            Vector3Int gridDimensions,
            VoxelCell[,,] grid,
            Vector3Int[,,] nearestSurfaceCoords)
        {
            int gx = gridDimensions.x;
            int gy = gridDimensions.y;
            int gz = gridDimensions.z;

            Queue<Vector3Int> queue = new Queue<Vector3Int>(gx * gy);

            // 初始化：将所有表面体素入队，距离设为0
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
                                nearestSurfaceCoords[x, y, z] = new Vector3Int(x, y, z);
                                queue.Enqueue(new Vector3Int(x, y, z));
                            }
                            else
                            {
                                grid[x, y, z].distanceToSurface = byte.MaxValue;
                            }
                        }
                    }
                }
            }

            // 3D BFS 扩散计算距离与最近表面来源
            while (queue.Count > 0)
            {
                Vector3Int curr = queue.Dequeue();
                byte currDist = grid[curr.x, curr.y, curr.z].distanceToSurface;
                Vector3Int sourceCoord = nearestSurfaceCoords[curr.x, curr.y, curr.z];

                for (int d = 0; d < 6; d++)
                {
                    Vector3Int n = curr + Directions6[d];
                    if (n.x >= 0 && n.x < gx && n.y >= 0 && n.y < gy && n.z >= 0 && n.z < gz)
                    {
                        if (grid[n.x, n.y, n.z].isOccupied)
                        {
                            byte newDist = (byte)Math.Min(255, currDist + 1);
                            if (newDist < grid[n.x, n.y, n.z].distanceToSurface)
                            {
                                grid[n.x, n.y, n.z].distanceToSurface = newDist;
                                nearestSurfaceCoords[n.x, n.y, n.z] = sourceCoord;

                                // 更新视觉层级分类
                                if (newDist == 1)
                                {
                                    grid[n.x, n.y, n.z].layer = VoxelLayerType.InnerSurface;
                                }
                                else if (newDist >= 4)
                                {
                                    grid[n.x, n.y, n.z].layer = VoxelLayerType.Core;
                                }
                                else
                                {
                                    grid[n.x, n.y, n.z].layer = VoxelLayerType.Interior;
                                }

                                queue.Enqueue(n);
                            }
                        }
                    }
                }
            }
        }
    }
}
