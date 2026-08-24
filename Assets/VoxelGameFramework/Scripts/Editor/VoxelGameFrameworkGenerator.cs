using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VoxelBaker.Data;
using VoxelBaker.Editor;
using VoxelBaker.Runtime;
using VoxelGameFramework.Cannons;
using VoxelGameFramework.Core;
using VoxelGameFramework.Level;
using VoxelGameFramework.UI;

namespace VoxelGameFramework.Editor
{
    public static class VoxelGameFrameworkGenerator
    {
        [MenuItem("Tools/Voxel Game Framework/🚀 一键生成独立完整体素射击游戏场景 (Create Game Scene)", false, 20)]
        public static void GenerateStandaloneGame()
        {
            // 1. 确保示例体素资产存在
            VoxelMenuTools.CreateSampleAssets();

            string configDir = "Assets/VoxelGameFramework/Configs";
            string sceneDir = "Assets/Scenes";
            if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
            if (!Directory.Exists(sceneDir)) Directory.CreateDirectory(sceneDir);

            // 2. 创建 4 个标准关卡配置 (ScriptableObject)
            VoxelAsset duckAsset = AssetDatabase.LoadAssetAtPath<VoxelAsset>("Assets/VoxelAssets/Characters/VoxelModel_Duck/VoxelModel_Duck.asset");
            VoxelAsset pinkAsset = AssetDatabase.LoadAssetAtPath<VoxelAsset>("Assets/VoxelAssets/Characters/VoxelModel_PinkHead/VoxelModel_PinkHead.asset");
            VoxelAsset houseAsset = AssetDatabase.LoadAssetAtPath<VoxelAsset>("Assets/VoxelAssets/Buildings/VoxelModel_House/VoxelModel_House.asset");
            VoxelAsset pandaAsset = AssetDatabase.LoadAssetAtPath<VoxelAsset>("Assets/VoxelAssets/Characters/VoxelModel_Giantpanda/VoxelModel_Giantpanda.asset");

            VoxelLevelConfig lvl1 = CreateOrUpdateConfig($"{configDir}/Level_01_Duck.asset", 1, "关卡 1: 可爱小黄鸭", duckAsset, new int[] { 33, 45, 55, 66, 77 }, new Color(0.16f, 0.22f, 0.30f));
            VoxelLevelConfig lvl2 = CreateOrUpdateConfig($"{configDir}/Level_02_PinkHead.asset", 2, "关卡 2: 多层粉色头颅", pinkAsset, new int[] { 45, 60, 75, 90, 110 }, new Color(0.18f, 0.24f, 0.35f));
            VoxelLevelConfig lvl3 = CreateOrUpdateConfig($"{configDir}/Level_03_House.asset", 3, "关卡 3: 像素复古房屋", houseAsset, new int[] { 60, 80, 100, 125, 150 }, new Color(0.15f, 0.20f, 0.28f));
            VoxelLevelConfig lvl4 = CreateOrUpdateConfig($"{configDir}/Level_04_Giantpanda.asset", 4, "关卡 4: 国宝大熊猫 (竹林食客)", pandaAsset ?? duckAsset, new int[] { 50, 75, 100, 125, 150 }, new Color(0.12f, 0.22f, 0.18f));

            // 3. 构建独立的游戏主场景
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string scenePath = $"{sceneDir}/VoxelShooterGameMainScene.unity";

            // 相机 (适配 1080x1920 竖屏视野，模型在上、槽位在中、队列在下)
            GameObject camObj = new GameObject("Main Camera");
            Camera cam = camObj.AddComponent<Camera>();
            camObj.tag = "MainCamera";
            camObj.transform.position = new Vector3(0f, 0.2f, -14.5f);
            camObj.transform.rotation = Quaternion.Euler(5.5f, 0f, 0f);
            cam.fieldOfView = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.15f, 0.20f, 0.28f);

            // 灯光
            GameObject lightObj = new GameObject("Directional Light");
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.0f, 0.98f, 0.92f);
            light.intensity = 1.3f;
            lightObj.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            // 碎片物理管理器
            GameObject debrisObj = new GameObject("VoxelDebrisManager");
            debrisObj.AddComponent<VoxelDebrisManager>();

            // 目标体素模型 (居中位于屏幕上半区域，与顶部HUD和底部槽位完美留白)
            GameObject targetObj = new GameObject("VoxelTargetModel");
            targetObj.transform.position = new Vector3(0f, 0.8f, 0f);
            VoxelModelInstance targetModel = targetObj.AddComponent<VoxelModelInstance>();
            targetModel.voxelAsset = duckAsset;
            Shader s = Shader.Find("VoxelBaker/URP/VoxelLit");
            if (s != null) targetModel.voxelMaterial = new Material(s);
            targetModel.InitializeModel();

            // 添加 3D 自转与浮动控制器
            VoxelModelRotator rotator = targetObj.AddComponent<VoxelModelRotator>();
            rotator.autoRotate = true;
            rotator.rotateSpeed = 22f;

            // 中间 5 联装活动槽位 (Slot Manager)
            GameObject slotObj = new GameObject("VoxelSlotManager");
            VoxelSlotManager slotManager = slotObj.AddComponent<VoxelSlotManager>();

            // 底部待命方块排队队列 (Queue Manager)
            GameObject queueObj = new GameObject("VoxelQueueManager");
            VoxelQueueManager queueManager = queueObj.AddComponent<VoxelQueueManager>();
            queueManager.slotManager = slotManager;
            queueManager.targetModel = targetModel;

            // 关卡核心总控 (Level Manager)
            GameObject lmObj = new GameObject("VoxelLevelManager");
            VoxelLevelManager levelManager = lmObj.AddComponent<VoxelLevelManager>();
            levelManager.mainCamera = cam;
            levelManager.targetModelInstance = targetModel;
            levelManager.slotManager = slotManager;
            levelManager.queueManager = queueManager;
            levelManager.levelPlaylists = new List<VoxelLevelConfig> { lvl1, lvl2, lvl3, lvl4 };

            // HUD 游戏界面
            GameObject hudObj = new GameObject("VoxelGameHUD");
            hudObj.AddComponent<VoxelGameHUD>();

            // 保存场景
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("体素游戏框架", $"🎮 独立游戏框架主场景已成功生成于:\n'{scenePath}'\n\n该框架完全独立于烘焙工具，支持自由扩展、替换关卡与打包发布！点击 Play 即可开始游玩！", "确定");
        }

        private static VoxelLevelConfig CreateOrUpdateConfig(string path, int idx, string title, VoxelAsset asset, int[] powers, Color bg)
        {
            VoxelLevelConfig cfg = AssetDatabase.LoadAssetAtPath<VoxelLevelConfig>(path);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<VoxelLevelConfig>();
                AssetDatabase.CreateAsset(cfg, path);
            }

            cfg.levelIndex = idx;
            cfg.levelTitle = title;
            cfg.targetAsset = asset;
            cfg.spawnPosition = new Vector3(0f, 0.8f, 0f);
            cfg.spawnScale = 0.95f;
            cfg.initialCannonPowers = powers;
            cfg.backgroundColor = bg;
            cfg.winDestructionRatio = 1.0f; // 必须 100% 彻底消灭所有体素才判定通关
            cfg.rewardCoins = 500 * idx;

            EditorUtility.SetDirty(cfg);
            return cfg;
        }
    }
}
