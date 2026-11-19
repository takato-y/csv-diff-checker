namespace CsvDiffChecker.Core;

/// <summary>読み込めなかった行。比較には使わず、画面に一覧で示す。</summary>
/// <param name="Fields">その行の値。キーを取り出せる場合は、相手側の同じキーの行も比較から外すために使う。</param>
public sealed record RowProblem(int LineNumber, string Message, IReadOnlyList<string> Fields);

/// <summary>見出し行つきの CSV を読み込んだ結果。</summary>
public sealed class CsvTable
{
    public required IReadOnlyList<string> Headers { get; init; }
    public required IReadOnlyList<CsvRecord> Rows { get; init; }
    public required IReadOnlyList<RowProblem> Problems { get; init; }
    public string EncodingName { get; init; } = "";

    /// <summary>CSV の文字列から表を作る。見出しの重複・データなしは <see cref="CsvFormatException"/>。</summary>
    public static CsvTable FromText(string text, string encodingName = "")
    {
        var records = CsvParser.Parse(text);
        if (records.Count == 0)
            throw new CsvFormatException(1, "データがありません（見出し行が見つかりません）。");

        var header = records[0];
        var headers = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < header.Fields.Count; i++)
        {
            var name = header.Fields[i].Trim();
            if (name.Length == 0)
                name = $"（列{i + 1}）";
            if (!seen.Add(name))
                throw new CsvFormatException(header.LineNumber, $"見出し「{name}」が重複しています。列を区別できないため比較できません。");
            headers.Add(name);
        }

        var rows = new List<CsvRecord>();
        var problems = new List<RowProblem>();
        foreach (var record in records.Skip(1))
        {
            if (record.Fields.Count != headers.Count)
            {
                problems.Add(new RowProblem(record.LineNumber,
                    $"列の数が見出しと合いません（見出し {headers.Count} 列、この行 {record.Fields.Count} 列）。", record.Fields));
                continue;
            }
            rows.Add(record);
        }

        return new CsvTable { Headers = headers, Rows = rows, Problems = problems, EncodingName = encodingName };
    }
}
