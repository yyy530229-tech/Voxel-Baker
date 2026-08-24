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

            InteriorStrategy strategy = profile != null ? profile.strategy : InteriorStrategy.NearestSurfaceMaterial;

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

                            Color32 finalColor = nearestSurf.customColor;
                            short hp = 1;

                            switch (strategy)
                            {
                                case InteriorStrategy.ExtendSurfaceColor:
                                case InteriorStrategy.NearestSurfaceMaterial:
                                    // 默认继承对应表面色块的颜色 (黄鸭内部即黄色，红屋顶内部即红色，彻底杜绝混入杂色！)
                                    finalColor = nearestSurf.customColor;
                                    grid[x, y, z].materialId = nearestSurf.materialId;
                                    hp = 1;
                                    break;

                                case InteriorStrategy.DominantMaterial:
                                    finalColor = profile != null ? (Color32)profile.defaultCoreColor : nearestSurf.customColor;
                                    hp = profile != null ? profile.defaultHP : (short)1;
                                    break;

                                case InteriorStrategy.ProceduralNoise:
                                    float freq = profile != null ? profile.noiseFrequency : 0.2f;
                                    float n = Mathf.PerlinNoise(x * freq + 0.1f, y * freq + z * freq * 0.5f);
                                    Color colA = profile != null ? profile.noiseColorA : Color.yellow;
                                    Color colB = profile != null ? profile.noiseColorB : new Color(0.9f, 0.4f, 0.1f);
                                    finalColor = Color.Lerp(colA, colB, n);
                                    hp = 1;
                                    break;

                                case InteriorStrategy.CustomProfileLayers:
                                    if (profile != null)
                                    {
                                        InteriorLayerRule rule = profile.GetRuleForDepth(depth);
                                        finalColor = rule.layerColor;
                                        hp = rule.initialHP;
                                        grid[x, y, z].materialId = (byte)rule.gameplayTag;
                                    }
                                    else
                                    {
                                        finalColor = nearestSurf.customColor;
                                        hp = 1;
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
