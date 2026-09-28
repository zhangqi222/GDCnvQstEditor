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

    public int IncentiveCount => Tasks.Sum(t => t.Incentives.Count);
    public int EventCount => Tasks.Sum(t => t.Events.Count);

    /// <summary>
    /// 本地化串表条数公式（2026-09-28 由本地 247 个样本零误差反推，并在官方 QuestEditor 中核实）：
    ///   S = 2 + 2T + O + I + E
    ///   2  = 组名 + 任务名；2T = 每 task 标题 + 描述；O/I/E = 各 objective / incentive / event 一条。
    /// 官方编辑器按此式索引串表——串数不足会直接抛 ArgumentOutOfRangeException，所以
    /// 增删 task/objective/event 后必须按此式重建串表（多任务文件的编辑约束即在此）。
    /// </summary>
    public static int LocalizedTextCount(int tasks, int objectives, int incentives, int events) =>
        2 + 2 * tasks + objectives + incentives + events;

    /// <summary>本文件按公式应有的串表条数。</summary>
    public int RequiredTextCount => LocalizedTextCount(Tasks.Count, ObjectiveCount, IncentiveCount, EventCount);

    /// <summary>串表条数与公式不符时返回差值（0 = 正确）。</summary>
    public int TextCountDrift => (Languages.Count > 0 ? Languages[0].Texts.Count : 0) - RequiredTextCount;

    /// <summary>
    /// 串表槽位的角色说明（供 dump/GUI 显示）。顺序即官方 QuestEditor 的索引顺序：
    /// group、name，然后每个 task 的 [标题, 描述, OnAccept…, 目标…, OnComplete…]。
    /// </summary>
    public string DescribeSlot(int index)
    {
        if (index == 0) return "group 任务组名";
        if (index == 1) return "name 任务名";
        int i = 2;
        for (int ti = 0; ti < Tasks.Count; ti++)
        {
            var t = Tasks[ti];
            if (index == i++) return $"T{ti + 1} 标题";
            if (index == i++) return $"T{ti + 1} 描述";
            for (int k = 0; k < t.Incentives.Count; k++) if (index == i++) return $"T{ti + 1} OnAccept #{k + 1}";
            for (int k = 0; k < t.Objectives.Count; k++) if (index == i++) return $"T{ti + 1} 目标 #{k + 1}";
            for (int k = 0; k < t.Events.Count; k++) if (index == i++) return $"T{ti + 1} OnComplete #{k + 1}";
        }
        return $"（越界槽 {index}）";
    }

    // ------------------------------------------------------------------
    // 结构编辑（task / objective / event 增删）：按公式与槽位顺序重建本地化表。
    //
    // 槽位顺序（官方 QuestEditor 中逐槽核实，见 qst_order.qst 的截图）：
    //   [组名, 任务名] + 每个 task: [标题, 描述, incentives(OnAccept)…, objectives…, events(OnComplete)…]
    // 文本按下标**跟随所属实体**移动（快照按实体身份保存），因此增删不会串行。
    // ------------------------------------------------------------------

    /// <summary>槽位归属键：(所属对象, 同对象内的槽号)。组名/任务名的 owner 是 QstFile 自身。</summary>
    private static List<(object owner, int tag)> SlotKeys(QstFile q)
    {
        var keys = new List<(object, int)> { (q, 0), (q, 1) };
        foreach (var t in q.Tasks)
        {
            keys.Add((t, 0));                                   // 标题
            keys.Add((t, 1));                                   // 描述
            for (int i = 0; i < t.Incentives.Count; i++) keys.Add((t, 2 + i));
            for (int i = 0; i < t.Objectives.Count; i++) keys.Add((t, 2 + t.Incentives.Count + i));
            for (int i = 0; i < t.Events.Count; i++) keys.Add((t, 2 + t.Incentives.Count + t.Objectives.Count + i));
        }
        return keys;
    }

    /// <summary>取出某语言块的"槽归属 → 文本"快照（结构改动前调用）。</summary>
    public Dictionary<(object owner, int tag), string> SnapshotTexts(int language = 0)
    {
        var map = new Dictionary<(object, int), string>();
        if (language >= Languages.Count) return map;
        var keys = SlotKeys(this);
        var texts = Languages[language].Texts;
        for (int i = 0; i < keys.Count && i < texts.Count; i++) map[keys[i]] = texts[i].Text;
        return map;
    }

    public List<Dictionary<(object, int), string>> SnapshotAll() =>
        Languages.Select((_, i) => SnapshotTexts(i)).ToList();

    /// <summary>按当前结构重建全部语言块的串表；有快照则按实体身份回填文本，缺失的填空槽。</summary>
    public void RebuildTexts(List<Dictionary<(object, int), string>>? snapshot = null)
    {
        var keys = SlotKeys(this);
        for (int li = 0; li < Languages.Count; li++)
        {
            var old = snapshot != null && li < snapshot.Count ? snapshot[li] : null;
            var texts = new List<GdString>(keys.Count);
            foreach (var k in keys)
                texts.Add(new GdString(old != null && old.TryGetValue(k, out var v) ? v : ""));
            Languages[li].Texts = texts;
        }
    }

    /// <summary>新增 task（本地化表按公式补槽）。uid 为 0 时按官方做法给一个随机 uid
    /// （QuestEditor 新建 task 走的就是 `App.random.UInt()`；他处引用本 task 用的就是这个值）。</summary>
    public QstTask AddTask(QstTask? task = null)
    {
        var snap = SnapshotAll();
        task ??= new QstTask();
        if (task.UnknownA8 == 0) task.UnknownA8 = RandomUid();
        Tasks.Add(task);
        RebuildTexts(snap);
        return task;
    }

    /// <summary>新增 objective（uid 同样按官方随机生成）。</summary>
    public QstObjective AddObjective(QstTask task, QstObjective? objective = null)
    {
        var snap = SnapshotAll();
        objective ??= new QstObjective();
        if (objective.Unknown0 == 0) objective.Unknown0 = RandomUid();
        task.Objectives.Add(objective);
        RebuildTexts(snap);
        return objective;
    }

    /// <summary>与官方同款的随机 uid（非 0，避免与"未设置"混淆）。</summary>
    public static uint RandomUid()
    {
        uint v;
        do { v = (uint)Random.Shared.NextInt64(0, uint.MaxValue); } while (v == 0);
        return v;
    }

    /// <summary>删除 task（含其 objectives/events 的槽位）。</summary>
    public bool RemoveTask(QstTask task)
    {
        var snap = SnapshotAll();
        bool ok = Tasks.Remove(task);
        if (ok) RebuildTexts(snap);
        return ok;
    }

    public bool RemoveObjective(QstTask task, QstObjective objective)
    {
        var snap = SnapshotAll();
        bool ok = task.Objectives.Remove(objective);
        if (ok) RebuildTexts(snap);
        return ok;
    }

    /// <summary>incentive=true 加进 Incentives（编辑器 OnAccept），否则加进 Events（OnComplete）。</summary>
    public QstEvent AddEvent(QstTask task, bool incentive, QstEvent? evt = null)
    {
        var snap = SnapshotAll();
        evt ??= new QstEvent();
        (incentive ? task.Incentives : task.Events).Add(evt);
        RebuildTexts(snap);
        return evt;
    }

    public bool RemoveEvent(QstTask task, QstEvent evt)
    {
        var snap = SnapshotAll();
        bool ok = task.Incentives.Remove(evt) || task.Events.Remove(evt);
        if (ok) RebuildTexts(snap);
        return ok;
    }

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
