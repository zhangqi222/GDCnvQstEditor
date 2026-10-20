using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Selection;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
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

    public event Action? LocRequested { add => _locRequested += value; remove => _locRequested -= value; }
    public event Action? SelectedChanged;

    public DocSession Doc => _doc;
    public int? SelectedTextSlot => SelectedStep?.Index;

    public CnvTableEditor()
    {
        _tree = new TreeDataGrid { CanUserResizeColumns = true };
        // 右键点击 → 先选中该行（右键菜单语义与左键一致）
        _tree.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _tree.ContextMenu = BuildContextMenu();
        Content = _tree;
    }

    private CnvStep? SelectedStep => _source?.RowSelection.SelectedItem is CnvRow r ? r.Step : null;

    // ------------------------------------------------------------------
    // 模板
    // ------------------------------------------------------------------

    private static FuncDataTemplate<CnvRow> CellBorder(Action<Border> build, Action<string>? onDoubleTap = null, string? tapTag = null) => new((_, _) =>
    {
        var border = new Border { Background = Brushes.Transparent, Padding = new Thickness(2, 3, 8, 3) };
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
                        width: new GridLength(TreeGridShared.ColNode)),
                    r => r.Children, r => r.HasChildren, r => r.IsExpanded),
                new TemplateColumn<CnvRow>(L.T("属性", "Attrs"),
                    CellBorder(b =>
                    {
                        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap };
                        tb.Bind(TextBlock.TextProperty, new Binding(nameof(CnvRow.Attrs)));
                        b.Child = tb;
                    }, OnCellDoubleTap, "1"),
                    width: new GridLength(TreeGridShared.ColAttrs)),
                new TemplateColumn<CnvRow>(L.T("本地化文本", "Localized Text"),
                    CellBorder(b =>
                    {
                        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap };
                        tb.Bind(TextBlock.TextProperty, new Binding(nameof(CnvRow.Text)));
                        if (!_comfortable) tb.MaxHeight = 54;
                        b.Child = tb;
                    }, OnCellDoubleTap, "2"),
                    width: new GridLength(TreeGridShared.ColText)),
                new TemplateColumn<CnvRow>(L.T("条件", "Conditions"),
                    CellBorder(b =>
                    {
                        var items = new ItemsControl();
                        items.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(CnvRow.Cond)));
                        items.ItemTemplate = LineTemplate();
                        b.Child = items;
                    }, OnCellDoubleTap, "3"),
                    width: new GridLength(TreeGridShared.ColCond)),
                new TemplateColumn<CnvRow>(L.T("动作", "Actions"),
                    CellBorder(b =>
                    {
                        var items = new ItemsControl();
                        items.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(CnvRow.Act)));
                        items.ItemTemplate = LineTemplate();
                        b.Child = items;
                    }, OnCellDoubleTap, "4"),
                    width: new GridLength(TreeGridShared.ColAct)),
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
                break;
            }
        }
    }

    /// <summary>按模型选行：遍历平面行定位 IndexPath 后 Select。</summary>
    private void SelectRow(CnvRow target, bool bringIntoView)
    {
        if (_source == null) return;
        int flat = 0;
        foreach (var r in _source.Rows)
        {
            if (r is HierarchicalRow<CnvRow> hr && ReferenceEquals(hr.Model, target))
            {
                _source.RowSelection.Select(hr.ModelIndexPath);
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
    // 右键菜单 / 双击
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
        cm.Items.Add(TreeGridShared.Mi(_comfortable ? L.T("平铺模式", "Flat Mode") : L.T("紧凑模式", "Compact Mode"), ToggleMode));
        return cm;
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
        if (roots.Count > 0) SelectRow(roots[0], bringIntoView: false);
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
        _cnv.InsertChild(anchor, dlg.Type);
        _changed();
        Bind(_doc, _changed);
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