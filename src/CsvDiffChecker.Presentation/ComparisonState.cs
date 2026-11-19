using CsvDiffChecker.Core;

namespace CsvDiffChecker.Presentation;

/// <summary>読み込んだファイル。</summary>
public sealed record LoadedFile(string Path, CsvTable Table);

/// <summary>
/// 画面の状態。値を変えられるのは Presentation の内部（<see cref="MainPresenter"/>）だけで、画面からは読むだけにする。
/// </summary>
public sealed class ComparisonState
{
    public LoadedFile? OldFile { get; private set; }
    public LoadedFile? NewFile { get; private set; }
    public IReadOnlyList<string> SelectedKeys { get; internal set; } = [];
    public bool TrimValues { get; internal set; } = true;
    public DiffResult? Result { get; internal set; }
    public bool IsBusy { get; internal set; }

    public bool BothLoaded => OldFile is not null && NewFile is not null;

    /// <summary>両方のファイルにある列（旧ファイルの並び順）。照合する列の候補になる。</summary>
    public IReadOnlyList<string> CommonColumns =>
        OldFile is null || NewFile is null
            ? []
            : OldFile.Table.Headers.Where(NewFile.Table.Headers.Contains).ToList();

    public LoadedFile? Get(FileSide side) => side == FileSide.Old ? OldFile : NewFile;

    internal void Set(FileSide side, LoadedFile? file)
    {
        if (side == FileSide.Old) OldFile = file; else NewFile = file;
    }

    /// <summary>入力として読み込んでいるファイルのフルパス。</summary>
    public IEnumerable<string> InputPaths =>
        new[] { OldFile?.Path, NewFile?.Path }.OfType<string>().Select(System.IO.Path.GetFullPath);
}
