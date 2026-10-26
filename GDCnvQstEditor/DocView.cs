using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using GDCnvQstLib;

namespace GDCnvQstEditor;

/// <summary>文档视图模式：只有可视化编辑器可修改；其余三种为只读。</summary>
internal enum DocViewMode
{
    Visual,
    Json,
    Localization,
    Dump,
}

/// <summary>
/// 单个文档视图：可视化表格（唯一可编辑入口）+ JSON / 本地化 / DUMP 三种只读视图。
/// 只读视图占满整个编辑区；本地化视图 = 合并视图（行号 + 节点名 + 描述 + 建议标注在左侧，
/// 内容为本地化文本），JSON 与 DUMP 用纯行号。
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

    private readonly TextEditor _jsonEditor = UiEditors.ReadOnly();
    private FoldingManager? _jsonFolding;   // JSON 折叠（首次进入 JSON 视图时安装）
    private readonly RowMargin _locRowMargin = new();
    private readonly TextEditor _locEditor;
    private readonly TextEditor _dumpEditor = UiEditors.ReadOnly();
    private Grid _jsonHost = null!;         // 只读 tag + JSON 编辑器（构造函数组装，字段初始化器不能互相引用）
    private Grid _dumpHost = null!;
    private readonly TextBlock _locInfo = new()
    {
        Foreground = UiTheme.MutedBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(10, 6, 10, 4),
    };
    private readonly Grid _locHost = new() { IsVisible = false };

    private readonly Grid _host = new();
    private readonly NodeTextPanel _panel = new() { IsVisible = false, ClipToBounds = true };
    private readonly GridSplitter _panelSplitter = new()
    {
        Height = 6,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Background = UiTheme.GridLineBrush,
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
        BorderBrush = UiTheme.GridLineBrush,
        Background = UiTheme.EditorBrush,
        Foreground = UiTheme.MutedBrush,
    };

    private bool _panelCollapsed;
    private GridLength _panelHeight = new(180);

    public DocView(DocSession doc)
    {
        Doc = doc;
        _locEditor = UiEditors.ReadOnly(_locRowMargin);
        // JSON 语法着色：AvaloniaEdit 内置 JSON 高亮定义（Json.xshd），配色随主题由 UiTheme.Refresh 同步
        _jsonEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(".json");

        // JSON / DUMP：顶部居中"只读"TagLabel + 编辑器；本地化：tag 在统计提示行上方
        _jsonHost = MakeReadOnlyHost(_jsonEditor);
        _dumpHost = MakeReadOnlyHost(_dumpEditor);

        var locTag = MakeTag();
        _locHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _locHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _locHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(locTag, 0);
        Grid.SetRow(_locInfo, 1);
        Grid.SetRow(_locEditor, 2);
        _locHost.Children.Add(locTag);
        _locHost.Children.Add(_locInfo);
        _locHost.Children.Add(_locEditor);

        foreach (var c in new Control[] { _cnvTable, _qstTable, _jsonHost, _locHost, _dumpHost, _emptyHint })
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
                LoadJson();
                break;
            case DocViewMode.Localization:
                ShowSingle(_locHost);
                LoadLocalization();
                break;
            case DocViewMode.Dump:
                ShowSingle(_dumpHost);
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
        foreach (var c in new Control[] { _cnvTable, _qstTable, _jsonHost, _locHost, _dumpHost, _emptyHint })
            c.IsVisible = c == target;
    }

    /// <summary>Semi TagLabel（Grey 浅色 tag，明暗主题自适应）：只读视图顶部居中提示。</summary>
    private static Label MakeTag() => new()
    {
        Theme = UiTheme.FindTheme("TagLabel"),
        Content = L.T("Read-only view; edit in the Visual view."),
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 8, 0, 2),
    };

    /// <summary>只读视图宿主：顶部居中只读 tag + 编辑器占满剩余高度。</summary>
    private static Grid MakeReadOnlyHost(TextEditor editor)
    {
        var host = new Grid();
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var tag = MakeTag();
        Grid.SetRow(tag, 0);
        Grid.SetRow(editor, 1);
        host.Children.Add(tag);
        host.Children.Add(editor);
        return host;
    }

    /// <summary>JSON 视图：语法着色（key/string/number/keyword/注释）+ 对象/数组折叠。</summary>
    private void LoadJson()
    {
        try
        {
            ApplyJson(Doc.Cnv != null
                ? JsonIO.Export(Doc.Cnv, Doc.Title)
                : Doc.Qst != null ? JsonIO.Export(Doc.Qst, Doc.Title) : "");
        }
        catch (Exception ex)
        {
            ApplyJson(ex.Message);
        }
    }

    private void ApplyJson(string text)
    {
        _jsonEditor.Text = text;
        _jsonFolding ??= FoldingManager.Install(_jsonEditor.TextArea);
        _jsonFolding.UpdateFoldings(JsonScanner.Analyze(text), -1);
    }

    /// <summary>
    /// 本地化合并视图：左侧行号列 = 行号 + 节点名（按类型着色）+ 节点描述 + 槽位建议（均为固定内容），
    /// 正文 = 本地化文本（换行压成 {^n} 保持单行对齐）。顶部保留统计与“未对齐请回可视化编辑器”提示。
    /// </summary>
    private void LoadLocalization()
    {
        try
        {
            var texts = Doc.PrimaryLanguage.Texts;
            var slots = Doc.Cnv != null ? SlotGuide.ForCnv(Doc.Cnv)
                : Doc.Qst != null ? SlotGuide.ForQst(Doc.Qst)
                : [];
            var rows = new List<RowLabel>(texts.Count);
            for (int i = 0; i < texts.Count; i++)
            {
                if (i < slots.Count)
                {
                    var s = slots[i];
                    rows.Add(new RowLabel(i + 1, SlotText.Role(s), SlotText.Brush(s), SlotText.Desc(s), SlotText.Advice(s)));
                }
                else
                {
                    rows.Add(new RowLabel(i + 1, L.T("(no matching slot)")));
                }
            }
            _locRowMargin.SetRows(rows);
            _locEditor.Text = string.Join("\n", texts.Select(t => OneLine(t.Text)));
            _locInfo.Text = L.F(
                "Total {0} slots · {1} suggested · {2} must stay empty · newline: {{^n}} · If rows look misaligned, edit in the visual editor.",
                slots.Count, slots.Count(s => s.Suggested), slots.Count(s => s.MustBeEmpty));
        }
        catch (Exception ex)
        {
            _locRowMargin.SetRows(null);
            _locEditor.Text = ex.Message;
            _locInfo.Text = "";
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
