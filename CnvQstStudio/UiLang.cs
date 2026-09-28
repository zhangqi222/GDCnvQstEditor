using System.IO;

namespace CnvQstStudio;

/// <summary>
/// 轻量 UI 本地化：默认简体中文，可在运行中切换到英文（工具菜单 → 语言/Language）。
/// 选择持久化到 %APPDATA%\CnvQstStudio\lang.txt（zh/en）。
/// 覆盖主菜单/工具栏/主窗口/状态栏/关于框及两个新建向导；其余细粒度对话框留待后续扩展。
/// </summary>
internal enum UiLang { Zh, En }

internal static class L
{
    public static UiLang Current { get; private set; } = UiLang.Zh;

    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CnvQstStudio");

    private static readonly string ConfigFile = Path.Combine(ConfigDir, "lang.txt");

    public static void Load()
    {
        try
        {
            if (File.Exists(ConfigFile) && File.ReadAllText(ConfigFile).Trim() == "en")
                Current = UiLang.En;
        }
        catch { /* 配置损坏忽略，回退默认中文 */ }
    }

    public static void Toggle()
    {
        Current = Current == UiLang.Zh ? UiLang.En : UiLang.Zh;
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigFile, Current == UiLang.En ? "en" : "zh");
        }
        catch { /* 写配置失败不影响本次切换 */ }
    }

    public static string T(string zh, string en) => Current == UiLang.Zh ? zh : en;

    // 主窗口 / 菜单 / 工具栏
    public static string WindowTitle => Current == UiLang.Zh
        ? "CnvQstStudio — Grim Dawn CNV/QST 工作台"
        : "CnvQstStudio — Grim Dawn CNV/QST Workbench";

    public const string MFile = "文件(&F)";
    public const string MFileEn = "&File";
    public const string MNew = "新建(&N)";
    public const string MNewEn = "&New";
    public const string MImport = "导入(&I)";
    public const string MImportEn = "&Import";
    public const string MExport = "导出(&E)";
    public const string MExportEn = "&Export";
    public const string MBatch = "批量(&B)";
    public const string MBatchEn = "&Batch";
    public const string MTools = "工具(&T)";
    public const string MToolsEn = "&Tools";
    public const string MHelp = "帮助(&H)";
    public const string MHelpEn = "&Help";
    public const string MLang = "语言 / Language";
    public const string MLangEn = "Language / 语言";

    public static string OpenTitle => T("打开…", "Open...");
    public static string OpenDirBatch => T("打开目录（批量）…", "Open Folder (Batch)...");
    public static string Save => T("保存", "Save");
    public static string SaveAs => T("另存为…", "Save As...");
    public static string CloseTab => T("关闭页签", "Close Tab");
    public static string Exit => T("退出", "Exit");
    public static string NewConversationMenu => T("对话（CNV v6）…", "Conversation (CNV v6)...");
    public static string NewQuestMenu => T("任务（QST2 v9）…", "Quest (QST2 v9)...");
    public static string ImportTxtToActive => T("TXT 回填到当前文件…", "Backfill TXT into Current File...");
    public static string ImportJsonNewTab => T("JSON 为新页签…", "Import JSON as New Tab...");
    public static string ExportTxtOfficial => T("TXT（官方本地化）…", "TXT (Official Localization)...");
    public static string ExportJson => T("JSON…", "JSON...");
    public static string ExportDump => T("dump 树形文本…", "Dump Tree Text...");
    public static string BatchPanel => T("批量面板…", "Batch Panel...");
    public static string VerifyCurrent => T("校验当前文件", "Verify Current File");
    public static string Anchors => T("anchors（类型锚定工作台）", "anchors (Type Anchoring Workbench)");
    public static string ReloadCurrent => T("重新加载当前文件", "Reload Current File");
    public static string About => T("关于…", "About...");

    public static string NewConversationBtn => T("新建对话", "New Conversation");
    public static string NewQuestBtn => T("新建任务", "New Quest");
    public static string ExportTxtBtn => T("导出TXT", "Export TXT");
    public static string ExportJsonBtn => T("导出JSON", "Export JSON");
    public static string ImportTxtBtn => T("导入TXT", "Import TXT");
    public static string BatchBtn => T("批量", "Batch");
    public static string VerifyBtn => T("校验", "Verify");
    public static string OpenBtn => T("打开", "Open");

    public static string AboutTitle => T("关于", "About");
    public static string AboutBody => T(
        "CnvQstStudio 1.0.0\nGrim Dawn .cnv / .qst 查看、编辑、新建、导入导出与批量处理。\n\n" +
        "格式依据：Game.dll 加载器逆向（gd-utility，MIT）+ 本项目 446 样本验证。\n" +
        "详见 CnvQstFormatDecoded.md。\n\n" +
        "保存前自动执行三检查（铺满文件 / 本地化偏移 / round-trip 字节一致），\n" +
        "结构合法 ≠ 语义正确，未知语义字段（标黄）修改后请在游戏内验证。",
        "CnvQstStudio 1.0.0\nView, edit, create, import/export and batch-process Grim Dawn .cnv / .qst.\n\n" +
        "Format based on Game.dll loader reverse engineering (gd-utility, MIT) + 446 samples verified.\n" +
        "See CnvQstFormatDecoded.md.\n\n" +
        "A three-check (tiling / locOffset / round-trip byte equality) runs automatically before saving;\n" +
        "structural validity does not equal semantic correctness - verify unknown-semantics fields (yellow) in-game.");

    // 新建向导
    public static string NewConversationTitle => T("新建对话（CNV v6）", "New Conversation (CNV v6)");
    public static string NewConversationSpeaker => T("说话人名（索引0）", "Speaker Name (index 0)");
    public static string NewConversationFirst => T("首句台词", "First Line");
    public static string NewConversationEnd => T("结束语（可空）", "Closing Line (optional)");
    public static string Create => T("创建", "Create");
    public static string Cancel => T("取消", "Cancel");
    public static string SpeakerRequired => T("说话人名不能为空（写入本地化索引 0）。", "Speaker name cannot be empty (written to localization index 0).");
    public static string NewQuestTitle => T("新建任务（QST2 v9）", "New Quest (QST2 v9)");
    public static string NewQuestGroup => T("任务组名（loc 0）", "Quest Group (loc 0)");
    public static string NewQuestName => T("任务名（loc 1）", "Quest Name (loc 1)");
    public static string NewQuestTaskTitle => T("Task 标题", "Task Title");
    public static string NewQuestTaskDesc => T("Task 描述", "Task Description");
    public static string QuestNameRequired => T("任务名不能为空（本地化索引 1）。", "Quest name cannot be empty (localization index 1).");

    // 空提示
    public static string EmptyHint => T(
        "← 在左侧选择节点进行查看 / 编辑。\n\n• 菜单\"导出\"可另存 TXT / JSON / dump 树形文本\n• 菜单\"导入\"可回填官方本地化 TXT 或 JSON\n• 保存时自动执行三检查并生成 .bak 备份",
        "← Select a node on the left to view / edit.\n\n• \"Export\" menu saves TXT / JSON / dump tree text\n• \"Import\" menu backfills official localization TXT or JSON\n• Saving runs the three-check and creates a .bak backup");

    public static string LocStringTable => T("本地化字符串表", "Localization String Table");

    // 状态栏 / 摘要通用片段
    public static string StatusSaving => T("● 未保存  ", "● Unsaved  ");
    public static string StatusReadonly(string reason) => T($"  ·  只读（{reason}）", $"  ·  Read-only ({reason})");
}
