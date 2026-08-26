namespace Egghead.DictionaryData
{
    internal static class DictionaryDataFormat
    {
        internal const uint Magic = 0x31444745; // EGD1
        internal const ushort Version = 1;
        internal const int BlockSize = 16;
        internal const int HashSize = 32;
        internal const int MaximumWordLength = 255;

        internal const int MagicOffset = 0;
        internal const int VersionOffset = 4;
        internal const int FlagsOffset = 6;
        internal const int WordCountOffset = 8;
        internal const int BlockSizeOffset = 12;
        internal const int BlockCountOffset = 16;
        internal const int LetterCountOffset = 20;
        internal const int DefinitionCountOffset = 24;
        internal const int DefinitionsOffsetOffset = 28;
        internal const int MaximumWordLengthOffset = 32;
        internal const int WordsHashOffset = 36;
        internal const int LettersHashOffset = WordsHashOffset + HashSize;
        internal const int LetterWeightsTotalOffset = LettersHashOffset + HashSize;
        internal const int FixedHeaderSize = LetterWeightsTotalOffset + sizeof(float);
        internal const int LetterRecordSize = sizeof(byte) + sizeof(int) + sizeof(int) + sizeof(float);
    }
}
