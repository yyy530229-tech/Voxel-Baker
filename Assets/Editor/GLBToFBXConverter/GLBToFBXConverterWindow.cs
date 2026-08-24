using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ModelConverter
{
    /// <summary>
    /// GLB 转 FBX 转换工具窗口 (支持单文件转换、文件夹批量转换、贴图提取与一键 Prefab 生成)
    /// </summary>
    public class GLBToFBXConverterWindow : EditorWindow
    {
        private string _singleGlbPath = "";
        private string _singleFbxOutputPath = "";
        private string _batchInputFolder = "";
        private string _batchOutputFolder = "";

        private bool _extractEmbeddedTextures = true;
        private bool _generateUnityMaterials = true;
        private bool _generatePrefab = true;

        private Vector2 _scrollPos;

        [MenuItem("Tools/Model Tools/📦 GLB to FBX 模型转换器 (Converter Window)", false, 1)]
        public static void ShowWindow()
        {
            var win = GetWindow<GLBToFBXConverterWindow>("GLB to FBX");
            win.minSize = new Vector2(480, 420);
            win.Show();
        }

        #region Right-Click Context Menu Support

        [MenuItem("Assets/📦 转换为 FBX (Convert to FBX)", false, 20)]
        public static void ConvertSelectedGLBToFBX()
        {
            string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(selectedPath) || !selectedPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("GLB to FBX", "请在 Project 视图中右键选中一个 .glb 文件！", "确定");
                return;
            }

            string fullGlbPath = Path.GetFullPath(selectedPath);
            string fullFbxPath = Path.ChangeExtension(fullGlbPath, ".fbx");

            bool ok = ConvertSingleFile(fullGlbPath, fullFbxPath, true, true, true);
            if (ok)
            {
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("GLB to FBX", $"✅ 转换成功！FBX 文件已生成：\n{fullFbxPath}", "确定");
            }
            else
            {
                EditorUtility.DisplayDialog("GLB to FBX", "❌ 转换失败，请查看 Console 控制台详细日志。", "确定");
            }
        }

        [MenuItem("Assets/📦 转换为 FBX (Convert to FBX)", true)]
        public static bool ValidateConvertSelectedGLBToFBX()
        {
            string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(selectedPath) && selectedPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region CLI Entry Point

        /// <summary>
        /// 命令行批处理入口
        /// 命令示例:
        /// Unity.exe -batchmode -projectPath "d:\UnityProject\Voxel" -executeMethod ModelConverter.GLBToFBXConverterWindow.ConvertGLBCLI -input "Assets/Models/test.glb" -output "Assets/Models/test.fbx" -quit
        /// </summary>
        public static void ConvertGLBCLI()
        {
            Debug.Log("[GLBToFBXConverter] === 开始执行 CLI GLB -> FBX 转换 ===");

            string inputPath = "";
            string outputPath = "";

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("-input", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    inputPath = args[i + 1];
                }
                if (args[i].Equals("-output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    outputPath = args[i + 1];
                }
            }

            if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
            {
                Debug.LogError($"[GLBToFBXConverter] ❌ 输入 GLB 文件不存在: {inputPath}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            if (string.IsNullOrEmpty(outputPath))
            {
                outputPath = Path.ChangeExtension(inputPath, ".fbx");
            }

            bool success = ConvertSingleFile(inputPath, outputPath, true, true, true);
            if (success)
            {
                Debug.Log($"[GLBToFBXConverter] ✅ CLI 转换成功: {outputPath}");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("[GLBToFBXConverter] ❌ CLI 转换失败！");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        #endregion

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("📦 GLB 转 FBX 工业级转换器", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("本工具支持将 .glb (glTF 2.0 Binary) 3D 模型完整转换为标准 Autodesk FBX 7.4 格式，支持解析网格顶点、法线、UV、顶点色、多材质子网格及嵌入贴图！", MessageType.Info);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("⚙️ 转换配置选项", EditorStyles.boldLabel);
            _extractEmbeddedTextures = EditorGUILayout.Toggle("提取嵌入贴图为 PNG", _extractEmbeddedTextures);
            _generateUnityMaterials = EditorGUILayout.Toggle("自动生成 URP Lit 材质球", _generateUnityMaterials);
            _generatePrefab = EditorGUILayout.Toggle("自动生成 Unity Prefab 预制体", _generatePrefab);

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("📄 单文件转换 (Single File)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            _singleGlbPath = EditorGUILayout.TextField("输入 GLB 文件", _singleGlbPath);
            if (GUILayout.Button("选择...", GUILayout.Width(70)))
            {
                string path = EditorUtility.OpenFilePanel("选择 GLB 文件", "Assets", "glb");
                if (!string.IsNullOrEmpty(path))
                {
                    _singleGlbPath = path;
                    _singleFbxOutputPath = Path.ChangeExtension(path, ".fbx");
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _singleFbxOutputPath = EditorGUILayout.TextField("输出 FBX 路径", _singleFbxOutputPath);
            if (GUILayout.Button("保存为...", GUILayout.Width(70)))
            {
                string path = EditorUtility.SaveFilePanel("输出 FBX 文件", "Assets", Path.GetFileNameWithoutExtension(_singleGlbPath), "fbx");
                if (!string.IsNullOrEmpty(path))
                {
                    _singleFbxOutputPath = path;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            GUI.backgroundColor = new Color(0.25f, 0.85f, 0.45f);
            if (GUILayout.Button("🚀 立即开始单文件转换 (Convert to FBX)", GUILayout.Height(32)))
            {
                if (string.IsNullOrEmpty(_singleGlbPath) || !File.Exists(_singleGlbPath))
                {
                    EditorUtility.DisplayDialog("提示", "请先选择有效的输入 GLB 文件！", "确定");
                }
                else
                {
                    string outPath = string.IsNullOrEmpty(_singleFbxOutputPath) ? Path.ChangeExtension(_singleGlbPath, ".fbx") : _singleFbxOutputPath;
                    bool ok = ConvertSingleFile(_singleGlbPath, outPath, _extractEmbeddedTextures, _generateUnityMaterials, _generatePrefab);
                    if (ok)
                    {
                        AssetDatabase.Refresh();
                        EditorUtility.DisplayDialog("成功", $"✅ FBX 转换成功！\n文件路径: {outPath}", "确定");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("失败", "❌ 转换失败，请检查 Console 控制台报错。", "确定");
                    }
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("📁 文件夹批量转换 (Batch Folder)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            _batchInputFolder = EditorGUILayout.TextField("输入文件夹", _batchInputFolder);
            if (GUILayout.Button("选择...", GUILayout.Width(70)))
            {
                string path = EditorUtility.OpenFolderPanel("选择包含 GLB 的文件夹", "Assets", "");
                if (!string.IsNullOrEmpty(path))
                {
                    _batchInputFolder = path;
                    if (string.IsNullOrEmpty(_batchOutputFolder)) _batchOutputFolder = path;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _batchOutputFolder = EditorGUILayout.TextField("输出文件夹", _batchOutputFolder);
            if (GUILayout.Button("选择...", GUILayout.Width(70)))
            {
                string path = EditorUtility.OpenFolderPanel("选择输出文件夹", "Assets", "");
                if (!string.IsNullOrEmpty(path)) _batchOutputFolder = path;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            GUI.backgroundColor = new Color(0.25f, 0.65f, 0.95f);
            if (GUILayout.Button("⚡ 批量转换文件夹内所有 GLB 文件", GUILayout.Height(32)))
            {
                if (string.IsNullOrEmpty(_batchInputFolder) || !Directory.Exists(_batchInputFolder))
                {
                    EditorUtility.DisplayDialog("提示", "请选择有效的输入文件夹！", "确定");
                }
                else
                {
                    string[] glbFiles = Directory.GetFiles(_batchInputFolder, "*.glb", SearchOption.AllDirectories);
                    if (glbFiles.Length == 0)
                    {
                        EditorUtility.DisplayDialog("提示", "所选文件夹内未找到任何 .glb 文件！", "确定");
                    }
                    else
                    {
                        int successCount = 0;
                        string outDir = string.IsNullOrEmpty(_batchOutputFolder) ? _batchInputFolder : _batchOutputFolder;

                        for (int i = 0; i < glbFiles.Length; i++)
                        {
                            string glb = glbFiles[i];
                            EditorUtility.DisplayProgressBar("批量转换中", $"正在转换 ({i + 1}/{glbFiles.Length}): {Path.GetFileName(glb)}", (float)i / glbFiles.Length);
                            string rel = Path.GetFileNameWithoutExtension(glb) + ".fbx";
                            string target = Path.Combine(outDir, rel);

                            if (ConvertSingleFile(glb, target, _extractEmbeddedTextures, _generateUnityMaterials, _generatePrefab))
                            {
                                successCount++;
                            }
                        }

                        EditorUtility.ClearProgressBar();
                        AssetDatabase.Refresh();
                        EditorUtility.DisplayDialog("完成", $"🎉 批量转换完成！共转换成功: {successCount}/{glbFiles.Length} 个文件。", "确定");
                    }
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
        }

        public static bool ConvertSingleFile(string glbPath, string fbxOutputPath, bool extractTextures, bool createMats, bool createPrefab)
        {
            try
            {
                if (!File.Exists(glbPath))
                {
                    Debug.LogError($"[GLBToFBXConverter] 文件不存在: {glbPath}");
                    return false;
                }

                byte[] glbBytes = File.ReadAllBytes(glbPath);
                string modelName = Path.GetFileNameWithoutExtension(glbPath);

                // 1. 解析 GLB
                var modelData = GLBParser.LoadGLB(glbBytes, modelName);
                if (modelData == null || modelData.meshes.Count == 0)
                {
                    Debug.LogError($"[GLBToFBXConverter] 解析 GLB 失败或未找到网格数据: {glbPath}");
                    return false;
                }

                // 2. 提取贴图为 PNG
                string outputDir = Path.GetDirectoryName(fbxOutputPath);
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

                if (extractTextures && modelData.textures != null && modelData.textures.Count > 0)
                {
                    for (int i = 0; i < modelData.textures.Count; i++)
                    {
                        var tex = modelData.textures[i];
                        string texExt = tex.mimeType == "image/jpeg" ? ".jpg" : ".png";
                        string texFileName = $"{modelName}_{tex.name}{texExt}";
                        string texFilePath = Path.Combine(outputDir, texFileName);
                        File.WriteAllBytes(texFilePath, tex.rawImageData);
                        Debug.Log($"[GLBToFBXConverter] 提取贴图: {texFilePath}");
                    }
                }

                // 3. 导出标准 FBX 7.4 文件
                FBXExporter.ExportToFBX(modelData, fbxOutputPath);

                // 4. (可选) 如果在 Assets 目录下，同时生成可以直接引用的 Prefab
                string projectRelativePath = GetProjectRelativePath(fbxOutputPath);
                if (!string.IsNullOrEmpty(projectRelativePath) && (createMats || createPrefab))
                {
                    AssetDatabase.ImportAsset(projectRelativePath, ImportAssetOptions.ForceUpdate);
                    CreateUnityAssets(modelData, projectRelativePath, createMats, createPrefab);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GLBToFBXConverter] 转换发生异常: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        private static void CreateUnityAssets(GLBParser.ParsedModelData modelData, string fbxAssetPath, bool createMats, bool createPrefab)
        {
            string folder = Path.GetDirectoryName(fbxAssetPath);

            // 创建材质球
            List<Material> unityMaterials = new List<Material>();
            Shader urpLitShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            if (createMats && modelData.materials != null)
            {
                for (int i = 0; i < modelData.materials.Count; i++)
                {
                    var pMat = modelData.materials[i];
                    string matAssetPath = Path.Combine(folder, $"{pMat.materialName}.mat").Replace("\\", "/");

                    Material m = AssetDatabase.LoadAssetAtPath<Material>(matAssetPath);
                    if (m == null)
                    {
                        m = new Material(urpLitShader);
                        AssetDatabase.CreateAsset(m, matAssetPath);
                    }

                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", pMat.baseColor);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", pMat.baseColor);
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", pMat.metallic);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f - pMat.roughness);

                    unityMaterials.Add(m);
                }
            }

            // 创建 Prefab
            if (createPrefab)
            {
                GameObject rootGo = new GameObject(modelData.modelName);

                for (int m = 0; m < modelData.meshes.Count; m++)
                {
                    var pMesh = modelData.meshes[m];
                    GameObject meshGo = new GameObject(pMesh.meshName);
                    meshGo.transform.SetParent(rootGo.transform);

                    MeshFilter mf = meshGo.AddComponent<MeshFilter>();
                    mf.sharedMesh = pMesh.unityMesh;

                    MeshRenderer mr = meshGo.AddComponent<MeshRenderer>();
                    Material[] assignedMats = new Material[pMesh.subMeshIndices.Count];
                    for (int s = 0; s < assignedMats.Length; s++)
                    {
                        int matIdx = (s < pMesh.subMeshMaterialIndices.Count && pMesh.subMeshMaterialIndices[s] >= 0) ? pMesh.subMeshMaterialIndices[s] : 0;
                        assignedMats[s] = (matIdx < unityMaterials.Count) ? unityMaterials[matIdx] : new Material(urpLitShader);
                    }
                    mr.sharedMaterials = assignedMats;
                }

                string prefabPath = Path.Combine(folder, $"{modelData.modelName}.prefab").Replace("\\", "/");
                PrefabUtility.SaveAsPrefabAsset(rootGo, prefabPath);
                DestroyImmediate(rootGo);
                Debug.Log($"[GLBToFBXConverter] 成功生成 Unity 预制体: {prefabPath}");
            }
        }

        private static string GetProjectRelativePath(string fullPath)
        {
            string projectPath = Path.GetFullPath(Application.dataPath + "/..").Replace("\\", "/");
            string normalized = Path.GetFullPath(fullPath).Replace("\\", "/");

            if (normalized.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
            {
                return normalized.Substring(projectPath.Length).TrimStart('/');
            }
            return "";
        }
    }
}
