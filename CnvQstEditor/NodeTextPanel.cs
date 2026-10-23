using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CnvQst;

namespace CnvQstEditor;

/// <summary>
/// 底部节点文本编辑器（官方编辑器风格：选中行后底部编辑，替代弹窗）：
/// 顶部 = Quest ID 行（仅 QST）；下方 = 任务文件主语言文本（单栏）。
/// 输入框自动换行，纵向滚动、无横向滚动条。
/// </summary>
internal sealed class NodeTextPanel : UserControl
{
    private readonly TextBlock _questIdText = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.MutedBrush };
    private readonly Button _copyId = new() { Content = L.Copy, MinWidth = 56, MinHeight = 22, Margin = new Thickness(6, 0, 0, 0) };
    private string _questId = "";   // 当前选中的任务 ID（Bind 时赋值；复制按钮直接取，避免解析显示文本）
    private readonly TextBox _main = NewBox();
    private readonly LineNumberGutter _gutter = new();
    private readonly TextBlock _mainLabel = MkLabel("");
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

        // 布局：id 行 → 标签行 → 单栏输入框（固定高度填满剩余空间）
        var grid = _grid = new Grid { Margin = new Thickness(10, 4, 10, 4) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var editorRow = new Grid();
        editorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        editorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _gutter.SetValue(Grid.ColumnProperty, 0);
        _main.SetValue(Grid.ColumnProperty, 1);
        editorRow.Children.Add(_gutter);
        editorRow.Children.Add(_main);

        Place(_idHost, 0);
        Place(_mainLabel, 1);
        Place(editorRow, 2);
        _gutter.Attach(_main);

        Content = grid;

        _main.TextChanged += (_, _) => OnEdited(_main);
    }

    private static TextBox NewBox()
    {
        var box = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,   // 超长自动换行；纵向滚动由 TextBox 内建，无横向滚动条
            VerticalAlignment = VerticalAlignment.Stretch,   // 固定填满父容器分给的高度，不随内容增高
            // Semi 主题默认 VerticalContentAlignment=Center（内容垂直居中）；改为默认顶格左上对齐
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        EnableReliableCopy(box);
        return box;
    }

    // 复制修复逻辑已提炼为全局 ClipboardHelper.Enable（所有 TextBox 统一启用），
    // 这里仅为可读性保留转调。
    private static void EnableReliableCopy(TextBox box) => ClipboardHelper.Enable(box);

    private static TextBlock MkLabel(string text) => new() { Text = text, Margin = new Thickness(0, 0, 0, 2) };

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
            _gutter.IsVisible = false;
            _assigned = "";
            SetBoxText(_main, "");
            _mainLabel.Text = L.T("Node text");
            _building = false;
            return;
        }

        IsEnabled = true;
        _gutter.IsVisible = true;
        _mainLabel.Text = L.T("Node text") + L.InParen(doc.PrimaryLanguage.Tag.Text);
        _assigned = TableLayout.Display(GetText(doc.PrimaryLanguage, slot.Value));
        SetBoxText(_main, _assigned);
        _building = false;
    }

    private static string GetText(LanguageBlock lang, int slot) =>
        slot >= 0 && slot < lang.Texts.Count ? lang.Texts[slot].Text : "";

    /// <summary>
    /// 程序化赋值不进撤销栈：临时关闭撤销并把 UndoLimit 归零（二者都会丢弃既有撤销历史），
    /// 这样 Ctrl+Z 只撤销用户自己的输入，不会一路回退清空绑定进来的原文。
    /// </summary>
    private static void SetBoxText(TextBox box, string text)
    {
        bool undo = box.IsUndoEnabled;
        box.IsUndoEnabled = false;
        box.UndoLimit = 0;
        box.Text = text;
        box.UndoLimit = 200;
        box.IsUndoEnabled = undo;
    }

    private void OnEdited(TextBox box)
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