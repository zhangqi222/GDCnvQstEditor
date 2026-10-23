using System.Text;

namespace CnvQst;

/// <summary>槽位语义：决定槽位名、是否建议填写、是否必须留空。</summary>
public enum SlotKind
{
    CnvRoot,
    CnvSpeech,
    CnvContinue,
    CnvAccept,
    CnvDecline,
    CnvEnd,
    CnvLink,
    CnvRandom,
    CnvGeneric,
    CnvUnknown,
    QstGroup,
    QstName,
    QstTaskTitle,
    QstTaskDescription,
    QstIncentive,
    QstObjective,
    QstEvent,
}

/// <summary>
/// 一个本地化槽位的结构信息。Index 为 0 起的槽位下标；TaskNo / ItemNo 为 1 起的显示序号
/// （CNV link 的 ItemNo 是跳转目标节点下标）。
/// </summary>
public sealed record SlotInfo(
    int Index,
    SlotKind Kind,
    bool Suggested,
    bool MustBeEmpty,
    int TaskNo = 0,
    int ItemNo = 0)
{
    public bool IsLink => Kind == SlotKind.CnvLink;

    /// <summary>CLI 用的中文槽位名（GUI 走语言包，不直接使用）。</summary>
    public string RoleZh => Kind switch
    {
        SlotKind.CnvRoot => "说话人名（根节点）",
        SlotKind.CnvSpeech => "台词",
        SlotKind.CnvContinue => "继续选项",
        SlotKind.CnvAccept => "接受选项",
        SlotKind.CnvDecline => "拒绝选项",
        SlotKind.CnvEnd => "结束对话",
        SlotKind.CnvLink => $"跳转 #{ItemNo}",
        SlotKind.CnvRandom => "随机选项",
        SlotKind.CnvGeneric => "通用节点",
        SlotKind.CnvUnknown => "未知类型",
        SlotKind.QstGroup => "任务组名",
        SlotKind.QstName => "任务名",
        SlotKind.QstTaskTitle => $"T{TaskNo} 标题",
        SlotKind.QstTaskDescription => $"T{TaskNo} 描述",
        SlotKind.QstIncentive => $"T{TaskNo} OnAccept #{ItemNo}",
        SlotKind.QstObjective => $"T{TaskNo} 目标 #{ItemNo}",
        SlotKind.QstEvent => $"T{TaskNo} OnComplete #{ItemNo}",
        _ => $"槽位 {Index + 1}",
    };
}

/// <summary>
/// 按结构规则生成槽位清单。推荐规则只做提醒，不参与“行数对齐”的硬校验：
///   · 建议填写：CNV 说话人名 / 台词 / 结束语 / 主要选项；QST 组名 / 任务名 / 标题 / 目标。
///   · 可为空：CNV 跳转、随机、通用节点；QST 任务描述。
///   · 应为空：QST OnAccept / OnComplete 的本地化槽，官方格式保留为空。
/// </summary>
public static class SlotGuide
{
    public static List<SlotInfo> ForCnv(CnvFile c) =>
        c.Steps.Select(s => new SlotInfo(
            s.Index,
            KindOf(s),
            s.Type is Names.StepRoot or Names.StepSpeech or Names.StepEnd or Names.StepContinue
                or Names.StepAccept or Names.StepDecline,
            false,
            ItemNo: s.Type == Names.StepLink ? (int)s.LinkId : 0)).ToList();

    public static List<SlotInfo> ForQst(QstFile q)
    {
        var slots = new List<SlotInfo>
        {
            new(0, SlotKind.QstGroup, true, false),
            new(1, SlotKind.QstName, true, false),
        };
        int index = 2;
        for (int ti = 0; ti < q.Tasks.Count; ti++)
        {
            var t = q.Tasks[ti];
            int taskNo = ti + 1;
            slots.Add(new SlotInfo(index++, SlotKind.QstTaskTitle, true, false, taskNo));
            slots.Add(new SlotInfo(index++, SlotKind.QstTaskDescription, false, false, taskNo));
            for (int k = 0; k < t.Incentives.Count; k++)
                slots.Add(new SlotInfo(index++, SlotKind.QstIncentive, false, true, taskNo, k + 1));
            for (int k = 0; k < t.Objectives.Count; k++)
                slots.Add(new SlotInfo(index++, SlotKind.QstObjective, true, false, taskNo, k + 1));
            for (int k = 0; k < t.Events.Count; k++)
                slots.Add(new SlotInfo(index++, SlotKind.QstEvent, false, true, taskNo, k + 1));
        }
        return slots;
    }

    public static int SuggestedCount(object model) => model switch
    {
        CnvFile c => ForCnv(c).Count(s => s.Suggested),
        QstFile q => ForQst(q).Count(s => s.Suggested),
        _ => 0,
    };

    public static int SuggestedCount(IEnumerable<SlotInfo> slots) => slots.Count(s => s.Suggested);

    public static string HintZh(SlotInfo slot) =>
        slot.Suggested ? "请输入文本内容"
        : slot.MustBeEmpty ? "应为空（格式保留）"
        : slot.IsLink ? "可为空（复用目标节点文本）"
        : "可为空";

    /// <summary>单文件/单模型的完整槽位明细（CLI 输出）。</summary>
    public static string Report(object model) => model switch
    {
        CnvFile c => Report(ForCnv(c), "CNV", Primary(c)),
        QstFile q => Report(ForQst(q), "QST", Primary(q)),
        _ => "",
    };

    private static string Report(List<SlotInfo> slots, string kind, LanguageBlock? lang)
    {
        int suggested = slots.Count(s => s.Suggested);
        int mustEmpty = slots.Count(s => s.MustBeEmpty);
        int optional = slots.Count - suggested - mustEmpty;
        var hints = slots.Select((s, i) => s.Suggested && !IsFilled(lang, i)
            ? HintZh(s) + "（当前为空）"
            : HintZh(s)).ToList();
        int emptySuggested = hints.Count(h => h.EndsWith("（当前为空）", StringComparison.Ordinal));

        var sb = new StringBuilder();
        sb.AppendLine($"{kind} 槽位明细：总槽位 {slots.Count} 行，建议填写 {suggested} 行，可为空 {optional} 行"
            + (mustEmpty > 0 ? $"，格式保留应为空 {mustEmpty} 行。" : "。"));
        if (lang != null)
        {
            string mark = lang.Texts.Count == slots.Count ? "" : "（与应有槽位数不一致）";
            sb.AppendLine($"语言 {lang.Tag.Text}：实际 {lang.Texts.Count} 行{mark}。");
        }
        if (emptySuggested > 0)
            sb.AppendLine($"其中建议填写但当前为空：{emptySuggested} 行（只提醒，不影响转换）。");
        sb.AppendLine("换行请写 {^n}；建议填写仅为提醒，不阻止转换。");
        sb.AppendLine(Pad("  行", 6) + Pad("槽位名", 30) + "建议");
        for (int i = 0; i < slots.Count; i++)
            sb.AppendLine(Pad($"  {i + 1}", 6) + Pad(slots[i].RoleZh, 30) + hints[i]);
        return sb.ToString();
    }

    private static bool IsFilled(LanguageBlock? lang, int index) =>
        lang != null && index < lang.Texts.Count && lang.Texts[index].Text.Length > 0;

    /// <summary>按终端显示宽度补空格（中日韩字符占 2 列），让明细表列对齐。</summary>
    private static string Pad(string text, int width)
    {
        int len = 0;
        foreach (char ch in text) len += IsWide(ch) ? 2 : 1;
        return text + new string(' ', Math.Max(1, width - len));
    }

    private static bool IsWide(char c) =>
        c >= 0x1100 && (
            c <= 0x115F ||                                  // 韩文字母
            (c >= 0x2E80 && c <= 0xA4CF && c != 0x303F) ||  // CJK 部首 / 汉字 / 假名
            (c >= 0xAC00 && c <= 0xD7A3) ||                 // 韩文音节
            (c >= 0xF900 && c <= 0xFAFF) ||                 // CJK 兼容汉字
            (c >= 0xFE30 && c <= 0xFE6F) ||                 // CJK 兼容标点
            (c >= 0xFF00 && c <= 0xFF60) ||                 // 全角字符
            (c >= 0xFFE0 && c <= 0xFFE6));

    private static SlotKind KindOf(CnvStep s) => s.Type switch
    {
        Names.StepRoot => SlotKind.CnvRoot,
        Names.StepSpeech => SlotKind.CnvSpeech,
        Names.StepContinue => SlotKind.CnvContinue,
        Names.StepAccept => SlotKind.CnvAccept,
        Names.StepDecline => SlotKind.CnvDecline,
        Names.StepEnd => SlotKind.CnvEnd,
        Names.StepLink => SlotKind.CnvLink,
        Names.StepRandom => SlotKind.CnvRandom,
        5 => SlotKind.CnvGeneric,
        _ => SlotKind.CnvUnknown,
    };

    private static LanguageBlock? Primary(CnvFile c) =>
        c.Languages.FirstOrDefault(l => l.Tag.Text == "enUS") ?? c.Languages.FirstOrDefault();

    private static LanguageBlock? Primary(QstFile q) =>
        q.Languages.FirstOrDefault(l => l.Tag.Text == "enUS") ?? q.Languages.FirstOrDefault();
}
