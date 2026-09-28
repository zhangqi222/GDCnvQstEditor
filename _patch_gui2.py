"""GUI 修订（第二批）：树定位方法、节点属性对话框中文化、比较符下拉、DocView 树。"""
import io

def patch(path, pairs):
    s = io.open(path, encoding='utf-8').read()
    for i, (old, new) in enumerate(pairs):
        assert old in s, f'{path} 第 {i} 组锚点未找到:\n{old[:140]}'
        s = s.replace(old, new, 1)
    io.open(path, 'w', encoding='utf-8').write(s)
    print(f'{path}: {len(pairs)} 组替换完成')

# ═══ A) 表格定位方法（供结构树调用）═══
patch('CnvQstStudio/TableEditors.cs', [
    ('''    private void AddChild()
    {
        if (_grid.SelectedRows.Count == 0) return;''',
     '''    /// <summary>结构树定位：选中对应表格行。</summary>
    public void SelectStep(CnvStep s)
    {
        int i = _steps.IndexOf(s);
        if (i < 0) return;
        _grid.ClearSelection();
        _grid.Rows[i].Selected = true;
        _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, i - 3);
    }

    private void AddChild()
    {
        if (_grid.SelectedRows.Count == 0) return;'''),
    ('''    private void AddObjective()
    {
        var t = ActiveTask();
        if (t == null) return;
        _qst.AddObjective(t);''',
     '''    /// <summary>结构树定位：选中对应表格行。</summary>
    public void SelectOwner(object owner)
    {
        int i = _rows.FindIndex(r => ReferenceEquals(r.OwnerObject, owner));
        if (i < 0) return;
        _grid.ClearSelection();
        _grid.Rows[i].Selected = true;
        _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, i - 3);
    }

    private void AddObjective()
    {
        var t = ActiveTask();
        if (t == null) return;
        _qst.AddObjective(t);'''),
])

# ═══ B) CNV 节点属性对话框：中文 + 三个显示开关 ═══
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
    private readonly CheckBox _once = new() { Text = L.T("仅一次"), AutoSize = true };
    private readonly CheckBox _always = new() { Text = L.T("总是显示"), AutoSize = true };
    private readonly CheckBox _unimportant = new() { Text = L.T("不重要"), AutoSize = true };
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
        Controls.Add(Gui.Label(L.T("类型") + ":", 14, y + 3, 80)); _type.Location = new Point(110, y); y += 32;
        Controls.Add(Gui.Label(L.T("显示开关") + ":", 14, y + 3, 80)); y += 24;
        _once.Location = new Point(114, y); _always.Location = new Point(230, y); _unimportant.Location = new Point(360, y); y += 30;
        Controls.Add(_once); Controls.Add(_always); Controls.Add(_unimportant);
        Controls.Add(Gui.Label(L.T("配音文件") + ":", 14, y + 3, 80)); _voPath.Location = new Point(110, y); y += 32;
        Controls.Add(Gui.Label(L.T("跳转到") + ":", 14, y + 3, 80)); _link.Location = new Point(110, y); y += 42;'''),
    ('''        s.Type = (uint)dlg._type.SelectedIndex;
        try { s.Unknown = Gui.ParseU32(dlg._unknown.Text); } catch { /* 忽略非法 */ }
        s.VoPath.SetText(dlg._voPath.Text);''',
     '''        s.Type = (uint)dlg._type.SelectedIndex;
        // 只改低 3 位（编辑器开关），高位为游戏侧保留位，原样保留
        s.Unknown = (s.Unknown & ~7u) | (dlg._once.Checked ? 1u : 0u) | (dlg._always.Checked ? 2u : 0u) | (dlg._unimportant.Checked ? 4u : 0u);
        s.VoPath.SetText(dlg._voPath.Text);'''),
])

# ═══ C) 条件编辑器：比较符下拉（替代十六进制 uid 框），隐藏 body 版本 ═══
patch('CnvQstStudio/Editors.cs', [
    ('''    private readonly NumericUpDown _subType = new() { Minimum = 0, Maximum = 255, Width = 60 };
    private readonly NumericUpDown _uid = new() { Minimum = 0, Maximum = uint.MaxValue, Width = 100 };
    private readonly Label _uidLabel = Gui.Label(L.T("比较符（条件）", "Comparator"), 0, 0, 90);  // 官方 Common.ScriptableCondition+Comparison''',
     '''    // 比较符（官方 Common.ScriptableCondition+Comparison：Less=0…NotEqual=5），下拉选择，不暴露数字
    private readonly ComboBox _cmp = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly Label _cmpLabel = Gui.Label(L.T("比较符", "Comparator"), 0, 0, 56);'''),
    ('''        _typeCombo.SelectedIndexChanged += (_, _) => ChangeType();
        _subType.ValueChanged += (_, _) => ChangeSubType();
        _uid.ValueChanged += (_, _) => { if (_building) return; if (Current() is { } r && r.IsCondition) { r.Uid = (uint)_uid.Value; OnEdit(); } };''',
     '''        _typeCombo.SelectedIndexChanged += (_, _) => ChangeType();
        _cmp.SelectedIndexChanged += (_, _) =>
        {
            if (_building) return;
            if (Current() is { } r && r.IsCondition) { r.Uid = (uint)Math.Max(0, _cmp.SelectedIndex); OnEdit(); }
        };'''),
    ('''        var editTop = new Panel { Dock = DockStyle.Top, Height = 40 };
        var eLabel = Gui.Label(L.T("类型", "Type"), 12, 10, 44);
        var sLabel = Gui.Label(L.T("body 版本", "Body ver"), 306, 10, 64);
        _uidLabel.Location = new Point(446, 10);
        _typeCombo.Location = new Point(60, 8);
        _subType.Location = new Point(370, 8);
        _uid.Location = new Point(540, 8);
        editTop.Controls.AddRange(new Control[] { eLabel, _typeCombo, sLabel, _subType, _uidLabel, _uid });''',
     '''        var editTop = new Panel { Dock = DockStyle.Top, Height = 40 };
        var eLabel = Gui.Label(L.T("类型", "Type"), 12, 10, 44);
        _cmpLabel.Location = new Point(452, 10);
        _cmp.Items.AddRange(new object[] { "<", "<=", "=", ">=", ">", "!=" });
        _cmp.Location = new Point(512, 8);
        _typeCombo.Location = new Point(60, 8);
        _typeCombo.Width = 380;
        editTop.Controls.AddRange(new Control[] { eLabel, _typeCombo, _cmpLabel, _cmp });'''),
    ('''        _typeCombo.Enabled = _subType.Enabled = _uid.Enabled = _fields.Enabled = has;''',
     '''        _typeCombo.Enabled = _cmp.Enabled = _fields.Enabled = has;'''),
    ('''        _subType.Value = r!.SubType;
        _uid.Value = r.Uid;
        _uid.Visible = _uidLabel.Visible = _isCondition;''',
     '''        _cmp.SelectedIndex = (int)Math.Min(r.Uid, 5);
        _cmp.Visible = _cmpLabel.Visible = _isCondition;'''),
    ('''        var rec = new ScriptRecord
        {
            IsCondition = _isCondition,
            Type = type,
            SubType = 0,
            Uid = NextUid(),''',
     '''        var rec = new ScriptRecord
        {
            IsCondition = _isCondition,
            Type = type,
            SubType = Scriptable.DefaultBodyVersion(type),
            Uid = 2,'''),
    ('''        rec.Fields = FieldFactory.Resync(rec.BodySpec, 0, null, _isCondition);''',
     '''        rec.Fields = FieldFactory.Resync(rec.BodySpec, rec.SubType, null, _isCondition);'''),
    ('''        r.Type = ct.Id;
        r.BodySpec = _isCondition ? SpecCond(ct.Id) : SpecAct(ct.Id);''',
     '''        r.Type = ct.Id;
        r.SubType = Scriptable.DefaultBodyVersion(ct.Id);
        r.BodySpec = _isCondition ? SpecCond(ct.Id) : SpecAct(ct.Id);'''),
    ('''    private void ChangeSubType()
    {
        if (_building) return;
        var r = Current();
        if (r == null) return;
        r.SubType = (byte)_subType.Value;
        r.Fields = FieldFactory.Resync(r.BodySpec, r.SubType, r.Fields, _isCondition);
        Rebuild();
        _changed();
    }

''', ''),
])

# ═══ D) DocView：左侧结构树 ═══
patch('CnvQstStudio/MainForm.cs', [
    ('''    private readonly Panel _host = new() { Dock = DockStyle.Fill, BackColor = Theme.Surface };

    public DocView(DocSession doc)
    {
        Doc = doc;
        BackColor = Theme.Surface;
        Font = Theme.UiFont;
        _host.Controls.AddRange(new Control[] { _cnvTable, _qstTable, _locGrid, _emptyHint });
        Controls.Add(_host);
        Controls.Add(_status);

        _cnvTable.LocRequested += ShowLoc;
        _qstTable.LocRequested += ShowLoc;
        ShowCurrent();
        UpdateStatus();
    }''',
     '''    private readonly Panel _host = new() { Dock = DockStyle.Fill, BackColor = Theme.Surface };
    private readonly TreeView _tree = new() { Dock = DockStyle.Fill };
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, SplitterWidth = 6 };

    public DocView(DocSession doc)
    {
        Doc = doc;
        BackColor = Theme.Surface;
        Font = Theme.UiFont;
        _host.Controls.AddRange(new Control[] { _cnvTable, _qstTable, _locGrid, _emptyHint });

        // 左：结构树（与官方编辑器一致的层级观感）；右：平铺表格 / 本地化表
        Theme.StyleTree(_tree);
        _tree.AfterSelect += TreeAfterSelect;
        var treeHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 2, 8), BackColor = Theme.Surface };
        treeHost.Controls.Add(_tree);
        _split.Panel1.Controls.Add(treeHost);
        _split.Panel2.Controls.Add(_host);
        _split.SplitterDistance = 300;
        Controls.Add(_split);
        Controls.Add(_status);

        _cnvTable.LocRequested += ShowLoc;
        _qstTable.LocRequested += ShowLoc;
        BuildTree();
        ShowCurrent();
        UpdateStatus();
    }

    /// <summary>重建结构树（打开 / 结构改动 / 语言切换后）。</summary>
    private void BuildTree()
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        var root = new TreeNode(Doc.Title);
        Doc.FillTree(root);
        _tree.Nodes.Add(root);
        root.Expand();
        foreach (TreeNode n in root.Nodes) n.Expand();
        _tree.EndUpdate();
    }

    private void TreeAfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (e.Node?.Tag is string tag && tag == DocSession.TagLoc) { ShowLoc(); return; }
        if (_locGrid.Visible) ShowCurrent();               // 从本地化表回到结构表
        switch (e.Node?.Tag)
        {
            case CnvStep step: _cnvTable.SelectStep(step); break;
            case QstTask t: _qstTable.SelectOwner(t); break;
            case QstObjective o: _qstTable.SelectOwner(o); break;
            case QstEvent ev: _qstTable.SelectOwner(ev); break;
        }
    }'''),
    ('''    public void RefreshAll()
    {
        if (_cnvTable.Visible) _cnvTable.Bind(Doc, OnEdited);
        else if (_qstTable.Visible) _qstTable.Bind(Doc, OnEdited);
        else if (_locGrid.Visible) _locGrid.Bind(Doc, OnEdited);
        UpdateStatus();
    }''',
     '''    public void RefreshAll()
    {
        BuildTree();
        if (_cnvTable.Visible) _cnvTable.Bind(Doc, OnEdited);
        else if (_qstTable.Visible) _qstTable.Bind(Doc, OnEdited);
        else if (_locGrid.Visible) _locGrid.Bind(Doc, OnEdited);
        UpdateStatus();
    }'''),
])

# ═══ E) 结构树文案中文化 + 任务标题 ═══
patch('CnvQstStudio/DocSession.cs', [
    ('''    private static void AddStep(TreeNode parent, CnvFile c, CnvStep s)
    {
        string text = c.TextFor(s);
        string label = $"[{s.Index}] {s.Kind}";
        if (s.Tag != null) label += $" {{{s.Tag}}}";
        if (text.Length > 0)
        {
            string t = text.Length > 26 ? text[..26] + "…" : text.Replace("\\n", " ");
            label += $" \\"{t}\\"";
        }
        if (s.HasLink) label += $" →[{s.LinkId}]";
        if (!s.VoPath.IsEmpty) label += " [VO]";
        var node = new TreeNode(label) { Tag = s };
        parent.Nodes.Add(node);
        foreach (var child in s.Children) AddStep(node, c, child);
    }''',
     '''    private static void AddStep(TreeNode parent, CnvFile c, CnvStep s)
    {
        var node = new TreeNode(CnvNodeLabel.Plain(c, s)) { Tag = s };
        parent.Nodes.Add(node);
        foreach (var child in s.Children) AddStep(node, c, child);
    }'''),
    ('''        for (int i = 0; i < q.Tasks.Count; i++)
        {
            var t = q.Tasks[i];
            var tn = new TreeNode($"Task {i}") { Tag = t };
            var inc = new TreeNode($"Incentives ({t.Incentives.Count})");
            for (int k = 0; k < t.Incentives.Count; k++)
                inc.Nodes.Add(new TreeNode($"Incentive {k}") { Tag = t.Incentives[k] });
            var objs = new TreeNode($"Objectives ({t.Objectives.Count})");
            for (int k = 0; k < t.Objectives.Count; k++)
                objs.Nodes.Add(new TreeNode($"Objective {k}") { Tag = t.Objectives[k] });
            var evs = new TreeNode($"Events ({t.Events.Count})");
            for (int k = 0; k < t.Events.Count; k++)
                evs.Nodes.Add(new TreeNode($"Event {k}") { Tag = t.Events[k] });
            tn.Nodes.AddRange(new[] { inc, objs, evs });
            root.Nodes.Add(tn);
        }''',
     '''        for (int i = 0; i < q.Tasks.Count; i++)
        {
            var t = q.Tasks[i];
            string title = q.Languages.Count > 0 && q.TaskTitleSlot(i) < q.Languages[0].Texts.Count
                ? q.Languages[0].Texts[q.TaskTitleSlot(i)].Text : "";
            string label = L.F("任务 {0}", i + 1) + (title.Length > 0 ? $"：{title}" : "");
            var tn = new TreeNode(label) { Tag = t };
            var inc = new TreeNode(L.F("接受事件 OnAccept（{0}）", t.Incentives.Count));
            for (int k = 0; k < t.Incentives.Count; k++)
                inc.Nodes.Add(new TreeNode(L.F("接受事件 #{0}", k + 1)) { Tag = t.Incentives[k] });
            var objs = new TreeNode(L.F("目标 Objectives（{0}）", t.Objectives.Count));
            for (int k = 0; k < t.Objectives.Count; k++)
                objs.Nodes.Add(new TreeNode(L.F("目标 #{0}", k + 1)) { Tag = t.Objectives[k] });
            var evs = new TreeNode(L.F("完成事件 OnComplete（{0}）", t.Events.Count));
            for (int k = 0; k < t.Events.Count; k++)
                evs.Nodes.Add(new TreeNode(L.F("完成事件 #{0}", k + 1)) { Tag = t.Events[k] });
            tn.Nodes.AddRange(new[] { inc, objs, evs });
            root.Nodes.Add(tn);
        }'''),
])

# ═══ F) lib：task 标题槽号 ═══
patch('CnvQstLib/QstFile.cs', [
    ('''    /// <summary>本文件按公式应有的串表条数。</summary>''',
     '''    /// <summary>第 ti 个 task 的标题在串表中的槽号（按 §3.3 的公式与槽位顺序）。</summary>
    public int TaskTitleSlot(int ti)
    {
        int slot = 2;
        for (int i = 0; i < ti; i++)
            slot += 2 + Tasks[i].Incentives.Count + Tasks[i].Objectives.Count + Tasks[i].Events.Count;
        return slot;
    }

    /// <summary>本文件按公式应有的串表条数。</summary>'''),
])
print('全部完成')
