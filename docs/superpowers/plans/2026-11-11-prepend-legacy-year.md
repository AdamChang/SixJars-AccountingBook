# 補匯舊年度 Excel 到既有帳本（CLI）實作計畫

> **For agentic workers:** 逐 Task 執行時使用 TDD：失敗測試 → 確認失敗原因 → 最小實作 → 全綠 → 一個 commit。

**Goal**：`import-legacy --file <xlsm> --into <bookId> [--dry-run]` 把帳本開始前一年的舊 Excel 接到既有帳本最前面。期初日往前移、期初餘額改成舊年度年初值、帶入舊年度交易與預定支出；接完後原本每一天的帳戶與財務規劃帳戶餘額完全不變。任何銜接檢查不通過就整批不寫入。

**Architecture**：Domain 的 `Book` 新增 `PrependHistory`（期初日與期初餘額一起改）。Application 的 `MappingSession` 新增「對應到既有帳本」模式，產出舊年度的交易與期初餘額但**不**改寫既有項目的期初；純函式 `LegacyJunction` 做年度、日期、銜接、類型檢查並產出銜接摘要；`PrependLegacyYear`（`internal`、`ICliOnlyRequest`）串起來並在單一 DB transaction 寫入。CLI `import-legacy` 新增互斥的 `--into` 模式。

**Tech Stack**：.NET 10、EF Core + Npgsql、MediatR、FluentValidation、System.CommandLine、xUnit v3、FluentAssertions、Testcontainers（postgres:17-alpine）。

- 設計文件：[`docs/superpowers/specs/2026-11-11-prepend-legacy-year-design.md`](../specs/2026-11-11-prepend-legacy-year-design.md)（R／D 編號）

---

## 執行前必讀

### 環境

- 工作目錄：`G:\Claude\SixJars-AccountingBook`。
- 分支：從 `master` 開 `feat/prepend-legacy-year`。目前的 `fix/summary-month-end-balances` 只改前端、與本計畫無關；spec、`CONTEXT.md`、本計畫這三個檔案在新分支的第一個 commit 一起提交（`docs(spec): ...`）。
- 後端全部測試：`dotnet test --solution SixJars.slnx`（需要 Docker）。單一類別：`dotnet test --project <proj> --filter-class "*Name*"`。
- 本機若有執行中的 `SixJars.Api`，先停掉（會鎖住 `bin` 的 DLL，build 以 MSB3027 失敗）。

### 基準線

- **開工前實測**：`dotnet build SixJars.slnx`（0 錯誤、0 編譯器警告；既有 NU1902／NU1903 不處理）、`dotnet test --solution SixJars.slnx` 記下總數／失敗／略過。之後數字只增不減。
- 現況：`RepoPaths.LegacyWorkbook` 預設的 `reference/2026帳本v1.xlsm` 不存在，所以驗收測試**全部略過**。Task 1 會改變這個狀況。

### 絕對不要碰

- `.env*`、`reference/`（個資，只讀）、`efbundle.exe`、`TestResults/`、`web/`。
- `git add` 一律指定路徑，禁止 `git add .`／`-A`。

### 慣例

- 註解與 commit 訊息用繁體中文，識別字用英文；commit 結尾 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`。多行 commit 訊息先寫檔再 `git commit -F`。
- CLI 專用 request 必須是 `internal` 並實作 `ICliOnlyRequest`（`BookScopeConventionTests`）。
- 「今天」用注入的 `TimeProvider`。
- 每個 Task 至少做一次變異檢查：改壞實作 → 測試變紅 → 還原。

---

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `src/SixJars.Application/LegacyImport/LegacyPrependMapping.cs` | 既有帳本模式的對應結果 record |
| `src/SixJars.Application/LegacyImport/LegacyJunction.cs` | 年度、日期、銜接、類型檢查；產出 `JunctionLine` 摘要（純函式） |
| `src/SixJars.Application/LegacyImport/PrependLegacyYear.cs` | request＋validator＋handler |
| `tests/SixJars.Domain.Tests/Books/BookPrependHistoryTests.cs` | D6 |
| `tests/SixJars.Application.Tests/LegacyImport/LegacyWorkbookMapperIntoTests.cs` | D3、D4、R3-Q3 |
| `tests/SixJars.Application.Tests/LegacyImport/LegacyJunctionTests.cs` | R2-Q1、R2-Q2、R2-Q5、R3-Q1、D5 |
| `tests/SixJars.Infrastructure.Tests/LegacyExcel/PrependLegacyYearTests.cs` | 寫入、稽核、dry run、鎖帳日、並行、逐日餘額不變 |
| `tests/SixJars.AcceptanceTests/CliPrependAcceptanceTests.cs` | 真實 2026帳本v3 ＋ 2025記帳 |

### 修改

| 檔案 | 修改 |
|---|---|
| `src/SixJars.Domain/Books/Book.cs`、`Account.cs`、`PlanningFund.cs` | `PrependHistory`、`internal SetOpeningBalance` |
| `src/SixJars.Application/LegacyImport/MappingSession.cs`（＋`.Journal.cs`／`.Templates.cs` 視需要） | 既有帳本模式 |
| `src/SixJars.Application/LegacyImport/LegacyWorkbookMapper.cs` | 新增 `MapInto(workbook, book)` |
| `src/SixJars.Cli/CliApp.cs` | `--into` 模式、報告印出銜接摘要、`Row == 0` 不印列號 |
| `tests/Shared/RepoPaths.cs` | 預設改 v3；新增 `PreviousLegacyWorkbook` |
| `tests/SixJars.AcceptanceTests/LegacyWorkbookFixture.cs` | 視需要新增讀舊年度的 fixture |
| `CLAUDE.md` | 檔名 v1 → v3、CLI 說明補 `--into` |

---

## Task 1：驗收測試改讀 `2026帳本v3.xlsm`

先處理，因為它會讓原本一直略過的驗收測試第一次真正執行（spec「風險」）。

- [ ] `tests/Shared/RepoPaths.cs`：預設路徑改 `reference/2026帳本v3.xlsm`；新增

  ```csharp
  /// <summary>補匯驗收用的前一年記帳本；可用環境變數 SIXJARS_LEGACY_PREVIOUS_WORKBOOK 覆寫。</summary>
  public static string PreviousLegacyWorkbook =>
      Environment.GetEnvironmentVariable("SIXJARS_LEGACY_PREVIOUS_WORKBOOK") ?? Path.Combine(Root, "reference", "2025記帳.xlsm");
  ```

- [ ] 跑 `dotnet test --project tests/SixJars.AcceptanceTests` 與 `--project tests/SixJars.Infrastructure.Tests --filter-class "*ExcelLegacyWorkbookReader*"`。
- [ ] **若全綠**：更新 `CLAUDE.md` 的檔名，commit `test(acceptance): 驗收改讀 2026帳本v3.xlsm`。
- [ ] **若有失敗**：停下來，不要改期望值、不要改 Excel。把失敗清單回報使用者決定（可能是 v3 與 v1 的資料差異，也可能是真的 bug）。本計畫後續 Task 不依賴這些測試，可以先回退 `RepoPaths` 預設值、繼續 Task 2，失敗另開 issue。

Expected：驗收測試由「全部略過」變成「實際執行」，略過數減少、總數不變。

## Task 2：Domain `Book.PrependHistory`（D6）

測試 `BookPrependHistoryTests`：

- 期初日改為更早的日期，指定帳戶與財務規劃帳戶的 `OpeningBalance` 改寫，其他不變。
- 新期初日 ≥ 目前期初日 → `DomainException`。
- 字典含不存在的 Id → `DomainException`（沿用 `GetAccount`／`GetPlanningFund` 的訊息）。
- 失敗時帳本完全沒被改（先驗證全部、再套用）。

實作：

```csharp
/// <summary>
/// 把更早年度的歷史接到帳本最前面（補匯舊年度）：期初日往前移，期初餘額改成那一年的年初值。
/// 呼叫端負責保證「新期初 + 新加入的分錄 == 舊期初」，這裡只守住期初日只能往前。
/// </summary>
public void PrependHistory(
    DateOnly newOpeningDate,
    IReadOnlyDictionary<AccountId, decimal> accountOpenings,
    IReadOnlyDictionary<PlanningFundId, decimal> fundOpenings)
{
    if (newOpeningDate >= OpeningDate)
    {
        throw new DomainException($"新的期初日 {newOpeningDate:yyyy-MM-dd} 必須早於目前的期初日 {OpeningDate:yyyy-MM-dd}。");
    }

    var accounts = accountOpenings.Select(p => (Account: GetAccount(p.Key), p.Value)).ToList();
    var funds = fundOpenings.Select(p => (Fund: GetPlanningFund(p.Key), p.Value)).ToList();
    accounts.ForEach(a => a.Account.SetOpeningBalance(a.Value));
    funds.ForEach(f => f.Fund.SetOpeningBalance(f.Value));
    OpeningDate = newOpeningDate;
}
```

`Account`／`PlanningFund` 各加 `internal void SetOpeningBalance(decimal amount) => OpeningBalance = amount;`。

> 對應時新增的項目也在字典裡，改寫成同一個值無害，所以不區分既有與新增（spec D6 已同步）。

Commit：`feat(domain): Book.PrependHistory 往前延伸期初日與期初餘額`

## Task 3：`MappingSession` 既有帳本模式（D3、D4、R3-Q3）

新增 `LegacyWorkbookMapper.MapInto(LegacyWorkbook workbook, Book book) → LegacyPrependMapping`：

```csharp
public sealed record LegacyPrependMapping(
    DateOnly OpeningDate,                                   // 舊年度推算的期初日（沿用 OpeningDate() 規則）
    IReadOnlyList<Transaction> Transactions,
    IReadOnlyList<PlannedExpense> PlannedExpenses,
    IReadOnlyDictionary<AccountId, decimal> AccountOpenings, // Excel 有出現的每個帳戶的舊年度期初（含新增的）
    IReadOnlyDictionary<PlanningFundId, decimal> FundOpenings,
    IReadOnlySet<AccountId> AddedAccounts,
    IReadOnlySet<PlanningFundId> AddedPlanningFunds,
    IReadOnlySet<CategoryId> AddedCategories,
    ImportReport Report);
```

做法：

- `MappingSession` 新增建構子接收既有 `Book`；`Run()` 拆成共用的對應流程，新舊模式只差在 `AddAccounts`／`AddPlanningFunds`／`AddCategories`：
  - 帳戶：`FindAccount(name)` 有 → 類型不同就 `AddError("設定", 0, "帳戶「X」在舊年度是銀行、帳本裡是信用卡")`（D4）；相同就只記下舊年度期初，**不**改 `OpeningBalance`。沒有 → `AddAccount(name, type, 舊年度期初, …)`。
  - 財務規劃帳戶：同上（沒有類型）。
  - 主分類：有 → 種類（收入／支出）不同是錯誤；支出性質不同是警告（R3-Q3：以帳本為準）。沒有 → 新增。子分類：沒有才新增。
- **新增清單不逐一插樁**：建構時記下帳本既有的帳戶、財務規劃帳戶、分類 Id 集合，`Run()` 結束後取差集。這樣流水帳與制式表格過程中自動新增的子分類（`AddSubCategoryWithWarning`、特別支出）也會被算進去。
- 既有模式（`Map`）行為不變：既有的 `LegacyWorkbookMapper*Tests` 必須全綠、不改。

測試 `LegacyWorkbookMapperIntoTests`（純記憶體，先用 `Map` 做出 2026 帳本，再 `MapInto` 2025 workbook）：

- 同名帳戶對到同一個 `AccountId`，舊年度交易的分錄掛在既有帳戶上；既有帳戶的 `OpeningBalance` 沒變，`AccountOpenings` 是舊年度的值。
- 舊年度才有的帳戶／分類／子分類出現在 `Added*`，且已加入帳本、尚未封存。
- 帳戶同名不同類型 → 錯誤。
- 主分類支出性質不同 → 警告，帳本的性質不變。
- 帳本裡有、舊年度沒有的帳戶不出現在 `AccountOpenings`。

Commit：`feat(import): MappingSession 支援對應到既有帳本`

## Task 4：`LegacyJunction` 銜接檢查（R2-Q1、R2-Q2、R2-Q5、R3-Q1、D5）

純函式，handler 在對應**之後**、寫入之前呼叫；輸入是對應前記下的帳本狀態，所以不受對應過程新增項目的影響：

```csharp
public sealed record JunctionLine(string Name, bool IsPlanningFund, decimal PreviousOpening, decimal Movement, decimal ExpectedOpening, decimal? CurrentOpening)
{
    public decimal PreviousClosing => PreviousOpening + Movement;
    /// <summary>null：只在帳本出現、不檢查（R3-Q1）。</summary>
    public decimal? Difference => CurrentOpening is null ? null : PreviousClosing - ExpectedOpening;
}

internal static class LegacyJunction
{
    /// <summary>年度必須剛好是帳本開始那年的前一年（R2-Q1）。對應前呼叫，不符就不必往下。</summary>
    public static string? CheckYear(DateOnly bookOpeningDate, int excelYear);

    /// <summary>日期檢查與銜接檢查；錯誤寫進 report（Sheet = "銜接檢查"、Row = 0），回傳每一列摘要。</summary>
    public static IReadOnlyList<JunctionLine> Check(Book book, DateOnly bookOpeningDate, LegacyPrependMapping mapping, ImportReport report);
}
```

規則：

- 年度：`excelYear == bookOpeningDate.AddDays(1).Year - 1`。
- 日期：每一筆 `Transaction.Date > bookOpeningDate` 各一則錯誤。`Transaction` 沒有來源列號，訊息帶日期、交易類型與金額即可。
- 銜接，以 `AccountOpenings`／`FundOpenings` 為準逐項計算 `Movement`（分錄加總；財務規劃帳戶用 `FundDelta`，與 `BalanceCalculator` 相同算法）：
  - 既有項目：`ExpectedOpening = 目前 OpeningBalance`，差額不為 0 → 錯誤。
  - 新增項目：`ExpectedOpening = 0`，差額不為 0 → 錯誤（D5）。
  - 帳本有、Excel 沒有：列入摘要，`CurrentOpening = null`，不檢查。
- 金額比較用 `decimal` 精確相等，不設容差。

測試 `LegacyJunctionTests`：年度相連／不相連／帳本開始於年中（`OpeningDate = 2026-10-18` → 只接受 2025，且多半會被銜接檢查擋下）、日期剛好等於期初日（通過）與晚一天（錯誤）、差 1 元、新增帳戶期末 0／非 0、只在帳本出現的帳戶。

Commit：`feat(import): 補匯舊年度的銜接檢查`

## Task 5：`PrependLegacyYear` handler（D1、D2、D7、D8、R3-Q2、R3-Q4）

```csharp
internal sealed record PrependLegacyYear(LegacyWorkbook Workbook, string FileName, Guid BookId, bool DryRun)
    : IRequest<PrependLegacyYearResult>, ICliOnlyRequest;

internal sealed record PrependLegacyYearResult(
    bool Written, ImportReport Report, DateOnly PreviousOpeningDate, DateOnly NewOpeningDate,
    IReadOnlyList<JunctionLine> Junction, int Transactions, int PlannedExpenses,
    int ArchivedAccounts, int ArchivedPlanningFunds, int ArchivedCategories);
```

handler 順序：

1. `db.GetBookForUpdateAsync(BookId)`（找不到 → `NotFoundException`）。記下 `previousOpeningDate` 與 Update 稽核用的 Before 快照。
2. `LegacyJunction.CheckYear`；不符 → 錯誤寫進 report，直接回傳。
3. `LegacyWorkbookMapper.MapInto`，接著 `LegacyJunction.Check`。
4. 帳本有鎖帳日 → `report.AddWarning("銜接檢查", 0, "帳本鎖帳日 …，補匯的舊年度交易略過鎖帳檢查")`（R3-Q2）。
5. 有錯誤或 dry run → 回傳，`Written = false`。不 `SaveChanges`，對應過程對 tracked 帳本的修改隨 DbContext 丟棄。
6. 寫入（單一 transaction，D8）：
   - `book.PrependHistory(mapping.OpeningDate, mapping.AccountOpenings, mapping.FundOpenings)`。
   - `db.Transactions.AddRange`、`db.PlannedExpenses.AddRange`。
   - 封存新增項目（D7）：帳戶與財務規劃帳戶用銜接點餘額（必為 0）呼叫 `ArchiveAccount`／`ArchivePlanningFund`；分類 `ArchiveCategory`。時間用 `clock.GetUtcNow()`。
   - 稽核：`Import`／`Book`（After：`fileName, transactions, plannedExpenses, warnings, previousOpeningDate`）；`Update`／`Book`（Before／After：`openingDate` 與**有變動**的帳戶、財務規劃帳戶期初）。
   - 一次 `SaveChangesAsync` → commit。

確認事項（實作時查，不要猜）：

- `ImportLegacyBook` 是分三段 `SaveChanges`（因為新帳本要先存在）。這裡帳本已存在，應可一次存完；若 EF 因相依順序失敗，比照分段，但仍在同一個 transaction 內。
- `PrependHistory` 一定會改 `OpeningDate`，所以 Books 的 xmin 一定被檢查（D2）。

測試 `PrependLegacyYearTests`（比照 `ImportLegacyBookTests` 的 `Host`；先用 `ImportLegacyBook` 匯入 2026 合成帳本，再補 2025）：

- 正常銜接：期初日、期初餘額改寫；**2026 每一天**（期初日 + 1 到最後一筆交易日）每個帳戶與財務規劃帳戶的 `AccountBalanceAsOf`／`FundBalanceAsOf` 與補匯前相同。
- 新增項目已封存；既有項目未被改名、改排序、改性質。
- 年度不相連、差 1 元、日期越界、類型不符：`Written = false`，資料庫與補匯前完全相同（比對帳本、交易數、稽核筆數）。
- 有鎖帳日：寫入成功、鎖帳日不變、有警告。
- dry run：`Report` 與 `Junction` 和實際匯入相同，資料庫沒變。
- 稽核：成功時恰好兩筆（`Import`、`Update`），內容正確；失敗時零筆。
- 並行：載入後另一個 context 改帳本名稱並存檔 → `DbUpdateConcurrencyException`。
- `BookScopeConventionTests` 仍全綠。

Commit：`feat(import): PrependLegacyYear 把舊年度接到既有帳本前面`

## Task 6：CLI `--into`（R2-Q4）

- `--into <Guid>` 與 `--book-name`／`--owner-email` 互斥：`--book-name`、`--owner-email` 從 `Required = true` 改成在 action 裡檢查，兩種模式都沒給或混用 → 印錯誤、回 1。用 `command.Validators` 或在 action 開頭檢查，選 repo 裡已有的寫法。
- `--into` 模式送 `PrependLegacyYear`，印出：
  - 既有的錯誤／警告／修正（`Row == 0` 時不印「第 0 列」）。
  - 期初日 舊 → 新。
  - 銜接摘要表：名稱、舊年度期初、變動、推算期末、帳本期初、差額（只在帳本出現的標「不檢查」）。
  - 新增並封存的數量；成功、dry run、失敗三種結尾訊息，結束碼與現有模式相同。
- 測試（`SixJars.Cli.Tests`，比照既有 `import-legacy` 測試）：互斥檢查、`--into` 不存在的帳本（`NotFoundException` 的訊息與結束碼，比照 `add-member`）、dry run 輸出含銜接摘要、成功輸出。

Commit：`feat(cli): import-legacy --into 補匯舊年度`

## Task 7：真實資料驗收

- `CliPrependAcceptanceTests`：用 CLI 匯入 `RepoPaths.LegacyWorkbook`（v3），記下 2026 每天的餘額 → `import-legacy --file RepoPaths.PreviousLegacyWorkbook --into <id>` 結束碼 0 → 2026 每天的餘額不變。
- 兩個檔案任一不存在 → `Assert.SkipUnless`（比照 `LegacyWorkbookFixture.Require`）。
- 若 `2025記帳.xlsm` 已放入但銜接失敗：**不要**放寬檢查。把銜接摘要回報使用者，判斷是 Excel 本身對不起來還是對應邏輯的 bug。

Commit：`test(acceptance): 補匯 2025 舊年度的真實資料驗收`

## Task 8：文件

- `CLAUDE.md`：CLI 那行補 `import-legacy --into`。
- `docs/superpowers/specs/2026-10-04-p2-backend-api-design.md` §8.1 **不改**（歷史紀錄），由新 spec 說明修正。

Commit：`docs: CLAUDE.md 補 import-legacy --into`

---

## Checkpoint

- [ ] `dotnet build SixJars.slnx`：0 錯誤、0 編譯器警告。
- [ ] `dotnet test --solution SixJars.slnx`：總數 ≥ 基準線 + 新測試，失敗 0。回報時列出略過的測試與原因（驗收測試依 `reference/` 檔案是否存在）。
- [ ] 手動 dry run：本機 DB 先 `import-legacy` 2026帳本v3，再 `--into … --dry-run` 2025記帳，檢查銜接摘要合理。
- [ ] Task 1 的驗收結果已回報使用者。
