using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Egghead.DictionaryData.Editor
{
    internal readonly struct CsvRecord
    {
        internal int LineNumber { get; }
        internal string[] Fields { get; }

        internal CsvRecord(int lineNumber, string[] fields)
        {
            LineNumber = lineNumber;
            Fields = fields;
        }
    }

    internal static class CsvRecordReader
    {
        internal static List<CsvRecord> Read(byte[] data, string sourceName)
        {
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(data);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException($"{sourceName}: source is not valid UTF-8.", exception);
            }

            int position = text.Length > 0 && text[0] == '\ufeff' ? 1 : 0;
            int lineNumber = 1;
            List<CsvRecord> records = new();
            while (position < text.Length)
            {
                int recordLine = lineNumber;
                List<string> fields = new();
                bool recordComplete = false;
                while (!recordComplete)
                {
                    StringBuilder field = new();
                    if (position < text.Length && text[position] == '"')
                    {
                        position++;
                        bool closed = false;
                        while (position < text.Length)
                        {
                            char character = text[position++];
                            if (character == '"')
                            {
                                if (position < text.Length && text[position] == '"')
                                {
                                    field.Append('"');
                                    position++;
                                    continue;
                                }

                                closed = true;
                                break;
                            }

                            if (character == '\r' || character == '\n')
                            {
                                ConsumeLineEnding(text, ref position, character);
                                field.Append('\n');
                                lineNumber++;
                            }
                            else
                            {
                                field.Append(character);
                            }
                        }

                        if (!closed)
                        {
                            throw Error(sourceName, recordLine, "quoted field is not terminated");
                        }

                        if (position < text.Length && text[position] != ',' && text[position] != '\r' && text[position] != '\n')
                        {
                            throw Error(sourceName, lineNumber, "unexpected character after a closing quote");
                        }
                    }
                    else
                    {
                        while (position < text.Length && text[position] != ',' && text[position] != '\r' && text[position] != '\n')
                        {
                            if (text[position] == '"')
                            {
                                throw Error(sourceName, lineNumber, "quote appears inside an unquoted field");
                            }

                            field.Append(text[position++]);
                        }
                    }

                    fields.Add(field.ToString());
                    if (position >= text.Length)
                    {
                        recordComplete = true;
                    }
                    else if (text[position] == ',')
                    {
                        position++;
                    }
                    else
                    {
                        char lineEnding = text[position++];
                        ConsumeLineEnding(text, ref position, lineEnding);
                        lineNumber++;
                        recordComplete = true;
                    }
                }

                records.Add(new CsvRecord(recordLine, fields.ToArray()));
            }

            return records;
        }

        private static void ConsumeLineEnding(string text, ref int position, char firstCharacter)
        {
            if (firstCharacter == '\r' && position < text.Length && text[position] == '\n')
            {
                position++;
            }
        }

        private static InvalidDataException Error(string sourceName, int lineNumber, string message) =>
            new($"{sourceName}:{lineNumber}: {message}.");
    }
}
