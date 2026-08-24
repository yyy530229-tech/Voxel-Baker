using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Data;
using VoxelBaker.Runtime;
using VoxelGameFramework.Cannons;
using VoxelGameFramework.Core;

namespace VoxelGameFramework.Level
{
    /// <summary>
    /// 独立游戏关卡管理器 (Level Manager)
    /// 负责关卡流程加载、目标模型生成、通关判定与奖励分发
    /// </summary>
    public class VoxelLevelManager : MonoBehaviour
    {
        public static VoxelLevelManager Instance { get; private set; }

        [Header("关卡列表配置")]
        public List<VoxelLevelConfig> levelPlaylists = new List<VoxelLevelConfig>();
        public int currentLevelIndex = 0;

        [Header("核心场景引用")]
        public VoxelModelInstance targetModelInstance;
        public VoxelSlotManager slotManager;
        public VoxelQueueManager queueManager;
        public VoxelCannonSquad cannonSquad;
        public Camera mainCamera;

        [Header("玩家游戏状态")]
        public int totalCoins = 1000;
        public bool isLevelFinished = false;

        private VoxelLevelConfig _currentConfig;
        private int _initialTotalVoxels = 0;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) { Destroy(gameObject); return; }
        }

        private void Start()
        {
            if (levelPlaylists.Count > 0)
            {
                LoadLevel(currentLevelIndex);
            }
        }

        public void LoadLevel(int levelIdx)
        {
            if (levelPlaylists == null || levelPlaylists.Count == 0) return;

            currentLevelIndex = Mathf.Clamp(levelIdx, 0, levelPlaylists.Count - 1);
            _currentConfig = levelPlaylists[currentLevelIndex];
            isLevelFinished = false;

            // 1. 设置背景色
            if (mainCamera != null && _currentConfig != null)
            {
                mainCamera.backgroundColor = _currentConfig.backgroundColor;
            }

            // 2. 加载目标体素模型
            if (targetModelInstance != null && _currentConfig != null && _currentConfig.targetAsset != null)
            {
                targetModelInstance.transform.position = _currentConfig.spawnPosition;
                targetModelInstance.transform.rotation = Quaternion.Euler(_currentConfig.spawnRotation);
                targetModelInstance.transform.localScale = Vector3.one * _currentConfig.spawnScale;

                targetModelInstance.voxelAsset = _currentConfig.targetAsset;
                targetModelInstance.InitializeModel();

                _initialTotalVoxels = targetModelInstance.ActiveVoxelCount;
            }

            // 3. 初始化 5 联装活动槽位与底部排队队列
            List<Color32> levelColors = new List<Color32>();
            if (_currentConfig != null && _currentConfig.targetAsset != null && _currentConfig.targetAsset.palette != null && _currentConfig.targetAsset.palette.entries.Count > 0)
            {
                foreach (var e in _currentConfig.targetAsset.palette.entries)
                {
                    if (e.baseColor.a > 0) levelColors.Add((Color32)e.baseColor);
                }
            }
            if (levelColors.Count == 0)
            {
                levelColors = new List<Color32>
                {
                    new Color32(230, 40, 50, 255),  // 红
                    new Color32(50, 130, 230, 255), // 蓝
                    new Color32(140, 80, 40, 255),  // 棕
                    new Color32(245, 200, 30, 255)  // 黄
                };
            }

            if (slotManager != null)
            {
                slotManager.ClearAll();
                slotManager.InitializeSlots();
            }

            if (queueManager != null)
            {
                queueManager.slotManager = slotManager;
                queueManager.SetupQueueFromModel(targetModelInstance);
            }

            // 触发初始进度更新
            VoxelGameEvents.OnDestructionProgressChanged?.Invoke(0f, _initialTotalVoxels, _initialTotalVoxels);
        }

        private void Update()
        {
            if (isLevelFinished || targetModelInstance == null || _currentConfig == null || _initialTotalVoxels <= 0) return;

            int destroyed = targetModelInstance.DestroyedVoxelCount;
            float ratio = (float)destroyed / _initialTotalVoxels;

            VoxelGameEvents.OnDestructionProgressChanged?.Invoke(ratio, targetModelInstance.ActiveVoxelCount, _initialTotalVoxels);

            // 通关判定
            if (ratio >= _currentConfig.winDestructionRatio)
            {
                OnLevelWin();
            }
        }

        private void OnLevelWin()
        {
            isLevelFinished = true;
            totalCoins += _currentConfig.rewardCoins;

            VoxelGameEvents.OnLevelCompleted?.Invoke(currentLevelIndex + 1, targetModelInstance.DestroyedVoxelCount);
            VoxelGameEvents.OnCoinEarned?.Invoke(_currentConfig.rewardCoins, targetModelInstance.transform.position);

            Debug.Log($"🎉 恭喜通关关卡 [{_currentConfig.levelTitle}]！获得金币奖励: {_currentConfig.rewardCoins}");
        }

        public void NextLevel()
        {
            if (currentLevelIndex < levelPlaylists.Count - 1)
            {
                LoadLevel(currentLevelIndex + 1);
            }
            else
            {
                // 循环重温第 1 关
                LoadLevel(0);
            }
        }

        public void RestartCurrentLevel()
        {
            LoadLevel(currentLevelIndex);
        }

        public void AddCoins(int amount)
        {
            totalCoins += amount;
        }
    }
}
