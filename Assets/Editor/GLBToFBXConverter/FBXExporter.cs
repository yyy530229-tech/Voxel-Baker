using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ModelConverter
{
    /// <summary>
    /// 纯 C# ASCII FBX 7.4 (Version 7400) 导出器
    /// 生成符合 Autodesk 官方标准的 FBX 文件，包含顶点网格、法线、UV、顶点色、多材质分区与层级节点！
    /// </summary>
    public static class FBXExporter
    {
        public static void ExportToFBX(GLBParser.ParsedModelData modelData, string outputFilePath)
        {
            if (modelData == null || modelData.meshes == null || modelData.meshes.Count == 0)
            {
                throw new Exception("没有可供导出的网格数据！");
            }

            string dir = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            StringBuilder sb = new StringBuilder(1024 * 1024);
            var inv = CultureInfo.InvariantCulture;

            // 1. FBX Header Extension (Version 7.4 / 7400)
            sb.AppendLine("; FBX 7.4.0 project file");
            sb.AppendLine("; Created by Unity AI ModelConverter Tools");
            sb.AppendLine("; ----------------------------------------------------");
            sb.AppendLine();
            sb.AppendLine("FBXHeaderExtension:  {");
            sb.AppendLine("\tFBXHeaderVersion: 1003");
            sb.AppendLine("\tFBXVersion: 7400");
            sb.AppendLine("\tCreationTimeStamp:  {");
            sb.AppendLine("\t\tVersion: 1000");
            sb.AppendLine($"\t\tYear: {DateTime.Now.Year}");
            sb.AppendLine($"\t\tMonth: {DateTime.Now.Month}");
            sb.AppendLine($"\t\tDay: {DateTime.Now.Day}");
            sb.AppendLine($"\t\tHour: {DateTime.Now.Hour}");
            sb.AppendLine($"\t\tMinute: {DateTime.Now.Minute}");
            sb.AppendLine($"\t\tSecond: {DateTime.Now.Second}");
            sb.AppendLine("\t}");
            sb.AppendLine("\tCreator: \"Unity GLB to FBX Exporter\"");
            sb.AppendLine("}");
            sb.AppendLine();

            // 2. GlobalSettings
            sb.AppendLine("GlobalSettings:  {");
            sb.AppendLine("\tVersion: 1000");
            sb.AppendLine("\tProperties70:  {");
            sb.AppendLine("\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",1");
            sb.AppendLine("\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1");
            sb.AppendLine("\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",2");
            sb.AppendLine("\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1");
            sb.AppendLine("\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0");
            sb.AppendLine("\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1");
            sb.AppendLine("\t\tP: \"OriginalUpAxis\", \"int\", \"Integer\", \"\",1");
            sb.AppendLine("\t\tP: \"OriginalUpAxisSign\", \"int\", \"Integer\", \"\",1");
            sb.AppendLine("\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",100.0");
            sb.AppendLine("\t\tP: \"OriginalUnitScaleFactor\", \"double\", \"Number\", \"\",100.0");
            sb.AppendLine("\t}");
            sb.AppendLine("}");
            sb.AppendLine();

            // 生成唯一 ID 映射
            long rootModelId = 100000;
            List<long> meshGeomIds = new List<long>();
            List<long> meshModelIds = new List<long>();
            List<long> materialIds = new List<long>();

            for (int i = 0; i < modelData.meshes.Count; i++)
            {
                meshGeomIds.Add(200000 + i);
                meshModelIds.Add(300000 + i);
            }

            int matCount = Mathf.Max(1, modelData.materials != null ? modelData.materials.Count : 1);
            for (int i = 0; i < matCount; i++)
            {
                materialIds.Add(400000 + i);
            }

            // 3. Definitions
            int totalObjects = 1 + modelData.meshes.Count * 2 + matCount;
            sb.AppendLine("Definitions:  {");
            sb.AppendLine("\tVersion: 100");
            sb.AppendLine($"\tCount: {totalObjects}");
            sb.AppendLine("\tObjectType: \"GlobalSettings\" {");
            sb.AppendLine("\t\tCount: 1");
            sb.AppendLine("\t}");
            sb.AppendLine("\tObjectType: \"Model\" {");
            sb.AppendLine($"\t\tCount: {1 + modelData.meshes.Count}");
            sb.AppendLine("\t}");
            sb.AppendLine("\tObjectType: \"Geometry\" {");
            sb.AppendLine($"\t\tCount: {modelData.meshes.Count}");
            sb.AppendLine("\t}");
            sb.AppendLine("\tObjectType: \"Material\" {");
            sb.AppendLine($"\t\tCount: {matCount}");
            sb.AppendLine("\t}");
            sb.AppendLine("}");
            sb.AppendLine();

            // 4. Objects (Entities, Geometries, Materials)
            sb.AppendLine("Objects:  {");

            // 根节点 (Root Model)
            sb.AppendLine($"\tModel: {rootModelId}, \"Model::{modelData.modelName}\", \"Null\" {{");
            sb.AppendLine("\t\tVersion: 232");
            sb.AppendLine("\t\tProperties70:  {");
            sb.AppendLine("\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
            sb.AppendLine("\t\t\tP: \"DefaultAttributeIndex\", \"int\", \"Integer\", \"\",0");
            sb.AppendLine("\t\t}");
            sb.AppendLine("\t\tShading: T");
            sb.AppendLine("\t\tCulling: \"CullingOff\"");
            sb.AppendLine("\t}");

            // 网格几何体与模型节点
            for (int m = 0; m < modelData.meshes.Count; m++)
            {
                var mesh = modelData.meshes[m];
                long geomId = meshGeomIds[m];
                long modelId = meshModelIds[m];

                // Geometry
                sb.AppendLine($"\tGeometry: {geomId}, \"Geometry::{mesh.meshName}\", \"Mesh\" {{");
                sb.AppendLine("\t\tVertices: *{0} {{".Replace("{0}", (mesh.vertices.Length * 3).ToString()));
                sb.Append("\t\t\ta: ");
                for (int v = 0; v < mesh.vertices.Length; v++)
                {
                    // 缩放 100 倍以适应 FBX 标准单位 (厘米)
                    sb.Append((mesh.vertices[v].x * 100f).ToString("F6", inv)).Append(",");
                    sb.Append((mesh.vertices[v].y * 100f).ToString("F6", inv)).Append(",");
                    sb.Append((mesh.vertices[v].z * 100f).ToString("F6", inv));
                    if (v < mesh.vertices.Length - 1) sb.Append(",");
                }
                sb.AppendLine("\n\t\t}");

                // PolygonVertexIndex (Triangles: v0, v1, ~v2)
                List<int> polyIndices = new List<int>();
                List<int> polyMaterialIndices = new List<int>();

                for (int s = 0; s < mesh.subMeshIndices.Count; s++)
                {
                    int[] subIdx = mesh.subMeshIndices[s];
                    int matIdx = (s < mesh.subMeshMaterialIndices.Count && mesh.subMeshMaterialIndices[s] >= 0) ? mesh.subMeshMaterialIndices[s] : 0;

                    for (int i = 0; i < subIdx.Length; i += 3)
                    {
                        polyIndices.Add(subIdx[i]);
                        polyIndices.Add(subIdx[i + 1]);
                        polyIndices.Add(~subIdx[i + 2]); // 按 FBX 规范，多边形末尾顶点为 ~idx (即 -idx - 1)
                        polyMaterialIndices.Add(matIdx);
                    }
                }

                sb.AppendLine($"\t\tPolygonVertexIndex: *{polyIndices.Count} {{");
                sb.Append("\t\t\ta: ");
                for (int i = 0; i < polyIndices.Count; i++)
                {
                    sb.Append(polyIndices[i]);
                    if (i < polyIndices.Count - 1) sb.Append(",");
                }
                sb.AppendLine("\n\t\t}");

                // LayerElementNormal
                if (mesh.normals != null && mesh.normals.Length == mesh.vertices.Length)
                {
                    sb.AppendLine("\t\tLayerElementNormal: 0 {");
                    sb.AppendLine("\t\t\tVersion: 101");
                    sb.AppendLine("\t\t\tName: \"\"");
                    sb.AppendLine("\t\t\tMappingInformationType: \"ByVertice\"");
                    sb.AppendLine("\t\t\tReferenceInformationType: \"Direct\"");
                    sb.AppendLine($"\t\t\tNormals: *{mesh.normals.Length * 3} {{");
                    sb.Append("\t\t\t\ta: ");
                    for (int n = 0; n < mesh.normals.Length; n++)
                    {
                        sb.Append(mesh.normals[n].x.ToString("F6", inv)).Append(",");
                        sb.Append(mesh.normals[n].y.ToString("F6", inv)).Append(",");
                        sb.Append(mesh.normals[n].z.ToString("F6", inv));
                        if (n < mesh.normals.Length - 1) sb.Append(",");
                    }
                    sb.AppendLine("\n\t\t\t}");
                    sb.AppendLine("\t\t}");
                }

                // LayerElementUV
                if (mesh.uvs != null && mesh.uvs.Length == mesh.vertices.Length)
                {
                    sb.AppendLine("\t\tLayerElementUV: 0 {");
                    sb.AppendLine("\t\t\tVersion: 101");
                    sb.AppendLine("\t\t\tName: \"UVMap\"");
                    sb.AppendLine("\t\t\tMappingInformationType: \"ByVertice\"");
                    sb.AppendLine("\t\t\tReferenceInformationType: \"Direct\"");
                    sb.AppendLine($"\t\t\tUV: *{mesh.uvs.Length * 2} {{");
                    sb.Append("\t\t\t\ta: ");
                    for (int u = 0; u < mesh.uvs.Length; u++)
                    {
                        sb.Append(mesh.uvs[u].x.ToString("F6", inv)).Append(",");
                        sb.Append(mesh.uvs[u].y.ToString("F6", inv));
                        if (u < mesh.uvs.Length - 1) sb.Append(",");
                    }
                    sb.AppendLine("\n\t\t\t}");
                    sb.AppendLine("\t\t}");
                }

                // LayerElementColor (Vertex Colors)
                if (mesh.colors != null && mesh.colors.Length == mesh.vertices.Length)
                {
                    sb.AppendLine("\t\tLayerElementColor: 0 {");
                    sb.AppendLine("\t\t\tVersion: 101");
                    sb.AppendLine("\t\t\tName: \"Col\"");
                    sb.AppendLine("\t\t\tMappingInformationType: \"ByVertice\"");
                    sb.AppendLine("\t\t\tReferenceInformationType: \"Direct\"");
                    sb.AppendLine($"\t\t\tColors: *{mesh.colors.Length * 4} {{");
                    sb.Append("\t\t\t\ta: ");
                    for (int c = 0; c < mesh.colors.Length; c++)
                    {
                        sb.Append(mesh.colors[c].r.ToString("F4", inv)).Append(",");
                        sb.Append(mesh.colors[c].g.ToString("F4", inv)).Append(",");
                        sb.Append(mesh.colors[c].b.ToString("F4", inv)).Append(",");
                        sb.Append(mesh.colors[c].a.ToString("F4", inv));
                        if (c < mesh.colors.Length - 1) sb.Append(",");
                    }
                    sb.AppendLine("\n\t\t\t}");
                    sb.AppendLine("\t\t}");
                }

                // LayerElementMaterial
                sb.AppendLine("\t\tLayerElementMaterial: 0 {");
                sb.AppendLine("\t\t\tVersion: 101");
                sb.AppendLine("\t\t\tName: \"\"");
                sb.AppendLine("\t\t\tMappingInformationType: \"ByPolygon\"");
                sb.AppendLine("\t\t\tReferenceInformationType: \"IndexToDirect\"");
                sb.AppendLine($"\t\t\tMaterials: *{polyMaterialIndices.Count} {{");
                sb.Append("\t\t\t\ta: ");
                for (int i = 0; i < polyMaterialIndices.Count; i++)
                {
                    sb.Append(polyMaterialIndices[i]);
                    if (i < polyMaterialIndices.Count - 1) sb.Append(",");
                }
                sb.AppendLine("\n\t\t\t}");
                sb.AppendLine("\t\t}");

                // Layer 0 绑定
                sb.AppendLine("\t\tLayer: 0 {");
                sb.AppendLine("\t\t\tVersion: 100");
                if (mesh.normals != null) sb.AppendLine("\t\t\tLayerElement:  {\n\t\t\t\tType: \"LayerElementNormal\"\n\t\t\t\tTypedIndex: 0\n\t\t\t}");
                if (mesh.uvs != null) sb.AppendLine("\t\t\tLayerElement:  {\n\t\t\t\tType: \"LayerElementUV\"\n\t\t\t\tTypedIndex: 0\n\t\t\t}");
                if (mesh.colors != null && mesh.colors.Length > 0) sb.AppendLine("\t\t\tLayerElement:  {\n\t\t\t\tType: \"LayerElementColor\"\n\t\t\t\tTypedIndex: 0\n\t\t\t}");
                sb.AppendLine("\t\t\tLayerElement:  {\n\t\t\t\tType: \"LayerElementMaterial\"\n\t\t\t\tTypedIndex: 0\n\t\t\t}");
                sb.AppendLine("\t\t}");

                sb.AppendLine("\t}");

                // Model Node for this Mesh
                sb.AppendLine($"\tModel: {modelId}, \"Model::{mesh.meshName}\", \"Mesh\" {{");
                sb.AppendLine("\t\tVersion: 232");
                sb.AppendLine("\t\tProperties70:  {");
                sb.AppendLine("\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
                sb.AppendLine("\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
                sb.AppendLine("\t\t\tP: \"DefaultAttributeIndex\", \"int\", \"Integer\", \"\",0");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\tShading: T");
                sb.AppendLine("\t\tCulling: \"CullingOff\"");
                sb.AppendLine("\t}");
            }

            // Materials
            for (int i = 0; i < matCount; i++)
            {
                long matId = materialIds[i];
                string matName = (modelData.materials != null && i < modelData.materials.Count) ? modelData.materials[i].materialName : $"Material_{i}";
                Color baseCol = (modelData.materials != null && i < modelData.materials.Count) ? modelData.materials[i].baseColor : Color.white;

                string rStr = baseCol.r.ToString("F4", inv);
                string gStr = baseCol.g.ToString("F4", inv);
                string bStr = baseCol.b.ToString("F4", inv);

                sb.AppendLine($"\tMaterial: {matId}, \"Material::{matName}\", \"\" {{");
                sb.AppendLine("\t\tVersion: 102");
                sb.AppendLine("\t\tShadingModel: \"phong\"");
                sb.AppendLine("\t\tMultiLayer: 0");
                sb.AppendLine("\t\tProperties70:  {");
                sb.AppendLine("\t\t\tP: \"ShadingModel\", \"KString\", \"\", \"\", \"Phong\"");
                sb.AppendLine($"\t\t\tP: \"DiffuseColor\", \"Color\", \"\", \"A\",{rStr},{gStr},{bStr}");
                sb.AppendLine("\t\t\tP: \"DiffuseFactor\", \"Number\", \"\", \"A\",1.0");
                sb.AppendLine("\t\t\tP: \"AmbientColor\", \"Color\", \"\", \"A\",0.1,0.1,0.1");
                sb.AppendLine("\t\t\tP: \"SpecularColor\", \"Color\", \"\", \"A\",0.2,0.2,0.2");
                sb.AppendLine("\t\t\tP: \"SpecularFactor\", \"Number\", \"\", \"A\",0.5");
                sb.AppendLine("\t\t\tP: \"Shininess\", \"Number\", \"\", \"A\",20.0");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t}");
            }

            sb.AppendLine("}");
            sb.AppendLine();

            // 5. Connections (Hierarchy & Relationships)
            sb.AppendLine("Connections:  {");

            // 连接各网格至 Root Model
            for (int m = 0; m < modelData.meshes.Count; m++)
            {
                long geomId = meshGeomIds[m];
                long modelId = meshModelIds[m];

                // Geometry -> Model
                sb.AppendLine($"\t;Geometry::{modelData.meshes[m].meshName} -> Model::{modelData.meshes[m].meshName}");
                sb.AppendLine($"\tC: \"OO\",{geomId},{modelId}");

                // Model -> Root Model
                sb.AppendLine($"\t;Model::{modelData.meshes[m].meshName} -> Model::{modelData.modelName}");
                sb.AppendLine($"\tC: \"OO\",{modelId},{rootModelId}");

                // Material -> Model
                for (int s = 0; s < modelData.meshes[m].subMeshIndices.Count; s++)
                {
                    int matIdx = (s < modelData.meshes[m].subMeshMaterialIndices.Count && modelData.meshes[m].subMeshMaterialIndices[s] >= 0) ? modelData.meshes[m].subMeshMaterialIndices[s] : 0;
                    if (matIdx < materialIds.Count)
                    {
                        long matId = materialIds[matIdx];
                        sb.AppendLine($"\t;Material -> Model::{modelData.meshes[m].meshName}");
                        sb.AppendLine($"\tC: \"OO\",{matId},{modelId}");
                    }
                }
            }

            // Root Model -> Scene Root (0)
            sb.AppendLine($"\t;Model::{modelData.modelName} -> RootNode");
            sb.AppendLine($"\tC: \"OO\",{rootModelId},0");

            sb.AppendLine("}");

            File.WriteAllText(outputFilePath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[FBXExporter] ✅ 成功将模型导出为标准 FBX 7.4 文件: {outputFilePath}");
        }
    }
}
