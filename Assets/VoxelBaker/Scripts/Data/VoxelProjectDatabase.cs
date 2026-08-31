using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxelBaker.Baker;

namespace VoxelBaker.Data
{
    /// <summary>
    /// 体素工程项目中央数据库 (ScriptableObject)
    /// 统一分类、索引、检索、批量管理项目内所有模型的烘焙工程与资产文件，彻底杜绝混乱！
    /// </summary>
    [CreateAssetMenu(fileName = "VoxelProjectDatabase", menuName = "Voxel Baker/Project Database")]
    public class VoxelProjectDatabase : ScriptableObject
    {
        [Header("工程配方列表")]
        public List<VoxelModelRecipe> recipes = new List<VoxelModelRecipe>();

        public const string DatabaseDefaultPath = "Assets/VoxelAssets/VoxelProjectDatabase.asset";

        public static VoxelProjectDatabase GetOrCreateDatabase()
        {
#if UNITY_EDITOR
            VoxelProjectDatabase db = AssetDatabase.LoadAssetAtPath<VoxelProjectDatabase>(DatabaseDefaultPath);
            if (db == null)
            {
                string dir = Path.GetDirectoryName(DatabaseDefaultPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                db = CreateInstance<VoxelProjectDatabase>();
                AssetDatabase.CreateAsset(db, DatabaseDefaultPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            return db;
#else
            return null;
#endif
        }

#if UNITY_EDITOR
        public void ScanAndRefreshRecipes()
        {
            recipes.Clear();
            string[] guids = AssetDatabase.FindAssets("t:VoxelModelRecipe");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                VoxelModelRecipe recipe = AssetDatabase.LoadAssetAtPath<VoxelModelRecipe>(path);
                if (recipe != null && !recipes.Contains(recipe))
                {
                    recipes.Add(recipe);
                }
            }
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        public VoxelModelRecipe CreateNewRecipe(string modelName, VoxelModelCategory category, Mesh mesh, Material[] mats)
        {
            VoxelModelRecipe recipe = CreateInstance<VoxelModelRecipe>();
            recipe.modelName = modelName;
            recipe.category = category;
            recipe.sourceMesh = mesh;
            recipe.sourceMaterials = mats;

            string targetFolder = recipe.GetTargetOutputFolder();
            if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

            string recipePath = $"{targetFolder}/{modelName}_Recipe.asset";
            AssetDatabase.CreateAsset(recipe, recipePath);

            if (!recipes.Contains(recipe)) recipes.Add(recipe);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return recipe;
        }

        public VoxelAsset BakeSingleRecipe(VoxelModelRecipe recipe, Action<float, string> onProgress = null)
        {
            if (recipe == null || recipe.sourceMesh == null) return null;

            string targetFolder = $"{recipe.GetTargetOutputFolder()}/{recipe.modelName}";
            if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

            VoxelBakeSettings settings = new VoxelBakeSettings
            {
                sourceMesh = recipe.sourceMesh,
                materials = recipe.sourceMaterials,
                voxelSize = recipe.voxelSize,
                autoCalculateVoxelSize = true,
                targetVoxelBudget = Mathf.Clamp(recipe.targetVoxelBudget, 500, 50000),
                paletteColorCount = Mathf.Clamp(recipe.paletteColorCount, 4, 128),
                fillInteriorSolid = recipe.fillInteriorSolid,
                interiorProfile = recipe.interiorProfile,
                chunkSize = recipe.chunkSize,
                assetName = recipe.modelName
            };

            VoxelAsset asset = VoxelBakerCore.Bake(settings, onProgress);
            if (asset != null)
            {
                string assetPath = $"{targetFolder}/{recipe.modelName}.asset";
                string palettePath = $"{targetFolder}/{recipe.modelName}_Palette.asset";

                // 覆盖旧产物：先删除已存在的资产文件，再创建新资产
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(palettePath) != null)
                {
                    AssetDatabase.DeleteAsset(palettePath);
                }
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }

                if (asset.palette != null)
                {
                    AssetDatabase.CreateAsset(asset.palette, palettePath);
                }

                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();

                recipe.bakedAsset = asset;
                recipe.voxelSize = asset.voxelSize;
                recipe.lastBakeTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                recipe.lastBakeDuration = asset.bakeDurationSeconds;
                recipe.lastTotalVoxels = asset.totalOccupiedVoxels;
                recipe.isDirty = false;

                EditorUtility.SetDirty(recipe);
                EditorUtility.SetDirty(this);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return asset;
        }
#endif
    }
}
