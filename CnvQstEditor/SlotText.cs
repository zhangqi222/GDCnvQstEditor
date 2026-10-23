using CnvQst;

namespace CnvQstEditor;

/// <summary>槽位名/建议文案的 GUI 本地化映射（CLI 用 SlotInfo.RoleZh，不经过这里）。</summary>
internal static class SlotText
{
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

    public static string Hint(SlotInfo s) =>
        s.Suggested ? L.T("Suggested")
        : s.MustBeEmpty ? L.T("Must be empty (reserved by format)")
        : s.IsLink ? L.T("May be empty") + " · " + L.T("Reuses the target node text")
        : L.T("May be empty");

    public static string Placeholder(SlotInfo s) =>
        s.Suggested ? L.T("Enter text here")
        : s.MustBeEmpty ? L.T("Must be empty (reserved by format)")
        : s.IsLink ? L.T("May be empty") + " · " + L.T("Reuses the target node text")
        : L.T("May be empty");
}
