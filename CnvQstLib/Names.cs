namespace CnvQst;

/// <summary>
/// 类型 ID → 语义名。
///
/// 三层来源（证据由强到弱）：
///   ① gd-utility 从 Game.dll 导出表与引擎诊断串证实的类名（"ScriptableCondition_HasFaction" 等），
///      证据见 参考资料/gd-utility/docs/QST.md §Bodies/§Naming；
///   ② 本地锚定表 <see cref="ConditionEditorNames"/> / <see cref="ActionEditorNames"/>：
///      用 cnv示例/qst示例 中带编辑器 UI 手工对照的文件，把编辑器下拉名钉到二进制类型 ID 上
///      （证据记录在 CnvQstFormatDecoded.md §本地锚定）；
///   ③ 官方选项词表 <see cref="EditorVocab"/>（cnv示例/cnv字段选项.txt 抄录）：按 body 形状唯一匹配
///      补入的名字，语料中样本为 0，标"（推断）"，待官方编辑器真机核对。
/// 命名不能套用编辑器下拉列表的序号（编辑器按字母排序，与类型 ID 无关）。
/// </summary>
public static class Names
{
    // ---- CNV step 类型（Game.dll ConversationStep::GetTypeTag / GetSortOrder）----
    public const uint StepRoot = 0;
    public const uint StepSpeech = 1;
    public const uint StepContinue = 2;
    public const uint StepAccept = 3;
    public const uint StepDecline = 4;
    public const uint StepEnd = 6;
    public const uint StepLink = 7;
    /// <summary>8 = 官方 ConversationStep.Type.Random（旧文档称 selector，以官方枚举为准）。</summary>
    public const uint StepRandom = 8;
    public const uint StepSelector = 8;

    public static string CnvStepKind(uint type) => type switch
    {
        StepRoot => "root",
        StepSpeech => "speech",
        StepContinue => "choice: continue",
        StepAccept => "choice: accept",
        StepDecline => "choice: decline",
        5 => "generic",
        StepEnd => "end",
        StepLink => "link",
        StepRandom => "random",
        _ => "unknown kind",
    };

    /// <summary>
    /// 条件的"比较符"枚举（官方 Common.ScriptableCondition+Comparison）。
    /// 取值 0..5 由 `probe uid` 交官方编辑器实测逐值核对（见 CnvQstFormatDecoded.md §4.1）。
    /// </summary>
    public static string ComparisonSymbol(uint comparison) => comparison switch
    {
        0 => "<",
        1 => "<=",
        2 => "=",
        3 => ">=",
        4 => ">",
        5 => "!=",
        _ => $"?{comparison}",
    };

    public static string ComparisonName(uint comparison) => comparison switch
    {
        0 => "Less",
        1 => "LessEqual",
        2 => "Equal",
        3 => "GreaterEqual",
        4 => "Greater",
        5 => "NotEqual",
        _ => $"?{comparison}",
    };

    public static string? CnvStepTag(uint type) => type switch
    {
        StepContinue => "tagContinue",
        StepAccept => "tagAccept",
        StepDecline => "tagDecline",
        StepEnd => "tagEndConversation",
        StepLink => "tagContinue",   // 链接步沿用目标覆盖前的默认标签
        _ => null,
    };

    // ---- gd-utility 证实的类名（null = 尚无证据，按 TypeN 呈现）----
    private static readonly string?[] CondClassTable =
    {
        /* 0*/ "OnQuest",          /* 1*/ "QuestTaskComplete",
        /* 2*/ null, /* 3*/ null,  /* 4*/ null,  /* 5*/ null,
        /* 6*/ "HasFaction",       /* 7*/ null,
        /* 8*/ null,               /* 9*/ "HasItem",
        /*10*/ "HasKilled",        /*11*/ "HasQuestObjective",
        /*12*/ "LuaScript",        /*13*/ null,
        /*14*/ "OnQuestTask",      /*15*/ null,
        /*16*/ "HasKilledVariant", /*17*/ null,
        /*18*/ null,               /*19*/ "QuestBlocked",
        /*20*/ null,               /*21*/ "CheckRandomValue",
        /*22*/ null, /*23*/ null,  /*24*/ null,  /*25*/ null,
    };

    private static readonly string?[] ActionClassTable =
    {
        /* 0*/ null,               /* 1*/ "BeginQuestTask",
        /* 2*/ null,               /* 3*/ null,
        /* 4*/ "GiveMoney",        /* 5*/ null,
        /* 6*/ "GiveItem",         /* 7*/ "GiveLevel",
        /* 8*/ "GiveExperience",   /* 9*/ null,
        /*10*/ null,               /*11*/ "LuaScript",
        // 13：gd-utility 记作 SoundPakPreload（其自身亦未证实）；官方选项表有 PlaySound(SoundRecord)
        //     且 body=S 吻合，故此处不填，交由推断层给出 PlaySound（见 ActionInferredNames）。
        /*12*/ null,               /*13*/ null,
        /*14*/ null,               /*15*/ "GiveSkillPoint",
        /*16*/ null,               /*17*/ null,
        /*18*/ "GiveAttribPoint",  /*19*/ null,
        /*20*/ "GiveRandomItem",   /*21*/ "GenerateRandomValue",
        /*22*/ "CastSkill",        /*23*/ null,
        /*24*/ null,               /*25*/ "GiveDevotion",
        /*26*/ "GiveTribute",      /*27*/ "UnlockTutorial",
        /*28*/ "PlayVideo",
    };

    // ---- 本地锚定：编辑器名 ← 带证据的二进制类型 ID ----
    // 条件 7：npc_refugee_01.cnv type 7 + 串 "BLACKSMITH_APPRENTICE" ↔ 编辑器 HasToken。
    // 条件 18：mq_burrwitch.qst 目标 OR 双条件 (7=HasToken, 18) ↔ 编辑器 ServerHasToken。
    // 条件 4：mq_burrwitch.qst task4 的三个 OnComplete 事件 type=4 body I=0/1/2，且各自恰与
    //         编辑器 IsDifficulty = Normal/Epic/Legendary 同现（GiveExperience 350/22000/54000）。
    // 条件 17：mq_burrwitch.qst task4 目标唯一条件，空 body ↔ 编辑器 WaitForCompletion（无参数）。
    // 动作 14：npc_jailor_01.cnv 根节点唯一动作 type=14 body I=9 ↔ 编辑器 PlayAnimation
    //          （参数 9 = AnimationType 枚举 "Chat"）。
    // 动作 17：mq_burrwitch.qst task2 事件动作 type=17 + 串 "tagNotification_…" ↔ 编辑器 Notification(Tag)。
    // 动作 2：mq_burrwitch.qst task4 OnComplete 第二事件 type=2 + 串 "quests/mq_burrwitch.qst"
    //         ↔ 编辑器 CompleteQuest（同事件序列中其余三个动作均为 GiveExperience）。
    public static readonly Dictionary<uint, string> ConditionEditorNames = new()
    {
        [4] = "IsDifficulty",
        [7] = "HasToken",
        [17] = "WaitForCompletion",
        [18] = "ServerHasToken",
        // ---- 2026-09-28 官方编辑器直接辨认（probe_out 探测文件逐类型显示，见反馈截图）----
        [2] = "HasExperience",
        [3] = "IsLevel",
        [5] = "IsHardcore",
        [8] = "HasMoney",
        [13] = "KnowsPlayer",
        [15] = "UsedQuestItem",
        [16] = "HasKilledProxy",
        [20] = "AnyoneHasToken",
        [22] = "OwnsDLC",
        [23] = "HasTribute",
        [24] = "EarnedDevotion",
        [25] = "HasMastery",
        // ---- 2026-09-28 从官方编辑器内嵌 Common.dll 的类型枚举直接读到（0–29 共 30 种）----
        [26] = "IsChallenge",
        [27] = "HasBuff",
        [28] = "HasKilledMonsterClass",
        [29] = "HasKilledFaction",
    };

    public static readonly Dictionary<uint, string> ActionEditorNames = new()
    {
        [2] = "CompleteQuest",
        [14] = "PlayAnimation",
        [17] = "Notification",
        // ---- 2026-09-28 官方编辑器直接辨认 ----
        [3] = "CompleteQuestTask",
        [5] = "GiveFaction",
        [9] = "GiveToken",
        [10] = "RemoveToken",
        [12] = "DebugPrint",
        [13] = "PlaySound",
        [23] = "UnlockFaction",
        [24] = "SetFaction",
    };

    // ---- 推断层（现仅剩一项）----
    // 动作 19：body S，串全部 "EV_…" 前缀 → ScriptEvent（推断；编辑器截图该行被弹窗遮住未确认）。
    //
    // 2026-09-28：原先靠"body 形状唯一匹配"推断的 12 项，已全部由官方编辑器用探测文件
    // （`probe cond` / `probe act`，见 反馈截图/）直接显示确认，上移到 ConditionEditorNames /
    // ActionEditorNames。两处修正：
    //   · 条件 22 确为 OwnsDLC（"哈希条件"的旧称作废）；
    //   · 动作 9/10 确为 GiveToken / RemoveToken——与原先推断**相反**，已改正。
    // 官方条件下拉仍有 4 项未定位：IsChallenge、Hasbuff、HasKilledFaction、HasKilledMonsterClass
    // （二进制槽位已排满，只能是某个 type 的 subType 变体，见 `probe sub`）。
    public static readonly Dictionary<uint, string> ConditionInferredNames = new()
    {
    };

    public static readonly Dictionary<uint, string> ActionInferredNames = new()
    {
        [19] = "ScriptEvent",   // 编辑器截图里该行被弹窗遮住，未亲眼确认
    };

    /// <summary>有实例但仍无法命名的类型（样本 0 或编辑器无对应词）的短提示，UI 下拉/列表用。</summary>
    public static readonly Dictionary<uint, string> ConditionHints = new()
    {
    };

    /// <summary>下拉/列表用标签：已命名不含推断后缀；未命名带短提示。</summary>
    public static string ConditionLabel(uint type)
    {
        if (ConditionClass(type) is (string n, true))
            return InferredCondition(type) ? $"{n}（推断）" : n;
        string? hint = ConditionHints.GetValueOrDefault(type);
        return hint == null ? $"Type{type}（未命名）" : $"Type{type}（{hint}）";
    }

    public static string ActionLabel(uint type) =>
        ActionClass(type) is (string n, true)
            ? (InferredAction(type) ? $"{n}（推断）" : n)
            : $"Type{type}（未命名）";

    public static (string? name, bool named) ConditionClass(uint type) =>
        type < (uint)CondClassTable.Length && CondClassTable[type] is string n ? (n, true)
        : ConditionEditorNames.TryGetValue(type, out var e) ? (e, true)
        : ConditionInferredNames.TryGetValue(type, out var i) ? (i, true)
        : (null, false);

    public static (string? name, bool named) ActionClass(uint type) =>
        type < (uint)ActionClassTable.Length && ActionClassTable[type] is string n ? (n, true)
        : ActionEditorNames.TryGetValue(type, out var e) ? (e, true)
        : ActionInferredNames.TryGetValue(type, out var i) ? (i, true)
        : (null, false);

    /// <summary>名字是否出自推断层（UI 显示"（推断）"后缀）。</summary>
    public static bool InferredCondition(uint type) => ConditionInferredNames.ContainsKey(type);
    public static bool InferredAction(uint type) => ActionInferredNames.ContainsKey(type);

    public static string ConditionName(uint type) =>
        ConditionClass(type) is (string n, true) ? n : $"Type{type}";

    public static string ActionName(uint type) =>
        ActionClass(type) is (string n, true) ? n : $"Type{type}";

    /// <summary>条件集合 logic 字：0=AND / 1=OR（编辑器金样对照；未经引擎 API 证实）。</summary>
    public static string LogicWord(uint logic) => logic switch
    {
        0 => "AND",
        1 => "OR",
        _ => "?",
    };
}
