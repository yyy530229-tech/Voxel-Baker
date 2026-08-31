using GameFramework;
using GameFramework.Event;
using GameFramework.Procedure;
using UnityEngine;
using VoxelGameFramework.Core;
using VoxelGameFramework.Events;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;

namespace VoxelGameFramework.Procedures
{
    /// <summary>
    /// 主菜单流程 (预留: 关卡选择界面)
    /// 当前直接跳过进入 Gameplay
    /// </summary>
    public class ProcedureMainMenu : ProcedureBase
    {
        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Debug.Log("[ProcedureMainMenu] Enter");

            // TODO: 打开主菜单 UIForm
            // 占位: 1 秒后自动进入 Gameplay
            _elapsed = 0f;
        }

        private float _elapsed = 0f;

        protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);
            _elapsed += elapseSeconds;
            if (_elapsed >= 0.5f)
            {
                ChangeState<ProcedureGameplay>(procedureOwner);
            }
        }
    }
}
