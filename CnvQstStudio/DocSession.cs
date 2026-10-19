using System.Text;
using CnvQst;

namespace CnvQstStudio;

/// <summary>
/// 一个打开的文档（.cnv 或 .qst）：模型 + 路径 + 脏标记 + 结构树构建 + 保存三检查。
/// 解析失败或校验不过的文件以只读方式打开（可看可导出，不可保存）。
/// </summary>
public sealed class DocSession
{
    public string? FilePath;            // null = 新建/JSON 导入，尚未保存
    public CnvFile? Cnv;
    public QstFile? Qst;
    public bool Dirty;
    public bool ReadOnly;
    public string? LoadError;

    public static DocSession Open(string path)
    {
        var s = new DocSession { FilePath = path };
        byte[] data = File.ReadAllBytes(path);
        if (Path.GetExtension(path).Equals(".cnv", StringComparison.OrdinalIgnoreCase))
        {
            s.Cnv = CnvFile.Parse(data);
            s.ReadOnly = !s.Cnv.Valid || !s.Cnv.Tiled || !s.Cnv.LocOffsetMatches;
            if (s.ReadOnly) s.LoadError = s.Cnv.Error ?? L.T("记录未铺满文件或未落在声明的本地化偏移");
        }
        else
        {
            s.Qst = QstFile.Parse(data);
            s.ReadOnly = !s.Qst.Valid || !s.Qst.Tiled || !s.Qst.LocOffsetMatches;
            if (s.ReadOnly) s.LoadError = s.Qst.Error ?? L.T("记录未铺满文件或未落在声明的本地化偏移");
        }
        return s;
    }

    /// <summary>从二进制字节新建会话（JSON 导入 / 未保存的新建文件）。</summary>
    public static DocSession FromModel(object model, string? path)
    {
        var s = new DocSession { FilePath = path };
        if (model is CnvFile c) s.Cnv = c;
        else if (model is QstFile q) s.Qst = q;
        return s;
    }

    public string Title
    {
        get
        {
            string name = FilePath != null ? Path.GetFileName(FilePath)
                : Cnv != null ? L.T("未命名对话.cnv") : L.T("未命名任务.qst");
            return name + (Dirty ? " *" : "") + (ReadOnly ? L.T("（只读）") : "");
        }
    }

    public LanguageBlock PrimaryLanguage => Cnv != null ? Cnv.PrimaryLanguage : Qst!.PrimaryLanguage;

    public byte[] Serialize() => Cnv != null ? Cnv.Write() : Qst!.Write();

    public string KindVersion => Cnv != null ? $"CNV v{Cnv.Version}" : $"QST2 v{Qst!.Version}";

    public string Summary
    {
        get
        {
            if (Cnv != null)
            {
                int recs = Cnv.Steps.Sum(x => x.Conditions.Records.Count + x.Actions.Records.Count);
                return L.F("{0} · {1} 节点 · {2} 条件/动作 · {3} B", KindVersion, Cnv.Steps.Count, recs, Cnv.Size)
                     + L.T(Cnv.Tiled ? " · ✓铺满" : " · ✗未铺满")
                     + (Cnv.LocOffsetMatches ? " · ✓locOffset" : " · ✗locOffset");
            }
            if (Qst != null)
                return L.F("{0} · {1} task · {2} objective · {3} 条件/动作 · {4} B", KindVersion, Qst.Tasks.Count, Qst.ObjectiveCount, Qst.RecordCount, Qst.Size)
                     + L.T(Qst.Tiled ? " · ✓铺满" : " · ✗未铺满")
                     + (Qst.LocOffsetMatches ? " · ✓locOffset" : " · ✗locOffset")
                     + (Qst.TextCountDrift == 0 ? L.T(" · ✓串表公式") : L.F(" · ✗串表公式({0})", Qst.TextCountDrift.ToString("+#;-#;0")));
            return L.T("（空）");
        }
    }

    /// <summary>导出 dump 文本（编辑器风格树形）。</summary>
    public string DumpText()
    {
        string name = FilePath != null ? Path.GetFileName(FilePath) : Title;
        return Cnv != null ? Dump.DumpCnv(Cnv, name) : Dump.DumpQst(Qst!, name);
    }

    /// <summary>导出 JSON 文本。</summary>
    public string JsonText()
    {
        string name = FilePath != null ? Path.GetFileName(FilePath) : Title;
        return Cnv != null ? JsonIO.Export(Cnv, name) : JsonIO.Export(Qst!, name);
    }

    /// <summary>
    /// 保存三检查：重序列化 → 重解析 → tiling/locOffset/round-trip 全过才落盘。
    /// backup=true 时先把已有目标文件复制为 .bak。
    /// </summary>
    public string? Save(string path, bool backup)
    {
        byte[] data = Serialize();
        string? err = null;
        if (Cnv != null)
        {
            var re = CnvFile.Parse(data);
            if (!re.Valid || !re.Tiled || !re.LocOffsetMatches || !re.Write().AsSpan().SequenceEqual(data))
                err = L.T("保存被拒绝：重解析未通过完整性检查（tiling / locOffset / round-trip）。");
        }
        else
        {
            var re = QstFile.Parse(data);
            if (!re.Valid || !re.Tiled || !re.LocOffsetMatches || !re.Write().AsSpan().SequenceEqual(data))
                err = L.T("保存被拒绝：重解析未通过完整性检查（tiling / locOffset / round-trip）。");
            else if (re.TextCountDrift != 0)
                err = L.F("保存被拒绝：本地化串表条数偏离公式 2+2T+O+I+E（差 {0}）——", re.TextCountDrift.ToString("+#;-#;0")) +
                      L.T("官方 QuestEditor 按该公式索引串表，条数不符会直接报错。");
        }
        if (err != null) return err;

        try
        {
            if (backup && File.Exists(path))
                File.Copy(path, path + ".bak", overwrite: true);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllBytes(path, data);
        }
        catch (Exception e)
        {
            return L.T("写入失败：") + e.Message;
        }
        FilePath = path;
        Dirty = false;
        if (Cnv != null) { Cnv.Size = data.Length; Cnv.Valid = true; }
        else { Qst!.Size = data.Length; Qst.Valid = true; }
        return null;
    }

    // ------------------------------------------------------------------
    // 结构树
    // ------------------------------------------------------------------

    public const string TagLoc = "loc";

    public void FillTree(TreeNodeInfo root)
    {
        if (Cnv != null) FillCnv(root, Cnv);
        else if (Qst != null) FillQst(root, Qst);
        else root.Text = L.T("（空）");

        if (!ReadOnly && LoadError == null && (Cnv != null || Qst != null))
        {
            root.Add(L.LocStringTable, TagLoc);
        }
        if (ReadOnly)
        {
            root.Text += L.T("（只读）") + (LoadError != null ? "：" + LoadError + "" : "");
        }
    }

    private static void FillCnv(TreeNodeInfo root, CnvFile c)
    {
        if (c.Steps.Count == 0) return;
        AddStep(root, c, c.Steps[0]);
    }

    private static void AddStep(TreeNodeInfo parent, CnvFile c, CnvStep s)
    {
        string text = c.TextFor(s);
        string label = $"[{s.Index}] {s.Kind}";
        if (s.Tag != null) label += $" {{{s.Tag}}}";
        if (text.Length > 0)
        {
            string t = text.Length > 26 ? text[..26] + "…" : text.Replace("\n", " ");
            label += $" \"{t}\"";
        }
        if (s.HasLink) label += $" →[{s.LinkId}]";
        if (!s.VoPath.IsEmpty) label += " [VO]";
        var node = parent.Add(label, s);
        foreach (var child in s.Children) AddStep(node, c, child);
    }

    private static void FillQst(TreeNodeInfo root, QstFile q)
    {
        string group = q.Languages.Count > 0 && q.Languages[0].Texts.Count > 0 ? q.Languages[0].Texts[0].Text : "";
        string name = q.Languages.Count > 0 && q.Languages[0].Texts.Count > 1 ? q.Languages[0].Texts[1].Text : "";
        root.Text = L.F("任务：{0}（组：{1}）", name, group);

        for (int i = 0; i < q.Tasks.Count; i++)
        {
            var t = q.Tasks[i];
            string title = q.Languages.Count > 0 && q.TaskTitleSlot(i) < q.Languages[0].Texts.Count
                ? q.Languages[0].Texts[q.TaskTitleSlot(i)].Text : "";
            string label = L.F("任务 {0}", i + 1) + (title.Length > 0 ? $"：{title}" : "");
            var tn = root.Add(label, t);
            var inc = tn.Add(L.F("接受事件 OnAccept（{0}）", t.Incentives.Count));
            for (int k = 0; k < t.Incentives.Count; k++)
                inc.Add(L.F("接受事件 #{0}", k + 1), t.Incentives[k]);
            var objs = tn.Add(L.F("目标 Objectives（{0}）", t.Objectives.Count));
            for (int k = 0; k < t.Objectives.Count; k++)
                objs.Add(L.F("目标 #{0}", k + 1), t.Objectives[k]);
            var evs = tn.Add(L.F("完成事件 OnComplete（{0}）", t.Events.Count));
            for (int k = 0; k < t.Events.Count; k++)
                evs.Add(L.F("完成事件 #{0}", k + 1), t.Events[k]);
        }
    }
}

/// <summary>结构树节点（框架无关：由 UI 层渲染成 TreeViewItem）。</summary>
public sealed class TreeNodeInfo
{
    public string Text { get; set; } = "";
    public object? Tag;
    public List<TreeNodeInfo> Children { get; } = new();

    public TreeNodeInfo Add(string text, object? tag = null)
    {
        var n = new TreeNodeInfo { Text = text, Tag = tag };
        Children.Add(n);
        return n;
    }
}
