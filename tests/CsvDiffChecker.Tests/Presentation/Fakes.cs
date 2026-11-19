using CsvDiffChecker.Core;
using CsvDiffChecker.Presentation;

namespace CsvDiffChecker.Tests.Presentation;

/// <summary>画面の代わり。Presenter からの指示を記録し、問い合わせには決めておいた答えを返す。</summary>
internal sealed class FakeView : IMainView
{
    /// <summary>画面への指示を受けたスレッド。UI スレッドだけから呼ばれることを確かめるために使う。</summary>
    public HashSet<int> CallerThreads { get; } = [];
    public bool? TrimShown { get; private set; }
    public int OpenFileAsked { get; private set; }
    public int SaveFileAsked { get; private set; }

    private void Called() => CallerThreads.Add(Environment.CurrentManagedThreadId);

    public Dictionary<FileSide, FileDisplay> Files { get; } = new();
    public IReadOnlyList<string> KeyCandidates { get; private set; } = [];
    public IReadOnlyCollection<string> SelectedKeys { get; private set; } = [];
    public ResultTable? Result { get; private set; }
    public ResultSummary? Summary { get; private set; }
    public string ResultMessage { get; private set; } = "";
    public CommandState? Commands { get; private set; }
    public List<CommandState> CommandHistory { get; } = [];
    public string Status { get; private set; } = "";
    public List<string> Warnings { get; } = [];
    public List<string> Informations { get; } = [];
    public List<string> Confirmations { get; } = [];

    public bool ConfirmAnswer { get; set; } = true;
    public string? OpenFileAnswer { get; set; }
    public string? SaveFileAnswer { get; set; }

    public void ShowFile(FileSide side, FileDisplay display)
    {
        Called();
        Files[side] = display;
    }

    public void SetKeyCandidates(IReadOnlyList<string> columns, IReadOnlyCollection<string> selected)
    {
        Called();
        KeyCandidates = columns.ToList();
        SelectedKeys = selected.ToList();
    }

    public void ShowResult(ResultTable table, ResultSummary summary)
    {
        Called();
        Result = table;
        Summary = summary;
    }

    public void SetTrimValues(bool trimValues)
    {
        Called();
        TrimShown = trimValues;
    }

    public void ClearResult(string message)
    {
        Called();
        Result = null;
        Summary = null;
        ResultMessage = message;
    }

    public void SetCommandState(CommandState state)
    {
        Called();
        Commands = state;
        CommandHistory.Add(state);
    }

    public void ShowStatus(string text) => Status = text;
    public void ShowWarning(string text) => Warnings.Add(text);
    public void ShowInformation(string text) => Informations.Add(text);

    public bool Confirm(string text)
    {
        Confirmations.Add(text);
        return ConfirmAnswer;
    }

    public string? AskOpenFile(FileSide side)
    {
        OpenFileAsked++;
        return OpenFileAnswer;
    }

    public string? AskSaveFile(string defaultFileName)
    {
        SaveFileAsked++;
        return SaveFileAnswer;
    }
}

/// <summary>パスに対応する CSV の文字列から表を作る読み込み部品。登録の無いパスは読み込みに失敗する。</summary>
internal sealed class FakeLoader : ICsvTableLoader
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>設定すると、読み込みはこれが完了するまで終わらない（処理中の操作を試すため）。</summary>
    public TaskCompletionSource? Gate { get; set; }

    public int LoadCount { get; private set; }

    /// <summary>読み込みにかける時間。await の時点で読み込みが終わっていない状態を作るために使う。</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>設定すると、このパスの読み込みでファイルが見つからない以外の例外を投げる。</summary>
    public string? UnexpectedFailurePath { get; set; }

    public FakeLoader Add(string path, string csv)
    {
        _files[Path.GetFullPath(path)] = csv;
        return this;
    }

    public CsvTable Load(string path)
    {
        LoadCount++;
        Gate?.Task.Wait();
        if (Delay > TimeSpan.Zero) Thread.Sleep(Delay);
        if (UnexpectedFailurePath is { } bad && string.Equals(Path.GetFullPath(bad), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("想定外の失敗");
        if (!_files.TryGetValue(Path.GetFullPath(path), out var csv))
            throw new FileNotFoundException($"ファイルが見つかりません：{path}");
        return CsvTable.FromText(csv, "UTF-8");
    }
}

/// <summary>本物の差分判定を使い、呼ばれた回数を数える。</summary>
internal sealed class CountingEngine : IDiffEngine
{
    private readonly CsvDiffEngine _inner = new();
    public int CompareCount { get; private set; }
    public DiffOptions? LastOptions { get; private set; }

    public DiffResult Compare(CsvTable oldTable, CsvTable newTable, DiffOptions options)
    {
        CompareCount++;
        LastOptions = options;
        return _inner.Compare(oldTable, newTable, options);
    }
}

/// <summary>書き出しを記録する。指定した保存先では失敗させられる。</summary>
internal sealed class FakeWriter : IDiffResultWriter
{
    private readonly DiffCsvWriter _paths = new();
    public List<string> Written { get; } = [];
    public List<string> IssuesWritten { get; } = [];
    public bool FailWrite { get; set; }
    public bool FailIssues { get; set; }

    public void Write(DiffResult result, string path)
    {
        if (FailWrite) throw new IOException("別のアプリが使用中です。");
        Written.Add(path);
    }

    public void WriteIssues(DiffResult result, string path)
    {
        if (FailIssues) throw new IOException("別のアプリが使用中です。");
        IssuesWritten.Add(path);
    }

    public string IssuesPathFor(string path) => _paths.IssuesPathFor(path);
}
