using System;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Runtime.Rendering
{
    public interface IVoxelRenderer : IDisposable
    {
        void Initialize(VoxelAsset asset, Material voxelMaterial, Transform rootTransform);
        void UpdateVisibleInstances(PackedVoxelGPU[] activeInstances, int count);
        void Render();
        void Release();
    }
}
