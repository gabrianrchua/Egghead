using UnityEngine;
using System;
using System.Linq;

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
    }

    public class WordList
    {
        public Word[] words;
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
        int wordTableSize = wordData.Length / 2 - 1;

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
            newWord.word = wordData[2 * (i + 1)];
            newWord.definition = wordData[2 * (i + 1) + 1];
            wordList.words[i] = newWord;
        }

        // calculate letter weights and cache
        letterWeights = letterList.letters.Select(letter => 1f / Mathf.Pow(letter.bonus, rarityFactor)).ToArray();
        letterWeightsTotal = letterWeights.Sum();
    }
}
