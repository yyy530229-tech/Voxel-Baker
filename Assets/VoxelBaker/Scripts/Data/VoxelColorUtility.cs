using System;
using UnityEngine;

namespace VoxelBaker.Data
{
    /// <summary>
    /// 纯通用体素色彩数学与空间转换工具库 (Generic Voxel Color Utility)
    /// 职责：提供色彩距离计算、色相感知聚类、亮度和对比度计算，无任何特定业务假定
    /// </summary>
    public static class VoxelColorUtility
    {
        /// <summary>
        /// 判断两个 Color32 是否在欧氏距离容差范围内匹配
        /// </summary>
        public static bool IsColorMatching(Color32 a, Color32 b, float tolerance = 48f)
        {
            float dr = a.r - b.r;
            float dg = a.g - b.g;
            float db = a.b - b.b;
            return (dr * dr + dg * dg + db * db) <= (tolerance * tolerance);
        }

        /// <summary>
        /// 获取色彩的相对感知亮度 (Perceived Luminance 0.0 ~ 1.0)
        /// </summary>
        public static float GetLuminance(Color32 c)
        {
            return (c.r * 0.299f + c.g * 0.587f + c.b * 0.114f) / 255f;
        }

        /// <summary>
        /// 通用色相空间家族分类 Key (基于 HSV 纯数学度量，用于将任意模型中的体素自适应归类)
        /// </summary>
        public static int GetHueFamilyKey(Color32 c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);

            // 极低饱和度（黑白灰系）：按明度细分为暗黑、中灰、纯白
            if (s < 0.18f)
            {
                if (v >= 0.70f) return 100; // 纯白/高亮浅色
                if (v <= 0.25f) return 101; // 纯黑/深暗色
                return 102;                 // 中度灰色
            }

            // 有彩色系：按色相分度（12个标准色相区间）
            float deg = h * 360f;
            int hueBucket = Mathf.FloorToInt((deg + 15f) / 30f) % 12;
            return hueBucket + 1;
        }

        /// <summary>
        /// 计算一组颜色的平均色 (Centroid Color)
        /// </summary>
        public static Color32 ComputeAverageColor(ReadOnlySpan<Color32> colors)
        {
            if (colors.Length == 0) return Color.white;

            long sumR = 0, sumG = 0, sumB = 0, sumA = 0;
            for (int i = 0; i < colors.Length; i++)
            {
                sumR += colors[i].r;
                sumG += colors[i].g;
                sumB += colors[i].b;
                sumA += colors[i].a;
            }

            int n = colors.Length;
            return new Color32((byte)(sumR / n), (byte)(sumG / n), (byte)(sumB / n), (byte)(sumA / n));
        }
    }
}
