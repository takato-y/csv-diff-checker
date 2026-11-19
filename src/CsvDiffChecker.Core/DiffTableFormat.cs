using System.Globalization;

namespace CsvDiffChecker.Core;

/// <summary>
/// 比較結果を表（見出しと行）の形にする。画面の表と保存する CSV で同じ形を使う。
/// </summary>
public static class DiffTableFormat
{
    public const string ChangeArrow = " → ";

    /// <summary>見出しの前に置く固定の列の数（差分・照合する値・旧ファイルの行・新ファイルの行・変更した列）。</summary>
    public const int FixedColumnCount = 5;

    public static IReadOnlyList<string> BuildHeader(DiffResult result) =>
        ["差分", "照合する値", "旧ファイルの行", "新ファイルの行", "変更した列", .. result.Columns];

    public static string CellText(DiffEntry entry, string column)
    {
        var change = entry.Changes.FirstOrDefault(c => c.Column == column);
        return change is null ? entry.Values[column] : change.OldValue + ChangeArrow + change.NewValue;
    }

    public static IReadOnlyList<string> BuildRow(DiffResult result, DiffEntry entry) =>
    [
        entry.Kind.ToLabel(),
        entry.KeyText,
        entry.OldLine?.ToString(CultureInfo.InvariantCulture) ?? "",
        entry.NewLine?.ToString(CultureInfo.InvariantCulture) ?? "",
        string.Join("、", entry.Changes.Select(c => c.Column)),
        .. result.Columns.Select(c => CellText(entry, c)),
    ];
}
