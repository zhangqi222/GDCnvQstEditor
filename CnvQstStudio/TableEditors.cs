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

    /// <summary>树型表用：类型名 + 文本摘要（层级由控件的层级缩进表达）。</summary>
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

// 行模型与所有者（同前）
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
    public virtual string IdText => "";
    public virtual int? TextSlot => null;

    protected static string SlotText(QstFile q, int slot)
    {
        var lang = q.Languages.Count > 0 ? q.Languages[0] : null;
        return lang != null && slot >= 0 && slot < lang.Texts.Count ? lang.Texts[slot].Text : "";
    }
}

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

internal sealed class QstGroupOwner(string label, Brush brush) : QstRowOwner
{
    public override object OwnerObject => this;
    public override string NodeLabel => label;
    public override bool HasScript => false;
    public override ScriptList? Conditions => null;
    public override ScriptList? Actions => null;
    public override Brush NodeBrush => brush;
}

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

internal static class TreeGridShared
{
    public static Style MkContainerStyle()
    {
        var style = new Style(typeof(TreeListViewItem));
        var implicitStyle = Application.Current.TryFindResource(typeof(TreeListViewItem)) as Style
            ?? Application.Current.TryFindResource("MaterialDesignTreeListViewItem") as Style;
        if (implicitStyle != null) style.BasedOn = implicitStyle;
        style.Setters.Add(new Setter(System.Windows.Controls.Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        return style;
    }

    public static FrameworkElement MkHeader((string, double)[] columns)
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

    public static MenuItem Mi(string header, Action onClick) =>
        new() { Header = header, Command = new RelayCommand(onClick) };

    public static T? FindAncestor<T>(DependencyObject? d) where T : System.Windows.Media.Visual
    {
        while (d != null && d is not T) d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        return d as T;
    }
}
