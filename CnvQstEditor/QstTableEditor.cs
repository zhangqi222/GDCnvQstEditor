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

/// <summary>QST 树型表格（Semi TreeDataGrid）：节点(树) | Objective ID | 条件 | 动作。</summary>
internal sealed class QstTableEditor : UserControl
{
    private DocSession _doc = null!;
    private QstFile _qst = null!;
    private Changed _changed = null!;
    private Action? _locRequested;
    private List<QstRow> _rows = new();
    private TreeDataGrid _tree = null!;
    private HierarchicalTreeDataGridSource<QstRow> _source = null!;
    private (QstRow Row, int Col)? _lastCell;   // 右键命中的单元格（菜单/复制用）

    public event Action? LocRequested { add => _locRequested += value; remove => _locRequested -= value; }
    public event Action? SelectedChanged;

    public DocSession Doc => _doc;
    public int? SelectedTextSlot => SelectedRow?.Owner.TextSlot;

    private static readonly Dictionary<string, int> ColFromTag = new() { ["0"] = 0, ["1"] = 1, ["2"] = 2, ["3"] = 3, ["4"] = 4 };

    public QstTableEditor()
    {
        _tree = new TreeDataGrid { CanUserResizeColumns = true };
        // 右键：选中行在此处理；菜单在单元格模板的 ContextRequested 里按行+列动态生成
        _tree.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _tree.ContextMenu = BuildContextMenu(null, -1);
        Content = _tree;
    }

    private QstRow? SelectedRow => _source?.RowSelection.SelectedItem is QstRow r ? r : null;

    // ------------------------------------------------------------------
    // 模板：单元格单行高度（不自动换行增高），网格线 = 右竖线 + 底横线
    // ------------------------------------------------------------------

    private FuncDataTemplate<QstRow> CellBorder(Action<Border> build, Action<string>? onDoubleTap = null, string? tapTag = null) => new((_, _) =>
    {
        var border = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(2, 3, 8, 3),
            BorderBrush = UiTheme.GridLineBrush,
            BorderThickness = new Thickness(0, 0, 1, 1),
        };
        border.Bind(Border.BackgroundProperty, new Binding(nameof(QstRow.Highlight)) { Converter = TreeGridShared.HighlightToBrush });
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
                if (border.DataContext is QstRow r) OnCellRightPressed(tag, r);
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

    private HierarchicalTreeDataGridSource<QstRow> BuildSource(List<QstRow> roots)
    {
        // 列宽：序号 Auto 按内容自适应；节点 30% / uid 8% / 条件 31% / 动作 31%（Star 权重）。
        var s = new HierarchicalTreeDataGridSource<QstRow>(roots)
        {
            Columns =
            {
                // 序号列（与 CNV 统一显示；= 行的显示顺序，Bind 时按展开树顺序编号）
                new TemplateColumn<QstRow>(L.T("序号", "No."),
                    CellBorder(b =>
                    {
                        var tb = OneLine();
                        tb.Bind(TextBlock.TextProperty, new Binding(nameof(QstRow.Seq)));
                        tb.Foreground = UiTheme.MutedBrush;
                        b.Child = tb;
                    }, OnCellDoubleTap, "0"),
                    width: GridLength.Auto),
                new HierarchicalExpanderColumn<QstRow>(
                    new TemplateColumn<QstRow>(L.T("节点", "Node"),
                        CellBorder(b => b.Child = TreeGridShared.NodeCellContent(), OnCellDoubleTap, "1"),
                        width: new GridLength(TreeGridShared.ColNode, GridUnitType.Star)),
                    r => r.Children, r => r.HasChildren, r => r.IsExpanded),
                new TemplateColumn<QstRow>("uid",
                    CellBorder(b =>
                    {
                        var tb = OneLine();
                        tb.Bind(TextBlock.TextProperty, new Binding(nameof(QstRow.IdText)));
                        // 该 ID 的作用说明（按行所有者不同而不同；空则不弹）
                        tb.Bind(ToolTip.TipProperty, new Binding(nameof(QstRow.IdTip)) { Converter = TreeGridShared.EmptyToNull });
                        b.Child = tb;
                    }, OnCellDoubleTap, "2"),
                    width: new GridLength(TreeGridShared.ColAttrs, GridUnitType.Star)),
                new TemplateColumn<QstRow>(L.T("条件", "Conditions"),
                    CellBorder(b =>
                    {
                        // 单行显示：水平排列，段间 " | "（生成端已拼入），超宽裁剪
                        var items = new ItemsControl { MaxHeight = 18, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top };
                        items.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal });
                        items.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(QstRow.Cond)));
                        items.ItemTemplate = LineTemplate();
                        b.Child = items;
                    }, OnCellDoubleTap, "3"),
                    width: new GridLength(TreeGridShared.ColCond, GridUnitType.Star)),
                new TemplateColumn<QstRow>(L.T("动作", "Actions"),
                    CellBorder(b =>
                    {
                        var items = new ItemsControl { MaxHeight = 18, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top };
                        items.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal });
                        items.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(QstRow.Act)));
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
            if (anc is Control c && _tree.TryGetRowModel(c, out QstRow? row) && row != null)
            {
                SelectRow(row, bringIntoView: false);
                return;
            }
        }
        // 空白区域（不在任何行上）：清空菜单
        _tree.ContextMenu = BuildContextMenu(null, -1);
    }

    /// <summary>右键命中单元格（模板 ContextRequested，按列 tag 定位）→ 生成精确到单元格的菜单。</summary>
    private void OnCellRightPressed(string tag, QstRow row)
    {
        int col = ColFromTag.GetValueOrDefault(tag, -1);
        _lastCell = (row, col);
        _tree.ContextMenu = BuildContextMenu(row, col);
    }

    private void SelectRow(QstRow target, bool bringIntoView)
    {
        if (_source == null) return;
        int flat = 0;
        foreach (var r in _source.Rows)
        {
            if (r is HierarchicalRow<QstRow> hr && ReferenceEquals(hr.Model, target))
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
        if (_source?.RowSelection.SelectedItem is QstRow sel)
            WalkHi(sel);
        return;

        void WalkHi(QstRow r)
        {
            r.Highlight = true;
            foreach (var c in r.Children) WalkHi(c);
        }
    }

    // ------------------------------------------------------------------
    // 右键菜单（按行/列动态生成：根→任务属性|添加任务；task→添加目标|完成|接受|删除任务；以此类推）
    // ------------------------------------------------------------------

    private ContextMenu BuildContextMenu(QstRow? row, int col)
    {
        var cm = new ContextMenu();
        if (row == null) return cm;   // 空白区域：无可用菜单

        // 复制单元格固定在菜单最上方（有内容即可复制，含任务 ID 列）
        string? cell = CellText(row, col);
        if (cell != null)
        {
            cm.Items.Add(TreeGridShared.Mi(L.T("复制单元格", "Copy Cell"), () => CopyCell()));
            cm.Items.Add(new Separator());
        }

        switch (row.Owner)
        {
            case QstRootOwner:
                cm.Items.Add(TreeGridShared.Mi(L.T("任务属性", "Quest Props"), () => _ = EditQuestProps()));
                cm.Items.Add(TreeGridShared.Mi(L.T("＋任务"), () => AddTask()));
                break;
            case QstQuestOwner:
                cm.Items.Add(TreeGridShared.Mi(L.T("任务属性", "Quest Props"), () => _ = EditQuestProps()));
                break;
            case QstTaskRow tr:
                // 顺序与表格实际显示一致：接受事件 → 目标 → 完成事件
                cm.Items.Add(TreeGridShared.Mi(L.T("＋接受事件"), () => AddEvent(tr.T, incentive: true)));
                cm.Items.Add(TreeGridShared.Mi(L.T("＋目标"), () => AddObjective(tr.T)));
                cm.Items.Add(TreeGridShared.Mi(L.T("＋完成事件"), () => AddEvent(tr.T, incentive: false)));
                cm.Items.Add(new Separator());
                cm.Items.Add(TreeGridShared.Mi(L.T("－删除任务", "- Delete Task"), () => DeleteRow()));
                break;
            case QstGroupOwner g:
                if (g.Kind == "Accept")
                    cm.Items.Add(TreeGridShared.Mi(L.T("＋接受事件"), () => AddEvent(g.Task, incentive: true)));
                else if (g.Kind == "Objectives")
                    cm.Items.Add(TreeGridShared.Mi(L.T("＋目标"), () => AddObjective(g.Task)));
                else if (g.Kind == "Complete")
                    cm.Items.Add(TreeGridShared.Mi(L.T("＋完成事件"), () => AddEvent(g.Task, incentive: false)));
                break;
            case QstObjectiveRow:
                cm.Items.Add(TreeGridShared.Mi(L.T("－删除目标", "- Delete Objective"), () => DeleteRow()));
                break;
            case QstEventRow:
                cm.Items.Add(TreeGridShared.Mi(L.T("－删除事件", "- Delete Event"), () => DeleteRow()));
                break;
        }

        // 节点编辑 / 编辑条件 / 编辑动作：同一组（分割线隔开；以行为准，不依赖右键命中的列）
        bool hasFields = row.Owner is QstTaskRow or QstObjectiveRow or QstEventRow;
        if (hasFields || row.Owner.HasScript)
        {
            cm.Items.Add(new Separator());
            if (hasFields)
                cm.Items.Add(TreeGridShared.Mi(L.T("节点编辑", "Edit Node"), () => _ = EditFields(row)));
            if (row.Owner.HasScript)
            {
                cm.Items.Add(TreeGridShared.Mi(L.T("编辑条件", "Edit Conditions"), () => _ = EditScript(row, isCondition: true)));
                cm.Items.Add(TreeGridShared.Mi(L.T("编辑动作", "Edit Actions"), () => _ = EditScript(row, isCondition: false)));
            }
        }
        return cm;
    }

    private static string? CellText(QstRow r, int col) => col switch
    {
        0 => r.Seq,
        1 => r.Node,
        2 => r.IdText.Length > 0 ? r.IdText : null,
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
    private async Task EditScript(QstRow row, bool isCondition)
    {
        var list = isCondition ? row.Owner.Conditions : row.Owner.Actions;
        if (list == null) return;
        if (await ScriptListDialog.Show(_doc, list, isCondition, TreeGridShared.OwnerOf(this)))
        {
            row.Reload();
            _changed();
        }
    }

    private async void OnCellDoubleTap(string tag)
    {
        if (_source?.RowSelection.SelectedItem is not QstRow sel) return;

        if (tag == "3")
        {
            if (sel.Owner.HasScript) await EditScript(sel, isCondition: true);
        }
        else if (tag == "4")
        {
            if (sel.Owner.HasScript) await EditScript(sel, isCondition: false);
        }
        else if (tag is "0" or "1" or "2")
        {
            switch (sel.Owner)
            {
                case QstRootOwner:
                case QstQuestOwner:
                    await EditQuestProps();
                    break;
                case QstTaskRow or QstEventRow or QstObjectiveRow:
                    await EditFields(sel);
                    break;
            }
        }
    }

    private async Task EditFields(QstRow row)
    {
        if (await QstFieldsDialog.Show(row.Owner, TreeGridShared.OwnerOf(this)))
        {
            row.Reload();
            _changed();
        }
    }

    private async Task EditQuestProps()
    {
        if (!await QuestPropsDialog.Show(_qst, TreeGridShared.OwnerOf(this))) return;
        _changed();
        Bind(_doc, _changed);
    }

    public void NotifyEdited()
    {
        _doc.Dirty = true;
        _changed();
    }

    /// <summary>本地化槽位文本变化后刷新显示该槽位的行（组名/任务名/标题/描述/目标）。</summary>
    public void RefreshTextSlot(int slot)
    {
        foreach (var r in _rows)
            if (r.Owner.TextSlot == slot)
                r.Reload();
    }

    public void Bind(DocSession doc, Changed changed)
    {
        _doc = doc;
        _qst = doc.Qst!;
        _changed = changed;

        // 层级对齐官方 QuestEditor：
        //   组名（根，红） → 任务名（旗） → task [N] 标题 → task 描述行 → OnAccept / Objectives / OnComplete 组 → 事件/目标行
        var rows = new List<QstRow>();
        var rootRow = new QstRow(this, new QstRootOwner(_qst));
        rows.Add(rootRow);
        var questRow = new QstRow(this, new QstQuestOwner(_qst));
        rows.Add(questRow);
        questRow.Attach(rootRow);

        int no = 1;
        foreach (var t in _qst.Tasks)
        {
            var taskRow = new QstRow(this, new QstTaskRow(_qst, t, no - 1));
            rows.Add(taskRow);
            taskRow.Attach(questRow);

            // task 描述行（串表槽=标题槽+1；对齐官方编辑器，可在底部编辑器编辑）
            var descRow = new QstRow(this, new QstTaskDescRow(_qst, t, no - 1));
            rows.Add(descRow);
            descRow.Attach(taskRow);

            var acceptRow = new QstRow(this, new QstGroupOwner(
                "▶ " + L.F("OnAccept（接受事件 ×{0}）", t.Incentives.Count), UiTheme.SecondaryBrush, "Accept", t));
            rows.Add(acceptRow);
            acceptRow.Attach(taskRow);
            for (int k = 0; k < t.Incentives.Count; k++)
            {
                var r = new QstRow(this, new QstEventRow(_qst, t, t.Incentives[k], titleSlotOf(no - 1) + 2 + k, k));
                rows.Add(r);
                r.Attach(acceptRow);
            }

            var objRow = new QstRow(this, new QstGroupOwner(
                "✅ " + L.F("Objectives（目标 ×{0}）", t.Objectives.Count), UiTheme.WarnBrush, "Objectives", t));
            rows.Add(objRow);
            objRow.Attach(taskRow);
            for (int k = 0; k < t.Objectives.Count; k++)
            {
                int slot = titleSlotOf(no - 1) + 2 + t.Incentives.Count + k;
                var r = new QstRow(this, new QstObjectiveRow(_qst, t, t.Objectives[k], slot));
                rows.Add(r);
                r.Attach(objRow);
            }

            var compRow = new QstRow(this, new QstGroupOwner(
                "🏁 " + L.F("OnComplete（完成事件 ×{0}）", t.Events.Count), UiTheme.OkBrush, "Complete", t));
            rows.Add(compRow);
            compRow.Attach(taskRow);
            for (int k = 0; k < t.Events.Count; k++)
            {
                var r = new QstRow(this, new QstEventRow(_qst, t, t.Events[k], titleSlotOf(no - 1) + 2 + t.Incentives.Count + t.Objectives.Count + k, k));
                rows.Add(r);
                r.Attach(compRow);
            }
            no++;
        }

        _rows = rows;
        // 序号：按显示顺序 1 起编号（Bind 在数据变化后整体重建，序号随之刷新）
        for (int i = 0; i < _rows.Count; i++) _rows[i].Seq = (i + 1).ToString();
        var roots = _rows.Where(r => r.Parent == null).ToList();
        _source = BuildSource(roots);
        _tree.Source = _source;
        _source.ExpandAll();
        // 默认不选定任何节点；子树高亮仅在真正选中后出现。
        SelectedChanged?.Invoke();
    }

    private int titleSlotOf(int ti) => _qst.TaskTitleSlot(ti);

    private QstTask ActiveTask()
    {
        for (var r = SelectedRow; r != null; r = r.Parent)
            if (r.Owner is QstTaskRow tr) return tr.T;
        return _qst.Tasks.FirstOrDefault()!;
    }

    public void SelectOwner(object owner, bool bringIntoView = true)
    {
        var row = _rows.FirstOrDefault(r => ReferenceEquals(r.Owner.OwnerObject, owner));
        if (row != null) SelectRow(row, bringIntoView);
    }

    // 结构改动统一走 QstFile 的增删方法：它们会按公式 2+2T+O+I+E 重建本地化表。
    private void AddTask()
    {
        var t = _qst.AddTask();
        _changed();
        Bind(_doc, _changed);
        SelectOwner(t);     // 自动选定并滚动到新行
    }

    private void AddObjective(QstTask? task)
    {
        var t = task ?? ActiveTask();
        if (t == null) return;
        var o = _qst.AddObjective(t);
        _changed();
        Bind(_doc, _changed);
        SelectOwner(o);
    }

    private void AddEvent(QstTask? task, bool incentive)
    {
        var t = task ?? ActiveTask();
        if (t == null) return;
        var e = _qst.AddEvent(t, incentive);
        _changed();
        Bind(_doc, _changed);
        SelectOwner(e);
    }

    private void DeleteRow()
    {
        var sel = SelectedRow;
        if (sel == null) return;
        switch (sel.Owner)
        {
            case QstTaskRow tr:
                if (!_qst.RemoveTask(tr.T)) return;
                break;
            case QstEventRow er:
                if (!_qst.RemoveEvent(er.OwnerTask, er.E)) return;
                break;
            case QstObjectiveRow or:
                if (!_qst.RemoveObjective(or.OwnerTask, or.O)) return;
                break;
            default:
                Gui.Warn(TreeGridShared.OwnerOf(this), L.T("该行是结构分组（组名 / 任务名 / 描述 / OnAccept / Objectives / OnComplete），不能删除；请删除其下的具体行。"), L.T("删除", "Delete"));
                return;
        }
        _changed();
        Bind(_doc, _changed);
    }
}