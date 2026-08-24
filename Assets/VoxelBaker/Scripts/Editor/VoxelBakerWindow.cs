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
    public class VoxelBakerWindow : EditorWindow
    {
        [MenuItem("Tools/Voxel Baker/体素烘焙生产工具箱 (Pipeline Tool)", false, 0)]
        public static void OpenWindow()
        {
            VoxelBakerWindow window = GetWindow<VoxelBakerWindow>("体素烘焙工作室");
            window.minSize = new Vector2(760, 780);
            window.Show();
        }

        public static void OpenWindowWithAsset(VoxelAsset asset)
        {
            VoxelBakerWindow window = GetWindow<VoxelBakerWindow>("体素烘焙工作室");
            window.bakedAsset = asset;
            window.currentNavIndex = 6; // 自动跳转至 3D 预览
            window.Show();
        }

        public static void OpenWindowWithRecipe(VoxelModelRecipe recipe)
        {
            VoxelBakerWindow window = GetWindow<VoxelBakerWindow>("体素烘焙工作室");
            window.LoadRecipeIntoPipeline(recipe);
            window.currentNavIndex = 1; // 切换至 输入与诊断
            window.Show();
        }

        // 左侧导航阶段名称
        private readonly string[] navTitles = new string[]
        {
            "📁 工程资产库 (Project Database)",
            "① 输入与诊断 (模型分析)",
            "② 尺寸与网格 (实体化填充)",
            "③ 外观与采样 (贴图调色板)",
            "④ 内部结构 (多层配方)",
            "⑤ 空间优化 (AO/分块/LOD)",
            "⑥ 3D 视图 (实时切片预览)",
            "⑦ 一键烘焙 (资产导出)"
        };
        private int currentNavIndex = 0; // 默认停留在 工程资产库

        // 3D 预览模式中文显示映射
        private readonly string[] previewModeNames = new string[]
        {
            "原始模型 (Original Mesh)",
            "仅表面体素 (Surface Only)",
            "实体占用分布 (Solid Occupancy)",
            "表面距离场 (Distance Field - SDF)",
            "深度视觉分层 (Layer Classification)",
            "调色板颜色 (Palette Color)",
            "环境光遮蔽 (Ambient Occlusion - AO)",
            "6面暴露掩码 (Face Mask)",
            "Chunk 分块空间包围盒 (Chunk Bounds)",
            "LOD 下采样分级视图 (LOD View)"
        };

        // 工程资产库过滤与搜索
        private VoxelProjectDatabase projectDb;
        private string searchQuery = "";
        private int selectedCategoryFilter = 0;
        private readonly string[] categoryFilterNames = new string[]
        {
            "全部 (All)",
            "角色/怪物 (Characters)",
            "建筑/房屋 (Buildings)",
            "场景道具 (Props)",
            "障碍物/靶子 (Obstacles)",
            "食物/蛋糕 (Food)",
            "武器/装备 (Weapons)",
            "通用常规 (General)"
        };

        // 当前正在编辑的配方
        private VoxelModelRecipe currentRecipe;

        // 阶段 1: 来源
        private GameObject sourceGameObject;
        private Mesh sourceMesh;
        private Material[] sourceMaterials;
        private MeshAnalysisReport analysisReport;

        // 阶段 2: 几何 (统一为标准预设尺寸：高度 3.0m，体素 0.22m，与小黄鸭/房子预设 100% 一模一样大)
        private float targetModelHeight = 3.0f;
        private float voxelSize = 0.22f;
        private bool fillInteriorSolid = true;

        // 阶段 3: 外观
        private float colorTolerance = 4f;

        // 阶段 4: 内部
        private VoxelInteriorProfile interiorProfile;

        // 阶段 5: 优化
        private int chunkSize = 16;
        private bool generateLODs = true;

        // 阶段 6: 预览
        private int selectedPreviewModeIndex = 5; // 默认为 调色板颜色
        private VoxelPreviewMode previewMode = VoxelPreviewMode.PaletteColor;
        private bool enableSlicePlane = false;
        private Vector3 sliceNormal = new Vector3(0, 0, 1);
        private float sliceOffset = 0f;

        // 阶段 7: 输出与分类
        private string assetName = "VoxelModel_Duck";
        private VoxelModelCategory assetCategory = VoxelModelCategory.General;
        private VoxelAsset bakedAsset;

        private Vector2 leftScroll;
        private Vector2 rightScroll;
        private Vector2 dbListScroll;

        // 自定义 UI 样式
        private GUIStyle headerTitleStyle;
        private GUIStyle headerSubStyle;
        private GUIStyle cardStyle;
        private GUIStyle sectionHeaderStyle;
        private GUIStyle navButtonStyle;
        private GUIStyle navButtonActiveStyle;
        private bool stylesInitialized = false;

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            RefreshDatabase();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void RefreshDatabase()
        {
            projectDb = VoxelProjectDatabase.GetOrCreateDatabase();
            if (projectDb != null)
            {
                projectDb.ScanAndRefreshRecipes();
            }
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;

            headerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                normal = { textColor = new Color(0.95f, 0.96f, 0.98f) }
            };

            headerSubStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.72f, 0.78f, 0.85f) }
            };

            cardStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(14, 14, 12, 12),
                margin = new RectOffset(0, 0, 6, 10)
            };

            sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                margin = new RectOffset(0, 0, 4, 8)
            };

            navButtonStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fixedHeight = 36,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(12, 8, 0, 0)
            };

            navButtonActiveStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                fixedHeight = 36,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(12, 8, 0, 0),
                normal = { textColor = new Color(0.25f, 0.78f, 1.0f) }
            };

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            // 顶部横幅
            DrawTopBanner();

            // 双栏工作区
            EditorGUILayout.BeginHorizontal();

            DrawLeftSidebar();
            DrawRightContent();

            EditorGUILayout.EndHorizontal();

            // 底部快捷工具栏
            DrawBottomBar();
        }

        private void DrawTopBanner()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("📦 体素资产生产与工程管理工作室 (Voxel Baker Studio)", headerTitleStyle);
            EditorGUILayout.LabelField("Unity 2022.3 LTS + URP ｜ 工程分类归档 · 批量烘焙 · 几何分析 · 实体填充 · GPU 渲染 · 运行时破坏", headerSubStyle);
            EditorGUILayout.EndVertical();
        }

        private void DrawLeftSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(240));
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll, GUILayout.Width(240));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("流水线与工程导航", EditorStyles.miniBoldLabel);
            EditorGUILayout.Space(4);

            for (int i = 0; i < navTitles.Length; i++)
            {
                bool isActive = (currentNavIndex == i);
                Rect btnRect = EditorGUILayout.GetControlRect(false, 36);

                if (isActive)
                {
                    EditorGUI.DrawRect(btnRect, new Color(0.18f, 0.30f, 0.45f, 0.7f));
                    EditorGUI.DrawRect(new Rect(btnRect.x, btnRect.y, 4, btnRect.height), new Color(0.25f, 0.75f, 1f));
                }
                else if (btnRect.Contains(Event.current.mousePosition))
                {
                    EditorGUI.DrawRect(btnRect, new Color(0.25f, 0.25f, 0.25f, 0.25f));
                }

                if (GUI.Button(btnRect, navTitles[i], isActive ? navButtonActiveStyle : navButtonStyle))
                {
                    currentNavIndex = i;
                    GUI.FocusControl(null);
                }
            }

            EditorGUILayout.Space(14);

            // 快速预设卡片
            EditorGUILayout.BeginVertical(cardStyle);
            EditorGUILayout.LabelField("⚡ 快速测试预设导入", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            if (GUILayout.Button("🦆 小黄鸭 (Duck)"))
            {
                sourceMesh = VoxelDemoModelGenerator.CreateYellowDuckMesh(out sourceMaterials);
                assetName = "VoxelModel_Duck";
                assetCategory = VoxelModelCategory.Characters;
                RunAnalysis();
                currentNavIndex = 1;
            }
            if (GUILayout.Button("🌸 多层粉色头颅 (Pink Head)"))
            {
                sourceMesh = VoxelDemoModelGenerator.CreatePinkCharacterMesh(out sourceMaterials);
                assetName = "VoxelModel_PinkHead";
                assetCategory = VoxelModelCategory.Characters;
                RunAnalysis();
                currentNavIndex = 1;
            }
            if (GUILayout.Button("🏠 像素房子 (House)"))
            {
                sourceMesh = VoxelDemoModelGenerator.CreateHouseMesh(out sourceMaterials);
                assetName = "VoxelModel_House";
                assetCategory = VoxelModelCategory.Buildings;
                RunAnalysis();
                currentNavIndex = 1;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRightContent()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

            EditorGUILayout.Space(6);

            switch (currentNavIndex)
            {
                case 0: DrawTabProjectDatabase(); break;
                case 1: DrawStepSource(); break;
                case 2: DrawStepGeometry(); break;
                case 3: DrawStepAppearance(); break;
                case 4: DrawStepInterior(); break;
                case 5: DrawStepOptimization(); break;
                case 6: DrawStepPreview(); break;
                case 7: DrawStepBakeExport(); break;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

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
            EditorGUILayout.LabelField("1. 模型整体尺寸与体素粒度配置", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            targetModelHeight = EditorGUILayout.Slider(new GUIContent("模型目标整体高度 (Target Height)", "【控制模型在场景中看起来有多大】：例如设为 4.0 米会让模型更大更显眼，设为 2.2 米为标准人偶尺寸。"), targetModelHeight, 1.0f, 10.0f);
            if (EditorGUI.EndChangeCheck() && sourceGameObject != null)
            {
                ExtractModelFromSource(sourceGameObject);
            }

            voxelSize = EditorGUILayout.Slider(new GUIContent("单个体素颗粒尺寸 (Voxel Size)", "【控制体素颗粒的粗细与精细度】：数值越小（如 0.08），细节越多、总方块数越多；数值越大（如 0.20），像素颗粒感越强、总方块数越少。"), voxelSize, 0.04f, 0.5f);

            if (analysisReport != null && GUILayout.Button("🎯 自动计算并应用推荐的最佳体素粒度", GUILayout.Height(26)))
            {
                voxelSize = analysisReport.recommendedVoxelSize;
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("2. 内部实体化填充", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            fillInteriorSolid = EditorGUILayout.Toggle(new GUIContent("启用 3D 边界泛洪实体填充 (Solid Fill)", "【控制是否为实心】：勾选后模型内部是实心肉体，消解时像削苹果皮一样由外向内逐层剥落；不勾选则为空心气球外壳。"), fillInteriorSolid);

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("【尺寸换算提示】：总占用体素数 ≈ (模型高度 / 体素尺寸)³ × 填充率。消除游戏推荐总数在 600 ~ 1,500 格之间最佳！", MessageType.Info);
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

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("【采样机制】：\n• 表面体素：基于三角面重心坐标插值 UV0 实时采样 Albedo 贴图，100% 保真还原原美术材质！\n• 无贴图降级：自动使用 SubMesh 材质固有色或顶点色。\n• 自动生成紧凑 64x64 调色板纹理（支持 4,096 种材质语义，同时体素内嵌 32 位 RGBA 支持 1677 万全真彩）。", MessageType.Info);
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
            EditorGUILayout.LabelField("⑥ Scene 视图 3D 实时切片与预览 (3D Scene Preview)", sectionHeaderStyle);

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
                GUI.backgroundColor = new Color(0.2f, 0.75f, 1f);
                if (GUILayout.Button("🌟 一键将此体素模型实例化至当前场景 (并在视口聚焦)", GUILayout.Height(32)))
                {
                    InstantiateRecipeInScene(bakedAsset);
                }
                GUI.backgroundColor = Color.white;
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
                    currentRecipe.voxelSize = voxelSize;
                    currentRecipe.fillInteriorSolid = fillInteriorSolid;
                    currentRecipe.colorTolerance = colorTolerance;
                    currentRecipe.interiorProfile = interiorProfile;
                    currentRecipe.chunkSize = chunkSize;
                    currentRecipe.generateLODs = generateLODs;
                    currentRecipe.bakedAsset = bakedAsset;
                    currentRecipe.lastBakeTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    currentRecipe.lastBakeDuration = bakedAsset.bakeDurationSeconds;
                    currentRecipe.lastTotalVoxels = bakedAsset.totalOccupiedVoxels;
                    currentRecipe.isDirty = false;

                    EditorUtility.SetDirty(currentRecipe);

                    RefreshDatabase();

                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();

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
            GameObject go = new GameObject($"VoxelModel_{asset.name}");
            VoxelModelInstance instance = go.AddComponent<VoxelModelInstance>();
            instance.voxelAsset = asset;
            instance.InitializeModel();
            Selection.activeGameObject = go;
            Undo.RegisterCreatedObjectUndo(go, "Instantiate Voxel Model");
        }
        #endregion

        private void DrawStatBadge(string label, string value, Color valColor)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            GUIStyle valStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = valColor } };
            EditorGUILayout.LabelField(value, valStyle);
            EditorGUILayout.EndVertical();
        }

        private void ExtractModelFromSource(GameObject go)
        {
            if (go == null) return;

            // 1. 自动确保源 FBX / OBJ 模型开启 Read/Write Enabled (isReadable = true)
            string assetPath = AssetDatabase.GetAssetPath(go);
            if (!string.IsNullOrEmpty(assetPath))
            {
                ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                if (importer != null && !importer.isReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }
            }

            // 2. 深度扫描根节点与所有子节点的 MeshFilter / SkinnedMeshRenderer
            List<Mesh> meshes = new List<Mesh>();
            List<Material> materials = new List<Material>();
            List<Matrix4x4> transforms = new List<Matrix4x4>();

            Matrix4x4 rootScaleMatrix = Matrix4x4.Scale(go.transform.localScale);

            MeshFilter[] filters = go.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in filters)
            {
                if (mf != null && mf.sharedMesh != null)
                {
                    meshes.Add(mf.sharedMesh);
                    Matrix4x4 localMat = (mf.gameObject == go) ? rootScaleMatrix : (go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix);
                    transforms.Add(localMat);

                    Renderer r = mf.GetComponent<Renderer>();
                    if (r != null && r.sharedMaterials != null)
                    {
                        materials.AddRange(r.sharedMaterials);
                    }
                }
            }

            SkinnedMeshRenderer[] skins = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in skins)
            {
                if (smr != null && smr.sharedMesh != null)
                {
                    meshes.Add(smr.sharedMesh);
                    Matrix4x4 localMat = (smr.gameObject == go) ? rootScaleMatrix : (go.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix);
                    transforms.Add(localMat);

                    if (smr.sharedMaterials != null)
                    {
                        materials.AddRange(smr.sharedMaterials);
                    }
                }
            }

            // 将所有网格与 Transform 实际缩放烘焙至统一 Mesh
            if (meshes.Count > 0)
            {
                CombineInstance[] combines = new CombineInstance[meshes.Count];
                for (int i = 0; i < meshes.Count; i++)
                {
                    combines[i].mesh = meshes[i];
                    combines[i].transform = transforms[i];
                }
                Mesh combined = new Mesh();
                combined.name = $"{go.name}_Combined";
                combined.CombineMeshes(combines, false, true);
                sourceMesh = combined;
            }

            // 尺寸自动规范化 (对齐至用户在界面设定的 targetModelHeight 目标物理尺寸)
            if (sourceMesh != null)
            {
                float maxBoundDim = Mathf.Max(sourceMesh.bounds.size.x, Mathf.Max(sourceMesh.bounds.size.y, sourceMesh.bounds.size.z));
                if (maxBoundDim > 0)
                {
                    float autoScale = targetModelHeight / maxBoundDim;
                    CombineInstance[] scaleCombines = new CombineInstance[1];
                    scaleCombines[0].mesh = sourceMesh;
                    scaleCombines[0].transform = Matrix4x4.Scale(Vector3.one * autoScale);
                    Mesh scaledMesh = new Mesh();
                    scaledMesh.name = $"{sourceMesh.name}_Normalized";
                    scaledMesh.CombineMeshes(scaleCombines, false, true);
                    sourceMesh = scaledMesh;
                }
            }

            // 自动寻找模型目录已有的原生材质与贴图 (绝不生成任何多余的新材质/贴图文件)
            if (materials.Count == 0 && !string.IsNullOrEmpty(assetPath))
            {
                string dir = Path.GetDirectoryName(assetPath);
                string[] matGuids = AssetDatabase.FindAssets("t:Material", new string[] { dir });
                foreach (var guid in matGuids)
                {
                    string mPath = AssetDatabase.GUIDToAssetPath(guid);
                    Material existingMat = AssetDatabase.LoadAssetAtPath<Material>(mPath);
                    if (existingMat != null && !materials.Contains(existingMat))
                    {
                        materials.Add(existingMat);
                    }
                }
            }

            if (materials.Count > 0)
            {
                sourceMaterials = materials.ToArray();
            }

            // 资产名称严格跟随导入模型的原名
            string cleanModelName = go.name.Replace(" ", "_");
            assetName = cleanModelName.StartsWith("VoxelModel_") ? cleanModelName : $"VoxelModel_{cleanModelName}";

            RunAnalysis();
        }

        private void RunAnalysis()
        {
            if (sourceMesh != null)
            {
                analysisReport = MeshAnalyzer.Analyze(sourceMesh, sourceMaterials, voxelSize);
                if (analysisReport != null && analysisReport.recommendedVoxelSize > 0)
                {
                    voxelSize = analysisReport.recommendedVoxelSize;
                }
            }
        }

        private void DrawBottomBar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🎬 一键生成并打开演示场景 (Playground Demo Scene)", GUILayout.Height(24)))
            {
                VoxelMenuTools.CreateAndOpenDemoScene();
            }
            if (GUILayout.Button("📦 批量生成所有示例资产 (Duck / PinkHead / House)", GUILayout.Height(24)))
            {
                VoxelMenuTools.CreateSampleAssets();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (bakedAsset != null && currentNavIndex == 6)
            {
                VoxelScenePreview.DrawPreviewScene(bakedAsset, null, previewMode, enableSlicePlane, sliceNormal.normalized, sliceOffset);
            }
        }
    }
}
