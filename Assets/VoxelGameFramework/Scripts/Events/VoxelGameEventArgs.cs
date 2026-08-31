using GameFramework;
using GameFramework.Event;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelGameFramework.Events
{
    /// <summary>
    /// 体素受击事件: (hitWorldPoint, damage, currentHP)
    /// </summary>
    public sealed class VoxelDamagedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(VoxelDamagedEventArgs).GetHashCode();
        public override int Id => EventId;

        public Vector3 HitWorldPoint { get; private set; }
        public int Damage { get; private set; }
        public short CurrentHP { get; private set; }

        public static VoxelDamagedEventArgs Create(Vector3 hitWorldPoint, int damage, short currentHP)
        {
            var args = ReferencePool.Acquire<VoxelDamagedEventArgs>();
            args.HitWorldPoint = hitWorldPoint;
            args.Damage = damage;
            args.CurrentHP = currentHP;
            return args;
        }

        public override void Clear()
        {
            HitWorldPoint = Vector3.zero;
            Damage = 0;
            CurrentHP = 0;
        }
    }

    /// <summary>
    /// 体素销毁事件: (destroyedPos, color, voxelLayer, remainingCount)
    /// </summary>
    public sealed class VoxelDestroyedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(VoxelDestroyedEventArgs).GetHashCode();
        public override int Id => EventId;

        public Vector3 DestroyedPos { get; private set; }
        public Color32 VoxelColor { get; private set; }
        public VoxelLayerType Layer { get; private set; }
        public int RemainingCount { get; private set; }

        public static VoxelDestroyedEventArgs Create(Vector3 pos, Color32 color, VoxelLayerType layer, int remaining)
        {
            var args = ReferencePool.Acquire<VoxelDestroyedEventArgs>();
            args.DestroyedPos = pos;
            args.VoxelColor = color;
            args.Layer = layer;
            args.RemainingCount = remaining;
            return args;
        }

        public override void Clear()
        {
            DestroyedPos = Vector3.zero;
            VoxelColor = default;
            Layer = VoxelLayerType.Empty;
            RemainingCount = 0;
        }
    }

    /// <summary>
    /// 关卡加载完成事件: (levelIndex, levelTitle, totalVoxels)
    /// </summary>
    public sealed class LevelLoadedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(LevelLoadedEventArgs).GetHashCode();
        public override int Id => EventId;

        public int LevelIndex { get; private set; }
        public string LevelTitle { get; private set; }
        public int TotalVoxels { get; private set; }

        public static LevelLoadedEventArgs Create(int levelIndex, string levelTitle, int totalVoxels)
        {
            var args = ReferencePool.Acquire<LevelLoadedEventArgs>();
            args.LevelIndex = levelIndex;
            args.LevelTitle = levelTitle;
            args.TotalVoxels = totalVoxels;
            return args;
        }

        public override void Clear()
        {
            LevelIndex = 0;
            LevelTitle = null;
            TotalVoxels = 0;
        }
    }

    /// <summary>
    /// 破坏进度变化事件: (progressRatio, activeVoxels, totalVoxels)
    /// </summary>
    public sealed class DestructionProgressEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(DestructionProgressEventArgs).GetHashCode();
        public override int Id => EventId;

        public float ProgressRatio { get; private set; }
        public int ActiveVoxels { get; private set; }
        public int TotalVoxels { get; private set; }

        public static DestructionProgressEventArgs Create(float ratio, int active, int total)
        {
            var args = ReferencePool.Acquire<DestructionProgressEventArgs>();
            args.ProgressRatio = ratio;
            args.ActiveVoxels = active;
            args.TotalVoxels = total;
            return args;
        }

        public override void Clear()
        {
            ProgressRatio = 0f;
            ActiveVoxels = 0;
            TotalVoxels = 0;
        }
    }

    /// <summary>
    /// 关卡完成事件: (levelIndex, totalDestroyed, rewardCoins)
    /// </summary>
    public sealed class LevelCompletedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(LevelCompletedEventArgs).GetHashCode();
        public override int Id => EventId;

        public int LevelIndex { get; private set; }
        public int TotalDestroyed { get; private set; }
        public int RewardCoins { get; private set; }

        public static LevelCompletedEventArgs Create(int levelIndex, int totalDestroyed, int rewardCoins)
        {
            var args = ReferencePool.Acquire<LevelCompletedEventArgs>();
            args.LevelIndex = levelIndex;
            args.TotalDestroyed = totalDestroyed;
            args.RewardCoins = rewardCoins;
            return args;
        }

        public override void Clear()
        {
            LevelIndex = 0;
            TotalDestroyed = 0;
            RewardCoins = 0;
        }
    }

    /// <summary>
    /// 金币获得事件: (amount, worldPos)
    /// </summary>
    public sealed class CoinEarnedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(CoinEarnedEventArgs).GetHashCode();
        public override int Id => EventId;

        public int Amount { get; private set; }
        public Vector3 WorldPos { get; private set; }

        public static CoinEarnedEventArgs Create(int amount, Vector3 worldPos)
        {
            var args = ReferencePool.Acquire<CoinEarnedEventArgs>();
            args.Amount = amount;
            args.WorldPos = worldPos;
            return args;
        }

        public override void Clear()
        {
            Amount = 0;
            WorldPos = Vector3.zero;
        }
    }

    /// <summary>
    /// 槽位填充事件: (slotIndex, blockColor)
    /// </summary>
    public sealed class SlotFilledEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(SlotFilledEventArgs).GetHashCode();
        public override int Id => EventId;

        public int SlotIndex { get; private set; }
        public Color32 BlockColor { get; private set; }

        public static SlotFilledEventArgs Create(int slotIndex, Color32 blockColor)
        {
            var args = ReferencePool.Acquire<SlotFilledEventArgs>();
            args.SlotIndex = slotIndex;
            args.BlockColor = blockColor;
            return args;
        }

        public override void Clear()
        {
            SlotIndex = 0;
            BlockColor = default;
        }
    }

    /// <summary>
    /// 槽位释放事件: (slotIndex)
    /// </summary>
    public sealed class SlotEmptiedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(SlotEmptiedEventArgs).GetHashCode();
        public override int Id => EventId;

        public int SlotIndex { get; private set; }

        public static SlotEmptiedEventArgs Create(int slotIndex)
        {
            var args = ReferencePool.Acquire<SlotEmptiedEventArgs>();
            args.SlotIndex = slotIndex;
            return args;
        }

        public override void Clear()
        {
            SlotIndex = 0;
        }
    }

    /// <summary>
    /// 队列方块部署事件: (columnIndex, blockColor, remainingAmmo)
    /// </summary>
    public sealed class QueueBlockDeployedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(QueueBlockDeployedEventArgs).GetHashCode();
        public override int Id => EventId;

        public int ColumnIndex { get; private set; }
        public Color32 BlockColor { get; private set; }
        public int RemainingAmmo { get; private set; }

        public static QueueBlockDeployedEventArgs Create(int columnIndex, Color32 blockColor, int remainingAmmo)
        {
            var args = ReferencePool.Acquire<QueueBlockDeployedEventArgs>();
            args.ColumnIndex = columnIndex;
            args.BlockColor = blockColor;
            args.RemainingAmmo = remainingAmmo;
            return args;
        }

        public override void Clear()
        {
            ColumnIndex = 0;
            BlockColor = default;
            RemainingAmmo = 0;
        }
    }

    /// <summary>
    /// 炮台升级/合并事件: (cannonIndex, newPower)
    /// </summary>
    public sealed class CannonUpgradedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(CannonUpgradedEventArgs).GetHashCode();
        public override int Id => EventId;

        public int CannonIndex { get; private set; }
        public int NewPower { get; private set; }

        public static CannonUpgradedEventArgs Create(int cannonIndex, int newPower)
        {
            var args = ReferencePool.Acquire<CannonUpgradedEventArgs>();
            args.CannonIndex = cannonIndex;
            args.NewPower = newPower;
            return args;
        }

        public override void Clear()
        {
            CannonIndex = 0;
            NewPower = 0;
        }
    }

    /// <summary>
    /// 请求进入下一关 (由 UI 按钮发出, 由 ProcedureLevelComplete 消费)
    /// 这样 UI 不再直接调用 VoxelLevelManager.NextLevel(), 关卡推进的唯一权威是流程状态机。
    /// </summary>
    public sealed class NextLevelRequestedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(NextLevelRequestedEventArgs).GetHashCode();
        public override int Id => EventId;

        public static NextLevelRequestedEventArgs Create()
        {
            return ReferencePool.Acquire<NextLevelRequestedEventArgs>();
        }

        public override void Clear() { }
    }

    /// <summary>
    /// 请求重开当前关卡 (由 UI 按钮发出, 由 ProcedureGameplay 消费)
    /// </summary>
    public sealed class RestartLevelRequestedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(RestartLevelRequestedEventArgs).GetHashCode();
        public override int Id => EventId;

        public static RestartLevelRequestedEventArgs Create()
        {
            return ReferencePool.Acquire<RestartLevelRequestedEventArgs>();
        }

        public override void Clear() { }
    }
}
