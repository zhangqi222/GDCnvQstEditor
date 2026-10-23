using System.Text;
using System.Text.Json;
using CnvQst.Cli;

namespace CnvQst;

/// <summary>一条校验结果：错误会阻止 JSON→二进制与保存，提示只做说明。</summary>
public sealed record CheckIssue(bool IsError, string Message);

/// <summary>模型/JSON 校验结果：节点数、应有文本行数与逐条问题。</summary>
public sealed class CheckReport
{
    public string Kind = "";            // cnv | qst
    public int NodeCount;               // CNV 节点数 / QST task 数
    public int TextSlots;               // 按节点结构应有的本地化文本行数
    public List<string> Languages = new();
    public List<CheckIssue> Issues = new();

    public bool HasErrors => Issues.Any(i => i.IsError);
    public bool Ok => !HasErrors;

    public void Error(string message) => Issues.Add(new CheckIssue(true, message));
    public void Warn(string message) => Issues.Add(new CheckIssue(false, message));

    public string ToText() =>
        string.Join("\n", Issues.Select(i => (i.IsError ? CliL.T("错误：") : CliL.T("提示：")) + i.Message));
}

/// <summary>
/// 节点与 languages 的对齐校验（JSON 与二进制共用）：
///   · CNV：文本行数必须等于节点数（下标即槽位；link 节点行可留空）。
///   · QST：文本行数必须等于 2 + 2×task + objective + incentive + event（官方索引公式）。
///   · CNV 结构：Steps 必须是深度前序，Children/Parent/Depth 必须自洽，否则写盘后槽位会错位。
/// </summary>
public static class ModelChecks
{
    /// <summary>官方编辑器/加载器认可的 6 个语言标签；其余标签只做提示（游戏可能忽略）。</summary>
    public static readonly string[] OfficialTags = ["enUS", "enGB", "es", "fr", "de", "it"];

    public static CheckReport Check(CnvFile c)
    {
        var r = new CheckReport { Kind = "cnv", NodeCount = c.Steps.Count, TextSlots = c.Steps.Count };
        if (c.Steps.Count == 0)
        {
            r.Error(CliL.T("节点数为 0：CNV 至少需要 1 个根节点。"));
            return r;
        }
        CheckCnvStructure(c, r);
        CheckLanguages(c.Languages, r, lang => CnvMismatchMessage(c, lang));
        return r;
    }

    public static CheckReport Check(QstFile q)
    {
        var r = new CheckReport { Kind = "qst", NodeCount = q.Tasks.Count, TextSlots = q.RequiredTextCount };
        if (q.Tasks.Count == 0) r.Warn(CliL.T("没有 task：任务文件可以保存，但游戏内没有可执行内容。"));
        CheckLanguages(q.Languages, r, lang => QstMismatchMessage(q, lang));
        return r;
    }

    /// <summary>
    /// 解析并校验 JSON：自动区分全保真模型（kind）与手写 spec（format）。
    /// model 为 null 表示无法生成模型（已记录错误）。
    /// </summary>
    public static CheckReport CheckJson(string json, out string kind, out object? model)
    {
        var r = new CheckReport();
        kind = "";
        model = null;
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException e)
        {
            r.Error(CliL.F("JSON 解析失败：第 {0} 行第 {1} 列：{2}", e.LineNumber + 1, e.BytePositionInLine + 1, e.Message));
            return r;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                r.Error(CliL.T("JSON 根节点必须是对象。"));
                return r;
            }
            bool modelShape = TryProp(root, "kind", out var kindEl);
            bool specShape = TryProp(root, "format", out var fmtEl);
            if (!modelShape && !specShape)
            {
                r.Error(CliL.T("无法识别 JSON 类型：全保真模型需要 \"kind\"（cnv/qst），手写 spec 需要 \"format\"（cnv/qst）。"));
                return r;
            }

            string declared = (modelShape ? kindEl.GetString() : fmtEl.GetString())?.ToLowerInvariant() ?? "";
            if (declared is not ("cnv" or "qst"))
            {
                r.Error(CliL.F("{0} 必须是 cnv 或 qst，当前为“{1}”。", modelShape ? "kind" : "format", declared));
                return r;
            }
            r.Kind = declared;
            kind = declared;

            try
            {
                if (modelShape)
                {
                    model = JsonIO.ImportModel(json);
                }
                else
                {
                    byte[] bytes = Spec.Build(json, out string builtKind);
                    model = builtKind switch
                    {
                        "cnv" => CnvFile.Parse(bytes),
                        "qst" => QstFile.Parse(bytes),
                        _ => null,
                    };
                    if (model is CnvFile bc && !bc.Valid) throw new GdParseException(bc.Error ?? CliL.T("CNV 生成结果无法重新解析"));
                    if (model is QstFile bq && !bq.Valid) throw new GdParseException(bq.Error ?? CliL.T("QST 生成结果无法重新解析"));
                }
            }
            catch (Exception e) when (e is GdParseException or SpecException or JsonException or FormatException or OverflowException)
            {
                r.Error(e.Message);
                return r;
            }

            var sub = model switch
            {
                CnvFile c => Check(c),
                QstFile q => Check(q),
                _ => null,
            };
            if (sub == null)
            {
                r.Error(CliL.T("JSON 生成的模型类型未知。"));
                return r;
            }
            kind = sub.Kind;
            r.Kind = sub.Kind;
            r.NodeCount = sub.NodeCount;
            r.TextSlots = sub.TextSlots;
            r.Languages.AddRange(sub.Languages);
            r.Issues.AddRange(sub.Issues);
        }
        return r;
    }

    /// <summary>CLI/GUI 摘要：CNV 16 节点 · 文本 16 行 · enUS 16 行；QST 4 task · 文本 24 行。</summary>
    public static string Summary(CheckReport r)
    {
        string baseText = r.Kind switch
        {
            "cnv" => CliL.F("CNV {0} 节点 · 文本槽 {1} 行", r.NodeCount, r.TextSlots),
            "qst" => CliL.F("QST {0} task · 文本槽 {1} 行", r.NodeCount, r.TextSlots),
            _ => "",
        };
        return r.Languages.Count > 0 ? baseText + " · " + string.Join(CliL.T("，"), r.Languages) : baseText;
    }

    // ------------------------------------------------------------------
    // languages 行数与节点对齐
    // ------------------------------------------------------------------

    private static void CheckLanguages(List<LanguageBlock> languages, CheckReport r, Func<LanguageBlock, string> mismatch)
    {
        if (languages.Count == 0)
        {
            r.Error(CliL.T("缺少 languages：至少需要 1 个语言块（通常 tag=enUS）。"));
            return;
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lang in languages)
        {
            string tag = lang.Tag.Text;
            if (string.IsNullOrWhiteSpace(tag)) r.Error(CliL.T("languages 中存在空 tag 的语言块。"));
            else
            {
                if (!seen.Add(tag)) r.Error(CliL.F("languages 中语言标签“{0}”重复。", tag));
                if (!OfficialTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    r.Warn(CliL.F("语言标签“{0}”不在官方支持的 6 种内（{1}），游戏与官方编辑器可能忽略该语言块。",
                        tag, string.Join("/", OfficialTags)));
            }
            if (lang.Texts.Count != r.TextSlots) r.Error(mismatch(lang));
            r.Languages.Add(CliL.F("{0}:{1} 行", tag, lang.Texts.Count));
        }
    }

    public static string CnvMismatchMessage(CnvFile c, LanguageBlock lang)
    {
        int need = c.Steps.Count, have = lang.Texts.Count;
        var sb = new StringBuilder();
        sb.Append(CliL.F("节点 {0} 个，languages[{1}] 有 {2} 行，{3} {4} 行；文本行数必须与节点数一致。",
            need, lang.Tag.Text, have, have < need ? CliL.T("缺") : CliL.T("多"), Math.Abs(have - need)));
        var fillable = Enumerable.Range(0, need)
            .Where(i => c.Steps[i].Type != Names.StepLink)
            .Select(i => i + 1).ToList();
        var links = Enumerable.Range(0, need)
            .Where(i => c.Steps[i].Type == Names.StepLink)
            .Select(i => i + 1).ToList();
        sb.Append(CliL.F("\n可填文字的节点行（1 起）：{0}。", Ranges(fillable)));
        if (links.Count > 0)
            sb.Append(CliL.F("link 节点行（{0}）可留空，显示时复用目标节点文本。", Ranges(links)));
        if (have < need)
        {
            var missed = Enumerable.Range(have, need - have)
                .Where(i => c.Steps[i].Type != Names.StepLink)
                .Select(i => i + 1).ToList();
            if (missed.Count > 0) sb.Append(CliL.F("\n按行数推断，缺少的末尾行中可填文字：{0}（若中间缺行则无法仅凭行数定位）。", Ranges(missed)));
        }
        return sb.ToString();
    }

    public static string QstMismatchMessage(QstFile q, LanguageBlock lang)
    {
        int need = q.RequiredTextCount, have = lang.Texts.Count;
        var sb = new StringBuilder();
        sb.Append(CliL.F("任务 {0} 个（标题/描述/事件/目标合计），文本槽应为 {1} 行，languages[{2}] 有 {3} 行，{4} {5} 行。",
            q.Tasks.Count, need, lang.Tag.Text, have, have < need ? CliL.T("缺") : CliL.T("多"), Math.Abs(have - need)));
        if (have < need)
        {
            var roles = Enumerable.Range(have, Math.Min(need - have, 10))
                .Select(i => $"{i + 1}={q.DescribeSlot(i)}").ToList();
            sb.Append(CliL.F("\n按前缀对齐推断缺少的槽位（仅供参考；若中间缺行则无法仅凭行数定位）：{0}",
                string.Join(CliL.T("，"), roles)));
            if (need - have > roles.Count) sb.Append(CliL.F(" …（共 {0} 行）", need - have));
            sb.Append(CliL.T("。"));
        }
        else if (have > need)
        {
            sb.Append(CliL.F("\n多出的行为第 {0}-{1} 行（超出官方索引公式，官方编辑器不会读取）。", need + 1, have));
        }
        sb.Append(CliL.F("\n槽位顺序：{0}", QstSlotPreview(q)));
        return sb.ToString();
    }

    private static string QstSlotPreview(QstFile q)
    {
        int n = q.RequiredTextCount;
        int take = Math.Min(n, 12);
        var parts = Enumerable.Range(0, take).Select(i => $"{i + 1}={q.DescribeSlot(i)}").ToList();
        return string.Join(CliL.T("，"), parts) + (n > take ? CliL.F(" …（共 {0} 行）", n) : "");
    }

    private static string Ranges(IEnumerable<int> values)
    {
        var xs = values.Distinct().OrderBy(x => x).ToList();
        if (xs.Count == 0) return CliL.T("（无）");
        var parts = new List<string>();
        int start = xs[0], prev = xs[0];
        foreach (int x in xs.Skip(1))
        {
            if (x == prev + 1) { prev = x; continue; }
            parts.Add(start == prev ? $"{start}" : $"{start}-{prev}");
            start = prev = x;
        }
        parts.Add(start == prev ? $"{start}" : $"{start}-{prev}");
        return string.Join(", ", parts);
    }

    // ------------------------------------------------------------------
    // CNV 结构自洽（JSON 手改后最容易出错的地方）
    // ------------------------------------------------------------------

    private static void CheckCnvStructure(CnvFile c, CheckReport r)
    {
        int n = c.Steps.Count;
        var pos = new Dictionary<CnvStep, int>();
        for (int i = 0; i < n; i++) pos[c.Steps[i]] = i;
        if (!pos.TryGetValue(c.Steps[0], out _)) return;
        if (c.Steps[0].Type != Names.StepRoot) r.Warn(CliL.F("Steps[0] 的类型是 {0}，通常是 root。", c.Steps[0].Kind));

        var seen = new bool[n];
        var order = new List<int>();
        void Walk(CnvStep s, int depth, int parent)
        {
            if (!pos.TryGetValue(s, out int idx))
            {
                r.Error(CliL.F("节点“{0}”不在 Steps 列表中，Children 引用了不存在的对象。", s.Kind));
                return;
            }
            if (seen[idx])
            {
                r.Error(CliL.F("节点 #{0} 被 Children 重复引用（成环或多父），无法写出合法 CNV。", idx));
                return;
            }
            seen[idx] = true;
            if (idx != order.Count)
                r.Error(CliL.F("Steps 顺序必须是深度前序：第 {0} 个访问到的是节点 #{1}，语言行会因此错位。", order.Count + 1, idx));
            if (s.Depth != depth) r.Warn(CliL.F("节点 #{0} 的 depth={1}，按树应为 {2}。", idx, s.Depth, depth));
            if (s.Parent != parent) r.Warn(CliL.F("节点 #{0} 的 parent={1}，按树应为 {2}。", idx, s.Parent, parent));
            if (s.Index != idx) r.Warn(CliL.F("节点 #{0} 的 index={1}，与列表位置不一致（写盘后会自动重排）。", idx, s.Index));
            if (s.HasLink && s.LinkId >= n)
                r.Error(CliL.F("节点 #{0} 的 linkId={1} 越界（有效 0-{2}）。", idx, s.LinkId, n - 1));
            order.Add(idx);
            foreach (var ch in s.Children) Walk(ch, depth + 1, idx);
        }
        Walk(c.Steps[0], 0, -1);

        if (order.Count != n)
        {
            var missing = Enumerable.Range(0, n).Where(i => !seen[i]).Select(i => i + 1).ToList();
            r.Error(CliL.F("有 {0} 个节点未挂在根节点的 Children 树中（行：{1}），写盘时会被丢弃。", missing.Count, Ranges(missing)));
        }
    }

    private static bool TryProp(JsonElement obj, string name, out JsonElement value)
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
}
