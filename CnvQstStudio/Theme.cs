using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace CnvQstStudio;

/// <summary>
/// 界面主题：统一的字体、配色与控件样式，让 WinForms 看起来接近现代扁平化设计。
/// 只做"外观"，不含任何业务逻辑；所有对话框都通过 <see cref="Apply"/> 统一套用。
/// </summary>
internal static class Theme
{
    // ---- 配色（浅色，冷灰底 + 蓝色强调）----
    public static readonly Color Bg = Color.FromArgb(0xF5, 0xF6, 0xF8);        // 窗口底
    public static readonly Color Surface = Color.White;                         // 内容面
    public static readonly Color Border = Color.FromArgb(0xE4, 0xE7, 0xEC);     // 分隔线
    public static readonly Color Text = Color.FromArgb(0x1F, 0x24, 0x30);       // 主文字
    public static readonly Color Muted = Color.FromArgb(0x6B, 0x72, 0x80);      // 次要文字
    public static readonly Color Accent = Color.FromArgb(0x2F, 0x6F, 0xED);     // 强调色
    public static readonly Color AccentSoft = Color.FromArgb(0xE8, 0xF0, 0xFE); // 强调浅底
    public static readonly Color Warn = Color.FromArgb(0xB4, 0x53, 0x09);       // 提示（未知语义）
    public static readonly Color Ok = Color.FromArgb(0x0E, 0x7A, 0x4F);         // 通过
    public static readonly Color Danger = Color.FromArgb(0xB4, 0x23, 0x18);     // 错误

    public static readonly Color GridAltRow = Color.FromArgb(0xFA, 0xFB, 0xFC);

    private static Font? _uiFont;

    /// <summary>统一字体：优先微软雅黑 UI（中文清晰），退化到 Segoe UI。</summary>
    public static Font UiFont
    {
        get
        {
            if (_uiFont != null) return _uiFont;
            foreach (string name in new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" })
            {
                try
                {
                    var f = new Font(name, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
                    if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { _uiFont = f; break; }
                    f.Dispose();
                }
                catch { }
            }
            _uiFont ??= SystemFonts.MessageBoxFont ?? new Font(FontFamily.GenericSansSerif, 9.5f);
            return _uiFont;
        }
    }

    public static Font MonoFont
    {
        get
        {
            foreach (string name in new[] { "Cascadia Mono", "Consolas", "Courier New" })
            {
                try
                {
                    var f = new Font(name, 9.5f);
                    if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return f;
                    f.Dispose();
                }
                catch { }
            }
            return UiFont;
        }
    }

    // ------------------------------------------------------------------
    // 通用套用
    // ------------------------------------------------------------------

    /// <summary>给窗口套用主题（字体 + 底色）。</summary>
    public static void Apply(Form form)
    {
        form.Font = UiFont;
        form.BackColor = Surface;
        form.ForeColor = Text;
        form.AutoScaleMode = AutoScaleMode.Font;
    }

    /// <summary>垂直排列一组控件，统一左边距与间距（替代手写坐标）。</summary>
    public static int Stack(Control parent, int left, int top, int gap, params Control[] controls)
    {
        foreach (var c in controls)
        {
            c.Left = left;
            c.Top = top;
            parent.Controls.Add(c);
            top += c.Height + gap;
        }
        return top;
    }

    // ------------------------------------------------------------------
    // 控件样式
    // ------------------------------------------------------------------

    public static void StyleMenu(MenuStrip menu)
    {
        menu.Font = UiFont;
        menu.BackColor = Surface;
        menu.ForeColor = Text;
        menu.Padding = new Padding(6, 2, 0, 2);
        menu.Renderer = new FlatMenuRenderer();
    }

    public static void StyleToolbar(ToolStrip bar, bool primary = true, int extraLeft = 0)
    {
        bar.Font = UiFont;
        bar.GripStyle = ToolStripGripStyle.Hidden;
        bar.RenderMode = ToolStripRenderMode.System;
        bar.BackColor = primary ? Surface : Bg;
        bar.ForeColor = Text;
        bar.Padding = new Padding(8 + extraLeft, primary ? 6 : 4, 8, primary ? 6 : 4);
        bar.AutoSize = true;
        bar.Renderer = new FlatToolbarRenderer(primary);
        foreach (ToolStripItem it in bar.Items)
        {
            if (it is ToolStripButton b) { b.Margin = new Padding(2, 0, 2, 0); b.Padding = new Padding(4, 2, 4, 2); }
            else if (it is ToolStripSeparator) it.Margin = new Padding(6, 2, 6, 2);
        }
    }

    public static void StyleStatus(StatusStrip status)
    {
        status.Font = UiFont;
        status.BackColor = Surface;
        status.ForeColor = Muted;
        status.SizingGrip = false;
        status.Padding = new Padding(10, 2, 10, 2);
    }

    /// <summary>页签：扁平 + 选中项底部强调条（OwnerDraw）。</summary>
    public static void StyleTabs(TabControl tabs)
    {
        tabs.Font = UiFont;
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.Padding = new Point(14, 6);
        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.ItemSize = new Size(168, 30);
        tabs.DrawItem += (_, e) => DrawTab(tabs, e);
    }

    private static void DrawTab(TabControl tabs, DrawItemEventArgs e)
    {
        var page = tabs.TabPages[e.Index];
        bool sel = tabs.SelectedIndex == e.Index;
        var r = e.Bounds;
        using var back = new SolidBrush(sel ? Surface : Bg);
        e.Graphics.FillRectangle(back, r);
        if (sel)
        {
            using var accent = new SolidBrush(Accent);
            e.Graphics.FillRectangle(accent, new Rectangle(r.X + 10, r.Bottom - 3, r.Width - 20, 3));
        }
        TextRenderer.DrawText(e.Graphics, page.Text, tabs.Font, r, sel ? Text : Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using var line = new Pen(Border);
        e.Graphics.DrawLine(line, r.Right - 1, r.Top + 6, r.Right - 1, r.Bottom - 6);
    }

    public static void StyleTree(TreeView tree)
    {
        tree.Font = UiFont;
        tree.BackColor = Surface;
        tree.ForeColor = Text;
        tree.BorderStyle = BorderStyle.None;
        tree.ShowLines = false;
        tree.ShowRootLines = false;
        tree.ShowPlusMinus = true;
        tree.FullRowSelect = true;
        tree.HideSelection = false;
        tree.ItemHeight = 24;
        tree.Indent = 18;
        tree.LineColor = Border;
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.Font = UiFont;
        grid.BackgroundColor = Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Border;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Bg,
            ForeColor = Muted,
            Font = new Font(UiFont, FontStyle.Bold),
            SelectionBackColor = Bg,
            SelectionForeColor = Muted,
            Padding = new Padding(6, 0, 0, 0),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
        };
        grid.ColumnHeadersHeight = 30;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface,
            ForeColor = Text,
            SelectionBackColor = AccentSoft,
            SelectionForeColor = Text,
            Padding = new Padding(6, 0, 4, 0),
            WrapMode = DataGridViewTriState.True,
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = GridAltRow,
            SelectionBackColor = AccentSoft,
            SelectionForeColor = Text,
        };
        grid.RowTemplate.Height = 24;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AllowUserToResizeRows = false;
        grid.MultiSelect = false;
        DoubleBuffer(grid);
    }

    /// <summary>DataGridView 双缓冲（反射打开，滚动不闪）。</summary>
    public static void DoubleBuffer(Control c)
    {
        try
        {
            var prop = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            prop?.SetValue(c, true, null);
        }
        catch { }
    }

    /// <summary>主按钮（实心强调色）。</summary>
    public static void MakePrimary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = Accent;
        b.ForeColor = Color.White;
        b.Font = UiFont;
        b.Height = Math.Max(b.Height, 30);
        b.Cursor = Cursors.Hand;
    }

    /// <summary>次要按钮（描边）。</summary>
    public static void MakeGhost(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Border;
        b.FlatAppearance.BorderSize = 1;
        b.BackColor = Surface;
        b.ForeColor = Text;
        b.Font = UiFont;
        b.Height = Math.Max(b.Height, 30);
        b.Cursor = Cursors.Hand;
    }

    /// <summary>构造一个统一风格的标题标签。</summary>
    public static Label Heading(string text) => new()
    {
        Text = text,
        Font = new Font(UiFont.FontFamily, UiFont.Size + 2.5f, FontStyle.Bold),
        ForeColor = Text,
        AutoSize = true,
    };

    public static Label Caption(string text) => new()
    {
        Text = text,
        ForeColor = Muted,
        AutoSize = true,
    };

    /// <summary>把控件包一层留白的容器（用于统一内边距）。</summary>
    public static Panel Padded(Control child, int pad = 12)
    {
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(pad), BackColor = Surface };
        child.Dock = DockStyle.Fill;
        p.Controls.Add(child);
        return p;
    }

    // ------------------------------------------------------------------
    // 渲染器
    // ------------------------------------------------------------------

    private sealed class FlatMenuRenderer : ToolStripProfessionalRenderer
    {
        public FlatMenuRenderer() : base(new FlatColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Accent : Text;
            base.OnRenderItemText(e);
        }
    }

    private sealed class FlatColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => AccentSoft;
        public override Color MenuItemSelectedGradientBegin => AccentSoft;
        public override Color MenuItemSelectedGradientEnd => AccentSoft;
        public override Color MenuItemBorder => AccentSoft;
        public override Color MenuBorder => Border;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
    }

    private sealed class FlatToolbarRenderer : ToolStripProfessionalRenderer
    {
        private readonly bool _primary;
        public FlatToolbarRenderer(bool primary) : base(new FlatColors())
        {
            _primary = primary;
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) =>
            e.Graphics.Clear(_primary ? Surface : Bg);

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item is not ToolStripButton b) { base.OnRenderButtonBackground(e); return; }
            var r = new Rectangle(0, 0, e.Item.Width, e.Item.Height);
            if (b.Pressed || b.Checked)
            {
                using var br = new SolidBrush(AccentSoft);
                e.Graphics.FillRectangle(br, r);
            }
            else if (b.Selected)
            {
                using var br = new SolidBrush(Color.FromArgb(0xEF, 0xF2, 0xF7));
                e.Graphics.FillRectangle(br, r);
            }
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, r.Right - 1, 4, r.Right - 1, r.Height - 5);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Border);
            int x = e.Item.Width / 2;
            e.Graphics.DrawLine(pen, x, 5, x, e.Item.Height - 6);
        }
    }
}
