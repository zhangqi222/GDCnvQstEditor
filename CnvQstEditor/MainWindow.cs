using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CnvQst;
using CnvQst.Cli;

namespace CnvQstEditor;

/// <summary>
/// 编辑器主窗口（Semi.Avalonia）。布局：
///   标题栏 → 全量菜单栏 → 常用操作工具条（Semi Light secondary 按钮）→ 文档页签（突出显示、带独立关闭按钮）→ 状态栏
/// 文档级操作菜单与工具条双入口，表格级操作走右键菜单。
/// 所有可见文案走 <see cref="L"/>（中文为基准、语言包可扩展）。
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly TabControl _tabs = new();
    private readonly TextBlock _statusDoc = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _statusLang = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _statusVer = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.MutedBrush };
    private readonly Grid _root = new();
    private Menu? _menu;
    private MenuItem? _recentMenu;
    private Control? _toolbar;
    private bool _forceClose;

    public MainWindow()
    {
        Title = L.WindowTitle;
        Width = 1280;
        Height = 820;
        MinWidth = 960;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _tabs.SelectionChanged += (_, _) => UpdateStatus();

        var status = new Border
        {
            BorderBrush = UiTheme.MutedBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { _statusDoc, new TextBlock { Text = "·", VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.MutedBrush }, _statusLang, _statusVer },
            },
        };

        for (int i = 0; i < 4; i++) _root.RowDefinitions.Add(new RowDefinition { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Grid.SetRow(_tabs, 2);
        Grid.SetRow(status, 3);
        _root.Children.Add(_tabs);
        _root.Children.Add(status);

        Content = _root;
        BuildChrome();
        UpdateStatus();

        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var paths = e.Data.GetFiles()?.Select(f => f.Path.LocalPath).ToArray();
            if (paths == null) return;
            foreach (string p in paths)
            {
                if (Directory.Exists(p)) ShowBatch(p);
                else if (File.Exists(p)) _ = OpenFile(p);
            }
            e.Handled = true;
        });

        L.Changed += OnLanguageChanged;
        Closing += OnWindowClosing;
    }

    private DocView? Active => _tabs.SelectedItem is TabItem page ? page.Content as DocView : null;

    // ------------------------------------------------------------------
    // 页签：Header 含独立关闭按钮（Semi 主题自带选中态高亮）
    // ------------------------------------------------------------------

    /// <summary>页签 Header：文件名 + 独立关闭按钮（中键关闭）。</summary>
    private Control MakeTabHeader(TabItem page, string title)
    {
        var dock = new DockPanel { LastChildFill = true };
        var close = new Button
        {
            Content = "✕",
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            FontSize = 11,
            Foreground = UiTheme.MutedBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(close, L.CloseTab);
        DockPanel.SetDock(close, Dock.Right);
        close.Click += (_, _) => _ = CloseTab(page);
        var text = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0) };
        dock.Children.Add(close);
        dock.Children.Add(text);
        dock.PointerReleased += (_, e) =>
        {
            if (e.GetCurrentPoint(dock).Properties.IsMiddleButtonPressed) _ = CloseTab(page);
        };
        return dock;
    }

    private void SetTabHeader(TabItem page, DocView view) => page.Header = MakeTabHeader(page, view.Doc.Title);

    // ------------------------------------------------------------------
    // 菜单（快捷键用 MenuItem.HotKey，窗口聚焦时全局生效）
    // ------------------------------------------------------------------

    private void BuildChrome()
    {
        if (_menu != null) _root.Children.Remove(_menu);
        if (_toolbar != null) _root.Children.Remove(_toolbar);

        var menu = new Menu();

        var file = MkMenu(L.MFile);
        file.Items.Add(MkItem(L.OpenTitle, new KeyGesture(Key.O, KeyModifiers.Control), () => _ = OpenViaDialog()));
        file.Items.Add(MkItem(L.OpenDirBatch, new KeyGesture(Key.B, KeyModifiers.Control), () => ShowBatch(null)));
        _recentMenu = MkMenu(L.T("最近打开", "Recent Files"));
        file.Items.Add(_recentMenu);
        file.Items.Add(new Separator());
        file.Items.Add(MkItem(L.Save, new KeyGesture(Key.S, KeyModifiers.Control), () => _ = SaveActive()));
        file.Items.Add(MkItem(L.SaveAs, new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift), () => _ = SaveActiveAs()));
        file.Items.Add(MkItem(L.CloseTab, new KeyGesture(Key.W, KeyModifiers.Control), () => _ = CloseTab(null)));
        file.Items.Add(new Separator());
        file.Items.Add(MkItem(L.Exit, null, Close));

        var newMenu = MkMenu(L.MNew);
        newMenu.Items.Add(MkItem(L.NewConversationMenu, new KeyGesture(Key.N, KeyModifiers.Control), () => _ = NewConversation()));
        newMenu.Items.Add(MkItem(L.NewQuestMenu, new KeyGesture(Key.N, KeyModifiers.Control | KeyModifiers.Shift), () => _ = NewQuest()));

        var import = MkMenu(L.MImport);
        import.Items.Add(MkItem(L.ImportTxtToActive, null, () => _ = ImportTxtToActive()));
        import.Items.Add(MkItem(L.ImportJsonNewTab, null, () => _ = ImportJson()));

        var export = MkMenu(L.MExport);
        export.Items.Add(MkItem(L.ExportTxtOfficial, new KeyGesture(Key.E, KeyModifiers.Control), () => _ = ExportActive("txt")));
        export.Items.Add(MkItem(L.ExportJson, null, () => _ = ExportActive("json")));
        export.Items.Add(MkItem(L.ExportDump, null, () => _ = ExportActive("dump")));

        var batch = MkMenu(L.MBatch);
        batch.Items.Add(MkItem(L.BatchPanel, new KeyGesture(Key.B, KeyModifiers.Control), () => ShowBatch(null)));

        var tools = MkMenu(L.MTools);
        tools.Items.Add(MkItem(L.VerifyCurrent, new KeyGesture(Key.F5), VerifyActive));
        tools.Items.Add(MkItem(L.Anchors, null, ShowAnchors));
        tools.Items.Add(MkItem(L.ReloadCurrent, new KeyGesture(Key.R, KeyModifiers.Control), () => _ = ReloadActive()));
        tools.Items.Add(new Separator());
        tools.Items.Add(MkItem(L.T("设置…", "Settings…"), null, () => _ = ShowSettings()));

        var lang = MkMenu(L.MenuLanguage);
        foreach (var info in L.Languages)
        {
            var code = info.Code;
            var item = MkItem(info.Name, null, () => L.Use(code));
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsChecked = code == L.Code;
            lang.Items.Add(item);
        }
        lang.Items.Add(new Separator());
        lang.Items.Add(MkItem(L.OpenLangFolder, null, OpenLangFolder));

        var help = MkMenu(L.MHelp);
        help.Items.Add(MkItem(L.About, new KeyGesture(Key.F1), ShowAbout));

        foreach (var m in new[] { file, newMenu, import, export, batch, tools, lang, help }) menu.Items.Add(m);
        Grid.SetRow(menu, 0);
        _root.Children.Add(menu);
        _menu = menu;

        // 常用操作工具条（菜单下、页签上）：Semi Light 主题 secondary 按钮
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(10, 6, 10, 4) };
        foreach (var (label, action) in new (string, Action)[]
        {
            (L.OpenTitle, () => _ = OpenViaDialog()),
            (L.Save, () => _ = SaveActive()),
            (L.SaveAs, () => _ = SaveActiveAs()),
            (L.VerifyCurrent, VerifyActive),
            (L.BatchPanel, () => ShowBatch(null)),
            (L.ReloadCurrent, () => _ = ReloadActive()),
            (L.T("设置…", "Settings…"), () => _ = ShowSettings()),
        })
            toolbar.Children.Add(ToolBtn(label, action));
        Grid.SetRow(toolbar, 1);
        _root.Children.Add(toolbar);
        _toolbar = toolbar;
        RefreshRecentMenu();
    }

    /// <summary>“最近打开”子菜单：按配置顺序列出现存文件，附“清除历史记录”。</summary>
    private void RefreshRecentMenu()
    {
        if (_recentMenu == null) return;
        _recentMenu.Items.Clear();
        var paths = AppSettings.RecentFiles
            .Where(File.Exists)
            .Take(AppSettings.MaxRecentFiles)
            .ToList();
        if (paths.Count == 0)
        {
            var empty = MkItem(L.T("（无最近打开的文件）", "(No recent files)"), null, () => { });
            empty.IsEnabled = false;
            _recentMenu.Items.Add(empty);
        }
        else
        {
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                var item = MkItem($"{i + 1}. {Path.GetFileName(path)}", null, () => _ = OpenFile(path));
                ToolTip.SetTip(item, path);
                _recentMenu.Items.Add(item);
            }
        }
        _recentMenu.Items.Add(new Separator());
        var clear = MkItem(L.T("清除历史记录", "Clear History"), null, () =>
        {
            AppSettings.ClearRecent();
            RefreshRecentMenu();
        });
        clear.IsEnabled = AppSettings.RecentFiles.Count > 0;
        _recentMenu.Items.Add(clear);
    }

    /// <summary>工具条按钮：Semi Light 主题 secondary 样式（浅色底、主题色文字，与菜单主色呼应）。
    /// Semi.Avalonia 的按钮变体经样式类切换：light = 主题 Light、secondary = 次要色。</summary>
    private static Button ToolBtn(string text, Action onClick)
    {
        var b = new Button { Content = text };
        b.Classes.Add("light");
        b.Classes.Add("secondary");
        b.Click += (_, _) => onClick();
        return b;
    }

    private static MenuItem MkMenu(string header) => new() { Header = header };

    private static MenuItem MkItem(string header, KeyGesture? hotkey, Action onClick) =>
        new() { Header = header, HotKey = hotkey, Command = new RelayCommand(onClick) };

    /// <summary>语言切换：重建菜单 + 各页签标题与内容。</summary>
    private void OnLanguageChanged()
    {
        Title = L.WindowTitle;
        foreach (TabItem page in _tabs.Items)
        {
            var view = (DocView)page.Content!;
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

    private void ShowAbout() =>
        Gui.Info(this, L.AboutBody + "\n\n" + L.F("版本：{0}", AppInfo.Version) + "\n\n" + L.AboutLicense, L.AboutTitle);

    private void UpdateStatus()
    {
        _statusDoc.Text = Active?.Doc.Summary ?? L.StatusReady;
        _statusDoc.Foreground = Active?.Doc.ReadOnly == true ? UiTheme.WarnBrush : UiTheme.TextBrush;
        _statusLang.Text = L.DisplayName;
        _statusVer.Text = "CnvQstEditor " + AppInfo.Version;
    }

    // ------------------------------------------------------------------
    // 打开 / 关闭
    // ------------------------------------------------------------------

    private async Task OpenViaDialog()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.OpenTitle,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(L.T("Grim Dawn 对话 / 任务")) { Patterns = new[] { "*.cnv", "*.qst" } },
                new FilePickerFileType(L.T("全部文件")) { Patterns = new[] { "*" } },
            },
        });
        if (files.Count == 0) return;
        await OpenFile(files[0].Path.LocalPath);
    }

    private async Task OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            AppSettings.RemoveRecent(path);
            RefreshRecentMenu();
            Gui.Warn(this, L.F("文件不存在：{0}", path), L.T("打开"));
            return;
        }
        foreach (TabItem page in _tabs.Items)
        {
            var existing = (DocView)page.Content!;
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
            AppSettings.AddRecent(path);
            RefreshRecentMenu();
        }
        catch (Exception e)
        {
            Gui.Warn(this, L.F("打开失败：{0}", e.Message), L.T("打开"));
        }
        await Task.CompletedTask;
    }

    /// <summary>冒烟测试专用：内存新建文档页签（不落盘、不弹框）。</summary>
    public void OpenSmokeTab(object model) => AddTab(DocSession.FromModel(model, null));

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

    private async Task<bool> CloseTab(TabItem? page)
    {
        page ??= _tabs.SelectedItem as TabItem;
        if (page == null) return false;
        var view = (DocView)page.Content!;
        if (view.Doc.Dirty)
        {
            var r = await MsgBox.Show(this,
                L.F("{0} 有未保存修改，保存后再关闭？", view.Doc.Title),
                L.CloseTab, MsgButtons.YesNoCancel, warning: true);
            if (r == MsgResult.Cancel) return false;
            if (r == MsgResult.Yes && !await DoSave(view, saveAs: false)) return false;
        }
        _tabs.Items.Remove(page);
        UpdateStatus();
        return true;
    }

    // ------------------------------------------------------------------
    // 新建
    // ------------------------------------------------------------------

    private async Task NewConversation()
    {
        var dlg = new NewConversationDialog();
        if (await dlg.ShowDialog<bool>(this) != true) return;
        AddTab(DocSession.FromModel(Creation.NewConversation(dlg.Speaker, dlg.FirstText, dlg.EndText), null));
    }

    private async Task NewQuest()
    {
        var dlg = new NewQuestDialog();
        if (await dlg.ShowDialog<bool>(this) != true) return;
        AddTab(DocSession.FromModel(Creation.NewQuest(dlg.Group, dlg.QuestName, dlg.TaskTitle, dlg.TaskDesc), null));
    }

    // ------------------------------------------------------------------
    // 保存
    // ------------------------------------------------------------------

    private Task<bool> SaveActive() => DoSave(Active, saveAs: false);

    private Task<bool> SaveActiveAs() => DoSave(Active, saveAs: true);

    private async Task<bool> DoSave(DocView? view, bool saveAs)
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
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = L.Save,
                SuggestedFileName = path != null ? System.IO.Path.GetFileName(path)
                    : doc.Cnv != null ? L.T("未命名对话.cnv") : L.T("未命名任务.qst"),
                DefaultExtension = doc.Cnv != null ? "cnv" : "qst",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(doc.Cnv != null ? L.T("Grim Dawn 对话") : L.T("Grim Dawn 任务"))
                        { Patterns = new[] { doc.Cnv != null ? "*.cnv" : "*.qst" } },
                },
            });
            if (file == null) return false;
            path = file.Path.LocalPath;
        }
        string? err = doc.Save(path, backup: true);
        if (err != null)
        {
            Gui.Warn(this, err, L.Save);
            return false;
        }
        view.AfterSaved();
        if (path != null)
        {
            AppSettings.AddRecent(path);
            RefreshRecentMenu();
        }
        Gui.Info(this, L.F("已保存 {0}（三检查通过，原文件已备份为 .bak）。", path), L.Save);
        return true;
    }

    // ------------------------------------------------------------------
    // 导入 / 导出
    // ------------------------------------------------------------------

    private async Task ImportTxtToActive()
    {
        var view = Active;
        if (view?.Doc.ReadOnly != false)
        {
            Gui.Warn(this, L.T("请先打开一个可编辑的 .cnv / .qst 页签。"), L.ImportTxtToActive);
            return;
        }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.ImportTxtToActive,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("TXT") { Patterns = new[] { "*.txt" } }, new FilePickerFileType(L.T("全部文件")) { Patterns = new[] { "*" } } },
        });
        if (files.Count == 0) return;
        var preview = new ImportTxtPreviewDialog(view.Doc, files[0].Path.LocalPath);
        if (await preview.ShowDialog<bool>(this) != true || !preview.Applied) return;
        view.RefreshAll();
        Gui.Info(this, L.T("回填完成（尚未保存，请检查后保存）。"), L.ImportTxtToActive);
    }

    private async Task ImportJson()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.ImportJsonNewTab,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } }, new FilePickerFileType(L.T("全部文件")) { Patterns = new[] { "*" } } },
        });
        if (files.Count == 0) return;
        try
        {
            byte[] data = JsonIO.ImportToBytes(await File.ReadAllTextAsync(files[0].Path.LocalPath));
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

    private async Task ExportActive(string kind)
    {
        var view = Active;
        if (view == null) { Gui.Warn(this, L.T("请先打开一个文件。"), L.MExport); return; }
        string ext = kind == "json" ? "json" : "txt";
        string name = Path.GetFileNameWithoutExtension(view.Doc.FilePath
            ?? (view.Doc.Cnv != null ? L.T("未命名对话") : L.T("未命名任务")));
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.MExport,
            SuggestedFileName = name + "." + ext,
            DefaultExtension = ext,
            FileTypeChoices = new[] { new FilePickerFileType(ext.ToUpperInvariant()) { Patterns = new[] { "*." + ext } } },
        });
        if (file == null) return;
        try
        {
            string content = kind switch
            {
                "txt" => view.Doc.Cnv != null ? TextExport.CnvTxt(view.Doc.Cnv) : TextExport.QstTxt(view.Doc.Qst!),
                "json" => view.Doc.JsonText(),
                _ => view.Doc.DumpText(),
            };
            await File.WriteAllTextAsync(file.Path.LocalPath, content, new System.Text.UTF8Encoding(false));
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
        new TextWindow("anchors — " + Path.GetFileName(view.Doc.FilePath), sw.ToString()).Show();
    }

    private async Task ShowSettings() => await new SettingsDialog().ShowDialog<bool>(this);

    private async Task ReloadActive()
    {
        if (_tabs.SelectedItem is not TabItem page || page.Content is not DocView view) return;
        string? path = view.Doc.FilePath;
        if (path == null) return;
        if (view.Doc.Dirty &&
            await MsgBox.Show(this, L.T("放弃未保存修改并重新加载？"), L.ReloadCurrent,
                MsgButtons.OkCancel, warning: true) != MsgResult.Ok)
            return;

        DocSession doc;
        try
        {
            doc = DocSession.Open(path);
        }
        catch (Exception e)
        {
            Gui.Warn(this, L.F("打开失败：{0}", e.Message), L.ReloadCurrent);
            return;
        }

        // 原地替换页签内容：不删除/重加页签，避免 TabControl 在移除选中项后残留不可点击状态。
        var newView = new DocView(doc);
        newView.DirtyChanged += () =>
        {
            SetTabHeader(page, newView);
            UpdateStatus();
        };
        page.Content = newView;
        SetTabHeader(page, newView);
        UpdateStatus();
    }

    private void ShowBatch(string? path)
    {
        var win = new BatchWindow();
        if (path != null) win.PresetDirectory(path);
        win.Show(this);
    }

    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose) return;
        var firstDirty = _tabs.Items.OfType<TabItem>()
            .Select(p => (TabItem: p, View: (DocView)p.Content!))
            .FirstOrDefault(x => x.View.Doc.Dirty);
        if (firstDirty.TabItem == null) return;
        e.Cancel = true;
        var r = await MsgBox.Show(this,
            L.F("{0} 有未保存修改，仍然退出？", firstDirty.View.Doc.Title), L.Exit,
            MsgButtons.YesNo, warning: true);
        if (r == MsgResult.Yes)
        {
            _forceClose = true;
            Close();
        }
    }
}