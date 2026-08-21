using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelBaker.Data
{
    /// <summary>
    /// 单个调色板材质条目
    /// </summary>
    [Serializable]
    public struct VoxelPaletteEntry
    {
        public string name;
        public Color baseColor;
        [Range(0f, 1f)] public float metallic;
        [Range(0f, 1f)] public float smoothness;
        [ColorUsage(false, true)] public Color emissionColor;
        public int gameplayTag;

        public static VoxelPaletteEntry Default(Color color, string name = "Entry")
        {
            return new VoxelPaletteEntry
            {
                name = name,
                baseColor = color,
                metallic = 0.0f,
                smoothness = 0.5f,
                emissionColor = Color.black,
                gameplayTag = 0
            };
        }
    }

    /// <summary>
    /// 体素调色板资产容器
    /// </summary>
    [CreateAssetMenu(fileName = "NewVoxelPalette", menuName = "Voxel Baker/Voxel Palette")]
    public class VoxelPalette : ScriptableObject
    {
        [SerializeField]
        public List<VoxelPaletteEntry> entries = new List<VoxelPaletteEntry>();

        public int Count => entries.Count;

        public ushort AddOrFindColor(Color32 color, float tolerance = 4f)
        {
            if (entries.Count == 0)
            {
                entries.Add(VoxelPaletteEntry.Default(color, "Color_0"));
                return 0;
            }

            int bestIdx = 0;
            float minDiff = float.MaxValue;

            for (int i = 0; i < entries.Count; i++)
            {
                Color32 c = entries[i].baseColor;
                float diff = Mathf.Abs(c.r - color.r) + Mathf.Abs(c.g - color.g) + Mathf.Abs(c.b - color.b);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    bestIdx = i;
                }
            }

            if (minDiff <= tolerance || entries.Count >= 4090)
            {
                return (ushort)bestIdx;
            }

            entries.Add(VoxelPaletteEntry.Default(color, $"Color_{entries.Count}"));
            return (ushort)(entries.Count - 1);
        }

        public VoxelPaletteEntry GetEntry(ushort index)
        {
            if (index < entries.Count)
                return entries[index];
            return VoxelPaletteEntry.Default(Color.magenta, "Fallback");
        }

        public Texture2D CreatePaletteTexture()
        {
            int size = 64; // 64x64 = 4096 条目
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "PaletteTex"
            };

            Color[] colors = new Color[size * size];
            for (int i = 0; i < colors.Length; i++)
            {
                if (i < entries.Count)
                    colors[i] = entries[i].baseColor;
                else
                    colors[i] = Color.magenta;
            }
            tex.SetPixels(colors);
            tex.Apply(false, true);
            return tex;
        }
    }
}
