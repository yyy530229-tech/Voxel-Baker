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
    public partial class VoxelBakerWindow : EditorWindow
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
            "⑥ 3D 视图 (参数预览 / 资产检查)",
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
        // 默认改为「单层壳」：把块数预算 100% 花在可见表面上，
        // 同样预算下表面分辨率显著高于实心填充（详见 VoxelBakeSettings.fillStrategy 注释）。
        private bool fillInteriorSolid = false;
        private bool useAutoVoxelSize = true; // 自动按预算推导体素尺寸
        private int targetVoxelBudget = 6000; // 目标体素总块数预算 (乐高风格推荐 3,000 ~ 10,000)

        // 阶段 3: 外观
        private float colorTolerance = 4f;
        private int paletteColorCount = 24; // 乐高式平色块数量

        // 抗锯齿 / 细腻度画质
        private bool enableAntiAliasing = true;   // 形态学平滑 (闭运算填孔 + 开运算去毛刺)
        private int smoothingIterations = 1;      // 默认 1：保细节；2/3 会明显抹平细小结构
        private int supersampleRate = 3;          // 1=整格单点, 2=2x2x2=8子盒, 3=3x3x3=27子盒

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

        //
        // 参数驱动的实时体素化预览面板。
        // 0 = 参数预览（改参数即时看到体素化效果，后台线程计算，不卡编辑器）
        // 1 = 已烘焙资产检查（沿用原来的 Scene 视图切片诊断）
        //
        private VoxelPreviewPanel previewPanel;
        private int previewTabMode = 0;
        private readonly string[] previewTabModeNames = new string[]
        {
            "参数预览 (实时体素化)",
            "已烘焙资产检查 (Scene 切片)"
        };
        private VoxelPreviewRequest previewRequestTemplate = new VoxelPreviewRequest();

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

            // 预览面板持有 PreviewRenderUtility / Mesh / Material，
            // 这些都是非序列化的原生资源，必须在窗口关闭时显式释放，否则会泄漏。
            if (previewPanel == null)
            {
                previewPanel = new VoxelPreviewPanel();
                previewPanel.RepaintRequested = () => Repaint();
            }
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnDestroy()
        {
            if (previewPanel != null)
            {
                previewPanel.Dispose();
                previewPanel = null;
            }
        }

        private void Update()
        {
            // 驱动预览面板的后台任务调度。
            // 只在预览 Tab 时才跑 —— 别的页面没必要占用后台线程。
            if (previewPanel != null && currentNavIndex == 6 && previewTabMode == 0)
            {
                previewPanel.Update();
            }
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

            // 快捷导入
            EditorGUILayout.BeginVertical(cardStyle);
            EditorGUILayout.LabelField("⚡ 快速导入选定模型", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            if (GUILayout.Button("📥 载入当前选中的 3D 模型"))
            {
                GameObject sel = Selection.activeGameObject;
                if (sel != null)
                {
                    sourceGameObject = sel;
                    MeshFilter mf = sel.GetComponentInChildren<MeshFilter>();
                    Renderer r = sel.GetComponentInChildren<Renderer>();
                    if (mf != null) sourceMesh = mf.sharedMesh;
                    if (r != null) sourceMaterials = r.sharedMaterials;
                    assetName = $"VoxelModel_{sel.name.Replace(" ", "_")}";
                    RunAnalysis();
                    currentNavIndex = 1;
                }
                else
                {
                    EditorUtility.DisplayDialog("提示", "请先在 Project 或 Hierarchy 中选中一个 3D 模型对象！", "确定");
                }
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

            // 网格提取：单网格直接使用原生对象保护 UV，多部件网格才执行组合
            if (meshes.Count == 1)
            {
                sourceMesh = meshes[0];
            }
            else if (meshes.Count > 1)
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

            // 自动寻找模型目录与 Materials 子目录已有的原生材质与贴图
            if ((materials.Count == 0 || materials[0] == null) && !string.IsNullOrEmpty(assetPath))
            {
                string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
                string[] searchDirs = new string[] { dir, dir + "/Materials" };
                string[] matGuids = AssetDatabase.FindAssets("t:Material", searchDirs);
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

        private static string DensityName(int budget)
        {
            if (budget <= 3000) return "轻量复古";
            if (budget <= 6000) return "标准乐高";
            if (budget <= 10000) return "高清细腻";
            return "超高清";
        }

        private void DrawBottomBar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🔄 刷新工程配方库 (Refresh Recipes)", GUILayout.Height(24)))
            {
                VoxelMenuTools.RefreshDatabase();
            }
            if (GUILayout.Button("📦 烘焙选中的 3D 模型 (Bake Selected)", GUILayout.Height(24)))
            {
                VoxelMenuTools.BakeSelectedModel();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (bakedAsset != null && currentNavIndex == 6)
            {
                // 如果场景中已经有实体化的体素模型，则直接使用其实体，不重复绘制 Handles 避免双层穿透重叠
                VoxelModelInstance existing = UnityEngine.Object.FindObjectOfType<VoxelModelInstance>();
                if (existing != null && existing.gameObject != null && existing.gameObject.activeInHierarchy)
                {
                    if (existing.voxelAsset == bakedAsset)
                    {
                        return;
                    }
                }

                Transform anchor = (existing != null) ? existing.transform : null;
                VoxelScenePreview.DrawPreviewScene(bakedAsset, anchor, previewMode, enableSlicePlane, sliceNormal.normalized, sliceOffset);
            }
        }
    }
}
