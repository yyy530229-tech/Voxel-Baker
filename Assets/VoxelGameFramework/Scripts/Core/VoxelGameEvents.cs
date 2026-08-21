using System;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelGameFramework.Core
{
    /// <summary>
    /// 解耦的全局游戏事件总线 (Gameplay Event Bus)
    /// 游戏玩法逻辑与烘焙工具彻底解耦，便于接入任何游戏系统（任务、成就、掉落、音效、UI）
    /// </summary>
    public static class VoxelGameEvents
    {
        // 体素受击事件: (hitWorldPoint, damage, currentHP)
        public static Action<Vector3, int, short> OnVoxelDamaged;

        // 体素粉碎销毁事件: (destroyedPos, color, voxelLayer, remainingCount)
        public static Action<Vector3, Color32, VoxelLayerType, int> OnVoxelDestroyed;

        // 内部新层级暴露事件: (exposedPos, newLayerType)
        public static Action<Vector3, VoxelLayerType> OnLayerExposed;

        // 关卡破坏进度变化: (destroyedPercent 0.0~1.0, activeVoxels, totalVoxels)
        public static Action<float, int, int> OnDestructionProgressChanged;

        // 关卡目标达成/通关事件: (levelIndex, totalDestroyed)
        public static Action<int, int> OnLevelCompleted;

        // 金币/分数获得事件: (amount, worldPos)
        public static Action<int, Vector3> OnCoinEarned;

        // 炮台升级/合并事件: (cannonIndex, newPower)
        public static Action<int, int> OnCannonUpgraded;
    }
}
