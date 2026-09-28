using CnvQst;

namespace CnvQstStudio;

/// <summary>GUI 小工具。</summary>
internal static class Gui
{
    public static Label Label(string text, int x, int y, int w = 120) =>
        new() { Text = text, Location = new Point(x, y), Size = new Size(w, 22), TextAlign = ContentAlignment.MiddleLeft };

    public static TextBox Box(int x, int y, int w, string text = "", bool multiline = false, int h = 0)
    {
        var b = new TextBox
        {
            Location = new Point(x, y),
            Width = w,
            Text = text,
            Multiline = multiline,
        };
        if (multiline) b.Height = h;
        return b;
    }

    /// <summary>u32 显示：大数值按 hex（多为 hash/任务标识），小数值按十进制。</summary>
    public static string U32Text(uint v) => v >= 0x10000 ? "0x" + v.ToString("X") : v.ToString();

    public static uint ParseU32(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToUInt32(s[2..], 16);
        return string.IsNullOrEmpty(s) ? 0 : uint.Parse(s);
    }

    public static string Describe(ScriptRecord r) => Dump.Describe(r);
}

/// <summary>编辑变化回调载体：刷新脏标记/状态栏/树节点。</summary>
internal delegate void Changed();

/// <summary>
/// 条件/动作列表编辑器：三个明确分区的区域 —— 已有项列表（删除/复制/排序）、添加新项、编辑选中项详细字段。
/// </summary>
internal sealed class ScriptListEditor : Panel
{
    private DocSession _doc;
    private ScriptList _list;
    private readonly bool _isCondition;
    private Changed _changed;

    /// <summary>换选节点/条目时重绑定到新的列表。</summary>
    public void Rebind(DocSession doc, ScriptList list, Changed changed)
    {
        _doc = doc;
        _list = list;
        _changed = changed;
        Rebuild();
    }

    private readonly GroupBox _existingBox = new() { Dock = DockStyle.Top, Height = 214 };
    private readonly ListBox _records = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false };
    private readonly ComboBox _addType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly Button _add = new() { Text = L.T("＋ 添加", "+ Add"), Width = 76 };
    private readonly Button _del = new() { Text = L.T("删除", "Delete"), Width = 64 };
    private readonly Button _dup = new() { Text = L.T("复制", "Copy"), Width = 64 };
    private readonly Button _up = new() { Text = L.T("上移", "Up"), Width = 56 };
    private readonly Button _down = new() { Text = L.T("下移", "Down"), Width = 56 };
    private readonly GroupBox _addBox = new() { Dock = DockStyle.Top, Height = 58 };
    private readonly GroupBox _editBox = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _typeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    // 比较符（官方 Common.ScriptableCondition+Comparison：Less=0…NotEqual=5），下拉选择，不暴露数字
    private readonly ComboBox _cmp = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly Label _cmpLabel = Gui.Label(L.T("比较符", "Comparator"), 0, 0, 56);
    private readonly DynamicFieldPanel _fields;
    private bool _building;

    public ScriptListEditor(DocSession doc, ScriptList list, bool isCondition, Changed changed, int width)
    {
        _doc = doc;
        _list = list;
        _isCondition = isCondition;
        _changed = changed;
        _fields = new DynamicFieldPanel(() => { OnEdit(); }, width);
        Width = width;

        _existingBox.Text = L.T("已有条件", "Existing Conditions");
        _addBox.Text = L.T("添加新条件", "Add Condition");
        _editBox.Text = L.T("编辑选中项", "Edit Selected");
        if (!isCondition)
        {
            _existingBox.Text = L.T("已有动作", "Existing Actions");
            _addBox.Text = L.T("添加新动作", "Add Action");
        }

        // ── 已有项区：列表 + 管理按钮 ──
        var existingBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(6, 2, 0, 0), WrapContents = false };
        existingBtns.Controls.AddRange(new Control[] { _del, _dup, _up, _down });
        _existingBox.Controls.Add(_records);
        _existingBox.Controls.Add(existingBtns);
        _records.BringToFront();

        // ── 添加区：类型下拉 + 添加按钮 ──
        var addLabel = Gui.Label(L.T("类型", "Type"), 64, 24, 60);
        _addBox.Controls.Add(addLabel);
        _addType.Location = new Point(64, 20);
        _addBox.Controls.Add(_addType);
        _add.Location = new Point(312, 18);
        _addBox.Controls.Add(_add);

        // ── 编辑选中项区：类型/subType/uid + 字段面板 ──
        var editTop = new Panel { Dock = DockStyle.Top, Height = 40 };
        var eLabel = Gui.Label(L.T("类型", "Type"), 12, 10, 44);
        _cmpLabel.Location = new Point(452, 10);
        _cmp.Items.AddRange(new object[] { "<", "<=", "=", ">=", ">", "!=" });
        _cmp.Location = new Point(512, 8);
        _typeCombo.Location = new Point(60, 8);
        _typeCombo.Width = 380;
        editTop.Controls.AddRange(new Control[] { eLabel, _typeCombo, _cmpLabel, _cmp });
        _fields.Dock = DockStyle.Fill;
        _editBox.Controls.Add(_fields);
        _editBox.Controls.Add(editTop);
        _fields.BringToFront();

        // 三区堆叠：已有(顶)、添加(中)、编辑(剩余)
        Controls.Add(_addBox);
        Controls.Add(_existingBox);
        Controls.Add(_editBox);
        _editBox.BringToFront();

        _records.SelectedIndexChanged += (_, _) => RebuildDetail();
        _add.Click += (_, _) => AddRecord();
        _del.Click += (_, _) => DelRecord();
        _dup.Click += (_, _) => DupRecord();
        _up.Click += (_, _) => MoveRecord(-1);
        _down.Click += (_, _) => MoveRecord(1);
        _typeCombo.SelectedIndexChanged += (_, _) => ChangeType();
        _cmp.SelectedIndexChanged += (_, _) =>
        {
            if (_building) return;
            if (Current() is { } r && r.IsCondition) { r.Uid = (uint)Math.Max(0, _cmp.SelectedIndex); OnEdit(); }
        };

        FillTypeCombo(_addType);
        FillTypeCombo(_typeCombo);
        Rebuild();
    }

    private void FillTypeCombo(ComboBox combo)
    {
        var items = new List<object>();
        for (uint t = 0; ; t++)
        {
            string? name = _isCondition ? NameOfCond(t) : NameOfAct(t);
            if (name == null) break;
            if (!_isCondition && (t == 0 || t == 16)) continue;   // 动作工厂对 0/16 返回 null
            items.Add(new ComboType(t, name));
        }
        combo.Items.AddRange(items.ToArray());
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private sealed record ComboType(uint Id, string Text)
    {
        public override string ToString() => Text;
    }

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
        _records.BeginUpdate();
        _records.Items.Clear();
        foreach (var r in _list.Records)
            _records.Items.Add((_isCondition ? "⚑ " : "⚡ ") + Gui.Describe(r));
        if (sel >= 0 && sel < _records.Items.Count) _records.SelectedIndex = sel;
        else if (_records.Items.Count > 0) _records.SelectedIndex = 0;
        _records.EndUpdate();
        RebuildDetail();
        _building = false;
    }

    private void RebuildDetail()
    {
        _building = true;
        var r = Current();
        bool has = r != null;
        _typeCombo.Enabled = _cmp.Enabled = _fields.Enabled = has;
        _del.Enabled = _dup.Enabled = _up.Enabled = _down.Enabled = has;
        if (!has)
        {
            _fields.Bind(null);
            _building = false;
            return;
        }
        for (int i = 0; i < _typeCombo.Items.Count; i++)
            if (_typeCombo.Items[i] is ComboType ct && ct.Id == r!.Type) { _typeCombo.SelectedIndex = i; break; }
        _cmp.SelectedIndex = (int)Math.Min(r.Uid, 5);
        _cmp.Visible = _cmpLabel.Visible = _isCondition;
        _fields.Bind(r);
        _building = false;
    }

    private void OnEdit()
    {
        if (_building) return;
        int sel = _records.SelectedIndex;
        if (sel >= 0) _records.Items[sel] = (_isCondition ? "⚑ " : "⚡ ") + Gui.Describe(_list.Records[sel]);
        RebuildHeader();
        _changed();
    }

    private void RebuildHeader()
    {
        string cn = L.T(_isCondition ? "条件" : "动作");
        // en 变量已并入语言包（"已有{0}…" 键），不再需要英文名拼接
        string logic = _isCondition ? $"  · {Names.LogicWord(_list.Logic)} ({_list.Logic})" : "";
        string empty = _list.Present ? "" : L.T("  （空列表）", "  (empty)");
        _existingBox.Text = L.F("已有{0}（{1} 条）{2}{3}", cn, _list.Records.Count, logic, empty);
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

    private static uint _uidSeed = 1;
    private static uint NextUid() => _uidSeed++;

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

    // body 规格表统一取自库（原先是本文件里手抄的一份，容易与 Scriptable 脱节）
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
internal sealed class DynamicFieldPanel : Panel
{
    private readonly Changed _changed;
    private ScriptRecord? _rec;
    private bool _building;

    public DynamicFieldPanel(Changed changed, int width)
    {
        _changed = changed;
        Width = width;
        AutoScroll = true;
        Height = 180;
    }

    public void Bind(ScriptRecord? rec)
    {
        _rec = rec;
        Rebuild();
    }

    private void Rebuild()
    {
        _building = true;
        SuspendLayout();
        Controls.Clear();
        if (_rec == null)
        {
            Controls.Add(new Label { Text = L.T("（未选择记录）"), AutoSize = true, ForeColor = Theme.Muted });
            ResumeLayout();
            _building = false;
            return;
        }

        int y = 0;
        int i = 0;
        foreach (var f in _rec.Fields)
        {
            switch (f.Kind)
            {
                case FieldKind.Str:
                {
                    Controls.Add(Gui.Label(f.Param.Length > 0 ? f.Param : L.F("串[{0}]", i), 0, y + 3, 60));
                    var box = Gui.Box(66, y, Math.Min(Width - 80, 460), f.S.Text);
                    box.TextChanged += (_, _) => { if (_building) return; f.S.SetText(box.Text); _changed(); };
                    Controls.Add(box);
                    y += 28;
                    break;
                }
                case FieldKind.U32:
                {
                    Controls.Add(Gui.Label(f.Param.Length > 0 ? f.Param : L.F("数值[{0}]", i), 0, y + 3, 60));
                    var box = Gui.Box(66, y, 150, Gui.U32Text(f.U));
                    var note = new Label
                    {
                        Text = f.Param.Length > 0 ? f.Note : L.T("（语义未知；支持 0x 十六进制）"),
                        AutoSize = true,
                        ForeColor = Color.Gray,
                        Location = new Point(224, y + 3),
                    };
                    box.TextChanged += (_, _) =>
                    {
                        if (_building) return;
                        try { f.U = Gui.ParseU32(box.Text); box.ForeColor = Color.Black; }
                        catch { box.ForeColor = Color.Red; return; }
                        _changed();
                    };
                    Controls.Add(box);
                    Controls.Add(note);
                    y += 28;
                    break;
                }
                case FieldKind.U8:
                {
                    var cb = new CheckBox
                    {
                        Text = (f.Param.Length > 0 ? f.Param : L.F("布尔[{0}]", i)) + L.T("（0=False / 非0=True）"),
                        AutoSize = true,
                        Checked = f.B != 0,
                        Location = new Point(0, y),
                    };
                    cb.CheckedChanged += (_, _) => { if (_building) return; f.B = cb.Checked ? (byte)1 : (byte)0; _changed(); };
                    Controls.Add(cb);
                    y += 26;
                    break;
                }
                case FieldKind.P:
                {
                    Controls.Add(Gui.Label(f.Param.Length > 0 ? f.Param : L.F("路径×{0}", f.Paths!.Count), 0, y + 3, 60));
                    var btn = new Button { Text = L.T("编辑路径列表…"), Location = new Point(66, y), Width = 130 };
                    btn.Click += (_, _) =>
                    {
                        using var dlg = new StringListDialog(L.T("编辑路径列表"), f.Paths!.Select(p => p.Text).ToList());
                        if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                        {
                            f.Paths = dlg.Items.Select(s => new GdString(s)).ToList();
                            f.Count = f.Paths.Count;
                            _changed();
                        }
                    };
                    Controls.Add(btn);
                    Controls.Add(Gui.Label(L.F("尾字={0}", f.Trailing), 204, y + 3, 200));
                    y += 30;
                    break;
                }
                case FieldKind.Lua:
                {
                    Controls.Add(Gui.Label(f.Param.Length > 0 ? f.Param : L.T("Lua 调用"), 0, y + 3, 60));
                    var call = Gui.Box(66, y, 300, f.S.Text);
                    call.TextChanged += (_, _) => { if (_building) return; f.S.SetText(call.Text); _changed(); };
                    Controls.Add(call);
                    var btn = new Button { Text = L.F("参数×{0}…", f.Params!.Count), Location = new Point(374, y), Width = 110 };
                    btn.Click += (_, _) =>
                    {
                        using var dlg = new LuaParamsDialog(f);
                        if (dlg.ShowDialog(FindForm()) == DialogResult.OK) _changed();
                    };
                    Controls.Add(btn);
                    y += 30;
                    if (f.Tail != null)
                    {
                        Controls.Add(Gui.Label(L.T("条件尾部"), 0, y + 3, 60));
                        var t = f.Tail!;
                        var t0 = Gui.Box(66, y, 90, Gui.U32Text(t.T0));
                        var t1 = Gui.Box(162, y, 90, Gui.U32Text(t.T1));
                        var t2 = Gui.Box(258, y, 40, t.T2.ToString());
                        var t3 = Gui.Box(304, y, 90, Gui.U32Text(t.T3));
                        var ts = Gui.Box(400, y, 200, t.S.Text);
                        t0.TextChanged += (_, _) => { if (!_building) Try(t0, v => t.T0 = v); };
                        t1.TextChanged += (_, _) => { if (!_building) Try(t1, v => t.T1 = v); };
                        t2.TextChanged += (_, _) => { if (!_building) { if (byte.TryParse(t2.Text, out byte b)) { t.T2 = b; _changed(); } } };
                        t3.TextChanged += (_, _) => { if (!_building) Try(t3, v => t.T3 = v); };
                        ts.TextChanged += (_, _) => { if (!_building) { t.S.SetText(ts.Text); _changed(); } };
                        Controls.AddRange(new Control[] { t0, t1, t2, t3, ts });
                        y += 30;
                    }
                    break;
                }
            }
            i++;
        }

        var warn = new Label
        {
            Text = L.T("⚠ 带“推断”的名称与位标志未经引擎证实；保存仅保证结构合法。"),
            AutoSize = true,
            ForeColor = Color.SaddleBrown,
            Location = new Point(0, y + 4),
        };
        Controls.Add(warn);
        ResumeLayout();
        _building = false;
    }

    private void Try(TextBox box, Action<uint> set)
    {
        try { set(Gui.ParseU32(box.Text)); box.ForeColor = Color.Black; _changed(); }
        catch { box.ForeColor = Color.Red; }
    }
}

/// <summary>本地化字符串表编辑（行数固定；空槽位可留空但不可增删行）。</summary>
internal sealed class LocalizationGrid : Panel
{
    private DocSession _doc = null!;
    private Changed _changed = null!;
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
    };
    private bool _building;

    public LocalizationGrid()
    {
        Controls.Add(_grid);
        var colIdx = new DataGridViewTextBoxColumn { Name = "idx", HeaderText = "#", ReadOnly = true, FillWeight = 8 };
        var colRole = new DataGridViewTextBoxColumn { Name = "role", HeaderText = L.T("角色 / 对应节点"), ReadOnly = true, FillWeight = 30 };
        var colTxt = new DataGridViewTextBoxColumn { Name = "text", HeaderText = L.T("文本（{^n} 表示换行；行数不可增删）"), FillWeight = 62 };
        _grid.Columns.AddRange(new DataGridViewColumn[] { colIdx, colRole, colTxt });
        Theme.StyleGrid(_grid);
        _grid.CellEndEdit += (_, e) =>
        {
            if (_building || e.RowIndex < 0) return;
            var texts = _doc.PrimaryLanguage.Texts;
            if (e.RowIndex < texts.Count)
            {
                string v = _grid.Rows[e.RowIndex].Cells["text"].Value?.ToString() ?? "";
                texts[e.RowIndex].SetText(v);
                _changed();
            }
        };
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
        _grid.Rows.Clear();
        var texts = _doc.PrimaryLanguage.Texts;
        for (int i = 0; i < texts.Count; i++)
        {
            string role = _doc.Cnv != null ? RoleCnv(i) : RoleQst(i);
            _grid.Rows.Add(i.ToString(), role, texts[i].Text);
            if (texts[i].IsEmpty)
                _grid.Rows[i].Cells["text"].Style.ForeColor = Color.Gray;
        }
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
}
