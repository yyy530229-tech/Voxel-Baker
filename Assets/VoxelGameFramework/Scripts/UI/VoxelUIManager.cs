using GameFramework;
using GameFramework.Event;
using UnityEngine;
using UnityEngine.UI;
using VoxelGameFramework.Audio;
using VoxelGameFramework.Cannons;
using VoxelGameFramework.Core;
using VoxelGameFramework.Events;
using VoxelGameFramework.Level;

namespace VoxelGameFramework.UI
{
    /// <summary>
    /// 游戏 UI 总管理器 (uGUI 运行时构建)
    /// 分层 (严格按参考图):
    ///   3D 场景: 体素目标模型 + 炮台/槽位底座 + 底部数字方块 + 飞行子弹
    ///   2D UI:   顶部 HUD (金币/关卡/设置/进度) + 通关弹窗 + 设置面板
    /// 方块与槽位是 3D 元素, 不重复创建 2D UI。
    ///
    /// GameFramework 规范化后:
    ///   - 破坏进度由 DestructionProgressEventArgs 推送, 不再逐帧从体素模型拉取
    ///   - 通关弹窗由 LevelCompletedEventArgs 触发, 不再轮询 levelManager.isLevelFinished
    ///   - "下一关"按钮只发 NextLevelRequestedEventArgs, 由 ProcedureLevelComplete 决定如何推进,
    ///     避免 UI 与流程状态机各自推进一次导致跳两关
    ///   - 没有 GameFrameworkEntryComponent 时走降级路径, 直接调用关卡管理器
    /// </summary>
    public class VoxelUIManager : MonoBehaviour
    {
        [Header("场景引用")]
        [Tooltip("关卡管理器 (金币/关卡标题/进度)")]
        public VoxelLevelManager levelManager;
        [Tooltip("音效管理器 (可选)")]
        public VoxelSoundManager soundManager;

        [Header("UI 配置")]
        [Tooltip("UI 根 Canvas (运行时自动创建)")]
        public Canvas uiCanvas;

        private HUDForm _hudForm;
        private VictoryForm _victoryForm;
        private SettingsForm _settingsForm;
        private bool _victoryVisible = false;
        private bool _subscribed = false;

        private void Awake()
        {
            // 不再用 FindObjectOfType 全场景扫描。
            // 改为从服务容器获取 (ServiceLocator 内部有首次兜底 FindObjectOfType, 注册后走缓存,
            // 因此即使本组件 Awake 早于 GameFrameworkEntryComponent.Bootstrap 也能安全拿到)。
            if (levelManager == null) levelManager = ServiceLocator.Get<VoxelLevelManager>();
            if (soundManager == null) soundManager = ServiceLocator.Get<VoxelSoundManager>();
        }

        private void Start()
        {
            BuildUI();
            TrySubscribeEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
        }

        private void Update()
        {
            // 自愈重试: GameFrameworkEntryComponent.Start() 与本组件的 Start() 之间没有
            // 保证的先后顺序 (除非在 .meta 里配 executionOrder), 所以启动首帧可能还没就绪。
            // 订阅成功后 _subscribed 置位, 这里就退化成一次布尔判断, 开销可忽略。
            if (!_subscribed) TrySubscribeEvents();

            // 只刷新低频的金币/关卡标题 (内部有脏检查, 值不变时不触碰 TMP)
            _hudForm?.RefreshStaticInfo();
        }

        private void BuildUI()
        {
            if (uiCanvas == null)
            {
                var canvasGo = new GameObject("GameUICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasGo.transform.SetParent(transform);
                uiCanvas = canvasGo.GetComponent<Canvas>();
                uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                uiCanvas.sortingOrder = 10;

                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = 0.5f;
            }

            var rootRt = (RectTransform)uiCanvas.transform;

            // 1. 顶部 HUD (金币/关卡/设置/进度条)
            var hudLayer = CreateLayer("HUDLayer", rootRt, 40);
            _hudForm = new HUDForm();
            _hudForm.Build(hudLayer, levelManager, OnSettingsClicked);

            // 2. 胜利弹窗 (初始隐藏)
            var modalLayer = CreateLayer("ModalLayer", rootRt, 50);
            modalLayer.gameObject.SetActive(false);
            _victoryForm = new VictoryForm();
            _victoryForm.Build(modalLayer, OnNextLevelClicked);

            // 3. 设置面板 (初始隐藏)
            var settingsLayer = CreateLayer("SettingsLayer", rootRt, 60);
            _settingsForm = new SettingsForm();
            _settingsForm.Build(settingsLayer, soundManager, levelManager);

            Debug.Log("[VoxelUIManager] UI 构建完成 (HUD/Modal/Settings, 3D 方块与槽位保持场景 3D)");
        }

        #region GameFramework 事件

        private void TrySubscribeEvents()
        {
            if (_subscribed) return;

            // 事件总线还没就绪时 GameEventBus 会静默丢弃订阅, 这里直接判定跳过等待下一帧重试
            if (!GameEventBus.IsAvailable) return;

            _subscribed = true;

            GameEventBus.Subscribe(DestructionProgressEventArgs.EventId, OnDestructionProgress);
            GameEventBus.Subscribe(LevelCompletedEventArgs.EventId, OnLevelCompleted);
            GameEventBus.Subscribe(LevelLoadedEventArgs.EventId, OnLevelLoaded);
        }

        private void UnsubscribeEvents()
        {
            if (!_subscribed) return;
            _subscribed = false;

            GameEventBus.Unsubscribe(DestructionProgressEventArgs.EventId, OnDestructionProgress);
            GameEventBus.Unsubscribe(LevelCompletedEventArgs.EventId, OnLevelCompleted);
            GameEventBus.Unsubscribe(LevelLoadedEventArgs.EventId, OnLevelLoaded);
        }

        private void OnDestructionProgress(object sender, GameEventArgs e)
        {
            var args = (DestructionProgressEventArgs)e;
            _hudForm?.SetProgress(args.ProgressRatio);
        }

        private void OnLevelCompleted(object sender, GameEventArgs e)
        {
            var args = (LevelCompletedEventArgs)e;
            ShowVictory(args.RewardCoins);
        }

        private void OnLevelLoaded(object sender, GameEventArgs e)
        {
            // 换关: 收起胜利弹窗, 清空 HUD 脏检查缓存强制全量刷新
            _victoryVisible = false;
            HideModalLayer();
            _hudForm?.InvalidateCache();
        }

        #endregion

        private void OnSettingsClicked()
        {
            _settingsForm?.Open();
            soundManager?.PlaySfx(VoxelSoundManager.SfxType.Click, 0.8f);
        }

        private void OnNextLevelClicked()
        {
            HideVictory();

            if (GameEventBus.IsAvailable)
            {
                // 正规路径: 交给 ProcedureLevelComplete 推进关卡索引并切回 Gameplay
                GameEventBus.Fire(this, NextLevelRequestedEventArgs.Create());
            }
            else
            {
                // 降级路径: 场景里没有 GameFrameworkEntryComponent, 直接推进
                levelManager?.NextLevel();
            }
        }

        private RectTransform CreateLayer(string name, Transform parent, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private void ShowVictory(int rewardCoins)
        {
            if (_victoryVisible) return;
            _victoryVisible = true;

            var modalLayer = uiCanvas.transform.Find("ModalLayer");
            if (modalLayer != null)
            {
                modalLayer.gameObject.SetActive(true);
                _victoryForm.SetReward(rewardCoins > 0 ? rewardCoins : GetFallbackReward());
            }
        }

        private int GetFallbackReward()
        {
            return levelManager != null && levelManager.CurrentConfig != null
                ? levelManager.CurrentConfig.rewardCoins : 500;
        }

        private void HideVictory()
        {
            _victoryVisible = false;
            HideModalLayer();
        }

        private void HideModalLayer()
        {
            if (uiCanvas == null) return;
            var modalLayer = uiCanvas.transform.Find("ModalLayer");
            if (modalLayer != null) modalLayer.gameObject.SetActive(false);
        }
    }
}
