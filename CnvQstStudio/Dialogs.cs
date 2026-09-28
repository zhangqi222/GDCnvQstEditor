using CnvQst;
using CnvQst.Cli;

namespace CnvQstStudio;

/// <summary>对话框通用骨架：统一主题、内边距与右下角按钮区。</summary>
internal static class DialogChrome
{
    /// <summary>在窗口底部放一行右对齐按钮（最后一个是主按钮）。返回按钮行高度。</summary>
    public static Panel Footer(Form form, params Button[] buttons)
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(14, 10, 14, 12), BackColor = Theme.Surface };
        int right = 0;
        for (int i = buttons.Length - 1; i >= 0; i--)
        {
            var b = buttons[i];
            b.Width = Math.Max(b.Width, 96);
            b.Height = 32;
            b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            b.Location = new Point(bar.Width - 14 - right - b.Width, 12);
            right += b.Width + 8;
            bar.Controls.Add(b);
            bar.Resize += (_, _) =>
            {
                int r2 = 0;
                for (int k = buttons.Length - 1; k >= 0; k--)
                {
                    buttons[k].Left = bar.Width - 14 - r2 - buttons[k].Width;
                    r2 += buttons[k].Width + 8;
                }
            };
        }
        form.Controls.Add(bar);
        return bar;
    }
}

/// <summary>字符串列表编辑（P body 的路径列表等）。</summary>
internal sealed class StringListDialog : Form
{
    public List<string> Items { get; private set; } = new();
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
    private readonly TextBox _edit = new() { Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle };

    public StringListDialog(string title, List<string> items)
    {
        Theme.Apply(this);
        Text = title;
        Width = 600;
        Height = 460;
        MinimumSize = new Size(420, 320);
        StartPosition = FormStartPosition.CenterParent;

        var top = new Panel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(14, 12, 14, 0) };
        _edit.Location = new Point(14, 40);
        _edit.Width = top.Width - 28;
        _edit.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        var label = Theme.Caption(L.T("每行一项；右侧按钮可增删。"));
        label.Location = new Point(14, 12);

        var btns = new Panel { Dock = DockStyle.Top, Height = 26 };
        var add = new Button { Text = L.Add, Location = new Point(14, 0), Width = 76 };
        var del = new Button { Text = L.Delete, Location = new Point(98, 0), Width = 76 };
        Theme.MakeGhost(add);
        Theme.MakeGhost(del);
        btns.Controls.AddRange(new Control[] { add, del });
        top.Controls.AddRange(new Control[] { btns, _edit, label });

        var ok = new Button { Text = L.Confirm, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = L.Cancel, DialogResult = DialogResult.Cancel };
        Theme.MakeGhost(cancel);
        Theme.MakePrimary(ok);
        var footer = DialogChrome.Footer(this, cancel, ok);

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 14, 6) };
        host.Controls.Add(_list);
        Controls.Add(host);
        Controls.Add(top);
        Controls.Add(footer);

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
        AcceptButton = ok;
        CancelButton = cancel;
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
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoGenerateColumns = false };

    public LuaParamsDialog(ScriptField field)
    {
        Theme.Apply(this);
        _field = field;
        Text = L.F("Lua 参数（{0} 项）", field.Params!.Count);
        Width = 700;
        Height = 440;
        MinimumSize = new Size(520, 320);
        StartPosition = FormStartPosition.CenterParent;

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "a", HeaderText = "u32 A", FillWeight = 15 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "b", HeaderText = "u8 B", FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "c", HeaderText = "u32 C", FillWeight = 15 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "s", HeaderText = L.T("字符串"), FillWeight = 60 });
        Theme.StyleGrid(_grid);

        var bar = new Panel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(14, 8, 14, 0) };
        var add = new Button { Text = L.T("添加参数"), Width = 100, Location = new Point(14, 6) };
        var del = new Button { Text = L.T("删除所选"), Width = 100, Location = new Point(122, 6) };
        Theme.MakeGhost(add);
        Theme.MakeGhost(del);
        bar.Controls.AddRange(new Control[] { add, del });

        var ok = new Button { Text = L.Confirm, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = L.Cancel, DialogResult = DialogResult.Cancel };
        Theme.MakeGhost(cancel);
        Theme.MakePrimary(ok);
        var footer = DialogChrome.Footer(this, cancel, ok);

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 14, 6) };
        host.Controls.Add(_grid);
        Controls.Add(host);
        Controls.Add(bar);
        Controls.Add(footer);

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
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void Reload()
    {
        _grid.Rows.Clear();
        foreach (var p in _field.Params!)
            _grid.Rows.Add(Gui.U32Text(p.A), p.B.ToString(), Gui.U32Text(p.C), p.S.Text);
        Text = L.F("Lua 参数（{0} 项）", _field.Params!.Count);
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
        Theme.Apply(this);
        Text = L.NewConversationTitle;
        Width = 540;
        Height = 300;
        MinimumSize = new Size(460, 280);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var head = Theme.Heading(L.NewConversationTitle);
        head.Location = new Point(16, 14);
        var hint = Theme.Caption(L.T("首句与结束语可以先留空，稍后在表格里补。"));
        hint.Location = new Point(16, 40);

        var sp = new TextBox { Location = new Point(150, 74), Width = 360, Text = "New Speaker", BorderStyle = BorderStyle.FixedSingle };
        var ft = new TextBox { Location = new Point(150, 108), Width = 360, BorderStyle = BorderStyle.FixedSingle };
        var et = new TextBox { Location = new Point(150, 142), Width = 360, BorderStyle = BorderStyle.FixedSingle };
        sp.Anchor = ft.Anchor = et.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var ok = new Button { Text = L.Create, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = L.Cancel, DialogResult = DialogResult.Cancel };
        Theme.MakeGhost(cancel);
        Theme.MakePrimary(ok);
        var footer = DialogChrome.Footer(this, cancel, ok);

        Controls.AddRange(new Control[]
        {
            head, hint,
            Gui.Label(L.NewConversationSpeaker, 16, 77, 128), sp,
            Gui.Label(L.NewConversationFirst, 16, 111, 128), ft,
            Gui.Label(L.NewConversationEnd, 16, 145, 128), et,
            footer,
        });
        AcceptButton = ok;
        CancelButton = cancel;
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            if (string.IsNullOrWhiteSpace(sp.Text))
            {
                MessageBox.Show(this, L.SpeakerRequired, L.NewConversationTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }
            Speaker = sp.Text;
            FirstText = ft.Text;
            EndText = et.Text;
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
        Theme.Apply(this);
        Text = L.NewQuestTitle;
        Width = 560;
        Height = 360;
        MinimumSize = new Size(480, 340);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var head = Theme.Heading(L.NewQuestTitle);
        head.Location = new Point(16, 14);
        var hint = Theme.Caption(L.T("会生成一个含单个 Task 的 QST2 v9 骨架，之后可在表格里增删。"));
        hint.Location = new Point(16, 40);

        var g = new TextBox { Location = new Point(150, 74), Width = 380, Text = "New Group", BorderStyle = BorderStyle.FixedSingle };
        var n = new TextBox { Location = new Point(150, 108), Width = 380, Text = "New Quest", BorderStyle = BorderStyle.FixedSingle };
        var t = new TextBox { Location = new Point(150, 142), Width = 380, Text = "New Task", BorderStyle = BorderStyle.FixedSingle };
        var d = new TextBox { Location = new Point(150, 176), Width = 380, Height = 70, Multiline = true, BorderStyle = BorderStyle.FixedSingle };
        g.Anchor = n.Anchor = t.Anchor = d.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var ok = new Button { Text = L.Create, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = L.Cancel, DialogResult = DialogResult.Cancel };
        Theme.MakeGhost(cancel);
        Theme.MakePrimary(ok);
        var footer = DialogChrome.Footer(this, cancel, ok);

        Controls.AddRange(new Control[]
        {
            head, hint,
            Gui.Label(L.NewQuestGroup, 16, 77, 128), g,
            Gui.Label(L.NewQuestName, 16, 111, 128), n,
            Gui.Label(L.NewQuestTaskTitle, 16, 145, 128), t,
            Gui.Label(L.NewQuestTaskDesc, 16, 179, 128), d,
            footer,
        });
        AcceptButton = ok;
        CancelButton = cancel;
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            if (string.IsNullOrWhiteSpace(n.Text))
            {
                MessageBox.Show(this, L.QuestNameRequired, L.NewQuestTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }
            Group = g.Text;
            QuestName = n.Text;
            TaskTitle = t.Text;
            TaskDesc = d.Text;
        };
    }
}

/// <summary>TXT 回填预览：txt 行 vs 当前本地化表逐行对照；行数不符禁止应用。</summary>
internal sealed class ImportTxtPreviewDialog : Form
{
    public bool Applied { get; private set; }

    public ImportTxtPreviewDialog(DocSession doc, string txtPath)
    {
        Theme.Apply(this);
        Text = L.F("TXT 回填预览 — {0}", Path.GetFileName(txtPath));
        Width = 960;
        Height = 620;
        MinimumSize = new Size(700, 420);
        StartPosition = FormStartPosition.CenterParent;

        var model = (object?)doc.Cnv ?? doc.Qst!;
        var preview = BatchOps.BuildImportPreview(model, txtPath);
        bool countOk = doc.PrimaryLanguage.Texts.Count == TextExport.SplitTxt(BatchOps.ReadTextAuto(txtPath)).Count;

        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        grid.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "i", HeaderText = L.T("行"), ReadOnly = true, FillWeight = 6 },
            new DataGridViewTextBoxColumn { Name = "new", HeaderText = L.T("txt 行"), ReadOnly = true, FillWeight = 47 },
            new DataGridViewTextBoxColumn { Name = "cur", HeaderText = L.T("当前文本"), ReadOnly = true, FillWeight = 47 },
        });
        Theme.StyleGrid(grid);
        foreach (var (idx, line, cur) in preview)
        {
            bool differ = line != cur;
            int r = grid.Rows.Add(idx.ToString(), line, cur);
            if (differ) grid.Rows[r].Cells["new"].Style.ForeColor = Theme.Danger;
            if (line == "<缺失>" || cur == "<缺失>")
            {
                var warn = new DataGridViewCellStyle { BackColor = Color.MistyRose, ForeColor = Theme.Danger };
                grid.Rows[r].Cells["new"].Style = warn;
                grid.Rows[r].Cells["cur"].Style = warn;
            }
        }

        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(14, 12, 14, 0),
            Text = countOk
                ? L.F("行数匹配：{0} 行。红色行是将被替换的内容。", preview.Count)
                : L.T("行数不匹配：txt 与本地化表不一致，禁止回填（游戏按行号位置回填文本）。"),
            ForeColor = countOk ? Theme.Text : Theme.Danger,
            BackColor = Theme.Surface,
        };

        var cancel = new Button { Text = L.Cancel, DialogResult = DialogResult.Cancel };
        var apply = new Button { Text = L.T("应用回填"), Width = 120, Enabled = countOk };
        Theme.MakeGhost(cancel);
        Theme.MakePrimary(apply);
        var footer = DialogChrome.Footer(this, cancel, apply);
        apply.Click += (_, _) =>
        {
            var lines = TextExport.SplitTxt(BatchOps.ReadTextAuto(txtPath));
            TextExport.ApplyTxt(doc.PrimaryLanguage, lines);
            Applied = true;
            DialogResult = DialogResult.OK;
        };

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 14, 6) };
        host.Controls.Add(grid);
        Controls.Add(host);
        Controls.Add(info);
        Controls.Add(footer);
        CancelButton = cancel;
    }
}

internal static class BatchOpsExt
{
    public static int BuildLines(string txtPath) =>
        TextExport.SplitTxt(BatchOps.ReadTextAuto(txtPath)).Count;
}
