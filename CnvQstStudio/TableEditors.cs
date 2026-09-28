using CnvQst;

namespace CnvQstStudio;

/// <summary>
/// 三栏平铺表格编辑器（CNV/QST）：形如官方编辑器的 `节点 | 属性/字段 | 本地化文本 | 条件 | 动作`。
/// 行高自适应，文本列双模式（紧凑 = 自动换行但封顶；平铺 = 无上限整格铺全），两模式选中行均完整显示。
/// 条件/动作单元格单击弹出模态编辑器，确定后回填。
/// </summary>
internal static class TableLayout
{
    /// <summary>把 {^n} 译成真实换行，用于单元格显示与内联编辑。</summary>
    public static string Display(string raw) => raw.Replace("{^n}", "\n");

    /// <summary>单元格编辑提交时把真实换行反译为 {^n}。</summary>
    public static string Stored(string edited) => edited.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "{^n}");

    /// <summary>条件/动作列：每记录一行（⚑/⚡ + Describe）。</summary>
    public static List<string> ScriptLines(ScriptList list)
    {
        var lines = new List<string>();
        foreach (var r in list.Records)
            lines.Add((r.IsCondition ? "⚑ " : "⚡ ") + Gui.Describe(r));
        if (lines.Count == 0) lines.Add("（空）");
        return lines;
    }
}

/// <summary>一行单元格的着色信息（供条件/动作列分色绘制：未命名灰、推断橙）。</summary>
internal readonly record struct ColoredLine(string Text, bool Unknown, bool Inferred);

/// <summary>CNV 节点结构列文本：类型徽标 + [idx] kind {tag} →link [VO]，不含大段正文。</summary>
internal static class CnvNodeLabel
{
    public static string For(CnvFile c, CnvStep s)
    {
        string label = $"[{s.Index}] {s.Kind}";
        if (s.Tag != null) label += $" {{{s.Tag}}}";
        if (s.HasLink) label += $" →[{s.LinkId}]";
        if (!s.VoPath.IsEmpty) label += " [VO]";
        return label;
    }
}

/// <summary>条件/动作编辑模态窗：克隆编辑，确定才回填原列表。逻辑字（AND/OR）仅条件显示。</summary>
internal sealed class ScriptListDialog : Form
{
    private readonly ScriptList _original;
    private readonly ScriptList _work;
    private bool _building;

    private ScriptListDialog(DocSession doc, ScriptList original, bool isCondition, IWin32Window? owner)
    {
        _original = original;
        _work = original.Clone();

Text = isCondition ? L.T("编辑条件", "Edit Conditions") : L.T("编辑动作", "Edit Actions");
        Width = 980;
        Height = 600;
        StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 6, 0, 0) };
        if (isCondition)
        {
            top.Controls.Add(Gui.Label(L.T("关系逻辑", "Logic") + ":", 0, 3, 80));
            var logic = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            logic.Items.AddRange(new object[] { "AND (0)", "OR (1)" });
            _building = true;
            logic.SelectedIndex = _work.Logic == 1 ? 1 : 0;
            _building = false;
            logic.SelectedIndexChanged += (_, _) => { if (!_building) _work.Logic = logic.SelectedIndex == 1 ? 1u : 0u; };
            top.Controls.Add(logic);
        }
        else
        {
            top.Controls.Add(Gui.Label(L.T("动作列表按格式本身不含关系逻辑字。", "Action lists carry no logic word by format."), 0, 3, 460));
        }
        Controls.Add(top);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48 };
        var ok = new Button { Text = L.T("确定", "OK"), DialogResult = DialogResult.OK, Width = 96, Location = new Point(852 - 96, 8) };
        var cancel = new Button { Text = L.T("取消", "Cancel"), DialogResult = DialogResult.Cancel, Width = 96, Location = new Point(852 + 6, 8) };
        CancelButton = cancel;
        AcceptButton = ok;
        bottom.Controls.Add(ok);
        bottom.Controls.Add(cancel);
        Controls.Add(bottom);

        var inner = new ScriptListEditor(doc, _work, isCondition, () => { }, 952) { Dock = DockStyle.Fill };
        Controls.Add(inner);
        inner.BringToFront();
    }

    /// <summary>弹窗编辑；用户确定时回填原列表并返回 true。</summary>
    public static bool Show(DocSession doc, ScriptList original, bool isCondition, IWin32Window? owner)
    {
        using var dlg = new ScriptListDialog(doc, original, isCondition, owner);
        if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
        original.CopyFrom(dlg._work);
        return true;
    }
}

/// <summary>表格行高与换行测量共享工具（文本/条件/动作列自适应，紧凑模式文本封顶）。</summary>
internal static class TableMetrics
{
    /// <summary>紧凑模式下文本单元格封顶的近似行数。</summary>
    public const int CompactCapLines = 3;

    /// <summary>按文本与列宽测量换行后的像素高度（含留白）。</summary>
    public static int WrappedHeight(string text, int cellWidth, Font? font = null)
    {
        if (string.IsNullOrEmpty(text) || cellWidth <= 16)
            return 20;
        Size sz = TextRenderer.MeasureText(text, font ?? Control.DefaultFont, new Size(cellWidth - 8, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        return Math.Max(sz.Height + 8, 20);
    }

    /// <summary>条件/动作列：每记录一行。</summary>
    public static int ScriptHeight(IReadOnlyList<string> lines) =>
        lines.Count == 0 ? 22 : lines.Count * 18 + 8;
}

/// <summary>CNV 三栏表格编辑器：`节点 | 属性 | 本地化文本 | 条件 | 动作`。</summary>
internal sealed class CnvTableEditor : Panel
{
    private DocSession _doc = null!;
    private CnvFile _cnv = null!;
    private Changed _changed = null!;

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        BackgroundColor = Color.White,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        BorderStyle = BorderStyle.None,
        AutoGenerateColumns = false,
    };
    private readonly ToolStrip _bar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
    private readonly List<CnvStep> _steps = new();
    private ToolStripButton _modeBtn = null!;
    private bool _comfortable;
    private bool _building;
    private Action? _locRequested;

    private const int ColNode = 0, ColAttrs = 1, ColText = 2, ColCond = 3, ColAct = 4;

    public event Action? LocRequested { add => _locRequested += value; remove => _locRequested -= value; }

    public CnvTableEditor()
    {
        Controls.Add(_grid);
        Controls.Add(_bar);
        BuildColumns();
        BuildBar();

        _grid.CellClick += (_, e) => OnCellClick(e);
        _grid.CellEndEdit += (_, e) => OnTextCommitted(e);
        _grid.SelectionChanged += (_, _) => RecalcHeights();
        _grid.ColumnWidthChanged += (_, _) => RecalcHeights();
        _grid.RowsAdded += (_, _) => RecalcHeights();
        _grid.CellPainting += PaintScriptCell;
    }

    private void BuildColumns()
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "node", HeaderText = L.T("节点", "Node"), ReadOnly = true, Width = 210,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.False, Alignment = DataGridViewContentAlignment.MiddleLeft },
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "attrs", HeaderText = L.T("属性", "Attrs"), ReadOnly = true, Width = 180,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.False, Alignment = DataGridViewContentAlignment.MiddleLeft, ForeColor = Color.DimGray },
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "text", HeaderText = L.T("本地化文本", "Localized Text"), Width = 340,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True, Alignment = DataGridViewContentAlignment.TopLeft },
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "cond", HeaderText = L.T("条件", "Conditions"), ReadOnly = true, Width = 260,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True, Alignment = DataGridViewContentAlignment.TopLeft },
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "act", HeaderText = L.T("动作", "Actions"), ReadOnly = true, Width = 260,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True, Alignment = DataGridViewContentAlignment.TopLeft },
        });
    }

    private void BuildBar()
    {
        _bar.Items.Add(new ToolStripButton(L.T("＋添加子节点", "+ Add Child"), null, (_, _) => AddChild()));
        _bar.Items.Add(new ToolStripButton(L.T("－删除子树", "- Delete"), null, (_, _) => DeleteStep()));
        _bar.Items.Add(new ToolStripSeparator());
        _bar.Items.Add(new ToolStripButton("↑" + L.T("上移", "Up"), null, (_, _) => MoveStep(-1)));
        _bar.Items.Add(new ToolStripButton("↓" + L.T("下移", "Down"), null, (_, _) => MoveStep(1)));
        _bar.Items.Add(new ToolStripSeparator());
        _bar.Items.Add(new ToolStripButton(L.T("本地化表", "Loc Table"), null, (_, _) => _locRequested?.Invoke()));
        _bar.Items.Add(new ToolStripSeparator());
        _modeBtn = new ToolStripButton(_comfortable ? L.T("平铺模式", "Flat Mode") : L.T("紧凑模式", "Compact Mode"));
        _modeBtn.Click += (_, _) => ToggleMode();
        _bar.Items.Add(_modeBtn);
    }

    private void ToggleMode()
    {
        _comfortable = !_comfortable;
        _modeBtn.Text = _comfortable ? L.T("平铺模式", "Flat Mode") : L.T("紧凑模式", "Compact Mode");
        RecalcHeights();
    }

    public void Bind(DocSession doc, Changed changed)
    {
        _doc = doc;
        _cnv = doc.Cnv!;
        _changed = changed;
        _building = true;
        _steps.Clear();
        _grid.Rows.Clear();
        foreach (var s in _cnv.Steps)
        {
            _steps.Add(s);
            _grid.Rows.Add(RowValues(s));
        }
        if (_grid.Rows.Count > 0) _grid.Rows[0].Selected = true;
        _building = false;
        RecalcHeights();
    }

    private string[] RowValues(CnvStep s) => new[]
    {
        CnvNodeLabel.For(_cnv, s),
        AttrLabel(s),
        TableLayout.Display(textOf(s)),
        string.Join("\n", TableLayout.ScriptLines(s.Conditions)),
        string.Join("\n", TableLayout.ScriptLines(s.Actions)),
    };

    private string textOf(CnvStep s) => _doc.Cnv!.TextFor(s);

    private static string AttrLabel(CnvStep s)
    {
        var parts = new List<string> { "type=" + s.Type };
        if (s.Unknown != 0) parts.Add($"flags=0x{s.Unknown:X}");
        if (!s.VoPath.IsEmpty) parts.Add($"vo={s.VoPath.Text}");
        return string.Join("  ", parts);
    }

    private void OnTextCommitted(DataGridViewCellEventArgs e)
    {
        if (_building || e.RowIndex < 0 || e.ColumnIndex != ColText) return;
        var texts = _doc.PrimaryLanguage.Texts;
        if (e.RowIndex >= texts.Count || e.RowIndex >= _steps.Count) return;
        string v = _grid.Rows[e.RowIndex].Cells[ColText].Value?.ToString() ?? "";
        texts[e.RowIndex].SetText(TableLayout.Stored(v));
        _changed();
    }

    private void OnCellClick(DataGridViewCellEventArgs e)
    {
        if (_building || e.RowIndex < 0 || e.RowIndex >= _steps.Count) return;
        var s = _steps[e.RowIndex];
        if (e.ColumnIndex == ColCond)
        {
            if (ScriptListDialog.Show(_doc, s.Conditions, true, FindForm())) { RefreshRow(s); _changed(); }
        }
        else if (e.ColumnIndex == ColAct)
        {
            if (ScriptListDialog.Show(_doc, s.Actions, false, FindForm())) { RefreshRow(s); _changed(); }
        }
        else if (e.ColumnIndex == ColAttrs || e.ColumnIndex == ColNode)
        {
            if (CnvNodeDialog.Show(_doc, s, FindForm())) { RefreshRow(s); _changed(); }
        }
    }

    private void RefreshRow(CnvStep s)
    {
        int i = _steps.IndexOf(s);
        if (i < 0) return;
        _grid.Rows[i].SetValues(RowValues(s));
        RecalcHeights();
    }

    private void AddChild()
    {
        if (_grid.SelectedRows.Count == 0) return;
        var anchor = _steps[_grid.SelectedRows[0].Index];
        using var dlg = new CnvTypePicker();
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        _cnv.InsertChild(anchor, dlg.Type);
        _changed();
        Bind(_doc, _changed);
    }

    private void DeleteStep()
    {
        if (_grid.SelectedRows.Count == 0) return;
        var s = _steps[_grid.SelectedRows[0].Index];
        if (s == _cnv.Root)
        {
            System.Windows.Forms.MessageBox.Show(FindForm(), L.T("不能删除根节点。", "The root node cannot be deleted."), L.T("删除", "Delete"), System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
            return;
        }
        if (!_cnv.RemoveSubtree(s)) return;
        _changed();
        Bind(_doc, _changed);
    }

    private void MoveStep(int delta)
    {
        if (_grid.SelectedRows.Count == 0) return;
        var s = _steps[_grid.SelectedRows[0].Index];
        _cnv.MoveSibling(s, delta);
        _changed();
        Bind(_doc, _changed);
    }

    private void RecalcHeights()
    {
        if (_building) return;
        int sel = _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Index : -1;
        for (int i = 0; i < _grid.Rows.Count; i++)
        {
            var row = _grid.Rows[i];
            string textVal = row.Cells[ColText].Value?.ToString() ?? "";
            int textH = TableMetrics.WrappedHeight(textVal, row.Cells[ColText].Size.Width);
            if (_comfortable == false && sel != i)
                textH = Math.Min(textH, TableMetrics.CompactCapLines * 18 + 6);

            int condH = TableMetrics.ScriptHeight((row.Cells[ColCond].Value?.ToString() ?? "").Split('\n'));
            int actH = TableMetrics.ScriptHeight((row.Cells[ColAct].Value?.ToString() ?? "").Split('\n'));
            row.Height = Math.Max(Math.Max(Math.Max(textH, condH), actH), 22);
        }
    }

    private void PaintScriptCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _steps.Count) return;
        if (e.ColumnIndex != ColCond && e.ColumnIndex != ColAct) return;
        var s = _steps[e.RowIndex];
        ScriptList list = e.ColumnIndex == ColCond ? s.Conditions : s.Actions;
        var lines = new List<ColoredLine>();
        foreach (var r in list.Records)
        {
            bool un = !r.Named;
            bool inf = r.Named && (r.IsCondition ? Names.InferredCondition(r.Type) : Names.InferredAction(r.Type));
            lines.Add(new ColoredLine((r.IsCondition ? "⚑ " : "⚡ ") + Gui.Describe(r), un, inf));
        }
        if (lines.Count == 0) { e.PaintBackground(e.ClipBounds, true); e.Handled = true; return; }
        e.PaintBackground(e.ClipBounds, true);
        Font font = _grid.Font;
        int y = e.CellBounds.Top + 3;
        foreach (var line in lines)
        {
            Color c = line.Unknown ? Color.Gray : line.Inferred ? Color.FromArgb(230, 126, 34) : Color.Black;
            TextRenderer.DrawText(e.Graphics!, line.Text, font,
                new Rectangle(e.CellBounds.Left + 4, y, e.CellBounds.Width - 8, 18), c, TextFormatFlags.WordBreak);
            y += 18;
        }
        e.Handled = true;
    }
}

/// <summary>CNV 节点属性对话框：类型 / flags / voPath / linkId。</summary>
internal sealed class CnvNodeDialog : Form
{
    private readonly CnvStep _step;
    private readonly CnvFile _cnv;
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TextBox _unknown = new() { Width = 130 };
    private readonly TextBox _voPath = new() { Width = 320 };
    private readonly ComboBox _link = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };

    private CnvNodeDialog(CnvFile c, CnvStep s)
    {
        _cnv = c;
        _step = s;
        Text = L.T($"节点属性  [{s.Index}]", $"Node Properties  [{s.Index}]");
        Width = 540;
        Height = 300;
        StartPosition = FormStartPosition.CenterParent;

        for (uint t = 0; t <= 8; t++) _type.Items.Add($"{t} {Names.CnvStepKind(t)}");
        _type.SelectedIndex = s.Type <= 8 ? (int)s.Type : 0;
        _unknown.Text = Gui.U32Text(s.Unknown);
        _voPath.Text = s.VoPath.Text;
        _voPath.Enabled = c.Version >= 6;
        for (int i = 0; i < c.Steps.Count; i++)
        {
            var t = c.TextFor(c.Steps[i]);
            _link.Items.Add($"{i}: {c.Steps[i].Kind} {(t.Length > 20 ? t[..20] + "…" : t)}");
        }
        _link.SelectedIndex = s.LinkId < (uint)c.Steps.Count ? (int)s.LinkId : -1;
        _link.Enabled = s.Type == Names.StepLink;

        int y = 16;
        Controls.Add(Gui.Label(L.T("类型", "Type") + ":", 14, y + 3, 80)); _type.Location = new Point(110, y); y += 34;
        Controls.Add(Gui.Label("flags:", 14, y + 3, 80)); _unknown.Location = new Point(110, y); y += 30;
        Controls.Add(Gui.Label("voPath:", 14, y + 3, 80)); _voPath.Location = new Point(110, y); y += 34;
        Controls.Add(Gui.Label("linkId:", 14, y + 3, 80)); _link.Location = new Point(110, y); y += 42;

        var ok = new Button { Text = L.T("确定", "OK"), DialogResult = DialogResult.OK, Width = 96, Location = new Point(300, y) };
        var cancel = new Button { Text = L.T("取消", "Cancel"), DialogResult = DialogResult.Cancel, Width = 96, Location = new Point(404, y) };
        AcceptButton = ok; CancelButton = cancel;
        Controls.Add(ok);
        Controls.Add(cancel);
    }

    public static bool Show(DocSession doc, CnvStep s, IWin32Window? owner)
    {
        using var dlg = new CnvNodeDialog(doc.Cnv!, s);
        if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
        s.Type = (uint)dlg._type.SelectedIndex;
        try { s.Unknown = Gui.ParseU32(dlg._unknown.Text); } catch { /* 忽略非法 */ }
        s.VoPath.SetText(dlg._voPath.Text);
        s.HasLink = s.Type == Names.StepLink;
        if (dlg._link.SelectedIndex >= 0 && s.HasLink) s.LinkId = (uint)dlg._link.SelectedIndex;
        return true;
    }
}

/// <summary>QST 行承载：可以是 task、objective、event（incentive 也按 event）。</summary>
internal abstract class QstRowOwner
{
    public abstract string NodeLabel { get; }
    public abstract string FieldLabel { get; }
    public abstract bool HasScript { get; }
    public abstract ScriptList? Conditions { get; }
    public abstract ScriptList? Actions { get; }
}

internal sealed class QstTaskRow : QstRowOwner
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
            return p.Count == 0 ? "（无字段）" : string.Join("  ", p);
        }
    }
    public override bool HasScript => false;
    public override ScriptList? Conditions => null;
    public override ScriptList? Actions => null;
}

internal sealed class QstEventRow : QstRowOwner
{
    public QstEvent E;
    public string Kind;
    public QstEventRow(QstEvent e, string kind) { E = e; Kind = kind; }
    public override string NodeLabel => $"{Kind} uid {E.Uid}";
    public override string FieldLabel => E.Unknown != 0 ? $"+0x2C=0x{E.Unknown:X}" : "（无字段）";
    public override bool HasScript => true;
    public override ScriptList? Conditions => E.Conditions;
    public override ScriptList? Actions => E.Actions;
}

internal sealed class QstObjectiveRow : QstRowOwner
{
    public QstObjective O;
    public QstObjectiveRow(QstObjective o) => O = o;
    public override string NodeLabel => $"Objective uid {O.Uid}";
    public override string FieldLabel
    {
        get
        {
            var p = new List<string>();
            if (O.Unknown0 != 0) p.Add($"+0x10=0x{O.Unknown0:X}");
            if (O.Unknown1 != 0) p.Add($"+0x34=0x{O.Unknown1:X}");
            return p.Count == 0 ? "（无字段）" : string.Join("  ", p);
        }
    }
    public override bool HasScript => true;
    public override ScriptList? Conditions => O.Conditions;
    public override ScriptList? Actions => O.Actions;
}

/// <summary>QST 三栏表格编辑器：`节点 | 字段 | 条件 | 动作`（task/objective/event 平铺缩进）。</summary>
internal sealed class QstTableEditor : Panel
{
    private DocSession _doc = null!;
    private QstFile _qst = null!;
    private Changed _changed = null!;

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        BackgroundColor = Color.White,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        BorderStyle = BorderStyle.None,
        AutoGenerateColumns = false,
    };
    private readonly ToolStrip _bar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
    private readonly List<QstRowOwner> _rows = new();
    private bool _building;
    private Action? _locRequested;

    private const int ColNode = 0, ColField = 1, ColCond = 2, ColAct = 3;

    public event Action? LocRequested { add => _locRequested += value; remove => _locRequested -= value; }

    public QstTableEditor()
    {
        Controls.Add(_grid);
        Controls.Add(_bar);
        BuildColumns();
        BuildBar();
        _grid.CellClick += (_, e) => OnCellClick(e);
        _grid.CellPainting += PaintScriptCell;
    }

    private void BuildColumns()
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "node", HeaderText = L.T("节点", "Node"), ReadOnly = true, Width = 220,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.False, Alignment = DataGridViewContentAlignment.MiddleLeft },
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "field", HeaderText = L.T("字段", "Fields"), ReadOnly = true, Width = 190,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.False, Alignment = DataGridViewContentAlignment.MiddleLeft, ForeColor = Color.DimGray },
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "cond", HeaderText = L.T("条件", "Conditions"), ReadOnly = true, Width = 300,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True, Alignment = DataGridViewContentAlignment.TopLeft },
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "act", HeaderText = L.T("动作", "Actions"), ReadOnly = true, Width = 300,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True, Alignment = DataGridViewContentAlignment.TopLeft },
        });
    }

    private void BuildBar()
    {
        _bar.Items.Add(new ToolStripButton(L.T("＋Objective", "+ Objective"), null, (_, _) => AddObjective()));
        _bar.Items.Add(new ToolStripButton(L.T("＋Event", "+ Event"), null, (_, _) => AddEvent()));
        _bar.Items.Add(new ToolStripButton(L.T("＋Incentive", "+ Incentive"), null, (_, _) => AddIncentive()));
        _bar.Items.Add(new ToolStripSeparator());
        _bar.Items.Add(new ToolStripButton(L.T("－删除", "- Delete"), null, (_, _) => DeleteRow()));
        _bar.Items.Add(new ToolStripSeparator());
        _bar.Items.Add(new ToolStripButton(L.T("本地化表", "Loc Table"), null, (_, _) => _locRequested?.Invoke()));
    }

    public void Bind(DocSession doc, Changed changed)
    {
        _doc = doc;
        _qst = doc.Qst!;
        _changed = changed;
        _building = true;
        _rows.Clear();
        _grid.Rows.Clear();
        foreach (var t in _qst.Tasks)
        {
            _rows.Add(new QstTaskRow(t));
            _grid.Rows.Add(RowValues(_rows[^1]));
            foreach (var inc in t.Incentives) { _rows.Add(new QstEventRow(inc, "Incentive")); _grid.Rows.Add(RowValues(_rows[^1])); }
            foreach (var o in t.Objectives) { _rows.Add(new QstObjectiveRow(o)); _grid.Rows.Add(RowValues(_rows[^1])); }
            foreach (var e in t.Events) { _rows.Add(new QstEventRow(e, "Event")); _grid.Rows.Add(RowValues(_rows[^1])); }
        }
        if (_grid.Rows.Count > 0) _grid.Rows[0].Selected = true;
        _building = false;
        RecalcHeights();
    }

    private string[] RowValues(QstRowOwner r) => new[]
    {
        r.NodeLabel,
        r.FieldLabel,
        r.HasScript && r.Conditions != null ? string.Join("\n", TableLayout.ScriptLines(r.Conditions)) : "",
        r.HasScript && r.Actions != null ? string.Join("\n", TableLayout.ScriptLines(r.Actions)) : "",
    };

    private void RecalcHeights()
    {
        if (_building) return;
        for (int i = 0; i < _grid.Rows.Count; i++)
        {
            var row = _grid.Rows[i];
            int condH = TableMetrics.ScriptHeight((row.Cells[ColCond].Value?.ToString() ?? "").Split('\n'));
            int actH = TableMetrics.ScriptHeight((row.Cells[ColAct].Value?.ToString() ?? "").Split('\n'));
            row.Height = Math.Max(Math.Max(condH, actH), 22);
        }
    }

    private QstTask ActiveTask()
    {
        if (_grid.SelectedRows.Count == 0) return _qst.Tasks.FirstOrDefault()!;
        return _rows[_grid.SelectedRows[0].Index] is QstTaskRow tr
            ? tr.T
            : (_rows[_grid.SelectedRows[0].Index] is QstEventRow er ? TaskOf(er.E) : TaskOf(((QstObjectiveRow)_rows[_grid.SelectedRows[0].Index]).O));
    }

    private QstTask TaskOf(QstEvent e) => _qst.Tasks.First(t => t.Incentives.Contains(e) || t.Events.Contains(e));
    private QstTask TaskOf(QstObjective o) => _qst.Tasks.First(t => t.Objectives.Contains(o));

    private void OnCellClick(DataGridViewCellEventArgs e)
    {
        if (_building || e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
        var r = _rows[e.RowIndex];
        if (e.ColumnIndex == ColCond && r.Conditions != null)
        {
            if (ScriptListDialog.Show(_doc, r.Conditions, true, FindForm())) { RefreshRow(e.RowIndex); _changed(); }
        }
        else if (e.ColumnIndex == ColAct && r.Actions != null)
        {
            if (ScriptListDialog.Show(_doc, r.Actions, false, FindForm())) { RefreshRow(e.RowIndex); _changed(); }
        }
        else if (e.ColumnIndex == ColNode || e.ColumnIndex == ColField)
        {
            if (QstFieldsDialog.Show(r, FindForm())) { RefreshRow(e.RowIndex); _changed(); }
        }
    }

    private void RefreshRow(int i) { _grid.Rows[i].SetValues(RowValues(_rows[i])); RecalcHeights(); }

    private void AddObjective()
    {
        var t = ActiveTask();
        if (t == null) return;
        t.Objectives.Add(new QstObjective());
        _changed(); Bind(_doc, _changed);
    }

    private void AddEvent()
    {
        var t = ActiveTask();
        if (t == null) return;
        t.Events.Add(new QstEvent());
        _changed(); Bind(_doc, _changed);
    }

    private void AddIncentive()
    {
        var t = ActiveTask();
        if (t == null) return;
        t.Incentives.Add(new QstEvent());
        _changed(); Bind(_doc, _changed);
    }

    private void DeleteRow()
    {
        if (_grid.SelectedRows.Count == 0) return;
        var r = _rows[_grid.SelectedRows[0].Index];
        switch (r)
        {
            case QstTaskRow: System.Windows.Forms.MessageBox.Show(FindForm(), L.T("task 行不可删除（需先解码本地化槽映射）。", "Task rows cannot be deleted (localization slot mapping not yet decoded)."), L.T("删除", "Delete"), System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information); return;
            case QstEventRow er:
                {
                    var t = TaskOf(er.E);
                    if (!t.Incentives.Remove(er.E)) t.Events.Remove(er.E);
                    break;
                }
            case QstObjectiveRow or:
                TaskOf(or.O).Objectives.Remove(or.O);
                break;
        }
        _changed(); Bind(_doc, _changed);
    }

    private void PaintScriptCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
        if (e.ColumnIndex != ColCond && e.ColumnIndex != ColAct) return;
        var r = _rows[e.RowIndex];
        ScriptList? list = e.ColumnIndex == ColCond ? r.Conditions : r.Actions;
        if (list == null) { e.PaintBackground(e.ClipBounds, true); e.Handled = true; return; }
        var lines = new List<ColoredLine>();
        foreach (var rec in list.Records)
        {
            bool un = !rec.Named;
            bool inf = rec.Named && (rec.IsCondition ? Names.InferredCondition(rec.Type) : Names.InferredAction(rec.Type));
            lines.Add(new ColoredLine((rec.IsCondition ? "⚑ " : "⚡ ") + Gui.Describe(rec), un, inf));
        }
        e.PaintBackground(e.ClipBounds, true);
        if (lines.Count == 0) { e.Handled = true; return; }
        Font font = _grid.Font;
        int y = e.CellBounds.Top + 3;
        foreach (var line in lines)
        {
            Color c = line.Unknown ? Color.Gray : line.Inferred ? Color.FromArgb(230, 126, 34) : Color.Black;
            TextRenderer.DrawText(e.Graphics!, line.Text, font,
                new Rectangle(e.CellBounds.Left + 4, y, e.CellBounds.Width - 8, 18), c, TextFormatFlags.WordBreak);
            y += 18;
        }
        e.Handled = true;
    }
}

/// <summary>QST 字段编辑对话框（task 标量 / objective / event）。</summary>
internal sealed class QstFieldsDialog : Form
{
    private readonly List<(string, NumericUpDown)> _nums = new();
    private readonly List<(string, CheckBox)> _flags = new();

    private QstFieldsDialog(QstRowOwner row, uint fontSize)
    {
        Text = L.T("字段编辑  " + row.NodeLabel, "Fields  " + row.NodeLabel);
        Width = 440;
        StartPosition = FormStartPosition.CenterParent;
        var y = 16;
        switch (row)
        {
            case QstTaskRow tr:
                _nums.Add(("field @0xA8", Num(tr.T.UnknownA8)));
                _nums.Add(("field @0xAC", Num(tr.T.UnknownAC)));
                _flags.Add(("flag v8", Check(tr.T.Flag8)));
                _flags.Add(("flag v9", Check(tr.T.Flag9)));
                break;
            case QstObjectiveRow or:
                _nums.Add(("field +0x10", Num(or.O.Unknown0)));
                _nums.Add(("field +0x34", Num(or.O.Unknown1)));
                break;
            case QstEventRow er2:
                _nums.Add(("field +0x2C", Num(er2.E.Unknown)));
                break;
        }
        foreach (var (lab, n) in _nums) { Controls.Add(Gui.Label(lab, 16, y + 3, 110)); n.Location = new Point(140, y); Controls.Add(n); y += 32; }
        foreach (var (lab, cb) in _flags) { cb.Location = new Point(16, y); Controls.Add(cb); y += 26; }
        y += 8;
        var ok = new Button { Text = L.T("确定", "OK"), DialogResult = DialogResult.OK, Width = 96, Location = new Point(220, y) };
        var cancel = new Button { Text = L.T("取消", "Cancel"), DialogResult = DialogResult.Cancel, Width = 96, Location = new Point(324, y) };
        AcceptButton = ok; CancelButton = cancel;
        Controls.Add(ok);
        Controls.Add(cancel);
        Height = y + 80;
    }

    private static NumericUpDown Num(uint v) => new()
    {
        Minimum = 0, Maximum = uint.MaxValue, Value = Math.Min(v, uint.MaxValue), Width = 140,
        Hexadecimal = true,
    };

    private static CheckBox Check(bool v) => new() { Text = "", Checked = v, AutoSize = true };

    public static bool Show(QstRowOwner row, IWin32Window? owner)
    {
        using var dlg = new QstFieldsDialog(row, 9);
        if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
        if (row is QstTaskRow tr)
        {
            tr.T.UnknownA8 = (uint)dlg._nums[0].Item2.Value;
            tr.T.UnknownAC = (uint)dlg._nums[1].Item2.Value;
            tr.T.Flag8 = dlg._flags[0].Item2.Checked;
            tr.T.Flag9 = dlg._flags[1].Item2.Checked;
        }
        else if (row is QstObjectiveRow or2)
        {
            or2.O.Unknown0 = (uint)dlg._nums[0].Item2.Value;
            or2.O.Unknown1 = (uint)dlg._nums[1].Item2.Value;
        }
        else if (row is QstEventRow er)
        {
            er.E.Unknown = (uint)dlg._nums[0].Item2.Value;
        }
        return true;
    }
}

/// <summary>新节点类型选择（添加子节点）。</summary>
internal sealed class CnvTypePicker : Form
{
    private readonly ComboBox _box = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    public uint Type { get; private set; }

    public CnvTypePicker()
    {
        Text = L.T("选择新节点类型", "Choose New Node Type");
        Width = 300;
        Height = 140;
        StartPosition = FormStartPosition.CenterParent;
        for (uint t = 0; t <= 8; t++) _box.Items.Add($"{t} {Names.CnvStepKind(t)}");
        _box.SelectedIndex = 1;
        _box.Location = new Point(14, 14);
        var ok = new Button { Text = L.T("确定", "OK"), DialogResult = DialogResult.OK, Width = 90, Location = new Point(96, 60) };
        AcceptButton = ok;
        ok.Click += (_, _) => Type = (uint)_box.SelectedIndex;
        Controls.Add(_box);
        Controls.Add(ok);
    }
}


