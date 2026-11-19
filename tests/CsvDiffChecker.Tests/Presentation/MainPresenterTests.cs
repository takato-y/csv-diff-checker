using CsvDiffChecker.Core;
using CsvDiffChecker.Presentation;

namespace CsvDiffChecker.Tests.Presentation;

/// <summary>
/// 画面の操作の流れ（FN-004）を、偽の画面で確かめる。
/// 画面を操作せずに、ボタンの可否・処理中の排他・結果の取り違え防止・保存の守りを検査する。
/// </summary>
public class MainPresenterTests
{
    private const string OldPath = @"C:\work\old.csv";
    private const string NewPath = @"C:\work\new.csv";
    private const string OldCsv = "code,name,price\nA1,ペン,100\nA2,ノート,200\nA3,のり,80\n";
    private const string NewCsv = "code,name,price\nA1,ペン,120\nA3,のり,80\nA4,定規,150\n";

    private readonly FakeView _view = new();
    private readonly FakeLoader _loader = new FakeLoader().Add(OldPath, OldCsv).Add(NewPath, NewCsv);
    private readonly CountingEngine _engine = new();
    private readonly FakeWriter _writer = new();
    private readonly HashSet<string> _existingFiles = new(StringComparer.OrdinalIgnoreCase) { OldPath, NewPath };
    private readonly MainPresenter _presenter;

    public MainPresenterTests()
    {
        _presenter = new MainPresenter(_view, _loader, _engine, _writer,
            fileExists: path => _existingFiles.Contains(Path.GetFullPath(path)),
            now: () => new DateTime(2026, 11, 19, 10, 0, 0));
    }

    private async Task LoadBothAsync()
    {
        await _presenter.LoadFileAsync(FileSide.Old, OldPath);
        await _presenter.LoadFileAsync(FileSide.New, NewPath);
    }

    private async Task LoadAndCompareAsync()
    {
        await LoadBothAsync();
        await _presenter.CompareAsync();
    }

    // ---------------------------------------------------------------- 起動

    [Fact]
    public async Task 起動直後はファイルの指定を促しボタンはすべて押せない()
    {
        await _presenter.StartAsync(StartupOptions.None);
        Assert.Equal("旧ファイルと新ファイルを選んでください。", _view.ResultMessage);
        Assert.Equal(new CommandState(true, false, false, false), _view.Commands);
    }

    [Fact]
    public async Task 起動引数でファイルと照合する列がそろえばそのまま比較する()
    {
        await _presenter.StartAsync(new StartupOptions(OldPath, NewPath, ["code"]));
        Assert.Equal(1, _engine.CompareCount);
        Assert.NotNull(_view.Result);
        Assert.Equal("変更 1件　追加 1件　削除 1件　一致 1件", _view.Summary!.CountsText);
    }

    [Fact]
    public async Task 起動引数の照合する列が無ければ比較せずに知らせる()
    {
        await _presenter.StartAsync(new StartupOptions(OldPath, NewPath, ["コード"]));
        Assert.Equal(0, _engine.CompareCount);
        Assert.Contains("コード", Assert.Single(_view.Warnings));
    }

    // ---------------------------------------------------------------- 読み込み

    [Fact]
    public async Task 両方を読み込むと共通の列を候補にし先頭の列を選ぶ()
    {
        await LoadBothAsync();
        Assert.Equal(["code", "name", "price"], _view.KeyCandidates);
        Assert.Equal(["code"], _view.SelectedKeys);
        Assert.Equal("UTF-8・3行・3列", _view.Files[FileSide.Old].Description);
        Assert.True(_view.Commands!.CanCompare);
    }

    [Fact]
    public async Task 読み込みに失敗したら状態と前の結果を消して知らせる()
    {
        await LoadAndCompareAsync();
        await _presenter.LoadFileAsync(FileSide.Old, @"C:\work\missing.csv");

        Assert.Null(_presenter.State.OldFile);
        Assert.Null(_view.Result);
        Assert.Equal(DisplayTone.Error, _view.Files[FileSide.Old].Tone);
        Assert.Contains("旧ファイルを読み込めませんでした", Assert.Single(_view.Warnings));
        Assert.Equal(new CommandState(true, true, false, false), _view.Commands);
    }

    [Fact]
    public async Task 想定外の種類の例外で失敗しても読み込めなかったとして扱う()
    {
        await LoadAndCompareAsync();
        _loader.UnexpectedFailurePath = OldPath;
        await _presenter.LoadFileAsync(FileSide.Old, OldPath);
        Assert.Null(_presenter.State.OldFile);
        Assert.Null(_view.Result);
        Assert.Contains("想定外の失敗", Assert.Single(_view.Warnings));
    }

    [Fact]
    public async Task 列の数が合わない行があればファイル欄を警告の表示にする()
    {
        _loader.Add(@"C:\work\broken.csv", "code,name\nA1,ペン\nA2\n");
        await _presenter.LoadFileAsync(FileSide.Old, @"C:\work\broken.csv");
        Assert.Equal(DisplayTone.Warning, _view.Files[FileSide.Old].Tone);
        Assert.EndsWith("列数の合わない行 1", _view.Files[FileSide.Old].Description);
    }

    [Fact]
    public async Task 共通の列が無ければその旨を示し比較できない()
    {
        _loader.Add(@"C:\work\other.csv", "id,value\n1,x\n");
        await _presenter.LoadFileAsync(FileSide.Old, OldPath);
        await _presenter.LoadFileAsync(FileSide.New, @"C:\work\other.csv");
        Assert.Contains("共通する列がありません", _view.ResultMessage);
        Assert.False(_view.Commands!.CanCompare);
    }

    [Fact]
    public async Task 一つだけドロップしたら空いている方に入れる()
    {
        await _presenter.DropFilesAsync([OldPath]);
        await _presenter.DropFilesAsync([NewPath]);
        Assert.Equal(OldPath, _presenter.State.OldFile!.Path);
        Assert.Equal(NewPath, _presenter.State.NewFile!.Path);
    }

    [Fact]
    public async Task ファイル欄へのドロップは存在するファイルが1つのときだけ読む()
    {
        _existingFiles.Add(@"C:\work\extra.csv");
        await _presenter.DropOnFileAsync(FileSide.New, [OldPath, NewPath]);
        await _presenter.DropOnFileAsync(FileSide.New, [@"C:\work\nothing.csv"]);
        Assert.Null(_presenter.State.NewFile);
        await _presenter.DropOnFileAsync(FileSide.New, [NewPath]);
        Assert.Equal(NewPath, _presenter.State.NewFile!.Path);
    }

    [Fact]
    public async Task まとめてドロップして読み込みに失敗したら割り当ての案内を出さない()
    {
        _existingFiles.Add(@"C:\work\unreadable.csv");
        await _presenter.DropFilesAsync([OldPath, @"C:\work\unreadable.csv"]);
        Assert.DoesNotContain("読み込みました", _view.Status);
        Assert.Single(_view.Warnings);
    }

    [Fact]
    public async Task まとめてドロップしたら旧新の割り当てを示す()
    {
        await _presenter.DropFilesAsync([OldPath, NewPath]);
        Assert.Contains("旧＝old.csv、新＝new.csv", _view.Status);
        Assert.Contains("入れ替える", _view.Status);
    }

    // ---------------------------------------------------------------- 比較と設定の変更

    [Fact]
    public async Task 比較の結果を表示し保存できるようにする()
    {
        await LoadAndCompareAsync();
        Assert.NotNull(_view.Result);
        Assert.Equal(new CommandState(true, true, true, true), _view.Commands);
        Assert.True(_engine.LastOptions!.TrimValues);
    }

    [Fact]
    public async Task 照合する列を変えたら前の結果を消す()
    {
        await LoadAndCompareAsync();
        _presenter.ChangeKeys(["code", "name"]);
        Assert.Null(_view.Result);
        Assert.Equal("設定が変わりました。「比較する」を押してください。", _view.ResultMessage);
        Assert.False(_view.Commands!.CanSave);
        Assert.Equal(["code", "name"], _presenter.State.SelectedKeys);
    }

    [Fact]
    public async Task 照合する列が同じなら結果を消さない()
    {
        await LoadAndCompareAsync();
        _presenter.ChangeKeys(["code"]);
        Assert.NotNull(_view.Result);
    }

    [Fact]
    public async Task 空白の設定を変えたら前の結果を消し次の比較に反映する()
    {
        await LoadAndCompareAsync();
        _presenter.ChangeTrim(false);
        Assert.Null(_view.Result);
        await _presenter.CompareAsync();
        Assert.False(_engine.LastOptions!.TrimValues);
    }

    [Fact]
    public async Task 照合する列をすべて外したら比較できない()
    {
        await LoadBothAsync();
        _presenter.ChangeKeys([]);
        Assert.False(_view.Commands!.CanCompare);
    }

    [Fact]
    public async Task 入れ替えると旧新が逆になり選んでいた照合する列を引き継ぐ()
    {
        await LoadBothAsync();
        _presenter.ChangeKeys(["name"]);
        var loadCount = _loader.LoadCount;
        await _presenter.SwapAsync();
        Assert.Equal(NewPath, _presenter.State.OldFile!.Path);
        Assert.Equal(OldPath, _presenter.State.NewFile!.Path);
        Assert.Equal(["name"], _view.SelectedKeys);
        // ファイルは読み直さない（読み込んだ後にファイルが変わっても、入れ替えで中身が変わらない）。
        Assert.Equal(loadCount, _loader.LoadCount);
    }

    // ---------------------------------------------------------------- 処理中の排他

    [Fact]
    public async Task 処理中はすべての操作を止め届いた操作は何もしない()
    {
        await LoadAndCompareAsync();
        var compareCountBefore = _engine.CompareCount;
        var loadCountBefore = _loader.LoadCount;
        _view.OpenFileAnswer = OldPath;
        _view.SaveFileAnswer = @"C:\work\結果.csv";
        _loader.Gate = new TaskCompletionSource();

        var loading = _presenter.LoadFileAsync(FileSide.Old, OldPath);
        Assert.True(_presenter.State.IsBusy);
        Assert.Equal(new CommandState(false, false, false, false), _view.Commands);

        // 読み込みの途中に届いた操作は、どの入口から来ても何もしない。
        await _presenter.CompareAsync();
        await _presenter.SwapAsync();
        await _presenter.LoadFileAsync(FileSide.New, OldPath);
        await _presenter.BrowseAsync(FileSide.New);
        await _presenter.DropFilesAsync([OldPath, NewPath]);
        await _presenter.DropOnFileAsync(FileSide.New, [OldPath]);
        await _presenter.SaveAsync();
        await _presenter.StartAsync(new StartupOptions(NewPath, OldPath, ["name"]));
        _presenter.ChangeKeys(["name"]);
        _presenter.ChangeTrim(false);

        // 捨てた設定の変更は、画面の表示を状態に戻す。
        Assert.Equal(["code"], _view.SelectedKeys);
        Assert.True(_view.TrimShown);

        _loader.Gate.SetResult();
        await loading;

        Assert.Equal(compareCountBefore, _engine.CompareCount);
        Assert.Equal(loadCountBefore + 1, _loader.LoadCount);
        Assert.Equal(0, _view.OpenFileAsked);
        Assert.Equal(0, _view.SaveFileAsked);
        Assert.Empty(_writer.Written);
        Assert.Equal(NewPath, _presenter.State.NewFile!.Path);
        Assert.Equal(["code"], _presenter.State.SelectedKeys);
        Assert.True(_presenter.State.TrimValues);
        Assert.False(_presenter.State.IsBusy);
        Assert.True(_view.Commands!.CanCompare);
    }

    [Fact]
    public void 画面への指示はすべて呼び出し元のスレッドから行う()
    {
        // WinForms と同じく、await の後が呼び出し元のスレッドに戻る同期コンテキストの上で動かす。
        // 読み込みに時間をかけ、await の時点で処理が終わっていない（後続が同期コンテキストに戻される）状態を作る。
        _loader.Delay = TimeSpan.FromMilliseconds(50);
        SingleThreadContext.Run(async () =>
        {
            await LoadAndCompareAsync();
            await _presenter.SwapAsync();
            await _presenter.CompareAsync();
            _view.SaveFileAnswer = @"C:\work\結果.csv";
            await _presenter.SaveAsync();
        });
        Assert.Single(_view.CallerThreads);
        Assert.NotNull(_view.Result);
    }

    [Fact]
    public async Task 処理中に例外が出てもボタンの状態は戻る()
    {
        var presenter = new MainPresenter(_view, _loader, new ThrowingEngine(), _writer);
        await presenter.LoadFileAsync(FileSide.Old, OldPath);
        await presenter.LoadFileAsync(FileSide.New, NewPath);
        await Assert.ThrowsAsync<InvalidOperationException>(presenter.CompareAsync);
        Assert.False(presenter.State.IsBusy);
        Assert.True(_view.Commands!.InputsEnabled);
    }

    private sealed class ThrowingEngine : IDiffEngine
    {
        public DiffResult Compare(CsvTable oldTable, CsvTable newTable, DiffOptions options) =>
            throw new InvalidOperationException("想定外");
    }

    // ---------------------------------------------------------------- 保存

    [Fact]
    public async Task 保存先を選ぶと結果を書き出す()
    {
        await LoadAndCompareAsync();
        _view.SaveFileAnswer = @"C:\work\結果.csv";
        await _presenter.SaveAsync();
        Assert.Equal([Path.GetFullPath(@"C:\work\結果.csv")], _writer.Written);
        Assert.Contains("保存しました", _view.Status);
    }

    [Fact]
    public async Task 保存先の選択を取り消したら何も書かない()
    {
        await LoadAndCompareAsync();
        _view.SaveFileAnswer = null;
        await _presenter.SaveAsync();
        Assert.Empty(_writer.Written);
    }

    [Fact]
    public async Task 入力ファイルと同じ名前には保存しない()
    {
        await LoadAndCompareAsync();
        _view.SaveFileAnswer = NewPath;
        await _presenter.SaveAsync();
        Assert.Empty(_writer.Written);
        Assert.Contains("上書きしてしまう名前", Assert.Single(_view.Warnings));
    }

    [Fact]
    public async Task 確認が必要な行がある場合は別のファイルにも保存する()
    {
        _loader.Add(NewPath, NewCsv + "A4,定規（重複）,150\n");
        await LoadAndCompareAsync();
        _view.SaveFileAnswer = @"C:\work\結果.csv";
        await _presenter.SaveAsync();
        Assert.Equal([Path.GetFullPath(@"C:\work\結果_要確認.csv")], _writer.IssuesWritten);
        Assert.Contains("結果_要確認.csv", Assert.Single(_view.Informations));
    }

    [Fact]
    public async Task 確認が必要な行の保存先が入力ファイルなら保存しない()
    {
        _loader.Add(@"C:\work\結果_要確認.csv", OldCsv).Add(NewPath, NewCsv + "A4,重複,1\n");
        _existingFiles.Add(@"C:\work\結果_要確認.csv");
        await _presenter.LoadFileAsync(FileSide.Old, @"C:\work\結果_要確認.csv");
        await _presenter.LoadFileAsync(FileSide.New, NewPath);
        await _presenter.CompareAsync();

        _view.SaveFileAnswer = @"C:\work\結果.csv";
        await _presenter.SaveAsync();
        Assert.Empty(_writer.Written);
        Assert.Single(_view.Warnings);
    }

    [Fact]
    public async Task 確認が必要な行の保存先が既にあれば上書きを確かめる()
    {
        _loader.Add(NewPath, NewCsv + "A4,重複,1\n");
        _existingFiles.Add(@"C:\work\結果_要確認.csv");
        await LoadAndCompareAsync();
        _view.SaveFileAnswer = @"C:\work\結果.csv";
        _view.ConfirmAnswer = false;

        await _presenter.SaveAsync();
        Assert.Single(_view.Confirmations);
        Assert.Empty(_writer.Written);
    }

    [Fact]
    public async Task 確認が必要な行が無いのに古い要確認ファイルが残っていれば知らせる()
    {
        _existingFiles.Add(@"C:\work\結果_要確認.csv");
        await LoadAndCompareAsync();
        _view.SaveFileAnswer = @"C:\work\結果.csv";
        await _presenter.SaveAsync();
        Assert.Single(_writer.Written);
        Assert.Empty(_writer.IssuesWritten);
        Assert.Contains("以前に保存した", Assert.Single(_view.Informations));
    }

    [Fact]
    public async Task 保存に失敗したら知らせる()
    {
        await LoadAndCompareAsync();
        _view.SaveFileAnswer = @"C:\work\結果.csv";
        _writer.FailWrite = true;
        await _presenter.SaveAsync();
        Assert.StartsWith("保存できませんでした", Assert.Single(_view.Warnings));
        Assert.True(_view.Commands!.CanSave);
    }

    [Fact]
    public async Task 要確認ファイルだけ失敗したら差分は保存できたことを知らせる()
    {
        _loader.Add(NewPath, NewCsv + "A4,重複,1\n");
        await LoadAndCompareAsync();
        _view.SaveFileAnswer = @"C:\work\結果.csv";
        _writer.FailIssues = true;
        await _presenter.SaveAsync();
        Assert.Single(_writer.Written);
        Assert.StartsWith("比較結果は保存しましたが", Assert.Single(_view.Warnings));
    }
}
