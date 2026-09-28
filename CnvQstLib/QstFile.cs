namespace CnvQst;

/// <summary>
/// .qst 任务文件（Game.dll：Quest2::Load / Quest2Task::Read / Quest2Objective::Read / Quest2Event::Read）。
///
/// 头部 24 字节："QST2" + version(≥7) + 本地化块偏移（加载器从不读）+ 任务 id + unknown + taskCount。
/// uid 不落盘：加载时从 1 起按文档顺序穿线分配（每个 task 占两个，objective/event 各占一个）。
/// 目标列表在文件中只有一份，运行时克隆成三个难度槽。
/// </summary>
public sealed class QstEvent
{
    public uint Uid;                    // 解析计数器分配，不存盘
    public uint Unknown;
    public int Offset;
    public ScriptList Conditions = new();
    public ScriptList Actions = new();
}

public sealed class QstObjective
{
    public uint Uid;
    public uint Unknown0;
    public uint Unknown1;
    public int Offset;
    public ScriptList Conditions = new();
    public ScriptList Actions = new();
}

public sealed class QstTask
{
    public uint Uid, Uid2;
    public uint UnknownA8, UnknownAC;
    public bool Flag8, Flag9;           // 版本门控尾字节（v≥8 / v≥9），漏读即错位
    public int Offset, Length;
    public List<QstEvent> Incentives = new();
    public List<QstObjective> Objectives = new();
    public List<QstEvent> Events = new();
}

public sealed class QstFile
{
    public uint Version;
    public uint LocalizationOffset;     // 头部 +0x08
    public uint Id;                     // 头部 +0x0C
    public uint Unknown10;              // 头部 +0x10
    public List<QstTask> Tasks = new();
    public List<LanguageBlock> Languages = new();

    public int TasksEnd;
    public int Size;
    public bool Tiled;
    public bool LocOffsetMatches;
    public bool Valid;
    public string? Error;

    public LanguageBlock PrimaryLanguage =>
        Languages.FirstOrDefault(l => l.Tag.Text == "enUS") ?? Languages.FirstOrDefault()
        ?? throw new GdParseException("quest has no language block");

    public int ObjectiveCount => Tasks.Sum(t => t.Objectives.Count);
    public int RecordCount => Tasks.Sum(t =>
        t.Incentives.Sum(e => e.Conditions.Records.Count + e.Actions.Records.Count)
        + t.Objectives.Sum(o => o.Conditions.Records.Count + o.Actions.Records.Count)
        + t.Events.Sum(e => e.Conditions.Records.Count + e.Actions.Records.Count));

    public static QstFile Parse(byte[] data)
    {
        var q = new QstFile { Size = data.Length };
        if (data.Length < 24 || data[0] != (byte)'Q' || data[1] != (byte)'S' || data[2] != (byte)'T' || data[3] != (byte)'2')
        {
            q.Error = "not a QST2 file";
            return q;
        }

        var r = new BinaryReaderEx(data, 4);
        try
        {
            q.Version = r.U32();
            if (q.Version < 7)
            {
                q.Error = $"quest version {q.Version} -- the loader requires 7 or above";
                return q;
            }
            q.LocalizationOffset = r.U32();
            q.Id = r.U32();
            q.Unknown10 = r.U32();
            int taskCount = r.I32();
            if (taskCount < 0 || taskCount > 4096)
                throw new GdParseException($"implausible task count {taskCount}");

            uint uid = 1;                   // Quest2::Load 把计数器置 1
            for (int i = 0; i < taskCount; i++)
            {
                var t = new QstTask { Offset = r.Pos, Uid = ++uid, Uid2 = ++uid };
                t.UnknownA8 = r.U32();
                t.UnknownAC = r.U32();
                for (int n = r.I32(); n > 0; n--) t.Incentives.Add(ReadEvent(r, ref uid));
                for (int n = r.I32(); n > 0; n--) t.Objectives.Add(ReadObjective(r, ref uid));
                for (int n = r.I32(); n > 0; n--) t.Events.Add(ReadEvent(r, ref uid));
                if (q.Version >= 8) t.Flag8 = r.U8() != 0;
                if (q.Version >= 9) t.Flag9 = r.U8() != 0;
                t.Length = r.Pos - t.Offset;
                q.Tasks.Add(t);
            }

            q.TasksEnd = r.Pos;
            q.LocOffsetMatches = q.TasksEnd == (long)q.LocalizationOffset;

            CnvFile.ReadLanguages(r, q.Languages, maxTexts: 100000);
            q.Tiled = r.Pos == data.Length;
            q.Valid = true;
        }
        catch (GdParseException e)
        {
            q.Error = e.Message;
            return q;
        }
        return q;
    }

    private static QstEvent ReadEvent(BinaryReaderEx r, ref uint uid)
    {
        var e = new QstEvent { Offset = r.Pos, Uid = ++uid, Unknown = r.U32() };
        e.Conditions = Scriptable.ReadConditionList(r, r.I32());
        e.Actions = Scriptable.ReadActionList(r, r.I32());
        return e;
    }

    private static QstObjective ReadObjective(BinaryReaderEx r, ref uint uid)
    {
        var o = new QstObjective { Offset = r.Pos, Uid = ++uid, Unknown0 = r.U32(), Unknown1 = r.U32() };
        o.Conditions = Scriptable.ReadConditionList(r, r.I32());
        o.Actions = Scriptable.ReadActionList(r, r.I32());
        return o;
    }

    /// <summary>重序列化（uid 由计数器分配，不写盘；本地化偏移按当前布局回填）。</summary>
    public byte[] Write()
    {
        var bw = new BinaryWriterEx();
        bw.Bytes("QST2"u8);
        bw.U32(Version);
        bw.U32(0);                          // 本地化偏移占位
        bw.U32(Id);
        bw.U32(Unknown10);
        bw.I32(Tasks.Count);
        foreach (var t in Tasks)
        {
            bw.U32(t.UnknownA8);
            bw.U32(t.UnknownAC);
            bw.I32(t.Incentives.Count);
            foreach (var e in t.Incentives) WriteEvent(bw, e);
            bw.I32(t.Objectives.Count);
            foreach (var o in t.Objectives) WriteObjective(bw, o);
            bw.I32(t.Events.Count);
            foreach (var e in t.Events) WriteEvent(bw, e);
            if (Version >= 8) bw.U8(t.Flag8 ? (byte)1 : (byte)0);
            if (Version >= 9) bw.U8(t.Flag9 ? (byte)1 : (byte)0);
        }
        bw.PatchU32(8, (uint)bw.Position);
        CnvFile.WriteLanguages(bw, Languages);
        return bw.ToArray();
    }

    private static void WriteEvent(BinaryWriterEx bw, QstEvent e)
    {
        bw.U32(e.Unknown);
        Scriptable.WriteConditionList(bw, e.Conditions);
        Scriptable.WriteActionList(bw, e.Actions);
    }

    private static void WriteObjective(BinaryWriterEx bw, QstObjective o)
    {
        bw.U32(o.Unknown0);
        bw.U32(o.Unknown1);
        Scriptable.WriteConditionList(bw, o.Conditions);
        Scriptable.WriteActionList(bw, o.Actions);
    }
}
