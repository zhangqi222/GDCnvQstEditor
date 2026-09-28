using System.Text;
using CnvQst;
using CnvQst.Cli;

namespace CnvQstStudio;

/// <summary>
/// 主窗口。现代扁平布局：
///   菜单栏 → 主工具栏 → 文档页签（每个页签一个 DocView）→ 状态栏
/// 所有可见文案走 <see cref="L"/>（中文为基准、语言包可扩展），外观走 <see cref="Theme"/>。
/// </summary>
internal sealed class MainForm : Form
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly StatusStrip _status = new() { Dock = DockStyle.Bottom };
    private readonly ToolStripStatusLabel _statusDoc = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _statusLang = new();
    private readonly ToolStripStatusLabel _statusVer = new();
    private MenuStrip? _menu;
    private ToolStrip? _toolbar;

    public MainForm()
    {
        Theme.Apply(this);
        Text = L.WindowTitle;
        Width = 1280;
        Height = 820;
        MinimumSize = new Size(960, 600);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        BackColor = Theme.Bg;

        BuildStatus();
        Theme.StyleTabs(_tabs);
        Controls.Add(_tabs);
        Controls.Add(_status);
        BuildChrome();                 // 菜单/工具栏最后添加（Dock 自后向前处理，Fill 页签最先）

        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;
            foreach (string p in paths)
            {
                if (Directory.Exists(p)) ShowBatch(p);
                else if (File.Exists(p)) OpenFile(p);
            }
        };

        L.Changed += OnLanguageChanged;
        _tabs.SelectedIndexChanged += (_, _) => UpdateStatus();
        UpdateStatus();
    }

    private DocView? Active => _tabs.SelectedTab?.Controls.OfType<DocView>().FirstOrDefault();

    private void BuildStatus()
    {
        Theme.StyleStatus(_status);
        _statusDoc.ForeColor = Theme.Text;
        _status.Items.Add(_statusDoc);
        _status.Items.Add(new ToolStripStatusLabel { Text = "·", ForeColor = Theme.Border });
        _status.Items.Add(_statusLang);
        _status.Items.Add(_statusVer);
    }

    private void UpdateStatus()
    {
        _statusDoc.Text = Active?.Doc.Summary ?? L.StatusReady;
        _statusDoc.ForeColor = Active?.Doc.ReadOnly == true ? Theme.Warn : Theme.Text;
        _statusLang.Text = L.DisplayName;
        _statusVer.Text = "CnvQstStudio 1.0";
    }

    // ------------------------------------------------------------------
    // 菜单与工具栏
    // ------------------------------------------------------------------

    private void BuildChrome()
    {
        if (_menu != null) Controls.Remove(_menu);
        if (_toolbar != null) Controls.Remove(_toolbar);

        var menu = new MenuStrip { Dock = DockStyle.Top };
        Theme.StyleMenu(menu);

        var file = new ToolStripMenuItem(L.MFile);
        file.DropDownItems.Add(Item(L.OpenTitle, Keys.Control | Keys.O, (_, _) => OpenViaDialog()));
        file.DropDownItems.Add(Item(L.OpenDirBatch, Keys.None, (_, _) => ShowBatch(null)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item(L.Save, Keys.Control | Keys.S, (_, _) => SaveActive()));
        file.DropDownItems.Add(Item(L.SaveAs, Keys.Control | Keys.Shift | Keys.S, (_, _) => SaveActiveAs()));
        file.DropDownItems.Add(Item(L.CloseTab, Keys.Control | Keys.W, (_, _) => CloseTab()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item(L.Exit, Keys.Alt | Keys.F4, (_, _) => Close()));

        var newMenu = new ToolStripMenuItem(L.MNew);
        newMenu.DropDownItems.Add(Item(L.NewConversationMenu, Keys.Control | Keys.N, (_, _) => NewConversation()));
        newMenu.DropDownItems.Add(Item(L.NewQuestMenu, Keys.Control | Keys.Shift | Keys.N, (_, _) => NewQuest()));

        var import = new ToolStripMenuItem(L.MImport);
        import.DropDownItems.Add(Item(L.ImportTxtToActive, Keys.None, (_, _) => ImportTxtToActive()));
        import.DropDownItems.Add(Item(L.ImportJsonNewTab, Keys.None, (_, _) => ImportJson()));

        var export = new ToolStripMenuItem(L.MExport);
        export.DropDownItems.Add(Item(L.ExportTxtOfficial, Keys.Control | Keys.E, (_, _) => ExportActive("txt")));
        export.DropDownItems.Add(Item(L.ExportJson, Keys.None, (_, _) => ExportActive("json")));
        export.DropDownItems.Add(Item(L.ExportDump, Keys.None, (_, _) => ExportActive("dump")));

        var batch = new ToolStripMenuItem(L.MBatch);
        batch.DropDownItems.Add(Item(L.BatchPanel, Keys.Control | Keys.B, (_, _) => ShowBatch(null)));

        var tools = new ToolStripMenuItem(L.MTools);
        tools.DropDownItems.Add(Item(L.VerifyCurrent, Keys.F5, (_, _) => VerifyActive()));
        tools.DropDownItems.Add(Item(L.Anchors, Keys.None, (_, _) => ShowAnchors()));
        tools.DropDownItems.Add(Item(L.ReloadCurrent, Keys.Control | Keys.R, (_, _) => ReloadActive()));

        var view = new ToolStripMenuItem(L.MView);
        view.DropDownItems.Add(Item(L.LocStringTable, Keys.Control | Keys.L, (_, _) => SelectLocTab()));

        var lang = new ToolStripMenuItem(L.MenuLanguage);
        foreach (var info in L.Languages)
        {
            var code = info.Code;
            lang.DropDownItems.Add(new ToolStripMenuItem(info.Name, null, (_, _) => L.Use(code))
            {
                Checked = code == L.Code,
                CheckOnClick = false,
            });
        }
        lang.DropDownItems.Add(new ToolStripSeparator());
        lang.DropDownItems.Add(Item(L.OpenLangFolder, Keys.None, (_, _) => OpenLangFolder()));

        var help = new ToolStripMenuItem(L.MHelp);
        help.DropDownItems.Add(Item(L.About, Keys.F1, (_, _) => ShowAbout()));

        menu.Items.AddRange(new ToolStripItem[] { file, newMenu, import, export, batch, tools, view, lang, help });
        MainMenuStrip = menu;
        Controls.Add(menu);
        menu.BringToFront();
        _menu = menu;

        var toolbar = new ToolStrip { Dock = DockStyle.Top };
        toolbar.Items.AddRange(new ToolStripItem[]
        {
            TextBtn(L.OpenBtn, (_, _) => OpenViaDialog()),
            TextBtn(L.SaveBtn, (_, _) => SaveActive()),
            new ToolStripSeparator(),
            TextBtn(L.NewConversationBtn, (_, _) => NewConversation()),
            TextBtn(L.NewQuestBtn, (_, _) => NewQuest()),
            new ToolStripSeparator(),
            TextBtn(L.ExportTxtBtn, (_, _) => ExportActive("txt")),
            TextBtn(L.ExportJsonBtn, (_, _) => ExportActive("json")),
            TextBtn(L.DumpBtn, (_, _) => ExportActive("dump")),
            TextBtn(L.ImportTxtBtn, (_, _) => ImportTxtToActive()),
            new ToolStripSeparator(),
            TextBtn(L.BatchBtn, (_, _) => ShowBatch(null)),
            TextBtn(L.VerifyBtn, (_, _) => VerifyActive()),
        });
        Theme.StyleToolbar(toolbar, primary: true);
        Controls.Add(toolbar);
        _toolbar = toolbar;
    }

    private static ToolStripMenuItem Item(string text, Keys shortcut, EventHandler onClick) =>
        new(text, null, onClick) { ShortcutKeys = shortcut, ShowShortcutKeys = shortcut != Keys.None };

    private static ToolStripButton TextBtn(string text, EventHandler onClick) => new(text, null, onClick);

    /// <summary>语言切换：重建菜单/工具栏 + 各页签标题与内容。</summary>
    private void OnLanguageChanged()
    {
        Text = L.WindowTitle;
        foreach (TabPage page in _tabs.TabPages)
        {
            var view = page.Controls.OfType<DocView>().FirstOrDefault();
            if (view == null) continue;
            page.Text = view.Doc.Title;
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
        catch (Exception e) { Warn(L.F("无法打开语言包目录：{0}", e.Message), L.Warning); }
    }

    private void ShowAbout() => MessageBox.Show(this, L.AboutBody, L.AboutTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void Warn(string text, string title) => MessageBox.Show(this, text, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    // ------------------------------------------------------------------
    // 打开 / 关闭
    // ------------------------------------------------------------------

    private void OpenViaDialog()
    {
        using var dlg = new OpenFileDialog { Filter = L.T("Grim Dawn 对话 / 任务|*.cnv;*.qst|全部文件|*.*") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        OpenFile(dlg.FileName);
    }

    private void OpenFile(string path)
    {
        foreach (TabPage page in _tabs.TabPages)
        {
            var existing = page.Controls.OfType<DocView>().FirstOrDefault();
            if (existing?.Doc.FilePath != null &&
                string.Equals(Path.GetFullPath(existing.Doc.FilePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            {
                _tabs.SelectedTab = page;
                return;
            }
        }
        try
        {
            AddTab(DocSession.Open(path));
        }
        catch (Exception e)
        {
            Warn(L.F("打开失败：{0}", e.Message), L.T("打开"));
        }
    }

    private void AddTab(DocSession doc)
    {
        var view = new DocView(doc) { Dock = DockStyle.Fill };
        view.DirtyChanged += () =>
        {
            if (_tabs.SelectedTab != null) _tabs.SelectedTab.Text = doc.Title;
            UpdateStatus();
        };
        var page = new TabPage(doc.Title) { BackColor = Theme.Surface };
        page.Controls.Add(view);
        _tabs.TabPages.Add(page);
        _tabs.SelectedTab = page;
        UpdateStatus();
    }

    private void CloseTab()
    {
        if (_tabs.SelectedTab == null) return;
        var view = Active!;
        if (view.Doc.Dirty)
        {
            var r = MessageBox.Show(this,
                L.F("{0} 有未保存修改，保存后再关闭？", view.Doc.Title),
                L.CloseTab, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return;
            if (r == DialogResult.Yes && !DoSave(view, saveAs: false)) return;
        }
        _tabs.TabPages.Remove(_tabs.SelectedTab);
        UpdateStatus();
    }

    // ------------------------------------------------------------------
    // 新建
    // ------------------------------------------------------------------

    private void NewConversation()
    {
        using var dlg = new NewConversationDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        AddTab(DocSession.FromModel(Creation.NewConversation(dlg.Speaker, dlg.FirstText, dlg.EndText), null));
    }

    private void NewQuest()
    {
        using var dlg = new NewQuestDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
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
            Warn(L.T("该文件为只读（解析或完整性检查未通过），不能保存。"), L.Save);
            return false;
        }
        string? path = doc.FilePath;
        if (saveAs || path == null)
        {
            using var dlg = new SaveFileDialog
            {
                Filter = doc.Cnv != null ? L.T("Grim Dawn 对话|*.cnv") : L.T("Grim Dawn 任务|*.qst"),
                FileName = path != null ? Path.GetFileName(path)
                    : doc.Cnv != null ? L.T("未命名对话.cnv") : L.T("未命名任务.qst"),
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return false;
            path = dlg.FileName;
        }
        string? err = doc.Save(path, backup: true);
        if (err != null)
        {
            MessageBox.Show(this, err, L.Save, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        view.AfterSaved();
        MessageBox.Show(this, L.F("已保存 {0}（三检查通过，原文件已备份为 .bak）。", path), L.Save,
            MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            Warn(L.T("请先打开一个可编辑的 .cnv / .qst 页签。"), L.ImportTxtToActive);
            return;
        }
        using var dlg = new OpenFileDialog { Filter = L.T("TXT|*.txt|全部文件|*.*") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        using var preview = new ImportTxtPreviewDialog(view.Doc, dlg.FileName);
        if (preview.ShowDialog(this) != DialogResult.OK || !preview.Applied) return;
        view.RefreshAll();
        MessageBox.Show(this, L.T("回填完成（尚未保存，请检查后保存）。"), L.ImportTxtToActive,
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ImportJson()
    {
        using var dlg = new OpenFileDialog { Filter = L.T("JSON|*.json|全部文件|*.*") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
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
            if (!valid) { Warn(L.T("JSON 反序列化后无法解析，文件可能被改坏。"), L.ImportJsonNewTab); return; }
            AddTab(DocSession.FromModel(model, null));
        }
        catch (Exception e)
        {
            Warn(L.F("导入失败：{0}", e.Message), L.ImportJsonNewTab);
        }
    }

    private void ExportActive(string kind)
    {
        var view = Active;
        if (view == null) { Warn(L.T("请先打开一个文件。"), L.MExport); return; }
        string filter = kind switch
        {
            "txt" => L.T("TXT|*.txt"),
            "json" => L.T("JSON|*.json"),
            _ => L.T("TXT|*.txt"),
        };
        string name = Path.GetFileNameWithoutExtension(view.Doc.FilePath
            ?? (view.Doc.Cnv != null ? L.T("未命名对话") : L.T("未命名任务")));
        using var dlg = new SaveFileDialog { Filter = filter, FileName = name + (kind == "json" ? ".json" : ".txt") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string content = kind switch
            {
                "txt" => view.Doc.Cnv != null ? TextExport.CnvTxt(view.Doc.Cnv) : TextExport.QstTxt(view.Doc.Qst!),
                "json" => view.Doc.JsonText(),
                _ => view.Doc.DumpText(),
            };
            File.WriteAllText(dlg.FileName, content, new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Warn(L.F("导出失败：{0}", e.Message), L.MExport);
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
        MessageBox.Show(this,
            err == null ? L.T("✓ 通过：解析、铺满文件、本地化偏移（QST 另含串表公式）全部正常。") : "✗ " + err,
            L.VerifyCurrent, MessageBoxButtons.OK, err == null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void ShowAnchors()
    {
        var view = Active;
        if (view?.Doc.FilePath == null)
        {
            Warn(L.T("anchors 需要已保存的文件（当前页签未落盘）。"), L.Anchors);
            return;
        }
        var sw = new StringWriter();
        CliApp.Run(["anchors", view.Doc.FilePath], sw, sw);
        new TextDialog("anchors — " + Path.GetFileName(view.Doc.FilePath), sw.ToString()).Show(this);
    }

    private void ReloadActive()
    {
        var view = Active;
        if (view?.Doc.FilePath == null) return;
        if (view.Doc.Dirty &&
            MessageBox.Show(this, L.T("放弃未保存修改并重新加载？"), L.ReloadCurrent,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        int idx = _tabs.SelectedIndex;
        string path = view.Doc.FilePath;
        _tabs.TabPages.RemoveAt(idx);
        OpenFile(path);
    }

    private void ShowBatch(string? path)
    {
        var form = new BatchForm();
        if (path != null) form.PresetDirectory(path);
        form.Show(this);
    }

    /// <summary>选中本地化字符串表页（视图菜单入口）。</summary>
    private void SelectLocTab()
    {
        foreach (TabPage page in _tabs.TabPages)
        {
            var view = page.Controls.OfType<DocView>().FirstOrDefault();
            if (view == null) continue;
            _tabs.SelectedTab = page;
            view.ShowLoc();
            return;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        foreach (TabPage page in _tabs.TabPages)
        {
            var view = page.Controls.OfType<DocView>().FirstOrDefault();
            if (view?.Doc.Dirty != true) continue;
            var r = MessageBox.Show(this, L.F("{0} 有未保存修改，仍然退出？", view.Doc.Title), L.Exit,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r == DialogResult.No) { e.Cancel = true; return; }
            return;
        }
    }
}

/// <summary>纯文本查看窗口（anchors 输出等）。</summary>
internal sealed class TextDialog : Form
{
    public TextDialog(string title, string text)
    {
        Theme.Apply(this);
        Text = title;
        Width = 940;
        Height = 640;
        StartPosition = FormStartPosition.CenterParent;
        var box = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Surface,
            Font = Theme.MonoFont,
            Text = text,
        };
        var copy = new Button { Text = L.Copy, Width = 100, Dock = DockStyle.Bottom, Height = 34 };
        Theme.MakeGhost(copy);
        copy.Click += (_, _) => { try { Clipboard.SetText(box.Text); } catch { } };
        Controls.Add(Theme.Padded(box, 10));
        Controls.Add(copy);
    }
}

/// <summary>单个文档视图：平铺表格编辑器（CNV/QST）+ 本地化表切换 + 页内状态行。</summary>
internal sealed class DocView : UserControl
{
    public DocSession Doc { get; }
    public event Action? DirtyChanged;

    private readonly CnvTableEditor _cnvTable = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly QstTableEditor _qstTable = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly LocalizationGrid _locGrid = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Bottom,
        Height = 26,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 0, 12, 0),
        ForeColor = Theme.Muted,
        BackColor = Theme.Surface,
    };
    private readonly Label _emptyHint = new()
    {
        Text = L.EmptyHint,
        Dock = DockStyle.Fill,
        ForeColor = Theme.Muted,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(28),
        BackColor = Theme.Surface,
    };
    private readonly Panel _host = new() { Dock = DockStyle.Fill, BackColor = Theme.Surface };
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
    }

    private void ShowCurrent()
    {
        if (Doc.Cnv != null)
        {
            _cnvTable.Visible = true;
            _cnvTable.BringToFront();
            _cnvTable.Bind(Doc, OnEdited);
        }
        else if (Doc.Qst != null)
        {
            _qstTable.Visible = true;
            _qstTable.BringToFront();
            _qstTable.Bind(Doc, OnEdited);
        }
        else
        {
            _emptyHint.Visible = true;
            _emptyHint.BringToFront();
        }
        _locGrid.Visible = false;
    }

    /// <summary>切换到本地化字符串表页。</summary>
    public void ShowLoc()
    {
        _locGrid.Visible = true;
        _locGrid.BringToFront();
        _locGrid.Bind(Doc, OnEdited);
        _cnvTable.Visible = false;
        _qstTable.Visible = false;
    }

    private void OnEdited()
    {
        Doc.Dirty = true;
        DirtyChanged?.Invoke();
        UpdateStatus();
    }

    public void RefreshAll()
    {
        BuildTree();
        if (_cnvTable.Visible) _cnvTable.Bind(Doc, OnEdited);
        else if (_qstTable.Visible) _qstTable.Bind(Doc, OnEdited);
        else if (_locGrid.Visible) _locGrid.Bind(Doc, OnEdited);
        UpdateStatus();
    }

    public void AfterSaved()
    {
        Doc.Dirty = false;
        DirtyChanged?.Invoke();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        string prefix = Doc.Dirty ? L.StatusSaving + "   " : "";
        _status.Text = prefix + Doc.Summary + (Doc.ReadOnly ? "   ·   " + L.StatusReadonly(Doc.LoadError ?? L.T("完整性检查未过")) : "");
        _status.ForeColor = Doc.ReadOnly ? Theme.Warn : Doc.Dirty ? Theme.Accent : Theme.Muted;
    }
}
