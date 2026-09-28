using System.Text;
using System.Text.Json;

namespace CnvQst;

/// <summary>spec 文件格式错误。</summary>
public sealed class SpecException : Exception
{
    public SpecException(string message) : base(message) { }
}

/// <summary>
/// 由「编写用 spec」生成 .cnv / .qst 的建造器。
///
/// 与 `import-json` 的差别：那个是全保真模型（含 unknown 原值、偏移，供 round-trip），
/// 这个是为**手写**设计的薄层——条件/动作可以用官方名书写（`{"HasFaction": [...]}`），
/// 也可以退回原始形态（`{"type": 6, "subType": 1, "args": [...]}`）以便探测未知类型。
///
/// CNV spec（行式，与 GUI 平铺表的三列一致：文本/标志 | 条件 | 动作）：
/// {
///   "format": "cnv", "version": 6, "id": 0, "speaker": "Alex",
///   "rows": [
///     { "level": 1, "kind": "speech", "text": "第一句", "flags": 0,
///       "cond": [ {"HasToken": ["MY_TOKEN", 1]} ],
///       "act":  [ {"GiveMoney": [500]} ] }
///   ]
/// }
///   level：缩进层级（1 = 根的直接子节点，2 = 子节点……）；父 = 最近一个 level-1 的行。
///   kind：root/speech/continue/accept/decline/end/link/selector（也可直接写数字类型）。
///   flags：step 的 unknown 字原值（编辑器的 Once Only/Always Show/Unimportant 位尚未定位，
///          探测时直接写数值，如 8）。
///   link：kind=link 时给目标行号（rows 的 1 起序号），不给则自指。
///
/// QST spec：
/// {
///   "format": "qst", "version": 9, "group": "组名", "name": "任务名",
///   "tasks": [
///     { "title": "任务标题", "desc": "描述",
///       "incentives": [ {"cond": [...], "act": [...]} ],   // 编辑器 OnAccept（待验证）
///       "objectives": [ {"text": "目标文本", "cond": [...], "act": [...]} ],
///       "events":     [ {"cond": [...], "act": [...]} ] }  // 编辑器 OnComplete（待验证）
///   ]
/// }
///
/// 条件/动作写法（两种）：
///   具名 + 位置参数：{"HasFaction": ["SURVIVORS", 10000]}
///   具名 + 具名参数：{"HasFaction": {"FactionName": "SURVIVORS", "Value": 10000}}
///   原始类型号写法：{"type": 6, "subType": 1, "args": ["SURVIVORS", 10000]}
///   原始类型号 + 具名：{"type": 6, "subType": 1, "args": {"FactionName": "SURVIVORS"}}
/// P 形态（路径集）写 {"paths": ["a.dbr"], "trailing": 3}；L（Lua）写函数名字符串或
/// {"call": "gd.x.y()", "args": [...]}。
/// </summary>
public static class Spec
{
    // ---- 类型号 ↔ 名字（供具名写法反查）----
    private static readonly Dictionary<string, uint> CondByName = BuildNameMap(true);
    private static readonly Dictionary<string, uint> ActByName = BuildNameMap(false);

    private static Dictionary<string, uint> BuildNameMap(bool condition)
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        int n = condition ? 26 : 29;
        for (uint t = 0; t < n; t++)
        {
            if (!condition && (t == 0 || t == 16)) continue;
            string name = condition ? Names.ConditionName(t) : Names.ActionName(t);
            if (name.StartsWith("Type")) continue;              // 未命名，不可具名引用
            map[name] = t;                                      // 后写覆盖：以命名表为准
        }
        // 官方词表里的名字（含尚未被命名表采用的，如 PlaySound）也可直接使用
        var vocab = condition ? EditorVocab.CondMap : EditorVocab.ActMap;
        foreach (var kv in vocab) map[kv.Value.name] = kv.Key;
        return map;
    }

    /// <summary>具名写法可用的名字（供错误提示）。</summary>
    public static IEnumerable<string> KnownNames(bool condition) =>
        (condition ? CondByName : ActByName).Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------
    // 入口
    // ------------------------------------------------------------------

    /// <summary>解析 spec 文本并生成文件字节（format 由 spec 自己声明）。</summary>
    public static byte[] Build(string json, out string format)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
        var root = doc.RootElement;
        format = Str(root, "format") ?? throw new SpecException("spec 缺少 \"format\"（cnv 或 qst）");
        return format.ToLowerInvariant() switch
        {
            "cnv" => BuildCnv(root).Write(),
            "qst" => BuildQst(root).Write(),
            _ => throw new SpecException($"未知的 format：{format}"),
        };
    }

    public static CnvFile BuildCnv(JsonElement root)
    {
        var c = new CnvFile
        {
            Version = (uint)(Int(root, "version") ?? 6),
            Id = (uint)(Int(root, "id") ?? 0),
        };
        if (c.Version < 5) throw new SpecException("CNV 版本必须 ≥5（加载器拒绝更旧的文件）");

        var rows = root.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().ToList()
            : throw new SpecException("cnv spec 缺少 \"rows\" 数组");

        // 根
        var rootStep = new CnvStep { Type = Names.StepRoot };
        c.Steps.Add(rootStep);
        var byLevel = new Dictionary<int, CnvStep> { [0] = rootStep };

        var texts = new List<string> { Str(root, "speaker") ?? "" };
        var links = new List<(CnvStep step, int target)>();

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            int level = (int)(Int(row, "level") ?? throw new SpecException($"第 {i + 1} 行缺少 level"));
            if (level < 1) throw new SpecException($"第 {i + 1} 行 level 必须 ≥1（根由 speaker 给出）");
            if (!byLevel.TryGetValue(level - 1, out var parent))
                throw new SpecException($"第 {i + 1} 行 level={level} 找不到 level={level - 1} 的父行");

            var step = new CnvStep
            {
                Type = ParseKind(row, i),
                Unknown = (uint)(Int(row, "flags") ?? 0),
                VoPath = new GdString(Str(row, "vo") ?? ""),
            };
            if (step.Type == Names.StepLink)
            {
                step.HasLink = true;
                links.Add((step, (int)(Int(row, "link") ?? i + 1)));   // rows 的 1 起序号
            }
            parent.Children.Add(step);
            byLevel[level] = step;
            for (int l = level + 1; l < 32; l++) byLevel.Remove(l);     // 更深的层级已断开

            texts.Add(Str(row, "text") ?? "");

            uint logic = ParseLogic(row, i);
            step.Conditions = ParseList(row, "cond", true, logic, i);
            step.Actions = ParseList(row, "act", false, 0, i);
        }

        // 展平为深度前序（下标 == 本地化索引）
        var flat = new List<CnvStep>();
        void Walk(CnvStep s, int depth, int parentIdx)
        {
            s.Index = flat.Count;
            s.Depth = depth;
            s.Parent = parentIdx;
            flat.Add(s);
            foreach (var ch in s.Children) Walk(ch, depth + 1, s.Index);
        }
        Walk(rootStep, 0, -1);
        c.Steps = flat;

        foreach (var (step, target) in links)
            step.LinkId = (uint)Math.Clamp(target, 0, flat.Count - 1);

        var lang = new LanguageBlock { Tag = new GdString("enUS") };
        foreach (var t in texts) lang.Texts.Add(new GdString(t));
        while (lang.Texts.Count < flat.Count) lang.Texts.Add(new GdString(""));
        c.Languages.Add(lang);
        return c;
    }

    public static QstFile BuildQst(JsonElement root)
    {
        var q = new QstFile
        {
            Version = (uint)(Int(root, "version") ?? 9),
            Id = (uint)(Int(root, "id") ?? 0),
            Unknown10 = (uint)(Int(root, "unknown10") ?? 0),
        };
        if (q.Version < 7) throw new SpecException("QST 版本必须 ≥7");

        var texts = new List<string>
        {
            Str(root, "group") ?? "",
            Str(root, "name") ?? "",
        };
        var tasks = root.TryGetProperty("tasks", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().ToList()
            : throw new SpecException("qst spec 缺少 \"tasks\" 数组");

        for (int i = 0; i < tasks.Count; i++)
        {
            var tj = tasks[i];
            var task = new QstTask { Uid = 1, Uid2 = 2 };
            texts.Add(Str(tj, "title") ?? "");
            texts.Add(Str(tj, "desc") ?? "");

            foreach (var e in Array(tj, "incentives")) task.Incentives.Add(ParseEvent(e, i));   // 编辑器 OnAccept（待验证）
            foreach (var o in Array(tj, "objectives"))
            {
                var obj = new QstObjective();
                obj.Conditions = ParseList(o, "cond", true, ParseLogic(o, i), i);
                obj.Actions = ParseList(o, "act", false, 0, i);
                texts.Add(Str(o, "text") ?? "");
                task.Objectives.Add(obj);
            }
            foreach (var e in Array(tj, "events")) task.Events.Add(ParseEvent(e, i));           // 编辑器 OnComplete（待验证）
            q.Tasks.Add(task);
        }

        var lang = new LanguageBlock { Tag = new GdString("enUS") };
        foreach (var s in texts) lang.Texts.Add(new GdString(s));
        q.Languages.Add(lang);
        return q;
    }

    private static QstEvent ParseEvent(JsonElement e, int taskIndex)
    {
        var ev = new QstEvent();
        ev.Conditions = ParseList(e, "cond", true, ParseLogic(e, taskIndex), taskIndex);
        ev.Actions = ParseList(e, "act", false, 0, taskIndex);
        return ev;
    }

    // ------------------------------------------------------------------
    // 条件/动作解析
    // ------------------------------------------------------------------

    private static ScriptList ParseList(JsonElement owner, string key, bool isCondition, uint logic, int rowIndex)
    {
        var list = new ScriptList { Logic = logic };
        if (!owner.TryGetProperty(key, out var arr)) return list;
        if (arr.ValueKind != JsonValueKind.Array)
            throw new SpecException($"第 {rowIndex + 1} 行的 \"{key}\" 必须是数组");

        int uid = 1;
        foreach (var item in arr.EnumerateArray())
        {
            var rec = ParseEntry(item, isCondition, rowIndex, key);
            if (isCondition) rec.Uid = (uint)uid++;
            list.Records.Add(rec);
        }
        list.Present = list.Records.Count > 0;
        list.Count = list.Records.Count;
        return list;
    }

    private static ScriptRecord ParseEntry(JsonElement item, bool isCondition, int rowIndex, string key)
    {
        if (item.ValueKind != JsonValueKind.Object)
            throw new SpecException($"第 {rowIndex + 1} 行 {key} 的每一项必须是对象");

        uint type;
        byte subType = 0;
        JsonElement args = default;
        bool hasArgs = false;

        if (item.TryGetProperty("type", out var tv))
        {
            type = (uint)Num(tv, "type");
            if (item.TryGetProperty("subType", out var sv)) subType = (byte)Num(sv, "subType");
        }
        else
        {
            var prop = item.EnumerateObject().FirstOrDefault(p => p.Name != "subType" && p.Name != "uid" && p.Name != "args");
            if (prop.NameEquals("args") || prop.Value.ValueKind == JsonValueKind.Undefined)
                throw new SpecException($"第 {rowIndex + 1} 行 {key}：既没有 \"type\" 也没有具名类型");
            var map = isCondition ? CondByName : ActByName;
            if (!map.TryGetValue(prop.Name, out type))
                throw new SpecException($"第 {rowIndex + 1} 行 {key}：未知类型名 \"{prop.Name}\"。" +
                    (isCondition ? "条件" : "动作") + "可用名：" + string.Join(", ", KnownNames(isCondition)));
            args = prop.Value;
            hasArgs = true;
            if (item.TryGetProperty("subType", out var sv2)) subType = (byte)Num(sv2, "subType");
        }
        if (!hasArgs && item.TryGetProperty("args", out var av)) { args = av; hasArgs = true; }

        var rec = new ScriptRecord
        {
            IsCondition = isCondition,
            Type = type,
            SubType = subType,
            BodySpec = isCondition ? Scriptable.ConditionBodySpec(type) : Scriptable.ActionBodySpec(type),
        };
        (rec.ClassName, rec.Named) = isCondition ? Names.ConditionClass(type) : Names.ActionClass(type);
        // 动作的 0/16 工厂返回 null（"(unused)"）；条件的空 body 是合法的（如 17 WaitForCompletion 无参数）
        if (!isCondition && rec.BodySpec == "(unused)")
            throw new SpecException($"第 {rowIndex + 1} 行：动作类型 {type} 未实现（工厂返回 null）");
        if (isCondition && type >= 26)
            throw new SpecException($"第 {rowIndex + 1} 行：条件类型 {type} 越界（有效范围 0–25）");

        rec.Fields = BuildFields(isCondition, type, subType, hasArgs ? args : default, hasArgs, rowIndex);
        if (item.TryGetProperty("uid", out var uv)) rec.Uid = (uint)Num(uv, "uid");
        return rec;
    }

    /// <summary>按 body 规格把 spec 里给的值填成字段列表（缺值给零值）。</summary>
    private static List<ScriptField> BuildFields(bool isCondition, uint type, byte sub, JsonElement args, bool hasArgs, int rowIndex)
    {
        string spec = rec_spec(isCondition, type);
        string?[]? names = isCondition ? Scriptable.ConditionParamNames(type) : Scriptable.ActionParamNames(type);
        var slots = new List<(char kind, string? name)>();
        for (int i = 0; i < spec.Length; i++)
        {
            char c = spec[i];
            if ((c == 'i' || c == 'b') && sub != 1) continue;     // 门控字段：subType!=1 时不落盘
            slots.Add((char.ToUpperInvariant(c), names != null && i < names.Length ? names[i] : null));
        }

        // 取值器：具名参数（对象）或位置参数（数组）
        var positional = new List<JsonElement>();
        if (hasArgs && args.ValueKind == JsonValueKind.Array)
            foreach (var v in args.EnumerateArray()) positional.Add(v);
        bool named = hasArgs && args.ValueKind == JsonValueKind.Object;

        JsonElement ValueAt(int idx, string? name)
        {
            if (named)
            {
                if (name != null && args.TryGetProperty(name, out var nv)) return nv;
                foreach (var p in args.EnumerateObject())
                    if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
                return default;
            }
            return idx < positional.Count ? positional[idx] : default;
        }

        var fields = new List<ScriptField>();
        for (int k = 0; k < slots.Count; k++)
        {
            var (kind, name) = slots[k];
            var v = ValueAt(k, name);
            switch (kind)
            {
                case 'S':
                    fields.Add(new ScriptField
                    {
                        Kind = FieldKind.Str,
                        S = new GdString(v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : ""),
                        Param = name ?? "",
                    });
                    break;
                case 'I':
                    fields.Add(new ScriptField
                    {
                        Kind = FieldKind.U32,
                        U = v.ValueKind == JsonValueKind.Number ? (uint)v.GetInt64() : 0u,
                        Param = name ?? "",
                    });
                    break;
                case 'B':
                    fields.Add(new ScriptField
                    {
                        Kind = FieldKind.U8,
                        B = v.ValueKind == JsonValueKind.Number ? (byte)v.GetInt64()
                            : v.ValueKind == JsonValueKind.True ? (byte)1 : (byte)0,
                        Param = name ?? "",
                    });
                    break;
                case 'P':
                {
                    var f = new ScriptField { Kind = FieldKind.P, Paths = new List<GdString>(), Trailing = 0, Param = name ?? "paths" };
                    if (v.ValueKind == JsonValueKind.Array)
                        foreach (var p in v.EnumerateArray()) f.Paths.Add(new GdString(p.GetString() ?? ""));
                    else if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("paths", out var pv))
                    {
                        foreach (var p in pv.EnumerateArray()) f.Paths.Add(new GdString(p.GetString() ?? ""));
                        if (v.TryGetProperty("trailing", out var tv2)) f.Trailing = (uint)tv2.GetInt64();
                    }
                    f.Count = f.Paths.Count;
                    fields.Add(f);
                    break;
                }
                case 'L':
                {
                    var f = new ScriptField { Kind = FieldKind.Lua, Params = new List<LuaParam>(), Param = name ?? "Function" };
                    if (v.ValueKind == JsonValueKind.String) f.S = new GdString(v.GetString() ?? "");
                    else if (v.ValueKind == JsonValueKind.Object)
                    {
                        f.S = new GdString(Str(v, "call") ?? "");
                        if (v.TryGetProperty("args", out var av2))
                            foreach (var p in av2.EnumerateArray())
                                f.Params.Add(new LuaParam { S = new GdString(p.GetString() ?? "") });
                    }
                    f.Count = f.Params.Count;
                    if (isCondition) f.Tail = new LuaTail();
                    fields.Add(f);
                    break;
                }
            }
        }
        return fields;
    }

    private static string rec_spec(bool isCondition, uint type) =>
        isCondition ? Scriptable.ConditionBodySpec(type) : Scriptable.ActionBodySpec(type);

    // ------------------------------------------------------------------
    // 小工具
    // ------------------------------------------------------------------

    private static uint ParseLogic(JsonElement owner, int rowIndex)
    {
        if (!owner.TryGetProperty("logic", out var l)) return 0;
        if (l.ValueKind == JsonValueKind.String)
        {
            string s = (l.GetString() ?? "").Trim().ToUpperInvariant();
            return s == "OR" ? 1u : s == "AND" ? 0u : throw new SpecException($"第 {rowIndex + 1} 行 logic 只接受 AND/OR");
        }
        return (uint)Num(l, "logic");
    }

    private static uint ParseKind(JsonElement row, int rowIndex)
    {
        if (!row.TryGetProperty("kind", out var k))
            throw new SpecException($"第 {rowIndex + 1} 行缺少 kind");
        if (k.ValueKind == JsonValueKind.Number) return (uint)k.GetInt64();
        string s = (k.GetString() ?? "").Trim().ToLowerInvariant();
        return s switch
        {
            "root" => Names.StepRoot,
            "speech" => Names.StepSpeech,
            "continue" => Names.StepContinue,
            "accept" => Names.StepAccept,
            "decline" => Names.StepDecline,
            "end" => Names.StepEnd,
            "link" => Names.StepLink,
            "selector" => Names.StepSelector,
            _ => throw new SpecException($"第 {rowIndex + 1} 行未知 kind：{s}"),
        };
    }

    private static IEnumerable<JsonElement> Array(JsonElement owner, string key) =>
        owner.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static string? Str(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Int(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;

    private static long Num(JsonElement v, string what) =>
        v.ValueKind == JsonValueKind.Number ? v.GetInt64() : throw new SpecException($"{what} 必须是数字");

    // ------------------------------------------------------------------
    // 模板
    // ------------------------------------------------------------------

    public static string SampleCnv() => """
{
  // 生成一段对话：根（说话人）→ 一句台词 → 玩家选项 → 结束
  "format": "cnv",
  "version": 6,
  "id": 0,
  "speaker": "Alex",
  "rows": [
    { "level": 1, "kind": "speech", "text": "你终于来了。",
      "cond": [ { "HasToken": ["MY_TOKEN", 1] } ],
      "act":  [ { "GiveMoney": [500] } ] },
    { "level": 2, "kind": "continue", "text": "继续", "flags": 0 },
    { "level": 3, "kind": "end", "text": "" }
  ]
}
""";

    public static string SampleQst() => """
{
  // 生成一个任务：组名/任务名 + 每 task 的标题、描述、目标
  "format": "qst",
  "version": 9,
  "group": "PROBE_GROUP",
  "name": "PROBE_QUEST",
  "tasks": [
    { "title": "T1 标题", "desc": "T1 描述",
      "objectives": [
        { "text": "O1 目标文本",
          "cond": [ { "type": 17 } ],
          "act":  [ { "GiveExperience": [350] } ] }
      ],
      "events": [
        { "cond": [], "act": [ { "CompleteQuest": ["quests/probe.qst"] } ] }
      ] }
  ]
}
""";
}
