namespace CsvDiffChecker.Core;

/// <summary>
/// 2つの CSV をキー列で突き合わせ、追加・削除・変更を求める。
/// 行の並び順には依存しない（並べ替えただけのファイルは「差分なし」になる）。
/// </summary>
public sealed class CsvDiffEngine : IDiffEngine
{
    // 複合キーを1つの文字列にするときの区切り。業務データに現れない制御文字を使う。
    private const char KeySeparator = '\u001F';

    public DiffResult Compare(CsvTable oldTable, CsvTable newTable, DiffOptions options)
    {
        ArgumentNullException.ThrowIfNull(oldTable);
        ArgumentNullException.ThrowIfNull(newTable);
        ArgumentNullException.ThrowIfNull(options);

        var keys = options.KeyColumns.Distinct().ToList();
        if (keys.Count == 0)
            throw new ArgumentException("照合する列を1つ以上選んでください。");
        foreach (var key in keys)
        {
            if (!oldTable.Headers.Contains(key))
                throw new ArgumentException($"照合する列「{key}」が旧ファイルにありません。");
            if (!newTable.Headers.Contains(key))
                throw new ArgumentException($"照合する列「{key}」が新ファイルにありません。");
        }

        var columns = oldTable.Headers.Where(newTable.Headers.Contains).ToList();
        var onlyOld = oldTable.Headers.Where(h => !newTable.Headers.Contains(h)).ToList();
        var onlyNew = newTable.Headers.Where(h => !oldTable.Headers.Contains(h)).ToList();

        var issues = new List<DiffIssue>();
        var oldIndex = BuildIndex(oldTable, FileSide.Old, keys, issues);
        var newIndex = BuildIndex(newTable, FileSide.New, keys, issues);

        // どちらかで重複したキーは、正しく突き合わせられないため比較から外す（一覧には出す）。
        var excluded = new HashSet<string>(oldIndex.Duplicates);
        excluded.UnionWith(newIndex.Duplicates);

        // 列の数が合わない行は比較できない。キーを取り出せれば、相手側の同じキーの行も外す
        // （外さないと、相手側の行が「削除」や「追加」と誤って出る）。
        excluded.UnionWith(CollectProblemKeys(oldTable, FileSide.Old, keys, issues));
        excluded.UnionWith(CollectProblemKeys(newTable, FileSide.New, keys, issues));

        // 改行コード（CRLF・LF・CR）の違いは画面上で見分けられないため、比較では揃える。
        string Normalize(string value)
        {
            var v = value.Replace("\r\n", "\n").Replace('\r', '\n');
            return options.TrimValues ? v.Trim() : v;
        }

        var changed = new List<DiffEntry>();
        var added = new List<DiffEntry>();
        var deleted = new List<DiffEntry>();
        var unchanged = 0;

        foreach (var (key, newRow) in newIndex.Ordered)
        {
            if (excluded.Contains(key)) continue;
            if (!oldIndex.Map.TryGetValue(key, out var oldRow))
            {
                added.Add(CreateEntry(DiffKind.Added, newTable, newRow, null, newRow, keys, columns, []));
                continue;
            }

            var changes = new List<CellChange>();
            // キー列は突き合わせに使った列のため比較しない（前後の空白の違いだけが残りうる）。
            foreach (var column in columns.Where(c => !keys.Contains(c)))
            {
                var oldValue = oldRow.Fields[IndexOf(oldTable, column)];
                var newValue = newRow.Fields[IndexOf(newTable, column)];
                if (!string.Equals(Normalize(oldValue), Normalize(newValue), StringComparison.Ordinal))
                    changes.Add(new CellChange(column, oldValue, newValue));
            }

            if (changes.Count == 0)
                unchanged++;
            else
                changed.Add(CreateEntry(DiffKind.Changed, newTable, newRow, oldRow, newRow, keys, columns, changes));
        }

        foreach (var (key, oldRow) in oldIndex.Ordered)
        {
            if (excluded.Contains(key)) continue;
            if (!newIndex.Map.ContainsKey(key))
                deleted.Add(CreateEntry(DiffKind.Deleted, oldTable, oldRow, oldRow, null, keys, columns, []));
        }

        return new DiffResult
        {
            Columns = columns,
            KeyColumns = keys,
            OnlyInOldColumns = onlyOld,
            OnlyInNewColumns = onlyNew,
            Entries = [.. changed, .. added, .. deleted],
            Issues = issues.OrderBy(i => i.Side).ThenBy(i => i.LineNumber).ToList(),
            UnchangedCount = unchanged,
        };
    }

    private sealed class KeyIndex
    {
        public Dictionary<string, CsvRecord> Map { get; } = new(StringComparer.Ordinal);
        public List<(string Key, CsvRecord Row)> Ordered { get; } = [];
        public HashSet<string> Duplicates { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// 列の数が合わない行からキーの候補を取り出す。どの列がずれたかは分からないため、
    /// 先頭から数えた位置と末尾から数えた位置の両方を候補にし、どちらも相手側で比較から外す
    /// （外しすぎても「確認が必要な行」に出るため、誤った「追加」「削除」を出すより安全）。
    /// </summary>
    private static IEnumerable<string> CollectProblemKeys(CsvTable table, FileSide side, IReadOnlyList<string> keys, List<DiffIssue> issues)
    {
        var keyIndexes = keys.Select(k => IndexOf(table, k)).ToArray();
        foreach (var problem in table.Problems)
        {
            var offset = problem.Fields.Count - table.Headers.Count;
            var candidates = new List<string[]>();
            foreach (var shift in new[] { 0, offset }.Distinct())
            {
                if (!keyIndexes.All(i => i + shift >= 0 && i + shift < problem.Fields.Count)) continue;
                var parts = keyIndexes.Select(i => problem.Fields[i + shift].Trim()).ToArray();
                if (parts.Any(p => p.Length > 0) && !candidates.Any(c => c.SequenceEqual(parts)))
                    candidates.Add(parts);
            }

            if (candidates.Count == 0)
            {
                issues.Add(new DiffIssue(side, problem.LineNumber,
                    $"{problem.Message}照合する値を取り出せないため、相手側の対応する行が「追加」または「削除」と出ている可能性があります。"));
                continue;
            }

            var display = string.Join("」「", candidates.Select(c => string.Join(" / ", c)));
            var message = candidates.Count == 1
                ? $"{problem.Message}照合する値「{display}」は相手側のファイルでも比較から外しました。"
                : $"{problem.Message}列がずれている可能性があるため、照合する値の候補「{display}」を相手側のファイルでも比較から外しました。";
            issues.Add(new DiffIssue(side, problem.LineNumber, message));
            foreach (var parts in candidates)
                yield return string.Join(KeySeparator, parts);
        }
    }

    private static KeyIndex BuildIndex(CsvTable table, FileSide side, IReadOnlyList<string> keys, List<DiffIssue> issues)
    {
        var keyIndexes = keys.Select(k => IndexOf(table, k)).ToArray();
        var index = new KeyIndex();
        var linesByKey = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        foreach (var row in table.Rows)
        {
            var parts = keyIndexes.Select(i => row.Fields[i].Trim()).ToArray();
            if (parts.All(p => p.Length == 0))
            {
                issues.Add(new DiffIssue(side, row.LineNumber, "照合する列の値が空のため比較できません。"));
                continue;
            }

            var key = string.Join(KeySeparator, parts);
            if (!linesByKey.TryGetValue(key, out var lines))
            {
                linesByKey[key] = lines = [];
                index.Map[key] = row;
                index.Ordered.Add((key, row));
            }
            lines.Add(row.LineNumber);
        }

        foreach (var (key, lines) in linesByKey.Where(p => p.Value.Count > 1))
        {
            index.Duplicates.Add(key);
            var display = key.Replace(KeySeparator.ToString(), " / ");
            foreach (var line in lines)
            {
                var others = string.Join("・", lines.Where(l => l != line));
                issues.Add(new DiffIssue(side, line, $"照合する値「{display}」が重複しています（{others}行目にもあります）。比較から外しました。"));
            }
        }

        return index;
    }

    private static DiffEntry CreateEntry(DiffKind kind, CsvTable source, CsvRecord sourceRow,
        CsvRecord? oldRow, CsvRecord? newRow, IReadOnlyList<string> keys, IReadOnlyList<string> columns,
        IReadOnlyList<CellChange> changes)
    {
        var values = columns.ToDictionary(c => c, c => sourceRow.Fields[IndexOf(source, c)]);
        return new DiffEntry
        {
            Kind = kind,
            KeyText = string.Join(" / ", keys.Select(k => sourceRow.Fields[IndexOf(source, k)].Trim())),
            OldLine = oldRow?.LineNumber,
            NewLine = newRow?.LineNumber,
            Values = values,
            Changes = changes,
        };
    }

    private static int IndexOf(CsvTable table, string column)
    {
        for (var i = 0; i < table.Headers.Count; i++)
            if (table.Headers[i] == column) return i;
        throw new InvalidOperationException($"列「{column}」が見つかりません。");
    }
}
