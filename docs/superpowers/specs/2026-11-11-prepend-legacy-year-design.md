# 補匯舊年度 Excel 到既有帳本（CLI）設計

- 需求與 grilling 結論：2026-11-11 對話，三輪共 15 題（R1–R3），本文摘錄。
- 相關 ADR：[`0002`](../../adr/0002-continuous-book-no-yearly-rollover.md)（連續帳本，本設計的前提）、[`0006`](../../adr/0006-explicit-snapshot-audit-and-soft-delete.md)（稽核快照）
- 相關 spec：[`p2 §8.1`](2026-10-04-p2-backend-api-design.md)（`import-legacy`）、[`create-book-backend`](2026-10-19-create-book-backend-design.md)（Q14 排除了在 Web 修改開帳日）

## 目標

使用者原本用 Excel 記帳，每年一個檔案。已經用 `import-legacy` 匯入某一年（例如 2026）成為帳本後，要能把**前一年**（2025）的 Excel 接到同一本帳本的前面。接完之後帳本從 2025 開始，2026 以後每一天的帳戶與財務規劃帳戶餘額**完全不變**。

這不是年度結轉：帳本仍然是 ADR 0002 的連續時間軸，只是把時間軸往前延伸。

p2 §8.1 原本假設「搬家只做一次」。本設計修正這個假設：搬家可以分好幾次做，每次往前補一年。否決 API 上傳的理由仍然成立，所以入口仍然只有 CLI。

## 範圍

**包含**

- CLI `import-legacy` 新增 `--into <bookId>` 模式。
- Application 新增 `PrependLegacyYear`（`internal`、`ICliOnlyRequest`）。
- `MappingSession` 支援「對應到既有帳本」模式。
- Domain：`Book` 新增移動期初日與改寫期初餘額的方法。
- 稽核記錄、測試、`CONTEXT.md` 用語。

**排除**

- Web 上傳（p2 §8.1）。
- 接在帳本後面，或補中間缺的年份（R2-Q1）。
- 改名對照（例如對照表檔案）（R2-Q3）。
- 覆寫現有帳本的任何設定（排序、支出性質、名稱）（R3-Q3）。
- 在 Web 修改期初日（create-book Q14 維持不變）。

## 決策

| # | 決定 | 理由 | 被否決的選項 |
|---|---|---|---|
| R1 | 需求從「分年度帳本 + 結轉」改成「把舊年度補進既有帳本」 | ADR 0002 已經決定帳本跨年連續；使用者原本不知道可以跨年記帳 | 年度帳本 + 結轉（推翻 ADR 0002） |
| R2-Q1 | （**已修正，見 M1**）只能接在帳本**最前面**，年度必須相連：`Excel 年度 == (OpeningDate + 1 天).Year − 1`。一次一年，要補更早的年度就再跑一次 | 帳本之後的資料本來就在系統裡記；只接一個邊界，檢查最單純 | 任意年份、補中間的缺口 |
| R2-Q2 | 銜接點檢查不通過就整批不寫入，報告列出每個帳戶的差額 | 與現有匯入「報告有錯誤就整本不寫」一致；保證既有餘額不變 | 用調整交易補差額；只警告並以舊年度為準（會改變既有餘額） |
| R2-Q3 | 設定項目照名稱完全相同（沿用 `LegacyNames` 的正規化）對應。只出現在舊年度的項目就新增並封存。不支援改名對照 | 最簡單、可預測；改名的項目會變成一個已封存的舊項目 | 改名對照表 |
| R2-Q4 | CLI `import-legacy --into <bookId>`，不另外開指令 | 同一份 Excel 讀取與 dry run 流程；只差在寫到新帳本還是既有帳本 | 新指令 `prepend-legacy`；Web 上傳 |
| R2-Q5 | （**已修正，見 M2**）舊年度每一筆交易的日期都必須 ≤ 目前的 `OpeningDate`（以 `LegacyDateNormalizer` 修正後的日期判斷），否則當成錯誤 | 避免銜接點附近依日期算的餘額出現暫時性錯誤，也避免跟既有交易重複 | 允許重疊，只檢查總額 |
| R3-Q1 | 帳本有、舊年度 Excel 沒有的帳戶與財務規劃帳戶：不做銜接檢查，期初餘額不變 | 期初餘額的意思是「開始記帳前就有的錢」，跟哪一年開始用無關 | 當成錯誤；改成 0 再補一筆開戶交易 |
| R3-Q2 | 目標帳本有鎖帳日時，略過鎖帳檢查，鎖帳日不動，報告提醒一行 | 這是補歷史資料，不是修改已鎖的帳；舊年度一樣落在鎖帳範圍內 | 拒絕匯入；自動推移鎖帳日 |
| R3-Q3 | 舊年度帶入流水帳交易與制式表格（轉成預定支出），範圍與現有匯入相同。「設定」工作表只拿來做名稱對應與新增已封存的項目。支出性質不同時以帳本為準，報告給警告 | 不讓舊資料改掉使用者在系統裡整理過的設定 | 以舊年度覆寫設定 |
| R3-Q4 | 稽核記錄寫兩筆：一筆 `Import`／`Book` 摘要（多一個 `previousOpeningDate`），一筆 `Update`／`Book` 記錄期初日與期初餘額的前後快照。支援 `--dry-run` | 這次匯入會修改既有資料，要能追查期初被改過 | 只留摘要 |
| R3-Q5 | 測試：Application 測試用手建的 `LegacyWorkbook`；有真實的舊年度 xlsm 時另外加驗收測試（沒有檔案時 skip） | 邏輯正確性不依賴個資檔案 | 只靠真實檔案 |
| D1 | `PrependLegacyYear(LegacyWorkbook Workbook, string FileName, Guid BookId, bool DryRun)` 是 `internal` 的 `ICliOnlyRequest`，**不**實作 `IBookScoped` | CLI 是直接連 DB 的管理工具，沒有登入身分；`BookScopeConventionTests` 允許 `ICliOnlyRequest` 例外 | 讓 `ImportLegacyBook` 加一個可為 null 的 `BookId`（兩種模式的驗證與流程差很多） |
| D2 | 帳本以 `IBookReader.GetBookForUpdateAsync` 載入，找不到時擲 `NotFoundException`。並行衝突靠 Books 的 xmin（`DbUpdateConcurrencyException`），CLI 印錯誤請使用者重跑 | 沿用既有寫入慣例；Web 同時在改帳本時不會蓋掉對方 | 加 advisory lock（衝突機率極低，重跑即可） |
| D3 | `MappingSession` 新增建構方式：傳入既有 `Book`，設定項目改為「依名稱找，找不到才新增並記入新增清單」；舊年度的期初餘額**另外收集**，不直接寫進 `Account` | 共用流水帳與制式表格的對應邏輯；先算完再決定要不要套用 | 先產生暫時帳本再把 Id 換掉（要重建所有交易與分錄） |
| D4 | 名稱相同但類型不同（例如舊年度是銀行、帳本是信用卡）：錯誤 | 錢不可能從銀行變成信用卡負債；硬接會讓餘額錯亂 | 以帳本的類型為準 |
| D5 | 只出現在舊年度的帳戶與財務規劃帳戶，在銜接點的餘額必須是 0，否則錯誤（列出餘額）。為 0 的就新增並封存 | 銜接點餘額不為 0 代表這筆錢在帳本裡憑空消失，跟 R2-Q2 是同一種對不起來 | 自動在帳本補一個未封存的帳戶（會改變 2026 以後的餘額） |
| D6 | Domain 新增 `Book.PrependHistory(DateOnly newOpeningDate, IReadOnlyDictionary<AccountId, decimal> accountOpenings, IReadOnlyDictionary<PlanningFundId, decimal> fundOpenings)`：新期初日必須早於目前期初日；字典裡的 Id 必須存在於帳本（含對應時新增的項目） | 改期初日與期初餘額是同一個不變條件（後面的餘額不變），要一起做 | 開放 `Account.OpeningBalance` 的 setter |
| D7 | 新增的項目先以未封存狀態加入，交易全部產生完才封存 | 避免交易建立時被封存檢查擋下；封存時也能檢查餘額為 0 | 加入時直接封存 |
| D8 | 寫入順序：交易、預定支出、設定變更、兩筆稽核記錄，全部包在單一 DB transaction | 與 `ImportLegacyBook` 一致：失敗就整批回滾 | — |

## 實作後修正（2026-11-11）

真實資料驗收發現：舊 Excel 是**依月表（歸屬月份）分年**，不是依日期分年。`2026帳本v3` 的 1 月表第一筆是 2025-12-31（歸屬 2026/01），所以帳本期初日是 2025-12-30；`2025記帳` 的 12 月表也有 12/31 的交易。原本的 R2-Q1 因此要求補匯 2024，R2-Q5 也擋下 2025 年 12/31 的交易。兩條一起修正，原決策保留在上表作為紀錄。

| # | 修正 | 理由 | 被否決的選項 |
|---|---|---|---|
| M1（修正 R2-Q1） | 帳本的開始年度 = 帳本裡最早一筆交易（未刪除）的**歸屬月份**的年度；帳本沒有任何交易時，退回 `(OpeningDate + 1 天).Year`。年度必須相連：`Excel 年度 == 帳本開始年度 − 1` | 與 Excel 的分年方式（月表 = 歸屬月份）一致；12/31 入帳、歸屬下一年 1 月的交易不會讓年度錯判。代價是多一次查詢 | 期初日落在 12 月底幾天內就視為下一年（天數是任意的）；`Book` 新增「開始年度」欄位（要 migration，Web 建的帳本還要決定怎麼填） |
| M2（修正 R2-Q5） | 舊年度每一筆交易的日期都必須 ≤ `OpeningDate + 1 天`（帳本第一天） | 要保證的是「帳本第一天起每天的餘額不變」。某天餘額 = 期初 + 當天（含）以前的分錄；舊年度交易都 ≤ 第一天，第一天起每天都會全數計入，再加上銜接檢查（新期初 + 舊年度變動 == 舊期初），餘額必然不變。晚於第一天的交易會讓第一天少算，所以界線剛好是第一天。期初日當天的餘額會變，但那天在帳本開始之前。兩個檔案重複記同一筆 12/31 交易時，銜接差額會剛好等於那筆金額，仍會被擋下 | 維持 ≤ `OpeningDate`（真實資料必然失敗）；用歸屬月份判斷（依日期的餘額仍可能在第一天之後變動） |

## 處理流程

1. 讀取 Excel（沿用 `ExcelLegacyWorkbookReader`），載入目標帳本（D2）。
2. 年度檢查（M1）。不相連時錯誤，不必往下對應。
3. `MappingSession` 以既有帳本模式對應（D3），產生舊年度的交易、預定支出、新增項目清單與舊年度期初餘額。逐列錯誤照舊收集。
4. 日期檢查（M2：≤ 帳本第一天），每一筆超出的交易各一則錯誤（工作表、列號、日期）。
5. 銜接檢查（R2-Q2、R3-Q1、D4、D5）：
   - 舊年度 Excel 與帳本都有的帳戶：`舊年度期初 + 舊年度所有分錄 == 帳本目前期初`。
   - 財務規劃帳戶比照辦理，以財務規劃帳戶的分錄計算。
   - 只在舊年度出現的：`舊年度期初 + 舊年度所有分錄 == 0`。
   - 只在帳本出現的：略過。
6. 報告有錯誤，或是 `--dry-run`：到此為止，不寫入。
7. 寫入（D8）：`Book.PrependHistory`（D6）、加入交易與預定支出、封存新增的項目（D7）、寫稽核記錄（R3-Q4）。

## 報告

沿用 `ImportReport` 的錯誤與警告，另外加一段銜接摘要：

- 期初日：舊 → 新。
- 每個帳戶與財務規劃帳戶的銜接結果：舊年度期初、舊年度變動、推算期末、帳本期初、差額。
- 新增並封存的帳戶、財務規劃帳戶、分類。
- 警告：支出性質不同的分類（R3-Q3）、目標帳本有鎖帳日（R3-Q2）。

## CLI

```
import-legacy --file <xlsm> --into <bookId> [--dry-run]
import-legacy --file <xlsm> --book-name <名稱> --owner-email <email> [--dry-run]   # 現有模式，不變
```

- `--into` 與 `--book-name`／`--owner-email` 互斥，兩種都沒給或混用時直接失敗。
- 結束碼與現有模式相同：報告有錯誤時回 1。

## 稽核記錄

| Action | EntityType | Before | After |
|---|---|---|---|
| `Import` | `Book` | null | `{ fileName, transactions, plannedExpenses, warnings, previousOpeningDate }` |
| `Update` | `Book` | `{ openingDate, accounts: [{ id, name, openingBalance }], planningFunds: [...] }` | 同左，改寫後的值；只列有變動的項目 |

新增並封存的設定項目不逐筆記錄，摘要裡的數字與帳本現況就能看出來。

## 測試

Application 測試（Testcontainers），先用 `ImportLegacyBook` 匯入一年，再用 `PrependLegacyYear` 補前一年：

- 正常銜接：期初日、期初餘額改寫；新年度每天的帳戶與財務規劃帳戶餘額跟補匯前完全相同（用 `BalanceCalculator` 逐日比對，不只比期末）。
- 年度不相連：錯誤，沒有寫入。
- 某個帳戶差 1 元：錯誤、報告列出差額，沒有寫入。
- 舊年度有一筆日期晚於帳本第一天（`OpeningDate + 1`）：錯誤；剛好是第一天：通過（M2）。
- 只在舊年度出現的帳戶：期末為 0 → 新增並封存；期末不為 0 → 錯誤。
- 只在帳本出現的帳戶（Web 新增、有期初餘額）：期初不變，不擋。
- 名稱相同、類型不同：錯誤。
- 支出性質不同：以帳本為準，有警告。
- 有鎖帳日：照樣匯入，鎖帳日不變，有警告。
- dry run：報告與實際匯入相同，資料庫沒有變動。
- 稽核記錄：恰好兩筆，內容正確；失敗時沒有任何記錄。
- 並行：補匯前另一個 context 改過帳本 → `DbUpdateConcurrencyException`。

驗收測試：先匯入本年度 `reference/2026帳本v3.xlsm`，再補舊年度 `reference/2025記帳.xlsm`，銜接檢查必須通過，且 2026 每天的餘額不變。兩個檔案都是個資（`reference/` 已 gitignore），任一個不存在時 skip。

- 路徑定義在 `tests/Shared/RepoPaths.cs`：新增 `PreviousLegacyWorkbook`，預設 `reference/2025記帳.xlsm`，可用 `SIXJARS_LEGACY_PREVIOUS_WORKBOOK` 覆寫。
- 既有的 `LegacyWorkbook` 預設路徑從 `2026帳本v1.xlsm` 改成 `2026帳本v3.xlsm`（`SIXJARS_LEGACY_WORKBOOK` 覆寫不變），`CLAUDE.md` 的檔名一併更新。

## 風險

- `reference/` 底下沒有 `2026帳本v1.xlsm`，所以現在的驗收測試**一直都是 skip**。預設路徑改成 v3 之後，這些測試會第一次真正執行；如果它們有針對 v1 寫死的期望值，可能會失敗。實作時要先單獨跑一次既有的驗收測試，確認結果後再加新測試。
- `reference/2025記帳.xlsm` 目前不在 repo 裡，要由使用者放進來。
