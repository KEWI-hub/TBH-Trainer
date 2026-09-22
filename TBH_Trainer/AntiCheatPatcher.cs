namespace TBH_Trainer;

internal sealed class AntiCheatPatcher
{
  private readonly GameMemory _mem;
  private readonly GameBuildProfile _profile;
  private (string Name, int Rva, byte[] Signature)[] _targets;
  private readonly Dictionary<IntPtr, byte[]> _originalBytes = new();

  /// <summary>Extra prologues seen in-process when on-disk bytes differ (1.00.09).</summary>
  private static readonly Dictionary<string, byte[][]> AlternateSignatures = new(StringComparer.Ordinal)
  {
    ["ObscuredCheatingDetector.Check"] =
    [
      [0x48, 0x83, 0xEC, 0x28, 0x80, 0x3D], // live module (common)
      [0x40, 0x53, 0x48, 0x83, 0xEC, 0x20], // GameAssembly.dll on disk
    ],
  };

  public AntiCheatPatcher(GameMemory mem, GameBuildProfile profile, string? gameAssemblyDllPath = null)
  {
    _mem = mem;
    _profile = profile;
    _ = gameAssemblyDllPath; // reserved for future disk-assisted restore
    _targets = profile.ActkTargets.Select(t => (t.Name, t.Rva, t.Signature)).ToArray();
  }

  public List<(string name, bool ok, string detail)> PatchDetectors()
  {
    var report = new List<(string, bool, string)>();
    if (!_mem.IsAttached || _mem.GameAssemblyBase == IntPtr.Zero)
    {
      report.Add(("Not connected", false, "Attach to the game first."));
      return report;
    }

    foreach (var t in _targets)
    {
      var (ok, detail) = PatchToRet(t);
      report.Add((t.Name, ok, detail));
    }
    return report;
  }

  public bool RestoreDetectors()
  {
    bool allOk = true;
    foreach (var (addr, original) in _originalBytes)
      allOk &= _mem.WriteBytes(addr, original);
    _originalBytes.Clear();
    return allOk;
  }

  private IEnumerable<byte[]> AcceptableSignatures((string Name, int Rva, byte[] Signature) t)
  {
    yield return t.Signature;
    if (AlternateSignatures.TryGetValue(t.Name, out var alts))
    {
      foreach (var alt in alts)
        yield return alt;
    }
  }

  private (bool ok, string detail) PatchToRet((string Name, int Rva, byte[] Signature) t)
  {
    IntPtr addr = _mem.GameAssemblyBase + t.Rva;
    int probeLen = 6;
    byte[] cur = _mem.ReadBytes(addr, probeLen);
    if (cur.Length != probeLen)
      return (false, "Could not read process memory (run as Administrator?).");

    if (cur[0] == 0xC3)
      return (true, "Already patched.");

    bool matched = AcceptableSignatures(t).Any(sig =>
      sig.Length == probeLen && cur.AsSpan().SequenceEqual(sig));

    // Legacy 1.00.09 escape hatch only; newer builds must match a known signature.
    if (!matched && _profile.BuildId == 10009 && cur[0] is not (0xC3 or 0x00))
    {
      matched = true;
      return WriteRet(addr, cur, $"Patched at RVA 0x{t.Rva:X} (live prologue {Hex(cur)}).");
    }

    if (!matched)
    {
      var expected = t.Signature;
      return (false,
        $"Signature mismatch at RVA 0x{t.Rva:X} (found {Hex(cur)}, expected {Hex(expected)}) — wrong game version? Skipped, memory untouched.");
    }

    return WriteRet(addr, cur, "RET written (verified).");
  }

  private (bool ok, string detail) WriteRet(IntPtr addr, byte[] cur, string successDetail)
  {
    if (!_originalBytes.ContainsKey(addr))
      _originalBytes[addr] = new[] { cur[0] };

    _mem.WriteBytes(addr, new byte[] { 0xC3 });

    byte[] check = _mem.ReadBytes(addr, 1);
    if (check.Length == 1 && check[0] == 0xC3)
      return (true, successDetail);
    return (false, "Write verification failed.");
  }

  private static string Hex(byte[] b) => string.Join(" ", b.Select(x => x.ToString("X2")));
}
