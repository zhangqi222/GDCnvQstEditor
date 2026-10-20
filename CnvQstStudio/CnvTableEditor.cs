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
    private bool _comfortable;
    private List<CnvRow> _rows = new();
    private TreeDataGrid _tree = null!;
    private HierarchicalTreeDataGridSource<CnvRow> _source = null!;
    private (CnvRow Row, int Col)? _lastCell;   // 右键命中的单元格（复制用）

    public event Action? LocRequested { add => _locRequested += value; remove => _locRequested -= value; }
    public event Action? SelectedChanged;

    public DocSession Doc => _doc;
    public int? SelectedTextSlot => SelectedStep?.Index;

    public CnvTableEditor()
    {
        _tree = new TreeDataGrid { CanUserResizeColumns = true };
        // 右键点击 → 先选中该行并记录命中单元格（右键菜单语义与左键一致）
        _tree.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _tree.ContextMenu = BuildContextMenu();
        Content = _tree;
    }

    private CnvStep? SelectedStep => _source?.RowSelection.SelectedItem is CnvRow r ? r.Step : null;

    // ------------------------------------------------------------------
    // 模板（单元格：网格线 = 右竖线 + 底横线；高亮底色随 Highlight）
    // ------------------------------------------------------------------

    private static FuncDataTemplate<CnvRow> CellBorder(Action<Border> build, Action<string>? onDoubleTap = null, string? tapTag = null) => new((_, _) =>
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
        return border;
    });

    private static FuncDataTemplate<ColoredLine> LineTemplate() => new((line, _) =>
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap };
        tb.Bind(TextBlock.TextProperty, new Binding(nameof(ColoredLine.Text)));
        tb.Bind(TextBlock.ForegroundProperty, new Binding(nameof(ColoredLine.Brush)));
        return tb;
    });

    private HierarchicalTreeDataGridSource<CnvRow> BuildSource(List<CnvRow> roots)
    {
        // 列宽用 Star 权重（保持 300:140:280:230:230 比例），表格填满父容器宽度。
        var s = new HierarchicalTreeDataGridSource<CnvRow>(roots)
        {
            Columns =
            {
                new HierarchicalExpanderColumn<CnvRow>(
                    new TemplateColumn<CnvRow>(L.T("节点", "Node"),
                        CellBorder(b =>
                        {
                            var node = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.TextBrush };
                            node.Bind(TextBlock.TextProperty, new Binding(nameof(CnvRow.Node)));
                            b.Child = node;
                        }, OnCellDoubleTap, "0"),
                        width: new GridLength(TreeGridShared.ColNode, GridUnitType.Star)),
                    r => r.Children, r => r.HasChildren, r => r.IsExpanded),
                new TemplateColumn<CnvRow>(L.T("属性", "Attrs"),
                    CellBorder(b =>
                    {
                        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap };
                        tb.Bind(TextBlock.TextProperty, new Binding(nameof(CnvRow.Attrs)));
                        b.Child = tb;
                    }, OnCellDoubleTap, "1"),
                    width: new GridLength(TreeGridShared.ColAttrs, GridUnitType.Star)),
                new TemplateColumn<CnvRow>(L.T("本地化文本", "Localized Text"),
                    CellBorder(b =>
                    {
                        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap };
                        tb.Bind(TextBlock.TextProperty, new Binding(nameof(CnvRow.Text)));
                        if (!_comfortable) tb.MaxHeight = 54;
                        b.Child = tb;
                    }, OnCellDoubleTap, "2"),
                    width: new GridLength(TreeGridShared.ColText, GridUnitType.Star)),
                new TemplateColumn<CnvRow>(L.T("条件", "Conditions"),
                    CellBorder(b =>
                    {
                        var items = new ItemsControl();
                        items.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(CnvRow.Cond)));
                        items.ItemTemplate = LineTemplate();
                        b.Child = items;
                    }, OnCellDoubleTap, "3"),
                    width: new GridLength(TreeGridShared.ColCond, GridUnitType.Star)),
                new TemplateColumn<CnvRow>(L.T("动作", "Actions"),
                    CellBorder(b =>
                    {
                        var items = new ItemsControl();
                        items.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(CnvRow.Act)));
                        items.ItemTemplate = LineTemplate();
                        b.Child = items;
                    }, OnCellDoubleTap, "4"),
                    width: new GridLength(TreeGridShared.ColAct, GridUnitType.Star)),
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
        (CnvRow? Row, int Col) hit = (null, -1);
        foreach (var anc in visual.GetSelfAndVisualAncestors())
        {
            if (hit.Col < 0 && anc is TreeDataGridCell cell) hit.Col = cell.ColumnIndex;
            if (hit.Row == null && anc is Control c && _tree.TryGetRowModel(c, out CnvRow? row) && row != null)
                hit.Row = row;
        }
        if (hit.Row != null)
        {
            _lastCell = (hit.Row, hit.Col);
            SelectRow(hit.Row, bringIntoView: false);
        }
        else
        {
            _lastCell = null;
        }
    }

    /// <summary>按模型选行：遍历平面行定位 IndexPath 后 Select；bringIntoView 时滚动到该行。</summary>
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
    // 右键菜单 / 双击 / 复制
    // ------------------------------------------------------------------

    private void ToggleMode()
    {
        _comfortable = !_comfortable;
        Bind(_doc, _changed);
    }

    private ContextMenu BuildContextMenu()
    {
        var cm = new ContextMenu();
        cm.Items.Add(TreeGridShared.Mi(L.T("＋添加子节点", "+ Add Child"), () => _ = AddChild()));
        cm.Items.Add(TreeGridShared.Mi(L.T("－删除子树", "- Delete"), DeleteStep));
        cm.Items.Add(new Separator());
        cm.Items.Add(TreeGridShared.Mi("↑ " + L.T("上移", "Up"), () => MoveStep(-1)));
        cm.Items.Add(TreeGridShared.Mi("↓ " + L.T("下移", "Down"), () => MoveStep(1)));
        cm.Items.Add(new Separator());
        cm.Items.Add(TreeGridShared.Mi(L.T("节点属性", "Node Properties"), () => _ = EditNodeProps()));
        cm.Items.Add(TreeGridShared.Mi(L.LocStringTable, () => _locRequested?.Invoke()));
        cm.Items.Add(new Separator());
        cm.Items.Add(TreeGridShared.Mi(L.T("复制单元格", "Copy Cell"), () => _ = CopyCell()));
        cm.Items.Add(new Separator());
        cm.Items.Add(TreeGridShared.Mi(_comfortable ? L.T("平铺模式", "Flat Mode") : L.T("紧凑模式", "Compact Mode"), ToggleMode));
        return cm;
    }

    /// <summary>复制右键命中单元格的文本（先右键单元格，再选菜单复制）。</summary>
    private async Task CopyCell()
    {
        if (_lastCell == null)
        {
            Gui.Warn(TreeGridShared.OwnerOf(this), L.T("请先右键点击要复制的单元格，再选“复制单元格”。"), L.T("复制", "Copy"));
            return;
        }
        string? text = _lastCell.Value.Col switch
        {
            0 => _lastCell.Value.Row.Node,
            1 => _lastCell.Value.Row.Attrs,
            2 => _lastCell.Value.Row.Text,
            3 => string.Join("\n", _lastCell.Value.Row.Cond.Select(l => l.Text)),
            4 => string.Join("\n", _lastCell.Value.Row.Act.Select(l => l.Text)),
            _ => null,
        };
        if (text == null) return;
        var tl = TopLevel.GetTopLevel(this);
        if (tl?.Clipboard != null) { try { await tl.Clipboard.SetTextAsync(text); } catch { } }
    }

    private async void OnCellDoubleTap(string tag)
    {
        var s = SelectedStep;
        if (s == null) return;
        if (tag == "3")
        {
            if (await ScriptListDialog.Show(_doc, s.Conditions, true, TreeGridShared.OwnerOf(this))) RefreshSelected();
        }
        else if (tag == "4")
        {
            if (await ScriptListDialog.Show(_doc, s.Actions, false, TreeGridShared.OwnerOf(this))) RefreshSelected();
        }
        else if (tag is "0" or "1" or "2")
        {
            await EditNodeProps();
        }
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
        // 默认不选定任何节点（用户指定）；子树高亮仅在真正选中后出现。
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