using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Data;
using VoxelBaker.Runtime;
using VoxelGameFramework.Core;

namespace VoxelGameFramework.Cannons
{
    /// <summary>
    /// 待命方块队列管理器
    /// 自动统计 3D 模型各色块体素数，生成总数 1:1 精确匹配的待命方块，消完即 100% 通关！
    /// </summary>
    public class VoxelQueueManager : MonoBehaviour
    {
        [Header("队列配置")]
        public int columnCount = 3;
        public float columnSpacing = 1.15f;
        public float rowSpacing = 1.1f;
        public float queueBaseY = -3.6f;

        [Header("关联组件")]
        public VoxelSlotManager slotManager;
        public VoxelModelInstance targetModel;

        private List<List<VoxelColorShooterBlock>> _columns = new List<List<VoxelColorShooterBlock>>();

        public void SetupQueueFromModel(VoxelModelInstance model)
        {
            targetModel = model;
            ClearQueue();

            if (model == null || model.Asset == null) return;

            // 1. 统计当前模型每种颜色的体素真实数量
            Dictionary<Color32, int> colorCounts = new Dictionary<Color32, int>();

            if (model.Asset.chunks != null)
            {
                foreach (var chunk in model.Asset.chunks)
                {
                    if (chunk.cells == null) continue;
                    foreach (var cell in chunk.cells)
                    {
                        if (!cell.isOccupied) continue;

                        Color32 c = cell.customColor;
                        Color32 matchedKey = c;
                        bool found = false;

                        foreach (var key in colorCounts.Keys)
                        {
                            if (VoxelColorUtility.IsColorMatching(key, c, 60f))
                            {
                                matchedKey = key;
                                found = true;
                                break;
                            }
                        }

                        if (found)
                        {
                            colorCounts[matchedKey]++;
                        }
                        else
                        {
                            colorCounts[c] = 1;
                        }
                    }
                }
            }

            // 2. 将各颜色的体素总数拆分为适量大小的方块（如 40~60/块）
            List<(Color32 color, int count)> blockTasks = new List<(Color32, int)>();

            foreach (var kvp in colorCounts)
            {
                int remaining = kvp.Value;
                while (remaining > 0)
                {
                    int chunkSize = Mathf.Min(remaining, UnityEngine.Random.Range(35, 65));
                    if (remaining - chunkSize < 20) chunkSize = remaining; // 避免产生过小的碎块

                    blockTasks.Add((kvp.Key, chunkSize));
                    remaining -= chunkSize;
                }
            }

            // 3. 乱序排列，增加解谜趣味性 (Fisher-Yates Shuffle)
            for (int i = blockTasks.Count - 1; i > 0; i--)
            {
                int r = UnityEngine.Random.Range(0, i + 1);
                var temp = blockTasks[i];
                blockTasks[i] = blockTasks[r];
                blockTasks[r] = temp;
            }

            // 4. 将方块均分排入 3 列队列中
            for (int col = 0; col < columnCount; col++)
            {
                _columns.Add(new List<VoxelColorShooterBlock>());
            }

            float startX = -((columnCount - 1) * columnSpacing) * 0.5f;

            for (int i = 0; i < blockTasks.Count; i++)
            {
                int col = i % columnCount;
                int row = i / columnCount;

                Vector3 blockPos = new Vector3(startX + col * columnSpacing, queueBaseY - row * rowSpacing, 0f);

                GameObject blockObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blockObj.name = $"QueueBlock_{col}_{row}";
                blockObj.transform.SetParent(transform);
                blockObj.transform.position = blockPos;
                blockObj.transform.localScale = new Vector3(0.88f, 0.88f, 0.88f);

                BoxCollider box = blockObj.GetComponent<BoxCollider>();
                if (box == null) box = blockObj.AddComponent<BoxCollider>();

                VoxelColorShooterBlock shooter = blockObj.AddComponent<VoxelColorShooterBlock>();
                shooter.Initialize(blockTasks[i].color, blockTasks[i].count, targetModel, OnBlockDisappeared);

                _columns[col].Add(shooter);
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

            // 队列后排平滑向前推进
            UpdateQueuePositions();
        }

        private void TryDeployBlock(VoxelColorShooterBlock block)
        {
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

            // 只能点击每列最前排的方块 (Row 0)
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
