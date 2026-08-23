using System;

namespace Egghead.SaveSystem
{
    public readonly struct SaveValidationResult
    {
        public SaveValidationResult(bool isValid, SaveData data, bool wasMigrated, string reason)
        {
            IsValid = isValid;
            Data = data;
            WasMigrated = wasMigrated;
            Reason = reason;
        }

        public bool IsValid { get; }
        public SaveData Data { get; }
        public bool WasMigrated { get; }
        public string Reason { get; }
    }

    public static class SaveDataValidator
    {
        public const int CurrentSchemaVersion = 1;
        private const int ColumnCount = 7;

        public static SaveValidationResult ValidateAndNormalize(SaveData data)
        {
            bool isLegacy = data.SchemaVersion == 0;
            if (!isLegacy && data.SchemaVersion != CurrentSchemaVersion)
            {
                return Invalid(data, $"unsupported schema version {data.SchemaVersion}");
            }

            if (data.Score < 0)
            {
                return Invalid(data, "score is negative");
            }

            if (data.Timestamp == default)
            {
                return Invalid(data, "timestamp is missing");
            }

            data.Timestamp = data.Timestamp.Kind switch
            {
                DateTimeKind.Utc => data.Timestamp,
                DateTimeKind.Local => data.Timestamp.ToUniversalTime(),
                _ => DateTime.SpecifyKind(data.Timestamp, DateTimeKind.Utc)
            };

            if (data.LetterTileData == null)
            {
                if (data.Score != 0)
                {
                    return Invalid(data, "new-game save has a nonzero score");
                }
            }
            else
            {
                string boardError = ValidateBoard(data.LetterTileData);
                if (boardError != null)
                {
                    return Invalid(data, boardError);
                }
            }

            data.SchemaVersion = CurrentSchemaVersion;
            return new SaveValidationResult(true, data, isLegacy, isLegacy ? "valid legacy save migrated to version 1" : "valid version 1 save");
        }

        private static string ValidateBoard(SavedLetterTileData[][] board)
        {
            if (board.Length != ColumnCount)
            {
                return $"board has {board.Length} columns; expected {ColumnCount}";
            }

            for (int column = 0; column < board.Length; column++)
            {
                SavedLetterTileData[] tiles = board[column];
                if (tiles == null)
                {
                    return $"column {column} is null";
                }

                int expectedRows = column % 2 == 0 ? 7 : 8;
                if (tiles.Length != expectedRows)
                {
                    return $"column {column} has {tiles.Length} rows; expected {expectedRows}";
                }

                for (int row = 0; row < tiles.Length; row++)
                {
                    SavedLetterTileData tile = tiles[row];
                    if (tile.column != column || tile.row != row)
                    {
                        return $"tile coordinate mismatch at column {column}, row {row}";
                    }

                    if (tile.letter < 'A' || tile.letter > 'Z')
                    {
                        return $"unsupported tile letter at column {column}, row {row}";
                    }

                    if (tile.tileType < 0 || tile.tileType > 4)
                    {
                        return $"invalid tile type at column {column}, row {row}";
                    }
                }
            }

            return null;
        }

        private static SaveValidationResult Invalid(SaveData data, string reason)
        {
            return new SaveValidationResult(false, data, false, reason);
        }
    }
}
