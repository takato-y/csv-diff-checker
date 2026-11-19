using System.Text;

namespace CsvDiffChecker.Core;

/// <summary>CSV の1件分（1行とは限らない。引用符内の改行を含む場合がある）。</summary>
/// <param name="LineNumber">そのレコードが始まる行番号（1 始まり）。エラー表示に使う。</param>
public sealed record CsvRecord(int LineNumber, IReadOnlyList<string> Fields);

/// <summary>CSV として読めない形式だったときの例外。</summary>
public sealed class CsvFormatException(int lineNumber, string message)
    : Exception($"{lineNumber}行目: {message}")
{
    public int LineNumber { get; } = lineNumber;
}

/// <summary>
/// RFC 4180 に沿った CSV の読み取り。
/// 引用符で囲んだ値の中のカンマ・改行・二重引用符（""）に対応する。
/// 実務の CSV には規格から外れたものもあるため、引用符で始まらない値の途中に現れた引用符は文字として扱う。
/// </summary>
public static class CsvParser
{
    public static List<CsvRecord> Parse(string text)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldQuoted = false;   // 現在の値が引用符で始まったか
        var line = 1;
        var recordStartLine = 1;

        var start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
            fieldQuoted = false;
        }

        void EndRecord()
        {
            // 空行（値が1つで空、かつ引用符も無い）は読み飛ばす。"" だけの行は空の値1つとして残す。
            var isBlankLine = fields.Count == 0 && field.Length == 0 && !fieldQuoted;
            EndField();
            if (!isBlankLine)
                records.Add(new CsvRecord(recordStartLine, fields.ToArray()));
            fields.Clear();
        }

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n') line++;
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0 && !fieldQuoted:
                    inQuotes = true;
                    fieldQuoted = true;
                    break;
                case ',':
                    EndField();
                    break;
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    EndRecord();
                    line++;
                    recordStartLine = line;
                    break;
                case '\n':
                    EndRecord();
                    line++;
                    recordStartLine = line;
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (inQuotes)
            throw new CsvFormatException(recordStartLine, "引用符（\"）が閉じられていません。");

        // 最終行に改行が無い場合の残り
        if (field.Length > 0 || fields.Count > 0 || fieldQuoted)
            EndRecord();

        return records;
    }
}
