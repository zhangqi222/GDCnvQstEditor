using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using GDCnvQstLib;

namespace GDCnvQstEditor;

/// <summary>数值框显示模式（按官方保存方式二选一，不再提供双框联动）：
/// DecOnly = 十进制单框（默认，计数/数值类字段）；HexOnly = 十六进制单框（ID/引用标识类字段）。</summary>
internal enum NumBoxMode { DecOnly, HexOnly }

/// <summary>
/// 数值输入框（各编辑弹窗的数值字段共用）：单框显示，按字段保存进制选择模式，
/// 括号提示固定显示保存进制；无效输入标红并保持上一个合法值。
/// 呈现约定：ID/引用标识类（Task/Objective、任务文件 Id 等）恒为十六进制；
/// 计数/数值类（数量、序号、flags 等）恒为十进制——与官方 Lua 脚本/编辑器惯例一致，
/// 避免双框让用户无从选择。
/// </summary>
internal sealed class NumPairBox : UserControl
{
    private readonly TextBox _dec = new() { MinWidth = 96 };
    private readonly TextBox _hex = new() { MinWidth = 96 };
    private readonly NumBoxMode _mode;   // 保存进制固定按字段类型决定，提示不随值变
    private readonly TextBlock _hint = new()
    {
        Foreground = UiTheme.MutedBrush,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 0, 0),
    };
    private readonly uint _max;
    private readonly string? _note;
    private bool _syncing;
    private bool _valid = true;
    private uint _value;

    /// <summary>数值经合法输入变化（无效输入不触发）。</summary>
    public event Action? ValueChanged;

    /// <summary>当前合法值（无效输入期间保持上一个合法值）。</summary>
    public uint Value => _value;

    public NumPairBox(uint value, uint max = uint.MaxValue, string? note = null, bool inlineHint = true, NumBoxMode mode = NumBoxMode.DecOnly)
    {
        _max = max == 0 ? uint.MaxValue : max;
        _note = note;
        _mode = mode;
        _dec.Text = value.ToString();
        _hex.Text = "0x" + value.ToString("X");

        _dec.TextChanged += (_, _) => Parse(_dec, fromHex: false);
        _hex.TextChanged += (_, _) => Parse(_hex, fromHex: true);

        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (mode != NumBoxMode.HexOnly)
            sp.Children.Add(_dec);
        if (mode != NumBoxMode.DecOnly)
            sp.Children.Add(_hex);
        if (inlineHint) sp.Children.Add(_hint);
        else ToolTip.SetTip(this, _note);
        Content = sp;
        UpdateHint();
    }

    /// <summary>程序化重设值（同时刷新两框与提示）。</summary>
    public void SetValue(uint v)
    {
        _value = v;
        _valid = true;
        _syncing = true;
        _dec.Text = v.ToString();
        _hex.Text = "0x" + v.ToString("X");
        _syncing = false;
        _dec.Foreground = _hex.Foreground = UiTheme.TextBrush;
        UpdateHint();
    }

    private void Parse(TextBox src, bool fromHex)
    {
        if (_syncing) return;
        string s = (src.Text ?? "").Trim();
        uint v;
        bool ok;
        try
        {
            if (fromHex)
            {
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
                v = s.Length == 0 ? 0u : Convert.ToUInt32(s, 16);
            }
            else
            {
                v = s.Length == 0 ? 0u : uint.Parse(s);
            }
            ok = v <= _max;
        }
        catch { ok = false; v = 0; }

        if (ok)
        {
            _valid = true;
            _value = v;
            _syncing = true;
            if (fromHex) _dec.Text = v.ToString();
            else _hex.Text = "0x" + v.ToString("X");
            _syncing = false;
            _dec.Foreground = _hex.Foreground = UiTheme.TextBrush;
            ValueChanged?.Invoke();
        }
        else
        {
            _valid = false;
            src.Foreground = UiTheme.DangerBrush;
        }
        UpdateHint();
    }

    private void UpdateHint()
    {
        string save = _valid
            ? L.F("(will save as {0})", _mode == NumBoxMode.HexOnly ? L.T("Hexadecimal") : L.T("Decimal"))
            : L.T("(invalid value; change not applied)");
        _hint.Text = string.IsNullOrEmpty(_note) ? save : _note + " " + save;
        _hint.Foreground = _valid ? UiTheme.MutedBrush : UiTheme.DangerBrush;
    }
}

/// <summary>
/// 条件/动作列表编辑器（列表 + 小弹窗模式）：
/// 默认只显示 已有项列表 与操作按钮；"添加"与"编辑"共用 <see cref="ScriptRecordDialog"/> 弹窗，
/// 从视觉上区分 已有列表 与 编辑/新增 的内容。
/// </summary>
internal sealed class ScriptListEditor : UserControl
{
    private DocSession _doc;
    private ScriptList _list;
    private readonly bool _isCondition;
    private Changed _changed;

    // 列表限高：条目多时在框内滚动，避免把弹窗撑满导致按钮/底部内容被挤出屏幕。
    private readonly ListBox _records = new() { MinHeight = 150, MaxHeight = 420, MinWidth = 560 };
    private readonly Border _existingBox = new();
    private readonly TextBlock _existingHeader = new() { FontWeight = FontWeight.Medium, Margin = new Thickness(0, 0, 0, 6) };
    private readonly ComboBox _logic = new() { MinWidth = 170 };
    private readonly TextBlock _logicLabel = new() { Text = L.T("Logic") + L.Colon, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _add, _edit, _del, _dup, _up, _down;
    private bool _building;

    public ScriptListEditor(DocSession doc, ScriptList list, bool isCondition, Changed changed)
    {
        _doc = doc;
        _list = list;
        _isCondition = isCondition;
        _changed = changed;

        _add = MkBtn(_isCondition ? L.T("＋ Add condition") : L.T("＋ Add action"), () => _ = AddRecord(), primary: true);
        _edit = MkBtn(L.T("Edit"), () => _ = EditRecord());
        _del = MkBtn(L.T("Delete"), DelRecord);
        _dup = MkBtn(L.T("Duplicate"), DupRecord);
        _up = MkBtn(L.T("Move up"), () => MoveRecord(-1));
        _down = MkBtn(L.T("Move down"), () => MoveRecord(1));

        _logic.ItemsSource = new[] { L.T("Match all (AND)"), L.T("Match any (OR)") };
        _logic.SelectedIndex = _list.Logic == 1 ? 1 : 0;
        _logic.SelectionChanged += (_, _) =>
        {
            if (!_building) _list.Logic = _logic.SelectedIndex == 1 ? 1u : 0u;
        };
        _logicLabel.IsVisible = _logic.IsVisible = _isCondition;

        _records.SelectionChanged += (_, _) => UpdateButtons();
        ScrollViewer.SetVerticalScrollBarVisibility(_records, ScrollBarVisibility.Auto);
        // 双击条目 = 编辑（点在条目上才触发；列表空白处双击不弹窗）。
        _records.DoubleTapped += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<ListBoxItem>() != null)
            {
                e.Handled = true;
                _ = EditRecord();
            }
        };

        Content = BuildLayout();
        Rebuild();
    }

    /// <summary>换选节点/条目时重绑定到新的列表。</summary>
    public void Rebind(DocSession doc, ScriptList list, Changed changed)
    {
        _doc = doc;
        _list = list;
        _changed = changed;
        _building = true;
        _logic.SelectedIndex = _list.Logic == 1 ? 1 : 0;
        _building = false;
        Rebuild();
    }

    private Control BuildLayout()
    {
        // 标题行：关系逻辑（仅条件）
        var logicRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        logicRow.Children.Add(_logicLabel);
        logicRow.Children.Add(_logic);

        // 操作按钮行
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var b in new[] { _add, _edit, _del, _dup, _up, _down }) btns.Children.Add(b);

        var inner = new StackPanel { Margin = new Thickness(8) };
        inner.Children.Add(_existingHeader);
        inner.Children.Add(logicRow);
        inner.Children.Add(_records);
        inner.Children.Add(btns);
        _existingBox.Child = inner;

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_existingBox, Dock.Top);
        root.Children.Add(_existingBox);
        return root;
    }

    private static Button MkBtn(string text, Action onClick, bool primary = false)
    {
        var b = new Button { Content = text, MinWidth = primary ? 96 : 72, MinHeight = 28, Margin = new Thickness(0, 0, 0, 0) };
        if (primary) UiTheme.MakePrimary(b);
        b.Click += (_, _) => onClick();
        return b;
    }

    private ScriptRecord? Current() =>
        _records.SelectedIndex >= 0 && _records.SelectedIndex < _list.Records.Count
            ? _list.Records[_records.SelectedIndex] : null;

    public void Rebuild()
    {
        _building = true;
        RebuildHeader();
        int sel = _records.SelectedIndex;
        var items = new List<string>();
        foreach (var r in _list.Records)
            items.Add((_isCondition ? "⚑ " : "⚡ ") + Gui.Describe(r));
        _records.ItemsSource = items;
        _records.SelectedIndex = sel >= 0 && sel < items.Count ? sel : items.Count > 0 ? 0 : -1;
        UpdateButtons();
        _building = false;
    }

    private void UpdateButtons()
    {
        bool has = Current() != null;
        _edit.IsEnabled = _del.IsEnabled = _dup.IsEnabled = _up.IsEnabled = _down.IsEnabled = has;
        RebuildHeader();
    }

    private void RebuildHeader()
    {
        string cn = L.T(_isCondition ? "Condition" : "Action");
        string logic = _isCondition ? $"  · {Names.LogicWord(_list.Logic)} ({_list.Logic})" : "";
        string empty = _list.Present ? "" : L.T("  (empty list)");
        _existingHeader.Text = L.F("Existing {0} ({1}){2}{3}", cn, _list.Records.Count, logic, empty);
    }

    private async Task AddRecord()
    {
        uint defType = _isCondition ? 0u : 1u;   // 动作类型 0 无效，从 1 开始
        var rec = new ScriptRecord
        {
            IsCondition = _isCondition,
            Type = defType,
            SubType = Scriptable.DefaultBodyVersion(defType),
            Uid = 2,
            BodySpec = _isCondition ? SpecCond(defType) : SpecAct(defType),
            Named = _isCondition ? Names.ConditionClass(defType).named : Names.ActionClass(defType).named,
            ClassName = _isCondition ? Names.ConditionClass(defType).name : Names.ActionClass(defType).name,
        };
        rec.Fields = FieldFactory.Resync(rec.BodySpec, rec.SubType, null, _isCondition, defType);
        string cn = L.T(_isCondition ? "Condition" : "Action");
        var dlg = new ScriptRecordDialog(rec, _isCondition, L.T("Add new {0}") + cn, _doc);
        if (await dlg.ShowDialog<bool>(TreeGridShared.OwnerOf(this)!) != true) return;

        int at = _records.SelectedIndex >= 0 ? _records.SelectedIndex + 1 : _list.Records.Count;
        _list.Records.Insert(Math.Min(at, _list.Records.Count), rec);
        if (!_list.Present && _list.Records.Count > 0)
        {
            _list.Present = true;
            _list.Count = _list.Records.Count;
        }
        Rebuild();
        _records.SelectedIndex = Math.Min(at, _list.Records.Count - 1);
        _changed();
    }

    private async Task EditRecord()
    {
        int sel = _records.SelectedIndex;
        if (sel < 0 || sel >= _list.Records.Count) return;
        var copy = ScriptRecordCloner.Clone(_list.Records[sel]);
        string cn = L.T(_isCondition ? "Condition" : "Action");
        var dlg = new ScriptRecordDialog(copy, _isCondition, L.T("Edit {0}") + cn, _doc);
        if (await dlg.ShowDialog<bool>(TreeGridShared.OwnerOf(this)!) != true) return;
        _list.Records[sel] = copy;
        Rebuild();
        _records.SelectedIndex = sel;
        _changed();
    }

    private void DelRecord()
    {
        int sel = _records.SelectedIndex;
        if (sel < 0) return;
        _list.Records.RemoveAt(sel);
        _list.Count = _list.Records.Count;
        if (_list.Records.Count == 0) { _list.Present = false; _list.Logic = 0; }
        Rebuild();
        _changed();
    }

    private void DupRecord()
    {
        var src = Current();
        if (src == null) return;
        var copy = ScriptRecordCloner.Clone(src);
        int at = _records.SelectedIndex + 1;
        _list.Records.Insert(at, copy);
        _list.Count = _list.Records.Count;
        Rebuild();
        _records.SelectedIndex = at;
        _changed();
    }

    private void MoveRecord(int delta)
    {
        int sel = _records.SelectedIndex;
        int to = sel + delta;
        if (sel < 0 || to < 0 || to >= _list.Records.Count) return;
        (_list.Records[sel], _list.Records[to]) = (_list.Records[to], _list.Records[sel]);
        Rebuild();
        _records.SelectedIndex = to;
        _changed();
    }

    // body 规格表统一取自库
    private static string SpecCond(uint t) => t < 30 ? Scriptable.ConditionBodySpec(t) : "";
    private static string SpecAct(uint t) => t < 29 ? Scriptable.ActionBodySpec(t) : "";
}

/// <summary>ScriptRecord 深拷贝（编辑弹窗用：确定才回填原列表）。</summary>
internal static class ScriptRecordCloner
{
    public static ScriptRecord Clone(ScriptRecord src) => new()
    {
        IsCondition = src.IsCondition,
        Type = src.Type,
        SubType = src.SubType,
        Uid = src.Uid,
        BodySpec = src.BodySpec,
        Named = src.Named,
        ClassName = src.ClassName,
        Fields = src.Fields.Select(FieldFactory.CloneField).ToList(),
    };
}

/// <summary>
/// 单条条件/动作的编辑弹窗：类型 / 比较符（仅条件）/ 逐字段编辑。
/// 新增与编辑共用；直接编辑传入的记录对象（调用方用克隆保证"确定才生效"）。
/// </summary>
internal sealed class ScriptRecordDialog : Window
{
    private readonly ScriptRecord _rec;
    private readonly bool _isCondition;
    private readonly ComboBox _type = new() { MinWidth = 340 };
    private readonly ComboBox _cmp = new() { MinWidth = 90 };
    private readonly TextBlock _sub = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.MutedBrush };
    private readonly FieldsPanel _fields;
    private bool _building;

    private sealed record ComboType(uint Id, string Text);

    public ScriptRecordDialog(ScriptRecord rec, bool isCondition, string title, DocSession? doc = null)
    {
        _rec = rec;
        _isCondition = isCondition;
        DialogChrome.Init(this, title, 620, 0, SizeToContent.Height);   // 620：容纳双进制框 + 保存提示

        _type.ItemsSource = TypeList(isCondition);
        _type.ItemTemplate = new FuncDataTemplate<ComboType>((ct, _) => new TextBlock { Text = ct?.Text ?? "" });
        // 先订阅再选中：SyncTypeCombo 选中即触发 ChangeType，字段面板/类名与类型严格同步
        // （修复：添加新条目时字段区不随默认类型刷新）
        _type.SelectionChanged += (_, _) => ChangeType();
        _type.SelectedItem = null;

        _cmp.ItemsSource = new[] { "<", "<=", "=", ">=", ">", "!=" };
        _cmp.SelectedIndex = (int)Math.Min(rec.Uid, 5);
        _cmp.SelectionChanged += (_, _) =>
        {
            if (_building) return;
            if (_isCondition) _rec.Uid = (uint)Math.Max(0, _cmp.SelectedIndex);
        };
        _cmp.IsVisible = isCondition;

        _fields = new FieldsPanel(doc, () => { });
        SyncTypeCombo();

        var ok = DialogChrome.Button(L.Confirm, primary: true);
        ok.IsDefault = true;
        var cancel = DialogChrome.Cancel(this);
        ok.Click += (_, _) => { Close(true); };

        var stack = new StackPanel { Margin = new Thickness(16) };
        // 类型行 / 比较符行：与字段行（Row）完全相同的 3px 上下边距，保证全弹窗行距一致
        var typeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 3, 0, 3) };
        typeRow.Children.Add(new TextBlock { Text = L.T("Kind"), VerticalAlignment = VerticalAlignment.Center });
        typeRow.Children.Add(_type);
        typeRow.Children.Add(_sub);
        stack.Children.Add(typeRow);
        // 比较符独立一行（仅条件；它是条件语义的重要组成，不与类型挤在一行）
        var cmpRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 3, 0, 3) };
        cmpRow.Children.Add(new TextBlock { Text = L.T("Comparator"), VerticalAlignment = VerticalAlignment.Center });
        cmpRow.Children.Add(_cmp);
        cmpRow.IsVisible = isCondition;
        stack.Children.Add(cmpRow);
        stack.Children.Add(_fields);
        stack.Children.Add(DialogChrome.Footer(cancel, ok));
        Content = stack;

        _building = false;
    }

    private static List<ComboType> TypeList(bool isCondition)
    {
        var items = new List<ComboType>();
        for (uint t = 0; ; t++)
        {
            string? name = isCondition
                ? (t < 30 ? Names.ConditionLabel(t) : null)
                : (t < 29 ? Names.ActionLabel(t) : null);
            if (name == null) break;
            if (!isCondition && (t == 0 || t == 16)) continue;   // 动作工厂对 0/16 返回 null
            items.Add(new ComboType(t, name));
        }
        return items;
    }

    private void SyncTypeCombo()
    {
        var items = (List<ComboType>)_type.ItemsSource!;
        for (int i = 0; i < items.Count; i++)
            if (items[i].Id == _rec.Type) { _type.SelectedIndex = i; return; }
        if (items.Count > 0) _type.SelectedIndex = 0;
    }

    /// <summary>显示 body 版本（表格 sub=N 对应的 SubType）——只读说明，换类型时随默认版本刷新。</summary>
    private void UpdateSub()
    {
        _sub.Text = L.F("Body version: {0}", _rec.SubType);
        ToolTip.SetTip(_sub, L.T("Internal version (subType, shown as sub=N) of the condition/action. It determines the field layout: version 1 includes extra fields (e.g. CompleteOnly for HasItem). A few types are fixed at 1 and the rest at 0; it is set by the type, matches the official editor, and cannot be changed."));
    }

    private void ChangeType()
    {
        if (_building || _type.SelectedItem is not ComboType ct) return;
        _rec.Type = ct.Id;
        _rec.SubType = Scriptable.DefaultBodyVersion(ct.Id);
        _rec.BodySpec = _isCondition
            ? (ct.Id < 30 ? Scriptable.ConditionBodySpec(ct.Id) : "")
            : (ct.Id < 29 ? Scriptable.ActionBodySpec(ct.Id) : "");
        var named = _isCondition ? Names.ConditionClass(ct.Id) : Names.ActionClass(ct.Id);
        _rec.ClassName = named.name;
        _rec.Named = named.named;
        _rec.Fields = FieldFactory.Resync(_rec.BodySpec, _rec.SubType, _rec.Fields, _isCondition, _rec.Type);
        _fields.Bind(_rec);
        UpdateSub();
    }
}

/// <summary>body 字段工厂：按规格同步字段列表（换类型/改 subType 时保位带值）。
/// type = 所属类型 ID：按官方参数名表（Scriptable.ConditionParamNames/ActionParamNames）
/// 统一刷新每个字段的 Param（含复用保值的旧字段），GUI 细则弹窗的字段行标签据此显示
/// "原文（本地化）"，与官方编辑器一致。</summary>
internal static class FieldFactory
{
    public static List<ScriptField> Resync(string spec, byte sub, List<ScriptField>? old, bool isCondition, uint type = 0)
    {
        var result = new List<ScriptField>();
        int oi = 0;
        old ??= new List<ScriptField>();
        ScriptField? Take(FieldKind k)
        {
            var f = oi < old.Count && old[oi].Kind == k ? old[oi] : null;
            oi++;
            return f;
        }
        foreach (char c in spec)
        {
            switch (c)
            {
                case 'S': result.Add(Take(FieldKind.Str) ?? NewStr()); break;
                case 'I': result.Add(Take(FieldKind.U32) ?? NewU32()); break;
                case 'B': result.Add(Take(FieldKind.U8) ?? NewU8()); break;
                case 'i': if (sub == 1) result.Add(Take(FieldKind.U32) ?? NewU32()); else oi++; break;
                case 'b': if (sub == 1) result.Add(Take(FieldKind.U8) ?? NewU8()); else oi++; break;
                case 'P': result.Add(Take(FieldKind.P) ?? NewP()); break;
                case 'L': result.Add(Take(FieldKind.Lua) ?? NewLua(isCondition)); break;
            }
        }
        // 按字段序位回填官方参数名；表内为 null/超长时留空，由界面回退到种类描述
        string?[]? names = isCondition ? Scriptable.ConditionParamNames(type) : Scriptable.ActionParamNames(type);
        for (int i = 0; i < result.Count; i++)
            result[i].Param = names != null && i < names.Length ? names[i] ?? "" : "";
        return result;
    }

    private static ScriptField NewStr() => new() { Kind = FieldKind.Str, S = new GdString("") };
    private static ScriptField NewU32() => new() { Kind = FieldKind.U32 };
    // 布尔字段新建默认 TRUE（官方编辑器新建条目亦为勾选态）；编辑既有记录时 Take() 保留原值。
    private static ScriptField NewU8() => new() { Kind = FieldKind.U8, B = 1 };
    private static ScriptField NewP() => new() { Kind = FieldKind.P, Paths = new List<GdString>(), Trailing = 0, Note = "trailing meaning unknown" };
    private static ScriptField NewLua(bool isCondition) => new()
    {
        Kind = FieldKind.Lua,
        S = new GdString(""),
        Params = new List<LuaParam>(),
        Tail = isCondition ? new LuaTail() : null,
        Note = "Lua function, resolved through LuaManager",
    };

    public static ScriptField CloneField(ScriptField f) => new()
    {
        Kind = f.Kind,
        S = new GdString(f.S.Raw),
        U = f.U,
        B = f.B,
        Count = f.Count,
        Paths = f.Paths?.Select(p => new GdString(p.Raw)).ToList(),
        Trailing = f.Trailing,
        Params = f.Params?.Select(p => new LuaParam { A = p.A, B = p.B, C = p.C, S = new GdString(p.S.Raw) }).ToList(),
        Tail = f.Tail == null ? null : new LuaTail { T0 = f.Tail.T0, T1 = f.Tail.T1, T2 = f.Tail.T2, T3 = f.Tail.T3, S = new GdString(f.Tail.S.Raw) },
        Note = f.Note,
    };
}

/// <summary>按字段种类生成编辑行（S/I/B 内联；P/Lua 用对话框）。</summary>
internal sealed class FieldsPanel : ScrollViewer
{
    private readonly DocSession? _doc;
    private readonly Changed _changed;
    private ScriptRecord? _rec;
    private bool _building;
    private bool _refilling;
    // 引用标识（Task/Objective）下拉：串字段（Quest）变化时按文件内容重新填充
    private readonly List<(ComboBox Combo, ScriptField Field)> _uidCombos = new();

    public FieldsPanel(DocSession? doc, Changed changed)
    {
        _doc = doc;
        _changed = changed;
        MaxHeight = 380;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    /// <summary>Task/Objective 引用标识字段（445 样本实测：官方保存为十六进制引用标识）：
    /// QuestTaskComplete / OnQuestTask 的 Task，BeginQuestTask / CompleteQuestTask 的 Task，
    /// HasQuestObjective 的 Task 与 Objective。</summary>
    private static bool IsTaskUidField(string? className, string param) =>
        param is "Task" or "Objective" && className is
            "QuestTaskComplete" or "OnQuestTask" or "BeginQuestTask" or "CompleteQuestTask" or "HasQuestObjective";

    public void Bind(ScriptRecord? rec)
    {
        _rec = rec;
        Rebuild();
    }

    private void Rebuild()
    {
        _building = true;
        _uidCombos.Clear();
        var panel = new StackPanel { Margin = new Thickness(0, 2, 8, 4) };
        Content = panel;

        if (_rec == null)
        {
            panel.Children.Add(new TextBlock { Text = L.T("(no record selected)"), Foreground = UiTheme.MutedBrush });
            _building = false;
            return;
        }

        int i = 0;
        foreach (var f in _rec.Fields)
        {
            switch (f.Kind)
            {
                case FieldKind.Str:
                {
                    var box = new TextBox { Text = f.S.Text, MinWidth = 300, MaxWidth = 460 };
                    // 资源路径字段辅助：值像路径（含 \ 或 /）时显示"浏览"。
                    // 读取基准：优先按"类型→目录"字典（445 个官方样本实测：任务类→resources\、物品/技能类→database\），
                    // 未命中字典回退扩展名规则（.dbr → database\，其余 → resources\）。
                    string? dir = _rec == null ? null : AppSettings.FieldDir(_rec.ClassName);
                    string[]? exts = _rec != null && AppSettings.FieldExts.TryGetValue(_rec.ClassName, out var fe) ? fe : null;
                    var browse = DialogChrome.Button(L.T("Browse…"), primary: false);
                    browse.Click += async (_, _) => await BrowseResource(TreeGridShared.OwnerOf(this), box, dir, exts, v =>
                    {
                        box.Text = v;
                        f.S.SetText(v);
                        _changed();
                    });
                    box.TextChanged += (_, _) =>
                    {
                        if (_building) return;
                        f.S.SetText(box.Text ?? "");
                        _changed();
                        UpdateResourceBtns();
                        // Quest 文件路径变化 → 重填 Task/Objective 引用标识下拉
                        RefreshUidCombos();
                    };
                    void UpdateResourceBtns()
                    {
                        string v = box.Text ?? "";
                        // 字典命中的文件类字段（如 GiveItem→database）恒显示浏览；其余值像路径才显示。
                        bool isPath = dir != null || v.Contains('\\') || v.Contains('/');
                        browse.IsVisible = isPath;
                    }
                    UpdateResourceBtns();
                    panel.Children.Add(Row(ParamLabel(f.Param, L.F("string[{0}]", i)), box, browse));
                    break;
                }
                case FieldKind.U32 when IsTaskUidField(_rec.ClassName, f.Param):
                {
                    // 引用标识字段：下拉直接选任务文件里的 task / objective（值 = 文件中落盘的 uid，
                    // 已实测与 cnv 引用一致）；默认禁止手工编辑，"强制编辑"解锁为十六进制单框。
                    var combo = new ComboBox { MinWidth = 300, MaxWidth = 430 };
                    // 当前 ID 不在任务文件中时，条目（含收起后的选中项）用红色文字提示。
                    combo.ItemTemplate = new FuncDataTemplate<UidItem>((it, _) => new TextBlock
                    {
                        Text = it?.Text ?? "",
                        Foreground = it?.Missing == true ? UiTheme.DangerBrush : UiTheme.TextBrush,
                    });
                    combo.SelectionChanged += (_, _) =>
                    {
                        if (_building || _refilling) return;
                        if (combo.SelectedItem is UidItem it && it.Hash != f.U) { f.U = it.Hash; _changed(); }
                    };
                    var force = DialogChrome.Button(L.T("Force edit…"), primary: false);
                    var hex = new NumPairBox(f.U, mode: NumBoxMode.HexOnly) { IsVisible = false };
                    force.Click += async (_, _) =>
                    {
                        var owner = TreeGridShared.OwnerOf(this);
                        var r = await MsgBox.Show(owner!, L.T("Force-edit the reference id? A mismatched quest file and ID will corrupt quest state. Use this only to repair broken references. Remove the restriction?"),
                            L.T("Force edit"), MsgButtons.YesNo, warning: true);
                        if (r != MsgResult.Yes) return;
                        combo.IsVisible = false;
                        force.IsVisible = false;
                        hex.IsVisible = true;
                    };
                    panel.Children.Add(Row(ParamLabel(f.Param, L.F("number[{0}]", i)), combo, force, hex));
                    _uidCombos.Add((combo, f));
                    RefillUidCombo(combo, f);
                    break;
                }
                case FieldKind.U32:
                {
                    // 普通数值字段：十进制单框（默认模式）。ID/引用标识类已在上面 IsTaskUidField 分支处理。
                    var num = new NumPairBox(f.U, note: f.Param.Length > 0 ? f.Note : null);
                    num.ValueChanged += () => { if (_building) return; f.U = num.Value; _changed(); };
                    panel.Children.Add(Row(ParamLabel(f.Param, L.F("number[{0}]", i)), num));
                    break;
                }
                case FieldKind.U8:
                {
                    // 布尔字段：Semi 开关组件，右侧文字随开/关状态切换（true/false），无需额外描述
                    var sw = new ToggleSwitch
                    {
                        OffContent = L.T("false (no / condition not met)"),
                        OnContent = L.T("true (yes / condition met)"),
                        IsChecked = f.B != 0,
                    };
                    sw.IsCheckedChanged += (_, _) => { if (!_building) { f.B = sw.IsChecked == true ? (byte)1 : (byte)0; _changed(); } };
                    panel.Children.Add(Row(ParamLabel(f.Param, L.F("bool[{0}]", i)), sw));
                    break;
                }
                case FieldKind.P:
                {
                    var btn = new Button { Content = L.T("Edit path list…"), MinWidth = 130 };
                    btn.Click += async (_, _) =>
                    {
                        var owner = TreeGridShared.OwnerOf(this);
                        var dlg = new StringListDialog(L.T("Edit path list"), f.Paths!.Select(p => p.Text).ToList());
                        if (await dlg.ShowDialog<bool>(owner!) != true) return;
                        f.Paths = dlg.Items.Select(s => new GdString(s)).ToList();
                        f.Count = f.Paths.Count;
                        _changed();
                    };
                    panel.Children.Add(Row(ParamLabel(f.Param, L.F("paths×{0}", f.Paths!.Count)), btn,
                        new TextBlock { Text = L.F("trailing={0}", f.Trailing), Foreground = UiTheme.MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }));
                    break;
                }
                case FieldKind.Lua:
                {
                    var call = new TextBox { Text = f.S.Text, MinWidth = 280, MaxWidth = 300 };
                    call.TextChanged += (_, _) => { if (_building) return; f.S.SetText(call.Text ?? ""); _changed(); };
                    var btn = new Button { Content = L.F("params×{0}…", f.Params!.Count), MinWidth = 110, Margin = new Thickness(8, 0, 0, 0) };
                    btn.Click += async (_, _) =>
                    {
                        var owner = TreeGridShared.OwnerOf(this);
                        var dlg = new LuaParamsDialog(f);
                        if (await dlg.ShowDialog<bool>(owner!) == true) _changed();
                    };
                    var row = Row(ParamLabel(f.Param, L.T("Lua call")), call, btn);
                    panel.Children.Add(row);
                    if (f.Tail != null)
                    {
                        var t = f.Tail!;
                        // 条件尾部 T0-T3：数值/计数语义（非 ID 引用），十进制单框
                        var t0 = new NumPairBox(t.T0);
                        var t1 = new NumPairBox(t.T1);
                        var t2 = new NumPairBox(t.T2, max: 255);
                        var t3 = new NumPairBox(t.T3);
                        var ts = new TextBox { Text = t.S.Text, MinWidth = 200 };
                        t0.ValueChanged += () => { if (!_building) { t.T0 = t0.Value; _changed(); } };
                        t1.ValueChanged += () => { if (!_building) { t.T1 = t1.Value; _changed(); } };
                        t2.ValueChanged += () => { if (!_building) { t.T2 = (byte)t2.Value; _changed(); } };
                        t3.ValueChanged += () => { if (!_building) { t.T3 = t3.Value; _changed(); } };
                        ts.TextChanged += (_, _) => { if (!_building) { t.S.SetText(ts.Text ?? ""); _changed(); } };
                        panel.Children.Add(Row(L.T("Condition tail T0"), t0));
                        panel.Children.Add(Row(L.T("Condition tail T1"), t1));
                        panel.Children.Add(Row(L.T("Condition tail T2"), t2));
                        panel.Children.Add(Row(L.T("Condition tail T3"), t3));
                        panel.Children.Add(Row(L.T("Condition tail text"), ts));
                    }
                    break;
                }
            }
            i++;
        }

        _building = false;
    }

    // ------------------------------------------------------------------
    // Task/Objective 引用标识下拉：从 Quest 串字段指向的 .qst 文件解析
    // task（UnknownA8）/ objective（Unknown0）uid——与 cnv 引用值逐字节一致（官方样本实测）。
    // ------------------------------------------------------------------

    /// <summary>下拉项：Missing = 当前值不在该任务文件中（红色提示）。</summary>
    private sealed record UidItem(uint Hash, string Text, bool Missing = false);

    /// <summary>Quest 文件路径变化后重填全部引用标识下拉（保持当前选中值不变）。</summary>
    private void RefreshUidCombos()
    {
        foreach (var (combo, f) in _uidCombos) RefillUidCombo(combo, f);
    }

    private void RefillUidCombo(ComboBox combo, ScriptField f)
    {
        if (_rec == null) return;
        bool objective = f.Param == "Objective";
        var items = new List<UidItem>();
        QstFile? q = null;
        string? abs = null;
        // QST 文档中的条件/动作引用的是当前任务文件本身；CNV 则按 Quest 串字段指向的 .qst 读取。
        if (_doc?.Qst != null)
        {
            q = _doc.Qst;
        }
        else
        {
            string? questPath = _rec.Fields.FirstOrDefault(x => x.Kind == FieldKind.Str)?.S.Text?.Trim();
            abs = questPath is { Length: > 0 }
                ? AppSettings.ResolveResource(questPath, AppSettings.FieldDir(_rec.ClassName))
                : null;
            if (abs != null && File.Exists(abs))
            {
                try
                {
                    var parsed = QstFile.Parse(File.ReadAllBytes(abs));
                    if (parsed.Valid) q = parsed;
                }
                catch { /* 任务文件不可用时下拉只显示当前值 */ }
            }
        }

        if (q != null)
        {
            var texts = q.PrimaryLanguage.Texts;
            for (int ti = 0; ti < q.Tasks.Count; ti++)
            {
                var t = q.Tasks[ti];
                int titleSlot = q.TaskTitleSlot(ti);
                if (!objective)
                    items.Add(new UidItem(t.UnknownA8, UidItemText($"T{ti + 1}", t.UnknownA8, SlotText(texts, titleSlot))));
                int objBase = titleSlot + 2 + t.Incentives.Count;
                for (int k = 0; k < t.Objectives.Count; k++)
                {
                    if (!objective) continue;
                    items.Add(new UidItem(t.Objectives[k].Unknown0,
                        UidItemText($"T{ti + 1}·" + L.T("Objective") + (k + 1), t.Objectives[k].Unknown0, SlotText(texts, objBase + k))));
                }
            }
        }

        // 当前值不在任务中（或任务文件不可用）时保留一个红色兜底项，避免丢失原值
        if (items.All(it => it.Hash != f.U))
        {
            string tail = q != null
                ? L.T("(not in this quest file)")
                : abs == null && _doc?.Qst == null
                    ? L.T("(quest file not found; set the Quest file first)")
                    : L.T("(cannot parse quest file)");
            items.Insert(0, new UidItem(f.U, "0x" + f.U.ToString("X8") + tail, Missing: true));
        }
        _refilling = true;
        combo.ItemsSource = items;
        combo.SelectedItem = items.FirstOrDefault(it => it.Hash == f.U);
        _refilling = false;
    }

    private static string UidItemText(string tag, uint hash, string? title)
    {
        string h = "0x" + hash.ToString("X8");
        return string.IsNullOrWhiteSpace(title) ? tag + L.InParen(h) : $"{tag} {title}" + L.InParen(h);
    }

    private static string? SlotText(List<GdString> texts, int slot) =>
        slot >= 0 && slot < texts.Count && texts[slot].Text.Length > 0 ? texts[slot].Text : null;

    // ------------------------------------------------------------------
    // 资源路径字段辅助（串字段"浏览"按钮）
    // ------------------------------------------------------------------

    /// <summary>内置文件浏览器（根目录固定：.dbr → database\，其余 → resources\），
    /// 字典命中时按扩展名过滤；选中后返回相对路径写回输入框。</summary>
    private async Task BrowseResource(Window? owner, TextBox box, string? dirOverride, string[]? exts, Action<string> apply)
    {
        string value = (box.Text ?? "").Trim();
        string subDir = dirOverride
            ?? (value.EndsWith(".dbr", StringComparison.OrdinalIgnoreCase) ? "database" : "resources");
        string? rel = await FileBrowserDialog.Pick(owner, subDir,
            L.T("Choose resource file"), exts, value);
        if (rel != null) apply(rel);
    }

    /// <summary>字段行标签：官方参数名按"原文（本地化）"显示（如 ItemRecord（物品记录））；
    /// 未命名参数回退到序号/种类描述。类型下拉与已有项列表同样经由 TypePair，全弹窗格式统一。</summary>
    private static string ParamLabel(string param, string fallback) =>
        param.Length > 0 ? L.TypePair(param) : fallback;

    /// <summary>一行：左标签 + 若干控件横排（标签自然宽度，不固定像素）。</summary>
    private static Control Row(string label, params Control[] controls)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 3, 0, 3) };
        sp.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        foreach (var c in controls) sp.Children.Add(c);
        return sp;
    }
}
