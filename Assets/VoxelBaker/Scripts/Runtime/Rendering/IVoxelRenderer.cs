using System;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelBaker.Data;

namespace VoxelBaker.Runtime.Rendering
{
    public interface IVoxelRenderer : IDisposable
    {
        void Initialize(VoxelAsset asset, Material voxelMaterial, Transform rootTransform);
        void UpdateVisibleInstances(PackedVoxelGPU[] activeInstances, int count);
        /// <summary>
        /// 把间接绘制写入 URP RendererFeature 提供的 CommandBuffer。
        /// 关键: CommandBuffer.DrawMeshInstancedIndirect 只有 7 参数重载
        /// (mesh, submeshIndex, material, shaderPass, argsBuffer, argsOffset, properties),
        /// 没有 Graphics 版的 bounds/shadow 参数 —— 绘制会随 Pass 进入带 MSAA + RenderScale 的相机 RT。
        /// </summary>
        void RenderToCommandBuffer(CommandBuffer cmd);
        void Release();
    }
}
