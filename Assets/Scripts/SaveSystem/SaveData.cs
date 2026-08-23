using System;

namespace Egghead.SaveSystem
{
    [Serializable]
    public struct SavedLetterTileData
    {
        public char letter;
        public int column;
        public int row;
        public int tileType;
    }

    [Serializable]
    public struct SaveData
    {
        public int SchemaVersion;
        public int Score;
        public DateTime Timestamp;
        public SavedLetterTileData[][] LetterTileData;

        public readonly string ToPrettyString()
        {
            return $"[{Timestamp:u}] score={Score}";
        }
    }
}
