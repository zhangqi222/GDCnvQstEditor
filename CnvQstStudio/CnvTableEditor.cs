using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CnvQst;

namespace CnvQstStudio;

/// <summary>CNV 树型表格（Semi TreeDataGrid）：节点(树) | 属性 | 本地化文本 | 条件 | 动作。</summary>
internal sealed class CnvTableEditor : UserControl
{
    private DocSession _doc = null!;
    private CnvFile _cnv = null!;
    private Changed _changed = null!;
    private Action? _locRequested;
    private List<CnvRow> _rows = new();
    private TreeDataGrid _tree = null!;
    private HierarchicalTreeDataGridSource<CnvRow> _source = null!;
    private (CnvRow Row, int Col)? _lastCell;   // 右键命中的单元格（菜单/复制用）

    public event Action? LocRequested { add => _locRequested += value; remove => _locRequested -= value; }
    public event Action? SelectedChanged;

    public DocSession Doc => _doc;
    public int? SelectedTextSlot => SelectedStep?.Index;

    private static readonly Dictionary<string, int> ColFromTag = new() { ["0"] = 0, ["1"] = 1, ["2"] = 2, ["3"] = 3, ["4"] = 4 };

    public CnvTableEditor()
    {
        _tree = new TreeDataGrid { CanUserResizeColumns = true };
        // 右键：选中行在此处理；菜单在单元格模板的 ContextRequested 里按行+列动态生成
        _tree.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _tree.ContextMenu = BuildContextMenu(null, -1);
        Content = _tree;
    }

    private CnvStep? SelectedStep => _source?.RowSelection.SelectedItem is CnvRow r ? r.Step : null;

    // ------------------------------------------------------------------
    // 模板：单元格单行高度（不自动换行增高），网格线 = 右竖线 + 底横线
    // ------------------------------------------------------------------

    private FuncDataTemplate<CnvRow> CellBorder(Action<Border> build, Action<string>? onDoubleTap = null, string? tapTag = null) => new((_, _) =>
    {
        var border = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(2, 3, 8, 3),
            BorderBrush = UiTheme.GridLineBrush,
            BorderThickness = new Thickness(0, 0, 1, 1),
        };
        border.Bind(Border.BackgroundProperty, new Binding(nameof(CnvRow.Highlight)) { Converter = TreeGridShared.HighlightToBrush });
        build(border);
        if (tapTag != null && onDoubleTap != null)
        {
            string tag = tapTag;
            border.DoubleTapped += (_, e) => { e.Handled = true; onDoubleTap(tag); };
        }
        if (tapTag != null)
        {
            // 右键以单元格为基准：ContextRequested 在单元格模板内捕获列 tag（与双击同一机制）
            string tag = tapTag;
            border.ContextRequested += (_, e) =>
            {
                if (border.DataContext is CnvRow r) OnCellRightPressed(tag, r);
            };
        }
        return border;
    });

    private static TextBlock OneLine() => new()
    {
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static FuncDataTemplate<ColoredLine> LineTemplate() => new((line, _) =>
    {
        var tb = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        tb.Bind(TextBlock.TextProperty, new Binding(nameof(ColoredLine.Text)));
        tb.Bind(TextBlock.ForegroundProperty, new Binding(nameof(ColoredLine.Brush)));
        return tb;
    });

    private HierarchicalTreeDataGridSource<CnvRow> BuildSource(List<CnvRow> roots)
    {
        // 列宽：节点 30% / 属性 10% / 本地化·条件·动作各 20%（Star 权重），表格填满父容器宽度。
        var s = new HierarchicalTreeDataGridSource<CnvRow>(roots)
        {
            Columns =
            {
                new HierarchicalExpanderColumn<CnvRow>(
                    new TemplateColumn<CnvRow>(L.T("节点", "Node"),
                        CellBorder(b =>
                        {
                            var row = new StackPanel { Orientation = Orientation.Horizontal };
                            var icon = new PathIcon { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
                            icon.Bind(Visual.IsVisibleProperty, new Binding(nameof(CnvRow.IconKey)) { Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty });
                            icon.Bind(PathIcon.DataProperty, new Binding(nameof(CnvRow.IconKey)) { Converter = TreeGridShared.IconToGeometry });
                            row.Children.Add(icon);
                            var node = OneLine();
                            node.Foreground = UiTheme.TextBrush;
                            node.Bind(TextBlock.TextProperty, new Binding(nameof(CnvRow.Node)));
                            row.Children.Add(node);
                            b.Child = row;
                        }, OnCellDoubleTap, "0"),
                        width: new GridLength(3, GridUnitType.Star)),
                    r => r.Children, r => r.HasChildren, r => r.IsExpanded),
                new TemplateColumn<CnvRow>(L.T("属性", "Attrs"),
                    CellBorder(b =>
                    {
                        var tb = OneLine();
                        tb.Bind(TextBlock.TextProperty, new Binding(nameof(CnvRow.Attrs)));
                        b.Child = tb;
                    }, OnCellDoubleTap, "1"),
                    width: new GridLength(1, GridUnitType.Star)),
                new TemplateColumn<CnvRow>(L.T("本地化文本", "Localized Text"),
                    // 本地化列暂不显示内容（待接入独立的本地化语言块数据源）
                    CellBorder(b => { }, OnCellDoubleTap, "2"),
                    width: new GridLength(1, GridUnitType.Star)),
                new TemplateColumn<CnvRow>(L.T("条件", "Conditions"),
                    CellBorder(b =>
                    {
                        var items = new ItemsControl { MaxHeight = 18, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top };
                        items.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(CnvRow.Cond)));
                        items.ItemTemplate = LineTemplate();
                        b.Child = items;
                    }, OnCellDoubleTap, "3"),
                    width: new GridLength(1, GridUnitType.Star)),
                new TemplateColumn<CnvRow>(L.T("动作", "Actions"),
                    CellBorder(b =>
                    {
                        var items = new ItemsControl { MaxHeight = 18, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top };
                        items.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(CnvRow.Act)));
                        items.ItemTemplate = LineTemplate();
                        b.Child = items;
                    }, OnCellDoubleTap, "4"),
                    width: new GridLength(1, GridUnitType.Star)),
            },
        };
        s.RowSelection.SingleSelect = true;
        s.RowSelection.SelectionChanged += (_, _) => { UpdateHighlight(); SelectedChanged?.Invoke(); };
        return s;
    }

    // ------------------------------------------------------------------
    // 选择 / 高亮
    // ------------------------------------------------------------------

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_tree).Properties.IsRightButtonPressed) return;
        var visual = e.Source as Visual;
        foreach (var anc in visual.GetSelfAndVisualAncestors())
        {
            if (anc is Control c && _tree.TryGetRowModel(c, out CnvRow? row) && row != null)
            {
                SelectRow(row, bringIntoView: false);
                return;
            }
        }
        // 空白区域（不在任何行上）：清空菜单
        _tree.ContextMenu = BuildContextMenu(null, -1);
    }

    /// <summary>右键命中单元格（模板 ContextRequested，按列 tag 定位）→ 生成精确到单元格的菜单。</summary>
    private void OnCellRightPressed(string tag, CnvRow row)
    {
        int col = ColFromTag.GetValueOrDefault(tag, -1);
        _lastCell = (row, col);
        _tree.ContextMenu = BuildContextMenu(row, col);
    }

    private void SelectRow(CnvRow target, bool bringIntoView)
    {
        if (_source == null) return;
        int flat = 0;
        foreach (var r in _source.Rows)
        {
            if (r is HierarchicalRow<CnvRow> hr && ReferenceEquals(hr.Model, target))
            {
                _source.RowSelection.Select(hr.ModelIndexPath);
                if (bringIntoView)
                {
                    int idx = flat;
                    Dispatcher.UIThread.Post(() =>
                    {
                        var row = _tree.TryGetRow(idx);
                        row?.BringIntoView();
                    }, DispatcherPriority.Loaded);
                }
                return;
            }
            flat++;
        }
    }

    private void UpdateHighlight()
    {
        foreach (var r in _rows) r.Highlight = false;
        if (_source?.RowSelection.SelectedItem is CnvRow sel)
            WalkHi(sel);
        return;

        void WalkHi(CnvRow r)
        {
            r.Highlight = true;
            foreach (var c in r.Children) WalkHi(c);
        }
    }

    // ------------------------------------------------------------------
    // 右键菜单（按行/列动态生成）
    // ------------------------------------------------------------------

    private ContextMenu BuildContextMenu(CnvRow? row, int col)
    {
        var cm = new ContextMenu();
        if (row == null) return cm;   // 空白区域：无可用菜单

        bool isRoot = ReferenceEquals(row.Step, _cnv.Root);
        cm.Items.Add(TreeGridShared.Mi(L.T("＋添加子节点", "+ Add Child"), () => _ = AddChild()));
        if (!isRoot)
        {
            cm.Items.Add(TreeGridShared.Mi(L.T("－删除子树", "- Delete"), DeleteStep));
            cm.Items.Add(new Separator());
            cm.Items.Add(TreeGridShared.Mi("↑ " + L.T("上移", "Up"), () => MoveStep(-1)));
            cm.Items.Add(TreeGridShared.Mi("↓ " + L.T("下移", "Down"), () => MoveStep(1)));
        }
        cm.Items.Add(new Separator());
        cm.Items.Add(TreeGridShared.Mi(L.T("节点属性", "Node Properties"), () => _ = EditNodeProps()));

        // 列特定菜单项
        if (col == 3 || col == 4)
        {
            cm.Items.Add(new Separator());
            cm.Items.Add(TreeGridShared.Mi(col == 3 ? L.T("编辑条件", "Edit Conditions") : L.T("编辑动作", "Edit Actions"),
                () => _ = EditScript(row, col == 3)));
        }

        // 全局：有内容即可复制
        string? cell = CellText(row, col);
        if (cell != null)
        {
            cm.Items.Add(new Separator());
            cm.Items.Add(TreeGridShared.Mi(L.T("复制单元格", "Copy Cell"), () => _ = CopyCell()));
        }
        return cm;
    }

    private static string? CellText(CnvRow r, int col) => col switch
    {
        0 => r.Node,
        1 => r.Attrs,
        2 => null,          // 本地化列暂不显示内容
        3 => r.Cond.Count > 0 ? string.Join("\n", r.Cond.Select(l => l.Text)) : null,
        4 => r.Act.Count > 0 ? string.Join("\n", r.Act.Select(l => l.Text)) : null,
        _ => null,
    };

    private async Task CopyCell()
    {
        if (_lastCell == null || CellText(_lastCell.Value.Row, _lastCell.Value.Col) is not string text) return;
        var tl = TopLevel.GetTopLevel(this);
        if (tl?.Clipboard != null) { try { await tl.Clipboard.SetTextAsync(text); } catch { } }
    }

    /// <summary>编辑指定行的条件/动作（右键菜单与双击共用）。</summary>
    private async Task EditScript(CnvRow row, bool isCondition)
    {
        var list = isCondition ? row.Step.Conditions : row.Step.Actions;
        if (await ScriptListDialog.Show(_doc, list, isCondition, TreeGridShared.OwnerOf(this)))
        {
            row.Reload();
            _changed();
        }
    }

    private async void OnCellDoubleTap(string tag)
    {
        if (_source?.RowSelection.SelectedItem is not CnvRow row) return;
        if (tag == "3") await EditScript(row, isCondition: true);
        else if (tag == "4") await EditScript(row, isCondition: false);
        else if (tag is "0" or "1" or "2") await EditNodeProps();
    }

    private async Task EditNodeProps()
    {
        var s = SelectedStep;
        if (s == null) return;
        if (await CnvNodeDialog.Show(_doc, s, TreeGridShared.OwnerOf(this)))
        {
            RefreshSelected();
            _changed();
        }
    }

    private void RefreshSelected()
    {
        if (_source?.RowSelection.SelectedItem is CnvRow r) r.Reload();
    }

    public void NotifyEdited()
    {
        _doc.Dirty = true;
        _changed();
    }

    public void Bind(DocSession doc, Changed changed)
    {
        _doc = doc;
        _cnv = doc.Cnv!;
        _changed = changed;

        var byStep = new Dictionary<CnvStep, CnvRow>();
        _rows = _cnv.Steps.Select(s => new CnvRow(this, doc, s)).ToList();
        foreach (var row in _rows) byStep[row.Step] = row;
        foreach (var row in _rows)
        {
            foreach (var childStep in row.Step.Children)
                if (byStep.TryGetValue(childStep, out var child)) child.Attach(row);
        }

        var roots = _rows.Where(r => r.Parent == null).ToList();
        _source = BuildSource(roots);
        _tree.Source = _source;
        _source.ExpandAll();
        // 默认不选定任何节点；子树高亮仅在真正选中后出现。
        SelectedChanged?.Invoke();
    }

    public void SelectStep(CnvStep s)
    {
        var row = _rows.FirstOrDefault(r => ReferenceEquals(r.Step, s));
        if (row != null) SelectRow(row, bringIntoView: true);
    }

    private async Task AddChild()
    {
        var anchor = SelectedStep;
        if (anchor == null) return;
        var owner = TreeGridShared.OwnerOf(this);
        var dlg = new CnvTypePicker();
        if (await dlg.ShowDialog<bool>(owner!) != true) return;
        var step = _cnv.InsertChild(anchor, dlg.Type);
        _changed();
        Bind(_doc, _changed);
        SelectStep(step);   // 自动选定并滚动到新行
    }

    private void DeleteStep()
    {
        var s = SelectedStep;
        if (s == null) return;
        if (s == _cnv.Root)
        {
            Gui.Warn(TreeGridShared.OwnerOf(this), L.T("不能删除根节点。", "The root node cannot be deleted."), L.T("删除", "Delete"));
            return;
        }
        if (!_cnv.RemoveSubtree(s)) return;
        _changed();
        Bind(_doc, _changed);
    }

    private void MoveStep(int delta)
    {
        var s = SelectedStep;
        if (s == null) return;
        _cnv.MoveSibling(s, delta);
        _changed();
        Bind(_doc, _changed);
    }
}