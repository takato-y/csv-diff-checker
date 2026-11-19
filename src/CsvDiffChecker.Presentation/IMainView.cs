using CsvDiffChecker.Core;

namespace CsvDiffChecker.Presentation;

/// <summary>ファイル欄に出す内容。</summary>
/// <param name="Path">表示するパス。未指定は空文字。</param>
/// <param name="Description">文字コード・行数・列数などの説明。</param>
/// <param name="Tone">説明の強調の度合い。</param>
public sealed record FileDisplay(string Path, string Description, DisplayTone Tone)
{
    public static FileDisplay Empty { get; } = new("", "", DisplayTone.Normal);
}

public enum DisplayTone
{
    Normal,
    Warning,
    Error,
}

/// <summary>操作の可否。画面はこのとおりにボタンと入力欄を有効・無効にする。</summary>
public sealed record CommandState(bool InputsEnabled, bool CanSwap, bool CanCompare, bool CanSave);

/// <summary>比較結果の要約。件数の文・確認が必要な行の文・列の違いの説明。</summary>
public sealed record ResultSummary(string CountsText, string IssuesText, string ColumnsText, bool HasColumnDifference);

/// <summary>
/// Presenter が画面に求める表示と問い合わせ。
/// 画面（WinForms）はこれを実装し、判断は Presenter に任せる。
/// すべてのメソッドは画面のスレッド（UI スレッド）から呼ばれる前提とする。
/// </summary>
public interface IMainView
{
    void ShowFile(FileSide side, FileDisplay display);

    /// <summary>照合する列の候補と選択を表示する。画面はこの表示の変更を Presenter に通知しない。</summary>
    void SetKeyCandidates(IReadOnlyList<string> columns, IReadOnlyCollection<string> selected);

    void ShowResult(ResultTable table, ResultSummary summary);

    void ClearResult(string message);

    /// <summary>「値の前後の空白を無視する」の表示を状態に合わせる。画面はこの表示の変更を Presenter に通知しない。</summary>
    void SetTrimValues(bool trimValues);

    void SetCommandState(CommandState state);

    void ShowStatus(string text);

    void ShowWarning(string text);

    void ShowInformation(string text);

    /// <summary>はい・いいえで問い合わせる。「はい」なら true。</summary>
    bool Confirm(string text);

    /// <summary>開くファイルを選ばせる。取り消したら null。</summary>
    string? AskOpenFile(FileSide side);

    /// <summary>保存先を選ばせる。取り消したら null。</summary>
    string? AskSaveFile(string defaultFileName);
}
