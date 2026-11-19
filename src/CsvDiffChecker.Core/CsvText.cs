using System.Globalization;

namespace CsvDiffChecker.Core;

/// <summary>CSV の1行を作る。表計算ソフトで開いたときに数式として動く値は無害化する。</summary>
public static class CsvText
{
    public static string FormatLine(IEnumerable<string> fields) =>
        string.Join(",", fields.Select(f => Escape(Neutralize(f))));

    /// <summary>
    /// 表計算ソフトで開いたときに数式として動く値（=, +, -, @ などで始まる値）の先頭に ' を付ける。
    /// 「-100」「+81」のような数値は値が変わらないようそのままにする。
    /// </summary>
    public static string Neutralize(string value)
    {
        if (value.Length == 0) return value;
        var first = value[0];
        if (first is not ('=' or '+' or '-' or '@' or '\t' or '\r'))
            return value;
        if (first is '+' or '-' &&
            decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out _))
            return value;
        return "'" + value;
    }

    public static string Escape(string value)
    {
        var needsQuote = value.IndexOfAny([',', '"', '\r', '\n']) >= 0
                         || (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])));
        return needsQuote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
