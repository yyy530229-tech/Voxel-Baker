using System;
using System.Collections.Generic;
using GameFramework;
using GameFramework.Event;

namespace VoxelGameFramework.Core
{
    /// <summary>
    /// 游戏事件总线门面 (GameFramework IEventManager 薄封装)
    ///
    /// 存在的意义:
    /// 1. 收敛散落各处的 GameFrameworkEntry.GetModule&lt;IEventManager&gt;() 样板代码,
    ///    业务侧只需一行 GameEventBus.Fire(...) / Subscribe(...)。
    /// 2. 统一处理"GameFramework 尚未启动"的降级路径 —— 直接丢弃而不是抛异常,
    ///    这样在没有 GameFrameworkEntryComponent 的测试场景里也能安全运行。
    /// 3. 缓存模块引用, 避免每帧事件的字典查找; 由 GameFrameworkEntryComponent 在
    ///    Shutdown 时调用 Invalidate() 失效缓存。
    ///
    /// 事件参数统一由 ReferencePool 分配, EventPool 分发后会自动归还, 无 GC 泄漏。
    /// 参见 ThirdParty/GameFramework/Base/EventPool/EventPool.cs:277
    /// </summary>
    public static class GameEventBus
    {
        private static IEventManager _cached;
        private static bool _warnedNotBootstrapped;

        // 时序兜底队列: 各 MonoBehaviour 的 Start() 顺序不保证, 订阅可能早于
        // GameFrameworkEntryComponent.Bootstrap (置 _initialized=true) 执行。此时 Resolve() 返回
        // null, 若直接丢弃会导致该订阅永远失效 (表现为"子弹不发射 / 消除无进度 / 无音效")。
        // 因此把这类订阅缓存起来, 等 Bootstrap 完成后由 FlushPending() 统一补订。
        private static readonly List<(int eventId, EventHandler<GameEventArgs> handler)> _pendingSubs
            = new List<(int, EventHandler<GameEventArgs>)>();

        /// <summary>
        /// 事件总线是否真正可用 (GameFramework 已启动且 Update 在驱动)。
        /// 不可用时 Fire 会被静默丢弃, 因此业务侧不应依赖返回值判断成败。
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                if (GameFrameworkEntryComponent.Instance == null ||
                    !GameFrameworkEntryComponent.Instance.IsInitialized)
                {
                    return false;
                }

                return Resolve() != null;
            }
        }

        /// <summary>
        /// 投递事件 (延迟到 GameFrameworkEntry.Update 里分发, 线程安全)。
        /// 绝大多数玩法事件都应该用这个。
        /// </summary>
        public static void Fire(object sender, GameEventArgs args)
        {
            IEventManager manager = Resolve();
            if (manager == null)
            {
                WarnOnce(sender, args);
                return;
            }

            manager.Fire(sender, args);
        }

        /// <summary>
        /// 立即分发事件 (不进队列)。
        /// 仅用于需要在同一次调用栈里拿到结果的场合, 例如状态切换前的收尾通知。
        /// 注意: 必须确保当前不在事件处理函数中, 否则会发生重入。
        /// </summary>
        public static void FireNow(object sender, GameEventArgs args)
        {
            IEventManager manager = Resolve();
            if (manager == null)
            {
                WarnOnce(sender, args);
                return;
            }

            manager.FireNow(sender, args);
        }

        /// <summary>
        /// 订阅事件。重复订阅同一 (id, handler) 组合是安全的 —— GF 的 AllowDuplicateHandler
        /// 校验会忽略重复项。
        /// </summary>
        public static void Subscribe(int eventId, EventHandler<GameEventArgs> handler)
        {
            // 缺组件 (测试场景等): 确实无法订阅, 丢弃并告警。
            if (GameFrameworkEntryComponent.Instance == null)
            {
                WarnOnce(null, null);
                return;
            }

            IEventManager manager = Resolve();
            if (manager == null)
            {
                // 组件存在但 GameFramework 尚未 Bootstrap 完 (Start 顺序竞态):
                // 缓存订阅, 待 Bootstrap 后 FlushPending() 一并补订, 绝不静默丢弃。
                _pendingSubs.Add((eventId, handler));
                return;
            }

            manager.Subscribe(eventId, handler);
        }

        /// <summary>
        /// 由 GameFrameworkEntryComponent.Bootstrap 在置 _initialized=true 之后调用,
        /// 把 Start 阶段因时序竞态缓存的订阅一次性补订上。
        /// </summary>
        internal static void FlushPending()
        {
            IEventManager manager = Resolve();
            if (manager == null) return;

            foreach (var (eventId, handler) in _pendingSubs)
            {
                manager.Subscribe(eventId, handler);
            }
            _pendingSubs.Clear();
        }

        /// <summary>
        /// 取消订阅。建议在 OnDisable / OnDestroy / Procedure.OnLeave 中成对调用。
        /// </summary>
        public static void Unsubscribe(int eventId, EventHandler<GameEventArgs> handler)
        {
            IEventManager manager = Resolve();
            if (manager == null) return;

            manager.Unsubscribe(eventId, handler);
        }

        /// <summary>
        /// 便捷订阅: 用事件参数类型自带的 EventId, 无需手写 typeof(T).GetHashCode()
        /// </summary>
        public static void Subscribe<T>(EventHandler<GameEventArgs> handler) where T : GameEventArgs
        {
            Subscribe(GetEventId<T>(), handler);
        }

        public static void Unsubscribe<T>(EventHandler<GameEventArgs> handler) where T : GameEventArgs
        {
            Unsubscribe(GetEventId<T>(), handler);
        }

        private static int GetEventId<T>() where T : GameEventArgs
        {
            return typeof(T).GetHashCode();
        }

        /// <summary>
        /// 失效模块缓存。由 GameFrameworkEntryComponent.OnDestroy 在
        /// GameFrameworkEntry.Shutdown() 之后调用, 防止持有已销毁的模块。
        /// </summary>
        internal static void Invalidate()
        {
            _cached = null;
            _warnedNotBootstrapped = false;
            _pendingSubs.Clear();
        }

        private static IEventManager Resolve()
        {
            if (_cached != null) return _cached;

            if (GameFrameworkEntryComponent.Instance == null ||
                !GameFrameworkEntryComponent.Instance.IsInitialized)
            {
                return null;
            }

            _cached = GameFrameworkEntry.GetModule<IEventManager>();
            return _cached;
        }

        private static void WarnOnce(object sender, GameEventArgs args)
        {
            if (_warnedNotBootstrapped) return;
            _warnedNotBootstrapped = true;

            string name = args != null ? args.GetType().Name : "unknown";
            UnityEngine.Debug.LogWarning(
                $"[GameEventBus] GameFramework 尚未启动, 事件 [{name}] 已被丢弃。" +
                "请确认场景中存在 GameFrameworkEntryComponent 且已完成 Bootstrap。");
        }
    }
}
