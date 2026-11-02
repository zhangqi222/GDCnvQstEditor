using System.Collections;
using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using GDCnvQstLib;
using GDCnvQstLib.Cli;

namespace GDCnvQstEditor;

/// <summary>
/// 批量处理窗口（Avalonia 版）：目录扫描 → 勾选 → 批量导出 TXT/JSON、校验、CSV 报告。
/// 布局：源目录行 / 生成目录行 / 选项行 / 操作行 / 结果表 / 状态行，全部走 <see cref="L"/>。
/// </summary>
internal sealed class BatchWindow : Window
{
    private readonly TextBox _path = new();
    private readonly TextBox _outPath = new();
    private readonly CheckBox _subDirs = new() { Content = L.T("Include subfolders"), IsChecked = true, FontWeight = FontWeight.Normal };
    private readonly RadioButton _keepStructure = new() { Content = L.T("Keep original folder structure"), IsChecked = true, GroupName = "out", FontWeight = FontWeight.Normal };
    private readonly RadioButton _flatten = new() { Content = L.T("Put all in one folder"), GroupName = "out", FontWeight = FontWeight.Normal };
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        CanUserSortColumns = true,
        CanUserResizeColumns = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        RowHeight = 28,   // 表格区限高 10 行（见 tableHost.MaxHeight）
    };
    private readonly TextBlock _status = new() { Foreground = UiTheme.MutedBrush, Margin = new Thickness(14, 2, 14, 4), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _failText = new() { TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.DangerBrush, FontSize = 12 };
    private readonly Border _failPanel = new();
    private readonly Border _progressOverlay = new();
    private readonly ProgressBar _progressRing = new()
    {
        Width = 84,
        Height = 84,
        BorderThickness = new Thickness(8),
        Minimum = 0,
        Maximum = 1,
        ShowProgressText = false,
    };
    private readonly TextBlock _progressTitle = new()
    {
        Foreground = Brushes.White,
        FontSize = 14,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private readonly TextBlock _progressCount = new()
    {
        Foreground = Brushes.White,
        FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private readonly DispatcherTimer _scanTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private WindowNotificationManager? _notify;
    private List<BatchItem> _items = new();
    private int _scanVersion;    // 只采用最后一次自动扫描的结果
    private bool _bulkSelect;    // 批量勾选期间抑制逐行状态刷新
    private string? _autoOut;    // 最近一次自动生成的目录，用于区分用户自定义目录
    private bool _busy;          // 扫描 / 校验 / 导出进行中
    private bool _scanning;      // 其中扫描单独标记，处理扫描期间源路径变化
    private bool _pendingScan;   // 忙时收到扫描请求，结束后补扫

    private const string OutSubdirKeep = "CnvQstToTxtKeepLevel";
    private const string OutSubdirFlat = "CnvQstToTxtNoLevel";

    public BatchWindow()
    {
        DialogChrome.Init(this, L.T("Batch"), 1080, 620);
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
        // 批量面板统一不加粗（Semi 的按钮 / 单选 / 表头默认加粗）
        Styles.Add(new Style(x => x.OfType<DataGridColumnHeader>())
        {
            Setters = { new Setter(TemplatedControl.FontWeightProperty, FontWeight.Normal) },
        });

        var browse = MkBtn(L.T("Browse"), () => _ = Browse());
        var pathRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "BatchPathLabel" });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lbl = DialogChrome.Label(L.T("Source folder") + L.Colon);
        lbl.SetValue(Grid.ColumnProperty, 0);
        _path.SetValue(Grid.ColumnProperty, 1);
        browse.SetValue(Grid.ColumnProperty, 2);
        browse.Margin = new Thickness(6, 0, 0, 0);
        pathRow.Children.Add(lbl);
        pathRow.Children.Add(_path);
        pathRow.Children.Add(browse);

        var outRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        outRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "BatchPathLabel" });
        outRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lblOut = DialogChrome.Label(L.T("Output folder") + L.Colon);
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
        actRow.Children.Add(MkBtn(L.T("Select QST only"), () => SelectKind("QST")));
        actRow.Children.Add(MkBtn(L.T("Select CNV only"), () => SelectKind("CNV")));
        actRow.Children.Add(MkBtn(L.T("Select none"), () => SetAllSelected(false)));
        actRow.Children.Add(MkBtn(L.T("Export CSV report"), () => _ = ExportCsv()));

        BuildColumns();

        // 校验失败项：有失败时在表格上方显示（最多 5 条，可滚动）
        _failPanel.Background = new SolidColorBrush(Color.FromArgb(0x14, 0xD5, 0x25, 0x15));
        _failPanel.BorderBrush = UiTheme.DangerBrush;
        _failPanel.BorderThickness = new Thickness(0, 0, 0, 1);
        _failPanel.Padding = new Thickness(14, 6);
        _failPanel.Child = new ScrollViewer
        {
            Content = _failText,
            MaxHeight = 90,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _failPanel.IsVisible = false;

        // 进度遮罩：覆盖表格区域（扫描 / 校验 / 导出共用，圆形进度环 + 当前/总数）
        if (UiTheme.FindTheme("ProgressRing") is { } ringTheme) _progressRing.Theme = ringTheme;
        var overlayStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8,
        };
        overlayStack.Children.Add(_progressRing);
        overlayStack.Children.Add(_progressTitle);
        overlayStack.Children.Add(_progressCount);
        _progressOverlay.Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
        _progressOverlay.Child = overlayStack;
        _progressOverlay.IsVisible = false;

        var tableHost = new Grid { MaxHeight = 320, VerticalAlignment = VerticalAlignment.Top };
        tableHost.Children.Add(_grid);
        tableHost.Children.Add(_progressOverlay);

        // 底部主按钮：TXT 为主色，突出本地化工作流；重新扫描保留冗余入口
        var rescanBtn = MkBtn(L.T("Rescan"), () => _ = ScanAsync());
        var verifyBtn = MkBtn(L.T("Verify"), () => _ = VerifyAsync());
        var txtBtn = MkBtn(L.T("Export TXT"), () => _ = ExportAsync(ExportKind.Txt));
        // Semi 实心主色按钮：TXT 是本地化主流程，需要明显突出
        if (UiTheme.FindTheme("SolidButton") is { } solid) txtBtn.Theme = solid;
        txtBtn.Classes.Add("Primary");
        var jsonBtn = MkBtn(L.T("Export JSON"), () => _ = ExportAsync(ExportKind.Json));
        var bottomBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(14, 8, 14, 10),
        };
        bottomBar.Children.Add(rescanBtn);
        bottomBar.Children.Add(verifyBtn);
        bottomBar.Children.Add(txtBtn);
        bottomBar.Children.Add(jsonBtn);
        var bottomBorder = new Border
        {
            BorderBrush = UiTheme.GridLineBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = bottomBar,
        };

        var head = new StackPanel { Margin = new Thickness(14, 12, 14, 4) };
        Grid.SetIsSharedSizeScope(head, true);   // 两行标签按内容取宽，并保持一致
        head.Children.Add(pathRow);
        head.Children.Add(outRow);
        head.Children.Add(optRow);
        head.Children.Add(actRow);

        var center = new DockPanel();
        DockPanel.SetDock(_failPanel, Dock.Top);
        center.Children.Add(_failPanel);
        DockPanel.SetDock(_status, Dock.Bottom);   // 状态栏贴在表格下方
        center.Children.Add(_status);
        center.Children.Add(tableHost);

        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        DockPanel.SetDock(bottomBorder, Dock.Bottom);
        root.Children.Add(bottomBorder);
        root.Children.Add(center);
        Content = root;

        // 源目录变化 → 自动扫描（防抖）；生成目录随之自动填充
        _path.TextChanged += (_, _) =>
        {
            UpdateDefaultOutDir();
            ScheduleScan();
        };
        _keepStructure.IsCheckedChanged += (_, _) => UpdateDefaultOutDir();
        _flatten.IsCheckedChanged += (_, _) => UpdateDefaultOutDir();
        // 用户清空生成目录后立即恢复自动跟随（改为填其他目录时保持自定义）
        _outPath.TextChanged += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_outPath.Text)) UpdateDefaultOutDir();
        };
        _scanTimer.Tick += (_, _) =>
        {
            _scanTimer.Stop();
            _ = ScanAsync();
        };

        SetStatus(L.T("Ready. Pick a source folder; scanning starts automatically."));
    }

    /// <summary>
    /// 生成目录为空或仍等于上次自动生成值（即用户没改过）时，自动跟随源目录与结构选项：
    /// 源目录\CnvQstToTxtKeepLevel / 源目录\CnvQstToTxtNoLevel；源路径是文件时取所在目录。
    /// 用户手动改过的目录会保留，清空后恢复自动跟随。
    /// </summary>
    private void UpdateDefaultOutDir()
    {
        string input = _path.Text?.Trim() ?? "";
        if (input.Length == 0)
        {
            if ((_outPath.Text ?? "").Length == 0 || _outPath.Text == _autoOut) SetOutText("");
            return;
        }

        string full;
        try { full = Path.GetFullPath(input); } catch { return; }
        string baseDir = Directory.Exists(full) ? full
            : File.Exists(full) ? Path.GetDirectoryName(full) ?? ""
            : "";
        if (baseDir.Length == 0) return;   // 路径还没输完整：等下一次变化

        string sub = _keepStructure.IsChecked == true ? OutSubdirKeep : OutSubdirFlat;
        string desired = Path.Combine(baseDir, sub);
        string current = _outPath.Text?.Trim() ?? "";
        // 非空且不是自动生成值（也不是目标值）→ 用户自定义，保留
        if (current.Length > 0 && current != _autoOut && current != desired) return;
        SetOutText(desired);
    }

    private void SetOutText(string text)
    {
        _autoOut = text;
        if (_outPath.Text != text) _outPath.Text = text;
    }

    private enum ExportKind { Txt, Json }

    private static Button MkBtn(string text, Action onClick)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 2, 10, 2), FontWeight = FontWeight.Normal };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void BuildColumns()
    {
        _grid.Columns.Add(MkCheckColumn(L.T("Sel"), nameof(BatchRow.Sel)));
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = L.T("File"),
            Binding = new Avalonia.Data.Binding(nameof(BatchRow.File)),
            // 按完整文件名排序（同名列再用相对路径分胜负），不只看首个字符
            CustomSortComparer = RowBy((a, b) =>
                string.Compare(a.FileName, b.FileName, StringComparison.CurrentCultureIgnoreCase)),
            IsReadOnly = true,
            Width = new DataGridLength(3, DataGridLengthUnitType.Star),
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = L.T("Kind"),
            Binding = new Avalonia.Data.Binding(nameof(BatchRow.Kind)),
            CustomSortComparer = RowBy((a, b) =>
                string.Compare(a.Kind, b.Kind, StringComparison.CurrentCultureIgnoreCase)),
            IsReadOnly = true,
            Width = new DataGridLength(80),
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = L.T("Ver"),
            Binding = new Avalonia.Data.Binding(nameof(BatchRow.Ver)),
            CustomSortComparer = RowBy((a, b) => a.VersionNum.CompareTo(b.VersionNum)),
            IsReadOnly = true,
            Width = new DataGridLength(64),
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = L.T("Steps / tasks"),
            Binding = new Avalonia.Data.Binding(nameof(BatchRow.Count)),
            CustomSortComparer = RowBy((a, b) => a.CountNum.CompareTo(b.CountNum)),
            IsReadOnly = true,
            Width = new DataGridLength(120),
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = L.T("TXT lines"),
            Binding = new Avalonia.Data.Binding(nameof(BatchRow.Lines)),
            CustomSortComparer = RowBy((a, b) => a.LinesNum.CompareTo(b.LinesNum)),
            IsReadOnly = true,
            Width = new DataGridLength(96),
        });
        _grid.Columns.Add(MkOkColumn(L.T("Check")));
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = L.T("Failure reason"),
            Binding = new Avalonia.Data.Binding(nameof(BatchRow.Err)),
            CustomSortComparer = RowBy((a, b) =>
                string.Compare(a.Err, b.Err, StringComparison.CurrentCultureIgnoreCase)),
            IsReadOnly = true,
            Width = new DataGridLength(2, DataGridLengthUnitType.Star),
        });
    }

    /// <summary>DataGrid 的自定义排序比较器按整行对象比较，这里统一转成 BatchRow 间比较。</summary>
    private static IComparer RowBy(Comparison<BatchRow> compare) => new RowComparer(compare);

    private sealed class RowComparer : IComparer
    {
        private readonly Comparison<BatchRow> _compare;
        public RowComparer(Comparison<BatchRow> compare) => _compare = compare;

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is not BatchRow a) return -1;
            if (y is not BatchRow b) return 1;
            int result = _compare(a, b);
            // 同值时按完整相对路径兜底，保证排序稳定且可复现
            return result != 0 ? result : string.Compare(a.File, b.File, StringComparison.CurrentCultureIgnoreCase);
        }
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
        return new DataGridTemplateColumn { Header = header, CellTemplate = template, Width = new DataGridLength(48), CanUserSort = false };
    }

    /// <summary>校验列：通过为绿色 ✓，失败为红色 ✗。</summary>
    private static DataGridTemplateColumn MkOkColumn(string header)
    {
        var template = new FuncDataTemplate<object?>((_, _) =>
        {
            var tb = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            tb.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(BatchRow.Ok)));
            tb.Bind(TextBlock.ForegroundProperty, new Avalonia.Data.Binding(nameof(BatchRow.OkBrush)));
            return tb;
        });
        return new DataGridTemplateColumn
        {
            Header = header,
            CellTemplate = template,
            Width = new DataGridLength(64),
            CustomSortComparer = RowBy((a, b) => string.CompareOrdinal(a.Ok, b.Ok)),
        };
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
        /// <summary>文件列的排序键：只按文件名，忽略子目录。</summary>
        public string FileName => System.IO.Path.GetFileName(_it.Rel);
        public string Kind => _it.Kind;
        public string Ver => _it.Version == 0 ? "" : "v" + _it.Version;
        public int VersionNum => (int)_it.Version;
        public string Count => _it.StepsOrTasks.ToString();
        public int CountNum => _it.StepsOrTasks;
        public string Lines => _it.TxtLines.ToString();
        public int LinesNum => _it.TxtLines;
        public string Ok => _it.AllOk ? "✓" : "✗";
        public IBrush OkBrush => _it.AllOk ? UiTheme.OkBrush : UiTheme.DangerBrush;
        public string Err => _it.FailReason;
    }

    /// <summary>供主窗口"打开目录（批量）"直接带入路径。</summary>
    public void PresetDirectory(string dir)
    {
        _path.Text = dir;
        _scanTimer.Stop();   // 拖入目录时立即扫描，不等防抖
        _ = ScanAsync();
    }

    private void SetStatus(string text) => _status.Text = text;

    private void UpdateCounts()
    {
        var sel = SelectedFiles().Count;
        SetStatus(L.F("Scanned {0} files: {1} passed, {2} failed, {3} selected.",
            _items.Count, _items.Count(i => i.AllOk), _items.Count(i => !i.AllOk), sel));
    }

    private void SetAllSelected(bool selected)
    {
        if (_grid.ItemsSource is not IEnumerable<BatchRow> rows) return;
        BulkSelect(() => { foreach (var row in rows) row.Sel = selected; });
    }

    /// <summary>按类型勾选：QST 同时覆盖 QST2，CNV 覆盖 CNV / CNV-JSON。</summary>
    private void SelectKind(string kind)
    {
        if (_grid.ItemsSource is not IEnumerable<BatchRow> rows) return;
        BulkSelect(() =>
        {
            foreach (var row in rows)
                row.Sel = row.Kind.StartsWith(kind, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>整批勾选后只刷新一次状态栏，避免逐行 O(n²) 统计。</summary>
    private void BulkSelect(Action apply)
    {
        _bulkSelect = true;
        try { apply(); }
        finally { _bulkSelect = false; }
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
        if (folders.Count > 0)
        {
            _path.Text = folders[0].Path.LocalPath;
            _scanTimer.Stop();
            _ = ScanAsync();
        }
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

    /// <summary>源目录变化后自动扫描；400ms 防抖，连续输入只扫最后一次。</summary>
    private void ScheduleScan()
    {
        _scanTimer.Stop();
        _scanTimer.Start();
    }

    private async Task ScanAsync()
    {
        if (_busy)
        {
            _pendingScan = true;
            if (_scanning) _scanVersion++;   // 扫描中源路径又变了：丢弃本次结果，结束后补扫
            return;
        }
        string path = _path.Text?.Trim() ?? "";
        if (path.Length == 0 || (!Directory.Exists(path) && !File.Exists(path)))
        {
            _scanVersion++;              // 使进行中的扫描结果失效
            _items = new();
            _grid.ItemsSource = null;
            _failPanel.IsVisible = false;
            SetStatus(path.Length == 0
                ? L.T("Ready. Pick a source folder; scanning starts automatically.")
                : L.T("Pick an existing file or folder first."));
            return;
        }

        int version = ++_scanVersion;
        BeginBusy(L.T("Scanning…"), 0);
        _scanning = true;
        try
        {
            IProgress<(int Done, int Total)> progress = new Progress<(int Done, int Total)>(p => ReportProgress(p.Done, p.Total));
            var items = await Task.Run(() => BatchOps.Scan(path, (done, total) => progress.Report((done, total))));
            if (version != _scanVersion) return;
            _items = items;
            var rows = items.Select(i => new BatchRow(i)).ToList();
            foreach (var row in rows)
                row.PropertyChanged += (_, e) =>
                {
                    if (!_bulkSelect && e.PropertyName == nameof(BatchRow.Sel)) UpdateCounts();
                };
            _grid.ItemsSource = rows;
            ShowFailures(items);
            UpdateCounts();
        }
        catch (Exception e)
        {
            if (version != _scanVersion) return;
            SetStatus(L.F("Scan failed: {0}", e.Message));
            Gui.Warn(this, L.F("Scan failed: {0}", e.Message), Title ?? "");
        }
        finally
        {
            _scanning = false;
            EndBusy();
        }
    }

    /// <summary>开始遮罩表格并显示进度；total 未知时先显示无限旋转的圆环。</summary>
    private void BeginBusy(string title, int total)
    {
        _busy = true;
        _scanTimer.Stop();
        _progressTitle.Text = title;
        _progressCount.Text = total > 0 ? $"0 / {total}" : "";
        _progressRing.IsIndeterminate = total <= 0;
        _progressRing.Maximum = Math.Max(total, 1);
        _progressRing.Value = 0;
        _progressOverlay.IsVisible = true;
        _grid.IsEnabled = false;
    }

    /// <summary>更新圆形进度与「当前处理数 / 总数」。</summary>
    private void ReportProgress(int done, int total)
    {
        if (total <= 0) return;
        _progressRing.IsIndeterminate = false;
        _progressRing.Maximum = total;
        _progressRing.Value = done;
        _progressCount.Text = $"{done} / {total}";
    }

    private void EndBusy()
    {
        _busy = false;
        _progressOverlay.IsVisible = false;
        _grid.IsEnabled = true;
        if (!_pendingScan) return;
        _pendingScan = false;
        _ = ScanAsync();
    }

    /// <summary>顶部失败摘要：最多列 5 条（原因内换行压成一行），其余只提示总数。</summary>
    private void ShowFailures(List<BatchItem> items)
    {
        var failed = items.Where(i => !i.AllOk).ToList();
        _failPanel.IsVisible = failed.Count > 0;
        if (failed.Count == 0) return;
        var lines = failed.Take(5).Select(i =>
            i.Rel + L.Colon + " " + i.FailReason.ReplaceLineEndings(" "));
        _failText.Text = L.T("Failed checks") + L.Colon + Environment.NewLine + string.Join(Environment.NewLine, lines)
            + (failed.Count > 5 ? Environment.NewLine + L.F("…{0} total", failed.Count) : "");
    }

    private List<string> SelectedFiles()
    {
        if (_grid.ItemsSource is not IEnumerable<BatchRow> rows) return new List<string>();
        return rows.Where(r => r.Sel).Select(r => r.Path).ToList();
    }

    private async Task ExportAsync(ExportKind kind)
    {
        if (_busy) return;
        var files = SelectedFiles();
        if (files.Count == 0) { Gui.Warn(this, L.T("Scan and select files first."), Title ?? ""); return; }
        string outDir = _outPath.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(outDir)) { Gui.Warn(this, L.T("Choose an output folder first."), Title ?? ""); return; }
        try { Directory.CreateDirectory(outDir); }
        catch (Exception e) { Gui.Warn(this, L.F("Cannot create output folder: {0}", e.Message), Title ?? ""); return; }

        string baseDir = _path.Text!.Trim();
        bool flat = _flatten.IsChecked == true;
        string title = kind == ExportKind.Txt ? L.T("Export TXT") : L.T("Export JSON");
        int ok = 0, fail = 0;
        BeginBusy(title, files.Count);
        try
        {
            IProgress<int> progress = new Progress<int>(done => ReportProgress(done, files.Count));
            await Task.Run(() =>
            {
                int done = 0;
                foreach (string file in files)
                {
                    try
                    {
                        byte[] data = File.ReadAllBytes(file);
                        var model = BatchOps.Load(file, data);
                        string target = BatchOps.TargetPath(file, baseDir, outDir, flat,
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
                    progress.Report(++done);
                }
            });
        }
        finally { EndBusy(); }
        string msg = L.F("Exported: {0} ok, {1} failed. Output: {2}", ok, fail, outDir);
        SetStatus(msg);
        if (ok > 0)
            await MsgBox.Show(this, msg, title, MsgButtons.Ok, openFolder: outDir);
        else
            Notify(title, msg, NotificationType.Error);
    }

    private async Task VerifyAsync()
    {
        if (_busy) return;
        var files = SelectedFiles();
        if (files.Count == 0) { Gui.Warn(this, L.T("Scan and select files first."), Title ?? ""); return; }

        string baseDir = _path.Text!.Trim();
        int pass = 0, fail = 0;
        var errors = new List<string>();
        BeginBusy(L.T("Verifying…"), files.Count);
        try
        {
            IProgress<int> progress = new Progress<int>(done => ReportProgress(done, files.Count));
            await Task.Run(() =>
            {
                int done = 0;
                foreach (string file in files)
                {
                    var item = BatchOps.Inspect(file, baseDir);
                    if (item.AllOk) pass++;
                    else { fail++; errors.Add(item.Rel + L.Colon + " " + item.FailReason); }
                    progress.Report(++done);
                }
            });
        }
        finally { EndBusy(); }
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
            await MsgBox.Show(this, msg, L.T("Export CSV report"), MsgButtons.Ok,
                openFolder: Path.GetDirectoryName(file.Path.LocalPath));
        }
        catch (Exception e)
        {
            string msg = L.F("Export failed: {0}", e.Message);
            SetStatus(msg);
            Notify(L.T("Export CSV report"), msg, NotificationType.Error);
        }
    }
}