using System;
using System.IO;
using UnityEngine;

namespace VoxelBaker.Data
{
    /// <summary>
    /// 模型类别标签
    /// </summary>
    public enum VoxelModelCategory
    {
        General,        // 通用常规
        Characters,     // 角色 / 怪物
        Buildings,      // 建筑 / 房屋
        Props,          // 场景道具 / 物件
        Obstacles,      // 障碍物 / 靶子
        Food,           // 食物 / 蛋糕 / 水果
        Weapons         // 武器 / 装备
    }

    /// <summary>
    /// 单个模型的烘焙工程配方文件 (ScriptableObject)
    /// 记录该模型的所有输入参数、材质覆盖、内部配方与烘焙产物关联，方便后续随时一键重新烘焙与版本追踪！
    /// </summary>
    [CreateAssetMenu(fileName = "NewVoxelRecipe", menuName = "Voxel Baker/Voxel Model Recipe")]
    public class VoxelModelRecipe : ScriptableObject
    {
        [Header("基础工程信息")]
        public string modelName = "NewModel";
        public VoxelModelCategory category = VoxelModelCategory.General;
        public string tags = "Enemy, Destructible";
        public string description = "";

        [Header("输入源配置")]
        public GameObject sourcePrefab;
        public Mesh sourceMesh;
        public Material[] sourceMaterials;

        [Header("几何与分辨率参数")]
        [Range(0.01f, 0.5f)] public float voxelSize = 0.08f;
        public bool fillInteriorSolid = true;

        [Header("材质与调色板参数")]
        [Range(0f, 15f)] public float colorTolerance = 4.0f;

        [Header("内部层级配方")]
        public VoxelInteriorProfile interiorProfile;

        [Header("空间优化参数")]
        public int chunkSize = 16;
        public bool generateLODs = true;

        [Header("工程输出路径")]
        public string customOutputSubFolder = ""; // 例如 "Characters/Enemies"

        [Header("烘焙状态与产物关联")]
        public VoxelAsset bakedAsset;
        public string lastBakeTime = "";
        public float lastBakeDuration = 0f;
        public int lastTotalVoxels = 0;
        public bool isDirty = true; // 是否源模型修改过需要重新烘焙

        public string GetTargetOutputFolder()
        {
            string baseFolder = "Assets/VoxelAssets";
            string catFolder = category.ToString();
            if (!string.IsNullOrEmpty(customOutputSubFolder))
            {
                return Path.Combine(baseFolder, customOutputSubFolder).Replace("\\", "/");
            }
            return Path.Combine(baseFolder, catFolder).Replace("\\", "/");
        }
    }
}
