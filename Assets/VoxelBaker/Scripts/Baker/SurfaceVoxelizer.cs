using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Baker
{
    public struct SurfaceHitInfo
    {
        public int subMeshIndex;
        public int triangleIndex;
        public Vector2 uv;
        public Vector3 normal;
        public Color32 vertexColor;
    }

    public static class SurfaceVoxelizer
    {
        //
        // 超采样点集定义
        //
        // 重要修正：采样盒子的半边长必须与采样步长严格匹配。
        // 旧实现里 8 个采样点各自偏移 ±0.25 格，却都使用 0.5 格的半边长，
        // 于是并集跨度达到 1.5 个格子 —— 体素模型被整体"膨胀"了 50%，
        // 表现为块数虚高、轮廓发福、阶梯状锯齿严重。
        //
        // 正确做法：把 cell 等分成 N×N×N 个子盒，每个采样点负责一个子盒，
        // 子盒并集 = 原 cell，既不膨胀也不漏采，
        // 同时 hitCount/N 就是真实的子体素覆盖率（可直接用于抗锯齿判断）。
        //

        // 2×2×2 = 8 子盒，半边长 0.25 格
        private static readonly Vector3[] SupersampleOffsets2x2x2 = new Vector3[]
        {
            new Vector3(-0.25f, -0.25f, -0.25f),
            new Vector3( 0.25f, -0.25f, -0.25f),
            new Vector3(-0.25f,  0.25f, -0.25f),
            new Vector3( 0.25f,  0.25f, -0.25f),
            new Vector3(-0.25f, -0.25f,  0.25f),
            new Vector3( 0.25f, -0.25f,  0.25f),
            new Vector3(-0.25f,  0.25f,  0.25f),
            new Vector3( 0.25f,  0.25f,  0.25f),
        };

        // 3×3×3 = 27 子盒，半边长 1/6 格（更细腻的轮廓覆盖率）
        private static readonly Vector3[] SupersampleOffsets3x3x3 = BuildOffsets3x3x3();

        private static Vector3[] BuildOffsets3x3x3()
        {
            float[] t = { -1f / 3f, 0f, 1f / 3f };
            var list = new Vector3[27];
            int i = 0;
            for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++)
                    for (int c = 0; c < 3; c++)
                        list[i++] = new Vector3(t[a], t[b], t[c]);
            return list;
        }

        /// <summary>
        /// 体素化表面。
        ///
        /// 入参是 MeshSnapshot 而不是 Mesh —— 这是为了让整条管线能在后台线程跑。
        /// Mesh.vertices / GetTriangles 这些属性访问都带主线程断言，
        /// 换成快照里的普通数组之后，本方法就变成纯计算，线程安全。
        /// </summary>
        public static void VoxelizeSurface(
            MeshSnapshot snapshot,
            float meshScale,
            float voxelSize,
            Vector3 gridOrigin,
            Vector3Int gridDimensions,
            VoxelCell[,,] grid,
            SurfaceHitInfo[,,] surfaceHits,
            int supersampleRate = 2)
        {
            if (snapshot == null) return;

            Vector3[] vertices = snapshot.Vertices;
            Vector2[] uvs = snapshot.UVs;
            Color32[] colors = snapshot.Colors;
            Vector3[] normals = snapshot.Normals;
            bool hasUV = snapshot.HasUV;
            bool hasColor = snapshot.HasColor;
            bool hasNormal = snapshot.HasNormal;

            // 超采样点集 (rate=1 时整格单点; rate=2 时 2×2×2=8 子盒; rate>=3 时 3×3×3=27 子盒)
            Vector3[] sampleOffsets;
            float sampleHalfExtent;

            if (supersampleRate <= 1)
            {
                sampleOffsets = new Vector3[] { Vector3.zero };
                sampleHalfExtent = voxelSize * 0.5f;
            }
            else if (supersampleRate == 2)
            {
                sampleOffsets = SupersampleOffsets2x2x2;
                sampleHalfExtent = voxelSize * 0.25f;
            }
            else
            {
                sampleOffsets = SupersampleOffsets3x3x3;
                sampleHalfExtent = voxelSize / 6f;
            }

            Vector3 halfExtents = Vector3.one * sampleHalfExtent;
            int sampleCount = sampleOffsets.Length;

            int subMeshCount = snapshot.SubMeshes.Length;
            for (int subIdx = 0; subIdx < subMeshCount; subIdx++)
            {
                int[] triangles = snapshot.SubMeshes[subIdx].Triangles;
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    int i0 = triangles[t];
                    int i1 = triangles[t + 1];
                    int i2 = triangles[t + 2];

                    // 将顶点缩放到归一化游戏坐标空间
                    Vector3 v0 = vertices[i0] * meshScale;
                    Vector3 v1 = vertices[i1] * meshScale;
                    Vector3 v2 = vertices[i2] * meshScale;

                    // 计算三角形在归一化坐标空间的包围盒范围
                    Vector3 minPos = Vector3.Min(v0, Vector3.Min(v1, v2));
                    Vector3 maxPos = Vector3.Max(v0, Vector3.Max(v1, v2));

                    int minX = Mathf.Clamp(Mathf.FloorToInt((minPos.x - gridOrigin.x) / voxelSize), 0, gridDimensions.x - 1);
                    int minY = Mathf.Clamp(Mathf.FloorToInt((minPos.y - gridOrigin.y) / voxelSize), 0, gridDimensions.y - 1);
                    int minZ = Mathf.Clamp(Mathf.FloorToInt((minPos.z - gridOrigin.z) / voxelSize), 0, gridDimensions.z - 1);

                    int maxX = Mathf.Clamp(Mathf.FloorToInt((maxPos.x - gridOrigin.x) / voxelSize), 0, gridDimensions.x - 1);
                    int maxY = Mathf.Clamp(Mathf.FloorToInt((maxPos.y - gridOrigin.y) / voxelSize), 0, gridDimensions.y - 1);
                    int maxZ = Mathf.Clamp(Mathf.FloorToInt((maxPos.z - gridOrigin.z) / voxelSize), 0, gridDimensions.z - 1);

                    for (int x = minX; x <= maxX; x++)
                    {
                        for (int y = minY; y <= maxY; y++)
                        {
                            for (int z = minZ; z <= maxZ; z++)
                            {
                                Vector3 cellCenter = gridOrigin + new Vector3((x + 0.5f) * voxelSize, (y + 0.5f) * voxelSize, (z + 0.5f) * voxelSize);

                                // 超采样: 测试多个采样点的覆盖率
                                int hitCount = 0;
                                Vector3 bestSamplePoint = cellCenter;

                                for (int s = 0; s < sampleCount; s++)
                                {
                                    Vector3 samplePoint = cellCenter + sampleOffsets[s] * voxelSize;
                                    if (TriangleAABBIntersection.TestOverlap(samplePoint, halfExtents, v0, v1, v2))
                                    {
                                        hitCount++;
                                        bestSamplePoint = samplePoint;
                                    }
                                }

                                if (hitCount > 0)
                                {
                                    byte coverage = (byte)((hitCount * 255) / sampleCount);

                                    // 若已有更高覆盖率的记录则跳过
                                    if (grid[x, y, z].isOccupied && grid[x, y, z].surfaceCoverage >= coverage)
                                        continue;

                                    grid[x, y, z].gridPos = new Vector3Int(x, y, z);
                                    grid[x, y, z].isOccupied = true;
                                    grid[x, y, z].layer = VoxelLayerType.OuterSurface;
                                    grid[x, y, z].distanceToSurface = 0;
                                    grid[x, y, z].materialId = (byte)subIdx;
                                    grid[x, y, z].isAlive = true;
                                    grid[x, y, z].initialHP = 1;
                                    grid[x, y, z].currentHP = 1;
                                    grid[x, y, z].surfaceCoverage = coverage;

                                    // 使用最佳采样点计算重心坐标 (更精确的外观采样)
                                    Vector3 bary = TriangleAABBIntersection.ComputeBarycentricCoordinates(bestSamplePoint, v0, v1, v2);

                                    Vector2 uv = Vector2.zero;
                                    if (hasUV)
                                    {
                                        uv = uvs[i0] * bary.x + uvs[i1] * bary.y + uvs[i2] * bary.z;
                                    }

                                    Vector3 norm = Vector3.up;
                                    if (hasNormal)
                                    {
                                        norm = (normals[i0] * bary.x + normals[i1] * bary.y + normals[i2] * bary.z).normalized;
                                    }

                                    Color32 vertCol = Color.white;
                                    if (hasColor)
                                    {
                                        Color c0 = colors[i0];
                                        Color c1 = colors[i1];
                                        Color c2 = colors[i2];
                                        vertCol = c0 * bary.x + c1 * bary.y + c2 * bary.z;
                                    }

                                    surfaceHits[x, y, z] = new SurfaceHitInfo
                                    {
                                        subMeshIndex = subIdx,
                                        triangleIndex = t / 3,
                                        uv = uv,
                                        normal = norm,
                                        vertexColor = vertCol
                                    };
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
