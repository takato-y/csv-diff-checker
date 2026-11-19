namespace CsvDiffChecker.Core;

/// <summary>ファイルを読み、見出しつきの表にする。</summary>
public interface ICsvTableLoader
{
    /// <summary>ファイルを読み込む。読めない場合は例外を投げる（種類は問わない）。元のファイルは変更しない。</summary>
    CsvTable Load(string path);
}

/// <summary>2つの表を照合する列で突き合わせ、差分を求める。</summary>
public interface IDiffEngine
{
    /// <summary>照合する列が未指定、またはどちらかの表に無い場合は <see cref="ArgumentException"/> を投げる。</summary>
    DiffResult Compare(CsvTable oldTable, CsvTable newTable, DiffOptions options);
}

/// <summary>比較結果をファイルに書き出す。</summary>
public interface IDiffResultWriter
{
    /// <summary>比較結果（差分）を書き出す。既存のファイルは置き換える。</summary>
    void Write(DiffResult result, string path);

    /// <summary>確認が必要な行を書き出す。既存のファイルは置き換える。</summary>
    void WriteIssues(DiffResult result, string path);

    /// <summary>差分の保存先に対応する、確認が必要な行の保存先を返す。</summary>
    string IssuesPathFor(string path);
}
