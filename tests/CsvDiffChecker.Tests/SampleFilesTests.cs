using CsvDiffChecker.Core;

namespace CsvDiffChecker.Tests;

/// <summary>samples フォルダのファイル（README の出力例）で、記載どおりの結果になることを確かめる。</summary>
public class SampleFilesTests
{
    private static string SamplePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "samples")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "samples", name);
    }

    [Fact]
    public void サンプルの比較結果がREADMEの記載と一致する()
    {
        var oldTable = new CsvTableLoader().Load(SamplePath("products_old.csv"));
        var newTable = new CsvTableLoader().Load(SamplePath("products_new.csv"));
        Assert.Equal("Shift_JIS", oldTable.EncodingName);
        Assert.Equal("UTF-8（BOM付き）", newTable.EncodingName);

        var result = new CsvDiffEngine().Compare(oldTable, newTable, new DiffOptions { KeyColumns = ["商品コード"] });

        Assert.Equal(["A001", "A003", "C002"], result.Entries.Where(e => e.Kind == DiffKind.Changed).Select(e => e.KeyText));
        Assert.Equal(["F001", "F003"], result.Entries.Where(e => e.Kind == DiffKind.Added).Select(e => e.KeyText));
        Assert.Equal(["B002"], result.Entries.Where(e => e.Kind == DiffKind.Deleted).Select(e => e.KeyText));
        Assert.Equal(5, result.UnchangedCount);
        Assert.Equal(3, result.Issues.Count);
        Assert.Equal(["備考"], result.OnlyInNewColumns);
    }
}
