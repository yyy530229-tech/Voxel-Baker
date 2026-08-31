using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Data;
using VoxelBaker.Runtime;
using VoxelGameFramework.Core;

namespace VoxelGameFramework.Cannons
{
    /// <summary>
    /// 待命消除方块队列管理器 (Voxel Queue Manager)
    /// 职责：
    /// 1. 从任意 VoxelModelInstance 中自动提取各色系存活体素并聚类；
    /// 2. 按 1:1 绝对守恒拆分为发射方块；
    /// 3. 管理 3 列排队队列的平滑推进与点击装填。
    /// 彻底独立于烘焙管线，不依赖任何特定模型或硬编码关卡逻辑。
    /// </summary>
    public class VoxelQueueManager : MonoBehaviour
    {
        [Header("队列排布参数")]
        public int columnCount = 3;
        public float columnSpacing = 1.15f;
        public float rowSpacing = 1.05f;
        public float queueBaseY = -3.4f;

        [Header("场景引用")]
        public VoxelSlotManager slotManager;
        public VoxelModelInstance targetModel;

        private readonly List<List<VoxelColorShooterBlock>> _columns = new List<List<VoxelColorShooterBlock>>();

        /// <summary>
        /// 从目标模型中自动构建消除队列
        /// </summary>
        public void SetupQueueFromModel(VoxelModelInstance model)
        {
            targetModel = model;
            ClearQueue();

            if (model == null || model.Asset == null) return;

            // 1. 遍历收集模型中所有有效存活体素及其原始颜色
            List<Vector3Int> allOccupiedPositions = new List<Vector3Int>();
            List<Color32> allVoxelColors = new List<Color32>();

            if (model.Asset.chunks != null)
            {
                foreach (var chunk in model.Asset.chunks)
                {
                    if (chunk.cells == null) continue;
                    foreach (var cell in chunk.cells)
                    {
                        if (!cell.isOccupied || !cell.isAlive) continue;
                        allOccupiedPositions.Add(cell.gridPos);
                        allVoxelColors.Add(cell.customColor);
                    }
                }
            }

            int totalModelVoxels = allVoxelColors.Count;
            if (totalModelVoxels == 0) return;

            // 2. 基于色相空间的通用自适应色彩聚类
            Dictionary<int, List<int>> colorBuckets = new Dictionary<int, List<int>>();
            for (int i = 0; i < allVoxelColors.Count; i++)
            {
                int bucketKey = VoxelColorUtility.GetHueFamilyKey(allVoxelColors[i]);
                if (!colorBuckets.ContainsKey(bucketKey))
                {
                    colorBuckets[bucketKey] = new List<int>();
                }
                colorBuckets[bucketKey].Add(i);
            }

            // 3. 计算每个色系的代表色并同步到模型体素网格
            List<(Color32 color, int count)> distinctCategoryList = new List<(Color32, int)>();
            foreach (var kvp in colorBuckets)
            {
                var indices = kvp.Value;
                long sumR = 0, sumG = 0, sumB = 0;
                for (int j = 0; j < indices.Count; j++)
                {
                    Color32 c = allVoxelColors[indices[j]];
                    sumR += c.r; sumG += c.g; sumB += c.b;
                }
                Color32 avgColor = new Color32((byte)(sumR / indices.Count), (byte)(sumG / indices.Count), (byte)(sumB / indices.Count), 255);

                for (int j = 0; j < indices.Count; j++)
                {
                    Vector3Int gPos = allOccupiedPositions[indices[j]];
                    var cell = model.GetCell(gPos);
                    if (cell.isOccupied)
                    {
                        cell.customColor = avgColor;
                    }
                }

                distinctCategoryList.Add((avgColor, indices.Count));
            }

            // 同步刷新模型 GPU 颜色缓冲，保证视觉绝对一致
            model.SynchronizeGPUColors();

            // 4. 1:1 数学守恒切分为发射方块任务
            List<(Color32 color, int count)> blockTasks = new List<(Color32, int)>();
            float targetBlockAverage = totalModelVoxels > 2500 ? Mathf.Clamp(totalModelVoxels / 36f, 45f, 180f) : 42f;
            int maxSingleBlockCapacity = Mathf.RoundToInt(targetBlockAverage * 1.3f);

            for (int c = 0; c < distinctCategoryList.Count; c++)
            {
                int count = distinctCategoryList[c].count;
                Color32 col = distinctCategoryList[c].color;
                if (count <= 0) continue;

                if (count <= maxSingleBlockCapacity)
                {
                    blockTasks.Add((col, count));
                }
                else
                {
                    int numBlocks = Mathf.CeilToInt((float)count / targetBlockAverage);
                    int baseSize = count / numBlocks;
                    int remainder = count % numBlocks;

                    for (int b = 0; b < numBlocks; b++)
                    {
                        int blockSize = baseSize + (b < remainder ? 1 : 0);
                        blockTasks.Add((col, blockSize));
                    }
                }
            }

            // 5. 随机洗牌并填充进 3 列队列中
            for (int i = blockTasks.Count - 1; i > 0; i--)
            {
                int r = UnityEngine.Random.Range(0, i + 1);
                var temp = blockTasks[i];
                blockTasks[i] = blockTasks[r];
                blockTasks[r] = temp;
            }

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
            // 3D 方块用射线拾取点击 (方块是 3D 立方体)
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

            for (int c = 0; c < _columns.Count; c++)
            {
                for (int r = 0; r < _columns[c].Count; r++)
                {
                    if (_columns[c][r] == block)
                    {
                        foundCol = c;
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

        /// <summary>
        /// 获取指定列最前排方块 (Row 0)
        /// </summary>
        public VoxelColorShooterBlock GetFrontBlock(int columnIndex)
        {
            if (columnIndex >= 0 && columnIndex < _columns.Count && _columns[columnIndex].Count > 0)
            {
                return _columns[columnIndex][0];
            }
            return null;
        }

        /// <summary>
        /// 获取指定列指定行的方块
        /// </summary>
        public VoxelColorShooterBlock GetBlockAt(int columnIndex, int rowIndex)
        {
            if (columnIndex >= 0 && columnIndex < _columns.Count &&
                rowIndex >= 0 && rowIndex < _columns[columnIndex].Count)
            {
                return _columns[columnIndex][rowIndex];
            }
            return null;
        }

        /// <summary>
        /// 从 UI 触发方块部署 (与鼠标点击等效)
        /// </summary>
        public void TryDeployFromUI(VoxelColorShooterBlock block)
        {
            if (block == null || block.state != ShooterBlockState.InQueue) return;
            TryDeployBlock(block);
        }
    }
}
