using System.Text;
using CnvQst;
using CnvQst.Cli;

namespace CnvQstStudio;

/// <summary>主窗口：文档页签 + 菜单/工具栏 + 拖拽。每个页签是一个 DocView。</summary>
internal sealed class MainForm : Form
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private MenuStrip? _menu;
    private ToolStrip? _toolbar;

    public MainForm()
    {
        Text = L.WindowTitle;
        Width = 1200;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        Controls.Add(_tabs);
        BuildMenu();   // menu/toolbar 后添加：停靠处理自后向前，Fill 页签最先处理

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
    }

    private DocView? Active => _tabs.SelectedTab?.Controls.OfType<DocView>().FirstOrDefault();

    // ------------------------------------------------------------------
    // 菜单与工具栏
    // ------------------------------------------------------------------

    private void BuildMenu()
    {
        // 语言切换：先移除旧菜单/工具栏后重建，即时生效
        if (_menu != null) Controls.Remove(_menu);
        if (_toolbar != null) Controls.Remove(_toolbar);

        var menu = new MenuStrip { Dock = DockStyle.Top };

        var file = new ToolStripMenuItem(L.MFile);
        file.DropDownItems.Add(new ToolStripMenuItem(L.OpenTitle, null, (_, _) => OpenViaDialog()) { ShortcutKeys = Keys.Control | Keys.O });
        file.DropDownItems.Add(new ToolStripMenuItem(L.OpenDirBatch, null, (_, _) => ShowBatch(null)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem(L.Save, null, (_, _) => SaveActive()) { ShortcutKeys = Keys.Control | Keys.S });
        file.DropDownItems.Add(new ToolStripMenuItem(L.SaveAs, null, (_, _) => SaveActiveAs()));
        file.DropDownItems.Add(new ToolStripMenuItem(L.CloseTab, null, (_, _) => CloseTab()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem(L.Exit, null, (_, _) => Close()));

        var newMenu = new ToolStripMenuItem(L.MNew);
        newMenu.DropDownItems.Add(new ToolStripMenuItem(L.NewConversationMenu, null, (_, _) => NewConversation()));
        newMenu.DropDownItems.Add(new ToolStripMenuItem(L.NewQuestMenu, null, (_, _) => NewQuest()));

        var lang = new ToolStripMenuItem(L.MLang);
        lang.DropDownItems.Add(new ToolStripMenuItem(L.MLangEn, null, (_, _) => { L.Toggle(); RebuildAll(); }));

        var import = new ToolStripMenuItem(L.MImport);
        import.DropDownItems.Add(new ToolStripMenuItem(L.ImportTxtToActive, null, (_, _) => ImportTxtToActive()));
        import.DropDownItems.Add(new ToolStripMenuItem(L.ImportJsonNewTab, null, (_, _) => ImportJson()));

        var export = new ToolStripMenuItem(L.MExport);
        export.DropDownItems.Add(new ToolStripMenuItem(L.ExportTxtOfficial, null, (_, _) => ExportActive("txt")));
        export.DropDownItems.Add(new ToolStripMenuItem(L.ExportJson, null, (_, _) => ExportActive("json")));
        export.DropDownItems.Add(new ToolStripMenuItem(L.ExportDump, null, (_, _) => ExportActive("dump")));

        var batch = new ToolStripMenuItem(L.MBatch);
        batch.DropDownItems.Add(new ToolStripMenuItem(L.BatchPanel, null, (_, _) => ShowBatch(null)));

        var tools = new ToolStripMenuItem(L.MTools);
        tools.DropDownItems.Add(new ToolStripMenuItem(L.VerifyCurrent, null, (_, _) => VerifyActive()));
        tools.DropDownItems.Add(new ToolStripMenuItem(L.Anchors, null, (_, _) => ShowAnchors()));
        tools.DropDownItems.Add(new ToolStripMenuItem(L.ReloadCurrent, null, (_, _) => ReloadActive()));

        var help = new ToolStripMenuItem(L.MHelp);
        help.DropDownItems.Add(new ToolStripMenuItem(L.LocStringTable, null, (_, _) => SelectLocTab()));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(new ToolStripMenuItem(L.About, null, (_, _) => MessageBox.Show(this, L.AboutBody, L.AboutTitle, MessageBoxButtons.OK, MessageBoxIcon.Information)));

        menu.Items.AddRange(new[] { file, newMenu, import, export, batch, tools, lang, help });
        MainMenuStrip = menu;
        Controls.Add(menu);
        menu.BringToFront();
        _menu = menu;

        var toolbar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        toolbar.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripButton(L.OpenBtn, null, (_, _) => OpenViaDialog()),
            new ToolStripButton(L.Save, null, (_, _) => SaveActive()),
            new ToolStripSeparator(),
            new ToolStripButton(L.NewConversationBtn, null, (_, _) => NewConversation()),
            new ToolStripButton(L.NewQuestBtn, null, (_, _) => NewQuest()),
            new ToolStripSeparator(),
            new ToolStripButton(L.ExportTxtBtn, null, (_, _) => ExportActive("txt")),
            new ToolStripButton(L.ExportJsonBtn, null, (_, _) => ExportActive("json")),
            new ToolStripButton(L.ImportTxtBtn, null, (_, _) => ImportTxtToActive()),
            new ToolStripSeparator(),
            new ToolStripButton(L.BatchBtn, null, (_, _) => ShowBatch(null)),
            new ToolStripButton(L.VerifyBtn, null, (_, _) => VerifyActive()),
        });
        Controls.Add(toolbar);
        _toolbar = toolbar;
    }

    /// <summary>语言切换后重建整个菜单/工具栏并刷新各页签。</summary>
    private void RebuildAll()
    {
        Text = L.WindowTitle;
        BuildMenu();
        foreach (TabPage page in _tabs.TabPages)
            (page.Controls.OfType<DocView>().FirstOrDefault())?.RefreshAll();
    }

    /// <summary>选中本地化字符串表页（帮助菜单入口）。</summary>
    private void SelectLocTab()
    {
        foreach (TabPage page in _tabs.TabPages)
        {
            var view = page.Controls.OfType<DocView>().FirstOrDefault();
            if (view != null)
            {
                _tabs.SelectedTab = page;
                view.ShowLoc();
                return;
            }
        }
    }

    // ------------------------------------------------------------------
    // 打开 / 关闭
    // ------------------------------------------------------------------

    private void OpenViaDialog()
    {
        using var dlg = new OpenFileDialog { Filter = "Grim Dawn 对话/任务|*.cnv;*.qst|全部文件|*.*" };
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
            var doc = DocSession.Open(path);
            AddTab(doc);
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "打开失败：" + e.Message, "打开");
        }
    }

    private void AddTab(DocSession doc)
    {
        var view = new DocView(doc) { Dock = DockStyle.Fill };
        view.DirtyChanged += () => { view.Text = doc.Title; };
        var page = new TabPage(doc.Title);
        page.Controls.Add(view);
        _tabs.TabPages.Add(page);
        _tabs.SelectedTab = page;
    }

    private void CloseTab()
    {
        if (_tabs.SelectedTab == null) return;
        var view = Active!;
        if (view.Doc.Dirty)
        {
            var r = MessageBox.Show(this, $"{view.Doc.Title} 有未保存修改，保存后再关闭？\n（是=保存并关闭，否=放弃修改关闭，取消=返回）",
                "关闭页签", MessageBoxButtons.YesNoCancel);
            if (r == DialogResult.Cancel) return;
            if (r == DialogResult.Yes && !DoSave(view, saveAs: false)) return;
        }
        _tabs.TabPages.Remove(_tabs.SelectedTab);
    }

    // ------------------------------------------------------------------
    // 新建
    // ------------------------------------------------------------------

    private void NewConversation()
    {
        using var dlg = new NewConversationDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var model = Creation.NewConversation(dlg.Speaker, dlg.FirstText, dlg.EndText);
        AddTab(DocSession.FromModel(model, null));
    }

    private void NewQuest()
    {
        using var dlg = new NewQuestDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var model = Creation.NewQuest(dlg.Group, dlg.QuestName, dlg.TaskTitle, dlg.TaskDesc);
        AddTab(DocSession.FromModel(model, null));
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
            MessageBox.Show(this, "该文件为只读（解析或完整性检查未通过），不能保存。", "保存");
            return false;
        }
        string? path = doc.FilePath;
        if (saveAs || path == null)
        {
            using var dlg = new SaveFileDialog
            {
                Filter = doc.Cnv != null ? "Grim Dawn 对话|*.cnv" : "Grim Dawn 任务|*.qst",
                FileName = path != null ? Path.GetFileName(path) : doc.Cnv != null ? "未命名对话.cnv" : "未命名任务.qst",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return false;
            path = dlg.FileName;
        }
        string? err = doc.Save(path, backup: true);
        if (err != null)
        {
            MessageBox.Show(this, err, "保存");
            return false;
        }
        view.AfterSaved();
        MessageBox.Show(this, $"已保存 {path}（三检查通过，原文件已备份为 .bak）。", "保存");
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
            MessageBox.Show(this, "请先打开一个可编辑的 .cnv/.qst 页签。", "导入 TXT");
            return;
        }
        using var dlg = new OpenFileDialog { Filter = "TXT|*.txt|全部文件|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        using var preview = new ImportTxtPreviewDialog(view.Doc, dlg.FileName);
        if (preview.ShowDialog(this) != DialogResult.OK || !preview.Applied) return;
        view.RefreshAll();
        MessageBox.Show(this, "回填完成（尚未保存，请检查后保存）。", "导入 TXT");
    }

    private void ImportJson()
    {
        using var dlg = new OpenFileDialog { Filter = "JSON|*.json|全部文件|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            byte[] data = JsonIO.ImportToBytes(File.ReadAllText(dlg.FileName));
            object model = data.Length > 4 && data[0] == 'C'
                ? CnvFile.Parse(data)
                : QstFile.Parse(data);
            bool valid = model switch
            {
                CnvFile c => c.Valid,
                QstFile q => q.Valid,
                _ => false,
            };
            if (!valid) { MessageBox.Show(this, "JSON 反序列化后无法解析，文件可能被改坏。", "导入 JSON"); return; }
            AddTab(DocSession.FromModel(model, null));
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "导入失败：" + e.Message, "导入 JSON");
        }
    }

    private void ExportActive(string kind)
    {
        var view = Active;
        if (view == null) { MessageBox.Show(this, "请先打开一个文件。", "导出"); return; }
        string filter = kind switch
        {
            "txt" => "TXT|*.txt",
            "json" => "JSON|*.json",
            _ => "TXT|*.txt",
        };
        string name = Path.GetFileNameWithoutExtension(view.Doc.FilePath ?? (view.Doc.Cnv != null ? "未命名对话" : "未命名任务"));
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
            MessageBox.Show(this, "导出失败：" + e.Message, "导出");
            return;
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
            err = !re.Valid ? re.Error : !re.Tiled ? "记录未铺满文件" : !re.LocOffsetMatches ? "未落在声明的本地化偏移" : null;
        }
        else if (view.Doc.Qst != null)
        {
            var re = QstFile.Parse(view.Doc.Serialize());
            err = !re.Valid ? re.Error : !re.Tiled ? "记录未铺满文件" : !re.LocOffsetMatches ? "未落在声明的本地化偏移" : null;
        }
        MessageBox.Show(this, err == null ? "✓ 通过：解析、铺满文件、本地化偏移全部正常。" : "✗ " + err, "校验当前文件");
    }

    private void ShowAnchors()
    {
        var view = Active;
        if (view?.Doc.FilePath == null)
        {
            MessageBox.Show(this, "anchors 需要已保存的文件（当前页签未落盘）。", "anchors");
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
            MessageBox.Show(this, "放弃未保存修改并重新加载？", "重新加载", MessageBoxButtons.OKCancel) != DialogResult.OK)
            return;
        int idx = _tabs.SelectedIndex;
        string path = view.Doc.FilePath;
        _tabs.TabPages.RemoveAt(idx);
        OpenFile(path);
    }

    private void ShowBatch(string? path)
    {
        var form = new BatchForm();
        form.Show(this);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        foreach (TabPage page in _tabs.TabPages)
        {
            var view = page.Controls.OfType<DocView>().FirstOrDefault();
            if (view?.Doc.Dirty == true)
            {
                var r = MessageBox.Show(this, $"{view.Doc.Title} 有未保存修改，仍然退出？", "退出", MessageBoxButtons.YesNo);
                if (r == DialogResult.No) { e.Cancel = true; return; }
                return;
            }
        }
    }
}

/// <summary>纯文本查看窗口（anchors 输出等）。</summary>
internal sealed class TextDialog : Form
{
    public TextDialog(string title, string text)
    {
        Text = title;
        Width = 900;
        Height = 600;
        StartPosition = FormStartPosition.CenterParent;
        var box = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9),
            Text = text,
        };
        Controls.Add(box);
    }
}

/// <summary>单个文档视图：平铺表格编辑器（CNV/QST）+ 本地化表切换 + 状态栏。</summary>
internal sealed class DocView : UserControl
{
    public DocSession Doc { get; }
    public event Action? DirtyChanged;

    private readonly CnvTableEditor _cnvTable = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly QstTableEditor _qstTable = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly LocalizationGrid _locGrid = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _emptyHint = new()
    {
        Text = L.EmptyHint,
        Dock = DockStyle.Fill,
        ForeColor = Color.Gray,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12),
    };
    private readonly Panel _host = new() { Dock = DockStyle.Fill };

    public DocView(DocSession doc)
    {
        Doc = doc;
        _host.Controls.AddRange(new Control[] { _cnvTable, _qstTable, _locGrid });
        _host.Controls.Add(_emptyHint);
        Controls.Add(_host);
        Controls.Add(_status);

        _cnvTable.LocRequested += ShowLoc;
        _qstTable.LocRequested += ShowLoc;
        ShowCurrent();
        UpdateStatus();
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

    /// <summary>切换到本地化字符串表页（工具菜单/表格工具栏入口）。</summary>
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

    private void UpdateStatus() =>
        _status.Text = (Doc.Dirty ? L.StatusSaving : "") + Doc.Summary + (Doc.ReadOnly ? L.StatusReadonly(Doc.LoadError ?? "完整性检查未过") : "");
}
