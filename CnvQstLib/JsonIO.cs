using System.Text.Json;
using System.Text.Json.Serialization;

namespace CnvQst;

/// <summary>
/// 结构化 JSON 导出/导入（全保真 DTO）。导出供阅读或程序处理；导入可改字段后重序列化为
/// .cnv/.qst。条件/动作 body 的每个字段按 Kind 区分，回写时按原顺序回放。
/// </summary>
public static class JsonIO
{
    // ---- DTO ----
    public sealed class FileDto
    {
        public string Kind = "";            // "cnv" | "qst"
        public uint Version;
        public uint Id;
        public uint LocalizationOffset;
        public uint? Unknown10;             // qst 头部 +0x10
        public List<StepDto>? Steps;        // cnv
        public List<TaskDto>? Tasks;        // qst
        public List<LangDto> Languages = new();
        public ChecksDto? Checks;
    }

    public sealed class ChecksDto
    {
        public int Size;
        public int RecordsEnd;
        public bool Tiled;
        public bool LocOffsetMatches;
    }

    public sealed class StepDto
    {
        public int Index;
        public uint Type;
        public string Kind = "";
        public string? Tag;
        public uint Unknown;
        public bool HasLink;
        public uint? LinkId;
        public string VoPath = "";
        public int Depth;
        public int Parent;
        public int Offset;
        public int Length;
        public string Text = "";
        public ListDto Conditions = new();
        public ListDto Actions = new();
        public List<int> Children = new();
    }

    public sealed class TaskDto
    {
        public uint Uid, Uid2;              // 解析计数器分配，不落盘
        public uint UnknownA8, UnknownAC;
        public bool Flag8, Flag9;
        public int Offset, Length;
        public List<EventDto> Incentives = new();
        public List<ObjectiveDto> Objectives = new();
        public List<EventDto> Events = new();
    }

    public sealed class EventDto
    {
        public uint Uid;
        public uint Unknown;
        public int Offset;
        public ListDto Conditions = new();
        public ListDto Actions = new();
    }

    public sealed class ObjectiveDto
    {
        public uint Uid;
        public uint Unknown0, Unknown1;
        public int Offset;
        public ListDto Conditions = new();
        public ListDto Actions = new();
    }

    public sealed class ListDto
    {
        public uint Logic;               // 条件集合前导字（观测 0=AND、1=OR）
        public List<RecordDto> Records = new();
    }

    public sealed class RecordDto
    {
        public bool IsCondition;
        public uint Type;
        public string Name = "";
        public byte SubType;
        public uint? Uid;
        public string BodySpec = "";
        public int Offset, Length;
        public List<FieldDto> Fields = new();
    }

    public sealed class FieldDto
    {
        public string Kind = "";            // str | u32 | u8 | p | lua
        public string? S;
        public uint? U;
        public byte? B;
        public int? Count;
        public List<string>? Paths;
        public uint? Trailing;
        public List<ParamDto>? Params;
        public TailDto? Tail;
        public string? Note;
    }

    public sealed class ParamDto
    {
        public uint A;
        public byte B;
        public uint C;
        public string S = "";
    }

    public sealed class TailDto
    {
        public uint T0, T1, T3;
        public byte T2;
        public string S = "";
    }

    public sealed class LangDto
    {
        public string Tag = "";
        public int Offset;
        public List<string> Texts = new();
    }

    private static readonly JsonSerializerOptions s_opts = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ---- 导出 ----
    public static string Export(CnvFile c, string fileName)
    {
        var dto = new FileDto
        {
            Kind = "cnv",
            Version = c.Version,
            Id = c.Id,
            LocalizationOffset = c.LocalizationOffset,
            Steps = new List<StepDto>(),
            Checks = new ChecksDto { Size = c.Size, RecordsEnd = c.StepsEnd, Tiled = c.Tiled, LocOffsetMatches = c.LocOffsetMatches },
        };
        foreach (var s in c.Steps)
        {
            dto.Steps!.Add(new StepDto
            {
                Index = s.Index,
                Type = s.Type,
                Kind = s.Kind,
                Tag = s.Tag,
                Unknown = s.Unknown,
                HasLink = s.HasLink,
                LinkId = s.HasLink ? s.LinkId : null,
                VoPath = s.VoPath.Text,
                Depth = s.Depth,
                Parent = s.Parent,
                Offset = s.Offset,
                Length = s.Length,
                Text = c.TextFor(s),
                Conditions = ToListDto(s.Conditions),
                Actions = ToListDto(s.Actions),
                Children = s.Children.Select(ch => ch.Index).ToList(),
            });
        }
        dto.Languages = c.Languages.Select(ToDto).ToList();
        return JsonSerializer.Serialize(dto, dto.GetType(), s_opts);
    }

    public static string Export(QstFile q, string fileName)
    {
        var dto = new FileDto
        {
            Kind = "qst",
            Version = q.Version,
            Id = q.Id,
            LocalizationOffset = q.LocalizationOffset,
            Unknown10 = q.Unknown10,
            Checks = new ChecksDto { Size = q.Size, RecordsEnd = q.TasksEnd, Tiled = q.Tiled, LocOffsetMatches = q.LocOffsetMatches },
        };
        dto.Tasks = q.Tasks.Select(t => new TaskDto
        {
            Uid = t.Uid,
            Uid2 = t.Uid2,
            UnknownA8 = t.UnknownA8,
            UnknownAC = t.UnknownAC,
            Flag8 = t.Flag8,
            Flag9 = t.Flag9,
            Offset = t.Offset,
            Length = t.Length,
            Incentives = t.Incentives.Select(ToDto).ToList(),
            Objectives = t.Objectives.Select(ToDto).ToList(),
            Events = t.Events.Select(ToDto).ToList(),
        }).ToList();
        dto.Languages = q.Languages.Select(ToDto).ToList();
        return JsonSerializer.Serialize(dto, dto.GetType(), s_opts);
    }

    private static ListDto ToListDto(ScriptList l) => new()
    {
        Logic = l.Present ? l.Logic : 0,
        Records = l.Records.Select(ToDto).ToList(),
    };

    private static RecordDto ToDto(ScriptRecord r) => new()
    {
        IsCondition = r.IsCondition,
        Type = r.Type,
        Name = r.DisplayName,
        SubType = r.SubType,
        Uid = r.IsCondition ? r.Uid : null,
        BodySpec = r.BodySpec,
        Offset = r.Offset,
        Length = r.Length,
        Fields = r.Fields.Select(ToDto).ToList(),
    };

    private static FieldDto ToDto(ScriptField f) => new()
    {
        Kind = f.Kind switch
        {
            FieldKind.Str => "str",
            FieldKind.U32 => "u32",
            FieldKind.U8 => "u8",
            FieldKind.P => "p",
            FieldKind.Lua => "lua",
            _ => throw new ArgumentOutOfRangeException(nameof(f)),
        },
        S = f.Kind is FieldKind.Str or FieldKind.Lua ? f.S.Text : null,
        U = f.Kind == FieldKind.U32 ? f.U : null,
        B = f.Kind == FieldKind.U8 ? f.B : null,
        Count = f.Kind is FieldKind.P or FieldKind.Lua ? f.Count : null,
        Paths = f.Paths?.Select(p => p.Text).ToList(),
        Trailing = f.Trailing,
        Params = f.Params?.Select(p => new ParamDto { A = p.A, B = p.B, C = p.C, S = p.S.Text }).ToList(),
        Tail = f.Tail is null ? null : new TailDto { T0 = f.Tail.T0, T1 = f.Tail.T1, T2 = f.Tail.T2, T3 = f.Tail.T3, S = f.Tail.S.Text },
        Note = string.IsNullOrEmpty(f.Note) ? null : f.Note,
    };

    private static LangDto ToDto(LanguageBlock l) => new()
    {
        Tag = l.Tag.Text,
        Offset = l.Offset,
        Texts = l.Texts.Select(t => t.Text).ToList(),
    };

    private static EventDto ToDto(QstEvent e) => new()
    {
        Uid = e.Uid,
        Unknown = e.Unknown,
        Offset = e.Offset,
        Conditions = ToListDto(e.Conditions),
        Actions = ToListDto(e.Actions),
    };

    private static ObjectiveDto ToDto(QstObjective o) => new()
    {
        Uid = o.Uid,
        Unknown0 = o.Unknown0,
        Unknown1 = o.Unknown1,
        Offset = o.Offset,
        Conditions = ToListDto(o.Conditions),
        Actions = ToListDto(o.Actions),
    };

    // ---- 导入（DTO → 模型 → 二进制）----
    public static byte[] ImportToBytes(string json)
    {
        var dto = JsonSerializer.Deserialize<FileDto>(json, s_opts)
                  ?? throw new GdParseException("JSON 为空或格式不符");
        return dto.Kind switch
        {
            "cnv" => CnvFromDto(dto).Write(),
            "qst" => QstFromDto(dto).Write(),
            _ => throw new GdParseException($"未知 Kind：{dto.Kind}（应为 cnv 或 qst）"),
        };
    }

    public static CnvFile CnvFromDto(FileDto dto)
    {
        var c = new CnvFile { Version = dto.Version, Id = dto.Id, Valid = true };
        var steps = new CnvStep[dto.Steps!.Count];
        for (int i = 0; i < dto.Steps.Count; i++)
        {
            var d = dto.Steps[i];
            steps[i] = new CnvStep
            {
                Index = d.Index,
                Type = d.Type,
                Unknown = d.Unknown,
                HasLink = d.HasLink,
                LinkId = d.LinkId ?? 0,
                VoPath = new GdString(d.VoPath),
                Depth = d.Depth,
                Parent = d.Parent,
            };
        }
        c.Steps.AddRange(steps);
        for (int i = 0; i < dto.Steps.Count; i++)
        {
            var d = dto.Steps[i];
            foreach (var ci in d.Children) steps[i].Children.Add(steps[ci]);
            steps[i].Conditions = FromDto(d.Conditions, isCondition: true);
            steps[i].Actions = FromDto(d.Actions, isCondition: false);
        }
        c.Languages = dto.Languages.Select(l => FromDto(l)).ToList();
        return c;
    }

    public static QstFile QstFromDto(FileDto dto)
    {
        var q = new QstFile
        {
            Version = dto.Version,
            Id = dto.Id,
            Unknown10 = dto.Unknown10 ?? 0,
            Valid = true,
        };
        foreach (var t in dto.Tasks!)
        {
            var task = new QstTask
            {
                Uid = t.Uid,
                Uid2 = t.Uid2,
                UnknownA8 = t.UnknownA8,
                UnknownAC = t.UnknownAC,
                Flag8 = t.Flag8,
                Flag9 = t.Flag9,
            };
            task.Incentives = t.Incentives.Select(e => FromDto(e)).ToList();
            task.Objectives = t.Objectives.Select(o => FromDto(o)).ToList();
            task.Events = t.Events.Select(e => FromDto(e)).ToList();
            q.Tasks.Add(task);
        }
        q.Languages = dto.Languages.Select(l => FromDto(l)).ToList();
        return q;
    }

    private static QstEvent FromDto(EventDto e) => new()
    {
        Uid = e.Uid,
        Unknown = e.Unknown,
        Conditions = FromDto(e.Conditions, true),
        Actions = FromDto(e.Actions, false),
    };

    private static QstObjective FromDto(ObjectiveDto o) => new()
    {
        Uid = o.Uid,
        Unknown0 = o.Unknown0,
        Unknown1 = o.Unknown1,
        Conditions = FromDto(o.Conditions, true),
        Actions = FromDto(o.Actions, false),
    };

    private static LanguageBlock FromDto(LangDto l) => new()
    {
        Tag = new GdString(l.Tag),
        Offset = l.Offset,
        Texts = l.Texts.Select(t => new GdString(t)).ToList(),
    };

    private static ScriptList FromDto(ListDto dto, bool isCondition)
    {
        var records = dto.Records;
        var list = new ScriptList();
        if (records.Count == 0) return list;
        list.Present = true;
        list.Count = records.Count;
        list.Logic = dto.Logic;
        foreach (var d in records)
        {
            var rec = new ScriptRecord
            {
                IsCondition = isCondition,
                Type = d.Type,
                SubType = d.SubType,
                Uid = d.Uid ?? 0,
                BodySpec = d.BodySpec,
                ClassName = d.Name,
                Named = true,
            };
            foreach (var f in d.Fields)
            {
                var field = new ScriptField
                {
                    Kind = f.Kind switch
                    {
                        "str" => FieldKind.Str,
                        "u32" => FieldKind.U32,
                        "u8" => FieldKind.U8,
                        "p" => FieldKind.P,
                        "lua" => FieldKind.Lua,
                        _ => throw new GdParseException($"未知字段 Kind：{f.Kind}"),
                    },
                };
                switch (field.Kind)
                {
                    case FieldKind.Str: field.S = new GdString(f.S ?? ""); break;
                    case FieldKind.U32: field.U = f.U ?? 0; break;
                    case FieldKind.U8: field.B = f.B ?? 0; break;
                    case FieldKind.P:
                        field.Count = f.Count ?? 0;
                        field.Paths = (f.Paths ?? []).Select(p => new GdString(p)).ToList();
                        field.Trailing = f.Trailing;
                        break;
                    case FieldKind.Lua:
                        field.S = new GdString(f.S ?? "");
                        field.Count = f.Count ?? 0;
                        field.Params = (f.Params ?? []).Select(p => new LuaParam
                        {
                            A = p.A, B = p.B, C = p.C, S = new GdString(p.S),
                        }).ToList();
                        field.Tail = f.Tail is null ? null : new LuaTail
                        {
                            T0 = f.Tail.T0, T1 = f.Tail.T1, T2 = f.Tail.T2, T3 = f.Tail.T3,
                            S = new GdString(f.Tail.S),
                        };
                        break;
                }
                rec.Fields.Add(field);
            }
            list.Records.Add(rec);
        }
        return list;
    }
}
