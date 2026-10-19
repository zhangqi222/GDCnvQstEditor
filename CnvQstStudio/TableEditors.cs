using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using CnvQst;
using MaterialDesignThemes.Wpf;

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

/// <summary>CNV 节点的界面名（树型列表第一列：类型徽标 + 文本摘要）。</summary>
internal static class CnvNodeLabel
{
    /// <summary>节点类型的界面名（中文基准，走语言包），带官方风格的图标前缀。</summary>
    public static string KindZh(uint type) => L.T(type switch
    {
        0 => "🏁 根", 1 => "💬 台词", 2 => "➡ 继续选项", 3 => "✅ 接受选项", 4 => "❌ 拒绝选项",
        5 => "🔹 通用", 6 => "🛑 结束对话", 7 => "↪ 跳转", 8 => "🎲 随机选项", _ => "❓ 未知类型",
    });

    /// <summary>树型表用：类型名 + 文本摘要（无缩进——层级由控件的层级缩进表达）。</summary>
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
// 行模型（INPC：编辑后单行刷新；IsExpanded 与 TreeListViewItem 容器双向绑定）
// ---------------------------------------------------------------------------

internal sealed class CnvRow : INotifyPropertyChanged, WpfTheme.HighlightableRow
{
    private readonly CnvTableEditor _editor;
    private bool _highlight;
    public DocSession Doc { get; }
    public CnvStep Step { get; }

    public CnvRow? Parent { get; private set; }
    public List<CnvRow> Children { get; } = new();
    public bool IsExpanded { get; set; } = true;

    public bool Highlight
    {
        get => _highlight;
        set
        {
            if (_highlight == value) return;
            _highlight = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Highlight)));
        }
    }

    public string Node { get; private set; } = "";
    public Brush NodeBrush => WpfTheme.TextBrush;
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

    public void Attach(CnvRow parent)
    {
        Parent = parent;
        parent.Children.Add(this);
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

/// <summary>QST 行所有者（层级对齐官方 QuestEditor：组名根 → 任务名 → task → 组 → 事件/目标）。</summary>
internal abstract class QstRowOwner
{
    public abstract object OwnerObject { get; }
    public abstract string NodeLabel { get; }
    public abstract bool HasScript { get; }
    public abstract ScriptList? Conditions { get; }
    public abstract ScriptList? Actions { get; }
    public virtual bool CanDelete => false;
    public virtual string SubText => "";
    public virtual Brush NodeBrush => WpfTheme.TextBrush;
    /// <summary>Objective ID 列（quest id / task uid / objective uid；事件无 uid 则显示 flags）。</summary>
    public virtual string IdText => "";
    /// <summary>本行在本地化串表中的文本槽位（null = 该行无对应文本）。</summary>
    public virtual int? TextSlot => null;

    protected static string SlotText(QstFile q, int slot)
    {
        var lang = q.Languages.Count > 0 ? q.Languages[0] : null;
        return lang != null && slot >= 0 && slot < lang.Texts.Count ? lang.Texts[slot].Text : "";
    }
}

/// <summary>根行：任务组名（官方红色圆点图标；文本=串表槽 0）。</summary>
internal sealed class QstRootOwner(QstFile q) : QstRowOwner
{
    public override object OwnerObject => this;
    public override string NodeLabel => "🔴 " + (SlotText(q, 0).Length > 0 ? SlotText(q, 0) : L.T("（任务组）"));
    public override bool HasScript => false;
    public override ScriptList? Conditions => null;
    public override ScriptList? Actions => null;
    public override int? TextSlot => 0;
    public override Brush NodeBrush => WpfTheme.DangerBrush;
}

/// <summary>任务名行（官方黄旗；文本=串表槽 1；双击编辑 quest 头部）。</summary>
internal sealed class QstQuestOwner(QstFile q) : QstRowOwner
{
    public QstFile Q => q;
    public override object OwnerObject => q;
    public override string NodeLabel => "🚩 " + (SlotText(q, 1).Length > 0 ? SlotText(q, 1) : L.T("（任务名）"));
    public override bool HasScript => false;
    public override ScriptList? Conditions => null;
    public override ScriptList? Actions => null;
    public override string IdText => q.Id.ToString();
    public override int? TextSlot => 1;
    public override Brush NodeBrush => WpfTheme.AccentBrush;
}

/// <summary>OnAccept / Objectives / OnComplete 组行（仅折叠分组，无脚本内容）。</summary>
internal sealed class QstGroupOwner(string label, Brush brush) : QstRowOwner
{
    public override object OwnerObject => this;
    public override string NodeLabel => label;
    public override bool HasScript => false;
    public override ScriptList? Conditions => null;
    public override ScriptList? Actions => null;
    public override Brush NodeBrush => brush;
}

/// <summary>task 行：图标 + [N] 标题（串表标题槽），副文本为描述。</summary>
internal sealed class QstTaskRow(QstFile q, QstTask t, int taskIndex) : QstRowOwner
{
    public QstTask T => t;
    public int TaskIndex => taskIndex;

    public override object OwnerObject => t;
    public override string NodeLabel => "📋 [" + (taskIndex + 1) + "] " + (Label(q, taskIndex).Length > 0 ? Label(q, taskIndex) : L.T("（无标题）"));
    public override bool HasScript => false;
    public override ScriptList? Conditions => null;
    public override ScriptList? Actions => null;
    public override bool CanDelete => true;
    public override string SubText => Label(q, taskIndex, desc: true);
    public override string IdText => t.UnknownA8.ToString();
    public override int? TextSlot => TitleSlotOf(q, taskIndex);

    internal static int TitleSlotOf(QstFile q, int ti)
    {
        int s = 2;
        for (int i = 0; i < ti; i++) s += 2 + q.Tasks[i].Incentives.Count + q.Tasks[i].Objectives.Count + q.Tasks[i].Events.Count;
        return s;
    }

    internal static string Label(QstFile q, int ti, bool desc = false)
    {
        int s = TitleSlotOf(q, ti) + (desc ? 1 : 0);
        return SlotText(q, s);
    }
}

internal sealed class QstEventRow(QstFile q, QstTask ownerTask, QstEvent e, int slot, int no) : QstRowOwner
{
    public QstTask OwnerTask => ownerTask;
    public QstEvent E => e;
    public int TextSlotIndex => slot;
    public override object OwnerObject => e;
    public override string NodeLabel => "➳ " + L.T("事件") + " #" + (no + 1);
    public override bool HasScript => true;
    public override ScriptList? Conditions => e.Conditions;
    public override ScriptList? Actions => e.Actions;
    public override bool CanDelete => true;
    public override string IdText => e.Unknown != 0 ? L.F("flags {0}", e.Unknown) : "";
    public override int? TextSlot => slot;
}

internal sealed class QstObjectiveRow(QstFile q, QstTask ownerTask, QstObjective o, int slot) : QstRowOwner
{
    public QstTask OwnerTask => ownerTask;
    public QstObjective O => o;
    public override object OwnerObject => o;
    public override string NodeLabel => "✅ " + (SlotText(q, slot).Length > 0 ? SlotText(q, slot) : L.T("（目标）"));
    public override bool HasScript => true;
    public override ScriptList? Conditions => o.Conditions;
    public override ScriptList? Actions => o.Actions;
    public override bool CanDelete => true;
    // Objective ID（官方编辑器同款：对话/任务的条件动作按此 ID 引用目标），常显
    public override string IdText => o.Unknown0.ToString();
    public override int? TextSlot => slot;
}

internal sealed class QstRow : INotifyPropertyChanged, WpfTheme.HighlightableRow
{
    private readonly QstTableEditor _editor;
    private bool _highlight;
    public QstRowOwner Owner { get; }

    public QstRow? Parent { get; private set; }
    public List<QstRow> Children { get; } = new();
    public bool IsExpanded { get; set; } = true;

    public bool Highlight
    {
        get => _highlight;
        set
        {
            if (_highlight == value) return;
            _highlight = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Highlight)));
        }
    }

    public string Node { get; private set; }
    public string SubText { get; private set; }
    public System.Windows.Visibility SubTextVisibility => string.IsNullOrEmpty(SubText) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public Brush NodeBrush { get; private set; }
    public string IdText { get; private set; }
    public List<ColoredLine> Cond { get; private set; } = new();
    public List<ColoredLine> Act { get; private set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public QstRow(QstTableEditor editor, QstRowOwner owner)
    {
        _editor = editor;
        Owner = owner;
        Node = owner.NodeLabel;
        IdText = owner.IdText;
        NodeBrush = owner.NodeBrush;
        Reload();
    }

    public void Attach(QstRow parent)
    {
        Parent = parent;
        parent.Children.Add(this);
    }

    public void Reload()
    {
        Node = Owner.NodeLabel;
        SubText = Owner.SubText;
        IdText = Owner.IdText;
        NodeBrush = Owner.NodeBrush;
        Cond = Owner.Conditions != null ? ScriptLines(Owner.Conditions) : new List<ColoredLine>();
        Act = Owner.Actions != null ? ScriptLines(Owner.Actions) : new List<ColoredLine>();
        foreach (string p in new[] { nameof(Node), nameof(SubText), nameof(SubTextVisibility), nameof(IdText), nameof(NodeBrush), nameof(Cond), nameof(Act) })
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
// 编辑器（MaterialDesign TreeListView：控件自带层级缩进与展开钮）
// ---------------------------------------------------------------------------

/// <summary>CNV 树型列表：节点(树) | 属性 | 本地化文本 | 条件 | 动作。</summary>
internal sealed class CnvTableEditor : UserControl
{
    private DocSession _doc = null!;
    private CnvFile _cnv = null!;
    private Changed _changed = null!;
    private Action? _locRequested;
    private bool _comfortable;
    private List<CnvRow> _rows = new();

    private readonly WorkingTreeListView _list = MkList();

    public event Action? LocRequested { add => _locRequested += value; remove => _locRequested -= value; }
    public event Action? SelectedChanged;

    public int? SelectedTextSlot => SelectedStep?.Index;

    public CnvTableEditor()
    {
        var header = MkHeader(new[] { (L.T("节点", "Node"), 320d), (L.T("属性", "Attrs"), 150d), (L.T("本地化文本", "Localized Text"), 0d), (L.T("条件", "Conditions"), 280d), (L.T("动作", "Actions"), 280d) });

        _list.ItemTemplate = BuildTemplate(compact: true);
        _list.ItemContainerStyle = MkContainerStyle();
        _list.SelectionChanged += (_, _) => { UpdateHighlight(); SelectedChanged?.Invoke(); };
        _list.MouseDoubleClick += OnDoubleClick;
        _list.PreviewMouseRightButtonDown += ListRightDown;
        _list.ContextMenu = BuildContextMenu();

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_list);
        Content = root;
    }

    private static WorkingTreeListView MkList()
    {
        var l = new WorkingTreeListView
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(l, ScrollBarVisibility.Disabled);
        return l;
    }

    private static FrameworkElement MkHeader((string, double)[] columns)
    {
        var g = new Grid { Margin = new Thickness(24, 2, 18, 2) };
        foreach (var (title, w) in columns)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
        }
        for (int i = 0; i < columns.Length; i++)
        {
            var tb = new TextBlock
            {
                Text = columns[i].Item1,
                FontWeight = FontWeights.Medium,
                Foreground = WpfTheme.MutedBrush,
                Margin = new Thickness(2, 0, 8, 0),
            };
            tb.SetValue(Grid.ColumnProperty, i);
            g.Children.Add(tb);
        }
        var border = new Border
        {
            BorderBrush = WpfTheme.MutedBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = g,
            Margin = new Thickness(0, 2, 0, 2),
        };
        return border;
    }

    /// <summary>行模板：多列 Grid（层级缩进/展开钮由 TreeListView 自带）。</summary>
    private DataTemplate BuildTemplate(bool compact)
    {
        int maxLines = compact ? 3 : 100000;
        const string xaml = """
<HierarchicalDataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                           xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                           xmlns:md="clr-namespace:MaterialDesignThemes.Wpf;assembly=MaterialDesignThemes.Wpf"
                           ItemsSource="{Binding Children}">
  <Grid Background="Transparent">
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="320"/>
      <ColumnDefinition Width="150"/>
      <ColumnDefinition Width="*"/>
      <ColumnDefinition Width="280"/>
      <ColumnDefinition Width="280"/>
    </Grid.ColumnDefinitions>
    <TextBlock Tag="0" Grid.Column="0" Text="{Binding Node}" TextWrapping="Wrap" VerticalAlignment="Center" Margin="2,2,8,2" Foreground="{Binding NodeBrush}"/>
    <TextBlock Tag="1" Grid.Column="1" Text="{Binding Attrs}" TextWrapping="Wrap" VerticalAlignment="Center" Margin="2,2,8,2" Foreground="#6B7280"/>
    <TextBlock Grid.Column="2" Text="{Binding Text}" TextWrapping="Wrap" VerticalAlignment="Top" Margin="2,2,8,2" MaxHeight="__MAXH__">
      <TextBlock.Style>
        <Style TargetType="TextBlock">
          <Style.Triggers>
            <DataTrigger Binding="{Binding IsSelected, RelativeSource={RelativeSource AncestorType={x:Type md:TreeListViewItem}}}" Value="True">
              <Setter Property="MaxHeight" Value="10000"/>
            </DataTrigger>
          </Style.Triggers>
        </Style>
      </TextBlock.Style>
    </TextBlock>
    <ItemsControl Tag="3" Grid.Column="3" ItemsSource="{Binding Cond}" IsTabStop="False" Margin="2,2,8,2">
      <ItemsControl.ItemTemplate>
        <DataTemplate><TextBlock Text="{Binding Text}" Foreground="{Binding Brush}" TextWrapping="Wrap"/></DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
    <ItemsControl Tag="4" Grid.Column="4" ItemsSource="{Binding Act}" IsTabStop="False" Margin="2,2,8,2">
      <ItemsControl.ItemTemplate>
        <DataTemplate><TextBlock Text="{Binding Text}" Foreground="{Binding Brush}" TextWrapping="Wrap"/></DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
  </Grid>
</HierarchicalDataTemplate>
""";
        return (HierarchicalDataTemplate)XamlReader.Parse(xaml.Replace("__MAXLINES__", maxLines.ToString()));
    }

    private Style MkContainerStyle()
    {
        var style = new Style(typeof(TreeListViewItem));
        style.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        return style;
    }

    private void ToggleMode()
    {
        _comfortable = !_comfortable;
        _list.ItemTemplate = BuildTemplate(_comfortable);
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
        cm.Items.Add(Mi(_comfortable ? L.T("平铺模式", "Flat Mode") : L.T("紧凑模式", "Compact Mode"), ToggleMode));
        return cm;
    }

    private void ListRightDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeListViewItem>(e.OriginalSource as DependencyObject);
        if (item != null) item.IsSelected = true;
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        string? tag = FindCellTag(e.OriginalSource as DependencyObject);
        if (tag == null) return;
        var s = SelectedStep;
        if (s == null) return;
        if (tag == "3")
        {
            if (ScriptListDialog.Show(_doc, s.Conditions, true, Window.GetWindow(this))) RefreshSelected();
        }
        else if (tag == "4")
        {
            if (ScriptListDialog.Show(_doc, s.Actions, false, Window.GetWindow(this))) RefreshSelected();
        }
        else if (tag is "0" or "1")
        {
            EditNodeProps();
        }
    }

    /// <summary>向上查找行模板里标记的列 Tag。</summary>
    private static string? FindCellTag(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is FrameworkElement fe && fe.Tag is string tag) return tag;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
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

    private void RefreshSelected()
    {
        if (_list.SelectedItem is CnvRow r) r.Reload();
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

        // TreeListView：ItemsSource 给根集合，子级由模板 Children 提供，展开/缩进由控件管理
        _list.ItemsSource = _rows.Where(r => r.Parent == null).ToList();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        SelectedChanged?.Invoke();
    }

    private CnvStep? SelectedStep => _list.SelectedItem is CnvRow r ? r.Step : null;

    /// <summary>选中行与其子孙行同色高亮。</summary>
    private void UpdateHighlight()
    {
        foreach (var r in _rows) r.Highlight = false;
        if (_list.SelectedItem is CnvRow sel)
            WalkHi(sel);
        return;

        void WalkHi(CnvRow r)
        {
            r.Highlight = true;
            foreach (var c in r.Children) WalkHi(c);
        }
    }

    /// <summary>定位：沿容器链展开父级并选中对应行。</summary>
    public void SelectStep(CnvStep s)
    {
        var row = _rows.FirstOrDefault(r => ReferenceEquals(r.Step, s));
        if (row == null) return;
        var path = new List<CnvRow>();
        for (var r = row; r != null; r = r.Parent) path.Insert(0, r);
        var gen = _list.ItemContainerGenerator;
        for (int i = 0; i < path.Count; i++)
        {
            if (gen.ContainerFromItem(path[i]) is not TreeListViewItem container) return;
            if (i == path.Count - 1)
            {
                _list.SelectedItem = path[i];
                _list.ScrollIntoView(path[i]);
            }
            else if (!container.IsExpanded)
            {
                container.IsExpanded = true;
            }
        }
    }

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

/// <summary>QST 树型列表：节点(树) | Objective ID | 本地化文本 | 条件 | 动作。</summary>
internal sealed class QstTableEditor : UserControl
{
    private DocSession _doc = null!;
    private QstFile _qst = null!;
    private Changed _changed = null!;
    private Action? _locRequested;

    private readonly WorkingTreeListView _list = MkList();
    private List<QstRow> _rows = new();

    public event Action? LocRequested { add => _locRequested += value; remove => _locRequested -= value; }
    public event Action? SelectedChanged;

    public DocSession Doc => _doc;
    public int? SelectedTextSlot => SelectedRow?.Owner.TextSlot;

    public QstTableEditor()
    {
        var header = MkHeader(new[] { (L.T("节点", "Node"), 340d), ("Objective ID", 110d), (L.T("本地化文本", "Localized Text"), 0d), (L.T("条件", "Conditions"), 270d), (L.T("动作", "Actions"), 270d) });

        _list.ItemTemplate = BuildTemplate();
        _list.ItemContainerStyle = MkContainerStyle();
        _list.SelectionChanged += (_, _) => { UpdateHighlight(); SelectedChanged?.Invoke(); };
        _list.MouseDoubleClick += OnDoubleClick;
        _list.PreviewMouseRightButtonDown += ListRightDown;
        _list.ContextMenu = BuildContextMenu();

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_list);
        Content = root;
    }

    private static WorkingTreeListView MkList()
    {
        var l = new WorkingTreeListView
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(l, ScrollBarVisibility.Disabled);
        return l;
    }

    private static FrameworkElement MkHeader((string, double)[] columns)
    {
        var g = new Grid { Margin = new Thickness(24, 2, 18, 2) };
        foreach (var (title, w) in columns)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
        }
        for (int i = 0; i < columns.Length; i++)
        {
            var tb = new TextBlock
            {
                Text = columns[i].Item1,
                FontWeight = FontWeights.Medium,
                Foreground = WpfTheme.MutedBrush,
                Margin = new Thickness(2, 0, 8, 0),
            };
            tb.SetValue(Grid.ColumnProperty, i);
            g.Children.Add(tb);
        }
        return new Border
        {
            BorderBrush = WpfTheme.MutedBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = g,
            Margin = new Thickness(0, 2, 0, 2),
        };
    }

    private DataTemplate BuildTemplate()
    {
        string xaml = """
<HierarchicalDataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                           xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                           ItemsSource="{Binding Children}">
  <Grid Background="Transparent">
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="340"/>
      <ColumnDefinition Width="110"/>
      <ColumnDefinition Width="*"/>
      <ColumnDefinition Width="270"/>
      <ColumnDefinition Width="270"/>
    </Grid.ColumnDefinitions>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>
    <TextBlock Tag="0" Grid.Column="0" Text="{Binding Node}" TextWrapping="Wrap" VerticalAlignment="Center" Margin="2,2,8,2" Foreground="{Binding NodeBrush}"/>
    <TextBlock Grid.Column="0" Grid.Row="1" Text="{Binding SubText}" FontStyle="Italic" FontSize="11" Foreground="#6B7280" Margin="2,0,8,2"
               Visibility="{Binding SubTextVisibility}"/>
    <TextBlock Grid.Column="1" Text="{Binding IdText}" VerticalAlignment="Center" Margin="2,2,8,2" Foreground="#6B7280"/>
    <TextBlock Grid.Column="2" Text="{Binding Text}" TextWrapping="Wrap" VerticalAlignment="Center" Margin="2,2,8,2" MaxHeight="90"/>
    <ItemsControl Tag="2" Grid.Column="3" ItemsSource="{Binding Cond}" IsTabStop="False" Margin="2,2,8,2">
      <ItemsControl.ItemTemplate>
        <DataTemplate><TextBlock Text="{Binding Text}" Foreground="{Binding Brush}" TextWrapping="Wrap"/></DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
    <ItemsControl Tag="3" Grid.Column="4" ItemsSource="{Binding Act}" IsTabStop="False" Margin="2,2,8,2">
      <ItemsControl.ItemTemplate>
        <DataTemplate><TextBlock Text="{Binding Text}" Foreground="{Binding Brush}" TextWrapping="Wrap"/></DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
  </Grid>
</HierarchicalDataTemplate>
""";
        // 模板里的 EmptyToCollapsed 转换器
        var tpl = (HierarchicalDataTemplate)XamlReader.Parse(xaml);
        tpl.Resources["EmptyToCollapsed"] = new EmptyToCollapsedConverter();
        return tpl;
    }

    private Style MkContainerStyle()
    {
        var style = new Style(typeof(TreeListViewItem));
        style.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        return style;
    }

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
        return cm;
    }

    private void ListRightDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeListViewItem>(e.OriginalSource as DependencyObject);
        if (item != null) item.IsSelected = true;
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        string? tag = null;
        var d = e.OriginalSource as DependencyObject;
        while (d != null)
        {
            if (d is FrameworkElement fe && fe.Tag is string t) { tag = t; break; }
            d = VisualTreeHelper.GetParent(d);
        }
        if (tag == null) return;
        var sel = SelectedRow;
        if (sel == null) return;

        if (tag == "2")
        {
            if (ScriptListDialog.Show(_doc, sel.Owner.Conditions, true, Window.GetWindow(this))) sel.Reload();
        }
        else if (tag == "3")
        {
            if (ScriptListDialog.Show(_doc, sel.Owner.Actions, false, Window.GetWindow(this))) sel.Reload();
        }
        else if (tag is "0" or "1")
        {
            EditFields();
        }
    }

    private void EditFields()
    {
        var sel = SelectedRow;
        if (sel == null) return;
        switch (sel.Owner)
        {
            case QstRootOwner:
            case QstQuestOwner:
                EditQuestProps();
                break;
            case QstTaskRow or QstEventRow or QstObjectiveRow:
                if (QstFieldsDialog.Show(sel.Owner, Window.GetWindow(this)))
                {
                    sel.Reload();
                    _changed();
                }
                break;
        }
    }

    private QstRow? SelectedRow => _list.SelectedItem is QstRow r ? r : null;

    private void EditQuestProps()
    {
        if (!QuestPropsDialog.Show(_qst, Window.GetWindow(this))) return;
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
        //   组名（根，红） → 任务名（旗） → task [N] 标题/描述 → OnAccept / Objectives / OnComplete 组 → 事件/目标行
        var rows = new List<QstRow>();
        var rootRow = new QstRow(this, new QstRootOwner(_qst));
        rows.Add(rootRow);
        var questRow = new QstRow(this, new QstQuestOwner(_qst));
        rows.Add(questRow);
        questRow.Attach(rootRow);

        int no = 1;
        foreach (var t in _qst.Tasks)
        {
            int titleSlot = _qst.TaskTitleSlot(no - 1);
            var taskRow = new QstRow(this, new QstTaskRow(_qst, t, no - 1));
            rows.Add(taskRow);
            taskRow.Attach(questRow);

            var acceptRow = new QstRow(this, new QstGroupOwner(
                "▶ " + L.F("OnAccept（接受事件 ×{0}）", t.Incentives.Count), WpfTheme.AccentBrush));
            rows.Add(acceptRow);
            acceptRow.Attach(taskRow);
            for (int k = 0; k < t.Incentives.Count; k++)
            {
                var r = new QstRow(this, new QstEventRow(_qst, t, t.Incentives[k], titleSlot + 2 + k, k));
                rows.Add(r);
                r.Attach(acceptRow);
            }

            var objRow = new QstRow(this, new QstGroupOwner(
                "✅ " + L.F("Objectives（目标 ×{0}）", t.Objectives.Count), WpfTheme.OkBrush));
            rows.Add(objRow);
            objRow.Attach(taskRow);
            for (int k = 0; k < t.Objectives.Count; k++)
            {
                int slot = titleSlot + 2 + t.Incentives.Count + k;   // 串表槽：标题+描述之后依序排目标
                var r = new QstRow(this, new QstObjectiveRow(_qst, t, t.Objectives[k], slot));
                rows.Add(r);
                r.Attach(objRow);
            }

            var compRow = new QstRow(this, new QstGroupOwner(
                "🏁 " + L.F("OnComplete（完成事件 ×{0}）", t.Events.Count), WpfTheme.OkBrush));
            rows.Add(compRow);
            compRow.Attach(taskRow);
            for (int k = 0; k < t.Events.Count; k++)
            {
                var r = new QstRow(this, new QstEventRow(_qst, t, t.Events[k], titleSlot + 2 + t.Incentives.Count + t.Objectives.Count + k, k));
                rows.Add(r);
                r.Attach(compRow);
            }
            no++;
        }

        _rows = rows;
        _list.ItemsSource = _rows.Where(r => r.Parent == null).ToList();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        SelectedChanged?.Invoke();
    }

    /// <summary>选中行与其子孙行同色高亮。</summary>
    private void UpdateHighlight()
    {
        foreach (var r in _rows) r.Highlight = false;
        if (_list.SelectedItem is QstRow sel)
            WalkHi(sel);
        return;

        void WalkHi(QstRow r)
        {
            r.Highlight = true;
            foreach (var c in r.Children) WalkHi(c);
        }
    }

    private QstTask ActiveTask()
    {
        for (var r = SelectedRow; r != null; r = r.Parent)
            if (r.Owner is QstTaskRow tr) return tr.T;
        return _qst.Tasks.FirstOrDefault()!;
    }

    /// <summary>定位：沿容器链展开父级并选中对应行。</summary>
    public void SelectOwner(object owner)
    {
        var row = _rows.FirstOrDefault(r => ReferenceEquals(r.Owner.OwnerObject, owner));
        if (row == null) return;
        var path = new List<QstRow>();
        for (var r = row; r != null; r = r.Parent) path.Insert(0, r);
        var gen = _list.ItemContainerGenerator;
        for (int i = 0; i < path.Count; i++)
        {
            if (gen.ContainerFromItem(path[i]) is not TreeListViewItem container) return;
            if (i == path.Count - 1)
            {
                _list.SelectedItem = path[i];
                _list.ScrollIntoView(path[i]);
            }
            else if (!container.IsExpanded)
            {
                container.IsExpanded = true;
            }
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
                if (!_qst.RemoveEvent(er.OwnerTask, er.E)) return;
                break;
            case QstObjectiveRow or:
                if (!_qst.RemoveObjective(or.OwnerTask, or.O)) return;
                break;
            default:
                Gui.Warn(Window.GetWindow(this), L.T("该行是结构分组（组名 / 任务名 / OnAccept / Objectives / OnComplete），不能删除；请删除其下的具体行。"), L.T("删除", "Delete"));
                return;
        }
        _changed();
        Bind(_doc, _changed);
    }
}
