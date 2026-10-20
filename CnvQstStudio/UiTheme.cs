using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CnvQstStudio;

/// <summary>
/// Semi.Avalonia 主题引导：挂载 Semi 控件主题、DataGrid 主题与 TreeDataGrid 主题，
/// 并暴露少量语义色刷（沿用旧版配色，保证视觉延续）。
/// </summary>
internal static class UiTheme
{
    // 主题样式已在 App.axaml 挂载（Semi 控件 / DataGrid / TreeDataGrid）。

    // ---- 主按钮：Semi accent 样式 ----
    public static void MakePrimary(Button button) => button.Classes.Add("accent");

    // ---- 语义色刷 ----
    public static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x24, 0x30));
    public static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
    public static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xED));
    public static readonly IBrush WarnBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09));
    public static readonly IBrush OkBrush = new SolidColorBrush(Color.FromRgb(0x0E, 0x7A, 0x4F));
    public static readonly IBrush DangerBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0x23, 0x18));
    public static readonly IBrush UnknownBrush = Brushes.Gray;                       // 未命名的条件/动作
    public static readonly IBrush InferredBrush = new SolidColorBrush(Color.FromRgb(230, 126, 34)); // 推断语义

    public static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");

    /// <summary>表格网格线（横竖分隔）。</summary>
    public static readonly IBrush GridLineBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));

    /// <summary>选中行 / 子树高亮底色（Indigo 浅底，与旧版一致）。</summary>
    public static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(38, 0x3F, 0x51, 0xB5));
}