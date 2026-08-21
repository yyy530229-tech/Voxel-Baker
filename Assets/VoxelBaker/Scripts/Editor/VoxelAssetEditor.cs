using UnityEditor;
using UnityEngine;
using VoxelBaker.Data;
using VoxelBaker.Runtime;

namespace VoxelBaker.Editor
{
    [CustomEditor(typeof(VoxelAsset))]
    public class VoxelAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            VoxelAsset asset = (VoxelAsset)target;

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField($"📦 体素资产: {asset.name} (v{asset.version})", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox($"源模型: {asset.sourceModelName}\n网格尺寸: {asset.gridDimensions.x}x{asset.gridDimensions.y}x{asset.gridDimensions.z}\n体素大小: {asset.voxelSize:F3}米 | 分块大小: {asset.chunkSize}\n占据体素总量: {asset.totalOccupiedVoxels:N0} (表面体素: {asset.totalSurfaceVoxels:N0}, 内部实体: {asset.totalInteriorVoxels:N0})\n初始 GPU 渲染可见集: {asset.totalVisibleVoxels:N0}\n烘焙耗时: {asset.bakeDurationSeconds:F2} 秒", MessageType.Info);

            EditorGUILayout.Space(10);
            if (GUILayout.Button("🌟 一键在当前场景中实例化体素模型", GUILayout.Height(36)))
            {
                InstantiateInScene(asset);
            }

            if (GUILayout.Button("🔍 在体素烘焙工作室中打开此资产", GUILayout.Height(28)))
            {
                VoxelBakerWindow.OpenWindowWithAsset(asset);
            }

            EditorGUILayout.Space(10);
            DrawDefaultInspector();
        }

        private static void InstantiateInScene(VoxelAsset asset)
        {
            GameObject go = new GameObject($"VoxelModel_{asset.name}");
            VoxelModelInstance instance = go.AddComponent<VoxelModelInstance>();
            instance.voxelAsset = asset;

            Shader s = Shader.Find("VoxelBaker/URP/VoxelLit");
            if (s != null)
            {
                instance.voxelMaterial = new Material(s);
            }

            instance.InitializeModel();
            Selection.activeGameObject = go;
            Undo.RegisterCreatedObjectUndo(go, "Instantiate Voxel Model");
        }
    }
}
