using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Egghead.DictionaryData.Editor
{
    public static class DictionaryDataCompiler
    {
        private sealed class SourceWord
        {
            internal string Word { get; }
            internal string Definition { get; }
            internal int Points { get; }
            internal int LineNumber { get; }

            internal SourceWord(string word, string definition, int points, int lineNumber)
            {
                Word = word;
                Definition = definition;
                Points = points;
                LineNumber = lineNumber;
            }
        }

        private readonly struct SourceLetter
        {
            internal char Letter { get; }
            internal int Bonus { get; }
            internal int ScrabblePoints { get; }

            internal SourceLetter(char letter, int bonus, int scrabblePoints)
            {
                Letter = letter;
                Bonus = bonus;
                ScrabblePoints = scrabblePoints;
            }
        }

        public static byte[] Compile(byte[] wordsCsv, byte[] lettersCsv)
        {
            if (wordsCsv == null)
            {
                throw new ArgumentNullException(nameof(wordsCsv));
            }

            if (lettersCsv == null)
            {
                throw new ArgumentNullException(nameof(lettersCsv));
            }

            List<SourceWord> words = ParseWords(wordsCsv);
            SourceLetter[] letters = ParseLetters(lettersCsv);
            words.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Word, right.Word));
            for (int i = 1; i < words.Count; i++)
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(words[i - 1].Word, words[i].Word))
                {
                    throw new InvalidDataException(
                        $"words.csv:{words[i].LineNumber}: duplicate word '{words[i].Word}' conflicts with line {words[i - 1].LineNumber} (ordinal ignore-case).");
                }
            }

            int[] frequencies = new int[26];
            int characterCount = 0;
            int maximumWordLength = 0;
            foreach (SourceWord sourceWord in words)
            {
                maximumWordLength = Math.Max(maximumWordLength, sourceWord.Word.Length);
                foreach (char character in sourceWord.Word)
                {
                    frequencies[character - 'a']++;
                    characterCount++;
                }
            }

            if (characterCount == 0)
            {
                throw new InvalidDataException("words.csv: the dictionary must contain at least one word.");
            }

            float frequencyTotal = characterCount;
            float[] weights = new float[26];
            float weightsTotal = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                int frequency = frequencies[i] == 0 ? 1 : frequencies[i];
                weights[i] = frequency / frequencyTotal;
                weightsTotal += weights[i];
            }

            return WriteData(words, letters, weights, weightsTotal, maximumWordLength, Hash(wordsCsv), Hash(lettersCsv));
        }

        public static bool IsCurrent(byte[] generatedData, byte[] wordsCsv, byte[] lettersCsv, out string reason)
        {
            if (generatedData == null || generatedData.Length == 0)
            {
                reason = "the generated runtime asset is missing or empty";
                return false;
            }

            byte[] expected;
            try
            {
                expected = Compile(wordsCsv, lettersCsv);
            }
            catch (Exception exception)
            {
                reason = exception.Message;
                return false;
            }

            if (!generatedData.SequenceEqual(expected))
            {
                reason = "the generated runtime asset does not match the current CSV sources";
                return false;
            }

            reason = null;
            return true;
        }

        private static List<SourceWord> ParseWords(byte[] data)
        {
            List<CsvRecord> records = CsvRecordReader.Read(data, "words.csv");
            ValidateHeader(records, "words.csv", "Word", "Definition", "Points");
            List<SourceWord> words = new(records.Count - 1);
            for (int i = 1; i < records.Count; i++)
            {
                CsvRecord record = records[i];
                RequireFieldCount(record, "words.csv", 3);
                string word = NormalizeWord(record.Fields[0], record.LineNumber);
                int points = ParsePositiveInt32(record.Fields[2], "words.csv", record.LineNumber, "Points");
                words.Add(new SourceWord(word, record.Fields[1], points, record.LineNumber));
            }

            return words;
        }

        private static SourceLetter[] ParseLetters(byte[] data)
        {
            List<CsvRecord> records = CsvRecordReader.Read(data, "letters.csv");
            ValidateHeader(records, "letters.csv", "Letter", "Bonus", "ScrabblePoints");
            Dictionary<char, SourceLetter> letters = new();
            for (int i = 1; i < records.Count; i++)
            {
                CsvRecord record = records[i];
                RequireFieldCount(record, "letters.csv", 3);
                if (record.Fields[0].Length != 1 || !IsAsciiLetter(record.Fields[0][0]))
                {
                    throw new InvalidDataException($"letters.csv:{record.LineNumber}: Letter must be one ASCII A-Z character.");
                }

                char letter = char.ToUpperInvariant(record.Fields[0][0]);
                if (letters.ContainsKey(letter))
                {
                    throw new InvalidDataException($"letters.csv:{record.LineNumber}: duplicate letter '{letter}'.");
                }

                int bonus = ParseNonNegativeInt32(record.Fields[1], "letters.csv", record.LineNumber, "Bonus");
                int scrabblePoints = ParsePositiveInt32(record.Fields[2], "letters.csv", record.LineNumber, "ScrabblePoints");
                letters.Add(letter, new SourceLetter(letter, bonus, scrabblePoints));
            }

            List<char> missing = new();
            for (char letter = 'A'; letter <= 'Z'; letter++)
            {
                if (!letters.ContainsKey(letter))
                {
                    missing.Add(letter);
                }
            }

            if (missing.Count > 0 || letters.Count != 26)
            {
                throw new InvalidDataException($"letters.csv: expected exactly A-Z once; missing [{string.Join(", ", missing)}].");
            }

            return letters.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
        }

        private static byte[] WriteData(
            IReadOnlyList<SourceWord> words,
            IReadOnlyList<SourceLetter> letters,
            IReadOnlyList<float> weights,
            float weightsTotal,
            int maximumWordLength,
            byte[] wordsHash,
            byte[] lettersHash)
        {
            int blockCount = words.Count == 0 ? 0 : ((words.Count - 1) / DictionaryDataFormat.BlockSize) + 1;
            List<(int WordIndex, string Definition)> definitions = new();
            for (int i = 0; i < words.Count; i++)
            {
                if (!string.IsNullOrEmpty(words[i].Definition))
                {
                    definitions.Add((i, words[i].Definition));
                }
            }

            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream, new UTF8Encoding(false), true);
            writer.Write(DictionaryDataFormat.Magic);
            writer.Write(DictionaryDataFormat.Version);
            writer.Write((ushort)(definitions.Count > 0 ? 1 : 0));
            writer.Write(words.Count);
            writer.Write(DictionaryDataFormat.BlockSize);
            writer.Write(blockCount);
            writer.Write(letters.Count);
            writer.Write(definitions.Count);
            writer.Write(0); // definition section offset, patched below
            writer.Write(maximumWordLength);
            writer.Write(wordsHash);
            writer.Write(lettersHash);
            writer.Write(weightsTotal);

            for (int i = 0; i < letters.Count; i++)
            {
                writer.Write((byte)letters[i].Letter);
                writer.Write(letters[i].Bonus);
                writer.Write(letters[i].ScrabblePoints);
                writer.Write(weights[i]);
            }

            long blockIndexPosition = stream.Position;
            for (int i = 0; i < blockCount; i++)
            {
                writer.Write(0);
            }

            int[] blockOffsets = new int[blockCount];
            for (int block = 0; block < blockCount; block++)
            {
                blockOffsets[block] = CheckedPosition(stream);
                int firstIndex = block * DictionaryDataFormat.BlockSize;
                int count = Math.Min(DictionaryDataFormat.BlockSize, words.Count - firstIndex);
                string previousWord = string.Empty;
                for (int i = 0; i < count; i++)
                {
                    SourceWord sourceWord = words[firstIndex + i];
                    int prefixLength = i == 0 ? 0 : CommonPrefixLength(previousWord, sourceWord.Word);
                    int suffixLength = sourceWord.Word.Length - prefixLength;
                    writer.Write((byte)prefixLength);
                    writer.Write((byte)suffixLength);
                    writer.Write(Encoding.ASCII.GetBytes(sourceWord.Word.Substring(prefixLength)));
                    WriteVarUInt32(writer, (uint)sourceWord.Points);
                    previousWord = sourceWord.Word;
                }
            }

            int definitionIndexOffset = CheckedPosition(stream);
            long definitionIndexPosition = stream.Position;
            for (int i = 0; i < definitions.Count; i++)
            {
                writer.Write(definitions[i].WordIndex);
                writer.Write(0);
            }

            int[] definitionOffsets = new int[definitions.Count];
            for (int i = 0; i < definitions.Count; i++)
            {
                definitionOffsets[i] = CheckedPosition(stream);
                byte[] definitionBytes = Encoding.UTF8.GetBytes(definitions[i].Definition);
                WriteVarUInt32(writer, (uint)definitionBytes.Length);
                writer.Write(definitionBytes);
            }

            stream.Position = DictionaryDataFormat.DefinitionsOffsetOffset;
            writer.Write(definitionIndexOffset);
            stream.Position = blockIndexPosition;
            foreach (int offset in blockOffsets)
            {
                writer.Write(offset);
            }

            stream.Position = definitionIndexPosition;
            for (int i = 0; i < definitions.Count; i++)
            {
                writer.Write(definitions[i].WordIndex);
                writer.Write(definitionOffsets[i]);
            }

            writer.Flush();
            return stream.ToArray();
        }

        private static string NormalizeWord(string word, int lineNumber)
        {
            if (string.IsNullOrEmpty(word))
            {
                throw new InvalidDataException($"words.csv:{lineNumber}: Word must not be empty.");
            }

            if (word.Length > DictionaryDataFormat.MaximumWordLength)
            {
                throw new InvalidDataException($"words.csv:{lineNumber}: word exceeds {DictionaryDataFormat.MaximumWordLength} characters.");
            }

            StringBuilder normalized = new(word.Length);
            foreach (char character in word)
            {
                if (!IsAsciiLetter(character))
                {
                    throw new InvalidDataException($"words.csv:{lineNumber}: word '{word}' contains unsupported character '{character}'; only ASCII A-Z is supported.");
                }

                normalized.Append(char.ToLowerInvariant(character));
            }

            return normalized.ToString();
        }

        private static bool IsAsciiLetter(char character) =>
            (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z');

        private static int ParsePositiveInt32(string value, string sourceName, int lineNumber, string fieldName)
        {
            int result = ParseInt32(value, sourceName, lineNumber, fieldName);
            if (result <= 0)
            {
                throw new InvalidDataException($"{sourceName}:{lineNumber}: {fieldName} must be greater than zero.");
            }

            return result;
        }

        private static int ParseNonNegativeInt32(string value, string sourceName, int lineNumber, string fieldName)
        {
            int result = ParseInt32(value, sourceName, lineNumber, fieldName);
            if (result < 0)
            {
                throw new InvalidDataException($"{sourceName}:{lineNumber}: {fieldName} must not be negative.");
            }

            return result;
        }

        private static int ParseInt32(string value, string sourceName, int lineNumber, string fieldName)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            {
                throw new InvalidDataException($"{sourceName}:{lineNumber}: {fieldName} '{value}' is not a valid 32-bit integer.");
            }

            return result;
        }

        private static void ValidateHeader(List<CsvRecord> records, string sourceName, params string[] expected)
        {
            if (records.Count == 0)
            {
                throw new InvalidDataException($"{sourceName}: file is empty.");
            }

            RequireFieldCount(records[0], sourceName, expected.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                if (!string.Equals(records[0].Fields[i], expected[i], StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"{sourceName}:1: expected header '{string.Join(",", expected)}'.");
                }
            }
        }

        private static void RequireFieldCount(CsvRecord record, string sourceName, int expected)
        {
            if (record.Fields.Length != expected)
            {
                throw new InvalidDataException($"{sourceName}:{record.LineNumber}: expected {expected} fields but found {record.Fields.Length}.");
            }
        }

        private static int CommonPrefixLength(string left, string right)
        {
            int limit = Math.Min(left.Length, right.Length);
            int index = 0;
            while (index < limit && left[index] == right[index])
            {
                index++;
            }

            return index;
        }

        private static int CheckedPosition(Stream stream)
        {
            if (stream.Position > int.MaxValue)
            {
                throw new InvalidDataException("Generated dictionary data exceeds the supported 2 GB size.");
            }

            return (int)stream.Position;
        }

        private static void WriteVarUInt32(BinaryWriter writer, uint value)
        {
            while (value >= 0x80)
            {
                writer.Write((byte)(value | 0x80));
                value >>= 7;
            }

            writer.Write((byte)value);
        }

        private static byte[] Hash(byte[] data)
        {
            using SHA256 sha256 = SHA256.Create();
            return sha256.ComputeHash(data);
        }
    }
}
