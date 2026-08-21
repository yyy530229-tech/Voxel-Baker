using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Baker
{
    public static class AOFaceMaskBaker
    {
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

                            // 邻居越多越暗，255为完全明亮
                            int aoVal = 255 - (neighborCount * 210 / 26);
                            grid[x, y, z].ao = (byte)Mathf.Clamp(aoVal, 50, 255);
                        }
                    }
                }
            }
        }
    }
}
