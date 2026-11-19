using CsvDiffChecker.Core;

namespace CsvDiffChecker.Tests;

public class CsvParserTests
{
    [Fact]
    public void 単純なCSVを行と列に分ける()
    {
        var records = CsvParser.Parse("a,b,c\r\n1,2,3\r\n");
        Assert.Equal(2, records.Count);
        Assert.Equal(["a", "b", "c"], records[0].Fields);
        Assert.Equal(["1", "2", "3"], records[1].Fields);
    }

    [Fact]
    public void 引用符内のカンマ_改行_二重引用符を値として読む()
    {
        var records = CsvParser.Parse("id,memo\n1,\"東京,大阪\"\n2,\"1行目\n2行目\"\n3,\"\"\"強調\"\"\"\n");
        Assert.Equal("東京,大阪", records[1].Fields[1]);
        Assert.Equal("1行目\n2行目", records[2].Fields[1]);
        Assert.Equal("\"強調\"", records[3].Fields[1]);
    }

    [Fact]
    public void 引用符内に改行がある場合も次のレコードの行番号を正しく数える()
    {
        var records = CsvParser.Parse("id,memo\n1,\"a\nb\nc\"\n2,x\n");
        Assert.Equal(1, records[0].LineNumber);
        Assert.Equal(2, records[1].LineNumber);
        Assert.Equal(5, records[2].LineNumber);
    }

    [Fact]
    public void 最終行に改行が無くても読む()
    {
        var records = CsvParser.Parse("a,b\n1,2");
        Assert.Equal(["1", "2"], records[1].Fields);
    }

    [Fact]
    public void 空行は読み飛ばすが行番号は数える()
    {
        var records = CsvParser.Parse("a,b\n\n1,2\r\n\r\n3,4\n");
        Assert.Equal(3, records.Count);
        Assert.Equal(3, records[1].LineNumber);
        Assert.Equal(5, records[2].LineNumber);
    }

    [Fact]
    public void 引用符だけの空の値は空行として捨てない()
    {
        var records = CsvParser.Parse("a\n\"\"\n");
        Assert.Equal(2, records.Count);
        Assert.Equal([""], records[1].Fields);
    }

    [Fact]
    public void 末尾の空の値を残す()
    {
        var records = CsvParser.Parse("a,b,c\n1,,\n");
        Assert.Equal(["1", "", ""], records[1].Fields);
    }

    [Fact]
    public void 先頭のBOM文字を見出しに含めない()
    {
        var records = CsvParser.Parse("﻿id,name\n1,x\n");
        Assert.Equal("id", records[0].Fields[0]);
    }

    [Fact]
    public void 引用符が閉じていなければ開始行を示して例外()
    {
        var ex = Assert.Throws<CsvFormatException>(() => CsvParser.Parse("id,memo\n1,ok\n2,\"閉じていない\n3,x\n"));
        Assert.Equal(3, ex.LineNumber);
    }

    [Fact]
    public void 値の途中の引用符は文字として扱う()
    {
        var records = CsvParser.Parse("a\n12\"インチ\n");
        Assert.Equal("12\"インチ", records[1].Fields[0]);
    }
}
