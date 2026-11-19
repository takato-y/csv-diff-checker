using System.Text;
using CsvDiffChecker.Core;

namespace CsvDiffChecker.Tests;

public class DiffCsvWriterTests
{
    private static DiffResult Sample() => new CsvDiffEngine().Compare(
        CsvTable.FromText("id,name,price\n1,りんご,100\n2,みかん,80\n"),
        CsvTable.FromText("id,name,price\n1,りんご,120\n3,\"もも, 大玉\",250\n"),
        new DiffOptions { KeyColumns = ["id"] });

    [Fact]
    public void 見出しと各区分の行を書き出す()
    {
        var writer = new StringWriter();
        new DiffCsvWriter().WriteTo(Sample(), writer);
        var lines = writer.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("差分,照合する値,旧ファイルの行,新ファイルの行,変更した列,id,name,price", lines[0]);
        Assert.Equal("変更,1,2,2,price,1,りんご,100 → 120", lines[1]);
        Assert.Equal("追加,3,,3,,3,\"もも, 大玉\",250", lines[2]);
        Assert.Equal("削除,2,3,,,2,みかん,80", lines[3]);
    }

    [Theory]
    [InlineData("=SUM(A1:A9)", "'=SUM(A1:A9)")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("+81-3-0000", "'+81-3-0000")]
    [InlineData("-100", "-100")]
    [InlineData("+3.5", "+3.5")]
    [InlineData("通常の文字", "通常の文字")]
    [InlineData("", "")]
    public void 数式として動く値だけ無害化する(string input, string expected)
    {
        Assert.Equal(expected, CsvText.Neutralize(input));
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("1行目\n2行目", "\"1行目\n2行目\"")]
    [InlineData(" 前に空白", "\" 前に空白\"")]
    public void 必要な値だけ引用符で囲む(string input, string expected)
    {
        Assert.Equal(expected, CsvText.Escape(input));
    }

    [Fact]
    public void ファイルはBOM付きUTF8で書き一時ファイルを残さない()
    {
        var dir = Path.Combine(Path.GetTempPath(), "csvdiff-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "結果.csv");
            new DiffCsvWriter().Write(Sample(), path);

            var bytes = File.ReadAllBytes(path);
            Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3));
            Assert.StartsWith("差分,", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
            Assert.Equal(["結果.csv"], Directory.GetFiles(dir).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 既存のファイルは上書きで置き換える()
    {
        var dir = Path.Combine(Path.GetTempPath(), "csvdiff-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "結果.csv");
            File.WriteAllText(path, "古い内容");
            new DiffCsvWriter().Write(Sample(), path);
            Assert.DoesNotContain("古い内容", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 確認が必要な行を別ファイルに書き出す()
    {
        var result = new CsvDiffEngine().Compare(
            CsvTable.FromText("id,v\n1,a\n"), CsvTable.FromText("id,v\n1,a\n1,b\n"), new DiffOptions { KeyColumns = ["id"] });
        var dir = Path.Combine(Path.GetTempPath(), "csvdiff-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = new DiffCsvWriter().IssuesPathFor(Path.Combine(dir, "結果.csv"));
            Assert.Equal("結果_要確認.csv", Path.GetFileName(path));
            new DiffCsvWriter().WriteIssues(result, path);
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            Assert.Equal("ファイル,行,内容", lines[0]);
            Assert.Equal(3, lines.Length);
            Assert.StartsWith("新ファイル,2,", lines[1]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
