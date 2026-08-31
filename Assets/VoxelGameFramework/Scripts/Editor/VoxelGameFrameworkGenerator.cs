using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VoxelBaker.Data;
using VoxelBaker.Editor;
using VoxelBaker.Runtime;
using VoxelGameFramework.Audio;
using VoxelGameFramework.Cannons;
using VoxelGameFramework.Core;
using VoxelGameFramework.Level;
using VoxelGameFramework.UI;

namespace VoxelGameFramework.Editor
{
    public static class VoxelGameFrameworkGenerator
    {
        [MenuItem("Tools/Voxel Game Framework/🚀 一键生成重构版游戏场景 (GameFramework Architecture)", false, 20)]
        public static void GenerateStandaloneGame()
        {
            string configDir = "Assets/VoxelGameFramework/Configs";
            string sceneDir = "Assets/Scenes";
            if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
            if (!Directory.Exists(sceneDir)) Directory.CreateDirectory(sceneDir);

            string scenePath = $"{sceneDir}/VoxelShooterGameMainScene.unity";
            bool sceneExists = File.Exists(scenePath);
            if (sceneExists)
            {
                bool confirm = EditorUtility.DisplayDialog("⚠️ 确认重新生成场景？",
                    "此操作会【清空并重建】当前主场景 VoxelShooterGameMainScene.unity：\n\n" +
                    "• 场景中手动摆放的对象（炮台、竹子、排队方块等）会被删除\n" +
                    "• 重新生成前会自动备份一份到同目录\n\n" +
                    "如果你只是想运行游戏，请勿点“确定”。",
                    "确定重建（先备份）", "取消");
                if (!confirm) return;
            }

            // 1. 查找并加载工程中的体素资产 (ScriptableObject)
            string[] guids = AssetDatabase.FindAssets("t:VoxelAsset");
            List<VoxelAsset> loadedAssets = new List<VoxelAsset>();
            foreach (var g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                VoxelAsset va = AssetDatabase.LoadAssetAtPath<VoxelAsset>(p);
                if (va != null) loadedAssets.Add(va);
            }

            VoxelAsset primaryAsset = loadedAssets.Count > 0 ? loadedAssets[0] : null;

            // 2. 为每个 VoxelAsset 生成/更新关卡配置
            List<VoxelLevelConfig> configs = new List<VoxelLevelConfig>();
            for (int i = 0; i < loadedAssets.Count; i++)
            {
                VoxelAsset va = loadedAssets[i];
                string cleanName = va.name.Replace("VoxelModel_", "");
                VoxelLevelConfig cfg = CreateOrUpdateConfig($"{configDir}/Level_{i + 1:D2}_{cleanName}.asset", i + 1, $"关卡 {i + 1}: {cleanName}", va, new Color(0.15f, 0.20f, 0.28f));
                configs.Add(cfg);
            }

            // 3. 构建独立游戏主场景 (GameFramework 重构架构)
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // === 相机 (适配 1080x1920 竖屏, 含 AudioListener) ===
            GameObject camObj = new GameObject("Main Camera");
            Camera cam = camObj.AddComponent<Camera>();
            camObj.tag = "MainCamera";
            camObj.transform.position = new Vector3(0f, 0.2f, -14.5f);
            camObj.transform.rotation = Quaternion.Euler(5.5f, 0f, 0f);
            cam.fieldOfView = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.15f, 0.20f, 0.28f);
            camObj.AddComponent<AudioListener>();

            // === 灯光 ===
            GameObject lightObj = new GameObject("Directional Light");
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.0f, 0.98f, 0.92f);
            light.intensity = 1.3f;
            lightObj.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            // === 碎片物理管理器 (已有对象池) ===
            GameObject debrisObj = new GameObject("VoxelDebrisManager");
            debrisObj.AddComponent<VoxelDebrisManager>();

            // === 目标体素模型载体 (由关卡 SO 运行时注入) ===
            GameObject targetObj = new GameObject("VoxelTargetModel");
            targetObj.transform.position = new Vector3(0f, 0.8f, 0f);
            VoxelModelInstance targetModel = targetObj.AddComponent<VoxelModelInstance>();
            targetModel.voxelAsset = null;
            Shader s = Shader.Find("VoxelBaker/URP/VoxelLit");
            if (s != null) targetModel.voxelMaterial = new Material(s);

            // 3D 自转与浮动控制器
            VoxelModelRotator rotator = targetObj.AddComponent<VoxelModelRotator>();
            rotator.autoRotate = true;
            rotator.rotateSpeed = 22f;

            // === 中间 5 联装活动槽位 (Slot Manager) ===
            GameObject slotObj = new GameObject("VoxelSlotManager");
            VoxelSlotManager slotManager = slotObj.AddComponent<VoxelSlotManager>();

            // === 底部待命方块排队队列 (Queue Manager) ===
            GameObject queueObj = new GameObject("VoxelQueueManager");
            VoxelQueueManager queueManager = queueObj.AddComponent<VoxelQueueManager>();
            queueManager.slotManager = slotManager;
            queueManager.targetModel = targetModel;

            // === 关卡核心总控 (Level Manager, Procedure 驱动) ===
            GameObject lmObj = new GameObject("VoxelLevelManager");
            VoxelLevelManager levelManager = lmObj.AddComponent<VoxelLevelManager>();
            levelManager.drivenByProcedure = true; // 由 GameFramework Procedure 驱动
            levelManager.mainCamera = cam;
            levelManager.targetModelInstance = targetModel;
            levelManager.slotManager = slotManager;
            levelManager.queueManager = queueManager;
            levelManager.levels = configs;

            // === GameFramework 入口组件 (重构核心: 驱动 Procedure FSM + Event + DataNode) ===
            GameObject gfObj = new GameObject("GameFrameworkEntry");
            gfObj.AddComponent<GameFrameworkEntryComponent>();

            // === 音效管理器 (程序化合成, 零资产依赖) ===
            GameObject soundObj = new GameObject("VoxelSoundManager");
            soundObj.AddComponent<VoxelSoundManager>();

            // === 子弹对象池 ===
            GameObject poolObj = new GameObject("VoxelBulletPool");
            poolObj.AddComponent<VoxelBulletPool>();

            // === uGUI 总管理器 (仅顶部 HUD + 弹窗 + 设置, 方块/槽位保持 3D) ===
            GameObject uiObj = new GameObject("VoxelUIManager");
            VoxelUIManager uiManager = uiObj.AddComponent<VoxelUIManager>();
            uiManager.levelManager = levelManager;
            uiManager.soundManager = soundObj.GetComponent<VoxelSoundManager>();

            // 保存场景（覆盖前先自动备份，防止手滑丢失手动摆放内容）
            if (File.Exists(scenePath))
            {
                string backupPath = $"{scenePath}.bak_{System.DateTime.Now:yyyyMMdd_HHmmss}";
                File.Copy(scenePath, backupPath, true);
            }
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("体素游戏框架", $"🎮 重构版独立游戏框架场景已成功生成于:\n'{scenePath}'\n\n包含:\n• GameFrameworkEntry (Procedure 状态机)\n• VoxelLevelManager (关卡 SO 驱动)\n• VoxelUIManager (uGUI 分层界面)\n• VoxelSoundManager (程序化音效)\n• VoxelBulletPool (子弹对象池)\n\n点击 Play 即可开始游玩！", "确定");
        }

        private static VoxelLevelConfig CreateOrUpdateConfig(string path, int idx, string title, VoxelAsset asset, Color bg)
        {
            VoxelLevelConfig cfg = AssetDatabase.LoadAssetAtPath<VoxelLevelConfig>(path);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<VoxelLevelConfig>();
                AssetDatabase.CreateAsset(cfg, path);
            }

            cfg.levelIndex = idx;
            cfg.levelTitle = title;
            cfg.targetVoxelAsset = asset;
            cfg.spawnPosition = new Vector3(0f, 0.8f, 0f);
            cfg.spawnScale = 0.95f;
            cfg.backgroundColor = bg;
            cfg.winDestructionRatio = 0.95f;
            cfg.rewardCoins = 500 * idx;

            EditorUtility.SetDirty(cfg);
            return cfg;
        }
    }
}
