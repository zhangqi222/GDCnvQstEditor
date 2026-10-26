using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CnvQst;
using CnvQst.Cli;

namespace CnvQstEditor;

/// <summary>
/// 批量处理窗口（Avalonia 版）：目录扫描 → 勾选 → 批量导出 TXT/JSON、校验、CSV 报告。
/// 布局：源目录行 / 生成目录行 / 选项行 / 操作行 / 结果表 / 状态行，全部走 <see cref="L"/>。
/// </summary>
internal sealed class BatchWindow : Window
{
    private readonly TextBox _path = new();
    private readonly TextBox _outPath = new();
    private readonly CheckBox _subDirs = new() { Content = L.T("Include subfolders"), IsChecked = true };
    private readonly RadioButton _keepStructure = new() { Content = L.T("Keep original folder structure"), IsChecked = true, GroupName = "out" };
    private readonly RadioButton _flatten = new() { Content = L.T("Put all in one folder"), GroupName = "out" };
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        CanUserSortColumns = false,
        CanUserResizeColumns = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
    };
    private readonly TextBlock _status = new() { Foreground = UiTheme.MutedBrush, Margin = new Thickness(14, 2, 14, 6), TextWrapping = TextWrapping.Wrap };
    private WindowNotificationManager? _notify;
    private List<BatchItem> _items = new();

    public BatchWindow()
    {
        DialogChrome.Init(this, L.T("Batch"), 1080, 680);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = true;

        Styles.Add(new Style(x => x.OfType<CheckBox>().Class("batchSel"))
        {
            Setters =
            {
                new Setter(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Center),
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
                new Setter(Layoutable.MarginProperty, new Thickness(0, 2, 0, 0)),
            },
        });

        var browse = MkBtn(L.T("Browse"), () => _ = Browse());
        var scan = MkBtn(L.T("Scan"), Scan);
        var pathRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lbl = DialogChrome.Label(L.T("Source folder"));
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
        var lblOut = DialogChrome.Label(L.T("Output folder"));
        lblOut.SetValue(Grid.ColumnProperty, 0);
        _outPath.SetValue(Grid.ColumnProperty, 1);
        var outBrowse = MkBtn(L.T("Browse"), () => _ = BrowseOut());
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
        actRow.Children.Add(MkBtn(L.T("Select all"), () => SetAllSelected(true)));
        actRow.Children.Add(MkBtn(L.T("Select none"), () => SetAllSelected(false)));
        actRow.Children.Add(MkBtn(L.T("Export TXT"), () => Export(ExportKind.Txt)));
        actRow.Children.Add(MkBtn(L.T("Export JSON"), () => Export(ExportKind.Json)));
        actRow.Children.Add(MkBtn(L.T("Verify selected"), VerifySelected));
        actRow.Children.Add(MkBtn(L.T("Export CSV report"), () => _ = ExportCsv()));

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

        SetStatus(L.T("Ready. Pick a source folder, then click Scan."));
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
        _grid.Columns.Add(MkCheckColumn(L.T("Sel"), nameof(BatchRow.Sel)));
        _grid.Columns.Add(new DataGridTextColumn { Header = L.T("File"), Binding = new Avalonia.Data.Binding(nameof(BatchRow.File)), IsReadOnly = true, Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = L.T("Kind"), Binding = new Avalonia.Data.Binding(nameof(BatchRow.Kind)), IsReadOnly = true, Width = new DataGridLength(80) });
        _grid.Columns.Add(new DataGridTextColumn { Header = L.T("Ver"), Binding = new Avalonia.Data.Binding(nameof(BatchRow.Ver)), IsReadOnly = true, Width = new DataGridLength(64) });
        _grid.Columns.Add(new DataGridTextColumn { Header = L.T("Steps / tasks"), Binding = new Avalonia.Data.Binding(nameof(BatchRow.Count)), IsReadOnly = true, Width = new DataGridLength(120) });
        _grid.Columns.Add(new DataGridTextColumn { Header = L.T("TXT lines"), Binding = new Avalonia.Data.Binding(nameof(BatchRow.Lines)), IsReadOnly = true, Width = new DataGridLength(96) });
        _grid.Columns.Add(new DataGridTextColumn { Header = L.T("Check"), Binding = new Avalonia.Data.Binding(nameof(BatchRow.Ok)), IsReadOnly = true, Width = new DataGridLength(64) });
        _grid.Columns.Add(new DataGridTextColumn { Header = L.T("Failure reason"), Binding = new Avalonia.Data.Binding(nameof(BatchRow.Err)), IsReadOnly = true, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
    }

    /// <summary>勾选列（CheckBox 双向绑定直接写回模型）。</summary>
    private static DataGridTemplateColumn MkCheckColumn(string header, string propertyName)
    {
        var template = new FuncDataTemplate<object?>((_, _) =>
        {
            var cb = new CheckBox { Classes = { "batchSel" } };
            cb.Bind(CheckBox.IsCheckedProperty, new Avalonia.Data.Binding(propertyName) { Mode = Avalonia.Data.BindingMode.TwoWay });
            return cb;
        });
        return new DataGridTemplateColumn { Header = header, CellTemplate = template, Width = new DataGridLength(48) };
    }

    private sealed class BatchRow : INotifyPropertyChanged
    {
        private readonly BatchItem _it;
        private bool _sel = true;
        public BatchRow(BatchItem it) { _it = it; Path = it.Path; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Path { get; }
        public bool Sel
        {
            get => _sel;
            set
            {
                if (_sel == value) return;
                _sel = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Sel)));
            }
        }
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
        SetStatus(L.F("Scanned {0} files, {1} selected.", _items.Count, sel));
    }

    private void SetAllSelected(bool selected)
    {
        if (_grid.ItemsSource is not IEnumerable<BatchRow> rows) return;
        foreach (var row in rows) row.Sel = selected;
        UpdateCounts();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _notify = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.TopCenter,
            MaxItems = 3,
        };
    }

    private void Notify(string title, string message, NotificationType type) =>
        _notify?.Show(new Notification(title, message, type));

    private static NotificationType ResultType(int ok, int fail) =>
        fail == 0 ? NotificationType.Success : ok == 0 ? NotificationType.Error : NotificationType.Warning;

    // ------------------------------------------------------------------

    private async Task Browse()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L.T("Select source folder"),
            AllowMultiple = false,
        });
        if (folders.Count > 0) _path.Text = folders[0].Path.LocalPath;
    }

    private async Task BrowseOut()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L.T("Select output folder"),
            AllowMultiple = false,
        });
        if (folders.Count > 0) _outPath.Text = folders[0].Path.LocalPath;
    }

    private void Scan()
    {
        string path = _path.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path) && !File.Exists(path))
        {
            Gui.Warn(this, L.T("Pick an existing file or folder first."), Title ?? "");
            return;
        }
        try
        {
            _items = BatchOps.Scan(path);
            var rows = _items.Select(i => new BatchRow(i)).ToList();
            foreach (var row in rows)
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(BatchRow.Sel)) UpdateCounts();
                };
            _grid.ItemsSource = rows;
            SetStatus(L.F("Scanned {0} files: {1} passed, {2} failed.", _items.Count,
                _items.Count(i => i.AllOk), _items.Count(i => !i.AllOk)));
        }
        catch (Exception e)
        {
            Gui.Warn(this, L.F("Scan failed: {0}", e.Message), Title ?? "");
        }
    }

    private List<string> SelectedFiles()
    {
        if (_grid.ItemsSource is not IEnumerable<BatchRow> rows) return new List<string>();
        return rows.Where(r => r.Sel).Select(r => r.Path).ToList();
    }

    private void Export(ExportKind kind)
    {
        if (_grid.ItemsSource is not IEnumerable<BatchRow> rows || !rows.Any(r => r.Sel)) { Gui.Warn(this, L.T("Scan and select files first."), Title ?? ""); return; }
        string outDir = _outPath.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(outDir)) { Gui.Warn(this, L.T("Choose an output folder first."), Title ?? ""); return; }
        try { Directory.CreateDirectory(outDir); }
        catch (Exception e) { Gui.Warn(this, L.F("Cannot create output folder: {0}", e.Message), Title ?? ""); return; }

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
                        _ => throw new GdParseException(L.T("Unknown model")),
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
                        _ => throw new GdParseException(L.T("Unknown model")),
                    };
                    File.WriteAllText(target, json, new UTF8Encoding(false));
                }
                ok++;
            }
            catch { fail++; }
        }
        string msg = L.F("Exported: {0} ok, {1} failed. Output: {2}", ok, fail, outDir);
        SetStatus(msg);
        Notify(kind == ExportKind.Txt ? L.T("Export TXT") : L.T("Export JSON"), msg, ResultType(ok, fail));
    }

    private void VerifySelected()
    {
        if (_grid.ItemsSource is not IEnumerable<BatchRow> rows || !rows.Any(r => r.Sel)) { Gui.Warn(this, L.T("Scan and select files first."), Title ?? ""); return; }
        int pass = 0, fail = 0;
        var errors = new List<string>();
        foreach (string file in SelectedFiles())
        {
            var item = BatchOps.Inspect(file, _path.Text!.Trim());
            if (item.AllOk) pass++;
            else { fail++; errors.Add($"{item.Rel}：{item.FailReason}"); }
        }
        string msg = L.F("Verified: {0} passed, {1} failed.", pass, fail);
        SetStatus(msg);
        Notify(L.T("Verify selected"), msg, ResultType(pass, fail));
        if (errors.Count > 0)
            Gui.Warn(this, string.Join("\n", errors.Take(20)) + (errors.Count > 20 ? "\n" + L.F("…{0} total", errors.Count) : ""),
                L.T("Failed checks"));
    }

    private async Task ExportCsv()
    {
        if (_items.Count == 0) { Gui.Warn(this, L.T("Scan first."), Title ?? ""); return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("Export CSV report"),
            SuggestedFileName = "batch_report.csv",
            DefaultExtension = "csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } },
        });
        if (file == null) return;
        var sb = new StringBuilder();
        sb.AppendLine(L.T("File,Kind,Version,Steps or tasks,TXT lines,Passed,Failure reason"));
        foreach (var i in _items)
            sb.Append('"').Append(i.Rel.Replace("\"", "\"\"")).Append("\",")
              .Append(i.Kind).Append(',').Append(i.Version).Append(',')
              .Append(i.StepsOrTasks).Append(',').Append(i.TxtLines).Append(',')
              .Append(i.AllOk ? L.T("yes") : L.T("no")).Append(",\"").Append(i.FailReason.Replace("\"", "\"\"")).Append("\"\n");
        try
        {
            await File.WriteAllTextAsync(file.Path.LocalPath, "\uFEFF" + sb, new UTF8Encoding(false));
            string msg = L.F("Report written to {0}", file.Path.LocalPath);
            SetStatus(msg);
            Notify(L.T("Export CSV report"), msg, NotificationType.Success);
        }
        catch (Exception e)
        {
            string msg = L.F("Export failed: {0}", e.Message);
            SetStatus(msg);
            Notify(L.T("Export CSV report"), msg, NotificationType.Error);
        }
    }
}