using System.Text;
using System.Text.Json;

namespace GDCnvQstLib.Cli;

/// <summary>
/// CLI 轻量多语言：以中文原文为 key。
/// · 中文（cn/zh/zh-CN）恒等返回；英文（en/en-US）查内嵌词典 cli_en.json（{"中文原文":"English"}），
///   缺 key 回退中文原文，词典加载失败时同样恒等回退。
/// · 语言决策顺序：--language 参数（值 / =值 两种形式，大小写不敏感）→
///   %APPDATA%\GDCnvQstEditor\lang.txt（与 GUI 共用，GUI 存语言 code 如 zh-CN/en）→ 默认 cn。
/// · 显式传了合法 --language 时写回 lang.txt（一行文本，UTF-8 无 BOM），GUI 下次启动生效；写失败静默忽略。
/// </summary>
internal static class CliL
{
    private static int _lang;                          // 0=中文（默认/恒等） 1=英文
    private static Dictionary<string, string>? _en;    // 英文词典；null=未加载或加载失败（恒等回退）
    private static bool _initialized;                  // dev 嵌套会重入 Run()，只初始化一次

    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GDCnvQstEditor");
    private static readonly string ConfigFile = Path.Combine(ConfigDir, "lang.txt");
    // GUI 改名前的旧语言选择：新位置不存在时回退（与 GDCnvQstEditor 的 I18n 一致）
    private static readonly string LegacyConfigFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CnvQstStudio", "lang.txt");

    /// <summary>当前是否英文输出。</summary>
    public static bool IsEnglish => _lang == 1;

    /// <summary>
    /// Run() 最先调用：扫描并剥离 args 中的 --language（"--language cn" 与 "--language=cn" 两种形式，
    /// 大小写不敏感；非法值忽略并回退配置文件），决定语言并按需写回配置文件，返回剥离后的剩余 args。
    /// </summary>
    public static string[] Initialize(string[] args)
    {
        if (_initialized) return args;                 // dev 嵌套再次进入 Run() 时不再处理
        _initialized = true;

        string? explicitCode = null;                   // 显式指定的合法 code（zh-CN / en）
        var rest = new List<string>(args.Length);
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            bool matched = false;
            string? value = null;
            if (a.Equals("--language", StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
                if (i + 1 < args.Length) value = args[++i];   // 消费 "--language cn" 的值
            }
            else if (a.StartsWith("--language=", StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
                value = a["--language=".Length..];
            }
            if (!matched) { rest.Add(a); continue; }          // 普通参数原样保留
            string? code = Normalize(value ?? "");
            if (code != null) explicitCode = code;            // 非法值忽略，回退配置文件
        }

        if (explicitCode != null)
        {
            SetLanguage(explicitCode);
            WriteConfig(explicitCode);                        // 显式指定 → 写回，GUI 下次启动生效
        }
        else
        {
            SetLanguage(ReadConfig() ?? "zh-CN");             // 无参数读配置文件；都没有默认 cn
        }
        return rest.ToArray();
    }

    // ---------------- 翻译 API ----------------

    /// <summary>按当前语言翻译固定文案（key=中文原文；英文缺译文时回退原文）。</summary>
    public static string T(string zh) =>
        _lang == 1 && _en != null && _en.TryGetValue(zh, out string? en) ? en : zh;

    /// <summary>带参数翻译：key 为代码里传给 F 的中文模板（含 {0}/{1} 占位符）。</summary>
    public static string F(string zh, params object?[] args) => string.Format(T(zh), args);

    // ---------------- 帮助大段（中文原文保留在 CliApp 常量里，英文整段替换，不逐行作 key） ----------------

    /// <summary>Usage 大段（随语言切换）。</summary>
    public static string UsageText => IsEnglish ? UsageEn : CliApp.Usage;

    /// <summary>help convert 大段（随语言切换）。</summary>
    public static string ConvertHelpText => IsEnglish ? ConvertHelpEn : CliApp.ConvertHelp;

    /// <summary>help dev 大段（随语言切换）。</summary>
    public static string DevUsageText => IsEnglish ? DevUsageEn : CliApp.DevUsage;

    // ---------------- 内部实现 ----------------

    /// <summary>归一化语言值：cn/zh/zh-CN → "zh-CN"；en/en-US → "en"；其他 → null（非法）。</summary>
    private static string? Normalize(string value)
    {
        string v = value.Trim().Replace('-', '_').ToLowerInvariant();
        return v switch
        {
            "cn" or "zh" or "zh_cn" => "zh-CN",
            "en" or "en_us" => "en",
            _ => null,
        };
    }

    private static void SetLanguage(string code)
    {
        _lang = code == "en" ? 1 : 0;
        _en = _lang == 1 ? LoadEnDict() : null;
    }

    /// <summary>读 %APPDATA%\GDCnvQstEditor\lang.txt（GUI 共用；新位置不存在时回退旧位置）。</summary>
    private static string? ReadConfig()
    {
        try
        {
            string path = File.Exists(ConfigFile) ? ConfigFile : LegacyConfigFile;
            if (!File.Exists(path)) return null;
            return Normalize(File.ReadAllText(path));
        }
        catch { return null; }                         // 读取失败按默认处理
    }

    /// <summary>写回语言 code（一行文本，UTF-8 无 BOM）；失败静默忽略。</summary>
    private static void WriteConfig(string code)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigFile, code, new UTF8Encoding(false));
        }
        catch { /* 只读盘符等场景静默忽略 */ }
    }

    /// <summary>从程序集内嵌资源加载英文词典（资源名以 cli_en.json 结尾即可，不依赖精确名）。</summary>
    private static Dictionary<string, string>? LoadEnDict()
    {
        try
        {
            var asm = typeof(CliL).Assembly;
            string? name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("cli_en.json", StringComparison.OrdinalIgnoreCase));
            if (name == null) return null;
            using Stream stream = asm.GetManifestResourceStream(name)!;
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            var dict = new Dictionary<string, string>();
            foreach (var p in doc.RootElement.EnumerateObject())
                dict[p.Name] = p.Value.GetString() ?? p.Name;
            return dict;
        }
        catch { return null; }                         // 词典缺失/损坏 → 恒等回退中文
    }

    // ---------------- 英文帮助大段（与中文版内容信息等价） ----------------

    private const string UsageEn = """
        Usage: GDCnvQstEditor-cli <command> [args] (same commands when launching GDCnvQstEditor with args)
        Common commands:
          view      <file.cnv|qst>                 Inspect: show conversation/quest content as a text tree
          convert   <file> [-o output]             Convert: .json → QST/CNV, or QST/CNV → .json
                                                   (omit -o to write next to the input; see "help convert")
          verify    <file|dir>                     Validate: structure / languages alignment / byte-identical rewrite (run this first)
                                                   (a directory also checks .cnv, .qst and .json)
          txt       <file|dir> [-o dir] [--flat]   Export official localization TXT (for translation/localization)
          sample    cnv|qst [-o output]            Extract the built-in full samples (cnv_full.json / qst_full.json, with hints, ready for convert)
          help      [command]                      Show help; "help dev" lists developer tools
          dev       <subcommand>                   Development/analysis tools (most users can ignore)
        Global option: --language cn|en            Output language: cn Chinese (default), en English; an explicit value is written to %APPDATA%\GDCnvQstEditor\lang.txt and shared with the GUI
        Compatibility aliases: dump=view; json, make=convert (old scripts keep working).
        Launching GDCnvQstEditor with no args opens the GUI; GDCnvQstEditor-cli with no args shows this help.
        """;

    private const string ConvertHelpEn = """
        Usage: convert <file> [-o output]
          Input .cnv / .qst: export the full-fidelity JSON model; edit it, then convert back.
          Input .json     : a .cnv or .qst is generated automatically based on "format" (hand-written spec)
                            or "kind" (full-fidelity model) declared in the JSON. Hand-written JSON must follow
                            the spec — see JSON转CNV.md / JSON转QST.md; templates: the sample command.
          Omitting -o   : output is written next to the input file with the same name and target extension.
          Direction is decided by the input extension; the output format is declared inside the JSON — no --format needed.
          JSON is validated before conversion: every language block must align with nodes/text slots;
          on mismatch, missing/extra lines and fillable slots are listed, and no file is written.
        Compatibility aliases: json, make.
        """;

    private const string DevUsageEn = """
        Development/analysis tools (most users can ignore):
          dev anchors   <file>                       Anchoring workbench for condition/action type IDs
          dev census    <dir>                        Type census over samples (naming evidence)
          dev vocab                                  Official editor options ↔ type ID reference table
          dev probe     cond|act|uid|sub|suball|flags|qst|all [-o dir] [--type N] [--sub N] [--split]
                                                    Batch-generate probe files
          dev selftest  <dir>                        Structure-edit self-test (add/remove/move round-trip, three checks)
          dev hash      <text>                       FNV1 hash (conversation id / Copy Id)
          dev legacytxt <file>                       v0.9 TXT algorithm baseline (for comparison)
          dev txt-diff  <dir>                        New TXT export vs v0.9 baseline, full comparison
        """;
}
