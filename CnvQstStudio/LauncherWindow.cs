using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CnvQst;

namespace CnvQstStudio;

/// <summary>
/// 启动 UI：仅两个入口 —— 编辑器、批量转换。选择后本窗口关闭、进入对应功能。
/// </summary>
internal sealed class LauncherWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly TextBlock _title = new()
    {
        Text = L.WindowTitle,
        FontSize = 20,
        FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 8, 0, 2),
    };
    private readonly TextBlock _subtitle = new()
    {
        Text = L.T("选择要启动的功能"),
        Foreground = WpfTheme.MutedBrush,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 0, 0, 20),
    };
    private readonly System.Windows.Controls.Button _editorBtn;
    private readonly System.Windows.Controls.Button _batchBtn;
    private readonly Wpf.Ui.Controls.TitleBar _titleBar;

    public LauncherWindow()
    {
        Title = L.WindowTitle;
        Width = 440;
        Height = 320;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        ExtendsContentIntoTitleBar = true;
        _titleBar = new Wpf.Ui.Controls.TitleBar { Title = L.WindowTitle };

        _editorBtn = BigButton(L.T("编辑器"), L.T("打开 / 查看 / 编辑 / 新建对话与任务"), OpenEditor);
        _batchBtn = BigButton(L.T("批量转换"), L.T("目录批量导出、校验与 TXT 回填"), OpenBatch);

        var langRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0),
        };
        langRow.Children.Add(new TextBlock { Text = L.Language + "：", VerticalAlignment = VerticalAlignment.Center, Foreground = WpfTheme.MutedBrush });
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

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_titleBar, 0);
        Grid.SetRow(stack, 1);
        root.Children.Add(_titleBar);
        root.Children.Add(stack);
        Content = root;

        L.Changed += OnLanguageChanged;
        Closed += (_, _) => L.Changed -= OnLanguageChanged;
    }

    private static System.Windows.Controls.Button BigButton(string title, string sub, Action onClick)
    {
        var content = new StackPanel { Margin = new Thickness(6, 8, 6, 8) };
        content.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = WpfTheme.MutedBrush, Margin = new Thickness(0, 2, 0, 0) });
        var b = new System.Windows.Controls.Button { Content = content, Height = 64, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 12) };
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
        _titleBar.Title = L.WindowTitle;
        _title.Text = L.WindowTitle;
        _subtitle.Text = L.T("选择要启动的功能");
    }
}
