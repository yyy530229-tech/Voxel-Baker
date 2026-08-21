using System;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelGameFramework.Level
{
    /// <summary>
    /// 单个关卡配置 (ScriptableObject)
    /// 独立管理关卡目标模型、通关条件、炮台初始数值与奖励
    /// </summary>
    [CreateAssetMenu(fileName = "NewLevelConfig", menuName = "Voxel Game Framework/Level Config")]
    public class VoxelLevelConfig : ScriptableObject
    {
        [Header("关卡基本信息")]
        public int levelIndex = 1;
        public string levelTitle = "关卡 1: 可爱小黄鸭";
        [TextArea(2, 4)] public string description = "射击粉碎小黄鸭，将其剥离并消除！";

        [Header("目标体素资产")]
        public VoxelAsset targetAsset;
        public Vector3 spawnPosition = new Vector3(0f, 1.2f, 0f);
        public Vector3 spawnRotation = Vector3.zero;
        public float spawnScale = 1.0f;

        [Header("通关规则与目标")]
        [Range(0.5f, 1.0f)] public float winDestructionRatio = 0.95f; // 破坏达到 95% 即判定通关
        public int rewardCoins = 500;

        [Header("炮台编队初始数值 (匹配参考图底部5联装)")]
        public int[] initialCannonPowers = new int[] { 33, 45, 55, 66, 77 };

        [Header("场景氛围")]
        public Color backgroundColor = new Color(0.16f, 0.22f, 0.30f);
    }
}
