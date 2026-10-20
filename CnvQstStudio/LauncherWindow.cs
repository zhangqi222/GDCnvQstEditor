using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CnvQst;

namespace CnvQstStudio;

/// <summary>
/// 启动 UI：仅两个入口 —— 编辑器、批量转换。选择后本窗口关闭、进入对应功能。
/// </summary>
internal sealed class LauncherWindow : Window
{
    private readonly TextBlock _title = new()
    {
        Text = L.WindowTitle,
        FontSize = 20,
        FontWeight = FontWeight.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 8, 0, 2),
    };
    private readonly TextBlock _subtitle = new()
    {
        Text = L.T("选择要启动的功能"),
        Foreground = UiTheme.MutedBrush,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 0, 0, 20),
    };
    private readonly Button _editorBtn;
    private readonly Button _batchBtn;

    public LauncherWindow()
    {
        Title = L.WindowTitle;
        Width = 440;
        Height = 320;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        _editorBtn = BigButton(L.T("编辑器"), L.T("打开 / 查看 / 编辑 / 新建对话与任务"), OpenEditor);
        _batchBtn = BigButton(L.T("批量转换"), L.T("目录批量导出、校验与 TXT 回填"), OpenBatch);

        var langRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0),
        };
        langRow.Children.Add(new TextBlock { Text = L.Language + "：", VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.MutedBrush });
        var lang = new ComboBox { MinWidth = 140 };
        lang.ItemsSource = L.Languages.Select(l => l.Name).ToList();
        lang.SelectedIndex = L.Languages.ToList().FindIndex(l => l.Code == L.Code);
        lang.SelectionChanged += (_, _) =>
        {
            int i = lang.SelectedIndex;
            if (i >= 0 && i < L.Languages.Count) L.Use(L.Languages[i].Code);
        };
        langRow.Children.Add(lang);

        var stack = new StackPanel { Margin = new Thickness(28, 4, 28, 20) };
        stack.Children.Add(_title);
        stack.Children.Add(_subtitle);
        stack.Children.Add(_editorBtn);
        stack.Children.Add(_batchBtn);
        stack.Children.Add(langRow);

        Content = stack;

        L.Changed += OnLanguageChanged;
        Closed += (_, _) => L.Changed -= OnLanguageChanged;
    }

    private static Button BigButton(string title, string sub, Action onClick)
    {
        var content = new StackPanel { Margin = new Thickness(6, 8, 6, 8) };
        content.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = UiTheme.MutedBrush, Margin = new Thickness(0, 2, 0, 0) });
        var b = new Button { Content = content, Height = 64, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 12) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void OpenEditor()
    {
        new MainWindow().Show();
        Close();
    }

    private void OpenBatch()
    {
        new BatchWindow().Show();
        Close();
    }

    private void OnLanguageChanged()
    {
        Title = L.WindowTitle;
        _title.Text = L.WindowTitle;
        _subtitle.Text = L.T("选择要启动的功能");
    }
}