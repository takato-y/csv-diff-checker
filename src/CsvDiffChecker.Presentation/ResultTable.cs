using CsvDiffChecker.Core;

namespace CsvDiffChecker.Presentation;

/// <summary>結果の表の列。</summary>
/// <param name="IsNumber">行番号など、数値として並べ替える列か。</param>
public sealed record ResultColumn(string Header, bool IsNumber);

/// <summary>結果の表のセル。</summary>
/// <param name="Value">表示と並べ替えに使う値（文字列か int。空の行番号は null）。</param>
/// <param name="ChangedFrom">変更したセルなら変更前の値。それ以外は null。</param>
/// <param name="ChangedTo">変更したセルなら変更後の値。それ以外は null。</param>
public sealed record ResultCell(object? Value, string? ChangedFrom = null, string? ChangedTo = null)
{
    public bool IsChanged => ChangedFrom is not null;
}

public enum ResultRowKind
{
    Changed,
    Added,
    Deleted,
}

/// <summary>結果の表の1行。</summary>
/// <param name="KindLabel">差分の種類の表示名（「変更」など）。絞り込みに使う。</param>
public sealed record ResultRow(ResultRowKind Kind, string KindLabel, IReadOnlyList<ResultCell> Cells);

/// <summary>確認が必要な行の表の1行。</summary>
public sealed record IssueRow(string FileLabel, int LineNumber, string Message);

/// <summary>画面に渡す結果の表。画面は列の意味を知らずに、このとおりに表示する。</summary>
public sealed record ResultTable(
    IReadOnlyList<ResultColumn> Columns,
    IReadOnlyList<ResultRow> Rows,
    IReadOnlyList<IssueRow> Issues,
    IReadOnlyList<string> KindLabels)
{
    /// <summary>
    /// 比較結果から表を作る。列の並びは保存する CSV と同じ（<see cref="DiffTableFormat"/>）。
    /// 行番号の列は数値として持ち、画面で数値として並べ替えられるようにする。
    /// </summary>
    public static ResultTable From(DiffResult result)
    {
        var headers = DiffTableFormat.BuildHeader(result);
        var columns = headers.Select((h, i) => new ResultColumn(h, IsLineNumberColumn(i))).ToList();

        var rows = result.Entries.Select(entry =>
        {
            var cells = new List<ResultCell>
            {
                new(entry.Kind.ToLabel()),
                new(entry.KeyText),
                new(entry.OldLine),
                new(entry.NewLine),
                new(string.Join("、", entry.Changes.Select(c => c.Column))),
            };
            foreach (var column in result.Columns)
            {
                var change = entry.Changes.FirstOrDefault(c => c.Column == column);
                cells.Add(new ResultCell(DiffTableFormat.CellText(entry, column), change?.OldValue, change?.NewValue));
            }
            return new ResultRow(ToRowKind(entry.Kind), entry.Kind.ToLabel(), cells);
        }).ToList();

        var issues = result.Issues.Select(i => new IssueRow(i.Side.ToLabel(), i.LineNumber, i.Message)).ToList();
        var kindLabels = new[] { DiffKind.Changed, DiffKind.Added, DiffKind.Deleted }.Select(k => k.ToLabel()).ToList();
        return new ResultTable(columns, rows, issues, kindLabels);
    }

    // 固定の列（差分・照合する値・旧ファイルの行・新ファイルの行・変更した列）のうち、行番号は 3・4 列目。
    private static bool IsLineNumberColumn(int index) => index is 2 or 3;

    private static ResultRowKind ToRowKind(DiffKind kind) => kind switch
    {
        DiffKind.Changed => ResultRowKind.Changed,
        DiffKind.Added => ResultRowKind.Added,
        _ => ResultRowKind.Deleted,
    };
}
