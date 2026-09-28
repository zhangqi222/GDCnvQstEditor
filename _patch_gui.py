"""GUI 体验修订（用户 6 条反馈）：树型层级、双击编辑、条件/动作独立、去代码感、统一语言、禁排序。"""
import io

def patch(path, pairs):
    s = io.open(path, encoding='utf-8').read()
    for i, (old, new) in enumerate(pairs):
        assert old in s, f'{path} 第 {i} 组锚点未找到:\n{old[:120]}'
        s = s.replace(old, new, 1)
    io.open(path, 'w', encoding='utf-8').write(s)
    print(f'{path}: {len(pairs)} 组替换完成')

# ═══ 1) lib：默认 body 版本（新建条件/动作时按语料取值，避免官方编辑器读错位）═══
patch('CnvQstLib/Scriptable.cs', [(
    '    public static string ConditionBodySpec(uint type) =>',
    '''    /// <summary>新建条件/动作时的默认 body 版本：语料里 HasFaction/HasItem/HasQuestObjective/LuaScript/
    /// CheckRandomValue 恒为 1，其余 0（版本错会让官方编辑器按另一种布局读，直接错位）。</summary>
    public static byte DefaultBodyVersion(uint type) => type switch
    {
        6 or 9 or 11 or 12 or 21 => 1,
        _ => 0,
    };

    public static string ConditionBodySpec(uint type) =>'''
)])

# ═══ 2) Theme：禁止一切列排序 ═══
patch('CnvQstStudio/Theme.cs', [(
    '        grid.RowTemplate.Height = 24;\n        grid.RowHeadersVisible = false;',
    '''        grid.RowTemplate.Height = 24;
        grid.RowHeadersVisible = false;
        // 禁止一切列排序：表格行序 = 文件内容顺序，排序只重排显示却让人误以为改了文件
        grid.ColumnAdded += (_, e) => e.Column.SortMode = DataGridViewColumnSortMode.NotSortable;'''
)])

# ═══ 3) CnvNodeLabel：中文类型名 + 缩进层级；树用 Plain ═══
patch('CnvQstStudio/TableEditors.cs', [(
    '''internal static class CnvNodeLabel
{
    public static string For(CnvFile c, CnvStep s)
    {
        string label = $"[{s.Index}] {s.Kind}";
        if (s.Tag != null) label += $" {{{s.Tag}}}";
        if (s.HasLink) label += $" →[{s.LinkId}]";
        if (!s.VoPath.IsEmpty) label += " [VO]";
        return label;
    }
}''',
    '''internal static class CnvNodeLabel
{
    /// <summary>节点类型的界面名（中文基准，走语言包；官方 ConversationStep.Type 枚举的对应项）。</summary>
    public static string KindZh(uint type) => L.T(type switch
    {
        0 => "根", 1 => "台词", 2 => "继续选项", 3 => "接受选项", 4 => "拒绝选项",
        5 => "通用", 6 => "结束对话", 7 => "跳转", 8 => "随机选项", _ => "未知类型",
    });

    /// <summary>结构树用：类型名 + 文本摘要（无缩进——层级由树的层级表达）。</summary>
    public static string Plain(CnvFile c, CnvStep s)
    {
        string text = c.TextFor(s).Replace("\\n", " ");
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
        string text = c.TextFor(s).Replace("\\n", " ");
        if (text.Length > 0)
        {
            if (text.Length > 24) text = text[..24] + "…";
            label += $"：{text}";
        }
        if (s.HasLink) label += L.T("（跳转）");
        return label;
    }
}''')
])

# ═══ 4) CNV 属性列：去掉 type=/flags= 十六进制，改中文开关 ═══
patch('CnvQstStudio/TableEditors.cs', [(
    '''    private static string AttrLabel(CnvStep s)
    {
        var parts = new List<string> { "type=" + s.Type };
        if (s.Unknown != 0) parts.Add($"flags=0x{s.Unknown:X}");
        if (!s.VoPath.IsEmpty) parts.Add($"vo={s.VoPath.Text}");
        return string.Join("  ", parts);
    }''',
    '''    private static string AttrLabel(CnvStep s)
    {
        // flags 低 3 位 = 编辑器里的三个开关（官方 ConversationStep.Flag），高位游戏侧保留不显示
        var parts = new List<string>();
        if ((s.Unknown & 1) != 0) parts.Add(L.T("仅一次"));
        if ((s.Unknown & 2) != 0) parts.Add(L.T("总是显示"));
        if ((s.Unknown & 4) != 0) parts.Add(L.T("不重要"));
        if (!s.VoPath.IsEmpty) parts.Add(L.T("有配音"));
        return string.Join("　", parts);
    }''')
])

# ═══ 5) 单击弹窗 → 双击弹窗（CNV / QST）═══
patch('CnvQstStudio/TableEditors.cs', [
    ('        _grid.CellClick += (_, e) => OnCellClick(e);\n        _grid.CellEndEdit += (_, e) => OnTextCommitted(e);',
     '        _grid.CellDoubleClick += (_, e) => OnCellClick(e);   // 双击才打开编辑窗口（单击只选中）\n        _grid.CellEndEdit += (_, e) => OnTextCommitted(e);'),
    ('        _grid.CellClick += (_, e) => OnCellClick(e);\n        _grid.CellPainting += PaintScriptCell;',
     '        _grid.CellDoubleClick += (_, e) => OnCellClick(e);   // 双击才打开编辑窗口（单击只选中）\n        _grid.CellPainting += PaintScriptCell;'),
])

# ═══ 6) 条件 / 动作编辑窗：加醒目横幅，逻辑下拉中文化 ═══
patch('CnvQstStudio/TableEditors.cs', [
    ('''        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 6, 0, 0) };
        if (isCondition)
        {
            top.Controls.Add(Gui.Label(L.T("关系逻辑", "Logic") + ":", 0, 3, 80));
            var logic = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            logic.Items.AddRange(new object[] { "AND (0)", "OR (1)" });''',
     '''        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 6, 0, 0) };
        var banner = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 8, 0),
            Font = new Font(Theme.UiFont, FontStyle.Bold),
            BackColor = isCondition ? Theme.AccentSoft : Color.FromArgb(0xE2, 0xF3, 0xEA),
            ForeColor = isCondition ? Theme.Accent : Theme.Ok,
            Text = isCondition
                ? L.T("⚑ 条件 —— 全部满足时，这段内容才会出现 / 生效")
                : L.T("⚡ 动作 —— 条件满足后要执行的效果"),
        };
        Controls.Add(banner);
        if (isCondition)
        {
            top.Controls.Add(Gui.Label(L.T("关系逻辑", "Logic") + ":", 0, 3, 80));
            var logic = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            logic.Items.AddRange(new object[] { L.T("满足全部（AND）"), L.T("满足任一（OR）") });'''),
    ('''            top.Controls.Add(Gui.Label(L.T("动作列表按格式本身不含关系逻辑字。", "Action lists carry no logic word by format."), 0, 3, 460));''',
     '''            top.Controls.Add(Gui.Label(L.T("动作按顺序执行（格式上没有 AND/OR 关系逻辑）。", "Action lists carry no logic word by format."), 0, 3, 460));'''),
])

# ═══ 7) QST 行：中文标签、隐藏内部十六进制 ═══
patch('CnvQstStudio/TableEditors.cs', [
    ('''internal abstract class QstRowOwner
{
    public abstract string NodeLabel { get; }''',
     '''internal abstract class QstRowOwner
{
    public abstract object OwnerObject { get; }   // 供结构树定位对应表格行
    public abstract string NodeLabel { get; }'''),
    ('''internal sealed class QstTaskRow : QstRowOwner
{
    public QstTask T;
    public QstTaskRow(QstTask t) => T = t;
    public override string NodeLabel => $"Task {T.Uid}/{T.Uid2}";
    public override string FieldLabel
    {
        get
        {
            var p = new List<string>();
            if (T.UnknownA8 != 0) p.Add($"@0xA8=0x{T.UnknownA8:X}");
            if (T.UnknownAC != 0) p.Add($"@0xAC=0x{T.UnknownAC:X}");
            if (T.Flag8) p.Add("flag8");
            if (T.Flag9) p.Add("flag9");
            return p.Count == 0 ? L.T("（无字段）") : string.Join("  ", p);
        }
    }
    public override bool HasScript => false;
    public override ScriptList? Conditions => null;
    public override ScriptList? Actions => null;
}''',
     '''internal sealed class QstTaskRow : QstRowOwner
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
}'''),
    ('''    public QstEvent E;
    public string Kind;
    public QstEventRow(QstEvent e, string kind) { E = e; Kind = kind; }
    public override string NodeLabel => $"{Kind} uid {E.Uid}";
    public override string FieldLabel => E.Unknown != 0 ? $"+0x2C=0x{E.Unknown:X}" : L.T("（无字段）");''',
     '''    public QstEvent E;
    public string Kind;
    public QstEventRow(QstEvent e, string kind) { E = e; Kind = kind; }
    public override object OwnerObject => E;
    public override string NodeLabel => Kind;
    public override string FieldLabel => E.Unknown != 0 ? L.F("标志 {0}", E.Unknown) : "";'''),
    ('''    public QstObjective O;
    public QstObjectiveRow(QstObjective o) => O = o;
    public override string NodeLabel => $"Objective uid {O.Uid}";
    public override string FieldLabel
    {
        get
        {
            var p = new List<string>();
            if (O.Unknown0 != 0) p.Add($"+0x10=0x{O.Unknown0:X}");
            if (O.Unknown1 != 0) p.Add($"+0x34=0x{O.Unknown1:X}");
            return p.Count == 0 ? L.T("（无字段）") : string.Join("  ", p);
        }
    }''',
     '''    public QstObjective O;
    public QstObjectiveRow(QstObjective o) => O = o;
    public override object OwnerObject => O;
    public override string NodeLabel => L.T("目标");
    public override string FieldLabel => "";'''),
])

# ═══ 8) QST 绑定：中文行标签 + 序号；工具栏中文化；SelectOwner ═══
patch('CnvQstStudio/TableEditors.cs', [
    ('''        foreach (var t in _qst.Tasks)
        {
            _rows.Add(new QstTaskRow(t));
            _grid.Rows.Add(RowValues(_rows[^1]));
            foreach (var inc in t.Incentives) { _rows.Add(new QstEventRow(inc, "Incentive")); _grid.Rows.Add(RowValues(_rows[^1])); }
            foreach (var o in t.Objectives) { _rows.Add(new QstObjectiveRow(o)); _grid.Rows.Add(RowValues(_rows[^1])); }
            foreach (var e in t.Events) { _rows.Add(new QstEventRow(e, "Event")); _grid.Rows.Add(RowValues(_rows[^1])); }
        }''',
     '''        int no = 1;
        foreach (var t in _qst.Tasks)
        {
            _rows.Add(new QstTaskRow(t, no++));
            _grid.Rows.Add(RowValues(_rows[^1]));
            foreach (var inc in t.Incentives) { _rows.Add(new QstEventRow(inc, L.T("接受事件"))); _grid.Rows.Add(RowValues(_rows[^1])); }
            foreach (var o in t.Objectives) { _rows.Add(new QstObjectiveRow(o)); _grid.Rows.Add(RowValues(_rows[^1])); }
            foreach (var e in t.Events) { _rows.Add(new QstEventRow(e, L.T("完成事件"))); _grid.Rows.Add(RowValues(_rows[^1])); }
        }'''),
    ('''        _bar.Items.Add(new ToolStripButton(L.T("＋Task", "+ Task"), null, (_, _) => AddTask()));
        _bar.Items.Add(new ToolStripButton(L.T("＋Objective", "+ Objective"), null, (_, _) => AddObjective()));
        _bar.Items.Add(new ToolStripButton(L.T("＋Event", "+ Event"), null, (_, _) => AddEvent()));
        _bar.Items.Add(new ToolStripButton(L.T("＋Incentive", "+ Incentive"), null, (_, _) => AddIncentive()));''',
     '''        _bar.Items.Add(new ToolStripButton(L.T("＋任务"), null, (_, _) => AddTask()));
        _bar.Items.Add(new ToolStripButton(L.T("＋目标"), null, (_, _) => AddObjective()));
        _bar.Items.Add(new ToolStripButton(L.T("＋完成事件"), null, (_, _) => AddEvent()));
        _bar.Items.Add(new ToolStripButton(L.T("＋接受事件"), null, (_, _) => AddIncentive()))'''),
])

# ═══ 9) 树定位表格行的方法 ═══
patch('CnvQstStudio/TableEditors.cs', [
    ('''    private void SetStatus(string text) => _status.Text = text;''',
     '''    private void SetStatus(string text) => _status.Text = text;

    /// <summary>结构树定位：选中对应表格行（找不到就忽略）。</summary>
    public void SelectOwner(object owner)
    {
        int i = _rows.FindIndex(r => ReferenceEquals(r.OwnerObject, owner));
        if (i < 0) return;
        _grid.ClearSelection();
        _grid.Rows[i].Selected = true;
        _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, i - 3);
    }'''),
    ('''    private string textOf(CnvStep s) => _doc.Cnv!.TextFor(s);''',
     '''    private string textOf(CnvStep s) => _doc.Cnv!.TextFor(s);

    /// <summary>结构树定位：选中对应表格行。</summary>
    public void SelectStep(CnvStep s)
    {
        int i = _steps.IndexOf(s);
        if (i < 0) return;
        _grid.ClearSelection();
        _grid.Rows[i].Selected = true;
        _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, i - 3);
    }'''),
])

# ═══ 10) CNV 节点属性对话框：中文 + 三个开关复选框 ═══
patch('CnvQstStudio/TableEditors.cs', [
    ('''    private readonly CnvStep _step;
    private readonly CnvFile _cnv;
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TextBox _unknown = new() { Width = 130 };
    private readonly TextBox _voPath = new() { Width = 320 };
    private readonly ComboBox _link = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };''',
     '''    private readonly CnvStep _step;
    private readonly CnvFile _cnv;
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly CheckBox _once = new() { Text = L.T("仅一次（Once Only）"), AutoSize = true };
    private readonly CheckBox _always = new() { Text = L.T("总是显示（Always Show）"), AutoSize = true };
    private readonly CheckBox _unimportant = new() { Text = L.T("不重要（Unimportant）"), AutoSize = true };
    private readonly TextBox _voPath = new() { Width = 320 };
    private readonly ComboBox _link = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };'''),
    ('''        for (uint t = 0; t <= 8; t++) _type.Items.Add($"{t} {Names.CnvStepKind(t)}");
        _type.SelectedIndex = s.Type <= 8 ? (int)s.Type : 0;
        _unknown.Text = Gui.U32Text(s.Unknown);
        _voPath.Text = s.VoPath.Text;''',
     '''        for (uint t = 0; t <= 8; t++) _type.Items.Add(CnvNodeLabel.KindZh(t));
        _type.SelectedIndex = s.Type <= 8 ? (int)s.Type : 0;
        _once.Checked = (s.Unknown & 1) != 0;
        _always.Checked = (s.Unknown & 2) != 0;
        _unimportant.Checked = (s.Unknown & 4) != 0;
        _voPath.Text = s.VoPath.Text;'''),
    ('''        int y = 16;
        Controls.Add(Gui.Label(L.T("类型", "Type") + ":", 14, y + 3, 80)); _type.Location = new Point(110, y); y += 34;
        Controls.Add(Gui.Label("flags:", 14, y + 3, 80)); _unknown.Location = new Point(110, y); y += 30;
        Controls.Add(Gui.Label("voPath:", 14, y + 3, 80)); _voPath.Location = new Point(110, y); y += 34;
        Controls.Add(Gui.Label("linkId:", 14, y + 3, 80)); _link.Location = new Point(110, y); y += 42;''',
     '''        int y = 16;
        Controls.Add(Gui.Label(L.T("类型") + ":", 14, y + 3, 80)); _type.Location = new Point(110, y); y += 34;
        Controls.Add(Gui.Label(L.T("显示开关") + ":", 14, y + 3, 80)); y += 26;
        _once.Location = new Point(114, y); _always.Location = new Point(260, y); _unimportant.Location = new Point(430, y); y += 30;
        Controls.Add(_once); Controls.Add(_always); Controls.Add(_unimportant);
        Controls.Add(Gui.Label(L.T("配音文件") + ":", 14, y + 3, 80)); _voPath.Location = new Point(110, y); y += 34;
        Controls.Add(Gui.Label(L.T("跳转到") + ":", 14, y + 3, 80)); _link.Location = new Point(110, y); y += 42;'''),
    ('''        s.Type = (uint)dlg._type.SelectedIndex;
        try { s.Unknown = Gui.ParseU32(dlg._unknown.Text); } catch { /* 忽略非法 */ }
        s.VoPath.SetText(dlg._voPath.Text);''',
     '''        s.Type = (uint)dlg._type.SelectedIndex;
        // 只改低 3 位（编辑器开关），高 3 位为游戏侧保留位，原样保留
        s.Unknown = (s.Unknown & ~7u) | (dlg._once.Checked ? 1u : 0u) | (dlg._always.Checked ? 2u : 0u) | (dlg._unimportant.Checked ? 4u : 0u);
        s.VoPath.SetText(dlg._voPath.Text);'''),
])
print('全部替换完成')
