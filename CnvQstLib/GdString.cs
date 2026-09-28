using System.Text;

namespace CnvQst;

/// <summary>
/// 长度前缀字符串的原始字节载体。写入时回放原始字节以保证 round-trip 逐字节一致；
/// 显示与编辑走 UTF-8（本体串均为 ASCII 路径/token/文本，翻译回填时以 UTF-8 重编码）。
/// </summary>
public sealed class GdString
{
    private byte[] _raw;

    public GdString(byte[] raw) => _raw = raw;
    public GdString(string text) => _raw = Encoding.UTF8.GetBytes(text);

    public byte[] Raw => _raw;
    public string Text => Encoding.UTF8.GetString(_raw);
    public int ByteLength => _raw.Length;

    /// <summary>替换内容（翻译回填）；以 UTF-8 重新编码。</summary>
    public void SetText(string text) => _raw = Encoding.UTF8.GetBytes(text);

    public bool IsEmpty => _raw.Length == 0;

    public override string ToString() => Text;
}
