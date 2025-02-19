using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;

public class CSVReader : MonoBehaviour
{
    [SerializeField] private TextAsset letters;
    [SerializeField] private TextAsset words;
    [SerializeField] private float rarityFactor; // weight = 1 / rarity ^ rarityFactor

    public class Letter
    {
        public char letter;
        public int bonus;
        public int scrabblePoints;
    }

    public class LetterList
    {
        public Letter[] letters;
    }

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
        public int Compare(Word x, Word y)
        {
            return string.Compare(x.word, y.word, StringComparison.OrdinalIgnoreCase);
        }
    }

    public WordList wordList = new WordList();
    public LetterList letterList = new LetterList();
    public float[] letterWeights; // calculate here and cache when random letter needed
    public float letterWeightsTotal;

    void Awake()
    {
        ReadCSV();
    }

    private void ReadCSV()
    {
        string[] letterData = letters.text.Split(new char[] { '\n', ',' }, StringSplitOptions.None);
        string[] wordData = words.text.Split(new char[] { '\n', ',' }, StringSplitOptions.None);

        // ignore first row
        int letterTableSize = letterData.Length / 3 - 1;
        int wordTableSize = wordData.Length / 3 - 1;

        letterList.letters = new Letter[letterTableSize];
        wordList.words = new Word[wordTableSize];

        for (int i = 0; i < letterTableSize; i++ )
        {
            Letter newLetter = new Letter();
            newLetter.letter = letterData[3 * (i + 1)][0];
            newLetter.bonus = int.Parse(letterData[3 * (i + 1) + 1]);
            newLetter.scrabblePoints = int.Parse(letterData[3 * (i + 1) + 2]);
            letterList.letters[i] = newLetter;
        }

        for (int i = 0; i < wordTableSize; i++)
        {
            Word newWord = new Word();
            newWord.word = wordData[3 * (i + 1)];
            newWord.definition = wordData[3 * (i + 1) + 1];
            newWord.points = int.Parse(wordData[3 * (i + 1) + 2]);
            wordList.words[i] = newWord;
        }

        // sort wordList for efficient binary search later
        Array.Sort(wordList.words, new WordComparer());

        // calculate letter weights and cache
        letterWeights = letterList.letters.Select(letter => 1f / Mathf.Pow(letter.bonus, rarityFactor)).ToArray();
        letterWeightsTotal = letterWeights.Sum();
    }
}
