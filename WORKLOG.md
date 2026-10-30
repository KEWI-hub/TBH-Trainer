# บันทึกงาน 2026-10-27 – 2026-10-30 (v3.0.0 → v3.1.1)

งานรอบ v3.1.1 (2026-10-28 – 2026-10-30) อยู่ท้ายไฟล์ ในหัวข้อ "รอบ v3.1.1"

สรุปงานรอบนี้ทั้งรอบ เก็บไว้อ่านย้อนหลัง ส่วนคู่มือใช้งานอยู่ที่ README.md

## 1. ตั้งต้นใหม่จาก zip

ที่ถามว่า "ก่อนย้อนก็ทำงานได้ดีอยู่นะ ย้อนทำไม" หมายถึงของที่ merge มาจาก `TBH-Trainer-2.4.0.zip`
ผลตรวจคือ **ไม่มีอะไรหาย** เพราะ zip ตัวนั้นเท่ากับ commit `cdba30a` ทุกไฟล์ (เทียบทีละไฟล์)
และในประวัติก็ไม่มี revert commit เลย

## 2. เกมอัปเดตเป็น 1.3.2

- BuildId 10302, profile `V132`
- RVA → file offset delta: ≤1.3.0 = `0xE00`, 1.3.1 = `0x1200`, 1.3.2 = `0x1A00`
- Steam เขียน `GameAssembly.dll` และ `global-metadata.dat` ใหม่ทั้งคู่ แต่ `Version.txt` ยังเป็น 1.3.2
  → **ดูจาก Version.txt อย่างเดียวไม่พอ** ต้องอ่านไบต์ที่ offset ของ ACTk ทั้ง 6 ตัวมาเทียบกับ prologue ผลรอบนี้ตรงทั้ง 6 ตัว

## 3. Logo / icon

เป็นนกขาวใส่หมวกอัศวิน ใช้ `D:/@ai/NewKewiDownloader/assets/bird-source.png` รวมกับหมวกที่ crop จาก logo เก่า
ไฟล์ ico มีขนาด 16–256 พื้นหลังลบด้วย flood-fill จากมุมภาพ เพราะถ้าลบตามสี ตัวนกสีครีมจะทะลุไปด้วย

## 4. UI

- ติ๊ก Speedhack แล้วไป 5x ทันที ติ๊กออกแล้วกลับเป็น 1x (slider ยังปรับ 1–5 ได้) กล่อง Speed ยุบลงเมื่อไม่ได้ติ๊ก
- หัวข้อ `4. Boxes & Stash` เหลือปุ่ม `Empty bag now` ปุ่มเดียว ปุ่มดีบักอีก 5 ปุ่มแค่ซ่อนไว้ (`Visible = false`) ไม่ได้ลบ
- caption ของ GroupBox ต้องเขียน `&&` เพราะ `&` ตัวเดียวถูกตีความเป็น mnemonic
- tagline บนหัวหน้าต่างวัดความกว้างตัวเองแล้วย่อลง ไม่ให้ทับลิงก์ Ko-fi
- คำอธิบายยาวของ "Bag → stash" ย้ายไปไว้ใน tooltip

## 5. หน้าต่างเปิดที่ Monitor 1

ก่อนหน้านี้ผมรายงานผิดสองครั้งว่าย้ายหน้าต่างแล้ว ความจริงคือ `SetWindowPos` ถูก UIPI ปฏิเสธ (error 5)
เพราะ trainer รันแบบ elevated ตอนนี้แก้แล้วโดยให้แอปวางหน้าต่างตัวเองผ่าน `PlaceOnFirstMonitor()` ที่หา `DISPLAY1`

## 6. Material Stash (บทเรียน)

ตอนแรกผมสรุปผิดว่า "1.3.2 ไม่รับของ" ทั้งที่ทดสอบแค่ชิ้นเดียว และชิ้นนั้นคือ `161003 หีบวัตถุโบราณปนเปื้อน`
ซึ่งเป็นหีบ ไม่ใช่วัสดุคราฟต์ เกมจึงปฏิเสธถูกต้องแล้ว พอทดสอบกับวัสดุจริงก็ย้ายได้ทั้ง 4 ชนิด:

| key | kcb ก่อน → หลัง |
|---|---|
| 110001 | 294 → 295 |
| 112001 | 295 → 300 |
| 113003 | 300 → 302 |
| 114003 | 302 → 305 |

- `TargetIndex` คือ **item key** ไม่ใช่ slot index
- `NoSlot = 2` หมายถึง "ไม่ใช่วัสดุคราฟต์" ไม่ได้แปลว่าช่องเต็ม
- บทเรียน: ก่อนสรุปว่าเป็นปัญหาของ build ให้ดูก่อนว่าของที่ใช้ทดสอบคืออะไร

## 7. ของที่ไม่มี Level req เข้า Stash ปกติ (v3.0.1)

เงื่อนไขเดิม `level < 90` นับ level 0 (ของที่ไม่มีเงื่อนไขเลเวล เช่น หีบหรือ artifact) เป็นของเลเวลต่ำ ของพวกนี้เลยค้างอยู่ในกระเป๋า
แก้ใน `TBHHook/dllmain.cpp` เป็น `it.level > 0 && it.level < kKeepGearLevel`
ผลจาก bagstash: 161003 → stash ช่อง 41, 143001 → Material Stash, 532193 → ช่อง 42, 321193 → ช่อง 43
**ข้อควรรู้:** เงื่อนไข `level > 0` ยังไม่เคยถูกใช้กับ gear จริง

## 8. build.bat

- เปลี่ยนเป็น `call .\build.bat` (เกี่ยวกับ `NoDefaultCurrentDirectoryInExePath`)
- สคริปต์ลูกปิดท้ายด้วย `exit /b 0` เพราะเดิมได้ errorlevel ค้างจาก vswhere จน build ที่ผ่านกลับแสดงว่า FAILED
- เปลี่ยนชื่อ exe เป็น `KEWI_TBH.exe` และใส่ `<Version>3.0.1</Version>` ใน csproj

## 9. Release

- ออก v3.0.0 และ v3.0.1 แล้ว latest คือ v3.0.1 (`KEWI_TBH-v3.0.1.zip`)
- README อัปเดตเป็น v3.0.1 ที่ commit `0151c70` หัวข้อเก่าที่สรุปผิดยังเก็บไว้ แต่มีกรอบเตือนกำกับ
- binary ในเครื่องเป็น 3.0.1 ตรงกันทุกที่

## 10. งานที่ยังค้าง

1. layout ของ Material Stash บน 1.3.2: `kcb()` นับถูก แต่ entry walk ได้ 0 และ qty ได้ −1 (กระทบแค่การรายงาน)
   เบาะแส: `UI_MaterialStash.ocf` rva `0xBED7E0`
2. ยืนยันเงื่อนไข `level > 0` กับ gear จริง
3. ลอง `kStuckNormalMs` และ `tools/il2dump`
4. ย่อความสูงหน้าต่าง (618px แต่เนื้อหาจบที่ราว 450px)

## ข้อตกลงที่ยังใช้อยู่

- ความเสี่ยงโดนแบนไม่ใช่เหตุผลที่จะปฏิเสธเรื่อง Drop Rate
- เลิกทดสอบด้วยการคูณ speed แล้ว ถ้าค้างหรือบัคให้ข้ามไป
- แอปและเกมต้องเปิดที่ Monitor 1
- God Mode ที่ hero ตายนานๆ ครั้ง ข้ามได้
- ไม่เอา Max AOE / Max Projectiles
- Push และ release ทำเมื่อได้รับคำสั่งเท่านั้น
- (เพิ่มรอบ v3.1.1) แก้ปัญหาประสิทธิภาพโดยไม่ปิดฟีเจอร์
- (เพิ่มรอบ v3.1.1) ไม่ spawn ไอเทมที่เกมตั้ง `IsCanExchangeMarketable = True` (ของ Steam Market)

---

# รอบ v3.1.1 (2026-10-28 – 2026-10-30)

## 11. ปุ่ม Spawn Item (ทำแล้วเอาออก)

- คำขอแรกคือปุ่มเสก "หินวิญญาณ - ทรมาน" (190004) และ "หินวิญญาณปนเปื้อน - ทรมาน" (190104)
  ใน `ItemInfoData.csv` ทั้งสองตัวเป็น `IsCanExchangeMarketable = True` คือของที่ขายใน Steam Market จึงไม่ทำ
- ทำปุ่ม Spawn ด้วย Item ID ที่กันของในตลาด (1,113 key) ไว้แทน แล้วผู้ใช้สั่งเอาออก
  revert ทั้งหมด ไม่มี commit ส่วน `do spawn` ในช่อง debug ยังอยู่เหมือนเดิม
- ข้อควรรู้: บน 1.3.1 API เพิ่มไอเทมคืน `AddResult = -1` ทุกครั้ง (เซิร์ฟเวอร์เป็นตัวตัดสิน) ส่วนบน 1.3.2 ยังไม่ได้ลอง

## 12. กด Connect แล้วเกมเหลือ 2–3 fps

วิธีวัด:
- `do speedwatch N` ใช้ได้เฉพาะตอน Connect อยู่
- ถ้าจะวัดตอนสั่งพัก ให้อ่าน `g_frameCount` จาก memory ของ TBHHook.dll เอา RVA มาจากการ build source เดียวกันด้วย `/MAP`
- เทียบตอนพักกับตอนทำงาน โดยเขียน `trainerPaused` (shared memory +0x60)

| สภาพ | fps |
|---|---|
| Connect อยู่ | 2–3 |
| สั่งพัก hook | 60 |
| ปิด God mode / One-hit / Max stats / Bag → stash | ยัง 2–3 |
| ปิด Auto open boxes | ~43 |

- สาเหตุ: `BoxJobTick` เรียก `FindObjectsOfType(StageBox)` ทุก 250 ms และ `ReadGuard` (เพิ่มใน v3.1.0) เรียกทุก 1 วินาที
  เซสชันที่เล่นมานานกินเวลาหลายร้อย ms ต่อครั้ง ส่วน 43 fps ที่เหลือมาจาก guard
- แก้ด้วย `FindObjectsCached`: เก็บ array ไว้หลัง GC handle แล้วค้นใหม่ทุก 10 วินาที (กล่อง) / 30 วินาที (guard) ไม่ได้ปิดฟีเจอร์ไหน

## 13. เกม crash ตอนทดสอบตัวแก้ครั้งแรก (ความผิดของผม)

- ประกาศ `il2cpp_gchandle_*` เป็น `uint32_t` ตามแบบ Unity รุ่นเก่า แต่ Unity 6 ใช้ handle ขนาด pointer
  handle จึงถูกตัดครึ่ง และเกม crash ใน `FindObjectsCached` (crash log ชี้ไปที่ RVA ใน TBHHook ซึ่งแปลงเป็นชื่อฟังก์ชันได้ด้วย map file)
- ยืนยันจาก assembly: `il2cpp_gchandle_get_target` ทำ mask กับ `rcx` เต็ม 64 bit แล้วจึงแก้เป็น `void*`
- บทเรียน: เช็ค signature ของ export กับ GameAssembly.dll จริงก่อนส่ง DLL ให้ทดสอบ

## 14. Box request guard ขึ้น "not read yet"

- ไม่ได้พังเพราะ cache เพราะตัว guard ไม่มีอยู่ในเกมเลยจนกว่าเกมจะเรียก getter ครั้งแรก
- เพิ่มการอ่านจาก static field ของ singleton `ob<StageBoxRequestAbuseGuard>` (หา field จากชนิด เพราะชื่อถูก obfuscate)
  ถ้า field ว่างจึงค้นฉาก รายงานบอกด้วยว่าว่างหรือหาไม่เจอ
- รอบหนึ่งอ่านได้หลังเปิดกล่องใบแรก (20 request / 300 s แล้วบล็อก 10 นาที) แต่อีกรอบเปิด 3 ใบก็ยังไม่มี
  ยังไม่รู้ว่าเกมสร้าง guard ตอนไหน กระทบแค่ Chest log ที่ซ่อนไว้

## 15. Code review ก่อน push

- เจอบั๊ก: array ที่ cache ไว้อาจมี StageBox ที่ถูกทำลายแล้ว (เปลี่ยนด่าน) ซึ่งยังอ่านจำนวนได้
  กดแล้วไม่มีอะไรเปิด และหลัง 2 ครั้ง hook จะคิดว่ากระเป๋าเต็ม
- แก้: เช็ค `m_CachedPtr` (+0x10) ถ้าเป็น null ให้ข้ามแล้วค้นใหม่ทันที
- push ที่ `2719da1`

## 16. Plugin Reverse Engineering

- ติดตั้ง `encidxvii/claude-reverse-engineering-skill` จาก fork ในเครื่อง
  (`~/.claude/local-plugins/reverse-engineering-skill` branch `windows-fixes`, version `2.2.4-win.1`)
  ไม่ได้ push กลับไปที่ upstream
- ตรวจก่อนติดตั้ง: ไม่มี hook / MCP / network และ test ผ่าน 63 ข้อ
- แก้บน Windows:
  - output เป็น UTF-8
  - หา dumpbin / WinDbg / x64dbg / Il2CppDumper ที่ไม่อยู่ใน PATH
  - เพิ่ม task `unity`
  - ไม่นับ strace ของ Git
  - ไม่อ่าน namespace .NET เป็น host
- **อย่ากด Add ในหน้า listing** เพราะจะได้ตัวที่ยังไม่แก้มาซ้อน

## 17. ตรวจซ้ำด้วย skill (dump 1.3.2 จาก Il2CppDumper)

- `m_CachedPtr` อยู่ที่ +0x10 (UnityEngine.Object)
- `ob<a>` มี static field ชนิด `a` ตัวเดียว (`bfol`)
- getter `ob<object>.gnl` (RVA `0x2C3DAE0`) เรียก `FindAnyObjectByType` ถ้าไม่เจอจะ `new GameObject` + `AddComponent` จึงห้ามเรียก
- `gchandle_new` คืน pointer เข้าไปในหน้าขนาด 8 KB ส่วน `free` / `get_target` ใช้ `rcx` เต็ม
- `FindObjectsOfType(Type)` คือ icall `FindObjectsOfType(Type, false)` และมี `FindObjectsByType(Type, SortMode)` ด้วย

## 18. FindObjectsByType(None)

- เปลี่ยนไปใช้ `FindObjectsByType(Type, FindObjectsSortMode.None)` ซึ่งไม่เรียงตาม instance ID
  เลือก overload จากชนิด parameter เพราะ `FindObjectsByType<T>` แบบ generic ก็มี 2 parameter ถ้าหาไม่เจอจะกลับไปใช้ตัวเดิม
- A/B ในเซสชันเดียวกัน สลับทุก 1 นาที 12 รอบ (เขียนสลับ `g_findUnsorted` ผ่าน memory) วิธีละ 48 ครั้ง:

| วิธี | เฉลี่ย | ช้าสุด |
|---|---|---|
| FindObjectsOfType | 14.9 ms | 21.5 ms |
| FindObjectsByType(None) | 8.3 ms | 11.1 ms |

- Self-check แสดงเวลาค้นของทั้งสองวิธี push ที่ `9600c0a`

## 19. Release v3.1.1

- version 3.1.1 ใน csproj / `Updater.cs` และเพิ่มหัวข้อ v3.1.1 ใน README ที่ commit `58fc804` + tag `v3.1.1`
- ทดสอบก่อนออก: Connect ทุกฟีเจอร์ได้ 60 fps
- zip รอบแรกไม่มี exe เพราะ trainer ที่เปิดอยู่ล็อกไฟล์ไว้ ทำใหม่จาก `publish\KEWI_TBH.exe` (hash ตรงกัน) ก่อนสร้าง release
- `KEWI_TBH-v3.1.1.zip` เป็น Latest แล้ว ส่วน notes อยู่ที่ `release/notes-v3.1.1.md` (โฟลเดอร์ `release/` ไม่อยู่ใน git)

## 20. งานที่ยังค้าง (เพิ่มจากข้อ 10)

1. ยังไม่รู้ว่าเกมสร้าง AbuseGuard ตอนไหน
2. ยังไม่ได้ลอง add-item API บน 1.3.2
3. ยังไม่ได้วัด FindObjectsByType ตอนเล่นยาวที่ object ในฉากเยอะ ใช้ Self-check ดูได้
4. การปิดเกมจาก shell ใช้ไม่ได้ทุกครั้ง (`CloseMainWindow` / `taskkill` ไม่มี /F) ต้องให้ผู้ใช้ปิดเอง
   ส่วน trainer รันแบบ elevated ปิดจาก shell ไม่ได้
