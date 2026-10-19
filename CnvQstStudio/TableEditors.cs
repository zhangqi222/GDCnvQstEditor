using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CnvQst;

namespace CnvQstStudio;

/// <summary>把 {^n} 译成真实换行（单元格显示），提交时反译回去。</summary>
internal static class TableLayout
{
    public static string Display(string raw) => raw.Replace("{^n}", "\n");
    public static string Stored(string edited) => edited.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "{^n}");
}

/// <summary>条件/动作列的一行文字与着色（未命名灰、推断橙、已知正文色）。</summary>
internal sealed class ColoredLine
{
    public string Text { get; }
    public Brush Brush { get; }

    public ColoredLine(string text, bool unknown, bool inferred)
    {
        Text = text;
        Brush = unknown ? WpfTheme.UnknownBrush : inferred ? WpfTheme.InferredBrush : WpfTheme.TextBrush;
    }
}

/// <summary>CNV 节点结构列文本：类型徽标 + [idx] kind {tag} →link [VO]，不含大段正文。</summary>
internal static class CnvNodeLabel
{
    /// <summary>节点类型的界面名（中文基准，走语言包）。</summary>
    public static string KindZh(uint type) => L.T(type switch
    {
        0 => "根", 1 => "台词", 2 => "继续选项", 3 => "接受选项", 4 => "拒绝选项",
        5 => "通用", 6 => "结束对话", 7 => "跳转", 8 => "随机选项", _ => "未知类型",
    });

    /// <summary>结构树用：类型名 + 文本摘要（无缩进——层级由树的层级表达）。</summary>
    public static string Plain(CnvFile c, CnvStep s)
    {
        string text = c.TextFor(s).Replace("\n", " ");
        string label = KindZh(s.Type);
        if (text.Length > 0) label += $"：{(text.Length > 30 ? text[..30] + "…" : text)}";
        if (!s.VoPath.IsEmpty) label += L.T("（配音）");
        return label;
    }

    /// <summary>平铺表用：按深度缩进，层级一眼可辨。</summary>
    public static string For(CnvFile c, CnvStep s)
    {
        string pad = new string('　', Math.Min(s.Depth, 10));
        string label = pad + KindZh(s.Type);
        string text = c.TextFor(s).Replace("\n", " ");
        if (text.Length > 0)
        {
            if (text.Length > 24) text = text[..24] + "…";
            label += $"：{text}";
        }
        if (s.HasLink) label += L.T("（跳转）");
        return label;
    }
}

// ---------------------------------------------------------------------------
// 行模型（INPC：编辑后单行刷新）
// ---------------------------------------------------------------------------

internal sealed class CnvRow : INotifyPropertyChanged
{
    private readonly CnvTableEditor _editor;
    public DocSession Doc { get; }
    public CnvStep Step { get; }

    public string Node { get; private set; } = "";
    public string Attrs { get; private set; } = "";
    public List<ColoredLine> Cond { get; private set; } = new();
    public List<ColoredLine> Act { get; private set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public CnvRow(CnvTableEditor editor, DocSession doc, CnvStep s)
    {
        _editor = editor;
        Doc = doc;
        Step = s;
        Reload();
    }

    public string Text
    {
        get => TableLayout.Display(Doc.PrimaryLanguage.Texts[Math.Min(Step.Index, Doc.PrimaryLanguage.Texts.Count - 1)].Text);
        set
        {
            var texts = Doc.PrimaryLanguage.Texts;
            if (Step.Index < texts.Count)
            {
                texts[Step.Index].SetText(TableLayout.Stored(value));
                _editor.NotifyEdited();
            }
        }
    }

    public void Reload()
    {
        var c = Doc.Cnv!;
        Node = CnvNodeLabel.For(c, Step);
        var parts = new List<string>();
        if ((Step.Unknown & 1) != 0) parts.Add(L.T("仅一次"));
        if ((Step.Unknown & 2) != 0) parts.Add(L.T("总是显示"));
        if ((Step.Unknown & 4) != 0) parts.Add(L.T("不重要"));
        if (!Step.VoPath.IsEmpty) parts.Add(L.T("有配音"));
        Attrs = string.Join("　", parts);
        Cond = ScriptLines(Step.Conditions);
        Act = ScriptLines(Step.Actions);
        foreach (string p in new[] { nameof(Node), nameof(Attrs), nameof(Cond), nameof(Act) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    private static List<ColoredLine> ScriptLines(ScriptList list)
    {
        var lines = new List<ColoredLine>();
        foreach (var r in list.Records)
        {
            bool un = !r.Named;
            bool inf = r.Named && (r.IsCondition ? Names.InferredCondition(r.Type) : Names.InferredAction(r.Type));
            lines.Add(new ColoredLine((r.IsCondition ? "⚑ " : "⚡ ") + Gui.Describe(r), un, inf));
        }
        return lines;
    }
}

/// <summary>QST 行承载：task / objective / event（incentive 也按 event）。</summary>
internal abstract class QstRowOwner
{
    public abstract object OwnerObject { get; }
    public abstract string NodeLabel { get; }
    public abstract string FieldLabel { get; }
    public abstract bool HasScript { get; }
    public abstract ScriptList? Conditions { get; }
    public abstract ScriptList? Actions { get; }
}

internal sealed class QstTaskRow : QstRowOwner
{
    public QstTask T;
    private readonly int _no;
    public QstTaskRow(QstTask t, int no) { T = t; _no = no; }
    public override object OwnerObject => T;
    public override string NodeLabel => L.F("任务 {0}", _no);
    public override string FieldLabel
    {
        get
        {
            var p = new List<string>();
            if (T.Flag8) p.Add(L.T("阻断后续任务"));
            if (T.Flag9) p.Add(L.T("不向上传递"));
            return string.Join("　", p);
        }
    }
    public override bool HasScript => false;
    public override ScriptList? Conditions => null;
    public override ScriptList? Actions => null;
}

internal sealed class QstEventRow : QstRowOwner
{
    public QstEvent E;
    public string Kind;
    public QstEventRow(QstEvent e, string kind) { E = e; Kind = kind; }
    public override object OwnerObject => E;
    public override string NodeLabel => Kind;
    public override string FieldLabel => E.Unknown != 0 ? L.F("标志 {0}", E.Unknown) : "";
    public override bool HasScript => true;
    public override ScriptList? Conditions => E.Conditions;
    public override ScriptList? Actions => E.Actions;
}

internal sealed class QstObjectiveRow : QstRowOwner
{
    public QstObjective O;
    public QstObjectiveRow(QstObjective o) => O = o;
    public override object OwnerObject => O;
    public override string NodeLabel => L.T("目标");
    public override string FieldLabel => "";
    public override bool HasScript => true;
    public override ScriptList? Conditions => O.Conditions;
    public override ScriptList? Actions => O.Actions;
}

internal sealed class QstRow : INotifyPropertyChanged
{
    public QstRowOwner Owner { get; }
    public string Node { get; private set; }
    public string Field { get; private set; }
    public List<ColoredLine> Cond { get; private set; } = new();
    public List<ColoredLine> Act { get; private set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public QstRow(QstRowOwner owner)
    {
        Owner = owner;
        Node = owner.NodeLabel;
        Field = owner.FieldLabel;
        Reload();
    }

    public void Reload()
    {
        Node = Owner.NodeLabel;
        Field = Owner.FieldLabel;
        Cond = Owner.Conditions != null ? ScriptLines(Owner.Conditions) : new List<ColoredLine>();
        Act = Owner.Actions != null ? ScriptLines(Owner.Actions) : new List<ColoredLine>();
        foreach (string p in new[] { nameof(Node), nameof(Field), nameof(Cond), nameof(Act) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    private static List<ColoredLine> ScriptLines(ScriptList list)
    {
        var lines = new List<ColoredLine>();
        foreach (var r in list.Records)
        {
            bool un = !r.Named;
            bool inf = r.Named && (r.IsCondition ? Names.InferredCondition(r.Type) : Names.InferredAction(r.Type));
            lines.Add(new ColoredLine((r.IsCondition ? "⚑ " : "⚡ ") + Gui.Describe(r), un, inf));
        }
        return lines;
    }
}

// ---------------------------------------------------------------------------
// 编辑器控件
// ---------------------------------------------------------------------------

/// <summary>CNV 三栏表格编辑器：`节点 | 属性 | 本地化文本 | 条件 | 动作`。</summary>
internal sealed class CnvTableEditor : UserControl
{
    private DocSession _doc = null!;
    private CnvFile _cnv = null!;
    private Changed _changed = null!;
    private Action? _locRequested;
    private bool _comfortable;

    private readonly DataGrid _grid = MkGrid();
    private List<CnvRow> _rows = new();
    private readonly Button _modeBtn;

    public event Action? LocRequested { add => _locRequested += value; remove => _locRequested -= value; }

    public CnvTableEditor()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 4, 0, 4) };
        _modeBtn = MkBtn(L.T("紧凑模式", "Compact Mode"), ToggleMode);
        bar.Children.Add(MkBtn(L.T("＋添加子节点", "+ Add Child"), AddChild));
        bar.Children.Add(MkBtn(L.T("－删除子树", "- Delete"), DeleteStep));
        bar.Children.Add(MkBtn("↑" + L.T("上移", "Up"), () => MoveStep(-1)));
        bar.Children.Add(MkBtn("↓" + L.T("下移", "Down"), () => MoveStep(1)));
        bar.Children.Add(MkBtn(L.T("本地化表", "Loc Table"), () => _locRequested?.Invoke()));
        bar.Children.Add(_modeBtn);

        BuildColumns();
        _grid.MouseDoubleClick += OnDoubleClick;

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_grid);
        Content = root;
    }

    private static DataGrid MkGrid() => new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        CanUserSortColumns = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
    };

    private static Button MkBtn(string text, Action onClick)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 2, 10, 2) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void BuildColumns()
    {
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = L.T("节点", "Node"),
            Binding = new Binding(nameof(CnvRow.Node)),
            IsReadOnly = true,
            Width = new DataGridLength(230),
            ElementStyle = MkTextBlockStyle(),
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = L.T("属性", "Attrs"),
            Binding = new Binding(nameof(CnvRow.Attrs)),
            IsReadOnly = true,
            Width = new DataGridLength(150),
            ElementStyle = MkTextBlockStyle(foreground: WpfTheme.MutedBrush),
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = L.T("本地化文本", "Localized Text"),
            Binding = new Binding(nameof(CnvRow.Text)) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _grid.Columns.Add(MkScriptColumn(L.T("条件", "Conditions"), nameof(CnvRow.Cond), new DataGridLength(280)));
        _grid.Columns.Add(MkScriptColumn(L.T("动作", "Actions"), nameof(CnvRow.Act), new DataGridLength(280)));
        ApplyTextColumnStyle();
    }

    private static Style MkTextBlockStyle(Brush? foreground = null)
    {
        var s = new Style(typeof(TextBlock));
        if (foreground != null) s.Setters.Add(new Setter(TextBlock.ForegroundProperty, foreground));
        return s;
    }

    /// <summary>文本列样式：换行；紧凑模式封顶 3 行，选中行完整显示。</summary>
    private void ApplyTextColumnStyle()
    {
        var col = (DataGridTextColumn)_grid.Columns[2];
        var s = new Style(typeof(TextBlock));
        s.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        s.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Top));
        if (!_comfortable)
        {
            s.Setters.Add(new Setter(TextBlock.MaxHeightProperty, 54.0));
            var trig = new DataTrigger
            {
                Binding = new Binding("IsSelected")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1),
                },
                Value = true,
            };
            trig.Setters.Add(new Setter(TextBlock.MaxHeightProperty, 10000.0));
            s.Triggers.Add(trig);
        }
        col.ElementStyle = s;

        var es = new Style(typeof(TextBox));
        es.Setters.Add(new Setter(TextBox.AcceptsReturnProperty, true));
        es.Setters.Add(new Setter(TextBox.TextWrappingProperty, TextWrapping.Wrap));
        col.EditingElementStyle = es;
    }

    private void ToggleMode()
    {
        _comfortable = !_comfortable;
        _modeBtn.Content = _comfortable ? L.T("平铺模式", "Flat Mode") : L.T("紧凑模式", "Compact Mode");
        ApplyTextColumnStyle();
    }

    /// <summary>条件/动作列：逐记录一行的模板列。</summary>
    private static DataGridTemplateColumn MkScriptColumn(string header, string propertyName, DataGridLength width)
    {
        var itemTemplate = new DataTemplate();
        var tb = new FrameworkElementFactory(typeof(TextBlock));
        tb.SetBinding(TextBlock.TextProperty, new Binding(nameof(ColoredLine.Text)));
        tb.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ColoredLine.Brush)));
        itemTemplate.VisualTree = tb;

        var itemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));

        var template = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(ItemsControl));
        factory.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(propertyName));
        factory.SetValue(ItemsControl.ItemTemplateProperty, itemTemplate);
        factory.SetValue(ItemsControl.ItemsPanelProperty, itemsPanel);
        template.VisualTree = factory;

        return new DataGridTemplateColumn
        {
            Header = header,
            Width = width,
            IsReadOnly = true,
            CellTemplate = template,
        };
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (cell == null || row == null) return;
        int colIndex = _grid.Columns.IndexOf(cell.Column);
        int rowIndex = row.GetIndex();
        if (rowIndex < 0 || rowIndex >= _rows.Count) return;
        var s = _rows[rowIndex].Step;
        if (colIndex == 3)
        {
            if (ScriptListDialog.Show(_doc, s.Conditions, true, Window.GetWindow(this))) RefreshRow(rowIndex);
        }
        else if (colIndex == 4)
        {
            if (ScriptListDialog.Show(_doc, s.Actions, false, Window.GetWindow(this))) RefreshRow(rowIndex);
        }
        else if (colIndex is 0 or 1)
        {
            if (CnvNodeDialog.Show(_doc, s, Window.GetWindow(this))) RefreshRow(rowIndex);
        }
    }

    private void RefreshRow(int i)
    {
        _rows[i].Reload();
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
        _rows = _cnv.Steps.Select(s => new CnvRow(this, doc, s)).ToList();
        _grid.ItemsSource = _rows;
        if (_rows.Count > 0) _grid.SelectedIndex = 0;
    }

    /// <summary>结构树定位：选中对应表格行。</summary>
    public void SelectStep(CnvStep s)
    {
        int i = _cnv.Steps.IndexOf(s);
        if (i < 0 || i >= _rows.Count) return;
        _grid.SelectedIndex = i;
        _grid.ScrollIntoView(_rows[i]);
    }

    private CnvStep? SelectedStep => _grid.SelectedIndex >= 0 && _grid.SelectedIndex < _rows.Count ? _rows[_grid.SelectedIndex].Step : null;

    private void AddChild()
    {
        var anchor = SelectedStep;
        if (anchor == null) return;
        var dlg = new CnvTypePicker { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
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
            Gui.Warn(Window.GetWindow(this), L.T("不能删除根节点。", "The root node cannot be deleted."), L.T("删除", "Delete"));
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

/// <summary>QST 三栏表格编辑器：`节点 | 字段 | 条件 | 动作`（task/objective/event 平铺）。</summary>
internal sealed class QstTableEditor : UserControl
{
    private DocSession _doc = null!;
    private QstFile _qst = null!;
    private Changed _changed = null!;
    private Action? _locRequested;

    private readonly DataGrid _grid = MkGrid();
    private List<QstRow> _rows = new();

    public event Action? LocRequested { add => _locRequested += value; remove => _locRequested -= value; }

    public QstTableEditor()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 4, 0, 4) };
        bar.Children.Add(MkBtn(L.T("＋任务"), AddTask));
        bar.Children.Add(MkBtn(L.T("＋目标"), AddObjective));
        bar.Children.Add(MkBtn(L.T("＋完成事件"), AddEvent));
        bar.Children.Add(MkBtn(L.T("＋接受事件"), AddIncentive));
        bar.Children.Add(MkBtn(L.T("－删除", "- Delete"), DeleteRow));
        bar.Children.Add(MkBtn(L.T("任务属性", "Quest Props"), EditQuestProps));
        bar.Children.Add(MkBtn(L.T("本地化表", "Loc Table"), () => _locRequested?.Invoke()));

        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = L.T("节点", "Node"),
            Binding = new Binding(nameof(QstRow.Node)),
            IsReadOnly = true,
            Width = new DataGridLength(220),
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = L.T("字段", "Fields"),
            Binding = new Binding(nameof(QstRow.Field)),
            IsReadOnly = true,
            Width = new DataGridLength(170),
        });
        _grid.Columns.Add(CnvTableEditorMkScriptColumn(L.T("条件", "Conditions"), nameof(QstRow.Cond), new DataGridLength(300)));
        _grid.Columns.Add(CnvTableEditorMkScriptColumn(L.T("动作", "Actions"), nameof(QstRow.Act), new DataGridLength(300)));

        _grid.MouseDoubleClick += OnDoubleClick;

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_grid);
        Content = root;
    }

    private static DataGrid MkGrid() => new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        CanUserSortColumns = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
    };

    private static Button MkBtn(string text, Action onClick)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 2, 10, 2) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static DataGridTemplateColumn CnvTableEditorMkScriptColumn(string header, string propertyName, DataGridLength width)
    {
        var itemTemplate = new DataTemplate();
        var tb = new FrameworkElementFactory(typeof(TextBlock));
        tb.SetBinding(TextBlock.TextProperty, new Binding(nameof(ColoredLine.Text)));
        tb.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ColoredLine.Brush)));
        itemTemplate.VisualTree = tb;

        var template = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(ItemsControl));
        factory.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(propertyName));
        factory.SetValue(ItemsControl.ItemTemplateProperty, itemTemplate);
        factory.SetValue(ItemsControl.ItemsPanelProperty, new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel))));
        template.VisualTree = factory;

        return new DataGridTemplateColumn { Header = header, Width = width, IsReadOnly = true, CellTemplate = template };
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }

    /// <summary>编辑 quest 头部：uid 与 Primary/Secondary/Repeatable（官方 Common.Quest+Flag）。</summary>
    private void EditQuestProps()
    {
        if (!QuestPropsDialog.Show(_qst, Window.GetWindow(this))) return;
        _changed();
        Bind(_doc, _changed);
    }

    public void Bind(DocSession doc, Changed changed)
    {
        _doc = doc;
        _qst = doc.Qst!;
        _changed = changed;
        var rows = new List<QstRow>();
        int no = 1;
        foreach (var t in _qst.Tasks)
        {
            rows.Add(new QstRow(new QstTaskRow(t, no++)));
            foreach (var inc in t.Incentives) rows.Add(new QstRow(new QstEventRow(inc, L.T("接受事件"))));
            foreach (var o in t.Objectives) rows.Add(new QstRow(new QstObjectiveRow(o)));
            foreach (var e in t.Events) rows.Add(new QstRow(new QstEventRow(e, L.T("完成事件"))));
        }
        _rows = rows;
        _grid.ItemsSource = _rows;
        if (_rows.Count > 0) _grid.SelectedIndex = 0;
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (cell == null || row == null) return;
        int colIndex = _grid.Columns.IndexOf(cell.Column);
        int rowIndex = row.GetIndex();
        if (rowIndex < 0 || rowIndex >= _rows.Count) return;
        var r = _rows[rowIndex].Owner;
        if (colIndex == 2 && r.Conditions != null)
        {
            if (ScriptListDialog.Show(_doc, r.Conditions, true, Window.GetWindow(this))) _rows[rowIndex].Reload();
        }
        else if (colIndex == 3 && r.Actions != null)
        {
            if (ScriptListDialog.Show(_doc, r.Actions, false, Window.GetWindow(this))) _rows[rowIndex].Reload();
        }
        else if (colIndex is 0 or 1)
        {
            if (QstFieldsDialog.Show(r, Window.GetWindow(this))) _rows[rowIndex].Reload();
        }
    }

    private QstRow? SelectedRow => _grid.SelectedIndex >= 0 && _grid.SelectedIndex < _rows.Count ? _rows[_grid.SelectedIndex] : null;

    private QstTask ActiveTask()
    {
        var sel = SelectedRow;
        if (sel == null) return _qst.Tasks.FirstOrDefault()!;
        return sel.Owner switch
        {
            QstTaskRow tr => tr.T,
            QstEventRow er => TaskOf(er.E),
            QstObjectiveRow or => TaskOf(or.O),
            _ => _qst.Tasks.FirstOrDefault()!,
        };
    }

    private QstTask TaskOf(QstEvent e) => _qst.Tasks.First(t => t.Incentives.Contains(e) || t.Events.Contains(e));
    private QstTask TaskOf(QstObjective o) => _qst.Tasks.First(t => t.Objectives.Contains(o));

    /// <summary>结构树定位：选中对应表格行。</summary>
    public void SelectOwner(object owner)
    {
        int i = _rows.FindIndex(r => ReferenceEquals(r.Owner.OwnerObject, owner));
        if (i < 0) return;
        _grid.SelectedIndex = i;
        _grid.ScrollIntoView(_rows[i]);
    }

    // 结构改动统一走 QstFile 的增删方法：它们会按公式 `2+2T+O+I+E` 重建本地化表。
    private void AddTask()
    {
        _qst.AddTask();
        _changed();
        Bind(_doc, _changed);
    }

    private void AddObjective()
    {
        var t = ActiveTask();
        if (t == null) return;
        _qst.AddObjective(t);
        _changed();
        Bind(_doc, _changed);
    }

    private void AddEvent()
    {
        var t = ActiveTask();
        if (t == null) return;
        _qst.AddEvent(t, incentive: false);
        _changed();
        Bind(_doc, _changed);
    }

    private void AddIncentive()
    {
        var t = ActiveTask();
        if (t == null) return;
        _qst.AddEvent(t, incentive: true);
        _changed();
        Bind(_doc, _changed);
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
                if (!_qst.RemoveEvent(TaskOf(er.E), er.E)) return;
                break;
            case QstObjectiveRow or:
                if (!_qst.RemoveObjective(TaskOf(or.O), or.O)) return;
                break;
        }
        _changed();
        Bind(_doc, _changed);
    }
}
