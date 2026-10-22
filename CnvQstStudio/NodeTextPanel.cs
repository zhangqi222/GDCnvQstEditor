using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CnvQst;

namespace CnvQstStudio;

/// <summary>
/// 底部节点文本编辑器（官方编辑器风格：选中行后底部编辑，替代弹窗）：
/// 顶部 = Quest ID 行（仅 QST）；下方两栏左右并排 ——
/// 左 = 任务文件主语言文本，右 = 文件内其它语言块的同一槽位（双语编辑）。
/// 输入框自动换行，纵向滚动、无横向滚动条。
/// </summary>
internal sealed class NodeTextPanel : UserControl
{
    private readonly TextBlock _questIdText = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.MutedBrush };
    private readonly Button _copyId = new() { Content = L.Copy, MinWidth = 56, MinHeight = 22, Margin = new Thickness(6, 0, 0, 0) };
    private string _questId = "";   // 当前选中的任务 ID（Bind 时赋值；复制按钮直接取，避免解析显示文本）
    private readonly TextBox _main = NewBox();
    private readonly TextBox _alt = NewBox();
    private readonly TextBlock _mainLabel = MkLabel("");
    private readonly TextBlock _altLabel = MkLabel("");
    private readonly StackPanel _idHost;
    private DocSession? _doc;
    private int? _slot;
    private LanguageBlock? _altLang;
    private bool _building;

    /// <summary>文本被编辑（调用方刷新脏标记/状态）。</summary>
    public event Action? TextEdited;

    public NodeTextPanel()
    {
        _copyId.Click += (_, _) => ClipboardHelper.SetText(_questId);

        _idHost = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        _idHost.Children.Add(_questIdText);
        _idHost.Children.Add(_copyId);

        // 布局：id 行（跨两列）→ 标签行 → 两栏输入框（左右并排，固定高度填满剩余空间，中间竖分割线）
        var grid = _grid = new Grid { Margin = new Thickness(10, 4, 10, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });          // 分割竖线
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var divider = new Border { Background = UiTheme.GridLineBrush, Margin = new Thickness(4, 0, 4, 0) };
        divider.SetValue(Grid.RowProperty, 1);
        Grid.SetRowSpan(divider, 2);
        divider.SetValue(Grid.ColumnProperty, 1);
        grid.Children.Add(divider);

        Place(_idHost, 0, 0, colSpan: 3);
        Place(_mainLabel, 1, 0);
        Place(_altLabel, 1, 2);
        Place(_main, 2, 0, rightMargin: 2);
        Place(_alt, 2, 2, leftMargin: 2);

        Content = grid;

        _main.TextChanged += (_, _) => OnEdited(_main, _doc?.PrimaryLanguage);
        _alt.TextChanged += (_, _) => OnEdited(_alt, _altLang);
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

    private void Place(Control e, int row, int col, int colSpan = 0, int rightMargin = 0, int leftMargin = 0)
    {
        e.SetValue(Grid.RowProperty, row);
        e.SetValue(Grid.ColumnProperty, col);
        if (colSpan > 0) Grid.SetColumnSpan(e, colSpan);
        var m = new Thickness(leftMargin, 0, rightMargin, 0);
        e.Margin = m;
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
            _questIdText.Text = "Quest ID: " + _questId;
            _idHost.IsVisible = true;
        }
        else
        {
            _idHost.IsVisible = false;
        }

        if (doc == null || slot == null)
        {
            IsEnabled = false;
            SetBoxText(_main, "");
            SetBoxText(_alt, "");
            _mainLabel.Text = L.T("任务文件文本");
            _altLabel.Text = L.T("本地化文本（无其它语言块）");
            _building = false;
            return;
        }

        IsEnabled = true;
        _mainLabel.Text = L.T("任务文件文本") + $"（{doc.PrimaryLanguage.Tag.Text}）";
        SetBoxText(_main, TableLayout.Display(GetText(doc.PrimaryLanguage, slot.Value)));

        _altLang = doc.SecondaryLanguage;
        if (_altLang != null)
        {
            _altLabel.Text = L.T("本地化文本") + $"（{_altLang.Tag.Text}）";
            SetBoxText(_alt, TableLayout.Display(GetText(_altLang, slot.Value)));
        }
        else
        {
            _altLabel.Text = L.T("本地化文本（文件内无其它语言块）");
            _alt.Text = "";
        }
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

    private void OnEdited(TextBox box, LanguageBlock? lang)
    {
        if (_building || _doc == null || _slot == null || lang == null) return;
        int s = _slot.Value;
        if (s >= lang.Texts.Count) return;
        lang.Texts[s].SetText(TableLayout.Stored(box.Text ?? ""));
        TextEdited?.Invoke();
    }
}