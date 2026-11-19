using CsvDiffChecker.Presentation;

namespace CsvDiffChecker.Tests.Presentation;

/// <summary>起動引数の解釈（FN-005）。</summary>
public class StartupOptionsTests
{
    [Fact]
    public void ファイルと照合する列を読む()
    {
        var options = StartupOptions.Parse(["--old", "a.csv", "--new", "b.csv", "--key", "店舗, 商品"]);
        Assert.Equal("a.csv", options.OldPath);
        Assert.Equal("b.csv", options.NewPath);
        Assert.Equal(["店舗", "商品"], options.Keys);
    }

    [Fact]
    public void 引数名は大文字小文字を区別しない()
    {
        Assert.Equal("a.csv", StartupOptions.Parse(["--OLD", "a.csv"]).OldPath);
    }

    [Fact]
    public void 知らない引数は読み飛ばし後ろの引数はずれない()
    {
        var options = StartupOptions.Parse(["--kye", "x", "--old", "a.csv"]);
        Assert.Equal("a.csv", options.OldPath);
        Assert.Empty(options.Keys);
    }

    [Fact]
    public void 値の無い引数は無視する()
    {
        var options = StartupOptions.Parse(["--old"]);
        Assert.Null(options.OldPath);
    }

    [Fact]
    public void 値の位置に次の引数名があれば値なしとして次の引数を読む()
    {
        var options = StartupOptions.Parse(["--old", "--new", "b.csv"]);
        Assert.Null(options.OldPath);
        Assert.Equal("b.csv", options.NewPath);
    }

    [Fact]
    public void 同じ列を重ねて指定しても1つにまとめる()
    {
        Assert.Equal(["code"], StartupOptions.Parse(["--key", "code", "--key", "code"]).Keys);
    }
}
