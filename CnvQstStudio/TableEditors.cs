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

/// <summary>CNV 节点的界面名（树形表格第一列：类型徽标 + 文本摘要，层次由缩进表达）。</summary>
internal static class CnvNodeLabel
{
    /// <summary>节点类型的界面名（中文基准，走语言包）。</summary>
    public static string KindZh(uint type) => L.T(type switch
    {
        0 => "根", 1 => "台词", 2 => "继续选项", 3 => "接受选项", 4 => "拒绝选项",
        5 => "通用", 6 => "结束对话", 7 => "跳转", 8 => "随机选项", _ => "未知类型",
    });

    /// <summary>树形表用：类型名 + 文本摘要（无缩进——层级由行的缩进表达）。</summary>
    public static string TreeLabel(CnvFile c, CnvStep s)
    {
        string label = KindZh(s.Type);
        string text = c.TextFor(s).Replace("\n", " ");
        if (text.Length > 0)
        {
            if (text.Length > 30) text = text[..30] + "…";
            label += $"：{text}";
        }
        if (s.HasLink) label += L.T("（跳转）");
        return label;
    }
}

// ---------------------------------------------------------------------------
// 行模型（树形表格：父子 + 展开态；INPC 用于编辑后单行刷新）
// ---------------------------------------------------------------------------

internal sealed class CnvRow : INotifyPropertyChanged
{
    private readonly CnvTableEditor _editor;
    public DocSession Doc { get; }
    public CnvStep Step { get; }

    public CnvRow? Parent { get; private set; }
    public List<CnvRow> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;
    public bool IsExpanded { get; set; } = true;
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";
    public double Indent => 2 + Depth * 18;

    public string Node { get; private set; } = "";
    public string Attrs { get; private set; } = "";
    public List<ColoredLine> Cond { get; private set; } = new();
    public List<ColoredLine> Act { get; private set; } = new();

    private int Depth { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CnvRow(CnvTableEditor editor, DocSession doc, CnvStep s)
    {
        _editor = editor;
        Doc = doc;
        Step = s;
        Depth = (int)s.Depth;
        Reload();
    }

    public void Attach(CnvRow parent)
    {
        Parent = parent;
        parent.Children.Add(this);
        Depth = parent.Depth + 1;
    }

    public void RaiseGlyph() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExpandGlyph)));

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
        Node = CnvNodeLabel.TreeLabel(Doc.Cnv!, Step);
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

/// <summary>task 行所有者：携带真实 QstTask 引用（删除/定位用），标题已格式化。</summary>
internal sealed class QstTaskRow(QstTask t, string nodeLabel) : QstRowOwner
{
    public QstTask T => t;
    public override object OwnerObject => t;
    public override string NodeLabel => nodeLabel;
    public override string FieldLabel
    {
        get
        {
            var p = new List<string>();
            if (t.Flag8) p.Add(L.T("阻断后续任务"));
            if (t.Flag9) p.Add(L.T("不向上传递"));
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
    private readonly QstTableEditor _editor;
    public QstRowOwner Owner { get; }

    public QstRow? Parent { get; private set; }
    public List<QstRow> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;
    public bool IsExpanded { get; set; } = true;
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";
    public double Indent => 2 + Depth * 18;

    public string Node { get; private set; }
    public string Field { get; private set; }
    public List<ColoredLine> Cond { get; private set; } = new();
    public List<ColoredLine> Act { get; private set; } = new();

    private int Depth { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public QstRow(QstTableEditor editor, QstRowOwner owner)
    {
        _editor = editor;
        Owner = owner;
        Node = owner.NodeLabel;
        Field = owner.FieldLabel;
        Reload();
    }

    public void Attach(QstRow parent)
    {
        Parent = parent;
        parent.Children.Add(this);
        Depth = parent.Depth + 1;
    }

    public void RaiseGlyph() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExpandGlyph)));

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
// 编辑器控件（树形表格 + 右键菜单；无常驻工具条）
// ---------------------------------------------------------------------------

/// <summary>CNV 树形表格：`节点(树) | 属性 | 本地化文本 | 条件 | 动作`。</summary>
internal sealed class CnvTableEditor : UserControl
{
    private DocSession _doc = null!;
    private CnvFile _cnv = null!;
    private Changed _changed = null!;
    private Action? _locRequested;
    private bool _comfortable;

    private readonly DataGrid _grid = MkGrid();
    private List<CnvRow> _rows = new();          // 全部行（含折叠的）
    private List<CnvRow> _visible = new();       // 当前可见行

    public event Action? LocRequested { add => _locRequested += value; remove => _locRequested -= value; }

    public CnvTableEditor()
    {
        _grid.Columns.Add(MkTreeColumn(L.T("节点", "Node"), new DataGridLength(330)));
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = L.T("属性", "Attrs"),
            Binding = new Binding(nameof(CnvRow.Attrs)),
            IsReadOnly = true,
            Width = new DataGridLength(150),
            ElementStyle = MkTextBlockStyle(WpfTheme.MutedBrush),
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

        _grid.MouseDoubleClick += OnDoubleClick;
        _grid.PreviewMouseRightButtonDown += GridRightDown;
        _grid.ContextMenu = BuildContextMenu();

        Content = _grid;
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
        ApplyTextColumnStyle();
    }

    /// <summary>第一列：缩进 + 展开/折叠钮 + 节点名（树形）。</summary>
    private DataGridTemplateColumn MkTreeColumn(string header, DataGridLength width)
    {
        var template = new DataTemplate();
        var sp = new FrameworkElementFactory(typeof(StackPanel));
        sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var spacer = new FrameworkElementFactory(typeof(Border));
        spacer.SetBinding(Border.WidthProperty, new Binding(nameof(CnvRow.Indent)));
        sp.AppendChild(spacer);

        var toggle = new FrameworkElementFactory(typeof(System.Windows.Controls.Button));
        toggle.SetValue(System.Windows.Controls.Button.ContentProperty, new Binding(nameof(CnvRow.ExpandGlyph)));
        toggle.SetValue(System.Windows.Controls.Button.BackgroundProperty, Brushes.Transparent);
        toggle.SetValue(System.Windows.Controls.Button.BorderThicknessProperty, new Thickness(0));
        toggle.SetValue(FrameworkElement.WidthProperty, 20.0);
        toggle.SetValue(FrameworkElement.HeightProperty, 18.0);
        toggle.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        toggle.SetValue(Control.FontSizeProperty, 10.0);
        toggle.SetValue(Control.ForegroundProperty, WpfTheme.MutedBrush);
        toggle.SetValue(FrameworkElement.VisibilityProperty, new Binding(nameof(CnvRow.HasChildren))
        {
            Converter = new System.Windows.Controls.BooleanToVisibilityConverter(),
        });
        toggle.AddHandler(System.Windows.Controls.Button.ClickEvent, new RoutedEventHandler(OnToggleClick));
        sp.AppendChild(toggle);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(CnvRow.Node)));
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.MarginProperty, new Thickness(4, 0, 0, 0));
        sp.AppendChild(text);

        template.VisualTree = sp;
        return new DataGridTemplateColumn { Header = header, Width = width, IsReadOnly = true, CellTemplate = template };
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is CnvRow r)
        {
            r.IsExpanded = !r.IsExpanded;
            r.RaiseGlyph();
            RebuildVisible(keepSelection: true);
        }
    }

    /// <summary>条件/动作列：逐记录一行的模板列。</summary>
    private static DataGridTemplateColumn MkScriptColumn(string header, string propertyName, DataGridLength width)
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

    private static MenuItem Mi(string header, Action onClick) =>
        new() { Header = header, Command = new RelayCommand(onClick) };

    private ContextMenu BuildContextMenu()
    {
        var cm = new ContextMenu();
        cm.Items.Add(Mi(L.T("＋添加子节点", "+ Add Child"), AddChild));
        cm.Items.Add(Mi(L.T("－删除子树", "- Delete"), DeleteStep));
        cm.Items.Add(new Separator());
        cm.Items.Add(Mi("↑ " + L.T("上移", "Up"), () => MoveStep(-1)));
        cm.Items.Add(Mi("↓ " + L.T("下移", "Down"), () => MoveStep(1)));
        cm.Items.Add(new Separator());
        cm.Items.Add(Mi(L.T("节点属性", "Node Properties"), EditNodeProps));
        cm.Items.Add(Mi(L.LocStringTable, () => _locRequested?.Invoke()));
        cm.Items.Add(new Separator());
        cm.Items.Add(Mi(_comfortable ? L.T("紧凑模式", "Compact Mode") : L.T("平铺模式", "Flat Mode"), ToggleMode));
        return cm;
    }

    private void GridRightDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row != null && row.GetIndex() >= 0) _grid.SelectedIndex = row.GetIndex();
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (cell == null || row == null) return;
        int colIndex = _grid.Columns.IndexOf(cell.Column);
        int rowIndex = row.GetIndex();
        if (rowIndex < 0 || rowIndex >= _visible.Count) return;
        var s = _visible[rowIndex].Step;
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
            EditNodeProps();
        }
    }

    private void EditNodeProps()
    {
        var s = SelectedStep;
        if (s == null) return;
        if (CnvNodeDialog.Show(_doc, s, Window.GetWindow(this)))
        {
            RefreshSelected();
            _changed();
        }
    }

    private void RefreshRow(int i)
    {
        if (i >= 0 && i < _visible.Count) _visible[i].Reload();
    }

    private void RefreshSelected()
    {
        if (_grid.SelectedItem is CnvRow r) r.Reload();
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

        // 构建全部行并挂父子关系
        var byStep = new Dictionary<CnvStep, CnvRow>();
        _rows = _cnv.Steps.Select(s => new CnvRow(this, doc, s)).ToList();
        foreach (var row in _rows) byStep[row.Step] = row;
        foreach (var row in _rows)
        {
            foreach (var childStep in row.Step.Children)
                if (byStep.TryGetValue(childStep, out var child)) child.Attach(row);
        }

        _visible = ComputeVisible();
        _grid.ItemsSource = _visible;
        if (_visible.Count > 0) _grid.SelectedIndex = 0;
    }

    private List<CnvRow> ComputeVisible()
    {
        var list = new List<CnvRow>();
        void Walk(CnvRow r)
        {
            list.Add(r);
            if (r.IsExpanded) foreach (var c in r.Children) Walk(c);
        }
        foreach (var root in _rows.Where(r => r.Parent == null)) Walk(root);
        return list;
    }

    private void RebuildVisible(bool keepSelection)
    {
        CnvRow? sel = keepSelection ? _grid.SelectedItem as CnvRow : null;
        _visible = ComputeVisible();
        _grid.ItemsSource = _visible;
        if (sel != null && _visible.Contains(sel))
        {
            _grid.SelectedItem = sel;
            _grid.ScrollIntoView(sel);
        }
        else if (_visible.Count > 0)
        {
            _grid.SelectedIndex = 0;
        }
    }

    /// <summary>定位：展开父链并选中对应行。</summary>
    public void SelectStep(CnvStep s)
    {
        var byStep = _rows.FirstOrDefault(r => ReferenceEquals(r.Step, s));
        if (byStep == null) return;
        for (var p = byStep.Parent; p != null; p = p.Parent)
        {
            if (!p.IsExpanded) { p.IsExpanded = true; p.RaiseGlyph(); }
        }
        RebuildVisible(keepSelection: false);
        if (_visible.Contains(byStep))
        {
            _grid.SelectedItem = byStep;
            _grid.ScrollIntoView(byStep);
        }
    }

    private CnvStep? SelectedStep => _grid.SelectedItem is CnvRow r ? r.Step : null;

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

/// <summary>QST 树形表格：`节点(树) | 字段 | 条件 | 动作`（task 下挂 接受事件/目标/完成事件）。</summary>
internal sealed class QstTableEditor : UserControl
{
    private DocSession _doc = null!;
    private QstFile _qst = null!;
    private Changed _changed = null!;
    private Action? _locRequested;

    private readonly DataGrid _grid = MkGrid();
    private List<QstRow> _rows = new();
    private List<QstRow> _visible = new();

    public event Action? LocRequested { add => _locRequested += value; remove => _locRequested -= value; }

    public QstTableEditor()
    {
        _grid.Columns.Add(MkTreeColumn(L.T("节点", "Node"), new DataGridLength(300)));
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = L.T("字段", "Fields"),
            Binding = new Binding(nameof(QstRow.Field)),
            IsReadOnly = true,
            Width = new DataGridLength(170),
        });
        _grid.Columns.Add(MkScriptColumn(L.T("条件", "Conditions"), nameof(QstRow.Cond), new DataGridLength(300)));
        _grid.Columns.Add(MkScriptColumn(L.T("动作", "Actions"), nameof(QstRow.Act), new DataGridLength(300)));

        _grid.MouseDoubleClick += OnDoubleClick;
        _grid.PreviewMouseRightButtonDown += GridRightDown;
        _grid.ContextMenu = BuildContextMenu();

        Content = _grid;
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

    private static MenuItem Mi(string header, Action onClick) =>
        new() { Header = header, Command = new RelayCommand(onClick) };

    private ContextMenu BuildContextMenu()
    {
        var cm = new ContextMenu();
        cm.Items.Add(Mi(L.T("＋任务"), AddTask));
        cm.Items.Add(Mi(L.T("＋目标"), AddObjective));
        cm.Items.Add(Mi(L.T("＋完成事件"), AddEvent));
        cm.Items.Add(Mi(L.T("＋接受事件"), AddIncentive));
        cm.Items.Add(new Separator());
        cm.Items.Add(Mi(L.T("－删除所选", "- Delete"), DeleteRow));
        cm.Items.Add(new Separator());
        cm.Items.Add(Mi(L.T("任务属性", "Quest Props"), EditQuestProps));
        cm.Items.Add(Mi(L.LocStringTable, () => _locRequested?.Invoke()));
        return cm;
    }

    private void GridRightDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row != null && row.GetIndex() >= 0) _grid.SelectedIndex = row.GetIndex();
    }

    private DataGridTemplateColumn MkTreeColumn(string header, DataGridLength width)
    {
        var template = new DataTemplate();
        var sp = new FrameworkElementFactory(typeof(StackPanel));
        sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var spacer = new FrameworkElementFactory(typeof(Border));
        spacer.SetBinding(Border.WidthProperty, new Binding(nameof(QstRow.Indent)));
        sp.AppendChild(spacer);

        var toggle = new FrameworkElementFactory(typeof(System.Windows.Controls.Button));
        toggle.SetValue(System.Windows.Controls.Button.ContentProperty, new Binding(nameof(QstRow.ExpandGlyph)));
        toggle.SetValue(System.Windows.Controls.Button.BackgroundProperty, Brushes.Transparent);
        toggle.SetValue(System.Windows.Controls.Button.BorderThicknessProperty, new Thickness(0));
        toggle.SetValue(FrameworkElement.WidthProperty, 20.0);
        toggle.SetValue(FrameworkElement.HeightProperty, 18.0);
        toggle.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        toggle.SetValue(Control.FontSizeProperty, 10.0);
        toggle.SetValue(Control.ForegroundProperty, WpfTheme.MutedBrush);
        toggle.SetValue(FrameworkElement.VisibilityProperty, new Binding(nameof(QstRow.HasChildren))
        {
            Converter = new System.Windows.Controls.BooleanToVisibilityConverter(),
        });
        toggle.AddHandler(System.Windows.Controls.Button.ClickEvent, new RoutedEventHandler(OnToggleClick));
        sp.AppendChild(toggle);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(QstRow.Node)));
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.MarginProperty, new Thickness(4, 0, 0, 0));
        sp.AppendChild(text);

        template.VisualTree = sp;
        return new DataGridTemplateColumn { Header = header, Width = width, IsReadOnly = true, CellTemplate = template };
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is QstRow r)
        {
            r.IsExpanded = !r.IsExpanded;
            r.RaiseGlyph();
            RebuildVisible(keepSelection: true);
        }
    }

    private static DataGridTemplateColumn MkScriptColumn(string header, string propertyName, DataGridLength width)
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
            string title = _qst.Languages.Count > 0 && _qst.TaskTitleSlot(no - 1) < _qst.Languages[0].Texts.Count
                ? _qst.Languages[0].Texts[_qst.TaskTitleSlot(no - 1)].Text : "";
            string nodeLabel = title.Length > 0
                ? L.F("任务 {0}", no) + "：" + (title.Length > 20 ? title[..20] + "…" : title)
                : L.F("任务 {0}", no);
            var taskRow = new QstRow(this, new QstTaskRow(t, nodeLabel));
            rows.Add(taskRow);
            foreach (var inc in t.Incentives) rows.Add(new QstRow(this, new QstEventRow(inc, L.T("接受事件"))));
            foreach (var o in t.Objectives) rows.Add(new QstRow(this, new QstObjectiveRow(o)));
            foreach (var e in t.Events) rows.Add(new QstRow(this, new QstEventRow(e, L.T("完成事件"))));
            // 挂父子：task 行的直属子行
            int idx = rows.IndexOf(taskRow);
            int childCount = t.Incentives.Count + t.Objectives.Count + t.Events.Count;
            for (int k = 1; k <= childCount; k++) rows[idx + k].Attach(taskRow);
            no++;
        }
        _rows = rows;
        _visible = ComputeVisible();
        _grid.ItemsSource = _visible;
        if (_visible.Count > 0) _grid.SelectedIndex = 0;
    }

    private List<QstRow> ComputeVisible()
    {
        var list = new List<QstRow>();
        void Walk(QstRow r)
        {
            list.Add(r);
            if (r.IsExpanded) foreach (var c in r.Children) Walk(c);
        }
        foreach (var root in _rows.Where(r => r.Parent == null)) Walk(root);
        return list;
    }

    private void RebuildVisible(bool keepSelection)
    {
        QstRow? sel = keepSelection ? _grid.SelectedItem as QstRow : null;
        _visible = ComputeVisible();
        _grid.ItemsSource = _visible;
        if (sel != null && _visible.Contains(sel))
        {
            _grid.SelectedItem = sel;
            _grid.ScrollIntoView(sel);
        }
        else if (_visible.Count > 0)
        {
            _grid.SelectedIndex = 0;
        }
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (cell == null || row == null) return;
        int colIndex = _grid.Columns.IndexOf(cell.Column);
        int rowIndex = row.GetIndex();
        if (rowIndex < 0 || rowIndex >= _visible.Count) return;
        var r = _visible[rowIndex].Owner;
        if (colIndex == 2 && r.Conditions != null)
        {
            if (ScriptListDialog.Show(_doc, r.Conditions, true, Window.GetWindow(this))) _visible[rowIndex].Reload();
        }
        else if (colIndex == 3 && r.Actions != null)
        {
            if (ScriptListDialog.Show(_doc, r.Actions, false, Window.GetWindow(this))) _visible[rowIndex].Reload();
        }
        else if (colIndex is 0 or 1)
        {
            EditFields();
        }
    }

    private void EditFields()
    {
        var sel = SelectedRow;
        if (sel == null) return;
        if (QstFieldsDialog.Show(sel.Owner, Window.GetWindow(this)))
        {
            sel.Reload();
            _changed();
        }
    }

    private QstRow? SelectedRow => _grid.SelectedItem is QstRow r ? r : null;

    private QstTask ActiveTask()
    {
        var sel = SelectedRow;
        if (sel == null) return _qst.Tasks.FirstOrDefault()!;
        if (sel.Owner is QstTaskRow tr) return tr.T;
        if (sel.Owner is QstEventRow er) return TaskOf(er.E);
        if (sel.Owner is QstObjectiveRow or) return TaskOf(or.O);
        return _qst.Tasks.FirstOrDefault()!;
    }

    private QstTask TaskOf(QstEvent e) => _qst.Tasks.First(t => t.Incentives.Contains(e) || t.Events.Contains(e));
    private QstTask TaskOf(QstObjective o) => _qst.Tasks.First(t => t.Objectives.Contains(o));

    /// <summary>定位：展开父链并选中对应行。</summary>
    public void SelectOwner(object owner)
    {
        var row = _rows.FirstOrDefault(r => ReferenceEquals(r.Owner.OwnerObject, owner));
        if (row == null) return;
        for (var p = row.Parent; p != null; p = p.Parent)
        {
            if (!p.IsExpanded) { p.IsExpanded = true; p.RaiseGlyph(); }
        }
        RebuildVisible(keepSelection: false);
        if (_visible.Contains(row))
        {
            _grid.SelectedItem = row;
            _grid.ScrollIntoView(row);
        }
    }

    // 结构改动统一走 QstFile 的增删方法：它们会按公式 2+2T+O+I+E 重建本地化表。
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
