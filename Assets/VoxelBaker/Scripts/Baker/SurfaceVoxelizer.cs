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
        public static void VoxelizeSurface(
            Mesh mesh,
            Material[] materials,
            float voxelSize,
            Vector3 gridOrigin,
            Vector3Int gridDimensions,
            VoxelCell[,,] grid,
            SurfaceHitInfo[,,] surfaceHits)
        {
            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            Color32[] colors = mesh.colors32;
            Vector3[] normals = mesh.normals;
            bool hasUV = uvs != null && uvs.Length == vertices.Length;
            bool hasColor = colors != null && colors.Length == vertices.Length;
            bool hasNormal = normals != null && normals.Length == vertices.Length;

            Vector3 halfExtents = Vector3.one * (voxelSize * 0.5f);

            int subMeshCount = mesh.subMeshCount;
            for (int subIdx = 0; subIdx < subMeshCount; subIdx++)
            {
                int[] triangles = mesh.GetTriangles(subIdx);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    int i0 = triangles[t];
                    int i1 = triangles[t + 1];
                    int i2 = triangles[t + 2];

                    Vector3 v0 = vertices[i0];
                    Vector3 v1 = vertices[i1];
                    Vector3 v2 = vertices[i2];

                    // 计算三角形在网格空间的包围盒范围
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

                                if (TriangleAABBIntersection.TestOverlap(cellCenter, halfExtents, v0, v1, v2))
                                {
                                    grid[x, y, z].gridPos = new Vector3Int(x, y, z);
                                    grid[x, y, z].isOccupied = true;
                                    grid[x, y, z].layer = VoxelLayerType.OuterSurface;
                                    grid[x, y, z].distanceToSurface = 0;
                                    grid[x, y, z].materialId = (byte)subIdx;
                                    grid[x, y, z].isAlive = true;
                                    grid[x, y, z].initialHP = 1;
                                    grid[x, y, z].currentHP = 1;

                                    // 计算重心坐标与采样属性
                                    Vector3 bary = TriangleAABBIntersection.ComputeBarycentricCoordinates(cellCenter, v0, v1, v2);

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
