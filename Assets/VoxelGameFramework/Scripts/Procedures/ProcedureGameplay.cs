using GameFramework;
using GameFramework.DataNode;
using GameFramework.Event;
using GameFramework.Procedure;
using UnityEngine;
using VoxelGameFramework.Core;
using VoxelGameFramework.Events;
using VoxelGameFramework.Level;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;

namespace VoxelGameFramework.Procedures
{
    /// <summary>
    /// 游戏主流程: 装配关卡 → 每帧评估破坏进度 → 通关后切到结算流程
    ///
    /// 这是关卡推进的唯一权威。UI 只负责发事件 (NextLevelRequested / RestartLevelRequested),
    /// 不再直接调用 VoxelLevelManager 的方法, 避免"UI 按钮推进一次 + 流程自动推进一次"的双重推进。
    ///
    /// ⚠️ 订阅/退订必须严格成对: GameFramework 的 EventPool 配置了
    /// AllowNoHandler | AllowMultiHandler, 但**没有** AllowDuplicateHandler,
    /// 重复订阅同一 (EventId, handler) 会直接抛 GameFrameworkException。
    /// 由于流程状态机切走时 isShutdown 恒为 false, 退订不能只在 isShutdown 分支里做,
    /// 否则第二次进入本流程时会在 Subscribe 处崩溃。
    /// </summary>
    public class ProcedureGameplay : ProcedureBase
    {
        private VoxelLevelManager _levelManager;
        private ProcedureOwner _procedureOwner;
        private bool _subscribed;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Debug.Log("[ProcedureGameplay] Enter");
            _procedureOwner = procedureOwner;

            // 从 DataNode 读取当前关卡索引
            var dataNode = GameFrameworkEntry.GetModule<IDataNodeManager>();
            int levelIndex = dataNode.GetData<VarInt32>("Game.CurrentLevelIndex");

            // 定位关卡装配器 (从服务容器获取, 不再使用静态单例)
            _levelManager = ServiceLocator.Get<VoxelLevelManager>();
            if (_levelManager == null)
            {
                Debug.LogError("[ProcedureGameplay] 场景中未找到 VoxelLevelManager! 无法进入玩法流程。");
                return;
            }

            // 订阅事件 (进入流程时订阅)
            SubscribeEvents();

            // 装配关卡
            _levelManager.LoadLevel(levelIndex);

            // 关卡索引可能因为越界/环绕而被 LoadLevel 修正, 回写到 DataNode 保持两边一致
            dataNode.SetData<VarInt32>("Game.CurrentLevelIndex", _levelManager.currentLevelIndex);

            // 广播关卡加载完成
            if (_levelManager.CurrentConfig != null)
            {
                GameEventBus.Fire(this, LevelLoadedEventArgs.Create(
                    _levelManager.currentLevelIndex,
                    _levelManager.CurrentConfig.levelTitle,
                    _levelManager.InitialTotalVoxels
                ));
            }
        }

        protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            if (_levelManager == null) return;

            // 破坏进度评估与通关判定统一收在这里, VoxelLevelManager 只做装配
            _levelManager.TickProgress();
        }

        protected override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
        {
            base.OnLeave(procedureOwner, isShutdown);

            // 无条件退订 —— 不管是切到结算流程还是整个框架关闭
            UnsubscribeEvents();
            _levelManager = null;
            _procedureOwner = null;
        }

        private void SubscribeEvents()
        {
            if (_subscribed) return;
            _subscribed = true;

            GameEventBus.Subscribe(LevelCompletedEventArgs.EventId, OnLevelCompleted);
            GameEventBus.Subscribe(RestartLevelRequestedEventArgs.EventId, OnRestartLevelRequested);
        }

        private void UnsubscribeEvents()
        {
            if (!_subscribed) return;
            _subscribed = false;

            GameEventBus.Unsubscribe(LevelCompletedEventArgs.EventId, OnLevelCompleted);
            GameEventBus.Unsubscribe(RestartLevelRequestedEventArgs.EventId, OnRestartLevelRequested);
        }

        private void OnLevelCompleted(object sender, GameEventArgs e)
        {
            var args = (LevelCompletedEventArgs)e;
            Debug.Log($"[ProcedureGameplay] Level {args.LevelIndex} completed! Destroyed: {args.TotalDestroyed}, Reward: {args.RewardCoins}");

            // 更新金币到 DataNode
            var dataNode = GameFrameworkEntry.GetModule<IDataNodeManager>();
            int currentCoins = dataNode.GetData<VarInt32>("Game.TotalCoins");
            dataNode.SetData<VarInt32>("Game.TotalCoins", currentCoins + args.RewardCoins);

            // 切换到通关结算流程
            if (_procedureOwner != null)
            {
                ChangeState<ProcedureLevelComplete>(_procedureOwner);
            }
        }

        private void OnRestartLevelRequested(object sender, GameEventArgs e)
        {
            if (_levelManager == null) return;
            Debug.Log("[ProcedureGameplay] 收到重开请求, 重新装配当前关卡");
            _levelManager.LoadLevel(_levelManager.currentLevelIndex);
        }
    }
}
