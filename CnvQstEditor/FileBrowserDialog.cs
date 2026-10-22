using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GdAssets;

namespace CnvQstEditor;

/// <summary>
/// 内置文件浏览器（跨层合并视图）：按 GD 覆盖顺序（游戏根 → DLC1/2/3 → MOD）
/// 合并各层 database / resources 下的文件，展示为单棵树；同名文件只出现一次，
/// 文件名带来源标签（如 aaa.dbr[DLC3]、bbb.tex[MOD]），ToolTip 显示实际落盘路径。
/// 文件索引由独立库 GdAssets 提供（分层枚举 + 合并 + 按层缓存），UI 只负责展示。
/// </summary>
internal sealed class FileBrowserDialog : Window
{
    private sealed record Entry(string RelPath, bool IsDir, GdFileEntry? File);

    private readonly string _subDir;
    private readonly Func<string, bool> _fileFilter;
    private readonly TreeView _tree = new() { MinHeight = 320 };
    private readonly TextBlock _countText = new() { Foreground = UiTheme.MutedBrush };
    private readonly TextBox _picked = new() { IsReadOnly = true, Watermark = L.T("（未选择文件）", "(No file selected)") };
    private readonly Button _ok;
    private readonly Button _refresh;
    private readonly Dictionary<string, TreeViewItem> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private GdIndex? _index;
    private CancellationTokenSource? _buildCts;
    private string? _result;
    private string? _resultRel;

    /// <summary>确定后选中的文件绝对路径（未选择为 null）。</summary>
    public string? SelectedFilePath => _result;

    /// <summary>确定后选中的文件相对路径（相对 database / resources，反斜杠）。</summary>
    public string? SelectedRelPath => _resultRel;

    private FileBrowserDialog(string subDir, string title, string[]? exts, string? initialRel)
    {
        _subDir = subDir;
        _fileFilter = CreateFilter(subDir, exts);
        DialogChrome.Init(this, title, 720, 660);

        _tree.SelectionChanged += (_, _) => UpdatePicked();
        _tree.DoubleTapped += (_, e) =>
        {
            if (SelectedFile() == null) return;
            e.Handled = true;
            Accept();
        };
        _tree.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || SelectedFile() == null) return;
            e.Handled = true;
            Accept();
        };

        _ok = DialogChrome.Button(L.Confirm, primary: true);
        _ok.IsDefault = true;
        _ok.IsEnabled = false;
        _ok.Click += (_, _) => Accept();
        var cancel = DialogChrome.Cancel(this);

        _refresh = DialogChrome.Button(L.T("重新扫描", "Rescan"), primary: false);
        _refresh.Click += (_, _) => _ = BuildIndexAsync(force: true);

        // 顶部说明：本层的目录规则 + 层状态
        var rules = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 8) };
        var ruleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var rulesText = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        rulesText.Children.Add(new TextBlock
        {
            Text = subDir == "database"
                ? L.T("浏览范围：各层 database\\ 下的记录文件（.dbr）", "Scope: record files (.dbr) under each layer's database\\")
                : L.T("浏览范围：各层 resources\\ 下的资源（qst,cnv,wav,ogg,tex,fx,anm,lua 等）",
                    "Scope: resources under each layer's resources\\ (qst,cnv,wav,ogg,tex,fx,anm,lua, etc.)"),
            Foreground = UiTheme.AccentBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        rulesText.Children.Add(new TextBlock
        {
            Text = L.T("加载覆盖顺序：游戏根 → DLC1 → DLC2 → DLC3 → MOD；同名文件以高层为准。",
                "Override order: game root → DLC1 → DLC2 → DLC3 → MOD; higher layers win for same-name files."),
            Foreground = UiTheme.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        rulesText.Children.Add(_countText);
        ruleRow.Children.Add(rulesText);
        var refreshHost = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        refreshHost.Children.Add(_refresh);
        ruleRow.Children.Add(refreshHost);
        rules.Children.Add(ruleRow);
        rules.Children.Add(DialogChrome.Caption(L.T("双击文件或选中后点击确定。", "Double-click a file, or select it and click OK.")));

        var pickedRow = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        var pickedLabel = new TextBlock { Text = L.T("路径", "Path") + "：", VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(pickedLabel, Dock.Left);
        pickedRow.Children.Add(pickedLabel);
        pickedRow.Children.Add(_picked);

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(rules, 0);
        Grid.SetRow(_tree, 1);
        Grid.SetRow(pickedRow, 2);
        var footer = DialogChrome.Footer(cancel, _ok);
        Grid.SetRow(footer, 3);
        grid.Children.Add(rules);
        grid.Children.Add(_tree);
        grid.Children.Add(pickedRow);
        grid.Children.Add(footer);
        Content = grid;

        ShowPlaceholder(L.T("正在扫描各层文件…", "Scanning layers…"));
        _ = BuildIndexAsync(force: false, initialRel);
        Closed += (_, _) => _buildCts?.Cancel();
    }

    // ------------------------------------------------------------------
    // 索引构建（后台线程）与树刷新
    // ------------------------------------------------------------------

    private async Task BuildIndexAsync(bool force, string? initialRel = null)
    {
        _buildCts?.Cancel();
        _buildCts = new CancellationTokenSource();
        var ct = _buildCts.Token;
        _refresh.IsEnabled = false;

        var install = AppSettings.Gd;
        if (install.IsEmpty)
        {
            ShowPlaceholder(L.T("未配置任何可用的游戏目录（每层须含 database 或 resources）。请先在 工具→设置… 中配置。",
                "No usable game folders configured (each layer needs database and/or resources). Set them in Tools → Settings… first."));
            _refresh.IsEnabled = true;
            return;
        }

        try
        {
            var index = await AppSettings.GdCache.GetAsync(install, forceRefresh: force, ct);
            if (ct.IsCancellationRequested) return;
            _index = index;
            await Dispatcher.UIThread.InvokeAsync(() => RebuildTree(initialRel));
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!ct.IsCancellationRequested) _refresh.IsEnabled = true;
        }
    }

    private void ShowPlaceholder(string text)
    {
        _tree.Items.Clear();
        _nodes.Clear();
        _tree.Items.Add(new TreeViewItem { Header = text, IsEnabled = false });
        _countText.Text = "";
    }

    /// <summary>索引就绪：重建整棵树（懒加载，只展开根）。在 UI 线程调用。</summary>
    private void RebuildTree(string? initialRel)
    {
        _tree.Items.Clear();
        _nodes.Clear();
        if (_index == null) return;

        string prefix = _subDir + "\\";
        int count = _index.Files.Keys.Count(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        _countText.Text = L.F("共 {0} 个文件（合并后）", count);

        var root = MakeDirNode(_subDir, _subDir);
        _tree.Items.Add(root);
        LoadChildren(root, _subDir);
        root.IsExpanded = true;

        SelectInitial(initialRel);
    }

    // ------------------------------------------------------------------
    // 树节点（从内存索引懒加载，不实时访问磁盘）
    // ------------------------------------------------------------------

    private static string LayerTag(GdLayer layer) => layer switch
    {
        GdLayer.Base => L.T("游戏", "Base"),
        GdLayer.Dlc1 => "DLC1",
        GdLayer.Dlc2 => "DLC2",
        GdLayer.Dlc3 => "DLC3",
        _ => "MOD",
    };

    private TreeViewItem MakeDirNode(string relDir, string name)
    {
        var item = new TreeViewItem
        {
            Header = "📁 " + name,
            Tag = new Entry(relDir, true, null),
        };
        ToolTip.SetTip(item, relDir);
        _nodes[relDir] = item;
        item.Expanded += (_, _) => LoadChildren(item, relDir);
        return item;
    }

    /// <summary>展开目录：从内存索引取下一层子目录与文件（20 万条以内毫秒级）。</summary>
    private void LoadChildren(TreeViewItem item, string relDir)
    {
        if (item.Items.Count > 0 && item.Items[0] is TreeViewItem first && first.Tag is Entry) return;   // 已加载
        item.Items.Clear();
        if (_index == null) return;

        string prefix = relDir.Length == 0 ? "" : relDir + "\\";
        int pl = prefix.Length;
        var dirs = new SortedDictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(string Name, GdFileEntry Entry)>();

        foreach (var (key, entry) in _index.Files)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = key[pl..];
            int slash = rest.IndexOf('\\');
            if (slash < 0)
            {
                if (_fileFilter(entry.RelativePath)) files.Add((rest, entry));
            }
            else
            {
                dirs.TryAdd(rest[..slash], null);
            }
        }

        foreach (var name in dirs.Keys)
        {
            string childRel = prefix + name;
            item.Items.Add(_nodes.TryGetValue(childRel, out var cached) ? cached : MakeDirNode(childRel, name));
        }
        foreach (var (name, entry) in files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            string childRel = prefix + name;
            var node = new TreeViewItem
            {
                Header = "📄 " + name + "[" + LayerTag(entry.Layer) + "]",
                Tag = new Entry(childRel, false, entry),
            };
            ToolTip.SetTip(node, entry.FullPath);
            _nodes[childRel] = node;
            item.Items.Add(node);
        }
        if (item.Items.Count == 0)
            item.Items.Add(new TreeViewItem { Header = L.T("（空）", "(empty)"), IsEnabled = false });
    }

    private static Func<string, bool> CreateFilter(string subDir, string[]? exts)
    {
        if (exts is { Length: > 0 })
        {
            var set = exts.Select(e => e.TrimStart('.')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return rel => set.Contains(Path.GetExtension(rel).TrimStart('.'));
        }
        if (subDir.Equals("database", StringComparison.OrdinalIgnoreCase))
            return rel => rel.EndsWith(".dbr", StringComparison.OrdinalIgnoreCase);
        // resources：显示所有非 dbr 文件（qst/cnv/wav/ogg/tex/fx/anm/lua 等）
        return rel => !rel.EndsWith(".dbr", StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // 选择与确定
    // ------------------------------------------------------------------

    private void SelectInitial(string? rel)
    {
        if (string.IsNullOrWhiteSpace(rel) || _index == null) return;
        string relWin = rel.Trim().Replace('/', '\\');
        if (_index.Find(_subDir + "\\" + relWin) == null) return;   // 合并视图中不存在

        string[] segs = relWin.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        TreeViewItem node = (TreeViewItem)_tree.Items[0]!;
        foreach (string seg in segs)
        {
            string nodeDir = node.Tag is Entry e && e.IsDir ? e.RelPath : _subDir;
            LoadChildren(node, nodeDir);
            node.IsExpanded = true;
            string childRel = nodeDir + "\\" + seg;
            if (!_nodes.TryGetValue(childRel, out var next)) return;
            node = next;
        }
        if (node.Tag is Entry { IsDir: false })
        {
            _tree.SelectedItem = node;
            Dispatcher.UIThread.Post(node.BringIntoView, DispatcherPriority.Loaded);
        }
    }

    private string? SelectedFile() =>
        _tree.SelectedItem is TreeViewItem { Tag: Entry { IsDir: false, File: { } f } } && File.Exists(f.FullPath)
            ? f.FullPath
            : null;

    private void UpdatePicked()
    {
        if (_tree.SelectedItem is TreeViewItem { Tag: Entry e } && !e.IsDir)
        {
            _ok.IsEnabled = e.File != null && File.Exists(e.File!.FullPath);
            _picked.Text = e.RelPath[_subDir.Length..].TrimStart('\\');
        }
        else
        {
            _ok.IsEnabled = false;
            _picked.Text = "";
        }
    }

    private void Accept()
    {
        if (_tree.SelectedItem is not TreeViewItem { Tag: Entry { IsDir: false, File: { } f } e }) return;
        _result = f.FullPath;
        _resultRel = e.RelPath[_subDir.Length..].TrimStart('\\');
        Close(true);
    }

    /// <summary>
    /// 打开内置浏览器并返回相对路径（相对 resources / database，反斜杠）。
    /// 目录未配置（无任何可用层）时提示并返回 null。
    /// </summary>
    public static async Task<string?> Pick(Window? owner, string subDir, string title, string[]? exts, string? initialRel)
    {
        if (AppSettings.Gd.IsEmpty)
        {
            Gui.Warn(owner,
                L.T("未配置游戏目录，请先在 工具→设置… 中配置（游戏根目录 / DLC / MOD）。", "Game folders are not configured. Set them in Tools → Settings… first (game root / DLC / MOD)."),
                title);
            return null;
        }
        var dlg = new FileBrowserDialog(subDir, title, exts, initialRel);
        if (await dlg.ShowDialog<bool>(owner!) != true || dlg.SelectedFilePath == null) return null;
        return dlg.SelectedRelPath ?? AppSettings.ToRelative(dlg.SelectedFilePath);
    }
}
