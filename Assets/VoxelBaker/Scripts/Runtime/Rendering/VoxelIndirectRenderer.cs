using System;
using System.Runtime.InteropServices;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Runtime.Rendering
{
    public class VoxelIndirectRenderer : IVoxelRenderer
    {
        private VoxelAsset _asset;
        private Material _material;
        private MaterialPropertyBlock _matProps;
        private Transform _transform;
        private Mesh _unitCubeMesh;

        private GraphicsBuffer _voxelBuffer;
        private GraphicsBuffer _argsBuffer;
        private uint[] _argsData = new uint[5] { 0, 0, 0, 0, 0 };

        private int _capacity = 0;
        private int _currentInstanceCount = 0;
        private bool _isInitialized = false;

        private static readonly int PropVoxelBuffer = Shader.PropertyToID("_VoxelBuffer");
        private static readonly int PropVoxelSize = Shader.PropertyToID("_VoxelSize");
        private static readonly int PropLocalOrigin = Shader.PropertyToID("_LocalOrigin");
        private static readonly int PropPaletteTex = Shader.PropertyToID("_PaletteTex");

        public void Initialize(VoxelAsset asset, Material voxelMaterial, Transform rootTransform)
        {
            _asset = asset;
            _material = voxelMaterial;
            _transform = rootTransform;
            _matProps = new MaterialPropertyBlock();

            _unitCubeMesh = CreateUnitCubeMesh();

            int initialCount = asset != null && asset.initialVisibleVoxels != null ? asset.initialVisibleVoxels.Length : 1024;
            // 预留 2 倍容量，以容纳破坏暴露出的内部体素
            _capacity = Mathf.Max(2048, initialCount * 2);

            _voxelBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _capacity, Marshal.SizeOf<PackedVoxelGPU>());
            _argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, sizeof(uint) * 5);

            _argsData[0] = _unitCubeMesh.GetIndexCount(0);
            _argsData[1] = 0; // Instance count
            _argsData[2] = _unitCubeMesh.GetIndexStart(0);
            _argsData[3] = _unitCubeMesh.GetBaseVertex(0);
            _argsData[4] = 0; // Start instance

            _argsBuffer.SetData(_argsData);

            if (asset != null && asset.initialVisibleVoxels != null)
            {
                UpdateVisibleInstances(asset.initialVisibleVoxels, asset.initialVisibleVoxels.Length);
            }

            _isInitialized = true;
        }

        public void UpdateVisibleInstances(PackedVoxelGPU[] activeInstances, int count)
        {
            if (activeInstances == null || count <= 0)
            {
                _currentInstanceCount = 0;
                _argsData[1] = 0;
                _argsBuffer?.SetData(_argsData);
                return;
            }

            if (count > _capacity)
            {
                _capacity = Mathf.Max(count + 2048, (int)(_capacity * 1.5f));
                _voxelBuffer?.Release();
                _voxelBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _capacity, Marshal.SizeOf<PackedVoxelGPU>());
            }

            _voxelBuffer.SetData(activeInstances, 0, 0, count);
            _currentInstanceCount = count;

            _argsData[1] = (uint)count;
            _argsBuffer.SetData(_argsData);
        }

        private static readonly int PropObjectToWorldMatrix = Shader.PropertyToID("_ObjectToWorldMatrix");

        public void Render()
        {
            if (!_isInitialized || _currentInstanceCount == 0 || _material == null || _unitCubeMesh == null)
                return;

            _matProps.SetBuffer(PropVoxelBuffer, _voxelBuffer);
            _matProps.SetFloat(PropVoxelSize, _asset != null ? _asset.voxelSize : 0.1f);
            _matProps.SetVector(PropLocalOrigin, _asset != null ? (Vector4)_asset.localOrigin : Vector4.zero);
            _matProps.SetMatrix(PropObjectToWorldMatrix, _transform != null ? _transform.localToWorldMatrix : Matrix4x4.identity);

            if (_asset != null && _asset.paletteTexture != null)
            {
                _matProps.SetTexture(PropPaletteTex, _asset.paletteTexture);
            }

            // 计算世界空间包围盒
            Bounds worldBounds = new Bounds(_transform.position + (_asset != null ? _asset.boundsCenter : Vector3.zero), (_asset != null ? _asset.boundsSize : Vector3.one * 10f) * 1.5f);

            Graphics.DrawMeshInstancedIndirect(
                _unitCubeMesh,
                0,
                _material,
                worldBounds,
                _argsBuffer,
                0,
                _matProps,
                UnityEngine.Rendering.ShadowCastingMode.On,
                true,
                0,
                null,
                UnityEngine.Rendering.LightProbeUsage.Off
            );
        }

        public void Release()
        {
            _voxelBuffer?.Release();
            _voxelBuffer = null;

            _argsBuffer?.Release();
            _argsBuffer = null;

            if (_unitCubeMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_unitCubeMesh);
                _unitCubeMesh = null;
            }

            _isInitialized = false;
        }

        public void Dispose()
        {
            Release();
        }

        private static Mesh CreateUnitCubeMesh()
        {
            Mesh mesh = new Mesh { name = "UnitCube" };

            // 1x1x1 立方体，中心在原点 (-0.5 .. +0.5)
            Vector3[] vertices = new Vector3[]
            {
                // Front (+Z)
                new Vector3(-0.5f, -0.5f,  0.5f), new Vector3( 0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f,  0.5f,  0.5f), new Vector3(-0.5f,  0.5f,  0.5f),
                // Back (-Z)
                new Vector3( 0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f,  0.5f, -0.5f), new Vector3( 0.5f,  0.5f, -0.5f),
                // Top (+Y)
                new Vector3(-0.5f,  0.5f,  0.5f), new Vector3( 0.5f,  0.5f,  0.5f),
                new Vector3( 0.5f,  0.5f, -0.5f), new Vector3(-0.5f,  0.5f, -0.5f),
                // Bottom (-Y)
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3( 0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f, -0.5f,  0.5f), new Vector3(-0.5f, -0.5f,  0.5f),
                // Right (+X)
                new Vector3( 0.5f, -0.5f,  0.5f), new Vector3( 0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f,  0.5f, -0.5f), new Vector3( 0.5f,  0.5f,  0.5f),
                // Left (-X)
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f,  0.5f),
                new Vector3(-0.5f,  0.5f,  0.5f), new Vector3(-0.5f,  0.5f, -0.5f)
            };

            Vector3[] normals = new Vector3[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.back, Vector3.back, Vector3.back, Vector3.back,
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                Vector3.down, Vector3.down, Vector3.down, Vector3.down,
                Vector3.right, Vector3.right, Vector3.right, Vector3.right,
                Vector3.left, Vector3.left, Vector3.left, Vector3.left
            };

            int[] triangles = new int[]
            {
                0, 1, 2, 0, 2, 3,       // Front
                4, 5, 6, 4, 6, 7,       // Back
                8, 9, 10, 8, 10, 11,    // Top
                12, 13, 14, 12, 14, 15, // Bottom
                16, 17, 18, 16, 18, 19, // Right
                20, 21, 22, 20, 22, 23  // Left
            };

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
