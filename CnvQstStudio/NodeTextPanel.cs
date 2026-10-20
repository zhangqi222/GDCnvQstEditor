using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CnvQst;

namespace CnvQstStudio;

/// <summary>
/// 底部节点文本编辑器（官方编辑器风格：选中行后底部编辑，替代弹窗）：
/// 第一栏 = 任务文件主语言文本；第二栏 = 文件内其它语言块的同一槽位（双语编辑）。
/// </summary>
internal sealed class NodeTextPanel : UserControl
{
    private readonly TextBlock _questIdText = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.MutedBrush };
    private readonly Button _copyId = new() { Content = L.Copy, MinWidth = 56, MinHeight = 22, Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBox _main = NewBox();
    private readonly TextBox _alt = NewBox();
    private readonly TextBlock _mainLabel = MkLabel("");
    private readonly TextBlock _altLabel = MkLabel("");
    private DocSession? _doc;
    private int? _slot;
    private LanguageBlock? _altLang;
    private bool _building;

    /// <summary>文本被编辑（调用方刷新脏标记/状态）。</summary>
    public event Action? TextEdited;

    public NodeTextPanel()
    {
        _copyId.Click += async (_, _) =>
        {
            string id = _questIdText.Text?.Replace("Quest ID: ", "") ?? "";
            var tl = TopLevel.GetTopLevel(this);
            if (tl?.Clipboard != null) { try { await tl.Clipboard.SetTextAsync(id); } catch { } }
        };

        var idRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        idRow.Children.Add(_questIdText);
        idRow.Children.Add(_copyId);

        var grid = _grid = new Grid { Margin = new Thickness(10, 4, 10, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Place(_mainLabel, 0, 0);
        Place(_main, 0, 1);
        Place(_altLabel, 1, 0);
        Place(_alt, 1, 1);
        var idHost = new StackPanel { Orientation = Orientation.Horizontal };
        idHost.Children.Add(idRow);
        Place(idHost, 2, 1);

        var root = new StackPanel();
        root.Children.Add(grid);
        Content = root;

        _main.TextChanged += (_, _) => OnEdited(_main, _doc?.PrimaryLanguage);
        _alt.TextChanged += (_, _) => OnEdited(_alt, _altLang);
    }

    private static TextBox NewBox() => new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 48,
        MaxHeight = 96,
    };

    private static TextBlock MkLabel(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) };

    private Grid _grid = null!;

    private void Place(Control e, int row, int col)
    {
        e.SetValue(Grid.RowProperty, row);
        e.SetValue(Grid.ColumnProperty, col);
        if (e is TextBox) e.Margin = new Thickness(0, 2, 0, 2);
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
            _questIdText.Text = "Quest ID: " + doc.Qst.Id;
            _questIdText.IsVisible = true;
            _copyId.IsVisible = true;
        }
        else
        {
            _questIdText.IsVisible = false;
            _copyId.IsVisible = false;
        }

        if (doc == null || slot == null)
        {
            IsEnabled = false;
            _main.Text = "";
            _alt.Text = "";
            _mainLabel.Text = L.T("任务文件文本");
            _altLabel.Text = L.T("本地化文本（无其它语言块）");
            _building = false;
            return;
        }

        IsEnabled = true;
        _mainLabel.Text = L.T("任务文件文本") + $"（{doc.PrimaryLanguage.Tag.Text}）";
        _main.Text = TableLayout.Display(GetText(doc.PrimaryLanguage, slot.Value));

        _altLang = doc.SecondaryLanguage;
        if (_altLang != null)
        {
            _altLabel.Text = L.T("本地化文本") + $"（{_altLang.Tag.Text}）";
            _alt.Text = TableLayout.Display(GetText(_altLang, slot.Value));
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

    private void OnEdited(TextBox box, LanguageBlock? lang)
    {
        if (_building || _doc == null || _slot == null || lang == null) return;
        int s = _slot.Value;
        if (s >= lang.Texts.Count) return;
        lang.Texts[s].SetText(TableLayout.Stored(box.Text ?? ""));
        TextEdited?.Invoke();
    }
}