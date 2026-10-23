using System.Text;
using CnvQst.Cli;

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
/// （CNV link 的 ItemNo 是跳转目标节点下标）。Desc 为该槽的简短语义描述（中文基准，GUI/CLI 各自本地化）。
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

    /// <summary>简短节点描述（中文基准，经 CliL 本地化；尽量 ≤8 字，GUI/CLI 明细列共用）。</summary>
    public string Desc => CliL.T(Kind switch
    {
        SlotKind.CnvRoot => "对话起点",
        SlotKind.CnvSpeech => "正文台词",
        SlotKind.CnvContinue => "继续选项",
        SlotKind.CnvAccept => "接受选项",
        SlotKind.CnvDecline => "拒绝选项",
        SlotKind.CnvEnd => "结束台词",
        SlotKind.CnvLink => "引用跳转",
        SlotKind.CnvRandom => "随机分支",
        SlotKind.CnvGeneric => "普通节点",
        SlotKind.CnvUnknown => "未识别",
        SlotKind.QstGroup => "任务分组",
        SlotKind.QstName => "任务名称",
        SlotKind.QstTaskTitle => "任务标题",
        SlotKind.QstTaskDescription => "任务描述",
        SlotKind.QstIncentive => "接受时触发",
        SlotKind.QstObjective => "任务目标",
        SlotKind.QstEvent => "完成时触发",
        _ => "",
    });

    /// <summary>CLI 用的槽位名（中文基准，经 CliL 本地化；GUI 走语言包，不直接使用）。</summary>
    public string RoleZh => Kind switch
    {
        SlotKind.CnvRoot => CliL.T("说话人名（根节点）"),
        SlotKind.CnvSpeech => CliL.T("台词"),
        SlotKind.CnvContinue => CliL.T("继续选项"),
        SlotKind.CnvAccept => CliL.T("接受选项"),
        SlotKind.CnvDecline => CliL.T("拒绝选项"),
        SlotKind.CnvEnd => CliL.T("结束对话"),
        SlotKind.CnvLink => CliL.F("跳转 #{0}", ItemNo),
        SlotKind.CnvRandom => CliL.T("随机选项"),
        SlotKind.CnvGeneric => CliL.T("通用节点"),
        SlotKind.CnvUnknown => CliL.T("未知类型"),
        SlotKind.QstGroup => CliL.T("任务组名"),
        SlotKind.QstName => CliL.T("任务名"),
        SlotKind.QstTaskTitle => CliL.F("T{0} 标题", TaskNo),
        SlotKind.QstTaskDescription => CliL.F("T{0} 描述", TaskNo),
        SlotKind.QstIncentive => CliL.F("T{0} OnAccept #{1}", TaskNo, ItemNo),
        SlotKind.QstObjective => CliL.F("T{0} 目标 #{1}", TaskNo, ItemNo),
        SlotKind.QstEvent => CliL.F("T{0} OnComplete #{1}", TaskNo, ItemNo),
        _ => CliL.F("槽位 {0}", Index + 1),
    };

    /// <summary>CLI 用的填写建议（肯定词，避免被忽略）：建议输入 / 必须为空 / 可为空。</summary>
    public string AdviceZh => Suggested ? CliL.T("建议输入内容")
        : MustBeEmpty ? CliL.T("必须为空（格式保留）")
        : IsLink ? CliL.T("可为空（复用目标文本）")
        : CliL.T("可为空");
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

    /// <summary>单文件/单模型的完整槽位明细（CLI 输出）：行号 / 槽位名 / 描述 / 建议 / 当前内容。</summary>
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
        int emptySuggested = slots.Count(s => s.Suggested && !IsFilled(lang, s.Index));

        var sb = new StringBuilder();
        sb.AppendLine(mustEmpty > 0
            ? CliL.F("{0} 槽位明细：总槽位 {1} 行，建议填写 {2} 行，可为空 {3} 行，格式保留必须为空 {4} 行。", kind, slots.Count, suggested, optional, mustEmpty)
            : CliL.F("{0} 槽位明细：总槽位 {1} 行，建议填写 {2} 行，可为空 {3} 行。", kind, slots.Count, suggested, optional));
        if (lang != null)
        {
            string mark = lang.Texts.Count == slots.Count ? "" : CliL.T("（与应有槽位数不一致）");
            sb.AppendLine(CliL.F("语言 {0}：实际 {1} 行{2}。", lang.Tag.Text, lang.Texts.Count, mark));
        }
        if (emptySuggested > 0)
            sb.AppendLine(CliL.F("其中建议输入但当前为空：{0} 行（只提醒，不影响转换）。", emptySuggested));
        sb.AppendLine(CliL.T("换行请写 {^n}；建议仅为提醒，不阻止转换；如发现未对齐，请在可视化编辑器（GUI）中修改。"));
        sb.AppendLine(Pad(CliL.T("  行"), 6) + Pad(CliL.T("槽位名"), 26) + Pad(CliL.T("描述"), 11) + Pad(CliL.T("建议"), 22) + CliL.T("内容"));
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            string content = OneLine(Filled(lang, s.Index));
            sb.AppendLine(Pad($"  {i + 1}", 6) + Pad(s.RoleZh, 26) + Pad(s.Desc, 11)
                + Pad(s.AdviceZh, 22) + (content.Length > 0 ? content : CliL.T("（空）")));
        }
        return sb.ToString();
    }

    private static string Filled(LanguageBlock? lang, int index) =>
        lang != null && index < lang.Texts.Count ? lang.Texts[index].Text : "";

    private static bool IsFilled(LanguageBlock? lang, int index) => Filled(lang, index).Length > 0;

    /// <summary>内容压成单行并截断（CLI 终端宽度有限；{^n} 表示换行）。</summary>
    private static string OneLine(string text)
    {
        text = text.Replace("\r\n", "{^n}").Replace("\n", "{^n}").Replace("\r", "{^n}");
        return text.Length <= 40 ? text : text[..40] + "…";
    }

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
