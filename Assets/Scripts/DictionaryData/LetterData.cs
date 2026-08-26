namespace Egghead.DictionaryData
{
    public readonly struct LetterData
    {
        public char Letter { get; }
        public int Bonus { get; }
        public int ScrabblePoints { get; }
        public float Weight { get; }

        internal LetterData(char letter, int bonus, int scrabblePoints, float weight)
        {
            Letter = letter;
            Bonus = bonus;
            ScrabblePoints = scrabblePoints;
            Weight = weight;
        }
    }
}
