using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Baker
{
    public static class AOFaceMaskBaker
    {
        // AO 曲线参数（见 BakeAOAndFaceMask 内的详细说明）
        private const int AoFloor = 14;  // 邻居数 ≤ 此值视为完全开放，不压暗
        private const int AoCeil = 24;   // 邻居数 ≥ 此值视为最深遮蔽
        private const int AoRange = 50;  // 最大压暗量（255 制），约 20% 亮度

        public static void BakeAOAndFaceMask(
            Vector3Int gridDimensions,
            VoxelCell[,,] grid)
        {
            int gx = gridDimensions.x;
            int gy = gridDimensions.y;
            int gz = gridDimensions.z;

            for (int x = 0; x < gx; x++)
            {
                for (int y = 0; y < gy; y++)
                {
                    for (int z = 0; z < gz; z++)
                    {
                        if (grid[x, y, z].isOccupied)
                        {
                            // 1. 计算 6 面暴露掩码 (FaceMask)
                            VoxelFaceMask mask = VoxelFaceMask.None;

                            if (x == gx - 1 || !grid[x + 1, y, z].isOccupied) mask |= VoxelFaceMask.PosX;
                            if (x == 0 || !grid[x - 1, y, z].isOccupied) mask |= VoxelFaceMask.NegX;

                            if (y == gy - 1 || !grid[x, y + 1, z].isOccupied) mask |= VoxelFaceMask.PosY;
                            if (y == 0 || !grid[x, y - 1, z].isOccupied) mask |= VoxelFaceMask.NegY;

                            if (z == gz - 1 || !grid[x, y, z + 1].isOccupied) mask |= VoxelFaceMask.PosZ;
                            if (z == 0 || !grid[x, y, z - 1].isOccupied) mask |= VoxelFaceMask.NegZ;

                            grid[x, y, z].faceMask = mask;

                            // 2. 计算 26 邻居 Ambient Occlusion (AO)
                            int neighborCount = 0;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                for (int dy = -1; dy <= 1; dy++)
                                {
                                    for (int dz = -1; dz <= 1; dz++)
                                    {
                                        if (dx == 0 && dy == 0 && dz == 0) continue;
                                        int nx = x + dx;
                                        int ny = y + dy;
                                        int nz = z + dz;
                                        if (nx >= 0 && nx < gx && ny >= 0 && ny < gy && nz >= 0 && nz < gz)
                                        {
                                            if (grid[nx, ny, nz].isOccupied)
                                            {
                                                neighborCount++;
                                            }
                                        }
                                    }
                                }
                            }

                            //
                            // 关键修正：旧公式为 255 - neighborCount * 210 / 26，
                            // 可见表面体素的 neighborCount 大致在 11~22 之间跳动，
                            // 换算下来相邻体素 AO 能差 40~90/255（约 15%~35% 亮度），
                            // 于是整片模型浮现出一张高对比的明暗网格 ——
                            // 这正是用户看到的"体素之间有缝 / 有锯齿感"。
                            //
                            // 新做法：保留 26 邻域（凹腔检测更准），
                            // 但把压暗量压缩到一个很窄的窗口内平滑过渡：
                            //   · 邻居数 ≤ AoFloor  → 完全开放，不压暗
                            //   · 邻居数 ≥ AoCeil   → 最深遮蔽，压暗 AoRange
                            // 相邻体素之间最多只差几个百分点，肉眼是连续过渡而非硬边。
                            //
                            float t = Mathf.Clamp01((float)(neighborCount - AoFloor) / (AoCeil - AoFloor));
                            int aoVal = 255 - Mathf.RoundToInt(t * AoRange);
                            grid[x, y, z].ao = (byte)Mathf.Clamp(aoVal, 255 - AoRange, 255);
                        }
                    }
                }
            }
        }
    }
}
