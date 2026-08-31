using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Baker
{
    public static class AppearanceSampler
    {
        /// <summary>
        /// 采样表面外观颜色。
        ///
        /// 入参是 MeshSnapshot 而非 Mesh/Material —— 与 SurfaceVoxelizer 同理，
        /// 为了能在后台线程运行：Material/Texture2D 的属性访问同样受主线程限制，
        /// 这里改用快照里预取好的 Color32[] 做纯 C# 双线性采样。
        ///
        /// palette 允许为 null（预览路径）：此时只写 customColor，不注册调色板索引。
        /// </summary>
        public static void SampleSurfaceAppearance(
            MeshSnapshot snapshot,
            Vector3Int gridDimensions,
            VoxelCell[,,] grid,
            SurfaceHitInfo[,,] surfaceHits,
            VoxelPalette palette,
            bool enableAntiAliasing = true)
        {
            if (snapshot == null) return;

            int gx = gridDimensions.x;
            int gy = gridDimensions.y;
            int gz = gridDimensions.z;

            bool hasVertexColor = snapshot.HasColor;

            for (int x = 0; x < gx; x++)
            {
                for (int y = 0; y < gy; y++)
                {
                    for (int z = 0; z < gz; z++)
                    {
                        if (grid[x, y, z].isOccupied && grid[x, y, z].layer == VoxelLayerType.OuterSurface)
                        {
                            SurfaceHitInfo hit = surfaceHits[x, y, z];
                            int matIdx = hit.subMeshIndex;

                            SubMeshSnapshot sub = (matIdx >= 0 && matIdx < snapshot.SubMeshes.Length)
                                ? snapshot.SubMeshes[matIdx]
                                : null;

                            Color matColor = sub != null ? sub.BaseColor : Color.white;
                            Color finalColor = matColor;

                            if (sub != null && sub.HasTexture)
                            {
                                Vector2 uv = hit.uv;
                                // 处理 UV 重复/取模
                                float u = uv.x - Mathf.Floor(uv.x);
                                float v = uv.y - Mathf.Floor(uv.y);

                                TextureSnapshot tex = sub.Texture;
                                Color texColor;
                                if (enableAntiAliasing && tex.Width > 2 && tex.Height > 2)
                                {
                                    // MSAA: 2x2 超采样平均，消除颜色锯齿
                                    float eU = 0.5f / tex.Width;
                                    float eV = 0.5f / tex.Height;
                                    Color c1 = tex.SampleBilinear(Wrap01(u - eU), Wrap01(v - eV));
                                    Color c2 = tex.SampleBilinear(Wrap01(u + eU), Wrap01(v - eV));
                                    Color c3 = tex.SampleBilinear(Wrap01(u - eU), Wrap01(v + eV));
                                    Color c4 = tex.SampleBilinear(Wrap01(u + eU), Wrap01(v + eV));
                                    texColor = (c1 + c2 + c3 + c4) * 0.25f;
                                }
                                else
                                {
                                    texColor = tex.SampleBilinear(u, v);
                                }
                                finalColor = texColor * matColor;
                            }
                            else if (hasVertexColor)
                            {
                                finalColor = (Color)hit.vertexColor * matColor;
                            }

                            Color32 c32 = finalColor;
                            grid[x, y, z].customColor = c32;
                            if (palette != null)
                                grid[x, y, z].paletteIndex = palette.AddOrFindColor(c32);
                        }
                    }
                }
            }
        }

        private static float Wrap01(float v)
        {
            v -= Mathf.Floor(v);
            // 浮点误差可能让 1-ε 落到 1.0，取一次模保证落在 [0,1)
            return v >= 1f ? v - 1f : v;
        }

    }
}
