using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VoxelBaker.Runtime.Rendering
{
    /// <summary>
    /// URP RendererFeature: 把所有注册进 VoxelIndirectRenderRegistry 的体素模型
    /// 在 BeforeRenderingOpaques 阶段通过 CommandBuffer 提交 GPU Indirect 绘制。
    ///
    /// 根治摩尔纹的关键:
    /// 之前体素在 Update() 里用 Graphics.DrawMeshInstancedIndirect 提交, DrawCall 完全
    /// 绕过 URP 管线 → RenderScale / MSAA 对体素无效 → 远处体素网格周期与屏幕像素
    /// 干涉, 旋转时产生摩尔条纹。走本 Feature 后绘制进入管线内的相机中间 RT
    /// (MSAA 8x + RenderScale 2.0 生效), 摩尔纹被硬件抗锯齿打散。
    ///
    /// 挂载方法 (一次性, 手动):
    ///   1. Project 窗口选中 Assets/Settings/URP-HighFidelity-Renderer.asset
    ///   2. Inspector → Add Renderer Feature → Voxel Indirect Render Feature
    ///   3. 若 Quality 分档使用了多张 URP Renderer 资产, 每张都要挂同一个 Feature
    /// </summary>
    public class VoxelIndirectRenderFeature : ScriptableRendererFeature
    {
        private VoxelIndirectRenderPass _pass;

        public override void Create()
        {
            _pass = new VoxelIndirectRenderPass
            {
                // 在不透明物体之前画, 体素正确参与深度测试, 与场景几何互相遮挡。
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camType = renderingData.cameraData.cameraType;

            // 只画 Game / SceneView 相机:
            // 排除 Preview (VoxelPreviewPanel 用 PreviewRenderUtility 自行渲染)、
            // Reflection 等相机, 避免重复提交与预览窗口污染。
            if (camType != CameraType.Game && camType != CameraType.SceneView) return;

            // 场景里没有体素模型时直接跳过, 零开销。
            if (VoxelIndirectRenderRegistry.Instances.Count == 0) return;

            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass = null;
        }

        private class VoxelIndirectRenderPass : ScriptableRenderPass
        {
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var instances = VoxelIndirectRenderRegistry.Instances;
                if (instances.Count == 0) return;

                CommandBuffer cmd = CommandBufferPool.Get("VoxelIndirectRender");

                for (int i = 0; i < instances.Count; i++)
                {
                    var inst = instances[i];
                    if (inst == null) continue;

                    var voxelRenderer = inst.Renderer;
                    if (voxelRenderer == null) continue;

                    voxelRenderer.RenderToCommandBuffer(cmd);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
