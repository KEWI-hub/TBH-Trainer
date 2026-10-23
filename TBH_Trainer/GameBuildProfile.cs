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

    /// <summary>static Stash.kcc(int) -> StashCache (per-slot unlock state; stash size differs per player).</summary>
    public int StashCacheRva { get; init; }

    /// <summary>static int wh.uy.jif(EBoxType, EContentType) -> unopened stage boxes (auto open boxes).</summary>
    public int BoxCountRva { get; init; }

    /// <summary>rz.ije(MoveRequest, Action&lt;MoveResult&gt;): drag &amp; drop between slots (bag -> stash). 0 = n/a.</summary>
    public int SlotMoveRva { get; init; }

    /// <summary>bool Monster.gwm(Unit killer) - the game's own monster death path.</summary>
    public int MonsterDieRva { get; init; }
    /// <summary>pl.gxf(float damage, Unit attacker) - the health controller's damage entry,
    /// which runs the game's whole kill path including the loot roll.</summary>
    public int MonsterDamageRva { get; init; }
    /// <summary>Max-HP field on the health controller: 0x4C up to 1.2.8, 0x50 from 1.3.0
    /// (the class gained a float). 0 means the hook keeps its own default.</summary>
    public int MaxHpOffset { get; init; }

    /// <summary>int AccountStatus.llw(EAccountStatus) - reads one account upgrade value.</summary>
    public int AccStatusGetRva { get; init; }

    /// <summary>void AccountStatus.llx(EAccountStatus, int) - writes one account upgrade value.</summary>
    public int AccStatusSetRva { get; init; }

    /// <summary>
    /// static bool xo.wb.kbv() - true when the Material Stash DLC is owned. The material stash
    /// is the stackable-materials tab (ores, extracts) added as paid DLC; without it the game
    /// refuses every move into ESlotType.MATERIALSTASH (EAddMaterialStashResult.NoDlc).
    /// 0 = not derived for this build, and the hook then leaves the material stash alone.
    /// </summary>
    public int MatStashDlcRva { get; init; }

    /// <summary>
    /// void pp.hby(float damage, Unit attacker) - the hero health controller's damage entry.
    /// God mode detours this so a hero never takes the hit, instead of writing HP back every
    /// frame and losing the race whenever the game resets the heroes mid-update.
    /// Monsters override it in pl at a different address, so one-hit kill is unaffected.
    /// </summary>
    public int HeroDamageRva { get; init; }

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
        StashCacheRva = 0x9AC0A0,   // wh.Stash.kcd(int) -> StashCache
        BoxCountRva   = 0x957EC0,   // wh.uy.jig(EBoxType, EContentType) -> box count
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
        StashCacheRva = 0x9C3A40,   // wh.Stash.kcd
        BoxCountRva   = 0x96D650,   // wh.uy.jig(EBoxType, EContentType) -> box count
        MonsterDieRva = 0xD079C0,   // Monster.gwm(Unit) -> finishes a monster the normal way
        MonsterDamageRva = 0xD10D00, // pl.gxf(float, Unit) -> damages it as a hero would
        MaxHpOffset = 0x4C,
        SlotMoveRva   = 0x8EEFD0,   // rz.ije (called by SlotInteractionManager drop; clone fap has none)
        AccStatusGetRva = 0xA8F440, // AccountStatus.llw(EAccountStatus) -> int
        AccStatusSetRva = 0xA8F4D0, // AccountStatus.llx(EAccountStatus, int)
        StashNamespace = "wh",
        GameApiVerified = true,
        ItemScanSupported = false,
        HeroStatsVerified = true,
    };

    /// <summary>
    /// Taskbar Hero 1.3.0 (Steam build 26124855, 2026-10-22). The ACTk detectors moved and the
    /// generic layout scan no longer finds them: the three ObscuredCheatingDetector methods are
    /// still 0x190 and 0x270 apart, but InjectionDetector sits at -0xA00 instead of -0xAE0 and
    /// SpeedHackDetector jumped from +0x4F10 to +0x103C0, so verifying the whole cluster fails.
    ///
    /// Each RVA below was matched by method shape against the same class in 1.2.8 and then
    /// confirmed by its prologue bytes. ACTk file offset = RVA - 0xE00, same as before.
    ///
    /// Unit's field layout is unchanged from 1.2.8 (0x104 attack damage through 0x2A0 elemental
    /// block, resistances still in the dictionary at 0x260), so the hero features are on. The
    /// obfuscated game-API RVAs have not been re-derived yet, so everything that needs one -
    /// item spawn, stash, boxes, account upgrades, the stranded-monster handler - stays off.
    /// </summary>
    public static GameBuildProfile V130 { get; } = new()
    {
        VersionLabel = "1.3.0",
        BuildId = 10300,
        ActkTargets =
        [
            ("ObscuredCheatingDetector.Check",       0x7421A0, 0x7413A0, [0x41, 0x56, 0x48, 0x83, 0xEC, 0x20]),
            ("ObscuredCheatingDetector.Compare",     0x742330, 0x741530, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("ObscuredCheatingDetector.CompareExt",  0x742410, 0x741610, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("InjectionDetector.Check",              0x7417A0, 0x7409A0, [0x40, 0x53, 0x48, 0x83, 0xEC, 0x20]),
            ("SpeedHackDetector.Update",             0x752560, 0x751760, [0x40, 0x56, 0x48, 0x83, 0xEC, 0x70]),
            ("SpeedHackDetector.OnApplicationPause", 0x7524D0, 0x7516D0, [0x48, 0x89, 0x5C, 0x24, 0x08, 0x57]),
        ],
        HpStatic = 0,
        HpOffsets = [],
        AtkStatic = 0,
        AtkOffsets = [],
        StashInsertRva = 0,
        StashSlotRva = 0,
        StashInitRva = 0,
        StashSaveDataCtor = 0,
        // Picked out of the decoy clones by counting E8 call sites in GameAssembly.dll; the
        // count is in each comment. A clone the game never calls gets none.
        ItemAddRva = 0x9C0870,       // xo.vu.jzd (23 calls; two clones have none)
        ItemInfoRva = 0x9C1F00,      // xo.vu.jzo - one of five identical clones, none called
                                     // directly; picked as jzd's sibling the way 1.2.8 paired
                                     // jml with jmv
        SlotUidRva = 0x916C30,       // sj.iuh (28 calls; three clones have none)
        StashCacheRva = 0xA6E130,    // xo.Stash.lae (35 calls)
        BoxCountRva = 0x9B4610,      // xo.vq.jux (71 calls). The 40-call sibling only forwards
                                     // to bcr.mre, which switches on box type and returns 5 -
                                     // that is the cap, not how many are held
        MonsterDieRva = 0xE02BC0,    // Monster, the only override bool (Unit) in the class
        MonsterDamageRva = 0xE0E340, // pl, the only override void (float, Unit) in the class
        MaxHpOffset = 0x50,          // 1.3.0 inserted a float before it
        SlotMoveRva = 0x910FF0,      // sj.itb (2 calls; the other six have none)
        AccStatusGetRva = 0xB1E500,  // AccountStatus.mnm (129 calls)
        AccStatusSetRva = 0xB1E590,  // AccountStatus.mnn (5 calls; the clone has none)
        StashNamespace = "xo",       // was "wh" up to 1.2.8
        GameApiVerified = true,
        ItemScanSupported = false,
        HeroStatsVerified = true,
    };

    /// <summary>
    /// Taskbar Hero 1.3.1 (Steam build 26151704, 2026-10-23). A small patch: the class table is
    /// the same size and no field layout moved, but every RVA shifted and the obfuscator
    /// re-rolled names again. The RVA -> file offset delta is 0x1200 on this build, not the
    /// 0xE00 every build up to 1.3.0 used, so it is read from the PE section table.
    ///
    /// Most targets were found by matching the declaring class and the method's own shape
    /// (modifiers, return type, parameter types) against 1.3.0 - names are no help. 1.3.1 also
    /// renamed most obfuscated methods by shifting the last letter +4 (bblq -> bblu, lae -> lai,
    /// iuh -> iul, ...; 10,064 methods follow it), which independently confirms several picks.
    /// Where several clones share a shape, the call-site count decides: that is noted inline.
    ///
    /// UnitHealthController (pp) still has max HP at +0x50 and current HP at +0x40, and Unit's
    /// 94 fields are byte-for-byte identical to 1.3.0 including the resistance dictionary at
    /// 0x260 - so the hero features carry over untouched.
    /// </summary>
    public static GameBuildProfile V131 { get; } = new()
    {
        VersionLabel = "1.3.1",
        BuildId = 10301,
        // All six prologues verified against the bytes at RVA - 0x1200.
        ActkTargets =
        [
            ("ObscuredCheatingDetector.Check",       0x73D150, 0x73BF50, [0x41, 0x56, 0x48, 0x83, 0xEC, 0x20]),
            ("ObscuredCheatingDetector.Compare",     0x73D2E0, 0x73C0E0, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("ObscuredCheatingDetector.CompareExt",  0x73D3C0, 0x73C1C0, [0x48, 0x89, 0x5C, 0x24, 0x10, 0x48]),
            ("InjectionDetector.Check",              0x73C670, 0x73B470, [0x40, 0x53, 0x48, 0x83, 0xEC, 0x20]),
            ("SpeedHackDetector.Update",             0x742060, 0x740E60, [0x40, 0x56, 0x48, 0x83, 0xEC, 0x70]),
            ("SpeedHackDetector.OnApplicationPause", 0x741FD0, 0x740DD0, [0x48, 0x89, 0x5C, 0x24, 0x08, 0x57]),
        ],
        HpStatic = 0,
        HpOffsets = [],
        AtkStatic = 0,
        AtkOffsets = [],
        StashInsertRva = 0,
        StashSlotRva = 0,
        StashInitRva = 0,
        StashSaveDataCtor = 0,
        ItemAddRva = 0x9B2890,       // xo.vu.jzh (20 calls) - also jzd+4, the 1.3.0 name shifted
        ItemInfoRva = 0x9B3F20,      // xo.vu.jzs - jzo+4; unused since item spawn was removed
        SlotUidRva = 0x906CC0,       // sj.iul (30 calls) - the only method of its shape in sj
        StashCacheRva = 0xA5C6D0,    // xo.Stash.lai (36 calls)
        BoxCountRva = 0x9A1980,      // xo.vq.jvb (63 calls). xo.vq was renamed wholesale, so the
                                     // name shift is no help here: the 44-call sibling is 69
                                     // bytes to its first ret and makes one call - the same
                                     // short forwarder shape that was the decoy on 1.3.0
        MonsterDieRva = 0xE1BE60,    // Monster.hbd, still the only override bool (Unit)
        MonsterDamageRva = 0xE277C0, // pl.hby, still the only override void (float, Unit)
        MaxHpOffset = 0x50,          // class pp layout is unchanged from 1.3.0
        SlotMoveRva = 0x901080,      // sj.itf (2 calls) - itb+4, the only method of its shape
        AccStatusGetRva = 0xB32F00,  // AccountStatus.mnq (128 calls)
        AccStatusSetRva = 0xB32F90,  // AccountStatus.mnr (8 calls)
        MatStashDlcRva = 0x9CE130,   // xo.wb.kbv (19 calls; the other bool of its shape has none)
        HeroDamageRva = 0xE287D0,    // pp.hby - prologue verified before it is patched
        StashNamespace = "xo",
        GameApiVerified = true,
        ItemScanSupported = false,
        HeroStatsVerified = true,
    };

    public static IReadOnlyList<GameBuildProfile> All { get; } = [V131, V130, V128, V127, V126, V124, V1009, V1008];

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

            // Unknown build: find the detectors by their layout so a game patch does not
            // have to wait for a trainer release. Only the ACTk bypass and the speed hack
            // are enabled — everything that needs verified il2cpp offsets stays off.
            var scan = ActkScanner.Scan(path);
            LastScanDetail = scan.Detail;
            if (scan.Ok) return FromActkScan(scan.Targets!);
        }

        return null;
    }

    /// <summary>Why the last ACTk layout scan succeeded or failed (for the trainer log).</summary>
    public static string? LastScanDetail { get; private set; }

    /// <summary>True for a profile built by scanning instead of one shipped with the trainer.</summary>
    public bool FromScan { get; init; }

    /// <summary>
    /// Minimal profile for a build the trainer does not know: the detector RVAs come from
    /// the scan, and every feature that depends on offsets we could not verify is off.
    /// </summary>
    public static GameBuildProfile FromActkScan((string Name, int Rva, int DiskOffset, byte[] Signature)[] targets) => new()
    {
        VersionLabel = "unknown build (detectors located by scan)",
        BuildId = 0,
        ActkTargets = targets,
        HpStatic = 0,
        HpOffsets = [],
        AtkStatic = 0,
        AtkOffsets = [],
        StashInsertRva = 0,
        StashSlotRva = 0,
        StashInitRva = 0,
        StashSaveDataCtor = 0,
        ItemAddRva = 0,
        StashNamespace = "wh",
        GameApiVerified = false,
        ItemScanSupported = false,
        HeroStatsVerified = false,
        FromScan = true,
    };

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
