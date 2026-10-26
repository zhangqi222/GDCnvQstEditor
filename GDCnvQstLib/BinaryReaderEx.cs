using System.Text;

namespace GDCnvQstLib;

/// <summary>解析失败（流错位、越界、非法值）。解析器在第一个不可能值处立即停止。</summary>
public class GdParseException : Exception
{
    public GdParseException(string message) : base(message) { }
}

/// <summary>
/// 紧凑、无对齐的小端读取器，语义对齐 Game.dll 的 BinaryReader：
/// 每次读取 = *(T*)cursor 后 cursor += sizeof(T)。
/// 字符串超长（&gt;1MB）按引擎同款规则判为流错位。
/// </summary>
public sealed class BinaryReaderEx
{
    private const uint MaxString = 0x100000;

    private readonly byte[] _data;

    public BinaryReaderEx(byte[] data, int pos = 0)
    {
        _data = data;
        Pos = pos;
    }

    public int Pos { get; set; }
    public int Size => _data.Length;

    private void Need(int n)
    {
        if (Pos + n > _data.Length)
            throw new GdParseException($"ran past the end of the file at 0x{Pos:X}");
    }

    public uint U32()
    {
        Need(4);
        uint v = BitConverter.ToUInt32(_data, Pos);
        Pos += 4;
        return v;
    }

    public int I32() => unchecked((int)U32());

    public byte U8()
    {
        Need(1);
        return _data[Pos++];
    }

    public GdString Str()
    {
        int at = Pos;
        uint n = U32();
        if (n > MaxString)
            throw new GdParseException($"implausible string length {n} at 0x{at:X}");
        Need((int)n);
        var raw = new byte[n];
        Buffer.BlockCopy(_data, Pos, raw, 0, (int)n);
        Pos += (int)n;
        return new GdString(raw);
    }
}

/// <summary>与 BinaryReaderEx 对应的追加式写入器；支持头部占位字段的回填。</summary>
public sealed class BinaryWriterEx
{
    private readonly MemoryStream _ms = new();
    private readonly BinaryWriter _w;

    public BinaryWriterEx()
    {
        _w = new BinaryWriter(_ms, Encoding.UTF8);
    }

    public long Position => _ms.Position;

    public void U32(uint v) => _w.Write(v);
    public void I32(int v) => _w.Write(v);
    public void U8(byte v) => _w.Write(v);

    public void Str(GdString s)
    {
        _w.Write((uint)s.ByteLength);
        _w.Write(s.Raw);
    }

    public void Bytes(ReadOnlySpan<byte> b) => _w.Write(b);

    /// <summary>回填此前写下的占位 u32（如本地化块偏移），写完恢复到流末尾。</summary>
    public void PatchU32(long at, uint v)
    {
        long end = _ms.Position;
        _ms.Position = at;
        _w.Write(v);
        _ms.Position = end;
    }

    public byte[] ToArray()
    {
        _w.Flush();
        return _ms.ToArray();
    }
}
