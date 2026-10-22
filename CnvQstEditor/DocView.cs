using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CnvQst;

namespace CnvQstEditor;

/// <summary>
/// 单个文档视图：100% 宽度的树形表格（结构与数据结合展示）+ 页内状态行。
/// 结构表（CNV/QST）与本地化字符串表通过 <see cref="ToggleView"/> 切换。
/// </summary>
internal sealed class DocView : UserControl
{
    public DocSession Doc { get; }
    public event Action? DirtyChanged;

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
    private bool _panelCollapsed;
    private GridLength _panelHeight = new(180);

    public DocView(DocSession doc)
    {
        Doc = doc;

        foreach (var c in new Control[] { _cnvTable, _qstTable, _locGrid, _emptyHint }) _host.Children.Add(c);

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
        ToolTip.SetTip(_panelSplitter, L.T("拖动调整高度；点击折叠 / 展开底部编辑器。", "Drag to resize; click to collapse / expand the editor."));
        ToolTip.SetTip(_panelToggle, L.T("点击折叠 / 展开底部编辑器。", "Click to collapse / expand the editor."));
        _panelSplitter.Tapped += (_, _) => TogglePanel();
        _panelToggle.Click += (_, _) => TogglePanel();
        UpdateToggle();
        Content = _rootGrid;

        _cnvTable.LocRequested += ToggleView;
        _qstTable.LocRequested += ToggleView;
        _cnvTable.SelectedChanged += OnSelectedChanged;
        _qstTable.SelectedChanged += OnSelectedChanged;
        _panel.TextEdited += () => { Doc.Dirty = true; DirtyChanged?.Invoke(); };

        ShowCurrent();
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
            ? "▴ " + L.T("展开编辑器", "Expand Editor")
            : "▾ " + L.T("底部编辑器", "Editor");

    private void ShowCurrent()
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
    }

    private void ShowOnly(Control target)
    {
        foreach (var c in new Control[] { _cnvTable, _qstTable, _locGrid, _emptyHint })
            c.IsVisible = c == target;
        bool structure = target == _cnvTable || target == _qstTable;
        _panel.IsVisible = structure;
        _panelSplitter.IsVisible = structure;
        _panelToggle.IsVisible = structure;
        if (structure) OnSelectedChanged();
    }

    /// <summary>在 结构表 / 本地化字符串表 之间切换（右键菜单与 Ctrl+L 共用）。</summary>
    public void ToggleView()
    {
        if (_locGrid.IsVisible)
        {
            ShowCurrent();
        }
        else
        {
            ShowOnly(_locGrid);
            _locGrid.Bind(Doc, OnEdited);
        }
    }

    private void OnEdited()
    {
        Doc.Dirty = true;
        DirtyChanged?.Invoke();
    }

    public void RefreshAll()
    {
        if (_cnvTable.IsVisible) _cnvTable.Bind(Doc, OnEdited);
        else if (_qstTable.IsVisible) _qstTable.Bind(Doc, OnEdited);
        else if (_locGrid.IsVisible) _locGrid.Bind(Doc, OnEdited);
        OnSelectedChanged();
    }

    public void AfterSaved()
    {
        Doc.Dirty = false;
        DirtyChanged?.Invoke();
    }
}