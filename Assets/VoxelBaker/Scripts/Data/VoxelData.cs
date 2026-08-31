using System;
using UnityEngine;

namespace VoxelBaker.Data
{
    /// <summary>
    /// 体素层级分类
    /// </summary>
    public enum VoxelLayerType : byte
    {
        Empty = 0,
        OuterSurface = 1,  // 最外表面（与外部空气直接接触）
        InnerSurface = 2,  // 次表面（距离表面1~2格）
        Interior = 3,      // 内部实体
        Core = 4,          // 核心骨架/深层内部
        Cavity = 5         // 内部封闭空腔
    }

    /// <summary>
    /// 体素6个面的暴露标志位（bit 0: +X, bit 1: -X, bit 2: +Y, bit 3: -Y, bit 4: +Z, bit 5: -Z）
    /// </summary>
    [Flags]
    public enum VoxelFaceMask : byte
    {
        None     = 0,
        PosX     = 1 << 0, // +X
        NegX     = 1 << 1, // -X
        PosY     = 1 << 2, // +Y
        NegY     = 1 << 3, // -Y
        PosZ     = 1 << 4, // +Z
        NegZ     = 1 << 5, // -Z
        AllFaces = PosX | NegX | PosY | NegY | PosZ | NegZ
    }

    /// <summary>
    /// 原始单体素完整描述（烘焙期使用）
    /// </summary>
    [Serializable]
    public struct VoxelCell
    {
        public Vector3Int gridPos;      // 网格局部整型坐标
        public bool isOccupied;         // 是否被占据
        public VoxelLayerType layer;    // 视觉/深度层级
        public byte distanceToSurface;  // 曼哈顿/欧氏距表面深度 (量化到 0-255)
        public ushort paletteIndex;     // 调色板索引 (VisualID)
        public byte materialId;         // 材质语义ID / Gameplay区域ID
        public byte ao;                 // 预烘焙环境光遮蔽 (0~255)
        public VoxelFaceMask faceMask;  // 6面暴露掩码
        public short initialHP;         // 初始生命值
        public short currentHP;         // 运行时生命值
        public bool isAlive;            // 是否存活
        public Color32 customColor;     // 原始/烘焙颜色
        public byte surfaceCoverage;    // 表面覆盖率 0-255 (超采样质量值, 0=无覆盖, 255=完全覆盖)
        public float exactDistance;     // 精确欧氏距离到表面 (烘焙期使用, 不打包到GPU)

        public static VoxelCell Empty => new VoxelCell
        {
            gridPos = Vector3Int.zero,
            isOccupied = false,
            layer = VoxelLayerType.Empty,
            distanceToSurface = byte.MaxValue,
            paletteIndex = 0,
            materialId = 0,
            ao = 255,
            faceMask = VoxelFaceMask.None,
            initialHP = 0,
            currentHP = 0,
            isAlive = false,
            customColor = new Color32(0, 0, 0, 0),
            surfaceCoverage = 0,
            exactDistance = float.MaxValue
        };
    }

    /// <summary>
    /// GPU渲染紧凑体素实例结构体 (16 bytes 对齐，极致内存与带宽)
    /// </summary>
    [Serializable]
    public struct PackedVoxelGPU
    {
        // bit 0..9: X (0~1023), bit 10..19: Y (0~1023), bit 20..29: Z (0~1023), bit 30..31: Reserved
        public uint packedPosition;
        
        // bit 0..11: PaletteIndex (0~4095), bit 12..15: LayerType (0~15), bit 16..23: AO (0~255), bit 24..29: FaceMask (6-bit), bit 30..31: Reserved
        public uint packedAttributes;

        // RGBA颜色（支持直接着色或调色板融合）
        public uint colorRGBA;

        // bit 0..15: VoxelLocalIndex / ID, bit 16..31: ChunkID / Flags
        public uint voxelMeta;

        public static uint PackPosition(int x, int y, int z)
        {
            uint ux = (uint)(Mathf.Clamp(x, 0, 1023) & 0x3FF);
            uint uy = (uint)(Mathf.Clamp(y, 0, 1023) & 0x3FF);
            uint uz = (uint)(Mathf.Clamp(z, 0, 1023) & 0x3FF);
            return ux | (uy << 10) | (uz << 20);
        }

        public static Vector3Int UnpackPosition(uint packed)
        {
            int x = (int)(packed & 0x3FF);
            int y = (int)((packed >> 10) & 0x3FF);
            int z = (int)((packed >> 20) & 0x3FF);
            return new Vector3Int(x, y, z);
        }

        public static uint PackAttributes(ushort paletteIdx, VoxelLayerType layer, byte ao, VoxelFaceMask mask)
        {
            uint p = (uint)(paletteIdx & 0xFFF);
            uint l = (uint)((byte)layer & 0xF);
            uint a = (uint)ao;
            uint m = (uint)((byte)mask & 0x3F);
            return p | (l << 12) | (a << 16) | (m << 24);
        }

        public static uint ColorToUInt(Color32 c)
        {
            return (uint)c.r | ((uint)c.g << 8) | ((uint)c.b << 16) | ((uint)c.a << 24);
        }

        public static Color32 UIntToColor(uint c)
        {
            return new Color32(
                (byte)(c & 0xFF),
                (byte)((c >> 8) & 0xFF),
                (byte)((c >> 16) & 0xFF),
                (byte)((c >> 24) & 0xFF)
            );
        }
    }
}
