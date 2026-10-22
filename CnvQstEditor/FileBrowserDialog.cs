using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CnvQstEditor;

/// <summary>
/// 内置文件浏览器：根目录固定为 &lt;游戏根目录&gt;\database（dbr）或
/// &lt;游戏根目录&gt;\resources（非 dbr），与官方编辑器的工作目录逻辑一致，
/// 不允许手动定位到其它目录，降低误选风险。
/// </summary>
internal sealed class FileBrowserDialog : Window
{
    private sealed record Entry(string Path, bool IsDir);

    private readonly string _baseDir;
    private readonly Func<string, bool> _fileFilter;
    private readonly TreeView _tree = new() { MinHeight = 320 };
    private readonly TextBox _picked = new() { IsReadOnly = true, Watermark = L.T("（未选择文件）", "(No file selected)") };
    private readonly Button _ok;
    private readonly TreeViewItem _rootItem;
    private readonly Dictionary<string, TreeViewItem> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private string? _result;

    /// <summary>确定后选中的文件绝对路径（未选择为 null）。</summary>
    public string? SelectedFilePath => _result;

    private FileBrowserDialog(string baseDir, string subDir, string title, string[]? exts, string? initialRel)
    {
        _baseDir = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar);
        _fileFilter = CreateFilter(subDir, exts);
        DialogChrome.Init(this, title, 700, 640);

        _rootItem = MakeDirNode(_baseDir);
        _tree.Items.Add(_rootItem);
        LoadDir(_rootItem, _baseDir);
        _rootItem.IsExpanded = true;
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

        var rules = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 8) };
        rules.Children.Add(Rule(L.F("dbr 文件应在：{0}\\database\\", RootForDisplay), subDir == "database"));
        rules.Children.Add(Rule(L.F("非 dbr 文件应在：{0}\\resources\\（qst,cnv,wav,ogg,tex,fx,anm,lua 等）", RootForDisplay), subDir != "database"));
        rules.Children.Add(new TextBlock
        {
            Text = L.F("根目录：{0}", _baseDir),
            Foreground = UiTheme.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
        rules.Children.Add(DialogChrome.Caption(L.T("双击文件或选中后点击确定。", "Double-click a file, or select it and click OK.")));
        if (!HasVisible(_baseDir))
            rules.Children.Add(new TextBlock
            {
                Text = L.T("（此目录下没有可选择的文件）", "(No selectable files under this folder)"),
                Foreground = UiTheme.DangerBrush,
            });

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

        SelectInitial(initialRel);
    }

    private string RootForDisplay => AppSettings.GameRoot.TrimEnd('\\');

    private static TextBlock Rule(string text, bool active) => new()
    {
        Text = text,
        Foreground = active ? UiTheme.AccentBrush : UiTheme.MutedBrush,
        TextWrapping = TextWrapping.Wrap,
    };

    private static Func<string, bool> CreateFilter(string subDir, string[]? exts)
    {
        if (exts is { Length: > 0 })
        {
            var set = exts.Select(e => e.TrimStart('.')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return path => set.Contains(Path.GetExtension(path).TrimStart('.'));
        }
        if (subDir.Equals("database", StringComparison.OrdinalIgnoreCase))
            return path => Path.GetExtension(path).Equals(".dbr", StringComparison.OrdinalIgnoreCase);
        // resources：显示所有非 dbr 文件（qst/cnv/wav/ogg/tex/fx/anm/lua 等）
        return path => !Path.GetExtension(path).Equals(".dbr", StringComparison.OrdinalIgnoreCase);
    }

    private TreeViewItem MakeDirNode(string path)
    {
        var item = new TreeViewItem
        {
            Header = HeaderText(Path.GetFileName(path), true),
            Tag = new Entry(path, true),
        };
        ToolTip.SetTip(item, path);
        _nodes[path] = item;
        if (HasVisible(path))
            item.Items.Add(new TreeViewItem { Header = "…", IsEnabled = false });
        item.Expanded += (_, _) => LoadDir(item, path);
        return item;
    }

    private static string HeaderText(string name, bool dir) => (dir ? "📁 " : "📄 ") + name;

    private bool HasVisible(string dir)
    {
        try
        {
            if (Directory.EnumerateDirectories(dir).Any()) return true;
            return Directory.EnumerateFiles(dir).Any(_fileFilter);
        }
        catch { return false; }
    }

    private void LoadDir(TreeViewItem item, string dir)
    {
        if (!_loaded.Add(dir)) return;
        item.Items.Clear();
        try
        {
            foreach (string sub in Directory.EnumerateDirectories(dir).OrderBy(p => Path.GetFileName(p) ?? "", StringComparer.OrdinalIgnoreCase))
                item.Items.Add(MakeDirNode(sub));
            foreach (string file in Directory.EnumerateFiles(dir).Where(_fileFilter).OrderBy(p => Path.GetFileName(p) ?? "", StringComparer.OrdinalIgnoreCase))
            {
                var node = new TreeViewItem
                {
                    Header = HeaderText(Path.GetFileName(file), false),
                    Tag = new Entry(file, false),
                };
                ToolTip.SetTip(node, file);
                _nodes[file] = node;
                item.Items.Add(node);
            }
        }
        catch { /* 无权限 / 枚举失败：显示为空目录 */ }
        if (item.Items.Count == 0)
            item.Items.Add(new TreeViewItem { Header = L.T("（空）", "(empty)"), IsEnabled = false });
    }

    private void SelectInitial(string? rel)
    {
        if (string.IsNullOrWhiteSpace(rel)) return;
        string full;
        try { full = Path.GetFullPath(Path.Combine(_baseDir, rel.Replace('/', '\\'))); }
        catch { return; }
        string prefix = _baseDir + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;

        string relative = Path.GetRelativePath(_baseDir, full);
        string[] segs = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        TreeViewItem cur = _rootItem;
        string curPath = _baseDir;
        foreach (string seg in segs)
        {
            string next = Path.Combine(curPath, seg);
            if (!_nodes.TryGetValue(next, out var child)) return;
            if (child.Tag is Entry { IsDir: true })
            {
                LoadDir(child, next);
                child.IsExpanded = true;
                cur = child;
                curPath = next;
            }
            else
            {
                _tree.SelectedItem = child;
                Dispatcher.UIThread.Post(child.BringIntoView, DispatcherPriority.Loaded);
                return;
            }
        }
    }

    private string? SelectedFile() =>
        _tree.SelectedItem is TreeViewItem { Tag: Entry { IsDir: false } entry } && File.Exists(entry.Path)
            ? entry.Path
            : null;

    private void UpdatePicked()
    {
        string? file = SelectedFile();
        _ok.IsEnabled = file != null;
        _picked.Text = file == null ? "" : Path.GetRelativePath(_baseDir, file);
    }

    private void Accept()
    {
        string? file = SelectedFile();
        if (file == null) return;
        _result = file;
        Close(true);
    }

    /// <summary>
    /// 打开内置浏览器并返回相对路径（相对 resources / database，反斜杠）。
    /// 游戏根目录或目标子目录不存在时提示并返回 null。
    /// </summary>
    public static async Task<string?> Pick(Window? owner, string subDir, string title, string[]? exts, string? initialRel)
    {
        string root = AppSettings.GameRoot.Trim();
        if (root.Length == 0 || !Directory.Exists(root))
        {
            string msg = root.Length == 0
                ? L.T("未设置游戏根目录，请先在 工具→设置… 中配置。", "Game root folder is not set. Configure it in Tools → Settings… first.")
                : L.F("游戏根目录不存在：{0}", root) + "\n\n" + L.T("请设置正确的游戏根目录，并解包相应资源。", "Set the correct game root folder and unpack the corresponding resources.");
            Gui.Warn(owner, msg, title);
            return null;
        }
        string baseDir = Path.Combine(root, subDir);
        if (!Directory.Exists(baseDir))
        {
            Gui.Warn(owner,
                L.F("目录不存在：{0}", baseDir) + "\n\n" +
                L.T("请设置正确的游戏根目录，并解包相应资源。", "Set the correct game root folder and unpack the corresponding resources."),
                title);
            return null;
        }
        var dlg = new FileBrowserDialog(baseDir, subDir, title, exts, initialRel);
        if (await dlg.ShowDialog<bool>(owner!) != true || dlg.SelectedFilePath == null) return null;
        return Path.GetRelativePath(dlg._baseDir, dlg.SelectedFilePath);
    }
}
