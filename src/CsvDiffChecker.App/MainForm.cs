using System.Data;
using CsvDiffChecker.Core;   // 旧・新の区別（FileSide）だけを使う
using CsvDiffChecker.Presentation;

namespace CsvDiffChecker.App;

/// <summary>
/// メイン画面（SCR-001）。表示と、利用者の操作を <see cref="MainPresenter"/> へ渡すことだけを受け持つ。
/// 状態の保持や処理の可否の判断は Presenter が行い、この画面は <see cref="IMainView"/> の指示どおりに表示する。
/// レイアウトは TableLayoutPanel で組み、画面の拡大率（DPI）が変わっても崩れないようにする。
/// </summary>
internal sealed class MainForm : Form, IMainView
{
    private const string EntryIndexColumn = "__entry";

    private static readonly Color AddedColor = Color.FromArgb(226, 244, 230);
    private static readonly Color DeletedColor = Color.FromArgb(251, 228, 228);
    private static readonly Color ChangedCellColor = Color.FromArgb(255, 241, 194);
    // 警告の文字色。既定の背景（#F0F0F0）とのコントラスト比が 4.5:1 を超える濃い橙。
    private static readonly Color WarningTextColor = Color.FromArgb(166, 75, 0);

    private readonly MainPresenter _presenter;
    private readonly StartupOptions _startup;

    private readonly TextBox _oldPathBox = CreatePathBox();
    private readonly TextBox _newPathBox = CreatePathBox();
    private readonly Label _oldInfo = CreateInfoLabel();
    private readonly Label _newInfo = CreateInfoLabel();
    private readonly CheckedListBox _keyList = new()
    {
        Dock = DockStyle.Fill, CheckOnClick = true, MultiColumn = true, ColumnWidth = 160, IntegralHeight = false,
    };
    private readonly CheckBox _trimCheck = new() { Text = "値の前後の空白を無視する", Checked = true, AutoSize = true };
    private readonly Button _compareButton = new() { Text = "比較する", AutoSize = true, Padding = new Padding(16, 4, 16, 4), Enabled = false };
    private readonly Button _saveButton = new() { Text = "結果をCSVに保存（全件）…", AutoSize = true, Enabled = false };
    private readonly Label _summary = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _issueSummary = new() { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = WarningTextColor, Margin = new Padding(12, 3, 3, 3) };
    private readonly Button _oldBrowseButton = new() { Text = "参照…", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _newBrowseButton = new() { Text = "参照…", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _swapButton = new() { Text = "旧と新を入れ替える", AutoSize = true, Enabled = false };
    private readonly CheckBox _showChanged = CreateFilterCheck("変更");
    private readonly CheckBox _showAdded = CreateFilterCheck("追加");
    private readonly CheckBox _showDeleted = CreateFilterCheck("削除");
    private readonly DataGridView _grid = CreateGrid();
    private readonly DataGridView _issueGrid = CreateGrid();
    private readonly TextBox _columnInfo = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window,
    };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _resultTab = new("比較結果");
    private readonly TabPage _issueTab = new("確認が必要な行");
    private readonly TabPage _columnTab = new("列の違い");
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolTip _toolTip = new();

    private ResultTable? _shownTable;
    private DataView? _resultView;
    // Presenter の指示で表示を変えている間は、その変更を Presenter に返さない。
    private bool _updatingKeyList;
    private bool _updatingTrim;

    public MainForm(StartupOptions startup, Func<IMainView, MainPresenter> createPresenter)
    {
        _startup = startup;
        _presenter = createPresenter(this);

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = MainPresenter.Title;
        Font = new Font("Yu Gothic UI", 10F);
        MinimumSize = new Size(900, 600);
        Size = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        WireEvents();
    }

    // ---------------------------------------------------------------- レイアウト

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // ファイル選択
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));  // キー列・比較ボタン
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 件数・絞り込み・保存
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 結果

        root.Controls.Add(BuildFilePanel(), 0, 0);
        root.Controls.Add(BuildSettingsPanel(), 0, 1);
        root.Controls.Add(BuildSummaryPanel(), 0, 2);

        _resultTab.Controls.Add(_grid);
        _issueTab.Controls.Add(_issueGrid);
        _columnTab.Controls.Add(_columnInfo);
        _tabs.TabPages.AddRange([_resultTab, _issueTab, _columnTab]);
        root.Controls.Add(_tabs, 0, 3);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);
        _status.Text = "CSVファイルはこの画面へドラッグ＆ドロップでも指定できます。元のファイルは変更しません。";

        Controls.Add(root);
        Controls.Add(statusStrip);
    }

    private Control BuildFilePanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddFileRow(panel, 0, "旧ファイル（比較元）", _oldPathBox, _oldBrowseButton, _oldInfo, () => _presenter.BrowseAsync(FileSide.Old));
        AddFileRow(panel, 1, "新ファイル（比較先）", _newPathBox, _newBrowseButton, _newInfo, () => _presenter.BrowseAsync(FileSide.New));
        panel.Controls.Add(_swapButton, 2, 2);
        return panel;
    }

    private static void AddFileRow(TableLayoutPanel panel, int row, string caption, TextBox box, Button button, Label info, Func<Task> browse)
    {
        var label = new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
        button.Click += async (_, _) => await browse();
        panel.Controls.Add(label, 0, row);
        panel.Controls.Add(box, 1, row);
        panel.Controls.Add(button, 2, row);
        panel.Controls.Add(info, 3, row);
    }

    private Control BuildSettingsPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 6, 0, 6) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var keyGroup = new GroupBox { Text = "照合する列（商品コード・社員番号など、行ごとに違う値が入る列。複数選ぶと組み合わせで照合します）", Dock = DockStyle.Fill };
        keyGroup.Controls.Add(_keyList);
        panel.Controls.Add(keyGroup, 0, 0);

        var actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(12, 8, 0, 0),
            WrapContents = false,
        };
        actions.Controls.Add(_trimCheck);
        actions.Controls.Add(_compareButton);
        panel.Controls.Add(actions, 1, 0);
        return panel;
    }

    private Control BuildSummaryPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 0, 0, 6) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _summary.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
        _issueSummary.Font = _summary.Font;
        panel.Controls.Add(_summary, 0, 0);
        panel.Controls.Add(_issueSummary, 1, 0);

        var filters = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false };
        filters.Controls.Add(new Label { Text = "表示：", AutoSize = true, Margin = new Padding(3, 6, 0, 0) });
        filters.Controls.AddRange([_showChanged, _showAdded, _showDeleted]);
        panel.Controls.Add(filters, 2, 0);
        panel.Controls.Add(_saveButton, 3, 0);
        return panel;
    }

    private static TextBox CreatePathBox() => new()
    {
        // 手入力は受け付けない（書き換えただけで、表示しているファイルと結果が食い違うため）。指定は参照かドロップで行う。
        Dock = DockStyle.Fill, AllowDrop = true, ReadOnly = true, BackColor = SystemColors.Window, TabStop = false,
        PlaceholderText = "「参照…」で選ぶか、ここへドラッグ＆ドロップ", Margin = new Padding(3, 4, 3, 4),
    };

    private static Label CreateInfoLabel() => new()
    {
        AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 6, 0, 6),
    };

    private static CheckBox CreateFilterCheck(string text) => new() { Text = text, Checked = true, AutoSize = true };

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            ShowCellToolTips = true,
        };
        // 行数が多いときのちらつきを抑える（DoubleBuffered は公開されていないため反射で設定）。
        typeof(DataGridView).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
            .SetValue(grid, true);
        return grid;
    }

    // ---------------------------------------------------------------- 操作を Presenter へ渡す

    private void WireEvents()
    {
        foreach (var (box, side) in new[] { (_oldPathBox, FileSide.Old), (_newPathBox, FileSide.New) })
        {
            box.DragEnter += (_, e) => e.Effect = DroppedFiles(e).Length == 1 ? DragDropEffects.Copy : DragDropEffects.None;
            box.DragDrop += async (_, e) => await _presenter.DropOnFileAsync(side, DroppedFiles(e));
        }

        AllowDrop = true;
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += async (_, e) => await _presenter.DropFilesAsync(DroppedFiles(e));

        // チェックの状態は ItemCheck の後で確定するため、確定後に Presenter へ渡す。
        _keyList.ItemCheck += (_, _) =>
        {
            if (_updatingKeyList) return;
            BeginInvoke(() => _presenter.ChangeKeys(_keyList.CheckedItems.Cast<string>().ToList()));
        };
        _trimCheck.CheckedChanged += (_, _) =>
        {
            if (!_updatingTrim) _presenter.ChangeTrim(_trimCheck.Checked);
        };
        _swapButton.Click += async (_, _) => await _presenter.SwapAsync();
        _compareButton.Click += async (_, _) => await _presenter.CompareAsync();
        _saveButton.Click += async (_, _) => await _presenter.SaveAsync();
        foreach (var check in new[] { _showChanged, _showAdded, _showDeleted })
            check.CheckedChanged += (_, _) => ApplyFilter();

        _grid.CellFormatting += Grid_CellFormatting;
        _grid.CellToolTipTextNeeded += Grid_CellToolTipTextNeeded;
        // 先頭行が選択された状態だと変更セルの色が見えないため、表示直後は選択を外す。
        _grid.DataBindingComplete += (_, _) => _grid.ClearSelection();
        _issueGrid.DataBindingComplete += (_, _) => _issueGrid.ClearSelection();
        Shown += async (_, _) => await _presenter.StartAsync(_startup);
    }

    // ファイルが存在するかどうかの判定は Presenter が行う。画面はドロップされたパスをそのまま渡す。
    private static string[] DroppedFiles(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) as string[] ?? [];

    // ---------------------------------------------------------------- IMainView の実装

    public void ShowFile(FileSide side, FileDisplay display)
    {
        var box = side == FileSide.Old ? _oldPathBox : _newPathBox;
        var info = side == FileSide.Old ? _oldInfo : _newInfo;
        box.Text = display.Path;
        _toolTip.SetToolTip(box, display.Path);
        info.Text = display.Description;
        info.ForeColor = display.Tone switch
        {
            DisplayTone.Warning => WarningTextColor,
            DisplayTone.Error => Color.Firebrick,
            _ => SystemColors.GrayText,
        };
    }

    public void SetKeyCandidates(IReadOnlyList<string> columns, IReadOnlyCollection<string> selected)
    {
        _updatingKeyList = true;
        try
        {
            _keyList.Items.Clear();
            foreach (var column in columns)
                _keyList.Items.Add(column, selected.Contains(column));
        }
        finally
        {
            _updatingKeyList = false;
        }
    }

    public void ShowResult(ResultTable table, ResultSummary summary)
    {
        _shownTable = table;
        var data = new DataTable();
        // 内部の列名は c0, c1… とする（CSV の見出しが「差分」などでも列名が重ならないようにする）。
        for (var i = 0; i < table.Columns.Count; i++)
            data.Columns.Add($"c{i}", table.Columns[i].IsNumber ? typeof(int) : typeof(string));
        data.Columns.Add(EntryIndexColumn, typeof(int));

        data.BeginLoadData();
        for (var r = 0; r < table.Rows.Count; r++)
            data.Rows.Add([.. table.Rows[r].Cells.Select(c => c.Value ?? DBNull.Value), r]);
        data.EndLoadData();

        _resultView = data.DefaultView;
        _grid.DataSource = _resultView;
        _grid.Columns[EntryIndexColumn]!.Visible = false;
        for (var i = 0; i < table.Columns.Count; i++)
        {
            var column = _grid.Columns[$"c{i}"]!;
            column.HeaderText = table.Columns[i].Header;
            column.SortMode = DataGridViewColumnSortMode.Automatic;
            column.Frozen = i < 2;   // 先頭の2列（差分・照合する値）は横に送っても見えるようにする
        }
        _grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
        foreach (DataGridViewColumn column in _grid.Columns)
            column.Width = Math.Min(Math.Max(column.Width, 70), 320);
        ApplyFilter();

        var issues = new DataTable();
        issues.Columns.Add("ファイル", typeof(string));
        issues.Columns.Add("行", typeof(int));
        issues.Columns.Add("内容", typeof(string));
        foreach (var issue in table.Issues)
            issues.Rows.Add(issue.FileLabel, issue.LineNumber, issue.Message);
        _issueGrid.DataSource = issues;
        _issueGrid.Columns["内容"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        _columnInfo.Text = summary.ColumnsText;
        _summary.Text = summary.CountsText;
        _issueSummary.Text = summary.IssuesText;
        _resultTab.Text = $"比較結果（{table.Rows.Count:N0}）";
        _issueTab.Text = table.Issues.Count > 0 ? $"⚠ 確認が必要な行（{table.Issues.Count:N0}）" : "確認が必要な行（0）";
        _columnTab.Text = summary.HasColumnDifference ? "列の違い（あり）" : "列の違い";
    }

    public void ClearResult(string message)
    {
        _shownTable = null;
        _resultView = null;
        _grid.DataSource = null;
        _issueGrid.DataSource = null;
        _columnInfo.Clear();
        _summary.Text = message;
        _issueSummary.Text = "";
        _resultTab.Text = "比較結果";
        _issueTab.Text = "確認が必要な行";
        _columnTab.Text = "列の違い";
    }

    public void SetTrimValues(bool trimValues)
    {
        _updatingTrim = true;
        try
        {
            _trimCheck.Checked = trimValues;
        }
        finally
        {
            _updatingTrim = false;
        }
    }

    public void SetCommandState(CommandState state)
    {
        UseWaitCursor = !state.InputsEnabled;
        foreach (Control control in new Control[] { _oldPathBox, _newPathBox, _oldBrowseButton, _newBrowseButton, _keyList, _trimCheck })
            control.Enabled = state.InputsEnabled;
        AllowDrop = state.InputsEnabled;
        _swapButton.Enabled = state.CanSwap;
        _compareButton.Enabled = state.CanCompare;
        _saveButton.Enabled = state.CanSave;
    }

    public void ShowStatus(string text) => _status.Text = text;

    public void ShowWarning(string text) =>
        MessageBox.Show(this, text, MainPresenter.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public void ShowInformation(string text) =>
        MessageBox.Show(this, text, MainPresenter.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public bool Confirm(string text) =>
        MessageBox.Show(this, text, MainPresenter.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public string? AskOpenFile(FileSide side)
    {
        using var dialog = new OpenFileDialog
        {
            Title = side == FileSide.Old ? "旧ファイル（比較元）を選ぶ" : "新ファイル（比較先）を選ぶ",
            Filter = "CSVファイル (*.csv;*.txt)|*.csv;*.txt|すべてのファイル (*.*)|*.*",
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    public string? AskSaveFile(string defaultFileName)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "比較結果を保存",
            Filter = "CSVファイル (*.csv)|*.csv",
            FileName = defaultFileName,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    // ---------------------------------------------------------------- 表の見た目（画面だけの関心事）

    private void ApplyFilter()
    {
        if (_resultView is null || _shownTable is null) return;
        var labels = _shownTable.KindLabels;   // 変更・追加・削除の順
        var kinds = new List<string>();
        if (_showChanged.Checked) kinds.Add(labels[0]);
        if (_showAdded.Checked) kinds.Add(labels[1]);
        if (_showDeleted.Checked) kinds.Add(labels[2]);
        if (kinds.Count == 0) _status.Text = "表示する種類が選ばれていません。「表示」のチェックを入れてください。";
        _resultView.RowFilter = kinds.Count == 0
            ? "1 = 0"
            : $"[c0] IN ({string.Join(",", kinds.Select(k => $"'{k}'"))})";
    }

    private ResultRow? RowAt(int rowIndex)
    {
        if (_shownTable is null || rowIndex < 0 || rowIndex >= _grid.Rows.Count) return null;
        return _grid.Rows[rowIndex].DataBoundItem is DataRowView rowView
            ? _shownTable.Rows[(int)rowView[EntryIndexColumn]]
            : null;
    }

    private ResultCell? CellAt(ResultRow row, int columnIndex)
    {
        if (columnIndex < 0) return null;
        var name = _grid.Columns[columnIndex].DataPropertyName;
        return name.StartsWith('c') && int.TryParse(name.AsSpan(1), out var i) && i < row.Cells.Count ? row.Cells[i] : null;
    }

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (RowAt(e.RowIndex) is not { } row || e.CellStyle is null) return;
        switch (row.Kind)
        {
            case ResultRowKind.Added:
                e.CellStyle.BackColor = AddedColor;
                break;
            case ResultRowKind.Deleted:
                e.CellStyle.BackColor = DeletedColor;
                e.CellStyle.ForeColor = Color.DimGray;
                break;
            case ResultRowKind.Changed when CellAt(row, e.ColumnIndex)?.IsChanged == true:
                e.CellStyle.BackColor = ChangedCellColor;
                break;
        }
    }

    private void Grid_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (RowAt(e.RowIndex) is { } row && CellAt(row, e.ColumnIndex) is { IsChanged: true } cell)
            e.ToolTipText = $"変更前：{cell.ChangedFrom}\n変更後：{cell.ChangedTo}";
    }
}
