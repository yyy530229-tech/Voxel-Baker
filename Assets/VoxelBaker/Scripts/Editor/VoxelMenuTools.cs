using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxelBaker.Baker;
using VoxelBaker.Data;

namespace VoxelBaker.Editor
{
    /// <summary>
    /// 体素烘焙通用编辑器菜单 (Pure Generic Voxel Baker Tools)
    /// 职责：提供纯净的烘焙工具箱入口与对选定 3D 资产的一键烘焙，不包含任何业务预设或游戏关卡逻辑
    /// </summary>
    public static class VoxelMenuTools
    {
        [MenuItem("Tools/Voxel Baker/📦 烘焙选中的 3D 模型 (Bake Selected 3D Model)", false, 1)]
        public static void BakeSelectedModel()
        {
            GameObject selectedGo = Selection.activeGameObject;
            if (selectedGo == null)
            {
                EditorUtility.DisplayDialog("提示", "请先在 Project 或 Hierarchy 中选中一个 3D 模型预制体或游戏对象 (FBX / Prefab / OBJ)！", "确定");
                return;
            }

            MeshFilter mf = selectedGo.GetComponentInChildren<MeshFilter>();
            SkinnedMeshRenderer smr = selectedGo.GetComponentInChildren<SkinnedMeshRenderer>();
            Renderer ren = selectedGo.GetComponentInChildren<Renderer>();

            Mesh mesh = mf != null ? mf.sharedMesh : (smr != null ? smr.sharedMesh : null);
            Material[] mats = ren != null ? ren.sharedMaterials : null;

            string assetPathSelected = AssetDatabase.GetAssetPath(selectedGo);
            if ((mats == null || mats.Length == 0 || mats[0] == null) && !string.IsNullOrEmpty(assetPathSelected))
            {
                string dir = Path.GetDirectoryName(assetPathSelected).Replace('\\', '/');
                string[] searchDirs = new string[] { dir, dir + "/Materials" };
                string[] matGuids = AssetDatabase.FindAssets("t:Material", searchDirs);
                List<Material> foundMats = new List<Material>();
                foreach (var guid in matGuids)
                {
                    string mPath = AssetDatabase.GUIDToAssetPath(guid);
                    Material existingMat = AssetDatabase.LoadAssetAtPath<Material>(mPath);
                    if (existingMat != null && !foundMats.Contains(existingMat))
                    {
                        foundMats.Add(existingMat);
                    }
                }
                if (foundMats.Count > 0) mats = foundMats.ToArray();
            }

            if (mesh == null)
            {
                EditorUtility.DisplayDialog("错误", $"选中的对象 '{selectedGo.name}' 未包含有效的 Mesh 数据！", "确定");
                return;
            }

            string modelName = $"VoxelModel_{selectedGo.name.Replace(" ", "_")}";
            string targetFolder = $"Assets/VoxelAssets/General/{modelName}";
            if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

            EditorUtility.DisplayProgressBar("体素烘焙中...", $"正在烘焙模型 '{selectedGo.name}'...", 0.4f);

            // 乐高风格：按块数预算自动推导体素尺寸 (默认标准乐高 ~6,000 格)
            VoxelBakeSettings settings = new VoxelBakeSettings
            {
                sourceMesh = mesh,
                materials = mats,
                autoCalculateVoxelSize = true,
                targetVoxelBudget = 6000,
                paletteColorCount = 32,
                fillInteriorSolid = true,
                chunkSize = 16,
                assetName = modelName
            };

            VoxelAsset asset = VoxelBakerCore.Bake(settings);
            EditorUtility.ClearProgressBar();

            if (asset != null)
            {
                string assetPath = $"{targetFolder}/{modelName}.asset";
                string palettePath = $"{targetFolder}/{modelName}_Palette.asset";
                string recipePath = $"{targetFolder}/{modelName}_Recipe.asset";

                if (asset.palette != null) AssetDatabase.CreateAsset(asset.palette, palettePath);
                AssetDatabase.CreateAsset(asset, assetPath);

                VoxelModelRecipe recipe = ScriptableObject.CreateInstance<VoxelModelRecipe>();
                recipe.modelName = modelName;
                recipe.category = VoxelModelCategory.General;
                recipe.sourcePrefab = selectedGo;
                recipe.sourceMesh = mesh;
                recipe.sourceMaterials = mats;
                recipe.voxelSize = asset.voxelSize;
                recipe.fillInteriorSolid = true;
                recipe.chunkSize = 16;
                recipe.targetVoxelBudget = 6000;
                recipe.paletteColorCount = 32;
                recipe.bakedAsset = asset;
                recipe.lastBakeTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                recipe.lastBakeDuration = asset.bakeDurationSeconds;
                recipe.lastTotalVoxels = asset.totalOccupiedVoxels;
                recipe.isDirty = false;

                AssetDatabase.CreateAsset(recipe, recipePath);

                VoxelProjectDatabase db = VoxelProjectDatabase.GetOrCreateDatabase();
                db.ScanAndRefreshRecipes();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                VoxelScenePreview.ClearCache();
                EditorGUIUtility.PingObject(asset);
                EditorUtility.DisplayDialog("体素烘焙完成", $"成功将模型 '{selectedGo.name}' 烘焙为通用体素资产！\n总占据体素: {asset.totalOccupiedVoxels:N0} 格\n文件路径: {assetPath}", "确定");
            }
        }

        [MenuItem("Tools/Voxel Baker/🔄 扫描并刷新工程配方库 (Refresh Project Database)", false, 20)]
        public static void RefreshDatabase()
        {
            VoxelProjectDatabase db = VoxelProjectDatabase.GetOrCreateDatabase();
            if (db != null)
            {
                db.ScanAndRefreshRecipes();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("提示", $"工程体素配方库已成功同步并刷新！当前已归档 {db.recipes.Count} 个配方。", "确定");
            }
        }

        /// <summary>
        /// 批量重新烘焙全部已登记配方 (可被 -executeMethod 命令行调用)
        /// 使用配方自身的块数预算与平色块数量设置，输出新的体素资产。
        /// </summary>
        public static void BatchRebakeAll()
        {
            VoxelProjectDatabase db = VoxelProjectDatabase.GetOrCreateDatabase();
            if (db == null)
            {
                UnityEngine.Debug.LogError("[VoxelMenuTools] 无法加载体素工程数据库！");
                return;
            }

            db.ScanAndRefreshRecipes();

            int count = 0;
            for (int i = 0; i < db.recipes.Count; i++)
            {
                VoxelModelRecipe r = db.recipes[i];
                if (r == null || r.sourceMesh == null) continue;

                UnityEngine.Debug.Log($"[VoxelMenuTools] 开始重新烘焙: {r.modelName} (预算 {r.targetVoxelBudget:N0} 格, 平色块 {r.paletteColorCount})...");

                VoxelAsset res = db.BakeSingleRecipe(r, null);
                if (res != null)
                {
                    count++;
                    UnityEngine.Debug.Log($"[VoxelMenuTools] ✓ {r.modelName} 完成: {res.totalOccupiedVoxels:N0} 格 (表面 {res.totalSurfaceVoxels:N0}, 内部 {res.totalInteriorVoxels:N0}), voxelSize={res.voxelSize:F4}, 耗时 {res.bakeDurationSeconds:F2}s");
                }
                else
                {
                    UnityEngine.Debug.LogError($"[VoxelMenuTools] ✗ {r.modelName} 烘焙失败");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            UnityEngine.Debug.Log($"[VoxelMenuTools] 批量重新烘焙完成: {count} 个配方");
        }
    }
}
