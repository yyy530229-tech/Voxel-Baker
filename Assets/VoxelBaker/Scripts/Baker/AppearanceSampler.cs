using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Baker
{
    public static class AppearanceSampler
    {
        public static void SampleSurfaceAppearance(
            Mesh mesh,
            Material[] materials,
            Vector3Int gridDimensions,
            VoxelCell[,,] grid,
            SurfaceHitInfo[,,] surfaceHits,
            VoxelPalette palette)
        {
            int gx = gridDimensions.x;
            int gy = gridDimensions.y;
            int gz = gridDimensions.z;

            // 缓存材质贴图的 CPU 像素数据，避免重复读取
            Dictionary<int, Texture2D> readableTextures = new Dictionary<int, Texture2D>();
            if (materials != null)
            {
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && materials[i].mainTexture is Texture2D tex)
                    {
                        readableTextures[i] = GetReadableTexture(tex);
                    }
                }
            }

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

                            Material mat = (materials != null && matIdx >= 0 && matIdx < materials.Length) ? materials[matIdx] : null;
                            Color matColor = mat != null ? (mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : (mat.HasProperty("_Color") ? mat.color : Color.white)) : Color.white;

                            Color finalColor = matColor;

                            if (readableTextures.TryGetValue(matIdx, out Texture2D tex) && tex != null)
                            {
                                Vector2 uv = hit.uv;
                                // 处理 UV 重复/取模
                                float u = Mathf.Repeat(uv.x, 1.0f);
                                float v = Mathf.Repeat(uv.y, 1.0f);
                                Color texColor = tex.GetPixelBilinear(u, v);
                                finalColor = texColor * matColor;
                            }
                            else if (mesh.colors32 != null && mesh.colors32.Length > 0)
                            {
                                finalColor = (Color)hit.vertexColor * matColor;
                            }

                            Color32 c32 = finalColor;
                            grid[x, y, z].customColor = c32;
                            grid[x, y, z].paletteIndex = palette.AddOrFindColor(c32);
                        }
                    }
                }
            }
        }

        private static Texture2D GetReadableTexture(Texture2D source)
        {
            if (source == null) return null;

            try
            {
                // 测试是否直接可读
                source.GetPixel(0, 0);
                return source;
            }
            catch
            {
                // 若不可读，使用 RenderTexture 离屏复制一张可读贴图
                RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, rt);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;

                Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                readable.Apply();

                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                return readable;
            }
        }
    }
}
