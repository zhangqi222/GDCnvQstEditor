using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using CnvQst;
using CnvQst.Cli;

namespace CnvQstStudio;

/// <summary>对话框通用骨架：统一尺寸、居中与右下角按钮区。</summary>
internal static class DialogChrome
{
    public static void Init(Window w, string title, double width, double height, SizeToContent sizeToContent = SizeToContent.Manual)
    {
        w.Title = title;
        w.Width = width;
        w.Height = height;
        w.MinWidth = Math.Max(380, width * 0.7);
        w.MinHeight = Math.Max(240, height * 0.6);
        w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        w.ShowInTaskbar = false;
        w.SizeToContent = sizeToContent;
    }

    /// <summary>右下角按钮行（参数顺序与 WinForms 版相反：第一个为主按钮）。</summary>
    public static UIElement Footer(params ButtonBase[] buttons)
    {
        var sp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        foreach (var b in buttons) sp.Children.Add(b);
        return sp;
    }

    public static Button Button(string text, bool primary = false, double minWidth = 96)
    {
        if (primary)
            return new Wpf.Ui.Controls.Button { Content = text, Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, MinWidth = minWidth, MinHeight = 28, Margin = new Thickness(6, 0, 0, 0) };
        return new Button { Content = text, MinWidth = minWidth, MinHeight = 28, Margin = new Thickness(6, 0, 0, 0) };
    }

    public static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 17,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 8),
    };

    public static TextBlock Caption(string text) => new()
    {
        Text = text,
        Foreground = WpfTheme.MutedBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 6),
    };

    public static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

}

/// <summary>纯文本查看窗口（anchors 输出等）。</summary>
internal sealed class TextWindow : Window
{
    public TextWindow(string title, string text)
    {
        DialogChrome.Init(this, title, 940, 640);
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            FontFamily = WpfTheme.MonoFont,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
        };
        var copy = DialogChrome.Button(L.Copy, primary: true);
        copy.Click += (_, _) => { try { Clipboard.SetText(box.Text); } catch { } };

        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(copy, Dock.Bottom);
        root.Children.Add(copy);
        root.Children.Add(box);
        Content = root;
    }
}

/// <summary>字符串列表编辑（P body 的路径列表等）。</summary>
internal sealed class StringListDialog : Window
{
    public List<string> Items { get; private set; } = new();
    private readonly ListBox _list = new() { MinHeight = 220 };
    private readonly TextBox _edit = new();

    public StringListDialog(string title, List<string> items)
    {
        DialogChrome.Init(this, title, 600, 460);
        var add = DialogChrome.Button(L.Add, minWidth: 76);
        var del = DialogChrome.Button(L.Delete, minWidth: 76);
        add.Click += (_, _) => { _list.Items.Add(""); _list.SelectedIndex = _list.Items.Count - 1; _edit.Focus(); };
        del.Click += (_, _) =>
        {
            int i = _list.SelectedIndex;
            if (i >= 0) { _list.Items.RemoveAt(i); _edit.Text = ""; }
        };
        _list.SelectionChanged += (_, _) => _edit.Text = _list.SelectedItem?.ToString() ?? "";
        _edit.TextChanged += (_, _) =>
        {
            int i = _list.SelectedIndex;
            if (i >= 0) _list.Items[i] = _edit.Text;
        };

        var ok = DialogChrome.Button(L.Confirm, primary: true);
        ok.IsDefault = true;
        var cancel = DialogChrome.Button(L.Cancel);
        cancel.IsCancel = true;
        ok.Click += (_, _) =>
        {
            Items = _list.Items.Cast<object>().Select(o => o?.ToString() ?? "").ToList();
            DialogResult = true;
        };

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(DialogChrome.Footer(cancel, ok), Dock.Bottom);
        root.Children.Add(DialogChrome.Footer(cancel, ok));

        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(DialogChrome.Caption(L.T("每行一项；右侧按钮可增删。")));
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var add2 = new Button { Content = L.Add, MinWidth = 76, Margin = new Thickness(0, 0, 6, 0) };
        var del2 = new Button { Content = L.Delete, MinWidth = 76 };
        add2.Click += (_, _) => { _list.Items.Add(""); _list.SelectedIndex = _list.Items.Count - 1; _edit.Focus(); };
        del2.Click += (_, _) =>
        {
            int i = _list.SelectedIndex;
            if (i >= 0) { _list.Items.RemoveAt(i); _edit.Text = ""; }
        };
        btnRow.Children.Add(add2);
        btnRow.Children.Add(del2);
        top.Children.Add(btnRow);
        top.Children.Add(_edit);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(_list);
        Content = root;

        foreach (var s in items) _list.Items.Add(s);
    }
}

/// <summary>Lua 参数编辑（A/B/C/字符串 4 列）。</summary>
internal sealed class LuaParamsDialog : Window
{
    private readonly ScriptField _field;
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column };

    public LuaParamsDialog(ScriptField field)
    {
        _field = field;
        DialogChrome.Init(this, L.F("Lua 参数（{0} 项）", field.Params!.Count), 700, 440);
        _grid.Columns.Add(new DataGridTextColumn { Header = "u32 A", Binding = new Binding(nameof(LuaRow.A)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "u8 B", Binding = new Binding(nameof(LuaRow.B)), Width = new DataGridLength(60) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "u32 C", Binding = new Binding(nameof(LuaRow.C)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = L.T("字符串"), Binding = new Binding(nameof(LuaRow.S)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });

        var add = new Button { Content = L.T("添加参数"), MinWidth = 100, Margin = new Thickness(0, 0, 6, 0) };
        var del = new Button { Content = L.T("删除所选"), MinWidth = 100 };
        add.Click += (_, _) => { _field.Params!.Add(new LuaParam()); Reload(); };
        del.Click += (_, _) =>
        {
            int i = _grid.SelectedIndex;
            if (i is >= 0) { _field.Params!.RemoveAt(i); Reload(); }
        };
        _grid.CellEditEnding += (_, _) => Apply();

        var ok = DialogChrome.Button(L.Confirm, primary: true);
        ok.IsDefault = true;
        var cancel = DialogChrome.Button(L.Cancel);
        cancel.IsCancel = true;
        ok.Click += (_, _) => { Apply(); DialogResult = true; };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(add);
        bar.Children.Add(del);

        var root = new DockPanel { Margin = new Thickness(14) };
        var bottom = new StackPanel();
        bottom.Children.Add(bar);
        bottom.Children.Add(DialogChrome.Footer(cancel, ok));
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(_grid);
        Content = root;

        Reload();
    }

    private void Reload()
    {
        Apply();
        _grid.ItemsSource = _field.Params!.Select(p => new LuaRow(p)).ToList();
        Title = L.F("Lua 参数（{0} 项）", _field.Params!.Count);
    }

    private void Apply()
    {
        if (_grid.ItemsSource is not IEnumerable<LuaRow> rows) return;
        var list = rows.ToList();
        for (int i = 0; i < _field.Params!.Count && i < list.Count; i++)
        {
            var r = list[i];
            _field.Params[i].A = r.AValue;
            if (byte.TryParse(r.B, out byte b)) _field.Params[i].B = b;
            _field.Params[i].C = r.CValue;
            _field.Params[i].S.SetText(r.S);
        }
    }

    private sealed class LuaRow
    {
        private readonly LuaParam _p;
        public LuaRow(LuaParam p) => _p = p;
        public string A { get => Gui.U32Text(_p.A); set { try { _p.A = Gui.ParseU32(value); } catch { } } }
        public string B { get => _p.B.ToString(); set { } }
        public string C { get => Gui.U32Text(_p.C); set { try { _p.C = Gui.ParseU32(value); } catch { } } }
        public string S { get => _p.S.Text; set => _p.S.SetText(value); }
        public uint AValue => _p.A;
        public uint CValue => _p.C;
    }
}

/// <summary>新建对话对话框。</summary>
internal sealed class NewConversationDialog : Window
{
    public string Speaker { get; private set; } = "";
    public string FirstText { get; private set; } = "";
    public string EndText { get; private set; } = "";

    public NewConversationDialog()
    {
        DialogChrome.Init(this, L.NewConversationTitle, 540, 300);
        var sp = new TextBox { Text = "New Speaker" };
        var ft = new TextBox();
        var et = new TextBox();

        var ok = DialogChrome.Button(L.Create, primary: true);
        ok.IsDefault = true;
        var cancel = DialogChrome.Button(L.Cancel);
        cancel.IsCancel = true;
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(sp.Text))
            {
                Gui.Warn(this, L.SpeakerRequired, L.NewConversationTitle);
                return;
            }
            Speaker = sp.Text;
            FirstText = ft.Text;
            EndText = et.Text;
            DialogResult = true;
        };

        Content = Layout(
            DialogChrome.Heading(L.NewConversationTitle),
            DialogChrome.Caption(L.T("首句与结束语可以先留空，稍后在表格里补。")),
            Field(L.NewConversationSpeaker, sp),
            Field(L.NewConversationFirst, ft),
            Field(L.NewConversationEnd, et),
            DialogChrome.Footer(cancel, ok));
    }

    private static UIElement Layout(params UIElement[] items)
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        var footer = items[^1];
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var stack = new StackPanel();
        for (int i = 0; i < items.Length - 1; i++) stack.Children.Add(items[i]);
        root.Children.Add(stack);
        return root;
    }

    private static UIElement Field(string label, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        DialogChrome.Label(label).SetValue(Grid.ColumnProperty, 0);
        control.SetValue(Grid.ColumnProperty, 1);
        g.Children.Add(DialogChrome.Label(label));
        g.Children.Add(control);
        return g;
    }
}

/// <summary>新建任务对话框。</summary>
internal sealed class NewQuestDialog : Window
{
    public string Group { get; private set; } = "";
    public string QuestName { get; private set; } = "";
    public string TaskTitle { get; private set; } = "";
    public string TaskDesc { get; private set; } = "";

    public NewQuestDialog()
    {
        DialogChrome.Init(this, L.NewQuestTitle, 560, 380);
        var g = new TextBox { Text = "New Group" };
        var n = new TextBox { Text = "New Quest" };
        var t = new TextBox { Text = "New Task" };
        var d = new TextBox { AcceptsReturn = true, Height = 70, TextWrapping = TextWrapping.Wrap };

        var ok = DialogChrome.Button(L.Create, primary: true);
        ok.IsDefault = true;
        var cancel = DialogChrome.Button(L.Cancel);
        cancel.IsCancel = true;
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(n.Text))
            {
                Gui.Warn(this, L.QuestNameRequired, L.NewQuestTitle);
                return;
            }
            Group = g.Text;
            QuestName = n.Text;
            TaskTitle = t.Text;
            TaskDesc = d.Text;
            DialogResult = true;
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        var footer = DialogChrome.Footer(cancel, ok);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var stack = new StackPanel();
        stack.Children.Add(DialogChrome.Heading(L.NewQuestTitle));
        stack.Children.Add(DialogChrome.Caption(L.T("会生成一个含单个 Task 的 QST2 v9 骨架，之后可在表格里增删。")));
        stack.Children.Add(NewConversationDialogField(L.NewQuestGroup, g));
        stack.Children.Add(NewConversationDialogField(L.NewQuestName, n));
        stack.Children.Add(NewConversationDialogField(L.NewQuestTaskTitle, t));
        stack.Children.Add(NewConversationDialogField(L.NewQuestTaskDesc, d));
        root.Children.Add(stack);
        Content = root;
    }

    private static UIElement NewConversationDialogField(string label, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = DialogChrome.Label(label);
        lbl.SetValue(Grid.ColumnProperty, 0);
        control.SetValue(Grid.ColumnProperty, 1);
        g.Children.Add(lbl);
        g.Children.Add(control);
        return g;
    }
}

/// <summary>TXT 回填预览：txt 行 vs 当前本地化表逐行对照；行数不符禁止应用。</summary>
internal sealed class ImportTxtPreviewDialog : Window
{
    public bool Applied { get; private set; }

    public ImportTxtPreviewDialog(DocSession doc, string txtPath)
    {
        DialogChrome.Init(this, L.F("TXT 回填预览 — {0}", Path.GetFileName(txtPath)), 960, 620);
        var model = (object?)doc.Cnv ?? doc.Qst!;
        var preview = BatchOps.BuildImportPreview(model, txtPath);
        bool countOk = doc.PrimaryLanguage.Texts.Count == TextExport.SplitTxt(BatchOps.ReadTextAuto(txtPath)).Count;

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserSortColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
        };
        grid.Columns.Add(new DataGridTextColumn { Header = L.T("行"), Binding = new Binding(nameof(PreviewRow.Idx)), Width = new DataGridLength(60) });
        var colNew = new DataGridTextColumn { Header = L.T("txt 行"), Binding = new Binding(nameof(PreviewRow.New)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
        var colCur = new DataGridTextColumn { Header = L.T("当前文本"), Binding = new Binding(nameof(PreviewRow.Cur)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
        grid.Columns.Add(colNew);
        grid.Columns.Add(colCur);

        var rows = new List<PreviewRow>();
        foreach (var (idx, line, cur) in preview)
            rows.Add(new PreviewRow(idx.ToString(), line, cur, line != cur, line == "<缺失>" || cur == "<缺失>"));
        grid.ItemsSource = rows;

        var info = new TextBlock
        {
            Text = countOk
                ? L.F("行数匹配：{0} 行。红色行是将被替换的内容。", preview.Count)
                : L.T("行数不匹配：txt 与本地化表不一致，禁止回填（游戏按行号位置回填文本）。"),
            Foreground = countOk ? WpfTheme.TextBrush : WpfTheme.DangerBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var cancel = DialogChrome.Button(L.Cancel);
        cancel.IsCancel = true;
        var apply = DialogChrome.Button(L.T("应用回填"), primary: true, minWidth: 120);
        apply.IsEnabled = countOk;
        apply.Click += (_, _) =>
        {
            var lines = TextExport.SplitTxt(BatchOps.ReadTextAuto(txtPath));
            TextExport.ApplyTxt(doc.PrimaryLanguage, lines);
            Applied = true;
            DialogResult = true;
        };

        var root = new DockPanel { Margin = new Thickness(14) };
        var bottom = new StackPanel();
        bottom.Children.Add(info);
        bottom.Children.Add(DialogChrome.Footer(cancel, apply));
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(grid);
        Content = root;
    }

    private sealed class PreviewRow
    {
        public string Idx { get; }
        public string New { get; }
        public string Cur { get; }
        public Brush NewBrush { get; }
        public Brush CurBrush { get; }

        public PreviewRow(string idx, string nw, string cur, bool differ, bool missing)
        {
            Idx = idx;
            New = nw;
            Cur = cur;
            NewBrush = missing ? WpfTheme.DangerBrush : differ ? WpfTheme.DangerBrush : WpfTheme.TextBrush;
            CurBrush = missing ? WpfTheme.DangerBrush : WpfTheme.TextBrush;
        }
    }
}

/// <summary>条件/动作编辑模态窗：克隆编辑，确定才回填原列表。逻辑字（AND/OR）仅条件显示。</summary>
internal sealed class ScriptListDialog : Window
{
    private readonly ScriptList _original;
    private readonly ScriptList _work;
    private readonly DocSession _doc;
    private ScriptListEditor? _editor;

    private ScriptListDialog(DocSession doc, ScriptList original, bool isCondition)
    {
        _doc = doc;
        _original = original;
        _work = original.Clone();
        DialogChrome.Init(this, isCondition ? L.T("编辑条件", "Edit Conditions") : L.T("编辑动作", "Edit Actions"), 980, 640);

        var banner = new TextBlock
        {
            Text = isCondition
                ? L.T("⚑ 条件 —— 全部满足时，这段内容才会出现 / 生效")
                : L.T("⚡ 动作 —— 条件满足后要执行的效果"),
            Foreground = isCondition ? WpfTheme.AccentBrush : WpfTheme.OkBrush,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(12, 8, 8, 8),
            Background = isCondition
                ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFE))
                : new SolidColorBrush(Color.FromRgb(0xE2, 0xF3, 0xEA)),
        };

        var logicRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        if (isCondition)
        {
            logicRow.Children.Add(DialogChrome.Label(L.T("关系逻辑", "Logic") + ":"));
            var logic = new ComboBox { MinWidth = 150, Margin = new Thickness(8, 0, 0, 0) };
            logic.ItemsSource = new[] { L.T("满足全部（AND）"), L.T("满足任一（OR）") };
            logic.SelectedIndex = _work.Logic == 1 ? 1 : 0;
            logic.SelectionChanged += (_, _) => _work.Logic = logic.SelectedIndex == 1 ? 1u : 0u;
            logicRow.Children.Add(logic);
        }
        else
        {
            logicRow.Children.Add(DialogChrome.Caption(L.T("动作按顺序执行（格式上没有 AND/OR 关系逻辑）。")));
        }

        var ok = DialogChrome.Button(L.T("确定", "OK"), primary: true);
        ok.IsDefault = true;
        var cancel = DialogChrome.Button(L.T("取消", "Cancel"));
        cancel.IsCancel = true;
        ok.Click += (_, _) => DialogResult = true;

        _editor = new ScriptListEditor(_doc, _work, isCondition, () => { });

        var root = new DockPanel();
        DockPanel.SetDock(banner, Dock.Top);
        root.Children.Add(banner);
        var topStack = new StackPanel { Margin = new Thickness(10, 4, 10, 0) };
        topStack.Children.Add(logicRow);
        topStack.Children.Add(_editor);
        root.Children.Add(topStack);
        var footer = DialogChrome.Footer(cancel, ok);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        Content = root;
    }

    /// <summary>弹窗编辑；用户确定时回填原列表并返回 true。</summary>
    public static bool Show(DocSession doc, ScriptList original, bool isCondition, Window? owner)
    {
        var dlg = new ScriptListDialog(doc, original, isCondition) { Owner = owner };
        if (dlg.ShowDialog() != true) return false;
        original.CopyFrom(dlg._work);
        return true;
    }
}

/// <summary>CNV 节点属性对话框：类型 / flags / voPath / linkId。</summary>
internal sealed class CnvNodeDialog : Window
{
    private readonly CnvStep _step;
    private readonly CnvFile _cnv;
    private readonly ComboBox _type = new() { MinWidth = 200 };
    private readonly CheckBox _once = new() { Content = L.T("仅一次") };
    private readonly CheckBox _always = new() { Content = L.T("总是显示") };
    private readonly CheckBox _unimportant = new() { Content = L.T("不重要") };
    private readonly TextBox _voPath = new() { MinWidth = 320 };
    private readonly ComboBox _link = new() { MinWidth = 320 };

    private CnvNodeDialog(CnvFile c, CnvStep s)
    {
        _cnv = c;
        _step = s;
        DialogChrome.Init(this, L.T($"节点属性  [{s.Index}]", $"Node Properties  [{s.Index}]"), 540, 320);

        var types = new List<string>();
        for (uint t = 0; t <= 8; t++) types.Add(CnvNodeLabel.KindZh(t));
        _type.ItemsSource = types;
        _type.SelectedIndex = s.Type <= 8 ? (int)s.Type : 0;
        _once.IsChecked = (s.Unknown & 1) != 0;
        _always.IsChecked = (s.Unknown & 2) != 0;
        _unimportant.IsChecked = (s.Unknown & 4) != 0;
        _voPath.Text = s.VoPath.Text;
        _voPath.IsEnabled = c.Version >= 6;
        var links = new List<string>();
        for (int i = 0; i < c.Steps.Count; i++)
        {
            var t = c.TextFor(c.Steps[i]);
            links.Add($"{i}: {c.Steps[i].Kind} {(t.Length > 20 ? t[..20] + "…" : t)}");
        }
        _link.ItemsSource = links;
        _link.SelectedIndex = s.LinkId < (uint)c.Steps.Count ? (int)s.LinkId : -1;
        _link.IsEnabled = s.Type == Names.StepLink;

        var ok = DialogChrome.Button(L.T("确定", "OK"), primary: true);
        ok.IsDefault = true;
        var cancel = DialogChrome.Button(L.T("取消", "Cancel"));
        cancel.IsCancel = true;
        ok.Click += (_, _) => DialogResult = true;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(Field(L.T("类型") + ":", _type));
        stack.Children.Add(new TextBlock
        {
            Text = L.T("台词文本请在主界面底部编辑器中修改（选中节点行后编辑）。"),
            Foreground = WpfTheme.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
        var flagsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        flagsRow.Children.Add(new TextBlock { Text = L.T("显示开关") + ":", Width = 100, VerticalAlignment = VerticalAlignment.Center });
        flagsRow.Children.Add(_once);
        flagsRow.Children.Add(_always);
        flagsRow.Children.Add(_unimportant);
        stack.Children.Add(flagsRow);
        stack.Children.Add(Field(L.T("配音文件") + ":", _voPath));
        stack.Children.Add(Field(L.T("跳转到") + ":", _link));
        stack.Children.Add(DialogChrome.Footer(cancel, ok));
        Content = stack;
    }

    private static UIElement Field(string label, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = DialogChrome.Label(label);
        lbl.SetValue(Grid.ColumnProperty, 0);
        control.SetValue(Grid.ColumnProperty, 1);
        g.Children.Add(lbl);
        g.Children.Add(control);
        return g;
    }

    public static bool Show(DocSession doc, CnvStep s, Window? owner)
    {
        var dlg = new CnvNodeDialog(doc.Cnv!, s) { Owner = owner };
        if (dlg.ShowDialog() != true) return false;
        s.Type = (uint)dlg._type.SelectedIndex;
        // 只改低 3 位（编辑器开关），高位为游戏侧保留位，原样保留
        s.Unknown = (s.Unknown & ~7u) | (dlg._once.IsChecked == true ? 1u : 0u) | (dlg._always.IsChecked == true ? 2u : 0u) | (dlg._unimportant.IsChecked == true ? 4u : 0u);
        s.VoPath.SetText(dlg._voPath.Text);
        s.HasLink = s.Type == Names.StepLink;
        if (dlg._link.SelectedIndex >= 0 && s.HasLink) s.LinkId = (uint)dlg._link.SelectedIndex;
        return true;
    }
}

/// <summary>quest 头部属性对话框。flags 语义取自官方 Common.Quest+Flag：Primary/Secondary 互斥，Repeatable 独立。</summary>
internal sealed class QuestPropsDialog : Window
{
    private readonly TextBox _uid = new() { MinWidth = 160 };
    private readonly CheckBox _primary = new() { Content = L.T("主线任务 Primary（bit0）", "Primary (bit0)") };
    private readonly CheckBox _secondary = new() { Content = L.T("支线任务 Secondary（bit1）", "Secondary (bit1)") };
    private readonly CheckBox _repeatable = new() { Content = L.T("可重复 Repeatable（bit2）", "Repeatable (bit2)") };
    private readonly List<(string Label, TextBox Box, Action<string> Commit)> _textEditors = new();

    internal QuestPropsDialog(QstFile q)
    {
        DialogChrome.Init(this, L.T("任务属性", "Quest Properties"), 380, 260);
        _uid.Text = Gui.U32Text(q.Id);
        uint f = q.Unknown10;
        _primary.IsChecked = (f & 1) != 0;
        _secondary.IsChecked = (f & 2) != 0;
        _repeatable.IsChecked = (f & 4) != 0;
        // Primary / Secondary 互斥（照官方 setter 的行为）
        _primary.Checked += (_, _) => _secondary.IsChecked = false;
        _secondary.Checked += (_, _) => _primary.IsChecked = false;

        var ok = DialogChrome.Button(L.T("确定", "OK"), primary: true);
        ok.IsDefault = true;
        var cancel = DialogChrome.Button(L.T("取消", "Cancel"));
        cancel.IsCancel = true;
        ok.Click += (_, _) => DialogResult = true;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(Field("uid", _uid));
        stack.Children.Add(_primary);
        stack.Children.Add(_secondary);
        stack.Children.Add(_repeatable);
        stack.Children.Add(new TextBlock
        {
            Text = L.T("组名 / 任务名请在主界面底部编辑器中修改（选中对应行后编辑）。"),
            Foreground = WpfTheme.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
        stack.Children.Add(DialogChrome.Footer(cancel, ok));
        Content = stack;
    }

    private static UIElement Field(string label, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = DialogChrome.Label(label);
        lbl.SetValue(Grid.ColumnProperty, 0);
        control.SetValue(Grid.ColumnProperty, 1);
        g.Children.Add(lbl);
        g.Children.Add(control);
        return g;
    }

    public static bool Show(QstFile q, Window? owner)
    {
        var dlg = new QuestPropsDialog(q) { Owner = owner };
        if (dlg.ShowDialog() != true) return false;
        try { q.Id = Gui.ParseU32(dlg._uid.Text); } catch { }
        uint f = 0;
        if (dlg._primary.IsChecked == true) f |= 1;
        if (dlg._secondary.IsChecked == true) f |= 2;
        if (dlg._repeatable.IsChecked == true) f |= 4;
        q.Unknown10 = f;
        return true;
    }
}

/// <summary>QST 字段编辑对话框（task / objective / event 的 uid、flags 与门控位）。
/// 字段名与位含义来自官方 Common.dll（QuestNode_Task/Objective/Event）。</summary>
internal sealed class QstFieldsDialog : Window
{
    private readonly List<(string, TextBox)> _nums = new();
    private readonly List<(string, CheckBox)> _flags = new();

    internal QstFieldsDialog(QstRowOwner row)
    {
        DialogChrome.Init(this, L.T("字段编辑  " + row.NodeLabel, "Fields  " + row.NodeLabel), 460, 0, SizeToContent.Height);
        switch (row)
        {
            case QstTaskRow tr:
                _nums.Add((L.T("uid（引用标识）", "uid (referenced by)"), Num(tr.T.UnknownA8)));
                _nums.Add((L.T("flags（暂只有 None）", "flags (only None)"), Num(tr.T.UnknownAC)));
                _flags.Add((L.T("isBlocker（v≥8）", "isBlocker (v>=8)"), Check(tr.T.Flag8)));
                _flags.Add((L.T("dontPropagate（v≥9）", "dontPropagate (v>=9)"), Check(tr.T.Flag9)));
                break;
            case QstObjectiveRow or:
                _nums.Add((L.T("uid", "uid"), Num(or.O.Unknown0)));
                _nums.Add((L.T("flags", "flags"), Num(or.O.Unknown1)));
                break;
            case QstEventRow er:
                _nums.Add((L.T("flags", "flags"), Num(er.E.Unknown)));
                break;
        }

        var ok = DialogChrome.Button(L.T("确定", "OK"), primary: true);
        ok.IsDefault = true;
        var cancel = DialogChrome.Button(L.T("取消", "Cancel"));
        cancel.IsCancel = true;
        ok.Click += (_, _) => DialogResult = true;

        var stack = new StackPanel { Margin = new Thickness(16) };
        foreach (var (lab, n) in _nums) stack.Children.Add(Field(lab, n));
        foreach (var (lab, cb) in _flags) { cb.Content = lab; stack.Children.Add(cb); }
        stack.Children.Add(new TextBlock
        {
            Text = L.T("文本内容请在主界面底部编辑器中修改（选中行后编辑，支持双语）。"),
            Foreground = WpfTheme.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
        stack.Children.Add(DialogChrome.Footer(cancel, ok));
        Content = stack;
    }

    private static TextBox Num(uint v) => new() { Text = Gui.U32Text(v), MinWidth = 140 };

    private static CheckBox Check(bool v) => new() { IsChecked = v };

    private static UIElement Field(string label, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = DialogChrome.Label(label);
        lbl.SetValue(Grid.ColumnProperty, 0);
        control.SetValue(Grid.ColumnProperty, 1);
        g.Children.Add(lbl);
        g.Children.Add(control);
        return g;
    }

    public static bool Show(QstRowOwner row, Window? owner)
    {
        var dlg = new QstFieldsDialog(row) { Owner = owner };
        if (dlg.ShowDialog() != true) return false;
        foreach (var (lab, box) in dlg._nums) _ = lab;
        if (row is QstTaskRow tr)
        {
            try { tr.T.UnknownA8 = Gui.ParseU32(dlg._nums[0].Item2.Text); } catch { }
            try { tr.T.UnknownAC = Gui.ParseU32(dlg._nums[1].Item2.Text); } catch { }
            tr.T.Flag8 = dlg._flags[0].Item2.IsChecked == true;
            tr.T.Flag9 = dlg._flags[1].Item2.IsChecked == true;
        }
        else if (row is QstObjectiveRow or)
        {
            try { or.O.Unknown0 = Gui.ParseU32(dlg._nums[0].Item2.Text); } catch { }
            try { or.O.Unknown1 = Gui.ParseU32(dlg._nums[1].Item2.Text); } catch { }
        }
        else if (row is QstEventRow er)
        {
            try { er.E.Unknown = Gui.ParseU32(dlg._nums[0].Item2.Text); } catch { }
        }
        return true;
    }
}

/// <summary>新节点类型选择（添加子节点）。</summary>
internal sealed class CnvTypePicker : Window
{
    public uint Type { get; private set; }
    private readonly ComboBox _box = new() { MinWidth = 220 };

    public CnvTypePicker()
    {
        DialogChrome.Init(this, L.T("选择新节点类型", "Choose New Node Type"), 300, 0, SizeToContent.Height);
        var kinds = new List<string>();
        for (uint t = 0; t <= 8; t++) kinds.Add($"{t} {Names.CnvStepKind(t)}");
        _box.ItemsSource = kinds;
        _box.SelectedIndex = 1;

        var ok = DialogChrome.Button(L.T("确定", "OK"), primary: true);
        ok.IsDefault = true;
        ok.Click += (_, _) => { Type = (uint)_box.SelectedIndex; DialogResult = true; };

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(_box);
        stack.Children.Add(DialogChrome.Footer(ok));
        Content = stack;
    }
}
