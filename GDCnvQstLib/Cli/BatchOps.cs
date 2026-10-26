using System.Text;

namespace GDCnvQstLib.Cli;

/// <summary>批量操作的单文件结果（GUI 表格与 CLI 输出共用同一模型）。</summary>
public sealed class BatchItem
{
    public string Path = "";
    public string Rel = "";
    public string Kind = "";            // "CNV" / "QST2" / "?"（无法识别）
    public uint Version;
    public int StepsOrTasks;            // CNV=节点数；QST=task 数
    public bool ParseOk;
    public bool Tiled;
    public bool LocOk;
    public bool RoundTripOk;
    public string? Error;
    public int TxtLines;
    /// <summary>QST 专用：串表条数与公式 2+2T+O+I+E 的差值（0 = 正确）。</summary>
    public int TextDrift;
    /// <summary>languages 的每个语言块是否都与节点/槽位数对齐。</summary>
    public bool LangOk = true;
    /// <summary>对齐失败时的逐条说明（含缺/多行数与可填槽位）。</summary>
    public string? LangDetail;
    /// <summary>应有的文本槽位数（CNV=节点数；QST=2+2T+O+I+E）。</summary>
    public int TextSlots;
    /// <summary>按结构规则建议填写的槽位数（仅提醒，不阻止转换）。</summary>
    public int SuggestedSlots;
    /// <summary>完整槽位明细（行号 / 槽位名 / 填写建议），供 verify 输出。</summary>
    public string? SlotDetail;

    public bool AllOk => ParseOk && Tiled && LocOk && RoundTripOk && LangOk;

    public string FailReason
    {
        get
        {
            string reason =
                Error ?? (!ParseOk ? CliL.T("解析失败") : !Tiled ? CliL.T("记录未铺满文件") : !LocOk ? CliL.T("未落在声明的本地化偏移")
                    : !RoundTripOk ? CliL.T("round-trip 不一致") : "");
            if (LangOk) return reason;
            return (reason.Length > 0 ? reason + "\n" : "") + LangDetail;
        }
    }

    /// <summary>通过时的简要说明（节点/任务数与文本槽行数）。</summary>
    public string Summary => Kind switch
    {
        "CNV" or "CNV-JSON" => CliL.F("CNV {0} 节点 · 文本槽 {1} 行", StepsOrTasks, TextSlots),
        "QST2" or "QST2-JSON" => CliL.F("QST {0} task · 文本槽 {1} 行", StepsOrTasks, TextSlots),
        _ => Kind,
    };
}

/// <summary>批量文件服务：扫描/检查/导出/回填。GUI 批量面板与 CLI 命令共用，保证行为一致。</summary>
public static class BatchOps
{
    /// <summary>枚举待处理文件：路径为文件则按扩展名识别，为目录则递归枚举 .cnv/.qst（verify 时再加 .json）。</summary>
    public static List<string> EnumerateFiles(string path, bool includeJson = false)
    {
        if (File.Exists(path))
            return [path];
        if (!Directory.Exists(path))
            throw new FileNotFoundException(path);
        var files = new List<string>();
        foreach (var ext in includeJson ? new[] { "*.cnv", "*.qst", "*.json" } : new[] { "*.cnv", "*.qst" })
            files.AddRange(Directory.EnumerateFiles(path, ext, SearchOption.AllDirectories));
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    /// <summary>解析 + 三项完整性检查 + round-trip 字节比对 + TXT 行数统计。</summary>
    public static BatchItem Inspect(string file, string baseDir)
    {
        var item = new BatchItem
        {
            Path = file,
            Rel = baseDir != file && (Directory.Exists(baseDir) || File.Exists(baseDir))
                ? Path.GetRelativePath(Path.GetFullPath(baseDir), Path.GetFullPath(file))
                : Path.GetFileName(file),
        };
        byte[] data;
        try { data = File.ReadAllBytes(file); }
        catch (Exception e) { item.Error = e.Message; return item; }

        // JSON：校验格式与 languages 对齐（不测试二进制 round-trip，改为生成模型）
        if (Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            string text;
            try { text = File.ReadAllText(file); }
            catch (Exception e) { item.Error = e.Message; return item; }
            var report = ModelChecks.CheckJson(text, out string jsonKind, out object? jsonModel);
            item.Kind = report.Kind switch
            {
                "cnv" => "CNV-JSON",
                "qst" => "QST2-JSON",
                _ => "JSON",
            };
            item.ParseOk = report.Kind.Length > 0;
            item.Tiled = true;
            item.LocOk = true;
            item.RoundTripOk = report.Kind.Length > 0;
            item.StepsOrTasks = report.NodeCount;
            item.TextSlots = report.TextSlots;
            item.TxtLines = report.TextSlots;
            item.LangOk = report.Ok;
            item.LangDetail = report.Ok ? null : report.ToText();
            if (jsonModel != null)
            {
                item.SuggestedSlots = SlotGuide.SuggestedCount(jsonModel);
                item.SlotDetail = SlotGuide.Report(jsonModel);
            }
            return item;
        }

        object model;
        try { model = Load(file, data); }
        catch (Exception e) { item.Error = e.Message; return item; }

        byte[] rewritten;
        switch (model)
        {
            case CnvFile c:
                item.Kind = "CNV";
                item.Version = c.Version;
                item.StepsOrTasks = c.Steps.Count;
                item.ParseOk = c.Valid;
                item.Tiled = c.Tiled;
                item.LocOk = c.LocOffsetMatches;
                if (!c.Valid) item.Error = c.Error;
                rewritten = c.Write();
                break;
            case QstFile q:
                item.Kind = "QST2";
                item.Version = q.Version;
                item.StepsOrTasks = q.Tasks.Count;
                item.ParseOk = q.Valid;
                item.Tiled = q.Tiled;
                item.LocOk = q.LocOffsetMatches;
                if (!q.Valid) item.Error = q.Error;
                item.TextDrift = q.Valid ? q.TextCountDrift : 0;
                rewritten = q.Write();
                break;
            default:
                item.Error = CliL.T("未知模型");
                return item;
        }

        if (item.ParseOk)
        {
            var report = model switch
            {
                CnvFile c => ModelChecks.Check(c),
                QstFile q => ModelChecks.Check(q),
                _ => null,
            };
            if (report != null)
            {
                item.TextSlots = report.TextSlots;
                item.LangOk = report.Ok;
                item.LangDetail = report.Ok ? null : report.ToText();
            }
        }

        item.RoundTripOk = rewritten.AsSpan().SequenceEqual(data);
        try
        {
            item.TxtLines = model switch
            {
                CnvFile c => c.PrimaryLanguage.Texts.Count,
                QstFile q => q.PrimaryLanguage.Texts.Count,
                _ => 0,
            };
        }
        catch { /* 无语言块等；已由 ParseOk 反映 */ }
        item.SuggestedSlots = SlotGuide.SuggestedCount(model);
        item.SlotDetail = SlotGuide.Report(model);
        return item;
    }

    public static object Load(string file, byte[] data) =>
        Path.GetExtension(file).Equals(".cnv", StringComparison.OrdinalIgnoreCase)
            ? CnvFile.Parse(data)
            : Path.GetExtension(file).Equals(".qst", StringComparison.OrdinalIgnoreCase)
                ? QstFile.Parse(data)
                : throw new GdParseException(CliL.F("无法按扩展名识别格式：{0}（仅支持 .cnv/.qst）", file));

    /// <summary>扫描目录（或单文件）并逐个检查。</summary>
    public static List<BatchItem> Scan(string path)
    {
        var items = new List<BatchItem>();
        foreach (string f in EnumerateFiles(path))
        {
            try { items.Add(Inspect(f, path)); }
            catch (Exception e)
            {
                items.Add(new BatchItem { Path = f, Rel = Path.GetFileName(f), Error = e.Message });
            }
        }
        return items;
    }

    /// <summary>导出官方本地化 TXT。返回 (源文件, 目标路径, 错误) 列表。</summary>
    public static List<(string File, string? Target, string? Error)> ExportTxt(
        string path, string? outDir, bool flat)
    {
        var results = new List<(string, string?, string?)>();
        foreach (string file in EnumerateFiles(path))
        {
            try
            {
                byte[] data = File.ReadAllBytes(file);
                var model = Load(file, data);
                string txt = model switch
                {
                    CnvFile c => TextExport.CnvTxt(c),
                    QstFile q => TextExport.QstTxt(q),
                    _ => throw new GdParseException(CliL.T("未知模型")),
                };
                string target = TargetPath(file, path, outDir, flat, ".txt");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, txt, new UTF8Encoding(false));
                results.Add((file, target, null));
            }
            catch (Exception e)
            {
                results.Add((file, null, e.Message));
            }
        }
        return results;
    }

    /// <summary>导出全保真 JSON。返回 (源文件, 目标路径, 错误) 列表。</summary>
    public static List<(string File, string? Target, string? Error)> ExportJson(
        string path, string outDir, bool flat)
    {
        var results = new List<(string, string?, string?)>();
        foreach (string file in EnumerateFiles(path))
        {
            try
            {
                byte[] data = File.ReadAllBytes(file);
                var model = Load(file, data);
                string name = Path.GetFileName(file);
                string json = model switch
                {
                    CnvFile c => JsonIO.Export(c, name),
                    QstFile q => JsonIO.Export(q, name),
                    _ => throw new GdParseException(CliL.T("未知模型")),
                };
                string target = TargetPath(file, path, outDir, flat, ".json");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, json, new UTF8Encoding(false));
                results.Add((file, target, null));
            }
            catch (Exception e)
            {
                results.Add((file, null, e.Message));
            }
        }
        return results;
    }

    public static string TargetPath(string file, string baseDir, string? outDir, bool flat, string newExt)
    {
        string name = Path.GetFileNameWithoutExtension(file) + newExt;
        if (outDir == null) return Path.Combine(Path.GetDirectoryName(file)!, name);
        if (flat || File.Exists(baseDir)) return Path.Combine(outDir, name);
        string rel = Path.GetRelativePath(Path.GetFullPath(baseDir), Path.GetFullPath(file));
        return Path.Combine(outDir, Path.GetDirectoryName(rel) ?? "", name);
    }
}
