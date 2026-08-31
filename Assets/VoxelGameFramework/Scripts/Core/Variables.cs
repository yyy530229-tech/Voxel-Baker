using GameFramework;

namespace VoxelGameFramework
{
    /// <summary>
    /// 整型变量 (DataNode 专用)
    /// </summary>
    public sealed class VarInt32 : Variable<int>
    {
        public VarInt32() { }
        public VarInt32(int value) { Value = value; }

        public static implicit operator VarInt32(int value)
        {
            return new VarInt32(value);
        }

        public static implicit operator int(VarInt32 value)
        {
            return value != null ? value.Value : 0;
        }
    }

    /// <summary>
    /// 浮点变量
    /// </summary>
    public sealed class VarFloat : Variable<float>
    {
        public VarFloat() { }
        public VarFloat(float value) { Value = value; }

        public static implicit operator VarFloat(float value)
        {
            return new VarFloat(value);
        }

        public static implicit operator float(VarFloat value)
        {
            return value != null ? value.Value : 0f;
        }
    }

    /// <summary>
    /// 布尔变量
    /// </summary>
    public sealed class VarBool : Variable<bool>
    {
        public VarBool() { }
        public VarBool(bool value) { Value = value; }

        public static implicit operator VarBool(bool value)
        {
            return new VarBool(value);
        }

        public static implicit operator bool(VarBool value)
        {
            return value != null && value.Value;
        }
    }

    /// <summary>
    /// 字符串变量
    /// </summary>
    public sealed class VarString : Variable<string>
    {
        public VarString() { }
        public VarString(string value) { Value = value; }

        public static implicit operator VarString(string value)
        {
            return new VarString(value);
        }

        public static implicit operator string(VarString value)
        {
            return value != null ? value.Value : null;
        }
    }
}
