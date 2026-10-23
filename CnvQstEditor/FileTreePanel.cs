using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using GdAssets;

namespace CnvQstEditor;

/// <summary>
/// 跨层合并文件树（公共组件）：按 GD 覆盖顺序（游戏根 → DLC1/2/3 → MOD）
/// 合并各层 database / resources 下的文件，展示为单棵树；同名文件只出现一次，
/// 文件名旁用 Semi TagLabel 标注来源层（如 aaa.dbr [DLC3]），ToolTip 为实际落盘路径。
/// 文件索引由独立库 GdAssets 提供（分层枚举 + 合并 + 按层缓存），本控件只负责展示。
/// 文件浏览弹窗（选择模式）与设置里的预览模式共用本组件。
/// </summary>
internal sealed class FileTreePanel : UserControl
{
    /// <summary>树节点 Tag：目录或文件。</summary>
    public sealed record NodeEntry(string RelPath, bool IsDir, GdFileEntry? File);

    /// <summary>选中项变化（可能变为目录或取消选中）。</summary>
    public event Action? SelectionChanged;

    /// <summary>双击 / 回车激活某个文件。</summary>
    public event Action<GdFileEntry>? FileActivated;

    private readonly TreeView _tree = new() { MinHeight = 240 };
    private readonly Grid _treeHost = new();   // 树 + 扫描进度覆盖层
    private readonly StackPanel _buildingTip;
    private readonly TextBlock _countText = new() { Foreground = UiTheme.MutedBrush };
    private readonly Button _refreshBtn;
    private readonly Dictionary<string, TreeViewItem> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loaded = new(StringComparer.OrdinalIgnoreCase);   // 已加载子项的目录
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);     // 索引中存在的全部目录（决定是否有展开箭头）
    private GdIndex? _index;
    private string _subDir = "";
    private Func<string, bool> _filter = _ => true;
    private CancellationTokenSource? _buildCts;

    internal GdIndex? IndexForTest => _index;

    public FileTreePanel()
    {
        _refreshBtn = DialogChrome.Button(L.T("Rescan"), primary: false);
        _refreshBtn.Click += (_, _) => _ = BindAsync(_subDir, _filter, null, force: true);

        _tree.SelectionChanged += (_, _) => SelectionChanged?.Invoke();
        _tree.DoubleTapped += (_, e) =>
        {
            if (SelectedFile == null) return;
            e.Handled = true;
            FileActivated?.Invoke(SelectedFile);
        };
        _tree.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || SelectedFile == null) return;
            e.Handled = true;
            FileActivated?.Invoke(SelectedFile);
        };

        // Semi 圆形进度（ProgressRing 主题）：扫描各层文件期间盖在树区中央
        var ring = new ProgressBar { IsIndeterminate = true, Width = 32, Height = 32 };
        if (Application.Current != null)
        {
            var theme = Application.Current.ActualThemeVariant ?? ThemeVariant.Default;
            if (Application.Current.TryFindResource("ProgressRing", theme, out var ringTheme) &&
                ringTheme is ControlTheme ct)
                ring.Theme = ct;
        }
        _buildingTip = new StackPanel
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _buildingTip.Children.Add(ring);
        _buildingTip.Children.Add(new TextBlock
        {
            Text = L.T("Scanning layers…"),
            Foreground = UiTheme.MutedBrush,
            Margin = new Thickness(0, 10, 0, 0),
        });

        _treeHost.Children.Add(_tree);
        _treeHost.Children.Add(_buildingTip);

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var refreshHost = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(refreshHost, Dock.Right);
        refreshHost.Children.Add(_refreshBtn);
        head.Children.Add(refreshHost);
        head.Children.Add(_countText);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(head, 0);
        Grid.SetRow(_treeHost, 1);
        grid.Children.Add(head);
        grid.Children.Add(_treeHost);
        Content = grid;
    }

    /// <summary>当前选中的文件（未选中文件 / 选中目录时为 null）。</summary>
    public GdFileEntry? SelectedFile =>
        _tree.SelectedItem is TreeViewItem { Tag: NodeEntry { IsDir: false, File: { } f } } && File.Exists(f.FullPath)
            ? f : null;

    /// <summary>选中文件的相对路径（相对 database / resources）；未选中为 null。</summary>
    public string? SelectedRelPath =>
        _tree.SelectedItem is TreeViewItem { Tag: NodeEntry e } && !e.IsDir
            ? e.RelPath[_subDir.Length..].TrimStart('\\')
            : null;

    /// <summary>
    /// 绑定并后台构建索引（完成前树显示占位文本）。install 为 null 时取 AppSettings.Gd。
    /// 返回后根节点已加载（可断言/选择）。
    /// </summary>
    public async Task BindAsync(string subDir, Func<string, bool> filter, string? scopeText,
        GdInstall? install = null, bool force = false)
    {
        _subDir = subDir;
        _filter = filter;
        _buildCts?.Cancel();
        _buildCts = new CancellationTokenSource();
        var ct = _buildCts.Token;
        _refreshBtn.IsEnabled = false;
        install ??= AppSettings.Gd;

        if (install.IsEmpty)
        {
            ShowMessage(L.T("No usable game folders configured (each layer needs database and/or resources). Set them in Tools → Settings… first."));
            _refreshBtn.IsEnabled = true;
            return;
        }

        ShowBuilding();
        try
        {
            var index = await AppSettings.GdCache.GetAsync(install, forceRefresh: force, ct);
            if (ct.IsCancellationRequested) return;
            _index = index;
            await Dispatcher.UIThread.InvokeAsync(() => RebuildTree());
        }
        catch (OperationCanceledException) { }
        finally
        {
            _buildingTip.IsVisible = false;
            if (!ct.IsCancellationRequested) _refreshBtn.IsEnabled = true;
        }
    }

    /// <summary>初始选中某相对路径（BindAsync 之后调用）。</summary>
    public void SelectRelative(string? rel)
    {
        if (string.IsNullOrWhiteSpace(rel) || _index == null) return;
        string relWin = rel.Trim().Replace('/', '\\');
        if (_index.Find(_subDir + "\\" + relWin) == null) return;

        string[] segs = relWin.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        TreeViewItem node = (TreeViewItem)_tree.Items[0]!;
        foreach (string seg in segs)
        {
            string nodeDir = node.Tag is NodeEntry e && e.IsDir ? e.RelPath : _subDir;
            LoadChildren(node, nodeDir);
            node.IsExpanded = true;
            if (!_nodes.TryGetValue(nodeDir + "\\" + seg, out var next)) return;
            node = next;
        }
        if (node.Tag is NodeEntry { IsDir: false })
        {
            _tree.SelectedItem = node;
            Dispatcher.UIThread.Post(node.BringIntoView, DispatcherPriority.Loaded);
        }
    }

    /// <summary>扫描中：清树并显示圆形进度覆盖层。</summary>
    private void ShowBuilding()
    {
        _tree.Items.Clear();
        _nodes.Clear();
        _loaded.Clear();
        _countText.Text = "";
        _buildingTip.IsVisible = true;
    }

    /// <summary>无法使用时的占位提示（无进度环）。</summary>
    private void ShowMessage(string text)
    {
        _tree.Items.Clear();
        _nodes.Clear();
        _loaded.Clear();
        _buildingTip.IsVisible = false;
        _tree.Items.Add(new TreeViewItem { Header = text, IsEnabled = false });
        _countText.Text = "";
    }

    /// <summary>索引就绪：重建整棵树（懒加载，只展开根）。在 UI 线程调用。</summary>
    private void RebuildTree()
    {
        _buildingTip.IsVisible = false;
        _tree.Items.Clear();
        _nodes.Clear();
        _loaded.Clear();
        if (_index == null) return;

        // 目录集合：决定每个目录节点是否挂占位子项（挂了才有展开箭头可点）
        _dirs.Clear();
        _dirs.Add(_subDir);
        string prefix = _subDir + "\\";
        foreach (var key in _index.Files.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            int idx = prefix.Length;
            while (true)
            {
                int slash = key.IndexOf('\\', idx);
                if (slash < 0) break;
                _dirs.Add(key[..slash]);
                idx = slash + 1;
            }
        }

        int count = _index.Files.Keys.Count(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        _countText.Text = L.F("{0} files (merged)", count);

        var root = MakeDirNode(_subDir, _subDir);
        _tree.Items.Add(root);
        LoadChildren(root, _subDir);
        root.IsExpanded = true;
    }

    // ------------------------------------------------------------------
    // 树节点（从内存索引懒加载，不实时访问磁盘）
    // ------------------------------------------------------------------

    private static string LayerTag(GdLayer layer) => layer switch
    {
        GdLayer.Base => L.T("Base"),
        GdLayer.Dlc1 => "DLC1",
        GdLayer.Dlc2 => "DLC2",
        GdLayer.Dlc3 => "DLC3",
        _ => "MOD",
    };

    /// <summary>来源标签的 Semi TagLabel 颜色类（见 Semi.Avalonia Label.axaml 的颜色 Classes）。</summary>
    private static string LayerClass(GdLayer layer) => layer switch
    {
        GdLayer.Base => "Grey",
        GdLayer.Dlc1 => "Blue",
        GdLayer.Dlc2 => "Cyan",
        GdLayer.Dlc3 => "Indigo",
        _ => "Green",
    };

    /// <summary>Semi 矢量图标（FillIcons 字典已并入应用资源；与 CNV/QST 表格同一套）。</summary>
    private static PathIcon MakeIcon(string semiKey, double size = 14)
    {
        var icon = new PathIcon
        {
            Width = size,
            Height = size,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        };
        if (Application.Current != null)
        {
            var theme = Application.Current.ActualThemeVariant ?? ThemeVariant.Default;
            if (Application.Current.TryFindResource(semiKey, theme, out var v) && v is Geometry g)
                icon.Data = g;
        }
        return icon;
    }

    /// <summary>目录节点头：Semi 文件夹图标 + 名称（根用打开态图标）。</summary>
    private Control MakeDirHeader(string name, bool isRoot)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(MakeIcon(isRoot ? "SemiIconFolderOpen" : "SemiIconFolder"));
        panel.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    /// <summary>文件节点头：Semi 文件图标 + 名称 + 来源标签（Semi TagLabel；主题缺失时退化为 [文本]）。</summary>
    private Control MakeFileHeader(string name, GdFileEntry entry)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        panel.Children.Add(MakeIcon("SemiIconFile"));
        panel.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        var tag = new Label
        {
            Content = LayerTag(entry.Layer),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // TagLabel 是 Semi.Avalonia 经 StyleInclude 并入应用样式区的 ControlTheme，
        // 须沿控件资源链查找（Application.Resources 里没有）
        if (this.TryFindResource("TagLabel", out var theme) && theme is ControlTheme ct)
        {
            tag.Theme = ct;
            tag.Classes.Add(LayerClass(entry.Layer));
        }
        else
        {
            tag.Content = "[" + LayerTag(entry.Layer) + "]";
            tag.Foreground = UiTheme.MutedBrush;
        }
        panel.Children.Add(tag);
        return panel;
    }

    private TreeViewItem MakeDirNode(string relDir, string name)
    {
        var item = new TreeViewItem
        {
            Header = MakeDirHeader(name, relDir == _subDir),
            Tag = new NodeEntry(relDir, true, null),
        };
        ToolTip.SetTip(item, relDir);
        _nodes[relDir] = item;
        item.Expanded += (_, _) => LoadChildren(item, relDir);
        // 关键：Avalonia TreeViewItem 无子项时是"叶子"（:empty），展开箭头不可用——
        // 有子级的目录必须先挂占位子项撑出箭头，展开时 LoadChildren 再替换为真实内容。
        if (_dirs.Contains(relDir))
            item.Items.Add(new TreeViewItem { Header = "…", IsEnabled = false });
        return item;
    }

    /// <summary>展开目录：从内存索引取下一层子目录与文件（20 万条以内毫秒级）。</summary>
    private void LoadChildren(TreeViewItem item, string relDir)
    {
        if (!_loaded.Add(relDir)) return;   // 已加载（含初次为空目录的情况）
        item.Items.Clear();
        if (_index == null) return;

        string prefix = relDir + "\\";
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
                if (_filter(entry.RelativePath)) files.Add((rest, entry));
            }
            else
            {
                dirs.TryAdd(rest[..slash], null);
            }
        }

        foreach (string name in dirs.Keys)
        {
            string childRel = prefix + name;
            item.Items.Add(_nodes.TryGetValue(childRel, out var cached) ? cached : MakeDirNode(childRel, name));
        }
        foreach (var (name, entry) in files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            string childRel = prefix + name;
            var node = new TreeViewItem
            {
                Header = MakeFileHeader(name, entry),
                Tag = new NodeEntry(childRel, false, entry),
            };
            ToolTip.SetTip(node, entry.FullPath);
            _nodes[childRel] = node;
            item.Items.Add(node);
        }
        if (item.Items.Count == 0)
            item.Items.Add(new TreeViewItem { Header = L.T("(empty)"), IsEnabled = false });
    }

    // ------------------------------------------------------------------
    // 自测支持（--smoke）：递归展开并收集全部文件节点的相对路径
    // ------------------------------------------------------------------

    /// <summary>取消后台索引构建（宿主窗口关闭时调用）。</summary>
    public void CancelBuild() => _buildCts?.Cancel();

    /// <summary>取消选中（关闭弹窗前调用可避免 SelectionChanged 触碰已隐藏控件）。</summary>
    public void ClearSelection() => _tree.SelectedItem = null;

    /// <summary>自测支持（--smoke）：递归展开并收集全部文件节点的相对路径。</summary>
    internal List<string> CollectFilePathsForTest()
    {
        var result = new List<string>();
        if (_tree.Items.Count == 0 || _tree.Items[0] is not TreeViewItem root) return result;
        Walk(root);
        return result;

        void Walk(TreeViewItem item)
        {
            if (item.Tag is NodeEntry e && e.IsDir) LoadChildren(item, e.RelPath);
            foreach (var child in item.Items.OfType<TreeViewItem>())
            {
                if (child.Tag is NodeEntry ce && !ce.IsDir) result.Add(ce.RelPath);
                else Walk(child);
            }
        }
    }

    /// <summary>自测支持：目录节点是否有非空 Items（占位或真实子项 → 展开箭头可用）。用于防"锁死目录"回归。</summary>
    internal bool DirExpandableForTest(string relDir) =>
        _nodes.TryGetValue(relDir, out var n) && n.Items.Count > 0;

    /// <summary>自测支持：收集已加载树中的全部目录节点相对路径。</summary>
    internal List<string> CollectDirRelPathsForTest()
    {
        var result = new List<string>();
        if (_tree.Items.Count == 0 || _tree.Items[0] is not TreeViewItem root) return result;
        Walk(root);
        return result;

        void Walk(TreeViewItem item)
        {
            if (item.Tag is NodeEntry e && e.IsDir)
            {
                LoadChildren(item, e.RelPath);
                result.Add(e.RelPath);
            }
            foreach (var child in item.Items.OfType<TreeViewItem>())
                Walk(child);
        }
    }

    /// <summary>
    /// --smoke 自测：临时目录造 Base+MOD 两层迷你树，断言目录与文件节点都能建立、MOD 覆盖生效。
    /// 返回错误消息；null = 通过。
    /// </summary>
    internal static async Task<string?> SelfTestAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "cnvqst_smoke_filetree");
        string baseDir = Path.Combine(root, "base");
        string modDir = Path.Combine(root, "mod");
        string baseDb = Path.Combine(baseDir, "database", "records");
        string modDb = Path.Combine(modDir, "database", "records");
        string modRes = Path.Combine(modDir, "resources", "snd");
        Directory.CreateDirectory(baseDb);
        Directory.CreateDirectory(modDb);
        Directory.CreateDirectory(modRes);
        File.WriteAllText(Path.Combine(baseDb, "only_base.dbr"), "x");
        File.WriteAllText(Path.Combine(baseDb, "shared.dbr"), "base");
        File.WriteAllText(Path.Combine(modDb, "shared.dbr"), "mod");   // MOD 覆盖同名
        File.WriteAllText(Path.Combine(modRes, "a.wav"), "x");

        var install = new GdInstall { GameRoot = baseDir, Mod = modDir };
        try
        {
            var panel = new FileTreePanel();
            await panel.BindAsync("database",
                rel => rel.EndsWith(".dbr", StringComparison.OrdinalIgnoreCase), null, install);
            var files = panel.CollectFilePathsForTest();
            int dbr = files.Count(p => p.EndsWith(".dbr", StringComparison.OrdinalIgnoreCase));
            if (dbr != 2)
                return L.F("File-tree self-test failed: expected 2 .dbr nodes, got {0} ({1})", dbr, string.Join(", ", files));
            // 展开箭头可用性：树中每个目录节点必须带非空子项，否则用户点不开（叶子锁死）
            var dirs = panel.CollectDirRelPathsForTest();
            var locked = dirs.Where(d => !panel.DirExpandableForTest(d) && panel.IndexForTest?.Files.Keys
                .Any(k => k.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase)) == true).ToList();
            if (locked.Count > 0)
                return L.F("File-tree self-test failed: folder lacks a placeholder child (expand arrow unavailable): {0}", string.Join(", ", locked));
            var idx = panel.IndexForTest;
            var shared = idx?.Find(@"database\records\shared.dbr");
            if (shared == null) return "文件树自测失败：索引缺少 shared.dbr";
            if (shared.Layer != GdLayer.Mod) return "文件树自测失败：shared.dbr 未被 MOD 层覆盖";
            return null;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
