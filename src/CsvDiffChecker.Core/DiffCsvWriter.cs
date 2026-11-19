using System.Globalization;
using System.Text;

namespace CsvDiffChecker.Core;

/// <summary>
/// 比較結果を CSV に書き出す。
/// Excel でそのまま開けるよう UTF-8（BOM付き）で書き、数式として解釈される値は無害化する。
/// </summary>
public sealed class DiffCsvWriter : IDiffResultWriter
{
    public void Write(DiffResult result, string path) => WriteAtomically(path, writer => WriteTo(result, writer));

    /// <summary>比較から外した行（確認が必要な行）を書き出す。差分の CSV だけを見た人が見落とさないよう別ファイルにする。</summary>
    public void WriteIssues(DiffResult result, string path) =>
        WriteAtomically(path, writer =>
        {
            writer.NewLine = "\r\n";
            writer.WriteLine(CsvText.FormatLine(["ファイル", "行", "内容"]));
            foreach (var issue in result.Issues)
                writer.WriteLine(CsvText.FormatLine([issue.Side.ToLabel(), issue.LineNumber.ToString(CultureInfo.InvariantCulture), issue.Message]));
        });

    /// <summary>確認が必要な行の保存先。差分の保存先と同じ場所に「_要確認」を付けた名前にする。</summary>
    public string IssuesPathFor(string path) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,
            Path.GetFileNameWithoutExtension(path) + "_要確認" + Path.GetExtension(path));

    public void WriteTo(DiffResult result, TextWriter writer)
    {
        writer.NewLine = "\r\n";
        writer.WriteLine(CsvText.FormatLine(DiffTableFormat.BuildHeader(result)));
        foreach (var entry in result.Entries)
            writer.WriteLine(CsvText.FormatLine(DiffTableFormat.BuildRow(result, entry)));
    }

    /// <summary>書き出しに失敗したときに中途半端なファイルを残さないよう、一時ファイルに書いてから置き換える。</summary>
    private static void WriteAtomically(string path, Action<TextWriter> write)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var writer = new StreamWriter(temp, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
                write(writer);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
