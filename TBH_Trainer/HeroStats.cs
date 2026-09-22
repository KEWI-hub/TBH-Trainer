namespace TBH_Trainer;

/// <summary>
/// External HP/ATK editing from GameAssembly.dll static roots.
/// Supports both 1.00.08 (static pointer chains) and 1.00.09 (runtime hero unit scan + ACTk decrypt).
/// </summary>
internal sealed class HeroStats
{
    private readonly GameMemory _mem;
    private readonly GameBuildProfile _profile;

    public HeroStats(GameMemory mem, GameBuildProfile profile)
    {
        _mem = mem;
        _profile = profile;
    }

    private const long V1009HpStatic = 0x57BC000;
    private static readonly long[] V1009HpChain = [0xB8, 0x30, 0x20, 0xB0, 0x38];

    private IntPtr ResolveProfileChain(long staticOffset, long[] offsets)
    {
        if (!_mem.IsAttached || _mem.GameAssemblyBase == IntPtr.Zero) return IntPtr.Zero;
        if (staticOffset == 0 || offsets.Length == 0) return IntPtr.Zero;

        IntPtr addr = _mem.GameAssemblyBase + (int)staticOffset;
        foreach (long off in offsets)
        {
            long val = _mem.ReadInt64(addr);
            if (val == 0) return IntPtr.Zero;
            addr = new IntPtr(val + off);
        }
        return addr;
    }

    private IntPtr ResolveV1009Hp() => ResolveProfileChain(V1009HpStatic, V1009HpChain);

    /// <summary>
    /// Finds the hero unit for 1.00.09 by trying known static candidate lists first,
    /// then falling back to a memory scan if all lists fail.
    /// </summary>
    private (long unit, long staticRva, long listOff) FindV1009HeroUnit()
    {
        var candidates = new List<(long unit, long staticRva, long listOff)>();

        var staticCandidates = new[]
        {
            (0x57BC000L, 0xB8L),
            (0x56D91C8L, 0x30L),
            (0x56D9270L, 0x30L),
            (0x56D9158L, 0x150L),
            (0x56D90E8L, 0x80L),
            (0x56D8E48L, 0x1F0L),
        };

        foreach (var (staticRva, listOff) in staticCandidates)
        {
            long unit = WalkToV1009Unit(staticRva, listOff);
            if (unit == 0) continue;

            byte isHero = _mem.ReadBytes(new IntPtr(unit + 0x100), 1)[0]; // Unit_b_isHero = 0x100
            int atkKey = _mem.ReadInt32(new IntPtr(unit + 0x104 + 8));    // Unit_Stat0 = 0x104

            if (isHero == 1 || atkKey != 0)
                candidates.Add((unit, staticRva, listOff));
        }

        // Pick the candidate with a valid decrypted ATK value
        foreach (var (unit, rva, loff) in candidates)
        {
            var data = ACTkCrypto.ReadObscuredFloat(_mem, new IntPtr(unit + 0x104));
            if (data.CryptoKey != 0)
            {
                float atk = data.Decrypt();
                if (!float.IsNaN(atk) && !float.IsInfinity(atk) && atk > 10.0f && atk < 10000000.0f)
                    return (unit, rva, loff);
            }
        }

        // Full memory scan fallback
        return ScanForValidHeroUnit();
    }

    private (long, long, long) ScanForValidHeroUnit()
    {
        if (!_mem.IsAttached || _mem.GameAssemblyBase == IntPtr.Zero || _mem.GameAssemblySize <= 0)
            return (0, 0, 0);

        long baseAddr = _mem.GameAssemblyBase.ToInt64();
        long endAddr  = baseAddr + _mem.GameAssemblySize;

        long[] testRvas = [0x57BC000, 0x56D9000, 0x56D8000, 0x57C0000, 0x57D0000,
                           0xB6E000,  0xB6D000,  0xB6C000];

        foreach (long rva in testRvas)
        {
            if (rva > _mem.GameAssemblySize) continue;

            IntPtr ptrAddr = _mem.GameAssemblyBase + (int)rva;
            long val = _mem.ReadInt64(ptrAddr);
            if (val == 0) continue;

            long[] offsets = [0x20, 0x30, 0x80, 0xB8, 0x100, 0x150, 0x1F0];
            foreach (long off in offsets)
            {
                long checkAddr = val + off;
                long unitPtr   = _mem.ReadInt64(new IntPtr(checkAddr));
                if (unitPtr == 0) continue;
                if (unitPtr < baseAddr || unitPtr > endAddr) continue;

                byte isHero = _mem.ReadBytes(new IntPtr(unitPtr + 0x100), 1)[0];
                if (isHero != 1) continue;

                var data = ACTkCrypto.ReadObscuredFloat(_mem, new IntPtr(unitPtr + 0x104));
                if (data.CryptoKey != 0)
                {
                    float atk = data.Decrypt();
                    if (!float.IsNaN(atk) && !float.IsInfinity(atk) && atk > 10.0f && atk < 10000000.0f)
                        return (unitPtr, rva, off);
                }
            }
        }

        return (0, 0, 0);
    }

    private long WalkToV1009Unit(long staticRva, long listOff)
    {
        if (!_mem.IsAttached || _mem.GameAssemblyBase == IntPtr.Zero) return 0;

        long addr = _mem.GameAssemblyBase.ToInt64() + staticRva;
        long val  = _mem.ReadInt64(new IntPtr(addr));
        if (val == 0) return 0;

        addr = val + listOff;
        val  = _mem.ReadInt64(new IntPtr(addr));
        if (val == 0) return 0;

        addr = val + 0x20;
        val  = _mem.ReadInt64(new IntPtr(addr));
        return val;
    }

    public IntPtr ResolveHp() =>
        _profile.BuildId >= 10009
            ? ResolveV1009Hp()
            : ResolveProfileChain(_profile.HpStatic, _profile.HpOffsets);

    public IntPtr ResolveAtk()
    {
        if (_profile.BuildId >= 10009)
        {
            var (unit, _, _) = FindV1009HeroUnit();
            return unit == 0 ? IntPtr.Zero : new IntPtr(unit + 0x104); // Unit_Stat0
        }
        return ResolveProfileChain(_profile.AtkStatic, _profile.AtkOffsets);
    }

    public float? ReadHp()
    {
        var addr = ResolveHp();
        return addr == IntPtr.Zero ? null : _mem.ReadFloat(addr);
    }

    public float? ReadAtk()
    {
        var addr = ResolveAtk();
        if (addr == IntPtr.Zero) return null;

        if (_profile.BuildId >= 10009)
        {
            var data = ACTkCrypto.ReadObscuredFloat(_mem, addr);
            if (data.CryptoKey == 0) return data.FakeValue;
            float decrypted = data.Decrypt();
            if (!float.IsNaN(decrypted) && !float.IsInfinity(decrypted) && decrypted >= 0)
                return decrypted;
            return data.FakeValue;
        }
        return _mem.ReadFloat(addr);
    }

    public bool SetHp(float value)
    {
        var addr = ResolveHp();
        if (addr == IntPtr.Zero) return false;
        return _mem.WriteBytes(addr, BitConverter.GetBytes(value));
    }

    public bool SetAtk(float value)
    {
        var addr = ResolveAtk();
        if (addr == IntPtr.Zero) return false;

        if (_profile.BuildId >= 10009)
        {
            var data = ACTkCrypto.ReadObscuredFloat(_mem, addr);
            if (data.CryptoKey == 0) return false;
            data.SetValue(value);
            ACTkCrypto.WriteObscuredFloat(_mem, addr, data);
            return true;
        }

        return _mem.WriteBytes(addr, BitConverter.GetBytes(value));
    }

    public (IntPtr result, string diag) ResolveHpWithDiag()
    {
        if (!_mem.IsAttached || _mem.GameAssemblyBase == IntPtr.Zero)
            return (IntPtr.Zero, "HP: Not attached.");

        if (_profile.BuildId < 10009)
        {
            var addr = ResolveHp();
            return addr == IntPtr.Zero
                ? (IntPtr.Zero, "HP: Profile pointer chain did not resolve.")
                : (addr, $"HP: OK at 0x{addr.ToInt64():X}");
        }

        IntPtr cur = _mem.GameAssemblyBase + (int)V1009HpStatic;
        for (int i = 0; i < V1009HpChain.Length; i++)
        {
            long val = _mem.ReadInt64(cur);
            if (val == 0)
                return (IntPtr.Zero, $"HP: Chain broke at hop {i}.");
            cur = new IntPtr(val + V1009HpChain[i]);
        }
        return (cur, $"HP: OK at 0x{cur.ToInt64():X}");
    }

    public (IntPtr result, string diag) ResolveAtkWithDiag()
    {
        if (_profile.BuildId < 10009)
        {
            var profileAddr = ResolveAtk();
            return profileAddr == IntPtr.Zero
                ? (IntPtr.Zero, "ATK: Profile pointer chain did not resolve.")
                : (profileAddr, $"ATK: OK at 0x{profileAddr.ToInt64():X}");
        }

        var (unit, staticRva, listOff) = FindV1009HeroUnit();
        if (unit == 0)
            return (IntPtr.Zero, "ATK: No valid Hero found. (Scanned lists and memory. Ensure you are in battle with valid stats.)");

        var addr = new IntPtr(unit + 0x104);
        var data = ACTkCrypto.ReadObscuredFloat(_mem, addr);
        float decrypted = data.Decrypt();

        return (addr, $"ATK: Found @ 0x{addr.ToInt64():X} (Unit base 0x{unit:X}). Key=0x{data.CryptoKey:X8}, Decrypted={decrypted:0.00}, Fake={data.FakeValue:0.00}");
    }
}
