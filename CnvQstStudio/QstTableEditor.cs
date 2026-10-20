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

/// <summary>QST 树型表格（Semi TreeDataGrid）：节点(树) | Objective ID | 本地化文本 | 条件 | 动作。</summary>
internal sealed class QstTableEditor : UserControl
{
    private DocSession _doc = null!;
    private QstFile _qst = null!;
    private Changed _changed = null!;
    private Action? _locRequested;
    private List<QstRow> _rows = new();
    private TreeDataGrid _tree = null!;
    private HierarchicalTreeDataGridSource<QstRow> _source = null!;
    private (QstRow Row, int Col)? _lastCell;   // 右键命中的单元格（复制用）

    public event Action? LocRequested { add => _locRequested += value; remove => _locRequested -= value; }
    public event Action? SelectedChanged;

    public DocSession Doc => _doc;
    public int? SelectedTextSlot => SelectedRow?.Owner.TextSlot;

    public QstTableEditor()
    {
        _tree = new TreeDataGrid { CanUserResizeColumns = true };
        _tree.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _tree.ContextMenu = BuildContextMenu();
        Content = _tree;
    }

    private QstRow? SelectedRow => _source?.RowSelection.SelectedItem is QstRow r ? r : null;

    // ------------------------------------------------------------------
    // 模板（单元格：网格线 = 右竖线 + 底横线；高亮底色随 Highlight）
    // ------------------------------------------------------------------

    private static FuncDataTemplate<QstRow> CellBorder(Action<Border> build, Action<string>? onDoubleTap = null, string? tapTag = null) => new((_, _) =>
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
        return border;
    });

    private static FuncDataTemplate<ColoredLine> LineTemplate() => new((line, _) =>
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap };
        tb.Bind(TextBlock.TextProperty, new Binding(nameof(ColoredLine.Text)));
        tb.Bind(TextBlock.ForegroundProperty, new Binding(nameof(ColoredLine.Brush)));
        return tb;
    });

    private HierarchicalTreeDataGridSource<QstRow> BuildSource(List<QstRow> roots)
    {
        // 列宽用 Star 权重（保持 300:140:280:230:230 比例），表格填满父容器宽度。
        var s = new HierarchicalTreeDataGridSource<QstRow>(roots)
        {
            Columns =
            {
                new HierarchicalExpanderColumn<QstRow>(
                    new TemplateColumn<QstRow>(L.T("节点", "Node"),
                        CellBorder(b =>
                        {
                            var row = new StackPanel { Orientation = Orientation.Horizontal };
                            var icon = new PathIcon { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
                            icon.Bind(Visual.IsVisibleProperty, new Binding(nameof(QstRow.IconKey)) { Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty });
                            icon.Bind(PathIcon.DataProperty, new Binding(nameof(QstRow.IconKey)) { Converter = TreeGridShared.IconToGeometry });
                            row.Children.Add(icon);
                            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                            var node = new TextBlock { TextWrapping = TextWrapping.Wrap };
                            node.Bind(TextBlock.TextProperty, new Binding(nameof(QstRow.Node)));
                            node.Bind(TextBlock.ForegroundProperty, new Binding(nameof(QstRow.NodeBrush)));
                            stack.Children.Add(node);
                            var sub = new TextBlock { FontStyle = FontStyle.Italic, FontSize = 11, Foreground = UiTheme.MutedBrush, Margin = new Thickness(0, 1, 0, 0) };
                            sub.Bind(TextBlock.TextProperty, new Binding(nameof(QstRow.SubText)));
                            sub.Bind(Visual.IsVisibleProperty, new Binding(nameof(QstRow.SubText)) { Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty });
                            stack.Children.Add(sub);
                            row.Children.Add(stack);
                            b.Child = row;
                        }, OnCellDoubleTap, "0"),
                        width: new GridLength(TreeGridShared.ColNode, GridUnitType.Star)),
                    r => r.Children, r => r.HasChildren, r => r.IsExpanded),
                new TemplateColumn<QstRow>("Objective ID",
                    CellBorder(b =>
                    {
                        var tb = new TextBlock();
                        tb.Bind(TextBlock.TextProperty, new Binding(nameof(QstRow.IdText)));
                        // 该 ID 的作用说明（按行所有者不同而不同；空则不弹）
                        tb.Bind(ToolTip.TipProperty, new Binding(nameof(QstRow.IdTip)) { Converter = TreeGridShared.EmptyToNull });
                        b.Child = tb;
                    }, OnCellDoubleTap, "1"),
                    width: new GridLength(TreeGridShared.ColAttrs, GridUnitType.Star)),
                new TemplateColumn<QstRow>(L.T("本地化文本", "Localized Text"),
                    CellBorder(b =>
                    {
                        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxHeight = 220 };
                        tb.Bind(TextBlock.TextProperty, new Binding(nameof(QstRow.Text)));
                        b.Child = tb;
                    }, OnCellDoubleTap, "2"),
                    width: new GridLength(TreeGridShared.ColText, GridUnitType.Star)),
                new TemplateColumn<QstRow>(L.T("条件", "Conditions"),
                    CellBorder(b =>
                    {
                        var items = new ItemsControl();
                        items.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(QstRow.Cond)));
                        items.ItemTemplate = LineTemplate();
                        b.Child = items;
                    }, OnCellDoubleTap, "2b"),
                    width: new GridLength(TreeGridShared.ColCond, GridUnitType.Star)),
                new TemplateColumn<QstRow>(L.T("动作", "Actions"),
                    CellBorder(b =>
                    {
                        var items = new ItemsControl();
                        items.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(QstRow.Act)));
                        items.ItemTemplate = LineTemplate();
                        b.Child = items;
                    }, OnCellDoubleTap, "3"),
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
        (QstRow? Row, int Col) hit = (null, -1);
        foreach (var anc in visual.GetSelfAndVisualAncestors())
        {
            if (hit.Col < 0 && anc is TreeDataGridCell cell) hit.Col = cell.ColumnIndex;
            if (hit.Row == null && anc is Control c && _tree.TryGetRowModel(c, out QstRow? row) && row != null)
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
    // 右键菜单 / 双击 / 复制
    // ------------------------------------------------------------------

    private ContextMenu BuildContextMenu()
    {
        var cm = new ContextMenu();
        cm.Items.Add(TreeGridShared.Mi(L.T("＋任务"), () => AddTask()));
        cm.Items.Add(TreeGridShared.Mi(L.T("＋目标"), () => AddObjective()));
        cm.Items.Add(TreeGridShared.Mi(L.T("＋完成事件"), () => AddEvent()));
        cm.Items.Add(TreeGridShared.Mi(L.T("＋接受事件"), () => AddIncentive()));
        cm.Items.Add(new Separator());
        cm.Items.Add(TreeGridShared.Mi(L.T("－删除所选", "- Delete"), DeleteRow));
        cm.Items.Add(new Separator());
        cm.Items.Add(TreeGridShared.Mi(L.T("任务属性", "Quest Props"), () => _ = EditQuestProps()));
        cm.Items.Add(TreeGridShared.Mi(L.T("复制单元格", "Copy Cell"), () => _ = CopyCell()));
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
            1 => _lastCell.Value.Row.IdText,
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
        var sel = SelectedRow;
        if (sel == null) return;

        if (tag == "2b")
        {
            if (await ScriptListDialog.Show(_doc, sel.Owner.Conditions, true, TreeGridShared.OwnerOf(this))) sel.Reload();
        }
        else if (tag == "3")
        {
            if (await ScriptListDialog.Show(_doc, sel.Owner.Actions, false, TreeGridShared.OwnerOf(this))) sel.Reload();
        }
        else if (tag is "0" or "1" or "2")
        {
            await EditFields();
        }
    }

    private async Task EditFields()
    {
        var sel = SelectedRow;
        if (sel == null) return;
        switch (sel.Owner)
        {
            case QstRootOwner:
            case QstQuestOwner:
                await EditQuestProps();
                break;
            case QstTaskRow or QstEventRow or QstObjectiveRow:
                if (await QstFieldsDialog.Show(sel.Owner, TreeGridShared.OwnerOf(this)))
                {
                    sel.Reload();
                    _changed();
                }
                break;
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
                "▶ " + L.F("OnAccept（接受事件 ×{0}）", t.Incentives.Count), UiTheme.AccentBrush));
            rows.Add(acceptRow);
            acceptRow.Attach(taskRow);
            for (int k = 0; k < t.Incentives.Count; k++)
            {
                var r = new QstRow(this, new QstEventRow(_qst, t, t.Incentives[k], titleSlotOf(no - 1) + 2 + k, k));
                rows.Add(r);
                r.Attach(acceptRow);
            }

            var objRow = new QstRow(this, new QstGroupOwner(
                "✅ " + L.F("Objectives（目标 ×{0}）", t.Objectives.Count), UiTheme.OkBrush));
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
                "🏁 " + L.F("OnComplete（完成事件 ×{0}）", t.Events.Count), UiTheme.OkBrush));
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
        var roots = _rows.Where(r => r.Parent == null).ToList();
        _source = BuildSource(roots);
        _tree.Source = _source;
        _source.ExpandAll();
        // 默认不选定任何节点（用户指定）；子树高亮仅在真正选中后出现。
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

    private void AddObjective()
    {
        var t = ActiveTask();
        if (t == null) return;
        var o = _qst.AddObjective(t);
        _changed();
        Bind(_doc, _changed);
        SelectOwner(o);
    }

    private void AddEvent()
    {
        var t = ActiveTask();
        if (t == null) return;
        var e = _qst.AddEvent(t, incentive: false);
        _changed();
        Bind(_doc, _changed);
        SelectOwner(e);
    }

    private void AddIncentive()
    {
        var t = ActiveTask();
        if (t == null) return;
        var e = _qst.AddEvent(t, incentive: true);
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