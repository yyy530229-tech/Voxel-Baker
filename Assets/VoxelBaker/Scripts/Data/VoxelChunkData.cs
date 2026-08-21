using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelBaker.Data
{
    /// <summary>
    /// 单个分块烘焙数据
    /// </summary>
    [Serializable]
    public class VoxelChunkData
    {
        public int chunkId;
        public Vector3Int chunkCoord;     // 分块网格坐标 (例如 0,0,0, 1,0,0)
        public Vector3Int minGridPos;      // 在整体网格中的起始体素坐标
        public int chunkSize;              // 分块尺寸（如 16 或 32）
        public Bounds localBounds;         // 分块局部包围盒

        // 稀疏存储的体素单元（烘焙期完整数据）
        public VoxelCell[] cells;
        
        // 初始可见体素列表（进入GPU渲染队列）
        public List<int> initialVisibleCellIndices = new List<int>();

        public int OccupiedCount => cells != null ? cells.Length : 0;
        public int VisibleCount => initialVisibleCellIndices != null ? initialVisibleCellIndices.Count : 0;
    }

    /// <summary>
    /// LOD 级别数据
    /// </summary>
    [Serializable]
    public class VoxelLODData
    {
        public int lodLevel;
        public float voxelSize;
        public Vector3Int dimensions;
        public PackedVoxelGPU[] visibleVoxels;
    }
}
