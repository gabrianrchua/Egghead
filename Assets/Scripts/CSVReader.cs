using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;

public class CSVReader : MonoBehaviour
{
    [SerializeField] private TextAsset letters;
    [SerializeField] private TextAsset words;

    public class Letter
    {
        public char letter;
        // once used for letter rarity, now unused (now uses true word list letter distribution)
        public int bonus;
        public int scrabblePoints;
    }

    public class LetterList { public Letter[] letters; }

    public class Word
    {
        public string word;
        public string definition;
        public int points;
    }

    public class WordList
    {
        public Word[] words;
        public Word FindWord(string word)
        {
            int index = Array.BinarySearch(words, new Word { word = word }, new WordComparer());
            if (index >= 0) return words[index];
            throw new InvalidOperationException("Word " + word + " not found.");
        }
    }

    private class WordComparer : IComparer<Word>
    {
        public int Compare(Word x, Word y) =>
            string.Compare(x.word, y.word, StringComparison.OrdinalIgnoreCase);
    }

    public WordList wordList = new();
    public LetterList letterList = new();

    [HideInInspector] public float[] letterWeights;
    [HideInInspector] public float letterWeightsTotal;

    void Awake() => ReadCSV();

    private void ReadCSV()
    {
        string[] letterData = letters.text.Split(new char[] { '\n', ',' }, StringSplitOptions.None);
        string[] wordData = words.text.Split(new char[] { '\n', ',' }, StringSplitOptions.None);

        int letterTableSize = letterData.Length / 3 - 1;
        int wordTableSize = wordData.Length / 3 - 1;

        letterList.letters = new Letter[letterTableSize];
        wordList.words = new Word[wordTableSize];

        for (int i = 0; i < letterTableSize; i++)
        {
            Letter newLetter = new()
            {
                letter = letterData[3 * (i + 1)][0],
                bonus = int.Parse(letterData[3 * (i + 1) + 1]),
                scrabblePoints = int.Parse(letterData[3 * (i + 1) + 2])
            };
            letterList.letters[i] = newLetter;
        }

        for (int i = 0; i < wordTableSize; i++)
        {
            Word newWord = new()
            {
                word = wordData[3 * (i + 1)],
                definition = wordData[3 * (i + 1) + 1],
                points = int.Parse(wordData[3 * (i + 1) + 2])
            };
            wordList.words[i] = newWord;
        }

        Array.Sort(wordList.words, new WordComparer());

        Dictionary<char, int> freqDict = new();

        foreach (Word word in wordList.words)
        {
            foreach (char c in word.word.ToUpperInvariant())
            {
                if (char.IsLetter(c))
                {
                    freqDict[c] = freqDict.TryGetValue(c, out int count) ? count + 1 : 1;
                }
            }
        }

        // Normalize to probabilities (summing to 1)
        float total = freqDict.Values.Sum();
        letterWeights = letterList.letters
            .Select(l =>
            {
                if (!freqDict.TryGetValue(l.letter, out int count))
                    count = 1; // fallback for rare/unlisted letters
                return count / total;
            })
            .ToArray();

        letterWeightsTotal = letterWeights.Sum();
    }
}
