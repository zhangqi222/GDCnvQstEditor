using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using CnvQst;

namespace CnvQstEditor;

/// <summary>
/// 底部节点文本编辑器（官方编辑器风格：选中行后底部编辑，替代弹窗）：
/// 顶部 = Quest ID 行（仅 QST）；下方 = 任务文件主语言文本（单栏）。
/// 文本编辑器使用 AvaloniaEdit（行号 + 自动换行），并在标题行提示当前槽位的填写建议。
/// </summary>
internal sealed class NodeTextPanel : UserControl
{
    private readonly TextBlock _questIdText = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.MutedBrush };
    private readonly Button _copyId = new() { Content = L.Copy, MinWidth = 56, MinHeight = 22, Margin = new Thickness(6, 0, 0, 0) };
    private string _questId = "";   // 当前选中的任务 ID（Bind 时赋值；复制按钮直接取，避免解析显示文本）
    private readonly TextEditor _main = NewEditor();
    private readonly TextBlock _mainLabel = MkLabel("");
    private readonly TextBlock _hint = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = UiTheme.MutedBrush,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly StackPanel _idHost;
    private DocSession? _doc;
    private int? _slot;
    private bool _building;
    private string? _assigned;   // Bind 最近一次程序化赋给 _main 的显示文本（识别延迟派发的 TextChanged）

    /// <summary>文本被编辑（调用方刷新脏标记/状态）。</summary>
    public event Action? TextEdited;

    public NodeTextPanel()
    {
        _copyId.Click += (_, _) => ClipboardHelper.SetText(_questId);

        _idHost = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        _idHost.Children.Add(_questIdText);
        _idHost.Children.Add(_copyId);

        // 布局：id 行 → 标签/建议行 → 单栏文本编辑器（填满剩余高度）
        var grid = _grid = new Grid { Margin = new Thickness(10, 4, 10, 4) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var labelRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        _mainLabel.Margin = new Thickness(0, 0, 8, 0);
        labelRow.Children.Add(_mainLabel);
        labelRow.Children.Add(_hint);

        Place(_idHost, 0);
        Place(labelRow, 1);
        Place(_main, 2);

        Content = grid;

        _main.TextChanged += (_, _) => OnEdited(_main);
    }

    private static TextEditor NewEditor()
    {
        var editor = new TextEditor
        {
            WordWrap = true,
            ShowLineNumbers = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Background = Brushes.White,
            Padding = new Thickness(4, 2, 4, 2),
            LineNumbersForeground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
            LineNumbersMargin = new Thickness(4, 0, 6, 0),
        };
        editor.TextArea.Foreground = UiTheme.TextBrush;
        editor.TextArea.Background = Brushes.White;
        return editor;
    }

    private static TextBlock MkLabel(string text) => new() { Text = text };

    private Grid _grid = null!;

    private void Place(Control e, int row)
    {
        e.SetValue(Grid.RowProperty, row);
        _grid.Children.Add(e);
    }

    /// <summary>绑定选中行的文本槽位；slot 为 null（未选中/该行无文本）时禁用。</summary>
    public void Bind(DocSession? doc, int? slot)
    {
        _building = true;
        _doc = doc;
        _slot = slot;

        // Quest ID 行（仅 QST 文档显示）
        if (doc?.Qst != null)
        {
            _questId = "0x" + doc.Qst.Id.ToString("X8");   // 引用标识统一十六进制（与表格列/官方 Lua 一致）
            _questIdText.Text = L.F("Quest ID: {0}", _questId);
            _idHost.IsVisible = true;
        }
        else
        {
            _idHost.IsVisible = false;
        }

        if (doc == null || slot == null)
        {
            IsEnabled = false;
            _assigned = "";
            _main.Text = "";
            _mainLabel.Text = L.T("Node text");
            _hint.Text = "";
            _building = false;
            return;
        }

        IsEnabled = true;
        _mainLabel.Text = L.T("Node text") + L.InParen(doc.PrimaryLanguage.Tag.Text);
        BindHint(doc, slot.Value);
        _assigned = TableLayout.Display(GetText(doc.PrimaryLanguage, slot.Value));
        _main.Text = _assigned;
        _building = false;
    }

    private void BindHint(DocSession doc, int slot)
    {
        SlotInfo? info = null;
        if (doc.Cnv != null)
        {
            var slots = SlotGuide.ForCnv(doc.Cnv);
            if (slot >= 0 && slot < slots.Count) info = slots[slot];
            else _hint.Text = L.T("(no bound node)");
        }
        else if (doc.Qst != null)
        {
            var slots = SlotGuide.ForQst(doc.Qst);
            if (slot >= 0 && slot < slots.Count) info = slots[slot];
            else _hint.Text = L.T("(no matching slot)");
        }
        if (info == null)
        {
            _hint.Foreground = UiTheme.MutedBrush;
            return;
        }

        _hint.Text = SlotText.Hint(info) + " · " + SlotText.Role(info);
        _hint.Foreground = info.Suggested ? UiTheme.AccentBrush
            : info.MustBeEmpty ? UiTheme.WarnBrush
            : UiTheme.MutedBrush;
    }

    private static string GetText(LanguageBlock lang, int slot) =>
        slot >= 0 && slot < lang.Texts.Count ? lang.Texts[slot].Text : "";

    private void OnEdited(TextEditor box)
    {
        if (_building || _doc == null || _slot == null) return;
        // 程序化赋值后 TextChanged 可能延迟到 Bind 结束才派发，_building 已复位；
        // 与 Bind 赋的确切文本一致时一律忽略（对任意内容都成立，不依赖 Display/Stored 往返），
        // 避免仅选中行就把文档标成未保存。
        if (box.Text == _assigned) return;
        var lang = _doc.PrimaryLanguage;
        int s = _slot.Value;
        if (s >= lang.Texts.Count) return;
        string incoming = TableLayout.Stored(box.Text ?? "");
        // 双保险：换行编码往返后与模型原文一致也视为未修改
        if (incoming == GetText(lang, s)) return;
        lang.Texts[s].SetText(incoming);
        _assigned = box.Text;
        TextEdited?.Invoke();
    }
}
