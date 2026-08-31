using System.Collections.Generic;
using UnityEngine;
using VoxelGameFramework.Audio;
using VoxelGameFramework.Core;
using VoxelGameFramework.Events;

namespace VoxelGameFramework.Cannons
{
    /// <summary>
    /// 5 联装活动射击槽位管理器 (匹配参考图中间 5 个收集槽位)
    /// 槽位状态通过 GameEventBus (GameFramework IEventManager) 广播, 表现层按需订阅
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
                if (_occupiedBlocks == null) return totalSlots;
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

                // 3D 底座几何体 (参考图中间 5 个暗色凹槽底座)
                GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pedestal.name = $"SlotPedestal_{i}";
                pedestal.transform.SetParent(transform);
                pedestal.transform.position = pos - Vector3.up * 0.52f;
                pedestal.transform.localScale = new Vector3(0.95f, 0.12f, 0.95f);

                Material pm = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                pm.color = new Color(0.11f, 0.14f, 0.18f); // 深色底座
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

                    // 广播槽位填充事件 (UI 监听)
                    GameEventBus.Fire(this, SlotFilledEventArgs.Create(i, block.blockColor));

                    // 入槽音效 (改发命令事件, 由 VoxelSoundManager 订阅执行)
                    GameEventBus.Fire(this, SfxPlayedEventArgs.Create(
                        VoxelSoundManager.SfxType.SlotPlace, 0.6f));
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

                // 广播槽位释放事件 (UI 监听)
                GameEventBus.Fire(this, SlotEmptiedEventArgs.Create(slotIndex));
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
                _pedestals = null;
            }
        }
    }
}
