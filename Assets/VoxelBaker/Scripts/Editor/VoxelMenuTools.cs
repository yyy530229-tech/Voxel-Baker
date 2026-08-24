using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VoxelBaker.Baker;
using VoxelBaker.Data;
using VoxelBaker.Runtime;

namespace VoxelBaker.Editor
{
    public static class VoxelMenuTools
    {
        [MenuItem("Tools/Voxel Baker/生成示例模型工程资产 (Create Sample Assets)", false, 10)]
        public static void CreateSampleAssets()
        {
            VoxelProjectDatabase db = VoxelProjectDatabase.GetOrCreateDatabase();

            // 1. 烘焙小黄鸭 (Characters - 休闲消除规格约 450 体素)
            EditorUtility.DisplayProgressBar("体素烘焙工作室", "正在烘焙小黄鸭 (Yellow Duck)...", 0.3f);
            Mesh duckMesh = VoxelDemoModelGenerator.CreateYellowDuckMesh(out Material[] duckMats);
            BakeAndRegisterSample(db, "VoxelModel_Duck", VoxelModelCategory.Characters, duckMesh, duckMats, 0.22f, "Duck, Animal, Destructible");

            // 2. 烘焙粉色多层头颅 (Characters - 约 380 体素)
            EditorUtility.DisplayProgressBar("体素烘焙工作室", "正在烘焙多层粉色头颅 (Pink Head)...", 0.6f);
            Mesh pinkMesh = VoxelDemoModelGenerator.CreatePinkCharacterMesh(out Material[] pinkMats);
            BakeAndRegisterSample(db, "VoxelModel_PinkHead", VoxelModelCategory.Characters, pinkMesh, pinkMats, 0.24f, "Character, Multi-Layer, Cake");

            // 3. 烘焙像素房子 (Buildings - 匹配参考图2约 480 体素)
            EditorUtility.DisplayProgressBar("体素烘焙工作室", "正在烘焙像素小房子 (House)...", 0.9f);
            Mesh houseMesh = VoxelDemoModelGenerator.CreateHouseMesh(out Material[] houseMats);
            BakeAndRegisterSample(db, "VoxelModel_House", VoxelModelCategory.Buildings, houseMesh, houseMats, 0.26f, "Building, House, Prop");

            EditorUtility.ClearProgressBar();
            db.ScanAndRefreshRecipes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("体素烘焙工程库", "成功生成并归档所有示例模型至 'Assets/VoxelAssets/' 工程目录，并已登记至工程数据库！", "确定");
        }

        private static void BakeAndRegisterSample(VoxelProjectDatabase db, string modelName, VoxelModelCategory category, Mesh mesh, Material[] mats, float vSize, string tags)
        {
            string targetFolder = $"Assets/VoxelAssets/{category}/{modelName}";
            if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

            VoxelBakeSettings settings = new VoxelBakeSettings
            {
                sourceMesh = mesh,
                materials = mats,
                voxelSize = vSize,
                fillInteriorSolid = true,
                chunkSize = 16,
                assetName = modelName
            };

            VoxelAsset asset = VoxelBakerCore.Bake(settings);
            if (asset != null)
            {
                string assetPath = $"{targetFolder}/{modelName}.asset";
                string palettePath = $"{targetFolder}/{modelName}_Palette.asset";
                string recipePath = $"{targetFolder}/{modelName}_Recipe.asset";

                if (asset.palette != null) AssetDatabase.CreateAsset(asset.palette, palettePath);
                AssetDatabase.CreateAsset(asset, assetPath);

                VoxelModelRecipe recipe = ScriptableObject.CreateInstance<VoxelModelRecipe>();
                recipe.modelName = modelName;
                recipe.category = category;
                recipe.sourceMesh = mesh;
                recipe.sourceMaterials = mats;
                recipe.voxelSize = vSize;
                recipe.fillInteriorSolid = true;
                recipe.tags = tags;
                recipe.chunkSize = 16;
                recipe.bakedAsset = asset;
                recipe.lastBakeTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                recipe.lastBakeDuration = asset.bakeDurationSeconds;
                recipe.lastTotalVoxels = asset.totalOccupiedVoxels;
                recipe.isDirty = false;

                AssetDatabase.CreateAsset(recipe, recipePath);
            }
        }

        [MenuItem("Tools/Voxel Baker/创建并打开射击破坏演示场景 (Playground Demo Scene)", false, 11)]
        public static void CreateAndOpenDemoScene()
        {
            CreateSampleAssets();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string scenePath = "Assets/Scenes/VoxelBakerDemoScene.unity";

            string sceneDir = Path.GetDirectoryName(scenePath);
            if (!Directory.Exists(sceneDir)) Directory.CreateDirectory(sceneDir);

            // 1. 设置主相机
            GameObject camObj = new GameObject("Main Camera");
            Camera cam = camObj.AddComponent<Camera>();
            camObj.tag = "MainCamera";
            camObj.transform.position = new Vector3(0f, 0.5f, -8.5f);
            camObj.transform.rotation = Quaternion.Euler(6f, 0f, 0f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.22f, 0.30f);

            // 2. 设置平行光
            GameObject lightObj = new GameObject("Directional Light");
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.0f, 0.98f, 0.92f);
            light.intensity = 1.3f;
            lightObj.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            // 3. 创建碎片管理器
            GameObject debrisObj = new GameObject("VoxelDebrisManager");
            debrisObj.AddComponent<VoxelDebrisManager>();

            // 4. 创建体素模型实例
            GameObject modelObj = new GameObject("VoxelModel_Target");
            modelObj.transform.position = new Vector3(0f, 1.2f, 0f);
            VoxelModelInstance modelInstance = modelObj.AddComponent<VoxelModelInstance>();

            VoxelAsset targetAsset = AssetDatabase.LoadAssetAtPath<VoxelAsset>("Assets/VoxelAssets/Characters/VoxelModel_PinkHead/VoxelModel_PinkHead.asset");
            if (targetAsset == null)
            {
                targetAsset = AssetDatabase.LoadAssetAtPath<VoxelAsset>("Assets/VoxelAssets/Characters/VoxelModel_Duck/VoxelModel_Duck.asset");
            }
            modelInstance.voxelAsset = targetAsset;

            Shader voxelShader = Shader.Find("VoxelBaker/URP/VoxelLit");
            if (voxelShader != null)
            {
                modelInstance.voxelMaterial = new Material(voxelShader);
            }

            modelInstance.InitializeModel();

            // 添加 3D 自转与浮动控制器
            var rotator = modelObj.AddComponent<VoxelGameFramework.Core.VoxelModelRotator>();
            rotator.autoRotate = true;
            rotator.rotateSpeed = 22f;

            // 5. 创建底部 5 联射击炮台
            GameObject shooterObj = new GameObject("VoxelShooter_Cannons");
            VoxelShooterDemo shooter = shooterObj.AddComponent<VoxelShooterDemo>();
            shooter.targetModel = modelInstance;

            // 6. 添加演示 HUD 控制器
            GameObject uiObj = new GameObject("VoxelDemoUI");
            VoxelDemoUI demoUI = uiObj.AddComponent<VoxelDemoUI>();
            demoUI.targetModelInstance = modelInstance;
            demoUI.shooterDemo = shooter;
            demoUI.duckAsset = AssetDatabase.LoadAssetAtPath<VoxelAsset>("Assets/VoxelAssets/Characters/VoxelModel_Duck/VoxelModel_Duck.asset");
            demoUI.pinkHeadAsset = AssetDatabase.LoadAssetAtPath<VoxelAsset>("Assets/VoxelAssets/Characters/VoxelModel_PinkHead/VoxelModel_PinkHead.asset");
            demoUI.houseAsset = AssetDatabase.LoadAssetAtPath<VoxelAsset>("Assets/VoxelAssets/Buildings/VoxelModel_House/VoxelModel_House.asset");

            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("体素烘焙工作室", $"演示场景已成功创建并保存于 '{scenePath}'！\n点击 Unity Play 运行按钮即可开始射击粉碎与多层体素剥落体验！", "确定");
        }
    }
}
