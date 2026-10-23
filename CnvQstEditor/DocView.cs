using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using CnvQst;

namespace CnvQstEditor;

/// <summary>文档视图模式：只有可视化编辑器可修改；其余四种为只读。</summary>
internal enum DocViewMode
{
    Visual,
    Json,
    Localization,
    Slot,
    Dump,
}

/// <summary>
/// 单个文档视图：可视化表格（唯一可编辑入口）+ JSON / 本地化 / 槽位 / DUMP 四种只读视图。
/// 只读视图占满整个编辑区；JSON、DUMP 用行号，本地化与槽位用“行号 + 槽位名”标注槽位。
/// </summary>
internal sealed class DocView : UserControl
{
    public DocSession Doc { get; }
    public DocViewMode Mode { get; private set; } = DocViewMode.Visual;
    public event Action? DirtyChanged;
    public event Action? ModeChanged;
    public event Action? LocalizationRequested;

    private readonly CnvTableEditor _cnvTable = new() { IsVisible = false };
    private readonly QstTableEditor _qstTable = new() { IsVisible = false };
    private readonly TextBlock _emptyHint = new()
    {
        Text = L.EmptyHint,
        Foreground = UiTheme.MutedBrush,
        Margin = new Thickness(28),
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Top,
    };

    private readonly TextEditor _jsonEditor = NewReadOnlyEditor(lineNumbers: true);
    private readonly TextEditor _locEditor = NewReadOnlyEditor(lineNumbers: false);
    private readonly TextEditor _slotEditor = NewReadOnlyEditor(lineNumbers: false);
    private readonly TextEditor _dumpEditor = NewReadOnlyEditor(lineNumbers: true);
    private readonly SlotMargin _locMargin = new();
    private readonly SlotMargin _slotMargin = new();
    private readonly TextBlock _slotInfo = new()
    {
        Foreground = UiTheme.MutedBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(10, 6, 10, 4),
    };
    private readonly Grid _slotHost = new() { IsVisible = false };

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

    private bool _panelCollapsed;
    private GridLength _panelHeight = new(180);

    public DocView(DocSession doc)
    {
        Doc = doc;

        _locEditor.TextArea.LeftMargins.Insert(0, _locMargin);
        _slotEditor.TextArea.LeftMargins.Insert(0, _slotMargin);

        _slotHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _slotHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_slotInfo, 0);
        Grid.SetRow(_slotEditor, 1);
        _slotHost.Children.Add(_slotInfo);
        _slotHost.Children.Add(_slotEditor);

        foreach (var c in new Control[] { _cnvTable, _qstTable, _jsonEditor, _locEditor, _slotHost, _dumpEditor, _emptyHint })
            _host.Children.Add(c);

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

        _cnvTable.LocRequested += () => LocalizationRequested?.Invoke();
        _qstTable.LocRequested += () => LocalizationRequested?.Invoke();
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

    private static TextEditor NewReadOnlyEditor(bool lineNumbers)
    {
        var editor = new TextEditor
        {
            IsReadOnly = true,
            ShowLineNumbers = lineNumbers,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            WordWrap = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.White,
            Padding = new Thickness(4, 2, 4, 2),
            LineNumbersForeground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
            LineNumbersMargin = new Thickness(4, 0, 6, 0),
        };
        editor.TextArea.Foreground = UiTheme.TextBrush;
        editor.TextArea.Background = Brushes.White;
        return editor;
    }

    /// <summary>切换整体视图（可视化 / JSON / 本地化 / 槽位 / DUMP）。</summary>
    public void SetView(DocViewMode mode)
    {
        Mode = mode;
        switch (mode)
        {
            case DocViewMode.Visual:
                ShowStructure();
                break;
            case DocViewMode.Json:
                ShowSingle(_jsonEditor);
                LoadJson();
                break;
            case DocViewMode.Localization:
                ShowSingle(_locEditor);
                LoadLocalization();
                break;
            case DocViewMode.Slot:
                ShowSingle(_slotHost);
                LoadSlots();
                break;
            case DocViewMode.Dump:
                ShowSingle(_dumpEditor);
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
        var splitRow = _rootGrid.RowDefinitions[1];
        var panelRow = _rootGrid.RowDefinitions[2];
        if (structure)
        {
            splitRow.Height = GridLength.Auto;
            panelRow.Height = _panelCollapsed ? new GridLength(0) : _panelHeight;
        }
        else
        {
            splitRow.Height = new GridLength(0);
            panelRow.Height = new GridLength(0);
        }
        if (structure) OnSelectedChanged();
    }

    /// <summary>只读视图占满整个编辑区：同时收起底部编辑器所在的两行。</summary>
    private void ShowSingle(Control target)
    {
        ShowOnly(target);
        _panel.IsVisible = false;
        _panelSplitter.IsVisible = false;
        _panelToggle.IsVisible = false;
        _rootGrid.RowDefinitions[1].Height = new GridLength(0);
        _rootGrid.RowDefinitions[2].Height = new GridLength(0);
    }

    private void ShowOnly(Control target)
    {
        foreach (var c in new Control[] { _cnvTable, _qstTable, _jsonEditor, _locEditor, _slotHost, _dumpEditor, _emptyHint })
            c.IsVisible = c == target;
    }

    private void LoadJson()
    {
        try
        {
            _jsonEditor.Text = Doc.Cnv != null
                ? JsonIO.Export(Doc.Cnv, Doc.Title)
                : Doc.Qst != null ? JsonIO.Export(Doc.Qst, Doc.Title) : "";
        }
        catch (Exception ex)
        {
            _jsonEditor.Text = ex.Message;
        }
    }

    private void LoadLocalization()
    {
        try
        {
            var texts = Doc.PrimaryLanguage.Texts;
            var labels = LocalizedRoles(texts.Count);
            _locMargin.SetLabels(labels);
            _locEditor.Text = string.Join("\n", texts.Select(t => OneLine(t.Text)));
        }
        catch (Exception ex)
        {
            _locMargin.SetLabels([]);
            _locEditor.Text = ex.Message;
        }
    }

    private List<string> LocalizedRoles(int count)
    {
        var labels = new List<string>(count);
        if (Doc.Cnv != null)
        {
            var slots = SlotGuide.ForCnv(Doc.Cnv);
            for (int i = 0; i < count; i++)
                labels.Add(i < slots.Count ? SlotText.Role(slots[i]) : L.T("(no bound node)"));
        }
        else if (Doc.Qst != null)
        {
            var slots = SlotGuide.ForQst(Doc.Qst);
            for (int i = 0; i < count; i++)
                labels.Add(i < slots.Count ? SlotText.Role(slots[i]) : L.T("(no matching slot)"));
        }
        return labels;
    }

    private void LoadSlots()
    {
        try
        {
            var slots = Doc.Cnv != null ? SlotGuide.ForCnv(Doc.Cnv)
                : Doc.Qst != null ? SlotGuide.ForQst(Doc.Qst)
                : [];
            _slotMargin.SetLabels(slots.Select(SlotText.Role).ToList());
            _slotEditor.Text = string.Join("\n", slots.Select(SlotText.Placeholder));
            _slotInfo.Text = L.F("Total {0} slots · {1} suggested · newline: {{^n}}", slots.Count, slots.Count(s => s.Suggested));
        }
        catch (Exception ex)
        {
            _slotMargin.SetLabels([]);
            _slotEditor.Text = ex.Message;
            _slotInfo.Text = "";
        }
    }

    private void LoadDump()
    {
        try
        {
            _dumpEditor.Text = Doc.Cnv != null
                ? Dump.DumpCnv(Doc.Cnv, Doc.Title)
                : Doc.Qst != null ? Dump.DumpQst(Doc.Qst, Doc.Title) : "";
        }
        catch (Exception ex)
        {
            _dumpEditor.Text = ex.Message;
        }
    }

    private static string OneLine(string text) =>
        text.Replace("\r\n", "{^n}").Replace("\n", "{^n}").Replace("\r", "{^n}");

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
                LoadJson();
                break;
            case DocViewMode.Localization:
                LoadLocalization();
                break;
            case DocViewMode.Slot:
                LoadSlots();
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
