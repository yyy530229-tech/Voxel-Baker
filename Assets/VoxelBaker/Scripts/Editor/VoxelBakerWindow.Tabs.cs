using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxelBaker.Baker;
using VoxelBaker.Data;
using VoxelBaker.Runtime;

namespace VoxelBaker.Editor
{
    /// <summary>
    /// VoxelBakerWindow 的各个流水线步骤页 (Tab 0 ~ Tab 7)。
    ///
    /// 原先把 8 个步骤页全塞在 VoxelBakerWindow.cs 里，单文件 1300+ 行，
    /// 找一个步骤要滚很久。这里用 partial class 把它们切出去：
    /// 主文件只留字段、生命周期与整体布局，步骤页各归各位。
    ///
    /// partial class 的字段是共享的，所以这些 Draw 方法可以直接读写窗口状态，
    /// 不需要传参、不需要改成静态 —— 拆分对调用方完全透明。
    /// </summary>
    public partial class VoxelBakerWindow
    {
        #region Tab 0: 工程资产库与配方管理 (Project Asset Manager)
        private void DrawTabProjectDatabase()
        {
            EditorGUILayout.LabelField("📁 体素工程资产库管理 (Project Assets Database)", sectionHeaderStyle);

            EditorGUILayout.BeginVertical(cardStyle);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("🔍 资产检索与分类过滤", EditorStyles.boldLabel);
            if (GUILayout.Button("🔄 扫描并刷新库", GUILayout.Width(110)))
            {
                RefreshDatabase();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            searchQuery = EditorGUILayout.TextField("搜索模型/标签", searchQuery);
            selectedCategoryFilter = EditorGUILayout.Popup(selectedCategoryFilter, categoryFilterNames, GUILayout.Width(180));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("➕ 新建模型配方并进入生产", GUILayout.Height(28)))
            {
                currentRecipe = null;
                sourceGameObject = null;
                sourceMesh = null;
                assetName = $"NewModel_{DateTime.Now:yyyyMMdd_HHmm}";
                currentNavIndex = 1; // 切换至输入阶段
            }
            GUI.backgroundColor = new Color(0.2f, 0.75f, 0.95f);
            if (GUILayout.Button("🚀 一键批量烘焙所有过期/未烘焙配方", GUILayout.Height(28)))
            {
                BatchBakeDirtyRecipes();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // 显示配方列表
            if (projectDb == null || projectDb.recipes == null || projectDb.recipes.Count == 0)
            {
                EditorGUILayout.HelpBox("当前工程库暂无已登记的烘焙配方文件。可点击上方【➕ 新建模型配方】或从左侧选择预设开始生产！", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"已收录配方: {projectDb.recipes.Count} 个", EditorStyles.boldLabel);

            for (int i = 0; i < projectDb.recipes.Count; i++)
            {
                VoxelModelRecipe recipe = projectDb.recipes[i];
                if (recipe == null) continue;

                // 分类与搜索过滤
                if (selectedCategoryFilter > 0)
                {
                    VoxelModelCategory targetCat = (VoxelModelCategory)(selectedCategoryFilter - 1);
                    if (recipe.category != targetCat) continue;
                }

                if (!string.IsNullOrEmpty(searchQuery))
                {
                    bool matchName = recipe.modelName.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchTag = recipe.tags.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!matchName && !matchTag) continue;
                }

                DrawRecipeCard(recipe);
            }
        }

        private void DrawRecipeCard(VoxelModelRecipe recipe)
        {
            EditorGUILayout.BeginVertical(cardStyle);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"📦 <b>{recipe.modelName}</b>  <color=#88bbff>[{recipe.category}]</color>", new GUIStyle(EditorStyles.boldLabel) { richText = true });

            // 状态徽章
            bool isBaked = recipe.bakedAsset != null;
            if (isBaked && !recipe.isDirty)
            {
                GUI.backgroundColor = new Color(0.2f, 0.85f, 0.3f);
                GUILayout.Box("✓ 已烘焙 (Up to date)", EditorStyles.miniButton, GUILayout.Width(140));
            }
            else
            {
                GUI.backgroundColor = new Color(0.95f, 0.6f, 0.1f);
                GUILayout.Box("△ 未烘焙 / 需更新", EditorStyles.miniButton, GUILayout.Width(140));
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField($"• 目标路径: {recipe.GetTargetOutputFolder()}/{recipe.modelName}.asset", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"• 体素参数: 尺寸 {recipe.voxelSize:F3}m | 内部实体: {(recipe.fillInteriorSolid ? "是" : "否")} | 分块: {recipe.chunkSize}³ | 标签: {recipe.tags}", EditorStyles.miniLabel);

            if (recipe.bakedAsset != null)
            {
                EditorGUILayout.LabelField($"• 烘焙产物: 总占据体素 {recipe.lastTotalVoxels:N0} 格 | 耗时 {recipe.lastBakeDuration:F2}s | 时间: {recipe.lastBakeTime}", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("🛠 加载并编辑配方", GUILayout.Height(26)))
            {
                LoadRecipeIntoPipeline(recipe);
                currentNavIndex = 1;
            }

            if (GUILayout.Button("🚀 立即重新烘焙", GUILayout.Height(26)))
            {
                VoxelAsset res = projectDb.BakeSingleRecipe(recipe, (progress, msg) =>
                {
                    EditorUtility.DisplayProgressBar("正在烘焙模型...", msg, progress);
                });
                EditorUtility.ClearProgressBar();
                if (res != null)
                {
                    bakedAsset = res;
                    EditorUtility.DisplayDialog("提示", $"模型 '{recipe.modelName}' 烘焙完成！", "确定");
                }
            }

            if (recipe.bakedAsset != null)
            {
                GUI.backgroundColor = new Color(0.2f, 0.75f, 1f);
                if (GUILayout.Button("👁 3D 预览", GUILayout.Height(26)))
                {
                    bakedAsset = recipe.bakedAsset;
                    currentNavIndex = 6;
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = Color.white;

                if (GUILayout.Button("🌟 场景实例化", GUILayout.Height(26)))
                {
                    InstantiateRecipeInScene(recipe.bakedAsset);
                }
            }

            if (GUILayout.Button("🔍 定位文件", GUILayout.Width(80), GUILayout.Height(26)))
            {
                EditorGUIUtility.PingObject(recipe);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void LoadRecipeIntoPipeline(VoxelModelRecipe recipe)
        {
            if (recipe == null) return;
            currentRecipe = recipe;
            sourceGameObject = recipe.sourcePrefab;
            sourceMesh = recipe.sourceMesh;
            sourceMaterials = recipe.sourceMaterials;
            voxelSize = recipe.voxelSize;
            fillInteriorSolid = recipe.fillInteriorSolid;
            colorTolerance = recipe.colorTolerance;
            interiorProfile = recipe.interiorProfile;
            chunkSize = recipe.chunkSize;
            generateLODs = recipe.generateLODs;
            useAutoVoxelSize = true;
            targetVoxelBudget = recipe.targetVoxelBudget;
            paletteColorCount = recipe.paletteColorCount;
            enableAntiAliasing = recipe.enableAntiAliasing;
            smoothingIterations = Mathf.Clamp(recipe.smoothingIterations, 1, 3);
            supersampleRate = Mathf.Clamp(recipe.supersampleRate, 1, 3);
            assetName = recipe.modelName;
            assetCategory = recipe.category;
            bakedAsset = recipe.bakedAsset;

            RunAnalysis();
        }

        private void BatchBakeDirtyRecipes()
        {
            if (projectDb == null || projectDb.recipes == null) return;

            int count = 0;
            try
            {
                for (int i = 0; i < projectDb.recipes.Count; i++)
                {
                    VoxelModelRecipe r = projectDb.recipes[i];
                    if (r != null && (r.bakedAsset == null || r.isDirty))
                    {
                        float progress = (float)i / projectDb.recipes.Count;
                        projectDb.BakeSingleRecipe(r, (p, msg) =>
                        {
                            EditorUtility.DisplayProgressBar($"批量烘焙进度 ({i + 1}/{projectDb.recipes.Count})", $"正在烘焙: {r.modelName}...", progress);
                        });
                        count++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorUtility.DisplayDialog("批量烘焙完成", $"已成功批量更新烘焙 {count} 个模型资产！", "确定");
        }
        #endregion

        #region Tab 1: 来源与分析
        private void DrawStepSource()
        {
            EditorGUILayout.LabelField("① 输入模型与网格分析 (Source & Analysis)", sectionHeaderStyle);

            EditorGUILayout.BeginVertical(cardStyle);
            EditorGUILayout.LabelField("1. 输入源 (支持场景中的 GameObject、Prefab 或 Mesh 资产)", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            sourceGameObject = (GameObject)EditorGUILayout.ObjectField("场景对象 / Prefab", sourceGameObject, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck() && sourceGameObject != null)
            {
                ExtractModelFromSource(sourceGameObject);
            }

            EditorGUI.BeginChangeCheck();
            sourceMesh = (Mesh)EditorGUILayout.ObjectField("直接指定 Mesh 资产", sourceMesh, typeof(Mesh), false);
            if (EditorGUI.EndChangeCheck() && sourceMesh != null)
            {
                RunAnalysis();
            }

            if (sourceMaterials != null && sourceMaterials.Length > 0)
            {
                EditorGUILayout.LabelField($"已识别材质数量: {sourceMaterials.Length}", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            EditorGUILayout.BeginVertical(cardStyle);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("2. 几何拓扑与合规性诊断", EditorStyles.boldLabel);
            if (GUILayout.Button("重新诊断", GUILayout.Width(80)))
            {
                RunAnalysis();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            if (analysisReport != null)
            {
                EditorGUILayout.HelpBox(analysisReport.diagnosticMessage, analysisReport.canDoSolidVoxelization ? MessageType.Info : MessageType.Warning);

                EditorGUILayout.BeginHorizontal();
                DrawStatBadge("网格封闭性", analysisReport.isWatertight ? "✓ 封闭实体 (Watertight)" : "△ 开放网格 (Open)", analysisReport.isWatertight ? new Color(0.2f, 0.85f, 0.3f) : new Color(0.95f, 0.65f, 0.1f));
                DrawStatBadge("顶点数", analysisReport.vertexCount.ToString("N0"), Color.cyan);
                DrawStatBadge("三角形数", analysisReport.triangleCount.ToString("N0"), Color.cyan);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                DrawStatBadge("UV0 贴图坐标", analysisReport.hasUV0 ? "✓ 具备" : "✗ 缺失", analysisReport.hasUV0 ? Color.green : Color.yellow);
                DrawStatBadge("预估体素总量", $"~{analysisReport.estimatedOccupiedVoxels:N0}", new Color(1f, 0.4f, 0.8f));
                DrawStatBadge("预计显存占用", $"{analysisReport.estimatedMemoryMB:F2} MB", Color.white);
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("请在上方指定模型或点击左侧快速预设，系统将自动进行合规性诊断与参数估算！", MessageType.None);
            }
            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Tab 2: 尺寸与几何
        private void DrawStepGeometry()
        {
            EditorGUILayout.LabelField("② 网格尺寸与实体化填充 (Geometry & Solid Voxelization)", sectionHeaderStyle);

            EditorGUILayout.BeginVertical(cardStyle);
            EditorGUILayout.LabelField("1. 体素总数量快捷预设 (Voxel Count Presets)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("【参数作用】：一键选择您希望该关卡生成的方块总数量。方块数量直接决定消除手感、难度和视觉颗粒度。", MessageType.None);
            EditorGUILayout.Space(4);

            float maxDim = sourceMesh != null ? Mathf.Max(sourceMesh.bounds.size.x, Mathf.Max(sourceMesh.bounds.size.y, sourceMesh.bounds.size.z)) : 2.0f;
            if (maxDim <= 0.001f) maxDim = 2.0f;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("⚡ 轻量复古\n(~3,000 格)", GUILayout.Height(40)))
            {
                useAutoVoxelSize = true;
                targetVoxelBudget = 3000;
            }
            if (GUILayout.Button("🎯 标准乐高\n(~6,000 格)", GUILayout.Height(40)))
            {
                useAutoVoxelSize = true;
                targetVoxelBudget = 6000;
            }
            if (GUILayout.Button("💎 高清细腻\n(~10,000 格)", GUILayout.Height(40)))
            {
                useAutoVoxelSize = true;
                targetVoxelBudget = 10000;
            }
            GUI.backgroundColor = new Color(1f, 0.8f, 0.2f);
            if (GUILayout.Button("🌟 超高清极细腻\n(~16,000 格)", GUILayout.Height(40)))
            {
                useAutoVoxelSize = true;
                targetVoxelBudget = 16000;
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("2. 详细尺寸与颗粒度微调", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            useAutoVoxelSize = EditorGUILayout.Toggle(new GUIContent("自动按块数预算推导体素尺寸 (推荐)", "开启后由下方预算自动推算体素边长；关闭则手动指定下方边长。"), useAutoVoxelSize);

            if (useAutoVoxelSize)
            {
                targetVoxelBudget = EditorGUILayout.IntSlider(new GUIContent("目标体素总块数预算", "总方块数越少越简洁复古，越多越细腻。乐高风格推荐 3,000 ~ 10,000。"), targetVoxelBudget, 1000, 30000);
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                voxelSize = EditorGUILayout.Slider(new GUIContent("单个体素颗粒边长 (Voxel Size)", "手动指定每个小方块的物理尺寸（米）。\n• 调小 (如 0.04)：细节丰富、颗粒超细、总方块数多。\n• 调大 (如 0.12)：复古像素大方块、总方块数少、消除通关极快。"), voxelSize, maxDim / 70.0f, maxDim / 8.0f);
                if (EditorGUI.EndChangeCheck())
                {
                    useAutoVoxelSize = false;
                }
            }

            if (analysisReport != null && GUILayout.Button("🎯 自动计算并应用推荐的最佳体素粒度", GUILayout.Height(26)))
            {
                useAutoVoxelSize = true;
                targetVoxelBudget = 6000;
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("3. 内部实体化填充", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            fillInteriorSolid = EditorGUILayout.Toggle(new GUIContent("启用 3D 边界泛洪实体填充 (Solid Fill)", "" +
                "【块数花在哪，直接决定画面有多高清】\n\n" +
                "同样 6000 块预算，实心 vs 单层壳：\n" +
                "• 实心 (勾选)：约 2/3 的块埋在永远看不见的内部 → 表面只剩 ~2000 块，长轴 ~38 格，明显偏粗。\n" +
                "• 单层壳 (不勾选)：每一块都用在看得见的表面上 → 可见 6000 块，长轴 ~67 格，细腻得多。\n\n" +
                "手感差异：实心在破坏时能一层层向内剥落；单层壳打穿就是透空。\n" +
                "追求「低块数 + 高清」时请取消勾选。"), fillInteriorSolid);

            EditorGUILayout.Space(6);
            int estGridX = Mathf.CeilToInt(maxDim / voxelSize);
            string fillTip = fillInteriorSolid
                ? "实心填充：预算约 2/3 埋在内部，表面分辨率偏低"
                : "单层壳：预算 100% 用于可见表面，分辨率最高";
            EditorGUILayout.HelpBox($"【实时估算】：目标块数预算 {targetVoxelBudget:N0} 格（{DensityName(targetVoxelBudget)}），长轴约 {estGridX} 格。\n{fillTip}\n外观采用乐高平色块 + 棱边高光砖块渲染。", MessageType.Info);
            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Tab 3: 外观
        private void DrawStepAppearance()
        {
            EditorGUILayout.LabelField("③ 外观采样与调色板生成 (Appearance & Palette)", sectionHeaderStyle);

            EditorGUILayout.BeginVertical(cardStyle);
            EditorGUILayout.LabelField("纹理采样与调色板设置", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            colorTolerance = EditorGUILayout.Slider(new GUIContent("颜色合并容差 (Color Tolerance)", "相近颜色的聚类合并阈值，用于优化调色板空间。"), colorTolerance, 0f, 15f);

            paletteColorCount = EditorGUILayout.IntSlider(new GUIContent("乐高平色块数量 (Flat Color Count)", "表面体素量化压平后的纯色块数量。越少越像乐高平色块，越多越接近原始贴图。推荐 24 ~ 48。"), paletteColorCount, 4, 128);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("轮廓抗锯齿 (Silhouette Anti-Aliasing)", EditorStyles.boldLabel);

            enableAntiAliasing = EditorGUILayout.Toggle(new GUIContent("开启形态学平滑", "对体素壳层做闭运算(填补单格孔洞与断缝) + 开运算(削掉孤立毛刺)，\n直接消除体素轮廓上的锯齿与碎点，是「低块数也能细腻」的核心手段。"), enableAntiAliasing);

            using (new EditorGUI.DisabledScope(!enableAntiAliasing))
            {
                smoothingIterations = EditorGUILayout.IntSlider(new GUIContent("平滑力度 (Smoothing)", "平滑迭代次数。每多一次就多抹掉一格宽的凹凸。\n• 1（推荐）：只填掉单格锯齿缺口，耳朵/爪子这类细小结构保得住，这才是「细腻」。\n• 2：轮廓更干净，但会吃掉约 2 格宽的细节。\n• 3：极致圆润，细小结构大概率被糊掉。\n\n注：体素风格的阶梯感是要保留的风格本体，这里做的是「修毛刺」不是「磨平」。" ), smoothingIterations, 1, 3);
            }

            supersampleRate = EditorGUILayout.IntSlider(new GUIContent("表面超采样 (Supersampling)", "把每个格子再细分成 N×N×N 个子盒来统计表面覆盖率：\n1=整格单点(最快,轮廓最硬)\n2=2×2×2=8 子盒\n3=3×3×3=27 子盒(最细腻,轮廓过渡最自然)\n子盒并集严格等于原格子，因此不会让模型膨胀。"), supersampleRate, 1, 3);

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("【采样机制】：\n• 表面体素：基于三角面重心坐标插值 UV0 实时采样 Albedo 贴图，100% 保真还原原美术材质！\n• K-Means 量化：将数千种近似颜色压平为少量纯色块，实现乐高积木般的干净色块外观，彻底消除色斑噪声。\n• 无贴图降级：自动使用 SubMesh 材质固有色或顶点色。\n• 自动生成紧凑 64x64 调色板纹理（支持 4,096 种材质语义，同时体素内嵌 32 位 RGBA 支持 1677 万全真彩）。", MessageType.Info);
            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Tab 4: 内部
        private void DrawStepInterior()
        {
            EditorGUILayout.LabelField("④ 内部配方与层级结构 (Interior Profile Recipe)", sectionHeaderStyle);

            EditorGUILayout.BeginVertical(cardStyle);
            EditorGUILayout.LabelField("内部结构配方 (ScriptableObject 配置)", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            interiorProfile = (VoxelInteriorProfile)EditorGUILayout.ObjectField("内部配方资产 (Profile)", interiorProfile, typeof(VoxelInteriorProfile), false);

            if (interiorProfile == null)
            {
                if (GUILayout.Button("➕ 创建默认多层蛋糕配方 (表层粉色 / 中层青色 / 深层绿色)", GUILayout.Height(28)))
                {
                    VoxelInteriorProfile newProfile = ScriptableObject.CreateInstance<VoxelInteriorProfile>();
                    string dir = "Assets/VoxelAssets";
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    AssetDatabase.CreateAsset(newProfile, $"{dir}/DefaultInteriorProfile.asset");
                    AssetDatabase.SaveAssets();
                    interiorProfile = newProfile;
                }
            }
            else
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField($"当前策略: {interiorProfile.strategy}", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox($"已定义 {interiorProfile.layerRules.Count} 个深度分层：\n当外层体素被击碎剥落时，将逐层露出次表面、果肉层与核心骨架！", MessageType.Info);
            }
            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Tab 5: 优化
        private void DrawStepOptimization()
        {
            EditorGUILayout.LabelField("⑤ 空间分块与预烘焙优化 (AO & Chunks & LOD)", sectionHeaderStyle);

            EditorGUILayout.BeginVertical(cardStyle);
            EditorGUILayout.LabelField("预烘焙加速与剔除设置", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            chunkSize = EditorGUILayout.IntPopup("Chunk 分块尺寸", chunkSize, new string[] { "16x16x16 (推荐，适合精细局部破坏与更新)", "32x32x32 (适合超大规模静态体素)" }, new int[] { 16, 32 });
            generateLODs = EditorGUILayout.Toggle("自动生成 LOD1 / LOD2 (下采样)", generateLODs);

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("【预烘焙优化】：\n1. 26 邻居 AO (环境光遮蔽)：预烘焙暗角阴影，方块立体感极佳。\n2. 6-Face 暴露掩码：未暴露的内部体素不进入 GPU 渲染队列，初始渲染集仅提交表面可见方块，杜绝 Overdraw 浪费！", MessageType.Info);
            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Tab 6: 预览
        private void DrawStepPreview()
        {
            EditorGUILayout.LabelField("⑥ 3D 视图 (参数预览 / 资产检查)", sectionHeaderStyle);

            EditorGUILayout.BeginVertical(cardStyle);
            int newMode = GUILayout.SelectionGrid(previewTabMode, previewTabModeNames, 2, EditorStyles.miniButton);
            if (newMode != previewTabMode)
            {
                previewTabMode = newMode;
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndVertical();

            if (previewTabMode == 0)
                DrawLiveParameterPreview();
            else
                DrawBakedAssetInspector();
        }
        #endregion

        #region Tab 6-A: 参数驱动的实时体素化预览
        private void DrawLiveParameterPreview()
        {
            if (previewPanel == null) return;

            // 每帧同步参数，面板内部自己比对差异 —— 没变就不重建
            SyncPreviewRequest();
            previewPanel.Sync(sourceMesh, sourceMaterials, previewRequestTemplate);
            previewPanel.Update();

            EditorGUILayout.BeginVertical(cardStyle);
            EditorGUILayout.LabelField("实时体素化预览 (后台线程计算，不阻塞编辑器)", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            if (sourceMesh == null)
            {
                EditorGUILayout.HelpBox(
                    "还没有来源模型。\n请到步骤 ① 指定一个带 MeshFilter 的模型 —— 之后改动 ②③④ 里的任何参数，这里都会按新参数实时重新体素化。",
                    MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            Rect viewport = EditorGUILayout.GetControlRect(false, 360f, GUILayout.ExpandWidth(true));
            previewPanel.Draw(viewport);

            EditorGUILayout.Space(8);
            DrawPreviewStats();

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "· 左键拖动旋转，滚轮缩放\n" +
                "· 拖动参数时出「草稿档」(单点采样 + 小网格)，跟手优先\n" +
                "· 松手 0.4 秒后自动出「成品档」，超采样与块数标定与正式烘焙完全一致\n" +
                "· 这里显示的块数、轮廓、配色，就是点步骤 ⑦ 烘焙后拿到的结果",
                MessageType.Info);

            EditorGUILayout.EndVertical();
        }

        private void SyncPreviewRequest()
        {
            //
            // 刻意与步骤 ⑦ 组装 VoxelBakeSettings 时取同一组字段、同样的 Clamp，
            // 保证"预览看到什么，烘焙就得到什么"。
            //
            previewRequestTemplate.TargetModelHeight = targetModelHeight;
            previewRequestTemplate.TargetVoxelBudget = Mathf.Clamp(targetVoxelBudget, 500, 50000);
            previewRequestTemplate.ManualVoxelSize = useAutoVoxelSize ? 0f : voxelSize;
            previewRequestTemplate.FillStrategy = fillInteriorSolid
                ? VoxelFillStrategy.SolidCore
                : VoxelFillStrategy.SurfaceShellOnly;
            previewRequestTemplate.ShellThickness = 2; // 窗口未暴露该参数，与 VoxelBakeSettings 默认值一致
            previewRequestTemplate.AntiAliasing = enableAntiAliasing;
            previewRequestTemplate.SmoothingIterations = smoothingIterations;
            previewRequestTemplate.SupersampleRate = supersampleRate;
            previewRequestTemplate.PaletteColorCount = Mathf.Clamp(paletteColorCount, 4, 128);
            previewRequestTemplate.PaletteTolerance = 24f;
        }

        private void DrawPreviewStats()
        {
            VoxelPreviewResult r = previewPanel != null ? previewPanel.Result : null;
            bool building = previewPanel != null && previewPanel.IsBuilding;

            EditorGUILayout.BeginVertical(cardStyle);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("预览统计", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("重新计算", GUILayout.Width(80)))
            {
                previewPanel?.ForceRebuild();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            if (r == null)
            {
                EditorGUILayout.LabelField(building ? "正在后台体素化…" : "等待参数…", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            if (!string.IsNullOrEmpty(r.ErrorMessage))
            {
                EditorGUILayout.HelpBox("构建失败：" + r.ErrorMessage, MessageType.Error);
                EditorGUILayout.EndVertical();
                return;
            }

            if (!r.IsValid)
            {
                EditorGUILayout.LabelField(building ? "正在后台体素化…" : "模型为空或没有产生任何体素",
                    EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            int budget = Mathf.Clamp(targetVoxelBudget, 500, 50000);
            float deviation = (r.TotalVoxels - budget) * 100f / budget;
            float visibleRatio = r.TotalVoxels > 0 ? r.VisibleVoxels * 100f / r.TotalVoxels : 0f;

            EditorGUILayout.LabelField("总块数", $"{r.TotalVoxels:N0}   (目标 {budget:N0}，偏差 {deviation:+#;-#;0}%)");
            EditorGUILayout.LabelField("可见块数", $"{r.VisibleVoxels:N0} / {r.TotalVoxels:N0}   ({visibleRatio:F0}%)");
            EditorGUILayout.LabelField("体素尺寸", $"{r.VoxelSize:F4} m");
            EditorGUILayout.LabelField("网格尺寸", $"{r.GridDimensions.x} × {r.GridDimensions.y} × {r.GridDimensions.z}");
            EditorGUILayout.LabelField("调色板", $"{r.PaletteColorCount} 色");
            EditorGUILayout.LabelField("构建耗时",
                $"{r.BuildMilliseconds:F0} ms" + (building ? "   (正在出更高精度档…)" : ""));

            if (r.GridWasCapped)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    "预览网格被安全阀限流，实际块数会低于预算。正式烘焙的网格上限更高，结果会更接近目标值。",
                    MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Tab 6-B: 已烘焙资产检查 (Scene 视图切片诊断)
        private void DrawBakedAssetInspector()
        {
            EditorGUILayout.BeginVertical(cardStyle);
            EditorGUILayout.LabelField("1. 选择预览的体素资产 (Voxel Asset)", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            bakedAsset = (VoxelAsset)EditorGUILayout.ObjectField("当前预览资产", bakedAsset, typeof(VoxelAsset), false);

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("快捷切换:", GUILayout.Width(70));
            if (projectDb != null && projectDb.recipes != null)
            {
                for (int i = 0; i < projectDb.recipes.Count; i++)
                {
                    var r = projectDb.recipes[i];
                    if (r != null && r.bakedAsset != null)
                    {
                        if (GUILayout.Button(r.modelName, GUILayout.Height(22)))
                        {
                            bakedAsset = r.bakedAsset;
                            SceneView.RepaintAll();
                        }
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("2. 可视化检查与剖面切片工具", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            selectedPreviewModeIndex = EditorGUILayout.Popup("预览显示模式", selectedPreviewModeIndex, previewModeNames);
            previewMode = (VoxelPreviewMode)selectedPreviewModeIndex;

            EditorGUILayout.Space(6);
            enableSlicePlane = EditorGUILayout.Toggle("启用 3D 剖面切片 (Slice Plane)", enableSlicePlane);
            if (enableSlicePlane)
            {
                sliceNormal = EditorGUILayout.Vector3Field("剖面法线方向 (Normal)", sliceNormal);
                sliceOffset = EditorGUILayout.Slider("剖面距离偏移 (Offset)", sliceOffset, -5f, 5f);
            }
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(8);
            if (bakedAsset != null)
            {
                EditorGUILayout.HelpBox($"✓ 当前预览资产: {bakedAsset.name}\n总占据体素: {bakedAsset.totalOccupiedVoxels:N0} (表面: {bakedAsset.totalSurfaceVoxels:N0}, 内部实体: {bakedAsset.totalInteriorVoxels:N0})\n初始 GPU 可见渲染实例: {bakedAsset.totalVisibleVoxels:N0}", MessageType.Info);

                EditorGUILayout.Space(6);
                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = new Color(0.2f, 0.75f, 1f);
                if (GUILayout.Button("🌟 一键实例化至场景 (自动替换)", GUILayout.Height(32)))
                {
                    InstantiateRecipeInScene(bakedAsset);
                }
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("🧹 清空场景已有体素对象", GUILayout.Height(32)))
                {
                    VoxelModelInstance[] existing = UnityEngine.Object.FindObjectsOfType<VoxelModelInstance>();
                    foreach (var inst in existing)
                    {
                        if (inst != null && inst.gameObject != null)
                        {
                            Undo.DestroyObjectImmediate(inst.gameObject);
                        }
                    }
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("请在上方选择一个已烘焙的体素资产，或点击左侧快速预设后在步骤 ⑦ 执行烘焙！", MessageType.Warning);
            }
            EditorGUILayout.EndVertical();
        }
        #endregion

        #region Tab 7: 烘焙与工程登记
        private void DrawStepBakeExport()
        {
            EditorGUILayout.LabelField("⑦ 一键烘焙与资产归档 (Full Pipeline Bake & Export)", sectionHeaderStyle);

            EditorGUILayout.BeginVertical(cardStyle);
            EditorGUILayout.LabelField("工程归档配置", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            assetName = EditorGUILayout.TextField("模型名称 (Asset Name)", assetName);
            assetCategory = (VoxelModelCategory)EditorGUILayout.EnumPopup("所属工程分类 (Category)", assetCategory);

            string targetFolder = $"Assets/VoxelAssets/{assetCategory}/{assetName}";
            EditorGUILayout.LabelField($"归档目录: {targetFolder}", EditorStyles.miniLabel);

            EditorGUILayout.Space(12);
            GUI.backgroundColor = new Color(0.2f, 0.85f, 0.45f);
            if (GUILayout.Button("🚀 START FULL PIPELINE BAKE (开始全链路一键烘焙)", GUILayout.Height(46)))
            {
                PerformFullBake();
            }
            GUI.backgroundColor = Color.white;

            if (bakedAsset != null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox($"🎉 烘焙完成！已自动登记归档至工程库。\n资产名称: {bakedAsset.name}\n分类: {assetCategory}\n总占据体素: {bakedAsset.totalOccupiedVoxels:N0}\n初始 GPU 可见实例: {bakedAsset.totalVisibleVoxels:N0}\n耗时: {bakedAsset.bakeDurationSeconds:F2} 秒", MessageType.Info);

                if (GUILayout.Button("🌟 一键在当前场景生成体素游戏对象 (Instantiate)", GUILayout.Height(34)))
                {
                    InstantiateRecipeInScene(bakedAsset);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void PerformFullBake()
        {
            if (sourceMesh == null)
            {
                EditorUtility.DisplayDialog("提示", "请先在步骤 ① 中指定 Source Mesh 或从左侧选择一个测试模型预设！", "确定");
                return;
            }

            try
            {
                string targetFolder = $"Assets/VoxelAssets/{assetCategory}/{assetName}";
                if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

                VoxelBakeSettings settings = new VoxelBakeSettings
                {
                    sourceMesh = sourceMesh,
                    materials = sourceMaterials,
                    voxelSize = voxelSize,
                    autoCalculateVoxelSize = useAutoVoxelSize,
                    targetVoxelBudget = Mathf.Clamp(targetVoxelBudget, 500, 50000),
                    paletteColorCount = Mathf.Clamp(paletteColorCount, 4, 128),
                    paletteTolerance = 24f,
                    enableAntiAliasing = enableAntiAliasing,
                    smoothingIterations = smoothingIterations,
                    supersampleRate = supersampleRate,
                    targetModelHeight = targetModelHeight,
                    fillInteriorSolid = fillInteriorSolid,
                    interiorProfile = interiorProfile,
                    chunkSize = chunkSize,
                    assetName = assetName
                };

                bakedAsset = VoxelBakerCore.Bake(settings, (progress, msg) =>
                {
                    EditorUtility.DisplayProgressBar("体素烘焙进行中...", msg, progress);
                });

                if (bakedAsset != null)
                {
                    string assetPath = $"{targetFolder}/{assetName}.asset";
                    string palettePath = $"{targetFolder}/{assetName}_Palette.asset";

                    // 覆盖旧产物：先删除已存在的资产文件，再创建新资产
                    if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(palettePath) != null)
                    {
                        AssetDatabase.DeleteAsset(palettePath);
                    }
                    if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                    {
                        AssetDatabase.DeleteAsset(assetPath);
                    }

                    if (bakedAsset.palette != null)
                    {
                        AssetDatabase.CreateAsset(bakedAsset.palette, palettePath);
                    }

                    AssetDatabase.CreateAsset(bakedAsset, assetPath);

                    // 自动登记/更新 Recipe 到工程数据库
                    if (currentRecipe == null)
                    {
                        currentRecipe = ScriptableObject.CreateInstance<VoxelModelRecipe>();
                        string recipePath = $"{targetFolder}/{assetName}_Recipe.asset";
                        AssetDatabase.CreateAsset(currentRecipe, recipePath);
                    }

                    currentRecipe.modelName = assetName;
                    currentRecipe.category = assetCategory;
                    currentRecipe.sourcePrefab = sourceGameObject;
                    currentRecipe.sourceMesh = sourceMesh;
                    currentRecipe.sourceMaterials = sourceMaterials;
                    currentRecipe.voxelSize = bakedAsset.voxelSize;
                    currentRecipe.fillInteriorSolid = fillInteriorSolid;
                    currentRecipe.colorTolerance = colorTolerance;
                    currentRecipe.interiorProfile = interiorProfile;
                    currentRecipe.chunkSize = chunkSize;
                    currentRecipe.generateLODs = generateLODs;
                    currentRecipe.targetVoxelBudget = targetVoxelBudget;
                    currentRecipe.paletteColorCount = paletteColorCount;
                    currentRecipe.enableAntiAliasing = enableAntiAliasing;
                    currentRecipe.smoothingIterations = smoothingIterations;
                    currentRecipe.supersampleRate = supersampleRate;
                    currentRecipe.bakedAsset = bakedAsset;
                    currentRecipe.lastBakeTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    currentRecipe.lastBakeDuration = bakedAsset.bakeDurationSeconds;
                    currentRecipe.lastTotalVoxels = bakedAsset.totalOccupiedVoxels;
                    currentRecipe.isDirty = false;

                    EditorUtility.SetDirty(currentRecipe);

                    RefreshDatabase();

                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();

                    VoxelScenePreview.ClearCache();
                    EditorGUIUtility.PingObject(bakedAsset);
                    currentNavIndex = 6; // 自动跳转至 3D 预览
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void InstantiateRecipeInScene(VoxelAsset asset)
        {
            if (asset == null) return;

            // 彻底清理场景中原有的旧体素模型游戏对象，绝不发生多个模型重叠！
            VoxelModelInstance[] existing = UnityEngine.Object.FindObjectsOfType<VoxelModelInstance>();
            foreach (var inst in existing)
            {
                if (inst != null && inst.gameObject != null)
                {
                    Undo.DestroyObjectImmediate(inst.gameObject);
                }
            }

            GameObject go = new GameObject($"VoxelTargetModel_{asset.name}");
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            VoxelModelInstance instance = go.AddComponent<VoxelModelInstance>();
            instance.voxelAsset = asset;
            instance.InitializeModel();
            Selection.activeGameObject = go;
            Undo.RegisterCreatedObjectUndo(go, "Instantiate Voxel Model");

            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }
        }
        #endregion
    }
}
