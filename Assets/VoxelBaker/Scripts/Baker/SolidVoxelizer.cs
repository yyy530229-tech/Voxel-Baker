using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Baker
{
    /// <summary>
    /// 高性能 3D 边界泛洪实体化填充器 (High-Performance Solid Voxelizer)
    /// 采用扁平化内存队列与紧凑寻址，毫秒级快速填充闭合内部实体
    /// </summary>
    public static class SolidVoxelizer
    {
        public static void VoxelizeSolid(
            Vector3Int gridDimensions,
            VoxelCell[,,] grid,
            bool fillInterior = true)
        {
            if (!fillInterior) return;

            int gx = gridDimensions.x;
            int gy = gridDimensions.y;
            int gz = gridDimensions.z;
            int totalCells = gx * gy * gz;

            // 0. 壳层补洞 (Hole Closing)：迭代填补外壳 1~2 格宽的孔洞，
            //    确保表面壳层封闭，外部 Flood Fill 不会漏进内部导致实体空洞。
            CloseShellHoles(grid, gx, gy, gz);

            bool[] isOutside = new bool[totalCells];
            int[] queue = new int[totalCells];
            int head = 0;
            int tail = 0;

            // 辅助寻址函数
            int GetIndex(int x, int y, int z) => x + y * gx + z * gx * gy;

            void TryEnqueue(int x, int y, int z)
            {
                int idx = GetIndex(x, y, z);
                if (!grid[x, y, z].isOccupied && !isOutside[idx])
                {
                    isOutside[idx] = true;
                    queue[tail++] = idx;
                }
            }

            // 1. 将网格外部 6 个包围面上的非表面体素加入泛洪队列
            for (int x = 0; x < gx; x++)
            {
                for (int y = 0; y < gy; y++)
                {
                    TryEnqueue(x, y, 0);
                    TryEnqueue(x, y, gz - 1);
                }
            }

            for (int x = 0; x < gx; x++)
            {
                for (int z = 0; z < gz; z++)
                {
                    TryEnqueue(x, 0, z);
                    TryEnqueue(x, gy - 1, z);
                }
            }

            for (int y = 0; y < gy; y++)
            {
                for (int z = 0; z < gz; z++)
                {
                    TryEnqueue(0, y, z);
                    TryEnqueue(gx - 1, y, z);
                }
            }

            // 2. 扁平化极速 3D BFS 边界泛洪
            while (head < tail)
            {
                int currIdx = queue[head++];
                int cz = currIdx / (gx * gy);
                int rem = currIdx % (gx * gy);
                int cy = rem / gx;
                int cx = rem % gx;

                // 6 邻域探索
                if (cx + 1 < gx) TryEnqueue(cx + 1, cy, cz);
                if (cx - 1 >= 0) TryEnqueue(cx - 1, cy, cz);
                if (cy + 1 < gy) TryEnqueue(cx, cy + 1, cz);
                if (cy - 1 >= 0) TryEnqueue(cx, cy - 1, cz);
                if (cz + 1 < gz) TryEnqueue(cx, cy, cz + 1);
                if (cz - 1 >= 0) TryEnqueue(cx, cy, cz - 1);
            }

            // 3. 所有未连通到外界的封闭非表面体素全部标记为内部实体 (Interior)
            for (int x = 0; x < gx; x++)
            {
                for (int y = 0; y < gy; y++)
                {
                    for (int z = 0; z < gz; z++)
                    {
                        int idx = GetIndex(x, y, z);
                        if (!grid[x, y, z].isOccupied && !isOutside[idx])
                        {
                            grid[x, y, z].gridPos = new Vector3Int(x, y, z);
                            grid[x, y, z].isOccupied = true;
                            grid[x, y, z].layer = VoxelLayerType.Interior;
                            grid[x, y, z].distanceToSurface = 1;
                            grid[x, y, z].isAlive = true;
                            grid[x, y, z].initialHP = 1;
                            grid[x, y, z].currentHP = 1;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 壳层补洞：迭代标记 6 邻域中 ≥4 个已占据的空 cell 为内部候选并填充。
        /// 用于封闭表面壳层上的 1~2 格宽孔洞（薄壁、对角阶梯处的缝隙），
        /// 使外部 Flood Fill 无法渗入模型内部，从而保证实体填充无空洞。
        /// </summary>
        private static void CloseShellHoles(VoxelCell[,,] grid, int gx, int gy, int gz)
        {
            bool[,,] toFill = new bool[gx, gy, gz];

            for (int pass = 0; pass < 3; pass++)
            {
                bool any = false;

                // 收集本轮的待填充 cell（原子应用，避免级联扩张）
                for (int x = 1; x < gx - 1; x++)
                {
                    for (int y = 1; y < gy - 1; y++)
                    {
                        for (int z = 1; z < gz - 1; z++)
                        {
                            if (grid[x, y, z].isOccupied || toFill[x, y, z]) continue;

                            int count = 0;
                            if (grid[x + 1, y, z].isOccupied || toFill[x + 1, y, z]) count++;
                            if (grid[x - 1, y, z].isOccupied || toFill[x - 1, y, z]) count++;
                            if (grid[x, y + 1, z].isOccupied || toFill[x, y + 1, z]) count++;
                            if (grid[x, y - 1, z].isOccupied || toFill[x, y - 1, z]) count++;
                            if (grid[x, y, z + 1].isOccupied || toFill[x, y, z + 1]) count++;
                            if (grid[x, y, z - 1].isOccupied || toFill[x, y, z - 1]) count++;

                            if (count >= 4)
                            {
                                toFill[x, y, z] = true;
                                any = true;
                            }
                        }
                    }
                }

                if (!any) break;
            }

            // 应用补洞（不覆盖表面层语义，后续 DistanceField 会重新分层）
            for (int x = 0; x < gx; x++)
            {
                for (int y = 0; y < gy; y++)
                {
                    for (int z = 0; z < gz; z++)
                    {
                        if (!toFill[x, y, z]) continue;

                        VoxelCell cell = grid[x, y, z];
                        if (!cell.isOccupied)
                        {
                            cell.gridPos = new Vector3Int(x, y, z);
                            cell.isOccupied = true;
                            cell.layer = VoxelLayerType.Interior;
                            cell.distanceToSurface = 1;
                            cell.isAlive = true;
                            cell.initialHP = 1;
                            cell.currentHP = 1;
                            grid[x, y, z] = cell;
                        }
                    }
                }
            }
        }
    }
}
