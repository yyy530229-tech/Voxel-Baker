using System.Collections.Generic;
using UnityEngine;

namespace VoxelBaker.Baker
{
    /// <summary>
    /// 生成符合参考截图的精美测试 3D Mesh 与多材质（如小黄鸭、房子、多色块体素蛋糕）
    /// 严格保留 SubMesh 分区与材质映射，确保体素化外观 100% 还原原模型颜色！
    /// </summary>
    public static class VoxelDemoModelGenerator
    {
        public static Mesh CreateYellowDuckMesh(out Material[] materials)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            Material matYellow = new Material(s) { name = "Mat_DuckYellow", color = new Color(1.0f, 0.85f, 0.08f) }; // 亮黄身体
            Material matOrange = new Material(s) { name = "Mat_DuckOrange", color = new Color(0.98f, 0.52f, 0.08f) }; // 橙色鸭嘴/翅膀
            Material matBlue   = new Material(s) { name = "Mat_DuckBlue",   color = new Color(0.18f, 0.55f, 0.95f) }; // 蓝色底座水面
            Material matEye    = new Material(s) { name = "Mat_DuckEye",    color = new Color(0.12f, 0.45f, 0.85f) }; // 蓝黑眼睛

            GameObject root = new GameObject("TempDuckRoot");

            // 1. 身体 (黄色)
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.transform.SetParent(root.transform);
            body.transform.localPosition = new Vector3(0, 0, 0);
            body.transform.localScale = new Vector3(2.4f, 1.8f, 2.8f);

            // 2. 头部 (黄色)
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.transform.SetParent(root.transform);
            head.transform.localPosition = new Vector3(0, 1.35f, 0.9f);
            head.transform.localScale = new Vector3(1.7f, 1.5f, 1.7f);

            // 3. 嘴巴 (橙色)
            GameObject beak = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beak.transform.SetParent(root.transform);
            beak.transform.localPosition = new Vector3(0, 1.15f, 1.85f);
            beak.transform.localScale = new Vector3(1.1f, 0.38f, 1.0f);

            // 4. 左翅膀 (橙色)
            GameObject wingL = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            wingL.transform.SetParent(root.transform);
            wingL.transform.localPosition = new Vector3(-1.15f, 0.1f, -0.1f);
            wingL.transform.localScale = new Vector3(0.45f, 1.1f, 1.7f);

            // 5. 右翅膀 (橙色)
            GameObject wingR = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            wingR.transform.SetParent(root.transform);
            wingR.transform.localPosition = new Vector3(1.15f, 0.1f, -0.1f);
            wingR.transform.localScale = new Vector3(0.45f, 1.1f, 1.7f);

            // 6. 底座水面 (蓝色)
            GameObject baseWater = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseWater.transform.SetParent(root.transform);
            baseWater.transform.localPosition = new Vector3(0, -0.92f, 0);
            baseWater.transform.localScale = new Vector3(2.5f, 0.28f, 2.9f);

            // 7. 左眼睛 (蓝色/深色)
            GameObject eyeL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            eyeL.transform.SetParent(root.transform);
            eyeL.transform.localPosition = new Vector3(-0.65f, 1.55f, 1.35f);
            eyeL.transform.localScale = new Vector3(0.25f, 0.25f, 0.3f);

            // 8. 右眼睛 (蓝色/深色)
            GameObject eyeR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            eyeR.transform.SetParent(root.transform);
            eyeR.transform.localPosition = new Vector3(0.65f, 1.55f, 1.35f);
            eyeR.transform.localScale = new Vector3(0.25f, 0.25f, 0.3f);

            GameObject[] parts = new GameObject[] { body, head, beak, wingL, wingR, baseWater, eyeL, eyeR };
            Material[] partMats = new Material[] { matYellow, matYellow, matOrange, matOrange, matOrange, matBlue, matEye, matEye };

            Mesh combinedMesh = CombinePartsPreservingSubMeshes(parts);
            materials = partMats;

            Object.DestroyImmediate(root);
            return combinedMesh;
        }

        public static Mesh CreatePinkCharacterMesh(out Material[] materials)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material matPink = new Material(s) { name = "Mat_Pink", color = new Color(0.96f, 0.22f, 0.65f) };

            GameObject root = new GameObject("TempPinkRoot");

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.transform.SetParent(root.transform);
            head.transform.localPosition = Vector3.zero;
            head.transform.localScale = new Vector3(2.2f, 2.8f, 2.2f);

            Mesh mesh = CombinePartsPreservingSubMeshes(new GameObject[] { head });
            materials = new Material[] { matPink };

            Object.DestroyImmediate(root);
            return mesh;
        }

        public static Mesh CreateHouseMesh(out Material[] materials)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            Material matBlue   = new Material(s) { name = "Mat_HouseBlue",   color = new Color(0.22f, 0.52f, 0.88f) }; // 蓝色墙壁
            Material matRed    = new Material(s) { name = "Mat_HouseRed",    color = new Color(0.88f, 0.16f, 0.22f) }; // 红色屋顶
            Material matYellow = new Material(s) { name = "Mat_HouseYellow", color = new Color(0.96f, 0.72f, 0.12f) }; // 黄色烟囱/台阶
            Material matBrown  = new Material(s) { name = "Mat_HouseBrown",  color = new Color(0.55f, 0.32f, 0.18f) }; // 棕色门窗框
            Material matGlass  = new Material(s) { name = "Mat_HouseGlass",  color = new Color(0.98f, 0.92f, 0.15f) }; // 发光窗户玻璃

            GameObject root = new GameObject("TempHouseRoot");

            // 1. 墙体 (蓝色)
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(root.transform);
            wall.transform.localPosition = new Vector3(0, 0, 0);
            wall.transform.localScale = new Vector3(3.0f, 2.6f, 3.0f);

            // 2. 屋顶 (红色长方体45度旋转)
            GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.transform.SetParent(root.transform);
            roof.transform.localPosition = new Vector3(0, 1.75f, 0);
            roof.transform.localScale = new Vector3(3.5f, 1.3f, 3.5f);
            roof.transform.localRotation = Quaternion.Euler(0, 0, 45f);

            // 3. 烟囱 (黄色)
            GameObject chimney = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chimney.transform.SetParent(root.transform);
            chimney.transform.localPosition = new Vector3(0.75f, 2.1f, 0.4f);
            chimney.transform.localScale = new Vector3(0.7f, 1.6f, 0.7f);

            // 4. 底座地基台阶 (黄色)
            GameObject baseStep = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseStep.transform.SetParent(root.transform);
            baseStep.transform.localPosition = new Vector3(0, -1.35f, 0);
            baseStep.transform.localScale = new Vector3(3.4f, 0.3f, 3.4f);

            // 5. 正面门框 (棕色)
            GameObject doorFrame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doorFrame.transform.SetParent(root.transform);
            doorFrame.transform.localPosition = new Vector3(-0.7f, -0.4f, 1.52f);
            doorFrame.transform.localScale = new Vector3(0.9f, 1.5f, 0.2f);

            // 6. 窗户框 (棕色)
            GameObject windowFrame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            windowFrame.transform.SetParent(root.transform);
            windowFrame.transform.localPosition = new Vector3(0.7f, -0.4f, 1.52f);
            windowFrame.transform.localScale = new Vector3(1.1f, 0.9f, 0.2f);

            // 7. 发光窗户 (亮黄)
            GameObject windowGlass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            windowGlass.transform.SetParent(root.transform);
            windowGlass.transform.localPosition = new Vector3(0.7f, -0.4f, 1.58f);
            windowGlass.transform.localScale = new Vector3(0.85f, 0.65f, 0.15f);

            GameObject[] parts = new GameObject[] { wall, roof, chimney, baseStep, doorFrame, windowFrame, windowGlass };
            Material[] partMats = new Material[] { matBlue, matRed, matYellow, matYellow, matBrown, matBrown, matGlass };

            Mesh combinedMesh = CombinePartsPreservingSubMeshes(parts);
            materials = partMats;

            Object.DestroyImmediate(root);
            return combinedMesh;
        }

        private static Mesh CombinePartsPreservingSubMeshes(GameObject[] parts)
        {
            List<CombineInstance> combines = new List<CombineInstance>();

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null) continue;
                MeshFilter mf = parts[i].GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    CombineInstance ci = new CombineInstance
                    {
                        mesh = mf.sharedMesh,
                        transform = parts[i].transform.localToWorldMatrix
                    };
                    combines.Add(ci);
                }
            }

            Mesh finalMesh = new Mesh { name = "PreservedSubMeshModel" };
            // mergeSubMeshes = false: 每个子网格作为独立的 SubMesh，完美保留材质分区！
            finalMesh.CombineMeshes(combines.ToArray(), false, true);
            finalMesh.RecalculateNormals();
            finalMesh.RecalculateBounds();
            return finalMesh;
        }
    }
}
