using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CnvQst;

namespace CnvQstEditor;

/// <summary>文档视图模式：可视化编辑器（表格 + 底部编辑器）、JSON、本地化文本、DUMP。</summary>
internal enum DocViewMode
{
    Visual,
    Json,
    Localization,
    Dump,
}

/// <summary>
/// 单个文档视图：100% 宽度的树形表格（结构与数据结合展示）+ 页内状态行。
/// 四种视图整体切换（<see cref="SetView"/>）：可视化编辑器 / JSON 模式 / 本地化文本（只读）/ DUMP（只读）。
/// </summary>
internal sealed class DocView : UserControl
{
    public DocSession Doc { get; }
    public DocViewMode Mode { get; private set; } = DocViewMode.Visual;
    public event Action? DirtyChanged;
    public event Action? ModeChanged;

    private readonly CnvTableEditor _cnvTable = new() { IsVisible = false };
    private readonly QstTableEditor _qstTable = new() { IsVisible = false };
    private readonly LocalizationGrid _locGrid = new() { IsVisible = false };
    private readonly TextBlock _emptyHint = new()
    {
        Text = L.EmptyHint,
        Foreground = UiTheme.MutedBrush,
        Margin = new Thickness(28),
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Top,
    };
    private readonly Grid _host = new();
    private readonly NodeTextPanel _panel = new() { IsVisible = false, ClipToBounds = true };
    private readonly GridSplitter _panelSplitter = new()
    {
        Height = 6,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
        ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        ResizeDirection = GridResizeDirection.Rows,
        IsVisible = false,
    };
    private readonly Grid _rootGrid = new();
    private readonly Button _panelToggle = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Padding = new Thickness(12, 1, 12, 1),
        MinHeight = 20,
        FontSize = 11,
        CornerRadius = new CornerRadius(10),
        BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
        Background = Brushes.White,
        Foreground = UiTheme.MutedBrush,
    };

    // JSON 模式：等宽文本框 + 应用 / 重新载入
    private readonly TextBox _jsonBox = new()
    {
        AcceptsReturn = true,
        AcceptsTab = true,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        TextWrapping = TextWrapping.NoWrap,
        IsVisible = false,
    };
    private readonly TextBlock _jsonStatus = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.MutedBrush };
    private readonly Grid _jsonHost = new() { IsVisible = false };
    private readonly Button _jsonApply = new() { Content = L.T("Apply JSON") };
    private readonly Button _jsonReload = new() { Content = L.T("Reload") };
    private bool _jsonDirty;
    private bool _jsonBuilding;

    // DUMP 模式：只读文本
    private readonly TextBox _dumpBox = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        TextWrapping = TextWrapping.NoWrap,
        IsVisible = false,
    };

    private bool _panelCollapsed;
    private GridLength _panelHeight = new(180);

    public DocView(DocSession doc)
    {
        Doc = doc;

        ScrollViewer.SetVerticalScrollBarVisibility(_jsonBox, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_jsonBox, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_dumpBox, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_dumpBox, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        _jsonApply.Click += (_, _) => ApplyJson();
        _jsonReload.Click += (_, _) => LoadJson(force: true);
        _jsonBox.TextChanged += (_, _) =>
        {
            if (_jsonBuilding) return;
            _jsonDirty = true;
            _jsonStatus.Text = L.T("JSON modified (not applied yet)");
        };

        var jsonBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(10, 6, 10, 4),
            Children = { _jsonApply, _jsonReload, _jsonStatus },
        };
        _jsonHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _jsonHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(jsonBar, 0);
        Grid.SetRow(_jsonBox, 1);
        _jsonHost.Children.Add(jsonBar);
        _jsonHost.Children.Add(_jsonBox);

        foreach (var c in new Control[] { _cnvTable, _qstTable, _locGrid, _emptyHint, _jsonHost, _dumpBox }) _host.Children.Add(c);

        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) });
        // 分割行 = 拖动调高的 GridSplitter + 居中的显式折叠/展开按钮（文字 + 箭头，便于识别）
        var splitterHost = new Grid();
        splitterHost.Children.Add(_panelSplitter);
        splitterHost.Children.Add(_panelToggle);
        Grid.SetRow(_host, 0);
        Grid.SetRow(splitterHost, 1);
        Grid.SetRow(_panel, 2);
        _rootGrid.Children.Add(_host);
        _rootGrid.Children.Add(splitterHost);
        _rootGrid.Children.Add(_panel);
        // 分割栏：拖动调高，点击（无拖动）在 折叠 / 展开 之间切换
        ToolTip.SetTip(_panelSplitter, L.T("Drag to resize; click to collapse / expand the bottom editor."));
        ToolTip.SetTip(_panelToggle, L.T("Click to collapse / expand the bottom editor."));
        _panelSplitter.Tapped += (_, _) => TogglePanel();
        _panelToggle.Click += (_, _) => TogglePanel();
        UpdateToggle();
        Content = _rootGrid;

        _cnvTable.LocRequested += ToggleLocalization;
        _qstTable.LocRequested += ToggleLocalization;
        _cnvTable.SelectedChanged += OnSelectedChanged;
        _qstTable.SelectedChanged += OnSelectedChanged;
        _panel.TextEdited += () =>
        {
            Doc.Dirty = true;
            DirtyChanged?.Invoke();
            if (_cnvTable.IsVisible)
            {
                if (_cnvTable.SelectedTextSlot is int s) _cnvTable.RefreshTextSlot(s);
            }
            else if (_qstTable.IsVisible && _qstTable.SelectedTextSlot is int q)
            {
                _qstTable.RefreshTextSlot(q);
            }
        };

        SetView(DocViewMode.Visual);
    }

    /// <summary>切换整体视图（可视化 / JSON / 本地化 / DUMP）。</summary>
    public void SetView(DocViewMode mode)
    {
        Mode = mode;
        switch (mode)
        {
            case DocViewMode.Visual:
                ShowStructure();
                break;
            case DocViewMode.Json:
                ShowSingle(_jsonHost);
                LoadJson(force: false);
                break;
            case DocViewMode.Localization:
                ShowSingle(_locGrid);
                _locGrid.Bind(Doc, OnEdited);
                break;
            case DocViewMode.Dump:
                ShowSingle(_dumpBox);
                LoadDump();
                break;
        }
        ModeChanged?.Invoke();
    }

    /// <summary>选中行变化 → 底部编辑器绑定该行的文本槽位。</summary>
    private void OnSelectedChanged()
    {
        if (!_panel.IsVisible) return;
        int? slot = _cnvTable.IsVisible ? _cnvTable.SelectedTextSlot
            : _qstTable.IsVisible ? _qstTable.SelectedTextSlot
            : null;
        _panel.Bind(Doc, slot);
    }

    /// <summary>点击分割栏/按钮：折叠（高度记入 _panelHeight 后置 0）/ 展开（恢复上次高度）。</summary>
    private void TogglePanel()
    {
        var row = _rootGrid.RowDefinitions[2];
        if (_panelCollapsed)
        {
            row.Height = _panelHeight;
            _panelCollapsed = false;
        }
        else
        {
            // 记录当前实际高度（拖动后 RowDefinition.Height 可能仍是旧值，以 ActualHeight 为准）
            double h = row.ActualHeight;
            if (h > 8) _panelHeight = new GridLength(Math.Max(h, 40));
            row.Height = new GridLength(0);
            _panelCollapsed = true;
        }
        UpdateToggle();
    }

    /// <summary>分割栏按钮文案随折叠状态切换（▾=可收起 / ▴=可展开），文字 + 符号便于识别。</summary>
    private void UpdateToggle() =>
        _panelToggle.Content = _panelCollapsed
            ? "▴ " + L.T("Expand editor")
            : "▾ " + L.T("Bottom editor");

    private void ShowStructure()
    {
        if (Doc.Cnv != null)
        {
            ShowOnly(_cnvTable);
            _cnvTable.Bind(Doc, OnEdited);
        }
        else if (Doc.Qst != null)
        {
            ShowOnly(_qstTable);
            _qstTable.Bind(Doc, OnEdited);
        }
        else
        {
            ShowOnly(_emptyHint);
        }
        bool structure = _cnvTable.IsVisible || _qstTable.IsVisible;
        _panel.IsVisible = structure;
        _panelSplitter.IsVisible = structure;
        _panelToggle.IsVisible = structure;
        if (structure) OnSelectedChanged();
    }

    private void ShowSingle(Control target)
    {
        ShowOnly(target);
        _panel.IsVisible = false;
        _panelSplitter.IsVisible = false;
        _panelToggle.IsVisible = false;
    }

    private void ShowOnly(Control target)
    {
        foreach (var c in new Control[] { _cnvTable, _qstTable, _locGrid, _emptyHint, _jsonHost, _dumpBox })
            c.IsVisible = c == target;
    }

    /// <summary>在 结构表 / 本地化字符串表 之间切换（右键菜单与 Ctrl+L 共用）。</summary>
    public void ToggleLocalization() =>
        SetView(Mode == DocViewMode.Localization ? DocViewMode.Visual : DocViewMode.Localization);

    private void LoadJson(bool force)
    {
        if (_jsonDirty && !force) return;
        _jsonBuilding = true;
        try
        {
            _jsonBox.Text = Doc.Cnv != null
                ? JsonIO.Export(Doc.Cnv, Doc.Title)
                : Doc.Qst != null ? JsonIO.Export(Doc.Qst, Doc.Title) : "";
            _jsonDirty = false;
            _jsonStatus.Text = L.T("Click \"Apply JSON\" to write changes back to the document.");
        }
        finally { _jsonBuilding = false; }
    }

    private void ApplyJson()
    {
        if (Doc.ReadOnly)
        {
            _jsonStatus.Text = L.T("The current document is read-only; JSON cannot be applied.");
            return;
        }
        try
        {
            object model = JsonIO.ImportModel(_jsonBox.Text);
            if (model is CnvFile c && Doc.Cnv != null) Doc.Cnv = c;
            else if (model is QstFile q && Doc.Qst != null) Doc.Qst = q;
            else throw new GdParseException(L.T("The JSON kind does not match the current document type."));
            Doc.Dirty = true;
            _jsonDirty = false;
            DirtyChanged?.Invoke();
            LoadJson(force: true);
            _jsonStatus.Text = L.T("JSON changes applied.");
        }
        catch (Exception ex)
        {
            Gui.Warn(TopLevel.GetTopLevel(this) as Window, ex.Message, L.Error);
        }
    }

    private void LoadDump()
    {
        _dumpBox.Text = Doc.Cnv != null
            ? Dump.DumpCnv(Doc.Cnv, Doc.Title)
            : Doc.Qst != null ? Dump.DumpQst(Doc.Qst, Doc.Title) : "";
    }

    private void OnEdited()
    {
        Doc.Dirty = true;
        DirtyChanged?.Invoke();
    }

    public void RefreshAll()
    {
        switch (Mode)
        {
            case DocViewMode.Visual:
                ShowStructure();
                break;
            case DocViewMode.Json:
                LoadJson(force: false);
                break;
            case DocViewMode.Localization:
                _locGrid.Bind(Doc, OnEdited);
                break;
            case DocViewMode.Dump:
                LoadDump();
                break;
        }
    }

    public void AfterSaved()
    {
        Doc.Dirty = false;
        DirtyChanged?.Invoke();
    }
}
