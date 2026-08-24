using System.Collections.Generic;
using UnityEngine;

namespace VoxelGameFramework.Cannons
{
    /// <summary>
    /// 5 联装活动射击槽位管理器 (匹配参考图2中间 5 个底座槽位)
    /// </summary>
    public class VoxelSlotManager : MonoBehaviour
    {
        [Header("槽位配置")]
        public int totalSlots = 5;
        public float slotSpacing = 1.15f;
        public float slotYPosition = -1.6f;

        private Vector3[] _slotPositions;
        private VoxelColorShooterBlock[] _occupiedBlocks;
        private GameObject[] _pedestals;

        public int FreeSlotCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < totalSlots; i++)
                {
                    if (_occupiedBlocks[i] == null) count++;
                }
                return count;
            }
        }

        public void InitializeSlots()
        {
            _slotPositions = new Vector3[totalSlots];
            _occupiedBlocks = new VoxelColorShooterBlock[totalSlots];
            _pedestals = new GameObject[totalSlots];

            float startX = -((totalSlots - 1) * slotSpacing) * 0.5f;

            for (int i = 0; i < totalSlots; i++)
            {
                Vector3 pos = new Vector3(startX + i * slotSpacing, slotYPosition, 0f);
                _slotPositions[i] = pos;

                // 创建底座槽位凹槽可视化几何体 (匹配截图2中的 5 个暗色凹槽)
                GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pedestal.name = $"SlotPedestal_{i}";
                pedestal.transform.SetParent(transform);
                pedestal.transform.position = pos - Vector3.up * 0.52f;
                pedestal.transform.localScale = new Vector3(0.95f, 0.12f, 0.95f);

                Material pm = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                pm.color = new Color(0.11f, 0.14f, 0.18f); // 优雅深色底座
                pedestal.GetComponent<Renderer>().sharedMaterial = pm;

                _pedestals[i] = pedestal;
            }
        }

        public bool TryPlaceBlock(VoxelColorShooterBlock block)
        {
            for (int i = 0; i < totalSlots; i++)
            {
                if (_occupiedBlocks[i] == null)
                {
                    _occupiedBlocks[i] = block;
                    block.MoveToSlot(i, _slotPositions[i]);
                    return true;
                }
            }
            return false;
        }

        public void FreeSlot(int slotIndex)
        {
            if (slotIndex >= 0 && slotIndex < totalSlots)
            {
                _occupiedBlocks[slotIndex] = null;
            }
        }

        public void ClearAll()
        {
            if (_occupiedBlocks != null)
            {
                for (int i = 0; i < _occupiedBlocks.Length; i++)
                {
                    if (_occupiedBlocks[i] != null)
                    {
                        Destroy(_occupiedBlocks[i].gameObject);
                        _occupiedBlocks[i] = null;
                    }
                }
            }
            if (_pedestals != null)
            {
                for (int i = 0; i < _pedestals.Length; i++)
                {
                    if (_pedestals[i] != null) Destroy(_pedestals[i]);
                }
            }
        }
    }
}
