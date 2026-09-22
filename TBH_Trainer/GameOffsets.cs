namespace TBH_Trainer;

/// <summary>
/// Reference offsets for 1.00.09. Runtime code uses <see cref="GameBuildProfile"/> per patch.
/// </summary>
internal static class GameOffsets
{
    // === ACTk Detector Methods (1.00.09 verified) ===
    public const int ObscuredCheatingDetector_Check      = 0x6C1840;
    public const int ObscuredCheatingDetector_Compare    = 0x6C19D0;
    public const int ObscuredCheatingDetector_CompareExt = 0x6C1AB0;
    public const int InjectionDetector_Check             = 0x6C0D60;
    public const int SpeedHackDetector_Update            = 0x6C6750;
    public const int SpeedHackDetector_OnAppPause        = 0x6C66C0;
    public const int SpeedHackDetector_Compare           = 0x6C7D50;
    public const int DetectorBase_Start                  = 0x42DF20;

    // === ObscuredType Encrypt/Decrypt (1.00.09) ===
    public const int ObscuredInt_Encrypt     = 0x6B1800;
    public const int ObscuredInt_Decrypt     = 0x6B1810;
    public const int ObscuredInt_GenerateKey = 0x6A4A70;
    public const int ObscuredFloat_Encrypt   = 0x6A0B30;
    public const int ObscuredFloat_Decrypt   = 0x6A0B60;
    public const int ObscuredLong_Encrypt    = 0x6AA5E0;
    public const int ObscuredLong_Decrypt    = 0x6AA5F0;

    // === Unit Class Field Offsets ===
    public const int Unit_b_isLive    = 0x00D8;
    public const int Unit_b_isHero    = 0x0100;
    public const int Unit_Stat0       = 0x0104;   // ObscuredFloat bcfr (ATK)
    public const int Unit_Stat1       = 0x0118;
    public const int Unit_Stat2       = 0x012C;
    public const int Unit_Stat3       = 0x0140;
    public const int Unit_Stat4       = 0x0154;
    public const int Unit_Stat5       = 0x0168;
    public const int Unit_Stat6       = 0x017C;
    public const int Unit_Stat7       = 0x0190;
    public const int Unit_Stat8       = 0x01A4;
    public const int Unit_Stat9       = 0x01B8;
    public const int Unit_Stat10      = 0x01CC;
    public const int Unit_Stat11      = 0x01E0;
    public const int Unit_IntStat0    = 0x01F4;
    public const int Unit_IntStat1    = 0x0204;
    public const int Unit_FloatStat12 = 0x0214;
    public const int Unit_IntStat2    = 0x0228;

    // === UnitHealthController (pf) Field Offsets ===
    public const int Health_Unit      = 0x0030;
    public const int Health_CurrentHP = 0x0038;
    public const int Health_MaxHP     = 0x003C;

    // === Currency (sv) Field Offsets ===
    public const int Currency_Amount   = 0x0028;
    public const int Currency_IntField = 0x0048;

    // === CurrencySaveData Field Offsets ===
    public const int CurrSave_Key      = 0x0010;
    public const int CurrSave_Quantity = 0x0018;

    // === HeroSaveData Field Offsets ===
    public const int HeroSave_Key       = 0x0010;
    public const int HeroSave_Level     = 0x0014;
    public const int HeroSave_IsUnlock  = 0x0018;
    public const int HeroSave_Exp       = 0x001C;
    public const int HeroSave_AbilityPt = 0x0020;

    // === Key RVA Methods (1.00.09) ===
    public const int Currency_GetInstance = 0x893BE0;
    public const int Currency_Add         = 0x894220;
    public const int Currency_Get         = 0x8941D0;
    public const int Unit_ApplyDamage     = 0xB6EA50;
    public const int Health_SetHP         = 0xB7FBF0;
    public const int Health_Heal          = 0xB7FF10;
}
