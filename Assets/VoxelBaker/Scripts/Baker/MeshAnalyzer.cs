using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelBaker.Baker
{
    [Serializable]
    public class MeshAnalysisReport
    {
        public string meshName;
        public int vertexCount;
        public int triangleCount;
        public int subMeshCount;
        public Bounds localBounds;
        public Vector3 dimensions;

        public bool hasUV0;
        public bool hasVertexColors;
        public int materialCount;
        public int textureCount;
        public bool isWatertight;
        public int openEdgeCount;
        public int nonManifoldEdgeCount;

        // 预估网格与体素指标
        public Vector3Int estimatedGridSize;
        public int totalCells;
        public int estimatedOccupiedVoxels;
        public float estimatedMemoryMB;
        public float recommendedVoxelSize;

        public string diagnosticMessage;
        public bool canDoSolidVoxelization;
    }

    public static class MeshAnalyzer
    {
        public static MeshAnalysisReport Analyze(Mesh mesh, Material[] materials, float targetVoxelSize)
        {
            var report = new MeshAnalysisReport();
            if (mesh == null)
            {
                report.diagnosticMessage = "Error: Input Mesh is null.";
                return report;
            }

            report.meshName = mesh.name;
            report.vertexCount = mesh.vertexCount;
            report.triangleCount = mesh.triangles.Length / 3;
            report.subMeshCount = mesh.subMeshCount;
            report.localBounds = mesh.bounds;
            report.dimensions = mesh.bounds.size;

            report.hasUV0 = mesh.uv != null && mesh.uv.Length > 0;
            report.hasVertexColors = mesh.colors32 != null && mesh.colors32.Length > 0;

            report.materialCount = materials != null ? materials.Length : 0;
            report.textureCount = 0;
            if (materials != null)
            {
                foreach (var mat in materials)
                {
                    if (mat != null && mat.mainTexture != null)
                    {
                        report.textureCount++;
                    }
                }
            }

            // 分析拓扑与封闭性 (Watertightness)
            CheckWatertightness(mesh, out report.isWatertight, out report.openEdgeCount, out report.nonManifoldEdgeCount);

            // 推荐体素尺寸（根据包围盒最大跨度，默认建议分辨率 64 左右）
            float maxDim = Mathf.Max(report.dimensions.x, Mathf.Max(report.dimensions.y, report.dimensions.z));
            report.recommendedVoxelSize = maxDim > 0 ? maxDim / 64f : 0.1f;
            if (targetVoxelSize <= 0) targetVoxelSize = report.recommendedVoxelSize;

            int gx = Mathf.Max(1, Mathf.CeilToInt(report.dimensions.x / targetVoxelSize) + 2); // 留出1圈边界用于泛洪
            int gy = Mathf.Max(1, Mathf.CeilToInt(report.dimensions.y / targetVoxelSize) + 2);
            int gz = Mathf.Max(1, Mathf.CeilToInt(report.dimensions.z / targetVoxelSize) + 2);
            report.estimatedGridSize = new Vector3Int(gx, gy, gz);
            report.totalCells = gx * gy * gz;

            // 预估占用体素数（表面通常为 Grid 平方级别，内部实体约为 体积占比 30%~60%）
            int surfaceEst = Mathf.Min(report.totalCells, (gx * gy + gy * gz + gz * gx) * 2);
            report.estimatedOccupiedVoxels = Mathf.Min(report.totalCells, (int)(report.totalCells * 0.35f));
            report.estimatedMemoryMB = (report.totalCells * 16f) / (1024f * 1024f);

            report.canDoSolidVoxelization = report.isWatertight || report.openEdgeCount < (report.triangleCount * 0.1f);

            // 生成诊断报告
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"【网格诊断报告: {mesh.name}】");
            sb.AppendLine($"• 几何数据: 顶点数 {report.vertexCount:N0}, 三角形数 {report.triangleCount:N0}, 子网格数 {report.subMeshCount}");
            sb.AppendLine($"• 模型跨度: 尺寸 ({report.dimensions.x:F2}m x {report.dimensions.y:F2}m x {report.dimensions.z:F2}m)");
            sb.AppendLine($"• 贴图与颜色: UV0 {(report.hasUV0 ? "✓ 具备" : "✗ 缺失")}, 顶点色 {(report.hasVertexColors ? "✓ 具备" : "无")}, 关联材质 {report.materialCount} 个, 主纹理贴图 {report.textureCount} 张");
            sb.AppendLine($"• 几何拓扑: {(report.isWatertight ? "✓ 完全封闭实体网格 (Watertight Solid)" : $"△ 开放网格 (发现 {report.openEdgeCount} 条开放边缘)")}");
            sb.AppendLine($"• 目标网格: {gx}x{gy}x{gz} = {report.totalCells:N0} 格 (预估体素显存: {report.estimatedMemoryMB:F2} MB)");

            if (report.isWatertight)
            {
                sb.AppendLine("• 质量等级: 【A级最佳】 完美支持 3D 边界泛洪实体内部填充与高保真贴图烘焙。");
            }
            else
            {
                sb.AppendLine("• 质量等级: 【B级可用】 存在开放边缘，系统将启用容错 Flood Fill 泛洪与表面保护策略。");
            }

            report.diagnosticMessage = sb.ToString();
            return report;
        }

        private static void CheckWatertightness(Mesh mesh, out bool isWatertight, out int openEdges, out int nonManifoldEdges)
        {
            openEdges = 0;
            nonManifoldEdges = 0;
            int[] triangles = mesh.triangles;
            if (triangles == null || triangles.Length == 0)
            {
                isWatertight = false;
                return;
            }

            // 统计每条无向边的共享三角形次数
            Dictionary<ulong, int> edgeCount = new Dictionary<ulong, int>(triangles.Length);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                AddEdge(i0, i1, edgeCount);
                AddEdge(i1, i2, edgeCount);
                AddEdge(i2, i0, edgeCount);
            }

            foreach (var kvp in edgeCount)
            {
                if (kvp.Value == 1) openEdges++;
                else if (kvp.Value > 2) nonManifoldEdges++;
            }

            isWatertight = (openEdges == 0 && nonManifoldEdges == 0);
        }

        private static void AddEdge(int a, int b, Dictionary<ulong, int> dict)
        {
            int min = Mathf.Min(a, b);
            int max = Mathf.Max(a, b);
            ulong key = ((ulong)(uint)min << 32) | (ulong)(uint)max;
            if (dict.TryGetValue(key, out int count))
            {
                dict[key] = count + 1;
            }
            else
            {
                dict[key] = 1;
            }
        }
    }
}
