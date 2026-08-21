using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelBaker.Data
{
    [CreateAssetMenu(fileName = "NewVoxelAsset", menuName = "Voxel Baker/Voxel Asset")]
    public class VoxelAsset : ScriptableObject
    {
        [Header("Header & Metadata")]
        public string version = "1.0.0";
        public string sourceModelName = "";
        public Vector3 boundsCenter;
        public Vector3 boundsSize;
        public Vector3Int gridDimensions;
        public float voxelSize = 0.1f;
        public Vector3 localOrigin;     // Grid (0,0,0) in local model space
        public int chunkSize = 16;

        [Header("Appearance & Palette")]
        public VoxelPalette palette;
        public Texture2D paletteTexture;

        [Header("Chunks & Geometry Data")]
        public List<VoxelChunkData> chunks = new List<VoxelChunkData>();

        [Header("Initial GPU Render Set (LOD0)")]
        public PackedVoxelGPU[] initialVisibleVoxels;

        [Header("LOD Hierarchies")]
        public List<VoxelLODData> lods = new List<VoxelLODData>();

        [Header("Bake Statistics")]
        public int totalOccupiedVoxels;
        public int totalSurfaceVoxels;
        public int totalInteriorVoxels;
        public int totalVisibleVoxels;
        public float bakeDurationSeconds;

        [Header("Interior Profile Config Used")]
        public VoxelInteriorProfile interiorProfile;

        public Vector3 GridToLocalPosition(Vector3Int gridPos)
        {
            return localOrigin + new Vector3(
                (gridPos.x + 0.5f) * voxelSize,
                (gridPos.y + 0.5f) * voxelSize,
                (gridPos.z + 0.5f) * voxelSize
            );
        }

        public Vector3Int LocalToGridPosition(Vector3 localPos)
        {
            Vector3 diff = localPos - localOrigin;
            int x = Mathf.FloorToInt(diff.x / voxelSize);
            int y = Mathf.FloorToInt(diff.y / voxelSize);
            int z = Mathf.FloorToInt(diff.z / voxelSize);
            return new Vector3Int(x, y, z);
        }

        public bool IsInBounds(Vector3Int gridPos)
        {
            return gridPos.x >= 0 && gridPos.x < gridDimensions.x &&
                   gridPos.y >= 0 && gridPos.y < gridDimensions.y &&
                   gridPos.z >= 0 && gridPos.z < gridDimensions.z;
        }
    }
}
