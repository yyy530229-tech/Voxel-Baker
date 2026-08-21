using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelBaker.Data
{
    public enum InteriorStrategy
    {
        ExtendSurfaceColor,     // 继承表面颜色（适合塑料、均质玩具、石头）
        DominantMaterial,       // 主体材质填充（适合单主体模型）
        NearestSurfaceMaterial, // 继承最近表面材质（适合多色多区域模型）
        CustomProfileLayers,    // 按分层厚度与自定义颜色/材质填充（适合水果、蛋糕、角色）
        ProceduralNoise         // 3D 噪声/矿石脉络生成
    }

    [Serializable]
    public struct InteriorLayerRule
    {
        public string name;
        [Range(1, 32)] public int depthThickness;  // 该层厚度（体素格数）
        public Color layerColor;                  // 该层颜色
        [Range(0f, 1f)] public float metallic;
        [Range(0f, 1f)] public float smoothness;
        public short initialHP;                   // 该层生命值
        public int gameplayTag;                   // 游戏逻辑标签
    }

    [CreateAssetMenu(fileName = "NewInteriorProfile", menuName = "Voxel Baker/Interior Profile")]
    public class VoxelInteriorProfile : ScriptableObject
    {
        [Header("Interior Strategy")]
        public InteriorStrategy strategy = InteriorStrategy.CustomProfileLayers;

        [Header("Layer Thickness & Rules (From Outside to Inside)")]
        public List<InteriorLayerRule> layerRules = new List<InteriorLayerRule>
        {
            new InteriorLayerRule { name = "Inner Sub-Surface", depthThickness = 1, layerColor = new Color(0.9f, 0.4f, 0.6f), metallic = 0f, smoothness = 0.5f, initialHP = 1, gameplayTag = 1 },
            new InteriorLayerRule { name = "Interior Pulp/Cake", depthThickness = 3, layerColor = new Color(0.2f, 0.85f, 0.7f), metallic = 0f, smoothness = 0.3f, initialHP = 2, gameplayTag = 2 },
            new InteriorLayerRule { name = "Core Skeleton/Seed", depthThickness = 99, layerColor = new Color(0.4f, 0.9f, 0.2f), metallic = 0.2f, smoothness = 0.8f, initialHP = 3, gameplayTag = 3 }
        };

        [Header("Procedural Noise Settings (If Procedural)")]
        public float noiseFrequency = 0.15f;
        public Color noiseColorA = new Color(1f, 0.8f, 0.2f);
        public Color noiseColorB = new Color(0.9f, 0.3f, 0.1f);

        [Header("Default Core Settings")]
        public Color defaultCoreColor = new Color(0.3f, 0.3f, 0.3f);
        public short defaultHP = 1;

        public InteriorLayerRule GetRuleForDepth(int depth)
        {
            int accumulated = 0;
            for (int i = 0; i < layerRules.Count; i++)
            {
                accumulated += layerRules[i].depthThickness;
                if (depth <= accumulated)
                {
                    return layerRules[i];
                }
            }

            if (layerRules.Count > 0)
                return layerRules[layerRules.Count - 1];

            return new InteriorLayerRule
            {
                name = "DefaultCore",
                depthThickness = 1,
                layerColor = defaultCoreColor,
                metallic = 0f,
                smoothness = 0.5f,
                initialHP = defaultHP,
                gameplayTag = 0
            };
        }
    }
}
