using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelGameFramework.Core
{
    /// <summary>
    /// 轻量服务定位器 (GameFramework 中心化架构的服务容器)
    ///
    /// 存在意义:
    ///   业务管理器 (VoxelSoundManager / VoxelBulletPool / VoxelLevelManager 等)
    ///   不再暴露 static Instance 全局单例, 而是由 GameFrameworkEntryComponent.Bootstrap() 在启动时
    ///   统一注册进本容器。需要服务的调用方通过 ServiceLocator.Get&lt;T&gt;() 获取, 彻底消灭
    ///   "全局静态单例 + FindObjectOfType 全场景扫描" 两种反模式。
    ///
    /// 时序兜底:
    ///   场景里 MonoBehaviour 的 Awake/Start 可能早于 GameFrameworkEntryComponent.Bootstrap() (Start 里跑)。
    ///   直接在 Get&lt;T&gt;() 返回 null 会让首批调用失败。为此 Get&lt;T&gt;() 在注册表为空时回退到
    ///   FindObjectOfType&lt;T&gt;() 兜底 —— 仅首次、仅编辑器/场景已存在实例时生效, 命中后写入缓存,
    ///   后续走缓存不再扫描。这样既不用改 executionOrder, 也不会每帧全场景扫描。
    ///
    /// 跨程序集:
    ///   本类放在 VoxelGameFramework.Core, VoxelBaker 运行时层已能引用 VoxelGameFramework,
    ///   所以 VoxelDebrisManager (位于 VoxelBaker/Scripts/Runtime) 也能注册/获取。
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        /// <summary>
        /// 注册服务实例。重复注册同类型会覆盖 (后注册者生效)。
        /// 由 GameFrameworkEntryComponent.Bootstrap() 在启动早期按顺序调用。
        /// </summary>
        public static void Register<T>(T service) where T : UnityEngine.Object
        {
            if (service == null) return;
            _services[typeof(T)] = service;
        }

        /// <summary>
        /// 获取已注册的服务。未注册时回退 FindObjectOfType 兜底 (仅首次), 仍找不到返回 null。
        /// 调用方应按 "为 null 则跳过" 的防御式写法处理降级。
        /// </summary>
        public static T Get<T>() where T : UnityEngine.Object
        {
            Type type = typeof(T);

            if (_services.TryGetValue(type, out var cached) && cached != null)
            {
                return (T)cached;
            }

            // 时序兜底: 注册表尚未填充 (Bootstrap 晚于本调用), 尝试从场景里找已存在的实例
            T fallback = UnityEngine.Object.FindObjectOfType<T>();
            if (fallback != null)
            {
                _services[type] = fallback; // 写入缓存, 避免后续重复扫描
                return fallback;
            }

            return null;
        }

        /// <summary>
        /// 获取服务并附带是否成功标志, 便于调用方区分 "未就绪" 与 "已就绪为 null"。
        /// </summary>
        public static bool TryGet<T>(out T service) where T : UnityEngine.Object
        {
            service = Get<T>();
            return service != null;
        }

        /// <summary>
        /// 是否已有注册实例 (不触发 FindObjectOfType 兜底扫描)。
        /// </summary>
        public static bool IsRegistered<T>() where T : UnityEngine.Object
        {
            return _services.ContainsKey(typeof(T)) && _services[typeof(T)] != null;
        }

        /// <summary>
        /// 清空所有注册。由 GameFrameworkEntryComponent.OnDestroy / 场景卸载时调用,
        /// 避免跨场景残留旧实例引用。
        /// </summary>
        public static void Reset()
        {
            _services.Clear();
        }
    }
}
