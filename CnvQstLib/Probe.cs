using System.Text;

namespace CnvQst;

/// <summary>
/// 探测器：批量生成最小合法文件，交给**官方编辑器**去"自报家门"。
///
/// 思路：官方编辑器按二进制里的 type id 显示对应的选项名与字段，所以只要造出含某个 type 的
/// 最小文件、在编辑器里打开，就能直接读到官方名——比手工点选逐条核对快得多，也能覆盖
/// 本地语料里样本为 0 的类型。
///
/// 说话人名（本地化索引 0）里写入 type 号，编辑器里一眼就能看出打开的是哪一个。
/// </summary>
public static class Probe
{
    public sealed record Item(string File, byte[] Data, string Note);

    /// <summary>
    /// 条件类型探测：默认合成**一个**对话（26 个节点，一节点一个类型）；split=true 时一类型一文件
    /// （某个类型让编辑器报错时用它隔离）。节点文本写着我们当前的猜测名，编辑器里可直接对照。
    /// </summary>
    public static List<Item> CondTypes(bool split = false)
    {
        var nodes = new List<Node>();
        var items = new List<Item>();
        for (uint t = 0; t < 26; t++)
        {
            string label = Names.ConditionLabel(t);
            var rec = Rec(true, t, DefaultSub(t), "T" + t, 10000);
            var text = $"cond {t} · 我们叫：{label}";
            if (split)
            {
                byte[] one = BuildCnv($"cond {t}", new[] { new Node(text, 0, List(new[] { rec }), Empty()) });
                items.Add(new Item($"cond_{t:00}_{Safe(Names.ConditionName(t))}.cnv", one,
                    $"cond_{t:00}  type={t}  body={Scriptable.ConditionBodySpec(t)}  当前名={label}"));
            }
            else nodes.Add(new Node(text, 0, List(new[] { rec }), Empty()));
        }
        if (!split)
        {
            byte[] data = BuildCnv("probe cond（每个节点一个条件类型）", nodes);
            items.Add(new Item("probe_cond.cnv", data,
                "26 个节点 = 条件 type 0–25；展开每个节点的 Conditions，官方编辑器会显示它的官方名。" +
                "节点文本里写着我们当前的猜测名。"));
        }
        return items;
    }

    /// <summary>动作类型探测：默认合成一个对话（27 个节点）；split=true 时一类型一文件。</summary>
    public static List<Item> ActTypes(bool split = false)
    {
        var nodes = new List<Node>();
        var items = new List<Item>();
        for (uint t = 1; t <= 28; t++)
        {
            if (t == 0 || t == 16) continue;
            string label = Names.ActionLabel(t);
            var rec = Rec(false, t, DefaultSub(t), "T" + t, 10000);
            var text = $"act {t} · 我们叫：{label}";
            if (split)
            {
                byte[] one = BuildCnv($"act {t}", new[] { new Node(text, 0, Empty(), List(new[] { rec })) });
                items.Add(new Item($"act_{t:00}_{Safe(Names.ActionName(t))}.cnv", one,
                    $"act_{t:00}  type={t}  body={Scriptable.ActionBodySpec(t)}  当前名={label}"));
            }
            else nodes.Add(new Node(text, 0, Empty(), List(new[] { rec })));
        }
        if (!split)
        {
            byte[] data = BuildCnv("probe act（每个节点一个动作类型）", nodes);
            items.Add(new Item("probe_act.cnv", data,
                "27 个节点 = 动作 type 1–28（跳过 0/16）；展开 Actions 看官方名。"));
        }
        return items;
    }

    /// <summary>
    /// 比较符探测：条件与参数全固定，只改 type/subType 之后那 4 字节（gd-utility 称作 uid 的字），
    /// 看官方编辑器显示哪个比较符。已知两点：该字=1 显示 "&lt;="（本探测器早先的探测文件），
    /// jailor_01 该字=3 编辑器显示 "&gt;="。
    /// </summary>
    public static List<Item> UidVariants(uint type, byte sub)
    {
        var nodes = new List<Node>();
        for (uint u = 0; u <= 5; u++)
        {
            var rec = Rec(true, type, sub, "SURVIVORS", 10000);
            rec.Uid = u;
            nodes.Add(new Node($"uid={u}（看它显示成哪个比较符）", 0, List(new[] { rec }), Empty()));
        }
        string name = Names.ConditionName(type);
        byte[] data = BuildCnv($"probe uid type={type}（6 个节点 = 该字取值 0..5）", nodes);
        return new List<Item> { new Item($"probe_uid_{type:00}_{Safe(name)}.cnv", data,
            $"type={type}（{Names.ConditionLabel(type)}）subType={sub} 固定，该字取 0..5；" +
            "编辑器显示的比较符序列即该字的编码。") };
    }

    /// <summary>比较符探测（两个代表类型：HasFaction 带字符串+数值，IsDifficulty 只有数值）。</summary>
    public static List<Item> UidDefaults()
    {
        var items = new List<Item>();
        items.AddRange(UidVariants(6, 1));
        items.AddRange(UidVariants(4, 0));
        return items;
    }

    /// <summary>
    /// subType 变体探测：固定 type，subType 0..5 各一节点。
    /// 官方条件下拉还剩 4 项没落点（IsChallenge / Hasbuff / HasKilledFaction / HasKilledMonsterClass），
    /// 槽位已排满 → 它们只能是某个 type 的 subType 变体，这一探测就是去找它们。
    /// 注意：subType 值不当时编辑器可能读错布局而报错，故错开成独立文件（split）。
    /// </summary>
    public static List<Item> SubVariants(uint type, bool split)
    {
        string name = Names.ConditionName(type);
        var items = new List<Item>();
        var nodes = new List<Node>();
        for (byte sub = 0; sub <= 5; sub++)
        {
            var rec = Rec(true, type, sub, "SURVIVORS", 10000);
            rec.Uid = 2;                                        // 语料里的常值，避免干扰
            string text = $"subType={sub} · 我们叫：{Names.ConditionLabel(type)}";
            if (split)
            {
                byte[] one = BuildCnv($"sub {type}/{sub}", new[] { new Node(text, 0, List(new[] { rec }), Empty()) });
                items.Add(new Item($"sub_{type:00}_s{sub}_{Safe(name)}.cnv", one,
                    $"type={type} subType={sub}（独立文件，隔离报错）"));
            }
            else nodes.Add(new Node(text, 0, List(new[] { rec }), Empty()));
        }
        if (!split)
        {
            byte[] data = BuildCnv($"probe sub type={type}（6 个节点 = subType 0..5）", nodes);
            items.Add(new Item($"probe_sub_{type:00}_{Safe(name)}.cnv", data,
                $"type={type}（{Names.ConditionLabel(type)}）的 subType 0..5；看哪个值让编辑器显示成别的选项名。"));
        }
        return items;
    }

    /// <summary>把 26 个条件类型的 subType 0..5 全扫一遍（一类型一文件）。</summary>
    public static List<Item> SubVariantsAll(bool split)
    {
        var items = new List<Item>();
        for (uint t = 0; t < 26; t++) items.AddRange(SubVariants(t, split));
        return items;
    }

    /// <summary>step unknown 位探测：0 / 0x4 / 0x8 / 0x10 / 0x20（对应编辑器 Once Only 等开关）。</summary>
    public static List<Item> StepFlags()
    {
        var nodes = new List<Node>();
        foreach (uint f in new uint[] { 0, 0x4, 0x8, 0x10, 0x20 })
            nodes.Add(new Node($"flag step.unknown=0x{f:X}（看勾了哪个开关）", f, Empty(), Empty()));
        byte[] data = BuildCnv("probe flags（5 个节点 = step.unknown 各位）", nodes);
        return new List<Item> { new Item("probe_flags.cnv", data,
            "step.unknown = 0/0x4/0x8/0x10/0x20 各一个节点；看编辑器里哪个开关被勾选" +
            "（Once Only / Always Show / Unimportant）。") };
    }

    /// <summary>QST 本地化槽位探测：1/2/3 个 task 的任务文件，文本带编号，看编辑器把它们摆到哪。</summary>
    public static List<Item> QuestTasks()
    {
        var items = new List<Item>();
        foreach (int nt in new[] { 1, 3 })
        {
            var q = new QstFile { Version = 9, Id = 0, Unknown10 = 0 };
            var texts = new List<string> { "GROUP_A", "NAME_A" };
            for (int t = 1; t <= nt; t++)
            {
                var task = new QstTask { Uid = 1, Uid2 = 2 };
                texts.Add($"T{t}_TITLE");
                texts.Add($"T{t}_DESC");
                int nObj = nt == 1 ? 2 : t switch { 1 => 2, 2 => 1, _ => 0 };   // 目标数递减，便于看槽位偏移
                for (int o = 1; o <= nObj; o++)
                {
                    var obj = new QstObjective();
                    obj.Conditions = List(new[] { Rec(true, 17, 0, "", 0) });
                    texts.Add($"T{t}_OBJ{o}");
                    task.Objectives.Add(obj);
                }
                task.Events.Add(new QstEvent { Actions = List(new[] { Rec(false, 8, 0, "", 350) }) });
                q.Tasks.Add(task);
            }
            // 官方编辑器按它自己的公式索引本地化串，串数不足会抛 ArgumentOutOfRange（首次探测即如此）。
            // 多补一批空槽，让它能顺利加载——加载成功后各段文本落在哪一槽，映射就看得见了。
            int real = texts.Count;
            for (int i = 0; i < 18; i++) texts.Add("");
            var lang = new LanguageBlock { Tag = new GdString("enUS") };
            foreach (var s in texts) lang.Texts.Add(new GdString(s));
            q.Languages.Add(lang);
            byte[] data = q.Write();
            items.Add(new Item($"qst_{nt}task.qst", data,
                $"qst_{nt}task  {nt} 个 task（目标数递减），前 {real} 条是实义文本、其后 18 条补空槽；" +
                "编辑器里看每段文本落在哪个槽（即可反推 task ↔ 串表映射）"));
        }
        return items;
    }

    public static List<Item> All(string? cmpType = null, bool split = false)
    {
        var items = new List<Item>();
        items.AddRange(CondTypes(split));
        items.AddRange(ActTypes(split));
        items.AddRange(UidDefaults());                     // 比较符编码（该字 0..5）
        items.AddRange(SubVariants(cmpType == null ? 6 : uint.Parse(cmpType), split));  // subType 变体
        items.AddRange(StepFlags());
        items.AddRange(QuestTasks());
        return items;
    }

    // ------------------------------------------------------------------

    /// <summary>写盘并逐文件跑三检查，返回索引文本。</summary>
    public static string Emit(IEnumerable<Item> items, string dir)
    {
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        sb.AppendLine("探测器输出：" + Path.GetFullPath(dir));
        sb.AppendLine("用法：用官方编辑器逐个打开，看它显示的条件/动作名、比较符、开关与文本槽位。");
        sb.AppendLine();
        int ok = 0;
        foreach (var it in items)
        {
            string path = Path.Combine(dir, it.File);
            File.WriteAllBytes(path, it.Data);
            string check = Check(it.Data);
            if (check == "OK") ok++;
            sb.AppendLine($"{(check == "OK" ? "OK  " : "FAIL")} {it.File,-34} {it.Note}" +
                          (check == "OK" ? "" : $"  ← {check}"));
        }
        sb.AppendLine();
        sb.AppendLine($"自检 {ok}/{items.Count()} 通过（铺满 + locOffset + round-trip 逐字节一致）。");
        string text = sb.ToString();
        File.WriteAllText(Path.Combine(dir, "索引.txt"), text);
        return text;
    }

    private static string Check(byte[] data)
    {
        try
        {
            if (data.Length >= 4 && data[0] == (byte)'C')
            {
                var c = CnvFile.Parse(data);
                if (!c.Valid) return c.Error ?? "解析失败";
                if (!c.Tiled) return "记录未铺满文件";
                if (!c.LocOffsetMatches) return "本地化偏移不符";
                return c.Write().SequenceEqual(data) ? "OK" : "round-trip 不一致";
            }
            var q = QstFile.Parse(data);
            if (!q.Valid) return q.Error ?? "解析失败";
            if (!q.Tiled) return "记录未铺满文件";
            if (!q.LocOffsetMatches) return "本地化偏移不符";
            return q.Write().SequenceEqual(data) ? "OK" : "round-trip 不一致";
        }
        catch (Exception e) { return e.Message; }
    }

    /// <summary>探测用的一个节点：文本 + unknown 原值 + 条件/动作列表。</summary>
    public sealed record Node(string Text, uint Unknown, ScriptList Conditions, ScriptList Actions);

    /// <summary>造一个对话：根（说话人名）+ 若干台词节点，每个节点可带自己的条件/动作与 unknown 值。</summary>
    private static byte[] BuildCnv(string speaker, IEnumerable<Node> nodes)
    {
        var c = new CnvFile { Version = 6, Id = 0, Valid = true };
        var root = new CnvStep { Type = Names.StepRoot };
        var list = new List<CnvStep> { root };
        foreach (var n in nodes)
        {
            var s = new CnvStep { Type = Names.StepSpeech, Unknown = n.Unknown, Conditions = n.Conditions, Actions = n.Actions };
            root.Children.Add(s);
            list.Add(s);
        }
        c.Steps = list;
        c.Renormalize();                       // 重建 Index/Depth/Parent

        var lang = new LanguageBlock { Tag = new GdString("enUS") };
        lang.Texts.Add(new GdString(speaker));
        foreach (var n in nodes) lang.Texts.Add(new GdString(n.Text));
        c.Languages.Add(lang);
        return c.Write();
    }

    private static ScriptList List(IEnumerable<ScriptRecord> recs)
    {
        var l = new ScriptList();
        foreach (var r in recs) l.Records.Add(r);
        l.Present = l.Records.Count > 0;
        l.Count = l.Records.Count;
        return l;
    }

    private static ScriptList Empty() => new();

    /// <summary>按 body 规格造一条记录；缺参数处填可辨识的探测值。</summary>
    private static ScriptRecord Rec(bool isCondition, uint type, byte sub, string mark, uint num)
    {
        string spec = isCondition ? Scriptable.ConditionBodySpec(type) : Scriptable.ActionBodySpec(type);
        var rec = new ScriptRecord
        {
            IsCondition = isCondition,
            Type = type,
            SubType = sub,
            Uid = 1,
            BodySpec = spec,
        };
        (rec.ClassName, rec.Named) = isCondition ? Names.ConditionClass(type) : Names.ActionClass(type);
        foreach (char k in spec)
        {
            switch (k)
            {
                case 'S': rec.Fields.Add(new ScriptField { Kind = FieldKind.Str, S = new GdString(mark) }); break;
                case 'I': rec.Fields.Add(new ScriptField { Kind = FieldKind.U32, U = EnumSafe(isCondition, type, num) }); break;
                case 'B': rec.Fields.Add(new ScriptField { Kind = FieldKind.U8, B = 1 }); break;
                case 'i': if (sub == 1) rec.Fields.Add(new ScriptField { Kind = FieldKind.U32, U = num }); break;
                case 'b': if (sub == 1) rec.Fields.Add(new ScriptField { Kind = FieldKind.U8, B = 1 }); break;
                case 'P': rec.Fields.Add(new ScriptField
                {
                    Kind = FieldKind.P, Count = 1,
                    Paths = new List<GdString> { new(mark) }, Trailing = 1,
                }); break;
                case 'L': rec.Fields.Add(new ScriptField
                {
                    Kind = FieldKind.Lua, S = new GdString("gd.probe.test()"),
                    Params = new List<LuaParam>(), Tail = isCondition ? new LuaTail() : null,
                }); break;
            }
        }
        rec.Length = 0;
        return rec;
    }

    /// <summary>
    /// 枚举型字段要给合法值：官方编辑器拿它当下标查表，越界会弹 ArgumentOutOfRange 对话框
    /// （首次探测时 PlayAnimation 的 I=10000 就是这么把编辑器弹窗的）。
    /// </summary>
    private static uint EnumSafe(bool isCondition, uint type, uint num) => (isCondition, type) switch
    {
        (false, 14) => 9,      // PlayAnimation 的 AnimationType，语料恒为 9（编辑器枚举 "Chat"）
        (false, 27) => 0,      // UnlockTutorial 的 TutorialPage
        _ => num,
    };

    /// <summary>默认 subType：语料里带比较符的类型普遍是 1（HasFaction/HasItem/HasQuestObjective…），其余取 0。</summary>
    private static byte DefaultSub(uint type) => type switch
    {
        6 or 9 or 11 or 21 => 1,
        _ => 0,
    };

    private static string Safe(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
            sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
        return sb.ToString().Trim('_');
    }
}
