namespace GDCnvQstLib;

/// <summary>
/// .cnv 对话文件（Game.dll：Conversation::Build / ConversationStep::Read）。
///
/// 头部 16 字节："CNV " + version(≥5) + 本地化块偏移（工具链写入、加载器从不读）+ 对话 id，
/// 0x10 起即根节点内联记录。节点深度前序递归，节点在深度前序走序中的位置即其
/// 本地化字符串索引；type 7 为跳转，linkId 是展平节点列表的下标。
/// </summary>
public sealed class CnvStep
{
    public int Index;                   // 深度前序位置 == 本地化索引
    public uint Type;
    public uint Unknown;                // ConversationStep+0x50，语义未知
    public bool HasLink;
    public uint LinkId;                 // 仅 Type==7
    public GdString VoPath = new("");   // 仅 version>=6；SoundManager::Load 使用
    public int Offset;
    public int Length;
    public int Depth;
    public int Parent = -1;
    public ScriptList Conditions = new();
    public ScriptList Actions = new();
    public List<CnvStep> Children = new();

    public string Kind => Names.CnvStepKind(Type);
    public string? Tag => Names.CnvStepTag(Type);
}

public sealed class LanguageBlock
{
    public GdString Tag = new("");      // "enUS"、"deDE"…
    public int Offset;
    public List<GdString> Texts = new();
}

public sealed class CnvFile
{
    public uint Version;
    public uint LocalizationOffset;     // 头部 +0x08
    public uint Id;                     // 头部 +0x0C
    public List<CnvStep> Steps = new(); // 展平、深度前序；下标即本地化索引、linkId 直接索引此表
    public List<LanguageBlock> Languages = new();

    public int StepsEnd;
    public int Size;
    public bool Tiled;
    public bool LocOffsetMatches;
    public bool Valid;
    public string? Error;

    public CnvStep Root => Steps[0];

    public LanguageBlock PrimaryLanguage =>
        Languages.FirstOrDefault(l => l.Tag.Text == "enUS") ?? Languages.FirstOrDefault()
        ?? throw new GdParseException("conversation has no language block");

    public static CnvFile Parse(byte[] data)
    {
        var c = new CnvFile { Size = data.Length };
        if (data.Length < 16 || data[0] != (byte)'C' || data[1] != (byte)'N' || data[2] != (byte)'V' || data[3] != (byte)' ')
        {
            c.Error = "not a CNV file";
            return c;
        }

        var r = new BinaryReaderEx(data, 4);
        try
        {
            c.Version = r.U32();
            if (c.Version < 5)
            {
                // 加载器对旧版直接放弃（"Conversation file is old version!"）。
                c.Error = $"conversation version {c.Version} -- the loader requires 5 or above";
                return c;
            }
            c.LocalizationOffset = r.U32();
            c.Id = r.U32();

            ReadStep(r, c, c.Version, depth: 0, parent: -1);
            c.StepsEnd = r.Pos;
            c.LocOffsetMatches = c.StepsEnd == (long)c.LocalizationOffset;

            ReadLanguages(r, c.Languages, maxTexts: 200000);
            c.Tiled = r.Pos == data.Length;
            c.Valid = true;
        }
        catch (GdParseException e)
        {
            c.Error = e.Message;
            return c;
        }
        return c;
    }

    private static void ReadStep(BinaryReaderEx r, CnvFile c, uint version, int depth, int parent)
    {
        if (depth > 512)
            throw new GdParseException("conversation nested past 512 deep");

        var s = new CnvStep
        {
            Index = c.Steps.Count,
            Offset = r.Pos,
            Depth = depth,
            Parent = parent,
        };
        c.Steps.Add(s);
        if (parent >= 0) c.Steps[parent].Children.Add(s);

        int childCount = r.I32();
        if (childCount < 0 || childCount > 100000)
            throw new GdParseException($"implausible child count {childCount}");

        s.Type = r.U32();
        s.Unknown = r.U32();
        if (s.Type == Names.StepLink)
        {
            s.HasLink = true;
            s.LinkId = r.U32();
        }
        if (version >= 6) s.VoPath = r.Str();

        s.Conditions = Scriptable.ReadConditionList(r, r.I32());
        s.Actions = Scriptable.ReadActionList(r, r.I32());

        for (int i = 0; i < childCount; i++)
            ReadStep(r, c, version, depth + 1, s.Index);
        s.Length = r.Pos - s.Offset;
    }

    internal static void ReadLanguages(BinaryReaderEx r, List<LanguageBlock> into, int maxTexts)
    {
        int langCount = r.I32();
        if (langCount < 0 || langCount > 64)
            throw new GdParseException($"implausible language count {langCount}");
        for (int i = 0; i < langCount; i++)
        {
            var lang = new LanguageBlock { Offset = r.Pos, Tag = r.Str() };
            int n = r.I32();
            if (n < 0 || n > maxTexts)
                throw new GdParseException($"implausible text count {n}");
            for (int k = 0; k < n; k++) lang.Texts.Add(r.Str());
            into.Add(lang);
        }
    }

    /// <summary>节点的显示文本（指定语言，-1 取首个）；空文本的 type 7 沿用跳转目标的文本（GetText 同款回退）。</summary>
    public string TextFor(CnvStep s, int lang = -1)
    {
        if (Languages.Count == 0) return "";
        int li = lang < 0 ? 0 : lang;
        if (li >= Languages.Count) return "";
        var texts = Languages[li].Texts;
        if (s.Index >= texts.Count) return "";
        if (!texts[s.Index].IsEmpty) return texts[s.Index].Text;
        if (s.Type == Names.StepLink && s.LinkId < (uint)texts.Count)
            return texts[(int)s.LinkId].Text;
        return "";
    }

    /// <summary>重序列化。布局与解析严格互逆；头部本地化偏移按当前布局回填。</summary>
    public byte[] Write()
    {
        if (Steps.Count == 0) throw new InvalidOperationException("conversation has no root step");
        var bw = new BinaryWriterEx();
        bw.Bytes("CNV "u8);
        bw.U32(Version);
        bw.U32(0);                                  // 本地化偏移占位
        bw.U32(Id);
        WriteStep(bw, Steps[0], Version);
        bw.PatchU32(8, (uint)bw.Position);
        WriteLanguages(bw, Languages);
        return bw.ToArray();
    }

    private static void WriteStep(BinaryWriterEx bw, CnvStep s, uint version)
    {
        bw.I32(s.Children.Count);
        bw.U32(s.Type);
        bw.U32(s.Unknown);
        if (s.HasLink) bw.U32(s.LinkId);
        if (version >= 6) bw.Str(s.VoPath);         // v5 无配音路径字段
        Scriptable.WriteConditionList(bw, s.Conditions);
        Scriptable.WriteActionList(bw, s.Actions);
        foreach (var child in s.Children) WriteStep(bw, child, version);
    }

// ------------------------------------------------------------------
    // 结构编辑（表格 UI 调用）：增/删/移节点后重规范化。
    // 不变量：Steps[] 恒为深度前序（下标==本地化索引、linkId 直接下标此表）；
    // 每个语言块 Texts 长度恒等于 Steps.Count，文本跟随节点身份移动（同步拼接/删除）。
    // 所有操作用完必须经过 DocSession.Save 的三检查兜底。
    // 操作逻辑：先修改 Children 树 → Renormalize 重建 Steps 并重映射。
    // ------------------------------------------------------------------

    /// <summary>在指定锚点下新增一个子节点（追加到其末位）。root 之外的任意节点都可作锚点。
    /// 注意：type 7（link）节点在流里恒带一个 LinkId，故创建时同步置 HasLink 与自指目标。</summary>
    public CnvStep InsertChild(CnvStep anchor, uint type)
    {
        var child = new CnvStep { Type = type, HasLink = type == Names.StepLink };
        anchor.Children.Add(child);
        Renormalize();
        if (child.HasLink) child.LinkId = (uint)child.Index;   // 自指，安全的目标
        return child;
    }

    /// <summary>删除节点整棵子树；拒绝删除根节点。</summary>
    public bool RemoveSubtree(CnvStep step)
    {
        if (step == Root) return false;
        // Parent 由上一次 Renormalize 设置，指向 Steps 下标
        var parent = Steps[step.Parent];
        parent.Children.Remove(step);
        Renormalize();
        return true;
    }

    /// <summary>把节点在同父兄弟序列中上移/下移 ±1（交换其子树块）；根节点忽略，越界忽略。</summary>
    public void MoveSibling(CnvStep step, int delta)
    {
        if (step == Root) return;
        var parent = Steps[step.Parent];
        int idx = parent.Children.IndexOf(step);
        int to = idx + delta;
        if (idx < 0 || to < 0 || to >= parent.Children.Count) return;
        (parent.Children[idx], parent.Children[to]) = (parent.Children[to], parent.Children[idx]);
        Renormalize();
    }

    /// <summary>
    /// 按当前 Children 树重建 Steps 表（Index/Depth/Parent/Children 同步），把 linkId 按
    /// "指向同一节点"重映射（目标随删除消失时 clamp 到最近存活祖先），并使每个语言块
    /// 的 Texts 长度对齐 Steps.Count、文本跟随节点身份（拼接/删除槽位）。
    /// </summary>
    public void Renormalize()
    {
        if (Steps.Count == 0) return;

        // 快照原始映射（按原始 Index == 位置）。Index/Parent 会被下面的 Walk 覆写，
        // 必须先取原值再重排，否则 linkId 祖先链回溯会用新值错乱。
        int n = Steps.Count;
        var origSteps = new CnvStep[n];
        var origParent = new int[n];          // 原始 Index → 父的原始 Index（-1=根）
        for (int i = 0; i < n; i++)
        {
            origSteps[i] = Steps[i];
            origParent[i] = Steps[i].Parent;
        }

        // 重建 Steps：深度前序遍历（Children 树为准）。
        var newSteps = new List<CnvStep>();
        void Walk(CnvStep s, int depth, int parentIdx)
        {
            s.Index = newSteps.Count;
            s.Depth = depth;
            s.Parent = parentIdx;
            newSteps.Add(s);
            foreach (var c in s.Children)
                Walk(c, depth + 1, s.Index);
        }
        Walk(Root, 0, -1);

        // linkId 重映射：指向同一节点；目标已删则沿“原始”祖先链找最近存活者。
        foreach (var s in newSteps)
        {
            if (!s.HasLink) continue;
            uint oldTarget = s.LinkId;
            if (oldTarget >= (uint)n) { s.LinkId = 0; continue; }
            var target = origSteps[(int)oldTarget];
            int ni = newSteps.IndexOf(target);
            if (ni >= 0) { s.LinkId = (uint)ni; continue; }
            int anc = origParent[(int)oldTarget];
            uint fallback = 0;
            while (anc >= 0 && anc < n)
            {
                int na = newSteps.IndexOf(origSteps[anc]);
                if (na >= 0) { fallback = (uint)na; break; }
                anc = origParent[anc];
            }
            s.LinkId = fallback;
        }

        // 语言块：文本跟随节点身份。
        foreach (var lang in Languages)
        {
            var oldTexts = lang.Texts.ToArray();
            lang.Texts.Clear();
            foreach (var s in newSteps)
            {
                int oi = Array.IndexOf(origSteps, s);
                lang.Texts.Add(oi >= 0 && oi < oldTexts.Length
                    ? oldTexts[oi]
                    : new GdString(""));
            }
        }

        Steps = newSteps;
    }

    internal static void WriteLanguages(BinaryWriterEx bw, List<LanguageBlock> languages)
    {
        bw.I32(languages.Count);
        foreach (var lang in languages)
        {
            bw.Str(lang.Tag);
            bw.I32(lang.Texts.Count);
            foreach (var t in lang.Texts) bw.Str(t);
        }
    }
}
