// TBHHook.dll - injected into TaskBarHero.exe (Il2Cpp).
// Phase 1: game speed control via UnityEngine.Time::set_timeScale.
// Phase 2: Item spawn via ud.Stash.jbg + StashCache construction.
// Communicates with the C# trainer through a named shared-memory block.

#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cstdarg>

// GameAssembly.dll RVAs — 1.00.08 defaults; trainer overwrites via shared memory for 1.00.09+
static constexpr uintptr_t kDefaultRvaStashJbg          = 0x8F0320;
static constexpr uintptr_t kDefaultRvaStashJbp          = 0x8F15D0;
static constexpr uintptr_t kDefaultRvaStashJbb          = 0x8EF8C0;
static constexpr uintptr_t kDefaultRvaStashSaveDataCtor = 0x99C9F0;
static constexpr int32_t   kStashMaxSlots        = 49;       // ud.Stash.becx

// ---- Shared state: MUST match TrainerBridge.cs byte-for-byte (56 bytes, pack=1) ----
#pragma pack(push, 1)
struct SharedState
{
    int32_t magic;         // 0x00  'TBH1' = 0x31484254
    int32_t heartbeat;     // 0x04
    int32_t speedEnabled;  // 0x08
    float   timeScale;     // 0x0C
    int32_t applyHp;       // 0x10
    float   hpValue;       // 0x14
    int32_t applyAtk;      // 0x18
    float   atkValue;      // 0x1C
    int32_t status;        // 0x20  bit0=il2cpp, bit1=Time, bit2=Stash
    int32_t spawnRequest;  // 0x24
    int32_t spawnItemKey;  // 0x28
    int32_t spawnGrade;    // 0x2C
    int32_t spawnResult;   // 0x30  1=ok -1=fail -2=bad key -3=create fail -4=stash full
    int32_t spawnDetail;   // 0x34  last failing step (debug)
    int32_t buildId;       // 0x38  10008 / 10009 / 10204 (0 = not published yet)
    int32_t rvaStashJbg;   // 0x3C  from trainer (0 = use default)
    int32_t rvaStashJbp;   // 0x40
    int32_t rvaStashJbb;   // 0x44
    int32_t rvaSaveCtor;   // 0x48
    int32_t rvaAddItem;    // 0x4C  ue.ti.blk (1.00.09) / we.va.jiv (1.2.4)
    int32_t rvaItemInfo;   // 0x50  static ItemInfoData lookup(int itemKey), e.g. we.va.jjc (1.2.4)
    int32_t rvaSlotUid;    // 0x54  rz.ikf(ESlotType, int) -> item uid in that slot (1.2.6)
    int32_t rvaSlotObj;    // 0x58  rz.ikc(ESlotType, int) -> ItemSlot UI object
    int32_t rvaSlotCtx;    // 0x5C  SlotInteractionManager.inj(rm, bool, ulong) -> SlotActionContext
    int32_t rvaSlotAction; // 0x60  SlotInteractionManager.inm(SlotActionResult, rm, SlotActionContext)
    int32_t rvaStashCache; // 0x64  static Stash.kcc(int index) -> StashCache (unlock state per slot)
    int32_t rvaBoxCount;   // 0x68  static int wh.uy.jif(EBoxType, EContentType) -> unopened boxes
    int32_t trainerPaused; // 0x6C  1 = trainer disconnected: stop every call into the game
    int32_t rvaStashSortAll; // 0x70  static wh.Stash.kcb() -> sorts every stash page (Sort button)
    int32_t rvaSlotMove;   // 0x74  rz.ije(MoveRequest, Action<MoveResult>) -> player drag & drop
};
#pragma pack(pop)

static_assert(sizeof(SharedState) == 120, "SharedState size mismatch");

static const wchar_t* kMapName = L"TBHTrainerShared";
static const int32_t  kMagic   = 0x31484254;
static const int      kMapSize = 120;

// Item scan report (trainer ItemScanBridge.cs)
#pragma pack(push, 1)
struct ScanState
{
    int32_t magic;    // 0x53424854 'TBHS'
    int32_t request;  // odd=catalog dump, even=stash scan
    int32_t done;     // 0=pending 1=ok -1=fail 2=truncated
    int32_t length;
    char    text[524288 - 16];
};
#pragma pack(pop)
static_assert(sizeof(ScanState) == 524288, "ScanState size mismatch");

// Dedicated hero scan/control state.
#pragma pack(push, 1)
struct HeroScanState
{
    int32_t magic;
    int32_t request;
    int32_t done;
    int32_t length;
    int32_t command;   // 0=scan, 1=current HP, 2=max HP, 3=attack damage, 4=attack speed, 5=both HP,
                       // 6=clear locks, 7=layout dump, 8=crit chance, 9=crit damage, 10=cooldown reduction,
                       // 11=armor, 12=one-hit kill toggle, 13=god mode toggle
    int32_t heroIndex;
    float   value;
    int32_t reserved;
    char    text[524288 - 32];
};
#pragma pack(pop)
static_assert(sizeof(HeroScanState) == 524288, "HeroScanState size mismatch");

static const wchar_t* kScanMapName = L"TBHTrainerScan";
static const int32_t  kScanMagic   = 0x53424854;
static const int      kScanMapSize = 524288;

static ScanState* g_scan           = nullptr;
static int32_t    g_lastScanReq    = 0;
static const wchar_t* kHeroScanMapName = L"TBHTrainerHeroes";
static const int32_t  kHeroScanMagic   = 0x48524854;
static HeroScanState* g_heroScan       = nullptr;
static int32_t    g_lastHeroScanReq    = 0;
static volatile LONG g_spawnBusy   = 0;
static int32_t    g_remapInKey     = 0;
static int32_t    g_remapInGrade   = -1;
static int32_t    g_remapOutKey    = 0;

// ---- il2cpp exports ----
typedef void* (*il2cpp_domain_get_t)();
typedef void* (*il2cpp_thread_attach_t)(void* domain);
typedef void  (*il2cpp_thread_detach_t)(void* thread);
typedef void** (*il2cpp_domain_get_assemblies_t)(void* domain, size_t* size);
typedef void* (*il2cpp_assembly_get_image_t)(void* assembly);
typedef void* (*il2cpp_class_from_name_t)(void* image, const char* ns, const char* name);
typedef void* (*il2cpp_class_get_method_from_name_t)(void* klass, const char* name, int argsCount);
typedef void* (*il2cpp_runtime_invoke_t)(void* method, void* obj, void** params, void** exc);
typedef void* (*il2cpp_object_new_t)(void* klass);
typedef void* (*il2cpp_class_get_field_from_name_t)(void* klass, const char* name);
typedef void  (*il2cpp_field_get_value_t)(void* obj, void* field, void* value);
typedef void  (*il2cpp_field_set_value_t)(void* obj, void* field, void* value);
typedef void  (*il2cpp_field_static_get_value_t)(void* field, void* value);
typedef size_t (*il2cpp_image_get_class_count_t)(void* image);
typedef void* (*il2cpp_image_get_class_t)(void* image, size_t index);
typedef const char* (*il2cpp_class_get_name_t)(void* klass);
typedef const char* (*il2cpp_class_get_namespace_t)(void* klass);
typedef void* (*il2cpp_class_get_methods_t)(void* klass, void** iter);
typedef const char* (*il2cpp_method_get_name_t)(void* method);
typedef uint8_t (*il2cpp_method_get_param_count_t)(void* method);
typedef void* (*il2cpp_method_get_pointer_t)(void* method);
typedef void* (*il2cpp_class_get_fields_t)(void* klass, void** iter);
typedef const char* (*il2cpp_field_get_name_t)(void* field);
typedef int32_t (*il2cpp_field_get_flags_t)(void* field);
typedef void* (*il2cpp_method_get_param_t)(void* method, uint32_t index);
typedef void* (*il2cpp_class_from_type_t)(const void* type);
typedef void* (*il2cpp_object_get_class_t)(void* obj);
typedef void* (*il2cpp_class_get_parent_t)(void* klass);
typedef uint32_t (*il2cpp_array_length_t)(void* array);
typedef void* (*il2cpp_array_get_t)(void* array, size_t elementSize, size_t index);
typedef size_t (*il2cpp_field_get_offset_t)(void* field);
typedef const void* (*il2cpp_field_get_type_t)(void* field);
typedef char* (*il2cpp_type_get_name_t)(const void* type);
typedef void (*il2cpp_free_t)(void* ptr);
typedef const void* (*il2cpp_class_get_type_t)(void* klass);
typedef void* (*il2cpp_type_get_object_t)(const void* type);

// Used by the read-only hero layout dump (metadata only, never dereferences objects).
static il2cpp_field_get_offset_t il2cpp_field_get_offset;
static il2cpp_field_get_type_t   il2cpp_field_get_type;
static il2cpp_type_get_name_t    il2cpp_type_get_name;
static il2cpp_free_t             il2cpp_free;
static il2cpp_class_get_type_t   il2cpp_class_get_type;
static il2cpp_type_get_object_t  il2cpp_type_get_object;

static il2cpp_domain_get_t                 il2cpp_domain_get;
static il2cpp_thread_attach_t              il2cpp_thread_attach;
static il2cpp_thread_detach_t              il2cpp_thread_detach;
static il2cpp_domain_get_assemblies_t      il2cpp_domain_get_assemblies;
static il2cpp_assembly_get_image_t         il2cpp_assembly_get_image;
static il2cpp_class_from_name_t            il2cpp_class_from_name;
static il2cpp_class_get_method_from_name_t il2cpp_class_get_method_from_name;
static il2cpp_runtime_invoke_t             il2cpp_runtime_invoke;
static il2cpp_object_new_t                 il2cpp_object_new;
static il2cpp_class_get_field_from_name_t  il2cpp_class_get_field_from_name;
static il2cpp_field_get_value_t            il2cpp_field_get_value;
static il2cpp_field_set_value_t            il2cpp_field_set_value;
static il2cpp_field_static_get_value_t     il2cpp_field_static_get_value;
static il2cpp_image_get_class_count_t      il2cpp_image_get_class_count;
static il2cpp_image_get_class_t            il2cpp_image_get_class;
static il2cpp_class_get_name_t             il2cpp_class_get_name;
static il2cpp_class_get_namespace_t        il2cpp_class_get_namespace;
static il2cpp_class_get_methods_t          il2cpp_class_get_methods;
static il2cpp_method_get_name_t            il2cpp_method_get_name;
static il2cpp_method_get_param_count_t     il2cpp_method_get_param_count;
static il2cpp_method_get_pointer_t         il2cpp_method_get_pointer;
static il2cpp_class_get_fields_t           il2cpp_class_get_fields;
static il2cpp_field_get_name_t             il2cpp_field_get_name;
static il2cpp_field_get_flags_t            il2cpp_field_get_flags;
static il2cpp_method_get_param_t           il2cpp_method_get_param;
static il2cpp_class_from_type_t            il2cpp_class_from_type;
static il2cpp_object_get_class_t           il2cpp_object_get_class;
static il2cpp_class_get_parent_t           il2cpp_class_get_parent;

static SharedState* g_shared            = nullptr;
static void*        g_domain            = nullptr;
static int32_t      g_resolveCounter    = 0;
static HMODULE      g_gameAssembly      = nullptr;

static void*        g_timeClass         = nullptr;
static void*        g_setTimeScale      = nullptr;

static void*        g_stashClass        = nullptr;
static void*        g_stashCacheClass   = nullptr;
static void*        g_stashSaveClass    = nullptr;
static void*        g_stashInstance     = nullptr;
static void*        g_jbg               = nullptr;
static void*        g_jbp               = nullptr;
static void*        g_jbi               = nullptr;
static void*        g_tiClass           = nullptr;
static void*        g_blkMethod         = nullptr;
static void*        g_isrMethod         = nullptr;
static void*        g_hyyMethod         = nullptr;
static void*        g_isiMethod         = nullptr;
static void*        g_gesMethod         = nullptr;
static void*        g_iskMethod         = nullptr;
static void*        g_ispMethod         = nullptr;
static void*        g_ishMethod         = nullptr;
static void*        g_opdMethod         = nullptr;
static void*        g_iryMethod         = nullptr;
static void*        g_islMethod         = nullptr;
static void*        g_esxMethod         = nullptr;
static void*        g_ysClass           = nullptr;
static void*        g_ysInstance        = nullptr;
static void*        g_lqkMethod         = nullptr;
static void*        g_playerSaveClass   = nullptr;
static void*        g_itemSaveClass     = nullptr;
static void*        g_tfClass           = nullptr;
static void*        g_itemInfoClass     = nullptr;
static il2cpp_array_length_t   il2cpp_array_length;
static il2cpp_array_get_t      il2cpp_array_get;
static void*        g_jboMethod         = nullptr;
static void*        g_jcdMethod         = nullptr;
static void*        g_yqClass            = nullptr;
static void*        g_yqInstance        = nullptr;
static void*        g_looMethod         = nullptr;
static void*        g_lopMethod         = nullptr;
static void*        g_addItemResultClass = nullptr;
static int32_t      g_lastSpawnDetail   = 0;
static void*        g_stashSaveCtor     = nullptr;
static int32_t      g_lastSpawnReq      = 0;
static uint64_t     g_nextUniqueId      = 0xDEAD000000000001ULL;
static uintptr_t    g_rvaStashJbg       = kDefaultRvaStashJbg;
static uintptr_t    g_rvaStashJbp       = kDefaultRvaStashJbp;
static uintptr_t    g_rvaStashJbb       = kDefaultRvaStashJbb;
static uintptr_t    g_rvaSaveCtor       = kDefaultRvaStashSaveDataCtor;
static uintptr_t    g_rvaAddItem        = 0;
static uintptr_t    g_rvaItemInfo       = 0;
static void*        g_itemApiClass      = nullptr; // class of the add-item method (wh.vc on 1.2.6)
static int32_t      g_blkArgCount       = 3;   // 3 = blk(int, ulong, bool); 5 = jiv(int, ulong, source, int, bool)
static int32_t      g_buildId           = 0;

// Native fallbacks (il2cpp x64: RCX=this, then args)
typedef void* (__fastcall *NativeJbp_t)(void* self, int32_t index);
typedef void  (__fastcall *NativeJbg_t)(void* self, uint64_t uid, void* cache);
typedef void  (__fastcall *NativeJbb_t)(void* self);

static NativeJbp_t g_nativeJbp = nullptr;
static NativeJbg_t g_nativeJbg = nullptr;
static NativeJbb_t g_nativeJbb = nullptr;
static void* g_obscuredFloatEncryptMethod = nullptr;

struct HeroLocks
{
    bool currentHp;
    bool maxHp;
    bool attackDamage;
    bool attackSpeed;
    float currentHpValue;
    float maxHpValue;
    float attackDamageValue;
    float attackSpeedValue;
    bool  extra[4];        // Crit Chance, Crit Damage, Cooldown Reduction, Armor
    float extraValue[4];
};
static HeroLocks g_heroLocks[3] = {};

// Shutdown safety: once the game starts closing, the worker thread must stop calling into
// il2cpp — Unity tears its runtime down underneath us and the next call crashes the game.
static volatile bool      g_shuttingDown   = false;

// Extra ObscuredFloat stat locks on Unit (commands 8-11). Offsets match the 1.00.09
// dump and the 1.2.4 hero layout dump (Unit.bdxp / bdxq / bdxr / bdxn).
static const int    kExtraStatCount = 4;
static const size_t kExtraStatOffsets[kExtraStatCount] = { 0x168, 0x17C, 0x190, 0x140 };
static const char*  kExtraStatNames[kExtraStatCount]   = { "Critical Chance", "Critical Damage", "Cooldown Reduction", "Armor" };

// Combat toggles (commands 12 / 13, value != 0 = on).
static volatile bool g_oneHitKill = false; // keep every live monster at 1 HP
static volatile bool g_godMode    = false; // refill hero HP every ~33ms instead of ~0.2s

// -------------------------------------------------------------------------

static void* ResolveStashInstance();
static void* ResolveYqInstance();
static void* TryStaticField(void* klass, const char* name);
static int32_t ListSize(void* list);
static void* ReadListItem(void* list, int32_t index);
static size_t ScanAppend(char* buf, size_t cap, size_t pos, const char* fmt, ...);
static void RunHeroScan();
static const char* GradeTypeName(int32_t grade);
static int32_t ReadGradeFromItemKey(int32_t itemKey);
static int32_t ReadGradeFromInfo(void* info);
static int32_t GetCatalogGradeForKey(int32_t itemKey);
static bool ItemKeyExists(int32_t itemKey);
static void* CreateItemInstance(void* itemInfo, uint64_t uid);

template <typename T>
static T Resolve(HMODULE m, const char* name)
{
    return reinterpret_cast<T>(GetProcAddress(m, name));
}

static uintptr_t GameAssemblyBase()
{
    if (!g_gameAssembly) return 0;
    return reinterpret_cast<uintptr_t>(g_gameAssembly);
}

static void* RvaToPtr(uintptr_t rva)
{
    uintptr_t base = GameAssemblyBase();
    return base ? reinterpret_cast<void*>(base + rva) : nullptr;
}

static bool LoadIl2Cpp()
{
    g_gameAssembly = GetModuleHandleW(L"GameAssembly.dll");
    if (!g_gameAssembly) return false;

    il2cpp_domain_get                 = Resolve<il2cpp_domain_get_t>(g_gameAssembly,                 "il2cpp_domain_get");
    il2cpp_thread_attach              = Resolve<il2cpp_thread_attach_t>(g_gameAssembly,              "il2cpp_thread_attach");
    il2cpp_thread_detach              = Resolve<il2cpp_thread_detach_t>(g_gameAssembly,              "il2cpp_thread_detach");
    il2cpp_domain_get_assemblies      = Resolve<il2cpp_domain_get_assemblies_t>(g_gameAssembly,      "il2cpp_domain_get_assemblies");
    il2cpp_assembly_get_image         = Resolve<il2cpp_assembly_get_image_t>(g_gameAssembly,         "il2cpp_assembly_get_image");
    il2cpp_class_from_name            = Resolve<il2cpp_class_from_name_t>(g_gameAssembly,            "il2cpp_class_from_name");
    il2cpp_class_get_method_from_name = Resolve<il2cpp_class_get_method_from_name_t>(g_gameAssembly, "il2cpp_class_get_method_from_name");
    il2cpp_runtime_invoke             = Resolve<il2cpp_runtime_invoke_t>(g_gameAssembly,             "il2cpp_runtime_invoke");
    il2cpp_object_new                 = Resolve<il2cpp_object_new_t>(g_gameAssembly,                 "il2cpp_object_new");
    il2cpp_class_get_field_from_name  = Resolve<il2cpp_class_get_field_from_name_t>(g_gameAssembly,  "il2cpp_class_get_field_from_name");
    il2cpp_field_get_value            = Resolve<il2cpp_field_get_value_t>(g_gameAssembly,            "il2cpp_field_get_value");
    il2cpp_field_set_value            = Resolve<il2cpp_field_set_value_t>(g_gameAssembly,            "il2cpp_field_set_value");
    il2cpp_field_static_get_value     = Resolve<il2cpp_field_static_get_value_t>(g_gameAssembly,     "il2cpp_field_static_get_value");
    il2cpp_image_get_class_count      = Resolve<il2cpp_image_get_class_count_t>(g_gameAssembly,      "il2cpp_image_get_class_count");
    il2cpp_image_get_class            = Resolve<il2cpp_image_get_class_t>(g_gameAssembly,            "il2cpp_image_get_class");
    il2cpp_class_get_name             = Resolve<il2cpp_class_get_name_t>(g_gameAssembly,             "il2cpp_class_get_name");
    il2cpp_class_get_namespace        = Resolve<il2cpp_class_get_namespace_t>(g_gameAssembly,        "il2cpp_class_get_namespace");
    il2cpp_class_get_methods          = Resolve<il2cpp_class_get_methods_t>(g_gameAssembly,          "il2cpp_class_get_methods");
    il2cpp_method_get_name            = Resolve<il2cpp_method_get_name_t>(g_gameAssembly,            "il2cpp_method_get_name");
    il2cpp_method_get_param_count     = Resolve<il2cpp_method_get_param_count_t>(g_gameAssembly,     "il2cpp_method_get_param_count");
    il2cpp_method_get_pointer         = Resolve<il2cpp_method_get_pointer_t>(g_gameAssembly,         "il2cpp_method_get_pointer");
    il2cpp_class_get_fields           = Resolve<il2cpp_class_get_fields_t>(g_gameAssembly,           "il2cpp_class_get_fields");
    il2cpp_field_get_name             = Resolve<il2cpp_field_get_name_t>(g_gameAssembly,             "il2cpp_field_get_name");
    il2cpp_field_get_flags            = Resolve<il2cpp_field_get_flags_t>(g_gameAssembly,            "il2cpp_field_get_flags");
    il2cpp_method_get_param           = Resolve<il2cpp_method_get_param_t>(g_gameAssembly,           "il2cpp_method_get_param");
    il2cpp_class_from_type            = Resolve<il2cpp_class_from_type_t>(g_gameAssembly,            "il2cpp_class_from_type");
    il2cpp_object_get_class           = Resolve<il2cpp_object_get_class_t>(g_gameAssembly,           "il2cpp_object_get_class");
    il2cpp_class_get_parent           = Resolve<il2cpp_class_get_parent_t>(g_gameAssembly,           "il2cpp_class_get_parent");
    il2cpp_array_length               = Resolve<il2cpp_array_length_t>(g_gameAssembly,               "il2cpp_array_length");
    il2cpp_array_get                  = Resolve<il2cpp_array_get_t>(g_gameAssembly,                   "il2cpp_array_get");
    il2cpp_field_get_offset           = Resolve<il2cpp_field_get_offset_t>(g_gameAssembly,            "il2cpp_field_get_offset");
    il2cpp_field_get_type             = Resolve<il2cpp_field_get_type_t>(g_gameAssembly,              "il2cpp_field_get_type");
    il2cpp_type_get_name              = Resolve<il2cpp_type_get_name_t>(g_gameAssembly,               "il2cpp_type_get_name");
    il2cpp_free                       = Resolve<il2cpp_free_t>(g_gameAssembly,                        "il2cpp_free");
    il2cpp_class_get_type             = Resolve<il2cpp_class_get_type_t>(g_gameAssembly,              "il2cpp_class_get_type");
    il2cpp_type_get_object            = Resolve<il2cpp_type_get_object_t>(g_gameAssembly,             "il2cpp_type_get_object");

    return il2cpp_domain_get && il2cpp_thread_attach && il2cpp_domain_get_assemblies &&
           il2cpp_assembly_get_image && il2cpp_class_from_name &&
           il2cpp_class_get_method_from_name && il2cpp_runtime_invoke &&
           il2cpp_object_new;
}

static void* FindClass(void* domain, const char* ns, const char* name)
{
    size_t count = 0;
    void** assemblies = il2cpp_domain_get_assemblies(domain, &count);
    for (size_t i = 0; i < count; ++i)
    {
        void* img = il2cpp_assembly_get_image(assemblies[i]);
        if (!img) continue;
        void* klass = il2cpp_class_from_name(img, ns, name);
        if (klass) return klass;
    }
    return nullptr;
}

static bool Streq(const char* a, const char* b)
{
    if (!a || !b) return false;
    while (*a && *b) { if (*a++ != *b++) return false; }
    return *a == *b;
}

static bool StrContains(const char* hay, const char* needle)
{
    if (!hay || !needle || !*needle) return false;
    for (const char* h = hay; *h; ++h)
    {
        const char* n = needle;
        const char* p = h;
        while (*n && *p && *n == *p) { ++n; ++p; }
        if (!*n) return true;
    }
    return false;
}

static void* FindClassByNameContains(void* domain, const char* needle)
{
    if (!il2cpp_image_get_class_count || !il2cpp_image_get_class || !il2cpp_class_get_name)
        return nullptr;

    size_t asmCount = 0;
    void** assemblies = il2cpp_domain_get_assemblies(domain, &asmCount);
    for (size_t a = 0; a < asmCount; ++a)
    {
        void* img = il2cpp_assembly_get_image(assemblies[a]);
        if (!img) continue;
        size_t n = il2cpp_image_get_class_count(img);
        for (size_t i = 0; i < n; ++i)
        {
            void* klass = il2cpp_image_get_class(img, i);
            if (!klass) continue;
            const char* cn = il2cpp_class_get_name(klass);
            if (cn && StrContains(cn, needle)) return klass;
        }
    }
    return nullptr;
}

// il2cpp_method_get_pointer is not exported by newer Unity builds (1.2.4); MethodInfo's
// first field is methodPointer, so fall back to reading it directly.
static void* MethodPointer(void* method)
{
    if (!method) return nullptr;
    if (il2cpp_method_get_pointer)
    {
        void* p = il2cpp_method_get_pointer(method);
        if (p) return p;
    }
    return *reinterpret_cast<void**>(method);
}

static void* FindClassWithMethodRva(void* domain, uintptr_t rva, const char* optName, int argCount)
{
    void* target = RvaToPtr(rva);
    if (!target || !il2cpp_image_get_class_count || !il2cpp_class_get_methods)
        return nullptr;

    size_t asmCount = 0;
    void** assemblies = il2cpp_domain_get_assemblies(domain, &asmCount);
    for (size_t a = 0; a < asmCount; ++a)
    {
        void* img = il2cpp_assembly_get_image(assemblies[a]);
        if (!img) continue;
        size_t n = il2cpp_image_get_class_count(img);
        for (size_t i = 0; i < n; ++i)
        {
            void* klass = il2cpp_image_get_class(img, i);
            if (!klass) continue;

            void* iter = nullptr;
            void* method = nullptr;
            while ((method = il2cpp_class_get_methods(klass, &iter)) != nullptr)
            {
                if (optName && il2cpp_method_get_name)
                {
                    const char* mn = il2cpp_method_get_name(method);
                    if (!mn || !Streq(mn, optName)) continue;
                }
                if (argCount >= 0 && il2cpp_method_get_param_count &&
                    il2cpp_method_get_param_count(method) != (uint8_t)argCount)
                    continue;

                void* ptr = MethodPointer(method);
                if (ptr == target) return klass;
                if (!ptr && optName) return klass; // pointer not set up yet: trust the name match
            }
        }
    }
    return nullptr;
}

static void* FindMethodOnClass(void* klass, const char* name, int argCount)
{
    if (!klass) return nullptr;
    void* m = il2cpp_class_get_method_from_name(klass, name, argCount);
    if (m) return m;

    if (!il2cpp_class_get_methods || !il2cpp_method_get_name) return nullptr;
    void* iter = nullptr;
    void* method = nullptr;
    while ((method = il2cpp_class_get_methods(klass, &iter)) != nullptr)
    {
        const char* mn = il2cpp_method_get_name(method);
        if (!mn || !Streq(mn, name)) continue;
        if (il2cpp_method_get_param_count && il2cpp_method_get_param_count(method) != (uint8_t)argCount)
            continue;
        return method;
    }
    return nullptr;
}

static void* FindFieldOnClassOrParents(void* klass, const char* name)
{
    if (!klass || !name || !il2cpp_class_get_field_from_name) return nullptr;
    for (void* k = klass; k; k = il2cpp_class_get_parent ? il2cpp_class_get_parent(k) : nullptr)
    {
        void* field = il2cpp_class_get_field_from_name(k, name);
        if (field) return field;
        if (!il2cpp_class_get_parent) break;
    }
    return nullptr;
}

static void* ClassFromMethodParam(void* method, uint32_t index)
{
    if (!method || !il2cpp_method_get_param || !il2cpp_class_from_type) return nullptr;
    const void* type = il2cpp_method_get_param(method, index);
    if (!type) return nullptr;
    return il2cpp_class_from_type(type);
}

static void SyncStashRvasFromShared()
{
    if (!g_shared) return;
    auto pick = [](int32_t fromTrainer, uintptr_t fallback) -> uintptr_t
    {
        // Defaults are 1.00.08 addresses; never use them once the trainer has published a build.
        if (fromTrainer > 0) return static_cast<uintptr_t>(fromTrainer);
        return g_shared->buildId == 0 ? fallback : 0;
    };
    g_buildId     = g_shared->buildId;
    g_rvaStashJbg = pick(g_shared->rvaStashJbg, kDefaultRvaStashJbg);
    g_rvaStashJbp = pick(g_shared->rvaStashJbp, kDefaultRvaStashJbp);
    g_rvaStashJbb = pick(g_shared->rvaStashJbb, kDefaultRvaStashJbb);
    g_rvaSaveCtor = pick(g_shared->rvaSaveCtor, kDefaultRvaStashSaveDataCtor);
    g_rvaAddItem  = pick(g_shared->rvaAddItem, 0);
    g_rvaItemInfo = pick(g_shared->rvaItemInfo, 0);
}

static void InitNativeStashCalls()
{
    SyncStashRvasFromShared();
    g_nativeJbg = reinterpret_cast<NativeJbg_t>(RvaToPtr(g_rvaStashJbg));
    g_nativeJbp = reinterpret_cast<NativeJbp_t>(RvaToPtr(g_rvaStashJbp));
    g_nativeJbb = reinterpret_cast<NativeJbb_t>(RvaToPtr(g_rvaStashJbb));
}

static int32_t BuildStatusFlags()
{
    int32_t flags = 1;
    if (g_setTimeScale) flags |= 2;
    if ((g_jbg || g_nativeJbg) && g_isrMethod && g_isiMethod)
        flags |= 4;
    else if (g_blkMethod || (g_stashCacheClass && (g_jbg || g_nativeJbg)))
        flags |= 4;
    return flags;
}

static void* FindMethodAny(void* klass, const char* const* names, const int* argCounts, int n)
{
    if (!klass) return nullptr;
    for (int i = 0; i < n; ++i)
    {
        void* m = FindMethodOnClass(klass, names[i], argCounts[i]);
        if (m) return m;
    }
    return nullptr;
}

// Finds the method whose native pointer is GameAssembly+rva (argCount < 0 = any).
static void* FindMethodByRva(uintptr_t rva, int argCount, void** outClass)
{
    void* klass = FindClassWithMethodRva(g_domain, rva, nullptr, argCount < 0 ? -1 : argCount);
    if (!klass) return nullptr;
    void* target = RvaToPtr(rva);
    void* iter = nullptr;
    void* method = nullptr;
    while ((method = il2cpp_class_get_methods(klass, &iter)) != nullptr)
    {
        if (argCount >= 0 && il2cpp_method_get_param_count &&
            il2cpp_method_get_param_count(method) != (uint8_t)argCount)
            continue;
        if (MethodPointer(method) == target)
        {
            if (outClass) *outClass = klass;
            return method;
        }
    }
    return nullptr;
}

// 1.00.08 / 1.00.09 resolve the item API by obfuscated name. Newer builds reuse those
// short names for unrelated classes (1.2.4 "ti" is a save-data class), so they are
// resolved strictly by the RVAs the trainer publishes.
static bool UseLegacyItemNames()
{
    return g_shared && g_shared->buildId > 0 && g_shared->buildId <= 10009;
}

static bool ResolveItemAdd()
{
    if (g_blkMethod && g_isrMethod) return true;
    if (!g_domain) return false;

    if (!UseLegacyItemNames())
    {
        SyncStashRvasFromShared();
        if (!g_blkMethod && g_rvaAddItem > 0)
        {
            void* klass = nullptr;
            g_blkMethod = FindMethodByRva(g_rvaAddItem, -1, &klass);
            if (g_blkMethod)
            {
                g_itemApiClass = klass;
                g_blkArgCount = il2cpp_method_get_param_count ? il2cpp_method_get_param_count(g_blkMethod) : 3;
                if (g_blkArgCount != 3 && g_blkArgCount != 5) g_blkMethod = nullptr; // unknown shape
            }
        }
        if (!g_isrMethod && g_rvaItemInfo > 0)
            g_isrMethod = FindMethodByRva(g_rvaItemInfo, 1, nullptr);
        return g_blkMethod != nullptr;
    }

    if (!g_tiClass)
    {
        g_tiClass = FindClass(g_domain, "", "ti");
        if (!g_tiClass) g_tiClass = FindClass(g_domain, "ue", "ti");
        if (!g_tiClass) g_tiClass = FindClass(g_domain, "ud", "ti");
    }
    if (g_tiClass)
    {
        if (!g_blkMethod) g_blkMethod = FindMethodOnClass(g_tiClass, "blk", 3);
        if (!g_isrMethod) g_isrMethod = FindMethodOnClass(g_tiClass, "isr", 1);
        if (!g_hyyMethod) g_hyyMethod = FindMethodOnClass(g_tiClass, "hyy", 1);
        if (!g_isiMethod) g_isiMethod = FindMethodOnClass(g_tiClass, "isi", 3);
        if (!g_gesMethod) g_gesMethod = FindMethodOnClass(g_tiClass, "ges", 3);
        if (!g_iskMethod) g_iskMethod = FindMethodOnClass(g_tiClass, "isk", 3);
        if (!g_ishMethod) g_ishMethod = FindMethodOnClass(g_tiClass, "ish", 1);
        if (!g_opdMethod) g_opdMethod = FindMethodOnClass(g_tiClass, "opd", 1);
        if (!g_islMethod) g_islMethod = FindMethodOnClass(g_tiClass, "isl", 1);
        if (!g_esxMethod) g_esxMethod = FindMethodOnClass(g_tiClass, "esx", 1);
        if (!g_iryMethod) g_iryMethod = FindMethodOnClass(g_tiClass, "iry", 0);
    }
    if (!g_blkMethod && g_rvaAddItem > 0)
    {
        void* klass = FindClassWithMethodRva(g_domain, g_rvaAddItem, "blk", 3);
        if (klass)
        {
            g_tiClass = klass;
            g_blkMethod = FindMethodOnClass(klass, "blk", 3);
        }
    }
    return g_blkMethod != nullptr || (g_isrMethod != nullptr && g_isiMethod != nullptr);
}

static void UpdateSharedStatus()
{
    if (g_shared) g_shared->status = BuildStatusFlags();
}

static bool TryResolveStash()
{
    if (!g_domain) return false;
    // Only touch the stash API once the trainer has published a profile with verified
    // stash RVAs. Unknown builds (e.g. 1.2.4) send 0; falling back to the 1.00.08
    // defaults there would call native code at a stale address.
    if (!g_shared || g_shared->buildId == 0) return false;
    if (g_shared->rvaStashJbg <= 0)
    {
        // Item-API-only build (1.2.4): spawn goes through the add-item RVA; never touch
        // stash natives or stash init here.
        if (g_shared->rvaAddItem <= 0) return false;
        bool ok = ResolveItemAdd();
        UpdateSharedStatus();
        return ok;
    }

    InitNativeStashCalls();

    // 1) Direct name lookup — 1.00.08 ud.* / 1.00.09 ue.*
    if (!g_stashClass)
    {
        static const char* kNs[] = { "ue", "ud", "", "UD", nullptr };
        for (int i = 0; kNs[i]; ++i)
        {
            g_stashClass = FindClass(g_domain, kNs[i], "Stash");
            if (g_stashClass) break;
        }
    }
    if (!g_stashCacheClass)
    {
        static const char* kNs[] = { "ue", "ud", "", nullptr };
        for (int i = 0; kNs[i]; ++i)
        {
            g_stashCacheClass = FindClass(g_domain, kNs[i], "StashCache");
            if (g_stashCacheClass) break;
        }
        if (!g_stashCacheClass)
            g_stashCacheClass = FindClassByNameContains(g_domain, "StashCache");
    }
    if (!g_stashSaveClass)
    {
        static const char* kSaveNs[] = { "", "TaskbarHero.EasySaveData", "ud", "ue", nullptr };
        for (int i = 0; kSaveNs[i]; ++i)
        {
            g_stashSaveClass = FindClass(g_domain, kSaveNs[i], "StashSaveData");
            if (g_stashSaveClass) break;
        }
        if (!g_stashSaveClass) g_stashSaveClass = FindClassByNameContains(g_domain, "StashSaveData");
    }

    // 2) Locate Stash by native RVA of insert method (jbg 1.00.08 / jbn 1.00.09)
    if (!g_stashClass)
        g_stashClass = FindClassWithMethodRva(g_domain, g_rvaStashJbg, "jbn", 2);
    if (!g_stashClass)
        g_stashClass = FindClassWithMethodRva(g_domain, g_rvaStashJbg, "jbg", 2);
    if (!g_stashClass)
        g_stashClass = FindClassWithMethodRva(g_domain, g_rvaStashJbg, nullptr, 2);

    if (g_stashClass)
    {
        if (!g_jbg)
        {
            static const char* kIns[] = { "jbn", "jbg" };
            static const int kInsArgs[] = { 2, 2 };
            g_jbg = FindMethodAny(g_stashClass, kIns, kInsArgs, 2);
        }
        if (!g_jbp)
        {
            static const char* kSlot[] = { "jbw", "jbp" };
            static const int kSlotArgs[] = { 1, 1 };
            g_jbp = FindMethodAny(g_stashClass, kSlot, kSlotArgs, 2);
        }
        if (!g_jbi)
        {
            static const char* kInit[] = { "jbi", "jbb" };
            static const int kInitArgs[] = { 0, 0 };
            g_jbi = FindMethodAny(g_stashClass, kInit, kInitArgs, 2);
        }
        if (!g_jboMethod)
        {
            static const char* kAddTf[] = { "jbo", "jbp", "jbq" };
            static const int kAddTfArgs[] = { 2, 2, 2 };
            g_jboMethod = FindMethodAny(g_stashClass, kAddTf, kAddTfArgs, 3);
        }
        if (!g_jcdMethod) g_jcdMethod = FindMethodOnClass(g_stashClass, "jcd", 0);
    }

    // 3) StashCache type from jbg's 2nd parameter
    if (!g_stashCacheClass && g_jbg)
        g_stashCacheClass = ClassFromMethodParam(g_jbg, 1);

    if (!g_stashInstance)
        g_stashInstance = ResolveStashInstance();

    if (g_stashSaveClass && !g_stashSaveCtor)
        g_stashSaveCtor = il2cpp_class_get_method_from_name(g_stashSaveClass, ".ctor", 1);

    // 4) Stash singleton — scan every static field on Stash
    if (!g_stashInstance && g_stashClass && il2cpp_class_get_fields && il2cpp_field_static_get_value)
    {
        void* iter = nullptr;
        void* field = nullptr;
        while ((field = il2cpp_class_get_fields(g_stashClass, &iter)) != nullptr)
        {
            if (il2cpp_field_get_flags)
            {
                int32_t flags = il2cpp_field_get_flags(field);
                if ((flags & 0x0010) == 0) continue; // FIELD_ATTRIBUTE_STATIC
            }
            void* val = nullptr;
            il2cpp_field_static_get_value(field, &val);
            if (val) { g_stashInstance = val; break; }
        }
    }
    if (!g_stashInstance)
        g_stashInstance = ResolveStashInstance();

    // 5) Init stash tables (static call on 1.00.09 ue.Stash)
    if (g_jbi)
    {
        void* exc = nullptr;
        il2cpp_runtime_invoke(g_jbi, nullptr, nullptr, &exc);
    }
    else if (g_stashInstance && g_nativeJbb)
        g_nativeJbb(g_stashInstance);

    ResolveYqInstance();

    ResolveItemAdd();
    if (!g_ispMethod && g_tiClass)
        g_ispMethod = FindMethodOnClass(g_tiClass, "isp", 1);

    UpdateSharedStatus();
    return g_blkMethod != nullptr ||
           ((g_stashCacheClass != nullptr) && (g_jbg != nullptr || g_nativeJbg != nullptr));
}

static void SetTimeScale(float v)
{
    if (!g_setTimeScale) return;
    void* args[1] = { &v };
    void* exc = nullptr;
    il2cpp_runtime_invoke(g_setTimeScale, nullptr, args, &exc);
}

static uint64_t ReadSaveUniqueId(void* cacheObj)
{
    if (!cacheObj || !g_stashCacheClass || !g_stashSaveClass) return 0;

    void* save = nullptr;
    void* fSave = il2cpp_class_get_field_from_name(g_stashCacheClass, "beeg");
    if (!fSave) fSave = il2cpp_class_get_field_from_name(g_stashCacheClass, "saveData");
    if (fSave) il2cpp_field_get_value(cacheObj, fSave, &save);
    if (!save) return 0;

    uint64_t uid = 0;
    void* fUid = il2cpp_class_get_field_from_name(g_stashSaveClass, "ItemUniqueId");
    if (fUid) il2cpp_field_get_value(save, fUid, &uid);
    return uid;
}

static void* InvokeJbp(void* stashInst, int32_t index)
{
    (void)stashInst;
    if (g_jbp)
    {
        void* args[1] = { &index };
        void* exc = nullptr;
        return il2cpp_runtime_invoke(g_jbp, nullptr, args, &exc);
    }
    if (g_nativeJbp)
        return g_nativeJbp(nullptr, index);
    return nullptr;
}

static bool InvokeJbg(void* stashInst, uint64_t uid, void* cacheObj)
{
    (void)stashInst;
    if (g_jbg)
    {
        void* args[2] = { &uid, cacheObj };
        void* exc = nullptr;
        il2cpp_runtime_invoke(g_jbg, nullptr, args, &exc);
        if (exc == nullptr) return true;
    }
    if (g_nativeJbg)
    {
        g_nativeJbg(nullptr, uid, cacheObj);
        return true;
    }
    return false;
}

// Name-independent singleton lookup: the generic singleton base (ob`1 on 1.2.4) renames
// its static instance field every build, so accept any static field on the class or its
// parents whose declared type and runtime class are the class itself.
static void* FindSingletonInstance(void* klass)
{
    if (!klass || !il2cpp_object_get_class || !il2cpp_field_get_type || !il2cpp_class_from_type)
        return nullptr;
    for (void* k = klass; k; k = il2cpp_class_get_parent ? il2cpp_class_get_parent(k) : nullptr)
    {
        void* iter = nullptr;
        void* field = nullptr;
        while ((field = il2cpp_class_get_fields(k, &iter)) != nullptr)
        {
            if (!il2cpp_field_get_flags || (il2cpp_field_get_flags(field) & 0x0010) == 0) continue;
            const void* type = il2cpp_field_get_type(field);
            if (!type || il2cpp_class_from_type(type) != klass) continue;
            void* value = nullptr;
            il2cpp_field_static_get_value(field, &value);
            if (value && il2cpp_object_get_class(value) == klass) return value;
        }
        if (!il2cpp_class_get_parent) break;
    }
    return nullptr;
}

// Item database (yq on 1.00.09, bco on 1.2.4): the only class declaring both the
// un-obfuscated "itemInfoData" and "currencyInfoData" lists.
static void* FindItemDatabaseClass()
{
    size_t asmCount = 0;
    void** assemblies = il2cpp_domain_get_assemblies(g_domain, &asmCount);
    for (size_t a = 0; a < asmCount; ++a)
    {
        void* img = il2cpp_assembly_get_image(assemblies[a]);
        if (!img || !il2cpp_image_get_class_count) continue;
        size_t n = il2cpp_image_get_class_count(img);
        for (size_t i = 0; i < n; ++i)
        {
            void* klass = il2cpp_image_get_class(img, i);
            if (klass &&
                il2cpp_class_get_field_from_name(klass, "itemInfoData") &&
                il2cpp_class_get_field_from_name(klass, "currencyInfoData"))
                return klass;
        }
    }
    return nullptr;
}

static void* TryStaticField(void* klass, const char* name)
{
    if (!klass || !il2cpp_field_static_get_value) return nullptr;
    void* field = il2cpp_class_get_field_from_name(klass, name);
    if (!field) return nullptr;
    void* value = nullptr;
    il2cpp_field_static_get_value(field, &value);
    return value;
}

static bool YqInstanceLooksValid(void* inst)
{
    if (!inst || !g_yqClass) return false;
    void* fDict = il2cpp_class_get_field_from_name(g_yqClass, "bfjb");
    void* fList = il2cpp_class_get_field_from_name(g_yqClass, "itemInfoData");
    if (fDict)
    {
        void* dict = nullptr;
        il2cpp_field_get_value(inst, fDict, &dict);
        if (dict) return true;
    }
    if (fList)
    {
        void* list = nullptr;
        il2cpp_field_get_value(inst, fList, &list);
        if (list && ListSize(list) > 0) return true;
    }
    return false;
}

static void* ResolveYqInstance()
{
    if (g_yqInstance && YqInstanceLooksValid(g_yqInstance)) return g_yqInstance;
    g_yqInstance = nullptr;
    if (!g_domain) return nullptr;

    if (!UseLegacyItemNames())
    {
        if (!g_yqClass) g_yqClass = FindItemDatabaseClass();
        void* inst = FindSingletonInstance(g_yqClass);
        if (YqInstanceLooksValid(inst)) { g_yqInstance = inst; return inst; }
        return nullptr;
    }

    if (!g_yqClass) g_yqClass = FindClass(g_domain, "", "yq");
    if (!g_yqClass) return nullptr;

    if (!g_looMethod) g_looMethod = FindMethodOnClass(g_yqClass, "loo", 1);
    if (!g_lopMethod) g_lopMethod = FindMethodOnClass(g_yqClass, "lop", 1);

    if (il2cpp_class_get_parent)
    {
        static const char* kInstFields[] = { "bbwm", "Instance", "instance", nullptr };
        for (void* k = g_yqClass; k; k = il2cpp_class_get_parent(k))
        {
            for (int fi = 0; kInstFields[fi]; ++fi)
            {
                void* cand = TryStaticField(k, kInstFields[fi]);
                if (YqInstanceLooksValid(cand))
                {
                    g_yqInstance = cand;
                    return cand;
                }
            }
        }
    }

    void* inst = TryStaticField(g_yqClass, "Instance");
    if (YqInstanceLooksValid(inst)) { g_yqInstance = inst; return inst; }

    if (il2cpp_domain_get_assemblies && il2cpp_class_get_fields && il2cpp_object_get_class)
    {
        size_t asmCount = 0;
        void** assemblies = il2cpp_domain_get_assemblies(g_domain, &asmCount);
        for (size_t a = 0; a < asmCount; ++a)
        {
            void* img = il2cpp_assembly_get_image(assemblies[a]);
            if (!img || !il2cpp_image_get_class_count) continue;
            size_t n = il2cpp_image_get_class_count(img);
            for (size_t i = 0; i < n; ++i)
            {
                void* klass = il2cpp_image_get_class(img, i);
                if (!klass) continue;
                void* iter = nullptr;
                void* field = nullptr;
                while ((field = il2cpp_class_get_fields(klass, &iter)) != nullptr)
                {
                    if (il2cpp_field_get_flags && (il2cpp_field_get_flags(field) & 0x0010) == 0)
                        continue;
                    void* obj = nullptr;
                    il2cpp_field_static_get_value(field, &obj);
                    if (!obj) continue;
                    if (il2cpp_object_get_class(obj) != g_yqClass) continue;
                    if (YqInstanceLooksValid(obj))
                    {
                        g_yqInstance = obj;
                        return obj;
                    }
                }
            }
        }
    }
    return nullptr;
}

static int32_t ReadItemKeyFromInfo(void* info)
{
    if (!info) return -1;
    return *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(info) + 0x30);
}

static int32_t ReadGradeFromInfo(void* info)
{
    if (!info) return -1;
    return *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(info) + 0x38);
}

static bool InvokeBoolMethod(void* obj, void* method)
{
    if (!obj || !method) return false;
    void* exc = nullptr;
    void* ret = il2cpp_runtime_invoke(method, obj, nullptr, &exc);
    return !exc && ret && *reinterpret_cast<bool*>(ret);
}

static void* InvokeMethod0(void* obj, void* method)
{
    if (!method) return nullptr;
    void* exc = nullptr;
    return il2cpp_runtime_invoke(method, obj, nullptr, &exc);
}

static int32_t UnboxInt32(void* boxed)
{
    if (!boxed) return -1;
    return *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(boxed) + 0x10);
}

static size_t CatalogAppendFromList(void* yqInst, char* text, size_t cap, size_t pos, int32_t& count, bool& truncated)
{
    if (!yqInst || !g_yqClass) return pos;
    void* fList = il2cpp_class_get_field_from_name(g_yqClass, "itemInfoData");
    if (!fList) return pos;

    void* list = nullptr;
    il2cpp_field_get_value(yqInst, fList, &list);
    if (!list) return pos;

    int32_t size = ListSize(list);
    for (int32_t i = 0; i < size; ++i)
    {
        void* info = ReadListItem(list, i);
        if (!info) continue;
        int32_t key = ReadItemKeyFromInfo(info);
        if (key <= 0) continue;
        int32_t grade = ReadGradeFromInfo(info);
        size_t need = 64;
        if (pos + need >= cap) { truncated = true; return pos; }
        pos = ScanAppend(text, cap, pos, "ITEM %d grade=%d (%s)\r\n", key, grade, GradeTypeName(grade));
        count++;
    }
    return pos;
}

static size_t CatalogAppendFromDict(void* yqInst, char* text, size_t cap, size_t pos, int32_t& count, bool& truncated)
{
    if (!yqInst || !g_yqClass || !il2cpp_object_get_class) return pos;
    void* fDict = il2cpp_class_get_field_from_name(g_yqClass, "bfjb");
    if (!fDict) return pos;

    void* dict = nullptr;
    il2cpp_field_get_value(yqInst, fDict, &dict);
    if (!dict) return pos;

    void* dictClass = il2cpp_object_get_class(dict);
    void* getKeys = il2cpp_class_get_method_from_name(dictClass, "get_Keys", 0);
    if (!getKeys) return pos;

    void* keys = InvokeMethod0(dict, getKeys);
    if (!keys) return pos;

    void* keysClass = il2cpp_object_get_class(keys);
    void* getEnum = il2cpp_class_get_method_from_name(keysClass, "GetEnumerator", 0);
    if (!getEnum) return pos;

    void* enumerator = InvokeMethod0(keys, getEnum);
    if (!enumerator) return pos;

    void* enumClass = il2cpp_object_get_class(enumerator);
    void* moveNext = il2cpp_class_get_method_from_name(enumClass, "MoveNext", 0);
    void* getCurrent = il2cpp_class_get_method_from_name(enumClass, "get_Current", 0);
    if (!moveNext || !getCurrent) return pos;

    while (InvokeBoolMethod(enumerator, moveNext))
    {
        void* exc = nullptr;
        void* cur = il2cpp_runtime_invoke(getCurrent, enumerator, nullptr, &exc);
        if (exc || !cur) continue;
        int32_t key = UnboxInt32(cur);
        if (key <= 0) continue;

        int32_t grade = -1;
        if (g_yqInstance && g_looMethod)
        {
            void* args[1] = { &key };
            exc = nullptr;
            void* info = il2cpp_runtime_invoke(g_looMethod, g_yqInstance, args, &exc);
            if (!exc && info) grade = ReadGradeFromInfo(info);
        }
        if (grade < 0) grade = ReadGradeFromItemKey(key);

        if (pos + 64 >= cap) { truncated = true; return pos; }
        pos = ScanAppend(text, cap, pos, "ITEM %d grade=%d (%s)\r\n", key, grade, GradeTypeName(grade));
        count++;
    }
    return pos;
}

static void* ResolveStashInstance()
{
    if (!g_stashClass) return nullptr;

    static const char* kStaticFields[] = {
        "bedv", "bedw", "bedy", "becv", "becw", "becy", "becz",
        "Instance", "instance", "_instance", "s_instance"
    };
    for (const char* name : kStaticFields)
    {
        void* inst = TryStaticField(g_stashClass, name);
        if (inst) return inst;
    }

    // Some builds expose the live stash on a static manager type.
    void* domain = g_domain ? g_domain : il2cpp_domain_get();
    void* mgr = FindClass(domain, "ud", "StashManager");
    if (!mgr) mgr = FindClass(domain, "", "StashManager");
    if (mgr)
    {
        for (const char* name : kStaticFields)
        {
            void* inst = TryStaticField(mgr, name);
            if (inst) return inst;
        }
    }

    return nullptr;
}

static int32_t FindEmptySlot(void* stashInst)
{
    (void)stashInst;
    for (int32_t i = 0; i < kStashMaxSlots; ++i)
    {
        void* cache = InvokeJbp(nullptr, i);
        if (!cache) return i;
        if (ReadSaveUniqueId(cache) == 0) return i;
    }
    return -1;
}

static void SetFieldInt32(void* obj, void* klass, const char* name, int32_t v)
{
    void* f = il2cpp_class_get_field_from_name(klass, name);
    if (f) il2cpp_field_set_value(obj, f, &v);
}

static void SetFieldUInt64(void* obj, void* klass, const char* name, uint64_t v)
{
    void* f = il2cpp_class_get_field_from_name(klass, name);
    if (f) il2cpp_field_set_value(obj, f, &v);
}

static void SetFieldBool(void* obj, void* klass, const char* name, bool v)
{
    void* f = il2cpp_class_get_field_from_name(klass, name);
    if (f) il2cpp_field_set_value(obj, f, &v);
}

static int32_t ReadAddResultCode(void* resultPtr)
{
    if (!resultPtr) return -1;
    // AddItemResult is an Il2Cpp value-type; AddResult field @ 0x14 (see dump)
    int32_t direct = *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(resultPtr) + 0x14);

    if (!g_addItemResultClass)
        g_addItemResultClass = FindClass(g_domain, "TaskbarHero", "AddItemResult");
    if (g_addItemResultClass)
    {
        int32_t viaField = -1;
        void* f = il2cpp_class_get_field_from_name(g_addItemResultClass, "AddResult");
        if (f) il2cpp_field_get_value(resultPtr, f, &viaField);
        if (viaField >= 0) return viaField;
    }
    return direct;
}

static bool AddItemResultOk(void* resultPtr)
{
    if (!resultPtr) return false;
    int32_t code = ReadAddResultCode(resultPtr);
    // TaskbarHero add-item APIs: 0/1 = success in 1.00.09 testing
    return code == 0 || code == 1;
}

static void SetSpawnFailFromResult(void* resultPtr)
{
    int32_t code = ReadAddResultCode(resultPtr);
    g_lastSpawnDetail = (code >= 0 && code <= 99) ? (-100 - code) : -6;
}

static void* ResolveItemInfo(int32_t itemKey)
{
    void* exc = nullptr;
    void* args[1] = { &itemKey };

    if (g_yqInstance && g_looMethod)
    {
        exc = nullptr;
        void* info = il2cpp_runtime_invoke(g_looMethod, g_yqInstance, args, &exc);
        if (!exc && info) return info;
    }

    if (g_isrMethod)
    {
        exc = nullptr;
        void* info = il2cpp_runtime_invoke(g_isrMethod, nullptr, args, &exc);
        if (!exc && info) return info;
    }
    if (g_hyyMethod)
    {
        exc = nullptr;
        void* info = il2cpp_runtime_invoke(g_hyyMethod, nullptr, args, &exc);
        if (!exc && info) return info;
    }
    return nullptr;
}

static int32_t GetCatalogGradeForKey(int32_t itemKey)
{
    void* info = ResolveItemInfo(itemKey);
    if (info) return ReadGradeFromInfo(info);
    return -1;
}

// Gear-style keys: 30XYYY — digit X (thousands) is EGradeType for many equipment rows.
static int32_t RemapItemKeyDigitGrade(int32_t itemKey, int32_t targetGrade)
{
    if (itemKey < 100000 || itemKey > 99999999) return itemKey;
    int32_t cur = (itemKey / 1000) % 10;
    if (cur == targetGrade) return itemKey;
    int32_t candidate = (itemKey / 10000) * 10000 + targetGrade * 1000 + (itemKey % 1000);
    if (ItemKeyExists(candidate)) return candidate;
    return itemKey;
}

static int32_t KeyGradeDigit(int32_t itemKey)
{
    if (itemKey < 100000) return -1;
    return (itemKey / 1000) % 10;
}

static int32_t TryCandidateKey(int32_t key, int32_t targetGrade, int32_t itemKey, int32_t* bestDist, int32_t* bestKey)
{
    if (key <= 0 || KeyGradeDigit(key) != targetGrade) return 0;
    if (!ItemKeyExists(key)) return 0;
    if (GetCatalogGradeForKey(key) != targetGrade) return 0;
    int32_t dist = key > itemKey ? key - itemKey : itemKey - key;
    if (dist < *bestDist)
    {
        *bestDist = dist;
        *bestKey = key;
        return 1;
    }
    return 0;
}

static int32_t FindItemKeyAlternateGrade(int32_t itemKey, int32_t targetGrade)
{
    if (itemKey < 100000) return itemKey;

    const int32_t prefix2 = itemKey / 10000;
    const int32_t suffix2 = itemKey % 100;
    const int32_t suffix3 = itemKey % 1000;
    int32_t bestKey = itemKey;
    int32_t bestDist = 0x7FFFFFFF;

    // Same 30x family, target grade digit only (304061 Immortal -> 308161 Divine)
    static const int32_t kSuffixDelta[] = { 0, 100, 200, 300, 1000, 1100, 1200, 1300 };
    for (int i = 0; i < 8; ++i)
    {
        int32_t key = prefix2 * 10000 + targetGrade * 1000 + suffix3 + kSuffixDelta[i];
        TryCandidateKey(key, targetGrade, itemKey, &bestDist, &bestKey);
        key = prefix2 * 10000 + targetGrade * 1000 + suffix2 + kSuffixDelta[i];
        TryCandidateKey(key, targetGrade, itemKey, &bestDist, &bestKey);
    }

    if (bestDist < 0x7FFFFFFF) return bestKey;
    return itemKey;
}

static int32_t ResolveSpawnItemKey(int32_t itemKey, int32_t requestedGrade, int32_t* outOriginalGrade)
{
    ResolveYqInstance();
    int32_t catalog = GetCatalogGradeForKey(itemKey);
    int32_t digit = KeyGradeDigit(itemKey);
    if (outOriginalGrade) *outOriginalGrade = (catalog >= 0 ? catalog : digit);

    if (requestedGrade < 0 || requestedGrade > 10) return itemKey;

    // Catalog ID already encodes grade in digit X of 30XYYY — do not remap 318192 etc.
    if (digit == requestedGrade) return itemKey;
    if (catalog == requestedGrade) return itemKey;

    if (itemKey == g_remapInKey && requestedGrade == g_remapInGrade && g_remapOutKey > 0)
        return g_remapOutKey;

    int32_t remapped = RemapItemKeyDigitGrade(itemKey, requestedGrade);
    if (remapped != itemKey && KeyGradeDigit(remapped) == requestedGrade &&
        GetCatalogGradeForKey(remapped) == requestedGrade)
    {
        g_remapInKey = itemKey;
        g_remapInGrade = requestedGrade;
        g_remapOutKey = remapped;
        return remapped;
    }

    remapped = FindItemKeyAlternateGrade(itemKey, requestedGrade);
    if (remapped != itemKey && KeyGradeDigit(remapped) == requestedGrade &&
        GetCatalogGradeForKey(remapped) == requestedGrade)
    {
        g_remapInKey = itemKey;
        g_remapInGrade = requestedGrade;
        g_remapOutKey = remapped;
        return remapped;
    }

    return itemKey;
}

static void* CreateItemInstanceWithGrade(void* itemInfo, uint64_t uid, int32_t grade)
{
    if (!itemInfo) return nullptr;
    if (grade >= 0 && grade <= 10)
        *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(itemInfo) + 0x38) = grade;
    return CreateItemInstance(itemInfo, uid);
}

static void BumpUniqueIdSeed()
{
    if (g_nextUniqueId == 0xDEAD000000000001ULL)
        g_nextUniqueId = (GetTickCount64() << 16) | 0x10001ULL;
}

static bool ItemKeyExists(int32_t itemKey)
{
    if (ResolveItemInfo(itemKey)) return true;

    if (g_ispMethod)
    {
        void* args[1] = { &itemKey };
        void* exc = nullptr;
        void* sprite = il2cpp_runtime_invoke(g_ispMethod, nullptr, args, &exc);
        if (!exc && sprite) return true;
    }

    if (g_yqInstance && g_lopMethod)
    {
        void* args[1] = { &itemKey };
        void* exc = nullptr;
        void* ret = il2cpp_runtime_invoke(g_lopMethod, g_yqInstance, args, &exc);
        if (!exc && ret)
        {
            bool ok = *reinterpret_cast<bool*>(ret);
            if (ok) return true;
        }
    }
    return false;
}

static void* CreateItemInstance(void* itemInfo, uint64_t uid)
{
    if (!itemInfo) return nullptr;
    bool notify = true;
    void* args[3] = { itemInfo, &uid, &notify };
    void* exc = nullptr;

    if (g_isiMethod)
    {
        exc = nullptr;
        void* inst = il2cpp_runtime_invoke(g_isiMethod, nullptr, args, &exc);
        if (!exc && inst) return inst;
    }
    if (g_gesMethod)
    {
        exc = nullptr;
        void* inst = il2cpp_runtime_invoke(g_gesMethod, nullptr, args, &exc);
        if (!exc && inst) return inst;
    }
    return nullptr;
}

static int32_t FindEmptySlotIndex()
{
    if (g_jcdMethod)
    {
        void* exc = nullptr;
        void* ret = il2cpp_runtime_invoke(g_jcdMethod, nullptr, nullptr, &exc);
        if (!exc && ret)
        {
            int32_t slot = *reinterpret_cast<int32_t*>(ret);
            if (slot >= 0 && slot < kStashMaxSlots) return slot;
        }
    }
    return FindEmptySlot(nullptr);
}

static bool SpawnViaJbo(int32_t itemKey, int32_t grade)
{
    if (!g_jboMethod) return false;
    if (!ResolveItemAdd()) return false;

    void* itemInfo = ResolveItemInfo(itemKey);
    if (!itemInfo)
    {
        g_lastSpawnDetail = -2;
        return false;
    }

    uint64_t uid = g_nextUniqueId++;
    void* itemInst = CreateItemInstanceWithGrade(itemInfo, uid, grade);
    if (!itemInst)
    {
        g_lastSpawnDetail = -3;
        return false;
    }

    if (g_jbi)
    {
        void* exc = nullptr;
        il2cpp_runtime_invoke(g_jbi, nullptr, nullptr, &exc);
    }

    void* args[2] = { &uid, itemInst };
    void* exc = nullptr;
    il2cpp_runtime_invoke(g_jboMethod, nullptr, args, &exc);
    if (exc)
    {
        g_lastSpawnDetail = -5;
        return false;
    }
    return true;
}

static bool SpawnViaIsk(int32_t itemKey)
{
    if (!g_iskMethod) return false;
    if (!ResolveItemAdd()) return false;
    BumpUniqueIdSeed();

    uint64_t uid = g_nextUniqueId++;
    bool notify = true;
    void* args[3] = { &itemKey, &uid, &notify };
    void* exc = nullptr;
    void* ret = il2cpp_runtime_invoke(g_iskMethod, nullptr, args, &exc);
    if (exc)
    {
        g_lastSpawnDetail = -3;
        return false;
    }
    if (!AddItemResultOk(ret))
    {
        SetSpawnFailFromResult(ret);
        return false;
    }
    return true;
}

static bool SpawnViaBlk(int32_t itemKey)
{
    if (!ResolveItemAdd() || !g_blkMethod) return false;
    BumpUniqueIdSeed();

    uint64_t uid = g_nextUniqueId++;
    bool notify = true;
    // 1.2.4 jiv(int itemKey, ulong uid, EItemGetSourceType source, int count, bool notify):
    // report the item as a normal monster drop (MonsterDieReward = 6), one at a time.
    int32_t source = 6;
    int32_t count = 1;
    void* args3[3] = { &itemKey, &uid, &notify };
    void* args5[5] = { &itemKey, &uid, &source, &count, &notify };
    void* exc = nullptr;
    void* ret = il2cpp_runtime_invoke(g_blkMethod, nullptr, g_blkArgCount == 5 ? args5 : args3, &exc);
    if (exc)
    {
        g_lastSpawnDetail = -3;
        return false;
    }
    if (!AddItemResultOk(ret))
    {
        SetSpawnFailFromResult(ret);
        return false;
    }
    return true;
}

static bool SpawnViaAddApis(int32_t spawnKey)
{
    // Prefer blk (inventory) — one API only; do not chain on success
    if (g_blkMethod && SpawnViaBlk(spawnKey))
        return true;
    if (g_iskMethod && SpawnViaIsk(spawnKey))
        return true;
    return false;
}

static bool SpawnViaStash(int32_t itemKey, int32_t grade)
{
    if (!g_stashCacheClass || (!g_jbg && !g_nativeJbg)) return false;
    if (!ResolveItemAdd()) return false;

    void* itemInfo = ResolveItemInfo(itemKey);
    if (!itemInfo)
    {
        g_lastSpawnDetail = -2;
        return false;
    }

    uint64_t uid = g_nextUniqueId++;
    void* itemInst = CreateItemInstanceWithGrade(itemInfo, uid, grade);
    if (!itemInst)
    {
        g_lastSpawnDetail = -3;
        return false;
    }

    if (g_jboMethod)
    {
        void* argsJbo[2] = { &uid, itemInst };
        void* exc = nullptr;
        il2cpp_runtime_invoke(g_jboMethod, nullptr, argsJbo, &exc);
        if (!exc) return true;
        g_lastSpawnDetail = -5;
        return false;
    }

    if (g_jbi)
    {
        void* exc = nullptr;
        il2cpp_runtime_invoke(g_jbi, nullptr, nullptr, &exc);
    }

    int32_t slotIdx = FindEmptySlotIndex();
    if (slotIdx < 0)
    {
        g_lastSpawnDetail = -4;
        return false;
    }

    void* saveObj = nullptr;
    if (g_stashSaveClass && g_stashSaveCtor)
    {
        saveObj = il2cpp_object_new(g_stashSaveClass);
        if (saveObj)
        {
            void* ctorArgs[1] = { &slotIdx };
            void* exc = nullptr;
            il2cpp_runtime_invoke(g_stashSaveCtor, saveObj, ctorArgs, &exc);
            if (!exc)
            {
                SetFieldInt32(saveObj, g_stashSaveClass, "Index", slotIdx);
                SetFieldUInt64(saveObj, g_stashSaveClass, "ItemUniqueId", uid);
                SetFieldBool(saveObj, g_stashSaveClass, "IsUnLock", true);
            }
        }
    }
    if (!saveObj)
    {
        g_lastSpawnDetail = -3;
        return false;
    }

    void* cacheObj = il2cpp_object_new(g_stashCacheClass);
    if (!cacheObj)
    {
        g_lastSpawnDetail = -3;
        return false;
    }

    void* cacheCtor = il2cpp_class_get_method_from_name(g_stashCacheClass, ".ctor", 1);
    if (cacheCtor)
    {
        void* ctorArgs[1] = { saveObj };
        void* exc = nullptr;
        il2cpp_runtime_invoke(cacheCtor, cacheObj, ctorArgs, &exc);
        if (exc)
        {
            g_lastSpawnDetail = -5;
            return false;
        }
    }

    if (!InvokeJbg(nullptr, uid, cacheObj))
    {
        g_lastSpawnDetail = -5;
        return false;
    }
    return true;
}

static void EnsureItemMetaTypes()
{
    if (!g_tfClass)
        g_tfClass = FindClass(g_domain, "", "tf");
    if (!g_itemInfoClass)
    {
        g_itemInfoClass = FindClass(g_domain, "TaskbarHero.Data", "ItemInfoData");
        if (!g_itemInfoClass) g_itemInfoClass = FindClass(g_domain, "", "ItemInfoData");
    }
}

static int32_t ReadItemKeyFromTf(void* tf)
{
    if (!tf) return -1;
    EnsureItemMetaTypes();
    if (!g_tfClass || !g_itemInfoClass) return -1;

    void* info = nullptr;
    void* fInfo = il2cpp_class_get_field_from_name(g_tfClass, "bdyy");
    if (!fInfo) return -1;
    il2cpp_field_get_value(tf, fInfo, &info);
    if (!info) return -1;

    int32_t key = -1;
    void* fKey = il2cpp_class_get_field_from_name(g_itemInfoClass, "ItemKey");
    if (fKey) il2cpp_field_get_value(info, fKey, &key);
    return key;
}

static int32_t ReadGradeFromTf(void* tf)
{
    if (!tf) return -1;
    EnsureItemMetaTypes();
    if (!g_tfClass || !g_itemInfoClass) return -1;

    void* info = nullptr;
    void* fInfo = il2cpp_class_get_field_from_name(g_tfClass, "bdyy");
    if (!fInfo) return -1;
    il2cpp_field_get_value(tf, fInfo, &info);
    if (!info) return -1;

    int32_t grade = -1;
    void* fGr = il2cpp_class_get_field_from_name(g_itemInfoClass, "GRADE");
    if (!fGr) fGr = il2cpp_class_get_field_from_name(g_itemInfoClass, "Grade");
    if (fGr) il2cpp_field_get_value(info, fGr, &grade);
    return grade;
}

static bool ResolveSaveTypes()
{
    if (!g_domain) return false;
    if (!g_ysClass) g_ysClass = FindClass(g_domain, "", "ys");
    if (!g_playerSaveClass)
    {
        g_playerSaveClass = FindClass(g_domain, "TaskbarHero", "PlayerSaveData");
        if (!g_playerSaveClass) g_playerSaveClass = FindClassByNameContains(g_domain, "PlayerSaveData");
    }
    if (!g_itemSaveClass)
    {
        g_itemSaveClass = FindClass(g_domain, "TaskbarHero.EasySaveData", "ItemSaveData");
        if (!g_itemSaveClass) g_itemSaveClass = FindClassByNameContains(g_domain, "ItemSaveData");
    }
    if (g_ysClass && !g_lqkMethod)
        g_lqkMethod = FindMethodOnClass(g_ysClass, "lqk", 0);
    return g_playerSaveClass != nullptr && g_itemSaveClass != nullptr;
}

static bool PlayerSaveHasItemList(void* playerSave)
{
    if (!playerSave || !g_playerSaveClass) return false;
    void* list = nullptr;
    void* fList = il2cpp_class_get_field_from_name(g_playerSaveClass, "itemSaveDatas");
    if (!fList) return false;
    il2cpp_field_get_value(playerSave, fList, &list);
    return list != nullptr;
}

static void* ResolveYsInstance()
{
    if (g_ysInstance) return g_ysInstance;
    if (!ResolveSaveTypes() || !g_ysClass) return nullptr;

    void* fBfkm = il2cpp_class_get_field_from_name(g_ysClass, "bfkm");

    // nn`1 singleton static bbwm on parent chain
    if (il2cpp_class_get_parent)
    {
        for (void* k = g_ysClass; k; k = il2cpp_class_get_parent(k))
        {
        static const char* kInstFields[] = { "bbwm", "Instance", "instance", nullptr };
        for (int fi = 0; kInstFields[fi]; ++fi)
        {
            const char* fn = kInstFields[fi];
            void* cand = TryStaticField(k, fn);
                if (!cand || !fBfkm) continue;
                void* ps = nullptr;
                il2cpp_field_get_value(cand, fBfkm, &ps);
                if (PlayerSaveHasItemList(ps))
                {
                    g_ysInstance = cand;
                    return cand;
                }
            }
        }
    }

    // Scan all static fields for a ys live object with populated save data
    if (il2cpp_domain_get_assemblies && il2cpp_class_get_fields && fBfkm)
    {
        size_t asmCount = 0;
        void** assemblies = il2cpp_domain_get_assemblies(g_domain, &asmCount);
        for (size_t a = 0; a < asmCount; ++a)
        {
            void* img = il2cpp_assembly_get_image(assemblies[a]);
            if (!img || !il2cpp_image_get_class_count) continue;
            size_t n = il2cpp_image_get_class_count(img);
            for (size_t i = 0; i < n; ++i)
            {
                void* klass = il2cpp_image_get_class(img, i);
                if (!klass) continue;
                void* iter = nullptr;
                void* field = nullptr;
                while ((field = il2cpp_class_get_fields(klass, &iter)) != nullptr)
                {
                    if (il2cpp_field_get_flags && (il2cpp_field_get_flags(field) & 0x0010) == 0)
                        continue;
                    void* obj = nullptr;
                    il2cpp_field_static_get_value(field, &obj);
                    if (!obj) continue;
                    if (il2cpp_object_get_class && il2cpp_object_get_class(obj) != g_ysClass)
                        continue;
                    void* ps = nullptr;
                    il2cpp_field_get_value(obj, fBfkm, &ps);
                    if (PlayerSaveHasItemList(ps))
                    {
                        g_ysInstance = obj;
                        return obj;
                    }
                }
            }
        }
    }
    return nullptr;
}

static void* GetPlayerSaveData()
{
    if (!ResolveSaveTypes()) return nullptr;

    void* ys = ResolveYsInstance();
    if (ys && g_lqkMethod)
    {
        void* exc = nullptr;
        void* ps = il2cpp_runtime_invoke(g_lqkMethod, ys, nullptr, &exc);
        if (!exc && ps) return ps;
    }
    if (ys)
    {
        void* ps = nullptr;
        void* f = il2cpp_class_get_field_from_name(g_ysClass, "bfkm");
        if (f)
        {
            il2cpp_field_get_value(ys, f, &ps);
            if (ps) return ps;
        }
    }
    return nullptr;
}

static void* ReadListItem(void* list, int32_t index)
{
    if (!list) return nullptr;
    void* items = *reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(list) + 0x10);
    if (!items) return nullptr;
    if (il2cpp_array_get)
    {
        void* elem = il2cpp_array_get(items, sizeof(void*), (size_t)index);
        if (elem) return *reinterpret_cast<void**>(elem);
    }
    return *reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(items) + 0x20 + index * (int)sizeof(void*));
}

static int32_t ListSize(void* list)
{
    if (!list) return 0;
    return *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(list) + 0x18);
}

static int32_t LookupItemKeyFromSave(uint64_t uid)
{
    void* playerSave = GetPlayerSaveData();
    if (!playerSave || !g_playerSaveClass || !g_itemSaveClass) return -1;

    void* list = nullptr;
    void* fList = il2cpp_class_get_field_from_name(g_playerSaveClass, "itemSaveDatas");
    if (!fList) return -1;
    il2cpp_field_get_value(playerSave, fList, &list);
    if (!list) return -1;

    int32_t size = ListSize(list);
    for (int32_t i = 0; i < size && i < 20000; ++i)
    {
        void* itemSave = ReadListItem(list, i);
        if (!itemSave) continue;

        uint64_t id = *reinterpret_cast<uint64_t*>(reinterpret_cast<uint8_t*>(itemSave) + 0x18);
        if (id != uid) continue;

        return *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(itemSave) + 0x10);
    }
    return -1;
}

static int32_t ReadGradeFromItemKey(int32_t itemKey)
{
    if (itemKey <= 0) return -1;
    EnsureItemMetaTypes();
    void* info = ResolveItemInfo(itemKey);
    if (!info || !g_itemInfoClass) return -1;
    int32_t grade = -1;
    void* fGr = il2cpp_class_get_field_from_name(g_itemInfoClass, "GRADE");
    if (!fGr) fGr = il2cpp_class_get_field_from_name(g_itemInfoClass, "Grade");
    if (fGr) il2cpp_field_get_value(info, fGr, &grade);
    return grade;
}

static void* TfFromDictionary(uint64_t uid)
{
    if (!g_tiClass || !il2cpp_object_get_class) return nullptr;
    void* dict = TryStaticField(g_tiClass, "beaj");
    if (!dict) return nullptr;

    void* outTf = nullptr;
    void* args[2] = { &uid, &outTf };
    void* exc = nullptr;
    void* dictClass = il2cpp_object_get_class(dict);
    if (!dictClass) return nullptr;
    void* m = il2cpp_class_get_method_from_name(dictClass, "TryGetValue", 2);
    if (!m) return nullptr;
    il2cpp_runtime_invoke(m, dict, args, &exc);
    if (exc) return nullptr;
    return outTf;
}

static void* TfFromUid(uint64_t uid)
{
    void* args[1] = { &uid };
    void* exc = nullptr;

    void* methods[4] = { g_ishMethod, g_opdMethod, g_islMethod, g_esxMethod };
    for (int i = 0; i < 4; ++i)
    {
        if (!methods[i]) continue;
        exc = nullptr;
        void* tf = il2cpp_runtime_invoke(methods[i], nullptr, args, &exc);
        if (!exc && tf) return tf;
    }

    return TfFromDictionary(uid);
}

static int32_t LookupItemKey(uint64_t uid, char* srcBuf, size_t srcCap)
{
    if (srcBuf && srcCap > 0) srcBuf[0] = '\0';

    void* tf = TfFromUid(uid);
    if (tf)
    {
        int32_t key = ReadItemKeyFromTf(tf);
        if (key > 0)
        {
            if (srcBuf && srcCap > 0) snprintf(srcBuf, srcCap, "tf");
            return key;
        }
    }

    int32_t saveKey = LookupItemKeyFromSave(uid);
    if (saveKey > 0)
    {
        if (srcBuf && srcCap > 0) snprintf(srcBuf, srcCap, "save");
        return saveKey;
    }
    return -1;
}

static const char* GradeTypeName(int32_t grade)
{
    switch (grade)
    {
    case 0:  return "Common";
    case 1:  return "Uncommon";
    case 2:  return "Rare";
    case 3:  return "Legendary";
    case 4:  return "Immortal";
    case 5:  return "Arcana";
    case 6:  return "Beyond";
    case 7:  return "Celestial";
    case 8:  return "Divine";
    case 9:  return "Cosmic";
    case 10: return "None";
    default: return "?";
    }
}

static int32_t LookupGrade(uint64_t uid, int32_t itemKey)
{
    void* tf = TfFromUid(uid);
    if (tf)
    {
        int32_t grade = ReadGradeFromTf(tf);
        if (grade >= 0) return grade;
    }
    return ReadGradeFromItemKey(itemKey);
}

static int32_t ReadSaveSlotIndex(void* cacheObj)
{
    if (!cacheObj || !g_stashCacheClass || !g_stashSaveClass) return -1;
    void* save = nullptr;
    void* fSave = il2cpp_class_get_field_from_name(g_stashCacheClass, "beeg");
    if (!fSave) return -1;
    il2cpp_field_get_value(cacheObj, fSave, &save);
    if (!save) return -1;
    int32_t idx = -1;
    void* fIdx = il2cpp_class_get_field_from_name(g_stashSaveClass, "Index");
    if (fIdx) il2cpp_field_get_value(save, fIdx, &idx);
    return idx;
}

static size_t ScanAppend(char* buf, size_t cap, size_t pos, const char* fmt, ...)
{
    if (pos >= cap) return pos;
    va_list ap;
    va_start(ap, fmt);
    int n = vsnprintf(buf + pos, cap - pos, fmt, ap);
    va_end(ap);
    if (n < 0) return pos;
    return pos + (size_t)n;
}

static float ReadObscuredFloat(void* obj, size_t fieldOffset)
{
    uint8_t* p = reinterpret_cast<uint8_t*>(obj) + fieldOffset;
    uint32_t bits = *reinterpret_cast<uint32_t*>(p + 0x04) ^
                    *reinterpret_cast<uint32_t*>(p + 0x08);
    float value = 0.0f;
    memcpy(&value, &bits, sizeof(value));
    return value;
}

static float ReadObscuredFloatFake(void* obj, size_t fieldOffset)
{
    return *reinterpret_cast<float*>(reinterpret_cast<uint8_t*>(obj) + fieldOffset + 0x0C);
}

static int32_t ReadObscuredInt(void* obj, size_t fieldOffset)
{
    uint8_t* p = reinterpret_cast<uint8_t*>(obj) + fieldOffset;
    return *reinterpret_cast<int32_t*>(p + 0x04) ^
           *reinterpret_cast<int32_t*>(p + 0x08);
}

static int32_t ReadObscuredIntFake(void* obj, size_t fieldOffset)
{
    return *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(obj) + fieldOffset + 0x0C);
}

static int32_t ManagedArrayLength(void* array)
{
    if (!array) return 0;
    if (il2cpp_array_length) return static_cast<int32_t>(il2cpp_array_length(array));
    return static_cast<int32_t>(*reinterpret_cast<uintptr_t*>(reinterpret_cast<uint8_t*>(array) + 0x18));
}

static void* ReadManagedArrayRef(void* array, int32_t index)
{
    if (!array || index < 0) return nullptr;
    if (il2cpp_array_get) return il2cpp_array_get(array, sizeof(void*), static_cast<size_t>(index));
    return *reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(array) + 0x20 + static_cast<size_t>(index) * sizeof(void*));
}

static bool InvokeFloat0(void* obj, const char* methodName, float& value)
{
    if (!obj || !methodName || !il2cpp_object_get_class) return false;
    void* method = nullptr;
    for (void* k = il2cpp_object_get_class(obj); k; k = il2cpp_class_get_parent ? il2cpp_class_get_parent(k) : nullptr)
    {
        method = FindMethodOnClass(k, methodName, 0);
        if (method || !il2cpp_class_get_parent) break;
    }
    if (!method) return false;
    void* exc = nullptr;
    void* boxed = il2cpp_runtime_invoke(method, obj, nullptr, &exc);
    if (exc || !boxed) return false;
    value = *reinterpret_cast<float*>(reinterpret_cast<uint8_t*>(boxed) + 0x10);
    return true;
}

static bool InvokeDamageInfoOrigin0(void* obj, const char* methodName, float& value)
{
    if (!obj || !methodName || !il2cpp_object_get_class) return false;
    void* method = nullptr;
    for (void* k = il2cpp_object_get_class(obj); k; k = il2cpp_class_get_parent ? il2cpp_class_get_parent(k) : nullptr)
    {
        method = FindMethodOnClass(k, methodName, 0);
        if (method || !il2cpp_class_get_parent) break;
    }
    if (!method) return false;
    void* exc = nullptr;
    void* boxed = il2cpp_runtime_invoke(method, obj, nullptr, &exc);
    if (exc || !boxed) return false;
    value = *reinterpret_cast<float*>(reinterpret_cast<uint8_t*>(boxed) + 0x18);
    return true;
}

static bool InvokeCachedDamageInfoOrigin(const char* methodName, void* cache, float& value)
{
    if (!methodName || !cache) return false;
    void* klass = FindClass(g_domain, "", "uh");
    void* method = klass ? FindMethodOnClass(klass, methodName, 1) : nullptr;
    if (!method) return false;
    void* args[1] = { &cache };
    void* exc = nullptr;
    void* boxed = il2cpp_runtime_invoke(method, nullptr, args, &exc);
    if (exc || !boxed) return false;
    value = *reinterpret_cast<float*>(reinterpret_cast<uint8_t*>(boxed) + 0x18);
    return true;
}

// Cached: god mode / one-hit kill resolve the stage several times per second.
static void* StageManagerClass()
{
    static void* s_klass = nullptr;
    if (!s_klass) s_klass = FindClass(g_domain, "TaskbarHero", "StageManager");
    if (!s_klass) s_klass = FindClass(g_domain, "", "StageManager");
    return s_klass;
}

static void* ResolveStageManager()
{
    void* klass = StageManagerClass();
    if (!klass) return nullptr;

    static const char* kInstFields[] = { "bbwm", "Instance", "instance", nullptr };
    for (void* k = klass; k; k = il2cpp_class_get_parent ? il2cpp_class_get_parent(k) : nullptr)
    {
        for (int i = 0; kInstFields[i]; ++i)
        {
            void* candidate = TryStaticField(k, kInstFields[i]);
            if (candidate) return candidate;
        }
        if (!il2cpp_class_get_parent) break;
    }

    return FindSingletonInstance(klass);
}

static void* ResolveHeroByIndex(int32_t index)
{
    if (index < 0) return nullptr;
    void* stageClass = StageManagerClass();
    void* stage = ResolveStageManager();
    if (!stageClass || !stage) return nullptr;

    void* heroListField = FindFieldOnClassOrParents(stageClass, "HeroList");
    void* heroes = nullptr;
    if (heroListField) il2cpp_field_get_value(stage, heroListField, &heroes);
    int32_t count = ManagedArrayLength(heroes);
    return index < count ? ReadManagedArrayRef(heroes, index) : nullptr;
}

static bool WriteObscuredFloat(void* obj, size_t fieldOffset, float value)
{
    if (!obj || !g_domain) return false;
    if (!g_obscuredFloatEncryptMethod)
    {
        void* klass = FindClass(g_domain, "CodeStage.AntiCheat.ObscuredTypes", "ObscuredFloat");
        g_obscuredFloatEncryptMethod = klass ? FindMethodOnClass(klass, "xat", 2) : nullptr;
    }

    uint8_t* field = reinterpret_cast<uint8_t*>(obj) + fieldOffset;
    int32_t key = *reinterpret_cast<int32_t*>(field + 0x08);
    int32_t hidden = 0;
    if (g_obscuredFloatEncryptMethod)
    {
        void* args[2] = { &value, &key };
        void* exc = nullptr;
        void* boxed = il2cpp_runtime_invoke(g_obscuredFloatEncryptMethod, nullptr, args, &exc);
        if (exc || !boxed) return false;
        hidden = *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(boxed) + 0x10);
    }
    else
    {
        // Encrypt method is renamed every build (xat on 1.00.09). ACTk's float scheme is
        // hidden = bits ^ key. 1.2.4 keeps fakeValue = 0, so sanity-check the current
        // decrypted value instead: it must be a plausible stat (0 allowed, e.g. 0% crit)
    // before we overwrite it.
        if (key == 0) return false;
        float current = ReadObscuredFloat(obj, fieldOffset);
        if (!(current >= 0.0f && current < 1.0e7f)) return false;
        uint32_t bits = 0;
        memcpy(&bits, &value, sizeof(bits));
        hidden = static_cast<int32_t>(bits ^ static_cast<uint32_t>(key));
    }
    *reinterpret_cast<int32_t*>(field + 0x04) = hidden;
    // fakeValue is only maintained while ACTk's fake-value mode is active (0 on 1.2.4).
    if (*reinterpret_cast<float*>(field + 0x0C) != 0.0f)
        *reinterpret_cast<float*>(field + 0x0C) = value;
    return true;
}

// StageManager's Dictionary<DamageableType, HashSet<Unit>> of units on the field
// (bepb @ +0x58 on 1.2.4), found by field type so the obfuscated name doesn't matter.
static void* ResolveUnitsByTypeField()
{
    static void* s_field = nullptr;
    static bool s_searched = false;
    if (s_field || s_searched) return s_field;
    s_searched = true;
    void* klass = StageManagerClass();
    if (!klass || !il2cpp_field_get_type || !il2cpp_type_get_name) return nullptr;
    void* iter = nullptr;
    void* field = nullptr;
    while ((field = il2cpp_class_get_fields(klass, &iter)) != nullptr)
    {
        char* tn = il2cpp_type_get_name(il2cpp_field_get_type(field));
        bool match = tn && StrContains(tn, "DamageableType") && StrContains(tn, "HashSet");
        if (tn && il2cpp_free) il2cpp_free(tn);
        if (match) { s_field = field; break; }
    }
    return s_field;
}

static void* HealthControllerOf(void* unit)
{
    static void* s_field = nullptr;
    if (!s_field)
    {
        void* unitClass = il2cpp_object_get_class ? il2cpp_object_get_class(unit) : nullptr;
        s_field = FindFieldOnClassOrParents(unitClass, "UnitHealthController");
        if (!s_field) return nullptr;
    }
    void* health = nullptr;
    il2cpp_field_get_value(unit, s_field, &health);
    return health;
}

// One-hit kill: every live monster is kept at 1 HP, so the next hit kills it through the
// game's normal death path (drops, EXP). Dead monsters (HP <= 1) are left alone.
// Calls fn(healthController) for every live unit of a DamageableType (1 = Hero, 2 = Monster)
// registered in StageManager's Dictionary<DamageableType, HashSet<Unit>>.
template <typename Fn>
static void ForEachUnitHealth(int32_t damageableType, Fn fn)
{
    void* stage = ResolveStageManager();
    void* field = ResolveUnitsByTypeField();
    if (!stage || !field || !il2cpp_object_get_class) return;

    void* dict = nullptr;
    il2cpp_field_get_value(stage, field, &dict);
    if (!dict) return;
    void* tryGet = il2cpp_class_get_method_from_name(il2cpp_object_get_class(dict), "TryGetValue", 2);
    if (!tryGet) return;

    int32_t unitType = damageableType;
    void* set = nullptr;
    void* args[2] = { &unitType, &set };
    void* exc = nullptr;
    il2cpp_runtime_invoke(tryGet, dict, args, &exc);
    if (exc || !set) return;

    // Mono HashSet<T>: _slots @ +0x18, _lastIndex @ +0x24; Slot = { int hashCode; int next; T value; }
    uint8_t* hs = reinterpret_cast<uint8_t*>(set);
    void* slots = *reinterpret_cast<void**>(hs + 0x18);
    int32_t lastIndex = *reinterpret_cast<int32_t*>(hs + 0x24);
    if (!slots || lastIndex <= 0 || lastIndex > ManagedArrayLength(slots)) return;

    uint8_t* data = reinterpret_cast<uint8_t*>(slots) + 0x20;
    for (int32_t i = 0; i < lastIndex; ++i)
    {
        uint8_t* slot = data + static_cast<size_t>(i) * 16;
        if (*reinterpret_cast<int32_t*>(slot) < 0) continue; // free slot
        void* unit = *reinterpret_cast<void**>(slot + 8);
        if (!unit) continue;
        uint8_t* hp = reinterpret_cast<uint8_t*>(HealthControllerOf(unit));
        if (!hp) continue;
        fn(hp);
    }
}

static void ApplyOneHitKill()
{
    ForEachUnitHealth(2, [](uint8_t* hp)   // DamageableType.Monster
    {
        float& current = *reinterpret_cast<float*>(hp + 0x40);
        if (current > 1.0f) current = 1.0f;
    });
}

// God mode: refill every hero in the stage, not only the three UI slots (some modes,
// e.g. Plague, may field heroes that the slot mapping does not resolve).
static const float kGodModeHp = 2.0e9f;   // just under int.MaxValue (the game may cast HP to int)
static void ApplyGodModeAllHeroes()
{
    ForEachUnitHealth(1, [](uint8_t* hp)   // DamageableType.Hero
    {
        float& current = *reinterpret_cast<float*>(hp + 0x40);
        float& maxHp = *reinterpret_cast<float*>(hp + 0x4C);
        if (current <= 0.0f) return;         // already dead: leave the game's death flow alone
        if (maxHp < kGodModeHp) maxHp = kGodModeHp;
        current = maxHp;
    });
}

static bool ApplyHeroLocks(int32_t heroIndex)
{
    if (heroIndex < 0 || heroIndex >= 3) return false;
    HeroLocks& locks = g_heroLocks[heroIndex];
    void* hero = ResolveHeroByIndex(heroIndex);
    if (!hero) return false;

    if (locks.attackDamage) WriteObscuredFloat(hero, 0x104, locks.attackDamageValue);
    if (locks.attackSpeed) WriteObscuredFloat(hero, 0x118, locks.attackSpeedValue);
    for (int i = 0; i < kExtraStatCount; ++i)
        if (locks.extra[i]) WriteObscuredFloat(hero, kExtraStatOffsets[i], locks.extraValue[i]);

    if (locks.currentHp || locks.maxHp)
    {
        void* heroClass = il2cpp_object_get_class ? il2cpp_object_get_class(hero) : nullptr;
        void* healthField = FindFieldOnClassOrParents(heroClass, "UnitHealthController");
        void* health = nullptr;
        if (healthField) il2cpp_field_get_value(hero, healthField, &health);
        if (health)
        {
            uint8_t* hp = reinterpret_cast<uint8_t*>(health);
            if (locks.currentHp) *reinterpret_cast<float*>(hp + 0x40) = locks.currentHpValue;
            if (locks.maxHp) *reinterpret_cast<float*>(hp + 0x4C) = locks.maxHpValue;
        }
    }
    return true;
}

// uid -> item instance: the item API class keeps a static Dictionary<ulong, Item>
// (wh.vc.bgla on 1.2.6). Found by type so the obfuscated name doesn't matter.
static void* ItemByUid(uint64_t uid)
{
    static void* s_dictField = nullptr;
    if (!s_dictField)
    {
        ResolveItemAdd();
        if (!g_itemApiClass || !il2cpp_type_get_name) return nullptr;
        void* iter = nullptr;
        void* field = nullptr;
        while ((field = il2cpp_class_get_fields(g_itemApiClass, &iter)) != nullptr)
        {
            if (!il2cpp_field_get_flags || (il2cpp_field_get_flags(field) & 0x0010) == 0) continue;
            char* tn = il2cpp_type_get_name(il2cpp_field_get_type(field));
            bool match = tn && StrContains(tn, "Dictionary<System.UInt64,");
            if (tn && il2cpp_free) il2cpp_free(tn);
            if (match) { s_dictField = field; break; }
        }
        if (!s_dictField) return nullptr;
    }
    void* dict = nullptr;
    il2cpp_field_static_get_value(s_dictField, &dict);
    if (!dict) return nullptr;
    void* tryGet = il2cpp_class_get_method_from_name(il2cpp_object_get_class(dict), "TryGetValue", 2);
    if (!tryGet) return nullptr;
    void* item = nullptr;
    void* args[2] = { &uid, &item };
    void* exc = nullptr;
    il2cpp_runtime_invoke(tryGet, dict, args, &exc);
    return exc ? nullptr : item;
}

// rz singleton + its slot-uid getter, resolved from the trainer-published RVA.
static bool ResolveSlotApi(void** rzInstance, void** uidMethod)
{
    static void* s_uid = nullptr;
    static void* s_rzClass = nullptr;
    SyncStashRvasFromShared();
    if (!s_uid && g_shared && g_shared->rvaSlotUid > 0)
        s_uid = FindMethodByRva(static_cast<uintptr_t>(g_shared->rvaSlotUid), 2, &s_rzClass);
    if (!s_uid || !s_rzClass) return false;
    void* inst = FindSingletonInstance(s_rzClass);
    if (!inst) return false;
    *rzInstance = inst;
    *uidMethod = s_uid;
    return true;
}

static uint64_t SlotUid(void* rz, void* uidMethod, int32_t slotType, int32_t index, bool* failed)
{
    void* args[2] = { &slotType, &index };
    void* exc = nullptr;
    void* boxed = il2cpp_runtime_invoke(uidMethod, rz, args, &exc);
    if (exc || !boxed) { *failed = true; return 0; }
    return *reinterpret_cast<uint64_t*>(reinterpret_cast<uint8_t*>(boxed) + 0x10);
}

static const int32_t kSlotInventory = 1; // ESlotType.INVENTORY
static const int32_t kSlotStash     = 2; // ESlotType.STASH
static const int32_t kMaxInventorySlots = 260; // InventoryInfoData rows
static const int32_t kMaxStashSlots     = 400;

// Stash slot stats from Stash.kcc(i) -> StashCache -> StashSaveData { ItemUniqueId, IsUnLock }.
static bool StashSlotStats(int32_t* used, int32_t* unlocked, int32_t* total, bool* unlockMask = nullptr)
{
    static void* s_kcc = nullptr;
    static void* s_saveField = nullptr;
    static void* s_uidField = nullptr;
    static void* s_unlockField = nullptr;
    if (!s_kcc && g_shared && g_shared->rvaStashCache > 0)
        s_kcc = FindMethodByRva(static_cast<uintptr_t>(g_shared->rvaStashCache), 1, nullptr);
    if (!s_kcc) return false;

    for (int32_t i = 0; i < kMaxStashSlots; ++i)
    {
        int32_t index = i;
        void* args[1] = { &index };
        void* exc = nullptr;
        void* cache = il2cpp_runtime_invoke(s_kcc, nullptr, args, &exc);
        if (exc || !cache) break;
        if (!s_saveField)
        {
            void* iter = nullptr;
            void* f = nullptr;
            while ((f = il2cpp_class_get_fields(il2cpp_object_get_class(cache), &iter)) != nullptr)
            {
                char* tn = il2cpp_type_get_name(il2cpp_field_get_type(f));
                bool match = tn && StrContains(tn, "StashSaveData");
                if (tn && il2cpp_free) il2cpp_free(tn);
                if (match) { s_saveField = f; break; }
            }
            if (!s_saveField) return false;
        }
        void* save = nullptr;
        il2cpp_field_get_value(cache, s_saveField, &save);
        if (!save) break;
        if (!s_unlockField)
        {
            void* saveClass = il2cpp_object_get_class(save);
            s_unlockField = il2cpp_class_get_field_from_name(saveClass, "IsUnLock");
            s_uidField = il2cpp_class_get_field_from_name(saveClass, "ItemUniqueId");
            if (!s_unlockField || !s_uidField) return false;
        }
        bool isUnlocked = false;
        uint64_t uid = 0;
        il2cpp_field_get_value(save, s_unlockField, &isUnlocked);
        il2cpp_field_get_value(save, s_uidField, &uid);
        ++*total;
        if (unlockMask) unlockMask[i] = isUnlocked;
        if (isUnlocked) ++*unlocked;
        if (uid) ++*used;
    }
    return *total > 0;
}

// ---------------------------------------------------------------------------------
// Stash organizer categories (page per category, 49 slots per page).
//   0 soul stones  1 decorations  2 engravings  3 inscriptions
//   4 main + sub weapons  5 helmet/armor/gloves/boots  6 amulet/earring/ring/bracer
// ItemInfoData: ItemKey +0x30, ITEMTYPE +0x34, GRADE +0x38, PARTS +0x40, Level +0x6C,
// MaxStack +0x88. Item (wh.vc.va): ItemInfoData +0x10, MaterialInfoData +0x18 (type +0x34).
// ---------------------------------------------------------------------------------
static const int32_t kStashPageSize = 49;
static const int32_t kOrganizePages = 7;

struct SlotItemInfo
{
    uint64_t uid;
    int32_t  key, itemType, grade, parts, level, maxStack, materialType, category;
};

static bool ReadSlotItem(uint64_t uid, SlotItemInfo* out)
{
    *out = {};
    out->uid = uid;
    out->category = -1;
    out->materialType = -1;
    void* item = ItemByUid(uid);
    uint8_t* info = item ? *reinterpret_cast<uint8_t**>(reinterpret_cast<uint8_t*>(item) + 0x10) : nullptr;
    if (!info) return false;
    out->key = *reinterpret_cast<int32_t*>(info + 0x30);
    out->itemType = *reinterpret_cast<int32_t*>(info + 0x34);
    out->grade = *reinterpret_cast<int32_t*>(info + 0x38);
    out->parts = *reinterpret_cast<int32_t*>(info + 0x40);
    out->level = *reinterpret_cast<int32_t*>(info + 0x6C);
    out->maxStack = *reinterpret_cast<int32_t*>(info + 0x88);
    uint8_t* mat = *reinterpret_cast<uint8_t**>(reinterpret_cast<uint8_t*>(item) + 0x18);
    if (mat) out->materialType = *reinterpret_cast<int32_t*>(mat + 0x34);

    if (out->itemType == 1)   // MATERIAL
    {
        switch (out->materialType)
        {
        case 1: out->category = 0; break;   // SOULSTONE
        case 0: out->category = 1; break;   // DECORATION
        case 2: out->category = 2; break;   // ENGRAVING
        case 3: out->category = 3; break;   // INSCRIPTION
        default: break;
        }
    }
    else if (out->itemType == 2)   // GEAR
    {
        if (out->parts == 1 || out->parts == 2) out->category = 4;
        else if (out->parts >= 3 && out->parts <= 6) out->category = 5;
        else if (out->parts >= 7 && out->parts <= 10) out->category = 6;
    }
    return true;
}

// Command 19: list every used stash slot with its organizer category (read-only).
static void RunStashList()
{
    char* text = g_heroScan->text;
    const size_t cap = sizeof(g_heroScan->text);
    void* rz = nullptr;
    void* uidMethod = nullptr;
    if (!ResolveSlotApi(&rz, &uidMethod))
    {
        size_t n = ScanAppend(text, cap, 0, "ERROR: slot API not available.\r\n");
        g_heroScan->length = static_cast<int32_t>(n);
        g_heroScan->done = -1;
        return;
    }
    size_t pos = ScanAppend(text, cap, 0, "=== Stash list ===\r\n");
    int32_t used = 0;
    for (int32_t i = 0; i < kMaxStashSlots; ++i)
    {
        bool failed = false;
        uint64_t uid = SlotUid(rz, uidMethod, kSlotStash, i, &failed);
        if (failed) break;
        if (!uid) continue;
        ++used;
        SlotItemInfo it;
        ReadSlotItem(uid, &it);
        pos = ScanAppend(text, cap, pos, "STASH slot=%3d page=%d uid=%llX key=%d type=%d parts=%d mat=%d grade=%d lv=%d max=%d cat=%d\r\n",
            i, i / kStashPageSize + 1, (unsigned long long)uid, it.key, it.itemType, it.parts, it.materialType,
            it.grade, it.level, it.maxStack, it.category);
    }
    pos = ScanAppend(text, cap, pos, "Used: %d\r\n", used);
    g_heroScan->length = static_cast<int32_t>(pos);
    g_heroScan->done = 1;
}

// Command 14: read-only listing of inventory items (level / grade / key) and stash usage.
static void RunInventoryScan()
{
    char* text = g_heroScan->text;
    const size_t cap = sizeof(g_heroScan->text);
    size_t pos = ScanAppend(text, cap, 0, "=== Inventory scan (read-only) ===\r\n");

    void* rz = nullptr;
    void* uidMethod = nullptr;
    if (!ResolveSlotApi(&rz, &uidMethod))
    {
        pos = ScanAppend(text, cap, pos, "ERROR: slot API not available on this build (enter the game first).\r\n");
        g_heroScan->length = static_cast<int32_t>(pos);
        g_heroScan->done = -1;
        return;
    }

    int32_t items = 0, lv90 = 0;
    for (int32_t i = 0; i < kMaxInventorySlots; ++i)
    {
        bool failed = false;
        uint64_t uid = SlotUid(rz, uidMethod, kSlotInventory, i, &failed);
        if (failed) break;
        if (!uid) continue;
        void* item = ItemByUid(uid);
        void* info = item ? *reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(item) + 0x10) : nullptr;
        if (!info)
        {
            pos = ScanAppend(text, cap, pos, "INV slot=%3d uid=0x%llX (item data not found)\r\n", i, (unsigned long long)uid);
            continue;
        }
        uint8_t* p = reinterpret_cast<uint8_t*>(info);
        int32_t key = *reinterpret_cast<int32_t*>(p + 0x30);
        int32_t type = *reinterpret_cast<int32_t*>(p + 0x34);
        int32_t grade = *reinterpret_cast<int32_t*>(p + 0x38);
        int32_t level = *reinterpret_cast<int32_t*>(p + 0x6C);
        ++items;
        if (level >= 90) ++lv90;
        pos = ScanAppend(text, cap, pos, "INV slot=%3d key=%d %s grade=%d (%s) lv=%d%s\r\n",
            i, key, type == 2 ? "GEAR" : type == 1 ? "MATERIAL" : type == 0 ? "BOX" : "?",
            grade, GradeTypeName(grade), level, level >= 90 ? "  <- move to stash" : "");
    }

    // Stash size differs per player (slots are unlocked over time): count unlocked slots only.
    int32_t stashUsed = 0, stashUnlocked = 0, stashTotal = 0;
    bool unlockKnown = StashSlotStats(&stashUsed, &stashUnlocked, &stashTotal);
    if (unlockKnown)
        pos = ScanAppend(text, cap, pos,
            "\r\nInventory: %d item(s), %d at Lv90+.  Stash: %d/%d unlocked slot(s) used, %d free (%d locked).\r\n",
            items, lv90, stashUsed, stashUnlocked, stashUnlocked - stashUsed, stashTotal - stashUnlocked);
    else
        pos = ScanAppend(text, cap, pos, "\r\nInventory: %d item(s), %d at Lv90+.  Stash: unlock state unavailable.\r\n",
            items, lv90);
    g_heroScan->length = static_cast<int32_t>(pos);
    g_heroScan->done = 1;
}

// ---------------------------------------------------------------------------------
// Main-thread jobs. Moving items touches Unity UI, which only works on the main thread,
// so SlotInteractionManager.Update (called every frame by Unity) is detoured and runs
// queued jobs before the original Update.
// ---------------------------------------------------------------------------------

typedef void (*SimUpdateFn)(void* self, const void* method);
static SimUpdateFn   g_origSimUpdate   = nullptr;
static bool          g_simHookTried    = false;

// Auto-stash job state (worker thread queues, main thread executes).
static volatile LONG g_stashJobActive  = 0;
static int32_t       g_stashJobMinLevel = 90;
static int32_t       g_stashJobIndex   = 0;
static int32_t       g_stashJobMoved   = 0;
static int32_t       g_stashJobFailed  = 0;
static int32_t       g_stashJobFrame   = 0;
static char          g_stashJobError[128] = {};

struct SlotActionResultNative   // TaskbarHero.SlotActionResult
{
    int32_t destinationSlotType;
    int32_t action;
    bool    allowTabSwitch;
};

static const int32_t kActionMoveToStash = 4; // ESlotAction.MoveToStash

static void* MethodFromSharedRva(int32_t rva, int argCount)
{
    return rva > 0 ? FindMethodByRva(static_cast<uintptr_t>(rva), argCount, nullptr) : nullptr;
}

// One Lv>=N inventory item per call: rz.ikc -> ItemSlot, SlotInteractionManager.inj ->
// context, then inm with Destination = STASH / Action = MoveToStash — the same calls the
// game makes when the player moves a slot. Returns false when the scan is finished.
static bool StashJobStep(void* sim)
{
    static void* s_slotObj = nullptr;
    static void* s_ctx = nullptr;
    static void* s_action = nullptr;
    if (!s_slotObj) s_slotObj = MethodFromSharedRva(g_shared ? g_shared->rvaSlotObj : 0, 2);
    if (!s_ctx)     s_ctx     = MethodFromSharedRva(g_shared ? g_shared->rvaSlotCtx : 0, 3);
    if (!s_action)  s_action  = MethodFromSharedRva(g_shared ? g_shared->rvaSlotAction : 0, 3);

    void* rz = nullptr;
    void* uidMethod = nullptr;
    if (!s_slotObj || !s_ctx || !s_action || !ResolveSlotApi(&rz, &uidMethod))
    {
        strcpy_s(g_stashJobError, "slot API not resolved on this build");
        return false;
    }

    for (; g_stashJobIndex < kMaxInventorySlots; ++g_stashJobIndex)
    {
        int32_t index = g_stashJobIndex;
        bool failed = false;
        uint64_t uid = SlotUid(rz, uidMethod, kSlotInventory, index, &failed);
        if (failed) return false;          // past the last inventory slot
        if (!uid) continue;

        void* item = ItemByUid(uid);
        void* info = item ? *reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(item) + 0x10) : nullptr;
        if (!info) continue;
        int32_t level = *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(info) + 0x6C);
        if (level < g_stashJobMinLevel) continue;

        int32_t slotType = kSlotInventory;
        void* exc = nullptr;
        void* argsSlot[2] = { &slotType, &index };
        void* slot = il2cpp_runtime_invoke(s_slotObj, rz, argsSlot, &exc);
        if (exc || !slot)
        {
            ++g_stashJobFailed;
            strcpy_s(g_stashJobError, "inventory slot UI not found (open the HERO bag)");
            ++g_stashJobIndex;
            return true;
        }

        bool flag = false;
        void* argsCtx[3] = { slot, &flag, &uid };
        exc = nullptr;
        void* ctxBoxed = il2cpp_runtime_invoke(s_ctx, sim, argsCtx, &exc);
        if (exc || !ctxBoxed)
        {
            ++g_stashJobFailed;
            strcpy_s(g_stashJobError, "could not build the slot action context");
            ++g_stashJobIndex;
            return true;
        }

        SlotActionResultNative result = { kSlotStash, kActionMoveToStash, true };
        void* argsMove[3] = { &result, slot, reinterpret_cast<uint8_t*>(ctxBoxed) + 0x10 };
        exc = nullptr;
        il2cpp_runtime_invoke(s_action, sim, argsMove, &exc);
        if (exc) { ++g_stashJobFailed; strcpy_s(g_stashJobError, "move call threw an exception"); }
        else ++g_stashJobMoved;

        ++g_stashJobIndex;
        return true;                       // one item per step; let the game settle
    }
    return false;
}

// ---------------------------------------------------------------------------------
// Auto open boxes: every stage box (StageBox) with unopened boxes is left-clicked through
// its own click handler — the same call as a player click, so the game's checks
// (inventory full, box busy) still apply. A box found waits 2s, then one box opens every 2s.
// ---------------------------------------------------------------------------------
static volatile bool g_autoOpenBoxes = false;
static const ULONGLONG kBoxOpenIntervalMs = 2000;

struct BoxApi
{
    void* stageBoxClass = nullptr;
    void* findObjects = nullptr;   // UnityEngine.Object.FindObjectsOfType(Type)
    void* typeObject = nullptr;    // typeof(StageBox)
    void* click = nullptr;         // StageBox.(PointerEventData.InputButton) click handler
    void* count = nullptr;         // static int (EBoxType, EContentType)
    void* boxTypeField = nullptr;
    void* contentTypeField = nullptr;
};
static BoxApi g_box;
static char   g_boxError[128] = {};

static bool ResolveBoxApi()
{
    if (g_box.findObjects && g_box.typeObject && g_box.click && g_box.count) return true;
    if (!g_domain || !il2cpp_class_get_type || !il2cpp_type_get_object || !il2cpp_method_get_param)
    {
        strcpy_s(g_boxError, "il2cpp type API missing");
        return false;
    }
    if (!g_box.stageBoxClass) g_box.stageBoxClass = FindClass(g_domain, "TaskbarHero.UI", "StageBox");
    if (!g_box.stageBoxClass) { strcpy_s(g_boxError, "StageBox class not found"); return false; }

    if (!g_box.findObjects)
    {
        void* objClass = FindClass(g_domain, "UnityEngine", "Object");
        g_box.findObjects = objClass ? il2cpp_class_get_method_from_name(objClass, "FindObjectsOfType", 1) : nullptr;
    }
    if (!g_box.typeObject)
        g_box.typeObject = il2cpp_type_get_object(il2cpp_class_get_type(g_box.stageBoxClass));
    if (!g_box.click)
    {
        void* iter = nullptr;
        void* m = nullptr;
        while ((m = il2cpp_class_get_methods(g_box.stageBoxClass, &iter)) != nullptr)
        {
            if (il2cpp_method_get_param_count(m) != 1) continue;
            char* tn = il2cpp_type_get_name(il2cpp_method_get_param(m, 0));
            bool match = tn && StrContains(tn, "InputButton");
            if (tn && il2cpp_free) il2cpp_free(tn);
            if (match) { g_box.click = m; break; }
        }
    }
    if (!g_box.count && g_shared && g_shared->rvaBoxCount > 0)
        g_box.count = FindMethodByRva(static_cast<uintptr_t>(g_shared->rvaBoxCount), 2, nullptr);
    if (!g_box.boxTypeField)
        g_box.boxTypeField = il2cpp_class_get_field_from_name(g_box.stageBoxClass, "m_boxType");
    if (!g_box.contentTypeField)
        g_box.contentTypeField = il2cpp_class_get_field_from_name(g_box.stageBoxClass, "m_contentType");

    if (!g_box.findObjects)      strcpy_s(g_boxError, "FindObjectsOfType not found");
    else if (!g_box.typeObject)  strcpy_s(g_boxError, "typeof(StageBox) not available");
    else if (!g_box.click)       strcpy_s(g_boxError, "box click handler not found");
    else if (!g_box.count)       strcpy_s(g_boxError, "box count method not resolved on this build");
    else if (!g_box.boxTypeField || !g_box.contentTypeField) strcpy_s(g_boxError, "box type fields not found");
    else return true;
    return false;
}

static int32_t BoxCount(void* stageBox)
{
    int32_t boxType = 0, contentType = 0;
    il2cpp_field_get_value(stageBox, g_box.boxTypeField, &boxType);
    il2cpp_field_get_value(stageBox, g_box.contentTypeField, &contentType);
    void* args[2] = { &boxType, &contentType };
    void* exc = nullptr;
    void* boxed = il2cpp_runtime_invoke(g_box.count, nullptr, args, &exc);
    if (exc || !boxed) return 0;
    return *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(boxed) + 0x10);
}

static volatile bool g_autoOrganize = false;   // organize the stash after boxes are opened
static ULONGLONG     g_orgDueAt = 0;
static volatile LONG g_orgAutoRuns = 0;
static volatile LONG g_orgRequest = 0;   // worker asks the main thread to start a run
static volatile LONG g_orgBeginFailed = 0;
static ULONGLONG g_boxNextCheck  = 0;
static ULONGLONG g_boxReadyAt    = 0;      // 0 = no box seen yet
static ULONGLONG g_boxPauseUntil = 0;
static void*     g_boxLastTarget = nullptr;
static int32_t   g_boxLastCount  = 0;
static int32_t   g_boxNoProgress = 0;
static volatile LONG g_boxOpened = 0;

static void BoxJobTick()
{
    ULONGLONG now = GetTickCount64();
    if (now < g_boxNextCheck || now < g_boxPauseUntil) return;
    g_boxNextCheck = now + 250;
    if (!ResolveBoxApi()) return;

    void* args[1] = { g_box.typeObject };
    void* exc = nullptr;
    void* arr = il2cpp_runtime_invoke(g_box.findObjects, nullptr, args, &exc);
    if (exc || !arr) return;

    void* target = nullptr;
    int32_t count = 0;
    uint32_t n = il2cpp_array_length(arr);
    void** elems = reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(arr) + 0x20);
    for (uint32_t i = 0; i < n && !target; ++i)
    {
        if (!elems[i]) continue;
        int32_t c = BoxCount(elems[i]);
        if (c > 0) { target = elems[i]; count = c; }
    }
    if (!target)
    {
        g_boxReadyAt = 0;
        g_boxNoProgress = 0;
        return;
    }
    if (!g_boxReadyAt) { g_boxReadyAt = now + kBoxOpenIntervalMs; return; }   // found: wait 2s
    if (now < g_boxReadyAt) return;

    // Count not going down (inventory full, popup open...): back off instead of spamming.
    if (target == g_boxLastTarget && count >= g_boxLastCount)
    {
        if (++g_boxNoProgress >= 5)
        {
            g_boxNoProgress = 0;
            g_boxReadyAt = 0;
            g_boxPauseUntil = now + 30000;
            return;
        }
    }
    else
        g_boxNoProgress = 0;

    int32_t leftButton = 0;
    void* clickArgs[1] = { &leftButton };
    exc = nullptr;
    il2cpp_runtime_invoke(g_box.click, target, clickArgs, &exc);
    if (!exc)
    {
        InterlockedIncrement(&g_boxOpened);
        g_orgDueAt = now + 4000;   // organize the stash once no box was opened for 4 s
    }
    g_boxLastTarget = target;
    g_boxLastCount = count;
    g_boxReadyAt = now + kBoxOpenIntervalMs;                                  // next one in 2s
}

static void* volatile g_stashJobSim = nullptr;   // SlotInteractionManager instance for the job

// ---------------------------------------------------------------------------------
// UI button press (main thread): finds a UI panel by class, reads one of its ButtonBase
// fields, and invokes the underlying UnityEngine.UI.Button.onClick — exactly what the
// game's own click does, so its checks and animations still run. Resolved by stable
// field names (SortingButton / button), not by obfuscated method names.
// ---------------------------------------------------------------------------------
static volatile LONG g_uiClickPending = 0;       // 1 = queued, 0 = done
static const char*   g_uiClickClass   = nullptr; // e.g. "UI_Stash"
static const char*   g_uiClickField   = nullptr; // e.g. "SortingButton"
static int32_t       g_uiClickResult  = 0;       // 1 ok, <0 error code
static char          g_uiClickError[128] = {};

static void* FindFieldOnHierarchy(void* klass, const char* name)
{
    for (void* k = klass; k; k = il2cpp_class_get_parent ? il2cpp_class_get_parent(k) : nullptr)
    {
        void* f = il2cpp_class_get_field_from_name(k, name);
        if (f) return f;
        if (!il2cpp_class_get_parent) break;
    }
    return nullptr;
}

static void* FindMethodOnHierarchy(void* klass, const char* name, int argc)
{
    for (void* k = klass; k; k = il2cpp_class_get_parent ? il2cpp_class_get_parent(k) : nullptr)
    {
        void* m = il2cpp_class_get_method_from_name(k, name, argc);
        if (m) return m;
        if (!il2cpp_class_get_parent) break;
    }
    return nullptr;
}

static bool g_uiClickSortedDirect = false;

static void UiClickStep()
{
    g_uiClickResult = -1;
    g_uiClickSortedDirect = false;

    // Stash sort: call the Sort button's own static handler (wh.Stash.kcb on 1.2.7/1.2.8),
    // which sorts every page and works without the stash window having been opened.
    static void* s_sortAll = nullptr;
    if (!s_sortAll && g_shared && g_shared->rvaStashSortAll > 0 && Streq(g_uiClickField, "SortingButton"))
        s_sortAll = FindMethodByRva(static_cast<uintptr_t>(g_shared->rvaStashSortAll), 0, nullptr);
    if (s_sortAll && Streq(g_uiClickField, "SortingButton"))
    {
        void* exc = nullptr;
        il2cpp_runtime_invoke(s_sortAll, nullptr, nullptr, &exc);
        if (exc)
        {
            strcpy_s(g_uiClickError, "the game's sort threw an exception");
            g_uiClickResult = -5;
            return;
        }
        g_uiClickSortedDirect = true;
        g_uiClickResult = 1;
        return;
    }

    void* panelClass = FindClass(g_domain, "TaskbarHero", g_uiClickClass);
    void* objClass = FindClass(g_domain, "UnityEngine", "Object");
    void* findAll = objClass ? il2cpp_class_get_method_from_name(objClass, "FindObjectsOfType", 2) : nullptr;
    if (!panelClass || !findAll || !il2cpp_class_get_type || !il2cpp_type_get_object)
    {
        strcpy_s(g_uiClickError, "UI class or FindObjectsOfType not found");
        return;
    }
    void* typeObj = il2cpp_type_get_object(il2cpp_class_get_type(panelClass));
    bool includeInactive = true;
    void* args[2] = { typeObj, &includeInactive };
    void* exc = nullptr;
    void* arr = il2cpp_runtime_invoke(findAll, nullptr, args, &exc);
    if (exc || !arr || il2cpp_array_length(arr) == 0)
    {
        strcpy_s(g_uiClickError, "panel not found (open it once in game)");
        g_uiClickResult = -2;
        return;
    }
    void* panel = *reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(arr) + 0x20);

    void* btnField = FindFieldOnHierarchy(il2cpp_object_get_class(panel), g_uiClickField);
    void* buttonBase = nullptr;
    if (btnField) il2cpp_field_get_value(panel, btnField, &buttonBase);
    void* uiButtonField = buttonBase ? FindFieldOnHierarchy(il2cpp_object_get_class(buttonBase), "button") : nullptr;
    void* uiButton = nullptr;
    if (uiButtonField) il2cpp_field_get_value(buttonBase, uiButtonField, &uiButton);
    if (!uiButton)
    {
        strcpy_s(g_uiClickError, "button object not found");
        g_uiClickResult = -3;
        return;
    }
    void* getOnClick = FindMethodOnHierarchy(il2cpp_object_get_class(uiButton), "get_onClick", 0);
    exc = nullptr;
    void* onClick = getOnClick ? il2cpp_runtime_invoke(getOnClick, uiButton, nullptr, &exc) : nullptr;
    void* invoke = onClick ? FindMethodOnHierarchy(il2cpp_object_get_class(onClick), "Invoke", 0) : nullptr;
    if (exc || !invoke)
    {
        strcpy_s(g_uiClickError, "onClick not available");
        g_uiClickResult = -4;
        return;
    }
    exc = nullptr;
    il2cpp_runtime_invoke(invoke, onClick, nullptr, &exc);
    if (exc)
    {
        strcpy_s(g_uiClickError, "the game's click handler threw an exception");
        g_uiClickResult = -5;
        return;
    }
    g_uiClickResult = 1;
}

// ---------------------------------------------------------------------------------
// Slot moves: rz.ije(MoveRequest, Action<MoveResult>) is what SlotInteractionManager calls
// when the player drags a slot onto another (DRAG = swap / stack onto the target).
// ---------------------------------------------------------------------------------
struct MoveRequestNative   // TaskbarHero.MoveRequest
{
    int32_t sourceType;
    int32_t sourceIndex;
    int32_t targetType;
    int32_t targetIndex;
    int32_t moveType;      // EMoveType.DRAG = 0
    int32_t quantity;
};

static void* g_moveMethod = nullptr;
static void* g_moveCbClass = nullptr;
static void* g_moveCbMethod = nullptr;
static void* g_moveCbTarget = nullptr;

// Resolves rz.ije and a callback: SlotInteractionManager's own MoveResult handler (shows the
// game's toast when a move is refused), the same kind of callback the drag & drop UI passes.
static bool ResolveMoveApi()
{
    if (!g_moveMethod && g_shared && g_shared->rvaSlotMove > 0)
        g_moveMethod = FindMethodByRva(static_cast<uintptr_t>(g_shared->rvaSlotMove), 2, nullptr);
    if (!g_moveMethod || !il2cpp_method_get_param) return false;
    if (!g_moveCbClass) g_moveCbClass = il2cpp_class_from_type(il2cpp_method_get_param(g_moveMethod, 1));
    void* simClass = FindClass(g_domain, "TaskbarHero", "SlotInteractionManager");
    if (!simClass || !g_moveCbClass) return false;
    g_moveCbTarget = FindSingletonInstance(simClass);
    if (!g_moveCbMethod)
    {
        // Prefer the handler that two obfuscated names share (toast-on-failure); else the first.
        void* first = nullptr;
        void* iter = nullptr;
        void* m = nullptr;
        void* candidates[8] = {};
        int n = 0;
        while ((m = il2cpp_class_get_methods(simClass, &iter)) != nullptr && n < 8)
        {
            if (il2cpp_method_get_param_count(m) != 1) continue;
            char* tn = il2cpp_type_get_name(il2cpp_method_get_param(m, 0));
            bool match = tn && StrContains(tn, "MoveResult");
            if (tn && il2cpp_free) il2cpp_free(tn);
            if (match) candidates[n++] = m;
        }
        for (int i = 0; i < n && !g_moveCbMethod; ++i)
            for (int j = i + 1; j < n; ++j)
                if (MethodPointer(candidates[i]) == MethodPointer(candidates[j])) { g_moveCbMethod = candidates[i]; break; }
        if (!g_moveCbMethod && n > 0) g_moveCbMethod = candidates[0];
        (void)first;
    }
    return g_moveMethod && g_moveCbMethod && g_moveCbTarget;
}

// Main thread only. Returns false if the call could not be made or threw.
static bool MoveSlot(int32_t srcType, int32_t srcIndex, int32_t dstType, int32_t dstIndex, int32_t quantity)
{
    void* rz = nullptr;
    void* uidMethod = nullptr;
    if (!ResolveSlotApi(&rz, &uidMethod) || !ResolveMoveApi()) return false;

    // A fresh delegate per move: it lives on this stack frame during the call, and the
    // game keeps its own reference if it stores it.
    void* cb = il2cpp_object_new(g_moveCbClass);
    void* ctor = cb ? il2cpp_class_get_method_from_name(g_moveCbClass, ".ctor", 2) : nullptr;
    if (!ctor) return false;
    void* methodInfo = g_moveCbMethod;
    void* ctorArgs[2] = { g_moveCbTarget, &methodInfo };
    void* exc = nullptr;
    il2cpp_runtime_invoke(ctor, cb, ctorArgs, &exc);
    if (exc) return false;

    MoveRequestNative req = { srcType, srcIndex, dstType, dstIndex, 0, quantity };
    void* args[2] = { &req, cb };
    exc = nullptr;
    il2cpp_runtime_invoke(g_moveMethod, rz, args, &exc);
    return exc == nullptr;
}

static bool InstallSimUpdateHook();

// ---------------------------------------------------------------------------------
// Stash organizer (main thread, one DRAG move per step):
//   1. import: bag gear Lv90+ and bag soul stones / decorations / engravings / inscriptions
//      go into free stash slots (their category page first)
//   2. merge: same-key material stacks are dragged onto each other once (the game stacks
//      up to MaxStack = 5 when there is room)
//   3. layout: page 1..7 = categories 0..6 (see ReadSlotItem); within a page gear is ordered
//      by part, grade (high first), level, key; materials by key. Items that don't fit their
//      page, and uncategorized items, fill pages 8+ first, then any free slot.
// Every item gets exactly one target slot; placing targets in slot order with swaps
// converges in at most one move per item.
// ---------------------------------------------------------------------------------
static const int32_t kOrgMaxSlots = kMaxStashSlots;
static volatile LONG g_orgActive = 0;
static int32_t  g_orgPhase = 0;          // 0 import, 1 merge, 2 layout, 3 done
static int32_t  g_orgFrame = 0;
static int32_t  g_orgSlots = 0;          // stash slots that exist
static bool     g_orgUnlocked[kOrgMaxSlots];
static uint64_t g_orgTarget[kOrgMaxSlots];   // desired uid per slot (0 = empty)
static bool     g_orgTargetBuilt = false;
static int32_t  g_orgMoves = 0, g_orgFails = 0, g_orgImported = 0, g_orgMerged = 0;
static int32_t  g_orgSteps = 0;
static char     g_orgError[128] = {};
static uint64_t g_orgStuck[64];
static int32_t  g_orgStuckCount = 0;
static uint64_t g_orgMergeTried[256];
static int32_t  g_orgMergeTriedCount = 0;

static bool OrgIsStuck(uint64_t uid)
{
    for (int32_t i = 0; i < g_orgStuckCount; ++i) if (g_orgStuck[i] == uid) return true;
    return false;
}

static int32_t OrgSortRank(const SlotItemInfo& a)
{
    return a.itemType == 2 ? a.parts : 0;
}

static bool OrgLess(const SlotItemInfo& a, const SlotItemInfo& b)
{
    if (OrgSortRank(a) != OrgSortRank(b)) return OrgSortRank(a) < OrgSortRank(b);
    if (a.itemType == 2)
    {
        if (a.grade != b.grade) return a.grade > b.grade;
        if (a.level != b.level) return a.level > b.level;
    }
    if (a.key != b.key) return a.key < b.key;
    return a.uid < b.uid;
}

static bool OrgSnapshot(void* rz, void* uidMethod, uint64_t* uids, SlotItemInfo* infos, int32_t* count)
{
    int32_t n = 0;
    for (int32_t i = 0; i < g_orgSlots; ++i)
    {
        bool failed = false;
        uids[i] = SlotUid(rz, uidMethod, kSlotStash, i, &failed);
        if (failed) return false;
        if (uids[i] && infos) ReadSlotItem(uids[i], &infos[n++]);
    }
    if (count) *count = n;
    return true;
}

static void OrgBuildTargets(SlotItemInfo* items, int32_t n)
{
    // simple insertion sort by (category page, order)
    for (int32_t i = 1; i < n; ++i)
    {
        SlotItemInfo v = items[i];
        int32_t j = i - 1;
        while (j >= 0)
        {
            int32_t ca = items[j].category < 0 ? 99 : items[j].category;
            int32_t cb = v.category < 0 ? 99 : v.category;
            bool greater = ca != cb ? ca > cb : OrgLess(v, items[j]);
            if (!greater) break;
            items[j + 1] = items[j];
            --j;
        }
        items[j + 1] = v;
    }
    for (int32_t i = 0; i < g_orgSlots; ++i) g_orgTarget[i] = 0;

    static uint64_t leftovers[kOrgMaxSlots];
    int32_t nLeft = 0;
    int32_t next[kOrganizePages] = {};
    for (int32_t i = 0; i < n; ++i)
    {
        const SlotItemInfo& it = items[i];
        if (OrgIsStuck(it.uid)) continue;
        bool placed = false;
        if (it.category >= 0 && it.category < kOrganizePages)
        {
            int32_t page = it.category;
            for (int32_t& k = next[page]; k < kStashPageSize && !placed; ++k)
            {
                int32_t slot = page * kStashPageSize + k;
                if (slot >= g_orgSlots || !g_orgUnlocked[slot]) continue;
                g_orgTarget[slot] = it.uid;
                placed = true;
            }
        }
        if (!placed) leftovers[nLeft++] = it.uid;
    }
    // leftovers: pages 8+ first, then any free unlocked slot from the last page backwards
    int32_t li = 0;
    for (int32_t slot = kOrganizePages * kStashPageSize; slot < g_orgSlots && li < nLeft; ++slot)
        if (g_orgUnlocked[slot] && !g_orgTarget[slot]) g_orgTarget[slot] = leftovers[li++];
    for (int32_t slot = g_orgSlots - 1; slot >= 0 && li < nLeft; --slot)
        if (g_orgUnlocked[slot] && !g_orgTarget[slot]) g_orgTarget[slot] = leftovers[li++];
    // stuck items stay where they are
    g_orgTargetBuilt = true;
}

static int32_t OrgFreeSlotFor(const uint64_t* uids, int32_t category)
{
    if (category >= 0 && category < kOrganizePages)
        for (int32_t k = 0; k < kStashPageSize; ++k)
        {
            int32_t slot = category * kStashPageSize + k;
            if (slot < g_orgSlots && g_orgUnlocked[slot] && !uids[slot]) return slot;
        }
    for (int32_t slot = g_orgSlots - 1; slot >= 0; --slot)
        if (g_orgUnlocked[slot] && !uids[slot]) return slot;
    return -1;
}

// One step; returns false when finished.
static bool OrganizeStep()
{
    void* rz = nullptr;
    void* uidMethod = nullptr;
    if (!ResolveSlotApi(&rz, &uidMethod) || !ResolveMoveApi())
    {
        strcpy_s(g_orgError, "slot move API not resolved on this build");
        return false;
    }
    if (++g_orgSteps > 4 * kOrgMaxSlots)
    {
        strcpy_s(g_orgError, "step limit reached");
        return false;
    }
    static uint64_t uids[kOrgMaxSlots];
    static SlotItemInfo infos[kOrgMaxSlots];
    int32_t n = 0;

    if (g_orgPhase == 0)   // import from the bag
    {
        if (!OrgSnapshot(rz, uidMethod, uids, nullptr, nullptr)) { strcpy_s(g_orgError, "stash read failed"); return false; }
        for (int32_t i = 0; i < kMaxInventorySlots; ++i)
        {
            bool failed = false;
            uint64_t uid = SlotUid(rz, uidMethod, kSlotInventory, i, &failed);
            if (failed) break;
            if (!uid) continue;
            SlotItemInfo it;
            if (!ReadSlotItem(uid, &it)) continue;
            bool want = (it.itemType == 2 && it.level >= 90 && it.category >= 0) ||
                        (it.itemType == 1 && it.category >= 0 && it.category <= 3);
            if (!want || OrgIsStuck(uid)) continue;
            int32_t dst = OrgFreeSlotFor(uids, it.category);
            if (dst < 0) { g_orgPhase = 1; return true; }   // stash full
            if (MoveSlot(kSlotInventory, i, kSlotStash, dst, 0)) ++g_orgImported;
            bool f2 = false;
            if (SlotUid(rz, uidMethod, kSlotInventory, i, &f2) == uid)
            {
                ++g_orgFails;
                if (g_orgStuckCount < 64) g_orgStuck[g_orgStuckCount++] = uid;
            }
            return true;
        }
        g_orgPhase = 1;
        return true;
    }

    if (g_orgPhase == 1)   // merge same-key material stacks
    {
        if (!OrgSnapshot(rz, uidMethod, uids, nullptr, nullptr)) { strcpy_s(g_orgError, "stash read failed"); return false; }
        static int32_t stackKey[kOrgMaxSlots];   // material key per slot if stackable, else 0
        for (int32_t i = 0; i < g_orgSlots; ++i)
        {
            stackKey[i] = 0;
            SlotItemInfo it;
            if (uids[i] && ReadSlotItem(uids[i], &it) && it.itemType == 1 && it.maxStack > 1) stackKey[i] = it.key;
        }
        for (int32_t a = 0; a < g_orgSlots; ++a)
        {
            if (!stackKey[a]) continue;
            for (int32_t b = a + 1; b < g_orgSlots; ++b)
            {
                if (stackKey[b] != stackKey[a]) continue;
                bool tried = false;
                for (int32_t t = 0; t < g_orgMergeTriedCount; ++t)
                    if (g_orgMergeTried[t] == (uids[a] ^ (uids[b] << 1))) tried = true;
                if (tried) continue;
                if (g_orgMergeTriedCount < 256) g_orgMergeTried[g_orgMergeTriedCount++] = uids[a] ^ (uids[b] << 1);
                uint64_t before = uids[b];
                MoveSlot(kSlotStash, b, kSlotStash, a, 0);
                bool f = false;
                if (SlotUid(rz, uidMethod, kSlotStash, b, &f) != before &&
                    SlotUid(rz, uidMethod, kSlotStash, a, &f) != before) ++g_orgMerged;   // not a swap
                ++g_orgMoves;
                return true;
            }
        }
        g_orgPhase = 2;
        g_orgTargetBuilt = false;
        return true;
    }

    if (g_orgPhase == 2)   // layout
    {
        if (!OrgSnapshot(rz, uidMethod, uids, infos, &n)) { strcpy_s(g_orgError, "stash read failed"); return false; }
        if (!g_orgTargetBuilt) OrgBuildTargets(infos, n);
        for (int32_t t = 0; t < g_orgSlots; ++t)
        {
            uint64_t want = g_orgTarget[t];
            if (!want || uids[t] == want) continue;
            int32_t src = -1;
            for (int32_t k = 0; k < g_orgSlots; ++k) if (uids[k] == want) { src = k; break; }
            if (src < 0) { g_orgTargetBuilt = false; return true; }   // item vanished/merged: rebuild
            MoveSlot(kSlotStash, src, kSlotStash, t, 0);
            ++g_orgMoves;
            bool f = false;
            if (SlotUid(rz, uidMethod, kSlotStash, t, &f) != want)
            {
                ++g_orgFails;
                if (g_orgStuckCount < 64) g_orgStuck[g_orgStuckCount++] = want;
                g_orgTargetBuilt = false;
            }
            return true;
        }
        g_orgPhase = 3;
    }
    return false;
}

// Prepares the job (reads unlock state); main thread or worker.
static bool OrganizeBegin()
{
    int32_t used = 0, unlocked = 0, total = 0;
    for (int32_t i = 0; i < kOrgMaxSlots; ++i) g_orgUnlocked[i] = false;
    if (!StashSlotStats(&used, &unlocked, &total, g_orgUnlocked))
    {
        strcpy_s(g_orgError, "stash unlock state unavailable");
        return false;
    }
    g_orgSlots = total < kOrgMaxSlots ? total : kOrgMaxSlots;
    g_orgPhase = 0;
    g_orgFrame = 0;
    g_orgMoves = g_orgFails = g_orgImported = g_orgMerged = g_orgSteps = 0;
    g_orgStuckCount = 0;
    g_orgMergeTriedCount = 0;
    g_orgTargetBuilt = false;
    g_orgError[0] = '\0';
    InterlockedExchange(&g_orgActive, 1);
    return true;
}

// Auto organize after boxes: BoxJobTick schedules, the frame hook starts it once boxes are done.

// One-shot main-thread call queued by the worker (probe / organizer helpers).
static void (*volatile g_mainCall)() = nullptr;

static bool RunOnMainThread(void (*fn)(), int timeoutMs)
{
    if (!InstallSimUpdateHook()) return false;
    g_mainCall = fn;
    MemoryBarrier();
    for (int waited = 0; g_mainCall && waited < timeoutMs; waited += 10)
        Sleep(10);
    if (g_mainCall) { g_mainCall = nullptr; return false; }
    return true;
}

// Probe (command 18): one DRAG move, parameters packed by the trainer / test harness.
static int32_t g_probeSrcType, g_probeSrcIdx, g_probeDstType, g_probeDstIdx, g_probeQty;
static bool    g_probeOk;
static void ProbeMoveMain()
{
    g_probeOk = MoveSlot(g_probeSrcType, g_probeSrcIdx, g_probeDstType, g_probeDstIdx, g_probeQty);
}

static void HookedSimUpdate(void* self, const void* method)
{
    bool paused = g_shared && g_shared->trainerPaused;
    if (g_mainCall && !g_shuttingDown && !paused)
    {
        void (*fn)() = g_mainCall;
        fn();
        g_mainCall = nullptr;
    }
    if (g_autoOpenBoxes && !g_shuttingDown && !paused && !g_orgActive) BoxJobTick();
    if (g_orgActive && !g_shuttingDown && !paused)
    {
        if ((++g_orgFrame % 3) == 0 && !OrganizeStep())
            InterlockedExchange(&g_orgActive, 0);
    }
    else if (g_orgRequest && !g_shuttingDown && !paused)
    {
        g_orgDueAt = 0;
        InterlockedExchange(&g_orgBeginFailed, OrganizeBegin() ? 0 : 1);
        InterlockedExchange(&g_orgRequest, 0);
    }
    else if (g_autoOrganize && g_orgDueAt && GetTickCount64() >= g_orgDueAt && !g_shuttingDown && !paused)
    {
        g_orgDueAt = 0;
        if (OrganizeBegin()) InterlockedIncrement(&g_orgAutoRuns);
    }
    if (g_uiClickPending && !g_shuttingDown && !paused)
    {
        UiClickStep();
        InterlockedExchange(&g_uiClickPending, 0);
    }
    if (g_stashJobActive && g_stashJobSim && !g_shuttingDown && !paused)
    {
        // ~6 frames between moves so the game's move animation / save can finish.
        if ((++g_stashJobFrame % 6) == 0 && !StashJobStep(g_stashJobSim))
            InterlockedExchange(&g_stashJobActive, 0);
    }
    g_origSimUpdate(self, method);
}

// Per-frame main-thread hook. Detours InputManager.Update (always active) and falls back to
// SlotInteractionManager.Update, which only runs while that UI is active. The prologue must
// be exactly "push rbx; sub rsp,20h" (40 53 48 83 EC 20), otherwise nothing is patched.
static bool InstallSimUpdateHook()
{
    if (g_origSimUpdate) return true;
    if (g_simHookTried) return false;
    g_simHookTried = true;

    static const uint8_t kPrologue[6] = { 0x40, 0x53, 0x48, 0x83, 0xEC, 0x20 };
    static const char* const kFrameClasses[] = { "InputManager", "SlotInteractionManager" };
    uint8_t* target = nullptr;
    for (const char* name : kFrameClasses)
    {
        void* klass = FindClass(g_domain, "TaskbarHero", name);
        void* update = klass ? il2cpp_class_get_method_from_name(klass, "Update", 0) : nullptr;
        uint8_t* p = static_cast<uint8_t*>(MethodPointer(update));
        if (p && memcmp(p, kPrologue, sizeof(kPrologue)) == 0) { target = p; break; }
    }
    if (!target) return false;

    // Stub within +-2GB of the target so a 5-byte rel32 jmp can reach it.
    uint8_t* stub = nullptr;
    uintptr_t base = reinterpret_cast<uintptr_t>(target) & ~static_cast<uintptr_t>(0xFFFF);
    for (uintptr_t delta = 0x10000; delta < 0x70000000 && !stub; delta += 0x10000)
    {
        for (int dir = -1; dir <= 1 && !stub; dir += 2)
        {
            uintptr_t addr = dir < 0 ? base - delta : base + delta;
            stub = static_cast<uint8_t*>(VirtualAlloc(reinterpret_cast<void*>(addr), 0x1000,
                MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
        }
    }
    if (!stub) return false;

    // stub+0:  jmp [rip+0] -> HookedSimUpdate
    // stub+32: original prologue, then jmp [rip+0] -> target+6 (trampoline)
    auto absJmp = [](uint8_t* at, const void* dest)
    {
        at[0] = 0xFF; at[1] = 0x25; memset(at + 2, 0, 4);
        void* d = const_cast<void*>(dest);
        memcpy(at + 6, &d, 8);
    };
    absJmp(stub, reinterpret_cast<void*>(&HookedSimUpdate));
    uint8_t* tramp = stub + 32;
    memcpy(tramp, kPrologue, sizeof(kPrologue));
    absJmp(tramp + sizeof(kPrologue), target + sizeof(kPrologue));

    // Swap the first 8 bytes in one atomic store (the function is 16-byte aligned) so the
    // main thread never executes a half-written jump. g_origSimUpdate is only set once the
    // patch is in, so a failure here is reported instead of looking "installed".
    if ((reinterpret_cast<uintptr_t>(target) & 7) != 0) return false;
    DWORD old = 0;
    if (!VirtualProtect(target, 8, PAGE_EXECUTE_READWRITE, &old)) return false;
    g_origSimUpdate = reinterpret_cast<SimUpdateFn>(tramp);
    uint8_t patch[8];
    memcpy(patch, target, 8);
    int32_t rel = static_cast<int32_t>(stub - (target + 5));
    patch[0] = 0xE9;
    memcpy(patch + 1, &rel, 4);
    patch[5] = 0x90;
    int64_t value = 0;
    memcpy(&value, patch, 8);
    InterlockedExchange64(reinterpret_cast<volatile LONG64*>(target), value);
    VirtualProtect(target, 8, old, &old);
    FlushInstructionCache(GetCurrentProcess(), target, 8);
    return true;
}

// Background work only runs while the game is not closing. (No frame-based check: a paused
// or hidden UI component must never stall hero locks and trainer commands.)
static bool GameAlive()
{
    return !g_shuttingDown;
}

// Only the real end of the game counts: the main Unity window being destroyed, WM_QUIT, or a
// Windows session end. Other windows on the same thread (popups, splash, IME) are destroyed
// during normal play, and WM_CLOSE can be cancelled, so neither may stop the hook.
static HWND g_unityHwnd = nullptr;

static LRESULT CALLBACK ShutdownCallWndHook(int code, WPARAM wParam, LPARAM lParam)
{
    if (code == HC_ACTION)
    {
        auto* m = reinterpret_cast<CWPSTRUCT*>(lParam);
        if ((m->message == WM_DESTROY && g_unityHwnd && m->hwnd == g_unityHwnd) ||
            (m->message == WM_ENDSESSION && m->wParam))
            g_shuttingDown = true;
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

static LRESULT CALLBACK ShutdownGetMsgHook(int code, WPARAM wParam, LPARAM lParam)
{
    if (code == HC_ACTION)
    {
        auto* m = reinterpret_cast<MSG*>(lParam);
        if (m->message == WM_QUIT)
            g_shuttingDown = true;
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

static BOOL CALLBACK FindUnityWindow(HWND hwnd, LPARAM out)
{
    DWORD pid = 0;
    DWORD tid = GetWindowThreadProcessId(hwnd, &pid);
    char cls[64] = {};
    GetClassNameA(hwnd, cls, sizeof(cls));
    if (pid == GetCurrentProcessId() && strcmp(cls, "UnityWndClass") == 0)
    {
        g_unityHwnd = hwnd;
        *reinterpret_cast<DWORD*>(out) = tid;
        return FALSE;
    }
    return TRUE;
}

// Watch the game window's thread for close / quit messages.
static void InstallShutdownWatch()
{
    DWORD mainThread = 0;
    EnumWindows(FindUnityWindow, reinterpret_cast<LPARAM>(&mainThread));
    if (!mainThread) return;
    SetWindowsHookExW(WH_CALLWNDPROC, ShutdownCallWndHook, nullptr, mainThread);
    SetWindowsHookExW(WH_GETMESSAGE, ShutdownGetMsgHook, nullptr, mainThread);
}

// The worker blocks while a main-thread job runs (auto stash up to 30s, UI click up to 5s);
// keep god mode / hero locks going meanwhile so heroes can't die during the wait.
static void KeepHeroesAlive()
{
    if (g_shuttingDown || (g_shared && g_shared->trainerPaused)) return;
    if (g_godMode) ApplyGodModeAllHeroes();
    for (int32_t heroIndex = 0; heroIndex < 3; ++heroIndex)
        ApplyHeroLocks(heroIndex);
}

// Command 15: move every inventory item with level >= value into the stash (main thread).
static void RunAutoStash()
{
    char* text = g_heroScan->text;
    const size_t cap = sizeof(g_heroScan->text);
    if (!InstallSimUpdateHook())
    {
        size_t n = ScanAppend(text, cap, 0, "ERROR: could not install the per-frame hook on this build.\r\n");
        g_heroScan->length = static_cast<int32_t>(n);
        g_heroScan->done = -1;
        return;
    }
    static void* s_simClass = nullptr;
    if (!s_simClass) s_simClass = FindClass(g_domain, "TaskbarHero", "SlotInteractionManager");
    g_stashJobSim = FindSingletonInstance(s_simClass);
    if (!g_stashJobSim)
    {
        size_t n = ScanAppend(text, cap, 0, "ERROR: SlotInteractionManager not found - open the HERO bag and STASH first.\r\n");
        g_heroScan->length = static_cast<int32_t>(n);
        g_heroScan->done = -1;
        return;
    }

    g_stashJobMinLevel = g_heroScan->value > 0 ? static_cast<int32_t>(g_heroScan->value) : 90;
    g_stashJobIndex = 0;
    g_stashJobMoved = 0;
    g_stashJobFailed = 0;
    g_stashJobFrame = 0;
    g_stashJobError[0] = '\0';
    InterlockedExchange(&g_stashJobActive, 1);

    for (int waited = 0; g_stashJobActive && waited < 30000; waited += 50)
    {
        Sleep(50);
        if ((waited % 100) == 0) KeepHeroesAlive();
    }
    bool timedOut = g_stashJobActive != 0;
    InterlockedExchange(&g_stashJobActive, 0);

    size_t n = ScanAppend(text, cap, 0, "%s Auto stash (Lv%d+): moved %d, failed %d%s%s%s\r\n",
        g_stashJobFailed == 0 && !timedOut ? "OK:" : "WARN:", g_stashJobMinLevel, g_stashJobMoved, g_stashJobFailed,
        timedOut ? " (timed out - is the game window running?)" : "",
        g_stashJobError[0] ? " - last error: " : "", g_stashJobError);
    g_heroScan->length = static_cast<int32_t>(n);
    g_heroScan->done = g_stashJobFailed == 0 && !timedOut ? 1 : -1;
}

static void RunHeroCommand()
{
    if (!g_heroScan) return;
    g_heroScan->done = 0;
    g_heroScan->length = 0;
    memset(g_heroScan->text, 0, sizeof(g_heroScan->text));

    if (g_heroScan->command == 21)
    {
        g_autoOrganize = g_heroScan->value != 0.0f;
        if (g_autoOrganize) InstallSimUpdateHook();
        size_t n = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0,
            "OK: Organize stash after opening boxes %s (auto runs so far: %ld, boxes opened: %ld)\r\n",
            g_autoOrganize ? "ON" : "OFF", g_orgAutoRuns, g_boxOpened);
        g_heroScan->length = static_cast<int32_t>(n);
        g_heroScan->done = 1;
        return;
    }

    if (g_heroScan->command == 20)
    {
        size_t n = 0;
        bool ok = false;
        if (!InstallSimUpdateHook())
            n = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0, "ERROR: Organize - could not install the per-frame hook.\r\n");
        else
        {
            // The main thread starts the run (so it can't race an automatic one).
            if (!g_orgActive)
            {
                InterlockedExchange(&g_orgBeginFailed, 0);
                InterlockedExchange(&g_orgRequest, 1);
                for (int w = 0; g_orgRequest && w < 5000; w += 20) Sleep(20);
            }
            int waited = 0;
            for (; g_orgActive && waited < 180000; waited += 50)
            {
                Sleep(50);
                if ((waited % 100) == 0) KeepHeroesAlive();
            }
            bool timedOut = g_orgActive != 0 || g_orgRequest != 0;
            if (g_orgRequest) { InterlockedExchange(&g_orgRequest, 0); strcpy_s(g_orgError, "game is not running frames"); }
            ok = !timedOut && !g_orgBeginFailed && g_orgError[0] == '\0';
            n = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0,
                "%s Organize stash: imported %d from bag, merged %d, moves %d, failed %d%s%s%s\r\n",
                ok ? "OK:" : "WARN:", g_orgImported, g_orgMerged, g_orgMoves, g_orgFails,
                timedOut ? " (still running)" : "", g_orgError[0] ? " - " : "", g_orgError);
        }
        g_heroScan->length = static_cast<int32_t>(n);
        g_heroScan->done = ok ? 1 : -1;
        return;
    }

    if (g_heroScan->command == 18)
    {
        // Move probe: heroIndex = srcType*10000 + srcIndex, value = dstType*10000 + dstIndex,
        // quantity = 0 (drag the whole slot).
        int32_t src = g_heroScan->heroIndex;
        int32_t dst = static_cast<int32_t>(g_heroScan->value);
        g_probeSrcType = src / 10000; g_probeSrcIdx = src % 10000;
        g_probeDstType = dst / 10000; g_probeDstIdx = dst % 10000;
        g_probeQty = 0;
        void* rz = nullptr;
        void* uidMethod = nullptr;
        size_t n = 0;
        if (!ResolveSlotApi(&rz, &uidMethod))
            n = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0, "ERROR: slot API not resolved.\r\n");
        else
        {
            bool f = false;
            uint64_t a0 = SlotUid(rz, uidMethod, g_probeSrcType, g_probeSrcIdx, &f);
            uint64_t b0 = SlotUid(rz, uidMethod, g_probeDstType, g_probeDstIdx, &f);
            bool ran = RunOnMainThread(ProbeMoveMain, 3000);
            Sleep(600);
            uint64_t a1 = SlotUid(rz, uidMethod, g_probeSrcType, g_probeSrcIdx, &f);
            uint64_t b1 = SlotUid(rz, uidMethod, g_probeDstType, g_probeDstIdx, &f);
            n = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0,
                "%s move %d:%d -> %d:%d  ran=%d ok=%d  before src=%llX dst=%llX  after src=%llX dst=%llX\r\n",
                ran && g_probeOk ? "OK:" : "ERROR:", g_probeSrcType, g_probeSrcIdx, g_probeDstType, g_probeDstIdx,
                ran ? 1 : 0, g_probeOk ? 1 : 0,
                (unsigned long long)a0, (unsigned long long)b0, (unsigned long long)a1, (unsigned long long)b1);
        }
        g_heroScan->length = static_cast<int32_t>(n);
        g_heroScan->done = 1;
        return;
    }

    if (g_heroScan->command == 17)
    {
        // Press a game UI button on the main thread. value 1 = stash Sort.
        size_t n = 0;
        if (!InstallSimUpdateHook())
            n = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0,
                "ERROR: Stash sort - could not install the per-frame hook on this build.\r\n");
        else
        {
            g_uiClickClass = "UI_Stash";
            g_uiClickField = "SortingButton";
            g_uiClickError[0] = '\0';
            g_uiClickResult = 0;   // a timeout must not report the previous click's result
            InterlockedExchange(&g_uiClickPending, 1);
            for (int waited = 0; g_uiClickPending && waited < 5000; waited += 20)
            {
                Sleep(20);
                if ((waited % 100) == 0) KeepHeroesAlive();
            }
            if (g_uiClickPending)
            {
                InterlockedExchange(&g_uiClickPending, 0);
                n = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0, "ERROR: Stash sort timed out (game not running frames?).\r\n");
            }
            else if (g_uiClickResult == 1)
                n = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0, g_uiClickSortedDirect
                    ? "OK: Stash sorted (all pages).\r\n" : "OK: Stash Sort button pressed.\r\n");
            else
                n = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0, "ERROR: Stash sort - %s.\r\n", g_uiClickError);
        }
        g_heroScan->length = static_cast<int32_t>(n);
        g_heroScan->done = g_uiClickResult == 1 ? 1 : -1;
        return;
    }

    if (g_heroScan->command == 16)
    {
        bool on = g_heroScan->value != 0.0f;
        size_t n = 0;
        if (on && !InstallSimUpdateHook())
            n = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0,
                "ERROR: Auto open boxes - could not install the per-frame hook on this build.\r\n");
        else if (on && !ResolveBoxApi())
            n = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0, "ERROR: Auto open boxes - %s.\r\n", g_boxError);
        else
        {
            g_boxReadyAt = 0;
            g_boxPauseUntil = 0;
            g_boxNoProgress = 0;
            g_autoOpenBoxes = on;
            n = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0, "OK: Auto open boxes %s%s (opened so far: %ld)\r\n",
                on ? "ON" : "OFF", on ? " - one box every 2s" : "", g_boxOpened);
        }
        g_heroScan->length = static_cast<int32_t>(n);
        g_heroScan->done = g_autoOpenBoxes == on ? 1 : -1;
        return;
    }

    if (g_heroScan->command == 12 || g_heroScan->command == 13)
    {
        bool on = g_heroScan->value != 0.0f;
        if (g_heroScan->command == 12) g_oneHitKill = on; else g_godMode = on;
        size_t n = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0, "OK: %s %s\r\n",
            g_heroScan->command == 12 ? "One-hit kill" : "God mode", on ? "ON" : "OFF");
        g_heroScan->length = static_cast<int32_t>(n);
        g_heroScan->done = 1;
        return;
    }

    // Negative value on an extra stat command releases just that lock.
    if (g_heroScan->command >= 8 && g_heroScan->command < 8 + kExtraStatCount &&
        g_heroScan->value < 0.0f && g_heroScan->heroIndex >= 0 && g_heroScan->heroIndex < 3)
    {
        int i = g_heroScan->command - 8;
        g_heroLocks[g_heroScan->heroIndex].extra[i] = false;
        size_t n = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0, "OK: UI Hero %d %s lock released\r\n",
            g_heroScan->heroIndex + 1, kExtraStatNames[i]);
        g_heroScan->length = static_cast<int32_t>(n);
        g_heroScan->done = 1;
        return;
    }

    void* hero = ResolveHeroByIndex(g_heroScan->heroIndex);
    bool ok = false;
    const char* label = "unknown";
    if (g_heroScan->command == 6 && g_heroScan->heroIndex >= 0 && g_heroScan->heroIndex < 3)
    {
        memset(&g_heroLocks[g_heroScan->heroIndex], 0, sizeof(HeroLocks));
        ok = true;
        label = "All Locks Cleared";
    }
    else if (hero)
    {
        if (g_heroScan->command == 1 || g_heroScan->command == 2 || g_heroScan->command == 5)
        {
            void* heroClass = il2cpp_object_get_class ? il2cpp_object_get_class(hero) : nullptr;
            void* healthField = FindFieldOnClassOrParents(heroClass, "UnitHealthController");
            void* health = nullptr;
            if (healthField) il2cpp_field_get_value(hero, healthField, &health);
            if (health)
            {
                uint8_t* hp = reinterpret_cast<uint8_t*>(health);
                if (g_heroScan->command == 1 || g_heroScan->command == 5)
                    *reinterpret_cast<float*>(hp + 0x40) = g_heroScan->value;
                if (g_heroScan->command == 2 || g_heroScan->command == 5)
                    *reinterpret_cast<float*>(hp + 0x4C) = g_heroScan->value;
                ok = true;
                label = g_heroScan->command == 1 ? "Current HP" :
                        g_heroScan->command == 2 ? "Max HP" : "Current + Max HP";
                HeroLocks& locks = g_heroLocks[g_heroScan->heroIndex];
                if (g_heroScan->command == 1 || g_heroScan->command == 5)
                {
                    locks.currentHp = true;
                    locks.currentHpValue = g_heroScan->value;
                }
                if (g_heroScan->command == 2 || g_heroScan->command == 5)
                {
                    locks.maxHp = true;
                    locks.maxHpValue = g_heroScan->value;
                }
            }
        }
        else if (g_heroScan->command == 3)
        {
            ok = WriteObscuredFloat(hero, 0x104, g_heroScan->value);
            label = "Attack Damage";
            if (ok)
            {
                g_heroLocks[g_heroScan->heroIndex].attackDamage = true;
                g_heroLocks[g_heroScan->heroIndex].attackDamageValue = g_heroScan->value;
            }
        }
        else if (g_heroScan->command == 4)
        {
            ok = WriteObscuredFloat(hero, 0x118, g_heroScan->value);
            label = "Attack Speed";
            if (ok)
            {
                g_heroLocks[g_heroScan->heroIndex].attackSpeed = true;
                g_heroLocks[g_heroScan->heroIndex].attackSpeedValue = g_heroScan->value;
            }
        }
        else if (g_heroScan->command >= 8 && g_heroScan->command < 8 + kExtraStatCount)
        {
            int i = g_heroScan->command - 8;
            ok = WriteObscuredFloat(hero, kExtraStatOffsets[i], g_heroScan->value);
            label = kExtraStatNames[i];
            if (ok)
            {
                g_heroLocks[g_heroScan->heroIndex].extra[i] = true;
                g_heroLocks[g_heroScan->heroIndex].extraValue[i] = g_heroScan->value;
            }
        }
    }
    else if ((g_heroScan->command == 4 || g_heroScan->command == 5 ||
              (g_heroScan->command >= 8 && g_heroScan->command < 8 + kExtraStatCount)) &&
             g_heroScan->heroIndex >= 0 && g_heroScan->heroIndex < 3)
    {
        // Hero slot is empty right now (menu / between stages). Store the lock anyway;
        // ApplyHeroLocks writes it about once per second as soon as the hero spawns.
        HeroLocks& locks = g_heroLocks[g_heroScan->heroIndex];
        if (g_heroScan->command >= 8)
        {
            int i = g_heroScan->command - 8;
            locks.extra[i] = true;
            locks.extraValue[i] = g_heroScan->value;
            label = "Extra stat (pending: applies when hero spawns)";
        }
        else if (g_heroScan->command == 4)
        {
            locks.attackSpeed = true;
            locks.attackSpeedValue = g_heroScan->value;
            label = "Attack Speed (pending: applies when hero spawns)";
        }
        else
        {
            locks.currentHp = locks.maxHp = true;
            locks.currentHpValue = locks.maxHpValue = g_heroScan->value;
            label = "Current + Max HP (pending: applies when hero spawns)";
        }
        ok = true;
    }

    size_t pos = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0,
        "%s UI Hero %d (index %d) %s = %.6g\r\n", ok ? "OK:" : "ERROR:",
        g_heroScan->heroIndex + 1, g_heroScan->heroIndex, label, g_heroScan->value);
    g_heroScan->length = static_cast<int32_t>(pos);
    g_heroScan->done = ok ? 1 : -1;
}

// Appends one class's own fields and methods (not parents) using il2cpp metadata only.
static size_t DumpClassLayout(char* text, size_t cap, size_t pos, void* klass, bool withMethods)
{
    if (!klass) return pos;
    const char* ns = il2cpp_class_get_namespace ? il2cpp_class_get_namespace(klass) : "";
    const char* name = il2cpp_class_get_name ? il2cpp_class_get_name(klass) : "?";
    void* parent = il2cpp_class_get_parent ? il2cpp_class_get_parent(klass) : nullptr;
    pos = ScanAppend(text, cap, pos, "\r\nclass %s%s%s : %s\r\n",
        ns && *ns ? ns : "", ns && *ns ? "." : "", name ? name : "?",
        parent && il2cpp_class_get_name ? il2cpp_class_get_name(parent) : "-");

    void* iter = nullptr;
    void* field = nullptr;
    while ((field = il2cpp_class_get_fields(klass, &iter)) != nullptr)
    {
        int32_t flags = il2cpp_field_get_flags ? il2cpp_field_get_flags(field) : 0;
        size_t offset = il2cpp_field_get_offset ? il2cpp_field_get_offset(field) : 0;
        char* typeName = nullptr;
        if (il2cpp_field_get_type && il2cpp_type_get_name)
        {
            const void* type = il2cpp_field_get_type(field);
            if (type) typeName = il2cpp_type_get_name(type);
        }
        pos = ScanAppend(text, cap, pos, "  field %s+0x%03llX %-40s %s\r\n",
            (flags & 0x0010) ? "static " : "", (unsigned long long)offset,
            typeName ? typeName : "?", il2cpp_field_get_name(field));
        if (typeName && il2cpp_free) il2cpp_free(typeName);
    }

    if (withMethods)
    {
        uintptr_t base = GameAssemblyBase();
        iter = nullptr;
        void* method = nullptr;
        while ((method = il2cpp_class_get_methods(klass, &iter)) != nullptr)
        {
            void* ptr = MethodPointer(method);
            uintptr_t rva = ptr && base ? reinterpret_cast<uintptr_t>(ptr) - base : 0;
            pos = ScanAppend(text, cap, pos, "  method RVA 0x%llX %s(%d)\r\n",
                (unsigned long long)rva, il2cpp_method_get_name(method),
                il2cpp_method_get_param_count ? il2cpp_method_get_param_count(method) : -1);
        }
    }
    return pos;
}

// Command 7: read-only dump of the hero-related class layouts so offsets can be re-mapped
// after a game update. Uses class metadata only — safe on unknown builds.
static void RunHeroLayoutDump()
{
    if (!g_heroScan) return;
    g_heroScan->done = 0;
    g_heroScan->length = 0;
    memset(g_heroScan->text, 0, sizeof(g_heroScan->text));

    char* text = g_heroScan->text;
    const size_t cap = sizeof(g_heroScan->text);
    size_t pos = ScanAppend(text, cap, 0, "=== TBH hero layout dump (metadata only) ===\r\n");

    void* stageClass = FindClass(g_domain, "TaskbarHero", "StageManager");
    if (!stageClass) stageClass = FindClass(g_domain, "", "StageManager");
    pos = DumpClassLayout(text, cap, pos, stageClass, false);

    void* heroClass = FindClass(g_domain, "TaskbarHero", "Hero");
    if (!heroClass) heroClass = FindClass(g_domain, "", "Hero");
    void* healthClass = nullptr;
    for (void* k = heroClass; k; k = il2cpp_class_get_parent ? il2cpp_class_get_parent(k) : nullptr)
    {
        const char* kn = il2cpp_class_get_name ? il2cpp_class_get_name(k) : nullptr;
        if (kn && (Streq(kn, "Object") || Streq(kn, "MonoBehaviour") || Streq(kn, "Behaviour")))
            break;
        pos = DumpClassLayout(text, cap, pos, k, true);
        if (!healthClass && il2cpp_class_get_field_from_name && il2cpp_field_get_type)
        {
            void* hf = il2cpp_class_get_field_from_name(k, "UnitHealthController");
            if (hf) healthClass = il2cpp_class_from_type(il2cpp_field_get_type(hf));
        }
        if (!il2cpp_class_get_parent) break;
    }
    if (!heroClass)
        pos = ScanAppend(text, cap, pos, "\r\nERROR: TaskbarHero.Hero not found.\r\n");

    pos = DumpClassLayout(text, cap, pos, healthClass, true);
    pos = DumpClassLayout(text, cap, pos,
        FindClass(g_domain, "CodeStage.AntiCheat.ObscuredTypes", "ObscuredFloat"), true);

    g_heroScan->length = static_cast<int32_t>(pos);
    g_heroScan->done = 1;
}

static void RunHeroScan()
{
    if (!g_heroScan) return;
    g_heroScan->done = 0;
    g_heroScan->length = 0;
    memset(g_heroScan->text, 0, sizeof(g_heroScan->text));

    char* text = g_heroScan->text;
    const size_t cap = sizeof(g_heroScan->text);
    size_t pos = ScanAppend(text, cap, 0,
        "=== TBH hero diagnostics (read-only) ===\r\n"
        "Candidate labels are inferred from the dump and require runtime confirmation.\r\n\r\n");

    void* stageClass = FindClass(g_domain, "TaskbarHero", "StageManager");
    if (!stageClass) stageClass = FindClass(g_domain, "", "StageManager");
    void* stage = ResolveStageManager();
    if (!stageClass || !stage)
    {
        pos = ScanAppend(text, cap, pos, "ERROR: StageManager singleton not resolved. Enter a run and retry.\r\n");
        g_heroScan->length = static_cast<int32_t>(pos);
        g_heroScan->done = -1;
        return;
    }

    void* heroListField = FindFieldOnClassOrParents(stageClass, "HeroList");
    void* heroes = nullptr;
    if (heroListField) il2cpp_field_get_value(stage, heroListField, &heroes);
    int32_t count = ManagedArrayLength(heroes);
    pos = ScanAppend(text, cap, pos, "StageManager=0x%llX HeroList=0x%llX count=%d\r\n",
        (unsigned long long)stage, (unsigned long long)heroes, count);
    if (!heroes || count <= 0 || count > 64)
    {
        pos = ScanAppend(text, cap, pos, "ERROR: HeroList unavailable or unexpected layout. Ensure a hero is active.\r\n");
        g_heroScan->length = static_cast<int32_t>(pos);
        g_heroScan->done = -1;
        return;
    }

    static const struct { const char* name; size_t offset; } kStats[] =
    {
        { "AttackDamage", 0x104 }, { "AttackSpeed", 0x118 },
        { "CastSpeed", 0x12C }, { "Armor", 0x140 },
        { "MoveSpeedInternal", 0x154 }, { "CriticalChance", 0x168 },
        { "CriticalDamage", 0x17C }, { "CooldownReduction", 0x190 },
        { "Unknown_1A4", 0x1A4 }, { "Unknown_1B8", 0x1B8 },
        { "SkillRangeExpansion", 0x1CC }, { "FireResistance", 0x1E0 },
    };
    static const struct { const char* name; const char* method; float displayScale; } kGetters[] =
    {
        { "Attack Damage", "gqg", 1.0f },
        { "Attack Speed", "gmv", 1.0f },
        { "Armor", "gqh", 1.0f },
        { "Move Speed", "gqi", 100.0f },
        { "Max HP", "gqj", 1.0f },
        { "Critical Chance", "gqk", 100.0f },
        { "Critical Damage", "gql", 100.0f },
        { "Cooldown Reduction", "gqm", 100.0f },
        { "Cast Speed", "gqn", 1.0f },
    };

    for (int32_t i = 0; i < count; ++i)
    {
        void* hero = ReadManagedArrayRef(heroes, i);
        if (!hero)
        {
            pos = ScanAppend(text, cap, pos, "\r\nHERO[%d] (UI Hero %d) empty slot - hero not spawned yet\r\n", i, i + 1);
            continue;
        }
        void* heroClass = il2cpp_object_get_class ? il2cpp_object_get_class(hero) : nullptr;
        const char* className = heroClass && il2cpp_class_get_name ? il2cpp_class_get_name(heroClass) : "?";
        bool isHero = *reinterpret_cast<bool*>(reinterpret_cast<uint8_t*>(hero) + 0x100);
        pos = ScanAppend(text, cap, pos, "\r\nHERO[%d] (UI Hero %d) object=0x%llX class=%s isHero=%d\r\n",
            i, i + 1, (unsigned long long)hero, className ? className : "?", isHero ? 1 : 0);

        float runtimeAttackDamage = 0.0f;
        float runtimeAttackSpeed = 0.0f;
        InvokeFloat0(hero, "gqg", runtimeAttackDamage);
        InvokeFloat0(hero, "gmv", runtimeAttackSpeed);
        for (const auto& stat : kStats)
            pos = ScanAppend(text, cap, pos, "  field %-26s +0x%03llX decrypt=%.6g fake=%.6g\r\n",
                stat.name, (unsigned long long)stat.offset, ReadObscuredFloat(hero, stat.offset),
                ReadObscuredFloatFake(hero, stat.offset));
        pos = ScanAppend(text, cap, pos, "  -- Unit runtime getters --\r\n");
        for (const auto& getter : kGetters)
        {
            float value = 0.0f;
            bool ok = InvokeFloat0(hero, getter.method, value);
            pos = ScanAppend(text, cap, pos, "  runtime %-26s %s = %s%.6g (display %.6g)\r\n",
                getter.name, getter.method, ok ? "" : "unresolved/", value, value * getter.displayScale);
        }
        pos = ScanAppend(text, cap, pos, "  runtime BasicAttackDPS formula = %.6g\r\n",
            runtimeAttackDamage * runtimeAttackSpeed);
        float grfOrigin = 0.0f;
        bool hasGrfOrigin = InvokeDamageInfoOrigin0(hero, "grf", grfOrigin);
        pos = ScanAppend(text, cap, pos, "  damage payload Unit.grf OriginDamage = %s%.6g\r\n",
            hasGrfOrigin ? "" : "unresolved/", grfOrigin);

        void* healthField = FindFieldOnClassOrParents(heroClass, "UnitHealthController");
        void* health = nullptr;
        if (healthField) il2cpp_field_get_value(hero, healthField, &health);
        pos = ScanAppend(text, cap, pos, "  healthController=0x%llX\r\n", (unsigned long long)health);
        if (health)
        {
            uint8_t* hp = reinterpret_cast<uint8_t*>(health);
            pos = ScanAppend(text, cap, pos,
                "  HP raw: +38=%.6g +3C=%.6g +40=%.6g +44=%.6g +48=%.6g +4C=%.6g\r\n",
                *reinterpret_cast<float*>(hp + 0x38), *reinterpret_cast<float*>(hp + 0x3C),
                *reinterpret_cast<float*>(hp + 0x40), *reinterpret_cast<float*>(hp + 0x44),
                *reinterpret_cast<float*>(hp + 0x48), *reinterpret_cast<float*>(hp + 0x4C));
            float gtp = 0.0f, gtq = 0.0f;
            bool hasGtp = InvokeFloat0(health, "gtp", gtp);
            bool hasGtq = InvokeFloat0(health, "gtq", gtq);
            pos = ScanAppend(text, cap, pos, "  HP getters: gtp=%s%.6g gtq=%s%.6g\r\n",
                hasGtp ? "" : "unresolved/", gtp, hasGtq ? "" : "unresolved/", gtq);
            pos = ScanAppend(text, cap, pos,
                "  CONFIRMED Current HP: controller+0x40 = %.6g | Max HP: controller+0x4C = %.6g\r\n",
                *reinterpret_cast<float*>(hp + 0x40), *reinterpret_cast<float*>(hp + 0x4C));
        }

        void* cache = *reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(hero) + 0x3A0);
        pos = ScanAppend(text, cap, pos, "  heroCache(+0x3A0)=0x%llX", (unsigned long long)cache);
        if (cache)
        {
            float jjeOrigin = 0.0f, jjdOrigin = 0.0f;
            bool hasJjeOrigin = InvokeCachedDamageInfoOrigin("jje", cache, jjeOrigin);
            bool hasJjdOrigin = InvokeCachedDamageInfoOrigin("jjd", cache, jjdOrigin);
            pos = ScanAppend(text, cap, pos,
                "  cached damage payloads: uh.jje OriginDamage=%s%.6g | uh.jjd OriginDamage=%s%.6g\r\n",
                hasJjeOrigin ? "" : "unresolved/", jjeOrigin, hasJjdOrigin ? "" : "unresolved/", jjdOrigin);
            pos = ScanAppend(text, cap, pos,
                " level candidates decrypt/fake: +CC=%d/%d +DC=%d/%d +EC=%d/%d +FC=%d/%d +138=%d/%d\r\n",
                ReadObscuredInt(cache, 0xCC), ReadObscuredIntFake(cache, 0xCC),
                ReadObscuredInt(cache, 0xDC), ReadObscuredIntFake(cache, 0xDC),
                ReadObscuredInt(cache, 0xEC), ReadObscuredIntFake(cache, 0xEC),
                ReadObscuredInt(cache, 0xFC), ReadObscuredIntFake(cache, 0xFC),
                ReadObscuredInt(cache, 0x138), ReadObscuredIntFake(cache, 0x138));
            pos = ScanAppend(text, cap, pos, "  CONFIRMED Level candidate: cache+0xCC fakeValue = %d\r\n",
                ReadObscuredIntFake(cache, 0xCC));
        }
        else
            pos = ScanAppend(text, cap, pos, "\r\n");

        void* heroInfo = cache ? *reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(cache) + 0x30) : nullptr;
        if (heroInfo)
        {
            uint8_t* info = reinterpret_cast<uint8_t*>(heroInfo);
            pos = ScanAppend(text, cap, pos,
                "  HeroInfoData=0x%llX base ints: ATK=%d AttackSpeed=%d CastSpeed=%d CritChance=%d CritDamage=%d CDR=%d MaxHP=%d Armor=%d MoveSpeed=%d\r\n",
                (unsigned long long)heroInfo,
                *reinterpret_cast<int32_t*>(info + 0x58), *reinterpret_cast<int32_t*>(info + 0x5C),
                *reinterpret_cast<int32_t*>(info + 0x60), *reinterpret_cast<int32_t*>(info + 0x64),
                *reinterpret_cast<int32_t*>(info + 0x68), *reinterpret_cast<int32_t*>(info + 0x6C),
                *reinterpret_cast<int32_t*>(info + 0x70), *reinterpret_cast<int32_t*>(info + 0x74),
                *reinterpret_cast<int32_t*>(info + 0x78));
        }
    }

    pos = ScanAppend(text, cap, pos, "\r\nScan complete. Compare candidates with the in-game hero panel.\r\n");
    g_heroScan->length = static_cast<int32_t>(pos < cap ? pos : cap - 1);
    g_heroScan->done = 1;
}

static bool ScanUidLine(char* text, size_t cap, size_t& pos, const char* source, uint64_t uid, bool* seen, int seenCap)
{
    int hash = (int)((uid >> 4) % (uint64_t)seenCap);
    if (seen[hash]) return false;
    seen[hash] = true;

    int32_t key = LookupItemKey(uid, nullptr, 0);
    int32_t grade = LookupGrade(uid, key);

    pos = ScanAppend(text, cap, pos, "%s uid=0x%llX itemKey=%d grade=%d (%s)\r\n",
                     source, (unsigned long long)uid, key, grade, GradeTypeName(grade));
    return true;
}

static void RunItemCatalogDump()
{
    if (!g_scan) return;
    g_scan->done = 0;
    g_scan->length = 0;
    memset(g_scan->text, 0, sizeof(g_scan->text));

    EnsureItemMetaTypes();
    ResolveItemAdd();
    void* yq = ResolveYqInstance();

    char* text = g_scan->text;
    const size_t cap = sizeof(g_scan->text);
    size_t pos = 0;
    int32_t count = 0;
    bool truncated = false;

    pos = ScanAppend(text, cap, pos,
        "=== TBH item catalog (all ItemInfoData from the item database) ===\r\n"
        "Format: ITEM itemKey grade=N (Name)\r\n\r\n");

    if (!yq)
    {
        pos = ScanAppend(text, cap, pos,
            "ERROR: item database not found (load into main menu / stash first).\r\n");
        g_scan->length = (int32_t)pos;
        g_scan->done = -1;
        return;
    }

    pos = CatalogAppendFromList(yq, text, cap, pos, count, truncated);
    if (count == 0)
        pos = CatalogAppendFromDict(yq, text, cap, pos, count, truncated);

    pos = ScanAppend(text, cap, pos, "\r\nTotal: %d item(s).", count);
    if (truncated)
        pos = ScanAppend(text, cap, pos, " (TRUNCATED — increase scan buffer or export partial)");
    pos = ScanAppend(text, cap, pos, "\r\n");

    g_scan->length = (int32_t)(pos < cap ? pos : cap - 1);
    g_scan->done = truncated ? 2 : 1;
}

static void RunItemScan()
{
    if (!g_scan) return;
    g_scan->done = 0;
    g_scan->length = 0;
    memset(g_scan->text, 0, sizeof(g_scan->text));

    if (!UseLegacyItemNames())
    {
        // Relies on obfuscated save/stash names that newer builds reuse for other classes.
        size_t n = ScanAppend(g_scan->text, sizeof(g_scan->text), 0,
            "Item scan is not supported on this game build. Use Export catalog for itemKeys.\r\n");
        g_scan->length = (int32_t)n;
        g_scan->done = -1;
        return;
    }

    TryResolveStash();
    ResolveItemAdd();
    ResolveSaveTypes();
    EnsureItemMetaTypes();

    char* text = g_scan->text;
    const size_t cap = sizeof(g_scan->text);
    size_t pos = 0;
    bool seen[256] = {};

    void* playerSave = GetPlayerSaveData();
    void* itemList = nullptr;
    int32_t saveItemCount = 0;
    if (playerSave && g_playerSaveClass)
    {
        void* fList = il2cpp_class_get_field_from_name(g_playerSaveClass, "itemSaveDatas");
        if (fList)
        {
            il2cpp_field_get_value(playerSave, fList, &itemList);
            saveItemCount = ListSize(itemList);
        }
    }

    pos = ScanAppend(text, cap, pos,
        "=== TBH item scan (use itemKey in spawn) ===\r\n"
        "grade = EGradeType: 0 Common, 1 Uncommon, 2 Rare, 3 Legendary, 4 Immortal, 5 Arcana, 6 Beyond, 7 Celestial, 8 Divine, 9 Cosmic, 10 None\r\n"
        "src: tf=live item, save=PlayerSaveData.itemSaveDatas\r\n\r\n");
    pos = ScanAppend(text, cap, pos,
        "Debug: saveItems=%d ys=%s ish=%s saveTypes=%s\r\n\r\n",
        saveItemCount,
        ResolveYsInstance() ? "ok" : "no",
        g_ishMethod ? "ok" : "no",
        (g_playerSaveClass && g_itemSaveClass) ? "ok" : "no");

    int stashRows = 0;
    for (int32_t slot = 0; slot < kStashMaxSlots; ++slot)
    {
        void* cache = InvokeJbp(nullptr, slot);
        if (!cache) continue;
        uint64_t uid = ReadSaveUniqueId(cache);
        if (uid == 0) continue;

        int32_t idx = ReadSaveSlotIndex(cache);
        char src[16] = {};
        int32_t key = LookupItemKey(uid, src, sizeof(src));
        int32_t grade = LookupGrade(uid, key);

        pos = ScanAppend(text, cap, pos,
                         "STASH tabSlot=%2d saveIndex=%d uid=0x%llX itemKey=%d grade=%d (%s) src=%s\r\n",
                         slot, idx, (unsigned long long)uid, key, grade, GradeTypeName(grade),
                         src[0] ? src : "?");
        stashRows++;
    }

    int invRows = 0;
    if (g_iryMethod)
    {
        void* exc = nullptr;
        void* coll = il2cpp_runtime_invoke(g_iryMethod, nullptr, nullptr, &exc);
        if (!exc && coll && il2cpp_array_length)
        {
            uint32_t len = il2cpp_array_length(coll);
            pos = ScanAppend(text, cap, pos, "\r\n-- INVENTORY (ti.iry UIDs) --\r\n");
            for (uint32_t i = 0; i < len && i < 500u; ++i)
            {
                uint64_t uid = 0;
                if (il2cpp_array_get)
                {
                    void* elem = il2cpp_array_get(coll, sizeof(uint64_t), i);
                    if (elem) uid = *reinterpret_cast<uint64_t*>(elem);
                }
                if (uid == 0) continue;
                if (ScanUidLine(text, cap, pos, "INV", uid, seen, 256))
                    invRows++;
            }
        }
    }

    pos = ScanAppend(text, cap, pos,
        "\r\nSummary: %d stash slot(s), %d inventory UID(s).\r\n"
        "Pick an itemKey from above for Spawn (same grade optional).\r\n",
        stashRows, invRows);

    g_scan->length = (int32_t)(pos < cap ? pos : cap - 1);
    g_scan->done = 1;
}

static bool SpawnItem(int32_t itemKey, int32_t grade)
{
    g_lastSpawnDetail = 0;
    TryResolveStash();
    ResolveItemAdd();

    int32_t catalogGrade = -1;
    int32_t spawnKey = ResolveSpawnItemKey(itemKey, grade, &catalogGrade);
    const int32_t keyDigit = KeyGradeDigit(itemKey);
    const bool gradeMismatch = (grade >= 0 && spawnKey == itemKey &&
                                keyDigit != grade &&
                                catalogGrade >= 0 && catalogGrade != grade);

    if (gradeMismatch)
    {
        g_lastSpawnDetail = -7;
        return false;
    }

    if (!ItemKeyExists(spawnKey))
    {
        g_lastSpawnDetail = -2;
        return false;
    }

    // Fast path: ti.blk / ti.isk (inventory). Stash path only if both fail.
    if (SpawnViaAddApis(spawnKey))
        return true;

    if (SpawnViaJbo(spawnKey, -1)) return true;
    if (SpawnViaStash(spawnKey, -1)) return true;

    if (g_lastSpawnDetail == 0) g_lastSpawnDetail = -1;
    return false;
}

static DWORD WINAPI WorkerThread(LPVOID)
{
    for (int tries = 0; tries < 200; ++tries)
    {
        if (LoadIl2Cpp()) break;
        Sleep(50);
    }
    if (!il2cpp_domain_get) return 0;

    void* domain = il2cpp_domain_get();
    if (!domain) return 0;
    g_domain = domain;
    // Attached only while the trainer is connected: an attached foreign thread keeps the
    // il2cpp runtime waiting at shutdown, so Disconnect detaches it (game can then exit).
    void* il2cppThread = il2cpp_thread_attach(domain);

    HANDLE hMap = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
                                     0, kMapSize, kMapName);
    if (!hMap) return 0;
    g_shared = static_cast<SharedState*>(
        MapViewOfFile(hMap, FILE_MAP_ALL_ACCESS, 0, 0, kMapSize));
    if (!g_shared) return 0;
    g_shared->magic       = kMagic;
    g_shared->spawnResult = 0;

    HANDLE hScanMap = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
                                         0, kScanMapSize, kScanMapName);
    if (hScanMap)
    {
        g_scan = static_cast<ScanState*>(MapViewOfFile(hScanMap, FILE_MAP_ALL_ACCESS, 0, 0, kScanMapSize));
        if (g_scan)
        {
            g_scan->magic = kScanMagic;
            g_scan->done = 0;
            g_scan->length = 0;
            g_scan->text[0] = '\0';
        }
    }

    HANDLE hHeroScanMap = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
                                             0, kScanMapSize, kHeroScanMapName);
    if (hHeroScanMap)
    {
        g_heroScan = static_cast<HeroScanState*>(MapViewOfFile(hHeroScanMap, FILE_MAP_ALL_ACCESS, 0, 0, kScanMapSize));
        if (g_heroScan)
        {
            g_heroScan->magic = kHeroScanMagic;
            g_heroScan->done = 0;
            g_heroScan->length = 0;
            g_heroScan->text[0] = '\0';
        }
    }

    int32_t statusFlags = 1;

    g_timeClass = FindClass(domain, "UnityEngine", "Time");
    if (g_timeClass)
    {
        g_setTimeScale = il2cpp_class_get_method_from_name(g_timeClass, "set_timeScale", 1);
        if (g_setTimeScale) statusFlags |= 2;
    }

    TryResolveStash();
    statusFlags = BuildStatusFlags();
    g_shared->status = statusFlags;
    g_lastSpawnReq   = g_shared->spawnRequest;

    bool wasEnabled = false;
    InstallShutdownWatch();
    InstallSimUpdateHook();   // main-thread jobs (auto stash, boxes, UI clicks)

    for (;;)
    {
        g_shared->heartbeat++;

        if (g_shuttingDown)
        {
            // Game is closing: never touch il2cpp again, but still answer the trainer so it
            // reports the state instead of timing out.
            if (g_heroScan && g_heroScan->request != g_lastHeroScanReq)
            {
                g_lastHeroScanReq = g_heroScan->request;
                size_t n = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0,
                    "ERROR: hook stopped - it saw the game window closing. Restart the game.\r\n");
                g_heroScan->length = static_cast<int32_t>(n);
                g_heroScan->done = -1;
            }
            Sleep(50);
            continue;
        }
        if (!GameAlive())
        {
            Sleep(16);
            continue;
        }

        if (g_shared->trainerPaused)
        {
            // Trainer disconnected: restore normal speed once, then stay idle until the
            // next Connect so the game can be closed without the hook touching il2cpp.
            if (wasEnabled)
            {
                SetTimeScale(1.0f);
                wasEnabled = false;
            }
            if (il2cppThread && il2cpp_thread_detach)
            {
                il2cpp_thread_detach(il2cppThread);
                il2cppThread = nullptr;
            }
            Sleep(50);
            continue;
        }
        if (!il2cppThread)
            il2cppThread = il2cpp_thread_attach(domain);   // reconnected: resume

        if (g_shared->speedEnabled)
        {
            float ts = g_shared->timeScale;
            if (ts < 0.0f)   ts = 0.0f;
            if (ts > 20.0f)  ts = 20.0f;   // trainer MaxSpeed
            SetTimeScale(ts);
            wasEnabled = true;
        }
        else if (wasEnabled)
        {
            SetTimeScale(1.0f);
            wasEnabled = false;
        }

        // Re-resolve stash (~1s) and pick up trainer-pushed RVAs for new game builds
        ++g_resolveCounter;
        if ((g_resolveCounter % 60) == 0)
            TryResolveStash();
        // Hero locks every ~0.2s (12 x 16ms) so HP is refilled before a hero can die;
        // god mode tightens that to ~33ms.
        if (g_oneHitKill && (g_resolveCounter % 6) == 0)
            ApplyOneHitKill();
        if (g_godMode && (g_resolveCounter % 2) == 0)
            ApplyGodModeAllHeroes();
        if ((g_resolveCounter % (g_godMode ? 2 : 12)) == 0)
        {
            for (int32_t heroIndex = 0; heroIndex < 3; ++heroIndex)
                ApplyHeroLocks(heroIndex);
        }

        int32_t curReq = g_shared->spawnRequest;
        if (curReq != g_lastSpawnReq)
        {
            int32_t reqKey = g_shared->spawnItemKey;
            int32_t reqGrade = g_shared->spawnGrade;
            if (g_shared->spawnRequest != curReq)
                continue;

            if (InterlockedCompareExchange(&g_spawnBusy, 1, 0) != 0)
            {
                g_lastSpawnReq = curReq;
                g_shared->spawnDetail = 0;
                g_shared->spawnResult = -8;
                continue;
            }

            g_lastSpawnReq = curReq;
            TryResolveStash();
            if (!g_stashInstance)
                g_stashInstance = ResolveStashInstance();

            bool ok = SpawnItem(reqKey, reqGrade);
            int32_t catalogGrade = -1;
            int32_t usedKey = ResolveSpawnItemKey(reqKey, reqGrade, &catalogGrade);
            if (ok)
                g_shared->spawnDetail = (usedKey != reqKey) ? usedKey : 0;
            else
            {
                if (g_lastSpawnDetail != 0)
                    g_shared->spawnDetail = g_lastSpawnDetail;
                else if (usedKey != reqKey)
                    g_shared->spawnDetail = usedKey;
                else
                    g_shared->spawnDetail = -1;
            }
            g_shared->spawnResult = ok ? 1 : (g_lastSpawnDetail != 0 ? g_lastSpawnDetail : -1);
            InterlockedExchange(&g_spawnBusy, 0);
        }

        if (g_scan && g_scan->request != g_lastScanReq)
        {
            g_lastScanReq = g_scan->request;
            if (g_scan->request & 1)
                RunItemCatalogDump();
            else
                RunItemScan();
        }

        if (g_heroScan && g_heroScan->request != g_lastHeroScanReq)
        {
            g_lastHeroScanReq = g_heroScan->request;
            if (g_heroScan->command == 0)
                RunHeroScan();
            else if (g_heroScan->command == 7)
                RunHeroLayoutDump();
            else if (g_heroScan->command == 15)
            {
                g_heroScan->done = 0;
                g_heroScan->length = 0;
                memset(g_heroScan->text, 0, sizeof(g_heroScan->text));
                RunAutoStash();
            }
            else if (g_heroScan->command == 19)
            {
                g_heroScan->done = 0;
                g_heroScan->length = 0;
                memset(g_heroScan->text, 0, sizeof(g_heroScan->text));
                RunStashList();
            }
            else if (g_heroScan->command == 14)
            {
                g_heroScan->done = 0;
                g_heroScan->length = 0;
                memset(g_heroScan->text, 0, sizeof(g_heroScan->text));
                RunInventoryScan();
            }
            else
                RunHeroCommand();
        }

        Sleep(16);
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(hModule);
        CreateThread(nullptr, 0, WorkerThread, nullptr, 0, nullptr);
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        g_shuttingDown = true;
    }
    return TRUE;
}
