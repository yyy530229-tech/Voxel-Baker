using UnityEngine;

namespace VoxelGameFramework.Core
{
    public static class VoxelColorUtility
    {
        public static bool IsColorMatching(Color32 a, Color32 b, float tolerance = 65f)
        {
            // 计算 RGB 欧氏距离
            float dr = a.r - b.r;
            float dg = a.g - b.g;
            float db = a.b - b.b;
            float dist = Mathf.Sqrt(dr * dr + dg * dg + db * db);
            return dist <= tolerance;
        }

        public static string GetColorName(Color32 c)
        {
            if (c.r > 180 && c.g < 80 && c.b < 80) return "红色 (Red)";
            if (c.r > 200 && c.g > 180 && c.b < 50) return "黄色 (Yellow)";
            if (c.r < 80 && c.g > 150 && c.b > 200) return "青色 (Cyan)";
            if (c.r < 80 && c.g < 120 && c.b > 180) return "蓝色 (Blue)";
            if (c.r > 120 && c.g < 80 && c.b > 150) return "紫色 (Purple)";
            if (c.r > 100 && c.g > 60 && c.b < 40) return "棕色 (Brown)";
            if (c.r > 200 && c.g > 100 && c.b < 50) return "橙色 (Orange)";
            if (c.r > 220 && c.g < 100 && c.b > 150) return "粉色 (Pink)";
            return "彩色";
        }
    }
}
