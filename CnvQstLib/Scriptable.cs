namespace CnvQst;

/// <summary>
/// CNV/QST 共用的 Scriptable 条件/动作系统（Game.dll：ScriptableCondition/ActionCollection::Read）。
///
///   条件   u32 type + u8 subType + u32 uid + 按类型的 body
///   动作   u32 type + u8 subType + 按类型的 body（无 uid、无前导字——两个易错的不对称点）
///
/// 条件集合自带前导 u32（logic），且调用方只在 count&gt;0 时才调 Read——
/// 因此空列表只占计数字本身，不带 logic 字。
/// </summary>
public enum FieldKind { Str, U32, U8, P, Lua }

public sealed class LuaParam
{
    public uint A;
    public byte B;
    public uint C;
    public GdString S = new("");
}

public sealed class LuaTail
{
    public uint T0;
    public uint T1;
    public byte T2;
    public uint T3;
    public GdString S = new("");
}

/// <summary>body 的一个字段。Kind 决定哪些成员有效；写入时按原顺序回放。</summary>
public sealed class ScriptField
{
    public FieldKind Kind;
    public GdString S = new("");        // Str；Lua 时为 LuaCall
    public uint U;                      // U32
    public byte B;                      // U8
    public int Count;                   // P / Lua 的参数个数
    public List<GdString>? Paths;       // P
    public uint? Trailing;              // P 末尾 u32
    public List<LuaParam>? Params;      // Lua
    public LuaTail? Tail;               // Lua（仅条件形态有固定尾部）
    public string Note = "";
    public string Param = "";           // body 参数字段名（编辑器字段表；未定名为空）

    /// <summary>深拷贝（供对话框克隆编辑、确定后回填；不共享任何可变成员）。</summary>
    public ScriptField Clone() => new()
    {
        Kind = Kind,
        S = new GdString(S.Raw),
        U = U,
        B = B,
        Count = Count,
        Paths = Paths?.Select(p => new GdString(p.Raw)).ToList(),
        Trailing = Trailing,
        Params = Params?.Select(p => new LuaParam { A = p.A, B = p.B, C = p.C, S = new GdString(p.S.Raw) }).ToList(),
        Tail = Tail == null ? null : new LuaTail { T0 = Tail.T0, T1 = Tail.T1, T2 = Tail.T2, T3 = Tail.T3, S = new GdString(Tail.S.Raw) },
        Note = Note,
        Param = Param,
    };
}

/// <summary>一个条件或一个动作。</summary>
public sealed class ScriptRecord
{
    public bool IsCondition;
    public uint Type;
    public byte SubType;
    /// <summary>
    /// 条件记录里 type/subType 之后的那 4 字节。
    ///
    /// 2026-09-28 由探测文件 + 官方编辑器比照发现：**它就是比较符**（不是 gd-utility 所说的 uid）——
    /// 同一条件（HasFaction / SURVIVORS / 10000）只改这一个字，编辑器显示的比较符跟着变：
    /// 1 → "&lt;="、3 → "&gt;="（jailor_01 真机显示 &gt;=）。动作没有这个字；QST 条件也不落盘此字
    /// （所以 QST 侧条件无从表达比较符——与语料相符）。完整编码由 `probe uid` 探测。
    /// </summary>
    public uint Uid;
    public int Offset;
    public int Length;
    public string BodySpec = "";
    public string? ClassName;           // 无法命名时为 null
    public bool Named;
    public List<ScriptField> Fields = new();

    public string DisplayName
    {
        get
        {
            if (!Named) return $"Type{Type}";
            bool inferred = IsCondition ? Names.InferredCondition(Type) : Names.InferredAction(Type);
            string label = inferred ? $"{ClassName}（推断）" : ClassName!;
            return Names.DisplayNameResolver?.Invoke(label) ?? label;
        }
    }

    /// <summary>深拷贝（不含偏移/长度，二者需重新定位）。</summary>
    public ScriptRecord Clone() => new()
    {
        IsCondition = IsCondition,
        Type = Type,
        SubType = SubType,
        Uid = Uid,
        BodySpec = BodySpec,
        ClassName = ClassName,
        Named = Named,
        Fields = Fields.Select(f => f.Clone()).ToList(),
    };
}

public sealed class ScriptList
{
    public bool Present;                // 空列表只占自己的计数字
    public int Count;                   // 流中读到的计数字（i32 原样保留供写回）
    public uint Logic;                  // 条件集合前导 u32
    public int Offset;
    public List<ScriptRecord> Records = new();

    /// <summary>深拷贝（offset 由写回重新定位）。</summary>
    public ScriptList Clone() => new()
    {
        Present = Present,
        Count = Count,
        Logic = Logic,
        Records = Records.Select(r => r.Clone()).ToList(),
    };

    /// <summary>把另一列表的内容整体覆写进来（供“克隆编辑 → 确定回填”）。offset 由写回重定位。</summary>
    public void CopyFrom(ScriptList other)
    {
        Present = other.Present;
        Count = other.Count;
        Logic = other.Logic;
        Records = other.Records;
    }
}

public static class Scriptable
{
    // body 规格字母（对齐 参考资料/gd-utility/src/scriptable.cpp）：
    //   S=字符串  I=u32  B=u8  i=u32(仅 body 版本==1)  b=u8(仅 body 版本==1)
    //   P=i32 n + n×字符串 + u32 尾字
    //   L=Lua 形态：串 + i32 n + n×{u32,u8,u32,串}，条件再加固定尾部 {u32,u32,u8,u32,串}
    //
    // 2026-09-28 反汇编官方编辑器（Common.dll）核实：
    //   · `ScriptableCondition.Read` = 先读 u8 version（我们原先叫 subType），再读 int32 comparison
    //     （我们原先叫 uid）——所以那个 u8 是**条件 body 的版本**，1 才有小写字母字段。
    //   · 条件类型共 **30 种（0–29）**，比 gd-utility 基于旧 Game.dll 的表多 4 种：
    //     26=IsChallenge、27=HasBuff、28=HasKilledMonsterClass、29=HasKilledFaction。
    //   · `ScriptableAction.Read` 只读 u8 version（动作没有 comparison 字）。
    private static readonly string[] CondBodies =
    {
        /* 0*/ "SB",   /* 1*/ "SIB",  /* 2*/ "I", /* 3*/ "I", /* 4*/ "I",
        /* 5*/ "B",    /* 6*/ "SI",   /* 7*/ "SB",/* 8*/ "I", /* 9*/ "SIb",
        /*10*/ "P",    /*11*/ "SIiB", /*12*/ "L", /*13*/ "B", /*14*/ "SIB",
        /*15*/ "SI",   /*16*/ "P",    /*17*/ "",  /*18*/ "SB",/*19*/ "SB",
        /*20*/ "SB",   /*21*/ "SI",   /*22*/ "I", /*23*/ "I", /*24*/ "I",
        /*25*/ "IB",   /*26*/ "I",    /*27*/ "SB",/*28*/ "III",/*29*/ "III",
    };

    // 动作工厂没有 0 和 16：两处返回 null，正式文件也不使用。
    private static readonly string?[] ActionBodies =
    {
        /* 0*/ null,
        /* 1*/ "SI",   /* 2*/ "S",    /* 3*/ "SI", /* 4*/ "I",  /* 5*/ "SI",
        /* 6*/ "SI",   /* 7*/ "I",    /* 8*/ "I",  /* 9*/ "S",  /*10*/ "S",
        /*11*/ "L",    /*12*/ "S",    /*13*/ "S",  /*14*/ "I",  /*15*/ "I",
        /*16*/ null,
        /*17*/ "S",    /*18*/ "I",    /*19*/ "S",  /*20*/ "SI", /*21*/ "SII",
        /*22*/ "S",    /*23*/ "S",    /*24*/ "SI", /*25*/ "I",  /*26*/ "I",
        /*27*/ "I",    /*28*/ "SB",
    };

    // body 参数字段名（对齐编辑器 cnv字段选项.txt；已命名类型才有，维度 = body 长度；
    // 'i'/'b' 占位在场但 subType!=1 时不落盘，序号保持对齐）。
    private static readonly string?[]?[] CondParams =
    {
        /* 0*/ ["QuestFile", "OnQuest"],
        /* 1*/ ["QuestFile", "Task", "TaskComplete"],
        /* 2*/ ["Experience"],                                  // HasExperience
        /* 3*/ ["IsLevel"],                                     // IsLevel
        /* 4*/ ["IsDifficulty"],                                // IsDifficulty
        /* 5*/ ["IsHardcore"],                                  // IsHardcore
        /* 6*/ ["FactionName", "Value"],                        // HasFaction
        /* 7*/ ["TokenName", "HasToken"],                       // HasToken
        /* 8*/ ["MoneyAmount"],                                 // HasMoney
        /* 9*/ ["ItemRecord", "ItemCount", "CompleteOnly"],     // HasItem
        /*10*/ ["MonsterRecords"],                              // HasKilled（P 尾字 = KillCount）
        /*11*/ ["QuestFile", "Task", "Objective", "Satisfied"], // HasQuestObjective
        /*12*/ ["Function"],                                    // Lua 条件
        /*13*/ ["KnowsPlayer"],                                 // KnowsPlayer
        /*14*/ ["QuestFile", "Task", "OnTask"],                 // OnQuestTask
        /*15*/ ["ItemRecord", "NumUsed"],                       // UsedQuestItem
        /*16*/ ["ProxyRecords"],                                // HasKilledProxy（P 尾字 = KillCount）
        /*17*/ [],                                              // WaitForCompletion
        /*18*/ ["TokenName", "HasToken"],                       // ServerHasToken
        /*19*/ ["QuestFile", "QuestBlocked"],                   // QuestBlocked
        /*20*/ ["TokenName", "HasToken"],                       // AnyoneHasToken
        /*21*/ ["Name", "RandomValue"],                         // CheckRandomValue
        /*22*/ ["DLC_ID"],                                      // OwnsDLC
        /*23*/ ["TributeAmount"],                               // HasTribute
        /*24*/ ["DevotionAmount"],                              // EarnedDevotion
        /*25*/ ["MasteryName", "HasMastery"],                    // HasMastery
        /*26*/ ["IsChallenge"],                                  // IsChallenge
        /*27*/ ["BuffName", "HasBuff"],                          // HasBuff
        /*28*/ ["Faction1", "Faction2", "KillCount"],            // HasKilledMonsterClass
        /*29*/ ["Faction1", "Faction2", "KillCount"],            // HasKilledFaction
    };

    private static readonly string?[]?[] ActionParams =
    {
        /* 0*/ null,
        /* 1*/ ["QuestFile", "Task"],                           // BeginQuestTask
        /* 2*/ ["QuestFile"],                                   // CompleteQuest
        /* 3*/ ["QuestFile", "Task"],                           // CompleteQuestTask（推断）
        /* 4*/ ["MoneyAmount"],                                 // GiveMoney
        /* 5*/ ["FactionType", "Amount"],                       // GiveFaction（推断）
        /* 6*/ ["ItemRecord", "NumItems"],                      // GiveItem
        /* 7*/ ["NumLevels"],                                   // GiveLevel
        /* 8*/ ["Experience"],                                  // GiveExperience
        /* 9*/ ["TokenName"],                                   // RemoveToken（推断）
        /*10*/ ["TokenName"],                                   // GiveToken（推断）
        /*11*/ ["Function"],                                    // Lua 动作
        /*12*/ ["Text"],                                        // DebugPrint（推断）
        /*13*/ ["SoundRecord"],                                 // PlaySound（推断；gd-utility 记作 SoundPakPreload）
        /*14*/ ["AnimationType"],                               // PlayAnimation
        /*15*/ ["NumSkillpoints"],                              // GiveSkillPoint
        /*16*/ null,
        /*17*/ ["Tag"],                                         // Notification
        /*18*/ ["NumAttributePoints"],                          // GiveAttribPoint
        /*19*/ ["ScriptEvent"],                                 // ScriptEvent（推断）
        /*20*/ ["ItemRecord", "NumItems"],                      // GiveRandomItem
        /*21*/ ["Name", "Min", "Max"],                          // GenerateRandomValue
        /*22*/ ["SkillRecord"],                                 // CastSkill
        /*23*/ ["FactionType"],                                 // UnlockFaction（推断）
        /*24*/ ["FactionType", "Value"],                        // SetFaction（推断；与 5 GiveFaction 可能互换）
        /*25*/ ["NumDevotionPoints"],                           // GiveDevotion
        /*26*/ ["TributeAmount"],                               // GiveTribute
        /*27*/ ["TutorialPage"],                                // UnlockTutorial
        /*28*/ ["VideoPath", "AllowSkip"],                      // PlayVideo
    };

    /// <summary>新建条件/动作时的默认 body 版本：语料里 HasFaction/HasItem/HasQuestObjective/LuaScript/
    /// CheckRandomValue 恒为 1，其余 0（版本错会让官方编辑器按另一种布局读，直接错位）。</summary>
    public static byte DefaultBodyVersion(uint type) => type switch
    {
        6 or 9 or 11 or 12 or 21 => 1,
        _ => 0,
    };

    public static string ConditionBodySpec(uint type) =>
        type < (uint)CondBodies.Length ? CondBodies[type] : "";

    /// <summary>body 参数字段名（下标与 body 规格字母一一对应；未命名类型为 null）。</summary>
    public static string?[]? ConditionParamNames(uint type) =>
        type < (uint)CondParams.Length ? CondParams[type] : null;

    public static string?[]? ActionParamNames(uint type) =>
        type < (uint)ActionParams.Length ? ActionParams[type] : null;

    public static string ActionBodySpec(uint type) =>
        type < (uint)ActionBodies.Length && ActionBodies[type] is string s ? s : "(unused)";

    public static ScriptList ReadConditionList(BinaryReaderEx r, int n)
    {
        var list = new ScriptList { Count = n, Offset = r.Pos };
        if (n <= 0) return list;
        list.Present = true;
        list.Logic = r.U32();
        for (int i = 0; i < n; i++)
        {
            var rec = new ScriptRecord { IsCondition = true, Offset = r.Pos };
            rec.Type = r.U32();
            rec.SubType = r.U8();
            rec.Uid = r.U32();
            if (rec.Type >= (uint)CondBodies.Length)
                throw new GdParseException($"unknown condition type {rec.Type} at 0x{rec.Offset:X}");
            rec.BodySpec = CondBodies[rec.Type];
            (rec.ClassName, rec.Named) = Names.ConditionClass(rec.Type);
            ReadBody(r, rec.BodySpec, rec.SubType, rec, condition: true);
            rec.Length = r.Pos - rec.Offset;
            list.Records.Add(rec);
        }
        return list;
    }

    public static ScriptList ReadActionList(BinaryReaderEx r, int n)
    {
        var list = new ScriptList { Count = n, Offset = r.Pos };
        if (n <= 0) return list;
        list.Present = true;
        for (int i = 0; i < n; i++)
        {
            var rec = new ScriptRecord { IsCondition = false, Offset = r.Pos };
            rec.Type = r.U32();
            rec.SubType = r.U8();
            if (rec.Type >= (uint)ActionBodies.Length || ActionBodies[rec.Type] is null)
                throw new GdParseException($"unknown action type {rec.Type} at 0x{rec.Offset:X}");
            rec.BodySpec = ActionBodies[rec.Type]!;
            (rec.ClassName, rec.Named) = Names.ActionClass(rec.Type);
            ReadBody(r, rec.BodySpec, rec.SubType, rec, condition: false);
            rec.Length = r.Pos - rec.Offset;
            list.Records.Add(rec);
        }
        return list;
    }

    private static void ReadBody(BinaryReaderEx r, string spec, byte sub, ScriptRecord rec, bool condition)
    {
        string?[]? names = (condition ? CondParams : ActionParams)[rec.Type];
        int idx = 0;
        foreach (char p in spec)
        {
            string? pm = names != null && idx < names.Length ? names[idx] : null;
            idx++;
            switch (p)
            {
                case 'S':
                    rec.Fields.Add(new ScriptField { Kind = FieldKind.Str, S = r.Str(), Param = pm ?? "" });
                    break;
                case 'I':
                    rec.Fields.Add(new ScriptField { Kind = FieldKind.U32, U = r.U32(), Param = pm ?? "" });
                    break;
                case 'B':
                    rec.Fields.Add(new ScriptField { Kind = FieldKind.U8, B = r.U8(), Param = pm ?? "" });
                    break;
                case 'i':
                    if (sub == 1)
                        rec.Fields.Add(new ScriptField { Kind = FieldKind.U32, U = r.U32(), Param = pm ?? "", Note = "present only when subType == 1" });
                    break;
                case 'b':
                    if (sub == 1)
                        rec.Fields.Add(new ScriptField { Kind = FieldKind.U8, B = r.U8(), Param = pm ?? "", Note = "present only when subType == 1" });
                    break;
                case 'P':
                {
                    var f = new ScriptField
                    {
                        Kind = FieldKind.P,
                        Count = r.I32(),
                        Paths = new List<GdString>(),
                        Param = pm ?? "paths",
                        Note = names != null ? "trailing = KillCount（HasKilled 系列）" : "trailing meaning unknown",
                    };
                    for (int i = 0; i < f.Count; i++) f.Paths.Add(r.Str());
                    f.Trailing = r.U32();
                    rec.Fields.Add(f);
                    break;
                }
                case 'L':
                {
                    var f = new ScriptField
                    {
                        Kind = FieldKind.Lua,
                        S = r.Str(),
                        Param = pm ?? "Function",
                        Note = "Lua function, resolved through LuaManager",
                        Params = new List<LuaParam>(),
                    };
                    f.Count = r.I32();
                    for (int i = 0; i < f.Count; i++)
                        f.Params.Add(new LuaParam { A = r.U32(), B = r.U8(), C = r.U32(), S = r.Str() });
                    if (condition)
                        f.Tail = new LuaTail { T0 = r.U32(), T1 = r.U32(), T2 = r.U8(), T3 = r.U32(), S = r.Str() };
                    rec.Fields.Add(f);
                    break;
                }
            }
        }
    }

    public static void WriteConditionList(BinaryWriterEx bw, ScriptList list)
    {
        bw.I32(list.Count);
        if (!list.Present) return;
        bw.U32(list.Logic);
        foreach (var rec in list.Records) WriteRecord(bw, rec);
    }

    public static void WriteActionList(BinaryWriterEx bw, ScriptList list)
    {
        bw.I32(list.Count);
        if (!list.Present) return;
        foreach (var rec in list.Records) WriteRecord(bw, rec);
    }

    private static void WriteRecord(BinaryWriterEx bw, ScriptRecord rec)
    {
        bw.U32(rec.Type);
        bw.U8(rec.SubType);
        if (rec.IsCondition) bw.U32(rec.Uid);
        foreach (var f in rec.Fields)
        {
            switch (f.Kind)
            {
                case FieldKind.Str: bw.Str(f.S); break;
                case FieldKind.U32: bw.U32(f.U); break;
                case FieldKind.U8: bw.U8(f.B); break;
                case FieldKind.P:
                    bw.I32(f.Count);
                    foreach (var p in f.Paths!) bw.Str(p);
                    bw.U32(f.Trailing ?? 0);
                    break;
                case FieldKind.Lua:
                    bw.Str(f.S);
                    bw.I32(f.Count);
                    foreach (var p in f.Params!) { bw.U32(p.A); bw.U8(p.B); bw.U32(p.C); bw.Str(p.S); }
                    if (f.Tail != null)
                    {
                        bw.U32(f.Tail.T0); bw.U32(f.Tail.T1); bw.U8(f.Tail.T2); bw.U32(f.Tail.T3);
                        bw.Str(f.Tail.S);
                    }
                    break;
            }
        }
    }
}
