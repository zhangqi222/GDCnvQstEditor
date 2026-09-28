using System.Text;
using CnvQst;
using CnvQst.Cli;

namespace CnvQstStudio;

/// <summary>
/// 批量处理面板：目录扫描 → 勾选 → 批量导出 TXT/JSON、校验、TXT 回填、CSV 报告。
/// 布局：源目录行 / 选项行 / 操作行 / 结果表 / 状态行，全部走 <see cref="Theme"/> 与 <see cref="L"/>。
/// </summary>
internal sealed class BatchForm : Form
{
    private readonly TextBox _path = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill };
    private readonly Button _scan = new() { Text = L.T("扫描"), Width = 90 };
    private readonly Button _browse = new() { Text = L.T("浏览"), Width = 90 };
    private readonly CheckBox _subDirs = new() { Text = L.T("含子目录"), Checked = true, AutoSize = true };
    private readonly Button _exportTxt = new() { Text = L.T("导出 TXT"), Width = 110 };
    private readonly Button _exportJson = new() { Text = L.T("导出 JSON"), Width = 120 };
    private readonly Button _verify = new() { Text = L.T("校验所选"), Width = 100 };
    private readonly Button _importTxt = new() { Text = L.T("TXT 回填"), Width = 110 };
    private readonly Button _csv = new() { Text = L.T("导出报告 CSV"), Width = 140 };
    private readonly CheckBox _flat = new() { Text = L.T("输出平铺（不镜像目录结构）"), AutoSize = true };
    private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted };
    private List<BatchItem> _items = new();

    public BatchForm()
    {
        Theme.Apply(this);
        Text = L.T("批量处理");
        Width = 1080;
        Height = 680;
        MinimumSize = new Size(900, 520);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Surface;

        // ---- 顶部：源目录 + 选项 + 操作 ----
        var head = new Panel { Dock = DockStyle.Top, Height = 132, Padding = new Padding(14, 12, 14, 6), BackColor = Theme.Surface };

        var row1 = new Panel { Dock = DockStyle.Top, Height = 34 };
        var lblPath = Theme.Caption(L.T("源目录"));
        lblPath.Location = new Point(0, 8);
        lblPath.Width = 60;
        _path.Location = new Point(64, 4);
        _path.Width = 700;
        _path.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _browse.Location = new Point(772, 2);
        _browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _scan.Location = new Point(868, 2);
        _scan.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        row1.Controls.AddRange(new Control[] { lblPath, _path, _browse, _scan });

        var row2 = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(0, 6, 0, 0) };
        var flow2 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        flow2.Controls.AddRange(new Control[] { _subDirs, _flat });
        row2.Controls.Add(flow2);

        var row3 = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(0, 6, 0, 0) };
        var flow3 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        flow3.Controls.AddRange(new Control[] { _exportTxt, _exportJson, _verify, _importTxt, _csv });
        row3.Controls.Add(flow3);

        head.Controls.Add(row3);
        head.Controls.Add(row2);
        head.Controls.Add(row1);

        // ---- 结果表 ----
        BuildColumns();
        Theme.StyleGrid(_grid);
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.CellValueChanged += (_, e) => { if (e.ColumnIndex == 0) UpdateCounts(); };
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(14, 0, 14, 0), BackColor = Theme.Bg };
        bottom.Controls.Add(_status);

        Controls.Add(_grid);
        Controls.Add(head);
        Controls.Add(bottom);

        // ---- 事件 ----
        _browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = L.T("选择源目录") };
            if (Directory.Exists(_path.Text)) dlg.SelectedPath = _path.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK) _path.Text = dlg.SelectedPath;
        };
        _scan.Click += (_, _) => Scan();
        _exportTxt.Click += (_, _) => Export(ExportKind.Txt);
        _exportJson.Click += (_, _) => Export(ExportKind.Json);
        _verify.Click += (_, _) => VerifySelected();
        _importTxt.Click += (_, _) => ImportTxt();
        _csv.Click += (_, _) => ExportCsv();
        foreach (var b in new[] { _scan, _browse, _exportTxt, _exportJson, _verify, _importTxt, _csv }) Theme.MakeGhost(b);

        SetStatus(L.T("就绪。先选择源目录，再点“扫描”。"));
    }

    private enum ExportKind { Txt, Json }

    private void BuildColumns()
    {
        void Col(string name, string header, int weight, bool check = false)
        {
            DataGridViewColumn c = check
                ? new DataGridViewCheckBoxColumn { Name = name, HeaderText = header }
                : new DataGridViewTextBoxColumn { Name = name, HeaderText = header };
            c.FillWeight = weight;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(c);
        }
        Col("sel", L.T("选"), 5, check: true);
        Col("file", L.T("文件"), 34);
        Col("kind", L.T("类型"), 7);
        Col("ver", L.T("版本"), 7);
        Col("count", L.T("节点 / task"), 10);
        Col("lines", L.T("txt 行数"), 8);
        Col("ok", L.T("校验"), 7);
        Col("err", L.T("失败原因"), 22);
    }

    /// <summary>供主窗口"打开目录（批量）"直接带入路径。</summary>
    public void PresetDirectory(string dir)
    {
        _path.Text = dir;
        Scan();
    }

    private void SetStatus(string text) => _status.Text = text;

    private void UpdateCounts()
    {
        var sel = SelectedFiles().Count;
        SetStatus(L.F("已扫描 {0} 个文件，勾选 {1} 个。", _items.Count, sel));
    }

    // ------------------------------------------------------------------

    private void Scan()
    {
        string path = _path.Text.Trim();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path) && !File.Exists(path))
        {
            Warn(L.T("请先选择存在的文件或目录。"));
            return;
        }
        Cursor = Cursors.WaitCursor;
        try
        {
            _items = BatchOps.Scan(path);
            _grid.Rows.Clear();
            foreach (var it in _items)
            {
                int r = _grid.Rows.Add(true, it.Rel, it.Kind, it.Version == 0 ? "" : "v" + it.Version,
                    it.StepsOrTasks, it.TxtLines, it.AllOk ? "✓" : "✗", it.FailReason);
                _grid.Rows[r].Cells["ok"].Style.ForeColor = it.AllOk ? Theme.Ok : Theme.Danger;
                if (!it.AllOk) _grid.Rows[r].Cells["err"].Style.ForeColor = Theme.Danger;
            }
            SetStatus(L.F("扫描 {0} 个文件：通过 {1}，失败 {2}。", _items.Count,
                _items.Count(i => i.AllOk), _items.Count(i => !i.AllOk)));
        }
        finally { Cursor = Cursors.Default; }
    }

    private List<string> SelectedFiles()
    {
        var list = new List<string>();
        for (int i = 0; i < _grid.Rows.Count; i++)
            if (Convert.ToBoolean(_grid.Rows[i].Cells["sel"].Value ?? true) && i < _items.Count)
                list.Add(_items[i].Path);
        return list;
    }

    private void Export(ExportKind kind)
    {
        var files = SelectedFiles();
        if (files.Count == 0) { Warn(L.T("请先扫描并勾选文件。")); return; }
        using var dlg = new FolderBrowserDialog { Description = L.T("选择输出目录") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        Cursor = Cursors.WaitCursor;
        try
        {
            int ok = 0, fail = 0;
            foreach (string file in files)
            {
                try
                {
                    byte[] data = File.ReadAllBytes(file);
                    var model = BatchOps.Load(file, data);
                    string target = BatchOps.TargetPath(file, _path.Text.Trim(), dlg.SelectedPath, _flat.Checked,
                        kind == ExportKind.Txt ? ".txt" : ".json");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (kind == ExportKind.Txt)
                    {
                        string txt = model switch
                        {
                            CnvFile c => TextExport.CnvTxt(c),
                            QstFile q => TextExport.QstTxt(q),
                            _ => throw new GdParseException(L.T("未知模型")),
                        };
                        File.WriteAllText(target, txt, new UTF8Encoding(false));
                    }
                    else
                    {
                        string name = Path.GetFileName(file);
                        string json = model switch
                        {
                            CnvFile c => JsonIO.Export(c, name),
                            QstFile q => JsonIO.Export(q, name),
                            _ => throw new GdParseException(L.T("未知模型")),
                        };
                        File.WriteAllText(target, json, new UTF8Encoding(false));
                    }
                    ok++;
                }
                catch { fail++; }
            }
            SetStatus(L.F("导出完成：成功 {0}，失败 {1}。输出目录 {2}", ok, fail, dlg.SelectedPath));
        }
        finally { Cursor = Cursors.Default; }
    }

    private void VerifySelected()
    {
        var files = SelectedFiles();
        if (files.Count == 0) { Warn(L.T("请先扫描并勾选文件。")); return; }
        Cursor = Cursors.WaitCursor;
        try
        {
            int pass = 0, fail = 0;
            var errors = new List<string>();
            foreach (string file in files)
            {
                var item = BatchOps.Inspect(file, _path.Text.Trim());
                if (item.AllOk) pass++;
                else { fail++; errors.Add($"{item.Rel}：{item.FailReason}"); }
            }
            SetStatus(L.F("校验完成：通过 {0}，失败 {1}。", pass, fail));
            if (errors.Count > 0)
                MessageBox.Show(this, string.Join("\n", errors.Take(20)) + (errors.Count > 20 ? L.F("\n…共 {0} 条", errors.Count) : ""),
                    L.T("校验失败项"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { Cursor = Cursors.Default; }
    }

    private void ImportTxt()
    {
        string binPath = _path.Text.Trim();
        if (string.IsNullOrEmpty(binPath) || !Directory.Exists(binPath) && !File.Exists(binPath))
        {
            Warn(L.T("请先选择二进制源目录。"));
            return;
        }
        using var dlg = new FolderBrowserDialog { Description = L.T("选择译文 txt 所在目录（按同名配对）") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        using var outDlg = new FolderBrowserDialog { Description = L.T("选择输出目录（不覆盖源文件）") };
        if (outDlg.ShowDialog(this) != DialogResult.OK) return;

        Cursor = Cursors.WaitCursor;
        try
        {
            var results = BatchOps.ImportTxtDir(binPath, dlg.SelectedPath, outDlg.SelectedPath);
            int ok = results.Count(r => r.Error == null), fail = results.Count - ok;
            SetStatus(L.F("TXT 回填完成：成功 {0}，跳过/失败 {1}。输出目录 {2}", ok, fail, outDlg.SelectedPath));
            var problems = results.Where(r => r.Error != null).Select(r => $"{Path.GetFileName(r.Bin)}：{r.Error}").ToList();
            if (problems.Count > 0)
                MessageBox.Show(this, string.Join("\n", problems.Take(20)) + (problems.Count > 20 ? L.F("\n…共 {0} 条", problems.Count) : ""),
                    L.T("回填未完成项"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { Cursor = Cursors.Default; }
    }

    private void ExportCsv()
    {
        if (_items.Count == 0) { Warn(L.T("请先扫描。")); return; }
        using var dlg = new SaveFileDialog { Filter = L.T("CSV|*.csv"), FileName = "batch_report.csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var sb = new StringBuilder();
        sb.AppendLine(L.T("文件,类型,版本,节点或task,txt行数,通过,失败原因"));
        foreach (var i in _items)
            sb.Append('"').Append(i.Rel.Replace("\"", "\"\"")).Append("\",")
              .Append(i.Kind).Append(',').Append(i.Version).Append(',')
              .Append(i.StepsOrTasks).Append(',').Append(i.TxtLines).Append(',')
              .Append(i.AllOk ? L.T("是") : L.T("否")).Append(",\"").Append(i.FailReason.Replace("\"", "\"\"")).Append("\"\n");
        File.WriteAllText(dlg.FileName, "\uFEFF" + sb, new UTF8Encoding(false));
        SetStatus(L.F("报告已写出 {0}", dlg.FileName));
    }

    private void Warn(string text) => MessageBox.Show(this, text, L.T("批量处理"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
