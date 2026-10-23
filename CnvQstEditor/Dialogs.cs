using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CnvQst;
using CnvQst.Cli;

namespace CnvQstEditor;

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
    public static Control Footer(params Button[] buttons)
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
        var b = new Button { Content = text, MinWidth = minWidth, MinHeight = 28, Margin = new Thickness(6, 0, 0, 0) };
        if (primary) UiTheme.MakePrimary(b);
        return b;
    }

    /// <summary>取消按钮：点击关闭窗口（ShowDialog 得到 false），Esc 同效。</summary>
    public static Button Cancel(Window w, string? text = null)
    {
        var b = Button(text ?? L.Cancel);
        b.IsCancel = true;
        b.Click += (_, _) => w.Close();
        return b;
    }

    public static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 17,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 0, 0, 8),
    };

    public static TextBlock Caption(string text) => new()
    {
        Text = text,
        Foreground = UiTheme.MutedBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 6),
    };

    public static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>左标签 + 右控件的表单行（各编辑弹窗共用）。标签列自然宽度（Auto），间距 8，不再固定像素。</summary>
    public static Control FieldRow(string label, Control control)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = Label(label);
        lbl.Margin = new Thickness(0, 0, 8, 0);
        lbl.SetValue(Grid.ColumnProperty, 0);
        control.SetValue(Grid.ColumnProperty, 1);
        g.Children.Add(lbl);
        g.Children.Add(control);
        return g;
    }
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
            FontFamily = UiTheme.MonoFont,
            TextWrapping = TextWrapping.NoWrap,
        };
        var copy = DialogChrome.Button(L.Copy, primary: true);
        copy.Click += (_, _) => ClipboardHelper.SetText(box.Text ?? "");

        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(copy, Dock.Bottom);
        root.Children.Add(copy);
        root.Children.Add(box);
        Content = root;
    }
}

/// <summary>字符串列表编辑（P body 的路径列表等）：表格单元格直接编辑，按钮增删行。</summary>
internal sealed class StringListDialog : Window
{
    public List<string> Items { get; private set; } = new();
    private readonly List<PathRow> _rows = new();
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        CanUserReorderColumns = false,
        MinHeight = 220,
    };
    private readonly string _baseTitle;

    public StringListDialog(string title, List<string> items)
    {
        _baseTitle = title;
        DialogChrome.Init(this, title, 640, 460);
        _rows.AddRange(items.Select(s => new PathRow(s)));

        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = L.T("#"),
            Binding = new Binding(nameof(PathRow.Num)),
            IsReadOnly = true,
            Width = new DataGridLength(48),
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = L.T("Path"),
            Binding = new Binding(nameof(PathRow.S)) { Mode = BindingMode.TwoWay },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        Reload();

        var add = DialogChrome.Button(L.Add, minWidth: 76);
        var del = DialogChrome.Button(L.Delete, minWidth: 76);
        add.Click += (_, _) => { _rows.Add(new PathRow("")); Reload(); _grid.SelectedIndex = _rows.Count - 1; };
        del.Click += (_, _) =>
        {
            int i = _grid.SelectedIndex;
            if (i is >= 0 && i < _rows.Count) { _rows.RemoveAt(i); Reload(); }
        };

        var ok = DialogChrome.Button(L.Confirm, primary: true);
        ok.IsDefault = true;
        var cancel = DialogChrome.Cancel(this);
        ok.Click += (_, _) =>
        {
            _grid.CommitEdit(DataGridEditingUnit.Row, true);   // 提交编辑中的单元格，避免最后一笔未落盘
            Items = _rows.Select(r => r.S).ToList();
            Close(true);
        };

        var root = new DockPanel { Margin = new Thickness(14) };
        var footer = DialogChrome.Footer(cancel, ok);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(DialogChrome.Caption(L.T("One item per line; edit directly in the cell. Use the buttons to add or remove rows.")));
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        btnRow.Children.Add(add);
        btnRow.Children.Add(del);
        top.Children.Add(btnRow);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(_grid);
        Content = root;
    }

    /// <summary>重编序号并刷新表格（增删后调用）。</summary>
    private void Reload()
    {
        for (int i = 0; i < _rows.Count; i++) _rows[i].Num = i + 1;
        _grid.ItemsSource = null;
        _grid.ItemsSource = _rows;
        Title = L.F("{0} ({1} items)", _baseTitle, _rows.Count);
    }

    private sealed class PathRow
    {
        public PathRow(string s) => S = s;
        public int Num { get; set; }
        public string S { get; set; } = "";
    }
}

/// <summary>Lua 参数编辑（A/B/C 数值 + 字符串；数值列内嵌十进制/十六进制联动框）。</summary>
internal sealed class LuaParamsDialog : Window
{
    private readonly ScriptField _field;
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, CanUserReorderColumns = false };

    public LuaParamsDialog(ScriptField field)
    {
        _field = field;
        DialogChrome.Init(this, L.F("Lua parameters ({0})", field.Params!.Count), 1040, 480);
        _grid.Columns.Add(NumCol("u32 A", p => p.A, (p, v) => p.A = v));
        _grid.Columns.Add(NumCol("u8 B", p => p.B, (p, v) => p.B = (byte)v, max: 255));
        _grid.Columns.Add(NumCol("u32 C", p => p.C, (p, v) => p.C = v));
        _grid.Columns.Add(new DataGridTextColumn { Header = L.T("String"), Binding = new Binding(nameof(LuaRow.S)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        var add = new Button { Content = L.T("Add parameter"), MinWidth = 100, Margin = new Thickness(0, 0, 6, 0) };
        var del = new Button { Content = L.T("Delete selected"), MinWidth = 100 };
        add.Click += (_, _) => { _field.Params!.Add(new LuaParam()); Reload(); };
        del.Click += (_, _) =>
        {
            int i = _grid.SelectedIndex;
            if (i is >= 0) { _field.Params!.RemoveAt(i); Reload(); }
        };

        var ok = DialogChrome.Button(L.Confirm, primary: true);
        ok.IsDefault = true;
        var cancel = DialogChrome.Cancel(this);
        ok.Click += (_, _) => { Close(true); };

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

    /// <summary>数值列：单元格内嵌十进制单框（Lua 参数为传给脚本的数值，非 ID），保存进制提示放 ToolTip。</summary>
    private DataGridTemplateColumn NumCol(string header, Func<LuaParam, uint> get, Action<LuaParam, uint> set, uint max = uint.MaxValue)
    {
        return new DataGridTemplateColumn
        {
            Header = header,
            Width = new DataGridLength(300),
            CellTemplate = new FuncDataTemplate<LuaRow>((row, _) =>
            {
                var p = row!.P;
                var num = new NumPairBox(get(p), max, inlineHint: false);
                num.ValueChanged += () => set(p, num.Value);
                return num;
            }),
        };
    }

    private void Reload()
    {
        _grid.ItemsSource = _field.Params!.Select(p => new LuaRow(p)).ToList();
        Title = L.F("Lua parameters ({0})", _field.Params!.Count);
    }

    private sealed class LuaRow
    {
        public LuaRow(LuaParam p) => P = p;
        public LuaParam P { get; }
        public string S { get => P.S.Text; set => P.S.SetText(value); }
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
        var cancel = DialogChrome.Cancel(this);
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(sp.Text))
            {
                Gui.Warn(this, L.SpeakerRequired, L.NewConversationTitle);
                return;
            }
            Speaker = sp.Text ?? "";
            FirstText = ft.Text ?? "";
            EndText = et.Text ?? "";
            Close(true);
        };

        Content = Layout(
            DialogChrome.Heading(L.NewConversationTitle),
            DialogChrome.Caption(L.T("First line and closing line can be left empty and filled in the table later.")),
            DialogChrome.FieldRow(L.NewConversationSpeaker, sp),
            DialogChrome.FieldRow(L.NewConversationFirst, ft),
            DialogChrome.FieldRow(L.NewConversationEnd, et),
            DialogChrome.Footer(cancel, ok));
    }

    private static Control Layout(params Control[] items)
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
        var cancel = DialogChrome.Cancel(this);
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(n.Text))
            {
                Gui.Warn(this, L.QuestNameRequired, L.NewQuestTitle);
                return;
            }
            Group = g.Text ?? "";
            QuestName = n.Text ?? "";
            TaskTitle = t.Text ?? "";
            TaskDesc = d.Text ?? "";
            Close(true);
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        var footer = DialogChrome.Footer(cancel, ok);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var stack = new StackPanel();
        stack.Children.Add(DialogChrome.Heading(L.NewQuestTitle));
        stack.Children.Add(DialogChrome.Caption(L.T("Generates a QST2 v9 skeleton with a single task; add / remove more in the table.")));
        stack.Children.Add(DialogChrome.FieldRow(L.NewQuestGroup, g));
        stack.Children.Add(DialogChrome.FieldRow(L.NewQuestName, n));
        stack.Children.Add(DialogChrome.FieldRow(L.NewQuestTaskTitle, t));
        stack.Children.Add(DialogChrome.FieldRow(L.NewQuestTaskDesc, d));
        root.Children.Add(stack);
        Content = root;
    }
}

/// <summary>TXT 回填预览：txt 行 vs 当前本地化表逐行对照；行数不符禁止应用。</summary>
internal sealed class ImportTxtPreviewDialog : Window
{
    public bool Applied { get; private set; }

    public ImportTxtPreviewDialog(DocSession doc, string txtPath)
    {
        DialogChrome.Init(this, L.F("TXT backfill preview — {0}", Path.GetFileName(txtPath)), 960, 620);
        var model = (object?)doc.Cnv ?? doc.Qst!;
        var preview = BatchOps.BuildImportPreview(model, txtPath);
        bool countOk = doc.PrimaryLanguage.Texts.Count == TextExport.SplitTxt(BatchOps.ReadTextAuto(txtPath)).Count;

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
        };
        grid.Columns.Add(new DataGridTextColumn { Header = L.T("Line"), Binding = new Binding(nameof(PreviewRow.Idx)), Width = new DataGridLength(60) });
        grid.Columns.Add(new DataGridTextColumn { Header = L.T("TXT line"), Binding = new Binding(nameof(PreviewRow.New)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = L.T("Current text"), Binding = new Binding(nameof(PreviewRow.Cur)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        var rows = new List<PreviewRow>();
        foreach (var (idx, line, cur) in preview)
            rows.Add(new PreviewRow(idx.ToString(), line, cur));
        grid.ItemsSource = rows;

        var info = new TextBlock
        {
            Text = countOk
                ? L.F("Line count matches: {0} rows. Red rows will be replaced.", preview.Count)
                : L.T("Line count mismatch between TXT and the localization table — backfill is blocked (the game fills text by line position)."),
            Foreground = countOk ? UiTheme.TextBrush : UiTheme.DangerBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var cancel = DialogChrome.Cancel(this);
        var apply = DialogChrome.Button(L.T("Apply backfill"), primary: true, minWidth: 120);
        apply.IsEnabled = countOk;
        apply.Click += (_, _) =>
        {
            var lines = TextExport.SplitTxt(BatchOps.ReadTextAuto(txtPath));
            TextExport.ApplyTxt(doc.PrimaryLanguage, lines);
            Applied = true;
            Close(true);
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

    private sealed record PreviewRow(string Idx, string New, string Cur);
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
        DialogChrome.Init(this, isCondition ? L.T("Edit condition") : L.T("Edit action"), 980, 640);

        var banner = new TextBlock
        {
            Text = isCondition
                ? L.T("⚑ Conditions — all must match for this content to appear")
                : L.T("⚡ Actions — effects executed when conditions match"),
            Foreground = isCondition ? UiTheme.AccentBrush : UiTheme.OkBrush,
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(12, 8, 8, 8),
            Background = isCondition
                ? UiTheme.BannerBlueBrush
                : UiTheme.BannerGreenBrush,
        };

        // 关系逻辑只保留一处：条件由内嵌 ScriptListEditor 顶部的"关系逻辑"下拉统一提供；
        // 动作无 AND/OR 关系，仅提示按顺序执行。
        var logicRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        if (!isCondition)
        {
            logicRow.Children.Add(DialogChrome.Caption(L.T("Actions run in order (no AND/OR logic by format).")));
        }

        var ok = DialogChrome.Button(L.T("OK"), primary: true);
        ok.IsDefault = true;
        var cancel = DialogChrome.Cancel(this, L.T("Cancel"));
        ok.Click += (_, _) => { Close(true); };

        _editor = new ScriptListEditor(_doc, _work, isCondition, () => { });

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(banner, Dock.Top);
        root.Children.Add(banner);
        // 取消/确定固定底部；内容区最后添加以填满剩余空间（LastChildFill）。
        var footer = DialogChrome.Footer(cancel, ok);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var topStack = new StackPanel { Margin = new Thickness(10, 4, 10, 0) };
        topStack.Children.Add(logicRow);
        topStack.Children.Add(_editor);
        root.Children.Add(topStack);
        Content = root;
    }

    /// <summary>弹窗编辑；用户确定时回填原列表并返回 true。</summary>
    public static async Task<bool> Show(DocSession doc, ScriptList original, bool isCondition, Window? owner)
    {
        var dlg = new ScriptListDialog(doc, original, isCondition);
        if (await dlg.ShowDialog<bool>(owner!) != true) return false;
        original.CopyFrom(dlg._work);
        return true;
    }
}

/// <summary>
/// CNV 节点编辑对话框（添加 / 编辑共用同一弹窗，字段与保存逻辑完全一致）：
/// 类型 / flags / voPath / linkId。添加模式 = 表格先插入默认节点再弹本窗，取消即回滚。
/// </summary>
internal sealed class CnvNodeDialog : Window
{
    private readonly CnvStep _step;
    private readonly CnvFile _cnv;
    private readonly ComboBox _type = new() { MinWidth = 200 };
    private readonly CheckBox _once = new() { Content = L.T("Once only") };
    private readonly CheckBox _always = new() { Content = L.T("Always show") };
    private readonly CheckBox _unimportant = new() { Content = L.T("Unimportant") };
    private readonly TextBox _voPath = new() { MinWidth = 320 };
    private readonly ComboBox _link = new() { MinWidth = 320 };

    /// <summary>跳转目标下拉项：Label=截断显示文本（保持原样），Full=完整文本（ToolTip 悬停显示）。</summary>
    private sealed record LinkItem(string Label, string Full);

    private CnvNodeDialog(CnvFile c, CnvStep s, bool isNew)
    {
        _cnv = c;
        _step = s;
        string title = isNew
            ? L.F("Add node  [{0}]", s.Index)
            : L.F("Node editor  [{0}]", s.Index);
        DialogChrome.Init(this, title, 540, 320);

        // 类型名"原文（本地化）"（如 speech（台词）），与表格节点列一致
        var types = new List<string>();
        for (uint t = 0; t <= 8; t++) types.Add(CnvNodeLabel.KindEnZh(t));
        _type.ItemsSource = types;
        _type.SelectedIndex = s.Type <= 8 ? (int)s.Type : 0;
        _once.IsChecked = (s.Unknown & 1) != 0;
        _always.IsChecked = (s.Unknown & 2) != 0;
        _unimportant.IsChecked = (s.Unknown & 4) != 0;
        _voPath.Text = s.VoPath.Text;
        _voPath.IsEnabled = c.Version >= 6;
        var links = new List<LinkItem>();
        for (int i = 0; i < c.Steps.Count; i++)
        {
            string kind = CnvNodeLabel.KindEnZh(c.Steps[i].Type);
            string t = c.TextFor(c.Steps[i]).Replace("\n", " ").Replace("\r", "");
            links.Add(new LinkItem(
                $"{i}: {kind} {(t.Length > 20 ? t[..20] + "…" : t)}",
                t.Length > 0 ? $"{i}: {kind} {t}" : $"{i}: {kind}"));
        }
        _link.ItemsSource = links;
        // 下拉项与收起后选中项都走该模板：显示截断文本，鼠标悬停 ToolTip 弹出完整内容
        _link.ItemTemplate = new FuncDataTemplate<LinkItem>((li, _) =>
        {
            var tb = new TextBlock { Text = li?.Label ?? "", TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(tb, li?.Full ?? "");
            return tb;
        });
        _link.SelectedIndex = s.LinkId < (uint)c.Steps.Count ? (int)s.LinkId : -1;
        _link.IsEnabled = s.Type == Names.StepLink;
        // 切换类型为"跳转"时实时启用跳转目标下拉（仅跳转节点有 LinkId 字段）
        _type.SelectionChanged += (_, _) => _link.IsEnabled = _type.SelectedIndex == (int)Names.StepLink;

        var ok = DialogChrome.Button(L.T("OK"), primary: true);
        ok.IsDefault = true;
        var cancel = DialogChrome.Cancel(this, L.T("Cancel"));
        ok.Click += (_, _) => { Close(true); };

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(DialogChrome.FieldRow(L.T("Kind") + L.Colon, _type));
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Edit dialogue text in the bottom editor of the main window (select a node row first)."),
            Foreground = UiTheme.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
        var flagsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 6) };
        var flagsLabel = new TextBlock { Text = L.T("Display switches") + L.Colon, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        flagsRow.Children.Add(flagsLabel);
        flagsRow.Children.Add(_once);
        flagsRow.Children.Add(_always);
        flagsRow.Children.Add(_unimportant);
        stack.Children.Add(flagsRow);
        var voBrowse = DialogChrome.Button(L.T("Browse…"), primary: false, minWidth: 88);
        voBrowse.Margin = new Thickness(6, 0, 0, 0);
        voBrowse.IsEnabled = c.Version >= 6;
        voBrowse.Click += async (_, _) =>
        {
            string? rel = await FileBrowserDialog.Pick(this, "resources",
                L.T("Choose voice-over file"), new[] { "wav", "ogg" }, _voPath.Text);
            if (rel != null) _voPath.Text = rel.Replace('\\', '/');
        };
        var voRow = new DockPanel();
        DockPanel.SetDock(voBrowse, Dock.Right);
        voRow.Children.Add(voBrowse);
        voRow.Children.Add(_voPath);
        stack.Children.Add(DialogChrome.FieldRow(L.T("Voice file") + L.Colon, voRow));
        stack.Children.Add(DialogChrome.FieldRow(L.T("Jump to") + L.Colon, _link));
        stack.Children.Add(DialogChrome.Footer(cancel, ok));
        Content = stack;
    }

    /// <summary>把弹窗当前值写回节点（添加 / 编辑共用；只改低 3 位开关，高位为游戏侧保留位）。</summary>
    private void Apply()
    {
        _step.Type = (uint)Math.Max(0, _type.SelectedIndex);
        _step.Unknown = (_step.Unknown & ~7u)
            | (_once.IsChecked == true ? 1u : 0u)
            | (_always.IsChecked == true ? 2u : 0u)
            | (_unimportant.IsChecked == true ? 4u : 0u);
        _step.VoPath.SetText(_voPath.Text ?? "");
        _step.HasLink = _step.Type == Names.StepLink;
        if (_link.SelectedIndex >= 0 && _step.HasLink) _step.LinkId = (uint)_link.SelectedIndex;
    }

    /// <summary>编辑既有节点。</summary>
    public static async Task<bool> Show(DocSession doc, CnvStep s, Window? owner)
    {
        var dlg = new CnvNodeDialog(doc.Cnv!, s, isNew: false);
        if (await dlg.ShowDialog<bool>(owner!) != true) return false;
        dlg.Apply();
        return true;
    }

    /// <summary>添加新节点（与编辑共用同一弹窗；step 已由调用方插入，取消时由调用方回滚）。</summary>
    public static async Task<bool> ShowNew(CnvFile c, CnvStep s, Window? owner)
    {
        var dlg = new CnvNodeDialog(c, s, isNew: true);
        if (await dlg.ShowDialog<bool>(owner!) != true) return false;
        dlg.Apply();
        return true;
    }
}

/// <summary>quest 头部属性对话框。flags 语义取自官方 Common.Quest+Flag：Primary/Secondary 互斥，Repeatable 独立。</summary>
internal sealed class QuestPropsDialog : Window
{
    // 任务文件 Id：引用标识，固定十六进制单框（官方 QuestEditor 底部同款显示）
    private readonly NumPairBox _uid = new(0, mode: NumBoxMode.HexOnly);
    private readonly CheckBox _primary = new() { Content = L.T("Primary quest (bit0)") };
    private readonly CheckBox _secondary = new() { Content = L.T("Secondary quest (bit1)") };
    private readonly CheckBox _repeatable = new() { Content = L.T("Repeatable (bit2)") };

    internal QuestPropsDialog(QstFile q)
    {
        DialogChrome.Init(this, L.T("Quest properties"), 380, 260);
        _uid.SetValue(q.Id);
        uint f = q.Unknown10;
        _primary.IsChecked = (f & 1) != 0;
        _secondary.IsChecked = (f & 2) != 0;
        _repeatable.IsChecked = (f & 4) != 0;
        // Primary / Secondary 互斥（照官方 setter 的行为）
        _primary.IsCheckedChanged += (_, _) => { if (_primary.IsChecked == true) _secondary.IsChecked = false; };
        _secondary.IsCheckedChanged += (_, _) => { if (_secondary.IsChecked == true) _primary.IsChecked = false; };

        var ok = DialogChrome.Button(L.T("OK"), primary: true);
        ok.IsDefault = true;
        var cancel = DialogChrome.Cancel(this, L.T("Cancel"));
        ok.Click += (_, _) => { Close(true); };

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(DialogChrome.FieldRow("uid", _uid));
        stack.Children.Add(_primary);
        stack.Children.Add(_secondary);
        stack.Children.Add(_repeatable);
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Edit the group / quest names in the bottom editor of the main window (select the row first)."),
            Foreground = UiTheme.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
        stack.Children.Add(DialogChrome.Footer(cancel, ok));
        Content = stack;
    }

    public static async Task<bool> Show(QstFile q, Window? owner)
    {
        var dlg = new QuestPropsDialog(q);
        if (await dlg.ShowDialog<bool>(owner!) != true) return false;
        q.Id = dlg._uid.Value;
        uint f = 0;
        if (dlg._primary.IsChecked == true) f |= 1;
        if (dlg._secondary.IsChecked == true) f |= 2;
        if (dlg._repeatable.IsChecked == true) f |= 4;
        q.Unknown10 = f;
        return true;
    }
}

/// <summary>QST 节点编辑对话框（task / objective / event 的 uid、flags 与门控位）。
/// 字段名与位含义来自官方 Common.dll（QuestNode_Task/Objective/Event）。</summary>
internal sealed class QstFieldsDialog : Window
{
    private readonly List<(string, NumPairBox)> _nums = new();
    private readonly List<(string, CheckBox)> _flags = new();

    internal QstFieldsDialog(QstRowOwner row)
    {
        // uid 行在官方编辑器无编辑入口 → NumPairBox 只读展示（十六进制单框）；flags 为计数类小值走十进制单框
        DialogChrome.Init(this, L.T("Node editor") + "  " + row.NodeLabel, 660, 0, SizeToContent.Height);
        switch (row)
        {
            case QstTaskRow tr:
                _nums.Add((L.T("uid (reference id)"), new NumPairBox(tr.T.UnknownA8, mode: NumBoxMode.HexOnly) { IsEnabled = false }));
                _nums.Add((L.T("flags (only None)"), new NumPairBox(tr.T.UnknownAC)));
                _flags.Add((L.T("isBlocker (blocks subsequent tasks, v≥8)"), Check(tr.T.Flag8)));
                _flags.Add((L.T("dontPropagate (do not propagate upward, v≥9)"), Check(tr.T.Flag9)));
                break;
            case QstObjectiveRow or:
                _nums.Add((L.T("uid"), new NumPairBox(or.O.Unknown0, mode: NumBoxMode.HexOnly) { IsEnabled = false }));
                _nums.Add((L.T("flags"), new NumPairBox(or.O.Unknown1)));
                break;
            case QstEventRow er:
                _nums.Add((L.T("flags"), new NumPairBox(er.E.Unknown)));
                break;
        }

        var ok = DialogChrome.Button(L.T("OK"), primary: true);
        ok.IsDefault = true;
        var cancel = DialogChrome.Cancel(this, L.T("Cancel"));
        ok.Click += (_, _) => { Close(true); };

        var stack = new StackPanel { Margin = new Thickness(16) };
        foreach (var (lab, n) in _nums) stack.Children.Add(DialogChrome.FieldRow(lab, n));
        foreach (var (lab, cb) in _flags) { cb.Content = lab; stack.Children.Add(cb); }
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Edit text in the bottom editor of the main window (select the row first)."),
            Foreground = UiTheme.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
        stack.Children.Add(DialogChrome.Footer(cancel, ok));
        Content = stack;
    }

    private static CheckBox Check(bool v) => new() { IsChecked = v };

    public static async Task<bool> Show(QstRowOwner row, Window? owner)
    {
        var dlg = new QstFieldsDialog(row);
        if (await dlg.ShowDialog<bool>(owner!) != true) return false;
        if (row is QstTaskRow tr)
        {
            tr.T.UnknownA8 = dlg._nums[0].Item2.Value;
            tr.T.UnknownAC = dlg._nums[1].Item2.Value;
            tr.T.Flag8 = dlg._flags[0].Item2.IsChecked == true;
            tr.T.Flag9 = dlg._flags[1].Item2.IsChecked == true;
        }
        else if (row is QstObjectiveRow or)
        {
            or.O.Unknown0 = dlg._nums[0].Item2.Value;
            or.O.Unknown1 = dlg._nums[1].Item2.Value;
        }
        else if (row is QstEventRow er)
        {
            er.E.Unknown = dlg._nums[0].Item2.Value;
        }
        return true;
    }
}

/// <summary>
/// 设置弹窗：游戏根目录等。配置持久化到 %USERPROFILE%\.CnvQstEditor\CnvQstEditor.json（JSONC）。
/// 游戏根目录用于串字段"资源路径"的 打开 / 浏览：非 dbr → root\resources\，.dbr → root\database\。
/// </summary>
internal sealed class SettingsDialog : Window
{
    /// <summary>一个目录行：输入框 + database/resources 就绪指示。</summary>
    private sealed class DirRow
    {
        public required Control Host;
        public required TextBox Box;
        public required TextBlock Status;
    }

    private readonly TextBox _root = new() { Watermark = L.T(@"e.g. G:\GRIM DAWN"), MinWidth = 320 };
    private readonly TextBox _dlc1 = new() { Watermark = L.T(@"Auto-filled: <root>\GDX1"), MinWidth = 320 };
    private readonly TextBox _dlc2 = new() { Watermark = L.T(@"Auto-filled: <root>\GDX2"), MinWidth = 320 };
    private readonly TextBox _dlc3 = new() { Watermark = L.T(@"Auto-filled: <root>\GDX3"), MinWidth = 320 };
    private readonly TextBox _mod = new() { Watermark = L.T(@"Optional; e.g. G:\GD\MODS\MyMod"), MinWidth = 320 };
    private readonly DirRow _rootRow, _dlc1Row, _dlc2Row, _dlc3Row, _modRow;
    private readonly string _themeOriginal;   // 打开弹窗前的主题模式（取消时回退）
    private string _theme;                    // 弹窗内当前选择的主题模式

    internal SettingsDialog()
    {
        DialogChrome.Init(this, L.T("Settings"), 720, 0, SizeToContent.Height);
        _themeOriginal = AppSettings.ThemeMode;
        _theme = _themeOriginal;
        _root.Text = AppSettings.GameRoot;
        _dlc1.Text = AppSettings.Dlc1Dir;
        _dlc2.Text = AppSettings.Dlc2Dir;
        _dlc3.Text = AppSettings.Dlc3Dir;
        _mod.Text = AppSettings.ModDir;

        _rootRow = MakeRow(_root, withBrowse: true, L.T("Choose game root folder"));
        _dlc1Row = MakeRow(_dlc1, withBrowse: false, null);
        _dlc2Row = MakeRow(_dlc2, withBrowse: false, null);
        _dlc3Row = MakeRow(_dlc3, withBrowse: false, null);
        _modRow = MakeRow(_mod, withBrowse: true, L.T("Select MOD folder"));

        // 游戏根目录输入后自动填充 GDX1/2/3 完整路径（可再手工修改；再次改动根目录会重新填充）
        _root.TextChanged += (_, _) =>
        {
            string root = _root.Text?.Trim() ?? "";
            if (root.Length == 0)
            {
                _dlc1.Text = _dlc2.Text = _dlc3.Text = "";
            }
            else
            {
                try
                {
                    _dlc1.Text = GdAssets.GdInstall.AutoDlcPath(root, 1);
                    _dlc2.Text = GdAssets.GdInstall.AutoDlcPath(root, 2);
                    _dlc3.Text = GdAssets.GdInstall.AutoDlcPath(root, 3);
                }
                catch { /* 根目录非法时保留原值 */ }
            }
            UpdateAll();
        };
        _dlc1.TextChanged += (_, _) => UpdateAll();
        _dlc2.TextChanged += (_, _) => UpdateAll();
        _dlc3.TextChanged += (_, _) => UpdateAll();
        _mod.TextChanged += (_, _) => UpdateAll();
        UpdateAll();

        var ok = DialogChrome.Button(L.T("OK"), primary: true);
        ok.IsDefault = true;
        ok.Click += async (_, _) =>
        {
            AppSettings.GameRoot = _root.Text?.Trim() ?? "";
            AppSettings.Dlc1Dir = _dlc1.Text?.Trim() ?? "";
            AppSettings.Dlc2Dir = _dlc2.Text?.Trim() ?? "";
            AppSettings.Dlc3Dir = _dlc3.Text?.Trim() ?? "";
            AppSettings.ModDir = _mod.Text?.Trim() ?? "";
            AppSettings.ThemeMode = _theme;
            string? err = AppSettings.Save();
            if (err != null)
                await MsgBox.Show(this,
                    L.F("Settings are in effect, but writing the config file failed: {0}. They may be lost on next start — check whether the file is read-only or locked.", err),
                    L.T("Save settings"), MsgButtons.Ok, warning: true);
            Close(true);
        };
        var cancel = DialogChrome.Cancel(this, L.T("Cancel"));
        // 取消：主题恢复为打开弹窗前的模式（切换时已即时预览）
        cancel.Click += (_, _) => UiTheme.Apply(_themeOriginal);

        var stack = new StackPanel { Margin = new Thickness(16) };
        // 主题模式：跟随系统 / 明亮 / 暗色（切换即时生效预览，确定时写入配置）
        var themeRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 16 };
        foreach (var (value, label) in new[]
                 {
                     (UiTheme.ThemeSystem, L.T("Follow system")),
                     (UiTheme.ThemeLight, L.T("Light theme")),
                     (UiTheme.ThemeDark, L.T("Dark theme")),
                 })
        {
            var rb = new RadioButton { GroupName = "ThemeMode", Content = label, IsChecked = value == _theme };
            rb.IsCheckedChanged += (_, _) =>
            {
                if (rb.IsChecked == true)
                {
                    _theme = value;
                    UiTheme.Apply(value);
                }
            };
            themeRow.Children.Add(rb);
        }
        stack.Children.Add(DialogChrome.FieldRow(L.T("UI theme"), themeRow));
        stack.Children.Add(DialogChrome.FieldRow(L.T("Game root folder"), _rootRow.Host));
        stack.Children.Add(DialogChrome.FieldRow(L.T("DLC1 folder"), _dlc1Row.Host));
        stack.Children.Add(DialogChrome.FieldRow(L.T("DLC2 folder"), _dlc2Row.Host));
        stack.Children.Add(DialogChrome.FieldRow(L.T("DLC3 folder"), _dlc3Row.Host));
        stack.Children.Add(DialogChrome.FieldRow(L.T("MOD folder"), _modRow.Host));

        // 预览内置文件树：database 仅 .dbr；resources 排除 ARZ / ARC / DBR
        var previewRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var prevDb = DialogChrome.Button(L.T("Preview database files"), primary: false);
        prevDb.Click += async (_, _) => await FileBrowserDialog.Preview(this,
            "database", L.T("Preview database files"), null, new[] { "dbr", "tpl" }, null);
        var prevRes = DialogChrome.Button(L.T("Preview resources files"), primary: false);
        prevRes.Click += async (_, _) => await FileBrowserDialog.Preview(this,
            "resources", L.T("Preview resources files"),
            L.T("Scope: resources\\ in each layer, excluding ARZ / ARC / DBR (qst, cnv, wav, ogg, tex, fx, anm, lua, etc.)"),
            null, new[] { "arz", "arc", "dbr" });
        previewRow.Children.Add(prevDb);
        previewRow.Children.Add(prevRes);
        stack.Children.Add(previewRow);
        stack.Children.Add(new TextBlock
        {
            Text = L.T("Load order: game root → DLC1 → DLC2 → DLC3 → MOD; later layers override same-name files and add new ones. Each layer needs a database and/or resources subfolder. GDX1/2/3 paths are auto-filled from the game root (editable)."),
            Foreground = UiTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 2),
        });
        stack.Children.Add(DialogChrome.Caption(L.T("Note: the browser merges files from each layer's database / resources folder using the override rules above (source tag shown by name). Config is saved to %USERPROFILE%\\.CnvQstEditor\\CnvQstEditor.json.")));
        stack.Children.Add(DialogChrome.Footer(cancel, ok));
        Content = stack;
    }

    /// <summary>构造一行：输入框（+可选浏览按钮）+ 状态指示；并缓存引用。</summary>
    private DirRow MakeRow(TextBox box, bool withBrowse, string? browseTitle)
    {
        var status = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        box.SetValue(Grid.ColumnProperty, 0);
        host.Children.Add(box);
        if (withBrowse)
        {
            var browse = DialogChrome.Button(L.T("Browse…"), primary: false);
            browse.Margin = new Thickness(8, 0, 0, 0);
            browse.SetValue(Grid.ColumnProperty, 1);
            browse.Click += async (_, _) =>
            {
                var tl = TopLevel.GetTopLevel(this);
                if (tl?.StorageProvider == null) return;
                var opts = new FolderPickerOpenOptions { AllowMultiple = false, Title = browseTitle ?? "" };
                string cur = box.Text?.Trim() ?? "";
                if (Directory.Exists(cur))
                {
                    try { opts.SuggestedStartLocation = await tl.StorageProvider.TryGetFolderFromPathAsync(new Uri(cur)); }
                    catch { }
                }
                var folders = await tl.StorageProvider.OpenFolderPickerAsync(opts);
                if (folders.Count > 0)
                    box.Text = folders[0].TryGetLocalPath() ?? box.Text;
            };
            host.Children.Add(browse);
        }
        status.SetValue(Grid.ColumnProperty, 2);
        host.Children.Add(status);
        return new DirRow { Box = box, Status = status, Host = host };
    }

    private void UpdateAll()
    {
        UpdateRow(_rootRow);
        UpdateRow(_dlc1Row);
        UpdateRow(_dlc2Row);
        UpdateRow(_dlc3Row);
        UpdateRow(_modRow);
    }

    /// <summary>刷新一行的 database/resources 指示：√ 绿、× 红；目录未填时灰显无标记。</summary>
    private static void UpdateRow(DirRow row)
    {
        string dir = row.Box.Text?.Trim() ?? "";
        var status = row.Status;
        var inlines = new Avalonia.Controls.Documents.InlineCollection();
        if (dir.Length == 0)
        {
            // 目录未填：灰色提示，无 √/×
            inlines.Add(new Avalonia.Controls.Documents.Run("database  resources") { Foreground = UiTheme.MutedBrush });
        }
        else
        {
            var st = GdAssets.GdInstall.Check(dir);
            void AddPart(string name, bool ok)
            {
                if (inlines.Count > 0) inlines.Add(new Avalonia.Controls.Documents.Run("  ") { Foreground = UiTheme.MutedBrush });
                inlines.Add(new Avalonia.Controls.Documents.Run(name + " ") { Foreground = UiTheme.MutedBrush });
                inlines.Add(new Avalonia.Controls.Documents.Run(ok ? "√" : "×")
                {
                    Foreground = ok ? UiTheme.OkBrush : UiTheme.DangerBrush,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                });
            }
            AddPart("database", st.HasDatabase);
            AddPart("resources", st.HasResources);
        }
        status.Inlines = inlines;
    }
}
