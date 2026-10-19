using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CnvQst;

namespace CnvQstStudio;

/// <summary>
/// 条件/动作列表编辑器：三个明确分区的区域 —— 已有项列表（删除/复制/排序）、添加新项、编辑选中项详细字段。
/// </summary>
internal sealed class ScriptListEditor : UserControl
{
    private DocSession _doc;
    private ScriptList _list;
    private readonly bool _isCondition;
    private Changed _changed;

    private readonly ListBox _records = new() { MinHeight = 110 };
    private readonly ComboBox _addType = new() { MinWidth = 240 };
    private readonly ComboBox _typeCombo = new() { MinWidth = 360 };
    // 比较符（官方 Common.ScriptableCondition+Comparison：Less=0…NotEqual=5），下拉选择，不暴露数字
    private readonly ComboBox _cmp = new() { MinWidth = 90 };
    private readonly TextBlock _cmpLabel = new() { Text = L.T("比较符", "Comparator"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) };
    private readonly FieldsPanel _fields;
    private readonly GroupBox _existingBox = new();
    private readonly Button _add, _del, _dup, _up, _down;
    private bool _building;

    public ScriptListEditor(DocSession doc, ScriptList list, bool isCondition, Changed changed)
    {
        _doc = doc;
        _list = list;
        _isCondition = isCondition;
        _changed = changed;
        _fields = new FieldsPanel(OnEdit);
        _add = MkBtn(L.T("＋ 添加", "+ Add"), AddRecord);
        _del = MkBtn(L.T("删除", "Delete"), DelRecord);
        _dup = MkBtn(L.T("复制", "Copy"), DupRecord);
        _up = MkBtn(L.T("上移", "Up"), () => MoveRecord(-1));
        _down = MkBtn(L.T("下移", "Down"), () => MoveRecord(1));

        Content = BuildLayout();
        FillTypeCombo(_addType);
        FillTypeCombo(_typeCombo);
        _cmp.ItemsSource = new[] { "<", "<=", "=", ">=", ">", "!=" };

        _records.SelectionChanged += (_, _) => RebuildDetail();
        _typeCombo.SelectionChanged += (_, _) => ChangeType();
        _cmp.SelectionChanged += (_, _) =>
        {
            if (_building) return;
            if (Current() is { } r && r.IsCondition) { r.Uid = (uint)Math.Max(0, _cmp.SelectedIndex); OnEdit(); }
        };

        Rebind(doc, list, changed);
    }

    /// <summary>换选节点/条目时重绑定到新的列表。</summary>
    public void Rebind(DocSession doc, ScriptList list, Changed changed)
    {
        _doc = doc;
        _list = list;
        _changed = changed;
        Rebuild();
    }

    private UIElement BuildLayout()
    {
        // ── 已有项区：标题 + 列表 + 管理按钮 ──
        _existingBox.Header = _isCondition ? L.T("已有条件", "Existing Conditions") : L.T("已有动作", "Existing Actions");
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 0, 4) };
        foreach (var b in new[] { _del, _dup, _up, _down }) btns.Children.Add(b);
        var existingStack = new StackPanel();
        existingStack.Children.Add(_records);
        existingStack.Children.Add(btns);
        _existingBox.Content = existingStack;

        // ── 添加区：类型下拉 + 添加按钮 ──
        var addBox = new GroupBox
        {
            Header = _isCondition ? L.T("添加新条件", "Add Condition") : L.T("添加新动作", "Add Action"),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = L.T("类型", "Type"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) },
                    _addType,
                    _add,
                },
            },
        };

        // ── 编辑选中项区：类型/比较符 + 字段面板 ──
        var editTop = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 6),
            Children =
            {
                new TextBlock { Text = L.T("类型", "Type"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) },
                _typeCombo,
                                _cmpLabel,
                _cmp,
            },
        };
        var editBox = new GroupBox
        {
            Header = L.T("编辑选中项", "Edit Selected"),
            Content = new DockPanel(),
        };
        var editDock = (DockPanel)editBox.Content;
        editDock.Children.Add(editTop);
        DockPanel.SetDock(editTop, Dock.Top);
        editDock.Children.Add(_fields);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(230) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_existingBox, 0);
        Grid.SetRow(addBox, 1);
        Grid.SetRow(editBox, 2);
        root.Children.Add(_existingBox);
        root.Children.Add(addBox);
        root.Children.Add(editBox);
        return root;
    }

    private static Button MkBtn(string text, Action onClick)
    {
        var b = new Button { Content = text, MinWidth = 64, Margin = new Thickness(0, 0, 6, 0) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void FillTypeCombo(ComboBox combo)
    {
        var items = new List<ComboType>();
        for (uint t = 0; ; t++)
        {
            string? name = _isCondition ? NameOfCond(t) : NameOfAct(t);
            if (name == null) break;
            if (!_isCondition && (t == 0 || t == 16)) continue;   // 动作工厂对 0/16 返回 null
            items.Add(new ComboType(t, name));
        }
        combo.ItemsSource = items;
        combo.DisplayMemberPath = nameof(ComboType.Text);
        if (items.Count > 0) combo.SelectedIndex = 0;
    }

    private sealed record ComboType(uint Id, string Text);

    private static string? NameOfCond(uint t) => t < 30 ? Names.ConditionLabel(t) : null;
    private static string? NameOfAct(uint t) => t < 29 ? Names.ActionLabel(t) : null;

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
        RebuildDetail();
        _building = false;
    }

    private void RebuildDetail()
    {
        _building = true;
        var r = Current();
        bool has = r != null;
        _typeCombo.IsEnabled = _cmp.IsEnabled = _fields.IsEnabled = has;
        _del.IsEnabled = _dup.IsEnabled = _up.IsEnabled = _down.IsEnabled = has;
        if (!has)
        {
            _fields.Bind(null);
            _building = false;
            return;
        }
        var items = (List<ComboType>)_typeCombo.ItemsSource;
        for (int i = 0; i < items.Count; i++)
            if (items[i].Id == r!.Type) { _typeCombo.SelectedIndex = i; break; }
        _cmp.SelectedIndex = (int)Math.Min(r.Uid, 5);
        Visibility cmpVis = _isCondition ? Visibility.Visible : Visibility.Collapsed;
        _cmp.Visibility = cmpVis;
        _cmpLabel.Visibility = cmpVis;
        _fields.Bind(r);
        _building = false;
    }

    private void OnEdit()
    {
        if (_building) return;
        int sel = _records.SelectedIndex;
        if (sel >= 0)
        {
            var items = (List<string>)_records.ItemsSource;
            items[sel] = (_isCondition ? "⚑ " : "⚡ ") + Gui.Describe(_list.Records[sel]);
            _records.ItemsSource = null;
            _records.ItemsSource = items;
            _records.SelectedIndex = sel;
        }
        RebuildHeader();
        _changed();
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
        uint type = _addType.SelectedItem is ComboType ct ? ct.Id : 0;
        var rec = new ScriptRecord
        {
            IsCondition = _isCondition,
            Type = type,
            SubType = Scriptable.DefaultBodyVersion(type),
            Uid = 2,
            BodySpec = _isCondition ? SpecCond(type) : SpecAct(type),
            Named = _isCondition ? Names.ConditionClass(type).named : Names.ActionClass(type).named,
        };
        rec.Fields = FieldFactory.Resync(rec.BodySpec, rec.SubType, null, _isCondition);
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
        var copy = CloneRecord(src);
        int at = _records.SelectedIndex + 1;
        _list.Records.Insert(at, copy);
        _list.Count = _list.Records.Count;
        Rebuild();
        _records.SelectedIndex = at;
        _changed();
    }

    internal static ScriptRecord CloneRecord(ScriptRecord src) => new()
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

    private void ChangeType()
    {
        if (_building) return;
        var r = Current();
        if (r == null || _typeCombo.SelectedItem is not ComboType ct) return;
        r.Type = ct.Id;
        r.SubType = Scriptable.DefaultBodyVersion(ct.Id);
        r.BodySpec = _isCondition ? SpecCond(ct.Id) : SpecAct(ct.Id);
        var named = _isCondition ? Names.ConditionClass(ct.Id) : Names.ActionClass(ct.Id);
        r.ClassName = named.name;
        r.Named = named.named;
        r.Fields = FieldFactory.Resync(r.BodySpec, r.SubType, r.Fields, _isCondition);
        Rebuild();
        _changed();
    }

    // body 规格表统一取自库
    private static string SpecCond(uint t) => t < 30 ? Scriptable.ConditionBodySpec(t) : "";
    private static string SpecAct(uint t) => t < 29 ? Scriptable.ActionBodySpec(t) : "";
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
    private bool _building;

    public LocalizationGrid()
    {
        var colIdx = MkCol("#", 40, isReadOnly: true, binding: new Binding(nameof(LocRow.Idx)));
        var colRole = MkCol(L.T("角色 / 对应节点"), 260, isReadOnly: true, binding: new Binding(nameof(LocRow.Role)));
        var colTxt = MkCol(L.T("文本（{^n} 表示换行；行数不可增删）"), 600, isReadOnly: false,
            binding: new Binding(nameof(LocRow.Text)) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
        colTxt.ElementStyle = MkWrapStyle(maxHeight: double.PositiveInfinity);
        colTxt.EditingElementStyle = MkEditStyle();
        _grid.Columns.Add(colIdx);
        _grid.Columns.Add(colRole);
        _grid.Columns.Add(colTxt);
        Content = _grid;
    }

    private static DataGridTextColumn MkCol(string header, double width, bool isReadOnly, Binding binding)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = binding,
            IsReadOnly = isReadOnly,
            Width = new DataGridLength(width, DataGridLengthUnitType.Star),
        };
    }

    private static Style MkWrapStyle(double maxHeight)
    {
        var s = new Style(typeof(TextBlock));
        s.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        s.Setters.Add(new Setter(TextBlock.MaxHeightProperty, maxHeight));
        s.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Top));
        return s;
    }

    private static Style MkEditStyle()
    {
        var s = new Style(typeof(TextBox));
        s.Setters.Add(new Setter(TextBox.AcceptsReturnProperty, true));
        s.Setters.Add(new Setter(TextBox.TextWrappingProperty, TextWrapping.Wrap));
        return s;
    }

    public void Bind(DocSession doc, Changed changed)
    {
        _doc = doc;
        _changed = changed;
        Rebuild();
    }

    public void Rebuild()
    {
        _building = true;
        var texts = _doc.PrimaryLanguage.Texts;
        var rows = new List<LocRow>();
        for (int i = 0; i < texts.Count; i++)
        {
            string role = _doc.Cnv != null ? RoleCnv(i) : RoleQst(i);
            rows.Add(new LocRow(i, role, texts[i], () => { _doc.Dirty = true; _changed(); }));
        }
        _grid.ItemsSource = rows;
        _building = false;
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
