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
    /// 通关结算流程: 展示胜利 → 等待玩家点"下一关" → 推进关卡索引 → 回到 Gameplay
    ///
    /// 关卡推进的唯一触发点是 NextLevelRequestedEventArgs (由 UI 按钮发出)。
    /// 旧版本在这里用 2 秒定时器自动推进, 会与胜利弹窗按钮各推一次导致跳两关, 已移除。
    /// </summary>
    public class ProcedureLevelComplete : ProcedureBase
    {
        private VoxelLevelManager _levelManager;
        private ProcedureOwner _procedureOwner;
        private bool _subscribed;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Debug.Log("[ProcedureLevelComplete] Enter");

            _procedureOwner = procedureOwner;
            _levelManager = ServiceLocator.Get<VoxelLevelManager>();

            // 订阅"下一关"请求 —— 由胜利弹窗按钮发出
            if (!_subscribed)
            {
                _subscribed = true;
                GameEventBus.Subscribe(NextLevelRequestedEventArgs.EventId, OnNextLevelRequested);
            }

            // 胜利弹窗的展示由 VoxelUIManager 订阅 LevelCompletedEventArgs 自行处理,
            // 这里不直接操作 UI, 保持流程与表现层解耦。
        }

        protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
            // 等待玩家操作, 不做任何自动推进
        }

        protected override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
        {
            base.OnLeave(procedureOwner, isShutdown);

            if (_subscribed)
            {
                _subscribed = false;
                GameEventBus.Unsubscribe(NextLevelRequestedEventArgs.EventId, OnNextLevelRequested);
            }

            _levelManager = null;
            _procedureOwner = null;
        }

        private void OnNextLevelRequested(object sender, GameEventArgs e)
        {
            var dataNode = GameFrameworkEntry.GetModule<IDataNodeManager>();
            int currentLevel = dataNode.GetData<VarInt32>("Game.CurrentLevelIndex");
            dataNode.SetData<VarInt32>("Game.CurrentLevelIndex", currentLevel + 1);

            if (_levelManager != null)
            {
                // 同步关卡装配器的内部索引, 保证 DataNode 与管理器两侧一致
                _levelManager.currentLevelIndex = currentLevel + 1;
            }

            Debug.Log($"[ProcedureLevelComplete] 推进到关卡索引 {currentLevel + 1}");

            if (_procedureOwner != null)
            {
                ChangeState<ProcedureGameplay>(_procedureOwner);
            }
        }
    }
}
