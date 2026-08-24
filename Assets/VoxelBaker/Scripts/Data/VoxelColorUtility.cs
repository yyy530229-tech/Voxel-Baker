using UnityEngine;

namespace VoxelBaker.Data
{
    /// <summary>
    /// 体素色彩核心工具库 (提供色相感知匹配、欧氏距离计算及色彩命名)
    /// </summary>
    public static class VoxelColorUtility
    {
        /// <summary>
        /// 判断两个颜色是否在容差范围内相近
        /// </summary>
        public static bool IsColorMatching(Color32 a, Color32 b, float tolerance = 65f)
        {
            float dr = a.r - b.r;
            float dg = a.g - b.g;
            float db = a.b - b.b;
            return (dr * dr + dg * dg + db * db) <= (tolerance * tolerance);
        }

        /// <summary>
        /// 色相感知(HSV)智能特征分类
        /// </summary>
        public static int GetHueFamilyKey(Color32 c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);

            // 无彩色系：纯白、深黑、浅灰
            if (s < 0.18f)
            {
                return (v >= 0.55f) ? 100 : 101;
            }

            // 有彩色系：按色相分度
            float deg = h * 360f;
            if (deg >= 55f && deg <= 165f) return 1;  // 🌿 绿色色系 (Green)
            if (deg >= 25f && deg < 55f)   return 2;  // 💛 黄色色系 (Yellow)
            if (deg >= 165f && deg <= 260f) return 3; // 💙 蓝色色系 (Blue)
            if (deg >= 260f && deg <= 330f) return 4; // 💜 粉紫色系 (Pink / Purple)
            return 5;                                 // ❤️ 橙红色系 (Red / Orange)
        }

        /// <summary>
        /// 获取色彩的用户友好显示名称
        /// </summary>
        public static string GetColorName(Color32 c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);

            if (s < 0.18f)
            {
                if (v >= 0.85f) return "纯白 (Pure White)";
                if (v >= 0.50f) return "浅灰 (Light Gray)";
                return "炭黑 (Charcoal Black)";
            }

            float deg = h * 360f;
            if (deg >= 55f && deg <= 165f) return "绿色 (Green)";
            if (deg >= 25f && deg < 55f)   return "明黄 (Bright Yellow)";
            if (deg >= 165f && deg <= 260f) return "湛蓝 (Ocean Blue)";
            if (deg >= 260f && deg <= 330f) return "粉紫 (Pink / Purple)";
            return "暖红 (Warm Red)";
        }
    }
}

namespace VoxelGameFramework.Core
{
    // 向后兼容命名空间别名
    public static class VoxelColorUtility
    {
        public static bool IsColorMatching(Color32 a, Color32 b, float tolerance = 65f) => VoxelBaker.Data.VoxelColorUtility.IsColorMatching(a, b, tolerance);
        public static int GetHueFamilyKey(Color32 c) => VoxelBaker.Data.VoxelColorUtility.GetHueFamilyKey(c);
        public static string GetColorName(Color32 c) => VoxelBaker.Data.VoxelColorUtility.GetColorName(c);
    }
}
