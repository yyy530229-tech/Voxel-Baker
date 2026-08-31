using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Baker
{
    /// <summary>
    /// 形态学平滑器 (Morphological Smoother)
    /// 通过膨胀-腐蚀操作消除表面锯齿、填充 1-cell 空洞、移除孤立毛刺体素。
    /// 相当于图像形态学的开/闭运算，使体素模型轮廓更光滑、更贴合原始网格外形。
    /// </summary>
    public static class MorphologySmoother
    {
        /// <summary>
        /// 执行形态学平滑: 先膨胀(闭运算填充空洞) 再腐蚀(开运算移除毛刺)
        /// </summary>
        /// <param name="preserveSurfaceColor">是否在平滑时保留表面原始颜色（填充体素继承邻近表面色）</param>
        public static void Smooth(Vector3Int gridDimensions, VoxelCell[,,] grid, int iterations = 1, bool preserveSurfaceColor = true)
        {
            for (int i = 0; i < iterations; i++)
            {
                Dilate(gridDimensions, grid);
                Erode(gridDimensions, grid);
            }
        }

        /// <summary>
        /// 膨胀: 6 邻域中 >=4 个 occupied 则填充空 cell (闭运算，填充表面小空洞)
        /// </summary>
        private static void Dilate(Vector3Int gridDimensions, VoxelCell[,,] grid)
        {
            int gx = gridDimensions.x;
            int gy = gridDimensions.y;
            int gz = gridDimensions.z;

            // 标记需要填充的 cell (不直接修改，避免级联)
            bool[,,] toFill = new bool[gx, gy, gz];

            for (int x = 1; x < gx - 1; x++)
            {
                for (int y = 1; y < gy - 1; y++)
                {
                    for (int z = 1; z < gz - 1; z++)
                    {
                        if (grid[x, y, z].isOccupied) continue;

                        int neighborCount = CountOccupied6(grid, x, y, z, gx, gy, gz);
                        if (neighborCount >= 4)
                        {
                            toFill[x, y, z] = true;
                        }
                    }
                }
            }

            // 应用填充
            for (int x = 0; x < gx; x++)
            {
                for (int y = 0; y < gy; y++)
                {
                    for (int z = 0; z < gz; z++)
                    {
                        if (toFill[x, y, z])
                        {
                            // 从最近的表面邻居继承属性
                            InheritFromNearestSurface(grid, x, y, z, gx, gy, gz);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 腐蚀: 6 邻域中 <=1 个 occupied 的表面 cell 移除 (开运算，去除孤立毛刺)
        /// 仅移除表面层低覆盖率体素，保护内部填充
        /// </summary>
        private static void Erode(Vector3Int gridDimensions, VoxelCell[,,] grid)
        {
            int gx = gridDimensions.x;
            int gy = gridDimensions.y;
            int gz = gridDimensions.z;

            bool[,,] toRemove = new bool[gx, gy, gz];

            for (int x = 1; x < gx - 1; x++)
            {
                for (int y = 1; y < gy - 1; y++)
                {
                    for (int z = 1; z < gz - 1; z++)
                    {
                        if (!grid[x, y, z].isOccupied) continue;
                        if (grid[x, y, z].layer != VoxelLayerType.OuterSurface) continue;

                        int neighborCount = CountOccupied6(grid, x, y, z, gx, gy, gz);

                        // 孤立或近孤立体素 (<=1 邻居) 且覆盖率低 → 移除
                        if (neighborCount <= 1 && grid[x, y, z].surfaceCoverage < 64)
                        {
                            toRemove[x, y, z] = true;
                        }
                        // 薄毛刺 (<=2 邻居) 且覆盖率极低 → 移除
                        else if (neighborCount <= 2 && grid[x, y, z].surfaceCoverage < 32)
                        {
                            toRemove[x, y, z] = true;
                        }
                    }
                }
            }

            // 应用移除
            for (int x = 0; x < gx; x++)
            {
                for (int y = 0; y < gy; y++)
                {
                    for (int z = 0; z < gz; z++)
                    {
                        if (toRemove[x, y, z])
                        {
                            grid[x, y, z] = VoxelCell.Empty;
                            grid[x, y, z].gridPos = new Vector3Int(x, y, z);
                        }
                    }
                }
            }
        }

        private static int CountOccupied6(VoxelCell[,,] grid, int x, int y, int z, int gx, int gy, int gz)
        {
            int count = 0;
            if (x + 1 < gx && grid[x + 1, y, z].isOccupied) count++;
            if (x - 1 >= 0 && grid[x - 1, y, z].isOccupied) count++;
            if (y + 1 < gy && grid[x, y + 1, z].isOccupied) count++;
            if (y - 1 >= 0 && grid[x, y - 1, z].isOccupied) count++;
            if (z + 1 < gz && grid[x, y, z + 1].isOccupied) count++;
            if (z - 1 >= 0 && grid[x, y, z - 1].isOccupied) count++;
            return count;
        }

        /// <summary>
        /// 从最近的表面邻居继承颜色和材质属性
        /// </summary>
        private static void InheritFromNearestSurface(VoxelCell[,,] grid, int x, int y, int z, int gx, int gy, int gz)
        {
            Color32 bestColor = new Color32(128, 128, 128, 255);
            byte bestMaterialId = 0;
            byte bestCoverage = 128; // 填充的 cell 给中等覆盖率
            bool found = false;

            int[][] dirs = new int[][]
            {
                new[] { 1, 0, 0 }, new[] { -1, 0, 0 },
                new[] { 0, 1, 0 }, new[] { 0, -1, 0 },
                new[] { 0, 0, 1 }, new[] { 0, 0, -1 },
            };

            foreach (var dir in dirs)
            {
                int nx = x + dir[0], ny = y + dir[1], nz = z + dir[2];
                if (nx < 0 || nx >= gx || ny < 0 || ny >= gy || nz < 0 || nz >= gz) continue;
                if (!grid[nx, ny, nz].isOccupied) continue;
                if (grid[nx, ny, nz].layer != VoxelLayerType.OuterSurface) continue;

                bestColor = grid[nx, ny, nz].customColor;
                bestMaterialId = grid[nx, ny, nz].materialId;
                found = true;
                break;
            }

            if (!found)
            {
                // 搜索 26 邻域
                for (int dx = -1; dx <= 1 && !found; dx++)
                {
                    for (int dy = -1; dy <= 1 && !found; dy++)
                    {
                        for (int dz = -1; dz <= 1 && !found; dz++)
                        {
                            if (dx == 0 && dy == 0 && dz == 0) continue;
                            int nx = x + dx, ny = y + dy, nz = z + dz;
                            if (nx < 0 || nx >= gx || ny < 0 || ny >= gy || nz < 0 || nz >= gz) continue;
                            if (!grid[nx, ny, nz].isOccupied) continue;

                            bestColor = grid[nx, ny, nz].customColor;
                            bestMaterialId = grid[nx, ny, nz].materialId;
                            found = true;
                        }
                    }
                }
            }

            grid[x, y, z].gridPos = new Vector3Int(x, y, z);
            grid[x, y, z].isOccupied = true;
            grid[x, y, z].layer = VoxelLayerType.OuterSurface;
            grid[x, y, z].distanceToSurface = 0;
            grid[x, y, z].materialId = bestMaterialId;
            grid[x, y, z].isAlive = true;
            grid[x, y, z].initialHP = 1;
            grid[x, y, z].currentHP = 1;
            grid[x, y, z].surfaceCoverage = bestCoverage;
            grid[x, y, z].customColor = bestColor;
            grid[x, y, z].exactDistance = 0f;
        }
    }
}
