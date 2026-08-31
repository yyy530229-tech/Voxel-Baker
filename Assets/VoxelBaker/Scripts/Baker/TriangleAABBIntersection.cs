using UnityEngine;

namespace VoxelBaker.Baker
{
    /// <summary>
    /// 基于分离轴定理 (SAT) 与 最近表面正交投影 的高精度 3D 三角形相交与外观采样计算
    /// </summary>
    public static class TriangleAABBIntersection
    {
        public static bool TestOverlap(Vector3 boxCenter, Vector3 boxHalfExtents, Vector3 v0, Vector3 v1, Vector3 v2)
        {
            // 将坐标平移到以 Box 中心为原点的局部空间
            Vector3 tv0 = v0 - boxCenter;
            Vector3 tv1 = v1 - boxCenter;
            Vector3 tv2 = v2 - boxCenter;

            // 1. 测试 AABB 的 3 个坐标轴 (X, Y, Z)
            float minX = Mathf.Min(tv0.x, Mathf.Min(tv1.x, tv2.x));
            float maxX = Mathf.Max(tv0.x, Mathf.Max(tv1.x, tv2.x));
            if (minX > boxHalfExtents.x || maxX < -boxHalfExtents.x) return false;

            float minY = Mathf.Min(tv0.y, Mathf.Min(tv1.y, tv2.y));
            float maxY = Mathf.Max(tv0.y, Mathf.Max(tv1.y, tv2.y));
            if (minY > boxHalfExtents.y || maxY < -boxHalfExtents.y) return false;

            float minZ = Mathf.Min(tv0.z, Mathf.Min(tv1.z, tv2.z));
            float maxZ = Mathf.Max(tv0.z, Mathf.Max(tv1.z, tv2.z));
            if (minZ > boxHalfExtents.z || maxZ < -boxHalfExtents.z) return false;

            // 2. 测试三角形法线轴
            Vector3 e0 = tv1 - tv0;
            Vector3 e1 = tv2 - tv1;
            Vector3 e2 = tv0 - tv2;
            Vector3 normal = Vector3.Cross(e0, e1);
            float d = Vector3.Dot(normal, tv0);
            float r = boxHalfExtents.x * Mathf.Abs(normal.x) +
                      boxHalfExtents.y * Mathf.Abs(normal.y) +
                      boxHalfExtents.z * Mathf.Abs(normal.z);
            if (Mathf.Abs(d) > r) return false;

            // 3. 测试 9 个边叉乘轴
            if (!AxisTestX(e0.z, e0.y, tv0, tv1, tv2, boxHalfExtents)) return false;
            if (!AxisTestY(e0.z, e0.x, tv0, tv1, tv2, boxHalfExtents)) return false;
            if (!AxisTestZ(e0.y, e0.x, tv0, tv1, tv2, boxHalfExtents)) return false;

            if (!AxisTestX(e1.z, e1.y, tv0, tv1, tv2, boxHalfExtents)) return false;
            if (!AxisTestY(e1.z, e1.x, tv0, tv1, tv2, boxHalfExtents)) return false;
            if (!AxisTestZ(e1.y, e1.x, tv0, tv1, tv2, boxHalfExtents)) return false;

            if (!AxisTestX(e2.z, e2.y, tv0, tv1, tv2, boxHalfExtents)) return false;
            if (!AxisTestY(e2.z, e2.x, tv0, tv1, tv2, boxHalfExtents)) return false;
            if (!AxisTestZ(e2.y, e2.x, tv0, tv1, tv2, boxHalfExtents)) return false;

            return true;
        }

        private static bool AxisTestX(float ez, float ey, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 halfExtents)
        {
            float p0 = -ez * v0.y + ey * v0.z;
            float p1 = -ez * v1.y + ey * v1.z;
            float p2 = -ez * v2.y + ey * v2.z;
            float min = Mathf.Min(p0, Mathf.Min(p1, p2));
            float max = Mathf.Max(p0, Mathf.Max(p1, p2));
            float rad = Mathf.Abs(ez) * halfExtents.y + Mathf.Abs(ey) * halfExtents.z;
            return !(min > rad || max < -rad);
        }

        private static bool AxisTestY(float ez, float ex, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 halfExtents)
        {
            float p0 = ez * v0.x - ex * v0.z;
            float p1 = ez * v1.x - ex * v1.z;
            float p2 = ez * v2.x - ex * v2.z;
            float min = Mathf.Min(p0, Mathf.Min(p1, p2));
            float max = Mathf.Max(p0, Mathf.Max(p1, p2));
            float rad = Mathf.Abs(ez) * halfExtents.x + Mathf.Abs(ex) * halfExtents.z;
            return !(min > rad || max < -rad);
        }

        private static bool AxisTestZ(float ey, float ex, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 halfExtents)
        {
            float p0 = -ey * v0.x + ex * v0.y;
            float p1 = -ey * v1.x + ex * v1.y;
            float p2 = -ey * v2.x + ex * v2.y;
            float min = Mathf.Min(p0, Mathf.Min(p1, p2));
            float max = Mathf.Max(p0, Mathf.Max(p1, p2));
            float rad = Mathf.Abs(ey) * halfExtents.x + Mathf.Abs(ex) * halfExtents.y;
            return !(min > rad || max < -rad);
        }

        /// <summary>
        /// 计算空间任意点 P 在三角形 (A, B, C) 上的最近投影点，并返回严格处于 [0, 1] 内的重心坐标
        /// </summary>
        public static Vector3 ComputeBarycentricCoordinates(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 closestP = ClosestPointOnTriangle(p, a, b, c);

            Vector3 v0 = b - a, v1 = c - a, v2 = closestP - a;
            float d00 = Vector3.Dot(v0, v0);
            float d01 = Vector3.Dot(v0, v1);
            float d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0);
            float d21 = Vector3.Dot(v2, v1);
            float denom = d00 * d11 - d01 * d01;

            if (Mathf.Abs(denom) < 1e-7f)
            {
                return new Vector3(1f / 3f, 1f / 3f, 1f / 3f);
            }

            float v = Mathf.Clamp01((d11 * d20 - d01 * d21) / denom);
            float w = Mathf.Clamp01((d00 * d21 - d01 * d20) / denom);
            if (v + w > 1.0f)
            {
                float sum = v + w;
                v /= sum;
                w /= sum;
            }
            float u = Mathf.Clamp01(1.0f - v - w);
            return new Vector3(u, v, w);
        }

        public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = p - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + v * ab;
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return a + w * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + w * (c - b);
            }

            float denom = 1.0f / (va + vb + vc);
            float v2 = vb * denom;
            float w2 = vc * denom;
            return a + ab * v2 + ac * w2;
        }
    }
}
