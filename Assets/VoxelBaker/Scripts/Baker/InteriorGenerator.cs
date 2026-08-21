using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Baker
{
    public static class InteriorGenerator
    {
        public static void GenerateInterior(
            Vector3Int gridDimensions,
            VoxelCell[,,] grid,
            Vector3Int[,,] nearestSurfaceCoords,
            VoxelInteriorProfile profile,
            VoxelPalette palette)
        {
            int gx = gridDimensions.x;
            int gy = gridDimensions.y;
            int gz = gridDimensions.z;

            InteriorStrategy strategy = profile != null ? profile.strategy : InteriorStrategy.CustomProfileLayers;

            for (int x = 0; x < gx; x++)
            {
                for (int y = 0; y < gy; y++)
                {
                    for (int z = 0; z < gz; z++)
                    {
                        if (grid[x, y, z].isOccupied && grid[x, y, z].layer != VoxelLayerType.OuterSurface)
                        {
                            int depth = grid[x, y, z].distanceToSurface;
                            Vector3Int nearest = nearestSurfaceCoords[x, y, z];
                            VoxelCell nearestSurf = grid[nearest.x, nearest.y, nearest.z];

                            Color32 finalColor = Color.white;
                            short hp = 1;

                            switch (strategy)
                            {
                                case InteriorStrategy.ExtendSurfaceColor:
                                    // 继承表面颜色并稍作明暗衰减
                                    float shade = Mathf.Clamp01(1.0f - depth * 0.08f);
                                    Color c = nearestSurf.customColor;
                                    finalColor = new Color(c.r * shade, c.g * shade, c.b * shade, 1.0f);
                                    hp = (short)Mathf.Clamp(depth, 1, 5);
                                    break;

                                case InteriorStrategy.NearestSurfaceMaterial:
                                    finalColor = nearestSurf.customColor;
                                    grid[x, y, z].materialId = nearestSurf.materialId;
                                    hp = 1;
                                    break;

                                case InteriorStrategy.DominantMaterial:
                                    finalColor = profile != null ? (Color32)profile.defaultCoreColor : new Color32(200, 150, 100, 255);
                                    hp = profile != null ? profile.defaultHP : (short)1;
                                    break;

                                case InteriorStrategy.ProceduralNoise:
                                    float freq = profile != null ? profile.noiseFrequency : 0.2f;
                                    float n = Mathf.PerlinNoise(x * freq + 0.1f, y * freq + z * freq * 0.5f);
                                    Color colA = profile != null ? profile.noiseColorA : Color.yellow;
                                    Color colB = profile != null ? profile.noiseColorB : Color.red;
                                    finalColor = Color.Lerp(colA, colB, n);
                                    hp = (short)Mathf.Clamp(Mathf.RoundToInt(n * 3f) + 1, 1, 4);
                                    break;

                                case InteriorStrategy.CustomProfileLayers:
                                default:
                                    if (profile != null)
                                    {
                                        InteriorLayerRule rule = profile.GetRuleForDepth(depth);
                                        finalColor = rule.layerColor;
                                        hp = rule.initialHP;
                                        grid[x, y, z].materialId = (byte)rule.gameplayTag;
                                    }
                                    else
                                    {
                                        // 默认粉色/青色/绿色多层蛋糕结构（匹配参考图）
                                        if (depth == 1) finalColor = new Color32(255, 105, 180, 255);      // Hot Pink
                                        else if (depth <= 3) finalColor = new Color32(50, 230, 190, 255); // Cyan/Mint
                                        else finalColor = new Color32(120, 240, 50, 255);                  // Lime Green
                                        hp = (short)depth;
                                    }
                                    break;
                            }

                            grid[x, y, z].customColor = finalColor;
                            grid[x, y, z].initialHP = hp;
                            grid[x, y, z].currentHP = hp;
                            grid[x, y, z].paletteIndex = palette.AddOrFindColor(finalColor);
                        }
                    }
                }
            }
        }
    }
}
