using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Egghead.DictionaryData.Editor;
using NUnit.Framework;

namespace Egghead.DictionaryData.Tests
{
    public class DictionaryDataTests
    {
        private const string WordsPath = "Assets/GameData/words.csv";
        private const string LettersPath = "Assets/GameData/letters.csv";
        private const string GeneratedPath = "Assets/GameData/dictionary.bytes";

        [Test]
        public void GeneratedAssetMatchesEverySourceEntryAndLetterWeight()
        {
            byte[] generated = File.ReadAllBytes(GeneratedPath);
            byte[] wordsSource = File.ReadAllBytes(WordsPath);
            byte[] lettersSource = File.ReadAllBytes(LettersPath);
            Assert.That(DictionaryDataCompiler.IsCurrent(generated, wordsSource, lettersSource, out string reason), Is.True, reason);

            DictionaryLookup lookup = new(generated);
            int rowCount = 0;
            int[] frequencies = new int[26];
            int totalCharacters = 0;
            foreach (string line in File.ReadLines(WordsPath).Skip(1))
            {
                string[] fields = line.Split(',');
                Assert.That(fields.Length, Is.EqualTo(3), $"Unexpected source shape at word row {rowCount + 2}.");
                int expectedPoints = int.Parse(fields[2], CultureInfo.InvariantCulture);
                if (!lookup.TryGetPoints(fields[0], out int actualPoints) || actualPoints != expectedPoints)
                {
                    Assert.Fail($"Generated lookup mismatch for '{fields[0]}': expected {expectedPoints}, found {actualPoints}.");
                }

                Assert.That(lookup.TryGetDefinition(fields[0], out _), Is.False);
                foreach (char character in fields[0])
                {
                    frequencies[character - 'a']++;
                    totalCharacters++;
                }

                rowCount++;
            }

            Assert.That(lookup.WordCount, Is.EqualTo(rowCount));
            Assert.That(lookup.DefinitionCount, Is.Zero);

            Dictionary<char, string[]> sourceLetters = File.ReadLines(LettersPath)
                .Skip(1)
                .Select(line => line.Split(','))
                .ToDictionary(fields => fields[0][0]);
            float expectedTotal = 0;
            for (int i = 0; i < lookup.LetterCount; i++)
            {
                LetterData actual = lookup.GetLetter(i);
                string[] expected = sourceLetters[actual.Letter];
                float expectedWeight = (frequencies[i] == 0 ? 1 : frequencies[i]) / (float)totalCharacters;
                expectedTotal += expectedWeight;
                Assert.That(actual.Bonus, Is.EqualTo(int.Parse(expected[1], CultureInfo.InvariantCulture)));
                Assert.That(actual.ScrabblePoints, Is.EqualTo(int.Parse(expected[2], CultureInfo.InvariantCulture)));
                Assert.That(actual.Weight, Is.EqualTo(expectedWeight).Within(0.0000001f));
            }

            Assert.That(lookup.LetterWeightsTotal, Is.EqualTo(expectedTotal).Within(0.0000001f));
        }

        [TestCase("aaa", 165)]
        [TestCase("AAA", 165)]
        [TestCase("AaA", 165)]
        [TestCase("queen", 461)]
        [TestCase("ZAQQUM", 821)]
        [TestCase("dichlorodiphenyltrichloroethane", 300017)]
        public void RepresentativeWordsReturnExpectedPoints(string word, int expectedPoints)
        {
            DictionaryLookup lookup = new(File.ReadAllBytes(GeneratedPath));
            Assert.That(lookup.TryGetPoints(word, out int points), Is.True);
            Assert.That(points, Is.EqualTo(expectedPoints));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("notaword")]
        [TestCase("café")]
        [TestCase("two words")]
        public void InvalidWordsAreAbsent(string word)
        {
            DictionaryLookup lookup = new(File.ReadAllBytes(GeneratedPath));
            Assert.That(lookup.TryGetPoints(word, out _), Is.False);
            Assert.That(lookup.TryGetDefinition(word, out _), Is.False);
        }

        [Test]
        public void QuotedAndMultilineDefinitionsRoundTripOnDemand()
        {
            byte[] words = Utf8(
                "Word,Definition,Points\r\n" +
                "cat,\"a small, \"\"quoted\"\" animal\",100\r\n" +
                "dog,\"line one\r\nline two\",200");
            DictionaryLookup lookup = Compile(words, ValidLetters());

            Assert.That(lookup.TryGetDefinition("CAT", out string catDefinition), Is.True);
            Assert.That(catDefinition, Is.EqualTo("a small, \"quoted\" animal"));
            Assert.That(lookup.TryGetDefinition("dog", out string dogDefinition), Is.True);
            Assert.That(dogDefinition, Is.EqualTo("line one\nline two"));
            Assert.That(lookup.TryGetPoints("dog", out int points), Is.True);
            Assert.That(points, Is.EqualTo(200));
        }

        [Test]
        public void GenerationIsDeterministic()
        {
            byte[] words = File.ReadAllBytes(WordsPath);
            byte[] letters = File.ReadAllBytes(LettersPath);
            Assert.That(DictionaryDataCompiler.Compile(words, letters), Is.EqualTo(DictionaryDataCompiler.Compile(words, letters)));
        }

        [TestCase("Word,Definition,Points\ncat,,100\nCAT,,101", "duplicate word")]
        [TestCase("Word,Definition,Points\ncan't,,100", "unsupported character")]
        [TestCase("Word,Definition,Points\ncat,,nope", "valid 32-bit integer")]
        [TestCase("Word,Definition,Points\ncat,,0", "greater than zero")]
        [TestCase("Word,Definition,Points\n\"cat,,100", "not terminated")]
        public void InvalidWordSourcesFailClearly(string wordsCsv, string expectedMessage)
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => DictionaryDataCompiler.Compile(Utf8(wordsCsv), ValidLetters()));
            StringAssert.Contains(expectedMessage, exception.Message);
            StringAssert.Contains("words.csv:", exception.Message);
        }

        [Test]
        public void MissingAndDuplicateLettersFailClearly()
        {
            byte[] words = Utf8("Word,Definition,Points\ncat,,100");
            InvalidDataException missing = Assert.Throws<InvalidDataException>(
                () => DictionaryDataCompiler.Compile(words, Utf8("Letter,Bonus,ScrabblePoints\nA,1,1")));
            StringAssert.Contains("expected exactly A-Z once", missing.Message);

            InvalidDataException duplicate = Assert.Throws<InvalidDataException>(
                () => DictionaryDataCompiler.Compile(words, Utf8(ValidLettersText() + "\na,1,1")));
            StringAssert.Contains("duplicate letter", duplicate.Message);
        }

        [Test]
        public void CorruptOrUnsupportedDataFailsClearly()
        {
            byte[] generated = DictionaryDataCompiler.Compile(
                Utf8("Word,Definition,Points\ncat,,100"),
                ValidLetters());

            byte[] unsupported = (byte[])generated.Clone();
            unsupported[4] = 99;
            StringAssert.Contains("unsupported", Assert.Throws<InvalidDataException>(() => new DictionaryLookup(unsupported)).Message);

            byte[] truncated = generated.Take(20).ToArray();
            StringAssert.Contains("truncated", Assert.Throws<InvalidDataException>(() => new DictionaryLookup(truncated)).Message);
        }

        private static DictionaryLookup Compile(byte[] words, byte[] letters) =>
            new(DictionaryDataCompiler.Compile(words, letters));

        private static byte[] ValidLetters() => Utf8(ValidLettersText());

        private static string ValidLettersText()
        {
            StringBuilder builder = new("Letter,Bonus,ScrabblePoints");
            for (char letter = 'A'; letter <= 'Z'; letter++)
            {
                builder.Append('\n').Append(letter).Append(",1,1");
            }

            return builder.ToString();
        }

        private static byte[] Utf8(string value) => new UTF8Encoding(false).GetBytes(value);
    }
}
