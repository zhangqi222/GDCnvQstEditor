using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace EditorProbe;

/// <summary>
/// 只读官方编辑器程序集（PE 元数据）的探针：**不加载程序集、不执行任何代码**，
/// 只读类型表、字段签名与方法 IL。用途：直接拿到条件/动作的
/// 类型号 ↔ 类名 ↔ 字段布局，不必反复开官方编辑器试。
///
///   EditorProbe types  &lt;程序集&gt; [过滤词]
///   EditorProbe fields &lt;程序集&gt; &lt;类型全名&gt;
///   EditorProbe il     &lt;程序集&gt; &lt;类型全名&gt; &lt;方法名&gt;
///   EditorProbe find   &lt;程序集&gt; &lt;词&gt;
/// </summary>
public static class Program
{
    private static MetadataReader Md = null!;
    private static PEReader Pe = null!;

    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法：EditorProbe types|fields|il|find <程序集> [...]");
            return 2;
        }
        string path = args[1];
        if (!File.Exists(path)) { Console.WriteLine($"找不到 {path}"); return 2; }

        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) { Console.WriteLine("该文件没有托管元数据（非 .NET 程序集）"); return 1; }
        Pe = pe;
        Md = pe.GetMetadataReader();

        return args[0] switch
        {
            "types" => Types(args.Length > 2 ? args[2] : null),
            "fields" => Fields(args[2]),
            "il" => Il(args[2], args[3]),
            "find" => Find(args[2]),
            "methods" => Methods(args[2]),
            "xref" => Xref(args[2]),
            "res" => Res(),
            "ex" => Extract(args[2], args[3]),
            _ => Unknown(),
        };
    }

    private static int Unknown() { Console.WriteLine("未知子命令"); return 2; }

    private static int Types(string? filter)
    {
        int n = 0;
        foreach (var h in Md.TypeDefinitions)
        {
            var t = Md.GetTypeDefinition(h);
            string name = FullName(t);
            if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            n++;
            Console.WriteLine($"{name}{((t.Attributes & TypeAttributes.Abstract) != 0 ? "  [abstract]" : "")}");
        }
        Console.WriteLine($"—— 共 {n} 个类型");
        return 0;
    }

    private static int Fields(string typeName)
    {
        foreach (var h in Md.TypeDefinitions)
        {
            var t = Md.GetTypeDefinition(h);
            if (FullName(t) != typeName) continue;

            Console.WriteLine($"类型 {typeName}   基类 {TypeName(t.BaseType)}");
            int i = 0;
            foreach (var fh in t.GetFields())
            {
                var f = Md.GetFieldDefinition(fh);
                bool isStatic = (f.Attributes & FieldAttributes.Static) != 0;
                bool literal = (f.Attributes & FieldAttributes.Literal) != 0;
                string type = DecodeFieldSignature(f);
                string val = literal ? " = " + ConstantValue(f) : "";
                Console.WriteLine($"  [{i++,2}] {(isStatic ? "static " : "")}{type,-30} {Md.GetString(f.Name)}{val}");
            }
            return 0;
        }
        Console.WriteLine($"未找到类型 {typeName}");
        return 1;
    }

    private static int Il(string typeName, string methodName)
    {
        foreach (var h in Md.TypeDefinitions)
        {
            var t = Md.GetTypeDefinition(h);
            if (FullName(t) != typeName) continue;
            foreach (var mh in t.GetMethods())
            {
                var m = Md.GetMethodDefinition(mh);
                if (Md.GetString(m.Name) != methodName) continue;
                if (m.RelativeVirtualAddress == 0) { Console.WriteLine("（无方法体）"); return 1; }
                var body = Pe.GetMethodBody(m.RelativeVirtualAddress);
                Console.WriteLine($"── {typeName}::{methodName}   IL {body.GetILBytes().Length} 字节 ──");
                Disassemble(body.GetILBytes());
                return 0;
            }
            Console.WriteLine($"类型 {typeName} 里没有方法 {methodName}");
            return 1;
        }
        Console.WriteLine($"未找到类型 {typeName}");
        return 1;
    }

    private static int Methods(string typeName)
    {
        foreach (var h in Md.TypeDefinitions)
        {
            var t = Md.GetTypeDefinition(h);
            if (FullName(t) != typeName) continue;
            foreach (var mh in t.GetMethods())
            {
                var m = Md.GetMethodDefinition(mh);
                string blob = "";
                try
                {
                    var sig = m.DecodeSignature(new SigProvider(), null);
                    blob = "(" + string.Join(", ", sig.ParameterTypes) + ") → " + sig.ReturnType;
                }
                catch { }
                Console.WriteLine($"{Md.GetString(m.Name),-40} {blob}");
            }
            return 0;
        }
        Console.WriteLine($"未找到类型 {typeName}");
        return 1;
    }

    /// <summary>资源目录的字节（整个 CorHeader.ResourcesDirectory 区）。</summary>
    private static byte[] ResourceDirectory()
    {
        var dir = Pe.PEHeaders.CorHeader!.ResourcesDirectory;
        var whole = Pe.GetSectionData(dir.RelativeVirtualAddress).GetContent().ToArray();
        var bytes = new byte[dir.Size];
        Array.Copy(whole, bytes, Math.Min(bytes.Length, whole.Length));
        return bytes;
    }

    /// <summary>列出内嵌清单资源（Costura 把依赖程序集塞在这里）。</summary>
    private static int Res()
    {
        var bytes = ResourceDirectory();
        foreach (var h in Md.ManifestResources)
        {
            var r = Md.GetManifestResource(h);
            string name = Md.GetString(r.Name);
            string size = "?";
            if (r.Implementation.IsNil && r.Offset + 4 <= bytes.Length)
                size = BitConverter.ToUInt32(bytes, (int)r.Offset).ToString();
            Console.WriteLine($"{size,10}  offset={r.Offset,-8} {name}");
        }
        return 0;
    }

    /// <summary>提取内嵌资源为文件；名字以 .compressed 结尾时按 Deflate 解压（Costura 约定）。</summary>
    private static int Extract(string name, string outPath)
    {
        var bytes = ResourceDirectory();
        foreach (var h in Md.ManifestResources)
        {
            var r = Md.GetManifestResource(h);
            if (Md.GetString(r.Name) != name) continue;
            if (!r.Implementation.IsNil) { Console.WriteLine("该资源在别的程序集里"); return 1; }
            int size = (int)BitConverter.ToUInt32(bytes, (int)r.Offset);
            var data = new byte[size];
            Array.Copy(bytes, (int)r.Offset + 4, data, 0, size);
            if (name.EndsWith(".compressed", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var input = new MemoryStream(data);
                using var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
                using var output = File.Create(outPath);
                deflate.CopyTo(output);
            }
            else File.WriteAllBytes(outPath, data);
            Console.WriteLine($"已提取 {name} → {outPath}（{new FileInfo(outPath).Length} 字节）");
            return 0;
        }
        Console.WriteLine($"没找到资源 {name}");
        return 1;
    }

    /// <summary>全库交叉引用：扫所有方法体，找调用/引用名字里含 <paramref name="word"/> 的成员的方法。</summary>
    private static int Xref(string word)
    {
        foreach (var th in Md.TypeDefinitions)
        {
            var t = Md.GetTypeDefinition(th);
            foreach (var mh in t.GetMethods())
            {
                var m = Md.GetMethodDefinition(mh);
                if (m.RelativeVirtualAddress == 0) continue;
                var il = Pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes();
                var hits = new SortedSet<string>();
                foreach (int tok in TokensIn(il))
                {
                    string r = ResolveToken(tok);
                    if (r.Contains(word, StringComparison.OrdinalIgnoreCase)) hits.Add(r);
                }
                if (hits.Count > 0)
                    Console.WriteLine($"{FullName(t)}::{Md.GetString(m.Name)}\n    ← {string.Join("\n    ← ", hits)}");
            }
        }
        return 0;
    }

    /// <summary>粗略扫出方法体里的所有元数据 token（按操作数表跳过其余字节）。</summary>
    private static IEnumerable<int> TokensIn(byte[] il)
    {
        int i = 0;
        while (i < il.Length)
        {
            byte op = il[i++];
            string operand;
            if (op == 0xFE)
            {
                if (i >= il.Length) yield break;
                byte op2 = il[i++];
                if (!OpsFe.TryGetValue(op2, out var e2)) yield break;
                operand = e2.operand;
            }
            else
            {
                if (!Ops.TryGetValue(op, out var e1)) yield break;
                operand = e1.operand;
            }
            switch (operand)
            {
                case "": break;
                case "u8": case "i8": i += 1; break;
                case "u16": i += 2; break;
                case "i32": case "r4": i += 4; break;
                case "i64": case "r8": i += 8; break;
                case "token": case "str":
                    if (i + 4 > il.Length) yield break;
                    yield return BitConverter.ToInt32(il, i);
                    i += 4;
                    break;
                case "switch":
                {
                    if (i + 4 > il.Length) yield break;
                    int n = BitConverter.ToInt32(il, i); i += 4 + 4 * n;
                    break;
                }
            }
        }
    }

    private static int Find(string word)
    {
        foreach (var h in Md.TypeDefinitions)
        {
            var t = Md.GetTypeDefinition(h);
            string tn = FullName(t);
            foreach (var mh in t.GetMethods())
            {
                var m = Md.GetMethodDefinition(mh);
                if (Md.GetString(m.Name).Contains(word, StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine($"方法 {tn}::{Md.GetString(m.Name)}");
            }
            foreach (var fh in t.GetFields())
            {
                var f = Md.GetFieldDefinition(fh);
                if (Md.GetString(f.Name).Contains(word, StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine($"字段 {tn}::{Md.GetString(f.Name)}");
            }
        }
        return 0;
    }

    // ------------------------------------------------------------------
    // 名字与签名
    // ------------------------------------------------------------------

    private static string FullName(TypeDefinition t)
    {
        string name = Md.GetString(t.Name);
        if (!t.GetDeclaringType().IsNil) return FullName(Md.GetTypeDefinition(t.GetDeclaringType())) + "+" + name;
        string ns = Md.GetString(t.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string TypeName(EntityHandle h)
    {
        if (h.IsNil) return "—";
        return h.Kind switch
        {
            HandleKind.TypeDefinition => FullName(Md.GetTypeDefinition((TypeDefinitionHandle)h)),
            HandleKind.TypeReference => RefName((TypeReferenceHandle)h),
            _ => h.Kind.ToString(),
        };
    }

    /// <summary>供 SigProvider 用（它是 internal 类，需要公开入口）。</summary>
    public static string TypeNamePublic(EntityHandle h) => TypeName(h);

    private static string RefName(TypeReferenceHandle h)
    {
        var t = Md.GetTypeReference(h);
        string name = Md.GetString(t.Name);
        if (t.ResolutionScope.Kind == HandleKind.TypeReference)
            return RefName((TypeReferenceHandle)t.ResolutionScope) + "+" + name;
        string ns = Md.GetString(t.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string ConstantValue(FieldDefinition f)
    {
        try
        {
            var ch = f.GetDefaultValue();
            if (ch.IsNil) return "?";
            var c = Md.GetConstant(ch);
            var blob = Md.GetBlobBytes(c.Value);
            return c.TypeCode switch
            {
                ConstantTypeCode.SByte => ((sbyte)blob[0]).ToString(),
                ConstantTypeCode.Byte => blob[0].ToString(),
                ConstantTypeCode.Int16 => BitConverter.ToInt16(blob).ToString(),
                ConstantTypeCode.UInt16 => BitConverter.ToUInt16(blob).ToString(),
                ConstantTypeCode.Int32 => BitConverter.ToInt32(blob).ToString(),
                ConstantTypeCode.UInt32 => BitConverter.ToUInt32(blob).ToString(),
                ConstantTypeCode.Int64 => BitConverter.ToInt64(blob).ToString(),
                ConstantTypeCode.UInt64 => BitConverter.ToUInt64(blob).ToString(),
                ConstantTypeCode.String => "\"" + Encoding.Unicode.GetString(blob).TrimEnd('\0') + "\"",
                _ => c.TypeCode.ToString(),
            };
        }
        catch (Exception e) { return "<" + e.Message + ">"; }
    }

    private static string DecodeFieldSignature(FieldDefinition f)
    {
        try
        {
            var blob = Md.GetBlobBytes(f.Signature);
            int i = 1;                                   // 跳过 FIELD(0x06)
            return DecodeType(blob, ref i);
        }
        catch { return "?"; }
    }

    private static string DecodeType(byte[] b, ref int i)
    {
        byte e = b[i++];
        return e switch
        {
            0x01 => "void",
            0x02 => "bool",
            0x03 => "char",
            0x04 => "sbyte",
            0x05 => "byte",
            0x06 => "short",
            0x07 => "ushort",
            0x08 => "int",
            0x09 => "uint",
            0x0A => "long",
            0x0B => "ulong",
            0x0C => "float",
            0x0D => "double",
            0x0E => "string",
            0x11 or 0x12 => TypeName((EntityHandle)MetadataTokens.EntityHandle((int)ReadCompressed(b, ref i)))
                            + (e == 0x11 ? "（值类型）" : ""),
            0x1D => "array",
            0x15 => GenericInst(b, ref i),
            _ => $"elem0x{e:X2}",
        };
    }

    private static string GenericInst(byte[] b, ref int i)
    {
        string inner = TypeName((EntityHandle)MetadataTokens.EntityHandle((int)ReadCompressed(b, ref i)));
        int argc = (int)ReadCompressed(b, ref i);
        var sb = new StringBuilder(inner.Split('`')[0]).Append('<');
        for (int k = 0; k < argc; k++) sb.Append(k > 0 ? "," : "").Append(DecodeType(b, ref i));
        return sb.Append('>').ToString();
    }

    private static uint ReadCompressed(byte[] b, ref int i)
    {
        byte x = b[i++];
        if ((x & 0x80) == 0) return x;
        if ((x & 0xC0) == 0x80) return (uint)(((x & 0x3F) << 8) | b[i++]);
        uint r = (uint)(((x & 0x1F) << 24) | (b[i] << 16) | (b[i + 1] << 8) | b[i + 2]);
        i += 3;
        return r;
    }

    // ------------------------------------------------------------------
    // IL 反汇编（够读工厂 switch 的子集）
    // ------------------------------------------------------------------

    private static readonly Dictionary<byte, (string name, string operand)> Ops = new()
    {
        [0x00] = ("nop", ""), [0x01] = ("break", ""),
        [0x02] = ("ldarg.0", ""), [0x03] = ("ldarg.1", ""), [0x04] = ("ldarg.2", ""), [0x05] = ("ldarg.3", ""),
        [0x06] = ("ldloc.0", ""), [0x07] = ("ldloc.1", ""), [0x08] = ("ldloc.2", ""), [0x09] = ("ldloc.3", ""),
        [0x0A] = ("stloc.0", ""), [0x0B] = ("stloc.1", ""), [0x0C] = ("stloc.2", ""), [0x0D] = ("stloc.3", ""),
        [0x0E] = ("ldarg.s", "u8"), [0x0F] = ("ldarga.s", "u8"), [0x10] = ("starg.s", "u8"),
        [0x11] = ("ldloc.s", "u8"), [0x12] = ("ldloca.s", "u8"), [0x13] = ("stloc.s", "u8"),
        [0x14] = ("ldnull", ""), [0x15] = ("ldc.i4.m1", ""),
        [0x16] = ("ldc.i4.0", ""), [0x17] = ("ldc.i4.1", ""), [0x18] = ("ldc.i4.2", ""), [0x19] = ("ldc.i4.3", ""),
        [0x1A] = ("ldc.i4.4", ""), [0x1B] = ("ldc.i4.5", ""), [0x1C] = ("ldc.i4.6", ""), [0x1D] = ("ldc.i4.7", ""),
        [0x1E] = ("ldc.i4.8", ""), [0x1F] = ("ldc.i4.s", "i8"), [0x20] = ("ldc.i4", "i32"),
        [0x21] = ("ldc.i8", "i64"), [0x22] = ("ldc.r4", "r4"), [0x23] = ("ldc.r8", "r8"),
        [0x25] = ("dup", ""), [0x26] = ("pop", ""), [0x27] = ("jmp", "token"),
        [0x28] = ("call", "token"), [0x29] = ("calli", "token"), [0x2A] = ("ret", ""),
        [0x2B] = ("br.s", "i8"), [0x2C] = ("brfalse.s", "i8"), [0x2D] = ("brtrue.s", "i8"),
        [0x2E] = ("beq.s", "i8"), [0x2F] = ("bge.s", "i8"), [0x30] = ("bgt.s", "i8"), [0x31] = ("ble.s", "i8"),
        [0x32] = ("blt.s", "i8"), [0x33] = ("bne.un.s", "i8"), [0x34] = ("bge.un.s", "i8"),
        [0x35] = ("bgt.un.s", "i8"), [0x36] = ("ble.un.s", "i8"), [0x37] = ("blt.un.s", "i8"),
        [0x38] = ("br", "i32"), [0x39] = ("brfalse", "i32"), [0x3A] = ("brtrue", "i32"),
        [0x3B] = ("beq", "i32"), [0x3C] = ("bge", "i32"), [0x3D] = ("bgt", "i32"), [0x3E] = ("ble", "i32"),
        [0x3F] = ("blt", "i32"), [0x40] = ("bne.un", "i32"), [0x41] = ("bge.un", "i32"),
        [0x42] = ("bgt.un", "i32"), [0x43] = ("ble.un", "i32"), [0x44] = ("blt.un", "i32"),
        [0x45] = ("switch", "switch"),
        [0x46] = ("ldind.i1", ""), [0x47] = ("ldind.u1", ""), [0x48] = ("ldind.i2", ""), [0x49] = ("ldind.u2", ""),
        [0x4A] = ("ldind.i4", ""), [0x4B] = ("ldind.u4", ""), [0x4C] = ("ldind.i8", ""), [0x4D] = ("ldind.i", ""),
        [0x4E] = ("ldind.r4", ""), [0x4F] = ("ldind.r8", ""), [0x50] = ("ldind.ref", ""),
        [0x51] = ("stind.ref", ""), [0x52] = ("stind.i1", ""), [0x53] = ("stind.i2", ""), [0x54] = ("stind.i4", ""),
        [0x55] = ("stind.i8", ""), [0x56] = ("stind.r4", ""), [0x57] = ("stind.r8", ""),
        [0x58] = ("add", ""), [0x59] = ("sub", ""), [0x5A] = ("mul", ""), [0x5B] = ("div", ""),
        [0x5C] = ("div.un", ""), [0x5D] = ("rem", ""), [0x5E] = ("rem.un", ""),
        [0x5F] = ("and", ""), [0x60] = ("or", ""), [0x61] = ("xor", ""), [0x62] = ("shl", ""),
        [0x63] = ("shr", ""), [0x64] = ("shr.un", ""), [0x65] = ("neg", ""), [0x66] = ("not", ""),
        [0x67] = ("conv.i1", ""), [0x68] = ("conv.i2", ""), [0x69] = ("conv.i4", ""), [0x6A] = ("conv.i8", ""),
        [0x6B] = ("conv.r4", ""), [0x6C] = ("conv.r8", ""), [0x6D] = ("conv.u4", ""), [0x6E] = ("conv.u8", ""),
        [0x6F] = ("callvirt", "token"), [0x70] = ("cpobj", "token"), [0x71] = ("ldobj", "token"),
        [0x72] = ("ldstr", "str"), [0x73] = ("newobj", "token"), [0x74] = ("castclass", "token"),
        [0x75] = ("isinst", "token"), [0x76] = ("conv.r.un", ""),
        [0x79] = ("unbox", "token"), [0x7A] = ("throw", ""),
        [0x7B] = ("ldfld", "token"), [0x7C] = ("ldflda", "token"), [0x7D] = ("stfld", "token"),
        [0x7E] = ("ldsfld", "token"), [0x7F] = ("ldsflda", "token"), [0x80] = ("stsfld", "token"),
        [0x81] = ("stobj", "token"), [0x8C] = ("box", "token"), [0x8D] = ("newarr", "token"),
        [0x8E] = ("ldlen", ""), [0x8F] = ("ldelema", "token"),
        [0x90] = ("ldelem.i1", ""), [0x91] = ("ldelem.u1", ""), [0x92] = ("ldelem.i2", ""), [0x93] = ("ldelem.u2", ""),
        [0x94] = ("ldelem.i4", ""), [0x95] = ("ldelem.u4", ""), [0x96] = ("ldelem.i8", ""),
        [0x97] = ("ldelem.i", ""), [0x98] = ("ldelem.r4", ""), [0x99] = ("ldelem.r8", ""), [0x9A] = ("ldelem.ref", ""),
        [0x9B] = ("stelem.i", ""), [0x9C] = ("stelem.i1", ""), [0x9D] = ("stelem.i2", ""),
        [0x9E] = ("stelem.i4", ""), [0x9F] = ("stelem.i8", ""), [0xA0] = ("stelem.r4", ""),
        [0xA1] = ("stelem.r8", ""), [0xA2] = ("stelem.ref", ""), [0xA3] = ("ldelem", "token"),
        [0xA4] = ("stelem", "token"), [0xA5] = ("unbox.any", "token"),
        [0xD0] = ("ldtoken", "token"), [0xD1] = ("conv.u2", ""), [0xD2] = ("conv.u1", ""),
        [0xD3] = ("conv.i", ""), [0xDD] = ("leave.s", "i8"), [0xDE] = ("leave", "i32"),
        [0xE0] = ("conv.u", ""),
    };

    private static readonly Dictionary<byte, (string name, string operand)> OpsFe = new()
    {
        [0x01] = ("ceq", ""), [0x02] = ("cgt", ""), [0x03] = ("cgt.un", ""), [0x04] = ("clt", ""),
        [0x05] = ("clt.un", ""), [0x06] = ("ldftn", "token"), [0x07] = ("ldvirtftn", "token"),
        [0x09] = ("ldarg", "u16"), [0x0A] = ("ldarga", "u16"), [0x0B] = ("starg", "u16"),
        [0x0C] = ("ldloc", "u16"), [0x0D] = ("ldloca", "u16"), [0x0E] = ("stloc", "u16"),
        [0x15] = ("initobj", "token"), [0x16] = ("constrained.", "token"), [0x1C] = ("sizeof", "token"),
        [0x1D] = ("refanytype", ""),
    };

    private static void Disassemble(byte[] il)
    {
        int i = 0;
        while (i < il.Length)
        {
            int at = i;
            byte op = il[i++];
            string name;
            string operand;
            if (op == 0xFE)
            {
                byte op2 = il[i++];
                if (!OpsFe.TryGetValue(op2, out var e2)) { Console.WriteLine($"IL_{at:X4}: <未支持 0xFE 0x{op2:X2}>"); return; }
                (name, operand) = e2;
            }
            else
            {
                if (!Ops.TryGetValue(op, out var e1)) { Console.WriteLine($"IL_{at:X4}: <未支持 0x{op:X2}>"); return; }
                (name, operand) = e1;
            }

            string text = "";
            switch (operand)
            {
                case "": break;
                case "u8": text = il[i++].ToString(); break;
                case "i8": text = ((sbyte)il[i++]).ToString(); break;
                case "u16": text = BitConverter.ToUInt16(il, i).ToString(); i += 2; break;
                case "i32": text = BitConverter.ToInt32(il, i).ToString(); i += 4; break;
                case "i64": text = BitConverter.ToInt64(il, i).ToString(); i += 8; break;
                case "r4": text = BitConverter.ToSingle(il, i).ToString("G"); i += 4; break;
                case "r8": text = BitConverter.ToDouble(il, i).ToString("G"); i += 8; break;
                case "token":
                {
                    int tok = BitConverter.ToInt32(il, i); i += 4;
                    text = $"0x{tok:X8}  {ResolveToken(tok)}";
                    break;
                }
                case "str":
                {
                    int tok = BitConverter.ToInt32(il, i); i += 4;
                    text = "\"" + Md.GetUserString(MetadataTokens.UserStringHandle(tok & 0xFFFFFF)) + "\"";
                    break;
                }
                case "switch":
                {
                    int n = BitConverter.ToInt32(il, i); i += 4;
                    var sb = new StringBuilder($"{n} 个目标 → ");
                    for (int k = 0; k < n; k++)
                    {
                        int off = BitConverter.ToInt32(il, i); i += 4;
                        sb.Append($"case {k}: IL_{i + off:X4}   ");
                    }
                    text = sb.ToString();
                    break;
                }
            }
            Console.WriteLine($"IL_{at:X4}: {name,-12} {text}");
        }
    }

    private static string ResolveToken(int token)
    {
        EntityHandle handle;
        try { handle = MetadataTokens.EntityHandle(token); }
        catch { return ""; }
        try
        {
            switch (handle.Kind)
            {
                case HandleKind.MemberReference:
                {
                    var mr = Md.GetMemberReference((MemberReferenceHandle)handle);
                    return TypeName(mr.Parent) + "::" + Md.GetString(mr.Name);
                }
                case HandleKind.MethodDefinition:
                {
                    var m = Md.GetMethodDefinition((MethodDefinitionHandle)handle);
                    return FullName(Md.GetTypeDefinition(m.GetDeclaringType())) + "::" + Md.GetString(m.Name);
                }
                case HandleKind.FieldDefinition:
                {
                    var f = Md.GetFieldDefinition((FieldDefinitionHandle)handle);
                    return FullName(Md.GetTypeDefinition(f.GetDeclaringType())) + "::" + Md.GetString(f.Name);
                }
                case HandleKind.MethodSpecification:
                {
                    var ms = Md.GetMethodSpecification((MethodSpecificationHandle)handle);
                    return ResolveToken(MetadataTokens.GetToken(ms.Method)) + "<…>";
                }
                case HandleKind.TypeReference: return RefName((TypeReferenceHandle)handle);
                case HandleKind.TypeDefinition: return FullName(Md.GetTypeDefinition((TypeDefinitionHandle)handle));
                default: return handle.Kind.ToString();
            }
        }
        catch (Exception e) { return "<解析失败 " + e.Message + ">"; }
    }
}

/// <summary>方法签名的极简 provider（只求可读）。</summary>
internal sealed class SigProvider : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[*]";
    public string GetByReferenceType(string elementType) => "ref " + elementType;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments) =>
        genericType.Split('`')[0] + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString().ToLowerInvariant();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
        Program.TypeNamePublic(handle);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
        Program.TypeNamePublic(handle);
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => "spec";
}
