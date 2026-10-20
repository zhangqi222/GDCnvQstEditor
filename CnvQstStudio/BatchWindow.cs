using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CnvQst;
using CnvQst.Cli;

namespace CnvQstStudio;

/// <summary>
/// 批量处理窗口（Avalonia 版）：目录扫描 → 勾选 → 批量导出 TXT/JSON、校验、TXT 回填、CSV 报告。
/// 布局：源目录行 / 生成目录行 / 选项行 / 操作行 / 结果表 / 状态行，全部走 <see cref="L"/>。
/// </summary>
internal sealed class BatchWindow : Window
{
    private readonly TextBox _path = new();
    private readonly TextBox _outPath = new();
    private readonly CheckBox _subDirs = new() { Content = L.T("含子目录"), IsChecked = true };
    private readonly RadioButton _keepStructure = new() { Content = L.T("保持原目录结构"), IsChecked = true, GroupName = "out" };
    private readonly RadioButton _flatten = new() { Content = L.T("全部放在同一目录"), GroupName = "out" };
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        CanUserSortColumns = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
    };
    private readonly TextBlock _status = new() { Foreground = UiTheme.MutedBrush, Margin = new Thickness(14, 2, 14, 6), TextWrapping = TextWrapping.Wrap };
    private List<BatchItem> _items = new();

    public BatchWindow()
    {
        DialogChrome.Init(this, L.T("批量处理"), 1080, 680);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var browse = MkBtn(L.T("浏览"), () => _ = Browse());
        var scan = MkBtn(L.T("扫描"), Scan);
        var pathRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lbl = DialogChrome.Label(L.T("源目录"));
        lbl.SetValue(Grid.ColumnProperty, 0);
        _path.SetValue(Grid.ColumnProperty, 1);
        browse.SetValue(Grid.ColumnProperty, 2);
        scan.SetValue(Grid.ColumnProperty, 3);
        browse.Margin = new Thickness(6, 0, 6, 0);
        pathRow.Children.Add(lbl);
        pathRow.Children.Add(_path);
        pathRow.Children.Add(browse);
        pathRow.Children.Add(scan);

        var outRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        outRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        outRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lblOut = DialogChrome.Label(L.T("生成目录"));
        lblOut.SetValue(Grid.ColumnProperty, 0);
        _outPath.SetValue(Grid.ColumnProperty, 1);
        var outBrowse = MkBtn(L.T("浏览"), () => _ = BrowseOut());
        outBrowse.SetValue(Grid.ColumnProperty, 2);
        outBrowse.Margin = new Thickness(6, 0, 0, 0);
        outRow.Children.Add(lblOut);
        outRow.Children.Add(_outPath);
        outRow.Children.Add(outBrowse);

        var optRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 0, 0, 8) };
        optRow.Children.Add(_subDirs);
        optRow.Children.Add(_keepStructure);
        optRow.Children.Add(_flatten);

        var actRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        actRow.Children.Add(MkBtn(L.T("导出 TXT"), () => Export(ExportKind.Txt)));
        actRow.Children.Add(MkBtn(L.T("导出 JSON"), () => Export(ExportKind.Json)));
        actRow.Children.Add(MkBtn(L.T("校验所选"), VerifySelected));
        actRow.Children.Add(MkBtn(L.T("TXT 回填"), () => _ = ImportTxt()));
        actRow.Children.Add(MkBtn(L.T("导出报告 CSV"), () => _ = ExportCsv()));

        BuildColumns();

        var head = new StackPanel { Margin = new Thickness(14, 12, 14, 4) };
        head.Children.Add(pathRow);
        head.Children.Add(outRow);
        head.Children.Add(optRow);
        head.Children.Add(actRow);

        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        var bottom = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        bottom.Children.Add(_status);
        bottom.Children.Add(_grid);
        root.Children.Add(bottom);
        Content = root;

        SetStatus(L.T("就绪。先选择源目录，再点“扫描”。"));
    }

    private enum ExportKind { Txt, Json }

    private static Button MkBtn(string text, Action onClick)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 2, 10, 2) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void BuildColumns()
    {
        _grid.Columns.Add(MkCheckColumn(L.T("选"), nameof(BatchRow.Sel)));
        _grid.Columns.Add(new DataGridTextColumn { Header = L.T("文件"), Binding = new Avalonia.Data.Binding(nameof(BatchRow.File)), IsReadOnly = true, Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = L.T("类型"), Binding = new Avalonia.Data.Binding(nameof(BatchRow.Kind)), IsReadOnly = true, Width = new DataGridLength(56) });
        _grid.Columns.Add(new DataGridTextColumn { Header = L.T("版本"), Binding = new Avalonia.Data.Binding(nameof(BatchRow.Ver)), IsReadOnly = true, Width = new DataGridLength(52) });
        _grid.Columns.Add(new DataGridTextColumn { Header = L.T("节点 / task"), Binding = new Avalonia.Data.Binding(nameof(BatchRow.Count)), IsReadOnly = true, Width = new DataGridLength(80) });
        _grid.Columns.Add(new DataGridTextColumn { Header = L.T("txt 行数"), Binding = new Avalonia.Data.Binding(nameof(BatchRow.Lines)), IsReadOnly = true, Width = new DataGridLength(66) });
        _grid.Columns.Add(new DataGridTextColumn { Header = L.T("校验"), Binding = new Avalonia.Data.Binding(nameof(BatchRow.Ok)), IsReadOnly = true, Width = new DataGridLength(48) });
        _grid.Columns.Add(new DataGridTextColumn { Header = L.T("失败原因"), Binding = new Avalonia.Data.Binding(nameof(BatchRow.Err)), IsReadOnly = true, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
    }

    /// <summary>勾选列（CheckBox 双向绑定直接写回模型）。</summary>
    private static DataGridTemplateColumn MkCheckColumn(string header, string propertyName)
    {
        var template = new FuncDataTemplate<object?>((_, _) =>
        {
            var cb = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center };
            cb.Bind(CheckBox.IsCheckedProperty, new Avalonia.Data.Binding(propertyName) { Mode = Avalonia.Data.BindingMode.TwoWay });
            return cb;
        });
        return new DataGridTemplateColumn { Header = header, CellTemplate = template, Width = new DataGridLength(40) };
    }

    private sealed class BatchRow
    {
        private readonly BatchItem _it;
        public BatchRow(BatchItem it) { _it = it; Path = it.Path; Sel = true; }
        public string Path { get; }
        public bool Sel { get; set; }
        public string File => _it.Rel;
        public string Kind => _it.Kind;
        public string Ver => _it.Version == 0 ? "" : "v" + _it.Version;
        public string Count => _it.StepsOrTasks.ToString();
        public string Lines => _it.TxtLines.ToString();
        public string Ok => _it.AllOk ? "✓" : "✗";
        public string Err => _it.FailReason;
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

    private async Task Browse()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L.T("选择源目录"),
            AllowMultiple = false,
        });
        if (folders.Count > 0) _path.Text = folders[0].Path.LocalPath;
    }

    private async Task BrowseOut()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L.T("选择输出目录"),
            AllowMultiple = false,
        });
        if (folders.Count > 0) _outPath.Text = folders[0].Path.LocalPath;
    }

    private void Scan()
    {
        string path = _path.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path) && !File.Exists(path))
        {
            Gui.Warn(this, L.T("请先选择存在的文件或目录。"), Title ?? "");
            return;
        }
        try
        {
            _items = BatchOps.Scan(path);
            _grid.ItemsSource = _items.Select(i => new BatchRow(i)).ToList();
            SetStatus(L.F("扫描 {0} 个文件：通过 {1}，失败 {2}。", _items.Count,
                _items.Count(i => i.AllOk), _items.Count(i => !i.AllOk)));
        }
        catch (Exception e)
        {
            Gui.Warn(this, L.F("扫描失败：{0}", e.Message), Title ?? "");
        }
    }

    private List<string> SelectedFiles()
    {
        if (_grid.ItemsSource is not IEnumerable<BatchRow> rows) return new List<string>();
        return rows.Where(r => r.Sel).Select(r => r.Path).ToList();
    }

    private void Export(ExportKind kind)
    {
        if (_grid.ItemsSource is not IEnumerable<BatchRow> rows || !rows.Any(r => r.Sel)) { Gui.Warn(this, L.T("请先扫描并勾选文件。"), Title ?? ""); return; }
        string outDir = _outPath.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(outDir) || !Directory.Exists(outDir))
        {
            Gui.Warn(this, L.T("请先在“生成目录”中选择输出目录。"), Title ?? "");
            return;
        }

        int ok = 0, fail = 0;
        foreach (string file in SelectedFiles())
        {
            try
            {
                byte[] data = File.ReadAllBytes(file);
                var model = BatchOps.Load(file, data);
                string target = BatchOps.TargetPath(file, _path.Text!.Trim(), outDir, _flatten.IsChecked == true,
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
        SetStatus(L.F("导出完成：成功 {0}，失败 {1}。输出目录 {2}", ok, fail, outDir));
    }

    private void VerifySelected()
    {
        if (_grid.ItemsSource is not IEnumerable<BatchRow> rows || !rows.Any(r => r.Sel)) { Gui.Warn(this, L.T("请先扫描并勾选文件。"), Title ?? ""); return; }
        int pass = 0, fail = 0;
        var errors = new List<string>();
        foreach (string file in SelectedFiles())
        {
            var item = BatchOps.Inspect(file, _path.Text!.Trim());
            if (item.AllOk) pass++;
            else { fail++; errors.Add($"{item.Rel}：{item.FailReason}"); }
        }
        SetStatus(L.F("校验完成：通过 {0}，失败 {1}。", pass, fail));
        if (errors.Count > 0)
            Gui.Warn(this, string.Join("\n", errors.Take(20)) + (errors.Count > 20 ? L.F("\n…共 {0} 条", errors.Count) : ""),
                L.T("校验失败项"));
    }

    private async Task ImportTxt()
    {
        string binPath = _path.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(binPath) || !Directory.Exists(binPath) && !File.Exists(binPath))
        {
            Gui.Warn(this, L.T("请先选择二进制源目录。"), Title ?? "");
            return;
        }
        string outDir = _outPath.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(outDir) || !Directory.Exists(outDir))
        {
            Gui.Warn(this, L.T("请先在“生成目录”中选择输出目录（不覆盖源文件）。"), Title ?? "");
            return;
        }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L.T("选择译文 txt 所在目录（按同名配对）"),
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;

        var results = BatchOps.ImportTxtDir(binPath, folders[0].Path.LocalPath, outDir);
        int ok = results.Count(r => r.Error == null), fail = results.Count - ok;
        SetStatus(L.F("TXT 回填完成：成功 {0}，跳过/失败 {1}。输出目录 {2}", ok, fail, outDir));
        var problems = results.Where(r => r.Error != null).Select(r => $"{Path.GetFileName(r.Bin)}：{r.Error}").ToList();
        if (problems.Count > 0)
            Gui.Warn(this, string.Join("\n", problems.Take(20)) + (problems.Count > 20 ? L.F("\n…共 {0} 条", problems.Count) : ""),
                L.T("回填未完成项"));
    }

    private async Task ExportCsv()
    {
        if (_items.Count == 0) { Gui.Warn(this, L.T("请先扫描。"), Title ?? ""); return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("导出报告 CSV"),
            SuggestedFileName = "batch_report.csv",
            DefaultExtension = "csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } },
        });
        if (file == null) return;
        var sb = new StringBuilder();
        sb.AppendLine(L.T("文件,类型,版本,节点或task,txt行数,通过,失败原因"));
        foreach (var i in _items)
            sb.Append('"').Append(i.Rel.Replace("\"", "\"\"")).Append("\",")
              .Append(i.Kind).Append(',').Append(i.Version).Append(',')
              .Append(i.StepsOrTasks).Append(',').Append(i.TxtLines).Append(',')
              .Append(i.AllOk ? L.T("是") : L.T("否")).Append(",\"").Append(i.FailReason.Replace("\"", "\"\"")).Append("\"\n");
        await File.WriteAllTextAsync(file.Path.LocalPath, "\uFEFF" + sb, new UTF8Encoding(false));
        SetStatus(L.F("报告已写出 {0}", file.Path.LocalPath));
    }
}