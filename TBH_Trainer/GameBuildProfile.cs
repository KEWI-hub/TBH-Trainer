namespace TBH_Trainer;

/// <summary>
/// Per-game-build offsets from Il2CppDumper (dump.cs). V1009 parsed from 1.00.09 hotfix dump.
/// </summary>
internal sealed class GameBuildProfile
{
    public required string VersionLabel { get; init; }
    public required int BuildId { get; init; }

    public required (string Name, int Rva, int DiskOffset, byte[] Signature)[] ActkTargets { get; init; }

    public required long HpStatic { get; init; }
    public required long[] HpOffsets { get; init; }
    public required long AtkStatic { get; init; }
    public required long[] AtkOffsets { get; init; }

    /// <summary>Stash insert: ud.Stash.jbg (1.00.08) / ue.Stash.jbn (1.00.09).</summary>
    public required int StashInsertRva { get; init; }
    /// <summary>Stash slot lookup: ud.Stash.jbp / ue.Stash.jbw.</summary>
    public required int StashSlotRva { get; init; }
    /// <summary>Stash init: ud.Stash.jbb / ue.Stash.jbi.</summary>
    public required int StashInitRva { get; init; }
    public required int StashSaveDataCtor { get; init; }
    /// <summary>ue.ti.blk — primary add-item API on 1.00.09 (0 = use stash path only).</summary>
    public required int ItemAddRva { get; init; }
    /// <summary>
    /// Static ItemInfoData lookup(int itemKey) used by the hook on builds whose item API is
    /// resolved by RVA instead of obfuscated names (1.2.4: we.va.jjc). 0 = name lookup.
    /// </summary>
    public int ItemInfoRva { get; init; }
    /// <summary>Il2cpp namespace for stash type: "ud" or "ue".</summary>
    public required string StashNamespace { get; init; }

    /// <summary>
    /// False when only the ACTk RVAs are known for this build. Stash spawn, item scan/export
    /// and hero stats rely on obfuscated il2cpp names and field offsets that must be
    /// re-verified from a fresh Il2CppDumper dump before they are safe to run.
    /// </summary>
    public bool GameApiVerified { get; init; } = true;

    /// <summary>
    /// Hero Stats (HP / Attack Speed locks) only need the Unit / health-controller field
    /// offsets, which the hero layout dump can confirm independently of the stash API.
    /// </summary>
    public bool HeroStatsVerified { get; init; } = true;

    /// <summary>Item scan reads stash/save classes by obfuscated name (1.00.08 / 1.00.09 only).</summary>
    public bool ItemScanSupported { get; init; } = true;

    /// <summary>rz.ikf(ESlotType, int) -> uid of the item in a slot (inventory scan / auto stash). 0 = n/a.</summary>
    public int SlotUidRva { get; init; }
    /// <summary>rz.ikc(ESlotType, int) -> ItemSlot UI object.</summary>
    public int SlotObjRva { get; init; }
    /// <summary>SlotInteractionManager.inj(rm, bool, ulong) -> SlotActionContext.</summary>
    public int SlotCtxRva { get; init; }
    /// <summary>SlotInteractionManager.inm(SlotActionResult, rm, SlotActionContext) — executes a slot move.</summary>
    public int SlotActionRva { get; init; }

    /// <summary>static Stash.kcc(int) -> StashCache (per-slot unlock state; stash size differs per player).</summary>
    public int StashCacheRva { get; init; }

    /// <summary>static int wh.uy.jif(EBoxType, EContentType) -> unopened stage boxes (auto open boxes).</summary>
    public int BoxCountRva { get; init; }

    /// <summary>static void wh.Stash.kcb(): the stash Sort button's handler, sorts every page. 0 = press the UI button instead.</summary>
    public int StashSortAllRva { get; init; }

    /// <summary>rz.ije(MoveRequest, Action&lt;MoveResult&gt;): drag &amp; drop between slots (stash organizer).</summary>
    public int SlotMoveRva { get; init; }

    /// <summary>Inventory scan through the slot API (1.2.6+), replaces the legacy item scan.</summary>
    public bool InventoryScanSupported => SlotUidRva > 0;

    // Legacy property names for TrainerBridge / hook shared mem
    public int StashJbg => StashInsertRva;
    public int StashJbp => StashSlotRva;
    public int StashJbb => StashInitRva;

    public static GameBuildProfile V1008 { get; } = new()
    {
        VersionLabel = "1.00.08",
        BuildId = 10008,
        ActkTargets =
        [
            ("ObscuredCheatingDetector.Check",       0x6CC1C0, 0x6CC1C0, [0x41, 0x56, 0x48, 0x83, 0xEC, 0x20]),
            ("ObscuredCheatingDetector.Compare",     0x6CC350, 0x6CC350, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("ObscuredCheatingDetector.CompareExt",  0x6CC430, 0x6CC430, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("InjectionDetector.Check",              0x6CB6E0, 0x6CB6E0, [0x40, 0x53, 0x48, 0x83, 0xEC, 0x20]),
            ("SpeedHackDetector.Update",             0x6D10D0, 0x6D10D0, [0x40, 0x56, 0x48, 0x83, 0xEC, 0x70]),
            ("SpeedHackDetector.OnApplicationPause", 0x6D1040, 0x6D1040, [0x48, 0x89, 0x5C, 0x24, 0x08, 0x57]),
        ],
        HpStatic = 0x5AEE670,
        HpOffsets = [0x28, 0x68, 0xB0, 0x40],
        AtkStatic = 0x57C6A50,
        AtkOffsets = [0xB8, 0x40, 0x10, 0x20, 0x18, 0x3C],
        StashInsertRva = 0x8F0320,
        StashSlotRva = 0x8F15D0,
        StashInitRva = 0x8EF8C0,
        StashSaveDataCtor = 0x99C9F0,
        ItemAddRva = 0,
        StashNamespace = "ud",
    };

    /// <summary>Taskbar Hero 1.00.09 hotfix — offsets verified Jun 2026.</summary>
    public static GameBuildProfile V1009 { get; } = new()
    {
        VersionLabel = "1.00.09",
        BuildId = 10009,
        ActkTargets =
        [
            // Signatures verified in live process Jun 2026.
            ("ObscuredCheatingDetector.Check",       0x6C1840, 0x6C0240, [0x41, 0x56, 0x48, 0x83, 0xEC, 0x20]),
            ("ObscuredCheatingDetector.Compare",     0x6C19D0, 0x6C03D0, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("ObscuredCheatingDetector.CompareExt",  0x6C1AB0, 0x6C04B0, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("InjectionDetector.Check",              0x6C0D60, 0x6BF760, [0x40, 0x53, 0x48, 0x83, 0xEC, 0x20]),
            ("SpeedHackDetector.Update",             0x6C6750, 0x6C5150, [0x40, 0x56, 0x48, 0x83, 0xEC, 0x70]),
            ("SpeedHackDetector.OnApplicationPause", 0x6C66C0, 0x6C50C0, [0x48, 0x89, 0x5C, 0x24, 0x08, 0x57]),
        ],
        // 1.00.09: HP resolves via static pointer [base+0x57BC000] -> +0xB8 -> +0x30 -> +0x20 -> +0xB0 -> +0x38.
        // AtkStatic/AtkOffsets unused for 1.00.09; HeroStats resolves Unit at runtime.
        HpStatic  = 0x57BC000,
        HpOffsets = [0xB8, 0x30, 0x20, 0xB0, 0x38],
        AtkStatic = 0,
        AtkOffsets = [],
        StashInsertRva = 0x8D62F0,  // ue.Stash.jbn(ulong, StashCache)
        StashSlotRva   = 0x8D75A0,  // ue.Stash.jbw(int)
        StashInitRva   = 0x8D5890,  // ue.Stash.jbi()
        StashSaveDataCtor = 0x94DEF0,
        ItemAddRva = 0x8BF830,      // ue.ti.blk(int itemKey, ulong uid, bool)
        StashNamespace = "ue",
    };

    /// <summary>
    /// Taskbar Hero 1.2.4 (Steam build 25336766, Version.txt "1.2.4", Sep 2026).
    /// ACTk RVAs located by prologue scan: all six detectors keep the 1.00.09 layout,
    /// shifted by +0x807B0. The il2cpp section maps file offset = RVA - 0x1A00.
    /// Hero Stats verified from the in-game hero layout dump (Sep 2026): Unit.b_isHero +0x100,
    /// AttackDamage/AttackSpeed ObscuredFloat +0x104/+0x118, UnitHealthController (pq) +0xB0,
    /// HP +0x40 / max HP +0x4C, StageManager.HeroList +0x30 — same as 1.00.09.
    /// Item spawn from the 1.2.4 Il2CppDumper dump (dump_1.2.4): we.va.jiv adds an item to the
    /// inventory, we.va.jjc looks up ItemInfoData; the hook resolves both by RVA. Stash natives
    /// (we.Stash jxs/jyb/jxn) are known but unused — spawn never goes through the stash here.
    /// </summary>
    public static GameBuildProfile V124 { get; } = new()
    {
        VersionLabel = "1.2.4",
        BuildId = 10204,
        ActkTargets =
        [
            ("ObscuredCheatingDetector.Check",       0x741FF0, 0x7405F0, [0x41, 0x56, 0x48, 0x83, 0xEC, 0x20]),
            ("ObscuredCheatingDetector.Compare",     0x742180, 0x740780, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("ObscuredCheatingDetector.CompareExt",  0x742260, 0x740860, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("InjectionDetector.Check",              0x741510, 0x73FB10, [0x40, 0x53, 0x48, 0x83, 0xEC, 0x20]),
            ("SpeedHackDetector.Update",             0x746F00, 0x745500, [0x40, 0x56, 0x48, 0x83, 0xEC, 0x70]),
            ("SpeedHackDetector.OnApplicationPause", 0x746E70, 0x745470, [0x48, 0x89, 0x5C, 0x24, 0x08, 0x57]),
        ],
        HpStatic = 0,
        HpOffsets = [],
        AtkStatic = 0,
        AtkOffsets = [],
        StashInsertRva = 0,         // we.Stash.jxs 0x9A76C0 — unused, see summary
        StashSlotRva = 0,           // we.Stash.jyb 0x9A8B40
        StashInitRva = 0,           // we.Stash.jxn 0x9A6C30
        StashSaveDataCtor = 0,      // StashSaveData.ctor(int) 0xA93070
        ItemAddRva = 0x96E930,      // we.va.jiv(int itemKey, ulong uid, EItemGetSourceType, int count, bool)
        ItemInfoRva = 0x96F2D0,     // we.va.jjc(int itemKey) -> ItemInfoData
        StashNamespace = "we",
        GameApiVerified = true,
        ItemScanSupported = false,
        HeroStatsVerified = true,
    };

    /// <summary>
    /// Taskbar Hero 1.2.6 (Steam build 25435119, Version.txt "1.2.6", 2026-09-22; 1.2.5 was
    /// superseded the same day). Same layout as 1.2.4: ACTk prologues found by scan (file offset
    /// = RVA - 0x1400), hero/Unit/StageManager/ItemInfoData offsets unchanged (dump_1.2.6).
    /// The obfuscator now clones the add-item and ItemInfoData lookup methods (4 and 2 copies);
    /// every clone calls the same internals, so the jm* originals are used: wh.vc.jmk / jmu.
    /// </summary>
    public static GameBuildProfile V126 { get; } = new()
    {
        VersionLabel = "1.2.6",
        BuildId = 10206,
        ActkTargets =
        [
            ("ObscuredCheatingDetector.Check",       0x738DE0, 0x7379E0, [0x41, 0x56, 0x48, 0x83, 0xEC, 0x20]),
            ("ObscuredCheatingDetector.Compare",     0x738F70, 0x737B70, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("ObscuredCheatingDetector.CompareExt",  0x739050, 0x737C50, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("InjectionDetector.Check",              0x738300, 0x736F00, [0x40, 0x53, 0x48, 0x83, 0xEC, 0x20]),
            ("SpeedHackDetector.Update",             0x73DCF0, 0x73C8F0, [0x40, 0x56, 0x48, 0x83, 0xEC, 0x70]),
            ("SpeedHackDetector.OnApplicationPause", 0x73DC60, 0x73C860, [0x48, 0x89, 0x5C, 0x24, 0x08, 0x57]),
        ],
        HpStatic = 0,
        HpOffsets = [],
        AtkStatic = 0,
        AtkOffsets = [],
        StashInsertRva = 0,
        StashSlotRva = 0,
        StashInitRva = 0,
        StashSaveDataCtor = 0,
        ItemAddRva = 0x978030,      // wh.vc.jmk(int itemKey, ulong uid, EItemGetSourceType, int count, bool)
        ItemInfoRva = 0x979070,     // wh.vc.jmu(int itemKey) -> ItemInfoData
        // Slot API (the path the game uses when the player clicks a slot). rz has many cloned
        // accessors; these are the ones SlotInteractionManager actually calls.
        SlotUidRva = 0x8E86A0,      // rz.ikf(ESlotType, int) -> ulong uid
        SlotObjRva = 0x8E7D00,      // rz.ikc(ESlotType, int) -> ItemSlot
        SlotCtxRva = 0x8F0830,      // SlotInteractionManager.inj(rm, bool, ulong) -> SlotActionContext
        SlotActionRva = 0x8F0A90,   // SlotInteractionManager.inm(SlotActionResult, rm, SlotActionContext)
        StashCacheRva = 0x9B35F0,   // wh.Stash.kcc(int) -> StashCache
        BoxCountRva   = 0x9638A0,   // wh.uy.jif(EBoxType, EContentType) -> box count (used by StageBox click)
        StashNamespace = "wh",
        GameApiVerified = true,
        ItemScanSupported = false,
        HeroStatsVerified = true,
    };

    /// <summary>
    /// Taskbar Hero 1.2.7 (Steam build 25453330, 2026-09-22). Same layout as 1.2.6 (Unit, Hero,
    /// StageManager, ItemInfoData, StageBox, StashSaveData, SlotInteractionManager unchanged in
    /// dump_1.2.7); ACTk file offset = RVA - 0xC00. Fewer decoy clones than 1.2.6; every RVA
    /// below is the copy the game itself calls (checked by scanning E8 call sites).
    /// </summary>
    public static GameBuildProfile V127 { get; } = new()
    {
        VersionLabel = "1.2.7",
        BuildId = 10207,
        ActkTargets =
        [
            ("ObscuredCheatingDetector.Check",       0x73B610, 0x73AA10, [0x41, 0x56, 0x48, 0x83, 0xEC, 0x20]),
            ("ObscuredCheatingDetector.Compare",     0x73B7A0, 0x73ABA0, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("ObscuredCheatingDetector.CompareExt",  0x73B880, 0x73AC80, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("InjectionDetector.Check",              0x73AB30, 0x739F30, [0x40, 0x53, 0x48, 0x83, 0xEC, 0x20]),
            ("SpeedHackDetector.Update",             0x740520, 0x73F920, [0x40, 0x56, 0x48, 0x83, 0xEC, 0x70]),
            ("SpeedHackDetector.OnApplicationPause", 0x740490, 0x73F890, [0x48, 0x89, 0x5C, 0x24, 0x08, 0x57]),
        ],
        HpStatic = 0,
        HpOffsets = [],
        AtkStatic = 0,
        AtkOffsets = [],
        StashInsertRva = 0,
        StashSlotRva = 0,
        StashInitRva = 0,
        StashSaveDataCtor = 0,
        ItemAddRva = 0x96E5E0,      // wh.vc.jml(int itemKey, ulong uid, EItemGetSourceType, int count, bool)
        ItemInfoRva = 0x96F760,     // wh.vc.jmv(int itemKey) -> ItemInfoData (clone of xj)
        SlotUidRva = 0x8DC860,      // rz.ikg(ESlotType, int) -> ulong uid
        SlotObjRva = 0x8DBEC0,      // rz.ikd(ESlotType, int) -> ItemSlot
        SlotCtxRva = 0x8E2700,      // SlotInteractionManager.ink(rm, bool, ulong) -> SlotActionContext
        SlotActionRva = 0x8E2960,   // SlotInteractionManager.inn(SlotActionResult, rm, SlotActionContext)
        StashCacheRva = 0x9AC0A0,   // wh.Stash.kcd(int) -> StashCache
        BoxCountRva   = 0x957EC0,   // wh.uy.jig(EBoxType, EContentType) -> box count
        StashSortAllRva = 0x9AB7E0, // wh.Stash.kcb() -> kcc(page) for every page
        StashNamespace = "wh",
        GameApiVerified = true,
        ItemScanSupported = false,
        HeroStatsVerified = true,
    };

    /// <summary>
    /// Taskbar Hero 1.2.8 (Steam build 25454993, 2026-09-22 16:22). Same layout as 1.2.7 and
    /// mostly the same obfuscated names, but more decoy clones; each RVA below is the copy the
    /// game itself calls (E8 call-site scan). ACTk file offset = RVA - 0xE00.
    /// </summary>
    public static GameBuildProfile V128 { get; } = new()
    {
        VersionLabel = "1.2.8",
        BuildId = 10208,
        ActkTargets =
        [
            ("ObscuredCheatingDetector.Check",       0x73CF60, 0x73C160, [0x41, 0x56, 0x48, 0x83, 0xEC, 0x20]),
            ("ObscuredCheatingDetector.Compare",     0x73D0F0, 0x73C2F0, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("ObscuredCheatingDetector.CompareExt",  0x73D1D0, 0x73C3D0, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("InjectionDetector.Check",              0x73C480, 0x73B680, [0x40, 0x53, 0x48, 0x83, 0xEC, 0x20]),
            ("SpeedHackDetector.Update",             0x741E70, 0x741070, [0x40, 0x56, 0x48, 0x83, 0xEC, 0x70]),
            ("SpeedHackDetector.OnApplicationPause", 0x741DE0, 0x740FE0, [0x48, 0x89, 0x5C, 0x24, 0x08, 0x57]),
        ],
        HpStatic = 0,
        HpOffsets = [],
        AtkStatic = 0,
        AtkOffsets = [],
        StashInsertRva = 0,
        StashSlotRva = 0,
        StashInitRva = 0,
        StashSaveDataCtor = 0,
        ItemAddRva = 0x97C2F0,      // wh.vc.jml (25 call sites; clone mjq has none)
        ItemInfoRva = 0x97D310,     // wh.vc.jmv (identical 260-byte clones: cbu/kdo/hwc/hgc)
        SlotUidRva = 0x8F4000,      // rz.ikg
        SlotObjRva = 0x8F3660,      // rz.ikd
        SlotCtxRva = 0x8FC4C0,      // SlotInteractionManager.ink
        SlotActionRva = 0x8FC720,   // SlotInteractionManager.inn
        StashCacheRva = 0x9C3A40,   // wh.Stash.kcd
        BoxCountRva   = 0x96D650,   // wh.uy.jig
        StashSortAllRva = 0x9C3180, // wh.Stash.kcb() (no direct callers: bound to the Sort button)
        SlotMoveRva   = 0x8EEFD0,   // rz.ije (called by SlotInteractionManager drop; clone fap has none)
        StashNamespace = "wh",
        GameApiVerified = true,
        ItemScanSupported = false,
        HeroStatsVerified = true,
    };

    public static IReadOnlyList<GameBuildProfile> All { get; } = [V128, V127, V126, V124, V1009, V1008];

    public static GameBuildProfile? Detect(GameMemory mem)
    {
        if (!mem.IsAttached || mem.GameAssemblyBase == IntPtr.Zero) return null;
        foreach (var p in All)
        {
            if (!p.IsConfigured) continue;
            if (p.MatchActk(mem)) return p;
        }
        return null;
    }

    /// <summary>
    /// Prefer in-memory signature match; then compare process bytes at RVA to on-disk bytes at DiskOffset.
    /// </summary>
    public static GameBuildProfile? DetectForAttach(GameMemory mem, string? gameAssemblyDllPath)
    {
        var fromMem = Detect(mem);
        if (fromMem != null) return fromMem;

        var path = ResolveDllPath(mem, gameAssemblyDllPath);
        if (path != null)
        {
            foreach (var p in All)
            {
                if (!p.IsConfigured) continue;
                if (p.MatchActkMemoryToDisk(mem, path)) return p;
            }

            var fromDisk = DetectFromDisk(path);
            if (fromDisk != null) return fromDisk;
        }

        return null;
    }

    public static string? ResolveDllPath(GameMemory mem, string? preferredPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
            return preferredPath;
        if (!string.IsNullOrWhiteSpace(mem.GameAssemblyPath) && File.Exists(mem.GameAssemblyPath))
            return mem.GameAssemblyPath;
        return FindInstalledGameAssembly();
    }

    /// <summary>Best-effort lookup of the installed GameAssembly.dll in any Steam library; null if not found.</summary>
    public static string? FindInstalledGameAssembly()
    {
        string[] steamRoots =
        {
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
        };

        // Games can live in any Steam library (e.g. D:\SteamLibrary), listed in libraryfolders.vdf.
        var libraries = new List<string>(steamRoots);
        foreach (var root in steamRoots)
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            try
            {
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch { /* unreadable vdf — keep defaults */ }
        }

        return libraries
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(lib => Path.Combine(lib, "steamapps", "common", "TaskbarHero", "GameAssembly.dll"))
            .FirstOrDefault(File.Exists);
    }

    public static GameBuildProfile? DetectFromDisk(string dllPath)
    {
        if (!File.Exists(dllPath)) return null;
        foreach (var p in All)
        {
            if (!p.IsConfigured) continue;
            if (p.MatchActkFile(dllPath)) return p;
        }
        return null;
    }

    public bool IsConfigured => ActkTargets.Length > 0 && ActkTargets[0].Rva > 0;

    public bool MatchActk(GameMemory mem)
    {
        int matched = 0;
        foreach (var (_, rva, _, sig) in ActkTargets)
        {
            var cur = mem.ReadBytes(mem.GameAssemblyBase + rva, sig.Length);
            if (cur.Length == sig.Length && cur.AsSpan().SequenceEqual(sig))
                matched++;
        }
        // The live ObscuredCheatingDetector.Check prologue can differ from disk; the other
        // five 6-byte prologues at fixed RVAs are enough to identify the build.
        return matched >= ActkTargets.Length - 1;
    }

    /// <summary>Loaded module at RVA must match the same bytes in GameAssembly.dll on disk.</summary>
    public bool MatchActkMemoryToDisk(GameMemory mem, string dllPath)
    {
        if (!mem.IsAttached || mem.GameAssemblyBase == IntPtr.Zero || !File.Exists(dllPath))
            return false;
        // Memory == disk alone proves nothing (any build matches itself); require the
        // profile's signatures on disk first.
        if (!MatchActkFile(dllPath)) return false;

        using var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int matched = 0;
        foreach (var (_, rva, diskOff, sig) in ActkTargets)
        {
            int len = sig.Length;
            long off = diskOff > 0 ? diskOff : rva;
            if (off <= 0 || off + len > fs.Length) return false;

            var disk = new byte[len];
            fs.Seek(off, SeekOrigin.Begin);
            if (fs.Read(disk, 0, len) != len) return false;

            var live = mem.ReadBytes(mem.GameAssemblyBase + rva, len);
            if (live.Length == len && live.AsSpan().SequenceEqual(disk))
                matched++;
        }
        // Allow Obscured Check to differ in RAM vs file; other ACTk RVAs must match.
        return matched >= ActkTargets.Length - 1;
    }

    public byte[] ResolveActkSignature(string dllPath, int rva, int diskOff, byte[] fallback)
    {
        int len = fallback.Length;
        if (len <= 0) return fallback;

        if (!string.IsNullOrEmpty(dllPath) && File.Exists(dllPath))
        {
            long off = diskOff > 0 ? diskOff : rva;
            try
            {
                using var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (off > 0 && off + len <= fs.Length)
                {
                    fs.Seek(off, SeekOrigin.Begin);
                    var buf = new byte[len];
                    if (fs.Read(buf, 0, len) == len) return buf;
                }
            }
            catch { /* use fallback */ }
        }

        return fallback;
    }

    private bool MatchActkFile(string dllPath)
    {
        using var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        foreach (var (_, _, diskOff, sig) in ActkTargets)
        {
            long off = diskOff > 0 ? diskOff : 0;
            if (off <= 0 || off + sig.Length > fs.Length) return false;
            fs.Seek(off, SeekOrigin.Begin);
            var buf = new byte[sig.Length];
            if (fs.Read(buf, 0, buf.Length) != sig.Length) return false;
            for (int i = 0; i < sig.Length; i++)
                if (buf[i] != sig[i]) return false;
        }
        return true;
    }
}
