using CsvDiffChecker.Core;

namespace CsvDiffChecker.Tests;

public class CsvDiffEngineTests
{
    private static DiffResult Compare(string oldCsv, string newCsv, params string[] keys) =>
        Compare(oldCsv, newCsv, trim: true, keys);

    private static DiffResult Compare(string oldCsv, string newCsv, bool trim, params string[] keys) =>
        new CsvDiffEngine().Compare(CsvTable.FromText(oldCsv), CsvTable.FromText(newCsv),
            new DiffOptions { KeyColumns = keys, TrimValues = trim });

    [Fact]
    public void 追加_削除_変更_一致を判定する()
    {
        var result = Compare(
            "id,name,price\n1,りんご,100\n2,みかん,80\n3,ぶどう,300\n",
            "id,name,price\n1,りんご,100\n2,みかん,90\n4,もも,250\n",
            "id");

        Assert.Equal(1, result.UnchangedCount);
        var changed = Assert.Single(result.Entries, e => e.Kind == DiffKind.Changed);
        Assert.Equal("2", changed.KeyText);
        Assert.Equal(new CellChange("price", "80", "90"), Assert.Single(changed.Changes));
        Assert.Equal("4", Assert.Single(result.Entries, e => e.Kind == DiffKind.Added).KeyText);
        var deleted = Assert.Single(result.Entries, e => e.Kind == DiffKind.Deleted);
        Assert.Equal("3", deleted.KeyText);
        Assert.Equal("ぶどう", deleted.Values["name"]);
        Assert.Null(deleted.NewLine);
        Assert.Equal(4, deleted.OldLine);
    }

    [Fact]
    public void 並び順が違うだけなら差分なし()
    {
        var result = Compare("id,v\n1,a\n2,b\n3,c\n", "id,v\n3,c\n1,a\n2,b\n", "id");
        Assert.Empty(result.Entries);
        Assert.Equal(3, result.UnchangedCount);
    }

    [Fact]
    public void 結果は変更_追加_削除の順に並ぶ()
    {
        var result = Compare("id,v\n1,a\n2,b\n", "id,v\n3,c\n1,x\n", "id");
        Assert.Equal([DiffKind.Changed, DiffKind.Added, DiffKind.Deleted], result.Entries.Select(e => e.Kind));
    }

    [Fact]
    public void 複合キーで突き合わせる()
    {
        var result = Compare(
            "店舗,商品,数\n東京,A,1\n大阪,A,2\n",
            "店舗,商品,数\n東京,A,1\n大阪,A,5\n",
            "店舗", "商品");
        var changed = Assert.Single(result.Entries);
        Assert.Equal("大阪 / A", changed.KeyText);
    }

    [Fact]
    public void 前後の空白を無視する設定では空白だけの違いを変更にしない()
    {
        var oldCsv = "id,v\n1,abc\n";
        var newCsv = "id,v\n1, abc \n";
        Assert.Empty(Compare(oldCsv, newCsv, trim: true, "id").Entries);
        Assert.Single(Compare(oldCsv, newCsv, trim: false, "id").Entries);
    }

    [Fact]
    public void キーは常に前後の空白を除いて突き合わせる()
    {
        var result = Compare("id,v\n A01 ,x\n", "id,v\nA01,x\n", trim: false, "id");
        Assert.Empty(result.Entries);
        Assert.Equal(1, result.UnchangedCount);
    }

    [Fact]
    public void 大文字と小文字は別の値として比べる()
    {
        var result = Compare("id,v\n1,abc\n", "id,v\n1,ABC\n", "id");
        Assert.Single(result.Entries);
    }

    [Fact]
    public void 重複キーは比較から外し両方の行を確認対象にする()
    {
        var result = Compare("id,v\n1,a\n1,b\n2,c\n", "id,v\n1,z\n2,c\n", "id");
        Assert.Empty(result.Entries);
        Assert.Equal(1, result.UnchangedCount);
        Assert.Equal(2, result.Issues.Count);
        Assert.All(result.Issues, i => Assert.Equal(FileSide.Old, i.Side));
        Assert.Contains("3行目にもあります", result.Issues[0].Message);
    }

    [Fact]
    public void 新ファイル側だけで重複したキーも追加や削除と誤判定しない()
    {
        var result = Compare("id,v\n1,a\n", "id,v\n1,a\n1,b\n", "id");
        Assert.Empty(result.Entries);
        Assert.Equal(2, result.Issues.Count);
    }

    [Fact]
    public void キーが空の行は確認対象にする()
    {
        var result = Compare("id,v\n,a\n1,b\n", "id,v\n1,b\n", "id");
        Assert.Empty(result.Entries);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(2, issue.LineNumber);
    }

    [Fact]
    public void 片方にしかない列は比較せず一覧にする()
    {
        var result = Compare("id,name,旧項目\n1,a,x\n", "id,name,新項目\n1,a,y\n", "id");
        Assert.Empty(result.Entries);
        Assert.Equal(["id", "name"], result.Columns);
        Assert.Equal(["旧項目"], result.OnlyInOldColumns);
        Assert.Equal(["新項目"], result.OnlyInNewColumns);
    }

    [Fact]
    public void 列の並び順が違っても列名で比べる()
    {
        var result = Compare("id,a,b\n1,x,y\n", "b,id,a\ny,1,x\n", "id");
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void 列数が合わない行は確認対象に含める()
    {
        var result = Compare("id,v\n1,a\n2\n", "id,v\n1,a\n", "id");
        var issue = Assert.Single(result.Issues);
        Assert.Equal(FileSide.Old, issue.Side);
        Assert.Equal(3, issue.LineNumber);
    }

    [Fact]
    public void キー列が無ければ例外()
    {
        var ex = Assert.Throws<ArgumentException>(() => Compare("id,v\n1,a\n", "code,v\n1,a\n", "id"));
        Assert.Contains("新ファイル", ex.Message);
    }

    [Fact]
    public void キー列が未指定なら例外()
    {
        Assert.Throws<ArgumentException>(() => Compare("id\n1\n", "id\n1\n"));
    }

    [Fact]
    public void 列数が合わない行のキーは相手側でも比較から外し誤った削除を出さない()
    {
        var result = Compare("id,name,price\n1,a,100\n2,b,200\n", "id,name,price\n1,a,100\n2,b\n", "id");
        Assert.Empty(result.Entries);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(FileSide.New, issue.Side);
        Assert.Contains("照合する値「2」は相手側のファイルでも比較から外しました", issue.Message);
    }

    [Fact]
    public void 旧ファイル側の列数不一致でも誤った追加を出さない()
    {
        var result = Compare("id,name,price\n1,a\n", "id,name,price\n1,a,100\n", "id");
        Assert.Empty(result.Entries);
        Assert.Equal(FileSide.Old, Assert.Single(result.Issues).Side);
    }

    [Fact]
    public void キーを取り出せない列数不一致の行はその旨を示す()
    {
        var result = Compare("name,price,id\na,100,1\n", "name,price,id\n\"\"\na,100,1\n", "id");
        Assert.Contains("照合する値を取り出せないため", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void 改行コードの違いだけでは変更にしない()
    {
        var result = Compare("id,memo\n1,\"x\r\ny\"\n", "id,memo\n1,\"x\ny\"\n", trim: false, "id");
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void キー列より前で値が増えた行でも相手側の行を誤って削除にしない()
    {
        // 引用符の無いカンマで「商品名」が2列に割れ、キー列（最後の列）が後ろにずれた行
        var result = Compare("name,price,code\nペン,100,K1\n", "name,price,code\nペン, 黒,100,K1\n", "code");
        Assert.Empty(result.Entries);
        var issue = Assert.Single(result.Issues);
        Assert.Contains("「100」「K1」", issue.Message);
    }

    [Fact]
    public void キー列より前で値が欠けた行でも相手側の行を誤って削除にしない()
    {
        var result = Compare("name,code,price\nペン,K1,100\n", "name,code,price\nK1,100\n", "code");
        Assert.Empty(result.Entries);
        Assert.Contains("「100」「K1」", Assert.Single(result.Issues).Message);
    }
}
