using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Baker
{
    /// <summary>
    /// 体素表面颜色聚类量化器 (K-Means Color Quantizer)
    /// 将逐体素采样的近真实纹理颜色压平为少数几种纯色块，
    /// 实现乐高积木般干净利落的平色块外观，消除相邻体素的颜色抖动。
    /// </summary>
    public static class VoxelColorQuantizer
    {
        /// <summary>
        /// 对表面层体素执行 K-Means 颜色聚类量化并重建调色板。
        /// 量化后每个表面体素的 customColor / paletteIndex 均指向最近的纯色中心。
        /// </summary>
        public static void QuantizeSurfaceColors(
            Vector3Int gridDimensions,
            VoxelCell[,,] grid,
            VoxelPalette palette,
            int targetColorCount,
            float tolerance = 20f)
        {
            // 复用纯数据版本，避免同一套 K-Means 逻辑维护两份
            List<Color32> paletteOut = new List<Color32>();
            QuantizeSurfaceColors(gridDimensions, grid, paletteOut, targetColorCount, tolerance);

            if (palette == null || paletteOut.Count == 0) return;

            palette.entries.Clear();
            for (int i = 0; i < paletteOut.Count; i++)
            {
                palette.entries.Add(VoxelPaletteEntry.Default(paletteOut[i], $"Color_{i}"));
            }
            palette.ClearLookupCache();
        }

        /// <summary>
        /// 纯数据版本：输出到一个普通的 Color32 列表，不碰任何 UnityEngine 对象，线程安全。
        /// 供后台线程的实时预览使用。
        /// </summary>
        public static void QuantizeSurfaceColors(
            Vector3Int gridDimensions,
            VoxelCell[,,] grid,
            List<Color32> paletteOut,
            int targetColorCount,
            float tolerance = 20f)
        {
            int gx = gridDimensions.x;
            int gy = gridDimensions.y;
            int gz = gridDimensions.z;

            // 1. 收集表面颜色与出现频次（去重，避免对数十万体素做重复聚类）
            var colorCounts = new Dictionary<Color32, int>();
            var surfaceCells = new List<Vector3Int>();
            var surfaceColors = new List<Color32>();

            for (int x = 0; x < gx; x++)
            {
                for (int y = 0; y < gy; y++)
                {
                    for (int z = 0; z < gz; z++)
                    {
                        if (grid[x, y, z].isOccupied && grid[x, y, z].layer == VoxelLayerType.OuterSurface)
                        {
                            surfaceCells.Add(new Vector3Int(x, y, z));
                            Color32 c = grid[x, y, z].customColor;
                            surfaceColors.Add(c);
                            colorCounts.TryGetValue(c, out int n);
                            colorCounts[c] = n + 1;
                        }
                    }
                }
            }

            if (surfaceCells.Count == 0) return;

            int uniqueCount = colorCounts.Count;
            int K = Mathf.Clamp(targetColorCount, 2, Mathf.Min(uniqueCount, 256));
            if (uniqueCount <= K + 2) return; // 颜色已经足够少，无需量化

            // 2. K-Means 聚类
            var uniqueColors = new List<Color32>(colorCounts.Keys);
            var weights = new List<int>(colorCounts.Count);
            for (int i = 0; i < uniqueColors.Count; i++) weights.Add(colorCounts[uniqueColors[i]]);

            Color32[] centroids = RunKMeans(uniqueColors, weights, K, tolerance);

            // 3. 输出 K 个纯色聚类中心（纯数据，不依赖 ScriptableObject）
            paletteOut.Clear();
            paletteOut.AddRange(centroids);

            // 4. 重映射所有表面体素
            for (int i = 0; i < surfaceCells.Count; i++)
            {
                Vector3Int p = surfaceCells[i];
                int idx = NearestCentroid(surfaceColors[i], centroids);
                grid[p.x, p.y, p.z].customColor = centroids[idx];
                grid[p.x, p.y, p.z].paletteIndex = (ushort)idx;
            }
        }

        private static Color32[] RunKMeans(List<Color32> colors, List<int> weights, int K, float tolerance)
        {
            // 初始化：按频次降序贪心选取互相差异足够大的颜色作为初始中心
            var initOrder = new List<int>(colors.Count);
            for (int i = 0; i < colors.Count; i++) initOrder.Add(i);
            initOrder.Sort((a, b) => weights[b].CompareTo(weights[a]));

            var centroids = new Color32[K];
            int taken = 0;
            var chosen = new List<Color32>();
            float initThresh = (tolerance * 0.75f) * (tolerance * 0.75f); // 距离为平方和，换算为通道差阈值
            for (int i = 0; i < initOrder.Count && taken < K; i++)
            {
                Color32 c = colors[initOrder[i]];
                bool tooClose = false;
                for (int j = 0; j < chosen.Count; j++)
                {
                    if (ColorDiff(chosen[j], c) <= initThresh) { tooClose = true; break; }
                }
                if (!tooClose)
                {
                    chosen.Add(c);
                    taken++;
                }
            }
            // 兜底：仍不足 K 个时用频次最高颜色补齐
            for (int i = 0; i < initOrder.Count && taken < K; i++)
            {
                Color32 c = colors[initOrder[i]];
                bool exists = false;
                for (int j = 0; j < chosen.Count; j++) { if (ColorDiff(chosen[j], c) < 1f) { exists = true; break; } }
                if (!exists) { chosen.Add(c); taken++; }
            }
            for (int i = 0; i < K; i++) centroids[i] = (i < chosen.Count) ? chosen[i] : colors[i % colors.Count];

            // Lloyd 迭代
            var sumR = new long[K];
            var sumG = new long[K];
            var sumB = new long[K];
            var count = new int[K];

            for (int iter = 0; iter < 18; iter++)
            {
                for (int k = 0; k < K; k++) { sumR[k] = sumG[k] = sumB[k] = 0; count[k] = 0; }

                for (int i = 0; i < colors.Count; i++)
                {
                    int best = NearestCentroid(colors[i], centroids);
                    int w = weights[i];
                    sumR[best] += (long)colors[i].r * w;
                    sumG[best] += (long)colors[i].g * w;
                    sumB[best] += (long)colors[i].b * w;
                    count[best] += w;
                }

                bool moved = false;
                for (int k = 0; k < K; k++)
                {
                    if (count[k] > 0)
                    {
                        Color32 newC = new Color32(
                            (byte)(sumR[k] / count[k]),
                            (byte)(sumG[k] / count[k]),
                            (byte)(sumB[k] / count[k]),
                            255);
                        if (ColorDiff(newC, centroids[k]) > 0.5f) moved = true;
                        centroids[k] = newC;
                    }
                    else if (iter < 5)
                    {
                        // 空簇：重置为最大离群点，避免簇退化
                        int farIdx = 0;
                        float far = float.MinValue;
                        for (int i = 0; i < colors.Count; i++)
                        {
                            float d = ColorDiff(colors[i], centroids[NearestCentroid(colors[i], centroids)]);
                            if (d > far) { far = d; farIdx = i; }
                        }
                        centroids[k] = colors[farIdx];
                        moved = true;
                    }
                }

                if (!moved) break;
            }

            return centroids;
        }

        private static int NearestCentroid(Color32 c, Color32[] centroids)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < centroids.Length; i++)
            {
                float d = ColorDiff(c, centroids[i]);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private static float ColorDiff(Color32 a, Color32 b)
        {
            float dr = a.r - b.r;
            float dg = a.g - b.g;
            float db = a.b - b.b;
            return dr * dr + dg * dg + db * db;
        }
    }
}
