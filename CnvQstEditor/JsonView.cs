using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace CnvQstEditor;

/// <summary>
/// JSON 视图的折叠支持：一次线性扫描产出折叠区间（仅跨行的对象/数组折叠；
/// 字符串字面量内的引号/括号被正确跳过，兼容 JSONC 行/块注释）。
/// 返回前按 StartOffset 升序排序——FoldingManager.UpdateFoldings 要求折叠区间有序，
/// 否则抛 "newFoldings must be sorted by start offset"（括号栈后进先出的天然顺序不保证有序）。
/// 语法着色使用 AvaloniaEdit 内置 JSON 高亮定义（Json.xshd），配色由 UiTheme.Refresh 按主题同步。
/// </summary>
internal static class JsonScanner
{
    /// <summary>扫描文本：返回折叠区间（按 StartOffset 升序）。</summary>
    public static List<NewFolding> Analyze(string text)
    {
        var foldings = new List<NewFolding>();
        var brackets = new Stack<int>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            switch (c)
            {
                case '"':   // 跳过字符串（key 或 value），字符串内的括号不算结构
                {
                    i++;
                    while (i < text.Length && text[i] != '"')
                        i += text[i] == '\\' && i + 1 < text.Length ? 2 : 1;
                    i = Math.Min(i + 1, text.Length);
                    break;
                }
                case '/' when i + 1 < text.Length && text[i + 1] == '/':   // 行注释
                {
                    while (i < text.Length && text[i] != '\n') i++;
                    break;
                }
                case '/' when i + 1 < text.Length && text[i + 1] == '*':   // 块注释
                {
                    i += 2;
                    while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                    i = Math.Min(i + 2, text.Length);
                    break;
                }
                case '{' or '[':
                    brackets.Push(i);
                    i++;
                    break;
                case '}' or ']':
                {
                    i++;
                    if (brackets.Count > 0)
                    {
                        int start = brackets.Pop();
                        // 只折叠跨行的对象/数组（单行 {} 无意义）；折叠区间含闭括号
                        if (text.AsSpan(start, i - start).IndexOf('\n') >= 0)
                            foldings.Add(new NewFolding(start, i));
                    }
                    break;
                }
                default:
                    i++;
                    break;
            }
        }
        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }
}
