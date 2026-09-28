# TBH Trainer — คู่มือรวม

Trainer สำหรับเกม **Taskbar Hero** (Unity Il2Cpp) โดย DENOISER
เอกสารนี้รวมเนื้อหาจาก `BUILD.md`, `MIGRATE_1.00.09.md`, `MONO_CE_GUIDE_1.00.09.md`,
`PATCH_1.00.09_STATUS.md` และ `UPDATE.md` ไว้ในไฟล์เดียว

**เวอร์ชัน trainer:** v2.0.1
**เวอร์ชันเกมที่รองรับ:** 1.00.08, 1.00.09, 1.2.4, 1.2.6, 1.2.7 และ **1.2.8** (build 25454993, profile `V128`: RVA ใหม่ layout เหมือน 1.2.7)
**อัปเดตล่าสุด:** 2026-09-22 (เกมอัปเดตเป็น 1.2.6 build 25435119 ส่วน 1.2.5 ถูกทับในวันเดียวกัน)

ถ้าเครื่องมือนี้มีประโยชน์ เลี้ยงกาแฟกันได้ที่ [ko-fi.com/kewiv](https://ko-fi.com/kewiv) ☕

---

# 1. Build

## Build ทั้งหมด

ดับเบิลคลิก `build.bat` ที่โฟลเดอร์หลัก ผลลัพธ์จะอยู่ที่:

- `TrainerBuild\TBH Trainer.exe`
- `TrainerBuild\TBHHook.dll`

## สิ่งที่ต้องมี

- Windows x64
- .NET 10 SDK
- C++ compiler อย่างใดอย่างหนึ่ง: MinGW-w64 `g++` ใน `PATH` หรือ Visual Studio พร้อม x64 C++ build tools

Script จะเช็กให้ทั้ง 2 ขั้น ถ้าขาดเครื่องมือจะหยุดพร้อมบอกสาเหตุ

## Build แยกส่วน

```bat
cd TBHHook
build.bat

cd ..\TBH_Trainer
build.bat
```

## เรื่องที่ต้องรู้ตอน build

- **แก้ hook (`TBHHook`) ต้องปิดเกม** เพราะ DLL ยังถูกโหลดค้างอยู่ในเกมหลัง inject และต้องเปิดเกมใหม่ถึงจะใช้ตัวใหม่
- **แก้เฉพาะ trainer** ปิดแค่ trainer พอ
- ถ้าแก้ `SharedState` ต้อง build ทั้ง hook และ trainer พร้อมกัน
- ไฟล์ถูก build ไปที่ `publish\` แล้ว copy ต่อไปที่ `TrainerBuild\`

## โครงสร้างโปรเจกต์

```
TBH_Trainer\        C# WinForms trainer (.NET 10)
  *.cs              source ทั้งหมด
  app.manifest      ขอสิทธิ์ Administrator
  logo.png/icon.ico embedded resource
TBHHook\            C++ injected DLL
  dllmain.cpp       source
dump_1.2.x\         ผลจาก Il2CppDumper + CSV ของเกม (ไม่อยู่ใน git)
TrainerBuild\       ไฟล์ที่ใช้งานจริง (ไม่อยู่ใน git)
```

---

# 2. สถานะฟีเจอร์บน 1.2.4 / 1.2.6

| ฟีเจอร์ | สถานะ |
|---|---|
| Connect อัตโนมัติ (เลือก process `TaskBarHero` เอง) | ✅ |
| ACTk bypass (6/6 detector) | ✅ |
| Speedhack (สูงสุด 20x) | ✅ |
| Hero lock: HP, Attack Speed, Crit Chance, Crit Damage, CDR, Armor | ✅ |
| ตีทีเดียวตาย (ตรึง HP มอนไว้ที่ 1) | ✅ |
| God mode (เติม HP **ทุกเฟรม** + lock Armor) | ✅ |
| Item spawn + Export catalog | ✅ |
| Scan items (อ่าน inventory + ช่อง Stash ที่ปลดล็อกแล้ว, 1.2.6) | ✅ |
| Bag → Stash อัตโนมัติเมื่อกระเป๋าเต็ม (หน้า 1 = หินวิญญาณ) | ✅ 1.2.8 |
| Sort Stash / Organize Stash (จัดเรียง 7 หน้า) | 🗑️ ลบออกแล้วใน v1.5.1 |
| Auto เปิดกล่อง: เจอกล่อง → รอ 2 วิ → เปิดทีละกล่องทุก 2 วิ (1.2.6) | ✅ |
| Chest log: ไทม์ไลน์กล่องดร็อบ/เปิด ทั้ง 6 ชนิด (อ่านอย่างเดียว) | ✅ 2.0.0 |
| Chest upgrades: อ่านค่าอัปเกรดกล่องของบัญชี + จำนวนที่ถืออยู่ | ✅ 2.0.0 |
| ปุ่ม Launch Game (เปิดเกมผ่าน Steam) | ✅ |
| Hero EXP on Kill | 🗑️ ลบออกแล้ว |
| แท็บ Extra Features | 🗑️ ลบออกแล้ว |

## วิธีใช้งาน

1. เปิดเกมให้เข้า run ก่อน
2. เปิด `TrainerBuild\TBH Trainer.exe` แบบ **Run as administrator**
3. กด **Connect** ปุ่มเดียว trainer จะทำต่อเองทั้งหมด: เลือก process → ACTk bypass → inject hook → scan hero → lock stat → เปิดตีทีเดียวตายและ God mode
4. Speedhack และ Item spawn กดใช้เพิ่มได้ตามต้องการ
5. กด Minimize แล้วหน้าต่างจะยุบไปที่ system tray มุมขวาล่าง คลิกซ้ายที่ไอคอนเพื่อเรียกกลับ
6. **ก่อนปิดเกมให้กด Disconnect ทุกครั้ง** (หรือปิด Trainer ก่อน) hook จะคืน speed ปกติแล้วหยุดเรียกเข้าเกม ถ้าปิดเกมตอนที่ hook ยังทำงานอยู่ เกมอาจ crash หรือค้างตอนปิด

## อัปเดตอัตโนมัติ (v1.4.1 ขึ้นไป)

ตอนเปิดโปรแกรม trainer จะเช็ก Release ล่าสุดของ repo `KEWI-hub/TBH-Trainer` (Private)

- **คนที่ได้รับเชิญ (Collaborator)** และมี GitHub login ที่ git เก็บไว้ใน Windows Credential Manager (ลองทุกบัญชีจนเจอตัวที่เข้า repo ได้) → ถ้ามีเวอร์ชันใหม่จะขึ้นถามก่อน กดตกลงแล้วโหลด แตกไฟล์ทับ และเปิด trainer ใหม่ให้เอง
- ได้รับเชิญแต่ไม่มี git login → คลิกขวาไอคอนที่ tray → **Check for updates** แล้วใส่ Personal Access Token ของตัวเอง (fine-grained, อ่าน Contents ของ repo นี้อย่างเดียว) เก็บไว้ใน Windows Credential Manager ของเครื่องนั้น
- **คนที่ไม่ได้รับเชิญ** → ไม่อัปเดต ไม่ถามอะไร มีแค่ข้อความใน log ให้ไปโหลด release ใหม่เอง
- ต้อง**ปิดเกมก่อนอัปเดต** เพราะ `TBHHook.dll` ถูกล็อกอยู่ในเกม
- ออกเวอร์ชันใหม่: แก้ `AppInfo.Version` ใน `Updater.cs` ให้ตรงกับ tag (`v<เวอร์ชัน>`) แล้วแนบ zip ที่มี `TBH Trainer.exe` + `TBHHook.dll` ใน Release

ค่าที่ lock อัตโนมัติ: HP 2,000,000,000 / Attack Speed 50 / Crit Chance 1.0 / Crit Damage 100 / CDR 10 / Armor 5,000,000 (ตอนเปิด God mode)

ปุ่ม **Max stats (all heroes)** ตั้งทั้ง 8 ค่าให้ฮีโร่ทั้ง 3 ช่องพร้อมกัน: Attack Damage 1,000,000 / Attack Speed 50 / Crit Chance 1.0 / Crit Damage 100 / CDR 10 / Armor 5,000,000 / Move Speed 60 / Cast Speed 10

lock ทำงานอยู่ใน hook ที่อยู่ในเกม และผูกกับ**ช่อง** hero ไม่ใช่ตัวละคร จัดทัพใหม่ก็ยังอมตะ ตั้งแต่ v1.4.3 **กด Disconnect หรือปิด trainer แล้ว hook จะหยุดทุกอย่าง** (คืน speed ปกติ) จนกว่าจะ Connect ใหม่

## เปลี่ยนแปลงใน v2.0.1

- ปุ่ม **☕ Support on Ko-fi** มุมขวาบนของแบนเนอร์ กดแล้วเปิด [ko-fi.com/kewiv](https://ko-fi.com/kewiv) ในเบราว์เซอร์
- ไม่มีการเปลี่ยนแปลงอย่างอื่น hook เหมือน v2.0.0 ทุกประการ

## เปลี่ยนแปลงใน v2.0.0

- **God mode ตามทันตอนเร่งสปีด** — เดิมเติม HP จาก worker thread ทุก ~33 ms ตามเวลาจริง
  ซึ่งใช้ไม่ได้ที่ 10x–20x เพราะเกมคำนวณการต่อสู้ไป 10–20 เท่าระหว่างสองรอบเติม ฮีโร่จึงตาย
  ก่อนรอบถัดไปจะมาถึง ตอนนี้เติมจาก frame hook **ทุกเฟรม** ผูกกับ update ของเกมเอง
  (ตัวจับเวลาเดิมยังอยู่เป็น fallback สำหรับ build ที่ติดตั้ง frame hook ไม่ได้)
- **Chest log** (ปุ่ม *Chest log*) — ไทม์ไลน์ว่ากล่องดร็อบและถูกเปิดเมื่อไหร่ ครอบทั้ง 6 ชนิด
  (โรคระบาด × 3 + ปกติ × 3) พร้อมยอดรวมและอัตรา "กล่องละกี่วินาที" อ่านจากจำนวนกล่องที่เกม
  เก็บอยู่แล้ว สุ่มทุก 1 วินาทีจาก main thread **ไม่เขียนอะไรลงเกมเลย** กด Shift ค้างตอนคลิก
  เพื่อล้างแล้วเริ่มนับใหม่
- **Chest upgrades** (ปุ่ม *Chest upgrades*) — อ่านค่า `EAccountStatus` ที่เกี่ยวกับกล่อง:
  โอกาสดรอปกล่อง (มอนธรรมดา / บอสด่าน) และค่าที่ผูกกับเพดานการถือครองกล่องโรคระบาด
  ทั้ง 3 ชนิด พร้อม**จำนวนกล่องที่ถืออยู่จริง** อ่านผ่านเมธอดของเกมเอง ค่าถูก encode แบบ
  ObscuredInt ตามเดิม
- Self-check เพิ่มบรรทัดจำนวนกล่องโรคระบาดที่ถืออยู่
- **ลบโค้ด auto corrode ออก** — ทดลองป้อนของเข้าคิวบ์เพื่อทำแต้มปนเปื้อนอัตโนมัติ แต่สั่งรัน
  สูตรไม่สำเร็จ (เรียก `wh.Cube.izp()` แล้วคิวบ์ไม่ใช้ของเลย) จึงถอดออกทั้งหมดแทนที่จะทิ้ง
  โค้ดที่เรียกไม่ถึงไว้ใน release
- `SharedState` เป็น 112 byte — ต้องใช้ hook กับ trainer เวอร์ชันเดียวกัน

### สิ่งที่รู้เพิ่มเกี่ยวกับกล่องโรคระบาด

- กล่องมี**เพดานการถือครอง** ถ้าถือครบแล้วไม่เปิด **กล่องใหม่จะไม่ดร็อบเลย** (ไม่ใช่ดร็อบน้อยลง)
  เป็นข้อจำกัดของที่เก็บ ไม่ใช่เรื่อง drop rate
- ค่าที่เก็บใน `MaxAmountPlagueNormalChest` อ่านได้ 5 ขณะที่เพดานจริงคือ 20 และค่านี้ไม่ขยับ
  ตอนเก็บกล่อง จึง**ไม่ใช่จำนวนกล่อง** — น่าจะเป็นเลเวลอัปเกรด ยังไม่ได้พิสูจน์
- เขียนค่าพวกนี้แล้ว**ไม่ติดข้ามการปิดเปิดเกม** โหลดใหม่จากเซฟทุกครั้ง
- เกมไม่ clamp ค่าเหล่านี้เลย (เขียน 1,000,000,000 ก็รับ) จึงไม่มี "เพดานที่เกมออกแบบไว้"
  ให้ดันไปหา ตัวโอกาสดรอปกล่องจึงเป็น **อ่านอย่างเดียว**
- ชนิดกล่องคือ `EBoxType` (NORMAL 0 / BOSS 1 / ACTBOSS 2) × `EContentType` (NONE 0 / PLAGUE 1)
- ตอนจบด่านเกมยิง request ไป backend (`StageClearFunction`) แล้วรอคำตอบ เป็นเวลาจริงที่
  เร่งสปีดไม่ช่วย จึงเป็นพื้นตายตัวของรอบการเล่น

## เปลี่ยนแปลงใน v1.7.0

- **Bag → stash เมื่อกระเป๋าเต็ม** (checkbox ติ๊กไว้ตั้งแต่เปิดโปรแกรม) เปิดกล่องรัวได้โดยไม่ต้องมาเคลียร์กระเป๋าเอง
  - **หน้า 1 ของคลังเป็นหินวิญญาณเท่านั้น**: หินวิญญาณลงหน้า 1 เสมอ (ลองวางซ้อนกองเดิมก่อน กองละ 5), ของอื่นลงหน้า 2 เป็นต้นไป, ของที่ไม่ใช่หินวิญญาณซึ่งค้างอยู่ในหน้า 1 จะถูกย้ายออก
  - นอกนั้นไม่จัดเรียงอะไรเลย
  - ปุ่ม **Empty bag now** เคลียร์กระเป๋าทันทีโดยไม่ต้องรอเต็ม
  - ย้ายด้วย `rz.ije(MoveRequest, Action<MoveResult>)` แบบเดียวกับผู้เล่นลากวาง เกมจึงเช็คเงื่อนไขของมันเองครบ (เฉพาะ **1.2.8** build อื่นยังไม่ได้ map RVA)
  - ตัวจับ "กระเป๋าเต็ม" ใช้สัญญาณจากตัวเปิดกล่อง (กด 2 ครั้งแล้วจำนวนกล่องไม่ลด) เพราะ slot API ตอบความจุเป็น 260 แถวของตาราง ไม่ใช่ความจุจริงที่ผู้เล่นปลดล็อก
- `SharedState` กลับมาเป็น 104 byte (`rvaSlotMove` 0x64) — ต้องใช้ hook กับ trainer เวอร์ชันเดียวกัน

## เปลี่ยนแปลงใน v1.6.0

- **Max stats เพิ่มเป็น 12 ค่า**: เพิ่ม Area of Effect 8, Skill Range 3, Projectile Count 5, Multistrike 3 (ของเดิม Attack Dmg 1,000,000 / Attack Speed 50 / Crit Chance 1.0 / Crit Damage 100 / CDR 10 / Armor 5,000,000 / Move Speed 60 / Cast Speed 10)
  - offset ทุกตัวดึงมาจาก `Hero.gwo(StatType)` ซึ่งเป็น switch ที่เกมใช้เขียนค่าลง Unit เอง ไม่ได้เดา และยืนยัน Move/Cast Speed ที่หาไว้ใน v1.5.0 ซ้ำอีกรอบ
  - Projectile Count กับ Multistrike ตั้งไว้น้อยตั้งใจ เพราะแต่ละหน่วยคือกระสุน/การตีจริง ใส่เยอะเป็นปัญหา FPS ไม่ใช่ความแรง
  - stat ไม่ได้เป็น ObscuredFloat ทั้งหมด: Skill Range เป็น float ธรรมดา, Projectile Count กับ Multistrike เป็น int ธรรมดา (Multistrike เขียน 2 ที่), Additional EXP เป็น ObscuredInt
  - **แก้บั๊ก ObscuredInt**: ACTk เก็บเป็น `hidden = (value ^ key) + key` ไม่ใช่ XOR ธรรมดา ตอนแรกอ่าน/เขียนผิดทำให้ค่าออกมาเป็นขยะ (เช่น `-1.85e+09` แทน `0`)
  - lock แยกราย stat เพิ่มใน dropdown: Area of Effect / Skill Range / Projectile Count / Multistrike
  - EXP (Increase EXP / Additional EXP) หา offset ได้แล้วแต่ยังไม่ใส่ใน Max stats เพราะเลเวลตันหมดแล้ว ทดสอบไม่ได้ (ยังเห็นค่าได้ใน Scan)
- **ปุ่ม Self-check** (แท็บ Hero): หน้าเดียวจบ แทนการอ่าน log ยาว ๆ — hook ยังตอบไหม, เกมเดินเฟรมกี่ fps, il2cpp/Time/Stash resolve หรือยัง, HP และจำนวน lock ของฮีโร่แต่ละตัว, box API + error, จำนวนช่อง Stash

- **เกมอัปเดตแล้วยังใช้ได้ทันที ไม่ต้องรอ trainer เวอร์ชันใหม่**: ถ้าไม่มี profile ไหนตรง trainer จะ scan หา detector ACTk ทั้ง 6 ตัวใน `GameAssembly.dll` เอง
  - ทั้ง 6 ตัวเรียงตัวเหมือนเดิมทุก build ตั้งแต่ 1.00.08 ถึง 1.2.8 (ระยะห่างจาก `ObscuredCheatingDetector.Check` เท่ากันเป๊ะ) จึงหาโดยจับคู่ prologue ทั้งชุด
  - ต้องเจอ **ตำแหน่งเดียว** และครบทั้ง 6 ตัวเท่านั้นถึงจะยอมใช้ ถ้าเจอหลายที่หรือไม่ครบจะไม่เดา (เขียน RET ผิดที่ = แก้โค้ดเกมมั่ว)
  - build ที่ scan ได้จะเปิดให้แค่ **ACTk bypass + Speedhack** ส่วน Hero stats / Item spawn / Scan ยังปิดไว้จนกว่าจะยืนยัน offset ของ build นั้น
  - ตอนเปิดโปรแกรมจะบอกไว้เลยว่า build ที่ติดตั้งอยู่รู้จักไหม และถ้าไม่รู้จัก scan ผ่านหรือเปล่า
- แก้ **Injector** รายงานผลตรงขึ้น (จาก v1.5.0) และ hook รอ il2cpp นานขึ้น

## เปลี่ยนแปลงใน v1.5.1

- ถอดฟีเจอร์จัดของออกทั้งหมดตามที่สั่ง: **Organize Stash (7 pages)**, checkbox **Organize after opening boxes**, **Sort Stash** และ **Auto Stash (Lv90+)** — ลบทั้งปุ่มและโค้ดใน hook (slot move API, organizer, UI click, auto-stash job)
- เหลือ **Auto open boxes (every 2s)** ไว้อย่างเดียวในกลุ่มนั้น
- `SharedState` เหลือ 100 byte: ตัด `rvaSlotObj` / `rvaSlotCtx` / `rvaSlotAction` / `rvaStashSortAll` / `rvaSlotMove` ที่ไม่มีใครใช้แล้ว (ต้องใช้ hook กับ trainer เวอร์ชันเดียวกัน)
- คำสั่งที่เหลือของ hook: 0 scan, 7 layout dump, 3/4/5/6/8-11/22/23 hero stat lock, 12 one-hit kill, 13 god mode, 14 inventory scan, 16 auto open boxes, 19 stash list (read-only), 24 max stats

## เปลี่ยนแปลงใน v1.5.0

- **Max stats (all heroes)**: ปุ่มเดียวตั้งค่าสถานะสูงสุดให้ฮีโร่ทุกตัวในทีม แล้ว lock ไว้ (hook เขียนซ้ำทุก ~0.2 วิ ฮีโร่ที่เกิดใหม่/ฟื้นก็ได้ด้วย) กด **Release** เพื่อปลด
  - ค่าที่ใช้: Attack Damage 1,000,000 / Attack Speed 50 / Crit Chance 1.0 (100%) / Crit Damage 100 / CDR 10 / Armor 5,000,000 / Move Speed 60 / Cast Speed 10
  - เป็นค่าสูงสุดเท่าที่เกมยังเล่นได้ปกติ (ค่าจริงในเกมตอนนี้ราว ๆ Move Speed 16-30, Cast Speed 0.7-3.6)
- เพิ่ม lock แยกราย stat: **Attack Dmg / Move Speed / Cast Speed** ในกล่อง dropdown (Move Speed = Unit +0x154, Cast Speed = Unit +0x12C ยืนยันจาก property getter ของ 1.2.8)
- แก้ **Injector**: ใช้ full path และเช็คผลของ `LoadLibraryW` จริง เมื่อก่อนถ้าโหลดไม่สำเร็จจะยังขึ้นว่า "DLL injected." แล้วไปพังทีหลังตอนทุกคำสั่ง timeout
- hook รอ il2cpp นานขึ้น (10 วิ → 2 นาที) สำหรับตอนเปิดเกมครั้งแรกหลังรีสตาร์ตเครื่องที่โหลดช้า

## เปลี่ยนแปลงใน v1.4.9

- **Organize Stash (7 pages)**: หน้า 1 Soul stone, 2 ตกแต่ง, 3 สลัก, 4 จารึก, 5 อาวุธหลัก+รอง, 6 หมวก/เกราะ/มือ/เท้า, 7 สร้อย/ต่างหู/แหวน/กำไล
  - ก่อนจัดจะย้ายเกียร์ Lv90+ และวัสดุ 4 หมวดข้างบนจากกระเป๋าเข้า Stash และลากวัสดุชนิดเดียวกันมารวมกอง (สูงสุด 5)
  - ในหน้าเกียร์เรียงตามชิ้น แล้วเกรดสูงก่อน แล้วเลเวล; วัสดุเรียงตามรหัสไอเทม
  - ของที่ไม่อยู่ในหมวด (Crafting / Offering / ETC) และส่วนที่เกิน 49 ชิ้นไปอยู่ท้าย Stash
  - checkbox **Organize after opening boxes**: จัดอัตโนมัติ 4 วินาทีหลังเปิดกล่องใบสุดท้าย
  - ย้ายด้วย `rz.ije(MoveRequest, Action<MoveResult>)` แบบเดียวกับผู้เล่นลากวาง (DRAG = สลับ / รวมกอง)
- `SharedState` ขยายเป็น 120 byte (`rvaSlotMove` 0x74)
- ปุ่ม Sort Stash (Sort ของเกม) จะจัดกลับเป็นแบบของเกม ถ้ากดแล้วให้กด Organize ใหม่

## เปลี่ยนแปลงใน v1.4.8

- แก้ hook ค้าง (ทุกคำสั่ง timeout, อมตะหยุด): เดิม hook เห็น `WM_DESTROY` ของหน้าต่างไหนก็ได้บน thread ของเกม แล้วคิดว่าเกมกำลังปิด แต่หน้าต่าง IME (`MSCTFIME UI`) ถูกปิดและสร้างใหม่เองระหว่างเล่น ตอนนี้ดูเฉพาะหน้าต่างหลักของ Unity, `WM_QUIT` และ `WM_ENDSESSION` เท่านั้น
- Sort Stash เรียก handler ของปุ่ม Sort (`wh.Stash.kcb`) ตรงๆ จัดทุกหน้า ไม่ต้องเปิดหน้า Stash ก่อน
- ระหว่างรองาน main thread (Auto Stash / Sort) ยังเติม HP และ lock ต่อ
- `SharedState` ขยายเป็น 116 byte (`rvaStashSortAll` 0x70)

## เปลี่ยนแปลงใน v1.4.7

- รองรับเกม 1.2.8 (Steam build 25454993, profile `V128`)
- ปุ่ม **Sort Stash**: กดปุ่ม Sort ของหน้า Stash ในเกมแทนผู้เล่น (invoke `UI_Stash.SortingButton` → `Button.onClick` บน main thread)

## เปลี่ยนแปลงใน v1.4.6

- God mode เติม HP ให้ **hero ทุกตัวในด่าน** ทุก ~33 ms (หาจาก `DamageableType.Hero` เหมือนตีทีเดียวตาย) ไม่ผูกกับช่อง UI กัน hero ตายในดินแดนโรคระบาด Torment
- HP lock เพิ่มจาก 10,000,000 เป็น **2,000,000,000** (ต่ำกว่า int.MaxValue เผื่อเกมแปลง HP เป็น int)

## เปลี่ยนแปลงใน v1.4.5

- แก้ hook ค้าง (ทุกคำสั่ง timeout, Stash API not ready) บนบางเครื่อง: เลิกเช็ก frame จาก `SlotInteractionManager.Update` แล้วย้ายตัวดัก per-frame ไปที่ `InputManager.Update` (Auto เปิดกล่องไม่ต้องเปิดหน้ากระเป๋าแล้ว)
- กด Disconnect แล้ว hook ปลด thread ออกจาก il2cpp (`il2cpp_thread_detach`) ปิดเกมแล้วไม่ค้าง Not responding

## เปลี่ยนแปลงใน v1.4.4

- รองรับเกม 1.2.7 (Steam build 25453330, profile `V127`) แก้เฉพาะ trainer, hook เหมือน v1.4.3

## เปลี่ยนแปลงใน v1.4.3

- Auto เปิดกล่อง (checkbox **Auto open boxes (every 2s)** ติ๊กไว้ตั้งแต่เปิดโปรแกรม) เรียก click handler ของ `StageBox` แบบเดียวกับคลิกซ้าย เกมจึงยังเช็กกระเป๋าเต็มเอง ถ้าจำนวนกล่องไม่ลด 5 ครั้งติดจะพัก 30 วิ
- Auto Stash (Lv90+) ย้ายของผ่าน `SlotInteractionManager` บน main thread (detour `SlotInteractionManager.Update`)
- Scan items นับเฉพาะช่อง Stash ที่ปลดล็อกแล้ว (แต่ละคนมีไม่เท่ากัน)
- ปุ่ม Launch Game
- Disconnect / ปิด trainer สั่ง hook ให้หยุด และมี remark ให้กด Disconnect ก่อนปิดเกม
- `SharedState` ขยายเป็น 112 byte (`rvaBoxCount` 0x68, `trainerPaused` 0x6C) ต้องใช้ hook กับ trainer เวอร์ชันเดียวกัน

---

# 3. ข้อมูล offset ของ 1.2.6

Profile: `GameBuildProfile.V126` (BuildId 10206) — layout เหมือน 1.2.4 ทุกอย่าง ต่างกันแค่ address
section `il2cpp` ใช้สูตร file offset = RVA − 0x1400

| Detector | RVA | Disk offset |
|---|---|---|
| ObscuredCheatingDetector.Check | 0x738DE0 | 0x7379E0 |
| ObscuredCheatingDetector.Compare | 0x738F70 | 0x737B70 |
| ObscuredCheatingDetector.CompareExt | 0x739050 | 0x737C50 |
| InjectionDetector.Check | 0x738300 | 0x736F00 |
| SpeedHackDetector.Update | 0x73DCF0 | 0x73C8F0 |
| SpeedHackDetector.OnApplicationPause | 0x73DC60 | 0x73C860 |

| หน้าที่ | Method | RVA |
|---|---|---|
| เพิ่ม item | `wh.vc.jmk(int itemKey, ulong uid, EItemGetSourceType, int count, bool)` | 0x978030 |
| หา item จาก key | `wh.vc.jmu(int)` | 0x979070 |

ตั้งแต่ 1.2.6 obfuscator **โคลน** method เพิ่ม item ออกมา 4 ชุด (`eep`, `jmk`, `hja`, `bva`) และตัวหา item 2 ชุด (`ecq`, `jmu`)
ทุกชุดขนาดเท่ากันและเรียก function ข้างในชุดเดียวกัน (`jml` / `jmm` / `jmn`) จึงใช้ตัวไหนก็ได้ เลือกกลุ่ม `jm*` ไว้
Hero, `StageManager`, `ItemInfoData`, `EGradeType` และ `EItemGetSourceType.MonsterDieReward = 6` เหมือน 1.2.4

---

# 3.1 ข้อมูล offset ของ 1.2.4

Profile: `GameBuildProfile.V124` (BuildId 10204)
วิธี detect: เทียบ signature ของ ACTk ใน memory ก่อน แล้วค่อยเทียบ memory กับ disk และตอนเปิด trainer จะ detect จากไฟล์บน disk เพื่อขึ้น pop-up บอกเวอร์ชัน

## ACTk (หาด้วยการ scan รูปแบบ prologue ใน GameAssembly.dll)

Detector ทั้ง 6 ตัวเรียงตัวเหมือน 1.00.09 แค่เลื่อนไป **+0x807B0** ทั้งชุด
section `il2cpp` ใช้สูตร file offset = RVA − 0x1A00

| Detector | RVA | Disk offset |
|---|---|---|
| ObscuredCheatingDetector.Check | 0x741FF0 | 0x7405F0 |
| ObscuredCheatingDetector.Compare | 0x742180 | 0x740780 |
| ObscuredCheatingDetector.CompareExt | 0x742260 | 0x740860 |
| InjectionDetector.Check | 0x741510 | 0x73FB10 |
| SpeedHackDetector.Update | 0x746F00 | 0x745500 |
| SpeedHackDetector.OnApplicationPause | 0x746E70 | 0x745470 |

## Item API (ได้จาก dump ใน `dump_1.2.4/`)

| หน้าที่ | 1.00.09 | 1.2.4 | RVA |
|---|---|---|---|
| เพิ่ม item | `ue.ti.blk(int, ulong, bool)` | `we.va.jiv(int itemKey, ulong uid, EItemGetSourceType, int count, bool)` | 0x96E930 |
| หา item จาก key | `ue.ti.isr(int)` | `we.va.jjc(int)` | 0x96F2D0 |

- Spawn ส่งเป็น `MonsterDieReward` (source 6) ครั้งละ 1 ชิ้น
- `AddItemResult` ไม่เปลี่ยน (`AddResult` อยู่ที่ +0x4)
- Stash map ไว้แล้วแต่ยังไม่ได้ใช้ (spawn เข้า inventory ตรงๆ): `we.Stash.jxs` 0x9A76C0 (insert), `jyb` 0x9A8B40 (slot), `jxn` 0x9A6C30 (init), `StashSaveData.ctor(int)` 0xA93070

## Hero (ยืนยันด้วย Dump hero layout ในเกม)

เหมือน 1.00.09 ทุกตำแหน่ง:

| ส่วน | Offset |
|---|---|
| `StageManager.HeroList` | +0x30 |
| `Unit.UnitHealthController` (type `pf` → `pq`) | +0xB0 |
| `Unit.b_isHero` | +0x100 |
| Attack Damage (ObscuredFloat) | +0x104 |
| Attack Speed | +0x118 |
| Cast Speed | +0x12C |
| Armor | +0x140 |
| Crit Chance | +0x168 |
| Crit Damage | +0x17C |
| Cooldown Reduction | +0x190 |
| HP ปัจจุบัน / HP สูงสุด (ใน health controller) | +0x40 / +0x4C |

มอนสเตอร์อ่านจาก `Dictionary<DamageableType, HashSet<Unit>>` ของ `StageManager` (+0x58 บน 1.2.4) โดยหา**จาก type ของ field** ไม่ใช้ชื่อ และ `DamageableType.Monster` = 2

---

# 4. สิ่งที่แก้ใน hook ให้ทนการอัปเดตเกม

- **1.2.4 ไม่ export `il2cpp_method_get_pointer`** จึงใช้ `MethodPointer()` อ่านจาก `MethodInfo->methodPointer` (field แรก) แทน ถ้าไม่มีส่วนนี้ การหา method จาก RVA จะล้มเหลวเงียบๆ ทั้งหมด
- **หา item API จาก RVA ไม่ใช้ชื่อ** เพราะ 1.2.4 เอาชื่อสั้นเดิมไปใช้กับ class อื่น (`ti` กลายเป็น class save data) และมี method หลอกที่ signature ซ้ำกัน
- **หา singleton จาก type**: `FindSingletonInstance()` รับ static field ที่ทั้ง type และ class ของค่าเป็น class นั้นเอง ชื่อ field อย่าง `bbwm` เปลี่ยนก็ไม่พัง
- **หา database item จากชื่อ field** (`itemInfoData` + `currencyInfoData`) ที่ obfuscator ไม่แตะ (`yq` บน 1.00.09 = `bco` บน 1.2.4)
- **ตัด fallback ไป 1.00.08 ทิ้ง** พอ trainer ส่ง profile แล้ว RVA ที่ไม่มีจะเป็น 0 แทนที่จะไปเรียก address เก่าซึ่งทำให้เกม crash
- Hero lock เก็บตามช่อง แล้วใส่ค่าซ้ำทุก ~0.2 วินาที (~33 ms ตอน God mode) รวมถึง hero ที่ spawn หรือฟื้นทีหลัง
- `SharedState` ขยายเป็น 84 byte (`rvaItemInfo` ที่ 0x50)

## สิ่งที่แก้ใน trainer

- **Connect ทำครบทุกขั้นในปุ่มเดียว**
- แท็บ Hero Stats: lock ได้ทุก stat รายตัว, `Clear Hero Locks`, checkbox ตีทีเดียวตาย / God mode และปุ่ม **Dump hero layout** สำหรับ build ที่ยังไม่ verify (อ่านแค่ metadata ปลอดภัยทุกเวอร์ชัน)
- Pop-up ตอนเปิดโปรแกรมและตอน Connect บอกว่าเวอร์ชันนี้ใช้อะไรไม่ได้บ้าง
- Minimize ยุบลง system tray
- หาเกมใน Steam library ทุก drive จาก `libraryfolders.vdf`
- `SafeComboBox` กัน crash จาก bug ของ WinForms ตอนกด Ctrl+Backspace ใน dropdown
- ลบแท็บ Extra Features และ `StaticPatcher` ออกแล้ว

---

# 5. ข้อจำกัดบน 1.2.4

## Item ที่ขายบน Steam Market ได้ spawn แล้วใช้ไม่ได้

เกมปฏิเสธของที่ spawn ซึ่งมี flag `IsCanExchangeMarketable = True` โดยขึ้นว่า "ไอเทมไม่ถูกต้อง" เพราะของพวกนี้ต้องมีตัวจริงอยู่ใน Steam Inventory ได้แก่:

- อุปกรณ์รุ่นที่ ItemKey ลงท้าย **`…193`**
- **วัสดุใส่รู**ทุกตัว: ตกแต่ง `11xxxx`, สลัก `12xxxx`, จารึก `13xxxx`
- **วัสดุเพิ่มแต้มกัดกร่อน**: `1501xx`, `1900xx`, `1901xx`
- วัสดุคราฟต์และเครื่องบูชา

**ให้ใช้รุ่น `…191` / `…192` แทน** stat เท่ากันแต่ไม่ติด flag นี้

การทำให้ของ spawn ขายบน Market ได้ เพิ่ม drop rate หรือเพิ่ม % สังเคราะห์ในคิวบ์ สามารถทำได้

## อื่นๆ

- **Scan items** ต้อง map class stash/save ก่อน
- **Attack Damage lock** เปลี่ยนตัวเลขที่แสดง แต่ดาเมจจริงไม่เปลี่ยน (เป็นแบบนี้ตั้งแต่ 1.00.09) จึงไม่เปิดใน UI

---

# 6. ข้อมูลเกมที่ extract ไว้

- `dump_1.2.4/` — ผลจาก Il2CppDumper (`dump.cs`, `il2cpp.h`, `script.json`)
- `dump_1.2.4/csv/` — ตาราง data ของเกม 48 ตาราง ที่ดึงจาก `sharedassets0.assets` เช่น `ItemInfoData`, `GearInfoData`, `HeroInfoData`, `UniqueModInfoData`, `MaterialInfoData`, `StatModInfoData` ใช้หา item ได้โดยไม่ต้องเปิดเกม

## การอ่าน ItemKey ของอุปกรณ์

รูปแบบ `PPGLLV`: 2 หลักแรกคือประเภท, หลักที่ 3 คือ grade, 2 หลักถัดมาคือ level, หลักสุดท้ายคือ variant
ตัวอย่าง `319191` = ธนู, Cosmic (9), Lv90, variant 1

| Grade | เลข | Grade | เลข |
|---|---|---|---|
| ทั่วไป (Common) | 0 | อาร์คานา (Arcana) | 5 |
| ไม่ธรรมดา (Uncommon) | 1 | เหนือกว่า (Beyond) | 6 |
| หายาก (Rare) | 2 | สวรรค์ (Celestial) | 7 |
| ตำนาน (Legendary) | 3 | ศักดิ์สิทธิ์ (Divine) | 8 |
| อมตะ (Immortal) | 4 | จักรวาล (Cosmic) | 9 |

## อาวุธของแต่ละอาชีพ

| อาชีพ | อาวุธหลัก | อาวุธรอง | ตัวอย่างชุด Cosmic Lv90 |
|---|---|---|---|
| Knight | SWORD | SHIELD | `309191` `409191` |
| Ranger | BOW | ARROW | `319191` `419191` |
| Sorcerer | STAFF | ORB | `329192` `429191` |
| Priest | SCEPTER | TOME | `339192` `439192` |
| Hunter | CROSSBOW | BOLT | `349191` `449192` |
| Slayer (DLC) | AXE | HATCHET | `359191` `459192` |

เกราะและเครื่องประดับใช้ร่วมกันได้ทุกอาชีพ: หมวก `509192`, เกราะ `519191`, ถุงมือ `529191`, รองเท้า `539191`, สร้อย `609191`, ต่างหู `619192`, แหวน `629191`, กำไล `639191`

---

# 7. วิธี migrate เมื่อเกมอัปเดตครั้งหน้า

ทุกครั้งที่เกมอัปเดต `GameAssembly.dll` จะถูก compile ใหม่ RVA และชื่อที่ obfuscate ไว้จะเปลี่ยนหมด
Trainer จะปฏิเสธการ patch เมื่อ byte ไม่ตรง (ปลอดภัย) และขึ้น pop-up บอกว่าไม่รองรับ

## ขั้นที่ 1 — ตรวจเวอร์ชัน

อ่าน `Version.txt` ในโฟลเดอร์เกม และ `buildid` ใน `steamapps/appmanifest_3678970.acf`

## ขั้นที่ 2 — หา ACTk RVA ชุดใหม่

Detector ทั้ง 6 ตัวมักเรียงตัวเหมือนเดิมและเลื่อนไปพร้อมกันทั้งชุด ใช้วิธี scan หา prologue 6 byte ของทั้ง 6 ตัวที่ระยะห่างสัมพัทธ์เท่าเดิม แล้วยืนยันว่าเจอตำแหน่งเดียว จากนั้นกรอกลง `GameBuildProfile` เป็น profile ใหม่

prologue ที่ใช้:

| Detector | Signature |
|---|---|
| ObscuredCheatingDetector.Check | `41 56 48 83 EC 20` |
| ObscuredCheatingDetector.Compare / CompareExt | `48 89 5C 24 10 48` |
| InjectionDetector.Check | `40 53 48 83 EC 20` |
| SpeedHackDetector.Update | `40 56 48 83 EC 70` |
| SpeedHackDetector.OnApplicationPause | `48 89 5C 24 08 57` |

อย่าลืมคำนวณ disk offset ใหม่จาก section header (`RVA − (VirtualAddress − PointerToRawData)` ของ section `il2cpp`)

## ขั้นที่ 3 — ยืนยัน layout ของ hero

ต่อเกมแล้วกด **Dump hero layout** ในแท็บ Hero Stats จะได้ field/offset/RVA ของ `StageManager`, `Hero`, `Unit`, health controller และ `ObscuredFloat` โดยอ่านแค่ metadata ไม่แตะ memory ของ object จึงปลอดภัยทุกเวอร์ชัน ไฟล์จะถูกบันทึกไว้ข้างๆ trainer

## ขั้นที่ 4 — หา item API ใหม่

รัน Il2CppDumper กับ `GameAssembly.dll` + `TaskBarHero_Data/il2cpp_data/Metadata/global-metadata.dat` แล้วหาใน `dump.cs`:

- method ที่คืนค่า `AddItemResult` (ตัวเพิ่ม item) → ใส่ `ItemAddRva`
- method static ที่รับ `int` แล้วคืน `ItemInfoData` (ตัวหา item) → ใส่ `ItemInfoRva`

ระวัง: obfuscator ใส่ method หลอกที่ signature ซ้ำกันไว้เยอะ ให้เลือกตัวที่ไม่ซ้ำ และตรวจว่า RVA นั้นไม่ถูกใช้ซ้ำกับ method อื่น
ถ้าเจอหลายตัวที่ signature เหมือนกัน (แบบ 1.2.6) ให้เทียบขนาด function จาก `.pdata` และ call target ข้างใน ถ้าเรียก function ชุดเดียวกันคือโคลนที่ใช้แทนกันได้

## ขั้นที่ 5 — ดึง CSV ของเกม (ถ้าต้องการข้อมูล item)

ตาราง data เก็บเป็น CSV แบบอ่านได้ใน `TaskBarHero_Data/sharedassets0.assets` มองหา BOM (`EF BB BF`) แล้วอ่าน TextAsset ออกมา

## ขั้นที่ 6 — ทดสอบ

| จุดที่ต้องเช็ก | ผลที่ควรได้ |
|---|---|
| Connect | log ขึ้น `Detected game build: <เวอร์ชัน>` |
| ACTk | 6/6 OK |
| Speedhack | ความเร็วเกมเปลี่ยน |
| Hero lock | HP / Attack Speed เปลี่ยนจริงในเกม |
| ตีทีเดียวตาย | มอนตายในทีเดียว ดรอปของปกติ |
| Item spawn | ของเข้า inventory |

---

# 8. การหา pointer ด้วย Cheat Engine (วิธีสำรอง)

ใช้เมื่อ dump ไม่ได้ หรืออยากยืนยันค่าที่เจอ

1. เข้าเกมให้อยู่ในการต่อสู้ที่มี hero
2. Cheat Engine → Attach `TaskBarHero.exe`
3. **Mono → Activate mono features → Dissect mono**
4. ใน `Assembly-CSharp.dll` หา `TaskbarHero.StageManager` (ดู static field ที่เป็น singleton) หรือหา instance ของ `TaskbarHero.Hero` / `TaskbarHero.Unit`
5. ที่ hero: field `UnitHealthController` → ในนั้นหา float ที่เปลี่ยนตอนโดนตี = HP
6. คลิกขวาที่ float นั้น → **Pointer scan** แล้วกรองเฉพาะ chain ที่ base เป็น `GameAssembly.dll` และนิ่ง

**ข้อควรรู้:** ชื่อ class ใน Mono dissector (เช่น `ue.Stash`, `26432251c10 : ue.ti`) ใช้ยืนยันชื่อได้อย่างเดียว ตัวเลขนำหน้าเป็น pointer ของ metadata ไม่ใช่ offset ที่ใช้ใน trainer
ส่วน stat ของ hero เป็น `ObscuredFloat` ของ ACTk จะเห็นเป็นค่าที่เข้ารหัสไว้ ต้องถอดด้วย `hiddenValue XOR cryptoKey` (layout: hash +0x00, hiddenValue +0x04, cryptoKey +0x08, fakeValue +0x0C)

---

# 9. หลักความปลอดภัยของ trainer

- ทุกการเขียน memory และการเขียนไฟล์ผ่านการเช็ก byte signature ก่อน เวอร์ชันไม่ตรงจะปฏิเสธ ไม่เขียนทับมั่ว
- ACTk patch ถูกคืนค่าเดิมตอนกด Disable และตอนปิดโปรแกรม
- Feature ที่ยังไม่ verify กับเวอร์ชันนั้นจะถูกปิดทั้งฝั่ง trainer และฝั่ง hook เพื่อไม่ให้เรียก native code ที่ address ผิด
- ใช้กับการเล่นคนเดียวแบบ offline เท่านั้น

---

# 10. สิ่งที่ยังค้าง

1. Map class stash/save ถ้าอยากได้ Scan items กลับมา
2. ตรวจ offset ทั้งหมดใหม่หลังเกมอัปเดตครั้งหน้า (scan ACTk prologue → Il2CppDumper → Dump hero layout)
3. สืบต่อเรื่อง Attack Damage ว่าเกมอ่านค่าดาเมจจริงจากตรงไหน เพราะการเขียน field ใน `Unit` ไม่มีผลกับดาเมจที่ตีออกไป

---

# เครดิต

- **DENOISER** — ตัว trainer
- **JamalGames** — dump ของเกมและ ACTk
- **KusursuzHacker** — offset จากฟอรัมที่เคยเป็น Extra Features (ลบออกแล้ว)
- **BabyGroot** — CE script ที่เคยเป็น High EXP (ลบออกแล้วใน v1.4.0)

---

# Support

ถ้า trainer ตัวนี้ช่วยคุณได้ สนับสนุนกันได้ที่ **[ko-fi.com/kewiv](https://ko-fi.com/kewiv)** ☕
