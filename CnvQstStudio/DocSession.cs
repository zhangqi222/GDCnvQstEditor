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
            if (s.ReadOnly) s.LoadError = s.Cnv.Error ?? "记录未铺满文件或未落在声明的本地化偏移";
        }
        else
        {
            s.Qst = QstFile.Parse(data);
            s.ReadOnly = !s.Qst.Valid || !s.Qst.Tiled || !s.Qst.LocOffsetMatches;
            if (s.ReadOnly) s.LoadError = s.Qst.Error ?? "记录未铺满文件或未落在声明的本地化偏移";
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
                : Cnv != null ? "未命名对话.cnv" : "未命名任务.qst";
            return name + (Dirty ? " *" : "") + (ReadOnly ? "（只读）" : "");
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
                return $"{KindVersion} · {Cnv.Steps.Count} 节点 · {recs} 条件/动作 · {Cnv.Size} B"
                     + (Cnv.Tiled ? " · ✓铺满" : " · ✗未铺满")
                     + (Cnv.LocOffsetMatches ? " · ✓locOffset" : " · ✗locOffset");
            }
            if (Qst != null)
                return $"{KindVersion} · {Qst.Tasks.Count} task · {Qst.ObjectiveCount} objective · {Qst.RecordCount} 条件/动作 · {Qst.Size} B"
                     + (Qst.Tiled ? " · ✓铺满" : " · ✗未铺满")
                     + (Qst.LocOffsetMatches ? " · ✓locOffset" : " · ✗locOffset")
                     + (Qst.TextCountDrift == 0 ? " · ✓串表公式" : $" · ✗串表公式({Qst.TextCountDrift:+#;-#;0})");
            return "（空）";
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
                err = "保存被拒绝：重解析未通过完整性检查（tiling / locOffset / round-trip）。";
        }
        else
        {
            var re = QstFile.Parse(data);
            if (!re.Valid || !re.Tiled || !re.LocOffsetMatches || !re.Write().AsSpan().SequenceEqual(data))
                err = "保存被拒绝：重解析未通过完整性检查（tiling / locOffset / round-trip）。";
            else if (re.TextCountDrift != 0)
                err = $"保存被拒绝：本地化串表条数偏离公式 2+2T+O+I+E（差 {re.TextCountDrift:+#;-#;0}）——" +
                      "官方 QuestEditor 按该公式索引串表，条数不符会直接报错。";
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
            return "写入失败：" + e.Message;
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

    public void FillTree(TreeNode root)
    {
        if (Cnv != null) FillCnv(root, Cnv);
        else if (Qst != null) FillQst(root, Qst);
        else root.Text = "（空）";

        if (!ReadOnly && LoadError == null && (Cnv != null || Qst != null))
        {
            var loc = new TreeNode(L.LocStringTable) { Tag = TagLoc };
            root.Nodes.Add(loc);
        }
        if (ReadOnly)
        {
            root.Text += "（只读" + (LoadError != null ? "：" + LoadError + "）" : "）");
        }
    }

    private static void FillCnv(TreeNode root, CnvFile c)
    {
        if (c.Steps.Count == 0) return;
        AddStep(root, c, c.Steps[0]);
    }

    private static void AddStep(TreeNode parent, CnvFile c, CnvStep s)
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
        var node = new TreeNode(label) { Tag = s };
        parent.Nodes.Add(node);
        foreach (var child in s.Children) AddStep(node, c, child);
    }

    private static void FillQst(TreeNode root, QstFile q)
    {
        string group = q.Languages.Count > 0 && q.Languages[0].Texts.Count > 0 ? q.Languages[0].Texts[0].Text : "";
        string name = q.Languages.Count > 0 && q.Languages[0].Texts.Count > 1 ? q.Languages[0].Texts[1].Text : "";
        root.Text = $"任务：{name}（组：{group}）";

        for (int i = 0; i < q.Tasks.Count; i++)
        {
            var t = q.Tasks[i];
            var tn = new TreeNode($"Task {i}") { Tag = t };
            var inc = new TreeNode($"Incentives ({t.Incentives.Count})");
            for (int k = 0; k < t.Incentives.Count; k++)
                inc.Nodes.Add(new TreeNode($"Incentive {k}") { Tag = t.Incentives[k] });
            var objs = new TreeNode($"Objectives ({t.Objectives.Count})");
            for (int k = 0; k < t.Objectives.Count; k++)
                objs.Nodes.Add(new TreeNode($"Objective {k}") { Tag = t.Objectives[k] });
            var evs = new TreeNode($"Events ({t.Events.Count})");
            for (int k = 0; k < t.Events.Count; k++)
                evs.Nodes.Add(new TreeNode($"Event {k}") { Tag = t.Events[k] });
            tn.Nodes.AddRange(new[] { inc, objs, evs });
            root.Nodes.Add(tn);
        }
    }
}
