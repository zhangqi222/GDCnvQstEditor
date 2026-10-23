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

namespace CnvQstEditor;

/// <summary>CNV 树型表格（Semi TreeDataGrid）：节点(树) | 属性 | 条件 | 动作。</summary>
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
        // 列宽：序号 Auto 按内容自适应；节点 30% / 属性 8% / 条件 31% / 动作 31%（Star 权重）。
        var s = new HierarchicalTreeDataGridSource<CnvRow>(roots)
        {
            Columns =
            {
                // 序号列（=Step.Index，与跳转节点 LinkId / 节点编辑对话框编号同一套编号）
                new TemplateColumn<CnvRow>(L.T("序号", "No."),
                    CellBorder(b =>
                    {
                        var tb = OneLine();
                        tb.Bind(TextBlock.TextProperty, new Binding(nameof(CnvRow.Seq)));
                        tb.Foreground = UiTheme.MutedBrush;
                        b.Child = tb;
                    }, OnCellDoubleTap, "0"),
                    width: GridLength.Auto),
                new HierarchicalExpanderColumn<CnvRow>(
                    new TemplateColumn<CnvRow>(L.T("节点", "Node"),
                        CellBorder(b => b.Child = TreeGridShared.NodeCellContent(), OnCellDoubleTap, "1"),
                        width: new GridLength(TreeGridShared.ColNode, GridUnitType.Star)),
                    r => r.Children, r => r.HasChildren, r => r.IsExpanded),
                new TemplateColumn<CnvRow>(L.T("属性", "Attrs"),
                    CellBorder(b =>
                    {
                        var tb = OneLine();
                        tb.Bind(TextBlock.TextProperty, new Binding(nameof(CnvRow.Attrs)));
                        b.Child = tb;
                    }, OnCellDoubleTap, "2"),
                    width: new GridLength(TreeGridShared.ColAttrs, GridUnitType.Star)),
                new TemplateColumn<CnvRow>(L.T("条件", "Conditions"),
                    CellBorder(b =>
                    {
                        // 单行显示：水平排列，段间 " | "（生成端已拼入），超宽裁剪
                        var items = new ItemsControl { MaxHeight = 18, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top };
                        items.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal });
                        items.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(CnvRow.Cond)));
                        items.ItemTemplate = LineTemplate();
                        b.Child = items;
                    }, OnCellDoubleTap, "3"),
                    width: new GridLength(TreeGridShared.ColCond, GridUnitType.Star)),
                new TemplateColumn<CnvRow>(L.T("动作", "Actions"),
                    CellBorder(b =>
                    {
                        var items = new ItemsControl { MaxHeight = 18, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top };
                        items.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal });
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

        // 复制单元格固定在菜单最上方（列只影响复制内容）
        if (CellText(row, col) != null)
        {
            cm.Items.Add(TreeGridShared.Mi(L.T("复制单元格", "Copy Cell"), () => CopyCell()));
            cm.Items.Add(new Separator());
        }

        // 菜单以行为准。
        bool isRoot = ReferenceEquals(row.Step, _cnv.Root);
        cm.Items.Add(TreeGridShared.Mi(L.T("＋添加子节点", "+ Add Child"), () => _ = AddChild()));
        if (!isRoot)
        {
            cm.Items.Add(TreeGridShared.Mi(L.T("－删除子树", "- Delete"), DeleteStep));
            cm.Items.Add(new Separator());
            cm.Items.Add(TreeGridShared.Mi("↑ " + L.T("上移", "Up"), () => MoveStep(-1)));
            cm.Items.Add(TreeGridShared.Mi("↓ " + L.T("下移", "Down"), () => MoveStep(1)));
        }

        // 节点编辑 / 编辑条件 / 编辑动作：同一组（分割线隔开），CNV 每个节点都有条件/动作列表
        cm.Items.Add(new Separator());
        cm.Items.Add(TreeGridShared.Mi(L.T("节点编辑", "Edit Node"), () => _ = EditNodeProps()));
        cm.Items.Add(TreeGridShared.Mi(L.T("编辑条件", "Edit Conditions"), () => _ = EditScript(row, isCondition: true)));
        cm.Items.Add(TreeGridShared.Mi(L.T("编辑动作", "Edit Actions"), () => _ = EditScript(row, isCondition: false)));
        return cm;
    }

    private static string? CellText(CnvRow r, int col) => col switch
    {
        0 => r.Seq,
        1 => r.Node,
        2 => r.Attrs,
        3 => r.Cond.Count > 0 ? string.Join(" | ", r.Cond.Select(l => l.Text)) : null,
        4 => r.Act.Count > 0 ? string.Join(" | ", r.Act.Select(l => l.Text)) : null,
        _ => null,
    };

    private void CopyCell()
    {
        if (_lastCell == null || CellText(_lastCell.Value.Row, _lastCell.Value.Col) is not string text) return;
        ClipboardHelper.SetText(text);
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

    /// <summary>本地化槽位文本变化后刷新显示该槽位的行（跳转节点无自身文本时回退显示目标文本）。</summary>
    public void RefreshTextSlot(int slot)
    {
        foreach (var r in _rows)
            if (r.Step.Index == slot || (r.Step.Type == Names.StepLink && r.Step.LinkId == (uint)slot))
                r.Reload();
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

    /// <summary>
    /// 添加子节点：与"节点编辑"共用同一 CnvNodeDialog（先插入默认台词节点 → 弹窗补全字段，
    /// 取消则撤回插入——新节点为叶子，RemoveSubtree 可干净回滚）。
    /// </summary>
    private async Task AddChild()
    {
        var anchor = SelectedStep;
        if (anchor == null) return;
        var owner = TreeGridShared.OwnerOf(this);
        var step = _cnv.InsertChild(anchor, Names.StepSpeech);
        _changed();
        Bind(_doc, _changed);
        SelectStep(step);   // 自动选定并滚动到新行
        if (await CnvNodeDialog.ShowNew(_cnv, step, owner))
        {
            RefreshSelected();
            _changed();
        }
        else
        {
            _cnv.RemoveSubtree(step);
            _changed();
            Bind(_doc, _changed);
        }
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