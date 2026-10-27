// Minimal static il2cpp v31 dumper: global-metadata.dat + GameAssembly.dll -> text dump.
// Read-only. Usage: il2dump <GameAssembly.dll> <global-metadata.dat> <out.txt>
using System.Text;

class PE
{
    public byte[] B; public ulong ImageBase;
    public List<(uint va, uint vsize, uint raw, uint rsize, string name)> Sec = new();
    public PE(string path)
    {
        B = File.ReadAllBytes(path);
        int pe = BitConverter.ToInt32(B, 0x3C);
        int nsec = BitConverter.ToUInt16(B, pe + 6);
        int optSize = BitConverter.ToUInt16(B, pe + 20);
        int opt = pe + 24;
        ImageBase = BitConverter.ToUInt64(B, opt + 24);
        int sec = opt + optSize;
        for (int i = 0; i < nsec; i++, sec += 40)
        {
            string name = Encoding.ASCII.GetString(B, sec, 8).TrimEnd('\0');
            Sec.Add((BitConverter.ToUInt32(B, sec + 12), BitConverter.ToUInt32(B, sec + 8),
                     BitConverter.ToUInt32(B, sec + 20), BitConverter.ToUInt32(B, sec + 16), name));
        }
    }
    public long Off(ulong va)
    {
        if (va < ImageBase) return -1;
        ulong rva = va - ImageBase;
        foreach (var s in Sec)
            if (rva >= s.va && rva < s.va + Math.Max(s.vsize, s.rsize))
            {
                ulong o = rva - s.va;
                if (o >= s.rsize) return -1;
                return s.raw + (long)o;
            }
        return -1;
    }
    public ulong U64(ulong va) { long o = Off(va); return o < 0 ? 0 : BitConverter.ToUInt64(B, (int)o); }
    public uint U32(ulong va) { long o = Off(va); return o < 0 ? 0 : BitConverter.ToUInt32(B, (int)o); }
    public string CStr(ulong va)
    {
        long o = Off(va); if (o < 0) return null;
        int e = (int)o; while (e < B.Length && B[e] != 0) e++;
        return Encoding.UTF8.GetString(B, (int)o, e - (int)o);
    }
}

class Program
{
    static byte[] M; static int[] H = new int[66];
    static string Str(int idx) { int o = H[4] + idx; int e = o; while (M[e] != 0) e++; return Encoding.UTF8.GetString(M, o, e - o); }
    static int I32(int o) => BitConverter.ToInt32(M, o);
    static ushort U16(int o) => BitConverter.ToUInt16(M, o);

    const int TD = 88, MD = 36, PD = 12, FD = 12, IMG = 40, GP = 16;
    static PE pe; static ulong[] types; static ulong[] fieldOffsetPtrs; static int typeDefCount;

    static string TDName(int ti)
    {
        int o = H[38] + ti * TD;
        string ns = Str(I32(o + 4)), n = Str(I32(o));
        int decl = I32(o + 12);
        if (decl >= 0)
        {
            // nested: declaringTypeIndex is a type index -> find its typedef
            string outer = TypeName(decl);
            return outer + "/" + n;
        }
        return ns.Length > 0 ? ns + "." + n : n;
    }

    static string TypeName(int typeIndex) => typeIndex < 0 || typeIndex >= types.Length ? "?" : TypePtrName(types[typeIndex], 0);

    static string TypePtrName(ulong p, int depth)
    {
        if (p == 0 || depth > 8) return "?";
        ulong data = pe.U64(p); uint bits = pe.U32(p + 8);
        int t = (int)((bits >> 16) & 0xFF); bool byref = ((bits >> 29) & 1) != 0;
        string s = t switch
        {
            0x01 => "void", 0x02 => "bool", 0x03 => "char", 0x04 => "sbyte", 0x05 => "byte",
            0x06 => "short", 0x07 => "ushort", 0x08 => "int", 0x09 => "uint", 0x0a => "long",
            0x0b => "ulong", 0x0c => "float", 0x0d => "double", 0x0e => "string", 0x1c => "object",
            0x18 => "IntPtr", 0x19 => "UIntPtr", 0x16 => "TypedReference",
            0x11 or 0x12 => ((int)data >= 0 && (int)data < typeDefCount) ? TDName((int)data) : "?td",
            0x0f => TypePtrName(data, depth + 1) + "*",
            0x1d => TypePtrName(data, depth + 1) + "[]",
            0x14 => TypePtrName(pe.U64(data), depth + 1) + "[,]",
            0x13 or 0x1e => GenParamName((int)data),
            0x15 => GenInstName(data, depth),
            _ => $"t{t:X}"
        };
        return byref ? s + "&" : s;
    }

    static string GenParamName(int idx)
    {
        if (idx < 0 || H[24] + (idx + 1) * GP > H[24] + H[25]) return "T";
        return Str(I32(H[24] + idx * GP + 4));
    }

    static string GenInstName(ulong gc, int depth)
    {
        ulong typePtr = pe.U64(gc);           // v27+: const Il2CppType* type
        ulong inst = pe.U64(gc + 8);          // context.class_inst
        string baseName = TypePtrName(typePtr, depth + 1);
        int tick = baseName.IndexOf('`'); if (tick > 0) baseName = baseName[..tick];
        var args = new List<string>();
        if (inst != 0)
        {
            ulong argc = pe.U64(inst), argv = pe.U64(inst + 8);
            for (ulong i = 0; i < argc && i < 8; i++) args.Add(TypePtrName(pe.U64(argv + i * 8), depth + 1));
        }
        return baseName + "<" + string.Join(", ", args) + ">";
    }

    static void Main(string[] a)
    {
        if (a[0] == "callers")   // callers <dll> <rva-hex>...
        {
            var p = new PE(a[1]);
            var want = a.Skip(2).Select(x => Convert.ToUInt64(x, 16)).ToHashSet();
            int pe0 = BitConverter.ToInt32(p.B, 0x3C), optSize = BitConverter.ToUInt16(p.B, pe0 + 20);
            int secTab = pe0 + 24 + optSize;
            for (int si = 0; si < p.Sec.Count; si++)
            {
                if ((BitConverter.ToUInt32(p.B, secTab + si * 40 + 36) & 0x20000000) == 0) continue;
                var s = p.Sec[si];
                for (long i = s.raw; i + 5 <= s.raw + s.rsize; i++)
                {
                    if (p.B[i] != 0xE8 && p.B[i] != 0xE9) continue;
                    long rva = s.va + (i - s.raw) + 5 + BitConverter.ToInt32(p.B, (int)i + 1);
                    if (rva > 0 && want.Contains((ulong)rva))
                        Console.WriteLine($"{(p.B[i] == 0xE8 ? "call" : "jmp ")} at 0x{s.va + (i - s.raw):X} -> 0x{rva:X}");
                }
            }
            return;
        }
        pe = new PE(a[0]);
        M = File.ReadAllBytes(a[1]);
        for (int i = 0; i < 66; i++) H[i] = BitConverter.ToInt32(M, i * 4);
        if ((uint)H[0] != 0xFAB11BAF) throw new Exception("bad metadata");
        // header pairs: index k -> off H[2+2k], size H[3+2k]
        int Off(int k) => H[2 + 2 * k]; int Size(int k) => H[3 + 2 * k];
        // remap into named fields used above (H[4]=strings off, H[38]=typedefs off, H[24]=genericParams off)
        int stringsOff = Off(2), methodsOff = Off(5), paramsOff = Off(10), fieldsOff = Off(11),
            genParOff = Off(12), typeDefsOff = Off(19), imagesOff = Off(20);
        H[4] = stringsOff; H[38] = typeDefsOff; H[24] = genParOff; H[25] = Size(12);
        typeDefCount = Size(19) / TD;
        int imageCount = Size(20) / IMG;
        Console.WriteLine($"typedefs {typeDefCount}, methods {Size(5) / MD}, images {imageCount}");

        // MetadataRegistration: fieldOffsetsCount (+0x50) and typeDefinitionsSizesCount (+0x60) both == typeDefCount.
        ulong mreg = 0;
        foreach (var s in pe.Sec)
        {
            if (s.name != ".data" && s.name != ".rdata") continue;
            for (uint o = 0; o + 0x70 < s.rsize; o += 8)
            {
                long fo = s.raw + o;
                if (BitConverter.ToInt64(pe.B, (int)fo) != typeDefCount) continue;
                if (fo + 0x10 + 8 > pe.B.Length) continue;
                if (BitConverter.ToInt64(pe.B, (int)fo + 0x10) != typeDefCount) continue;
                ulong cand = pe.ImageBase + s.va + o - 0x50;
                ulong typesCount = pe.U64(cand + 0x30);
                if (typesCount > 1000 && typesCount < 2000000) { mreg = cand; break; }
            }
            if (mreg != 0) break;
        }
        if (mreg == 0) throw new Exception("MetadataRegistration not found");
        ulong tCount = pe.U64(mreg + 0x30), tArr = pe.U64(mreg + 0x38);
        types = new ulong[tCount];
        for (ulong i = 0; i < tCount; i++) types[i] = pe.U64(tArr + i * 8);
        ulong foArr = pe.U64(mreg + 0x58);
        fieldOffsetPtrs = new ulong[typeDefCount];
        for (int i = 0; i < typeDefCount; i++) fieldOffsetPtrs[i] = pe.U64(foArr + (ulong)i * 8);
        Console.WriteLine($"MetadataRegistration at 0x{mreg - pe.ImageBase:X}, types {tCount}");

        // CodeGenModules: find a pointer to each image's name string, followed by count + pointer array.
        var moduleNamePtr = new Dictionary<ulong, string>();
        var imageNames = new string[imageCount];
        for (int i = 0; i < imageCount; i++) imageNames[i] = Str(I32(imagesOff + i * IMG));
        var wanted = new HashSet<string>(imageNames);
        // index every NUL-terminated string in .rdata that equals an image name
        var nameVa = new Dictionary<ulong, string>();
        foreach (var s in pe.Sec)
        {
            if (s.name != ".rdata" && s.name != ".data") continue;
            int start = (int)s.raw, end = (int)(s.raw + s.rsize);
            for (int p = start; p < end; p++)
            {
                if (pe.B[p] != (byte)'.' || p < 4) continue;
                if (p + 4 >= end || pe.B[p + 1] != 'd' || pe.B[p + 2] != 'l' || pe.B[p + 3] != 'l' || pe.B[p + 4] != 0) continue;
                int b = p; while (b > start && pe.B[b - 1] >= 0x20 && pe.B[b - 1] < 0x7F) b--;
                string nm = Encoding.ASCII.GetString(pe.B, b, p + 4 - b);
                if (wanted.Contains(nm)) nameVa[pe.ImageBase + s.va + (ulong)(b - s.raw)] = nm;
            }
        }
        var modulePtrs = new Dictionary<string, (ulong count, ulong arr)>();
        foreach (var s in pe.Sec)
        {
            if (s.name != ".data" && s.name != ".rdata") continue;
            for (uint o = 0; o + 24 < s.rsize; o += 8)
            {
                ulong v = BitConverter.ToUInt64(pe.B, (int)(s.raw + o));
                if (!nameVa.TryGetValue(v, out var nm) || modulePtrs.ContainsKey(nm)) continue;
                ulong cnt = BitConverter.ToUInt64(pe.B, (int)(s.raw + o + 8)) & 0xFFFFFFFF;
                ulong arr = BitConverter.ToUInt64(pe.B, (int)(s.raw + o + 16));
                if (cnt < 200000 && (cnt == 0 || pe.Off(arr) >= 0)) modulePtrs[nm] = (cnt, arr);
            }
        }
        Console.WriteLine($"code gen modules found: {modulePtrs.Count} of {imageCount}");

        // Direct call / jmp counts per target RVA, over every executable section.
        var calls = new Dictionary<ulong, int>();
        {
            int pe0 = BitConverter.ToInt32(pe.B, 0x3C);
            int optSize = BitConverter.ToUInt16(pe.B, pe0 + 20);
            int secTab = pe0 + 24 + optSize;
            for (int si = 0; si < pe.Sec.Count; si++)
            {
                uint ch = BitConverter.ToUInt32(pe.B, secTab + si * 40 + 36);
                if ((ch & 0x20000000) == 0) continue;
                var s = pe.Sec[si];
                for (long i = s.raw; i + 5 <= s.raw + s.rsize; i++)
                {
                    if (pe.B[i] != 0xE8 && pe.B[i] != 0xE9) continue;
                    long rva = s.va + (i - s.raw) + 5 + BitConverter.ToInt32(pe.B, (int)i + 1);
                    if (rva <= 0) continue;
                    ulong key = (ulong)rva;
                    calls[key] = calls.TryGetValue(key, out var c) ? c + 1 : 1;
                }
            }
        }

        using var w = new StreamWriter(a[2], false, new UTF8Encoding(false), 1 << 20);
        w.WriteLine($"# static il2cpp v{H[1]} dump; MetadataRegistration 0x{mreg - pe.ImageBase:X}");
        for (int im = 0; im < imageCount; im++)
        {
            int io = imagesOff + im * IMG;
            string imgName = imageNames[im];
            int typeStart = I32(io + 8), typeCount = I32(io + 12);
            modulePtrs.TryGetValue(imgName, out var mod);
            for (int ti = typeStart; ti < typeStart + typeCount; ti++)
            {
                int o = typeDefsOff + ti * TD;
                int parentIdx = I32(o + 16), flags = I32(o + 28), fieldStart = I32(o + 32), methodStart = I32(o + 36);
                int methodCount = U16(o + 64), fieldCount = U16(o + 68);
                string parent = parentIdx >= 0 ? TypeName(parentIdx) : "";
                w.WriteLine($"C {TDName(ti)} : {parent} [{imgName}] td={ti} flags=0x{flags:X}");
                ulong offs = fieldOffsetPtrs[ti];
                for (int f = 0; f < fieldCount; f++)
                {
                    int fo = fieldsOff + (fieldStart + f) * FD;
                    int typeIdx = I32(fo + 4);
                    ulong tp = typeIdx >= 0 && typeIdx < types.Length ? types[typeIdx] : 0;
                    uint attrs = tp != 0 ? pe.U32(tp + 8) & 0xFFFF : 0;
                    bool isStatic = (attrs & 0x10) != 0, isLit = (attrs & 0x40) != 0;
                    int offset = offs != 0 ? (int)pe.U32(offs + (ulong)f * 4) : -1;
                    w.WriteLine($"  F {(isLit ? "const " : isStatic ? "static " : "")}0x{offset:X} {TypeName(typeIdx)} {Str(I32(fo))}");
                }
                for (int mi = 0; mi < methodCount; mi++)
                {
                    int mo = methodsOff + (methodStart + mi) * MD;
                    string mname = Str(I32(mo));
                    int retType = I32(mo + 8), paramStart = I32(mo + 16);
                    uint token = (uint)I32(mo + 24);
                    ushort mflags = U16(mo + 28), pcount = U16(mo + 34);
                    uint rid = token & 0x00FFFFFF;
                    ulong rva = 0;
                    if (mod.arr != 0 && rid >= 1 && rid <= mod.count)
                    {
                        ulong ptr = pe.U64(mod.arr + (rid - 1) * 8);
                        if (ptr > pe.ImageBase) rva = ptr - pe.ImageBase;
                    }
                    var ps = new List<string>();
                    for (int p = 0; p < pcount; p++) ps.Add(TypeName(I32(paramsOff + (paramStart + p) * PD + 8)));
                    calls.TryGetValue(rva, out int nCalls);
                    w.WriteLine($"  M 0x{rva:X} 0x{mflags:X4} {TypeName(retType)} {mname}({string.Join(", ", ps)}) calls={nCalls}");
                }
            }
        }
        Console.WriteLine("done");
    }
}
