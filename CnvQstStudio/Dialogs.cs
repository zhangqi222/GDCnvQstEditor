using CnvQst;
using CnvQst.Cli;

namespace CnvQstStudio;

/// <summary>字符串列表编辑（P body 的路径列表等）。</summary>
internal sealed class StringListDialog : Form
{
    public List<string> Items { get; private set; } = new();
    private readonly ListBox _list = new() { Dock = DockStyle.Fill };
    private readonly TextBox _edit = new() { Dock = DockStyle.Top };
    private readonly Button _ok = new() { Text = "确定", DialogResult = DialogResult.OK, Width = 80 };
    private readonly Button _cancel = new() { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80 };

    public StringListDialog(string title, List<string> items)
    {
        Text = title;
        Width = 560;
        Height = 420;
        StartPosition = FormStartPosition.CenterParent;

        var top = new Panel { Dock = DockStyle.Top, Height = 118 };
        var btns = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
        var add = new Button { Text = "添加", Width = 70 };
        var del = new Button { Text = "删除", Width = 70 };
        btns.Controls.AddRange(new Control[] { add, del });
        top.Controls.Add(_edit);
        top.Controls.Add(btns);
        _edit.BringToFront();

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
        bottom.Controls.Add(_cancel);
        bottom.Controls.Add(_ok);

        Controls.Add(_list);
        Controls.Add(top);
        Controls.Add(bottom);

        foreach (var s in items) _list.Items.Add(s);
        _list.SelectedIndexChanged += (_, _) => _edit.Text = _list.SelectedItem?.ToString() ?? "";
        _edit.TextChanged += (_, _) =>
        {
            int i = _list.SelectedIndex;
            if (i >= 0) _list.Items[i] = _edit.Text;
        };
        add.Click += (_, _) => { _list.Items.Add(""); _list.SelectedIndex = _list.Items.Count - 1; _edit.Focus(); };
        del.Click += (_, _) =>
        {
            int i = _list.SelectedIndex;
            if (i >= 0) { _list.Items.RemoveAt(i); _edit.Text = ""; }
        };
        _list.SelectedIndexChanged += (_, _) => { };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (DialogResult == DialogResult.OK)
            Items = _list.Items.Cast<object>().Select(o => o.ToString() ?? "").ToList();
    }
}

/// <summary>Lua 形态参数表编辑：n × {u32, u8, u32, 串}。</summary>
internal sealed class LuaParamsDialog : Form
{
    private readonly ScriptField _field;
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        AutoGenerateColumns = false,
    };
    private readonly Button _ok = new() { Text = "确定", DialogResult = DialogResult.OK, Width = 80 };

    public LuaParamsDialog(ScriptField field)
    {
        _field = field;
        Text = $"Lua 参数（{field.Params!.Count} 项）";
        Width = 640;
        Height = 400;
        StartPosition = FormStartPosition.CenterParent;

        var cA = new DataGridViewTextBoxColumn { Name = "a", HeaderText = "u32 A", FillWeight = 15 };
        var cB = new DataGridViewTextBoxColumn { Name = "b", HeaderText = "u8 B", FillWeight = 10 };
        var cC = new DataGridViewTextBoxColumn { Name = "c", HeaderText = "u32 C", FillWeight = 15 };
        var cS = new DataGridViewTextBoxColumn { Name = "s", HeaderText = "字符串", FillWeight = 60 };
        _grid.Columns.AddRange(new DataGridViewColumn[] { cA, cB, cC, cS });

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
        var add = new Button { Text = "添加参数", Width = 90 };
        var del = new Button { Text = "删除所选", Width = 90 };
        buttons.Controls.AddRange(new Control[] { add, del });

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
        bottom.Controls.Add(new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80 });
        bottom.Controls.Add(_ok);

        Controls.Add(_grid);
        Controls.Add(buttons);
        Controls.Add(bottom);

        Reload();
        add.Click += (_, _) => { _field.Params!.Add(new LuaParam()); Reload(); };
        del.Click += (_, _) =>
        {
            if (_grid.CurrentRow?.Index is >= 0 and int i)
            {
                _field.Params!.RemoveAt(i);
                Reload();
            }
        };
        _grid.CellEndEdit += (_, _) => Apply();
    }

    private void Reload()
    {
        _grid.Rows.Clear();
        foreach (var p in _field.Params!)
            _grid.Rows.Add(Gui.U32Text(p.A), p.B.ToString(), Gui.U32Text(p.C), p.S.Text);
        Text = $"Lua 参数（{_field.Params!.Count} 项）";
    }

    private void Apply()
    {
        for (int i = 0; i < _field.Params!.Count && i < _grid.Rows.Count; i++)
        {
            var row = _grid.Rows[i];
            try { _field.Params[i].A = Gui.ParseU32(row.Cells["a"].Value?.ToString() ?? "0"); } catch { }
            if (byte.TryParse(row.Cells["b"].Value?.ToString(), out byte b)) _field.Params[i].B = b;
            try { _field.Params[i].C = Gui.ParseU32(row.Cells["c"].Value?.ToString() ?? "0"); } catch { }
            _field.Params[i].S.SetText(row.Cells["s"].Value?.ToString() ?? "");
        }
    }
}

/// <summary>新建对话对话框。</summary>
internal sealed class NewConversationDialog : Form
{
    public string Speaker { get; private set; } = "";
    public string FirstText { get; private set; } = "";
    public string EndText { get; private set; } = "";

    public NewConversationDialog()
    {
        Text = L.NewConversationTitle;
        Width = 480;
        Height = 240;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var sp = Gui.Box(120, 14, 320, "New Speaker");
        var ft = Gui.Box(120, 44, 320, "");
        var et = Gui.Box(120, 74, 320, "");
        var ok = new Button { Text = L.Create, DialogResult = DialogResult.OK, Width = 90, Location = new Point(300, 150) };
        var cancel = new Button { Text = L.Cancel, DialogResult = DialogResult.Cancel, Width = 90, Location = new Point(200, 150) };
        Controls.AddRange(new Control[]
        {
            Gui.Label(L.NewConversationSpeaker, 10, 17, 106), sp,
            Gui.Label(L.NewConversationFirst, 10, 47, 106), ft,
            Gui.Label(L.NewConversationEnd, 10, 77, 106), et,
            ok, cancel,
        });
        AcceptButton = ok;
        CancelButton = cancel;
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK)
            {
                if (string.IsNullOrWhiteSpace(sp.Text))
                {
                    MessageBox.Show(this, L.SpeakerRequired, L.NewConversationTitle);
                    e.Cancel = true;
                    return;
                }
                Speaker = sp.Text;
                FirstText = ft.Text;
                EndText = et.Text;
            }
        };
    }
}

/// <summary>新建任务对话框。</summary>
internal sealed class NewQuestDialog : Form
{
    public string Group { get; private set; } = "";
    public string QuestName { get; private set; } = "";
    public string TaskTitle { get; private set; } = "";
    public string TaskDesc { get; private set; } = "";

    public NewQuestDialog()
    {
        Text = L.NewQuestTitle;
        Width = 500;
        Height = 280;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var g = Gui.Box(120, 14, 340, "New Group");
        var n = Gui.Box(120, 44, 340, "New Quest");
        var t = Gui.Box(120, 74, 340, "New Task");
        var d = new TextBox { Multiline = true, Height = 60, Width = 340, Location = new Point(120, 104) };
        var ok = new Button { Text = L.Create, DialogResult = DialogResult.OK, Width = 90, Location = new Point(360, 178) };
        var cancel = new Button { Text = L.Cancel, DialogResult = DialogResult.Cancel, Width = 90, Location = new Point(260, 178) };
        Controls.AddRange(new Control[]
        {
            Gui.Label(L.NewQuestGroup, 10, 17, 106), g,
            Gui.Label(L.NewQuestName, 10, 47, 106), n,
            Gui.Label(L.NewQuestTaskTitle, 10, 77, 106), t,
            Gui.Label(L.NewQuestTaskDesc, 10, 107, 106), d,
            ok, cancel,
        });
        AcceptButton = ok;
        CancelButton = cancel;
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK)
            {
                if (string.IsNullOrWhiteSpace(n.Text))
                {
                    MessageBox.Show(this, L.QuestNameRequired, L.NewQuestTitle);
                    e.Cancel = true;
                    return;
                }
                Group = g.Text;
                QuestName = n.Text;
                TaskTitle = t.Text;
                TaskDesc = d.Text;
            }
        };
    }
}

/// <summary>TXT 回填预览：txt 行 vs 当前本地化表逐行对照；行数不符禁止应用。</summary>
internal sealed class ImportTxtPreviewDialog : Form
{
    public bool Applied { get; private set; }

    public ImportTxtPreviewDialog(DocSession doc, string txtPath)
    {
        Text = "TXT 回填预览 — " + Path.GetFileName(txtPath);
        Width = 900;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;

        var model = (object?)doc.Cnv ?? doc.Qst!;
        var preview = BatchOps.BuildImportPreview(model, txtPath);
        bool countOk = doc.PrimaryLanguage.Texts.Count ==
                       TextExport.SplitTxt(BatchOps.ReadTextAuto(txtPath)).Count;

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        grid.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "i", HeaderText = "行", ReadOnly = true, FillWeight = 6 },
            new DataGridViewTextBoxColumn { Name = "new", HeaderText = "txt 行", ReadOnly = true, FillWeight = 47 },
            new DataGridViewTextBoxColumn { Name = "cur", HeaderText = "当前文本", ReadOnly = true, FillWeight = 47 },
        });
        foreach (var (idx, line, cur) in preview)
        {
            bool differ = line != cur;
            int r = grid.Rows.Add(idx.ToString(), line, cur);
            if (differ) grid.Rows[r].Cells["new"].Style.ForeColor = Color.Firebrick;
            if (line == "<缺失>" || cur == "<缺失>")
            {
                grid.Rows[r].Cells["new"].Style.BackColor = Color.MistyRose;
                grid.Rows[r].Cells["cur"].Style.BackColor = Color.MistyRose;
            }
        }

        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = countOk
                ? $"行数匹配：{preview.Count} 行。红色行为将被替换的内容。"
                : $"⚠ 行数不匹配：txt 与本地化表不一致，禁止回填（游戏按行号位置回填文本）。",
            ForeColor = countOk ? Color.Black : Color.Firebrick,
        };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 90 };
        var apply = new Button { Text = "应用回填", Width = 110, Enabled = countOk };
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(apply);
        apply.Click += (_, _) =>
        {
            var lines = TextExport.SplitTxt(BatchOps.ReadTextAuto(txtPath));
            TextExport.ApplyTxt(doc.PrimaryLanguage, lines);
            Applied = true;
            DialogResult = DialogResult.OK;
        };
        Controls.Add(grid);
        Controls.Add(info);
        Controls.Add(bottom);
        CancelButton = cancel;
    }
}

internal static class BatchOpsExt
{
    public static int BuildLines(string txtPath) =>
        TextExport.SplitTxt(BatchOps.ReadTextAuto(txtPath)).Count;
}
