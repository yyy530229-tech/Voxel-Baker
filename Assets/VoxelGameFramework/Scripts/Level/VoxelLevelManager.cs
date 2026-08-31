using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Data;
using VoxelBaker.Runtime;
using VoxelGameFramework.Audio;
using VoxelGameFramework.Cannons;
using VoxelGameFramework.Core;
using VoxelGameFramework.Events;

namespace VoxelGameFramework.Level
{
    /// <summary>
    /// 关卡装配器 (Voxel Level Manager)
    ///
    /// 职责边界 (GameFramework 规范化后):
    ///   ✅ 管理关卡 ScriptableObject 播放列表
    ///   ✅ 装配: 注入体素模型资产 / 归一化缩放 / 重置槽位与排队队列 / 设置相机氛围色
    ///   ✅ 评估破坏进度并在达成目标时广播通关事件
    ///   ❌ 不再自己驱动 Update 做胜负判定 —— 那属于 ProcedureGameplay 的职责
    ///   ❌ 不再使用静态事件总线 —— 一律走 GameEventBus (GameFramework IEventManager)
    ///
    /// 由谁驱动 TickProgress():
    ///   - drivenByProcedure = true  → ProcedureGameplay.OnUpdate 每帧调用 (推荐)
    ///   - drivenByProcedure = false → 自身 Update 调用 (无 GameFramework 的降级路径)
    /// </summary>
    public class VoxelLevelManager : MonoBehaviour
    {
        /// <summary>
        /// 关卡破坏进度快照
        /// </summary>
        public struct LevelProgress
        {
            /// <summary>已破坏体素占初始总数的比例 (0~1)</summary>
            public float Ratio;
            /// <summary>已破坏体素数量</summary>
            public int DestroyedCount;
            /// <summary>当前存活体素数量</summary>
            public int ActiveCount;
            /// <summary>本关初始体素总数</summary>
            public int TotalCount;
            /// <summary>本帧是否刚刚达成通关条件</summary>
            public bool WonThisTick;
        }

        [Header("📋 关卡播放列表 (把创建好的关卡 SO 拖入这里)")]
        [UnityEngine.Serialization.FormerlySerializedAs("levelPlaylists")]
        [Tooltip("关卡配置列表，可按序自由增删或拖拽排序")]
        public List<VoxelLevelConfig> levels = new List<VoxelLevelConfig>();

        [Tooltip("当前正在游玩的关卡下标 (从 0 开始)")]
        public int currentLevelIndex = 0;

        [Header("🎯 场景组件引用")]
        [Tooltip("场景中的体素模型载体 (无需预设模型，关卡 SO 运行时自动注入赋值)")]
        public VoxelModelInstance targetModelInstance;

        [Tooltip("5 联装活动槽位管理器")]
        public VoxelSlotManager slotManager;

        [Tooltip("消除方块排队队列管理器")]
        public VoxelQueueManager queueManager;

        [Tooltip("主渲染相机")]
        public Camera mainCamera;

        [Header("💰 玩家状态")]
        public int totalCoins = 1000;
        public bool isLevelFinished = false;

        private VoxelLevelConfig _currentConfig;
        private int _initialTotalVoxels = 0;

        /// <summary>当前加载的关卡配置 (只读)</summary>
        public VoxelLevelConfig CurrentConfig => _currentConfig;

        /// <summary>当前关卡初始体素总数</summary>
        public int InitialTotalVoxels => _initialTotalVoxels;

        /// <summary>防止 Start 重复加载 (由 Procedure 驱动)</summary>
        [Tooltip("由 GameFramework Procedure 驱动加载 (取消勾选则由自身 Start 加载)")]
        public bool drivenByProcedure = true;

        private void Awake()
        {
            // 不再暴露 static Instance。防重复挂载改为检查服务容器。
            if (ServiceLocator.IsRegistered<VoxelLevelManager>())
            {
                Destroy(gameObject);
                return;
            }
            ServiceLocator.Register(this);
        }

        private void Start()
        {
            // 若由 Procedure 驱动则跳过自身加载
            if (drivenByProcedure) return;

            if (levels != null && levels.Count > 0)
            {
                LoadLevel(currentLevelIndex);
            }
            else
            {
                Debug.LogWarning("[VoxelLevelManager] 当前关卡列表为空！请在 Inspector 面板中将关卡配置 ScriptableObject 拖入 levels 列表中。");
            }
        }

        /// <summary>
        /// 加载并启动指定关卡
        /// </summary>
        public void LoadLevel(int levelIdx)
        {
            if (levels == null || levels.Count == 0) return;

            // 环绕式取模: 越过最后一关回到第 1 关。
            // 这样与 ProcedureLevelComplete 里只做自增的 DataNode 索引天然一致 ——
            // 无论关卡索引被加到多大, 总能映射到有效关卡, 不用在两处各写一回绕逻辑。
            currentLevelIndex = ((levelIdx % levels.Count) + levels.Count) % levels.Count;
            _currentConfig = levels[currentLevelIndex];
            isLevelFinished = false;

            if (_currentConfig == null)
            {
                Debug.LogError($"[VoxelLevelManager] 关卡索引 [{currentLevelIndex}] 处的配置为 null！");
                return;
            }

            // 1. 设置主相机背景氛围色
            if (mainCamera != null)
            {
                mainCamera.backgroundColor = _currentConfig.backgroundColor;
            }

            // 2. 动态注入关卡 SO 中指定的体素模型资产并初始化
            if (targetModelInstance != null && _currentConfig.targetVoxelAsset != null)
            {
                targetModelInstance.voxelAsset = _currentConfig.targetVoxelAsset;
                targetModelInstance.transform.position = _currentConfig.spawnPosition;
                targetModelInstance.transform.rotation = Quaternion.Euler(_currentConfig.spawnRotation);

                // 自动将任意原始尺寸的模型智能归一化至标准游戏视野大小 (约 2.8m 跨度)，彻底告别过小/过大
                float assetMaxDim = Mathf.Max(targetModelInstance.voxelAsset.boundsSize.x, 
                                    Mathf.Max(targetModelInstance.voxelAsset.boundsSize.y, targetModelInstance.voxelAsset.boundsSize.z));
                float autoFitScale = (assetMaxDim > 0.001f) ? (2.8f / assetMaxDim) : 1.0f;
                targetModelInstance.transform.localScale = Vector3.one * (autoFitScale * _currentConfig.spawnScale);

                targetModelInstance.InitializeModel();

                _initialTotalVoxels = targetModelInstance.ActiveVoxelCount;
            }
            else if (_currentConfig.targetVoxelAsset == null)
            {
                Debug.LogWarning($"[VoxelLevelManager] 关卡 [{_currentConfig.levelTitle}] 未分配 targetVoxelAsset！请在该关卡 SO 中拖入烘焙好的体素资产。");
                _initialTotalVoxels = 0;
            }

            // 3. 重置活动槽位
            if (slotManager != null)
            {
                slotManager.ClearAll();
                slotManager.InitializeSlots();
            }

            // 4. 根据当前模型存活体素自动生成消除队列
            if (queueManager != null)
            {
                queueManager.slotManager = slotManager;
                queueManager.SetupQueueFromModel(targetModelInstance);
            }

            // 5. 触发初始进度广播
            GameEventBus.Fire(this, DestructionProgressEventArgs.Create(
                0f, _initialTotalVoxels, _initialTotalVoxels));

            Debug.Log($"[VoxelLevelManager] 关卡已装配: [{_currentConfig.levelTitle}] 体素总数 {_initialTotalVoxels}");
        }

        /// <summary>
        /// 每帧评估破坏进度并广播。由 ProcedureGameplay 驱动 (drivenByProcedure=true),
        /// 或在无 GameFramework 的降级路径下由自身 Update 驱动。
        /// </summary>
        /// <returns>当前进度快照；关卡已结束或数据未就绪时 WonThisTick 恒为 false</returns>
        public LevelProgress TickProgress()
        {
            var progress = new LevelProgress
            {
                Ratio = 0f,
                DestroyedCount = 0,
                ActiveCount = _initialTotalVoxels,
                TotalCount = _initialTotalVoxels,
                WonThisTick = false
            };

            if (isLevelFinished || targetModelInstance == null || _currentConfig == null || _initialTotalVoxels <= 0)
            {
                return progress;
            }

            int destroyed = targetModelInstance.DestroyedVoxelCount;
            progress.DestroyedCount = destroyed;
            progress.ActiveCount = targetModelInstance.ActiveVoxelCount;
            progress.Ratio = Mathf.Clamp01((float)destroyed / _initialTotalVoxels);

            // 进度广播 (UI 进度条、HUD 都订阅这个事件)
            GameEventBus.Fire(this, DestructionProgressEventArgs.Create(
                progress.Ratio, progress.ActiveCount, progress.TotalCount));

            // 通关胜负判定
            if (progress.Ratio >= _currentConfig.winDestructionRatio)
            {
                OnLevelWin(progress.DestroyedCount);
                progress.WonThisTick = true;
            }

            return progress;
        }

        private void Update()
        {
            // 降级路径: 没有 GameFramework 驱动时自己跑, 行为与旧版本保持一致
            if (drivenByProcedure) return;
            TickProgress();
        }

        private void OnLevelWin(int destroyedCount)
        {
            isLevelFinished = true;
            totalCoins += _currentConfig.rewardCoins;

            // 通关与金币奖励分别广播, 让 UI / 音效 / 存档各自按需订阅
            GameEventBus.Fire(this, LevelCompletedEventArgs.Create(
                currentLevelIndex + 1, destroyedCount, _currentConfig.rewardCoins));
            GameEventBus.Fire(this, CoinEarnedEventArgs.Create(
                _currentConfig.rewardCoins, targetModelInstance.transform.position));

            // 通关音效 (改发命令事件, 由 VoxelSoundManager 订阅执行)
            GameEventBus.Fire(this, SfxPlayedEventArgs.Create(
                VoxelSoundManager.SfxType.Win, 1f));

            Debug.Log($"🎉 恭喜通关 [{_currentConfig.levelTitle}]！获得金币: {_currentConfig.rewardCoins}");
        }

        public void NextLevel()
        {
            LoadLevel(currentLevelIndex + 1);
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
