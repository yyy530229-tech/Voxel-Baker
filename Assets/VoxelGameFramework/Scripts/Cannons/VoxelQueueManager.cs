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

            // 1. 提取模型中所有有效体素的颜色，并使用色相感知(Hue-Aware)特征聚类，确保竹绿、亮橙等特征色绝对保留！
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

            // 执行色相感知聚类：将所有体素分类到各自鲜明的主题色系（如：绿色竹子、纯白毛发、深黑四肢）
            Dictionary<int, List<int>> colorBuckets = new Dictionary<int, List<int>>();
            Dictionary<int, Color32> bucketRepresentativeColors = new Dictionary<int, Color32>();

            for (int i = 0; i < allVoxelColors.Count; i++)
            {
                Color32 c = allVoxelColors[i];
                int bucketKey = GetHueAwareBucketKey(c);

                if (!colorBuckets.ContainsKey(bucketKey))
                {
                    colorBuckets[bucketKey] = new List<int>();
                    bucketRepresentativeColors[bucketKey] = c;
                }
                colorBuckets[bucketKey].Add(i);
            }

            // 计算每个色系的纯净平均代表色，并将模型网格体素更新为该纯净色
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

                // 如果是竹绿色系，增强饱和度让方块鲜艳亮丽
                Color.RGBToHSV(avgColor, out float h, out float s, out float v);
                if (s > 0.15f)
                {
                    avgColor = Color.HSVToRGB(h, Mathf.Clamp01(s * 1.35f + 0.1f), Mathf.Clamp01(v * 1.1f));
                }

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

            // 2. 将每个色系切分为大容量方块 (若某个特征色如竹子总量较少，如 25~45 发，直接生成专属绿色大方块)
            List<(Color32 color, int count)> blockTasks = new List<(Color32, int)>();
            int totalQueueAmmo = 0;

            for (int c = 0; c < distinctCategoryList.Count; c++)
            {
                int count = distinctCategoryList[c].count;
                Color32 col = distinctCategoryList[c].color;

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
                        if (count - size < 20)
                        {
                            size = count; // 尾数自动合并
                        }
                        blockTasks.Add((col, size));
                        totalQueueAmmo += size;
                        count -= size;
                    }
                }
            }

            Debug.Log($"[VoxelQueueManager] 色相特征提取成功！提取特征色数: {distinctCategoryList.Count}, 总占用: {totalModelVoxels}, 总弹药: {totalQueueAmmo} (1:1 绝对守恒)");

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

        /// <summary>
        /// 色相感知(Hue-Aware)分类器：将颜色分类为纯白、深黑、竹绿、明黄、亮橙、湛蓝等独立的主题色系
        /// </summary>
        private static int GetHueAwareBucketKey(Color32 c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);

            // 1. 无彩色系 (黑、白、灰)
            if (s < 0.18f)
            {
                if (v >= 0.55f) return 100; // 白色系 (White / Off-white)
                return 101;                 // 黑色/深灰系 (Black / Charcoal)
            }

            // 2. 有彩色系 (根据色相 Hue 划分为鲜明色系，保证绿竹子绝对独立！)
            float deg = h * 360f;
            if (deg >= 55f && deg <= 165f)
            {
                return 1; // 🌿 绿色竹子 / 植物色系 (Green)
            }
            else if (deg >= 25f && deg < 55f)
            {
                return 2; // 💛 黄色色系 (Yellow)
            }
            else if (deg >= 165f && deg <= 260f)
            {
                return 3; // 💙 蓝色色系 (Blue)
            }
            else if (deg >= 260f && deg <= 330f)
            {
                return 4; // 💜 紫/粉色系 (Purple / Pink)
            }
            else
            {
                return 5; // ❤️ 红/橙色系 (Red / Orange)
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
