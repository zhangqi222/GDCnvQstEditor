using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CnvQst;

namespace CnvQstStudio;

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

    private readonly ListBox _records = new() { MinHeight = 150, MinWidth = 560 };
    private readonly GroupBox _existingBox = new();
    private readonly ComboBox _logic = new() { MinWidth = 170 };
    private readonly TextBlock _logicLabel = new() { Text = L.T("关系逻辑", "Logic") + "：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
    private readonly System.Windows.Controls.Button _add, _edit, _del, _dup, _up, _down;
    private bool _building;

    public ScriptListEditor(DocSession doc, ScriptList list, bool isCondition, Changed changed)
    {
        _doc = doc;
        _list = list;
        _isCondition = isCondition;
        _changed = changed;

        _add = MkBtn(_isCondition ? L.T("＋ 添加条件", "+ Add") : L.T("＋ 添加动作", "+ Add"), AddRecord, primary: true);
        _edit = MkBtn(L.T("编辑", "Edit"), EditRecord);
        _del = MkBtn(L.T("删除", "Delete"), DelRecord);
        _dup = MkBtn(L.T("复制", "Copy"), DupRecord);
        _up = MkBtn(L.T("上移", "Up"), () => MoveRecord(-1));
        _down = MkBtn(L.T("下移", "Down"), () => MoveRecord(1));

        _logic.ItemsSource = new[] { L.T("满足全部（AND）"), L.T("满足任一（OR）") };
        _logic.SelectedIndex = _list.Logic == 1 ? 1 : 0;
        _logic.SelectionChanged += (_, _) =>
        {
            if (!_building) _list.Logic = _logic.SelectedIndex == 1 ? 1u : 0u;
        };
        _logicLabel.Visibility = _logic.Visibility = _isCondition ? Visibility.Visible : Visibility.Collapsed;

        _records.SelectionChanged += (_, _) => UpdateButtons();

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

    private UIElement BuildLayout()
    {
        // 标题行：关系逻辑（仅条件）
        var logicRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        logicRow.Children.Add(_logicLabel);
        logicRow.Children.Add(_logic);

        // 操作按钮行
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var b in new[] { _add, _edit, _del, _dup, _up, _down }) btns.Children.Add(b);

        var inner = new StackPanel();
        inner.Children.Add(logicRow);
        inner.Children.Add(_records);
        inner.Children.Add(btns);
        _existingBox.Content = inner;

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_existingBox, Dock.Top);
        root.Children.Add(_existingBox);
        return root;
    }

    private static System.Windows.Controls.Button MkBtn(string text, Action onClick, bool primary = false)
    {
        System.Windows.Controls.Button b = primary
            ? new Wpf.Ui.Controls.Button { Content = text, Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, MinWidth = 96, MinHeight = 28 }
            : new System.Windows.Controls.Button { Content = text, MinWidth = 72, MinHeight = 28 };
        b.Margin = new Thickness(0, 0, 8, 0);
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
        string cn = L.T(_isCondition ? "条件" : "动作");
        string logic = _isCondition ? $"  · {Names.LogicWord(_list.Logic)} ({_list.Logic})" : "";
        string empty = _list.Present ? "" : L.T("  （空列表）", "  (empty)");
        _existingBox.Header = L.F("已有{0}（{1} 条）{2}{3}", cn, _list.Records.Count, logic, empty);
    }

    private void AddRecord()
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
        };
        rec.Fields = FieldFactory.Resync(rec.BodySpec, rec.SubType, null, _isCondition);
        string cn = L.T(_isCondition ? "条件" : "动作");
        var dlg = new ScriptRecordDialog(rec, _isCondition, L.T("添加新{0}", "Add New") + cn) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

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

    private void EditRecord()
    {
        int sel = _records.SelectedIndex;
        if (sel < 0 || sel >= _list.Records.Count) return;
        var copy = ScriptRecordCloner.Clone(_list.Records[sel]);
        string cn = L.T(_isCondition ? "条件" : "动作");
        var dlg = new ScriptRecordDialog(copy, _isCondition, L.T("编辑{0}", "Edit") + cn) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
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
    private readonly FieldsPanel _fields;
    private bool _building;

    private sealed record ComboType(uint Id, string Text);

    public ScriptRecordDialog(ScriptRecord rec, bool isCondition, string title)
    {
        _rec = rec;
        _isCondition = isCondition;
        DialogChrome.Init(this, title, 580, 0, SizeToContent.Height);

        _type.ItemsSource = TypeList(isCondition);
        _type.DisplayMemberPath = nameof(ComboType.Text);
        SyncTypeCombo();
        _type.SelectionChanged += (_, _) => ChangeType();

        _cmp.ItemsSource = new[] { "<", "<=", "=", ">=", ">", "!=" };
        _cmp.SelectedIndex = (int)Math.Min(rec.Uid, 5);
        _cmp.SelectionChanged += (_, _) =>
        {
            if (_building) return;
            if (_isCondition) _rec.Uid = (uint)Math.Max(0, _cmp.SelectedIndex);
        };
        Visibility cmpVis = isCondition ? Visibility.Visible : Visibility.Collapsed;
        _cmp.Visibility = cmpVis;

        _fields = new FieldsPanel(() => { });
        _fields.Bind(rec);

        var ok = DialogChrome.Button(L.Confirm, primary: true);
        ok.IsDefault = true;
        var cancel = DialogChrome.Button(L.Cancel);
        cancel.IsCancel = true;
        ok.Click += (_, _) => DialogResult = true;

        var stack = new StackPanel { Margin = new Thickness(16) };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(new TextBlock { Text = L.T("类型", "Type"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        top.Children.Add(_type);
        top.Children.Add(new TextBlock { Text = L.T("比较符", "Comparator"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0), Visibility = cmpVis });
        top.Children.Add(_cmp);
        stack.Children.Add(top);
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
        var items = (List<ComboType>)_type.ItemsSource;
        for (int i = 0; i < items.Count; i++)
            if (items[i].Id == _rec.Type) { _type.SelectedIndex = i; return; }
        if (items.Count > 0) _type.SelectedIndex = 0;
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
        _rec.Fields = FieldFactory.Resync(_rec.BodySpec, _rec.SubType, _rec.Fields, _isCondition);
        _fields.Bind(_rec);
    }
}

/// <summary>body 字段工厂：按规格同步字段列表（换类型/改 subType 时保位带值）。</summary>
internal static class FieldFactory
{
    public static List<ScriptField> Resync(string spec, byte sub, List<ScriptField>? old, bool isCondition)
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
        return result;
    }

    private static ScriptField NewStr() => new() { Kind = FieldKind.Str, S = new GdString("") };
    private static ScriptField NewU32() => new() { Kind = FieldKind.U32, Note = "meaning unknown" };
    private static ScriptField NewU8() => new() { Kind = FieldKind.U8, Note = "meaning unknown" };
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
    private readonly Changed _changed;
    private ScriptRecord? _rec;
    private bool _building;

    public FieldsPanel(Changed changed)
    {
        _changed = changed;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        MaxHeight = 380;
    }

    public void Bind(ScriptRecord? rec)
    {
        _rec = rec;
        Rebuild();
    }

    private void Rebuild()
    {
        _building = true;
        var panel = new StackPanel { Margin = new Thickness(0, 2, 8, 4) };
        Content = panel;

        if (_rec == null)
        {
            panel.Children.Add(new TextBlock { Text = L.T("（未选择记录）"), Foreground = WpfTheme.MutedBrush });
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
                    box.TextChanged += (_, _) => { if (_building) return; f.S.SetText(box.Text); _changed(); };
                    panel.Children.Add(Row(f.Param.Length > 0 ? f.Param : L.F("串[{0}]", i), box));
                    break;
                }
                case FieldKind.U32:
                {
                    var box = new TextBox { Text = Gui.U32Text(f.U), MinWidth = 150 };
                    var note = new TextBlock
                    {
                        Text = f.Param.Length > 0 ? f.Note : L.T("（语义未知；支持 0x 十六进制）"),
                        Foreground = WpfTheme.MutedBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0),
                    };
                    box.TextChanged += (_, _) =>
                    {
                        if (_building) return;
                        try { f.U = Gui.ParseU32(box.Text); box.Foreground = WpfTheme.TextBrush; }
                        catch { box.Foreground = Brushes.Red; return; }
                        _changed();
                    };
                    panel.Children.Add(Row(f.Param.Length > 0 ? f.Param : L.F("数值[{0}]", i), box, note));
                    break;
                }
                case FieldKind.U8:
                {
                    var cb = new CheckBox
                    {
                        Content = (f.Param.Length > 0 ? f.Param : L.F("布尔[{0}]", i)) + L.T("（0=False / 非0=True）"),
                        IsChecked = f.B != 0,
                    };
                    cb.Checked += (_, _) => { if (!_building) { f.B = 1; _changed(); } };
                    cb.Unchecked += (_, _) => { if (!_building) { f.B = 0; _changed(); } };
                    panel.Children.Add(cb);
                    break;
                }
                case FieldKind.P:
                {
                    var btn = new Button { Content = L.T("编辑路径列表…"), MinWidth = 130 };
                    btn.Click += (_, _) =>
                    {
                        var dlg = new StringListDialog(L.T("编辑路径列表"), f.Paths!.Select(p => p.Text).ToList()) { Owner = Window.GetWindow(this) };
                        if (dlg.ShowDialog() == true)
                        {
                            f.Paths = dlg.Items.Select(s => new GdString(s)).ToList();
                            f.Count = f.Paths.Count;
                            _changed();
                        }
                    };
                    panel.Children.Add(Row(f.Param.Length > 0 ? f.Param : L.F("路径×{0}", f.Paths!.Count), btn,
                        new TextBlock { Text = L.F("尾字={0}", f.Trailing), Foreground = WpfTheme.MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }));
                    break;
                }
                case FieldKind.Lua:
                {
                    var call = new TextBox { Text = f.S.Text, MinWidth = 280, MaxWidth = 300 };
                    call.TextChanged += (_, _) => { if (_building) return; f.S.SetText(call.Text); _changed(); };
                    var btn = new Button { Content = L.F("参数×{0}…", f.Params!.Count), MinWidth = 110, Margin = new Thickness(8, 0, 0, 0) };
                    btn.Click += (_, _) =>
                    {
                        var dlg = new LuaParamsDialog(f) { Owner = Window.GetWindow(this) };
                        if (dlg.ShowDialog() == true) _changed();
                    };
                    var row = Row(f.Param.Length > 0 ? f.Param : L.T("Lua 调用"), call, btn);
                    panel.Children.Add(row);
                    if (f.Tail != null)
                    {
                        var t = f.Tail!;
                        var t0 = new TextBox { Text = Gui.U32Text(t.T0), MinWidth = 90 };
                        var t1 = new TextBox { Text = Gui.U32Text(t.T1), MinWidth = 90 };
                        var t2 = new TextBox { Text = t.T2.ToString(), MinWidth = 40 };
                        var t3 = new TextBox { Text = Gui.U32Text(t.T3), MinWidth = 90 };
                        var ts = new TextBox { Text = t.S.Text, MinWidth = 200 };
                        t0.TextChanged += (_, _) => { if (!_building) Try(t0, v => t.T0 = v); };
                        t1.TextChanged += (_, _) => { if (!_building) Try(t1, v => t.T1 = v); };
                        t2.TextChanged += (_, _) => { if (!_building) { if (byte.TryParse(t2.Text, out byte b)) { t.T2 = b; _changed(); } } };
                        t3.TextChanged += (_, _) => { if (!_building) Try(t3, v => t.T3 = v); };
                        ts.TextChanged += (_, _) => { if (!_building) { t.S.SetText(ts.Text); _changed(); } };
                        panel.Children.Add(Row(L.T("条件尾部"), t0, t1, t2, t3, ts));
                    }
                    break;
                }
            }
            i++;
        }

        panel.Children.Add(new TextBlock
        {
            Text = L.T("⚠ 带“推断”的名称与位标志未经引擎证实；保存仅保证结构合法。"),
            Foreground = WpfTheme.WarnBrush,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        _building = false;
    }

    private void Try(TextBox box, Action<uint> set)
    {
        try { set(Gui.ParseU32(box.Text)); box.Foreground = WpfTheme.TextBrush; _changed(); }
        catch { box.Foreground = Brushes.Red; }
    }

    /// <summary>一行：左标签 + 若干控件横排。</summary>
    private static FrameworkElement Row(string label, params FrameworkElement[] controls)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        sp.Children.Add(new TextBlock
        {
            Text = label,
            Width = 120,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        foreach (var c in controls) sp.Children.Add(c);
        return sp;
    }
}

/// <summary>本地化字符串表编辑（行数固定；空槽位可留空但不可增删行）。</summary>
internal sealed class LocalizationGrid : UserControl
{
    private DocSession _doc = null!;
    private Changed _changed = null!;
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        IsReadOnly = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
    };

    public LocalizationGrid()
    {
        var colIdx = new DataGridTextColumn
        {
            Header = "#",
            Binding = new Binding(nameof(LocRow.Idx)),
            IsReadOnly = true,
            Width = new DataGridLength(40),
        };
        var colRole = new DataGridTextColumn
        {
            Header = L.T("角色 / 对应节点"),
            Binding = new Binding(nameof(LocRow.Role)),
            IsReadOnly = true,
            Width = new DataGridLength(260),
        };
        var colTxt = new DataGridTextColumn
        {
            Header = L.T("文本（{^n} 表示换行；行数不可增删）"),
            Binding = new Binding(nameof(LocRow.Text)) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        };
        var wrap = new Style(typeof(TextBlock));
        wrap.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        wrap.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Top));
        colTxt.ElementStyle = wrap;
        var edit = new Style(typeof(TextBox));
        edit.Setters.Add(new Setter(TextBox.AcceptsReturnProperty, true));
        edit.Setters.Add(new Setter(TextBox.TextWrappingProperty, TextWrapping.Wrap));
        colTxt.EditingElementStyle = edit;

        _grid.Columns.Add(colIdx);
        _grid.Columns.Add(colRole);
        _grid.Columns.Add(colTxt);
        WpfTheme.ApplyGrid(_grid);
        Content = _grid;
    }

    public void Bind(DocSession doc, Changed changed)
    {
        _doc = doc;
        _changed = changed;
        Rebuild();
    }

    public void Rebuild()
    {
        var texts = _doc.PrimaryLanguage.Texts;
        var rows = new List<LocRow>();
        for (int i = 0; i < texts.Count; i++)
        {
            string role = _doc.Cnv != null ? RoleCnv(i) : RoleQst(i);
            rows.Add(new LocRow(i, role, texts[i], () => { _doc.Dirty = true; _changed(); }));
        }
        _grid.ItemsSource = rows;
    }

    private string RoleCnv(int i)
    {
        var c = _doc.Cnv!;
        if (i == 0) return L.T("说话人名（根节点）");
        if (i < c.Steps.Count)
        {
            var s = c.Steps[i];
            return $"[{i}] {s.Kind}";
        }
        return L.T("（无对应节点）");
    }

    private static string RoleQst(int i) => i switch
    {
        0 => L.T("任务组名"),
        1 => L.T("任务名"),
        _ => L.F("按位置 {0}", i),
    };

    private sealed class LocRow
    {
        private readonly GdString _text;
        private readonly Changed _edited;
        public string Idx { get; }
        public string Role { get; }

        public LocRow(int idx, string role, GdString text, Changed edited)
        {
            Idx = idx.ToString();
            Role = role;
            _text = text;
            _edited = edited;
        }

        public string Text
        {
            get => _text.Text;
            set { _text.SetText(value); _edited(); }
        }
    }
}
