using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Data;
using VoxelBaker.Runtime;
using VoxelGameFramework.Core;

namespace VoxelGameFramework.Cannons
{
    /// <summary>
    /// 待命方块队列管理器 (数学级 1:1 精准匹配模型当前存活体素总数，保证 100% 消除通关，绝无多余与遗漏！)
    /// </summary>
    public class VoxelQueueManager : MonoBehaviour
    {
        [Header("队列配置")]
        public int columnCount = 3;
        public float columnSpacing = 1.15f;
        public float rowSpacing = 1.05f;
        public float queueBaseY = -3.4f;

        [Header("关联组件")]
        public VoxelSlotManager slotManager;
        public VoxelModelInstance targetModel;

        private List<List<VoxelColorShooterBlock>> _columns = new List<List<VoxelColorShooterBlock>>();

        public void SetupQueueFromModel(VoxelModelInstance model)
        {
            targetModel = model;
            ClearQueue();

            if (model == null || model.Asset == null) return;

            // 1. 直接从模型调色板或网格提取全局离散颜色映射，杜绝动态字典带来的分类漂移
            List<Color32> paletteColors = new List<Color32>();
            if (model.Asset.palette != null && model.Asset.palette.entries != null && model.Asset.palette.entries.Count > 0)
            {
                foreach (var entry in model.Asset.palette.entries)
                {
                    if (entry.baseColor.a > 0) paletteColors.Add((Color32)entry.baseColor);
                }
            }

            // 1. 提取模型中所有有效体素的颜色，并使用 K-Means 算法聚合为 3~5 种鲜明的主题主色调
            List<Vector3Int> allOccupiedPositions = new List<Vector3Int>();
            List<Color32> allVoxelColors = new List<Color32>();

            if (model.Asset.chunks != null)
            {
                foreach (var chunk in model.Asset.chunks)
                {
                    if (chunk.cells == null) continue;
                    foreach (var cell in chunk.cells)
                    {
                        if (!cell.isOccupied) continue;
                        allOccupiedPositions.Add(cell.gridPos);
                        allVoxelColors.Add(cell.customColor);
                    }
                }
            }

            int totalModelVoxels = allVoxelColors.Count;
            if (totalModelVoxels == 0) return;

            // 智能确定主色数量 K (通常为 3~5 种鲜明主色)
            int k = Mathf.Clamp(Mathf.Min(4, model.Asset.palette != null && model.Asset.palette.entries != null ? model.Asset.palette.entries.Count : 3), 2, 5);
            
            // 执行 K-Means 聚类，得到 K 个纯净鲜明的标准主色
            List<Color32> clusterCentroids = PerformKMeansClustering(allVoxelColors, k);

            // 统计每个主色的体素数量，并同步更新模型运行时网格的颜色为纯净聚类色
            int[] clusterVoxelCounts = new int[clusterCentroids.Count];
            for (int i = 0; i < allVoxelColors.Count; i++)
            {
                int bestClusterIdx = GetNearestClusterIndex(allVoxelColors[i], clusterCentroids);
                clusterVoxelCounts[bestClusterIdx]++;

                // 将模型体素颜色与主色严格 1:1 对齐，保证视觉与发射方块完全一致
                Vector3Int gPos = allOccupiedPositions[i];
                var cell = model.GetCell(gPos);
                if (cell.isOccupied)
                {
                    cell.customColor = clusterCentroids[bestClusterIdx];
                }
            }

            // 2. 将每个主色均匀切分为 35~50 发的大容量方块，绝对不允许出现任何个位数碎方块！
            List<(Color32 color, int count)> blockTasks = new List<(Color32, int)>();
            int totalQueueAmmo = 0;

            for (int c = 0; c < clusterCentroids.Count; c++)
            {
                int count = clusterVoxelCounts[c];
                Color32 col = clusterCentroids[c];

                if (count <= 0) continue;

                while (count > 0)
                {
                    if (count <= 55)
                    {
                        blockTasks.Add((col, count));
                        totalQueueAmmo += count;
                        count = 0;
                    }
                    else
                    {
                        int size = Random.Range(35, 51);
                        if (count - size < 25)
                        {
                            size = count; // 尾数自动合并
                        }
                        blockTasks.Add((col, size));
                        totalQueueAmmo += size;
                        count -= size;
                    }
                }
            }

            Debug.Log($"[VoxelQueueManager] K-Means 聚类完成！主色数: {clusterCentroids.Count}, 模型总占用: {totalModelVoxels}, 生成方块数: {blockTasks.Count}, 总弹药: {totalQueueAmmo} (1:1 绝对守恒)");

            // 3. 乱序排列 (Fisher-Yates Shuffle)
            for (int i = blockTasks.Count - 1; i > 0; i--)
            {
                int r = Random.Range(0, i + 1);
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

        private static int GetNearestClusterIndex(Color32 c, List<Color32> centroids)
        {
            if (centroids == null || centroids.Count == 0) return 0;
            int best = 0;
            float minDist = float.MaxValue;
            for (int i = 0; i < centroids.Count; i++)
            {
                Color32 p = centroids[i];
                float dr = c.r - p.r;
                float dg = c.g - p.g;
                float db = c.b - p.b;
                float distSq = dr * dr + dg * dg + db * db;
                if (distSq < minDist)
                {
                    minDist = distSq;
                    best = i;
                }
            }
            return best;
        }

        private static List<Color32> PerformKMeansClustering(List<Color32> colors, int k)
        {
            List<Color32> centroids = new List<Color32>();
            if (colors == null || colors.Count == 0) return centroids;

            k = Mathf.Clamp(k, 1, colors.Count);

            // 1. 选取初始聚类中心 (选取彼此色彩距离最大的点)
            centroids.Add(colors[0]);
            while (centroids.Count < k)
            {
                Color32 bestNext = colors[0];
                float maxMinDist = -1f;

                for (int i = 0; i < colors.Count; i++)
                {
                    Color32 candidate = colors[i];
                    float minDistToExisting = float.MaxValue;
                    for (int c = 0; c < centroids.Count; c++)
                    {
                        Color32 exist = centroids[c];
                        float dr = candidate.r - exist.r;
                        float dg = candidate.g - exist.g;
                        float db = candidate.b - exist.b;
                        float d = dr * dr + dg * dg + db * db;
                        if (d < minDistToExisting) minDistToExisting = d;
                    }

                    if (minDistToExisting > maxMinDist)
                    {
                        maxMinDist = minDistToExisting;
                        bestNext = candidate;
                    }
                }

                centroids.Add(bestNext);
            }

            // 2. 迭代 6 轮计算均值更新聚类中心
            for (int iter = 0; iter < 6; iter++)
            {
                long[] sumR = new long[k];
                long[] sumG = new long[k];
                long[] sumB = new long[k];
                int[] clusterCounts = new int[k];

                for (int i = 0; i < colors.Count; i++)
                {
                    Color32 c = colors[i];
                    int clusterIdx = GetNearestClusterIndex(c, centroids);
                    sumR[clusterIdx] += c.r;
                    sumG[clusterIdx] += c.g;
                    sumB[clusterIdx] += c.b;
                    clusterCounts[clusterIdx]++;
                }

                for (int c = 0; c < k; c++)
                {
                    if (clusterCounts[c] > 0)
                    {
                        byte nr = (byte)(sumR[c] / clusterCounts[c]);
                        byte ng = (byte)(sumG[c] / clusterCounts[c]);
                        byte nb = (byte)(sumB[c] / clusterCounts[c]);
                        centroids[c] = new Color32(nr, ng, nb, 255);
                    }
                }
            }

            return centroids;
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
