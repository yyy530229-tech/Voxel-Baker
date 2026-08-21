using System.Collections.Generic;
using UnityEngine;

namespace VoxelBaker.Baker
{
    /// <summary>
    /// 生成符合参考截图的精美测试 3D Mesh 与材质（如小黄鸭、粉色头颅、房子、体素蛋糕）
    /// </summary>
    public static class VoxelDemoModelGenerator
    {
        public static Mesh CreateYellowDuckMesh(out Material[] materials)
        {
            // 创建由黄色身体、橙色鸭嘴/翅膀、蓝色底座组合的完整封闭 Mesh 与材质
            GameObject root = new GameObject("TempDuckRoot");

            // 1. 身体 (黄色)
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.transform.SetParent(root.transform);
            body.transform.localPosition = new Vector3(0, 0, 0);
            body.transform.localScale = new Vector3(2.4f, 1.8f, 2.8f);

            // 2. 头部 (黄色)
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.transform.SetParent(root.transform);
            head.transform.localPosition = new Vector3(0, 1.4f, 1.0f);
            head.transform.localScale = new Vector3(1.6f, 1.5f, 1.6f);

            // 3. 嘴巴 (橙色)
            GameObject beak = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beak.transform.SetParent(root.transform);
            beak.transform.localPosition = new Vector3(0, 1.2f, 2.0f);
            beak.transform.localScale = new Vector3(1.0f, 0.4f, 1.1f);

            // 4. 左翅膀 (橙色)
            GameObject wingL = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            wingL.transform.SetParent(root.transform);
            wingL.transform.localPosition = new Vector3(-1.2f, 0.1f, 0);
            wingL.transform.localScale = new Vector3(0.5f, 1.2f, 1.8f);

            // 5. 右翅膀 (橙色)
            GameObject wingR = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            wingR.transform.SetParent(root.transform);
            wingR.transform.localPosition = new Vector3(1.2f, 0.1f, 0);
            wingR.transform.localScale = new Vector3(0.5f, 1.2f, 1.8f);

            // 6. 底座水面 (蓝色)
            GameObject baseWater = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseWater.transform.SetParent(root.transform);
            baseWater.transform.localPosition = new Vector3(0, -0.9f, 0);
            baseWater.transform.localScale = new Vector3(2.6f, 0.25f, 3.0f);

            Mesh combinedMesh = CombineChildren(root);
            Object.DestroyImmediate(root);

            // 构造对应的材质
            Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material matYellow = new Material(s) { name = "Mat_DuckYellow", color = new Color(1f, 0.84f, 0.05f) };
            Material matOrange = new Material(s) { name = "Mat_DuckOrange", color = new Color(0.98f, 0.5f, 0.08f) };
            Material matBlue = new Material(s) { name = "Mat_DuckBlue", color = new Color(0.15f, 0.5f, 0.9f) };

            materials = new Material[] { matYellow, matOrange, matBlue };
            return combinedMesh;
        }

        public static Mesh CreatePinkCharacterMesh(out Material[] materials)
        {
            GameObject root = new GameObject("TempPinkRoot");

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.transform.SetParent(root.transform);
            head.transform.localPosition = Vector3.zero;
            head.transform.localScale = new Vector3(2.2f, 2.8f, 2.2f);

            Mesh mesh = CombineChildren(root);
            Object.DestroyImmediate(root);

            Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material matPink = new Material(s) { name = "Mat_Pink", color = new Color(0.96f, 0.2f, 0.65f) };
            materials = new Material[] { matPink };
            return mesh;
        }

        public static Mesh CreateHouseMesh(out Material[] materials)
        {
            GameObject root = new GameObject("TempHouseRoot");

            // 墙体 (蓝色)
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(root.transform);
            wall.transform.localPosition = new Vector3(0, 0, 0);
            wall.transform.localScale = new Vector3(3f, 2.6f, 3f);

            // 屋顶 (红色棱锥/长方体)
            GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.transform.SetParent(root.transform);
            roof.transform.localPosition = new Vector3(0, 1.8f, 0);
            roof.transform.localScale = new Vector3(3.6f, 1.4f, 3.6f);
            roof.transform.localRotation = Quaternion.Euler(0, 0, 45f);

            // 烟囱 (黄色)
            GameObject chimney = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chimney.transform.SetParent(root.transform);
            chimney.transform.localPosition = new Vector3(0.8f, 2.2f, 0.5f);
            chimney.transform.localScale = new Vector3(0.7f, 1.6f, 0.7f);

            Mesh mesh = CombineChildren(root);
            Object.DestroyImmediate(root);

            Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material matBlue = new Material(s) { name = "Mat_HouseBlue", color = new Color(0.2f, 0.5f, 0.85f) };
            Material matRed = new Material(s) { name = "Mat_HouseRed", color = new Color(0.85f, 0.15f, 0.2f) };
            Material matYellow = new Material(s) { name = "Mat_HouseYellow", color = new Color(0.95f, 0.75f, 0.1f) };

            materials = new Material[] { matBlue, matRed, matYellow };
            return mesh;
        }

        private static Mesh CombineChildren(GameObject root)
        {
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>();
            CombineInstance[] combines = new CombineInstance[filters.Length];

            for (int i = 0; i < filters.Length; i++)
            {
                combines[i].mesh = filters[i].sharedMesh;
                combines[i].transform = filters[i].transform.localToWorldMatrix;
            }

            Mesh finalMesh = new Mesh { name = "CombinedModelMesh" };
            finalMesh.CombineMeshes(combines, true, true);
            finalMesh.RecalculateNormals();
            finalMesh.RecalculateBounds();
            return finalMesh;
        }
    }
}
