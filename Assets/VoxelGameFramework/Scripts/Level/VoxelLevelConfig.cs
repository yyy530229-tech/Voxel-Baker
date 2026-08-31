using System;
using UnityEngine;
using UnityEngine.Serialization;
using VoxelBaker.Data;

namespace VoxelGameFramework.Level
{
    /// <summary>
    /// 独立关卡配置资产 (ScriptableObject)
    /// 职责：为每个关卡独立配置目标体素模型、生成位置、背景氛围与通关规则
    /// 用户在 Project 视图中右键即可一键创建新关卡：Create -> 🎮 关卡配置 (Level Config)
    /// </summary>
    [CreateAssetMenu(fileName = "Level_01", menuName = "🎮 关卡配置 (Level Config)", order = 1)]
    public class VoxelLevelConfig : ScriptableObject
    {
        [Header("📋 关卡基本信息")]
        [Tooltip("关卡序号 (从 1 开始)")]
        public int levelIndex = 1;

        [Tooltip("关卡显示标题")]
        public string levelTitle = "第一关";

        [Header("🎯 目标体素模型 (直接拖入任意烘焙好的 VoxelAsset)")]
        [FormerlySerializedAs("targetAsset")]
        [Tooltip("该关卡需要玩家消除的 3D 体素模型资产")]
        public VoxelAsset targetVoxelAsset;

        [Tooltip("模型在场景中的生成坐标")]
        public Vector3 spawnPosition = new Vector3(0f, 0.8f, 0f);

        [Tooltip("模型初始旋转欧拉角")]
        public Vector3 spawnRotation = new Vector3(0f, 0f, 0f);

        [Tooltip("模型缩放比例")]
        [Range(0.1f, 5.0f)]
        public float spawnScale = 1.0f;

        [Header("🎨 场景氛围")]
        [Tooltip("主相机背景颜色")]
        public Color backgroundColor = new Color(0.14f, 0.18f, 0.24f);

        [Header("🏆 通关规则与奖励")]
        [Tooltip("通关所需的最小粉碎比例 (0.95 表示破坏 95% 体素即判定通关)")]
        [Range(0.5f, 1.0f)]
        public float winDestructionRatio = 0.95f;

        [Tooltip("通关获得的金币奖励")]
        public int rewardCoins = 500;
    }
}
