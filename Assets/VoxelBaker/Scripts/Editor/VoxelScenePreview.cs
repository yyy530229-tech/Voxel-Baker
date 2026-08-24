using System;
using UnityEditor;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Editor
{
    public enum VoxelPreviewMode
    {
        OriginalMesh,
        SurfaceOnly,
        SolidOccupancy,
        DistanceField,
        LayerClassification,
        PaletteColor,
        AmbientOcclusion,
        FaceMask,
        ChunkBounds,
        LODView
    }

    public static class VoxelScenePreview
    {
        public static void DrawPreviewScene(
            VoxelAsset asset,
            Transform transform,
            VoxelPreviewMode mode,
            bool enableSlicePlane,
            Vector3 slicePlaneNormal,
            float slicePlaneOffset)
        {
            if (asset == null || asset.chunks == null) return;

            Vector3 rootPos = transform != null ? transform.position : Vector3.zero;
            Quaternion rootRot = transform != null ? transform.rotation : Quaternion.identity;

            float vSize = asset.voxelSize * 0.98f;
            int totalOccupied = asset.totalOccupiedVoxels;
            // 超过 15000 体素时启用步进采样以保持 Scene 视图 60fps 丝滑流畅
            int step = (totalOccupied > 15000) ? Mathf.CeilToInt(totalOccupied / 15000f) : 1;

            int drawnCount = 0;

            // 启用深度测试 (LessEqual)，彻底消除半透明 X 光穿透感，恢复 100% 实体遮挡！
            var prevZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            try
            {
                foreach (var chunk in asset.chunks)
                {
                    if (chunk.cells == null) continue;

                    if (mode == VoxelPreviewMode.ChunkBounds)
                    {
                        Handles.color = Color.cyan;
                        Handles.DrawWireCube(rootPos + rootRot * chunk.localBounds.center, chunk.localBounds.size);
                        continue;
                    }

                    for (int i = 0; i < chunk.cells.Length; i += step)
                    {
                        var cell = chunk.cells[i];
                        if (!cell.isOccupied) continue;

                        Vector3 localPos = asset.GridToLocalPosition(cell.gridPos);

                        // 切片剖面裁切检查
                        if (enableSlicePlane)
                        {
                            float dist = Vector3.Dot(localPos, slicePlaneNormal.normalized) - slicePlaneOffset;
                            if (dist > 0) continue;
                        }

                        Vector3 worldPos = rootPos + rootRot * localPos;

                        Color c = Color.white;
                        switch (mode)
                        {
                            case VoxelPreviewMode.SurfaceOnly:
                                if (cell.layer != VoxelLayerType.OuterSurface) continue;
                                c = cell.customColor;
                                break;

                            case VoxelPreviewMode.SolidOccupancy:
                                c = (cell.layer == VoxelLayerType.OuterSurface) ? new Color(0.2f, 0.85f, 0.3f) : new Color(1f, 0.8f, 0.2f);
                                break;

                            case VoxelPreviewMode.DistanceField:
                                float dNorm = Mathf.Clamp01(cell.distanceToSurface / 8f);
                                c = Color.Lerp(Color.blue, Color.red, dNorm);
                                break;

                            case VoxelPreviewMode.LayerClassification:
                                switch (cell.layer)
                                {
                                    case VoxelLayerType.OuterSurface: c = new Color(1f, 0.2f, 0.6f); break; // 粉
                                    case VoxelLayerType.InnerSurface: c = new Color(0.2f, 0.8f, 0.9f); break; // 青
                                    case VoxelLayerType.Interior:     c = new Color(0.4f, 0.9f, 0.2f); break; // 绿
                                    case VoxelLayerType.Core:         c = new Color(1f, 0.8f, 0.1f); break; // 黄
                                    default: c = Color.gray; break;
                                }
                                break;

                            case VoxelPreviewMode.AmbientOcclusion:
                                float ao = cell.ao / 255f;
                                c = new Color(ao, ao, ao, 1f);
                                break;

                            case VoxelPreviewMode.FaceMask:
                                c = cell.faceMask != VoxelFaceMask.None ? Color.magenta : Color.gray;
                                break;

                            case VoxelPreviewMode.PaletteColor:
                            default:
                                c = cell.customColor;
                                break;
                        }

                        Handles.color = c;
                        Handles.CubeHandleCap(0, worldPos, rootRot, vSize, EventType.Repaint);
                        drawnCount++;
                    }
                }
            }
            finally
            {
                Handles.zTest = prevZTest;
            }
        }
    }
}
