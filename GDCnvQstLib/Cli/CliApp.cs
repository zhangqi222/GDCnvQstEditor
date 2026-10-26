using System.Text;
using System.Text.Json;

namespace GDCnvQstLib.Cli;

/// <summary>
/// CLI 命令实现（自 GDCnvQstEditor-cli 迁入；GUI 单 EXE 的参数模式也调用此处）。
/// Run(args, out, err) → 退出码：0=成功；1=存在失败项；2=用法错误。
/// </summary>
public static class CliApp
{
    public static int Run(string[] args, TextWriter @out, TextWriter err)
    {
        args = CliL.Initialize(args);   // 多语言：最先剥离 --language 并决定输出语言（cn/en）
        if (args.Length < 1)
        {
            err.WriteLine(CliL.UsageText);
            return 2;
        }
        if (args[0] is "help" or "--help" or "-h")
            return CmdHelp(args, @out);
        return args[0] switch
        {
            "view" or "dump" => CmdView(args, @out),
            "convert" or "json" or "make" => CmdConvert(args, @out),
            "txt" => CmdTxt(args, @out),
            "legacytxt" => CmdLegacyTxt(args, @out),
            "verify" => CmdVerify(args, @out),
            "txt-diff" => CmdTxtDiff(args, @out),
            "anchors" => CmdAnchors(args, @out),
            "census" => CmdCensus(args, @out),
            "vocab" => CmdVocab(args, @out),
            "hash" => CmdHash(args, @out),
            "sample" => CmdSample(args, @out),
            "probe" => CmdProbe(args, @out),
            "selftest" => CmdSelfTest(args, @out),
            "dev" => CmdDev(args, @out, err),
            _ => Unknown(args[0], err),
        };
    }

    static int Unknown(string cmd, TextWriter err)
    {
        err.WriteLine(CliL.F("未知命令：{0}（运行 help 查看命令列表）", cmd));
        return 2;
    }

    static int CmdHelp(string[] args, TextWriter @out)
    {
        if (args.Length < 2 || args[1].StartsWith('-'))
        {
            @out.WriteLine(CliL.UsageText);
            return 0;
        }

        string cmd = args[1];
        if (cmd.Equals("dev", StringComparison.OrdinalIgnoreCase))
        {
            @out.WriteLine(CliL.DevUsageText);
            return 0;
        }

        string lookup = cmd.ToLowerInvariant() switch
        {
            "dump" => "view",
            "json" or "make" => "convert",
            _ => cmd,
        };
        if (!lookup.Equals(cmd, StringComparison.OrdinalIgnoreCase))
            @out.WriteLine(CliL.F("{0} 是 {1} 的兼容别名。", cmd, lookup));
        if (lookup.Equals("convert", StringComparison.OrdinalIgnoreCase))
        {
            @out.WriteLine(CliL.ConvertHelpText);
            return 0;
        }

        string[] hits = (CliL.UsageText + "\n" + CliL.DevUsageText).Split('\n')
            .Where(l =>
            {
                string t = l.TrimStart();
                return t.StartsWith(lookup + " ", StringComparison.OrdinalIgnoreCase)
                       || t.StartsWith("dev " + lookup + " ", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();
        if (hits.Length == 0)
        {
            @out.WriteLine(CliL.F("未知命令：{0}", cmd));
            @out.WriteLine(CliL.T("运行 help 查看全部命令。"));
            return 2;
        }

        @out.WriteLine(CliL.T("用法："));
        foreach (string line in hits)
            @out.WriteLine("  " + line.Trim());
        return 0;
    }

    static int CmdDev(string[] args, TextWriter @out, TextWriter err)
    {
        if (args.Length < 2 || args[1] is "help" or "--help" or "-h")
        {
            @out.WriteLine(CliL.DevUsageText);
            return 0;
        }
        if (args[1].Equals("dev", StringComparison.OrdinalIgnoreCase))
        {
            err.WriteLine(CliL.T("dev 不能嵌套。"));
            return 2;
        }

        string[] rest = new string[args.Length - 1];
        rest[0] = args[1];
        Array.Copy(args, 2, rest, 1, args.Length - 2);
        return Run(rest, @out, err);
    }

    public const string Usage = """
        用法：GDCnvQstEditor-cli <命令> [参数]（GDCnvQstEditor 带参数时命令相同）
        常用命令：
          view      <文件.cnv|qst>                 查看：以树形文本显示对话/任务内容
          convert   <文件> [-o 输出]                转换：.json → QST/CNV，或 QST/CNV → .json
                                                   （省略 -o 时输出到输入同目录；help convert 看详情）
          verify    <文件|目录>                    验证：结构 / languages 对齐 / 读写字节一致（推荐先运行）
                                                   （目录同时校验 .cnv、.qst、.json）
          txt       <文件|目录> [-o 目录] [--flat]  导出官方本地化 TXT（供翻译/本地化使用）
          sample    cnv|qst [-o 输出]              释放内置全量示例（cnv_full.json / qst_full.json，含描述，可直接 convert 使用）
          help      [命令]                         显示帮助；help dev 查看开发工具
          dev       <子命令>                       开发/分析工具（一般用户可忽略）
        全局参数：--language cn|en                   输出语言：cn 中文（默认）、en 英文；显式指定时写入 %APPDATA%\GDCnvQstEditor\lang.txt 供 GUI 共用
        兼容别名：dump=view；json、make=convert（旧脚本仍可用）。
        无参数启动 GDCnvQstEditor 进入图形界面；GDCnvQstEditor-cli 无参数显示本说明。
        """;

    public const string ConvertHelp = """
        用法：convert <文件> [-o 输出]
          输入 .cnv / .qst：导出全保真 JSON 模型；修改后再 convert 即可转回。
          输入 .json     ：按 JSON 内的 format（手写 spec）或 kind（全保真模型）
                           自动生成 .cnv 或 .qst。手写 JSON 需遵循规范，
                           详见 JSON转CNV.md / JSON转QST.md，模板见 sample 命令。
          省略 -o 时：输出到输入文件同目录，按目标格式生成同名文件。
          转换方向由输入扩展名决定，输出格式由 JSON 声明决定，无需 --format。
          转换前先校验 JSON：languages 的每个语言块必须与节点/文本槽数对齐，
          不符时列出缺/多行及可填文字的槽位，且不生成文件。
        兼容别名：json、make。
        """;

    public const string DevUsage = """
        开发/分析工具（一般用户可忽略）：
          dev anchors   <文件>                       条件/动作类型锚定工作台
          dev census    <目录>                       样本类型普查（命名证据）
          dev vocab                                  官方编辑器选项 ↔ 类型 ID 对照表
          dev probe     cond|act|uid|sub|suball|flags|qst|all [-o 目录] [--type N] [--sub N] [--split]
                                                    批量生成探测文件
          dev selftest  <目录>                       结构编辑自检（增/删/移后 round-trip 三检）
          dev hash      <文本>                       FNV1 哈希（对话 id / Copy Id）
          dev legacytxt <文件>                       v0.9 TXT 算法基线（比对用）
          dev txt-diff  <目录>                       新 TXT 导出 vs v0.9 基线，全量比对
        """;

    // ------------------------------------------------------------------

    static (object model, byte[] raw) Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        return Path.GetExtension(path).Equals(".cnv", StringComparison.OrdinalIgnoreCase)
            ? (CnvFile.Parse(data), data)
            : Path.GetExtension(path).Equals(".qst", StringComparison.OrdinalIgnoreCase)
                ? (QstFile.Parse(data), data)
                : throw new GdParseException(CliL.F("无法按扩展名识别格式：{0}（仅支持 .cnv/.qst）", path));
    }

    static int CmdView(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(CliL.T("用法：view <文件.cnv|qst>")); return 2; }
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

    static int CmdConvert(string[] args, TextWriter @out)
    {
        if (args.Length < 2)
        {
            @out.WriteLine(CliL.ConvertHelpText);
            return 2;
        }

        string input = args[1];
        if (!File.Exists(input))
        {
            @out.WriteLine(CliL.F("找不到输入文件：{0}", input));
            return 1;
        }
        string? outPath = OutPath(args);
        if (outPath == null && args.Length > 2 && !args[2].StartsWith('-'))
            outPath = args[2];
        if (Arg(args, "--format") != null)
            @out.WriteLine(CliL.T("提示：convert 已自动识别转换方向，--format 已忽略。"));

        string ext = Path.GetExtension(input);
        if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jsonc", StringComparison.OrdinalIgnoreCase))
            return JsonToBinary(input, outPath, @out);
        if (ext.Equals(".cnv", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".qst", StringComparison.OrdinalIgnoreCase))
            return BinaryToJson(input, outPath, @out);

        @out.WriteLine(CliL.F("无法识别输入类型：{0}", input));
        @out.WriteLine(CliL.T("支持：.json → QST/CNV，或 .cnv/.qst → JSON。"));
        return 2;
    }

    static int JsonToBinary(string input, string? outPath, TextWriter @out)
    {
        string text;
        try { text = File.ReadAllText(input); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            @out.WriteLine(CliL.F("读取失败：{0}", e.Message));
            return 1;
        }

        string format;
        byte[] data;
        var report = ModelChecks.CheckJson(text, out format, out object? model);
        foreach (var issue in report.Issues)
            @out.WriteLine((issue.IsError ? CliL.T("错误：") : CliL.T("提示：")) + issue.Message);
        if (report.Kind.Length == 0 || report.HasErrors || model == null)
        {
            @out.WriteLine(CliL.T("JSON 校验未通过，未生成文件。"));
            return 2;
        }
        @out.WriteLine(CliL.T("JSON 校验通过：") + ModelChecks.Summary(report));
        @out.Write(SlotGuide.Report(model));
        try
        {
            data = model switch
            {
                CnvFile c => c.Write(),
                QstFile q => q.Write(),
                _ => throw new GdParseException(CliL.T("未知模型类型")),
            };
        }
        catch (GdParseException e) { @out.WriteLine(CliL.F("转换失败：{0}", e.Message)); return 2; }

        outPath ??= DefaultBinaryPath(input, format);
        string outExt = Path.GetExtension(outPath);
        if ((outExt.Equals(".cnv", StringComparison.OrdinalIgnoreCase) && format != "cnv")
            || (outExt.Equals(".qst", StringComparison.OrdinalIgnoreCase) && format != "qst"))
        {
            @out.WriteLine(CliL.F("输出扩展名与 format={0} 不一致：{1}", format, outPath));
            return 2;
        }

        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(outPath, data);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            @out.WriteLine(CliL.F("写入失败：{0}", e.Message));
            return 1;
        }

        string check = format == "qst" ? CheckQst(data) : CheckCnv(data);
        @out.WriteLine(CliL.F("已转换 {0} → {1}（format={2}，{3} 字节）", input, outPath, format, data.Length));
        @out.WriteLine(check == "OK"
            ? CliL.T("校验通过：铺满 / locOffset / round-trip / languages 对齐")
            : CliL.F("校验失败：{0}", check));
        return check == "OK" ? 0 : 1;
    }

    static int BinaryToJson(string input, string? outPath, TextWriter @out)
    {
        object model;
        try { (model, _) = Load(input); }
        catch (Exception e) when (e is IOException or GdParseException)
        {
            @out.WriteLine(CliL.F("读取失败：{0}", e.Message));
            return 1;
        }

        string json = model switch
        {
            CnvFile c => JsonIO.Export(c, Path.GetFileName(input)),
            QstFile q => JsonIO.Export(q, Path.GetFileName(input)),
            _ => throw new InvalidOperationException(),
        };
        @out.Write(SlotGuide.Report(model));
        outPath ??= DefaultJsonPath(input);
        try { File.WriteAllText(outPath, json, new UTF8Encoding(false)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            @out.WriteLine(CliL.F("写入失败：{0}", e.Message));
            return 1;
        }
        @out.WriteLine(CliL.F("已转换 {0} → {1}（JSON 模型，可修改后用 convert 转回）", input, outPath));
        return 0;
    }

    static bool TryProp(JsonElement obj, string name, out JsonElement value)
    {
        foreach (JsonProperty p in obj.EnumerateObject())
        {
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    static string DefaultJsonPath(string input)
    {
        string dir = Path.GetDirectoryName(input) ?? ".";
        string name = Path.GetFileNameWithoutExtension(input);
        return Path.Combine(dir, name + ".json");
    }

    static string DefaultBinaryPath(string input, string format)
    {
        string dir = Path.GetDirectoryName(input) ?? ".";
        string name = Path.GetFileNameWithoutExtension(input);
        if (name.EndsWith(".qst", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".cnv", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return Path.Combine(dir, name + "." + format);
    }

    static int CmdTxt(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(CliL.UsageText); return 2; }
        string? outDir = OutPath(args);
        bool flat = args.Contains("--flat");
        var results = BatchOps.ExportTxt(args[1], outDir, flat);
        foreach (var (_, target, error) in results)
            if (error != null) @out.WriteLine(CliL.F("失败：{0}", error));
        @out.WriteLine(CliL.F("共导出 {0} 个 txt。", results.Count(r => r.Error == null)));
        return results.Any(r => r.Error != null) ? 1 : 0;
    }

    static int CmdLegacyTxt(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(CliL.UsageText); return 2; }
        byte[] data = File.ReadAllBytes(args[1]);
        @out.Write(TextExport.LegacyTxtV09(data));
        return 0;
    }

    static int CmdVerify(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(CliL.UsageText); return 2; }
        string path = args[1];
        bool single = File.Exists(path);
        int pass = 0, fail = 0;
        foreach (string file in BatchOps.EnumerateFiles(path, includeJson: true))
        {
            var item = BatchOps.Inspect(file, path);
            if (item.AllOk)
            {
                pass++;
                string advice = item.SuggestedSlots > 0 ? CliL.F(" · 建议填写 {0} 行", item.SuggestedSlots) : "";
                @out.WriteLine(CliL.F("PASS  {0}  （{1}{2}）", item.Rel, item.Summary, advice));
            }
            else
            {
                fail++;
                @out.WriteLine(CliL.F("FAIL  {0}", item.Rel));
                foreach (string line in item.FailReason.Split('\n'))
                    @out.WriteLine("      " + line);
            }
            if (item.ParseOk && (single || !item.AllOk) && !string.IsNullOrWhiteSpace(item.SlotDetail))
                @out.WriteLine(item.SlotDetail.TrimEnd());
        }
        @out.WriteLine(CliL.F("\n通过 {0}，失败 {1}。", pass, fail));
        return fail == 0 ? 0 : 1;
    }

    static int CmdTxtDiff(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(CliL.UsageText); return 2; }
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
            @out.WriteLine(CliL.F("DIFF  {0}", Path.GetFileName(file)));
            string[] a = modern.Split("\r\n"), b = legacy.Split("\r\n");
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                string x = i < a.Length ? a[i] : CliL.T("<无>");
                string y = i < b.Length ? b[i] : CliL.T("<无>");
                if (x != y) @out.WriteLine(CliL.F("   行{0}  新: \"{1}\"\n        旧: \"{2}\"", i + 1, Trunc(x, 60), Trunc(y, 60)));
            }
        }
        @out.WriteLine(CliL.F("\n一致 {0}，有差异 {1}（差异应全部来自旧工具的吃字符/切割缺陷）。", same, diff));
        return 0;
    }

    static int CmdAnchors(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(CliL.UsageText); return 2; }
        var (model, _) = Load(args[1]);
        string name = Path.GetFileName(args[1]);
        @out.WriteLine(CliL.F("── {0} 条件/动作记录（类型 ID 锚定工作台）──", name));

        void PrintList(string owner, ScriptList list)
        {
            foreach (var rec in list.Records)
            {
                string known = rec.Named ? "" : CliL.T("  ← 未命名");
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
        if (args.Length < 2) { @out.WriteLine(CliL.UsageText); return 2; }
        @out.Write(Census.Report(BatchOps.EnumerateFiles(args[1])));
        return 0;
    }

    static int CmdVocab(string[] args, TextWriter @out)
    {
        @out.Write(EditorVocab.Report());
        return 0;
    }

    /// <summary>FNV1 哈希（官方编辑器 Copy Id；对话 id = FNV1(根文本)）。</summary>
    static int CmdHash(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(CliL.UsageText); return 2; }
        string text = args[1];
        @out.WriteLine(CliL.F("输入：{0}", text));
        @out.WriteLine($"FNV1(UTF-16LE) = 0x{Fnv1.Hash(text):X8}");
        @out.WriteLine($"FNV1(ASCII)    = 0x{Fnv1.Hash(text, false):X8}");
        return 0;
    }

    // ---- 内置示例 / 开发探针 ----
    //   sample cnv|qst [-o 输出]                 释放内置全量示例（cnv_full.json / qst_full.json，含描述，可直接 convert 使用）
    //   dev probe <cond|act|cmp|flags|qst|all> [-o 目录] [--type N]
    //       批量生成最小文件，交给官方编辑器"自报"类型名/比较符/开关/文本槽位

    /// <summary>sample：释放程序集内嵌的全量示例 JSON（源文件在 JSON示例\，编译时以内嵌资源打入；恒写文件，不再输出到 stdout）。</summary>
    static int CmdSample(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(CliL.UsageText); return 2; }
        string file = args[1].ToLowerInvariant() switch
        {
            "cnv" => "cnv_full.json",
            "qst" => "qst_full.json",
            _ => "",
        };
        if (file.Length == 0) { @out.WriteLine(CliL.UsageText); return 2; }

        // 读取内嵌资源全文（缺失属于发布遗漏，给出明确错误）
        string text;
        try { text = ReadResource("GDCnvQstLib.Cli.sample_" + file); }
        catch (Exception e)
        {
            @out.WriteLine(CliL.F("内置示例资源缺失：{0}（{1}）", file, e.Message));
            return 1;
        }

        // -o 决定输出位置：路径是目录（存在或以 \ / 结尾）→ 该目录下固定文件名（目录不存在则创建）；
        // 否则当作完整文件路径写；省略 -o → 当前目录。
        string? outPath = Arg(args, "-o") ?? Arg(args, "--out");
        string target;
        if (string.IsNullOrEmpty(outPath))
            target = file;
        else if (Directory.Exists(outPath) || outPath.EndsWith('\\') || outPath.EndsWith('/'))
        {
            Directory.CreateDirectory(outPath);
            target = Path.Combine(outPath, file);
        }
        else
            target = outPath;
        string? dir = Path.GetDirectoryName(Path.GetFullPath(target));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(target, text, new UTF8Encoding(false));
        @out.WriteLine(CliL.F("已释放内置全量示例 {0}（含描述，可直接 convert 使用）", target));
        return 0;
    }

    /// <summary>按完整资源名读取程序集内嵌文本资源。</summary>
    static string ReadResource(string name)
    {
        using var stream = typeof(CliApp).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("resource not found: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static int CmdProbe(string[] args, TextWriter @out)
    {
        if (args.Length < 2) { @out.WriteLine(CliL.UsageText); return 2; }
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
            if (items == null) { @out.WriteLine(CliL.UsageText); return 2; }
            @out.Write(Probe.Emit(items, dir));
            return 0;
        }
        catch (Exception e) { @out.WriteLine(CliL.F("探测失败：{0}", e.Message)); return 2; }
    }

    private static string CheckCnv(byte[] data)
    {
        var c = CnvFile.Parse(data);
        if (!c.Valid) return CliL.T(c.Error ?? "解析失败");
        if (!c.Tiled) return CliL.T("记录未铺满文件");
        if (!c.LocOffsetMatches) return CliL.T("本地化偏移不符");
        var report = ModelChecks.Check(c);
        if (!report.Ok) return report.ToText();
        return c.Write().SequenceEqual(data) ? "OK" : CliL.T("round-trip 不一致");
    }

    private static string CheckQst(byte[] data)
    {
        var q = QstFile.Parse(data);
        if (!q.Valid) return CliL.T(q.Error ?? "解析失败");
        if (!q.Tiled) return CliL.T("记录未铺满文件");
        if (!q.LocOffsetMatches) return CliL.T("本地化偏移不符");
        var report = ModelChecks.Check(q);
        if (!report.Ok) return report.ToText();
        return q.Write().SequenceEqual(data) ? "OK" : CliL.T("round-trip 不一致");
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
        if (args.Length < 2) { @out.WriteLine(CliL.UsageText); return 2; }
        int pass = 0, fail = 0;
        foreach (string file in BatchOps.EnumerateFiles(args[1]))
        {
            try
            {
                if (Path.GetExtension(file).Equals(".qst", StringComparison.OrdinalIgnoreCase))
                {
                    var q = QstFile.Parse(File.ReadAllBytes(file));
                    if (!q.Valid) continue;
                    var report = RunQstEditSequence(q);
                    VerifyQst(q, file, Path.GetFileName(file), @out, ref pass, ref fail, report);
                    continue;
                }
                var c = CnvFile.Parse(File.ReadAllBytes(file));
                if (!c.Valid || c.Steps.Count == 0) continue;
                RunEditSequence(c);
                VerifyCnv(c, file, Path.GetFileName(file), @out, ref pass, ref fail);
            }
            catch (Exception e)
            {
                fail++;
                @out.WriteLine(CliL.F("FAIL  {0}  异常 {1}", Path.GetFileName(file), e.Message));
            }
        }
        @out.WriteLine(CliL.F("\n结构编辑自检：通过 {0}，失败 {1}。", pass, fail));
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
        if (c.Steps.Count == 0) throw new InvalidDataException(CliL.T("编辑后无节点"));
        for (int i = 0; i < c.Steps.Count; i++)
            if (c.Steps[i].Index != i) throw new InvalidDataException(CliL.F("Index 不连续 @{0}", i));
        foreach (var lang in c.Languages)
            if (lang.Texts.Count != c.Steps.Count)
                throw new InvalidDataException(CliL.T("语言块文本槽数与节点数不一致"));
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
                @out.WriteLine(CliL.F("FAIL  {0}  round-trip 未过（valid={1} tiled={2} loc={3} err={4}）",
                    name, r2.Valid, r2.Tiled, r2.LocOffsetMatches, r2.Error));
            }
        }
        catch (Exception e)
        {
            fail++;
            @out.WriteLine(CliL.F("FAIL  {0}  序列化异常 {1}", name, e.Message));
        }
    }

    /// <summary>
    /// QST 结构编辑自检：新增/删除 task、objective、event 后，串表必须仍严格等于公式
    /// `2 + 2T + O + I + E`，且重解析三检查全过、字节一致。
    /// 返回这趟编辑的动作描述，便于在输出里看清做了什么。
    /// </summary>
    private static string RunQstEditSequence(QstFile q)
    {
        var t = q.Tasks.FirstOrDefault() ?? q.AddTask();
        int t0 = q.Tasks.Count, o0 = q.ObjectiveCount, i0 = q.IncentiveCount, e0 = q.EventCount;

        q.AddObjective(t);
        q.AddObjective(t);
        q.AddEvent(t, true);            // OnAccept
        q.AddEvent(t, false);           // OnComplete
        var extra = q.AddTask();
        q.AddObjective(extra);
        q.AddEvent(extra, false);

        // 删：删掉刚加的 task（及其目标/事件），删掉 t 的最后一个 objective 与最后一个 event
        q.RemoveTask(extra);
        if (t.Objectives.Count > 1) q.RemoveObjective(t, t.Objectives[^1]);
        if (t.Events.Count > 1) q.RemoveEvent(t, t.Events[^1]);
        if (t.Incentives.Count > 1) q.RemoveEvent(t, t.Incentives[^1]);

        int drift = q.TextCountDrift;
        if (drift != 0)
            throw new InvalidDataException(CliL.F("编辑后串表条数偏离公式 {0}（应为 {1}，实为 {2}）",
                drift.ToString("+0;-0;0"), q.RequiredTextCount, q.Languages[0].Texts.Count));

        return CliL.F("T {0}→{1}  O {2}→{3}  I {4}→{5}  E {6}→{7}  串表={8}",
            t0, q.Tasks.Count, o0, q.ObjectiveCount, i0, q.IncentiveCount, e0, q.EventCount, q.Languages[0].Texts.Count);
    }

    private static void VerifyQst(QstFile q, string fullPath, string name, TextWriter @out, ref int pass, ref int fail, string note)
    {
        try
        {
            byte[] re = q.Write();
            var r2 = QstFile.Parse(re);
            bool ok = r2.Valid && r2.Tiled && r2.LocOffsetMatches && r2.TextCountDrift == 0
                      && r2.Write().AsSpan().SequenceEqual(re);
            if (ok) { pass++; @out.WriteLine(CliL.F("PASS  {0,-32} {1}", name, note)); }
            else
            {
                fail++;
                @out.WriteLine(CliL.F("FAIL  {0}  round-trip 未过（valid={1} tiled={2} loc={3} drift={4} err={5}）",
                    name, r2.Valid, r2.Tiled, r2.LocOffsetMatches, r2.TextCountDrift, r2.Error));
            }
        }
        catch (Exception e)
        {
            fail++;
            @out.WriteLine(CliL.F("FAIL  {0}  序列化异常 {1}", name, e.Message));
        }
    }

    // ------------------------------------------------------------------

    static string? OutPath(string[] args) => Arg(args, "-o") ?? Arg(args, "--out");

    static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
