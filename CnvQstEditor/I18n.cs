using System.Text;
using System.Text.Json;
using CnvQst;

namespace CnvQstEditor;

/// <summary>一种可用语言（来自 lang/*.json）。</summary>
internal sealed class LangInfo
{
    public string Code = "zh-CN";
    public string Name = "简体中文";
    public string Path = "";
    /// <summary>英文 key → 该语言译文。英文包为恒等映射。</summary>
    public Dictionary<string, string> Strings = new();
    /// <summary>条件/动作等"类型"的显示名：官方英文名 → 该语言名。</summary>
    public Dictionary<string, string> Types = new();
}

/// <summary>
/// UI 本地化。**以英文为 key，中文（zh-CN）为默认语言**，语言包是 exe 旁 `lang/*.json`，放进去即生效：
///
/// {
///   "code": "zh-CN",
///   "name": "简体中文",
///   "strings": { "Open": "打开", "Saved {0}": "已保存 {0}" },
///   "types":   { "HasFaction": "阵营声望" }
/// }
///
/// · key 缺译文时回退 key 本身（英文），因此语言包可以逐步补全，不会出现空标签；
/// · 默认语言 zh-CN；英文包 en.json 保留恒等映射，作为可用 key 清单；
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

    /// <summary>语言包目录（exe 旁的 lang/）。</summary>
    public static string LangDir => Path.Combine(AppContext.BaseDirectory, "lang");

    /// <summary>
    /// 固定术语清单：动作 / 条件 / 节点类型等官方术语**不进入 strings 字典**（不整体翻译），
    /// 中文界面由语言包 types 段括注。后期发现新的内置固定文本直接加入即可。
    /// </summary>
    public static readonly HashSet<string> FixedTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "root", "speech", "continue", "accept", "decline", "generic", "end", "link", "random", "selector",
    };

    static L()
    {
        for (uint t = 0; t < 30; t++) FixedTerms.Add(Names.ConditionName(t));
        for (uint t = 0; t < 29; t++)
        {
            string name = Names.ActionName(t);
            if (!name.StartsWith("Type", StringComparison.Ordinal)) FixedTerms.Add(name);
        }
        for (uint t = 0; t <= 8; t++) FixedTerms.Add(Names.CnvStepKind(t));
        FixedTerms.Add("unknown kind");
    }

    public static bool IsFixedTerm(string label) => FixedTerms.Contains(label);

    public static void Load()
    {
        _languages.Clear();

        // ① 内嵌语言包（CnvQstEditor.lang.*.json）：单文件发布兜底
        try
        {
            var asm = typeof(L).Assembly;
            var names = asm.GetManifestResourceNames()
                .Where(n => n.StartsWith("CnvQstEditor.lang.", StringComparison.Ordinal)
                         && n.EndsWith(".json", StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.Ordinal);
            foreach (string name in names)
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream == null) continue;
                Absorb(TryLoad(stream, "embedded:" + name), overlay: false);
            }
        }
        catch { /* 内嵌资源不可读时只用磁盘包 */ }

        // ② exe 旁 lang\*.json：用户自定义/新增语言，覆盖内嵌包
        try
        {
            if (Directory.Exists(LangDir))
                foreach (string file in Directory.EnumerateFiles(LangDir, "*.json").OrderBy(f => f))
                    Absorb(TryLoad(file), overlay: true);
        }
        catch { /* 语言包目录不可读时跳过，由内嵌包兜底 */ }

        // 中文基准包始终在列（即使文件缺失也保底可用）
        if (!_languages.Any(l => l.Code == BaselineCode))
            _languages.Add(new LangInfo { Code = BaselineCode, Name = "简体中文" });

        string want = BaselineCode;
        try
        {
            string cfg = File.Exists(ConfigFile) ? ConfigFile : LegacyConfigFile;
            if (File.Exists(cfg)) want = File.ReadAllText(cfg).Trim();
        }
        catch { }
        _current = _languages.FirstOrDefault(l => l.Code == want)
                ?? _languages.First(l => l.Code == BaselineCode);
    }

    /// <summary>
    /// 吸收一个语言包：overlay=true（磁盘）覆盖同名 code 的译文；false（内嵌）只补缺，
    /// 不覆盖已有包。同一 code 的 strings/types 按键合并，便于增量语言包。
    /// </summary>
    private static void Absorb(LangInfo? info, bool overlay)
    {
        if (info == null) return;
        var target = _languages.FirstOrDefault(l => l.Code == info.Code);
        if (target == null)
        {
            _languages.Add(info);
            return;
        }
        if (overlay)
        {
            target.Name = info.Name;
            target.Path = info.Path;
        }
        foreach (var kv in info.Strings)
            if (overlay || !target.Strings.ContainsKey(kv.Key))
                target.Strings[kv.Key] = kv.Value;
        foreach (var kv in info.Types)
            if (overlay || !target.Types.ContainsKey(kv.Key))
                target.Types[kv.Key] = kv.Value;
    }

    private static LangInfo? TryLoad(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            return TryLoad(stream, file);
        }
        catch { return null; }                                  // 单个语言包坏了不影响其它
    }

    private static LangInfo? TryLoad(Stream stream, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            var root = doc.RootElement;
            var info = new LangInfo
            {
                Path = path,
                Code = root.TryGetProperty("code", out var c) ? c.GetString() ?? "?" : Path.GetFileNameWithoutExtension(path),
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

    /// <summary>按英文 key 取当前语言译文；没有译文时回退 key（英文）。</summary>
    public static string T(string key) =>
        _current.Strings.TryGetValue(key, out var v) && v.Length > 0 ? v : key;

    /// <summary>带占位符的文案：先翻译再格式化（key 里用 {0} {1}）。</summary>
    public static string F(string key, params object?[] args) => string.Format(T(key), args);

    /// <summary>
    /// 类型名配对显示（条件/动作名等，经由 Names.DisplayNameResolver 进入）：
    /// 固定术语以官方原文为主，语言包 types 段有译文时括注为"原文（本地化）"；
    /// 无译文（如英文界面）只显示原文。传入的 label 可能带推断后缀，查找前剥离、结果后补回。
    /// </summary>
    public static string TypePair(string label)
    {
        string suffix = "";
        const string inferred = "（推断）";
        if (label.EndsWith(inferred, StringComparison.Ordinal))
        {
            label = label[..^inferred.Length];
            suffix = T("(inferred)");
        }
        string loc = TypeName(label);
        return string.Equals(loc, label, StringComparison.Ordinal)
            ? label + suffix
            : label + InParen(loc) + suffix;
    }

    /// <summary>本地化的括号包裹："({0})"——中文为全角括号，英文为半角。</summary>
    public static string InParen(string text) => string.Format(T("({0})"), text);

    /// <summary>标签后的冒号：中文全角"："，英文半角":"。</summary>
    public static string Colon => T(":");

    /// <summary>固定术语在非中文界面下的本地化名：无译文时回退原文。</summary>
    public static string TypeName(string official) =>
        _current.Types.TryGetValue(official, out var v) && v.Length > 0 ? v : official;

    /// <summary>语言切换菜单文案。</summary>
    public static string MenuLanguage => T("_Language");

    public static string WindowTitle => T("CnvQstEditor — Grim Dawn conversation / quest editor");

    // 主菜单（下划线为 Avalonia 访问键：Alt+F 打开文件菜单）
    public static string MFile => T("_File");
    public static string MNew => T("_New");
    public static string MImport => T("_Import");
    public static string MExport => T("_Export");
    public static string MBatch => T("_Batch");
    public static string MTools => T("_Tools");
    public static string MView => T("_View");
    public static string MHelp => T("_Help");

    public static string OpenTitle => T("Open");
    public static string OpenDirBatch => T("Open folder (batch)");
    public static string Save => T("Save");
    public static string SaveAs => T("Save as");
    public static string CloseTab => T("Close tab");
    public static string Exit => T("Exit");
    public static string NewConversationMenu => T("New conversation (CNV v6)");
    public static string NewQuestMenu => T("New quest (QST2 v9)");
    public static string ImportJsonNewTab => T("Import JSON as new tab");
    public static string ExportTxtOfficial => T("TXT (official localization)");
    public static string ExportJson => T("JSON");
    public static string ExportDump => T("dump tree text");
    public static string BatchPanel => T("Batch panel");
    public static string VerifyCurrent => T("Verify current file");
    public static string Anchors => T("anchors (type anchoring workbench)");
    public static string ReloadCurrent => T("Reload current file");
    public static string About => T("About");
    public static string ExpandAll => T("Expand all");
    public static string CollapseAll => T("Collapse all");
    public static string Filter => T("Filter nodes");

    public static string NewConversationBtn => T("New conversation");
    public static string NewQuestBtn => T("New quest");
    public static string ExportTxtBtn => T("Export TXT");
    public static string ExportJsonBtn => T("Export JSON");
    public static string BatchBtn => T("Batch");
    public static string VerifyBtn => T("Verify");
    public static string OpenBtn => T("Open");
    public static string SaveBtn => T("Save");
    public static string DumpBtn => T("dump");

    public static string AboutTitle => T("About");
    public static string AboutBody => T(
        "CnvQstEditor — Grim Dawn .cnv / .qst editor\n\n" +
        "View, edit, create, import/export and batch-process; the format is based on reverse-engineering the Game.dll loader and 447 verified samples.\n\n" +
        "Three checks run before saving (records tile the file / localization offset / round-trip bytes). A structurally valid file is not necessarily semantically correct;\n" +
        "after changing yellow unknown-semantics fields, verify in game or the official editor.");

    /// <summary>关于对话框的许可证摘要（只列名称与许可证类型，不展示全文）。</summary>
    public static string AboutLicense => T(
        "License: MIT — this project and its third-party components (Avalonia, Avalonia.Controls.DataGrid, Avalonia.Controls.TreeDataGrid, Semi.Avalonia) are all MIT.\n" +
        "See LICENSE in the program directory or repository root for the full text.");

    // 新建向导
    public static string NewConversationTitle => T("New conversation (CNV v6)");
    public static string NewConversationSpeaker => T("Speaker name");
    public static string NewConversationFirst => T("First line");
    public static string NewConversationEnd => T("Closing line (optional)");
    public static string Create => T("Create");
    public static string Cancel => T("Cancel");
    public static string Confirm => T("OK");
    public static string SpeakerRequired => T("Speaker name cannot be empty.");
    public static string NewQuestTitle => T("New quest (QST2 v9)");
    public static string NewQuestGroup => T("Quest group");
    public static string NewQuestName => T("Quest name");
    public static string NewQuestTaskTitle => T("Task title");
    public static string NewQuestTaskDesc => T("Task description");
    public static string QuestNameRequired => T("Quest name cannot be empty.");

    public static string EmptyHint => T(
        "Select a node on the left to view / edit, or drag a .cnv / .qst file (or folder) into the window.\n\n" +
        "· Export to TXT / JSON / dump tree text\n" +
        "· Import supports JSON files\n" +
        "· Three checks run before saving, and a .bak backup is created");

    public static string LocStringTable => T("Localization string table");

    // 状态栏
    public static string StatusSaving => T("● Unsaved");
    public static string StatusReadonly(string reason) => F("Read-only: {0}", reason);
    public static string StatusReady => T("Ready");

    // 通用
    public static string Ok => T("OK");
    public static string Warning => T("Notice");
    public static string Error => T("Error");
    public static string Close => T("Close");
    public static string Apply => T("Apply");
    public static string Add => T("Add");
    public static string Delete => T("Delete");
    public static string Copy => T("Copy");
    public static string MoveUp => T("Move up");
    public static string MoveDown => T("Move down");
    public static string Language => T("Language");
}
