using GameFramework;
using GameFramework.Event;
using UnityEngine;
using VoxelBaker.Runtime;
using VoxelGameFramework.Audio;

namespace VoxelGameFramework.Events
{
    /// <summary>
    /// 命令事件: 播放音效。调用方只发事件、不持有服务引用,
    /// 由 VoxelSoundManager 订阅并在自身线程安全的上下文里执行。
    /// </summary>
    public sealed class SfxPlayedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(SfxPlayedEventArgs).GetHashCode();
        public override int Id => EventId;

        public VoxelSoundManager.SfxType Type { get; private set; }
        public float VolumeScale { get; private set; }

        public static SfxPlayedEventArgs Create(VoxelSoundManager.SfxType type, float volumeScale = 1f)
        {
            var args = ReferencePool.Acquire<SfxPlayedEventArgs>();
            args.Type = type;
            args.VolumeScale = volumeScale;
            return args;
        }

        public override void Clear()
        {
            Type = default;
            VolumeScale = 1f;
        }
    }

    /// <summary>
    /// 命令事件: 发射能量弹。Spawn 与 Despawn 共用本事件, 以 RequestType 区分,
    /// 由 VoxelBulletPool 订阅执行。
    /// </summary>
    public sealed class BulletFiredEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(BulletFiredEventArgs).GetHashCode();
        public override int Id => EventId;

        public enum RequestType
        {
            Spawn,
            Despawn
        }

        public RequestType Kind { get; private set; }
        public Vector3 SpawnPos { get; private set; }
        public Vector3Int TargetGridPos { get; private set; }
        public Color32 Color { get; private set; }
        public float Speed { get; private set; }
        public VoxelModelInstance Model { get; private set; }
        public GameObject BulletObject { get; private set; }

        public static BulletFiredEventArgs CreateSpawn(
            Vector3 spawnPos, Vector3Int targetGridPos, Color32 color, float speed, VoxelModelInstance model)
        {
            var args = ReferencePool.Acquire<BulletFiredEventArgs>();
            args.Kind = RequestType.Spawn;
            args.SpawnPos = spawnPos;
            args.TargetGridPos = targetGridPos;
            args.Color = color;
            args.Speed = speed;
            args.Model = model;
            args.BulletObject = null;
            return args;
        }

        public static BulletFiredEventArgs CreateDespawn(GameObject bulletObject)
        {
            var args = ReferencePool.Acquire<BulletFiredEventArgs>();
            args.Kind = RequestType.Despawn;
            args.BulletObject = bulletObject;
            return args;
        }

        public override void Clear()
        {
            Kind = RequestType.Spawn;
            SpawnPos = Vector3.zero;
            TargetGridPos = Vector3Int.zero;
            Color = new Color32(0, 0, 0, 0);
            Speed = 0f;
            Model = null;
            BulletObject = null;
        }
    }

    /// <summary>
    /// 命令事件: 生成碎片爆裂效果。
    /// </summary>
    public sealed class DebrisSpawnedEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(DebrisSpawnedEventArgs).GetHashCode();
        public override int Id => EventId;

        public Vector3 Position { get; private set; }
        public Vector3 Direction { get; private set; }
        public Color32 Color { get; private set; }
        public float Size { get; private set; }
        public int Count { get; private set; }

        public static DebrisSpawnedEventArgs Create(Vector3 position, Vector3 direction, Color32 color, float size, int count)
        {
            var args = ReferencePool.Acquire<DebrisSpawnedEventArgs>();
            args.Position = position;
            args.Direction = direction;
            args.Color = color;
            args.Size = size;
            args.Count = count;
            return args;
        }

        public override void Clear()
        {
            Position = Vector3.zero;
            Direction = Vector3.zero;
            Color = new Color32(0, 0, 0, 0);
            Size = 0f;
            Count = 0;
        }
    }
}
