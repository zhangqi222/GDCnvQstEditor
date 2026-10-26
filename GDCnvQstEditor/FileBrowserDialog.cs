using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GdAssets;

namespace GDCnvQstEditor;

/// <summary>
/// 内置文件浏览弹窗：纯壳，树体由公共组件 <see cref="FileTreePanel"/> 提供。
/// 两种模式共用一个组件：选择模式（条件/动作编辑的"浏览…"、设置预览均用它）带 确定/路径 行；
/// 预览模式只读浏览，仅"关闭"。
/// </summary>
internal sealed class FileBrowserDialog : Window
{
    private readonly FileTreePanel _panel;
    private readonly TextBox _picked = new() { IsReadOnly = true, Watermark = L.T("(no file selected)") };
    private readonly Button _ok;
    private string? _result;
    private string? _resultRel;

    /// <summary>确定后选中的文件绝对路径（未选择为 null）。</summary>
    public string? SelectedFilePath => _result;

    /// <summary>确定后选中的文件相对路径（相对 database / resources，反斜杠）。</summary>
    public string? SelectedRelPath => _resultRel;

    private FileBrowserDialog(string subDir, string title, string[]? exts, string? initialRel,
        string[]? excludeExts = null, bool preview = false, string? scopeText = null)
    {
        DialogChrome.Init(this, title, 720, 660);

        _ok = DialogChrome.Button(L.Confirm, primary: true);
        _ok.IsDefault = true;
        _ok.IsEnabled = false;
        _ok.Click += (_, _) => Accept();
        var cancel = preview
            ? DialogChrome.Cancel(this, L.T("Close"))
            : DialogChrome.Cancel(this);

        _panel = new FileTreePanel();
        _panel.SelectionChanged += UpdatePicked;
        _panel.FileActivated += _ => { if (!preview) Accept(); };

        // 顶部说明：本层的目录规则
        var rules = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 8) };
        rules.Children.Add(new TextBlock
        {
            Text = scopeText ?? (subDir == "database"
                ? L.T("Scope: record files (.dbr/.tpl) under each layer's database\\")
                : L.T("Scope: resources files under each layer's resources\\, excluding ARZ / ARC / DBR / TPL.")),
            Foreground = UiTheme.AccentBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        rules.Children.Add(new TextBlock
        {
            Text = L.T("Override order: game root → DLC1 → DLC2 → DLC3 → MOD; higher layers win for same-name files."),
            Foreground = UiTheme.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        rules.Children.Add(DialogChrome.Caption(preview
            ? L.T("Preview mode: browse files across layers; double-click folders to expand. No selection needed.")
            : L.T("Double-click a file, or select one and click OK.")));

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(rules, 0);
        Grid.SetRow(_panel, 1);
        grid.Children.Add(rules);
        grid.Children.Add(_panel);

        if (!preview)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var pickedRow = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
            var pickedLabel = new TextBlock { Text = L.T("Path") + L.Colon, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(pickedLabel, Dock.Left);
            pickedRow.Children.Add(pickedLabel);
            pickedRow.Children.Add(_picked);
            Grid.SetRow(pickedRow, 2);
            var footer = DialogChrome.Footer(cancel, _ok);
            Grid.SetRow(footer, 3);
            grid.Children.Add(pickedRow);
            grid.Children.Add(footer);
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var footer = DialogChrome.Footer(cancel);
            Grid.SetRow(footer, 2);
            grid.Children.Add(footer);
        }
        Content = grid;

        _ = BindAsync(subDir, BuildFilter(subDir, exts, excludeExts), initialRel);
        Closed += (_, _) => _panel.CancelBuild();
    }

    private async Task BindAsync(string subDir, Func<string, bool> filter, string? initialRel)
    {
        await _panel.BindAsync(subDir, filter, null);
        _panel.SelectRelative(initialRel);
    }

    private void UpdatePicked()
    {
        if (_panel.SelectedFile != null)
        {
            _ok.IsEnabled = true;
            _picked.Text = _panel.SelectedRelPath ?? "";
        }
        else
        {
            _ok.IsEnabled = false;
            _picked.Text = "";
        }
    }

    private void Accept()
    {
        var f = _panel.SelectedFile;
        if (f == null) return;
        _result = f.FullPath;
        _resultRel = _panel.SelectedRelPath;
        Close(true);
    }

    private static Func<string, bool> BuildFilter(string subDir, string[]? exts, string[]? excludeExts)
    {
        if (exts is { Length: > 0 })
        {
            var set = exts.Select(e => e.TrimStart('.')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return rel => set.Contains(Path.GetExtension(rel).TrimStart('.'));
        }
        if (excludeExts is { Length: > 0 })
        {
            var set = excludeExts.Select(e => e.TrimStart('.')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return rel => !set.Contains(Path.GetExtension(rel).TrimStart('.'));
        }
        if (subDir.Equals("database", StringComparison.OrdinalIgnoreCase))
        {
            // 记录文件：.dbr 为主，.tpl（模板记录）同属 database 侧
            return rel => rel.EndsWith(".dbr", StringComparison.OrdinalIgnoreCase) ||
                          rel.EndsWith(".tpl", StringComparison.OrdinalIgnoreCase);
        }
        // resources：排除归档/记录文件（.arz/.arc/.dbr/.tpl），显示 qst/cnv/wav/ogg/tex/fx/anm/lua 等
        return rel => !rel.EndsWith(".arz", StringComparison.OrdinalIgnoreCase) &&
                      !rel.EndsWith(".arc", StringComparison.OrdinalIgnoreCase) &&
                      !rel.EndsWith(".dbr", StringComparison.OrdinalIgnoreCase) &&
                      !rel.EndsWith(".tpl", StringComparison.OrdinalIgnoreCase);
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
                L.T("Game folders are not configured. Set them in Tools → Settings… first (game root / DLC / MOD)."),
                title);
            return null;
        }
        var dlg = new FileBrowserDialog(subDir, title, exts, initialRel);
        if (await dlg.ShowDialog<bool>(owner!) != true || dlg.SelectedFilePath == null) return null;
        return dlg.SelectedRelPath ?? AppSettings.ToRelative(dlg.SelectedFilePath);
    }

    /// <summary>
    /// 预览模式打开内置浏览器（只浏览不选择，无"确定/路径"行）：
    /// database 传 onlyExts=["dbr"]；resources 传 excludeExts=["arz","arc","dbr"]。
    /// </summary>
    public static async Task Preview(Window? owner, string subDir, string title, string? scopeText,
        string[]? onlyExts, string[]? excludeExts)
    {
        if (AppSettings.Gd.IsEmpty)
        {
            Gui.Warn(owner,
                L.T("Game folders are not configured. Set them in Tools → Settings… first (game root / DLC / MOD)."),
                title);
            return;
        }
        var dlg = new FileBrowserDialog(subDir, title, onlyExts, null,
            excludeExts: excludeExts, preview: true, scopeText: scopeText);
        await dlg.ShowDialog<bool>(owner!);
    }
}
