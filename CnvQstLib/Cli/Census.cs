using System.Text;

namespace CnvQst.Cli;

/// <summary>
/// census 命令：遍历全部 .cnv/.qst 样本，按类型 ID 统计条件/动作的出现次数、
/// body 字面值、subType、同列表共现的已命名类型、所在列表的 logic 字；
/// 并对 CNV 每步的 unknown 字做 (step 类型 × 取值) 相关普查。
/// 输出是一份"命名证据表"，供类型 ID ↔ 编辑器名称的锚定决策。
/// </summary>
public static class Census
{
    sealed class TypeCensus
    {
        public uint Type;
        public int Count;
        public int Lists;                                   // 类型所在的条件/动作集合数
        public Dictionary<byte, int> SubTypes = new();      // subType 分布
        public Dictionary<uint, int> Uids = new();          // 条件 uid 字段分布（动作无 uid）
        public SortedDictionary<uint, int> Numbers = new(); // U32 body 字段取值（<0x10000）
        public SortedDictionary<uint, int> BigNums = new();  // U32 ≥0x10000（hash 形态）取值
        public Dictionary<byte, int> Bytes = new();         // U8 body 字段
        public Dictionary<string, int> Strings = new();     // Str 字段字面值
        public Dictionary<string, int> PathSets = new();    // P 字段路径集
        public Dictionary<uint, int> Trailings = new();     // P 字段尾字
        public Dictionary<string, int> Luas = new();        // Lua call 串
        public Dictionary<string, int> CoNamed = new();     // 同列表其他记录 DisplayName
        public Dictionary<uint, int> ListLogic = new();     // 所在列表 logic 字
    }

    sealed class StepUnknownCensus
    {
        public int Count;
        public SortedDictionary<uint, int> Values = new();  // unknown 取值分布
    }

    public static string Report(IEnumerable<string> files)
    {
        var cond = new Dictionary<uint, TypeCensus>();
        var act = new Dictionary<uint, TypeCensus>();
        var qstShapes = new List<(string file, int T, int I, int O, int E, int S)>();
        var stepUnknown = new Dictionary<uint, StepUnknownCensus>();   // key = step Type
        var logic = new Dictionary<uint, int>();                        // logic 字 → 列表数
        var logicByLen = new Dictionary<(uint, int), int>();            // (logic, recordCount) → 列表数
        int filesCn = 0, filesQs = 0, filesBad = 0, totalLists = 0;

        void Visit(ScriptList list, bool isCondition)
        {
            totalLists++;
            var table = isCondition ? cond : act;
            if (list.Present)
            {
                logic[list.Logic] = logic.GetValueOrDefault(list.Logic) + 1;
                logicByLen[(list.Logic, list.Records.Count)] =
                    logicByLen.GetValueOrDefault((list.Logic, list.Records.Count)) + 1;
            }
            foreach (var rec in list.Records)
            {
                if (!table.TryGetValue(rec.Type, out var tc))
                    table[rec.Type] = tc = new TypeCensus { Type = rec.Type };
                tc.Count++;
                tc.SubTypes[rec.SubType] = tc.SubTypes.GetValueOrDefault(rec.SubType) + 1;
                if (rec.IsCondition) tc.Uids[rec.Uid] = tc.Uids.GetValueOrDefault(rec.Uid) + 1;
                if (list.Present)
                {
                    tc.Lists++;
                    tc.ListLogic[list.Logic] = tc.ListLogic.GetValueOrDefault(list.Logic) + 1;
                }
                foreach (var f in rec.Fields)
                {
                    switch (f.Kind)
                    {
                        case FieldKind.Str:
                            if (f.S.Text.Length > 0) tc.Strings[f.S.Text] = tc.Strings.GetValueOrDefault(f.S.Text) + 1;
                            break;
                        case FieldKind.U32:
                            if (f.U < 0x10000) tc.Numbers[f.U] = tc.Numbers.GetValueOrDefault(f.U) + 1;
                            else tc.BigNums[f.U] = tc.BigNums.GetValueOrDefault(f.U) + 1;
                            break;
                        case FieldKind.U8:
                            tc.Bytes[f.B] = tc.Bytes.GetValueOrDefault(f.B) + 1;
                            break;
                        case FieldKind.P:
                            string joined = string.Join(" ; ", f.Paths!.Select(p => p.Text));
                            if (joined.Length > 0) tc.PathSets[joined] = tc.PathSets.GetValueOrDefault(joined) + 1;
                            if (f.Trailing is uint tr && tr < 0x10000)
                                tc.Trailings[tr] = tc.Trailings.GetValueOrDefault(tr) + 1;
                            break;
                        case FieldKind.Lua:
                            string call = f.S.Text.Length > 0 ? f.S.Text : "(empty)";
                            tc.Luas[call] = tc.Luas.GetValueOrDefault(call) + 1;
                            break;
                    }
                }
            }
            // 共现：同列表内其他记录的显示名（协助判别归属）
            if (list.Records.Count > 1)
            {
                foreach (var rec in list.Records)
                {
                    if (!table.TryGetValue(rec.Type, out var tc)) continue;
                    foreach (var other in list.Records)
                    {
                        if (other == rec) continue;
                        tc.CoNamed[other.DisplayName] = tc.CoNamed.GetValueOrDefault(other.DisplayName) + 1;
                    }
                }
            }
        }

        foreach (string file in files)
        {
            byte[] data;
            try { data = File.ReadAllBytes(file); }
            catch { continue; }
            object model;
            try { model = BatchOps.Load(file, data); }
            catch { filesBad++; continue; }

            switch (model)
            {
                case CnvFile c:
                    if (!c.Valid) { filesBad++; break; }
                    filesCn++;
                    foreach (var s in c.Steps)
                    {
                        if (!stepUnknown.TryGetValue(s.Type, out var su))
                            stepUnknown[s.Type] = su = new StepUnknownCensus();
                        su.Count++;
                        su.Values[s.Unknown] = su.Values.GetValueOrDefault(s.Unknown) + 1;
                        Visit(s.Conditions, true);
                        Visit(s.Actions, false);
                    }
                    break;
                case QstFile q:
                    if (!q.Valid) { filesBad++; break; }
                    filesQs++;
                    {
                        int ti = q.Tasks.Count;
                        int ii = q.Tasks.Sum(t => t.Incentives.Count);
                        int oi = q.Tasks.Sum(t => t.Objectives.Count);
                        int ei = q.Tasks.Sum(t => t.Events.Count);
                        int si = q.Languages.Count > 0 ? q.Languages[0].Texts.Count : 0;
                        qstShapes.Add((Path.GetFileName(file), ti, ii, oi, ei, si));
                    }
                    foreach (var t in q.Tasks)
                    {
                        foreach (var e in t.Incentives) { Visit(e.Conditions, true); Visit(e.Actions, false); }
                        foreach (var o in t.Objectives) { Visit(o.Conditions, true); Visit(o.Actions, false); }
                        foreach (var e in t.Events) { Visit(e.Conditions, true); Visit(e.Actions, false); }
                    }
                    break;
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine(CliL.F("═══ census ═══ 文件 {0} cnv + {1} qst，解析失败 {2}，ScriptList 总数 {3}",
            filesCn, filesQs, filesBad, totalLists));

        sb.AppendLine();
        sb.AppendLine(CliL.T("── 条件集合 logic 字普查 ──"));
        foreach (var (lg, n) in logic.OrderBy(k => k.Key))
            sb.AppendLine(CliL.F("logic={0}: {1} 个列表", lg, n));
        sb.AppendLine(CliL.T("logic × 列表记录数："));
        foreach (var ((lg, len), n) in logicByLen.OrderBy(k => k.Key))
            sb.AppendLine(CliL.F("  logic={0} × 长度 {1}: {2}", lg, len, n));

        sb.AppendLine();
        sb.AppendLine(CliL.T("── QST 本地化槽位配平（串表条数 vs 结构）──"));
        sb.AppendLine(CliL.T("T=task 数  I=incentives(OnAccept) 数  O=objective 数  E=events(OnComplete) 数  S=串表条数"));
        sb.AppendLine(CliL.T("基准 = 2(组名+任务名) + 2T(每 task 标题+描述) + O(每 objective 一条)"));
        sb.AppendLine(CliL.T("形状 (T,I,O,E) : S 取值 → extra = S-基准（extra 与哪个计数相等，就是那部分的额外预留槽）"));
        foreach (var g in qstShapes
                     .GroupBy(s => (s.T, s.I, s.O, s.E))
                     .OrderBy(g => g.Key.T).ThenBy(g => g.Key.O).ThenBy(g => g.Key.I).ThenBy(g => g.Key.E))
        {
            var (t, i, o, e) = g.Key;
            int baseV = 2 + 2 * t + o;
            var ss = g.Select(x => x.S).Distinct().OrderBy(v => v).ToList();
            string sv = string.Join("/", ss.Select(v => CliL.F("{0}(extra {1})", v, v - baseV)));
            sb.AppendLine(CliL.F("  T={0} I={1} O={2} E={3} ×{4} : S = {5}", t, i, o, e, g.Count(), sv));
        }
        sb.AppendLine(CliL.T("残差统计（S - 基准 - 各计数）："));
        foreach (var cand in new (string name, Func<(string file, int T, int I, int O, int E, int S), int> f)[]
                 {
                     ("E", s => s.E), ("I", s => s.I), ("T", s => s.T), ("O", s => s.O),
                     ("I+E", s => s.I + s.E), ("E+T", s => s.E + s.T), ("2E+T", s => 2 * s.E + s.T),
                 })
        {
            var res = qstShapes.GroupBy(s => s.S - (2 + 2 * s.T + s.O) - cand.f(s))
                               .OrderByDescending(g => g.Count()).Take(3)
                               .Select(g => $"{g.Key}×{g.Count()}");
            sb.AppendLine(CliL.F("  S - 基准 - {0} → {1}", cand.name, string.Join("  ", res)));
        }

        sb.AppendLine();
        sb.AppendLine(CliL.T("── CNV step unknown 字 × step 类型 ──"));
        foreach (var (type, su) in stepUnknown.OrderBy(k => k.Key))
        {
            sb.AppendLine(CliL.F("[step type {0} ({1})] n={2}", type, Names.CnvStepKind(type), su.Count));
            var top = su.Values.OrderByDescending(k => k.Value).ToList();
            foreach (var (v, n) in top.Take(14))
                sb.AppendLine($"    unknown=0x{v:X} ({v}) × {n}");
            if (top.Count > 14) sb.AppendLine(CliL.F("    … 共 {0} 种取值", top.Count));
        }

        void DumpTable(string title, IEnumerable<TypeCensus> rows, bool isCondition)
        {
            sb.AppendLine();
            sb.AppendLine(CliL.F("── {0} ──", title));
            foreach (var tc in rows.OrderBy(r => r.Count).ThenBy(r => r.Lists).ThenByDescending(r => r.Lists))
            {
                bool named = isCondition ? Names.ConditionClass(tc.Type).named : Names.ActionClass(tc.Type).named;
                string name = isCondition ? Names.ConditionName(tc.Type) : Names.ActionName(tc.Type);
                string spec = isCondition ? Scriptable.ConditionBodySpec(tc.Type) : Scriptable.ActionBodySpec(tc.Type);
                sb.AppendLine(CliL.F("type={0,2}  n={1,5}  lists={2,4}  {3}  body={4}{5}",
                    tc.Type, tc.Count, tc.Lists, name, spec, named ? "" : CliL.T("  ← 未命名")));
                if (tc.SubTypes.Count > 1 || (tc.SubTypes.Count == 1 && tc.SubTypes.First().Key != 0))
                    sb.AppendLine($"    subType: {string.Join(" ", tc.SubTypes.OrderBy(k => k.Key).Select(k => $"sub{k.Key}×{k.Value}"))}");
                if (isCondition && tc.Uids.Count > 0)
                {
                    var topUid = tc.Uids.OrderBy(k => k.Key).ToList();
                    sb.AppendLine($"    uid: {string.Join(" ", topUid.Take(12).Select(k => (k.Key <= 6 ? $"0x{k.Key:X}" : k.Key.ToString()) + (k.Key <= 6 ? "" : $"") + $"×{k.Value}"))}" +
                                  (topUid.Count > 12 ? CliL.F(" …共{0}种", topUid.Count) : ""));
                }
                sb.AppendLine(CliL.F("    所在列表 logic: {0}", string.Join(" ", tc.ListLogic.OrderBy(k => k.Key).Select(k => $"logic{k.Key}×{k.Value}"))));
                if (tc.CoNamed.Count > 0)
                    sb.AppendLine(CliL.F("    同列表共现: {0}", string.Join(", ", tc.CoNamed.OrderByDescending(k => k.Value).Take(8).Select(k => $"{k.Key}×{k.Value}"))));
                var ns = tc.Numbers.Take(12).ToList();
                if (ns.Count > 0)
                    sb.AppendLine(CliL.F("    U32(小值): {0}", string.Join(" ", ns.Select(k => $"{k.Key}×{k.Value}"))));
                if (tc.BigNums.Count > 0)
                {
                    var bn = tc.BigNums.Take(14).ToList();
                    sb.AppendLine(CliL.F("    U32 ≥0x10000(hash 形态, 共{0}种, 前14): {1}",
                        tc.BigNums.Count, string.Join(" ", bn.Select(k => $"0x{k.Key:X8}×{k.Value}"))));
                }
                var bs = tc.Bytes.Take(8).ToList();
                if (bs.Count > 0) sb.AppendLine($"    U8: {string.Join(" ", bs.Select(k => $"{k.Key}×{k.Value}"))}");
                var ss = tc.Strings.OrderByDescending(k => k.Value).Take(10).ToList();
                if (ss.Count > 0)
                {
                    sb.AppendLine(CliL.F("    Str(前10, 共{0}种):", tc.Strings.Count));
                    foreach (var (s, n) in ss)
                        sb.AppendLine($"      \"{Trunc(s, 120)}\" × {n}");
                }
                var ps = tc.PathSets.OrderByDescending(k => k.Value).Take(5).ToList();
                if (ps.Count > 0)
                {
                    sb.AppendLine(CliL.T("    P路径集(前5):"));
                    foreach (var (s, n) in ps) sb.AppendLine($"      [{s}] × {n}");
                }
                var tl = tc.Trailings.Take(8).ToList();
                if (tl.Count > 0) sb.AppendLine(CliL.F("    P尾字: {0}", string.Join(" ", tl.Select(k => $"{k.Key}×{k.Value}"))));
                var ls = tc.Luas.OrderByDescending(k => k.Value).Take(5).ToList();
                if (ls.Count > 0) sb.AppendLine($"    Lua: {string.Join(" | ", ls.Select(k => $"{k.Key}×{k.Value}"))}");
            }
        }

        DumpTable(CliL.T("条件类型普查"), cond.Values, true);
        DumpTable(CliL.T("动作类型普查"), act.Values, false);
        return sb.ToString();
    }

    static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}