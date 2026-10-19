using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace CnvQstStudio;

/// <summary>
/// WPF-UI 主题引导：合并 Fluent 控件样式与浅色主题字典，并暴露少量语义色刷
/// （条件/动作列分色、状态行着色等 WPF-UI 没有直接给出的部分）。
/// </summary>
internal static class WpfTheme
{
    /// <summary>应用启动（含 --smoke）时调用一次。</summary>
    public static void Init(Application app)
    {
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Wpf.Ui;component/Resources/Theme/Light.xaml", UriKind.Absolute),
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Wpf.Ui;component/Resources/Wpf.Ui.xaml", UriKind.Absolute),
        });
        ApplicationThemeManager.Apply(ApplicationTheme.Light);
    }

    public static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x24, 0x30));
    public static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
    public static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xED));
    public static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09));
    public static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x0E, 0x7A, 0x4F));
    public static readonly Brush DangerBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0x23, 0x18));
    public static readonly Brush UnknownBrush = Brushes.Gray;                       // 未命名的条件/动作
    public static readonly Brush InferredBrush = new SolidColorBrush(Color.FromRgb(230, 126, 34)); // 推断语义

    public static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");
}
