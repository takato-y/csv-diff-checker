namespace CsvDiffChecker.Core;

public enum DiffKind
{
    Changed,
    Added,
    Deleted,
}

public enum FileSide
{
    Old,
    New,
}

public static class DiffKindExtensions
{
    public static string ToLabel(this DiffKind kind) => kind switch
    {
        DiffKind.Changed => "変更",
        DiffKind.Added => "追加",
        DiffKind.Deleted => "削除",
        _ => kind.ToString(),
    };

    public static string ToLabel(this FileSide side) => side == FileSide.Old ? "旧ファイル" : "新ファイル";
}

public sealed class DiffOptions
{
    /// <summary>行を突き合わせる列（複数指定すると組み合わせで突き合わせる）。</summary>
    public required IReadOnlyList<string> KeyColumns { get; init; }

    /// <summary>値の前後の空白を無視して比べるか。キーは常に前後の空白を除いて突き合わせる。</summary>
    public bool TrimValues { get; init; } = true;
}

public sealed record CellChange(string Column, string OldValue, string NewValue);

/// <summary>差分1件。Values は表示用の値（追加・変更は新ファイル、削除は旧ファイルの値）。</summary>
public sealed class DiffEntry
{
    public required DiffKind Kind { get; init; }
    public required string KeyText { get; init; }
    public int? OldLine { get; init; }
    public int? NewLine { get; init; }
    public required IReadOnlyDictionary<string, string> Values { get; init; }
    public IReadOnlyList<CellChange> Changes { get; init; } = [];

    public bool IsChanged(string column) => Changes.Any(c => c.Column == column);
}

/// <summary>比較できなかった行（重複キー・キーが空・列数の不一致など）。</summary>
public sealed record DiffIssue(FileSide Side, int LineNumber, string Message);

public sealed class DiffResult
{
    /// <summary>両方のファイルにあり、比較の対象にした列（旧ファイルの並び順）。</summary>
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<string> KeyColumns { get; init; }
    public required IReadOnlyList<string> OnlyInOldColumns { get; init; }
    public required IReadOnlyList<string> OnlyInNewColumns { get; init; }
    public required IReadOnlyList<DiffEntry> Entries { get; init; }
    public required IReadOnlyList<DiffIssue> Issues { get; init; }
    public required int UnchangedCount { get; init; }

    public int Count(DiffKind kind) => Entries.Count(e => e.Kind == kind);
}
