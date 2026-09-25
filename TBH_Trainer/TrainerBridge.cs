using System.IO.MemoryMappedFiles;

namespace TBH_Trainer;

/// <summary>
/// Shared-memory channel to the injected TBHHook.dll.
/// Layout MUST match SharedState in dllmain.cpp (pack=1, 120 bytes).
/// </summary>
internal sealed class TrainerBridge : IDisposable
{
    private const string MapName = "TBHTrainerShared";
    private const int    MapSize = 100;
    private const int    Magic   = 0x31484254; // "TBH1"

    private const long OffMagic        = 0x00;
    private const long OffHeartbeat    = 0x04;
    private const long OffSpeedEnabled = 0x08;
    private const long OffTimeScale    = 0x0C;
    private const long OffApplyHp      = 0x10;
    private const long OffHpValue      = 0x14;
    private const long OffApplyAtk     = 0x18;
    private const long OffAtkValue     = 0x1C;
    private const long OffStatus       = 0x20;
    private const long OffSpawnRequest = 0x24;
    private const long OffSpawnItemKey = 0x28;
    private const long OffSpawnGrade   = 0x2C;
    private const long OffSpawnResult  = 0x30;
    private const long OffSpawnDetail  = 0x34;
    private const long OffBuildId      = 0x38;
    private const long OffRvaStashJbg  = 0x3C;
    private const long OffRvaStashJbp  = 0x40;
    private const long OffRvaStashJbb  = 0x44;
    private const long OffRvaSaveCtor  = 0x48;
    private const long OffRvaAddItem   = 0x4C;
    private const long OffRvaItemInfo  = 0x50;
    private const long OffRvaSlotUid   = 0x54;
    private const long OffRvaStashCache = 0x58;
    private const long OffRvaBoxCount = 0x5C;
    private const long OffTrainerPaused = 0x60;

    private MemoryMappedFile?         _mmf;
    private MemoryMappedViewAccessor? _view;

    public bool IsConnected => _view != null;

    public bool Connect(int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                _mmf  = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.ReadWrite);
                _view = _mmf.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.ReadWrite);
                if (_view.ReadInt32(OffMagic) == Magic)
                    return true;
                _view.Dispose(); _view = null;
                _mmf.Dispose();  _mmf  = null;
            }
            catch { /* not created yet */ }
            Thread.Sleep(100);
        }
        return false;
    }

    /// <summary>Pushes version-specific stash RVAs for TBHHook native fallbacks.</summary>
    /// <param name="allowGameApi">
    /// False for unknown/unverified builds: stash RVAs are sent as 0 so the hook never
    /// resolves or calls the stash API at stale addresses.
    /// </param>
    public void PublishBuildProfile(GameBuildProfile profile, bool allowGameApi = true)
    {
        if (_view == null) return;
        bool api = allowGameApi && profile.GameApiVerified;
        _view.Write(OffBuildId, profile.BuildId);
        _view.Write(OffRvaStashJbg, api ? profile.StashJbg : 0);
        _view.Write(OffRvaStashJbp, api ? profile.StashJbp : 0);
        _view.Write(OffRvaStashJbb, api ? profile.StashInitRva : 0);
        _view.Write(OffRvaSaveCtor, api ? profile.StashSaveDataCtor : 0);
        _view.Write(OffRvaAddItem,  api ? profile.ItemAddRva : 0);
        _view.Write(OffRvaItemInfo, api ? profile.ItemInfoRva : 0);
        _view.Write(OffRvaSlotUid,   api ? profile.SlotUidRva : 0);
        _view.Write(OffRvaStashCache, api ? profile.StashCacheRva : 0);
        _view.Write(OffRvaBoxCount, api ? profile.BoxCountRva : 0);
        _view.Write(OffTrainerPaused, 0);   // (re)connected: resume
    }

    public int  Heartbeat    => _view?.ReadInt32(OffHeartbeat) ?? -1;
    public int  Status       => _view?.ReadInt32(OffStatus)    ?? 0;
    public bool Il2CppReady  => (Status & 1) != 0;
    public bool TimeResolved => (Status & 2) != 0;
    public bool StashResolved => (Status & 4) != 0;

    public bool WaitForStash(int timeoutMs = 8000)
    {
        if (_view == null) return false;
        if (StashResolved) return true;
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (StashResolved) return true;
            Thread.Sleep(100);
        }
        return StashResolved;
    }

    /// <summary>Disconnect: the hook restores normal speed and stops all game calls until the next Connect.</summary>
    public void Pause()
    {
        if (_view == null) return;
        _view.Write(OffSpeedEnabled, 0);
        _view.Write(OffTrainerPaused, 1);
    }

    public void SetSpeed(bool enabled, float timeScale)
    {
        if (_view == null) return;
        _view.Write(OffTimeScale,    timeScale);
        _view.Write(OffSpeedEnabled, enabled ? 1 : 0);
    }

    /// <summary>Matches TaskbarHero.Data.EGradeType (1.00.09 Il2Cpp dump order).</summary>
    public enum GradeType
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Legendary = 3,
        Immortal = 4,
        Arcana = 5,
        Beyond = 6,
        Celestial = 7,
        Divine = 8,
        Cosmic = 9,
        None = 10,
    }

    public static string GradeLabel(GradeType grade) =>
        $"{grade} ({(int)grade})";

    public static bool TryGradeFromInt(int value, out GradeType grade)
    {
        if (Enum.IsDefined(typeof(GradeType), value))
        {
            grade = (GradeType)value;
            return true;
        }
        grade = GradeType.Common;
        return false;
    }

    public bool SpawnItem(int itemKey, GradeType grade = GradeType.Legendary, int timeoutMs = 5000)
    {
        if (_view == null) return false;
        if (!WaitForStash(Math.Min(timeoutMs, 8000)))
            return false;

        _view.Write(OffSpawnResult, 0);
        _view.Write(OffSpawnItemKey, itemKey);
        _view.Write(OffSpawnGrade, (int)grade);
        Thread.Sleep(1);

        int prev = _view.ReadInt32(OffSpawnRequest);
        _view.Write(OffSpawnRequest, prev + 1);

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            int result = _view.ReadInt32(OffSpawnResult);
            if (result == 1) return true;
            if (result < 0) return false;
            Thread.Sleep(20);
        }
        return false;
    }

    public int LastSpawnErrorCode => _view?.ReadInt32(OffSpawnResult) ?? 0;

    /// <summary>On success: remapped ItemKey if &gt; 1000; on failure: negative error code in spawnResult.</summary>
    public int LastSpawnDetail => _view?.ReadInt32(OffSpawnDetail) ?? 0;

    public static string DescribeSpawnError(int code) => code switch
    {
        -2 => "Invalid ItemKey — not in game data (try an ID from ItemInfoData / an item you own).",
        -3 => "Could not create item instance (isi/ges failed).",
        -4 => "No empty stash slot.",
        -5 => "Stash insert (jbo/jbn) failed.",
        -6 => "Game rejected add item (isk/blk AddItemResult).",
        int c when c <= -100 && c > -200 =>
            $"Game AddItemResult code {-100 - c} (inventory full? wait 1s between spawns).",
        -7 => "Grade not available for that ItemKey — pick an ID from the catalog with that rarity.",
        -8 => "Spawn busy — wait for the previous spawn to finish (do not spam click).",
        -1 => "Spawn API failed.",
        _  => $"Spawn error code {code}."
    };

    public int SpawnItems(int itemKey, GradeType grade, int count, int delayBetweenMs = 400)
    {
        int success = 0;
        for (int i = 0; i < count; i++)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (SpawnItem(itemKey, grade, 6000))
                {
                    success++;
                    break;
                }
                if (LastSpawnErrorCode != -8)
                    break;
                Thread.Sleep(200);
            }
            if (i < count - 1)
                Thread.Sleep(delayBetweenMs);
        }
        return success;
    }

    public bool WaitHeartbeat(int waitMs = 500)
    {
        if (_view == null) return false;
        int start    = Heartbeat;
        var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);
        while (DateTime.UtcNow < deadline)
        {
            if (Heartbeat != start) return true;
            Thread.Sleep(30);
        }
        return false;
    }

    public void Dispose()
    {
        _view?.Dispose();
        _mmf?.Dispose();
        _view = null;
        _mmf  = null;
    }
}
