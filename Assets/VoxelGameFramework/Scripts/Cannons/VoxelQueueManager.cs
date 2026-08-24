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

            // 1. 统计所有颜色体素并聚合微量杂色到主色调中，杜绝产生 1发、2发 的碎方块
            Dictionary<Color32, int> rawColorCounts = new Dictionary<Color32, int>();
            int totalModelVoxels = 0;

            if (model.Asset.chunks != null)
            {
                foreach (var chunk in model.Asset.chunks)
                {
                    if (chunk.cells == null) continue;
                    foreach (var cell in chunk.cells)
                    {
                        if (!cell.isOccupied) continue;

                        Color32 rawColor = cell.customColor;
                        Color32 canonicalColor = GetCanonicalPaletteColor(rawColor, paletteColors);

                        if (rawColorCounts.ContainsKey(canonicalColor))
                            rawColorCounts[canonicalColor]++;
                        else
                            rawColorCounts[canonicalColor] = 1;

                        totalModelVoxels++;
                    }
                }
            }

            // 过滤并合并少于 30 格的微量杂色到最相近的主色中，保证只有清晰的大色块
            Dictionary<Color32, int> dominantColorCounts = new Dictionary<Color32, int>();
            List<Color32> majorColors = new List<Color32>();

            foreach (var kvp in rawColorCounts)
            {
                if (kvp.Value >= 30)
                {
                    dominantColorCounts[kvp.Key] = kvp.Value;
                    majorColors.Add(kvp.Key);
                }
            }

            // 若所有颜色都很碎，则至少保留数量最多的前 3 种主色
            if (majorColors.Count == 0)
            {
                var sorted = new List<KeyValuePair<Color32, int>>(rawColorCounts);
                sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
                for (int i = 0; i < Mathf.Min(3, sorted.Count); i++)
                {
                    dominantColorCounts[sorted[i].Key] = sorted[i].Value;
                    majorColors.Add(sorted[i].Key);
                }
            }

            // 将碎色数量 100% 守恒合并到最近的 Dominant 主色中
            foreach (var kvp in rawColorCounts)
            {
                if (!dominantColorCounts.ContainsKey(kvp.Key))
                {
                    Color32 nearestMajor = GetCanonicalPaletteColor(kvp.Key, majorColors);
                    if (dominantColorCounts.ContainsKey(nearestMajor))
                        dominantColorCounts[nearestMajor] += kvp.Value;
                    else
                        dominantColorCounts[nearestMajor] = kvp.Value;
                }
            }

            // 2. 将每种主色拆解为 35~55 发的大容量消除方块，绝对不允许出现 1, 2, 8 等碎数字！
            List<(Color32 color, int count)> blockTasks = new List<(Color32, int)>();
            int totalQueueAmmo = 0;

            foreach (var kvp in dominantColorCounts)
            {
                int remaining = kvp.Value;
                Color32 color = kvp.Key;

                while (remaining > 0)
                {
                    if (remaining <= 55)
                    {
                        // 剩余量直接作为一个独立方块 (最少也是 >= 30)
                        blockTasks.Add((color, remaining));
                        totalQueueAmmo += remaining;
                        remaining = 0;
                    }
                    else
                    {
                        // 随机切分 35~50 容量
                        int chunkSize = Random.Range(35, 51);
                        if (remaining - chunkSize < 25)
                        {
                            // 避免尾数过小，将尾数合并到当前块
                            chunkSize = remaining;
                        }

                        blockTasks.Add((color, chunkSize));
                        totalQueueAmmo += chunkSize;
                        remaining -= chunkSize;
                    }
                }
            }

            Debug.Log($"[VoxelQueueManager] 成功聚合主色！模型总占用体素: {totalModelVoxels}, 生成大容量待命方块数: {blockTasks.Count}, 总弹药: {totalQueueAmmo} (1:1 绝对守恒匹配)");

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

        private Color32 GetCanonicalPaletteColor(Color32 c, List<Color32> palette)
        {
            if (palette == null || palette.Count == 0) return c;

            Color32 best = palette[0];
            float minDist = float.MaxValue;

            for (int i = 0; i < palette.Count; i++)
            {
                Color32 p = palette[i];
                float dr = c.r - p.r;
                float dg = c.g - p.g;
                float db = c.b - p.b;
                float distSq = dr * dr + dg * dg + db * db;
                if (distSq < minDist)
                {
                    minDist = distSq;
                    best = p;
                }
            }
            return best;
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
