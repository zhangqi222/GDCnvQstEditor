using System.Text;
using System.Text.RegularExpressions;

namespace CnvQst;

/// <summary>
/// 官方本地化 TXT 导出/导入（text_zh.arc 流程）。
///
/// 格式 = GrimDawnCnvQstToTXT v0.9 的输出（UTF-8 无 BOM、CRLF、一行 = 本地化字符串表一条、
/// 空槽位 → 空行、行中保留 {^n}、剥行尾 {^n}、文件末尾再补一个 CRLF），但改为按已解码的
/// 本地化块精准提取：不再"首个 enUS+12 截断 + 逐 null 强插换行"，因此修复了旧工具的
/// 吃字符、按 char 切割错位等缺陷。行数与位置 = 本地化字符串表，游戏按行号回填，不可增删。
/// </summary>
public static partial class TextExport
{
    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]")]
    private static partial Regex RxControl();

    [GeneratedRegex(@"[\uD800-\uDFFF]")]
    private static partial Regex RxSurrogate();

    [GeneratedRegex(@" {2,}")]
    private static partial Regex RxMultiSpace();

    /// <summary>单条本地化字符串 → 一行文本（与 v0.9 清洗规则一致，但以字符串为界、不越界）。</summary>
    public static string CleanLine(string text)
    {
        string t = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "{^n}");
        t = RxControl().Replace(t, "");
        t = RxSurrogate().Replace(t, "");
        t = RxMultiSpace().Replace(t, " ");
        t = t.Replace("\uFFFD", "");
        t = t.Trim();
        while (t.EndsWith("{^n}")) t = t[..^4].TrimEnd();
        while (t.EndsWith("{^n")) t = t[..^3].TrimEnd();
        return t;
    }

    /// <summary>本地化字符串表 → txt 内容（每行一条 + 末尾 CRLF）。</summary>
    public static string BuildTxt(IEnumerable<GdString> texts)
    {
        var lines = texts.Select(t => CleanLine(t.Text));
        return string.Join("\r\n", lines) + "\r\n";
    }

    public static string CnvTxt(CnvFile c) => BuildTxt(c.PrimaryLanguage.Texts);
    public static string QstTxt(QstFile q) => BuildTxt(q.PrimaryLanguage.Texts);

    /// <summary>txt → 行列表。按 CRLF/LF 切分，保留空行（空槽位），不产生末尾伪行。</summary>
    public static List<string> SplitTxt(string content)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] != '\n') continue;
            int end = i > start && content[i - 1] == '\r' ? i - 1 : i;
            lines.Add(content[start..end]);
            start = i + 1;
        }
        if (start < content.Length) lines.Add(content[start..]);
        return lines;
    }

    /// <summary>把翻译行回填进语言块（行数必须与本地化字符串表严格一致）。</summary>
    public static void ApplyTxt(LanguageBlock lang, IReadOnlyList<string> lines)
    {
        if (lines.Count != lang.Texts.Count)
            throw new GdParseException(
                $"行数不匹配：txt 有 {lines.Count} 行，本地化表有 {lang.Texts.Count} 条。" +
                "游戏按行号位置回填文本，空行（空槽位）必须原样保留，不能增删。");
        for (int i = 0; i < lines.Count; i++) lang.Texts[i].SetText(lines[i]);
    }

    // ------------------------------------------------------------------
    // GrimDawnCnvQstToTXT v0.9 的算法移植（Form1.cs:600-701）。
    // 仅用于"新旧输出逐字节比对"的基准生成（legacytxt 命令），含其固有缺陷，
    // 不用于正式导出。
    // ------------------------------------------------------------------
    public static string LegacyTxtV09(byte[] data)
    {
        string content = Encoding.UTF8.GetString(data);
        int idx = content.IndexOf("enUS", StringComparison.Ordinal);
        if (idx >= 0) content = content[(idx + 12)..];

        byte[] processed = Encoding.UTF8.GetBytes(content);
        processed = ReplaceBytes(processed, [(byte)0x0A], "{^n}"u8.ToArray());

        int nullPos = Array.LastIndexOf(processed, (byte)0);
        while (nullPos > 3)
        {
            processed[nullPos - 3] = (byte)'\r';
            for (int i = nullPos - 2; i <= nullPos; i++) processed[i] = 0x20;
            nullPos = Array.LastIndexOf(processed, (byte)0);
        }

        string str = Encoding.UTF8.GetString(processed);
        str = RxControl().Replace(str, "");
        str = RxSurrogate().Replace(str, "");
        str = RxMultiSpace().Replace(str, " ");
        str = str.Replace("\uFFFD", "");
        str = str.Replace("\r\n", "\n").Replace("\r", "\n");

        var cleaned = new List<string>();
        foreach (string raw in str.Split('\n'))
        {
            string line = raw.Trim();
            while (line.EndsWith("{^n}")) line = line[..^4].TrimEnd();
            while (line.EndsWith("{^n")) line = line[..^3].TrimEnd();
            cleaned.Add(line);
        }
        return string.Join("\r\n", cleaned) + "\r\n";
    }

    private static byte[] ReplaceBytes(byte[] input, byte[] from, byte[] to)
    {
        var ms = new MemoryStream();
        for (int i = 0; i < input.Length; i++)
        {
            if (i + from.Length <= input.Length && input.AsSpan(i, from.Length).SequenceEqual(from))
            {
                ms.Write(to);
                i += from.Length - 1;
            }
            else ms.WriteByte(input[i]);
        }
        return ms.ToArray();
    }
}
