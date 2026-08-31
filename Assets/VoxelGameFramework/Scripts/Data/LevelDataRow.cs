using System.Text;
using GameFramework.DataTable;
using UnityEngine;

namespace VoxelGameFramework.Data
{
    /// <summary>
    /// 关卡数据行 (DataTable 行定义)
    /// 数据源: 每关一个条目, 对应 VoxelLevelConfig
    /// </summary>
    public sealed class LevelDataRow : IDataRow
    {
        public int Id { get; private set; }
        public string LevelTitle { get; private set; }
        public string VoxelAssetName { get; private set; }
        public Vector3 SpawnPosition { get; private set; }
        public Vector3 SpawnRotation { get; private set; }
        public float SpawnScale { get; private set; }
        public float WinDestructionRatio { get; private set; }
        public int RewardCoins { get; private set; }
        public Color BackgroundColor { get; private set; }

        public bool ParseDataRow(string dataRowString, object userData)
        {
            // 格式: Id,Title,AssetName,PosX,PosY,PosZ,RotX,RotY,RotZ,Scale,WinRatio,Reward,R,G,B
            string[] cols = dataRowString.Split(',');
            if (cols.Length < 15) return false;

            Id = int.Parse(cols[0].Trim());
            LevelTitle = cols[1].Trim();
            VoxelAssetName = cols[2].Trim();
            SpawnPosition = new Vector3(
                float.Parse(cols[3].Trim()),
                float.Parse(cols[4].Trim()),
                float.Parse(cols[5].Trim()));
            SpawnRotation = new Vector3(
                float.Parse(cols[6].Trim()),
                float.Parse(cols[7].Trim()),
                float.Parse(cols[8].Trim()));
            SpawnScale = float.Parse(cols[9].Trim());
            WinDestructionRatio = float.Parse(cols[10].Trim());
            RewardCoins = int.Parse(cols[11].Trim());
            BackgroundColor = new Color(
                float.Parse(cols[12].Trim()),
                float.Parse(cols[13].Trim()),
                float.Parse(cols[14].Trim()),
                1f);
            return true;
        }

        public bool ParseDataRow(byte[] dataRowBytes, int startIndex, int length, object userData)
        {
            string text = Encoding.UTF8.GetString(dataRowBytes, startIndex, length);
            return ParseDataRow(text, userData);
        }
    }
}
