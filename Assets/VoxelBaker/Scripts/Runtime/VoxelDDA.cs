using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Runtime
{
    public struct VoxelRaycastHit
    {
        public bool hasHit;
        public Vector3Int gridPos;
        public Vector3 worldHitPoint;
        public Vector3 hitNormal;
        public float distance;
    }

    /// <summary>
    /// 基于 3D 快速 DDA (Digital Differential Analyzer) 算法的体素网格高精度求交
    /// </summary>
    public static class VoxelDDA
    {
        public static bool Raycast(
            Ray worldRay,
            Transform modelTransform,
            VoxelAsset asset,
            System.Func<Vector3Int, bool> isVoxelOccupiedAndAlive,
            out VoxelRaycastHit hitResult,
            float maxDistance = 100f)
        {
            hitResult = new VoxelRaycastHit { hasHit = false };

            if (asset == null || modelTransform == null || isVoxelOccupiedAndAlive == null)
                return false;

            // 1. 将世界射线转换至模型局部坐标系
            Vector3 localOrigin = modelTransform.InverseTransformPoint(worldRay.origin);
            Vector3 localDir = modelTransform.InverseTransformDirection(worldRay.direction).normalized;

            // 2. 与模型整体包围盒求交 (AABB Ray Test)
            Vector3 gridMinLocal = asset.localOrigin;
            Vector3 gridMaxLocal = asset.localOrigin + new Vector3(
                asset.gridDimensions.x * asset.voxelSize,
                asset.gridDimensions.y * asset.voxelSize,
                asset.gridDimensions.z * asset.voxelSize
            );

            if (!IntersectAABB(localOrigin, localDir, gridMinLocal, gridMaxLocal, out float tMin, out float tMax))
            {
                return false;
            }

            if (tMax < 0 || tMin > maxDistance)
                return false;

            // 确定进入网格的起始起点
            float startT = Mathf.Max(0.0f, tMin + 1e-4f);
            Vector3 startLocalPos = localOrigin + localDir * startT;

            // 3. 初始化 3D DDA 步进参数
            Vector3 gridCoordFloat = (startLocalPos - asset.localOrigin) / asset.voxelSize;
            int x = Mathf.Clamp(Mathf.FloorToInt(gridCoordFloat.x), 0, asset.gridDimensions.x - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(gridCoordFloat.y), 0, asset.gridDimensions.y - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(gridCoordFloat.z), 0, asset.gridDimensions.z - 1);

            int stepX = localDir.x > 0 ? 1 : (localDir.x < 0 ? -1 : 0);
            int stepY = localDir.y > 0 ? 1 : (localDir.y < 0 ? -1 : 0);
            int stepZ = localDir.z > 0 ? 1 : (localDir.z < 0 ? -1 : 0);

            float tDeltaX = stepX != 0 ? Mathf.Abs(asset.voxelSize / localDir.x) : float.MaxValue;
            float tDeltaY = stepY != 0 ? Mathf.Abs(asset.voxelSize / localDir.y) : float.MaxValue;
            float tDeltaZ = stepZ != 0 ? Mathf.Abs(asset.voxelSize / localDir.z) : float.MaxValue;

            float nextVoxelBoundaryX = (x + (stepX > 0 ? 1 : 0)) * asset.voxelSize + asset.localOrigin.x;
            float nextVoxelBoundaryY = (y + (stepY > 0 ? 1 : 0)) * asset.voxelSize + asset.localOrigin.y;
            float nextVoxelBoundaryZ = (z + (stepZ > 0 ? 1 : 0)) * asset.voxelSize + asset.localOrigin.z;

            float tMaxX = stepX != 0 ? (nextVoxelBoundaryX - localOrigin.x) / localDir.x : float.MaxValue;
            float tMaxY = stepY != 0 ? (nextVoxelBoundaryY - localOrigin.y) / localDir.y : float.MaxValue;
            float tMaxZ = stepZ != 0 ? (nextVoxelBoundaryZ - localOrigin.z) / localDir.z : float.MaxValue;

            Vector3 hitLocalNormal = Vector3.up;

            // 4. 步进遍历体素网格 (最多步进 500 次)
            int maxSteps = asset.gridDimensions.x + asset.gridDimensions.y + asset.gridDimensions.z;
            for (int step = 0; step < maxSteps; step++)
            {
                Vector3Int currentCoord = new Vector3Int(x, y, z);

                if (isVoxelOccupiedAndAlive(currentCoord))
                {
                    hitResult.hasHit = true;
                    hitResult.gridPos = currentCoord;
                    hitResult.hitNormal = modelTransform.TransformDirection(hitLocalNormal).normalized;
                    
                    Vector3 localHitPos = asset.GridToLocalPosition(currentCoord);
                    hitResult.worldHitPoint = modelTransform.TransformPoint(localHitPos);
                    hitResult.distance = Vector3.Distance(worldRay.origin, hitResult.worldHitPoint);
                    return true;
                }

                // 沿最近轴步进
                if (tMaxX < tMaxY)
                {
                    if (tMaxX < tMaxZ)
                    {
                        x += stepX;
                        if (x < 0 || x >= asset.gridDimensions.x) break;
                        tMaxX += tDeltaX;
                        hitLocalNormal = new Vector3(-stepX, 0, 0);
                    }
                    else
                    {
                        z += stepZ;
                        if (z < 0 || z >= asset.gridDimensions.z) break;
                        tMaxZ += tDeltaZ;
                        hitLocalNormal = new Vector3(0, 0, -stepZ);
                    }
                }
                else
                {
                    if (tMaxY < tMaxZ)
                    {
                        y += stepY;
                        if (y < 0 || y >= asset.gridDimensions.y) break;
                        tMaxY += tDeltaY;
                        hitLocalNormal = new Vector3(0, -stepY, 0);
                    }
                    else
                    {
                        z += stepZ;
                        if (z < 0 || z >= asset.gridDimensions.z) break;
                        tMaxZ += tDeltaZ;
                        hitLocalNormal = new Vector3(0, 0, -stepZ);
                    }
                }
            }

            return false;
        }

        private static bool IntersectAABB(Vector3 rayOrigin, Vector3 rayDir, Vector3 boxMin, Vector3 boxMax, out float tMin, out float tMax)
        {
            tMin = 0f;
            tMax = float.MaxValue;

            for (int i = 0; i < 3; i++)
            {
                float invD = 1.0f / (Mathf.Abs(rayDir[i]) > 1e-6f ? rayDir[i] : 1e-6f);
                float t0 = (boxMin[i] - rayOrigin[i]) * invD;
                float t1 = (boxMax[i] - rayOrigin[i]) * invD;
                if (invD < 0.0f)
                {
                    float temp = t0; t0 = t1; t1 = temp;
                }
                tMin = Mathf.Max(tMin, t0);
                tMax = Mathf.Min(tMax, t1);
                if (tMax < tMin) return false;
            }
            return true;
        }
    }
}
