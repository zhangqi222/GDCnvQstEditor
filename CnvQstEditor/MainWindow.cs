using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
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
    private readonly TextBlock _statusVer = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.MutedBrush };
    private readonly Grid _root = new();
    private Menu? _menu;
    private MenuItem? _recentMenu;
    private Control? _toolbar;
    private readonly Dictionary<DocViewMode, MenuItem> _viewItems = new();
    private readonly Dictionary<DocViewMode, RadioButton> _viewButtons = new();
    /// <summary>Alt+字母 打开对应顶级菜单（Avalonia 的内嵌 Menu 不处理 Alt 访问键，需显式绑定）。</summary>
    private readonly Dictionary<char, MenuItem> _accessMenus = new();
    private bool _forceClose;

    public MainWindow()
    {
        Title = L.WindowTitle;
        Width = 1280;
        Height = 820;
        MinWidth = 960;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _tabs.SelectionChanged += (_, _) => { UpdateStatus(); UpdateViewChecks(); };

        var status = new Border
        {
            BorderBrush = UiTheme.MutedBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { _statusDoc, _statusVer },
            },
        };

        for (int i = 0; i < 4; i++) _root.RowDefinitions.Add(new RowDefinition { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Grid.SetRow(_tabs, 2);
        Grid.SetRow(status, 3);
        _root.Children.Add(_tabs);
        _root.Children.Add(status);

        Content = _root;
        BuildChrome();
        InstallShortcuts();
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
        _viewItems.Clear();
        _viewButtons.Clear();
        _accessMenus.Clear();

        var menu = new Menu();

        var file = MkMenu(L.MFile);
        _accessMenus['F'] = file;
        file.Items.Add(MkItem(L.OpenTitle, new KeyGesture(Key.O, KeyModifiers.Control), () => _ = OpenViaDialog()));
        file.Items.Add(MkItem(L.OpenDirBatch, new KeyGesture(Key.B, KeyModifiers.Control | KeyModifiers.Shift), () => ShowBatch(null)));
        _recentMenu = MkMenu(L.T("Recent files"));
        file.Items.Add(_recentMenu);
        file.Items.Add(new Separator());
        file.Items.Add(MkItem(L.Save, new KeyGesture(Key.S, KeyModifiers.Control), () => _ = SaveActive()));
        file.Items.Add(MkItem(L.SaveAs, new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift), () => _ = SaveActiveAs()));
        file.Items.Add(MkItem(L.CloseTab, new KeyGesture(Key.W, KeyModifiers.Control), () => _ = CloseTab(null)));
        file.Items.Add(new Separator());
        file.Items.Add(MkItem(L.Exit, null, Close));

        var newMenu = MkMenu(L.MNew);
        _accessMenus['N'] = newMenu;
        newMenu.Items.Add(MkItem(L.NewConversationMenu, new KeyGesture(Key.N, KeyModifiers.Control), () => _ = NewConversation()));
        newMenu.Items.Add(MkItem(L.NewQuestMenu, new KeyGesture(Key.N, KeyModifiers.Control | KeyModifiers.Shift), () => _ = NewQuest()));

        var import = MkMenu(L.MImport);
        _accessMenus['I'] = import;
        import.Items.Add(MkItem(L.ImportJsonNewTab, null, () => _ = ImportJson()));

        var export = MkMenu(L.MExport);
        _accessMenus['E'] = export;
        export.Items.Add(MkItem(L.ExportTxtOfficial, new KeyGesture(Key.E, KeyModifiers.Control), () => _ = ExportActive("txt")));
        export.Items.Add(MkItem(L.ExportJson, null, () => _ = ExportActive("json")));
        export.Items.Add(MkItem(L.ExportDump, null, () => _ = ExportActive("dump")));

        var batch = MkMenu(L.MBatch);
        _accessMenus['B'] = batch;
        batch.Items.Add(MkItem(L.BatchPanel, new KeyGesture(Key.B, KeyModifiers.Control), () => ShowBatch(null)));

        var viewMenu = MkMenu(L.MView);
        _accessMenus['V'] = viewMenu;
        foreach (var (mode, gesture) in new (DocViewMode, KeyGesture)[]
        {
            (DocViewMode.Visual, new KeyGesture(Key.D1, KeyModifiers.Control)),
            (DocViewMode.Localization, new KeyGesture(Key.D2, KeyModifiers.Control)),
            (DocViewMode.Json, new KeyGesture(Key.D3, KeyModifiers.Control)),
            (DocViewMode.Dump, new KeyGesture(Key.D4, KeyModifiers.Control)),
        })
        {
            var item = MkItem(ViewName(mode), gesture, () => SetActiveView(mode));
            item.ToggleType = MenuItemToggleType.CheckBox;
            viewMenu.Items.Add(item);
            _viewItems[mode] = item;
        }

        var tools = MkMenu(L.MTools);
        _accessMenus['T'] = tools;
        tools.Items.Add(MkItem(L.VerifyCurrent, new KeyGesture(Key.F5), VerifyActive));
        tools.Items.Add(MkItem(L.Anchors, null, ShowAnchors));
        tools.Items.Add(MkItem(L.ReloadCurrent, new KeyGesture(Key.R, KeyModifiers.Control), () => _ = ReloadActive()));
        tools.Items.Add(new Separator());
        tools.Items.Add(MkItem(L.T("Settings…"), null, () => _ = ShowSettings()));

        var lang = MkMenu(L.MenuLanguage);
        _accessMenus['L'] = lang;
        foreach (var info in L.Languages)
        {
            var code = info.Code;
            var item = MkItem(info.Name, null, () => L.Use(code));
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsChecked = code == L.Code;
            lang.Items.Add(item);
        }
        var help = MkMenu(L.MHelp);
        _accessMenus['H'] = help;
        help.Items.Add(MkItem(L.About, new KeyGesture(Key.F1), ShowAbout));

        foreach (var m in new[] { file, newMenu, import, export, batch, viewMenu, tools, lang, help }) menu.Items.Add(m);
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
            (L.T("Settings…"), () => _ = ShowSettings()),
        })
            toolbar.Children.Add(ToolBtn(label, action));

        toolbar.Children.Add(new Border
        {
            Width = 1,
            Margin = new Thickness(4, 3, 4, 3),
            Background = UiTheme.GridLineBrush,
        });
        var viewRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var mode in new[] { DocViewMode.Visual, DocViewMode.Localization, DocViewMode.Json, DocViewMode.Dump })
        {
            var b = new RadioButton
            {
                Content = ViewShortName(mode),
                GroupName = "DocViewMode",
                Theme = FindTheme("ButtonRadioButton"),
            };
            b.Classes.Add("Small");
            ToolTip.SetTip(b, ViewName(mode));
            b.Click += (_, _) => SetActiveView(mode);
            viewRow.Children.Add(b);
            _viewButtons[mode] = b;
        }
        var viewGroup = new Border
        {
            Theme = FindTheme("RadioButtonGroupBorder"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = viewRow,
        };
        toolbar.Children.Add(viewGroup);
        Grid.SetRow(toolbar, 1);
        _root.Children.Add(toolbar);
        _toolbar = toolbar;
        RefreshRecentMenu();
        UpdateViewChecks();
    }

    /// <summary>五种文档视图的显示名（菜单与工具条共用）。</summary>
    private static string ViewName(DocViewMode mode) => mode switch
    {
        DocViewMode.Visual => L.T("Visual editor"),
        DocViewMode.Json => L.T("JSON editor (read-only)"),
        DocViewMode.Localization => L.T("Localization text (read-only)"),
        DocViewMode.Dump => L.T("DUMP view (read-only)"),
        _ => mode.ToString(),
    };

    /// <summary>工具条按钮组用短名；完整名称（含只读）用于菜单与提示。</summary>
    private static string ViewShortName(DocViewMode mode) => mode switch
    {
        DocViewMode.Visual => L.T("Visual"),
        DocViewMode.Json => L.T("JSON"),
        DocViewMode.Localization => L.T("Localization"),
        DocViewMode.Dump => L.T("DUMP"),
        _ => mode.ToString(),
    };

    private static ControlTheme? FindTheme(string key) =>
        Application.Current?.TryFindResource(key, out object? value) == true ? value as ControlTheme : null;

    /// <summary>视图模式是全局设置：切换所有已打开文档，并写入配置供下次启动沿用。</summary>
    private void SetActiveView(DocViewMode mode)
    {
        foreach (TabItem page in _tabs.Items)
            ((DocView)page.Content!).SetView(mode);
        AppSettings.ViewMode = mode;
        AppSettings.Save();
        UpdateViewChecks();
    }

    /// <summary>当前页签的视图模式同步到菜单勾选与工具条按钮。</summary>
    private void UpdateViewChecks()
    {
        var mode = Active?.Mode;
        foreach (var (m, item) in _viewItems)
        {
            item.IsChecked = mode == m;
            item.IsEnabled = mode != null;
        }
        foreach (var (m, b) in _viewButtons)
        {
            b.IsChecked = mode == m;
            b.IsEnabled = mode != null;
        }
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
            var empty = MkItem(L.T("(no recent files)"), null, () => { });
            empty.IsEnabled = false;
            _recentMenu.Items.Add(empty);
        }
        else
        {
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                _recentMenu.Items.Add(MkItem($"{i + 1}. {path}", null, () => _ = OpenFile(path)));
            }
        }
        _recentMenu.Items.Add(new Separator());
        var clear = MkItem(L.T("Clear history"), null, () =>
        {
            AppSettings.ClearRecent();
            RefreshRecentMenu();
        });
        clear.IsEnabled = AppSettings.RecentFiles.Count > 0;
        _recentMenu.Items.Add(clear);
    }

    /// <summary>工具条按钮：Semi Light 主题 secondary 样式（浅色底、主题色文字，与菜单主色呼应）。
    /// Semi.Avalonia 的按钮变体经样式类切换：light = 主题 Light、secondary = 次要色。
    /// Semi 按钮默认 SemiFontWeightBold + Regular 字号，这里改为常规字重、与右侧视图按钮组同字号（12）。</summary>
    private static Button ToolBtn(string text, Action onClick)
    {
        var b = new Button { Content = text, FontSize = 12, FontWeight = FontWeight.Normal };
        b.Classes.Add("light");
        b.Classes.Add("secondary");
        b.Click += (_, _) => onClick();
        return b;
    }

    private static MenuItem MkMenu(string header) => new() { Header = AccessHeader(header) };

    /// <summary>Avalonia 的访问键用下划线（_F），而语言包沿用 Windows 习惯的 "(&amp;F)"：统一转换。</summary>
    private static string AccessHeader(string header) => header.Replace("(&", "(_").Replace("&", "_");

    private static MenuItem MkItem(string header, KeyGesture? hotkey, Action onClick) =>
        new() { Header = header, HotKey = hotkey, Command = new RelayCommand(onClick) };

    /// <summary>
    /// 菜单项的 HotKey 在 Avalonia 中只负责显示手势，不注册按键。这里在窗口级补齐真正的快捷键，
    /// 与菜单/工具条走同一批动作；菜单里仍显示手势方便发现。
    /// </summary>
    private void InstallShortcuts()
    {
        void Bind(KeyGesture gesture, Action action) =>
            KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = new RelayCommand(action) });

        Bind(new KeyGesture(Key.O, KeyModifiers.Control), () => _ = OpenViaDialog());
        Bind(new KeyGesture(Key.B, KeyModifiers.Control | KeyModifiers.Shift), () => ShowBatch(null));
        Bind(new KeyGesture(Key.S, KeyModifiers.Control), () => _ = SaveActive());
        Bind(new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift), () => _ = SaveActiveAs());
        Bind(new KeyGesture(Key.W, KeyModifiers.Control), () => _ = CloseTab(null));
        Bind(new KeyGesture(Key.N, KeyModifiers.Control), () => _ = NewConversation());
        Bind(new KeyGesture(Key.N, KeyModifiers.Control | KeyModifiers.Shift), () => _ = NewQuest());
        Bind(new KeyGesture(Key.E, KeyModifiers.Control), () => _ = ExportActive("txt"));
        Bind(new KeyGesture(Key.B, KeyModifiers.Control), () => ShowBatch(null));
        Bind(new KeyGesture(Key.F5), VerifyActive);
        Bind(new KeyGesture(Key.R, KeyModifiers.Control), () => _ = ReloadActive());
        Bind(new KeyGesture(Key.F1), ShowAbout);
        Bind(new KeyGesture(Key.L, KeyModifiers.Control), () =>
            SetActiveView(Active?.Mode == DocViewMode.Localization ? DocViewMode.Visual : DocViewMode.Localization));
        Bind(new KeyGesture(Key.D1, KeyModifiers.Control), () => SetActiveView(DocViewMode.Visual));
        Bind(new KeyGesture(Key.D2, KeyModifiers.Control), () => SetActiveView(DocViewMode.Localization));
        Bind(new KeyGesture(Key.D3, KeyModifiers.Control), () => SetActiveView(DocViewMode.Json));
        Bind(new KeyGesture(Key.D4, KeyModifiers.Control), () => SetActiveView(DocViewMode.Dump));

        // 菜单标签里的访问键（文件(F) 等）在 Avalonia 内嵌 Menu 中不生效，按 Alt+字母 直接展开菜单。
        Bind(new KeyGesture(Key.F, KeyModifiers.Alt), () => OpenAccessMenu('F'));
        Bind(new KeyGesture(Key.N, KeyModifiers.Alt), () => OpenAccessMenu('N'));
        Bind(new KeyGesture(Key.I, KeyModifiers.Alt), () => OpenAccessMenu('I'));
        Bind(new KeyGesture(Key.E, KeyModifiers.Alt), () => OpenAccessMenu('E'));
        Bind(new KeyGesture(Key.B, KeyModifiers.Alt), () => OpenAccessMenu('B'));
        Bind(new KeyGesture(Key.V, KeyModifiers.Alt), () => OpenAccessMenu('V'));
        Bind(new KeyGesture(Key.T, KeyModifiers.Alt), () => OpenAccessMenu('T'));
        Bind(new KeyGesture(Key.L, KeyModifiers.Alt), () => OpenAccessMenu('L'));
        Bind(new KeyGesture(Key.H, KeyModifiers.Alt), () => OpenAccessMenu('H'));
    }

    private void OpenAccessMenu(char key)
    {
        if (!_accessMenus.TryGetValue(key, out var item)) return;
        foreach (var other in _accessMenus.Values)
            if (!ReferenceEquals(other, item)) other.IsSubMenuOpen = false;
        item.IsSubMenuOpen = true;
        item.Focus();
    }

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

    private void ShowAbout() =>
        Gui.Info(this, L.AboutBody + "\n\n" + L.F("Version: {0}", AppInfo.Version) + "\n\n" +
            L.F("Author: {0}", "laozhangggg(老张allif)") + "\n\n" + L.AboutLicense, L.AboutTitle);

    private void UpdateStatus()
    {
        var v = Active;
        // 文档状态统一在这一条最外层状态栏展示：摘要 + 未保存前缀 + 只读说明（原页内状态行已并入）
        string text = v?.Doc.Summary ?? L.StatusReady;
        if (v != null && v.Doc.Dirty) text = L.StatusSaving + "   " + text;
        if (v != null && v.Doc.ReadOnly) text += "   .   " + L.StatusReadonly(v.Doc.LoadError ?? L.T("Integrity check failed"));
        _statusDoc.Text = text;
        _statusDoc.Foreground = v?.Doc.ReadOnly == true ? UiTheme.WarnBrush : UiTheme.TextBrush;
        _statusVer.Text = "GDCnvQstEditor " + AppInfo.Version;
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
                new FilePickerFileType(L.T("Grim Dawn conversation / quest")) { Patterns = new[] { "*.cnv", "*.qst" } },
                new FilePickerFileType(L.T("All files")) { Patterns = new[] { "*" } },
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
            Gui.Warn(this, L.F("File not found: {0}", path), L.T("Open"));
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
            Gui.Warn(this, L.F("Open failed: {0}", e.Message), L.T("Open"));
        }
        await Task.CompletedTask;
    }

    /// <summary>冒烟测试专用：内存新建文档页签（不落盘、不弹框）。</summary>
    public void OpenSmokeTab(object model) => AddTab(DocSession.FromModel(model, null));

    /// <summary>冒烟测试专用：依次切换四种视图并检查 JSON 模式确实生成含 languages 的模型。</summary>
    public string? SmokeTestViews()
    {
        var view = Active;
        if (view == null) return "视图自测失败：没有活动页签";
        foreach (var mode in Enum.GetValues<DocViewMode>())
        {
            view.SetView(mode);
            if (view.Mode != mode) return $"视图自测失败：切换到 {mode} 后 Mode={view.Mode}";
        }
        view.SetView(DocViewMode.Json);
        string json = view.Doc.Cnv != null ? JsonIO.Export(view.Doc.Cnv, view.Doc.Title) : JsonIO.Export(view.Doc.Qst!, view.Doc.Title);
        if (!json.Contains("\"Languages\"")) return "视图自测失败：JSON 模式未包含 Languages";
        view.SetView(DocViewMode.Visual);
        return null;
    }

    private void AddTab(DocSession doc)
    {
        var view = new DocView(doc);
        view.SetView(AppSettings.ViewMode);
        view.DirtyChanged += () =>
        {
            if (_tabs.SelectedItem is TabItem sel) SetTabHeader(sel, view);
            UpdateStatus();
        };
        view.ModeChanged += UpdateViewChecks;
        view.LocalizationRequested += () =>
            SetActiveView(Active?.Mode == DocViewMode.Localization ? DocViewMode.Visual : DocViewMode.Localization);
        var page = new TabItem { Content = view };
        SetTabHeader(page, view);
        _tabs.Items.Add(page);
        _tabs.SelectedItem = page;
        UpdateViewChecks();
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
                L.F("{0} has unsaved changes. Save before closing?", view.Doc.Title),
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
            Gui.Warn(this, L.T("This file is read-only (parsing or integrity check failed) and cannot be saved."), L.Save);
            return false;
        }
        string? path = doc.FilePath;
        if (saveAs || path == null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = L.Save,
                SuggestedFileName = path != null ? System.IO.Path.GetFileName(path)
                    : doc.Cnv != null ? L.T("Untitled conversation.cnv") : L.T("Untitled quest.qst"),
                DefaultExtension = doc.Cnv != null ? "cnv" : "qst",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(doc.Cnv != null ? L.T("Grim Dawn conversation") : L.T("Grim Dawn quest"))
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
        Gui.Info(this, L.F("Saved {0} (three-check passed; original backed up as .bak).", path), L.Save);
        return true;
    }

    // ------------------------------------------------------------------
    // 导入 / 导出
    // ------------------------------------------------------------------

    private async Task ImportJson()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.ImportJsonNewTab,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } }, new FilePickerFileType(L.T("All files")) { Patterns = new[] { "*" } } },
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
            if (!valid) { Gui.Warn(this, L.T("The imported JSON cannot be parsed; the file may be corrupted."), L.ImportJsonNewTab); return; }
            AddTab(DocSession.FromModel(model, null));
        }
        catch (Exception e)
        {
            Gui.Warn(this, L.F("Import failed: {0}", e.Message), L.ImportJsonNewTab);
        }
    }

    private async Task ExportActive(string kind)
    {
        var view = Active;
        if (view == null) { Gui.Warn(this, L.T("Open a file first."), L.MExport); return; }
        string ext = kind == "json" ? "json" : "txt";
        string name = Path.GetFileNameWithoutExtension(view.Doc.FilePath
            ?? (view.Doc.Cnv != null ? L.T("Untitled conversation") : L.T("Untitled quest")));
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
            Gui.Warn(this, L.F("Export failed: {0}", e.Message), L.MExport);
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
            err = !re.Valid ? re.Error : !re.Tiled ? L.T("Records do not tile the file") : !re.LocOffsetMatches ? L.T("Miss the declared localization offset") : null;
        }
        else if (view.Doc.Qst != null)
        {
            var re = QstFile.Parse(view.Doc.Serialize());
            err = !re.Valid ? re.Error : !re.Tiled ? L.T("Records do not tile the file")
                : !re.LocOffsetMatches ? L.T("Miss the declared localization offset")
                : re.TextCountDrift != 0 ? L.F("String count deviates from the formula by {0}", re.TextCountDrift)
                : null;
        }
        if (err == null)
            Gui.Info(this, L.T("✓ Passed: parsing, tiling, localization offset (QST also checks the string-count formula)."), L.VerifyCurrent);
        else
            Gui.Warn(this, "✗ " + err, L.VerifyCurrent);
    }

    private void ShowAnchors()
    {
        var view = Active;
        if (view?.Doc.FilePath == null)
        {
            Gui.Warn(this, L.T("anchors needs a saved file (this tab is not on disk yet)."), L.Anchors);
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
            await MsgBox.Show(this, L.T("Discard unsaved changes and reload?"), L.ReloadCurrent,
                MsgButtons.OkCancel, warning: true) != MsgResult.Ok)
            return;

        DocSession doc;
        try
        {
            doc = DocSession.Open(path);
        }
        catch (Exception e)
        {
            Gui.Warn(this, L.F("Open failed: {0}", e.Message), L.ReloadCurrent);
            return;
        }

        // 原地替换页签内容：不删除/重加页签，避免 TabControl 在移除选中项后残留不可点击状态。
        var newView = new DocView(doc);
        newView.DirtyChanged += () =>
        {
            SetTabHeader(page, newView);
            UpdateStatus();
        };
        newView.ModeChanged += UpdateViewChecks;
        page.Content = newView;
        SetTabHeader(page, newView);
        UpdateStatus();
        UpdateViewChecks();
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
            L.F("{0} has unsaved changes. Exit anyway?", firstDirty.View.Doc.Title), L.Exit,
            MsgButtons.YesNo, warning: true);
        if (r == MsgResult.Yes)
        {
            _forceClose = true;
            Close();
        }
    }
}