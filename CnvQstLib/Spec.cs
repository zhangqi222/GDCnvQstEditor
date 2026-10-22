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
///       "cond": [ {"HasToken": ["MY_TOKEN", 1], "cmp": "="} ],
///       "act":  [ {"GiveMoney": [500]} ] }
///   ]
/// }
///   level：缩进层级（1 = 根的直接子节点，2 = 子节点……）；父 = 最近一个 level-1 的行。
///   kind：root/speech/continue/accept/decline/end/link/selector/random（也可直接写数字类型）。
///   flags：step 的 unknown 字原值（低 3 位为 OnceOnly/AlwaysShow/Unimportant）。
///   link：kind=link 时给目标行号（rows 的 1 起序号），不给则自指。
///   根级也可写 flags/vo/cond/act/logic。
///
/// QST spec：
/// {
///   "format": "qst", "version": 9, "flags": 0, "id": 0, "group": "组名", "name": "任务名",
///   "tasks": [
///     { "uid": 123, "title": "任务标题", "desc": "描述",
///       "incentives": [ {"cond": [...], "act": [...]} ],   // 编辑器 OnAccept
///       "objectives": [ {"uid": 456, "text": "目标文本", "cond": [...], "act": [...]} ],
///       "events":     [ {"cond": [...], "act": [...]} ] }  // 编辑器 OnComplete
///   ]
/// }
///
/// 条件/动作写法（两种）：
///   具名 + 位置参数：{"HasFaction": ["SURVIVORS", 10000], "cmp": ">="}
///   具名 + 具名参数：{"HasFaction": {"FactionName": "SURVIVORS", "Value": 10000}, "cmp": 3}
///   原始类型号写法：{"type": 6, "subType": 1, "args": ["SURVIVORS", 10000], "cmp": ">="}
///   原始类型号 + 具名：{"type": 6, "subType": 1, "args": {"FactionName": "SURVIVORS"}}
/// P 形态（路径集）写 {"paths": ["a.dbr"], "trailing": 3}，或
/// {"MonsterRecords": ["a.dbr"], "KillCount": 3}；L（Lua）写函数名字符串或
/// {"call": "gd.x.y()", "args": [...], "tail": {"t0":0,"t1":0,"t2":0,"t3":0,"text":""}}。
/// </summary>
public static class Spec
{
    // ---- 类型号 ↔ 名字（供具名写法反查）----
    private static readonly Dictionary<string, uint> CondByName = BuildNameMap(true);
    private static readonly Dictionary<string, uint> ActByName = BuildNameMap(false);

    private static Dictionary<string, uint> BuildNameMap(bool condition)
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        int n = condition ? 30 : 29;
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
            // 头部 +0x0C = 根文本（说话人名）的 FNV1 哈希；spec 未显式给 id 时按官方算法算
            Id = root.TryGetProperty("id", out _)
                ? (uint)(Int(root, "id") ?? 0)
                : Fnv1.Hash(Str(root, "speaker") ?? ""),
        };
        if (c.Version < 5) throw new SpecException("CNV 版本必须 ≥5（加载器拒绝更旧的文件）");

        var rows = root.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().ToList()
            : throw new SpecException("cnv spec 缺少 \"rows\" 数组");

        // 根：文本来自 speaker；也可带 flags/vo/cond/act（与普通节点同构）
        var rootStep = new CnvStep
        {
            Type = Names.StepRoot,
            Unknown = (uint)(Int(root, "flags") ?? 0),
            VoPath = new GdString(Str(root, "vo") ?? ""),
            Conditions = ParseList(root, "cond", true, ParseLogic(root, 0), 0),
            Actions = ParseList(root, "act", false, 0, 0),
        };
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

        var lang = new LanguageBlock { Tag = new GdString(Str(root, "lang") ?? Str(root, "language") ?? "enUS") };
        foreach (var t in texts) lang.Texts.Add(new GdString(t));
        while (lang.Texts.Count < flat.Count) lang.Texts.Add(new GdString(""));
        c.Languages.Add(lang);
        return c;
    }

    public static QstFile BuildQst(JsonElement root)
    {
        // 本文件自有 ID：显式写入（含 0）原样保留，缺省才随机；旧任务改 JSON 时必须照抄原值
        long? explicitId = Int(root, "id");
        uint fileUid = explicitId.HasValue ? (uint)explicitId.Value : QstFile.RandomUid();
        var q = new QstFile
        {
            Version = (uint)(Int(root, "version") ?? 9),
            Id = fileUid,
            // +0x10：任务 flags（None/Primary/Secondary/Repeatable）；兼容旧字段名 unknown10
            Unknown10 = (uint)(Int(root, "flags") ?? Int(root, "unknown10") ?? 0),
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
        if (tasks.Count > 4096) throw new SpecException($"task 数量 {tasks.Count} 超过上限 4096");

        for (int i = 0; i < tasks.Count; i++)
        {
            var tj = tasks[i];
            uint taskUid = (uint)(Int(tj, "uid") ?? 0);
            if (taskUid == 0) taskUid = QstFile.RandomUid();
            var task = new QstTask
            {
                Uid = 1,
                Uid2 = 2,                       // 运行时计数器，不落盘
                UnknownA8 = taskUid,            // 持久化 task uid（CNV 的任务引用就是它）
                UnknownAC = (uint)(Int(tj, "unknownAC") ?? 0),
                Flag8 = Bool(tj, "flag8"),
                Flag9 = Bool(tj, "flag9"),
            };
            texts.Add(Str(tj, "title") ?? "");
            texts.Add(Str(tj, "desc") ?? "");

            // 槽序（官方 QuestEditor 实测）：标题、描述、OnAccept…、目标…、OnComplete…
            foreach (var e in Array(tj, "incentives"))
            {
                task.Incentives.Add(ParseEvent(e, i));
                texts.Add("");                  // incentive 的本地化槽（官方文件为空）
            }
            foreach (var o in Array(tj, "objectives"))
            {
                uint objUid = (uint)(Int(o, "uid") ?? 0);
                if (objUid == 0) objUid = QstFile.RandomUid();
                var obj = new QstObjective
                {
                    Unknown0 = objUid,          // 持久化 objective uid
                    Unknown1 = (uint)(Int(o, "unknown1") ?? 0),
                };
                obj.Conditions = ParseList(o, "cond", true, ParseLogic(o, i), i);
                obj.Actions = ParseList(o, "act", false, 0, i);
                texts.Add(Str(o, "text") ?? "");
                task.Objectives.Add(obj);
            }
            foreach (var e in Array(tj, "events"))
            {
                task.Events.Add(ParseEvent(e, i));
                texts.Add("");                  // event 的本地化槽（官方文件为空）
            }
            q.Tasks.Add(task);
        }

        int want = QstFile.LocalizedTextCount(q.Tasks.Count, q.ObjectiveCount, q.IncentiveCount, q.EventCount);
        while (texts.Count < want) texts.Add("");

        var lang = new LanguageBlock { Tag = new GdString(Str(root, "lang") ?? Str(root, "language") ?? "enUS") };
        foreach (var s in texts) lang.Texts.Add(new GdString(s));
        q.Languages.Add(lang);
        return q;
    }

    private static QstEvent ParseEvent(JsonElement e, int taskIndex)
    {
        var ev = new QstEvent { Unknown = (uint)(Int(e, "unknown") ?? 0) };
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

        foreach (var item in arr.EnumerateArray())
            list.Records.Add(ParseEntry(item, isCondition, rowIndex, key));
        list.Present = list.Records.Count > 0;
        list.Count = list.Records.Count;
        return list;
    }

    private static ScriptRecord ParseEntry(JsonElement item, bool isCondition, int rowIndex, string key)
    {
        if (item.ValueKind != JsonValueKind.Object)
            throw new SpecException($"第 {rowIndex + 1} 行 {key} 的每一项必须是对象");

        uint type;
        byte? subTypeArg = null;
        JsonElement args = default;
        bool hasArgs = false;

        if (item.TryGetProperty("type", out var tv))
        {
            type = (uint)Num(tv, "type");
            if (item.TryGetProperty("subType", out var sv)) subTypeArg = (byte)Num(sv, "subType");
        }
        else
        {
            var prop = item.EnumerateObject().FirstOrDefault(p => p.Name != "subType" && p.Name != "cmp"
                && p.Name != "comparator" && p.Name != "comparison" && p.Name != "uid" && p.Name != "args");
            if (prop.NameEquals("args") || prop.Value.ValueKind == JsonValueKind.Undefined)
                throw new SpecException($"第 {rowIndex + 1} 行 {key}：既没有 \"type\" 也没有具名类型");
            var map = isCondition ? CondByName : ActByName;
            if (!map.TryGetValue(prop.Name, out type))
                throw new SpecException($"第 {rowIndex + 1} 行 {key}：未知类型名 \"{prop.Name}\"。" +
                    (isCondition ? "条件" : "动作") + "可用名：" + string.Join(", ", KnownNames(isCondition)));
            args = prop.Value;
            hasArgs = true;
            if (item.TryGetProperty("subType", out var sv2)) subTypeArg = (byte)Num(sv2, "subType");
        }
        if (!hasArgs && item.TryGetProperty("args", out var av)) { args = av; hasArgs = true; }
        byte subType = subTypeArg ?? Scriptable.DefaultBodyVersion(type);

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
        if (isCondition && type >= 30)
            throw new SpecException($"第 {rowIndex + 1} 行：条件类型 {type} 越界（有效范围 0–29）");
        if (isCondition) rec.Uid = ParseComparison(item, rowIndex, key);

        rec.Fields = BuildFields(isCondition, type, subType, hasArgs ? args : default, hasArgs, rowIndex);
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
                    void ReadPaths(JsonElement pv)
                    {
                        if (pv.ValueKind != JsonValueKind.Array) return;
                        foreach (var p in pv.EnumerateArray())
                            f.Paths.Add(new GdString(p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : ""));
                    }
                    // 具名写法的 P 字段是"整个值对象"（paths/MonsterRecords/ProxyRecords + KillCount/trailing）；
                    // 位置写法直接给数组或 {paths,trailing} 对象。
                    JsonElement pobj = named ? args : v;
                    if (pobj.ValueKind == JsonValueKind.Object)
                    {
                        if (TryProp(pobj, "paths", out var pv)) ReadPaths(pv);
                        else if (name != null && TryProp(pobj, name, out pv)) ReadPaths(pv);
                        // 尾字（HasKilled / HasKilledProxy 为 KillCount）
                        if (TryProp(pobj, "trailing", out var tv2) ||
                            TryProp(pobj, "KillCount", out tv2) ||
                            TryProp(pobj, "count", out tv2))
                            f.Trailing = tv2.ValueKind == JsonValueKind.Number ? (uint)tv2.GetInt64() : 0u;
                    }
                    else if (v.ValueKind == JsonValueKind.Array) ReadPaths(v);
                    f.Count = f.Paths.Count;
                    fields.Add(f);
                    break;
                }
                case 'L':
                {
                    var f = new ScriptField { Kind = FieldKind.Lua, Params = new List<LuaParam>(), Param = name ?? "Function" };
                    // 具名写法：若给了 Function 键则取其值；否则把整个对象当作 Lua 专用写法
                    // （{"call": "...", "args": [...], "tail": {...}}）。
                    JsonElement lua = v;
                    if (named)
                    {
                        if (name != null && TryProp(args, name, out var fv)) lua = fv;
                        else if (TryProp(args, "call", out _) || TryProp(args, "function", out _) ||
                                 TryProp(args, "tail", out _) || TryProp(args, "args", out _))
                            lua = args;
                    }
                    // 具名写法的值直接是字符串（{"LuaScript": "gd.x()"}）时 args 不是数组/对象，
                    // ValueAt 取不到值，这里退回整串参数。
                    else if (hasArgs && args.ValueKind == JsonValueKind.String)
                        lua = args;
                    if (lua.ValueKind == JsonValueKind.String) f.S = new GdString(lua.GetString() ?? "");
                    else if (lua.ValueKind == JsonValueKind.Object)
                    {
                        f.S = new GdString(Str(lua, "call") ?? Str(lua, "function") ?? "");
                        if (lua.TryGetProperty("args", out var av2) && av2.ValueKind == JsonValueKind.Array)
                            foreach (var p in av2.EnumerateArray())
                                f.Params.Add(new LuaParam { S = new GdString(p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "") });
                    }
                    f.Count = f.Params.Count;
                    if (isCondition)
                    {
                        var tail = new LuaTail();
                        if (lua.ValueKind == JsonValueKind.Object && TryProp(lua, "tail", out var tv) && tv.ValueKind == JsonValueKind.Object)
                        {
                            tail.T0 = (uint)(Int(tv, "t0") ?? 0);
                            tail.T1 = (uint)(Int(tv, "t1") ?? 0);
                            tail.T2 = (byte)(Int(tv, "t2") ?? 0);
                            tail.T3 = (uint)(Int(tv, "t3") ?? 0);
                            tail.S = new GdString(Str(tv, "text") ?? Str(tv, "s") ?? "");
                        }
                        f.Tail = tail;
                    }
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

    /// <summary>条件比较符：cmp / comparator / comparison，接受 0–5 或符号/枚举名；默认 2（=）。</summary>
    private static uint ParseComparison(JsonElement item, int rowIndex, string key)
    {
        JsonElement v;
        if (!item.TryGetProperty("cmp", out v) &&
            !item.TryGetProperty("comparator", out v) &&
            !item.TryGetProperty("comparison", out v))
            return 2;

        if (v.ValueKind == JsonValueKind.Number)
        {
            long n = v.GetInt64();
            if (n is < 0 or > 5)
                throw new SpecException($"第 {rowIndex + 1} 行 {key}：比较符 {n} 越界（0..5）");
            return (uint)n;
        }
        if (v.ValueKind == JsonValueKind.String)
        {
            string s = (v.GetString() ?? "").Trim();
            return s.ToLowerInvariant() switch
            {
                "<" or "lt" or "less" => 0,
                "<=" or "le" or "lessequal" => 1,
                "=" or "==" or "eq" or "equal" => 2,
                ">=" or "ge" or "greaterequal" => 3,
                ">" or "gt" or "greater" => 4,
                "!=" or "<>" or "ne" or "notequal" => 5,
                _ => throw new SpecException(
                    $"第 {rowIndex + 1} 行 {key}：未知比较符 \"{s}\"（可用 <, <=, =, >=, >, !=）"),
            };
        }
        throw new SpecException($"第 {rowIndex + 1} 行 {key}：cmp 必须是数字 0–5 或比较符字符串");
    }

    private static bool TryProp(JsonElement o, string name, out JsonElement value)
    {
        if (o.TryGetProperty(name, out value)) return true;
        foreach (var p in o.EnumerateObject())
        {
            if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = p.Value;
            return true;
        }
        value = default;
        return false;
    }

    private static bool Bool(JsonElement o, string key)
    {
        if (!TryProp(o, key, out var v)) return false;
        return v.ValueKind == JsonValueKind.True
               || (v.ValueKind == JsonValueKind.Number && v.GetInt64() != 0);
    }

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
            "selector" or "random" => Names.StepRandom,
            "generic" => 5,
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
  // 本文件自有 ID：显式写就保留（含 0），完全省略才随机；改旧任务时照抄原值。
  // "id": 123456789,
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
