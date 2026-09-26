namespace TBH_Trainer;

/// <summary>
/// Finds the six ACTk detectors in a GameAssembly.dll the trainer has never seen.
///
/// Every build from 1.00.08 to 1.2.8 keeps the detectors in the same order and at the same
/// distance from each other — only the whole block moves. So the scan looks for the first
/// detector's prologue and accepts a hit only when the other five sit exactly where that
/// layout says they should. A single unique hit is required: anything else is reported as
/// a failure rather than guessed at, because writing a RET to the wrong function would
/// patch some unrelated game code.
/// </summary>
internal static class ActkScanner
{
    /// <summary>Offsets from ObscuredCheatingDetector.Check, identical on every known build.</summary>
    private static readonly (string Name, int Delta, byte[] Signature)[] Layout =
    [
        ("ObscuredCheatingDetector.Check",             0, new byte[] { 0x41, 0x56, 0x48, 0x83, 0xEC, 0x20 }),
        ("ObscuredCheatingDetector.Compare",       0x190, new byte[] { 0x48, 0x89, 0x5C, 0x24, 0x10, 0x48 }),
        ("ObscuredCheatingDetector.CompareExt",    0x270, new byte[] { 0x48, 0x89, 0x5C, 0x24, 0x10, 0x48 }),
        ("InjectionDetector.Check",               -0xAE0, new byte[] { 0x40, 0x53, 0x48, 0x83, 0xEC, 0x20 }),
        ("SpeedHackDetector.Update",              0x4F10, new byte[] { 0x40, 0x56, 0x48, 0x83, 0xEC, 0x70 }),
        ("SpeedHackDetector.OnApplicationPause",  0x4E80, new byte[] { 0x48, 0x89, 0x5C, 0x24, 0x08, 0x57 }),
    ];

    private readonly record struct Section(int VirtualAddress, int VirtualSize, int RawPointer, int RawSize, bool Executable);

    public readonly record struct Result(
        (string Name, int Rva, int DiskOffset, byte[] Signature)[]? Targets,
        string Detail)
    {
        public bool Ok => Targets != null;
    }

    /// <summary>Scans the DLL on disk. Never throws: failures come back in <see cref="Result.Detail"/>.</summary>
    public static Result Scan(string dllPath)
    {
        try
        {
            using var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var sections = ReadSections(fs);
            if (sections.Count == 0) return new Result(null, "not a PE file the scanner understands");

            var hits = new List<(string Name, int Rva, int DiskOffset, byte[] Signature)[]>();
            foreach (var section in sections)
            {
                if (!section.Executable) continue;
                foreach (int fileOffset in FindAll(fs, section, Layout[0].Signature))
                {
                    int rva = fileOffset - section.RawPointer + section.VirtualAddress;
                    if ((rva & 0xF) != 0) continue;             // functions are 16-byte aligned
                    var targets = VerifyLayout(fs, sections, rva);
                    if (targets != null) hits.Add(targets);
                    if (hits.Count > 1) return new Result(null, "more than one layout match — refusing to guess");
                }
            }

            if (hits.Count == 0) return new Result(null, "detector layout not found (the game changed more than an offset shift)");
            return new Result(hits[0], $"layout found at RVA 0x{hits[0][0].Rva:X}");
        }
        catch (Exception ex)
        {
            return new Result(null, $"scan failed: {ex.Message}");
        }
    }

    /// <summary>All six prologues must match at their expected place, or the candidate is rejected.</summary>
    private static (string Name, int Rva, int DiskOffset, byte[] Signature)[]? VerifyLayout(
        FileStream fs, List<Section> sections, int checkRva)
    {
        var targets = new (string, int, int, byte[])[Layout.Length];
        for (int i = 0; i < Layout.Length; i++)
        {
            var (name, delta, signature) = Layout[i];
            int rva = checkRva + delta;
            int offset = RvaToFileOffset(sections, rva);
            if (offset < 0) return null;
            if (!MatchesAt(fs, offset, signature)) return null;
            targets[i] = (name, rva, offset, signature);
        }
        return targets;
    }

    private static List<Section> ReadSections(FileStream fs)
    {
        var sections = new List<Section>();
        var header = new byte[0x1000];
        fs.Seek(0, SeekOrigin.Begin);
        if (fs.Read(header, 0, header.Length) < 0x200) return sections;
        if (header[0] != (byte)'M' || header[1] != (byte)'Z') return sections;

        int pe = BitConverter.ToInt32(header, 0x3C);
        if (pe <= 0 || pe + 24 > header.Length) return sections;
        if (BitConverter.ToUInt32(header, pe) != 0x00004550) return sections;   // "PE\0\0"

        int sectionCount = BitConverter.ToUInt16(header, pe + 6);
        int optionalSize = BitConverter.ToUInt16(header, pe + 20);
        int table = pe + 24 + optionalSize;
        for (int i = 0; i < sectionCount; i++)
        {
            int e = table + i * 40;
            if (e + 40 > header.Length) break;
            sections.Add(new Section(
                VirtualAddress: BitConverter.ToInt32(header, e + 12),
                VirtualSize: BitConverter.ToInt32(header, e + 8),
                RawPointer: BitConverter.ToInt32(header, e + 20),
                RawSize: BitConverter.ToInt32(header, e + 16),
                Executable: (BitConverter.ToUInt32(header, e + 36) & 0x20000000) != 0));
        }
        return sections;
    }

    private static int RvaToFileOffset(List<Section> sections, int rva)
    {
        foreach (var s in sections)
        {
            if (rva < s.VirtualAddress) continue;
            if (rva >= s.VirtualAddress + Math.Max(s.VirtualSize, s.RawSize)) continue;
            int offset = rva - s.VirtualAddress + s.RawPointer;
            return offset < s.RawPointer + s.RawSize ? offset : -1;
        }
        return -1;
    }

    private static bool MatchesAt(FileStream fs, int offset, byte[] signature)
    {
        Span<byte> buffer = stackalloc byte[8];
        fs.Seek(offset, SeekOrigin.Begin);
        var slice = buffer[..signature.Length];
        return fs.ReadAtLeast(slice, signature.Length, throwOnEndOfStream: false) == signature.Length &&
               slice.SequenceEqual(signature);
    }

    /// <summary>Streams one section looking for a signature (the DLL is ~100 MB, so no full read).</summary>
    private static IEnumerable<int> FindAll(FileStream fs, Section section, byte[] signature)
    {
        const int chunk = 4 << 20;
        var buffer = new byte[chunk + 16];
        int end = section.RawPointer + section.RawSize;
        for (int start = section.RawPointer; start < end; start += chunk)
        {
            int want = Math.Min(chunk + signature.Length - 1, end - start);
            fs.Seek(start, SeekOrigin.Begin);
            int got = fs.ReadAtLeast(buffer.AsSpan(0, want), want, throwOnEndOfStream: false);
            for (int i = 0; i + signature.Length <= got; i++)
            {
                if (buffer[i] != signature[0]) continue;
                if (buffer.AsSpan(i, signature.Length).SequenceEqual(signature))
                    yield return start + i;
            }
        }
    }
}
