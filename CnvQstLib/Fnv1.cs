using System.Text;

namespace CnvQst;

/// <summary>
/// FNV1-32 哈希（官方编辑器的 `Utility.FNV1::Hash(string, bool)` 逐字节等价实现）。
///
/// 实测用途（2026-09-28 反汇编 ConversationEditor 得到）：
///   · CNV 头部 +0x0C 的"对话 id" = `Hash(根节点文本, unicode: true)`——即 NPC 名的哈希；
///     编辑器"Copy Id"菜单复制的正是这个值。三个样本已逐值核对通过。
///   · 任务的引用标识（条件 QuestTaskComplete / 动作 BeginQuestTask 等参数里那些
///     编辑器显示为 "?" 的大数）= 同一个哈希再左移 8 位（mod 2^32）。
/// </summary>
public static class Fnv1
{
    private const uint Offset = 0x811C9DC5;   // -2128831035
    private const uint Prime = 16777619;      // 0x01000193

    /// <summary>unicode=true 用 UTF-16LE 字节（编辑器默认），false 用 ASCII 字节。</summary>
    public static uint Hash(string text, bool unicode = true)
    {
        uint h = Offset;
        foreach (byte b in unicode ? Encoding.Unicode.GetBytes(text) : Encoding.ASCII.GetBytes(text))
            h = (h ^ b) * Prime;
        return h;
    }

    /// <summary>任务引用标识形态：哈希左移 8 位（低 8 位恒为 0，与语料观测一致）。</summary>
    public static uint Reference(string text, bool unicode = true) => Hash(text, unicode) << 8;
}
