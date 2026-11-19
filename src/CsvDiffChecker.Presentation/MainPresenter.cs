using CsvDiffChecker.Core;

namespace CsvDiffChecker.Presentation;

/// <summary>
/// メイン画面の操作の流れを受け持つ。
/// 画面は <see cref="IMainView"/> 越しにだけ扱うため、偽の画面を渡して自動テストできる。
/// 公開メソッドは画面のスレッド（UI スレッド）から呼ぶ。重い処理だけを別スレッドで実行し、
/// await の後は呼び出し元の同期コンテキスト（UI スレッド）に戻って画面を更新する。
/// このため await に ConfigureAwait(false) を付けない。
/// </summary>
public sealed class MainPresenter
{
    private const string AppTitle = "リスト変更点チェッカー";

    private readonly IMainView _view;
    private readonly ICsvTableLoader _loader;
    private readonly IDiffEngine _engine;
    private readonly IDiffResultWriter _writer;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<DateTime> _now;

    public MainPresenter(IMainView view, ICsvTableLoader loader, IDiffEngine engine, IDiffResultWriter writer,
        Func<string, bool>? fileExists = null, Func<DateTime>? now = null)
    {
        _view = view;
        _loader = loader;
        _engine = engine;
        _writer = writer;
        _fileExists = fileExists ?? File.Exists;
        _now = now ?? (() => DateTime.Now);
    }

    public ComparisonState State { get; } = new();

    public static string Title => AppTitle;

    // ---------------------------------------------------------------- 画面からの操作（入口）

    /// <summary>画面を表示した直後に呼ぶ。起動引数が無くても、初期の表示とボタンの状態をそろえる。</summary>
    public Task StartAsync(StartupOptions options) => RunExclusiveAsync(async () =>
    {
        _view.ClearResult(MessageForFiles());
        if (options.OldPath is { } oldPath) await LoadCoreAsync(FileSide.Old, oldPath);
        if (options.NewPath is { } newPath) await LoadCoreAsync(FileSide.New, newPath);
        if (options.Keys.Count == 0 || !State.BothLoaded) return;

        var missing = options.Keys.Where(k => !State.CommonColumns.Contains(k)).ToList();
        if (missing.Count > 0)
        {
            _view.ShowWarning($"起動時に指定された照合する列「{string.Join("、", missing)}」が、2つのファイルに共通する列にありません。照合する列を選んでから「比較する」を押してください。");
            return;
        }
        SelectKeys(options.Keys);
        await CompareCoreAsync();
    });

    public Task BrowseAsync(FileSide side) => RunExclusiveAsync(async () =>
    {
        if (_view.AskOpenFile(side) is { } path) await LoadCoreAsync(side, path);
    });

    public Task LoadFileAsync(FileSide side, string path) => RunExclusiveAsync(() => LoadCoreAsync(side, path));

    /// <summary>ファイル欄へのドロップ。存在するファイルがちょうど1つのときだけ、その欄に読み込む。</summary>
    public Task DropOnFileAsync(FileSide side, IReadOnlyList<string> paths) => RunExclusiveAsync(async () =>
    {
        var files = paths.Where(_fileExists).ToList();
        if (files.Count == 1) await LoadCoreAsync(side, files[0]);
    });

    /// <summary>画面全体へのドロップ。1つなら空いている方へ、2つ以上なら先頭の2つを旧・新の順に読む。</summary>
    public Task DropFilesAsync(IReadOnlyList<string> paths) => RunExclusiveAsync(async () =>
    {
        var files = paths.Where(_fileExists).ToList();
        if (files.Count >= 2)
        {
            await LoadCoreAsync(FileSide.Old, files[0]);
            await LoadCoreAsync(FileSide.New, files[1]);
            // エクスプローラーから渡る順は選んだ順と一致するとは限らないため、割り当てを示す（両方読めたときだけ）。
            if (State.BothLoaded) _view.ShowStatus($"旧＝{Path.GetFileName(files[0])}、新＝{Path.GetFileName(files[1])} として読み込みました。逆の場合は「旧と新を入れ替える」を押してください。");
        }
        else if (files.Count == 1)
        {
            await LoadCoreAsync(State.OldFile is null ? FileSide.Old : FileSide.New, files[0]);
        }
    });

    /// <summary>
    /// 読み込み済みの2つのファイルをメモリ上で入れ替える。ファイルは読み直さない
    /// （読み込んだ後にファイルが更新されていても、入れ替えただけで中身が変わらないようにする）。
    /// 照合する列は両方にある列のまま変わらないため、選択はそのまま引き継がれる。
    /// </summary>
    public Task SwapAsync() => RunExclusiveAsync(() =>
    {
        var (oldFile, newFile) = (State.OldFile, State.NewFile);
        State.Set(FileSide.Old, newFile);
        State.Set(FileSide.New, oldFile);
        _view.ShowFile(FileSide.Old, newFile is null ? FileDisplay.Empty : Describe(newFile.Path, newFile.Table));
        _view.ShowFile(FileSide.New, oldFile is null ? FileDisplay.Empty : Describe(oldFile.Path, oldFile.Table));
        ClearResult(MessageForFiles());
        RefreshKeyCandidates();
        _view.ShowStatus("旧ファイルと新ファイルを入れ替えました。");
        return Task.CompletedTask;
    });

    public Task CompareAsync() => RunExclusiveAsync(CompareCoreAsync);

    public Task SaveAsync() => RunExclusiveAsync(() =>
    {
        SaveCore();
        return Task.CompletedTask;
    });

    /// <summary>照合する列の選択が変わった。前の結果を消す（前の設定の結果を取り違えないため）。</summary>
    public void ChangeKeys(IReadOnlyCollection<string> columns)
    {
        if (State.IsBusy)
        {
            // 処理中の変更は受け付けない。画面のチェックを状態に戻し、表示と状態を食い違わせない。
            _view.SetKeyCandidates(State.CommonColumns, State.SelectedKeys);
            return;
        }
        if (columns.ToHashSet().SetEquals(State.SelectedKeys)) return;
        State.SelectedKeys = State.CommonColumns.Where(columns.Contains).ToList();
        InvalidateResult();
        UpdateCommands();
    }

    /// <summary>「値の前後の空白を無視する」が変わった。前の結果を消す。</summary>
    public void ChangeTrim(bool trimValues)
    {
        if (State.IsBusy)
        {
            _view.SetTrimValues(State.TrimValues);
            return;
        }
        if (State.TrimValues == trimValues) return;
        State.TrimValues = trimValues;
        InvalidateResult();
        UpdateCommands();
    }

    // ---------------------------------------------------------------- 処理中の排他

    /// <summary>
    /// 読み込み・比較・入れ替え・保存は、どれか1つが終わるまで他を始めない。
    /// 途中で別の処理が割り込むと、表示しているファイルと結果が食い違うため、入口をすべてここに通す。
    /// </summary>
    private async Task RunExclusiveAsync(Func<Task> work)
    {
        if (State.IsBusy) return;
        State.IsBusy = true;
        UpdateCommands();
        try
        {
            await work();
        }
        finally
        {
            State.IsBusy = false;
            UpdateCommands();
        }
    }

    private void UpdateCommands()
    {
        var idle = !State.IsBusy;
        _view.SetCommandState(new CommandState(
            InputsEnabled: idle,
            CanSwap: idle && (State.OldFile is not null || State.NewFile is not null),
            CanCompare: idle && State.BothLoaded && State.SelectedKeys.Count > 0,
            CanSave: idle && State.Result is { } r && (r.Entries.Count > 0 || r.Issues.Count > 0)));
    }

    // ---------------------------------------------------------------- 読み込み

    private async Task LoadCoreAsync(FileSide side, string path)
    {
        try
        {
            var table = await Task.Run(() => _loader.Load(path));
            State.Set(side, new LoadedFile(path, table));
            _view.ShowFile(side, Describe(path, table));
        }
        catch (Exception ex)
        {
            // 失敗の種類を問わず「読み込めなかった」として状態をそろえる（前のファイルの表や結果を残さない）。
            State.Set(side, null);
            _view.ShowFile(side, new FileDisplay(path, "読み込めませんでした", DisplayTone.Error));
            var hint = ex is CsvFormatException ? "" : "\n\nファイルの場所と、開く権限があるかを確認してください。";
            _view.ShowWarning($"{side.ToLabel()}を読み込めませんでした。\n\n{ex.Message}{hint}");
        }

        ClearResult(MessageForFiles());
        RefreshKeyCandidates();
    }

    private static FileDisplay Describe(string path, CsvTable table)
    {
        var problems = table.Problems.Count > 0 ? $"・列数の合わない行 {table.Problems.Count}" : "";
        return new FileDisplay(path,
            $"{table.EncodingName}・{table.Rows.Count:N0}行・{table.Headers.Count}列{problems}",
            table.Problems.Count > 0 ? DisplayTone.Warning : DisplayTone.Normal);
    }

    /// <summary>両方のファイルにある列を照合する列の候補にする。選択済みの列は引き継ぎ、無ければ先頭の列を選ぶ。</summary>
    private void RefreshKeyCandidates()
    {
        var common = State.CommonColumns;
        var selected = common.Where(State.SelectedKeys.Contains).ToList();
        if (selected.Count == 0 && common.Count > 0) selected.Add(common[0]);
        State.SelectedKeys = selected;
        _view.SetKeyCandidates(common, selected);
    }

    private void SelectKeys(IEnumerable<string> keys)
    {
        var wanted = keys.ToHashSet();
        State.SelectedKeys = State.CommonColumns.Where(wanted.Contains).ToList();
        _view.SetKeyCandidates(State.CommonColumns, State.SelectedKeys);
    }

    private string MessageForFiles()
    {
        if (!State.BothLoaded) return "旧ファイルと新ファイルを選んでください。";
        return State.CommonColumns.Count == 0
            ? "2つのファイルに共通する列がありません。見出し行を確認してください。"
            : "照合する列を選んで「比較する」を押してください。";
    }

    // ---------------------------------------------------------------- 比較

    private async Task CompareCoreAsync()
    {
        if (State.OldFile is not { } oldFile || State.NewFile is not { } newFile) return;
        var options = new DiffOptions { KeyColumns = State.SelectedKeys.ToList(), TrimValues = State.TrimValues };
        try
        {
            var result = await Task.Run(() => _engine.Compare(oldFile.Table, newFile.Table, options));
            State.Result = result;
            _view.ShowResult(ResultTable.From(result), Summarize(result));
            _view.ShowStatus(result.Entries.Count == 0
                ? "差分はありません。"
                : "変更された項目は「変更前 → 変更後」と表示しています。列見出しをクリックすると並べ替えられます。");
        }
        catch (ArgumentException ex)
        {
            _view.ShowWarning(ex.Message);
        }
    }

    public static ResultSummary Summarize(DiffResult result)
    {
        static string List(IReadOnlyList<string> items) => items.Count == 0 ? "（なし）" : string.Join("、", items);

        var counts = $"変更 {result.Count(DiffKind.Changed):N0}件　追加 {result.Count(DiffKind.Added):N0}件　" +
                     $"削除 {result.Count(DiffKind.Deleted):N0}件　一致 {result.UnchangedCount:N0}件";
        var issues = result.Issues.Count > 0 ? $"⚠ 要確認 {result.Issues.Count:N0}行（比較から外しました）" : "";
        var columns = string.Join(Environment.NewLine,
            $"照合する列：{List(result.KeyColumns)}",
            $"比較した列：{List(result.Columns)}",
            "",
            $"旧ファイルだけにある列：{List(result.OnlyInOldColumns)}",
            $"新ファイルだけにある列：{List(result.OnlyInNewColumns)}",
            "",
            "片方のファイルにしかない列は比較していません。");
        return new ResultSummary(counts, issues, columns, result.OnlyInOldColumns.Count + result.OnlyInNewColumns.Count > 0);
    }

    private void ClearResult(string message)
    {
        State.Result = null;
        _view.ClearResult(message);
    }

    private void InvalidateResult()
    {
        if (State.Result is null) return;
        ClearResult("設定が変わりました。「比較する」を押してください。");
        _view.ShowStatus("前の結果は消しました。今の設定で比較し直すと結果が表示されます。");
    }

    // ---------------------------------------------------------------- 保存

    private void SaveCore()
    {
        if (State.Result is not { } result) return;
        if (_view.AskSaveFile($"差分結果_{_now():yyyyMMdd_HHmmss}.csv") is not { } chosen) return;

        // 入力ファイルを結果で上書きしてしまう誤操作を防ぐ（「_要確認」のファイルも対象にする）。
        var target = Path.GetFullPath(chosen);
        var issuesPath = _writer.IssuesPathFor(target);
        var inputs = State.InputPaths.ToList();
        bool IsInput(string path) => inputs.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        var hasIssues = result.Issues.Count > 0;

        if (IsInput(target) || (hasIssues && IsInput(issuesPath)))
        {
            _view.ShowWarning("比較元・比較先のファイルに上書きしてしまう名前です。別のファイル名を指定してください。");
            return;
        }
        if (hasIssues && _fileExists(issuesPath) &&
            !_view.Confirm($"確認が必要な行の保存先「{Path.GetFileName(issuesPath)}」が既にあります。上書きしますか？"))
        {
            return;
        }

        try
        {
            _writer.Write(result, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _view.ShowWarning($"保存できませんでした。\n\n{ex.Message}\n\n保存先のファイルを Excel などで開いている場合は閉じてから、もう一度保存してください。");
            return;
        }
        _view.ShowStatus($"保存しました：{target}");

        if (!hasIssues)
        {
            // 前回の保存で作った「_要確認」のファイルが残っていると、今回の結果と取り違えるため知らせる。
            if (_fileExists(issuesPath))
                _view.ShowInformation($"比較結果を保存しました。\n\n今回は確認が必要な行はありませんが、以前に保存した「{Path.GetFileName(issuesPath)}」が同じ場所に残っています。今回の結果とは関係ありません。");
            return;
        }

        try
        {
            // 差分の CSV だけを見た人が、比較から外した行を見落とさないよう別ファイルに出す。
            _writer.WriteIssues(result, issuesPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _view.ShowWarning($"比較結果は保存しましたが、確認が必要な行のファイルは保存できませんでした。\n\n{ex.Message}");
            return;
        }
        _view.ShowInformation($"比較結果を保存しました。\n\n比較から外した「確認が必要な行」{result.Issues.Count:N0}行は、次のファイルに保存しました。\n{Path.GetFileName(issuesPath)}");
        _view.ShowStatus($"保存しました：{target}（確認が必要な行：{Path.GetFileName(issuesPath)}）");
    }
}
