using System.Text;
using CnvQst;
using CnvQst.Cli;

namespace CnvQstStudio;

/// <summary>批量面板：目录扫描表 + 批量导出 TXT/JSON + 校验 + TXT 回填 + CSV 报告。</summary>
internal sealed class BatchForm : Form
{
    private readonly TextBox _path = new() { Width = 520, Dock = DockStyle.Top };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
    };
    private readonly Button _scan = new() { Text = "扫描", Width = 80 };
    private readonly Button _browse = new() { Text = "浏览…", Width = 80 };
    private readonly CheckBox _subDirs = new() { Text = "含子目录", Checked = true, AutoSize = true };
    private readonly Button _exportTxt = new() { Text = "导出 TXT…", Width = 110 };
    private readonly Button _exportJson = new() { Text = "导出 JSON…", Width = 130 };
    private readonly Button _verify = new() { Text = "校验所选", Width = 100 };
    private readonly Button _importTxt = new() { Text = "TXT 回填…", Width = 110 };
    private readonly Button _csv = new() { Text = "导出报告 CSV…", Width = 140 };
    private readonly CheckBox _flat = new() { Text = "输出平铺（不镜像目录结构）", AutoSize = true };
    private List<BatchItem> _items = new();

    public BatchForm()
    {
        Text = "批量处理";
        Width = 1000;
        Height = 640;
        StartPosition = FormStartPosition.CenterParent;

        var top = new Panel { Dock = DockStyle.Top, Height = 96 };
        var row1 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
        row1.Controls.AddRange(new Control[] { new Label { Text = " 源目录：", AutoSize = true }, _path, _browse, _scan });
        var row2 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
        row2.Controls.AddRange(new Control[] { _subDirs, _flat, _exportTxt, _exportJson, _verify, _importTxt, _csv });
        _path.Dock = DockStyle.Fill;
        top.Controls.Add(row2);
        top.Controls.Add(row1);
        row1.BringToFront();

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 30 };
        var status = new Label { Text = "就绪。", AutoSize = true };
        bottom.Controls.Add(status);

        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(bottom);

        _grid.Columns.Add("sel", "选");
        _grid.Columns.Add("file", "文件");
        _grid.Columns.Add("kind", "类型");
        _grid.Columns.Add("ver", "版本");
        _grid.Columns.Add("count", "节点/task");
        _grid.Columns.Add("lines", "txt 行数");
        _grid.Columns.Add("ok", "校验");
        _grid.Columns.Add("err", "失败原因");
        _grid.Columns["sel"].FillWeight = 5;
        _grid.Columns["file"].FillWeight = 34;
        _grid.Columns["kind"].FillWeight = 7;
        _grid.Columns["ver"].FillWeight = 7;
        _grid.Columns["count"].FillWeight = 10;
        _grid.Columns["lines"].FillWeight = 8;
        _grid.Columns["ok"].FillWeight = 7;
        _grid.Columns["err"].FillWeight = 22;
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;

        _browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog();
            if (Directory.Exists(_path.Text)) dlg.SelectedPath = _path.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK) _path.Text = dlg.SelectedPath;
        };
        _scan.Click += (_, _) => Scan(status);
        _exportTxt.Click += (_, _) => Export(ExportKind.Txt, status);
        _exportJson.Click += (_, _) => Export(ExportKind.Json, status);
        _verify.Click += (_, _) => VerifySelected(status);
        _importTxt.Click += (_, _) => ImportTxt(status);
        _csv.Click += (_, _) => ExportCsv(status);
    }

    private enum ExportKind { Txt, Json }

    private void Scan(Label status)
    {
        string path = _path.Text.Trim();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path) && !File.Exists(path))
        {
            MessageBox.Show(this, "请先选择存在的文件或目录。", "批量处理");
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
                if (!it.AllOk) _grid.Rows[r].Cells["ok"].Style.ForeColor = Color.Firebrick;
            }
            status.Text = $"扫描 {_items.Count} 个文件，通过 {_items.Count(i => i.AllOk)}，失败 {_items.Count(i => !i.AllOk)}。";
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

    private void Export(ExportKind kind, Label status)
    {
        var files = SelectedFiles();
        if (files.Count == 0) { MessageBox.Show(this, "请先扫描并勾选文件。", "批量处理"); return; }
        using var dlg = new FolderBrowserDialog { Description = "选择输出目录" };
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
                            _ => throw new GdParseException("未知模型"),
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
                            _ => throw new GdParseException("未知模型"),
                        };
                        File.WriteAllText(target, json, new UTF8Encoding(false));
                    }
                    ok++;
                }
                catch
                {
                    fail++;
                }
            }
            status.Text = $"导出完成：成功 {ok}，失败 {fail}。输出目录 {dlg.SelectedPath}";
        }
        finally { Cursor = Cursors.Default; }
    }

    private void VerifySelected(Label status)
    {
        var files = SelectedFiles();
        if (files.Count == 0) { MessageBox.Show(this, "请先扫描并勾选文件。", "批量处理"); return; }
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
            status.Text = $"校验完成：通过 {pass}，失败 {fail}。";
            if (errors.Count > 0)
                MessageBox.Show(this, string.Join("\n", errors.Take(20)) + (errors.Count > 20 ? $"\n…共 {errors.Count} 条" : ""),
                    "校验失败项");
        }
        finally { Cursor = Cursors.Default; }
    }

    private void ImportTxt(Label status)
    {
        string binPath = _path.Text.Trim();
        if (string.IsNullOrEmpty(binPath) || !Directory.Exists(binPath) && !File.Exists(binPath))
        {
            MessageBox.Show(this, "请先选择二进制源目录。", "TXT 回填");
            return;
        }
        using var dlg = new FolderBrowserDialog { Description = "选择译文 txt 所在目录（按同名配对）" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        using var outDlg = new FolderBrowserDialog { Description = "选择输出目录（不覆盖源文件）" };
        if (outDlg.ShowDialog(this) != DialogResult.OK) return;

        Cursor = Cursors.WaitCursor;
        try
        {
            var results = BatchOps.ImportTxtDir(binPath, dlg.SelectedPath, outDlg.SelectedPath);
            int ok = results.Count(r => r.Error == null), fail = results.Count - ok;
            status.Text = $"TXT 回填完成：成功 {ok}，跳过/失败 {fail}。输出目录 {outDlg.SelectedPath}";
            var problems = results.Where(r => r.Error != null).Select(r => $"{Path.GetFileName(r.Bin)}：{r.Error}").ToList();
            if (problems.Count > 0)
                MessageBox.Show(this, string.Join("\n", problems.Take(20)) + (problems.Count > 20 ? $"\n…共 {problems.Count} 条" : ""),
                    "回填未完成项");
        }
        finally { Cursor = Cursors.Default; }
    }

    private void ExportCsv(Label status)
    {
        if (_items.Count == 0) { MessageBox.Show(this, "请先扫描。", "批量处理"); return; }
        using var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "batch_report.csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var sb = new StringBuilder();
        sb.AppendLine("文件,类型,版本,节点或task,txt行数,通过,失败原因");
        foreach (var i in _items)
            sb.Append('"').Append(i.Rel.Replace("\"", "\"\"")).Append("\",")
              .Append(i.Kind).Append(',').Append(i.Version).Append(',')
              .Append(i.StepsOrTasks).Append(',').Append(i.TxtLines).Append(',')
              .Append(i.AllOk ? "是" : "否").Append(",\"").Append(i.FailReason.Replace("\"", "\"\"")).Append("\"\n");
        File.WriteAllText(dlg.FileName, "\uFEFF" + sb, new UTF8Encoding(false));
        status.Text = "报告已写出 " + dlg.FileName;
    }
}
