using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit.Highlighting;

namespace CnvQstEditor;

/// <summary>
/// 主题中心：Semi.Avalonia 主题引导 + 语义色刷 + 明/暗模式切换（跟随系统 / 明亮 / 暗色）。
/// 所有自定义色值均取 Semi 色板相近值（对应 Semi.Avalonia Tokens/Palette/Light|Dark.axaml），
/// 明暗两套由 <see cref="Refresh"/> 统一换色：刷实例保持不变、仅改 Color，引用处随主题自动重绘；
/// 自绘表面（margin / TextView）通过 <see cref="ThemeChanged"/> 通知失效重绘。
/// </summary>
internal static class UiTheme
{
    /// <summary>主题模式：system=跟随系统 / light / dark（配置持久化键值）。</summary>
    public const string ThemeSystem = "system";
    public const string ThemeLight = "light";
    public const string ThemeDark = "dark";

    /// <summary>当前生效的主题模式（system/light/dark）。</summary>
    public static string Mode { get; private set; } = ThemeSystem;

    /// <summary>主题切换后通知（自绘表面失效重绘用；刷实例自身的变化由 Avalonia 自动重绘）。</summary>
    public static event Action? ThemeChanged;

    // ---- 默认色值（亮色 = Semi Light 色板；静态字段按声明顺序初始化，必须位于刷子字段之前） ----
    private static readonly Color defaultText = Color.FromRgb(0x1C, 0x1F, 0x23);      // SemiGrey9 = SemiColorText0
    private static readonly Color defaultMuted = Color.FromRgb(0x6B, 0x70, 0x75);     // SemiGrey5
    private static readonly Color defaultAccent = Color.FromRgb(0x00, 0x64, 0xFA);    // SemiBlue5
    private static readonly Color defaultOk = Color.FromRgb(0x30, 0x95, 0x3B);        // SemiGreen6
    private static readonly Color defaultWarn = Color.FromRgb(0xD2, 0x67, 0x00);      // SemiOrange6
    private static readonly Color defaultDanger = Color.FromRgb(0xD5, 0x25, 0x15);    // SemiRed6
    private static readonly Color defaultUnknown = Color.FromRgb(0x88, 0x8D, 0x92);   // SemiGrey4
    private static readonly Color defaultInferred = Color.FromRgb(0xFC, 0x88, 0x00);  // SemiOrange5
    private static readonly Color defaultGridLine = Color.FromRgb(0xE6, 0xE8, 0xEA);  // SemiGrey1
    private static readonly Color defaultBannerBlue = Color.FromRgb(0xEA, 0xF5, 0xFF);  // SemiBlue0
    private static readonly Color defaultBannerGreen = Color.FromRgb(0xEC, 0xF7, 0xEC); // SemiGreen0
    private static readonly Color defaultRowNumber = Color.FromRgb(0x88, 0x8D, 0x92); // SemiGrey4
    private static readonly Color defaultRowDesc = Color.FromRgb(0x6B, 0x70, 0x75);   // SemiGrey5
    private static readonly Color defaultRowAdvice = Color.FromRgb(0xD2, 0x67, 0x00); // SemiOrange6
    private static readonly Color defaultEditor = Color.FromRgb(0xF9, 0xF9, 0xF9);    // SemiGrey0
    private static readonly Color defaultZebra = Color.FromRgb(0xFF, 0xF8, 0xEA);     // SemiOrange0

    // ---- 主按钮：Semi accent 样式 ----
    public static void MakePrimary(Button button) => button.Classes.Add("accent");

    // ---- 语义色刷（亮色取 6 档保证白底对比，暗色取 5 档保证深底亮度；见 Refresh） ----
    public static readonly SolidColorBrush TextBrush = new(defaultText);
    public static readonly SolidColorBrush MutedBrush = new(defaultMuted);
    public static readonly SolidColorBrush AccentBrush = new(defaultAccent);        // SemiBlue5
    /// <summary>Semi secondary 色（SemiColorSecondary = LightBlue5）。</summary>
    public static readonly SolidColorBrush SecondaryBrush = new(Color.FromRgb(0x00, 0x95, 0xEE));
    public static readonly SolidColorBrush WarnBrush = new(defaultWarn);
    public static readonly SolidColorBrush OkBrush = new(defaultOk);
    public static readonly SolidColorBrush DangerBrush = new(defaultDanger);
    public static readonly SolidColorBrush UnknownBrush = new(defaultUnknown);      // 未命名的条件/动作
    public static readonly SolidColorBrush InferredBrush = new(defaultInferred);    // 推断语义

    public static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");

    /// <summary>表格网格线（横竖分隔）。</summary>
    public static readonly SolidColorBrush GridLineBrush = new(defaultGridLine);

    /// <summary>选中行 / 子树高亮底色（SemiIndigo5 #3F51B5 半透明，明暗通用）。</summary>
    public static readonly SolidColorBrush HighlightBrush =
        new(Color.FromArgb(38, 0x3F, 0x51, 0xB5));

    // ---- 条件/动作横幅底色（明：SemiBlue0/Green0；暗：SemiBlue9/Green9） ----
    public static readonly SolidColorBrush BannerBlueBrush = new(defaultBannerBlue);
    public static readonly SolidColorBrush BannerGreenBrush = new(defaultBannerGreen);

    // ---- 行号列三色（RowMargin：行号 / 节点描述 / 槽位建议） ----
    public static readonly SolidColorBrush RowNumberBrush = new(defaultRowNumber);
    public static readonly SolidColorBrush RowDescBrush = new(defaultRowDesc);
    public static readonly SolidColorBrush RowAdviceBrush = new(defaultRowAdvice);

    // ---- 编辑器底色：与窗口背景接近但不同（窗口背景在 App.axaml 的 UiWindowBackground，两处色值需同步） ----
    /// <summary>编辑器底色（奇数行 / 空白区；亮=SemiGrey0 #F9F9F9，暗=SemiBackground1 #232429）。</summary>
    public static readonly SolidColorBrush EditorBrush = new(defaultEditor);

    /// <summary>偶数行交叉着色底色（亮=SemiOrange0 #FFF8EA，暗=介于 Background1/2 的相近值）。</summary>
    public static readonly SolidColorBrush ZebraBrush = new(defaultZebra);

    /// <summary>按 key 查 Semi 主题里的 ControlTheme（如 "TagLabel"、"ButtonRadioButton"）；未命中返回 null。</summary>
    public static ControlTheme? FindTheme(string key) =>
        Application.Current?.TryFindResource(key, out object? value) == true ? value as ControlTheme : null;

    /// <summary>启动时应用主题：按配置模式设置 Application 主题变体并刷新语义刷（GUI 与冒烟共用）。</summary>
    public static void ApplyStartup()
    {
        Apply(AppSettings.ThemeMode);
        // 跟随系统：系统明暗切换时自动重新换色（Application 变体变化事件）
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += (_, _) => { if (Mode == ThemeSystem) Refresh(app.ActualThemeVariant); };
    }

    /// <summary>应用主题模式（system/light/dark）：设置变体 + 立即刷新；设置弹窗实时预览也走这里。</summary>
    public static void Apply(string mode)
    {
        Mode = mode is ThemeLight or ThemeDark ? mode : ThemeSystem;
        if (Application.Current is { } app)
            app.RequestedThemeVariant = Mode switch
            {
                ThemeLight => ThemeVariant.Light,
                ThemeDark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        Refresh(Application.Current?.ActualThemeVariant ?? ThemeVariant.Light);
    }

    /// <summary>按当前变体统一换色（刷实例不变、仅改 Color）+ 通知自绘表面。</summary>
    public static void Refresh(ThemeVariant variant)
    {
        bool dark = variant == ThemeVariant.Dark;
        // 文本/中性色：亮 = Semi Light 色板，暗 = Semi Dark 色板（灰阶反向）
        Set(TextBrush, dark ? "#F9F9F9" : "#1C1F23");          // Grey9（两板同名档）
        Set(MutedBrush, dark ? "#A7ABB0" : "#6B7075");         // Grey6 / Grey5
        Set(AccentBrush, "#0064FA");                            // SemiBlue5（明暗同档）
        Set(SecondaryBrush, "#0095EE");                         // LightBlue5（明暗同档）
        Set(OkBrush, dark ? "#3BB346" : "#30953B");             // Green5 / Green6
        Set(WarnBrush, dark ? "#FC8800" : "#D26700");           // Orange5 / Orange6
        Set(DangerBrush, dark ? "#F93920" : "#D52515");         // Red5 / Red6
        Set(UnknownBrush, "#888D92");                           // Grey4（明暗同档）
        Set(InferredBrush, dark ? "#FDA633" : "#FC8800");       // Orange4 / Orange5
        Set(GridLineBrush, dark ? "#41464C" : "#E6E8EA");       // Grey2 / Grey1
        Set(BannerBlueBrush, dark ? "#002C6B" : "#EAF5FF");     // Blue9 / Blue0
        Set(BannerGreenBrush, dark ? "#113C18" : "#ECF7EC");    // Green9 / Green0
        Set(RowNumberBrush, dark ? "#6B7075" : "#888D92");      // Grey4（明暗同值）
        Set(RowDescBrush, dark ? "#A7ABB0" : "#6B7075");        // Grey6 / Grey5
        Set(RowAdviceBrush, dark ? "#FC8800" : "#D26700");      // Orange5 / Orange6
        Set(EditorBrush, dark ? "#232429" : "#F9F9F9");         // Background1 / Grey0
        Set(ZebraBrush, dark ? "#2B2C31" : "#FFF8EA");          // Background1~2 相近值 / Orange0
        RefreshJsonHighlightingColors(dark);
        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// AvaloniaEdit 内置 JSON 语法高亮（Json.xshd）配色同步到 Semi 语义色：
    /// 原定义的 Black/Olive 等固定色在暗色下不可读，这里按主题覆盖命名色。
    /// </summary>
    private static void RefreshJsonHighlightingColors(bool dark)
    {
        var def = HighlightingManager.Instance.GetDefinitionByExtension(".json");
        if (def == null) return;
        def.GetNamedColor("Punctuation").Foreground = new SimpleHighlightingBrush(TextBrush.Color);
        def.GetNamedColor("FieldName").Foreground = new SimpleHighlightingBrush(AccentBrush.Color);
        def.GetNamedColor("String").Foreground = new SimpleHighlightingBrush(OkBrush.Color);
        def.GetNamedColor("Number").Foreground = new SimpleHighlightingBrush(WarnBrush.Color);
        def.GetNamedColor("Bool").Foreground = new SimpleHighlightingBrush(DangerBrush.Color);
        def.GetNamedColor("Null").Foreground = new SimpleHighlightingBrush(DangerBrush.Color);
    }

    /// <summary>解析 #RRGGBB 并设置刷色（集中换色用）。</summary>
    private static void Set(SolidColorBrush brush, string hex) => brush.Color = Color.Parse(hex);
}
