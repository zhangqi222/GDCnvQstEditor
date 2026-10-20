using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
    public IBrush Brush { get; }

    public ColoredLine(string text, bool unknown, bool inferred)
    {
        Text = text;
        Brush = unknown ? UiTheme.UnknownBrush : inferred ? UiTheme.InferredBrush : UiTheme.TextBrush;
    }
}

/// <summary>CNV 节点的界面名（树形表格第一列：类型徽标 + 文本摘要，层次由缩进表达）。</summary>
internal static class CnvNodeLabel
{
    /// <summary>节点类型的界面名（中文基准，走语言包），带官方风格的图标前缀。</summary>
    public static string KindZh(uint type) => L.T(type switch
    {
        0 => "🏁 根", 1 => "💬 台词", 2 => "➡ 继续选项", 3 => "✅ 接受选项", 4 => "❌ 拒绝选项",
        5 => "🔹 通用", 6 => "🛑 结束对话", 7 => "↪ 跳转", 8 => "🎲 随机选项", _ => "❓ 未知类型",
    });

    /// <summary>无 emoji 的类型名（根节点改用 Semi 图标时，文本不再重复图标语义）。</summary>
    public static string KindName(uint type) => L.T(type switch
    {
        0 => "根", 1 => "台词", 2 => "继续选项", 3 => "接受选项", 4 => "拒绝选项",
        5 => "通用", 6 => "结束对话", 7 => "跳转", 8 => "随机选项", _ => "未知类型",
    });

    /// <summary>树形表用：类型名 + 文本摘要（无缩进——层级由行的缩进表达）。</summary>
    public static string TreeLabel(CnvFile c, CnvStep s)
    {
        // 根节点：图标改用 Semi 小房子（PathIcon），文本不再带 emoji
        string label = s.Type == Names.StepRoot && s.Depth == 0 ? KindName(s.Type) : KindZh(s.Type);
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
// 行模型（父子 + 展开态；INPC 用于编辑后单行刷新）
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

    private bool _highlight;
    public bool Highlight
    {
        get => _highlight;
        set { if (_highlight != value) { _highlight = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Highlight))); } }
    }

    /// <summary>Semi 图标资源键（null = 无图标，仅根节点 = SemiIconHome）。</summary>
    private string? _iconKey;
    public string? IconKey
    {
        get => _iconKey;
        private set { if (_iconKey != value) { _iconKey = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconKey))); } }
    }

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
        IconKey = Step.Type == Names.StepRoot && Step.Depth == 0 ? "SemiIconHome" : null;
        Node = CnvNodeLabel.TreeLabel(Doc.Cnv!, Step);
        var parts = new List<string>();
        if ((Step.Unknown & 1) != 0) parts.Add(L.T("仅一次"));
        if ((Step.Unknown & 2) != 0) parts.Add(L.T("总是显示"));
        if ((Step.Unknown & 4) != 0) parts.Add(L.T("不重要"));
        if (!Step.VoPath.IsEmpty) parts.Add(L.T("有配音"));
        Attrs = string.Join("　", parts);
        Cond = ScriptLines(Step.Conditions);
        Act = ScriptLines(Step.Actions);
        foreach (string p in new[] { nameof(Node), nameof(IconKey), nameof(Attrs), nameof(Cond), nameof(Act) })
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

/// <summary>
/// QST 行所有者（层级对齐官方 QuestEditor：
/// 组名根 → 任务名 → task → OnAccept/Objectives/OnComplete 组 → 事件/目标）。
/// 文本（组名/任务名/标题/描述/目标）从串表槽位动态读取，编辑后自动刷新。
/// </summary>
internal abstract class QstRowOwner
{
    public abstract object OwnerObject { get; }
    public abstract string NodeLabel { get; }
    public abstract bool HasScript { get; }
    public abstract ScriptList? Conditions { get; }
    public abstract ScriptList? Actions { get; }
    public virtual bool CanDelete => false;
    public virtual string SubText => "";
    public virtual IBrush NodeBrush => UiTheme.TextBrush;
    /// <summary>第二列常显的 ID（OBJID 列：quest id / task uid / objective uid；事件无 uid 则显示 flags）。</summary>
    public virtual string IdText => "";
    /// <summary>该 ID 的作用说明（单元格 ToolTip）。</summary>
    public virtual string IdTip => "";
    /// <summary>Semi 图标资源键（null = 无图标）。</summary>
    public virtual string? IconKey => null;
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
    public override string NodeLabel => SlotText(q, 0).Length > 0 ? SlotText(q, 0) : L.T("（任务组）");
    public override string? IconKey => "SemiIconHome";
    public override bool HasScript => false;
    public override ScriptList? Conditions => null;
    public override ScriptList? Actions => null;
    public override int? TextSlot => 0;
    public override IBrush NodeBrush => UiTheme.DangerBrush;
}

/// <summary>任务名行（官方黄旗；文本=串表槽 1；双击编辑 quest 头部与组名/任务名）。</summary>
internal sealed class QstQuestOwner(QstFile q) : QstRowOwner
{
    public QstFile Q => q;
    public override object OwnerObject => q;
    public override string NodeLabel => "🚩 " + (SlotText(q, 1).Length > 0 ? SlotText(q, 1) : L.T("（任务名）"));
    public override bool HasScript => false;
    public override ScriptList? Conditions => null;
    public override ScriptList? Actions => null;
    public override string IdText => q.Id.ToString();
    public override string IdTip => L.T("Quest ID：任务在存档中的引用标识（官方 QuestEditor 底部显示的 Quest ID）。", "Quest ID: reference id shown at the bottom of the official QuestEditor.");
    public override int? TextSlot => 1;
    public override IBrush NodeBrush => UiTheme.AccentBrush;
}

/// <summary>OnAccept / Objectives / OnComplete 组行（仅折叠分组，无脚本内容）。</summary>
internal sealed class QstGroupOwner(string label, IBrush brush) : QstRowOwner
{
    public override object OwnerObject => this;
    public override string NodeLabel => label;
    public override bool HasScript => false;
    public override ScriptList? Conditions => null;
    public override ScriptList? Actions => null;
    public override IBrush NodeBrush => brush;
}

/// <summary>task 行：图标 + [N] 标题（串表标题槽），副文本为描述（标题槽+1）。</summary>
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
    public override string SubText => "";   // 描述改为任务节点下的固定描述行，不再以斜体重复显示
    public override string IdText => t.UnknownA8.ToString();
    public override string IdTip => L.T("uid：task 的引用标识，引擎按此引用 task。", "uid: task reference id used by the engine.");
    public override int? TextSlot => TitleSlotOf(q, taskIndex);

    internal static int TitleSlotOf(QstFile q, int ti) { int s = 2; for (int i = 0; i < ti; i++) s += 2 + q.Tasks[i].Incentives.Count + q.Tasks[i].Objectives.Count + q.Tasks[i].Events.Count; return s; }
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
    // 事件本身没有落盘 uid（官方运行时分配）；flags 非 0 时在此提示
    public override string IdText => e.Unknown != 0 ? L.F("flags {0}", e.Unknown) : "";
    public override string IdTip => L.T("事件无落盘 uid（官方运行时分配）；此处显示 flags。", "Events have no stored uid (assigned at runtime); flags shown here.");
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
    // Objective ID（官方编辑器同款：对话/任务的条件动作按此 ID 引用目标），第二列常显
    public override string IdText => o.Unknown0.ToString();
    public override string IdTip => L.T("Objective ID：对话/任务的条件与动作按此 ID 引用该目标（=objective 的 uid）。", "Objective ID: conditions/actions reference this objective by id (= objective uid).");
    public override int? TextSlot => slot;
}

/// <summary>task 描述行（串表槽=标题槽+1；对齐官方编辑器：任务标题下固定显示描述行，可编辑）。</summary>
internal sealed class QstTaskDescRow(QstFile q, QstTask t, int taskIndex) : QstRowOwner
{
    public QstTask T => t;

    private int Slot => QstTaskRow.TitleSlotOf(q, taskIndex) + 1;

    public override object OwnerObject => t;
    public override string NodeLabel => "📝 " + (SlotText(q, Slot).Length > 0 ? SlotText(q, Slot) : L.T("（无描述）", "(no description)"));
    public override bool HasScript => false;
    public override ScriptList? Conditions => null;
    public override ScriptList? Actions => null;
    public override int? TextSlot => Slot;
    public override IBrush NodeBrush => UiTheme.MutedBrush;
}

internal sealed class QstRow : INotifyPropertyChanged
{
    private readonly QstTableEditor _editor;
    public QstRowOwner Owner { get; }

    public QstRow? Parent { get; private set; }
    public List<QstRow> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;
    public bool IsExpanded { get; set; } = true;

    private bool _highlight;
    public bool Highlight
    {
        get => _highlight;
        set { if (_highlight != value) { _highlight = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Highlight))); } }
    }

    public string Node { get; private set; }
    public string SubText { get; private set; }
    public IBrush NodeBrush { get; private set; }
    public string IdText { get; private set; }
    public string IdTip { get; private set; } = "";
    public string? IconKey { get; private set; }
    public List<ColoredLine> Cond { get; private set; } = new();
    public List<ColoredLine> Act { get; private set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public QstRow(QstTableEditor editor, QstRowOwner owner)
    {
        _editor = editor;
        Owner = owner;
        Node = owner.NodeLabel;
        IdText = owner.IdText;
        IdTip = owner.IdTip;
        IconKey = owner.IconKey;
        NodeBrush = owner.NodeBrush;
        SubText = owner.SubText;
        Reload();
    }

    public void Attach(QstRow parent)
    {
        Parent = parent;
        parent.Children.Add(this);
    }

    /// <summary>刷新标签（Node/SubText/IdText/脚本列）——不动 Text，避免文本列编辑期间回环。</summary>
    public void Reload()
    {
        Node = Owner.NodeLabel;
        SubText = Owner.SubText;
        IdText = Owner.IdText;
        IdTip = Owner.IdTip;
        IconKey = Owner.IconKey;
        NodeBrush = Owner.NodeBrush;
        Cond = Owner.Conditions != null ? ScriptLines(Owner.Conditions) : new List<ColoredLine>();
        Act = Owner.Actions != null ? ScriptLines(Owner.Actions) : new List<ColoredLine>();
        foreach (string p in new[] { nameof(Node), nameof(SubText), nameof(IdText), nameof(IdTip), nameof(IconKey), nameof(NodeBrush), nameof(Cond), nameof(Act) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    /// <summary>本行串表文本（本地化文本列；可编辑，写回后同步主行标签）。</summary>
    public string Text
    {
        get
        {
            int? slot = Owner.TextSlot;
            if (slot == null) return "";
            var lang = _editor.Doc.Qst!.Languages.Count > 0 ? _editor.Doc.Qst!.Languages[0] : null;
            return lang != null && slot.Value >= 0 && slot.Value < lang.Texts.Count
                ? TableLayout.Display(lang.Texts[slot.Value].Text) : "";
        }
        set
        {
            int? slot = Owner.TextSlot;
            if (slot == null) return;
            var lang = _editor.Doc.Qst!.Languages.Count > 0 ? _editor.Doc.Qst!.Languages[0] : null;
            if (lang == null || slot.Value >= lang.Texts.Count) return;
            lang.Texts[slot.Value].SetText(TableLayout.Stored(value));
            Node = Owner.NodeLabel;
            SubText = Owner.SubText;
            foreach (string p in new[] { nameof(Node), nameof(SubText) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
            _editor.NotifyEdited();
        }
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
// 树形表格共享辅助（TreeDataGrid 版）
// ---------------------------------------------------------------------------

/// <summary>Highlight=true → 行/单元格高亮底色（子树随选中行同色）。</summary>
internal sealed class HighlightToBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is true ? UiTheme.HighlightBrush : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>空字符串 → null（ToolTip 空内容不弹）。</summary>
internal sealed class EmptyToNullConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? null : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Semi 图标资源键（如 SemiIconHome）→ Geometry（图标字典已随主题并入应用资源）。</summary>
internal sealed class IconKeyToGeometryConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string key || Application.Current == null) return null;
        var theme = Application.Current.ActualThemeVariant ?? global::Avalonia.Styling.ThemeVariant.Default;
        return Application.Current.TryFindResource(key, theme, out var v) ? v as Geometry : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

internal static class TreeGridShared
{
    public static readonly EmptyToNullConverter EmptyToNull = new();
    public static readonly IconKeyToGeometryConverter IconToGeometry = new();

    public static readonly HighlightToBrushConverter HighlightToBrush = new();

    // 列宽（供 Cnv/Qst 两表共用，保证两文档视图布局一致）
    public const double ColNode = 300;
    public const double ColAttrs = 140;
    public const double ColText = 280;
    public const double ColCond = 230;
    public const double ColAct = 230;

    public static Window? OwnerOf(Visual c) => TopLevel.GetTopLevel(c) as Window;

    public static MenuItem Mi(string header, Action onClick) =>
        new() { Header = header, Command = new RelayCommand(onClick) };
}