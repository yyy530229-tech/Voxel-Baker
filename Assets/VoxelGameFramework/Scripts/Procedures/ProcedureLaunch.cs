using GameFramework;
using GameFramework.DataNode;
using GameFramework.Procedure;
using UnityEngine;
using VoxelGameFramework.Level;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;

namespace VoxelGameFramework.Procedures
{
    /// <summary>
    /// 启动流程: 初始化全局状态 → 自动进入 Gameplay
    /// </summary>
    public class ProcedureLaunch : ProcedureBase
    {
        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Debug.Log("[ProcedureLaunch] Enter");

            // 初始化默认关卡索引与金币
            var dataNodeManager = GameFrameworkEntry.GetModule<GameFramework.DataNode.IDataNodeManager>();
            dataNodeManager.SetData<VarInt32>("Game.CurrentLevelIndex", 0);
            dataNodeManager.SetData<VarInt32>("Game.TotalCoins", 1000);

            // 进入 Gameplay
            // 注: 这里不订阅 LevelCompletedEventArgs —— 那是 ProcedureGameplay 的职责。
            // 旧版本在这里挂了一个空处理函数且永不退订, 属于无效订阅, 已移除。
            ChangeState<ProcedureGameplay>(procedureOwner);
        }
    }
}
