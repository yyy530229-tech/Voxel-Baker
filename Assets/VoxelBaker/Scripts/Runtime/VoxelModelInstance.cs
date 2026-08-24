using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Data;
using VoxelBaker.Runtime.Rendering;

namespace VoxelBaker.Runtime
{
    [ExecuteAlways]
    public class VoxelModelInstance : MonoBehaviour
    {
        [Header("Asset Reference")]
        public VoxelAsset voxelAsset;
        public Material voxelMaterial;

        [Header("Runtime Status")]
        [SerializeField] private int activeVoxelCount = 0;
        [SerializeField] private int destroyedVoxelCount = 0;

        private IVoxelRenderer _renderer;
        private VoxelCell[,,] _runtimeGrid;
        private int[,,] _gpuInstanceIndexMap; // 映射 (x,y,z) 到 _activeGPUList 中的下标 (-1 表示未渲染)
        private List<PackedVoxelGPU> _activeGPUList = new List<PackedVoxelGPU>();
        private bool _isDirty = false;
        private bool _isInitialized = false;

        public VoxelAsset Asset => voxelAsset;
        public int ActiveVoxelCount => activeVoxelCount;
        public int DestroyedVoxelCount => destroyedVoxelCount;

        private static readonly Vector3Int[] Directions6 = new Vector3Int[]
        {
            new Vector3Int( 1,  0,  0),
            new Vector3Int(-1,  0,  0),
            new Vector3Int( 0,  1,  0),
            new Vector3Int( 0, -1,  0),
            new Vector3Int( 0,  0,  1),
            new Vector3Int( 0,  0, -1)
        };

        private void OnEnable()
        {
            InitializeModel();
        }

        private void OnDisable()
        {
            ReleaseRenderer();
        }

        public void InitializeModel()
        {
            ReleaseRenderer();

            if (voxelAsset == null) return;

            if (voxelMaterial == null)
            {
                Shader s = Shader.Find("VoxelBaker/URP/VoxelLit");
                if (s != null)
                {
                    voxelMaterial = new Material(s);
                }
            }

            int gx = voxelAsset.gridDimensions.x;
            int gy = voxelAsset.gridDimensions.y;
            int gz = voxelAsset.gridDimensions.z;

            _runtimeGrid = new VoxelCell[gx, gy, gz];
            _gpuInstanceIndexMap = new int[gx, gy, gz];

            for (int x = 0; x < gx; x++)
            {
                for (int y = 0; y < gy; y++)
                {
                    for (int z = 0; z < gz; z++)
                    {
                        _gpuInstanceIndexMap[x, y, z] = -1;
                    }
                }
            }

            // 从 Chunk 还原完整网格状态
            activeVoxelCount = 0;
            destroyedVoxelCount = 0;

            if (voxelAsset.chunks != null)
            {
                foreach (var chunk in voxelAsset.chunks)
                {
                    if (chunk.cells == null) continue;
                    foreach (var cell in chunk.cells)
                    {
                        _runtimeGrid[cell.gridPos.x, cell.gridPos.y, cell.gridPos.z] = cell;
                        activeVoxelCount++;
                    }
                }
            }

            // 加载初始可见集合
            _activeGPUList.Clear();
            if (voxelAsset.initialVisibleVoxels != null)
            {
                for (int i = 0; i < voxelAsset.initialVisibleVoxels.Length; i++)
                {
                    PackedVoxelGPU v = voxelAsset.initialVisibleVoxels[i];
                    Vector3Int pos = PackedVoxelGPU.UnpackPosition(v.packedPosition);
                    _gpuInstanceIndexMap[pos.x, pos.y, pos.z] = _activeGPUList.Count;
                    _activeGPUList.Add(v);
                }
            }

            _renderer = new VoxelIndirectRenderer();
            _renderer.Initialize(voxelAsset, voxelMaterial, transform);
            _renderer.UpdateVisibleInstances(_activeGPUList.ToArray(), _activeGPUList.Count);

            _isInitialized = true;
        }

        public bool Raycast(Ray worldRay, out VoxelRaycastHit hitResult, float maxDistance = 100f)
        {
            return VoxelDDA.Raycast(worldRay, transform, voxelAsset, IsVoxelAlive, out hitResult, maxDistance);
        }

        public bool IsVoxelAlive(Vector3Int gridPos)
        {
            if (_runtimeGrid == null || voxelAsset == null || !voxelAsset.IsInBounds(gridPos))
                return false;
            return _runtimeGrid[gridPos.x, gridPos.y, gridPos.z].isOccupied &&
                   _runtimeGrid[gridPos.x, gridPos.y, gridPos.z].isAlive;
        }

        public VoxelCell GetCell(Vector3Int gridPos)
        {
            if (_runtimeGrid == null || voxelAsset == null || !voxelAsset.IsInBounds(gridPos))
                return VoxelCell.Empty;
            return _runtimeGrid[gridPos.x, gridPos.y, gridPos.z];
        }

        private readonly List<int> _tempMatchingIndices = new List<int>(128);
        private readonly List<int> _tempFrontIndices = new List<int>(64);
        private readonly HashSet<Vector3Int> _targetedVoxels = new HashSet<Vector3Int>();

        public void ReserveTargetVoxel(Vector3Int gridPos)
        {
            _targetedVoxels.Add(gridPos);
        }

        public void ReleaseTargetVoxel(Vector3Int gridPos)
        {
            _targetedVoxels.Remove(gridPos);
        }

        /// <summary>
        /// 查找并独占锁定最外层表面同色体素 (1:1 绝对守恒，绝不允许多发子弹重打同一格已毁体素)
        /// </summary>
        public bool FindAndReserveExposedVoxel(Color32 targetColor, Vector3 fromWorldPos, out Vector3Int hitGridPos, out Vector3 hitWorldPos)
        {
            hitGridPos = Vector3Int.zero;
            hitWorldPos = Vector3.zero;

            if (_activeGPUList == null || _activeGPUList.Count == 0 || voxelAsset == null)
                return false;

            _tempMatchingIndices.Clear();
            _tempFrontIndices.Clear();

            Vector3 center = transform.position;

            for (int i = 0; i < _activeGPUList.Count; i++)
            {
                PackedVoxelGPU gpuVoxel = _activeGPUList[i];
                Vector3Int pos = PackedVoxelGPU.UnpackPosition(gpuVoxel.packedPosition);

                // 关键点：已被其它在途子弹锁定的体素跳过，杜绝多弹打一格造成的虚耗弹药！
                if (_targetedVoxels.Contains(pos)) continue;

                Color32 vColor = PackedVoxelGPU.UIntToColor(gpuVoxel.colorRGBA);

                // 严格颜色匹配判定 (RGB 距离容差)
                float dr = vColor.r - targetColor.r;
                float dg = vColor.g - targetColor.g;
                float db = vColor.b - targetColor.b;
                float dist = Mathf.Sqrt(dr * dr + dg * dg + db * db);

                if (dist <= 80f)
                {
                    _tempMatchingIndices.Add(i);

                    Vector3 localPos = voxelAsset.GridToLocalPosition(pos);
                    Vector3 wPos = transform.TransformPoint(localPos);

                    if (wPos.z <= center.z + 0.15f)
                    {
                        _tempFrontIndices.Add(i);
                    }
                }
            }

            if (_tempMatchingIndices.Count == 0) return false;

            // 剥皮式层序消除核心算法 (Peeling Layer-by-Layer)：
            // 挑选当前最外层、最凸出的“表皮层”同色体素进行消解！
            int bestIdx = -1;
            float maxOutwardness = -float.MaxValue;

            var candidates = (_tempFrontIndices.Count > 0) ? _tempFrontIndices : _tempMatchingIndices;

            for (int k = 0; k < candidates.Count; k++)
            {
                int idx = candidates[k];
                PackedVoxelGPU gpuVoxel = _activeGPUList[idx];
                Vector3Int pos = PackedVoxelGPU.UnpackPosition(gpuVoxel.packedPosition);
                Vector3 localPos = voxelAsset.GridToLocalPosition(pos);
                Vector3 wPos = transform.TransformPoint(localPos);

                float distFromCenter = (wPos - center).sqrMagnitude;
                float frontBias = (center.z - wPos.z) * 3.0f;
                float outwardness = distFromCenter + frontBias;

                if (outwardness > maxOutwardness)
                {
                    maxOutwardness = outwardness;
                    bestIdx = idx;
                }
            }

            if (bestIdx == -1) bestIdx = candidates[0];

            PackedVoxelGPU chosenGpu = _activeGPUList[bestIdx];

            hitGridPos = PackedVoxelGPU.UnpackPosition(chosenGpu.packedPosition);
            Vector3 finalLocal = voxelAsset.GridToLocalPosition(hitGridPos);
            hitWorldPos = transform.TransformPoint(finalLocal);

            // 独占锁定此体素，直到这发子弹命中或销毁
            ReserveTargetVoxel(hitGridPos);
            return true;
        }

        public bool FindExposedVoxelOfColor(Color32 targetColor, Vector3 fromWorldPos, out Vector3Int hitGridPos, out Vector3 hitWorldPos)
        {
            return FindAndReserveExposedVoxel(targetColor, fromWorldPos, out hitGridPos, out hitWorldPos);
        }

        public bool ApplyColorDamage(Vector3Int gridPos, int damageAmount, Color32 attackerColor, Vector3 hitWorldPoint, Vector3 hitWorldNormal)
        {
            if (!IsVoxelAlive(gridPos)) return false;

            VoxelCell cell = _runtimeGrid[gridPos.x, gridPos.y, gridPos.z];

            // 检查颜色是否匹配
            float dr = cell.customColor.r - attackerColor.r;
            float dg = cell.customColor.g - attackerColor.g;
            float db = cell.customColor.b - attackerColor.b;
            float dist = Mathf.Sqrt(dr * dr + dg * dg + db * db);

            if (dist > 80f)
            {
                // 颜色不匹配，无法消除！
                return false;
            }

            ApplyDamage(gridPos, damageAmount, hitWorldPoint, hitWorldNormal);
            return true;
        }

        public void ApplyDamage(Vector3Int gridPos, int damageAmount, Vector3 hitWorldPoint, Vector3 hitWorldNormal)
        {
            if (!IsVoxelAlive(gridPos)) return;

            VoxelCell cell = _runtimeGrid[gridPos.x, gridPos.y, gridPos.z];
            cell.currentHP -= (short)damageAmount;

            if (cell.currentHP <= 0)
            {
                // 体素被彻底破坏
                cell.isAlive = false;
                cell.isOccupied = false;
                _runtimeGrid[gridPos.x, gridPos.y, gridPos.z] = cell;

                activeVoxelCount--;
                destroyedVoxelCount++;

                // 产生高精度物理碎片特效 (精确从被消除体素的实际中心爆发)
                if (VoxelDebrisManager.Instance != null)
                {
                    Vector3 exactVoxelWorldCenter = transform.TransformPoint(voxelAsset.GridToLocalPosition(gridPos));
                    VoxelDebrisManager.Instance.SpawnDebris(
                        exactVoxelWorldCenter,
                        hitWorldNormal,
                        cell.customColor,
                        voxelAsset.voxelSize,
                        20
                    );
                }

                // 从 GPU 可见列表中移除自身
                RemoveVoxelFromGPU(gridPos);

                // 核心机制：检测 6 个相邻体素，将新暴露出来的内部体素动态加入 GPU 渲染集合！
                for (int d = 0; d < 6; d++)
                {
                    Vector3Int n = gridPos + Directions6[d];
                    if (voxelAsset.IsInBounds(n) && _runtimeGrid[n.x, n.y, n.z].isOccupied && _runtimeGrid[n.x, n.y, n.z].isAlive)
                    {
                        UpdateVoxelExposure(n);
                    }
                }

                _isDirty = true;
            }
            else
            {
                _runtimeGrid[gridPos.x, gridPos.y, gridPos.z] = cell;
            }
        }

        private void RemoveVoxelFromGPU(Vector3Int gridPos)
        {
            int index = _gpuInstanceIndexMap[gridPos.x, gridPos.y, gridPos.z];
            if (index < 0 || index >= _activeGPUList.Count) return;

            int lastIndex = _activeGPUList.Count - 1;
            if (index != lastIndex)
            {
                // 用末尾元素填充被移除的位置 (O(1) 紧凑移除)
                PackedVoxelGPU lastVoxel = _activeGPUList[lastIndex];
                Vector3Int lastPos = PackedVoxelGPU.UnpackPosition(lastVoxel.packedPosition);
                _activeGPUList[index] = lastVoxel;
                _gpuInstanceIndexMap[lastPos.x, lastPos.y, lastPos.z] = index;
            }

            _activeGPUList.RemoveAt(lastIndex);
            _gpuInstanceIndexMap[gridPos.x, gridPos.y, gridPos.z] = -1;
        }

        private void UpdateVoxelExposure(Vector3Int gridPos)
        {
            VoxelCell neighbor = _runtimeGrid[gridPos.x, gridPos.y, gridPos.z];
            VoxelFaceMask mask = VoxelFaceMask.None;

            int gx = voxelAsset.gridDimensions.x;
            int gy = voxelAsset.gridDimensions.y;
            int gz = voxelAsset.gridDimensions.z;

            if (gridPos.x == gx - 1 || !_runtimeGrid[gridPos.x + 1, gridPos.y, gridPos.z].isOccupied) mask |= VoxelFaceMask.PosX;
            if (gridPos.x == 0 || !_runtimeGrid[gridPos.x - 1, gridPos.y, gridPos.z].isOccupied) mask |= VoxelFaceMask.NegX;
            if (gridPos.y == gy - 1 || !_runtimeGrid[gridPos.x, gridPos.y + 1, gridPos.z].isOccupied) mask |= VoxelFaceMask.PosY;
            if (gridPos.y == 0 || !_runtimeGrid[gridPos.x, gridPos.y - 1, gridPos.z].isOccupied) mask |= VoxelFaceMask.NegY;
            if (gridPos.z == gz - 1 || !_runtimeGrid[gridPos.x, gridPos.y, gridPos.z + 1].isOccupied) mask |= VoxelFaceMask.PosZ;
            if (gridPos.z == 0 || !_runtimeGrid[gridPos.x, gridPos.y, gridPos.z - 1].isOccupied) mask |= VoxelFaceMask.NegZ;

            neighbor.faceMask = mask;
            _runtimeGrid[gridPos.x, gridPos.y, gridPos.z] = neighbor;

            int gpuIdx = _gpuInstanceIndexMap[gridPos.x, gridPos.y, gridPos.z];

            if (mask != VoxelFaceMask.None)
            {
                PackedVoxelGPU gpuVoxel = new PackedVoxelGPU
                {
                    packedPosition = PackedVoxelGPU.PackPosition(gridPos.x, gridPos.y, gridPos.z),
                    packedAttributes = PackedVoxelGPU.PackAttributes(neighbor.paletteIndex, neighbor.layer, neighbor.ao, neighbor.faceMask),
                    colorRGBA = PackedVoxelGPU.ColorToUInt(neighbor.customColor),
                    voxelMeta = 0
                };

                if (gpuIdx >= 0 && gpuIdx < _activeGPUList.Count)
                {
                    // 已经在渲染队列，更新其 FaceMask
                    _activeGPUList[gpuIdx] = gpuVoxel;
                }
                else
                {
                    // 新暴露的内部体素，正式推入 GPU 渲染集合！
                    _gpuInstanceIndexMap[gridPos.x, gridPos.y, gridPos.z] = _activeGPUList.Count;
                    _activeGPUList.Add(gpuVoxel);
                }
            }
            else
            {
                if (gpuIdx >= 0)
                {
                    RemoveVoxelFromGPU(gridPos);
                }
            }
        }

        private void LateUpdate()
        {
            if (!_isInitialized || _renderer == null)
            {
                if (voxelAsset != null) InitializeModel();
                else return;
            }

            if (_isDirty)
            {
                _renderer.UpdateVisibleInstances(_activeGPUList.ToArray(), _activeGPUList.Count);
                _isDirty = false;
            }

            _renderer.Render();
        }

        private void ReleaseRenderer()
        {
            _renderer?.Release();
            _renderer = null;
            _isInitialized = false;
        }

        private void OnDestroy()
        {
            ReleaseRenderer();
        }
    }
}
