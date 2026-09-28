using System.Text;

namespace CnvQst;

/// <summary>名字的证据等级——UI 与对照表据此标注置信度。</summary>
public enum Evidence
{
    Engine,     // Game.dll 导出表/诊断串证实（gd-utility）
    Anchored,   // 本地金样与编辑器 UI 手工对照钉住
    Inferred,   // 仅凭官方选项表 + body 形状/语料推断，未经真机核对
}

/// <summary>官方编辑器下拉里的一项（照抄 cnv示例/cnv字段选项.txt）。</summary>
public sealed class OfficialOption
{
    public string Name = "";
    public string Params = "";    // 编辑器显示的字段名与取值类型
    public string Note = "";
}

/// <summary>
/// 官方编辑器选项词表（CNV/QST 共用同一套条件/动作系统）。
///
/// 用途有二：
///   ① 给二进制 type id 提供"官方名"这一层命名（<see cref="Names"/> 消费）；
///   ② `vocab` 命令输出"官方选项 ↔ type id"对照表，供在官方编辑器里逐项核对。
///
/// 抄录来源：cnv示例/cnv字段选项.txt（用户在官方编辑器中逐项展开抄录）。
/// 官方条件下拉 30 项、动作下拉 28 项；二进制只有 26 个条件槽（0–25）与
/// 27 个可用动作槽（0–28，其中 0/16 工厂返回 null），因此：
///   · 部分官方选项只能是同一 type 的 subType 变体（如 HasKilled 系列）；
///   · OperMerchantWindow 在编辑器里点击即报错，与"工厂返回 null 的 0/16"吻合。
/// </summary>
public static class EditorVocab
{
    public static readonly OfficialOption[] Conditions =
    {
        new() { Name = "AnyoneHasToken",      Params = "TokenName str / HasToken TRUE-FALSE" },
        new() { Name = "CheckRandomValue",    Params = "Name str / Value num / Comparaior" },
        new() { Name = "EarnedDevotion",      Params = "DevotionAmount num / Comparaior" },
        new() { Name = "Hasbuff",             Params = "BuffName file / HasBuff TRUE-FALSE" },
        new() { Name = "HasExperience",       Params = "Experience num / Comparaior" },
        new() { Name = "HasFaction",          Params = "FactionName str / Value num / Comparaior" },
        new() { Name = "HasItem",             Params = "ItemRecord file / ItemCount num / CompleteOnly TRUE-FALSE / Comparaior" },
        new() { Name = "HasKilled",           Params = "MonsterRecords arr / KillCount num / Comparaior" },
        new() { Name = "HasKilledFaction",    Params = "Faction1 内置arr / Faction2 内置arr / KillCount num / Comparaior" },
        new() { Name = "HasKilledMonsterClass", Params = "Faction1 内置arr Common-Champion-Hero-Boss-Quest-SuperBoss / Faction2 同 / KillCount num / Comparaior" },
        new() { Name = "HasKilledProxy",      Params = "ProxyRecords arr / KillCount num / Comparaior" },
        new() { Name = "HasMastery",          Params = "MasteryName 内置arr SkillClass01..30 / HasMastery TRUE-FALSE" },
        new() { Name = "HasMoney",            Params = "MoneyAmount num / Comparaior" },
        new() { Name = "HasQuestObjective",   Params = "Quest file / Task 自动 / Objective 自动 / Satisfied TRUE-FALSE" },
        new() { Name = "HasToken",            Params = "TokenName str / HasToken TRUE-FALSE" },
        new() { Name = "HasTribute",          Params = "TributeAmount num / Comparaior" },
        new() { Name = "IsChallenge",         Params = "IsChallenge 内置arr / Comparaior" },
        new() { Name = "IsDifficulty",        Params = "IsDifficulty 内置arr / Comparaior" },
        new() { Name = "IsHardcore",          Params = "IsHardcore TRUE-FALSE" },
        new() { Name = "IsLevel",             Params = "IsLevel num / Comparaior" },
        new() { Name = "KnowsPlayer",         Params = "KnowsPlayer TRUE-FALSE" },
        new() { Name = "LuaScript",           Params = "Function 内置lua / args 表格 / Evaluate 类型+比较符" },
        new() { Name = "OnQuest",             Params = "QusstFile file / OnQuest TRUE-FALSE" },
        new() { Name = "OnQuestTask",         Params = "QusstFile file / Task 自动 / OnTask TRUE-FALSE" },
        new() { Name = "OwnsDLC",             Params = "DLC_ID num / Comparaior" },
        new() { Name = "QuestBlocked",        Params = "QuestBlocked file / QuestBlocked TRUE-FALSE" },
        new() { Name = "QuestTaskComplete",   Params = "QusstFile file / Task 自动 / TaskComplete TRUE-FALSE" },
        new() { Name = "ServerHasToken",      Params = "TokenName str / HasToken TRUE-FALSE" },
        new() { Name = "UsedQuestItem",       Params = "ItemRecord file / NumUsed num / Comparaior" },
        new() { Name = "WaitForCompletion",   Params = "无选项" },
    };

    public static readonly OfficialOption[] Actions =
    {
        new() { Name = "BeginQuestTask",      Params = "QuestFile file / Task 自动" },
        new() { Name = "CastSkill",           Params = "SkillRecord file" },
        new() { Name = "CompleteQuest",       Params = "QuestFile file" },
        new() { Name = "CompleteQuestTask",   Params = "QuestFile file / Task 自动" },
        new() { Name = "DebugPrint",          Params = "Text str" },
        new() { Name = "GenerateRandomValue", Params = "Name str / Min num / Max num" },
        new() { Name = "GiveAttributePoint",  Params = "NumAttributePoints num" },
        new() { Name = "GiveDevotion",        Params = "NumDevotionPoints num" },
        new() { Name = "GiveExperience",      Params = "Experience num" },
        new() { Name = "GiveFaction",         Params = "FactionType str / Amount num" },
        new() { Name = "GiveItem",            Params = "ItemRecord file / NumItems num" },
        new() { Name = "GiveLevel",           Params = "NumLevels num" },
        new() { Name = "GiveMoney",           Params = "MoneyAmount num" },
        new() { Name = "GiveRandomItem",      Params = "ItemRecord file / NumItems num" },
        new() { Name = "GiveSkillPoint",      Params = "NumSkillpoints num" },
        new() { Name = "GiveToken",           Params = "TokenName str" },
        new() { Name = "GiveTribute",         Params = "TributeAmount num" },
        new() { Name = "LuaScript",           Params = "Function 内置lua / args 表格" },
        new() { Name = "Notification",        Params = "Tag str" },
        new() { Name = "OperMerchantWindow",  Params = "（编辑器点击报错，无法打开）", Note = "疑似对应工厂返回 null 的 type 0/16" },
        new() { Name = "PlayAnimation",       Params = "AnimationType num" },
        new() { Name = "PlaySound",           Params = "SoundRecord file" },
        new() { Name = "PlayVideo",           Params = "Video Path file / AllowSkip TRUE-FALSE" },
        new() { Name = "RemoveToken",         Params = "TokenName str" },
        new() { Name = "ScriptEvent",         Params = "ScriptEvent str", Note = "选项表原文写作 ScriptEyent，系笔误" },
        new() { Name = "SetFaction",          Params = "FactionType str / Value num" },
        new() { Name = "UnlockFaction",       Params = "FactionType str" },
        new() { Name = "UnlockTutorial",      Params = "TutorialPage num" },
    };

    /// <summary>条件 type → 官方名。</summary>
    public static readonly Dictionary<uint, (string name, Evidence ev)> CondMap = new()
    {
        [0] = ("OnQuest", Evidence.Engine),
        [1] = ("QuestTaskComplete", Evidence.Engine),
        [2] = ("HasExperience", Evidence.Anchored),
        [3] = ("IsLevel", Evidence.Anchored),
        [4] = ("IsDifficulty", Evidence.Anchored),
        [5] = ("IsHardcore", Evidence.Anchored),
        [6] = ("HasFaction", Evidence.Engine),
        [7] = ("HasToken", Evidence.Anchored),
        [8] = ("HasMoney", Evidence.Anchored),
        [9] = ("HasItem", Evidence.Engine),
        [10] = ("HasKilled", Evidence.Engine),
        [11] = ("HasQuestObjective", Evidence.Engine),
        [12] = ("LuaScript", Evidence.Engine),
        [13] = ("KnowsPlayer", Evidence.Anchored),
        [14] = ("OnQuestTask", Evidence.Engine),
        [15] = ("UsedQuestItem", Evidence.Anchored),
        [16] = ("HasKilledProxy", Evidence.Anchored),
        [17] = ("WaitForCompletion", Evidence.Anchored),
        [18] = ("ServerHasToken", Evidence.Anchored),
        [19] = ("QuestBlocked", Evidence.Engine),
        [20] = ("AnyoneHasToken", Evidence.Anchored),
        [21] = ("CheckRandomValue", Evidence.Engine),
        [22] = ("OwnsDLC", Evidence.Anchored),
        [23] = ("HasTribute", Evidence.Anchored),
        [24] = ("EarnedDevotion", Evidence.Anchored),
        [25] = ("HasMastery", Evidence.Anchored),
        // 官方编辑器内嵌 Common.dll 的类型枚举（0–29）——条件共 30 种，这 4 种是 gd-utility 旧表所缺
        [26] = ("IsChallenge", Evidence.Engine),
        [27] = ("HasBuff", Evidence.Engine),
        [28] = ("HasKilledMonsterClass", Evidence.Engine),
        [29] = ("HasKilledFaction", Evidence.Engine),
    };

    /// <summary>动作 type → 官方名。</summary>
    public static readonly Dictionary<uint, (string name, Evidence ev)> ActMap = new()
    {
        [1] = ("BeginQuestTask", Evidence.Engine),
        [2] = ("CompleteQuest", Evidence.Anchored),
        [3] = ("CompleteQuestTask", Evidence.Anchored),
        [4] = ("GiveMoney", Evidence.Engine),
        [5] = ("GiveFaction", Evidence.Anchored),
        [6] = ("GiveItem", Evidence.Engine),
        [7] = ("GiveLevel", Evidence.Engine),
        [8] = ("GiveExperience", Evidence.Engine),
        [9] = ("GiveToken", Evidence.Anchored),
        [10] = ("RemoveToken", Evidence.Anchored),
        [11] = ("LuaScript", Evidence.Engine),
        [12] = ("DebugPrint", Evidence.Anchored),
        [13] = ("PlaySound", Evidence.Anchored),
        [14] = ("PlayAnimation", Evidence.Anchored),
        [15] = ("GiveSkillPoint", Evidence.Engine),
        [17] = ("Notification", Evidence.Anchored),
        [18] = ("GiveAttribPoint", Evidence.Engine),
        [19] = ("ScriptEvent", Evidence.Inferred),
        [20] = ("GiveRandomItem", Evidence.Engine),
        [21] = ("GenerateRandomValue", Evidence.Engine),
        [22] = ("CastSkill", Evidence.Engine),
        [23] = ("UnlockFaction", Evidence.Anchored),
        [24] = ("SetFaction", Evidence.Anchored),
        [25] = ("GiveDevotion", Evidence.Engine),
        [26] = ("GiveTribute", Evidence.Engine),
        [27] = ("UnlockTutorial", Evidence.Engine),
        [28] = ("PlayVideo", Evidence.Engine),
    };

    // 2026-09-28：全部官方选项均已落位（条件 30 项 = type 0–29，动作 28 项 = type 0–28）。
    // 下面这张表保留为空，便于日后新增官方选项时再登记候选。
    public static readonly (string name, string candidates)[] CondPending =
    {
    };

    public static (string name, Evidence ev)? Official(bool condition, uint type) =>
        condition
            ? CondMap.TryGetValue(type, out var c) ? c : null
            : ActMap.TryGetValue(type, out var a) ? a : null;

    public static string Tag(Evidence ev) => ev switch
    {
        Evidence.Engine => "引擎证实",
        Evidence.Anchored => "金样锚定",
        _ => "推断·待核对",
    };

    /// <summary>官方选项 ↔ 二进制 type 对照表（`vocab` 命令输出）。</summary>
    public static string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══ 官方编辑器选项 ↔ 二进制类型 对照表 ═══");
        sb.AppendLine("来源：cnv示例/cnv字段选项.txt（官方编辑器逐项抄录）；QST 与 CNV 共用同一套。");
        sb.AppendLine();

        void Section(bool condition)
        {
            var map = condition ? CondMap : ActMap;
            var opts = condition ? Conditions : Actions;
            int slots = condition ? 30 : 29;
            sb.AppendLine($"── {(condition ? "条件" : "动作")}（官方 {opts.Length} 项 / 二进制 {slots} 槽）──");
            for (uint t = 0; t < slots; t++)
            {
                string body = condition ? Scriptable.ConditionBodySpec(t) : Scriptable.ActionBodySpec(t);
                if (!condition && (t == 0 || t == 16))
                {
                    sb.AppendLine($"  type {t,2}  （工厂返回 null，无文件使用；疑为 OperMerchantWindow）");
                    continue;
                }
                string name = condition ? Names.ConditionName(t) : Names.ActionName(t);
                string tag = map.TryGetValue(t, out var m)
                    ? (m.name == name ? Tag(m.ev) : $"{Tag(m.ev)}·命名表不一致({m.name})")
                    : (name.StartsWith("Type") ? "未定位" : "已命名·未登记证据");
                sb.AppendLine($"  type {t,2}  {name,-22} body={body,-5} [{tag}]");
            }
            var mapped = new HashSet<string>(map.Values.Select(v => v.name));
            var unmapped = opts.Where(o => !mapped.Contains(o.Name)).ToList();
            if (unmapped.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"  ---- 官方有、但尚未定位到 type 的 {unmapped.Count} 项 ----");
                foreach (var o in unmapped)
                {
                    var pend = CondPending.FirstOrDefault(p => p.name == o.Name);
                    sb.AppendLine($"  {o.Name,-24} {o.Params}");
                    sb.AppendLine($"      候选：{(pend.name == null ? "—" : pend.candidates)}");
                }
            }
            sb.AppendLine();
        }

        Section(true);
        Section(false);
        sb.AppendLine("比较符：条件记录里 type/body 版本之后的那个 32 位字（gd-utility 旧称 uid）即比较符，");
        sb.AppendLine("      取值 Less=0 <=1 Equal=2 >=3 Greater=4 NotEqual=5 —— 来自官方编辑器 Common.dll 的");
        sb.AppendLine("      ScriptableCondition+Comparison 枚举，并已用 probe uid 在编辑器里逐值实测一致。");
        return sb.ToString();
    }
}
