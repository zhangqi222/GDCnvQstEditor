using System.Text;

namespace GDCnvQstLib;

/// <summary>编辑器风格的树形文本输出（dump 命令），附 tiling/locOffset 两道完整性检查结果。</summary>
public static class Dump
{
    public static string DumpCnv(CnvFile c, string fileName)
    {
        var sb = new StringBuilder();
        if (!c.Valid)
        {
            sb.AppendLine($"═══ {fileName} ── 解析失败：{c.Error}");
            return sb.ToString();
        }

        int records = c.Steps.Sum(s => s.Conditions.Records.Count + s.Actions.Records.Count);
        sb.AppendLine($"═══ {fileName} ── CNV v{c.Version} ── id 0x{c.Id:X8} ── {c.Steps.Count} steps ── {records} 条件/动作 ── {c.Size} B");
        foreach (var lang in c.Languages)
            sb.AppendLine($"loc: {lang.Tag.Text}  {lang.Texts.Count} 条文本  @0x{lang.Offset:X}");
        sb.AppendLine($"{(c.Tiled ? "✓" : "✗")} 记录恰好铺满文件   {(c.LocOffsetMatches ? "✓" : "✗")} 节点走序落在声明的本地化偏移 (0x{c.LocalizationOffset:X})");
        sb.AppendLine();

        Walk(sb, c.Root, "", true, c);
        return sb.ToString();
    }

    private static void Walk(StringBuilder sb, CnvStep s, string prefix, bool last, CnvFile c)
    {
        string branch = s.Depth > 0 ? prefix + (last ? "└─ " : "├─ ") : "";
        string pad = prefix + (s.Depth > 0 ? (last ? "   " : "│  ") : "");

        string label = $"[{s.Index,3}] {s.Kind}";
        if (s.Tag != null) label += $" {{{s.Tag}}}";
        sb.AppendLine($"{branch}{label}   @0x{s.Offset:X} +{s.Length}");

        string text = c.TextFor(s);
        if (text.Length > 0) sb.AppendLine($"{pad}    \"{text}\"");
        if (!s.VoPath.IsEmpty) sb.AppendLine($"{pad}    vo: {s.VoPath.Text}");
        if (s.HasLink) sb.AppendLine($"{pad}    -> [{s.LinkId}]");
        if (s.Unknown != 0) sb.AppendLine($"{pad}    flags = 0x{s.Unknown:X}   <- 仅 speech 步非零，位标志（0x4/0x8/0x10/0x20；编辑器候选 Once Only/Always Show/Unimportant，未经证实）");

        if (s.Conditions.Present)
            WriteList(sb, pad + "    ", "Conditions", s.Conditions);
        if (s.Actions.Present)
            WriteList(sb, pad + "    ", "Actions", s.Actions);

        for (int i = 0; i < s.Children.Count; i++)
            Walk(sb, s.Children[i], pad, i == s.Children.Count - 1, c);
    }

    public static string DumpQst(QstFile q, string fileName)
    {
        var sb = new StringBuilder();
        if (!q.Valid)
        {
            sb.AppendLine($"═══ {fileName} ── 解析失败：{q.Error}");
            return sb.ToString();
        }

        sb.AppendLine($"═══ {fileName} ── QST2 v{q.Version} ── uid 0x{q.Id:X8} ── flags {Names.QuestFlagTag(q.Unknown10)} ── {q.Tasks.Count} task ── {q.ObjectiveCount} objective ── {q.RecordCount} 条件/动作 ── {q.Size} B");
        foreach (var lang in q.Languages)
        {
            sb.AppendLine($"loc: {lang.Tag.Text}  {lang.Texts.Count} 条文本  @0x{lang.Offset:X}（公式 2+2T+O+I+E = {q.RequiredTextCount}）");
            for (int i = 0; i < lang.Texts.Count; i++)
            {
                if (lang.Texts[i].IsEmpty) continue;
                sb.AppendLine($"   [{i,2}] {q.DescribeSlot(i),-22} \"{lang.Texts[i].Text}\"");
            }
        }
        sb.AppendLine($"{(q.Tiled ? "✓" : "✗")} 记录恰好铺满文件   {(q.LocOffsetMatches ? "✓" : "✗")} task 记录落在声明的本地化偏移 (0x{q.LocalizationOffset:X})");
        sb.AppendLine();

        foreach (var t in q.Tasks)
        {
            sb.AppendLine($"Task  @0x{t.Offset:X} +{t.Length}   uid=0x{t.UnknownA8:X8} flags={t.UnknownAC}" +
                          $"   isBlocker={(t.Flag8 ? 1 : 0)} dontPropagate={(t.Flag9 ? 1 : 0)}" +
                          $"   （运行时 uid {t.Uid},{t.Uid2}）");
            WriteEvents(sb, "Incentives", t.Incentives);
            sb.AppendLine($"   Objectives ({t.Objectives.Count})");
            foreach (var o in t.Objectives)
            {
                sb.AppendLine($"      Objective  uid=0x{o.Unknown0:X8} flags={o.Unknown1}   @0x{o.Offset:X}");
                WriteList(sb, "         ", "Conditions", o.Conditions);
                WriteList(sb, "         ", "Actions", o.Actions);
            }
            WriteEvents(sb, "Events", t.Events);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static void WriteEvents(StringBuilder sb, string label, List<QstEvent> events)
    {
        sb.AppendLine($"   {label} ({events.Count})");
        foreach (var e in events)
        {
            sb.AppendLine($"      Event  flags={e.Unknown}   @0x{e.Offset:X}");
            WriteList(sb, "         ", "Conditions", e.Conditions);
            WriteList(sb, "         ", "Actions", e.Actions);
        }
    }

    private static void WriteList(StringBuilder sb, string pad, string label, ScriptList list)
    {
        if (!list.Present) return;
        sb.Append($"{pad}{label} ({list.Records.Count})");
        sb.AppendLine($" logic={list.Logic}{LogicGuess(list.Logic)}   @0x{list.Offset:X}");
        foreach (var rec in list.Records)
            sb.AppendLine($"{pad}  {(rec.IsCondition ? "⚑" : "⚡")} {Describe(rec)}{Compare(rec)}   @0x{rec.Offset:X} +{rec.Length}");
    }

    /// <summary>条件的比较符（type / body 版本 之后那个字，官方 Common.ScriptableCondition+Comparison）。</summary>
    private static string Compare(ScriptRecord rec) =>
        rec.IsCondition ? $"  [{Names.ComparisonSymbol(rec.Uid)}]" : "";

    /// <summary>logic 字：0=AND、1=OR。alex_01/jailor_01(AND)/mq_burrwitch(OR) 编辑器金样对照，
    /// 全量语料与 446 样本一致；未经引擎 API 证实。</summary>
    private static string LogicGuess(uint logic) => logic switch
    {
        0 => "(AND)",
        1 => "(OR)",
        _ => "",
    };

    /// <summary>单条条件/动作的行内描述，字段值逗号连接（布尔 0/1 → False/True，同编辑器习惯）。</summary>
    public static string Describe(ScriptRecord rec)
    {
        var parts = new List<string>();
        foreach (var f in rec.Fields)
        {
            switch (f.Kind)
            {
                case FieldKind.Str:
                    parts.Add($"\"{f.S.Text}\"");
                    break;
                case FieldKind.U32:
                    // 大数值多为任务/资源 hash（编辑器显示 "?"），用 hex 更可读
                    parts.Add(f.U >= 0x10000 ? $"0x{f.U:X8}" : f.U.ToString());
                    break;
                case FieldKind.U8:
                    parts.Add(f.B != 0 ? "True" : "False");
                    break;
                case FieldKind.P:
                    parts.Add(string.Join(" ; ", f.Paths!.Select(p => p.Text)));
                    if (f.Trailing != 0) parts.Add($"tail={f.Trailing}");
                    break;
                case FieldKind.Lua:
                    string args = string.Join(", ", f.Params!.Select(p => p.S.Text));
                    parts.Add(f.Tail != null ? $"{f.S.Text}({args}) [tail]" : $"{f.S.Text}({args})");
                    break;
            }
        }
        string sub = rec.SubType != 0 ? $" sub={rec.SubType}" : "";
        return $"{rec.DisplayName}{sub} {string.Join(", ", parts)}".TrimEnd();
    }
}
