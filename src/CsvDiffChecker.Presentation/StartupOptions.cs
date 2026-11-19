namespace CsvDiffChecker.Presentation;

/// <summary>
/// 起動引数（任意）: --old 旧ファイル --new 新ファイル --key 列名[,列名]
/// </summary>
public sealed record StartupOptions(string? OldPath, string? NewPath, IReadOnlyList<string> Keys)
{
    public static StartupOptions None { get; } = new(null, null, []);

    /// <summary>1つずつ読む。値を取る引数の後ろに値が無い場合（次が -- で始まる場合を含む）や、知らない引数は読み飛ばす。</summary>
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        string? oldPath = null, newPath = null;
        var keys = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i].ToLowerInvariant();
            if (name is not ("--old" or "--new" or "--key")) continue;
            // 値の位置に次の引数名（-- で始まる）がある場合は「値なし」とし、その引数名は次の周で読む。
            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal)) continue;
            var value = args[++i];
            switch (name)
            {
                case "--old": oldPath = value; break;
                case "--new": newPath = value; break;
                case "--key":
                    keys.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
            }
        }
        return new StartupOptions(oldPath, newPath, keys.Distinct().ToList());
    }
}
