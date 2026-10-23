using CnvQst;

namespace CnvQstEditor;

/// <summary>
/// 槽位 UI 文案的 GUI 本地化（CLI 用 SlotInfo.RoleZh / AdviceZh，不经过这里）。
/// 节点名/描述/建议三列均为固定内容，随 <see cref="RowMargin"/> 一次装载。
/// </summary>
internal static class SlotText
{
    /// <summary>槽位名（与可视化编辑器节点列同源：类型术语走 types 段括注）。</summary>
    public static string Role(SlotInfo s) => s.Kind switch
    {
        SlotKind.CnvRoot => L.T("Speaker name (root)"),
        SlotKind.CnvSpeech => L.TypeName("speech"),
        SlotKind.CnvContinue => L.TypeName("choice: continue"),
        SlotKind.CnvAccept => L.TypeName("choice: accept"),
        SlotKind.CnvDecline => L.TypeName("choice: decline"),
        SlotKind.CnvEnd => L.TypeName("end"),
        SlotKind.CnvLink => L.F("Link #{0}", s.ItemNo),
        SlotKind.CnvRandom => L.TypeName("random"),
        SlotKind.CnvGeneric => L.TypeName("generic"),
        SlotKind.CnvUnknown => L.TypeName("unknown kind"),
        SlotKind.QstGroup => L.T("Quest group name"),
        SlotKind.QstName => L.T("Quest name"),
        SlotKind.QstTaskTitle => L.F("T{0} title", s.TaskNo),
        SlotKind.QstTaskDescription => L.F("T{0} description", s.TaskNo),
        SlotKind.QstIncentive => L.F("T{0} OnAccept #{1}", s.TaskNo, s.ItemNo),
        SlotKind.QstObjective => L.F("T{0} objective #{1}", s.TaskNo, s.ItemNo),
        SlotKind.QstEvent => L.F("T{0} OnComplete #{1}", s.TaskNo, s.ItemNo),
        _ => L.T("(no matching slot)"),
    };

    /// <summary>简短节点描述（≤8 字级别的固定短语）。</summary>
    public static string Desc(SlotInfo s) => s.Kind switch
    {
        SlotKind.CnvRoot => L.T("Conversation start"),
        SlotKind.CnvSpeech => L.T("Main speech"),
        SlotKind.CnvContinue => L.T("Continue option"),
        SlotKind.CnvAccept => L.T("Accept option"),
        SlotKind.CnvDecline => L.T("Decline option"),
        SlotKind.CnvEnd => L.T("Closing line"),
        SlotKind.CnvLink => L.T("Link reference"),
        SlotKind.CnvRandom => L.T("Random branch"),
        SlotKind.CnvGeneric => L.T("Plain node"),
        SlotKind.CnvUnknown => L.T("Unrecognized"),
        SlotKind.QstGroup => L.T("Quest group"),
        SlotKind.QstName => L.T("Quest name"),
        SlotKind.QstTaskTitle => L.T("Quest task title"),
        SlotKind.QstTaskDescription => L.T("Quest task description"),
        SlotKind.QstIncentive => L.T("Fired on accept"),
        SlotKind.QstObjective => L.T("Quest objective"),
        SlotKind.QstEvent => L.T("Fired on complete"),
        _ => "",
    };

    /// <summary>槽位建议（肯定词，明确指令避免被忽略）。</summary>
    public static string Advice(SlotInfo s) =>
        s.Suggested ? L.T("Suggest entering content")
        : s.MustBeEmpty ? L.T("Must stay empty (reserved by format)")
        : s.IsLink ? L.T("May be empty (reuses target text)")
        : L.T("May be empty");

    /// <summary>单行占位提示（建议词同款，用于槽位视图的正文占位；合并视图已并入本地化内容，不再使用占位）。</summary>
    public static string Placeholder(SlotInfo s) => Advice(s);

    /// <summary>槽位建议的强调色：建议输入=accent、必须为空=warn、可为空=muted。</summary>
    public static Avalonia.Media.IBrush AdviceBrush(SlotInfo s) =>
        s.Suggested ? UiTheme.AccentBrush
        : s.MustBeEmpty ? UiTheme.WarnBrush
        : UiTheme.MutedBrush;

    /// <summary>节点名着色：与可视化编辑器同款（台词=accent、拒绝/结束=danger、跳转=success、
    /// QST task 标题=accent、描述/触发器=muted，其余正文色）。</summary>
    public static Avalonia.Media.IBrush Brush(SlotInfo s) => s.Kind switch
    {
        SlotKind.CnvSpeech => UiTheme.AccentBrush,          // 台词（可视化 type=1 同款）
        SlotKind.CnvDecline or SlotKind.CnvEnd => UiTheme.DangerBrush,
        SlotKind.CnvLink => UiTheme.OkBrush,                // 跳转
        SlotKind.CnvUnknown => UiTheme.UnknownBrush,
        SlotKind.QstTaskTitle => UiTheme.AccentBrush,       // task 标题（QstTaskRow 同款）
        SlotKind.QstTaskDescription => UiTheme.MutedBrush,  // 描述行（QstTaskDescRow 同款）
        SlotKind.QstIncentive or SlotKind.QstEvent => UiTheme.MutedBrush,
        _ => UiTheme.TextBrush,
    };
}
