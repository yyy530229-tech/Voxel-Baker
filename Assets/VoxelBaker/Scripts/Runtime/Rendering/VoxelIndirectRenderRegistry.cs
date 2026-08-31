using System.Collections.Generic;

namespace VoxelBaker.Runtime.Rendering
{
    /// <summary>
    /// 体素间接绘制静态注册表。
    /// 所有活跃的 VoxelModelInstance 在 OnEnable 时把自己注册进来,
    /// 由挂载在 URP Renderer 资产上的 VoxelIndirectRenderFeature 在管线内每帧取走绘制。
    ///
    /// 为什么需要它:
    /// 体素 DrawCall 之前在 Update() 里用 Graphics.DrawMeshInstancedIndirect 直接提交,
    /// 完全绕过 URP 管线 → RenderScale / MSAA 对体素无效 → 远处体素周期与屏幕像素
    /// 干涉产生摩尔纹 (Scene/Game 视图现象一致, 因为 [ExecuteAlways] 让编辑态走同一路径)。
    /// 改为注册表 + RendererFeature 后, 绘制发生在 BeforeRenderingOpaques,
    /// 落在相机的 MSAA + RenderScale 中间 RT 上, 摩尔纹被硬件抗锯齿打散。
    ///
    /// 注意: 本注册表不负责绘制, 也不依赖任何 Unity 对象生命周期;
    /// 域重载时静态列表自动清空, 各实例 OnEnable 会重新注册。
    /// </summary>
    public static class VoxelIndirectRenderRegistry
    {
        private static readonly List<VoxelModelInstance> _instances = new List<VoxelModelInstance>();

        /// <summary>当前注册的所有体素模型 (只读视图)。</summary>
        public static IReadOnlyList<VoxelModelInstance> Instances => _instances;

        public static void Register(VoxelModelInstance instance)
        {
            if (instance == null) return;
            if (!_instances.Contains(instance))
                _instances.Add(instance);
        }

        public static void Unregister(VoxelModelInstance instance)
        {
            if (instance == null) return;
            _instances.Remove(instance);
        }
    }
}
