using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CnvQstEditor;

/// <summary>
/// 应用配置，持久化到 %USERPROFILE%\.CnvQstEditor\CnvQstEditor.json
/// （行业惯用的"用户目录\.软件名\软件名.json"位置；JSONC 格式——文件内带说明注释，手工编辑友好）。
/// </summary>
internal static class AppSettings
{
    private static readonly string UserProfile =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string Dir = Path.Combine(UserProfile, ".CnvQstEditor");
    private static readonly string FilePath = Path.Combine(Dir, "CnvQstEditor.json");
    // 改名前的旧位置：仅首次读取时回退，避免丢失游戏根目录与最近文件
    private static readonly string LegacyFilePath = Path.Combine(UserProfile, ".CnvQstStudio", "CnvQstStudio.json");

    /// <summary>游戏根目录（如 G:\GRIM DAWN）。资源读取基准见 ResolveResource。</summary>
    public static string GameRoot { get; set; } = "";

    /// <summary>DLC1 目录（惯例 &lt;游戏根目录&gt;\GDX1）。文件加载顺序在基础游戏之后。</summary>
    public static string Dlc1Dir { get; set; } = "";

    /// <summary>DLC2 目录（惯例 &lt;游戏根目录&gt;\GDX2）。</summary>
    public static string Dlc2Dir { get; set; } = "";

    /// <summary>DLC3 目录（惯例 &lt;游戏根目录&gt;\GDX3）。</summary>
    public static string Dlc3Dir { get; set; } = "";

    /// <summary>MOD 目录（用户自由设置，最高加载优先级）。</summary>
    public static string ModDir { get; set; } = "";

    /// <summary>把五个目录设置装配成 GdAssets.GdInstall（空串视为未配置）。</summary>
    public static GdAssets.GdInstall Gd => new()
    {
        GameRoot = GameRoot,
        Dlc1 = Dlc1Dir,
        Dlc2 = Dlc2Dir,
        Dlc3 = Dlc3Dir,
        Mod = ModDir,
    };

    /// <summary>
    /// 共享的跨层文件索引缓存：按层根目录缓存枚举结果（默认 3 秒新鲜窗口），
    /// 大目录（20 万文件级）短时间内多次浏览不重复扫描；设置变更后可 Clear()。
    /// </summary>
    public static readonly GdAssets.GdIndexCache GdCache = new();

    /// <summary>最近打开的文件（新的在前，最多 <see cref="MaxRecentFiles"/> 条）。</summary>
    public static List<string> RecentFiles { get; private set; } = new();

    public const int MaxRecentFiles = 10;

    static AppSettings() => Load();

    public static void Load()
    {
        string srcPath = File.Exists(FilePath) ? FilePath : LegacyFilePath;
        if (!File.Exists(srcPath)) return;
        try
        {
            string json = StripJsonc(File.ReadAllText(srcPath));
            var root = JsonNode.Parse(json);
            GameRoot = root?["GameRoot"]?.GetValue<string>() ?? "";
            Dlc1Dir = root?["Dlc1Dir"]?.GetValue<string>() ?? "";
            Dlc2Dir = root?["Dlc2Dir"]?.GetValue<string>() ?? "";
            Dlc3Dir = root?["Dlc3Dir"]?.GetValue<string>() ?? "";
            ModDir = root?["ModDir"]?.GetValue<string>() ?? "";
            RecentFiles = new List<string>();
            if (root?["RecentFiles"] is JsonArray recent)
            {
                foreach (var item in recent)
                {
                    string? path = item?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(path) &&
                        !RecentFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
                        RecentFiles.Add(path);
                }
            }
            if (RecentFiles.Count > MaxRecentFiles)
                RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);

            // 升级迁移：旧版配置缺新键（如 v2.0.13 只有 GameRoot/RecentFiles）时立即补写一次
            // 完整 schema 到新位置——否则升级用户会一直缺 DLC/MOD 目录键，界面上以为没保存过。
            bool missingKeys = root != null &&
                (root["GameRoot"] == null || root["Dlc1Dir"] == null || root["Dlc2Dir"] == null ||
                 root["Dlc3Dir"] == null || root["ModDir"] == null || root["RecentFiles"] == null);
            if (missingKeys || srcPath == LegacyFilePath)
                Save();
        }
        catch
        {
            // 配置损坏：先把原文件留档为 .corrupt（避免随后任何 Save() 覆盖掉现场），再按默认值启动
            try
            {
                if (File.Exists(srcPath))
                    File.Copy(srcPath, srcPath + ".corrupt", overwrite: true);
            }
            catch { }
        }
    }

    /// <summary>
    /// 写盘：手写 JSONC 模板（注释落盘），先写临时文件再原子替换（旧配置保留为 .bak）。
    /// 返回 null = 成功；否则返回错误消息（调用方决定是否提示——静默失败会让用户以为已保存）。
    /// </summary>
    public static string? Save()
    {
        string tmp = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Dir);
            // 手写 JSONC 模板（注释落盘，方便用户手工改）；值用 JsonSerializer 转义
            string esc = JsonSerializer.Serialize(GameRoot);
            string d1 = JsonSerializer.Serialize(Dlc1Dir);
            string d2 = JsonSerializer.Serialize(Dlc2Dir);
            string d3 = JsonSerializer.Serialize(Dlc3Dir);
            string md = JsonSerializer.Serialize(ModDir);
            string recent = JsonSerializer.Serialize(RecentFiles);
            string text = "// CnvQstEditor 配置（JSONC：允许 // 与 /* */ 注释，允许尾逗号）\n" +
                "{\n" +
                "  /* 游戏根目录：用于条件/动作中\"资源路径\"类字段的 打开 / 浏览 功能。\n" +
                "     非 dbr 串（如 quest\\xxx.qst）按 <目录>\\resources\\<值> 读取；\n" +
                "     .dbr 串按 <目录>\\database\\<值> 读取（Grim Dawn 固定规则）。\n" +
                "     文件加载顺序：游戏根目录 → DLC1 → DLC2 → DLC3 → MOD，后者覆盖前者同名文件。 */\n" +
                "  \"GameRoot\": " + esc + ",\n" +
                "  /* DLC1/2/3 目录：惯例为根目录下 GDX1/GDX2/GDX3；每层须含 database 或 resources 之一。 */\n" +
                "  \"Dlc1Dir\": " + d1 + ",\n" +
                "  \"Dlc2Dir\": " + d2 + ",\n" +
                "  \"Dlc3Dir\": " + d3 + ",\n" +
                "  /* MOD 目录：最高加载优先级，自由设置；须含 database 或 resources 之一。 */\n" +
                "  \"ModDir\": " + md + ",\n" +
                "  /* 最近打开的文件：新的在前，最多 10 条。 */\n" +
                "  \"RecentFiles\": " + recent + "\n" +
                "}\n";
            File.WriteAllText(tmp, text);
            if (File.Exists(FilePath))
            {
                string bak = FilePath + ".bak";
                File.Delete(bak);
                File.Replace(tmp, FilePath, bak);   // 原子替换；上一份配置留作 .bak
            }
            else
                File.Move(tmp, FilePath);
            return null;
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return ex.Message;
        }
    }

    /// <summary>记录一次成功打开 / 保存的文件（去重、置顶、截断）。</summary>
    public static void AddRecent(string path)
    {
        try { path = Path.GetFullPath(path); } catch { return; }
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles)
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
        Save();
    }

    public static void RemoveRecent(string path)
    {
        if (RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
            Save();
    }

    public static void ClearRecent()
    {
        if (RecentFiles.Count == 0) return;
        RecentFiles.Clear();
        Save();
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
    /// .dbr → &lt;层&gt;\database\，其余（.qst/.cnv/.lua/.txt…）→ &lt;层&gt;\resources\；
    /// dirOverride（字段字典导向）优先于扩展名规则。
    /// 按 GD 加载覆盖顺序（MOD → DLC3 → DLC2 → DLC1 → 游戏根）取第一个实际存在的文件；
    /// 都不存在时回退到最高可用层的候选路径（保持旧版"只设根目录"的行为）。
    /// 未配置任何目录或值为空返回 null。
    /// </summary>
    public static string? ResolveResource(string rel, string? dirOverride = null)
    {
        rel = rel.Trim();
        if (rel.Length == 0) return null;
        string sub = dirOverride ??
            (rel.EndsWith(".dbr", StringComparison.OrdinalIgnoreCase) ? "database" : "resources");
        string relWin = rel.Replace('/', '\\');

        // 由高到低优先级逐层找实际存在的文件；都不存在时回退到最低层（游戏根）候选，
        // 与旧版"只设根目录"的行为一致
        string? fallback = null;
        foreach (var (_, root) in Gd.ActiveLayers().AsEnumerable().Reverse())
        {
            string cand = Path.GetFullPath(Path.Combine(root, sub, relWin));
            fallback = cand;
            if (File.Exists(cand)) return cand;
        }
        return fallback;
    }

    /// <summary>
    /// 把游戏目录内选中的绝对路径换算回串字段用的相对路径；
    /// 按所有已配置层的 database / resources 目录逐层匹配（反斜杠），都不匹配返回 null。
    /// </summary>
    public static string? ToRelative(string abs)
    {
        if (abs.Length == 0) return null;
        try
        {
            string norm = Path.GetFullPath(abs);
            foreach (var (_, root) in Gd.ActiveLayers())
            {
                foreach (string sub in new[] { "resources", "database" })
                {
                    string baseDir = Path.GetFullPath(Path.Combine(root, sub)) + Path.DirectorySeparatorChar;
                    if (norm.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                        return norm[baseDir.Length..];   // 官方写法用反斜杠（quest\xxx.qst）
                }
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
