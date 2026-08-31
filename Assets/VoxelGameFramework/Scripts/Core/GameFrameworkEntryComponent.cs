using GameFramework;
using GameFramework.DataNode;
using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.ObjectPool;
using GameFramework.Procedure;
using UnityEngine;

namespace VoxelGameFramework.Core
{
    /// <summary>
    /// GameFramework 入口组件 (Unity 桥接)
    /// 职责:
    /// 1. 驱动 GameFrameworkEntry.Update/Shutdown
    /// 2. 初始化各模块并建立依赖
    /// 3. 创建 Procedure 状态机并启动
    /// </summary>
    public class GameFrameworkEntryComponent : MonoBehaviour
    {
        public static GameFrameworkEntryComponent Instance { get; private set; }

        /// <summary>
        /// GameFramework 是否已完成 Bootstrap 且 Update 正在驱动。
        /// GameEventBus 依赖此标志判断事件能否被真正投递出去。
        /// </summary>
        public bool IsInitialized => _initialized;

        [Header("Procedure 流程列表 (按需注入)")]
        [SerializeField] private bool autoBootstrap = true;

        private bool _initialized = false;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        private void Start()
        {
            if (autoBootstrap)
            {
                Bootstrap();
            }
        }

        private void Update()
        {
            if (_initialized)
            {
                GameFrameworkEntry.Update(Time.deltaTime, Time.unscaledDeltaTime);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                // 先让事件总线放开对模块的持有, 再整体关闭, 避免残留已销毁的引用
                _initialized = false;
                GameEventBus.Invalidate();

                GameFrameworkEntry.Shutdown();
                Instance = null;
            }
        }

        /// <summary>
        /// 初始化 GameFramework 全部模块
        /// </summary>
        public void Bootstrap()
        {
            if (_initialized) return;

            // 1. 获取核心模块 (自动创建)
            var eventManager = GameFrameworkEntry.GetModule<IEventManager>();
            var fsmManager = GameFrameworkEntry.GetModule<IFsmManager>();
            var dataNodeManager = GameFrameworkEntry.GetModule<IDataNodeManager>();
            var objectPoolManager = GameFrameworkEntry.GetModule<IObjectPoolManager>();

            // 先置就绪标志, 再启动 Procedure。否则 Procedure.OnEnter 里同步触发的
            // GameEventBus.Subscribe 会因 "GameFramework 尚未启动" 被静默丢弃 (详见日志里的
            // [GameEventBus] 事件 [unknown] 已被丢弃)。
            _initialized = true;

            // 各 MonoBehaviour 的 Start() 可能在 Bootstrap 之前就订阅了事件 (Start 顺序不保证)。
            // 这些订阅被缓存进 GameEventBus 的 pending 队列, 此处统一补订, 保证不丢任何订阅。
            GameEventBus.FlushPending();

            // 2. 设置 UI 系统
            SetupUIManager(objectPoolManager);

            // 3. 创建 Procedure 状态机并启动
            SetupProcedure(fsmManager);

            Debug.Log("[GameFrameworkEntry] Bootstrap complete.");
        }

        private void SetupUIManager(IObjectPoolManager objectPoolManager)
        {
            // UI 系统由 VoxelUIManager 运行时构建 (uGUI Canvas + 分层表单)
            // GameFramework UI 模块仅注册辅助器备用, 不主动创建 Canvas
            var uiManager = GameFrameworkEntry.GetModule<GameFramework.UI.IUIManager>();
            uiManager.SetObjectPoolManager(objectPoolManager);
            var uiFormHelper = new UI.UnityUIFormHelper();
            uiManager.SetUIFormHelper(uiFormHelper);
        }

        private void SetupProcedure(IFsmManager fsmManager)
        {
            var procedureManager = GameFrameworkEntry.GetModule<IProcedureManager>();

            // 创建流程状态机
            procedureManager.Initialize(
                fsmManager,
                new Procedures.ProcedureLaunch(),
                new Procedures.ProcedureMainMenu(),
                new Procedures.ProcedureGameplay(),
                new Procedures.ProcedureLevelComplete()
            );

            // 启动初始流程
            procedureManager.StartProcedure<Procedures.ProcedureLaunch>();
        }

        public T GetModule<T>() where T : class
        {
            return GameFrameworkEntry.GetModule<T>();
        }
    }
}
