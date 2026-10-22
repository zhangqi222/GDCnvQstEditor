using System.Text;
using System.Text.Json;

namespace CnvQstEditor;

/// <summary>一种可用语言（来自 lang/*.json）。</summary>
internal sealed class LangInfo
{
    public string Code = "zh-CN";
    public string Name = "简体中文";
    public string Path = "";
    /// <summary>中文原文 → 该语言译文。基线语言（zh-CN）为空表。</summary>
    public Dictionary<string, string> Strings = new();
    /// <summary>条件/动作等"类型"的显示名：官方英文名 → 该语言名。</summary>
    public Dictionary<string, string> Types = new();
}

/// <summary>
/// UI 本地化。**以中文为基准（key = 中文原文）**，语言包是 exe 旁 `lang/*.json`，放进去即生效：
///
/// {
///   "code": "en",
///   "name": "English",
///   "strings": { "打开": "Open", "已保存 {0}": "Saved {0}" },
///   "types":   { "HasFaction": "Faction reputation" }
/// }
///
/// · 缺 key 时回退中文原文，因此语言包可以逐步补全，不会出现空标签；
/// · 新增语言只需复制一份 json 改 code/name/strings；中文包（zh-CN）作为键清单模板；
/// · 选择持久化到 %APPDATA%\CnvQstEditor\lang.txt（存语言 code）。
/// </summary>
internal static class L
{
    public const string BaselineCode = "zh-CN";

    private static readonly string AppData =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private static readonly string ConfigDir = Path.Combine(AppData, "CnvQstEditor");

    private static readonly string ConfigFile = Path.Combine(ConfigDir, "lang.txt");

    // 改名前的旧语言选择：新位置不存在时回退
    private static readonly string LegacyConfigFile = Path.Combine(AppData, "CnvQstStudio", "lang.txt");

    private static readonly List<LangInfo> _languages = new();

    private static LangInfo _current = new() { Code = BaselineCode, Name = "简体中文" };

    /// <summary>语言切换后触发（主窗口据此重建菜单/工具栏并刷新页签）。</summary>
    public static event Action? Changed;

    public static IReadOnlyList<LangInfo> Languages => _languages;

    public static string Code => _current.Code;

    public static string DisplayName => _current.Name;

    public static bool IsBaseline => _current.Code == BaselineCode;

    /// <summary>语言包目录（exe 旁的 lang/）。</summary>
    public static string LangDir => Path.Combine(AppContext.BaseDirectory, "lang");

    public static void Load()
    {
        _languages.Clear();
        // 中文基准包始终在列（即使文件缺失也保底可用）
        var baseline = new LangInfo { Code = BaselineCode, Name = "简体中文" };
        _languages.Add(baseline);

        try
        {
            if (Directory.Exists(LangDir))
            {
                foreach (string file in Directory.EnumerateFiles(LangDir, "*.json").OrderBy(f => f))
                {
                    var info = TryLoad(file);
                    if (info == null) continue;
                    if (info.Code == BaselineCode)
                    {
                        baseline.Name = info.Name;
                        // 基线包的 types 段（类型名/参数名中文名）也加载：TypePair 在中文界面下
                        // 才能输出"原文（本地化）"配对，与表格的 L.Type() 显示保持一致。
                        foreach (var kv in info.Types) baseline.Types[kv.Key] = kv.Value;
                        continue;
                    }
                    _languages.RemoveAll(l => l.Code == info.Code);
                    _languages.Add(info);
                }
            }
        }
        catch { /* 语言包目录不可读时只用中文基准 */ }

        string want = BaselineCode;
        try
        {
            string cfg = File.Exists(ConfigFile) ? ConfigFile : LegacyConfigFile;
            if (File.Exists(cfg)) want = File.ReadAllText(cfg).Trim();
        }
        catch { }
        _current = _languages.FirstOrDefault(l => l.Code == want) ?? baseline;
    }

    private static LangInfo? TryLoad(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            var root = doc.RootElement;
            var info = new LangInfo
            {
                Path = file,
                Code = root.TryGetProperty("code", out var c) ? c.GetString() ?? "?" : Path.GetFileNameWithoutExtension(file),
                Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?",
            };
            ReadMap(root, "strings", info.Strings);
            ReadMap(root, "types", info.Types);
            return info;
        }
        catch { return null; }                                  // 单个语言包坏了不影响其它
    }

    private static void ReadMap(JsonElement root, string key, Dictionary<string, string> into)
    {
        if (!root.TryGetProperty(key, out var map) || map.ValueKind != JsonValueKind.Object) return;
        foreach (var p in map.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String)
                into[p.Name] = p.Value.GetString()!;
    }

    /// <summary>切换语言并持久化；返回是否真的换了。</summary>
    public static bool Use(string code)
    {
        var target = _languages.FirstOrDefault(l => l.Code == code);
        if (target == null || target.Code == _current.Code) return false;
        _current = target;
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigFile, target.Code, new UTF8Encoding(false));
        }
        catch { /* 写配置失败不影响本次切换 */ }
        Changed?.Invoke();
        return true;
    }

    // ------------------------------------------------------------------
    // 取词
    // ------------------------------------------------------------------

    /// <summary>按中文原文取当前语言译文；没有译文时回退中文原文。</summary>
    public static string T(string zh) =>
        IsBaseline ? zh : _current.Strings.TryGetValue(zh, out var v) && v.Length > 0 ? v : zh;

    /// <summary>兼容旧的"中英成对"写法：优先查语言包，其次用给定的英文兜底。</summary>
    public static string T(string zh, string en) =>
        IsBaseline ? zh : _current.Strings.TryGetValue(zh, out var v) && v.Length > 0 ? v : en;

    /// <summary>带占位符的文案：先翻译再格式化（key 里用 {0} {1}）。</summary>
    public static string F(string zh, params object?[] args) => string.Format(T(zh), args);

    /// <summary>
    /// 类型名（条件/动作/节点类型等）：中文基准给中文名，其它语言给官方英文原名。
    /// 例如 Type("HasFaction", "阵营声望") → 中文界面显示"阵营声望"，英文界面显示"HasFaction"。
    /// </summary>
    public static string Type(string official, string zh) =>
        IsBaseline ? (ZhAlias(official) ?? zh)
                   : _current.Types.TryGetValue(official, out var v) && v.Length > 0 ? v : official;

    /// <summary>
    /// 类型名"原文（本地化）"配对显示（条件/动作名等，经由 Names.DisplayNameResolver 进入）：
    /// 本地化名取自当前语言包 types 段（zh-CN 基准包同样带中文名）；
    /// 无译文或与原文相同 → 只显示原文。传入的 label 可能带"（推断）"后缀，查找前剥离、结果后补回。
    /// </summary>
    public static string TypePair(string label)
    {
        string suffix = "";
        const string inferred = "（推断）";
        if (label.EndsWith(inferred, StringComparison.Ordinal))
        {
            label = label[..^inferred.Length];
            suffix = inferred;
        }
        string loc = _current.Types.TryGetValue(label, out var v) && v.Length > 0 ? v : label;
        return string.Equals(loc, label, StringComparison.Ordinal) ? label + suffix : $"{label}（{loc}）" + suffix;
    }

    /// <summary>中文别名表（来自各语言包的 types 反向不可用，故中文侧从 en 包同名字段读）。</summary>
    private static readonly Dictionary<string, string> _zhAlias = new();

    private static string? ZhAlias(string official) => _zhAlias.TryGetValue(official, out var v) ? v : null;

    /// <summary>供外部登记中文别名（由语言包 types 段在加载后反查填充）。</summary>
    public static void RegisterZhAliases(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        foreach (var kv in pairs) _zhAlias[kv.Key] = kv.Value;
    }

    /// <summary>语言切换菜单文案。</summary>
    public static string MenuLanguage => T("语言(&L)", "&Language");

    public static string WindowTitle => T("CnvQstEditor — Grim Dawn 对话 / 任务编辑器");

    // 主菜单（键即中文原文，英文取自语言包）
    public static string MFile => T("文件(&F)");
    public static string MNew => T("新建(&N)");
    public static string MImport => T("导入(&I)");
    public static string MExport => T("导出(&E)");
    public static string MBatch => T("批量(&B)");
    public static string MTools => T("工具(&T)");
    public static string MView => T("视图(&V)");
    public static string MHelp => T("帮助(&H)");

    public static string OpenTitle => T("打开");
    public static string OpenDirBatch => T("打开目录（批量）");
    public static string Save => T("保存");
    public static string SaveAs => T("另存为");
    public static string CloseTab => T("关闭页签");
    public static string Exit => T("退出");
    public static string NewConversationMenu => T("新建对话（CNV v6）");
    public static string NewQuestMenu => T("新建任务（QST2 v9）");
    public static string ImportTxtToActive => T("TXT 回填到当前文件");
    public static string ImportJsonNewTab => T("JSON 导入为新页签");
    public static string ExportTxtOfficial => T("TXT（官方本地化）");
    public static string ExportJson => T("JSON");
    public static string ExportDump => T("dump 树形文本");
    public static string BatchPanel => T("批量面板");
    public static string VerifyCurrent => T("校验当前文件");
    public static string Anchors => T("anchors 类型锚定工作台");
    public static string ReloadCurrent => T("重新加载当前文件");
    public static string About => T("关于");
    public static string ExpandAll => T("展开全部");
    public static string CollapseAll => T("折叠全部");
    public static string Filter => T("过滤节点");
    public static string OpenLangFolder => T("打开语言包目录");

    public static string NewConversationBtn => T("新建对话");
    public static string NewQuestBtn => T("新建任务");
    public static string ExportTxtBtn => T("导出 TXT");
    public static string ExportJsonBtn => T("导出 JSON");
    public static string ImportTxtBtn => T("导入 TXT");
    public static string BatchBtn => T("批量");
    public static string VerifyBtn => T("校验");
    public static string OpenBtn => T("打开");
    public static string SaveBtn => T("保存");
    public static string DumpBtn => T("dump");

    public static string AboutTitle => T("关于");
    public static string AboutBody => T(
        "CnvQstEditor —— Grim Dawn .cnv / .qst 编辑器\n\n" +
        "查看、编辑、新建、导入导出与批量处理；格式依据 Game.dll 加载器逆向与 447 个样本验证。\n\n" +
        "保存前自动执行三检查（记录铺满 / 本地化偏移 / round-trip 字节一致），结构合法不等于语义正确，\n" +
        "标黄的未知语义字段改完请在游戏或官方编辑器中验证。");

    /// <summary>关于对话框的许可证摘要（只列名称与许可证类型，不展示全文）。</summary>
    public static string AboutLicense => T(
        "许可证：MIT —— 本项目及所用 Avalonia、Avalonia.Controls.DataGrid、Avalonia.Controls.TreeDataGrid、Semi.Avalonia 第三方组件均为 MIT。\n" +
        "完整许可文本见程序目录或仓库根目录的 LICENSE 文件。",
        "License: MIT — this project and its third-party components (Avalonia, Avalonia.Controls.DataGrid, Avalonia.Controls.TreeDataGrid, Semi.Avalonia) are all MIT.\n" +
        "See LICENSE in the program directory or repository root for the full text.");

    // 新建向导
    public static string NewConversationTitle => T("新建对话（CNV v6）");
    public static string NewConversationSpeaker => T("说话人名");
    public static string NewConversationFirst => T("首句台词");
    public static string NewConversationEnd => T("结束语（可空）");
    public static string Create => T("创建");
    public static string Cancel => T("取消");
    public static string Confirm => T("确定");
    public static string SpeakerRequired => T("说话人名不能为空。");
    public static string NewQuestTitle => T("新建任务（QST2 v9）");
    public static string NewQuestGroup => T("任务组名");
    public static string NewQuestName => T("任务名");
    public static string NewQuestTaskTitle => T("Task 标题");
    public static string NewQuestTaskDesc => T("Task 描述");
    public static string QuestNameRequired => T("任务名不能为空。");

    public static string EmptyHint => T(
        "从左侧选择节点开始查看 / 编辑，或直接把 .cnv / .qst 文件（目录也可以）拖进窗口。\n\n" +
        "· 导出可另存 TXT / JSON / dump 树形文本\n" +
        "· 导入可回填官方本地化 TXT 或 JSON\n" +
        "· 保存前自动三检查，并生成 .bak 备份");

    public static string LocStringTable => T("本地化字符串表");

    // 状态栏
    public static string StatusSaving => T("● 未保存");
    public static string StatusReadonly(string reason) => F("只读：{0}", reason);
    public static string StatusReady => T("就绪");

    // 通用
    public static string Ok => T("确定");
    public static string Warning => T("提示");
    public static string Error => T("错误");
    public static string Close => T("关闭");
    public static string Apply => T("应用");
    public static string Add => T("添加");
    public static string Delete => T("删除");
    public static string Copy => T("复制");
    public static string MoveUp => T("上移");
    public static string MoveDown => T("下移");
    public static string Language => T("语言");
}
