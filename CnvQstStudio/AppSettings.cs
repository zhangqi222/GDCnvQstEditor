using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CnvQstStudio;

/// <summary>
/// 应用配置，持久化到 %USERPROFILE%\.CnvQstStudio\CnvQstStudio.json
/// （行业惯用的"用户目录\.软件名\软件名.json"位置；JSONC 格式——文件内带说明注释，手工编辑友好）。
/// </summary>
internal static class AppSettings
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".CnvQstStudio");
    private static readonly string FilePath = Path.Combine(Dir, "CnvQstStudio.json");

    /// <summary>游戏根目录（如 G:\GRIM DAWN）。资源读取基准见 ResolveResource。</summary>
    public static string GameRoot { get; set; } = "";

    static AppSettings() => Load();

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            string json = StripJsonc(File.ReadAllText(FilePath));
            GameRoot = JsonNode.Parse(json)?["GameRoot"]?.GetValue<string>() ?? "";
        }
        catch { /* 配置损坏时忽略，用默认值 */ }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            // 手写 JSONC 模板（注释落盘，方便用户手工改）；值用 JsonSerializer 转义
            string esc = JsonSerializer.Serialize(GameRoot);
            string text = "// CnvQstStudio 配置（JSONC：允许 // 与 /* */ 注释，允许尾逗号）\n" +
                "{\n" +
                "  /* 游戏根目录：用于条件/动作中\"资源路径\"类字段的 打开 / 浏览 功能。\n" +
                "     非 dbr 串（如 quest\\xxx.qst）按 <游戏根目录>\\resources\\<值> 读取；\n" +
                "     .dbr 串按 <游戏根目录>\\database\\<值> 读取（Grim Dawn 固定规则）。 */\n" +
                "  \"GameRoot\": " + esc + "\n" +
                "}\n";
            File.WriteAllText(FilePath, text);
        }
        catch { /* 写失败不影响使用 */ }
    }

    /// <summary>
    /// 条件/动作串字段 → 资源目录字典（键 = 官方类名）。
    /// 依据：遍历 cnv示例 / qst示例 全部 445 个官方样本实测归纳——
    /// 任务引用类串恒为 .qst（resources\），物品/技能记录类串恒为 .dbr（database\）；
    /// Token / 名称 / 数值类串不带路径，不在字典内（不显示浏览按钮）。
    /// </summary>
    public static readonly Dictionary<string, string> FieldDirs = new(StringComparer.Ordinal)
    {
        // .qst 任务文件 → resources\
        ["OnQuest"] = "resources",
        ["QuestTaskComplete"] = "resources",
        ["OnQuestTask"] = "resources",
        ["HasQuestObjective"] = "resources",
        ["QuestBlocked"] = "resources",
        ["BeginQuestTask"] = "resources",
        ["CompleteQuest"] = "resources",
        ["CompleteQuestTask"] = "resources",
        // .dbr 记录文件 → database\
        ["HasItem"] = "database",
        ["GiveItem"] = "database",
        ["GiveRandomItem"] = "database",
        ["CastSkill"] = "database",
        // 音效/视频文件 → resources\（官方样本：sound\...\*.wav、videos\*.vid）
        ["PlaySound"] = "resources",
        ["PlayVideo"] = "resources",
    };

    /// <summary>字段类型约定扩展名（浏览对话框的文件类型过滤；null 时按导向目录默认规则）。
    /// 依据：官方样本实测（PlaySound 串值形如 sound\dialog\quest\xxx.wav；PlayVideo 形如 videos\xxx.vid）。</summary>
    public static readonly Dictionary<string, string[]> FieldExts = new(StringComparer.Ordinal)
    {
        ["PlaySound"] = new[] { "wav", "ogg" },
        ["PlayVideo"] = new[] { "vid", "bik" },
    };

    /// <summary>字典 + 扩展名规则的综合判断：该类名的串字段应导向的资源子目录；null = 非文件类（不导向）。</summary>
    public static string? FieldDir(string? className) =>
        className != null && FieldDirs.TryGetValue(className, out string? dir) ? dir : null;

    /// <summary>
    /// 把串字段里的资源相对路径换算成绝对路径：
    /// .dbr → <GameRoot>\database\，其余（.qst/.cnv/.lua/.txt…）→ <GameRoot>\resources\；
    /// dirOverride（字段字典导向）优先于扩展名规则。
    /// 未设置根目录或值为空返回 null。
    /// </summary>
    public static string? ResolveResource(string rel, string? dirOverride = null)
    {
        rel = rel.Trim();
        if (GameRoot.Length == 0 || rel.Length == 0) return null;
        string baseDir = Path.Combine(GameRoot,
            dirOverride ?? (rel.EndsWith(".dbr", StringComparison.OrdinalIgnoreCase) ? "database" : "resources"));
        return Path.GetFullPath(Path.Combine(baseDir, rel.Replace('/', '\\')));
    }

    /// <summary>把游戏目录内选中的绝对路径换算回串字段用的相对路径；不在 resources/database 下返回 null。</summary>
    public static string? ToRelative(string abs)
    {
        if (GameRoot.Length == 0 || abs.Length == 0) return null;
        try
        {
            string norm = Path.GetFullPath(abs);
            foreach (string sub in new[] { "resources", "database" })
            {
                string baseDir = Path.GetFullPath(Path.Combine(GameRoot, sub)) + Path.DirectorySeparatorChar;
                if (norm.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                    return norm[baseDir.Length..];   // 官方写法用反斜杠（quest\xxx.qst）
            }
        }
        catch { }
        return null;
    }

    /// <summary>用系统默认程序打开文件（dbr/qst 均为文本）。</summary>
    public static void OpenInShell(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    /// <summary>剥离 JSONC 的注释（字符串感知）与尾逗号，转成严格 JSON。</summary>
    private static string StripJsonc(string src)
    {
        var sb = new StringBuilder(src.Length);
        bool inStr = false, inLine = false, inBlock = false;
        for (int i = 0; i < src.Length; i++)
        {
            char c = src[i];
            char n = i + 1 < src.Length ? src[i + 1] : '\0';
            if (inLine) { if (c == '\n') { inLine = false; sb.Append(c); } continue; }
            if (inBlock) { if (c == '*' && n == '/') { inBlock = false; i++; } continue; }
            if (inStr)
            {
                sb.Append(c);
                if (c == '\\' && n != '\0') { sb.Append(n); i++; }
                else if (c == '"') inStr = false;
                continue;
            }
            if (c == '"') { inStr = true; sb.Append(c); continue; }
            if (c == '/' && n == '/') { inLine = true; i++; continue; }
            if (c == '/' && n == '*') { inBlock = true; i++; continue; }
            sb.Append(c);
        }
        // 去尾逗号：", }" / ", ]" → "}" / "]"
        return Regex.Replace(sb.ToString(), @",(\s*[}\]])", "$1");
    }
}
