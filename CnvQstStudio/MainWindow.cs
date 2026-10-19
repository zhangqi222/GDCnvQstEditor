using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CnvQst;
using CnvQst.Cli;

namespace CnvQstStudio;

/// <summary>
/// 编辑器主窗口（MaterialDesign）。布局：
///   标题栏 → 全量菜单栏 → 文档页签（突出显示、带独立关闭按钮）→ 状态栏
/// 不设常驻工具栏：文档级操作走菜单，表格级操作走右键菜单（层次感）。
/// 所有可见文案走 <see cref="L"/>（中文为基准、语言包可扩展）。
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly TabControl _tabs = new();
    private readonly StatusBarItem _statusDoc = new();
    private readonly StatusBarItem _statusLang = new();
    private readonly StatusBarItem _statusVer = new();
    private readonly Grid _root = new();
    private Menu? _menu;

    public MainWindow()
    {
        Title = L.WindowTitle;
        Width = 1280;
        Height = 820;
        MinWidth = 960;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AllowDrop = true;

        // MaterialDesign：使用系统标题栏，无需自绘 TitleBar
        _tabs.SelectionChanged += (_, _) => UpdateStatus();
        _tabs.ItemContainerStyle = MkTabItemStyle();

        var status = new StatusBar();
        _statusDoc.HorizontalContentAlignment = HorizontalAlignment.Left;
        status.Items.Add(_statusDoc);
        status.Items.Add(new StatusBarItem { Content = "·", Foreground = WpfTheme.MutedBrush });
        status.Items.Add(_statusLang);
        status.Items.Add(_statusVer);

        for (int i = 0; i < 3; i++) _root.RowDefinitions.Add(new RowDefinition { Height = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Grid.SetRow(_tabs, 1);
        Grid.SetRow(status, 2);
        _root.Children.Add(_tabs);
        _root.Children.Add(status);

        Content = _root;
        BuildChrome();
        UpdateStatus();

        DragEnter += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
            foreach (string p in paths)
            {
                if (Directory.Exists(p)) ShowBatch(p);
                else if (File.Exists(p)) OpenFile(p);
            }
            e.Handled = true;
        };

        L.Changed += OnLanguageChanged;
        Closing += OnWindowClosing;
    }

    private DocView? Active => _tabs.SelectedItem is TabItem page ? page.Content as DocView : null;

    // ------------------------------------------------------------------
    // 页签样式：选中态突出（白底 + 强调色下边框），Header 含独立关闭按钮
    // ------------------------------------------------------------------

    private static Style MkTabItemStyle()
    {
        var template = new ControlTemplate(typeof(TabItem));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.Name = "Bd";
        bd.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)));
        bd.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xE4, 0xE7, 0xEC)));
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(1, 1, 1, 0));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6, 6, 0, 0));
        bd.SetValue(Border.MarginProperty, new Thickness(4, 4, 0, 0));
        bd.SetValue(Border.PaddingProperty, new Thickness(4, 4, 2, 4));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        cp.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        bd.AppendChild(cp);
        template.VisualTree = bd;

        var selBg = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selBg.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.White, "Bd"));
        selBg.Setters.Add(new Setter(Border.BorderBrushProperty, WpfTheme.AccentBrush, "Bd"));
        template.Triggers.Add(selBg);

        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(TabItem.TemplateProperty, template));
        style.Setters.Add(new Setter(TabItem.ForegroundProperty, WpfTheme.TextBrush));
        return style;
    }

    /// <summary>页签 Header：文件名 + 独立关闭按钮。</summary>
    private FrameworkElement MakeTabHeader(TabItem page, string title)
    {
        var dock = new DockPanel { LastChildFill = true };
        var close = new System.Windows.Controls.Button
        {
            Content = "✕",
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            FontSize = 11,
            Foreground = WpfTheme.MutedBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = L.CloseTab,
        };
        DockPanel.SetDock(close, Dock.Right);
        close.Click += (_, _) => CloseTab(page);
        var text = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0) };
        dock.Children.Add(close);
        dock.Children.Add(text);
        dock.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) CloseTab(page); };
        return dock;
    }

    private void SetTabHeader(TabItem page, DocView view) => page.Header = MakeTabHeader(page, view.Doc.Title);

    // ------------------------------------------------------------------
    // 菜单
    // ------------------------------------------------------------------

    private void BindKey(Key key, ModifierKeys mods, Action exec) =>
        InputBindings.Add(new KeyBinding(new RelayCommand(exec), key, mods));

    private void BuildChrome()
    {
        if (_menu != null) _root.Children.Remove(_menu);
        InputBindings.Clear();

        BindKey(Key.O, ModifierKeys.Control, OpenViaDialog);
        BindKey(Key.S, ModifierKeys.Control, SaveActive);
        BindKey(Key.S, ModifierKeys.Control | ModifierKeys.Shift, SaveActiveAs);
        BindKey(Key.W, ModifierKeys.Control, () => CloseTab(null));
        BindKey(Key.N, ModifierKeys.Control, NewConversation);
        BindKey(Key.N, ModifierKeys.Control | ModifierKeys.Shift, NewQuest);
        BindKey(Key.E, ModifierKeys.Control, () => ExportActive("txt"));
        BindKey(Key.B, ModifierKeys.Control, () => ShowBatch(null));
        BindKey(Key.R, ModifierKeys.Control, ReloadActive);
        BindKey(Key.L, ModifierKeys.Control, SelectLocTab);
        BindKey(Key.F5, ModifierKeys.None, VerifyActive);
        BindKey(Key.F1, ModifierKeys.None, ShowAbout);

        var menu = new Menu();

        var file = MkMenu(L.MFile);
        file.Items.Add(MkItem(L.OpenTitle, "Ctrl+O", OpenViaDialog));
        file.Items.Add(MkItem(L.OpenDirBatch, "Ctrl+B", () => ShowBatch(null)));
        file.Items.Add(new Separator());
        file.Items.Add(MkItem(L.Save, "Ctrl+S", SaveActive));
        file.Items.Add(MkItem(L.SaveAs, "Ctrl+Shift+S", SaveActiveAs));
        file.Items.Add(MkItem(L.CloseTab, "Ctrl+W", () => CloseTab(null)));
        file.Items.Add(new Separator());
        file.Items.Add(MkItem(L.Exit, "", Close));

        var newMenu = MkMenu(L.MNew);
        newMenu.Items.Add(MkItem(L.NewConversationMenu, "Ctrl+N", NewConversation));
        newMenu.Items.Add(MkItem(L.NewQuestMenu, "Ctrl+Shift+N", NewQuest));

        var import = MkMenu(L.MImport);
        import.Items.Add(MkItem(L.ImportTxtToActive, "", ImportTxtToActive));
        import.Items.Add(MkItem(L.ImportJsonNewTab, "", ImportJson));

        var export = MkMenu(L.MExport);
        export.Items.Add(MkItem(L.ExportTxtOfficial, "Ctrl+E", () => ExportActive("txt")));
        export.Items.Add(MkItem(L.ExportJson, "", () => ExportActive("json")));
        export.Items.Add(MkItem(L.ExportDump, "", () => ExportActive("dump")));

        var batch = MkMenu(L.MBatch);
        batch.Items.Add(MkItem(L.BatchPanel, "Ctrl+B", () => ShowBatch(null)));

        var tools = MkMenu(L.MTools);
        tools.Items.Add(MkItem(L.VerifyCurrent, "F5", VerifyActive));
        tools.Items.Add(MkItem(L.Anchors, "", ShowAnchors));
        tools.Items.Add(MkItem(L.ReloadCurrent, "Ctrl+R", ReloadActive));

        var view = MkMenu(L.MView);
        view.Items.Add(MkItem(L.LocStringTable, "Ctrl+L", SelectLocTab));

        var lang = MkMenu(L.MenuLanguage);
        foreach (var info in L.Languages)
        {
            var code = info.Code;
            var item = MkItem(info.Name, "", () => L.Use(code));
            item.IsCheckable = true;
            item.IsChecked = code == L.Code;
            lang.Items.Add(item);
        }
        lang.Items.Add(new Separator());
        lang.Items.Add(MkItem(L.OpenLangFolder, "", OpenLangFolder));

        var help = MkMenu(L.MHelp);
        help.Items.Add(MkItem(L.About, "F1", ShowAbout));

        foreach (var m in new[] { file, newMenu, import, export, batch, tools, view, lang, help }) menu.Items.Add(m);
        Grid.SetRow(menu, 0);
        _root.Children.Add(menu);
        _menu = menu;
    }

    private static MenuItem MkMenu(string header) => new() { Header = header };

    private static MenuItem MkItem(string header, string gesture, Action onClick) =>
        new() { Header = header, InputGestureText = gesture, Command = new RelayCommand(onClick) };

    /// <summary>语言切换：重建菜单 + 各页签标题与内容。</summary>
    private void OnLanguageChanged()
    {
        Title = L.WindowTitle;
        foreach (TabItem page in _tabs.Items)
        {
            var view = (DocView)page.Content;
            SetTabHeader(page, view);
            view.RefreshAll();
        }
        BuildChrome();
        UpdateStatus();
    }

    private void OpenLangFolder()
    {
        try
        {
            Directory.CreateDirectory(L.LangDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(L.LangDir) { UseShellExecute = true });
        }
        catch (Exception e) { Gui.Warn(this, L.F("无法打开语言包目录：{0}", e.Message), L.Warning); }
    }

    private void ShowAbout() => Gui.Info(this, L.AboutBody, L.AboutTitle);

    private void UpdateStatus()
    {
        _statusDoc.Content = Active?.Doc.Summary ?? L.StatusReady;
        _statusDoc.Foreground = Active?.Doc.ReadOnly == true ? WpfTheme.WarnBrush : WpfTheme.TextBrush;
        _statusLang.Content = L.DisplayName;
        _statusVer.Content = "CnvQstStudio 1.2";
    }

    // ------------------------------------------------------------------
    // 打开 / 关闭
    // ------------------------------------------------------------------

    private void OpenViaDialog()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = L.T("Grim Dawn 对话 / 任务|*.cnv;*.qst|全部文件|*.*") };
        if (dlg.ShowDialog(this) != true) return;
        OpenFile(dlg.FileName);
    }

    private void OpenFile(string path)
    {
        foreach (TabItem page in _tabs.Items)
        {
            var existing = (DocView)page.Content;
            if (existing.Doc.FilePath != null &&
                string.Equals(Path.GetFullPath(existing.Doc.FilePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            {
                _tabs.SelectedItem = page;
                return;
            }
        }
        try
        {
            AddTab(DocSession.Open(path));
        }
        catch (Exception e)
        {
            Gui.Warn(this, L.F("打开失败：{0}", e.Message), L.T("打开"));
        }
    }

    private void AddTab(DocSession doc)
    {
        var view = new DocView(doc);
        view.DirtyChanged += () =>
        {
            if (_tabs.SelectedItem is TabItem sel) SetTabHeader(sel, view);
            UpdateStatus();
        };
        var page = new TabItem { Content = view };
        SetTabHeader(page, view);
        _tabs.Items.Add(page);
        _tabs.SelectedItem = page;
        UpdateStatus();
    }

    private void CloseTab(TabItem? page)
    {
        page ??= _tabs.SelectedItem as TabItem;
        if (page == null) return;
        var view = (DocView)page.Content;
        if (view.Doc.Dirty)
        {
            var r = MessageBox.Show(this,
                L.F("{0} 有未保存修改，保存后再关闭？", view.Doc.Title),
                L.CloseTab, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes && !DoSave(view, saveAs: false)) return;
        }
        _tabs.Items.Remove(page);
        UpdateStatus();
    }

    // ------------------------------------------------------------------
    // 新建
    // ------------------------------------------------------------------

    private void NewConversation()
    {
        var dlg = new NewConversationDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        AddTab(DocSession.FromModel(Creation.NewConversation(dlg.Speaker, dlg.FirstText, dlg.EndText), null));
    }

    private void NewQuest()
    {
        var dlg = new NewQuestDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        AddTab(DocSession.FromModel(Creation.NewQuest(dlg.Group, dlg.QuestName, dlg.TaskTitle, dlg.TaskDesc), null));
    }

    // ------------------------------------------------------------------
    // 保存
    // ------------------------------------------------------------------

    private void SaveActive() => _ = DoSave(Active, saveAs: false);

    private void SaveActiveAs() => _ = DoSave(Active, saveAs: true);

    private bool DoSave(DocView? view, bool saveAs)
    {
        if (view == null) return false;
        var doc = view.Doc;
        if (doc.ReadOnly)
        {
            Gui.Warn(this, L.T("该文件为只读（解析或完整性检查未通过），不能保存。"), L.Save);
            return false;
        }
        string? path = doc.FilePath;
        if (saveAs || path == null)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = doc.Cnv != null ? L.T("Grim Dawn 对话|*.cnv") : L.T("Grim Dawn 任务|*.qst"),
                FileName = path != null ? Path.GetFileName(path)
                    : doc.Cnv != null ? L.T("未命名对话.cnv") : L.T("未命名任务.qst"),
            };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }
        string? err = doc.Save(path, backup: true);
        if (err != null)
        {
            Gui.Warn(this, err, L.Save);
            return false;
        }
        view.AfterSaved();
        Gui.Info(this, L.F("已保存 {0}（三检查通过，原文件已备份为 .bak）。", path), L.Save);
        return true;
    }

    // ------------------------------------------------------------------
    // 导入 / 导出
    // ------------------------------------------------------------------

    private void ImportTxtToActive()
    {
        var view = Active;
        if (view?.Doc.ReadOnly != false)
        {
            Gui.Warn(this, L.T("请先打开一个可编辑的 .cnv / .qst 页签。"), L.ImportTxtToActive);
            return;
        }
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = L.T("TXT|*.txt|全部文件|*.*") };
        if (dlg.ShowDialog(this) != true) return;
        var preview = new ImportTxtPreviewDialog(view.Doc, dlg.FileName) { Owner = this };
        if (preview.ShowDialog() != true || !preview.Applied) return;
        view.RefreshAll();
        Gui.Info(this, L.T("回填完成（尚未保存，请检查后保存）。"), L.ImportTxtToActive);
    }

    private void ImportJson()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = L.T("JSON|*.json|全部文件|*.*") };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            byte[] data = JsonIO.ImportToBytes(File.ReadAllText(dlg.FileName));
            object model = data.Length > 4 && data[0] == 'C' ? CnvFile.Parse(data) : QstFile.Parse(data);
            bool valid = model switch
            {
                CnvFile c => c.Valid,
                QstFile q => q.Valid,
                _ => false,
            };
            if (!valid) { Gui.Warn(this, L.T("JSON 反序列化后无法解析，文件可能被改坏。"), L.ImportJsonNewTab); return; }
            AddTab(DocSession.FromModel(model, null));
        }
        catch (Exception e)
        {
            Gui.Warn(this, L.F("导入失败：{0}", e.Message), L.ImportJsonNewTab);
        }
    }

    private void ExportActive(string kind)
    {
        var view = Active;
        if (view == null) { Gui.Warn(this, L.T("请先打开一个文件。"), L.MExport); return; }
        string filter = kind switch
        {
            "txt" => L.T("TXT|*.txt"),
            "json" => L.T("JSON|*.json"),
            _ => L.T("TXT|*.txt"),
        };
        string name = Path.GetFileNameWithoutExtension(view.Doc.FilePath
            ?? (view.Doc.Cnv != null ? L.T("未命名对话") : L.T("未命名任务")));
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = name + (kind == "json" ? ".json" : ".txt") };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            string content = kind switch
            {
                "txt" => view.Doc.Cnv != null ? TextExport.CnvTxt(view.Doc.Cnv) : TextExport.QstTxt(view.Doc.Qst!),
                "json" => view.Doc.JsonText(),
                _ => view.Doc.DumpText(),
            };
            File.WriteAllText(dlg.FileName, content, new System.Text.UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Gui.Warn(this, L.F("导出失败：{0}", e.Message), L.MExport);
        }
    }

    // ------------------------------------------------------------------
    // 工具
    // ------------------------------------------------------------------

    private void VerifyActive()
    {
        var view = Active;
        if (view == null) return;
        string? err = null;
        if (view.Doc.Cnv != null)
        {
            var re = CnvFile.Parse(view.Doc.Serialize());
            err = !re.Valid ? re.Error : !re.Tiled ? L.T("记录未铺满文件") : !re.LocOffsetMatches ? L.T("未落在声明的本地化偏移") : null;
        }
        else if (view.Doc.Qst != null)
        {
            var re = QstFile.Parse(view.Doc.Serialize());
            err = !re.Valid ? re.Error : !re.Tiled ? L.T("记录未铺满文件")
                : !re.LocOffsetMatches ? L.T("未落在声明的本地化偏移")
                : re.TextCountDrift != 0 ? L.F("串表条数偏离公式 {0}", re.TextCountDrift)
                : null;
        }
        if (err == null)
            Gui.Info(this, L.T("✓ 通过：解析、铺满文件、本地化偏移（QST 另含串表公式）全部正常。"), L.VerifyCurrent);
        else
            Gui.Warn(this, "✗ " + err, L.VerifyCurrent);
    }

    private void ShowAnchors()
    {
        var view = Active;
        if (view?.Doc.FilePath == null)
        {
            Gui.Warn(this, L.T("anchors 需要已保存的文件（当前页签未落盘）。"), L.Anchors);
            return;
        }
        var sw = new StringWriter();
        CliApp.Run(["anchors", view.Doc.FilePath], sw, sw);
        new TextWindow("anchors — " + Path.GetFileName(view.Doc.FilePath), sw.ToString()) { Owner = this }.Show();
    }

    private void ReloadActive()
    {
        var view = Active;
        if (view?.Doc.FilePath == null) return;
        if (view.Doc.Dirty &&
            MessageBox.Show(this, L.T("放弃未保存修改并重新加载？"), L.ReloadCurrent,
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        string path = view.Doc.FilePath;
        if (_tabs.SelectedItem is TabItem page) _tabs.Items.Remove(page);
        OpenFile(path);
    }

    private void ShowBatch(string? path)
    {
        var win = new BatchWindow { Owner = this };
        if (path != null) win.PresetDirectory(path);
        win.Show();
    }

    /// <summary>切换当前文档的 本地化字符串表 / 结构表 视图。</summary>
    private void SelectLocTab()
    {
        if (Active != null) Active.ToggleView();
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        foreach (TabItem page in _tabs.Items)
        {
            var view = (DocView)page.Content;
            if (view.Doc.Dirty)
            {
                var r = MessageBox.Show(this, L.F("{0} 有未保存修改，仍然退出？", view.Doc.Title), L.Exit,
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r == MessageBoxResult.No) e.Cancel = true;
                return;
            }
        }
    }
}
