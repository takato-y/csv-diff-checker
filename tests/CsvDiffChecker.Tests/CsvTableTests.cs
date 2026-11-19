using System.Text;
using CsvDiffChecker.Core;

namespace CsvDiffChecker.Tests;

public class CsvTableTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csvdiff-tests-" + Guid.NewGuid().ToString("N"));

    public CsvTableTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteFile(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private const string Sample = "商品コード,商品名\r\nA001,ボールペン（黒）\r\n";

    [Fact]
    public void ShiftJISのファイルを判定して読む()
    {
        var path = WriteFile("sjis.csv", EncodingDetector.ShiftJis.GetBytes(Sample));
        var table = new CsvTableLoader().Load(path);
        Assert.Equal("Shift_JIS", table.EncodingName);
        Assert.Equal("ボールペン（黒）", table.Rows[0].Fields[1]);
    }

    [Fact]
    public void BOM付きUTF8を判定して見出しにBOMを含めない()
    {
        var path = WriteFile("bom.csv", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Sample)]);
        var table = new CsvTableLoader().Load(path);
        Assert.Equal("UTF-8（BOM付き）", table.EncodingName);
        Assert.Equal("商品コード", table.Headers[0]);
    }

    [Fact]
    public void BOMなしUTF8を判定する()
    {
        var path = WriteFile("utf8.csv", new UTF8Encoding(false).GetBytes(Sample));
        Assert.Equal("UTF-8", new CsvTableLoader().Load(path).EncodingName);
    }

    [Fact]
    public void 他のアプリが書き込み可能で開いていても読める()
    {
        var path = WriteFile("locked.csv", Encoding.UTF8.GetBytes(Sample));
        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Single(new CsvTableLoader().Load(path).Rows);
    }

    [Fact]
    public void 読み込んでも元のファイルを変更しない()
    {
        var bytes = EncodingDetector.ShiftJis.GetBytes(Sample);
        var path = WriteFile("keep.csv", bytes);
        new CsvTableLoader().Load(path);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void 列数が合わない行は除外して行番号つきで記録する()
    {
        var table = CsvTable.FromText("id,name\n1,a\n2\n3,c,余分\n4,d\n");
        Assert.Equal(["1", "4"], table.Rows.Select(r => r.Fields[0]));
        Assert.Equal([3, 4], table.Problems.Select(p => p.LineNumber));
    }

    [Fact]
    public void 見出しの重複は比較できないため例外()
    {
        var ex = Assert.Throws<CsvFormatException>(() => CsvTable.FromText("id,name,name\n1,a,b\n"));
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void 空の見出しには列番号の仮名を付ける()
    {
        var table = CsvTable.FromText("id,,name\n1,x,a\n");
        Assert.Equal("（列2）", table.Headers[1]);
    }

    [Fact]
    public void 空のファイルは例外()
    {
        Assert.Throws<CsvFormatException>(() => CsvTable.FromText(""));
    }

    [Fact]
    public void BOM付きUTF16を判定する()
    {
        var path = WriteFile("utf16.csv", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Sample)]);
        var table = new CsvTableLoader().Load(path);
        Assert.Equal("UTF-16 LE", table.EncodingName);
        Assert.Equal("ボールペン（黒）", table.Rows[0].Fields[1]);
    }

    [Fact]
    public void UTF8としてもShiftJISとしても読めなければ例外()
    {
        // 0x81 は Shift_JIS の2バイト文字の1バイト目だが、続く改行は2バイト目として不正。UTF-8 としても不正。
        var path = WriteFile("broken.csv", [.. Encoding.ASCII.GetBytes("id,name\r\n1,"), 0x81, 0x0A]);
        var ex = Assert.Throws<CsvFormatException>(() => new CsvTableLoader().Load(path));
        Assert.Contains("文字コードを判定できません", ex.Message);
    }
}
