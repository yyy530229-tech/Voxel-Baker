using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Runtime;

namespace VoxelGameFramework.Cannons
{
    /// <summary>
    /// 底部排队待命方块队列管理器 (匹配参考图2底部的 3~4 列待命方块)
    /// 玩家点击最前排方块时，方块飞升进入 5 个活动槽位；后排方块自动向前递进补位！
    /// </summary>
    public class VoxelQueueManager : MonoBehaviour
    {
        [Header("队列配置")]
        public int columnCount = 3;
        public int rowCount = 4;
        public float columnSpacing = 1.15f;
        public float rowSpacing = 1.1f;
        public float queueBaseY = -3.6f;

        [Header("关联组件")]
        public VoxelSlotManager slotManager;
        public VoxelModelInstance targetModel;

        private List<List<VoxelColorShooterBlock>> _columns = new List<List<VoxelColorShooterBlock>>();

        public void SetupQueue(List<Color32> availableColors, int[] defaultCapacities)
        {
            ClearQueue();

            float startX = -((columnCount - 1) * columnSpacing) * 0.5f;

            for (int col = 0; col < columnCount; col++)
            {
                List<VoxelColorShooterBlock> columnBlocks = new List<VoxelColorShooterBlock>();

                for (int row = 0; row < rowCount; row++)
                {
                    Vector3 blockPos = new Vector3(startX + col * columnSpacing, queueBaseY - row * rowSpacing, 0f);

                    GameObject blockObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    blockObj.name = $"QueueBlock_{col}_{row}";
                    blockObj.transform.SetParent(transform);
                    blockObj.transform.position = blockPos;
                    blockObj.transform.localScale = new Vector3(0.88f, 0.88f, 0.88f);

                    // 确保有碰撞体可供点击
                    BoxCollider box = blockObj.GetComponent<BoxCollider>();
                    if (box == null) box = blockObj.AddComponent<BoxCollider>();

                    VoxelColorShooterBlock shooter = blockObj.AddComponent<VoxelColorShooterBlock>();

                    // 随机分配关卡主要颜色与容量 (如 40, 50, 80)
                    Color32 c = availableColors[Random.Range(0, availableColors.Count)];
                    int cap = (defaultCapacities != null && defaultCapacities.Length > 0)
                        ? defaultCapacities[Random.Range(0, defaultCapacities.Length)]
                        : (row == 0 ? 50 : 80);

                    shooter.Initialize(c, cap, targetModel, OnBlockDisappeared);
                    columnBlocks.Add(shooter);
                }

                _columns.Add(columnBlocks);
            }
        }

        private void Update()
        {
            // 监听玩家点击最前排方块
            if (Input.GetMouseButtonDown(0) && Camera.main != null)
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    VoxelColorShooterBlock clickedBlock = hit.collider.GetComponent<VoxelColorShooterBlock>();
                    if (clickedBlock != null && clickedBlock.state == ShooterBlockState.InQueue)
                    {
                        TryDeployBlock(clickedBlock);
                    }
                }
            }

            // 平滑移动队列后排方块向前补位
            UpdateQueuePositions();
        }

        private void TryDeployBlock(VoxelColorShooterBlock block)
        {
            // 检查该方块是否处于列的最前端
            int foundCol = -1;
            int foundRow = -1;

            for (int col = 0; col < _columns.Count; col++)
            {
                for (int r = 0; r < _columns[col].Count; r++)
                {
                    if (_columns[col][r] == block)
                    {
                        foundCol = col;
                        foundRow = r;
                        break;
                    }
                }
            }

            // 只有每列最前排的方块可以点击上阵
            if (foundCol != -1 && foundRow == 0)
            {
                if (slotManager != null && slotManager.FreeSlotCount > 0)
                {
                    _columns[foundCol].RemoveAt(0);
                    slotManager.TryPlaceBlock(block);
                }
            }
        }

        private void UpdateQueuePositions()
        {
            float startX = -((columnCount - 1) * columnSpacing) * 0.5f;

            for (int col = 0; col < _columns.Count; col++)
            {
                for (int row = 0; row < _columns[col].Count; row++)
                {
                    VoxelColorShooterBlock block = _columns[col][row];
                    if (block != null && block.state == ShooterBlockState.InQueue)
                    {
                        Vector3 targetPos = new Vector3(startX + col * columnSpacing, queueBaseY - row * rowSpacing, 0f);
                        block.transform.position = Vector3.Lerp(block.transform.position, targetPos, Time.deltaTime * 10f);
                    }
                }
            }
        }

        private void OnBlockDisappeared(VoxelColorShooterBlock block)
        {
            if (slotManager != null && block != null)
            {
                slotManager.FreeSlot(block.currentSlotIndex);
            }
        }

        public void ClearQueue()
        {
            for (int i = 0; i < _columns.Count; i++)
            {
                for (int j = 0; j < _columns[i].Count; j++)
                {
                    if (_columns[i][j] != null) Destroy(_columns[i][j].gameObject);
                }
            }
            _columns.Clear();
        }
    }
}
