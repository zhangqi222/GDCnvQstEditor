using System.Text;
using System.Text.Json;

namespace CnvQst.Cli;

/// <summary>
/// CLI 命令实现（自 CnvQstTool 迁入；GUI 单 EXE 的参数模式也调用此处）。
/// Run(args, out, err) → 退出码：0=成功；1=存在失败项；2=用法错误。
/// </summary>
public static class CliApp
{
    public static int Run(string[] args, TextWriter @out, TextWriter err)
    {
        if (args.Length < 1)
        {
            err.WriteLine(Usage);
            return 2;
        }
        return args[0] switch
        {
            "dump" => CmdDump(args, @out),
            "json" => CmdJson(args, @out),
            "txt" => CmdTxt(args, @out),
            "legacytxt" => CmdLegacyTxt(args, @out),
            "import-txt" => CmdImportTxt(args, @out),
            "import-json" => CmdImportJson(args, @out),
            "verify" => CmdVerify(args, @out),
            "txt-diff" => CmdTxtDiff(args, @out),
            "anchors" => CmdAnchors(args, @out),
            "census" => CmdCensus(args, @out),
            "vocab" => CmdVocab(args, @out),
            "hash" => CmdHash(args, @out),
            "make" => CmdMake(args, @out),
            "sample" => CmdSample(args, @out),
            "probe" => CmdProbe(args, @out),
            "selftest" => CmdSelfTest(args, @out),
            "new" => CmdNew(args, @out),
            _ => Unknown(args[0], err),
        };
    }

    static int Unknown(string cmd, TextWriter err)
    {
        err.WriteLine($"未知命令：{cmd}");
        return 2;
    }

    public const string Usage = """
        用法：CnvQstStudio <命令> …
          dump        <file>                          编辑器风格树形文本
          json        <file> [out.json]               结构化 JSON 导出
          txt         <file|dir> [-o dir] [--flat]    官方本地化 TXT 导出
          legacytxt   <file>                          v0.9 旧算法基线（比对用）
          import-txt  <file> <txt> [-o out]           翻译行回填 → 重序列化
          import-json <file> <json> [-o out]          JSON 模型 → 重序列化
          verify      <file|dir>                      解析 + round-trip 校验
          txt-diff    <dir>                           新 TXT 导出 vs v0.9 基线，全量比对
          anchors     <file>                          条件/动作类型锚定工作台
          census      <dir>                           全样本类型普查（命名证据表）
          vocab                                       官方编辑器选项 ↔ 类型 ID 对照表
          hash   <文本>                               FNV1 哈希 / 任务引用标识（官方算法）
          make   <spec.json> [-o out]                 由编写用 spec 生成 .cnv/.qst
          sample cnv|qst [-o spec.json]               输出 spec 模板
          probe  cond|act|uid|sub|suball|flags|qst|all [-o 目录] [--type N] [--sub N] [--split]
                                                      批量生成探测文件（交官方编辑器辨认）
          selftest    <dir>                           结构编辑自检（增/删/移后 round-trip 三检）
          new cnv     -o out.cnv [--speaker 名] [--text 首句] [--end 结束语]
          new qst     -o out.qst [--group 组] [--name 任务名] [--task 标题] [--desc 描述]
        无参数启动本程序则进入图形界面。
        """;

    // ------------------------------------------------------------------

    static (object model, byte[] raw) Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        return Path.GetExtension(path).Equals(".cnv", StringComparison.OrdinalIgnoreCase)
            ? (CnvFile.Parse(data), data)
            : Path.GetExtension(path).Equals(".qst", StringComparison.OrdinalIgnoreCase)
                ? (QstFile.Parse(data), data)
                : throw new GdParseException($"无法按扩展名识别格式：{path}（仅支持 .cnv/.qst）");
    }

    static int CmdDump(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(Usage); return 2; }
        var (model, _) = Load(args[1]);
        string name = Path.GetFileName(args[1]);
        @out.Write(model switch
        {
            CnvFile c => Dump.DumpCnv(c, name),
            QstFile q => Dump.DumpQst(q, name),
            _ => throw new InvalidOperationException(),
        });
        return 0;
    }

    static int CmdJson(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(Usage); return 2; }
        var (model, _) = Load(args[1]);
        string name = Path.GetFileName(args[1]);
        string json = model switch
        {
            CnvFile c => JsonIO.Export(c, name),
            QstFile q => JsonIO.Export(q, name),
            _ => throw new InvalidOperationException(),
        };
        if (args.Length > 2)
        {
            File.WriteAllText(args[2], json, new UTF8Encoding(false));
            @out.WriteLine($"已写出 {args[2]}");
        }
        else @out.WriteLine(json);
        return 0;
    }

    static int CmdTxt(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(Usage); return 2; }
        string? outDir = Opt(args, "-o");
        bool flat = args.Contains("--flat");
        var results = BatchOps.ExportTxt(args[1], outDir, flat);
        foreach (var (_, target, error) in results)
            if (error != null) @out.WriteLine($"失败：{error}");
        @out.WriteLine($"共导出 {results.Count(r => r.Error == null)} 个 txt。");
        return results.Any(r => r.Error != null) ? 1 : 0;
    }

    static int CmdLegacyTxt(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(Usage); return 2; }
        byte[] data = File.ReadAllBytes(args[1]);
        @out.Write(TextExport.LegacyTxtV09(data));
        return 0;
    }

    static int CmdImportTxt(string[] args, TextWriter @out)
    {
        if (args.Length < 3) { @out.WriteLine(Usage); return 2; }
        string file = args[1], txtPath = args[2];
        string? outPath = Opt(args, "-o") ?? Path.ChangeExtension(file, ".imported" + Path.GetExtension(file));
        BatchOps.ImportTxtOne(file, txtPath, outPath);
        @out.WriteLine($"已写出 {outPath}（重解析通过）");
        return 0;
    }

    static int CmdImportJson(string[] args, TextWriter @out)
    {
        if (args.Length < 3) { @out.WriteLine(Usage); return 2; }
        string file = args[1], jsonPath = args[2];
        string? outPath = Opt(args, "-o") ?? Path.ChangeExtension(file, ".imported" + Path.GetExtension(file));
        byte[] rewritten = JsonIO.ImportToBytes(File.ReadAllText(jsonPath));
        File.WriteAllBytes(outPath, rewritten);
        @out.WriteLine($"已写出 {outPath}（{rewritten.Length} B）");
        return 0;
    }

    static int CmdVerify(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(Usage); return 2; }
        string path = args[1];
        int pass = 0, fail = 0;
        foreach (string file in BatchOps.EnumerateFiles(path))
        {
            var item = BatchOps.Inspect(file, path);
            if (item.AllOk)
            {
                pass++;
                @out.WriteLine($"PASS  {item.Rel}");
            }
            else
            {
                fail++;
                @out.WriteLine($"FAIL  {item.Rel}  {item.FailReason}");
            }
        }
        @out.WriteLine($"\n通过 {pass}，失败 {fail}。");
        return fail == 0 ? 0 : 1;
    }

    static int CmdTxtDiff(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(Usage); return 2; }
        string path = args[1];
        int same = 0, diff = 0;
        foreach (string file in BatchOps.EnumerateFiles(path))
        {
            var (model, raw) = Load(file);
            string modern = model switch
            {
                CnvFile c => TextExport.CnvTxt(c),
                QstFile q => TextExport.QstTxt(q),
                _ => "",
            };
            string legacy = TextExport.LegacyTxtV09(raw);
            if (modern == legacy) { same++; continue; }
            diff++;
            @out.WriteLine($"DIFF  {Path.GetFileName(file)}");
            string[] a = modern.Split("\r\n"), b = legacy.Split("\r\n");
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                string x = i < a.Length ? a[i] : "<无>";
                string y = i < b.Length ? b[i] : "<无>";
                if (x != y) @out.WriteLine($"   行{i + 1}  新: \"{Trunc(x, 60)}\"\n        旧: \"{Trunc(y, 60)}\"");
            }
        }
        @out.WriteLine($"\n一致 {same}，有差异 {diff}（差异应全部来自旧工具的吃字符/切割缺陷）。");
        return 0;
    }

    static int CmdAnchors(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(Usage); return 2; }
        var (model, _) = Load(args[1]);
        string name = Path.GetFileName(args[1]);
        @out.WriteLine($"── {name} 条件/动作记录（类型 ID 锚定工作台）──");

        void PrintList(string owner, ScriptList list)
        {
            foreach (var rec in list.Records)
            {
                string known = rec.Named ? "" : "  ← 未命名";
                string cmp = rec.IsCondition ? $"  cmp={rec.Uid}({Names.ComparisonName(rec.Uid)} {Names.ComparisonSymbol(rec.Uid)})" : "";
                @out.WriteLine($"{owner}  type={rec.Type,-2} bodyVer={rec.SubType}{cmp}  {Dump.Describe(rec)}{known}");
            }
        }

        switch (model)
        {
            case CnvFile c:
                foreach (var s in c.Steps)
                {
                    string text = c.TextFor(s);
                    string owner = $"[{s.Index,3}] {s.Kind,-18}";
                    if (text.Length > 0) owner += $" \"{Trunc(text, 40)}\"";
                    PrintList(owner, s.Conditions);
                    PrintList(owner, s.Actions);
                }
                break;
            case QstFile q:
                for (int ti = 0; ti < q.Tasks.Count; ti++)
                {
                    var t = q.Tasks[ti];
                    foreach (var e in t.Incentives)
                    {
                        PrintList($"task{ti} incentive", e.Conditions);
                        PrintList($"task{ti} incentive", e.Actions);
                    }
                    for (int oi = 0; oi < t.Objectives.Count; oi++)
                    {
                        var o = t.Objectives[oi];
                        PrintList($"task{ti} objective{oi}", o.Conditions);
                        PrintList($"task{ti} objective{oi}", o.Actions);
                    }
                    foreach (var e in t.Events)
                    {
                        PrintList($"task{ti} event", e.Conditions);
                        PrintList($"task{ti} event", e.Actions);
                    }
                }
                break;
        }
        return 0;
    }

    static int CmdCensus(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(Usage); return 2; }
        @out.Write(Census.Report(BatchOps.EnumerateFiles(args[1])));
        return 0;
    }

    static int CmdVocab(string[] args, TextWriter @out)
    {
        @out.Write(EditorVocab.Report());
        return 0;
    }

    /// <summary>FNV1 哈希 / 任务引用标识（官方编辑器的 Copy Id 与引用参数同款算法）。</summary>
    static int CmdHash(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(Usage); return 2; }
        string text = args[1];
        @out.WriteLine($"输入：{text}");
        @out.WriteLine($"FNV1(UTF-16LE) = 0x{Fnv1.Hash(text):X8}");
        @out.WriteLine($"FNV1(ASCII)    = 0x{Fnv1.Hash(text, false):X8}");
        @out.WriteLine($"引用标识(hash<<8, 条件/动作参数里编辑器显示 \"?\" 的那种) = 0x{Fnv1.Reference(text):X8}");
        return 0;
    }

    // ---- spec → 二进制 ----
    //   make   <spec.json> [-o out.cnv|out.qst]   由编写用 spec 生成文件
    //   sample cnv|qst [-o spec.json]             输出 spec 模板
    //   probe  <cond|act|cmp|flags|qst|all> [-o 目录] [--type N]
    //       批量生成最小文件，交给官方编辑器"自报"类型名/比较符/开关/文本槽位

    static int CmdMake(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(Usage); return 2; }
        string specPath = args[1];
        string? outPath = Arg(args, "-o") ?? Arg(args, "--out");
        try
        {
            byte[] data = Spec.Build(File.ReadAllText(specPath), out string format);
            outPath ??= Path.ChangeExtension(specPath, format == "qst" ? ".qst" : ".cnv");
            File.WriteAllBytes(outPath, data);

            // 生成物同样过三检查（与保存/导入同一条守卫）
            string check = format == "qst" ? CheckQst(data) : CheckCnv(data);
            @out.WriteLine($"已生成 {outPath}（{data.Length} 字节，format={format}）");
            @out.WriteLine(check == "OK" ? "三检查通过：铺满 / locOffset / round-trip" : $"校验失败：{check}");
            return check == "OK" ? 0 : 1;
        }
        catch (SpecException e) { @out.WriteLine($"spec 有误：{e.Message}"); return 2; }
        catch (JsonException e) { @out.WriteLine($"JSON 解析失败：{e.Message}"); return 2; }
        catch (IOException e) { @out.WriteLine($"IO 失败：{e.Message}"); return 2; }
    }

    static int CmdSample(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(Usage); return 2; }
        string text = args[1].ToLowerInvariant() switch
        {
            "cnv" => Spec.SampleCnv(),
            "qst" => Spec.SampleQst(),
            _ => null!,
        };
        if (text == null) { @out.WriteLine(Usage); return 2; }
        string? outPath = Arg(args, "-o") ?? Arg(args, "--out");
        if (outPath != null) { File.WriteAllText(outPath, text); @out.WriteLine($"已写出模板 {outPath}"); }
        else @out.WriteLine(text);
        return 0;
    }

    static int CmdProbe(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(Usage); return 2; }
        string what = args[1].ToLowerInvariant();
        string dir = Arg(args, "-o") ?? Arg(args, "--out") ?? "probe_out";
        string? typeArg = Arg(args, "--type");
        string? subArg = Arg(args, "--sub");
        try
        {
            bool split = args.Any(a => a.Equals("--split", StringComparison.OrdinalIgnoreCase));
            var items = what switch
            {
                "cond" => Probe.CondTypes(split),
                "act" => Probe.ActTypes(split),
                "uid" => Probe.UidVariants(typeArg != null ? uint.Parse(typeArg) : 6,
                                           subArg != null ? byte.Parse(subArg) : (byte)1),
                "sub" => Probe.SubVariants(typeArg != null ? uint.Parse(typeArg) : 6, split),
                "suball" => Probe.SubVariantsAll(split),
                "flags" => Probe.StepFlags(),
                "qst" => Probe.QuestTasks(),
                "all" => Probe.All(typeArg, split),
                _ => null!,
            };
            if (items == null) { @out.WriteLine(Usage); return 2; }
            @out.Write(Probe.Emit(items, dir));
            return 0;
        }
        catch (Exception e) { @out.WriteLine($"探测失败：{e.Message}"); return 2; }
    }

    private static string CheckCnv(byte[] data)
    {
        var c = CnvFile.Parse(data);
        if (!c.Valid) return c.Error ?? "解析失败";
        if (!c.Tiled) return "记录未铺满文件";
        if (!c.LocOffsetMatches) return "本地化偏移不符";
        return c.Write().SequenceEqual(data) ? "OK" : "round-trip 不一致";
    }

    private static string CheckQst(byte[] data)
    {
        var q = QstFile.Parse(data);
        if (!q.Valid) return q.Error ?? "解析失败";
        if (!q.Tiled) return "记录未铺满文件";
        if (!q.LocOffsetMatches) return "本地化偏移不符";
        return q.Write().SequenceEqual(data) ? "OK" : "round-trip 不一致";
    }

    /// <summary>取 -o / --out / --type 这类"键 + 值"参数。</summary>
    private static string? Arg(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    static int CmdSelfTest(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(Usage); return 2; }
        int pass = 0, fail = 0;
        foreach (string file in BatchOps.EnumerateFiles(args[1]).Where(f => Path.GetExtension(f).Equals(".cnv", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var c = CnvFile.Parse(File.ReadAllBytes(file));
                if (!c.Valid || c.Steps.Count == 0) continue;
                RunEditSequence(c);
                VerifyCnv(c, file, Path.GetFileName(file), @out, ref pass, ref fail);
            }
            catch (Exception e)
            {
                fail++;
                @out.WriteLine($"FAIL  {Path.GetFileName(file)}  异常 {e.Message}");
            }
        }
        @out.WriteLine($"\n结构编辑自检：通过 {pass}，失败 {fail}。");
        return fail == 0 ? 0 : 1;
    }

    /// <summary>在已解析的 CNV 副本上执行一趟增/删/移序列（不写盘，仅验证重规范化正确性）。</summary>
    private static void RunEditSequence(CnvFile c)
    {
        // 取一个非根、尽量有子节点的锚点。
        var pool = c.Steps.Where(s => s != c.Root).ToArray();
        if (pool.Length == 0) return;
        var anchor = pool[pool.Length / 2];

        // 增：在锚点下插入两个节点。
        c.InsertChild(anchor, Names.StepSpeech);
        c.InsertChild(anchor, Names.StepLink);

        // 移：移动一个节点到其兄弟相邻位（若存在）。
        var mov = c.Steps.FirstOrDefault(s => s != c.Root && !s.HasLink);
        if (mov != null) c.MoveSibling(mov, 1);

        // 删：删除一个非根、非锚的叶节点（保留足够结构）。
        var leaf = c.Steps.LastOrDefault(s => s != c.Root && s.Children.Count == 0);
        if (leaf != null) c.RemoveSubtree(leaf);

        // 校验文本槽与节点数一致、Index 连续。
        if (c.Steps.Count == 0) throw new InvalidDataException("编辑后无节点");
        for (int i = 0; i < c.Steps.Count; i++)
            if (c.Steps[i].Index != i) throw new InvalidDataException($"Index 不连续 @{i}");
        foreach (var lang in c.Languages)
            if (lang.Texts.Count != c.Steps.Count)
                throw new InvalidDataException("语言块文本槽数与节点数不一致");
    }

    private static void VerifyCnv(CnvFile c, string fullPath, string name, TextWriter @out, ref int pass, ref int fail)
    {
        try
        {
            byte[] re = c.Write();
            var r2 = CnvFile.Parse(re);
            bool ok = r2.Valid && r2.Tiled && r2.LocOffsetMatches && r2.Write().AsSpan().SequenceEqual(re);
            if (ok) { pass++; }
            else
            {
                fail++;
                @out.WriteLine($"FAIL  {name}  round-trip 未过（valid={r2.Valid} tiled={r2.Tiled} loc={r2.LocOffsetMatches} err={r2.Error}）");
            }
        }
        catch (Exception e)
        {
            fail++;
            @out.WriteLine($"FAIL  {name}  序列化异常 {e.Message}");
        }
    }

    static int CmdNew(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(Usage); return 2; }
        string? outPath = Opt(args, "-o");
        if (outPath == null) { @out.WriteLine("缺少 -o 输出路径。"); return 2; }

        object model;
        switch (args[1])
        {
            case "cnv":
                model = Creation.NewConversation(
                    Opt(args, "--speaker") ?? "New Speaker",
                    Opt(args, "--text") ?? "",
                    Opt(args, "--end") ?? "");
                break;
            case "qst":
                model = Creation.NewQuest(
                    Opt(args, "--group") ?? "New Group",
                    Opt(args, "--name") ?? "New Quest",
                    Opt(args, "--task") ?? "New Task",
                    Opt(args, "--desc") ?? "");
                break;
            default:
                @out.WriteLine("未知类型：" + args[1] + "（应为 cnv 或 qst）");
                return 2;
        }

        byte[] data = model switch
        {
            CnvFile c => c.Write(),
            QstFile q => q.Write(),
            _ => throw new InvalidOperationException(),
        };
        // 新建文件同样必须通过三检查
        var re = model is CnvFile ? CnvFile.Parse(data) : (object)QstFile.Parse(data);
        bool ok = re switch
        {
            CnvFile c => c.Valid && c.Tiled && c.LocOffsetMatches,
            QstFile q => q.Valid && q.Tiled && q.LocOffsetMatches,
            _ => false,
        };
        if (!ok) { @out.WriteLine("新建文件未通过完整性检查（内部错误）。"); return 1; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllBytes(outPath, data);
        @out.WriteLine($"已写出 {outPath}（{data.Length} B，三检查通过）");
        return 0;
    }

    // ------------------------------------------------------------------

    static string? Opt(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
