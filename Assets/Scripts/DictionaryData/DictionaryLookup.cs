using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Egghead.DictionaryData
{
    public sealed class DictionaryLookup
    {
        [StructLayout(LayoutKind.Explicit)]
        private struct Int32SingleUnion
        {
            [FieldOffset(0)] public int Int32;
            [FieldOffset(0)] public float Single;
        }

        private readonly byte[] data;
        private readonly LetterData[] letters;
        private readonly int blockOffsetsOffset;
        private readonly int definitionIndexOffset;
        private readonly int maximumWordLength;

        public int WordCount { get; }
        public int DefinitionCount { get; }
        public int LetterCount => letters.Length;
        public float LetterWeightsTotal { get; }

        public DictionaryLookup(byte[] data)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            EnsureRange(0, DictionaryDataFormat.FixedHeaderSize);

            if (ReadUInt32(DictionaryDataFormat.MagicOffset) != DictionaryDataFormat.Magic)
            {
                throw new InvalidDataException("Dictionary data has an invalid magic value. Regenerate the runtime asset.");
            }

            ushort version = ReadUInt16(DictionaryDataFormat.VersionOffset);
            if (version != DictionaryDataFormat.Version)
            {
                throw new InvalidDataException($"Dictionary data version {version} is unsupported; expected {DictionaryDataFormat.Version}. Regenerate the runtime asset.");
            }

            WordCount = ReadNonNegativeInt32(DictionaryDataFormat.WordCountOffset, "word count");
            int blockSize = ReadInt32(DictionaryDataFormat.BlockSizeOffset);
            int blockCount = ReadNonNegativeInt32(DictionaryDataFormat.BlockCountOffset, "block count");
            int letterCount = ReadNonNegativeInt32(DictionaryDataFormat.LetterCountOffset, "letter count");
            DefinitionCount = ReadNonNegativeInt32(DictionaryDataFormat.DefinitionCountOffset, "definition count");
            definitionIndexOffset = ReadNonNegativeInt32(DictionaryDataFormat.DefinitionsOffsetOffset, "definition offset");
            maximumWordLength = ReadNonNegativeInt32(DictionaryDataFormat.MaximumWordLengthOffset, "maximum word length");

            if (blockSize != DictionaryDataFormat.BlockSize || blockCount != ExpectedBlockCount(WordCount))
            {
                throw new InvalidDataException("Dictionary data has an invalid word-block index. Regenerate the runtime asset.");
            }

            if (letterCount != 26 || maximumWordLength > DictionaryDataFormat.MaximumWordLength)
            {
                throw new InvalidDataException("Dictionary data has invalid letter or word-length metadata. Regenerate the runtime asset.");
            }

            LetterWeightsTotal = ReadSingle(DictionaryDataFormat.LetterWeightsTotalOffset);
            letters = new LetterData[letterCount];
            int letterOffset = DictionaryDataFormat.FixedHeaderSize;
            for (int i = 0; i < letters.Length; i++)
            {
                EnsureRange(letterOffset, DictionaryDataFormat.LetterRecordSize);
                char letter = (char)data[letterOffset];
                int bonus = ReadInt32(letterOffset + 1);
                int scrabblePoints = ReadInt32(letterOffset + 5);
                float weight = ReadSingle(letterOffset + 9);
                letters[i] = new LetterData(letter, bonus, scrabblePoints, weight);
                letterOffset += DictionaryDataFormat.LetterRecordSize;
            }

            blockOffsetsOffset = letterOffset;
            EnsureRange(blockOffsetsOffset, checked(blockCount * sizeof(int)));
            if (definitionIndexOffset < blockOffsetsOffset + (blockCount * sizeof(int)) || definitionIndexOffset > data.Length)
            {
                throw new InvalidDataException("Dictionary data has an invalid definition-section offset. Regenerate the runtime asset.");
            }

            EnsureRange(definitionIndexOffset, checked(DefinitionCount * 2 * sizeof(int)));
        }

        public LetterData GetLetter(int index) => letters[index];

        public bool TryGetPoints(string word, out int points) => TryFindWord(word, out points, out _);

        public bool TryGetDefinition(string word, out string definition)
        {
            definition = null;
            if (!TryFindWord(word, out _, out int wordIndex) || DefinitionCount == 0)
            {
                return false;
            }

            int low = 0;
            int high = DefinitionCount - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                int entryOffset = definitionIndexOffset + (middle * 2 * sizeof(int));
                int candidateIndex = ReadInt32(entryOffset);
                if (candidateIndex < wordIndex)
                {
                    low = middle + 1;
                }
                else if (candidateIndex > wordIndex)
                {
                    high = middle - 1;
                }
                else
                {
                    int definitionOffset = ReadNonNegativeInt32(entryOffset + sizeof(int), "definition data offset");
                    int cursor = definitionOffset;
                    uint byteLength = ReadVarUInt32(ref cursor);
                    if (byteLength > int.MaxValue)
                    {
                        throw new InvalidDataException("Dictionary definition length exceeds the supported range.");
                    }

                    EnsureRange(cursor, (int)byteLength);
                    definition = Encoding.UTF8.GetString(data, cursor, (int)byteLength);
                    return true;
                }
            }

            return false;
        }

        internal byte[] CopyWordsSourceHash() => CopyHash(DictionaryDataFormat.WordsHashOffset);
        internal byte[] CopyLettersSourceHash() => CopyHash(DictionaryDataFormat.LettersHashOffset);

        private bool TryFindWord(string word, out int points, out int wordIndex)
        {
            points = 0;
            wordIndex = -1;
            if (!IsSupportedQuery(word) || WordCount == 0)
            {
                return false;
            }

            int blockCount = ExpectedBlockCount(WordCount);
            int low = 0;
            int high = blockCount - 1;
            int selectedBlock = -1;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                int blockOffset = ReadBlockOffset(middle);
                int comparison = CompareFirstWordToQuery(blockOffset, word);
                if (comparison <= 0)
                {
                    selectedBlock = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            if (selectedBlock < 0)
            {
                return false;
            }

            int cursor = ReadBlockOffset(selectedBlock);
            int count = Math.Min(DictionaryDataFormat.BlockSize, WordCount - (selectedBlock * DictionaryDataFormat.BlockSize));
            Span<byte> currentWord = stackalloc byte[maximumWordLength];
            int currentLength = 0;
            for (int i = 0; i < count; i++)
            {
                EnsureRange(cursor, 2);
                int prefixLength = data[cursor++];
                int suffixLength = data[cursor++];
                if (prefixLength > currentLength || prefixLength + suffixLength > currentWord.Length)
                {
                    throw new InvalidDataException("Dictionary data contains an invalid front-coded word.");
                }

                EnsureRange(cursor, suffixLength);
                data.AsSpan(cursor, suffixLength).CopyTo(currentWord.Slice(prefixLength));
                cursor += suffixLength;
                currentLength = prefixLength + suffixLength;
                uint rawPoints = ReadVarUInt32(ref cursor);
                if (rawPoints > int.MaxValue)
                {
                    throw new InvalidDataException("Dictionary data contains a score outside the supported range.");
                }

                int comparison = CompareWordToQuery(currentWord.Slice(0, currentLength), word);
                if (comparison == 0)
                {
                    points = (int)rawPoints;
                    wordIndex = (selectedBlock * DictionaryDataFormat.BlockSize) + i;
                    return true;
                }

                if (comparison > 0)
                {
                    return false;
                }
            }

            return false;
        }

        private int CompareFirstWordToQuery(int blockOffset, string query)
        {
            EnsureRange(blockOffset, 2);
            int prefixLength = data[blockOffset];
            int suffixLength = data[blockOffset + 1];
            if (prefixLength != 0 || suffixLength > maximumWordLength)
            {
                throw new InvalidDataException("Dictionary data contains an invalid block leader.");
            }

            EnsureRange(blockOffset + 2, suffixLength);
            return CompareWordToQuery(data.AsSpan(blockOffset + 2, suffixLength), query);
        }

        private static int CompareWordToQuery(ReadOnlySpan<byte> candidate, string query)
        {
            int sharedLength = Math.Min(candidate.Length, query.Length);
            for (int i = 0; i < sharedLength; i++)
            {
                int difference = candidate[i] - ToLowerAscii(query[i]);
                if (difference != 0)
                {
                    return difference;
                }
            }

            return candidate.Length - query.Length;
        }

        private static bool IsSupportedQuery(string word)
        {
            if (string.IsNullOrEmpty(word) || word.Length > DictionaryDataFormat.MaximumWordLength)
            {
                return false;
            }

            foreach (char character in word)
            {
                char lower = ToLowerAscii(character);
                if (lower < 'a' || lower > 'z')
                {
                    return false;
                }
            }

            return true;
        }

        private static char ToLowerAscii(char character) =>
            character >= 'A' && character <= 'Z' ? (char)(character + ('a' - 'A')) : character;

        private int ReadBlockOffset(int blockIndex)
        {
            int offset = ReadNonNegativeInt32(blockOffsetsOffset + (blockIndex * sizeof(int)), "word block offset");
            if (offset < blockOffsetsOffset || offset >= definitionIndexOffset)
            {
                throw new InvalidDataException("Dictionary data contains an invalid word-block offset.");
            }

            return offset;
        }

        private uint ReadVarUInt32(ref int cursor)
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                EnsureRange(cursor, 1);
                byte value = data[cursor++];
                if (shift == 28 && (value & 0xf0) != 0)
                {
                    throw new InvalidDataException("Dictionary data contains an invalid variable-length integer.");
                }

                result |= (uint)(value & 0x7f) << shift;
                if ((value & 0x80) == 0)
                {
                    return result;
                }
            }

            throw new InvalidDataException("Dictionary data contains an unterminated variable-length integer.");
        }

        private byte[] CopyHash(int offset)
        {
            byte[] hash = new byte[DictionaryDataFormat.HashSize];
            Buffer.BlockCopy(data, offset, hash, 0, hash.Length);
            return hash;
        }

        private static int ExpectedBlockCount(int wordCount) =>
            wordCount == 0 ? 0 : ((wordCount - 1) / DictionaryDataFormat.BlockSize) + 1;

        private int ReadNonNegativeInt32(int offset, string fieldName)
        {
            int value = ReadInt32(offset);
            if (value < 0)
            {
                throw new InvalidDataException($"Dictionary data has a negative {fieldName}.");
            }

            return value;
        }

        private ushort ReadUInt16(int offset)
        {
            EnsureRange(offset, sizeof(ushort));
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private uint ReadUInt32(int offset)
        {
            EnsureRange(offset, sizeof(uint));
            return (uint)(data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24));
        }

        private int ReadInt32(int offset) => unchecked((int)ReadUInt32(offset));

        private float ReadSingle(int offset)
        {
            return new Int32SingleUnion { Int32 = ReadInt32(offset) }.Single;
        }

        private void EnsureRange(int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > data.Length - length)
            {
                throw new InvalidDataException("Dictionary data is truncated or contains an invalid offset.");
            }
        }
    }
}
