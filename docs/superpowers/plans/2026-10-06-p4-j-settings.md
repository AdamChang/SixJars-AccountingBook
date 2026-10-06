# P4 段 J：帳本設定 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL：逐 Task 執行時使用 superpowers:test-driven-development；分派子代理時使用 superpowers:subagent-driven-development。每個 Task 都是「失敗測試 → 確認失敗原因 → 最小實作 → 全綠 → 一個 commit」。

**Goal**：完成 P4 段 J。分類、帳戶、財務規劃帳戶可以新增、改名、封存、排序，沒有被參照時可以刪除；支出主分類可以改支出性質；設定頁另外提供鎖帳日與資料下載；記帳頁的選項排除已封存的項目，並依排序顯示。

**Architecture**：規則都放在 Domain 的 `Book` 聚合，包含排序、封存、改名、刪除與改性質。需要查資料庫的前提，例如餘額、是否被參照、是否有預定支出，由 Application 查好之後當參數傳進 Domain。Application 用三個通用 command（`ArchiveSetting`、`ReorderSettings`、`RemoveSetting`，以 `SettingKind` 分派）加上三個各自的 `Update*` command。前端新增設定頁（`features/settings`），由可重用的 `SettingList` 元件負責 CDK drag-drop 與 ▲▼ 按鈕，再以 `SettingDialog` 處理新增與修改。

**Tech Stack**：.NET 10、EF Core 10.0.12（Npgsql）、MediatR、FluentValidation、xUnit v3、FluentAssertions；Angular 22.2、Angular Material／CDK 22.2、Vitest 5、Playwright 1.63。

- 設計文件：[`docs/superpowers/specs/2026-10-06-p4-settings-planning-budget-reports-design.md`](../specs/2026-10-06-p4-settings-planning-budget-reports-design.md) §3、§7、§8、§10
- 前一期計畫：[`2026-10-04-p3-frontend-pwa.md`](2026-10-04-p3-frontend-pwa.md)（前端慣例、FormDriver、錯誤分類）、[`2026-10-04-p2-backend-api.md`](2026-10-04-p2-backend-api.md)（migration 指令、往返次數）

---

## 執行前必讀

### 環境

- 工作目錄：`F:\VibeCode\SixJars-AccountingBook`（Windows）。分支：`claude/p4-settings-budget-reports`（**不是 master**）。
- 後端全部測試：`dotnet test`（需要 Docker Desktop，`PostgresFixture` 使用 Testcontainers）。
- 後端單一類別：`dotnet test --filter "FullyQualifiedName~BookSettingsTests"`
- Migration：`dotnet ef migrations add <Name> --project src/SixJars.Infrastructure --output-dir Persistence/Migrations`（dotnet-ef 10.0.12）。
- 前端指令都在 `web/` 內執行。Node ≥ 24.15.0。
  - 全部：`npx ng test --watch=false`
  - 單檔：`npx ng test --watch=false --include src/app/<path>.spec.ts`
  - E2E：`npx playwright test`

### 基準線（2026-10-06 本機實測）

- 後端：`dotnet test` 總計 **375**，失敗 0，略過 0（`reference/` 存在時）。`dotnet build` 0 warning。
- 前端：`ng test` **194 passed（24 個檔案）**；Playwright 4 個（P3 回寫的數字，本次沒有重跑）。
- 任何時候數字低於基準線，就是弄壞了東西。以下各 Task 的「Expected」數字是**預測**，以實際為準，只要只增不減即可。

### 絕對不要碰的檔案

- `.env`、`.env.gcp`、`reference/`（個資）、`efbundle.exe`、`web/.angular/`。
- 每個 Task 都用明確路徑 `git add`，**禁止 `git add .`／`git add -A`**。

### 慣例

- 註解、commit 訊息用繁體中文；識別字用英文。commit 訊息結尾加上 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`。
- TDD：先寫失敗測試，確認它**以正確的理由**失敗（編譯錯誤指向「尚未存在的成員」也算正確理由；打錯字不算），再寫實作。一個 Task 一個 commit。
- 後端測試沿用：`ApiFactory.CreateAsync(postgres, Ct)`、`factory.SeedBookAsync(Ct)`、`factory.CreateMemberClientAsync()`、`ApiJson.Options`、`TestContext.Current.CancellationToken`。
- 前端：standalone 元件、signals、zoneless；HTTP 測試用 `HttpTestingController`；Material 元件用 harness。
- **段落順序**：J1–J12 是後端，J12 完成後是**後端 checkpoint**（全綠、既有驗收不變），停下來讓使用者檢視；J13–J19 是前端，J19 完成後是**前端 checkpoint**。

### 本計畫做的決定（spec 沒有寫死、或與 spec 不同的地方）

| # | 決定 | 理由 | 被否決的選項 |
|---|---|---|---|
| D1 | **SortOrder 回填依 `ORDER BY "Id"`**，部署後在設定頁手動調整（使用者 2026-10-06 選定） | 匯入程式在記憶體中是照 Excel 的順序 `Add*`，但資料表沒有順序欄位。Id 是 `Guid.CreateVersion7()`，只精確到毫秒，同一毫秒內的後段位元是隨機的。EF Core 寫入時又會依主鍵排序 INSERT，所以 `ctid` 也不可靠。結論是只能**大致**還原順序 | CLI 重讀 Excel 依名稱重排；清空後重新匯入 |
| D2 | 「被參照、餘額不為 0、有預定支出」都回 **422**，`code` 分別是 `in-use`、`non-zero-balance`；**不用 409** | 後端目前只用 409 表示 `DbUpdateConcurrencyException`。前端 `classifyError` 也把 409 固定歸類成 `conflict`，顯示「這筆交易已在其他裝置修改」。改用 409 會把兩種完全不同的錯誤混在一起。422 加上 `code` 本來就會原樣顯示後端訊息（spec §7 要求的效果） | spec §3.2、§7 原本寫 409。使用者已於 2026-10-06 確認，spec 已同步改為 422 + code |
| D3 | 參照檢查**包含已軟刪除**的交易與預定支出 | `RestoreBackup` 會用 Domain 重建已刪除的交易與預定支出。設定被硬刪除之後，備份就還原不了。未來如果要做「還原軟刪除」也會缺少分類 | 只看未刪除的資料 |
| D4 | DTO 用 `ArchivedAt`（`DateTimeOffset?`），不用 `archived: bool` | 備份要能完整還原封存時間；前端用 `archivedAt !== null` 判斷 | spec §3.2 的 `archived` |
| D5 | 同一組內的 `SortOrder` **永遠是 0..n-1 連續**：新增時接在最後，排序時重新編號，刪除後補齊空號 | 還原備份時是照 DTO 順序呼叫 `Add*` 來重建，只有在編號連續的情況下，重建結果才會和備份完全一樣，`RestoreBook` 的一致性檢查才會通過 | 允許空號（還原時要另外寫入 SortOrder） |
| D6 | 支出主分類改成**浮動**時，只要主分類或子分類有任何預定支出（含已刪除），就拒絕 | `PlannedExpense` 規定分類只能是固定、貸款、特別。改成浮動會讓既有的預定支出違反這個不變條件，還原備份時也會失敗 | 不檢查（spec 只提到預算檢查，沒有想到預定支出） |
| D7 | 封存**不**影響建立或修改交易的後端驗證；只有前端的選項會排除已封存的項目 | 修改舊交易時，原本的帳戶或分類必須仍然可以使用；`TransactionFactory` 不知道這是新增還是修改 | 後端拒絕使用已封存的項目 |
| D8 | 備份格式升到 **v2**，還原時接受 v1 與 v2；v1 不比對 SortOrder | `BackupJson` 有設定 `RespectRequiredConstructorParameters = true`，所以新欄位必須是有預設值的選用參數，v1 的備份檔才能反序列化 | 只接受 v2（P3 期間做的備份就無法還原了） |

---

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `src/SixJars.Application/Books/SettingKind.cs` | `SettingKind` enum 與共用的稽核 entity type 對照 |
| `src/SixJars.Application/Books/UpdateSettings.cs` | `UpdateAccount`、`UpdatePlanningFund`、`UpdateCategory` command、validator、handler |
| `src/SixJars.Application/Books/ArchiveSetting.cs` | `ArchiveSetting`、`UnarchiveSetting`（帳戶與財務規劃帳戶先查餘額） |
| `src/SixJars.Application/Books/ReorderSettings.cs` | `ReorderSettings` command |
| `src/SixJars.Application/Books/RemoveSetting.cs` | `RemoveSetting` command |
| `src/SixJars.Application/Books/SettingReferences.cs` | 參照查詢（含軟刪除） |
| `src/SixJars.Infrastructure/Persistence/Migrations/<ts>_AddSettingsOrderAndArchive.cs`（＋Designer、Snapshot 變更） | 新欄位與 SortOrder 回填 |
| `tests/SixJars.Domain.Tests/Books/BookSettingsTests.cs` | J1–J5 的 Domain 測試 |
| `tests/SixJars.Infrastructure.Tests/Persistence/SettingsOrderMigrationTests.cs` | 回填 SQL 與 round-trip |
| `tests/SixJars.Api.Tests/SettingsMaintenanceEndpointsTests.cs` | J7–J11 的 API 測試 |
| `web/src/app/features/settings/settings-rules.ts`（＋spec） | 分組、列標籤、移動、排序 body（純函式） |
| `web/src/app/features/settings/setting-list.ts`（＋spec） | 單一組的清單：drag-drop、▲▼、選單、已封存區 |
| `web/src/app/features/settings/setting-dialog.ts`（＋spec） | 新增與修改的對話框 |
| `web/src/app/features/settings/settings.page.ts`、`.html`、`.scss`（＋spec） | 設定頁 |
| `web/e2e/settings.spec.ts` | 排序的 E2E |

### 修改

| 檔案 | 改動 |
|---|---|
| `src/SixJars.Domain/Books/{Category,Account,PlanningFund}.cs` | `SortOrder`、`ArchivedAt`、internal 變更方法 |
| `src/SixJars.Domain/Books/Book.cs` | Add 時接在最後；Rename／Archive／Reorder／Remove／ChangeExpenseNature |
| `src/SixJars.Domain/Common/DomainException.cs` | `NonZeroBalanceCode`、`InUseCode` |
| `src/SixJars.Application/Books/BookDto.cs` | DTO 新欄位（選用參數）、依排序輸出 |
| `src/SixJars.Application/Ledger/GetLedgerSummary.cs` | 帳戶、財務規劃帳戶依 SortOrder 輸出 |
| `src/SixJars.Application/Backup/{BackupDocument,RestoreBackup}.cs` | v2、還原封存、v1 相容 |
| `src/SixJars.Api/Endpoints/BooksEndpoints.cs` | PUT、archive、unarchive、DELETE、order |
| `tests/Shared/PostgresFixture.cs` | 可以只 migrate 到指定 migration |
| `tests/SixJars.Api.Tests/BackupExportTests.cs` | `CurrentFormatVersion` 改成 2 |
| `tests/SixJars.Infrastructure.Tests/Backup/RestoreBackupTests.cs` | 情境加上封存與重新排序；v1 測試 |
| `web/src/app/core/api/{dto,book-api}.ts`（＋`book-api.spec.ts`） | 新欄位、設定 API |
| `web/src/app/core/book/current-book.ts`（＋spec） | `reload()` |
| `web/src/app/features/transactions/{transaction-rules,transaction-form}.ts`、`transaction-form.html`（＋兩個 spec） | 排除已封存項目，但保留編輯中交易用到的項目 |
| 前端 fixture：`features/transactions/testing/book-fixture.ts`、`transaction-rules.spec.ts`、`core/api/book-api.spec.ts`、`core/api/summary-api.spec.ts`、`core/book/{book.guard,current-book}.spec.ts`、`features/summary/summary.page.spec.ts`、`e2e/fixtures.ts` | 補上 `sortOrder`、`archivedAt` |
| `web/src/app/app.routes.ts`、`web/src/app/app.html` | `settings` 路由與導覽連結 |

### Task 相依順序

```
後端（全部循序：J1–J5 都改 Book.cs；J8–J11 都改 BooksEndpoints.cs）
執行順序：J1 ─► J6 ─► J2 ─► J3 ─► J4 ─► J5 ─► J7 ─► J8 ─► J9 ─► J10 ─► J11 ─► J12
  J6 必須緊接在 J1 之後：EF Core 9 起，model 與 snapshot 不一致時 Migrate 會擲出
  PendingModelChangesWarning，J1 一加欄位，所有用資料庫的測試就會失敗，直到 migration 產生為止。
  J6 的 round-trip 測試只用到 J1 的欄位，以及 J3、J4 的方法（ReorderAccounts、ArchiveAccount）。
  → 這個測試先只驗證 SortOrder，封存的部分等 J3 完成後再補上（見 J6 Step 1）。
                                                                                              ║ 後端 checkpoint
前端
J13 DTO/API/reload ─┬► J14 記帳選項排除封存（獨立，可與 J15–J17 並行）
                    └► J15 settings-rules ─► J16 SettingList ─┐
                                         J17 SettingDialog ───┴► J18 設定頁＋路由 ─► J19 E2E
                                                                                     ║ 前端 checkpoint
```

---

## Task J1：設定項目的 SortOrder 與 ArchivedAt

**Files:** Create `tests/SixJars.Domain.Tests/Books/BookSettingsTests.cs`／Modify `src/SixJars.Domain/Books/{Category,Account,PlanningFund,Book}.cs`

這個 Task 先把兩個欄位放進 Domain，並讓 `Add*` 自動把新項目接在組內最後。分組規則（spec §3.1）是最容易寫錯的地方：主分類以「同一種類」為一組，收入主分類和支出主分類**各自**從 0 開始編號；子分類以主分類為一組；帳戶、財務規劃帳戶各自一組。匯入程式（`MappingSession`）與還原備份（`RestoreBook`）都是呼叫 `Add*`，所以不用另外修改。

- [ ] **Step 1：寫失敗測試**

```csharp
using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using Xunit;

namespace SixJars.Domain.Tests.Books;

public class BookSettingsTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 6, 1, 0, 0, TimeSpan.Zero);
    private readonly Book _book = new("測試帳本", new DateOnly(2025, 12, 30));

    [Fact]
    public void Added_items_are_appended_to_their_own_group()
    {
        var cash = _book.AddAccount("現金", AccountType.Cash);
        var bank = _book.AddAccount("銀行", AccountType.Bank);
        var fund = _book.AddPlanningFund("旅遊基金");
        var salary = _book.AddIncomeCategory("薪資");
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        var transport = _book.AddExpenseCategory("交通", ExpenseNature.Floating);
        var lunch = _book.AddSubCategory(food.Id, "午餐");
        var dinner = _book.AddSubCategory(food.Id, "晚餐");
        var bus = _book.AddSubCategory(transport.Id, "公車");

        (cash.SortOrder, bank.SortOrder).Should().Be((0, 1));
        fund.SortOrder.Should().Be(0);
        // 收入主分類與支出主分類各自一組，都從 0 開始
        salary.SortOrder.Should().Be(0);
        (food.SortOrder, transport.SortOrder).Should().Be((0, 1));
        // 子分類以主分類為一組
        (lunch.SortOrder, dinner.SortOrder, bus.SortOrder).Should().Be((0, 1, 0));
    }

    [Fact]
    public void New_items_are_not_archived()
    {
        _book.AddAccount("現金", AccountType.Cash).ArchivedAt.Should().BeNull();
        _book.AddPlanningFund("旅遊基金").ArchivedAt.Should().BeNull();
        _book.AddIncomeCategory("薪資").IsArchived.Should().BeFalse();
    }
}
```

`At` 在 J3 之後才會用到，現在先宣告；如果 build 對未使用的欄位出現 warning，就把它移到 J3 再加。

- [ ] **Step 2：跑測試確認失敗**

`dotnet test tests/SixJars.Domain.Tests --filter "FullyQualifiedName~BookSettingsTests"` → 編譯錯誤 `CS1061: 'Account' 未包含 'SortOrder' 的定義`（`ArchivedAt`、`IsArchived` 也一樣）。這是正確的失敗理由。

- [ ] **Step 3：最小實作**

三個實體都加上同樣的成員，以 `Category` 為例（`Account`、`PlanningFund` 照做，建構子多一個 `int sortOrder` 參數）：

```csharp
    internal Category(CategoryId id, string name, CategoryKind kind, ExpenseNature? nature, CategoryId? parentId, int sortOrder)
    {
        // ……既有指派……
        SortOrder = sortOrder;
    }

    /// <summary>同一組內的顯示順序，0 起算且連續（組的定義見 <see cref="Book"/>）。</summary>
    public int SortOrder { get; private set; }
    /// <summary>封存時間；封存的項目不再出現在新增交易的選項中，但既有資料不受影響。</summary>
    public DateTimeOffset? ArchivedAt { get; private set; }
    public bool IsArchived => ArchivedAt is not null;

    internal void MoveTo(int sortOrder) => SortOrder = sortOrder;
```

`Book.cs`：

```csharp
    // 排序的組：帳戶一組、財務規劃帳戶一組、同一種類的主分類一組、同一個主分類的子分類一組（spec §3.1）。
    private IEnumerable<Category> MainCategoriesOf(CategoryKind kind) => _categories.Where(c => c.IsMain && c.Kind == kind);

    private IEnumerable<Category> SubCategoriesOf(CategoryId parentId) => _categories.Where(c => c.ParentId == parentId);

    private static int NextSortOrder<T>(IEnumerable<T> group, Func<T, int> sortOrderOf) =>
        group.Select(sortOrderOf).DefaultIfEmpty(-1).Max() + 1;
```

各個 `Add*` 在建構時傳入 `NextSortOrder(...)`，例如 `new Account(..., NextSortOrder(_accounts, a => a.SortOrder))`；`AddMainCategory` 用 `MainCategoriesOf(kind)`，`AddSubCategory` 用 `SubCategoriesOf(parentId)`。

- [ ] **Step 4：跑單檔測試**：同 Step 2，Expected：2 passed。
- [ ] **Step 5：跑全部測試**：`dotnet test tests/SixJars.Domain.Tests`，全綠。**這時不要跑完整的 `dotnet test`**：model 已經和 snapshot 不一致，用到資料庫的測試會因為 `PendingModelChangesWarning` 而失敗，這是預期中的狀況，J6 會修好。J1 的 commit 只要求 Domain 測試全綠；J6 完成後再一次確認完整套件是 379。
- [ ] **Step 6：Commit**

```bash
git add src/SixJars.Domain/Books/Category.cs src/SixJars.Domain/Books/Account.cs src/SixJars.Domain/Books/PlanningFund.cs src/SixJars.Domain/Books/Book.cs tests/SixJars.Domain.Tests/Books/BookSettingsTests.cs
git commit -m "feat(domain): 設定項目加入 SortOrder 與 ArchivedAt，新增時接在組內最後

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task J2：改名與「計入可用現金」

**Files:** Modify `src/SixJars.Domain/Books/{Book,Account,PlanningFund,Category}.cs`、`tests/SixJars.Domain.Tests/Books/BookSettingsTests.cs`

名稱唯一性**沿用新增時的規則**：帳戶、財務規劃帳戶在各自全帳本內唯一；主分類在所有主分類間唯一（`FindCategory(name)` 不分種類）；子分類在同一個主分類內唯一。容易漏掉的是「改成自己原本的名字」必須允許，以及名稱要經過 `RequireName` 去掉前後空白。

- [ ] **Step 1：寫失敗測試**（加進 `BookSettingsTests`）

```csharp
    [Fact]
    public void Rename_account_keeps_names_unique()
    {
        var cash = _book.AddAccount("現金", AccountType.Cash);
        _book.AddAccount("銀行", AccountType.Bank);

        _book.RenameAccount(cash.Id, "  零用金 ");
        cash.Name.Should().Be("零用金");
        _book.RenameAccount(cash.Id, "零用金");   // 改成原本的名字不算重複

        var act = () => _book.RenameAccount(cash.Id, "銀行");
        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.RuleCode);
    }

    [Fact]
    public void Rename_planning_fund_keeps_names_unique()
    {
        var travel = _book.AddPlanningFund("旅遊基金");
        _book.AddPlanningFund("緊急預備金");

        _book.RenamePlanningFund(travel.Id, "旅行基金");
        travel.Name.Should().Be("旅行基金");
        var act = () => _book.RenamePlanningFund(travel.Id, "緊急預備金");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Main_category_names_are_unique_across_kinds()
    {
        _book.AddIncomeCategory("其它");
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);

        var act = () => _book.RenameCategory(food.Id, "其它");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Sub_category_names_are_unique_within_parent_only()
    {
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        var transport = _book.AddExpenseCategory("交通", ExpenseNature.Floating);
        var lunch = _book.AddSubCategory(food.Id, "午餐");
        _book.AddSubCategory(food.Id, "晚餐");
        var bus = _book.AddSubCategory(transport.Id, "公車");

        _book.RenameCategory(bus.Id, "晚餐");   // 不同主分類可以同名
        bus.Name.Should().Be("晚餐");
        var act = () => _book.RenameCategory(lunch.Id, "晚餐");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Only_cash_accounts_can_count_as_available_cash_when_changed()
    {
        var foreign = _book.AddAccount("外幣現鈔", AccountType.Cash, countsAsAvailableCash: false);
        var bank = _book.AddAccount("銀行", AccountType.Bank);

        _book.SetCountsAsAvailableCash(foreign.Id, true);
        foreign.CountsAsAvailableCash.Should().BeTrue();
        _book.SetCountsAsAvailableCash(bank.Id, false);   // 非現金帳戶設成 false 是允許的（本來就是 false）

        var act = () => _book.SetCountsAsAvailableCash(bank.Id, true);
        act.Should().Throw<DomainException>();
    }
```

- [ ] **Step 2：跑測試確認失敗**：`CS1061: 'Book' 未包含 'RenameAccount' 的定義` 等。
- [ ] **Step 3：最小實作**

三個實體都加上 `internal void Rename(string name) => Name = name;`，`Account` 再加上 `internal void SetCountsAsAvailableCash(bool value) => CountsAsAvailableCash = value;`。

`Book.cs`：

```csharp
    public void RenameAccount(AccountId id, string name)
    {
        var account = GetAccount(id);
        name = RequireName(name);
        if (FindAccount(name) is { } other && other.Id != id)
        {
            throw new DomainException($"帳戶名稱「{name}」已存在。");
        }

        account.Rename(name);
    }

    public void RenamePlanningFund(PlanningFundId id, string name) { /* 同上，用 FindPlanningFund */ }

    public void RenameCategory(CategoryId id, string name)
    {
        var category = GetCategory(id);
        name = RequireName(name);
        var duplicate = category.IsMain
            ? _categories.Find(c => c.IsMain && c.Name == name && c.Id != id)
            : _categories.Find(c => c.ParentId == category.ParentId && c.Name == name && c.Id != id);
        if (duplicate is not null)
        {
            throw new DomainException(category.IsMain ? $"主分類名稱「{name}」已存在。" : $"同一個主分類底下已有子分類「{name}」。");
        }

        category.Rename(name);
    }

    /// <summary>與新增時相同：只有現金帳戶可以計入可用現金。</summary>
    public void SetCountsAsAvailableCash(AccountId id, bool countsAsAvailableCash)
    {
        var account = GetAccount(id);
        if (countsAsAvailableCash && account.Type != AccountType.Cash)
        {
            throw new DomainException($"「{account.Name}」不是現金帳戶，不能計入可用現金。");
        }

        account.SetCountsAsAvailableCash(countsAsAvailableCash);
    }
```

- [ ] **Step 4**：單檔 Expected 7 passed。**Step 5**：`dotnet test` Expected 384。
- [ ] **Step 6：Commit**：`git add` 上述 5 個檔案，訊息 `feat(domain): 帳本設定項目改名與變更計入可用現金`（加上 Co-Authored-By）。

---

## Task J3：封存與解除封存

**Files:** Modify `src/SixJars.Domain/Common/DomainException.cs`、`src/SixJars.Domain/Books/{Book,Account,PlanningFund,Category}.cs`、`BookSettingsTests.cs`、`tests/SixJars.Infrastructure.Tests/Persistence/SettingsOrderMigrationTests.cs`（補上封存的 round-trip 斷言，見 J6）

帳戶與財務規劃帳戶的餘額要從分錄計算，Domain 本身不查詢，所以由呼叫端把餘額傳進來（spec §3.1）。封存主分類**不會**修改子分類的狀態（Q14）。重複封存時保留第一次的時間，讓稽核與備份的時間點保持穩定。

- [ ] **Step 1：寫失敗測試**

```csharp
    [Fact]
    public void Account_with_balance_cannot_be_archived()
    {
        var bank = _book.AddAccount("銀行", AccountType.Bank);

        var act = () => _book.ArchiveAccount(bank.Id, balance: 12.5m, At);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.NonZeroBalanceCode);
        bank.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public void Archive_and_unarchive_account()
    {
        var bank = _book.AddAccount("銀行", AccountType.Bank);

        _book.ArchiveAccount(bank.Id, balance: 0m, At);
        _book.ArchiveAccount(bank.Id, balance: 0m, At.AddDays(1));   // 重複封存保留第一次的時間
        bank.ArchivedAt.Should().Be(At);

        _book.UnarchiveAccount(bank.Id);
        bank.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public void Planning_fund_with_balance_cannot_be_archived()
    {
        var fund = _book.AddPlanningFund("旅遊基金");

        var act = () => _book.ArchivePlanningFund(fund.Id, balance: -1m, At);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.NonZeroBalanceCode);

        _book.ArchivePlanningFund(fund.Id, balance: 0m, At);
        fund.ArchivedAt.Should().Be(At);
        _book.UnarchivePlanningFund(fund.Id);
        fund.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public void Archiving_main_category_leaves_sub_categories_untouched()
    {
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        var lunch = _book.AddSubCategory(food.Id, "午餐");

        _book.ArchiveCategory(food.Id, At);

        food.ArchivedAt.Should().Be(At);
        lunch.ArchivedAt.Should().BeNull();
        _book.UnarchiveCategory(food.Id);
        food.IsArchived.Should().BeFalse();
    }
```

- [ ] **Step 2：跑測試確認失敗**：`CS0117: 'DomainException' 未包含 'NonZeroBalanceCode' 的定義`，以及缺少 `ArchiveAccount` 等方法。
- [ ] **Step 3：最小實作**

`DomainException.cs` 在 `LockedCode` 下方加上：

```csharp
    /// <summary>帳戶或財務規劃帳戶的餘額不為 0，不能封存。</summary>
    public const string NonZeroBalanceCode = "non-zero-balance";
```

三個實體：

```csharp
    internal void Archive(DateTimeOffset at) => ArchivedAt ??= at;

    internal void Unarchive() => ArchivedAt = null;
```

`Book.cs`：

```csharp
    /// <param name="balance">由呼叫端以分錄算出的目前餘額（Domain 不查詢資料庫）。</param>
    public void ArchiveAccount(AccountId id, decimal balance, DateTimeOffset at)
    {
        var account = GetAccount(id);
        EnsureZeroBalance(account.Name, balance);
        account.Archive(at);
    }

    public void UnarchiveAccount(AccountId id) => GetAccount(id).Unarchive();

    public void ArchivePlanningFund(PlanningFundId id, decimal balance, DateTimeOffset at) { /* 同上 */ }

    public void UnarchivePlanningFund(PlanningFundId id) => GetPlanningFund(id).Unarchive();

    /// <summary>封存主分類不改變子分類的狀態；子分類是否可選 = 自己與主分類都未封存（spec §3.1）。</summary>
    public void ArchiveCategory(CategoryId id, DateTimeOffset at) => GetCategory(id).Archive(at);

    public void UnarchiveCategory(CategoryId id) => GetCategory(id).Unarchive();

    private static void EnsureZeroBalance(string name, decimal balance)
    {
        if (balance != 0m)
        {
            throw new DomainException($"「{name}」的餘額為 {balance:N0}，餘額為 0 才能封存。", DomainException.NonZeroBalanceCode);
        }
    }
```

- [ ] **Step 4**：單檔 Expected 11 passed。**Step 5**：`dotnet test` Expected 388。
- [ ] **Step 6：Commit**：`feat(domain): 設定項目的封存與解除封存，帳戶需餘額為 0`。

---

## Task J4：重新排序

**Files:** Modify `src/SixJars.Domain/Books/Book.cs`、`BookSettingsTests.cs`、`SettingsOrderMigrationTests.cs`（round-trip 補上 `ReorderAccounts`，見 J6）

傳入的 Id 清單必須**恰好等於**這一組目前的成員：不能多、不能少，也不能重複（spec §3.1）。這樣可以擋下過時的畫面造成的漏項或重複。已封存的項目也是組的成員，前端送出的順序必須包含它們（J15 會處理）。

- [ ] **Step 1：寫失敗測試**

```csharp
    [Fact]
    public void Reorder_accounts_renumbers_from_zero()
    {
        var a = _book.AddAccount("A", AccountType.Cash);
        var b = _book.AddAccount("B", AccountType.Bank);
        var c = _book.AddAccount("C", AccountType.Bank);

        _book.ReorderAccounts([c.Id, a.Id, b.Id]);

        (c.SortOrder, a.SortOrder, b.SortOrder).Should().Be((0, 1, 2));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    public void Reorder_rejects_ids_that_do_not_match_the_group(string mismatch)
    {
        var a = _book.AddAccount("A", AccountType.Cash);
        var b = _book.AddAccount("B", AccountType.Bank);
        AccountId[] ids = mismatch switch
        {
            "missing" => [a.Id],
            "duplicate" => [a.Id, b.Id, b.Id],
            _ => [a.Id, b.Id, AccountId.New()],
        };

        var act = () => _book.ReorderAccounts(ids);

        act.Should().Throw<DomainException>();
        (a.SortOrder, b.SortOrder).Should().Be((0, 1));
    }

    [Fact]
    public void Reorder_main_categories_only_touches_that_kind()
    {
        var salary = _book.AddIncomeCategory("薪資");
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        var transport = _book.AddExpenseCategory("交通", ExpenseNature.Floating);

        _book.ReorderCategories(CategoryKind.Expense, parentId: null, [transport.Id, food.Id]);

        (transport.SortOrder, food.SortOrder, salary.SortOrder).Should().Be((0, 1, 0));
        var mixed = () => _book.ReorderCategories(CategoryKind.Expense, parentId: null, [transport.Id, food.Id, salary.Id]);
        mixed.Should().Throw<DomainException>();
    }

    [Fact]
    public void Reorder_sub_categories_of_one_parent()
    {
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        var lunch = _book.AddSubCategory(food.Id, "午餐");
        var dinner = _book.AddSubCategory(food.Id, "晚餐");

        // 有 parentId 時以主分類為組，kind 參數不參與判斷
        _book.ReorderCategories(CategoryKind.Expense, food.Id, [dinner.Id, lunch.Id]);

        (dinner.SortOrder, lunch.SortOrder).Should().Be((0, 1));
    }

    [Fact]
    public void Reorder_planning_funds()
    {
        var a = _book.AddPlanningFund("A");
        var b = _book.AddPlanningFund("B");

        _book.ReorderPlanningFunds([b.Id, a.Id]);

        (b.SortOrder, a.SortOrder).Should().Be((0, 1));
    }
```

- [ ] **Step 2：跑測試確認失敗**：缺少 `ReorderAccounts` 等方法的編譯錯誤。
- [ ] **Step 3：最小實作**

```csharp
    public void ReorderAccounts(IReadOnlyList<AccountId> ids) =>
        Reorder(_accounts, ids, a => a.Id, (a, order) => a.MoveTo(order), "帳戶");

    public void ReorderPlanningFunds(IReadOnlyList<PlanningFundId> ids) =>
        Reorder(_planningFunds, ids, f => f.Id, (f, order) => f.MoveTo(order), "財務規劃帳戶");

    /// <summary><paramref name="parentId"/> 有值時排序該主分類的子分類；否則排序 <paramref name="kind"/> 的主分類。</summary>
    public void ReorderCategories(CategoryKind kind, CategoryId? parentId, IReadOnlyList<CategoryId> ids)
    {
        var group = parentId is { } parent ? [.. SubCategoriesOf(GetCategory(parent).Id)] : MainCategoriesOf(kind).ToList();
        Reorder(group, ids, c => c.Id, (c, order) => c.MoveTo(order), "分類");
    }

    private static void Reorder<TItem, TId>(IReadOnlyCollection<TItem> group, IReadOnlyList<TId> ids,
        Func<TItem, TId> idOf, Action<TItem, int> moveTo, string groupName) where TId : notnull
    {
        if (ids.Count != group.Count || ids.Distinct().Count() != ids.Count || !group.Select(idOf).ToHashSet().SetEquals(ids))
        {
            throw new DomainException($"{groupName}的排序清單與目前的項目不一致，請重新載入後再試。");
        }

        var byId = group.ToDictionary(idOf);
        for (var order = 0; order < ids.Count; order++)
        {
            moveTo(byId[ids[order]], order);
        }
    }
```

`ReorderCategories` 的 `group` 型別要統一成 `List<Category>`（上面的寫法裡，兩個分支一個是 collection expression、一個是 `ToList()`，如果無法推斷型別，就兩邊都改用 `.ToList()`）。

- [ ] **Step 4**：單檔 Expected 18 passed。**Step 5**：`dotnet test` Expected 395。
- [ ] **Step 6：Commit**：`feat(domain): 設定項目重新排序，清單必須恰好等於組內成員`。

---

## Task J5：刪除與改支出性質

**Files:** Modify `src/SixJars.Domain/Common/DomainException.cs`、`src/SixJars.Domain/Books/{Book,Category}.cs`、`BookSettingsTests.cs`

刪除之後要把組內剩下的項目重新編號，維持 D5 的連續性。有子分類的主分類不能刪除，必須先刪掉子分類，因為一次刪掉整棵樹的參照檢查會很難說明給使用者。改性質（D6）時要連子分類一起改，因為每一列分類都有自己的 `Nature` 欄位，子分類是在新增時從主分類複製過來的。

- [ ] **Step 1：寫失敗測試**

```csharp
    [Fact]
    public void Referenced_item_cannot_be_removed()
    {
        var bank = _book.AddAccount("銀行", AccountType.Bank);

        var act = () => _book.RemoveAccount(bank.Id, isReferenced: true);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.InUseCode);
        _book.Accounts.Should().Contain(bank);
    }

    [Fact]
    public void Removing_compacts_sort_order_of_the_group()
    {
        var a = _book.AddAccount("A", AccountType.Cash);
        var b = _book.AddAccount("B", AccountType.Bank);
        var c = _book.AddAccount("C", AccountType.Bank);

        _book.RemoveAccount(b.Id, isReferenced: false);

        _book.Accounts.Should().BeEquivalentTo([a, c]);
        (a.SortOrder, c.SortOrder).Should().Be((0, 1));
        _book.AddAccount("D", AccountType.Bank).SortOrder.Should().Be(2);
    }

    [Fact]
    public void Remove_planning_fund_and_sub_category()
    {
        var fund = _book.AddPlanningFund("旅遊基金");
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        var lunch = _book.AddSubCategory(food.Id, "午餐");
        var dinner = _book.AddSubCategory(food.Id, "晚餐");

        _book.RemovePlanningFund(fund.Id, isReferenced: false);
        _book.RemoveCategory(lunch.Id, isReferenced: false);

        _book.PlanningFunds.Should().BeEmpty();
        _book.Categories.Should().BeEquivalentTo([food, dinner]);
        dinner.SortOrder.Should().Be(0);
    }

    [Fact]
    public void Main_category_with_sub_categories_cannot_be_removed()
    {
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        _book.AddSubCategory(food.Id, "午餐");

        var act = () => _book.RemoveCategory(food.Id, isReferenced: false);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.InUseCode);
    }

    [Fact]
    public void Changing_nature_applies_to_sub_categories()
    {
        var other = _book.AddExpenseCategory("其他", ExpenseNature.Special);
        var gift = _book.AddSubCategory(other.Id, "禮金");

        _book.ChangeExpenseNature(other.Id, ExpenseNature.Floating, hasPlannedExpenses: false);

        other.Nature.Should().Be(ExpenseNature.Floating);
        gift.Nature.Should().Be(ExpenseNature.Floating);
    }

    [Fact]
    public void Nature_can_only_change_on_expense_main_category()
    {
        var salary = _book.AddIncomeCategory("薪資");
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        var lunch = _book.AddSubCategory(food.Id, "午餐");

        var onIncome = () => _book.ChangeExpenseNature(salary.Id, ExpenseNature.Fixed, hasPlannedExpenses: false);
        var onSub = () => _book.ChangeExpenseNature(lunch.Id, ExpenseNature.Fixed, hasPlannedExpenses: false);

        onIncome.Should().Throw<DomainException>();
        onSub.Should().Throw<DomainException>();
    }

    [Fact]
    public void Category_with_planned_expenses_cannot_become_floating()
    {
        var insurance = _book.AddExpenseCategory("保險", ExpenseNature.Fixed);

        var toFloating = () => _book.ChangeExpenseNature(insurance.Id, ExpenseNature.Floating, hasPlannedExpenses: true);
        toFloating.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.InUseCode);

        // 固定 → 特別仍然是預定支出允許的性質
        _book.ChangeExpenseNature(insurance.Id, ExpenseNature.Special, hasPlannedExpenses: true);
        insurance.Nature.Should().Be(ExpenseNature.Special);
    }
```

- [ ] **Step 2：跑測試確認失敗**：缺少 `InUseCode`、`RemoveAccount`、`ChangeExpenseNature` 的編譯錯誤。
- [ ] **Step 3：最小實作**

`DomainException.cs`：

```csharp
    /// <summary>設定項目仍被使用（交易、預定支出、子分類……），不能刪除或變更。</summary>
    public const string InUseCode = "in-use";
```

`Category.cs`：`internal void ChangeNature(ExpenseNature nature) => Nature = nature;`

`Book.cs`：

```csharp
    /// <param name="isReferenced">由呼叫端查詢：是否有交易或預定支出（含已軟刪除）使用這個帳戶。</param>
    public void RemoveAccount(AccountId id, bool isReferenced)
    {
        var account = GetAccount(id);
        EnsureUnreferenced(account.Name, isReferenced);
        _accounts.Remove(account);
        Compact(_accounts, a => a.SortOrder, (a, order) => a.MoveTo(order));
    }

    public void RemovePlanningFund(PlanningFundId id, bool isReferenced) { /* 同上 */ }

    public void RemoveCategory(CategoryId id, bool isReferenced)
    {
        var category = GetCategory(id);
        if (category.IsMain && SubCategoriesOf(id).Any())
        {
            throw new DomainException($"「{category.Name}」底下還有子分類，請先刪除子分類。", DomainException.InUseCode);
        }

        EnsureUnreferenced(category.Name, isReferenced);
        _categories.Remove(category);
        var siblings = category.ParentId is { } parentId ? SubCategoriesOf(parentId) : MainCategoriesOf(category.Kind);
        Compact(siblings, c => c.SortOrder, (c, order) => c.MoveTo(order));
    }

    /// <summary>
    /// 改支出主分類的性質，回溯生效，子分類一起改（spec §3.1、Q4）。
    /// 預定支出只允許固定、貸款、特別，所以有預定支出（含已刪除）時不能改成浮動。
    /// L 段會再加上「有預算時拒絕」。
    /// </summary>
    public void ChangeExpenseNature(CategoryId id, ExpenseNature nature, bool hasPlannedExpenses)
    {
        var category = GetCategory(id);
        if (!category.IsMain || category.Kind != CategoryKind.Expense)
        {
            throw new DomainException($"只有支出主分類可以修改支出性質，「{category.Name}」不是。");
        }

        if (category.Nature == nature)
        {
            return;
        }

        if (nature == ExpenseNature.Floating && hasPlannedExpenses)
        {
            throw new DomainException($"「{category.Name}」有預定支出，不能改成浮動支出。", DomainException.InUseCode);
        }

        category.ChangeNature(nature);
        foreach (var sub in SubCategoriesOf(id))
        {
            sub.ChangeNature(nature);
        }
    }

    private static void EnsureUnreferenced(string name, bool isReferenced)
    {
        if (isReferenced)
        {
            throw new DomainException($"「{name}」已有交易或預定支出使用，不能刪除；可以改用封存。", DomainException.InUseCode);
        }
    }

    private static void Compact<T>(IEnumerable<T> group, Func<T, int> sortOrderOf, Action<T, int> moveTo)
    {
        var order = 0;
        foreach (var item in group.OrderBy(sortOrderOf).ToList())
        {
            moveTo(item, order++);
        }
    }
```

- [ ] **Step 4**：單檔 Expected 25 passed。**Step 5**：`dotnet test` Expected 402。
- [ ] **Step 6：Commit**：`feat(domain): 刪除未被參照的設定項目、修改支出主分類的性質`。

---

## Task J6：EF 對應、migration 與 SortOrder 回填

**Files:** Create `tests/SixJars.Infrastructure.Tests/Persistence/SettingsOrderMigrationTests.cs`、migration 三個檔案／Modify `src/SixJars.Infrastructure/Persistence/BookConfiguration.cs`（若需要）、`tests/Shared/PostgresFixture.cs`

**緊接在 J1 之後執行**（見 Task 相依順序）。這時 Domain 還沒有 `ReorderAccounts`、`ArchiveAccount`，所以 round-trip 測試先只驗證「新增順序的 SortOrder 存得進去、讀得回來」。J3 完成時，在同一個測試裡補上封存的斷言（`book.ArchiveAccount(...)` 與 `ArchivedAt` 的檢查），J4 完成時補上 `ReorderAccounts`，並和那兩個 Task 一起 commit。下面的程式碼是 J4 完成後的最終形狀。

EF 依慣例就會對應 `SortOrder`（int，非 null）與 `ArchivedAt`（timestamptz，可為 null），`BookConfiguration` 原則上不用改；`IsArchived` 是只讀的計算屬性，EF 會忽略。這個 Task 的重點在於**回填**。`AddColumn` 預設全部是 0，所以要依 D1 用 `ROW_NUMBER() OVER (PARTITION BY … ORDER BY "Id") - 1` 回填。分類的組是 `("BookId", "Kind", "ParentId")`，PostgreSQL 的 `PARTITION BY` 會把 `NULL` 視為同一組，剛好對應到「同一種類的主分類」。

- [ ] **Step 1：寫失敗測試**

`PostgresFixture` 加一個方法（放在 `CreateConnectionStringAsync` 旁邊）：

```csharp
    /// <summary>只套用到 <paramref name="targetMigration"/> 為止的資料庫（測試 migration 的資料回填）。</summary>
    public async Task<string> CreateConnectionStringAtAsync(string targetMigration, CancellationToken cancellationToken)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = $"t_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var db = new SixJarsDbContext(Options(connectionString));
        await db.Database.MigrateAsync(targetMigration, cancellationToken);
        return connectionString;
    }
```

測試：

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Persistence;

public class SettingsOrderMigrationTests(PostgresFixture postgres)
{
    private const string BeforeSettingsOrder = "20261004050139_AddDataProtectionKeys";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migration_backfills_sort_order_by_id_within_each_group()
    {
        var connectionString = await postgres.CreateConnectionStringAtAsync(BeforeSettingsOrder, Ct);
        var bookId = Guid.CreateVersion7();
        // Id 以字面值寫死，刻意讓插入順序與 Id 順序相反，證明回填依 Id 而不是依實體順序
        const string accountLater = "00000000-0000-7000-8000-000000000002", accountEarlier = "00000000-0000-7000-8000-000000000001";
        const string income = "00000000-0000-7000-8000-000000000010";
        const string food = "00000000-0000-7000-8000-000000000011", transport = "00000000-0000-7000-8000-000000000012";
        const string dinner = "00000000-0000-7000-8000-000000000022", lunch = "00000000-0000-7000-8000-000000000021";
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand($"""
                INSERT INTO "Books" ("Id", "Name", "OpeningDate") VALUES ('{bookId}', '帳本', '2025-12-30');
                INSERT INTO "Accounts" ("Id", "Name", "Type", "OpeningBalance", "CountsAsAvailableCash", "BookId") VALUES
                  ('{accountLater}', '銀行', 'Bank', 0, false, '{bookId}'),
                  ('{accountEarlier}', '現金', 'Cash', 0, true, '{bookId}');
                INSERT INTO "Categories" ("Id", "Name", "Kind", "Nature", "ParentId", "BookId") VALUES
                  ('{transport}', '交通', 'Expense', 'Floating', NULL, '{bookId}'),
                  ('{food}', '飲食', 'Expense', 'Floating', NULL, '{bookId}'),
                  ('{income}', '薪資', 'Income', NULL, NULL, '{bookId}'),
                  ('{dinner}', '晚餐', 'Expense', 'Floating', '{food}', '{bookId}'),
                  ('{lunch}', '午餐', 'Expense', 'Floating', '{food}', '{bookId}');
                """, connection);
            await command.ExecuteNonQueryAsync(Ct);
        }

        await using var db = new SixJarsDbContext(new DbContextOptionsBuilder<SixJarsDbContext>()
            .UseNpgsql(SixJarsConnectionString.ForNpgsql(connectionString)).Options);
        await db.Database.MigrateAsync(Ct);
        var book = await db.Books.AsNoTracking().SingleAsync(Ct);

        Order(book.Accounts.Select(a => (a.Id.Value, a.SortOrder))).Should().Equal(Guid.Parse(accountEarlier), Guid.Parse(accountLater));
        book.Accounts.Select(a => a.SortOrder).Should().BeEquivalentTo([0, 1]);
        book.GetCategory(new CategoryId(Guid.Parse(income))).SortOrder.Should().Be(0);
        book.GetCategory(new CategoryId(Guid.Parse(food))).SortOrder.Should().Be(0);
        book.GetCategory(new CategoryId(Guid.Parse(transport))).SortOrder.Should().Be(1);
        book.GetCategory(new CategoryId(Guid.Parse(lunch))).SortOrder.Should().Be(0);
        book.GetCategory(new CategoryId(Guid.Parse(dinner))).SortOrder.Should().Be(1);
        book.Categories.Should().OnlyContain(c => c.ArchivedAt == null);
    }

    [Fact]
    public async Task Sort_order_and_archive_round_trip()
    {
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        var archivedAt = new DateTimeOffset(2026, 10, 6, 1, 0, 0, TimeSpan.Zero);
        var book = new Book("帳本", new DateOnly(2025, 12, 30));
        var a = book.AddAccount("A", AccountType.Cash);
        var b = book.AddAccount("B", AccountType.Bank);
        book.ReorderAccounts([b.Id, a.Id]);
        book.ArchiveAccount(a.Id, balance: 0m, archivedAt);
        await using (var db = createContext())
        {
            db.Books.Add(book);
            await db.SaveChangesAsync(Ct);
        }

        await using var read = createContext();
        var loaded = await read.Books.AsNoTracking().SingleAsync(Ct);
        loaded.GetAccount(b.Id).SortOrder.Should().Be(0);
        loaded.GetAccount(a.Id).SortOrder.Should().Be(1);
        loaded.GetAccount(a.Id).ArchivedAt.Should().Be(archivedAt);
    }

    private static IEnumerable<Guid> Order(IEnumerable<(Guid Id, int SortOrder)> items) =>
        items.OrderBy(i => i.SortOrder).Select(i => i.Id);
}
```

- [ ] **Step 2：跑測試確認失敗**：`dotnet test tests/SixJars.Infrastructure.Tests --filter "FullyQualifiedName~SettingsOrderMigrationTests"`。在還沒產生 migration 之前，`MigrateAsync` 不會建立新欄位，讀取時會出現 `42703: column a.SortOrder does not exist`；如果 EF 10 先擲出 pending model changes，也算正確的失敗。
- [ ] **Step 3：實作**
  1. `dotnet ef migrations add AddSettingsOrderAndArchive --project src/SixJars.Infrastructure --output-dir Persistence/Migrations`
  2. 檢查產生的 `Up`：三張表各有 `AddColumn<int>("SortOrder", nullable: false, defaultValue: 0)` 與 `AddColumn<DateTimeOffset>("ArchivedAt", type: "timestamp with time zone", nullable: true)`。
  3. 在 `Up` 的最後加上回填：

```csharp
            // SortOrder 回填（P4 J plan D1）：Id 是 UUIDv7，依 Id 排序可以大致還原建立順序；
            // 同一毫秒內建立的項目順序不保證，部署後由使用者在設定頁調整。
            migrationBuilder.Sql("""
                UPDATE "Accounts" AS t SET "SortOrder" = o.rn - 1
                FROM (SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "BookId" ORDER BY "Id") AS rn FROM "Accounts") AS o
                WHERE t."Id" = o."Id";
                UPDATE "PlanningFunds" AS t SET "SortOrder" = o.rn - 1
                FROM (SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "BookId" ORDER BY "Id") AS rn FROM "PlanningFunds") AS o
                WHERE t."Id" = o."Id";
                UPDATE "Categories" AS t SET "SortOrder" = o.rn - 1
                FROM (SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "BookId", "Kind", "ParentId" ORDER BY "Id") AS rn FROM "Categories") AS o
                WHERE t."Id" = o."Id";
                """);
```

  `Down` 只要 `DropColumn`，不需要反向處理資料。
- [ ] **Step 4**：單檔 Expected 2 passed。**Step 5**：`dotnet test` Expected 379（J1 的 377＋2），用到資料庫的測試全部恢復綠色。
- [ ] **Step 6：Commit**：`git add` 三個 migration 檔（含 `SixJarsDbContextModelSnapshot.cs`）、`tests/Shared/PostgresFixture.cs`、`tests/SixJars.Infrastructure.Tests/Persistence/SettingsOrderMigrationTests.cs`（`BookConfiguration.cs` 如果有改也要加），訊息為 `feat(infra): 設定項目排序與封存欄位，SortOrder 依 Id 回填`。

---

## Task J7：BookDto 帶出排序與封存，並依排序輸出

**Files:** Create `tests/SixJars.Api.Tests/SettingsMaintenanceEndpointsTests.cs`／Modify `src/SixJars.Application/Books/BookDto.cs`、`src/SixJars.Application/Ledger/GetLedgerSummary.cs`

分類維持扁平清單，但輸出順序改成「樹的前序」：先是收入主分類，再是支出主分類（`CategoryKind` 的 enum 順序），每個主分類後面緊接著它的子分類，各自依 SortOrder 排列。這樣記帳頁的下拉選單不用另外排序。新欄位是**有預設值的選用參數**（D8），這是為了讓舊版的 v1 備份能夠反序列化。由於 EF 讀出來的 owned 集合沒有固定順序，所以測試要先在資料庫裡把順序打亂，否則可能碰巧通過。

- [ ] **Step 1：寫失敗測試**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Application.Ledger;
using SixJars.Domain.Books;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

public class SettingsMaintenanceEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Book_lists_settings_in_sort_order()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await ReverseAccountsAndExpenseMainsAsync(factory, book);
        var client = await factory.CreateMemberClientAsync();

        var dto = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;

        dto.Accounts.Select(a => a.Name).Should().Equal("房屋貸款", "悠遊卡", "國泰Combo卡", "國泰世華銀行", "現金");
        dto.Accounts.Select(a => a.SortOrder).Should().Equal(0, 1, 2, 3, 4);
        // 前序：收入主分類 → 支出主分類（每個主分類後接它的子分類）
        dto.Categories.Select(c => c.Name).Should().Equal("工作薪資", "貸款支出", "房屋貸款", "固定支出", "保險費", "主食", "午餐");
        dto.Accounts.Should().OnlyContain(a => a.ArchivedAt == null);
    }

    [Fact]
    public async Task Summary_lists_accounts_in_sort_order()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await ReverseAccountsAndExpenseMainsAsync(factory, book);
        var client = await factory.CreateMemberClientAsync();

        var summary = (await client.GetFromJsonAsync<LedgerSummaryDto>(
            Url(book, "/summary?budgetMonth=202602&asOf=2026-02-20"), ApiJson.Options, Ct))!;

        summary.Accounts.Select(a => a.Name).Should().Equal("房屋貸款", "悠遊卡", "國泰Combo卡", "國泰世華銀行", "現金");
    }

    internal static string Url(Book book, string path) => $"/api/books/{book.Id.Value}{path}";

    /// <summary>直接在資料庫裡把順序倒過來，證明輸出是依 SortOrder，而不是碰巧依插入順序。</summary>
    private static async Task ReverseAccountsAndExpenseMainsAsync(ApiFactory factory, Book book)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ISixJarsDbContext>();
        var tracked = await db.Books.SingleAsync(b => b.Id == book.Id, Ct);
        tracked.ReorderAccounts([.. tracked.Accounts.OrderByDescending(a => a.SortOrder).Select(a => a.Id)]);
        tracked.ReorderCategories(CategoryKind.Expense, parentId: null,
            [.. tracked.Categories.Where(c => c.IsMain && c.Kind == CategoryKind.Expense).OrderByDescending(c => c.SortOrder).Select(c => c.Id)]);
        await db.SaveChangesAsync(Ct);
    }

    internal static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
```

`ApiSeed` 的建立順序是：主食、固定支出、貸款支出，所以倒過來之後是貸款支出、固定支出、主食。`LedgerSummaryDto` 的型別名稱與 `Accounts` 屬性，執行時以 `src/SixJars.Application/Ledger/LedgerSummaryDto.cs` 為準。

- [ ] **Step 2：跑測試確認失敗**：編譯錯誤，`AccountDto` 沒有 `SortOrder`、`ArchivedAt`。
- [ ] **Step 3：最小實作**

```csharp
    public static BookDto From(Book book) => new(
        book.Id.Value,
        book.Name,
        book.OpeningDate,
        book.LockDate,
        [.. book.Accounts.OrderBy(a => a.SortOrder).Select(AccountDto.From)],
        [.. book.PlanningFunds.OrderBy(f => f.SortOrder).Select(PlanningFundDto.From)],
        [.. InTreeOrder(book.Categories).Select(CategoryDto.From)]);

    /// <summary>收入主分類、支出主分類各依 SortOrder；每個主分類後面接它的子分類（記帳頁的下拉選單直接用這個順序）。</summary>
    private static IEnumerable<Category> InTreeOrder(IReadOnlyList<Category> categories) =>
        categories.Where(c => c.IsMain).OrderBy(c => c.Kind).ThenBy(c => c.SortOrder)
            .SelectMany(main => categories.Where(c => c.ParentId == main.Id).OrderBy(c => c.SortOrder).Prepend(main));
}

// SortOrder、ArchivedAt 是選用參數：P3 的 v1 備份沒有這兩個欄位，BackupJson 又要求必填的建構子參數（P4 J plan D8）。
public sealed record AccountDto(Guid Id, string Name, AccountType Type, decimal OpeningBalance, bool CountsAsAvailableCash,
    int SortOrder = 0, DateTimeOffset? ArchivedAt = null)
{
    public static AccountDto From(Account a) =>
        new(a.Id.Value, a.Name, a.Type, a.OpeningBalance, a.CountsAsAvailableCash, a.SortOrder, a.ArchivedAt);
}
// PlanningFundDto、CategoryDto 同樣在最後加上 int SortOrder = 0, DateTimeOffset? ArchivedAt = null
```

`GetLedgerSummary.cs` 第 50–51 行的 `book.Accounts`、`book.PlanningFunds` 前面加上 `.OrderBy(x => x.SortOrder)`。

- [ ] **Step 4**：單檔 Expected 2 passed。**Step 5**：`dotnet test` Expected 404。這裡要特別注意 `RestoreBackupTests`：還原時是照 DTO 順序呼叫 `Add*`，所以 SortOrder 會重建成一樣的值（D5），既有的 round-trip 測試應該維持全綠。
- [ ] **Step 6：Commit**：`feat(api): 帳本設定與總覽依 SortOrder 輸出，DTO 帶出排序與封存時間`。

---

## Task J8：PUT 修改帳戶、財務規劃帳戶、分類

**Files:** Create `src/SixJars.Application/Books/UpdateSettings.cs`／Modify `src/SixJars.Api/Endpoints/BooksEndpoints.cs`、`SettingsMaintenanceEndpointsTests.cs`

body 的形狀：帳戶是 `{ name, countsAsAvailableCash }`，財務規劃帳戶是 `{ name }`，分類是 `{ name, nature? }`。`nature` 是 null 時表示不修改性質；這個欄位只對支出主分類有意義，其他分類帶了不同的值，會由 Domain 拒絕（422）。改成浮動之前要查詢預定支出，依 D3、D6 包含已刪除的資料，而且要涵蓋這個主分類的所有子分類。

- [ ] **Step 1：寫失敗測試**

```csharp
    [Fact]
    public async Task Rename_account_and_change_cash_flag()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var cash = book.FindAccount("現金")!;

        var response = await client.PutAsJsonAsync(Url(book, $"/accounts/{cash.Id.Value}"),
            new { name = "零用金", countsAsAvailableCash = false }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var dto = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        dto.Accounts.Should().ContainSingle(a => a.Id == cash.Id.Value && a.Name == "零用金" && !a.CountsAsAvailableCash);
    }

    [Fact]
    public async Task Rename_to_existing_name_is_422_and_blank_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var cash = book.FindAccount("現金")!.Id.Value;

        var duplicate = await client.PutAsJsonAsync(Url(book, $"/accounts/{cash}"),
            new { name = "國泰世華銀行", countsAsAvailableCash = true }, ApiJson.Options, Ct);
        var blank = await client.PutAsJsonAsync(Url(book, $"/planning-funds/{book.PlanningFunds[0].Id.Value}"),
            new { name = " " }, ApiJson.Options, Ct);

        duplicate.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(duplicate)).GetProperty("code").GetString().Should().Be("rule");
        blank.StatusCode.Should().Be(HttpStatusCode.BadRequest);   // 空白名稱由 validator 擋下
    }

    [Fact]
    public async Task Change_nature_of_expense_main_category()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var food = book.FindCategory("主食")!;

        var response = await client.PutAsJsonAsync(Url(book, $"/categories/{food.Id.Value}"),
            new { name = "飲食", nature = "Special" }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var dto = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        dto.Categories.Where(c => c.Id == food.Id.Value || c.ParentId == food.Id.Value)
            .Should().OnlyContain(c => c.Nature == ExpenseNature.Special).And.Contain(c => c.Name == "飲食");
    }

    [Fact]
    public async Task Category_with_deleted_planned_expense_cannot_become_floating()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var planned = await PlannedExpensesEndpointsTests.CreateAsync(client, book,
            PlannedExpensesEndpointsTests.InsuranceInput(book, 202602, -1200m, "保險"));
        (await client.DeleteAsync(Url(book, $"/planned-expenses/{planned.Id}?version={planned.Version}"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var fixedMain = book.FindCategory("固定支出")!;

        var response = await client.PutAsJsonAsync(Url(book, $"/categories/{fixedMain.Id.Value}"),
            new { name = "固定支出", nature = "Floating" }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("in-use");
    }

    [Fact]
    public async Task Update_writes_audit_entry_with_before_and_after()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var fund = book.PlanningFunds[0];

        await client.PutAsJsonAsync(Url(book, $"/planning-funds/{fund.Id.Value}"), new { name = "自由基金" }, ApiJson.Options, Ct);

        var history = await client.GetFromJsonAsync<JsonElement>(Url(book, $"/audit?entityType=PlanningFund&entityId={fund.Id.Value}"), ApiJson.Options, Ct);
        history.ToString().Should().Contain("自由基金").And.Contain("財務自由帳戶");
    }
```

兩個要先查證的地方：預定支出的 DELETE 路由與 `PlannedExpenseDto.Version` 是否存在，以 `PlannedExpensesEndpoints.cs` 為準；稽核查詢的路由與參數名稱，以 `AuditEndpoints.cs` 為準。如果稽核查詢不支援依實體過濾，就改成直接查詢 `ISixJarsDbContext.AuditEntries`，寫法比照 J7 的 scope。

- [ ] **Step 2：跑測試確認失敗**：路由還不存在，PUT 會回 **405 Method Not Allowed**，因為同一個路徑已經有 POST。請確認看到的是 405，而不是 404（404 表示 URL 拼錯了）。
- [ ] **Step 3：最小實作**

```csharp
namespace SixJars.Application.Books;

public sealed record UpdateAccount(Guid BookId, Guid AccountId, string Name, bool CountsAsAvailableCash) : IRequest, IBookScoped;

public sealed record UpdatePlanningFund(Guid BookId, Guid PlanningFundId, string Name) : IRequest, IBookScoped;

/// <summary><see cref="Nature"/> 為 null 時不修改性質；只有支出主分類可以修改（回溯生效，spec Q4）。</summary>
public sealed record UpdateCategory(Guid BookId, Guid CategoryId, string Name, ExpenseNature? Nature) : IRequest, IBookScoped;

internal sealed class UpdateAccountValidator : AbstractValidator<UpdateAccount>
{
    public UpdateAccountValidator() => RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
}
// UpdatePlanningFundValidator 同上；UpdateCategoryValidator 另加 RuleFor(c => c.Nature).IsInEnum()

internal sealed class UpdateAccountHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<UpdateAccount>
{
    public async Task Handle(UpdateAccount request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        var id = new AccountId(request.AccountId);
        var before = AccountDto.From(book.GetAccount(id));
        book.RenameAccount(id, request.Name);
        book.SetCountsAsAvailableCash(id, request.CountsAsAvailableCash);
        audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.Account, id.Value, before, AccountDto.From(book.GetAccount(id)));
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class UpdateCategoryHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<UpdateCategory>
{
    public async Task Handle(UpdateCategory request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        var id = new CategoryId(request.CategoryId);
        var category = book.GetCategory(id);
        var before = CategoryDto.From(category);
        book.RenameCategory(id, request.Name);
        if (request.Nature is { } nature && nature != category.Nature)
        {
            var hasPlannedExpenses = nature == ExpenseNature.Floating
                && await db.HasPlannedExpensesAsync(book, id, cancellationToken);
            book.ChangeExpenseNature(id, nature, hasPlannedExpenses);
        }

        audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.Category, id.Value, before, CategoryDto.From(category));
        await db.SaveChangesAsync(cancellationToken);
    }
}
```

`HasPlannedExpensesAsync` 放在 J11 會擴充的 `SettingReferences.cs`，這個 Task 先建立這個檔案：

```csharp
namespace SixJars.Application.Books;

/// <summary>
/// 設定項目是否被使用。已軟刪除的交易與預定支出也算（P4 J plan D3）：
/// RestoreBackup 會以 Domain 重建它們，設定被刪除或性質不符時，備份就無法還原。
/// </summary>
internal static class SettingReferences
{
    /// <summary>主分類本身或其子分類，是否有任何預定支出（含已刪除）。</summary>
    public static Task<bool> HasPlannedExpensesAsync(this ISixJarsDbContext db, Book book, CategoryId mainId, CancellationToken cancellationToken)
    {
        var ids = book.Categories.Where(c => c.Id == mainId || c.ParentId == mainId).Select(c => c.Id).ToList();
        return db.PlannedExpenses.IgnoreQueryFilters().AnyAsync(p => p.BookId == book.Id && ids.Contains(p.CategoryId), cancellationToken);
    }
}
```

`BooksEndpoints.cs`：

```csharp
        // 被修改的項目 Id 一律以路由為準。
        book.MapPut("/accounts/{id:guid}", (Guid bookId, Guid id, UpdateAccount command, ISender sender, CancellationToken ct) =>
            NoContent(sender.Send(command with { BookId = bookId, AccountId = id }, ct)));
        book.MapPut("/planning-funds/{id:guid}", (Guid bookId, Guid id, UpdatePlanningFund command, ISender sender, CancellationToken ct) =>
            NoContent(sender.Send(command with { BookId = bookId, PlanningFundId = id }, ct)));
        book.MapPut("/categories/{id:guid}", (Guid bookId, Guid id, UpdateCategory command, ISender sender, CancellationToken ct) =>
            NoContent(sender.Send(command with { BookId = bookId, CategoryId = id }, ct)));

    private static async Task<IResult> NoContent(Task command)
    {
        await command;
        return Results.NoContent();
    }
```

`UpdatePlanningFundHandler` 的寫法和 `UpdateAccountHandler` 相同。

- [ ] **Step 4**：單檔 Expected 7 passed。**Step 5**：`dotnet test` Expected 409，其中 `BookScopeConventionTests`、`SecurityConventionTests` 也要全綠。如果 `ids.Contains(p.CategoryId)` 因為 value converter 而無法翻譯，就改成 `db.Books.Where(b => b.Id == book.Id).SelectMany(b => b.Categories).Where(c => c.Id == mainId || c.ParentId == mainId).Select(c => c.Id)` 子查詢，並在回寫時記錄。
- [ ] **Step 6：Commit**：`feat(api): 修改帳戶、財務規劃帳戶、分類（改名、計入可用現金、支出性質）`。

---

## Task J9：封存與解除封存 API

**Files:** Create `src/SixJars.Application/Books/SettingKind.cs`、`src/SixJars.Application/Books/ArchiveSetting.cs`／Modify `BooksEndpoints.cs`、`SettingsMaintenanceEndpointsTests.cs`

三種設定共用同一組 command，以 `SettingKind` 分派，所以只需要 2 個 command，而不是 6 個。每種設定的 Domain 方法本來就不同，handler 用 `switch` 呼叫對應的方法即可，不必為了共用再加抽象層。餘額用 `ILedgerSummaryQuery` 計算，截止點是 `AsOf(DateOnly.MaxValue)`，也就是包含未來日期的交易，因為封存的意思是「之後不會再使用」。

- [ ] **Step 1：寫失敗測試**

```csharp
    [Fact]
    public async Task Archive_and_unarchive_zero_balance_account()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var postOffice = await AddAccountAsync(client, book, "郵局");

        (await client.PostAsync(Url(book, $"/accounts/{postOffice}/archive"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var archived = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        archived.Accounts.Single(a => a.Id == postOffice).ArchivedAt.Should().NotBeNull();

        (await client.PostAsync(Url(book, $"/accounts/{postOffice}/unarchive"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var restored = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        restored.Accounts.Single(a => a.Id == postOffice).ArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task Account_with_balance_cannot_be_archived()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync()).PostAsync(
            Url(book, $"/accounts/{book.FindAccount("現金")!.Id.Value}/archive"), null, Ct);   // 期初 1000

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("non-zero-balance");
    }

    [Fact]
    public async Task Archive_main_category_and_planning_fund()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var food = book.FindCategory("主食")!.Id.Value;

        (await client.PostAsync(Url(book, $"/categories/{food}/archive"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var fund = await client.PostAsync(Url(book, $"/planning-funds/{book.PlanningFunds[0].Id.Value}/archive"), null, Ct);

        fund.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);   // 財務自由帳戶期初 10000
        var dto = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        dto.Categories.Single(c => c.Id == food).ArchivedAt.Should().NotBeNull();
        dto.Categories.Single(c => c.Name == "午餐").ArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task Archive_unknown_id_is_422()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync()).PostAsync(Url(book, $"/categories/{Guid.NewGuid()}/archive"), null, Ct);

        // 與既有的 GetCategory 一致：找不到設定項目是領域錯誤（422），不是 404
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    internal static async Task<Guid> AddAccountAsync(HttpClient client, Book book, string name)
    {
        var response = await client.PostAsJsonAsync(Url(book, "/accounts"), new { name, type = "Bank", openingBalance = 0m }, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }
```

- [ ] **Step 2：跑測試確認失敗**：路由不存在，回 404。這裡請確認 404 的 body **不是** `找不到帳本` 的 ProblemDetails；路由不存在時，body 是空的。
- [ ] **Step 3：最小實作**

```csharp
// SettingKind.cs
namespace SixJars.Application.Books;

/// <summary>可封存、排序、刪除的帳本設定種類；API 的路徑分別是 accounts、planning-funds、categories。</summary>
public enum SettingKind { Account, PlanningFund, Category }
```

```csharp
// ArchiveSetting.cs
public sealed record ArchiveSetting(Guid BookId, SettingKind Kind, Guid Id) : IRequest, IBookScoped;

public sealed record UnarchiveSetting(Guid BookId, SettingKind Kind, Guid Id) : IRequest, IBookScoped;

internal sealed class ArchiveSettingHandler(ISixJarsDbContext db, ILedgerSummaryQuery ledger, IAuditTrail audit, TimeProvider clock)
    : IRequestHandler<ArchiveSetting>
{
    // 包含未來日期的交易：封存表示之後不再使用，未來的交易也算在餘額內。
    private static readonly BalanceCutoff Everything = new BalanceCutoff.AsOf(DateOnly.MaxValue);

    public async Task Handle(ArchiveSetting request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        var at = clock.GetUtcNow();
        switch (request.Kind)
        {
            case SettingKind.Account:
                var account = book.GetAccount(new AccountId(request.Id));
                var before = AccountDto.From(account);
                var postings = await ledger.PostingTotalsAsync(book.Id, Everything, cancellationToken);
                book.ArchiveAccount(account.Id, LedgerBalances.Account(account, postings), at);
                audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.Account, request.Id, before, AccountDto.From(account));
                break;
            case SettingKind.PlanningFund:
                // 同上，改用 FundDeltaTotalsAsync 與 LedgerBalances.Fund
                break;
            default:
                var category = book.GetCategory(new CategoryId(request.Id));
                var categoryBefore = CategoryDto.From(category);
                book.ArchiveCategory(category.Id, at);
                audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.Category, request.Id, categoryBefore, CategoryDto.From(category));
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
// UnarchiveSettingHandler：同樣的 switch，呼叫 Unarchive*，不查餘額。
```

`BooksEndpoints.cs`：三種設定的 archive、unarchive，以及 J10、J11 要加的路由，都集中在一個 helper：

```csharp
        MapSettingActions(book, "accounts", SettingKind.Account);
        MapSettingActions(book, "planning-funds", SettingKind.PlanningFund);
        MapSettingActions(book, "categories", SettingKind.Category);

    private static void MapSettingActions(RouteGroupBuilder book, string path, SettingKind kind)
    {
        book.MapPost($"/{path}/{{id:guid}}/archive", (Guid bookId, Guid id, ISender sender, CancellationToken ct) =>
            NoContent(sender.Send(new ArchiveSetting(bookId, kind, id), ct)));
        book.MapPost($"/{path}/{{id:guid}}/unarchive", (Guid bookId, Guid id, ISender sender, CancellationToken ct) =>
            NoContent(sender.Send(new UnarchiveSetting(bookId, kind, id), ct)));
    }
```

`TimeProvider` 已經在 `Program.cs` 註冊成 singleton。Application 是否已經有 handler 直接注入 `TimeProvider`，請以 `AuditTrail.cs` 的寫法為準。

- [ ] **Step 4**：單檔 Expected 11 passed。**Step 5**：`dotnet test` Expected 413。
- [ ] **Step 6：Commit**：`feat(api): 封存與解除封存設定項目，帳戶與財務規劃帳戶需餘額為 0`。

---

## Task J10：重新排序 API

**Files:** Create `src/SixJars.Application/Books/ReorderSettings.cs`／Modify `BooksEndpoints.cs`、`SettingsMaintenanceEndpointsTests.cs`

body 是 `{ kind?, parentId?, ids }`，一次送出整組的新順序。路由 `/categories/order` 與 `/categories/{id:guid}` 不會衝突，因為後者有 guid 限制。稽核記錄只寫 SortOrder 有改變的項目（每筆一條 Update），不然每次拖曳都會寫出整組的記錄。

- [ ] **Step 1：寫失敗測試**

```csharp
    [Fact]
    public async Task Reorder_accounts()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var reversed = book.Accounts.Select(a => a.Id.Value).Reverse().ToArray();

        var response = await client.PutAsJsonAsync(Url(book, "/accounts/order"), new { ids = reversed }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var dto = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        dto.Accounts.Select(a => a.Id).Should().Equal(reversed);
    }

    [Fact]
    public async Task Reorder_sub_categories_of_a_parent()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var food = book.FindCategory("主食")!.Id.Value;
        await client.PostAsJsonAsync(Url(book, "/categories"), new { name = "晚餐", kind = "Expense", parentId = food }, ApiJson.Options, Ct);
        var subs = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!.Categories.Where(c => c.ParentId == food).ToList();

        var response = await client.PutAsJsonAsync(Url(book, "/categories/order"),
            new { parentId = food, ids = subs.Select(c => c.Id).Reverse() }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var dto = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        dto.Categories.Where(c => c.ParentId == food).Select(c => c.Name).Should().Equal("晚餐", "午餐");
    }

    [Fact]
    public async Task Stale_order_is_422()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync()).PutAsJsonAsync(Url(book, "/accounts/order"),
            new { ids = book.Accounts.Skip(1).Select(a => a.Id.Value) }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Main_category_order_requires_kind()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync()).PutAsJsonAsync(Url(book, "/categories/order"),
            new { ids = Array.Empty<Guid>() }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).GetProperty("errors").TryGetProperty("kind", out _).Should().BeTrue();
    }
```

- [ ] **Step 2：跑測試確認失敗**：PUT `/accounts/order` 回 404。`/accounts/{id:guid}` 只吃 guid，所以 `order` 不會被這個路由接住。
- [ ] **Step 3：最小實作**

```csharp
/// <summary>一次送出整組的新順序。分類：<see cref="ParentId"/> 有值時排序子分類，否則以 <see cref="CategoryKind"/> 排序主分類。</summary>
public sealed record ReorderSettings(Guid BookId, SettingKind Kind, CategoryKind? CategoryKind, Guid? ParentId, IReadOnlyList<Guid> Ids)
    : IRequest, IBookScoped;

internal sealed class ReorderSettingsValidator : AbstractValidator<ReorderSettings>
{
    public ReorderSettingsValidator()
    {
        RuleFor(c => c.Ids).NotNull();
        RuleFor(c => c.CategoryKind).NotNull().IsInEnum()
            .When(c => c.Kind == SettingKind.Category && c.ParentId is null)
            .WithMessage("排序主分類時必須指定種類。");
    }
}

internal sealed class ReorderSettingsHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<ReorderSettings>
{
    public async Task Handle(ReorderSettings request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        var before = BookDto.From(book);
        switch (request.Kind)
        {
            case SettingKind.Account:
                book.ReorderAccounts([.. request.Ids.Select(id => new AccountId(id))]);
                break;
            case SettingKind.PlanningFund:
                book.ReorderPlanningFunds([.. request.Ids.Select(id => new PlanningFundId(id))]);
                break;
            default:
                book.ReorderCategories(request.CategoryKind ?? CategoryKind.Expense,
                    request.ParentId is { } parentId ? new CategoryId(parentId) : null,
                    [.. request.Ids.Select(id => new CategoryId(id))]);
                break;
        }

        RecordMoved(request.BookId, AuditEntityTypes.Account, before.Accounts, BookDto.From(book).Accounts, a => a.Id);
        // PlanningFunds、Categories 同樣比較 before／after，只記錄 SortOrder 不同的項目
        await db.SaveChangesAsync(cancellationToken);
    }

    private void RecordMoved<T>(Guid bookId, string entityType, IReadOnlyList<T> before, IReadOnlyList<T> after, Func<T, Guid> idOf)
        where T : notnull
    {
        var beforeById = before.ToDictionary(idOf);
        foreach (var item in after.Where(item => !beforeById[idOf(item)].Equals(item)))
        {
            audit.Record(bookId, AuditAction.Update, entityType, idOf(item), beforeById[idOf(item)], item);
        }
    }
}
```

`BookDto` 的子 DTO 都是 record，`Equals` 是值相等，所以只要 SortOrder 有變就會判斷為不同。端點加在 `MapSettingActions` 裡：

```csharp
        book.MapPut($"/{path}/order", (Guid bookId, ReorderBody body, ISender sender, CancellationToken ct) =>
            NoContent(sender.Send(new ReorderSettings(bookId, kind, body.Kind, body.ParentId, body.Ids ?? []), ct)));

/// <summary>重新排序的 body；<see cref="Kind"/>、<see cref="ParentId"/> 只用於分類。</summary>
internal sealed record ReorderBody(CategoryKind? Kind, Guid? ParentId, IReadOnlyList<Guid>? Ids);
```

- [ ] **Step 4**：單檔 Expected 15 passed。**Step 5**：`dotnet test` Expected 417。
- [ ] **Step 6：Commit**：`feat(api): 設定項目重新排序（一次送出整組）`。

---

## Task J11：刪除 API 與參照檢查

**Files:** Create `src/SixJars.Application/Books/RemoveSetting.cs`／Modify `src/SixJars.Application/Books/SettingReferences.cs`、`BooksEndpoints.cs`、`SettingsMaintenanceEndpointsTests.cs`

參照的範圍：帳戶會出現在交易的 `AccountId`、`CounterAccountId`、分錄的 `AccountId`，以及預定支出的 `AccountId`。分類會出現在交易與預定支出的 `CategoryId`。財務規劃帳戶只會出現在交易的 `PlanningFundId`。依 D3，以上都用 `IgnoreQueryFilters()` 把已軟刪除的資料包含進來。K 段的週期項目與 L 段的預算，屆時再擴充這個檔案。

- [ ] **Step 1：寫失敗測試**

```csharp
    [Fact]
    public async Task Remove_unused_account()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var postOffice = await AddAccountAsync(client, book, "郵局");

        (await client.DeleteAsync(Url(book, $"/accounts/{postOffice}"), Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var dto = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        dto.Accounts.Should().NotContain(a => a.Id == postOffice);
        dto.Accounts.Select(a => a.SortOrder).Should().Equal(0, 1, 2, 3, 4);
    }

    [Fact]
    public async Task Category_used_only_by_deleted_transaction_cannot_be_removed()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var lunch = book.FindCategory("主食", "午餐")!;
        var created = await client.PostAsJsonAsync(Url(book, "/transactions"), new
        {
            kind = "Expense", date = "2026-01-05", amount = -120m, note = "午餐",
            accountId = book.FindAccount("現金")!.Id.Value, categoryId = lunch.Id.Value,
        }, ApiJson.Options, Ct);
        var transaction = (await created.Content.ReadFromJsonAsync<TransactionDto>(ApiJson.Options, Ct))!;
        (await client.DeleteAsync(Url(book, $"/transactions/{transaction.Id}?version={transaction.Version}"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await client.DeleteAsync(Url(book, $"/categories/{lunch.Id.Value}"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("in-use");
    }

    [Fact]
    public async Task Account_used_by_planned_expense_cannot_be_removed()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await PlannedExpensesEndpointsTests.CreateAsync(client, book, PlannedExpensesEndpointsTests.InsuranceInput(book, 202602, -1200m, "保險"));

        var response = await client.DeleteAsync(Url(book, $"/accounts/{book.FindAccount("國泰世華銀行")!.Id.Value}"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("in-use");
    }

    [Fact]
    public async Task Main_category_with_subs_cannot_be_removed_but_unused_fund_can()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();

        var main = await client.DeleteAsync(Url(book, $"/categories/{book.FindCategory("主食")!.Id.Value}"), Ct);
        var fund = await client.DeleteAsync(Url(book, $"/planning-funds/{book.PlanningFunds[0].Id.Value}"), Ct);

        main.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        fund.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Remove_writes_delete_audit_entry()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var postOffice = await AddAccountAsync(client, book, "郵局");

        await client.DeleteAsync(Url(book, $"/accounts/{postOffice}"), Ct);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ISixJarsDbContext>();
        (await db.AuditEntries.CountAsync(e => e.EntityId == postOffice && e.Action == AuditAction.Delete, Ct)).Should().Be(1);
    }
```

測試檔要加上 `using SixJars.Application.Auditing;`、`using SixJars.Application.Transactions;`。`AuditEntry` 的屬性名稱（`EntityId`、`Action`）以 `AuditEntry.cs` 為準。

- [ ] **Step 2：跑測試確認失敗**：DELETE 回 405，因為同一個路徑已有 PUT。
- [ ] **Step 3：最小實作**

`SettingReferences.cs` 加上：

```csharp
    public static async Task<bool> IsAccountReferencedAsync(this ISixJarsDbContext db, BookId bookId, AccountId id, CancellationToken cancellationToken) =>
        await db.Transactions.IgnoreQueryFilters().AnyAsync(t => t.BookId == bookId
            && (t.AccountId == id || t.CounterAccountId == id || t.Postings.Any(p => p.AccountId == id)), cancellationToken)
        || await db.PlannedExpenses.IgnoreQueryFilters().AnyAsync(p => p.BookId == bookId && p.AccountId == id, cancellationToken);

    public static async Task<bool> IsCategoryReferencedAsync(this ISixJarsDbContext db, BookId bookId, CategoryId id, CancellationToken cancellationToken) =>
        await db.Transactions.IgnoreQueryFilters().AnyAsync(t => t.BookId == bookId && t.CategoryId == id, cancellationToken)
        || await db.PlannedExpenses.IgnoreQueryFilters().AnyAsync(p => p.BookId == bookId && p.CategoryId == id, cancellationToken);

    public static Task<bool> IsPlanningFundReferencedAsync(this ISixJarsDbContext db, BookId bookId, PlanningFundId id, CancellationToken cancellationToken) =>
        db.Transactions.IgnoreQueryFilters().AnyAsync(t => t.BookId == bookId && t.PlanningFundId == id, cancellationToken);
```

`RemoveSetting.cs`：

```csharp
public sealed record RemoveSetting(Guid BookId, SettingKind Kind, Guid Id) : IRequest, IBookScoped;

internal sealed class RemoveSettingHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<RemoveSetting>
{
    public async Task Handle(RemoveSetting request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        switch (request.Kind)
        {
            case SettingKind.Account:
                var account = AccountDto.From(book.GetAccount(new AccountId(request.Id)));
                book.RemoveAccount(new AccountId(request.Id), await db.IsAccountReferencedAsync(book.Id, new AccountId(request.Id), cancellationToken));
                audit.Record<AccountDto>(request.BookId, AuditAction.Delete, AuditEntityTypes.Account, request.Id, account, null);
                break;
            // PlanningFund、Category 同理
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
```

注意分類的檢查順序。`Book.RemoveCategory` 會先檢查有沒有子分類，再檢查是否被參照；但參照查詢是在呼叫 Domain **之前**完成的，所以就算有子分類，也會多查一次資料庫。這是可以接受的成本。

`MapSettingActions` 加上：

```csharp
        book.MapDelete($"/{path}/{{id:guid}}", (Guid bookId, Guid id, ISender sender, CancellationToken ct) =>
            NoContent(sender.Send(new RemoveSetting(bookId, kind, id), ct)));
```

- [ ] **Step 4**：單檔 Expected 20 passed。**Step 5**：`dotnet test` Expected 422。
- [ ] **Step 6：Commit**：`feat(api): 刪除未被使用的設定項目（參照含已軟刪除的資料）`。

---

## Task J12：備份格式 v2：保存排序與封存

**Files:** Modify `src/SixJars.Application/Backup/{BackupDocument,RestoreBackup}.cs`、`tests/SixJars.Api.Tests/BackupExportTests.cs`、`tests/SixJars.Infrastructure.Tests/Backup/RestoreBackupTests.cs`

J7 之後 SortOrder 已經可以靠 D5 重建，但封存時間還沒有還原。還原時，`ArchiveAccount` 需要傳入餘額；這裡傳 0，因為封存的帳戶在備份當下已經通過餘額檢查，而且交易會原樣還原，所以餘額不會改變。v1 的備份沒有 SortOrder，比對時要忽略這個欄位（D8）。

- [ ] **Step 1：寫失敗測試**
  1. `BackupExportTests.cs:98` 改成 `BackupDocument.CurrentFormatVersion.Should().Be(2);`
  2. `RestoreBackupTests.Scenario.BuildAsync` 在 `FillAsync(book)` 之後加上：

```csharp
            // (h) 排序與封存：新增一個餘額為 0 的帳戶並封存，再把帳戶順序整個倒過來。
            var added = await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/accounts",
                new { name = "郵局", type = "Bank", openingBalance = 0m }, ApiJson.Options, Ct);
            var postOffice = (await added.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
            (await client.PostAsync($"/api/books/{book.Id.Value}/accounts/{postOffice}/archive", null, Ct)).EnsureSuccessStatusCode();
            var current = (await client.GetFromJsonAsync<BookDto>($"/api/books/{book.Id.Value}", ApiJson.Options, Ct))!;
            (await client.PutAsJsonAsync($"/api/books/{book.Id.Value}/accounts/order",
                new { ids = current.Accounts.Select(a => a.Id).Reverse() }, ApiJson.Options, Ct)).EnsureSuccessStatusCode();
```

  3. `ShouldCoverTrickyStates` 加上：

```csharp
            // (h) 封存的帳戶，且帳戶順序與建立順序不同。
            a.Book.Accounts.Should().Contain(x => x.Name == "郵局" && x.ArchivedAt != null);
            a.Book.Accounts.Select(x => x.Id).Should().NotBeInAscendingOrder();
```

  4. 新增 v1 相容測試：

```csharp
    [Fact]
    public async Task Version_1_backup_without_sort_order_is_restored()
    {
        var source = await Scenario.BuildAsync(postgres);
        await using var _ = source;
        var a = await source.ExportAsync();
        // 模擬 P3 的 v1 備份：沒有 sortOrder、archivedAt 欄位
        var json = JsonNode.Parse(BackupJson.SerializeToUtf8Bytes(a))!;
        json["formatVersion"] = 1;
        foreach (var list in new[] { "accounts", "planningFunds", "categories" })
        {
            foreach (var item in json["book"]![list]!.AsArray())
            {
                item!.AsObject().Remove("sortOrder");
                item.AsObject().Remove("archivedAt");
            }
        }

        var v1 = json.Deserialize<BackupDocument>(BackupJson.Options)!;
        var target = await postgres.CreateConnectionStringAsync(Ct);

        (await RestoreAsync(target, v1)).Should().Be(a.Book.Id);
    }
```

`using System.Text.Json.Nodes;`。JSON 屬性名稱是 camelCase（`JsonSerializerDefaults.Web`）。如果 `BackupDocument.Book` 的實際屬性名稱不是 `book`，以序列化後的結果為準。

- [ ] **Step 2：跑測試確認失敗**，預期的三種結果：
  - `BackupExportTests`：斷言 `2` 但實際是 `1`。
  - `Export_restore_export_round_trips`：還原時擲出「帳本 … 的設定經 Domain 重建後與備份不一致」，原因是 `郵局` 沒有被重新封存。請確認是這個訊息，而不是 (h) 的 HTTP 呼叫失敗。
  - `Version_1_backup_without_sort_order_is_restored`：**這時可能已經通過**，因為版本號仍然是 1，而且 DTO 的新欄位是選用參數。這不代表測試無效，它保護的是 Step 3 升版之後的行為。Step 3 完成後要再跑一次，確認它仍然通過；也可以暫時把 Step 3 的 v1 分支（`WithoutSortOrder`）拿掉，確認它會失敗。
- [ ] **Step 3：最小實作**
  - `BackupDocument.CurrentFormatVersion = 2`，`<summary>` 補上一句：「v2 起設定項目帶有 SortOrder 與 ArchivedAt；還原也接受 v1」。
  - Validator：`RuleFor(c => c.Backup.FormatVersion).InclusiveBetween(1, BackupDocument.CurrentFormatVersion).WithMessage($"只支援格式版本 1 到 {BackupDocument.CurrentFormatVersion} 的備份。");`
  - `RestoreBook(BookDto dto, int formatVersion)`：在 `Add*` 之後、一致性檢查之前，加上：

```csharp
        // 封存的帳戶在備份當下已通過餘額檢查，交易也會原樣還原，所以這裡以 0 傳入。
        foreach (var account in dto.Accounts.Where(a => a.ArchivedAt is not null))
        {
            book.ArchiveAccount(new AccountId(account.Id), balance: 0m, account.ArchivedAt!.Value);
        }
        // PlanningFunds 同理；Categories 用 ArchiveCategory

        // v1 沒有 SortOrder（全部是 0），重建出來的是依清單順序的編號，比對時忽略。
        var expected = formatVersion == 1 ? WithoutSortOrder(dto) : dto;
        var restored = formatVersion == 1 ? WithoutSortOrder(BookDto.From(book)) : BookDto.From(book);
```

```csharp
    private static BookDto WithoutSortOrder(BookDto book) => book with
    {
        Accounts = [.. book.Accounts.Select(a => a with { SortOrder = 0 })],
        PlanningFunds = [.. book.PlanningFunds.Select(f => f with { SortOrder = 0 })],
        Categories = [.. book.Categories.Select(c => c with { SortOrder = 0 })],
    };
```

  原本的比對要改用 `expected` 與 `restored`。v1 的帳戶比對是 `SequenceEqual`，前提是 `BookDto.From` 的輸出順序要和 v1 的清單順序一致。v1 的清單順序是 P3 時從資料庫讀出來的任意順序；還原時照這個順序 `Add`，SortOrder 就會依這個順序遞增，`From` 又依 SortOrder 輸出，所以順序一致。
- [ ] **Step 4**：`dotnet test --filter "FullyQualifiedName~RestoreBackupTests|FullyQualifiedName~BackupExportTests"`，全綠。
- [ ] **Step 5**：`dotnet test` Expected 423。
- [ ] **Step 6：Commit**：`feat(backup): 備份格式 v2 保存設定的排序與封存，還原仍接受 v1`。

### ═══ 後端 checkpoint ═══

- [ ] `dotnet build`：0 warning。`dotnet test`：約 423 個，失敗 0，其中 `SixJars.AcceptanceTests`（`MonthFigureComparison`）**全部維持綠色**，這是 spec §9 對 J 段的驗收要求。
- [ ] `git log --oneline master..` 應該新增 12 個 commit（J1–J12）。
- [ ] 用 `docs(plans):` commit 把偏差回寫到文末的「執行結果與偏差」，然後**停下來讓使用者檢視**，再開始前端。

---

## Task J13：前端 DTO、設定 API、CurrentBook.reload

**Files:** Modify `web/src/app/core/api/{dto,book-api}.ts`、`book-api.spec.ts`、`core/book/current-book.ts`、`current-book.spec.ts`，以及「修改」表列出的 fixture

DTO 的新欄位設成**必填**，讓 TypeScript 編譯器找出所有需要補欄位的 fixture，不要讓測試資料默默缺少欄位。`reload()` 要清掉快取，而且必須沿用 `load()` 的競態保護（`cached.promise === promise`），否則在設定頁連續操作時，較舊的回應可能覆蓋較新的回應。

- [ ] **Step 1：寫失敗測試**

`book-api.spec.ts` 加上（沿用該檔既有的 TestBed 設定；如果沒有，就照 `current-book.spec.ts` 的方式設定）：

```ts
describe('BookApi settings', () => {
  let api: BookApi;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(BookApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpTesting.verify());

  it('update_account_puts_name_and_cash_flag', async () => {
    const done = firstValueFrom(api.updateAccount('b1', 'a1', { name: '郵局', countsAsAvailableCash: false }));
    const request = httpTesting.expectOne({ method: 'PUT', url: '/api/books/b1/accounts/a1' });
    expect(request.request.body).toEqual({ name: '郵局', countsAsAvailableCash: false });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await done;
  });

  it('archive_unarchive_and_remove_use_kind_path', async () => {
    const archive = firstValueFrom(api.archive('b1', 'categories', 'c1'));
    httpTesting.expectOne({ method: 'POST', url: '/api/books/b1/categories/c1/archive' }).flush(null, { status: 204, statusText: '' });
    await archive;
    const unarchive = firstValueFrom(api.unarchive('b1', 'planning-funds', 'f1'));
    httpTesting.expectOne({ method: 'POST', url: '/api/books/b1/planning-funds/f1/unarchive' }).flush(null, { status: 204, statusText: '' });
    await unarchive;
    const remove = firstValueFrom(api.remove('b1', 'accounts', 'a1'));
    httpTesting.expectOne({ method: 'DELETE', url: '/api/books/b1/accounts/a1' }).flush(null, { status: 204, statusText: '' });
    await remove;
  });

  it('reorder_sends_whole_group', async () => {
    const done = firstValueFrom(api.reorder('b1', 'categories', { kind: 'Expense', parentId: null, ids: ['c2', 'c1'] }));
    const request = httpTesting.expectOne({ method: 'PUT', url: '/api/books/b1/categories/order' });
    expect(request.request.body).toEqual({ kind: 'Expense', parentId: null, ids: ['c2', 'c1'] });
    request.flush(null, { status: 204, statusText: '' });
    await done;
  });

  it('add_and_lock_date', async () => {
    const add = firstValueFrom(api.add('b1', 'planning-funds', { name: '旅遊基金', openingBalance: 0 }));
    httpTesting.expectOne({ method: 'POST', url: '/api/books/b1/planning-funds' }).flush({ id: 'f9' }, { status: 201, statusText: '' });
    expect(await add).toEqual({ id: 'f9' });
    const lock = firstValueFrom(api.setLockDate('b1', null));
    const request = httpTesting.expectOne({ method: 'PUT', url: '/api/books/b1/lock-date' });
    expect(request.request.body).toEqual({ lockDate: null });
    request.flush(null, { status: 204, statusText: '' });
    await lock;
  });
});
```

`current-book.spec.ts` 加上：

```ts
  it('current_book_reload_refetches_same_book', async () => {
    const first = current.load('b1');
    httpTesting.expectOne('/api/books/b1').flush(book('b1'));
    await first;

    const reloaded = current.reload('b1');
    httpTesting.expectOne('/api/books/b1').flush({ ...book('b1'), name: '改名後' });
    expect((await reloaded).name).toBe('改名後');
    expect(current.book()?.name).toBe('改名後');
  });
```

- [ ] **Step 2：跑測試確認失敗**：`npx ng test --watch=false --include src/app/core/api/book-api.spec.ts`，看到 TS 錯誤 `Property 'updateAccount' does not exist`。
- [ ] **Step 3：最小實作**

`dto.ts`：

```ts
export interface AccountDto {
  id: string; name: string; type: AccountType; openingBalance: number; countsAsAvailableCash: boolean;
  sortOrder: number; archivedAt: string | null;
}
export interface PlanningFundDto { id: string; name: string; openingBalance: number; sortOrder: number; archivedAt: string | null }
export interface CategoryDto {
  id: string; name: string; kind: CategoryKind; nature: ExpenseNature | null; parentId: string | null;
  sortOrder: number; archivedAt: string | null;
}

// 設定的路徑片段，與後端 BooksEndpoints 的 MapSettingActions 一致
export type SettingPath = 'accounts' | 'planning-funds' | 'categories';
export interface ReorderBody { kind?: CategoryKind | null; parentId?: string | null; ids: string[] }
export interface AddAccountBody { name: string; type: AccountType; openingBalance: number; countsAsAvailableCash: boolean }
export interface AddPlanningFundBody { name: string; openingBalance: number }
export interface AddCategoryBody { name: string; kind: CategoryKind; nature: ExpenseNature | null; parentId: string | null }
```

`book-api.ts`：

```ts
  add(bookId: string, path: 'accounts', body: AddAccountBody): Observable<{ id: string }>;
  add(bookId: string, path: 'planning-funds', body: AddPlanningFundBody): Observable<{ id: string }>;
  add(bookId: string, path: 'categories', body: AddCategoryBody): Observable<{ id: string }>;
  add(bookId: string, path: SettingPath, body: object): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`/api/books/${bookId}/${path}`, body);
  }

  updateAccount(bookId: string, id: string, body: { name: string; countsAsAvailableCash: boolean }): Observable<void> {
    return this.http.put<void>(`/api/books/${bookId}/accounts/${id}`, body);
  }
  updatePlanningFund(bookId: string, id: string, body: { name: string }): Observable<void> { /* 同理 */ }
  updateCategory(bookId: string, id: string, body: { name: string; nature: ExpenseNature | null }): Observable<void> { /* 同理 */ }

  archive(bookId: string, path: SettingPath, id: string): Observable<void> {
    return this.http.post<void>(`/api/books/${bookId}/${path}/${id}/archive`, null);
  }
  unarchive(…) / remove(…) / reorder(bookId, path, body: ReorderBody) / setLockDate(bookId, lockDate: string | null)
```

`current-book.ts`：

```ts
  // 設定變更後呼叫：丟掉快取重新取得，讓記帳頁的選項同步更新
  reload(bookId: string): Promise<BookDto> {
    this.cached = undefined;
    return this.load(bookId);
  }
```

fixture：在每一筆帳戶、財務規劃帳戶、分類加上 `sortOrder`（依陣列位置在組內遞增）與 `archivedAt: null`。`book-fixture.ts` 的分類順序已經是前序，所以 `cat-food` 0、`cat-lunch` 0、`cat-salary` 0、`cat-bonus` 0。

- [ ] **Step 4**：兩個單檔，Expected：`book-api.spec.ts`、`current-book.spec.ts` 全綠。
- [ ] **Step 5**：`npx ng test --watch=false` Expected 199 passed；另外跑 `npx playwright test` 確認 e2e fixture 能編譯（4 passed）。
- [ ] **Step 6：Commit**：明確列出 `dto.ts`、`book-api.ts`、`book-api.spec.ts`、`current-book.ts`、`current-book.spec.ts`，以及上表的每一個 fixture 檔，訊息為 `feat(web): 設定 API 與 CurrentBook.reload，DTO 帶出排序與封存`。

---

## Task J14：記帳選項排除已封存，但保留編輯中交易用到的項目

**Files:** Modify `web/src/app/features/transactions/{transaction-rules.ts,transaction-rules.spec.ts,transaction-form.ts,transaction-form.html,transaction-form.spec.ts}`

這個 Task 最容易出錯的地方：`applyKindRule()` 會把「不在選項裡的值」清空。如果只是把已封存的項目濾掉，修改一筆舊交易時，原本的帳戶就會被清掉，使用者也不會發現（D7）。所以兩個選項函式都要接受一個 `keep` 集合，內容是編輯中交易的 `accountId`、`counterAccountId`、`categoryId`、`planningFundId`。子分類是否可選，要同時看自己和主分類（spec §3.1）。財務規劃帳戶的選項，目前是 template 直接讀 `book().planningFunds`，也要改成經過同一個篩選。

- [ ] **Step 1：寫失敗測試**

`transaction-rules.spec.ts`：

```ts
describe('archived settings', () => {
  const archived = '2026-05-01T00:00:00+00:00';
  const accounts: AccountDto[] = [
    { id: 'a1', name: '現金', type: 'Cash', openingBalance: 0, countsAsAvailableCash: true, sortOrder: 0, archivedAt: null },
    { id: 'a2', name: '舊現金', type: 'Cash', openingBalance: 0, countsAsAvailableCash: true, sortOrder: 1, archivedAt: archived },
  ];
  const categories: CategoryDto[] = [
    { id: 'm1', name: '飲食', kind: 'Expense', nature: 'Floating', parentId: null, sortOrder: 0, archivedAt: archived },
    { id: 's1', name: '午餐', kind: 'Expense', nature: 'Floating', parentId: 'm1', sortOrder: 0, archivedAt: null },
    { id: 'm2', name: '交通', kind: 'Expense', nature: 'Floating', parentId: null, sortOrder: 1, archivedAt: null },
    { id: 's2', name: '舊車', kind: 'Expense', nature: 'Floating', parentId: 'm2', sortOrder: 0, archivedAt: archived },
  ];

  it('accounts_for_excludes_archived_unless_kept', () => {
    const slot = KIND_RULES.Expense.account;
    expect(accountsFor(accounts, slot).map(a => a.id)).toEqual(['a1']);
    expect(accountsFor(accounts, slot, new Set(['a2'])).map(a => a.id)).toEqual(['a1', 'a2']);
  });

  it('category_options_exclude_archived_self_or_parent_unless_kept', () => {
    expect(categoryOptions(categories, 'Expense').map(o => o.id)).toEqual(['m2']);
    expect(categoryOptions(categories, 'Expense', new Set(['s1'])).map(o => o.id)).toEqual(['s1', 'm2']);
  });

  it('selectable_funds_exclude_archived_unless_kept', () => {
    const funds = [
      { id: 'f1', name: '旅遊', openingBalance: 0, sortOrder: 0, archivedAt: null },
      { id: 'f2', name: '舊基金', openingBalance: 0, sortOrder: 1, archivedAt: archived },
    ];
    expect(selectable(funds).map(f => f.id)).toEqual(['f1']);
    expect(selectable(funds, new Set(['f2'])).map(f => f.id)).toEqual(['f1', 'f2']);
  });
});
```

（import 加上 `selectable`、`CategoryDto`。）

`transaction-form.spec.ts`：先把 `setup()` 改成 `setup(book: BookDto = BOOK)`，裡面的 `setInput('book', BOOK)` 改成用參數，然後加上：

```ts
  describe('archived settings', () => {
    const OLD_BANK = {
      id: 'acc-old', name: '舊銀行', type: 'Bank' as const, openingBalance: 0, countsAsAvailableCash: false,
      sortOrder: 5, archivedAt: '2026-05-01T00:00:00+00:00',
    };
    const WITH_ARCHIVED: BookDto = { ...BOOK, accounts: [...BOOK.accounts, OLD_BANK] };

    it('hides_archived_account_for_new_transaction', async () => {
      const form = await setup(WITH_ARCHIVED);
      expect(await form.selectOptions('accountId')).not.toContain('舊銀行');
    });

    it('keeps_archived_account_of_editing_transaction', async () => {
      const form = await setup(WITH_ARCHIVED);
      await form.edit({ ...EDITING, accountId: 'acc-old' });
      expect(await form.selectedText('accountId')).toBe('舊銀行');
      expect(await form.selectOptions('accountId')).toContain('舊銀行');
    });
  });
```

- [ ] **Step 2：跑測試確認失敗**：rules 測試會出現 TS 錯誤（`selectable` 不存在，`accountsFor` 只接受 2 個參數）。form 的第 1 個測試會因為選項裡有「舊銀行」而失敗，請確認失敗原因是這個，而不是 harness 找不到元素。
- [ ] **Step 3：最小實作**

```ts
// 封存的項目不出現在選項中；keep 是編輯中交易已經用到的 id，必須保留，否則 applyKindRule 會清掉原本的選擇
export function selectable<T extends { id: string; archivedAt: string | null }>(items: T[], keep: ReadonlySet<string> = NO_KEEP): T[] {
  return items.filter(item => item.archivedAt === null || keep.has(item.id));
}
const NO_KEEP: ReadonlySet<string> = new Set();

export function accountsFor(accounts: AccountDto[], slot: AccountSlot, keep: ReadonlySet<string> = NO_KEEP): AccountDto[] {
  return selectable(accounts, keep).filter(account => slot.types.includes(account.type));
}

export function categoryOptions(categories: CategoryDto[], kind: CategoryKind, keep: ReadonlySet<string> = NO_KEEP) {
  const byId = new Map(categories.map(category => [category.id, category]));
  const nameById = new Map(categories.map(category => [category.id, category.name]));
  // 子分類可選 = 自己與主分類都未封存（spec §3.1）
  const usable = (category: CategoryDto) => category.archivedAt === null
    && (category.parentId === null || byId.get(category.parentId)?.archivedAt === null);
  return categories
    .filter(category => category.kind === kind && (usable(category) || keep.has(category.id)))
    .map(category => ({ id: category.id, label: labelOf(category, nameById) }));
}

// 編輯中交易用到的設定 id
export function keepIdsOf(editing: TransactionDto | null): ReadonlySet<string> {
  if (editing === null) {
    return NO_KEEP;
  }
  return new Set([editing.accountId, editing.counterAccountId, editing.categoryId, editing.planningFundId]
    .filter((id): id is string => id !== null));
}
```

`NO_KEEP` 要宣告在 `selectable` 之前，避免 TDZ。`transaction-form.ts`：新增 `private readonly keep = computed(() => keepIdsOf(<元件保存編輯中交易的 signal>()))`。依 P3 回寫，這個 signal 是用 `linkedSignal` 保存的，名稱以實際程式為準。然後把第 146、149、152 行，以及 `applyKindRule()` 裡的 `accountsFor`、`categoryOptions` 呼叫都加上 `this.keep()`；再新增 `protected readonly fundChoices = computed(() => selectable(this.book().planningFunds, this.keep()))`，`transaction-form.html:133` 改成 `@for (fund of fundChoices(); …)`。`TransactionDto.planningFundId` 的欄位名稱以 `dto.ts` 為準。

**順序陷阱**：`applyKindRule()` 必須在 editing signal 已經更新之後才執行，否則 `keep()` 還是空集合。如果第 2 個 form 測試失敗，請先確認 `edit()` 的處理流程中，「把交易寫進表單」和「applyKindRule」的執行順序。

- [ ] **Step 4**：兩個單檔全綠。**Step 5**：`npx ng test --watch=false` Expected 204 passed。
- [ ] **Step 6：Commit**：`feat(web): 記帳選項排除已封存的設定，但保留編輯中交易用到的項目`。

---

## Task J15：設定頁的純函式

**Files:** Create `web/src/app/features/settings/settings-rules.ts`、`settings-rules.spec.ts`

把「哪些項目屬於同一組、每一列要顯示什麼、重新排序時要送出什麼」抽成純函式，這樣元件只負責呈現。要特別注意，排序送出的 `ids` 必須包含**已封存**的成員（J4 要求整組完全相同），但畫面上只能拖曳未封存的列。做法是把已封存的成員依原本的順序接在最後面。

- [ ] **Step 1：寫失敗測試**

```ts
import { AccountDto, CategoryDto } from '../../core/api/dto';
import { accountGroup, categoryGroups, moveRow, reorderRequest } from './settings-rules';

const archived = '2026-05-01T00:00:00+00:00';
const cat = (id: string, name: string, kind: 'Income' | 'Expense', parentId: string | null, sortOrder: number,
  archivedAt: string | null = null): CategoryDto =>
  ({ id, name, kind, nature: kind === 'Expense' ? 'Floating' : null, parentId, sortOrder, archivedAt });

describe('settings-rules', () => {
  const categories: CategoryDto[] = [
    cat('salary', '薪資', 'Income', null, 0),
    cat('food', '飲食', 'Expense', null, 0),
    cat('lunch', '午餐', 'Expense', 'food', 0),
    cat('dinner', '晚餐', 'Expense', 'food', 1, archived),
    cat('fixed', '固定支出', 'Expense', null, 1),
  ];

  it('category_groups_are_income_mains_expense_mains_then_subs_per_main', () => {
    const groups = categoryGroups(categories);
    // 沒有子分類的主分類也有一組（空的），才能在上面新增第一個子分類
    expect(groups.map(g => g.title)).toEqual(['收入主分類', '支出主分類', '薪資 的子分類', '飲食 的子分類', '固定支出 的子分類']);
    expect(groups[1].rows.map(r => r.label)).toEqual(['飲食', '固定支出']);
    expect(groups[1].rows[0].detail).toBe('浮動');
    expect(groups[2].rows).toEqual([]);
    expect(groups[3].rows.map(r => [r.label, r.archived])).toEqual([['午餐', false], ['晚餐', true]]);
    expect(groups[3]).toMatchObject({ path: 'categories', categoryKind: 'Expense', parentId: 'food' });
  });

  it('account_group_shows_type_and_archived_state', () => {
    const accounts: AccountDto[] = [
      { id: 'a1', name: '現金', type: 'Cash', openingBalance: 0, countsAsAvailableCash: true, sortOrder: 0, archivedAt: null },
      { id: 'a2', name: '舊卡', type: 'CreditCard', openingBalance: 0, countsAsAvailableCash: false, sortOrder: 1, archivedAt: archived },
    ];
    const group = accountGroup(accounts);
    expect(group.rows.map(r => [r.label, r.detail, r.archived])).toEqual([['現金', '現金', false], ['舊卡', '信用卡', true]]);
  });

  it('move_row_returns_new_array', () => {
    const ids = ['a', 'b', 'c'];
    expect(moveRow(ids, 2, 0)).toEqual(['c', 'a', 'b']);
    expect(ids).toEqual(['a', 'b', 'c']);
  });

  it('reorder_request_appends_archived_members_of_the_group', () => {
    const group = categoryGroups(categories)[3];
    expect(reorderRequest(group, ['lunch'])).toEqual({ kind: 'Expense', parentId: 'food', ids: ['lunch', 'dinner'] });
    const mains = categoryGroups(categories)[1];
    expect(reorderRequest(mains, ['fixed', 'food'])).toEqual({ kind: 'Expense', parentId: null, ids: ['fixed', 'food'] });
  });
});
```

- [ ] **Step 2：跑測試確認失敗**：`Cannot find module './settings-rules'`。
- [ ] **Step 3：最小實作**

```ts
import { moveItemInArray } from '@angular/cdk/drag-drop';
import { AccountDto, AccountType, CategoryDto, CategoryKind, ExpenseNature, PlanningFundDto, ReorderBody, SettingPath } from '../../core/api/dto';

export interface SettingRow { id: string; label: string; detail: string; archived: boolean }
export interface SettingGroup {
  key: string; title: string; path: SettingPath; categoryKind: CategoryKind | null; parentId: string | null; rows: SettingRow[];
}

// 若 summary.page.ts 已有同樣的對照表，改成共用那一份
export const ACCOUNT_TYPE_LABELS: Record<AccountType, string> = {
  Cash: '現金', Bank: '銀行', CreditCard: '信用卡', EWallet: '電子錢包', Loan: '貸款',
};
export const NATURE_LABELS: Record<ExpenseNature, string> = { Floating: '浮動', Fixed: '固定', Loan: '貸款', Special: '特別' };

const bySortOrder = <T extends { sortOrder: number }>(items: T[]) => [...items].sort((a, b) => a.sortOrder - b.sortOrder);

export function accountGroup(accounts: AccountDto[]): SettingGroup {
  return {
    key: 'accounts', title: '帳戶', path: 'accounts', categoryKind: null, parentId: null,
    rows: bySortOrder(accounts).map(a => ({ id: a.id, label: a.name, detail: ACCOUNT_TYPE_LABELS[a.type], archived: a.archivedAt !== null })),
  };
}

export function fundGroup(funds: PlanningFundDto[]): SettingGroup { /* 同理，detail 為 '' */ }

export function categoryGroups(categories: CategoryDto[]): SettingGroup[] {
  const row = (c: CategoryDto): SettingRow => ({
    id: c.id, label: c.name, detail: c.nature ? NATURE_LABELS[c.nature] : '', archived: c.archivedAt !== null,
  });
  const mains = (kind: CategoryKind) => bySortOrder(categories.filter(c => c.parentId === null && c.kind === kind));
  const mainGroup = (kind: CategoryKind, title: string): SettingGroup =>
    ({ key: `main-${kind}`, title, path: 'categories', categoryKind: kind, parentId: null, rows: mains(kind).map(row) });
  const subGroups = [...mains('Income'), ...mains('Expense')].map(main => ({
    key: `sub-${main.id}`, title: `${main.name} 的子分類`, path: 'categories' as const, categoryKind: main.kind, parentId: main.id,
    rows: bySortOrder(categories.filter(c => c.parentId === main.id)).map(row),
  }));
  return [mainGroup('Income', '收入主分類'), mainGroup('Expense', '支出主分類'), ...subGroups];
}

export function moveRow(ids: string[], from: number, to: number): string[] {
  const copy = [...ids];
  moveItemInArray(copy, from, to);
  return copy;
}

// 後端要求整組完全相同（含已封存）；已封存的成員依原順序接在最後
export function reorderRequest(group: SettingGroup, activeIds: string[]): ReorderBody {
  const ids = [...activeIds, ...group.rows.filter(r => r.archived).map(r => r.id)];
  return group.path === 'categories' ? { kind: group.categoryKind, parentId: group.parentId, ids } : { ids };
}
```

- [ ] **Step 4**：單檔全綠（4 passed）。**Step 5**：全部 Expected 208 passed。
- [ ] **Step 6：Commit**：`feat(web): 設定頁的分組、移動與排序請求（純函式）`。

---

## Task J16：SettingList 元件

**Files:** Create `web/src/app/features/settings/setting-list.ts`、`setting-list.spec.ts`

一個元件負責顯示一組設定。▲▼ 與 drag-drop 走**同一條** `move(from, to)` 路徑（spec §8），所以測試用按鈕來測，drag-drop 只驗證 `drop()` 有轉呼叫 `move()`。為了做到樂觀更新加失敗還原，`reorder` 事件附帶一個 `revert()`，由父層在 API 失敗時呼叫。不用 `viewChild` 讓父層直接操作子元件，是因為一頁裡有很多個清單。

- [ ] **Step 1：寫失敗測試**

```ts
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CdkDragDrop } from '@angular/cdk/drag-drop';
import { ReorderRequest, SettingList } from './setting-list';
import { SettingRow } from './settings-rules';

const ROWS: SettingRow[] = [
  { id: 'a', label: '甲', detail: '', archived: false },
  { id: 'b', label: '乙', detail: '', archived: false },
  { id: 'c', label: '丙', detail: '', archived: false },
  { id: 'z', label: '舊', detail: '', archived: true },
];

describe('SettingList', () => {
  let fixture: ComponentFixture<SettingList>;
  let el: HTMLElement;
  const reorders: ReorderRequest[] = [];
  const unarchived: string[] = [];

  beforeEach(async () => {
    reorders.length = 0;
    unarchived.length = 0;
    fixture = TestBed.createComponent(SettingList);
    fixture.componentRef.setInput('rows', ROWS);
    fixture.componentInstance.reorder.subscribe(r => reorders.push(r));
    fixture.componentInstance.unarchive.subscribe(id => unarchived.push(id));
    await fixture.whenStable();
    el = fixture.nativeElement;
  });

  const labels = () => [...el.querySelectorAll('.active .label')].map(n => n.textContent!.trim());
  const button = (row: number, cls: 'up' | 'down') =>
    el.querySelectorAll<HTMLButtonElement>(`.active .row`)[row].querySelector<HTMLButtonElement>(`button.${cls}`)!;

  it('up_button_moves_row_and_emits_ids', async () => {
    button(1, 'up').click();
    await fixture.whenStable();
    expect(labels()).toEqual(['乙', '甲', '丙']);
    expect(reorders.map(r => r.ids)).toEqual([['b', 'a', 'c']]);
  });

  it('first_up_and_last_down_are_disabled', () => {
    expect(button(0, 'up').disabled).toBe(true);
    expect(button(2, 'down').disabled).toBe(true);
    expect(button(1, 'down').disabled).toBe(false);
  });

  it('drop_uses_same_path', async () => {
    fixture.componentInstance.drop({ previousIndex: 0, currentIndex: 2 } as CdkDragDrop<SettingRow[]>);
    await fixture.whenStable();
    expect(reorders.map(r => r.ids)).toEqual([['b', 'c', 'a']]);
  });

  it('revert_restores_previous_order', async () => {
    button(2, 'up').click();
    await fixture.whenStable();
    reorders[0].revert();
    await fixture.whenStable();
    expect(labels()).toEqual(['甲', '乙', '丙']);
  });

  it('archived_rows_are_collapsed_until_toggled', async () => {
    expect(el.querySelector('.archived')).toBeNull();
    el.querySelector<HTMLButtonElement>('button.toggle-archived')!.click();
    await fixture.whenStable();
    expect(el.querySelector('.archived .label')?.textContent?.trim()).toBe('舊');
    el.querySelector<HTMLButtonElement>('.archived button.unarchive')!.click();
    expect(unarchived).toEqual(['z']);
  });

  it('new_rows_input_resets_local_order', async () => {
    button(1, 'up').click();
    fixture.componentRef.setInput('rows', [...ROWS]);
    await fixture.whenStable();
    expect(labels()).toEqual(['甲', '乙', '丙']);
  });
});
```

- [ ] **Step 2：跑測試確認失敗**：`Cannot find module './setting-list'`。
- [ ] **Step 3：最小實作**

```ts
import { ChangeDetectionStrategy, Component, computed, input, linkedSignal, output, signal } from '@angular/core';
import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList } from '@angular/cdk/drag-drop';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { SettingRow, moveRow } from './settings-rules';

export interface ReorderRequest { ids: string[]; revert(): void }

@Component({
  selector: 'app-setting-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CdkDropList, CdkDrag, CdkDragHandle, MatButtonModule, MatMenuModule],
  template: `
    <ul class="active" cdkDropList (cdkDropListDropped)="drop($event)">
      @for (row of order(); track row.id; let i = $index, last = $last) {
        <li class="row" cdkDrag>
          <span class="handle" cdkDragHandle aria-hidden="true">⠿</span>
          <span class="label">{{ row.label }}</span>
          <span class="detail">{{ row.detail }}</span>
          <button mat-button class="up" type="button" [disabled]="i === 0" (click)="move(i, i - 1)"
            [attr.aria-label]="row.label + ' 上移'">▲</button>
          <button mat-button class="down" type="button" [disabled]="last" (click)="move(i, i + 1)"
            [attr.aria-label]="row.label + ' 下移'">▼</button>
          <button mat-button type="button" [matMenuTriggerFor]="menu" [attr.data-menu]="row.id"
            [attr.aria-label]="row.label + ' 更多操作'">⋮</button>
          <mat-menu #menu="matMenu">
            <button mat-menu-item type="button" (click)="edit.emit(row.id)">修改</button>
            <button mat-menu-item type="button" (click)="archive.emit(row.id)">封存</button>
            <button mat-menu-item type="button" (click)="remove.emit(row.id)">刪除</button>
          </mat-menu>
        </li>
      }
    </ul>
    @if (archivedRows().length > 0) {
      <button mat-button type="button" class="toggle-archived" (click)="showArchived.update(v => !v)">
        已封存（{{ archivedRows().length }}）{{ showArchived() ? '▴' : '▾' }}
      </button>
      @if (showArchived()) {
        <ul class="archived">
          @for (row of archivedRows(); track row.id) {
            <li class="row">
              <span class="label">{{ row.label }}</span>
              <span class="detail">{{ row.detail }}</span>
              <button mat-button type="button" class="unarchive" (click)="unarchive.emit(row.id)">解除封存</button>
              <button mat-button type="button" class="remove" (click)="remove.emit(row.id)">刪除</button>
            </li>
          }
        </ul>
      }
    }
  `,
  styles: `
    ul { list-style: none; margin: 0; padding: 0; }
    .row { display: flex; align-items: center; gap: 4px; min-height: 44px; }
    .label { flex: 1; }
    .detail { color: var(--mat-sys-on-surface-variant); font-size: 0.875rem; }
    .handle { cursor: grab; padding: 0 8px; }
    .archived .row { opacity: 0.6; }
  `,
})
export class SettingList {
  readonly rows = input.required<SettingRow[]>();
  readonly reorder = output<ReorderRequest>();
  readonly edit = output<string>();
  readonly archive = output<string>();
  readonly unarchive = output<string>();
  readonly remove = output<string>();

  // 樂觀更新：拖曳或按 ▲▼ 立刻改畫面；父層重新載入（rows 變成新陣列）或呼叫 revert() 時回到伺服器的順序
  protected readonly order = linkedSignal(() => this.rows().filter(row => !row.archived));
  protected readonly archivedRows = computed(() => this.rows().filter(row => row.archived));
  protected readonly showArchived = signal(false);

  drop(event: CdkDragDrop<SettingRow[]>): void {
    this.move(event.previousIndex, event.currentIndex);
  }

  move(from: number, to: number): void {
    const previous = this.order();
    if (from === to || to < 0 || to >= previous.length) {
      return;
    }
    const byId = new Map(previous.map(row => [row.id, row]));
    const ids = moveRow(previous.map(row => row.id), from, to);
    this.order.set(ids.map(id => byId.get(id)!));
    this.reorder.emit({ ids, revert: () => this.order.set(previous) });
  }
}
```

- [ ] **Step 4**：單檔 6 passed。**Step 5**：全部 Expected 214 passed。
- [ ] **Step 6：Commit**：`feat(web): SettingList 元件（drag-drop、▲▼、封存區）`。

---

## Task J17：SettingDialog（新增與修改）

**Files:** Create `web/src/app/features/settings/setting-dialog.ts`、`setting-dialog.spec.ts`

用一個對話框處理三種設定的新增與修改，欄位依情境顯示：

| 情境 | 欄位 |
|---|---|
| 新增帳戶 | 名稱、類型、期初餘額、計入可用現金（只有類型是現金時顯示） |
| 修改帳戶 | 名稱、計入可用現金（只有現金帳戶） |
| 新增財務規劃帳戶 | 名稱、期初餘額 |
| 修改財務規劃帳戶 | 名稱 |
| 支出主分類（新增或修改） | 名稱、支出性質 |
| 其他分類 | 名稱 |

修改時不顯示期初餘額，因為後端沒有修改期初餘額的 API。這個功能留在範圍外。

- [ ] **Step 1：寫失敗測試**

```ts
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatInputHarness } from '@angular/material/input/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { MatCheckboxHarness } from '@angular/material/checkbox/testing';
import { SettingDialog, SettingDialogData } from './setting-dialog';

async function open(data: SettingDialogData) {
  const close = vi.fn();
  TestBed.configureTestingModule({
    providers: [{ provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: { close } }],
  });
  const fixture = TestBed.createComponent(SettingDialog);
  await fixture.whenStable();
  return { fixture, close, loader: TestbedHarnessEnvironment.loader(fixture), el: fixture.nativeElement as HTMLElement };
}

describe('SettingDialog', () => {
  it('add_account_returns_all_fields', async () => {
    const { loader, el, close } = await open({ mode: 'add', path: 'accounts' });
    await (await loader.getHarness(MatInputHarness.with({ selector: '[formControlName=name]' }))).setValue(' 郵局 ');
    const type = await loader.getHarness(MatSelectHarness.with({ selector: '[formControlName=type]' }));
    await type.open();
    await type.clickOptions({ text: '現金' });
    await (await loader.getHarness(MatInputHarness.with({ selector: '[formControlName=openingBalance]' }))).setValue('500');
    await (await loader.getHarness(MatCheckboxHarness)).uncheck();
    el.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    expect(close).toHaveBeenCalledWith({ name: '郵局', type: 'Cash', openingBalance: 500, countsAsAvailableCash: false, nature: null });
  });

  it('edit_bank_account_shows_only_name', async () => {
    const { loader } = await open({ mode: 'edit', path: 'accounts', name: '銀行', accountType: 'Bank', countsAsAvailableCash: false });
    expect(await loader.getAllHarnesses(MatCheckboxHarness)).toHaveLength(0);
    expect(await loader.getAllHarnesses(MatSelectHarness)).toHaveLength(0);
    expect(await (await loader.getHarness(MatInputHarness)).getValue()).toBe('銀行');
  });

  it('expense_main_category_requires_nature', async () => {
    const { loader, el, close } = await open({ mode: 'edit', path: 'categories', name: '飲食', categoryKind: 'Expense', isMain: true, nature: 'Floating' });
    const nature = await loader.getHarness(MatSelectHarness.with({ selector: '[formControlName=nature]' }));
    await nature.open();
    await nature.clickOptions({ text: '特別' });
    el.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    expect(close).toHaveBeenCalledWith(expect.objectContaining({ name: '飲食', nature: 'Special' }));
  });

  it('blank_name_does_not_close', async () => {
    const { el, close } = await open({ mode: 'add', path: 'planning-funds' });
    el.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    expect(close).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2：跑測試確認失敗**：`Cannot find module './setting-dialog'`。
- [ ] **Step 3：最小實作**

```ts
export interface SettingDialogData {
  mode: 'add' | 'edit';
  path: SettingPath;
  name?: string;
  accountType?: AccountType;           // 修改帳戶時
  countsAsAvailableCash?: boolean;
  categoryKind?: CategoryKind;         // 分類
  isMain?: boolean;
  nature?: ExpenseNature | null;
}
export interface SettingDialogResult {
  name: string; type?: AccountType; openingBalance?: number; countsAsAvailableCash?: boolean; nature: ExpenseNature | null;
}
```

元件使用 `ReactiveFormsModule`、`MatDialogModule`、`MatFormFieldModule`、`MatInputModule`、`MatSelectModule`、`MatCheckboxModule`、`MatButtonModule`。顯示條件寫成 `computed`：

- `showType = mode === 'add' && path === 'accounts'`
- `showOpening = mode === 'add' && path !== 'categories'`
- `showCash = path === 'accounts' && (目前類型 === 'Cash')`。新增時的類型由 `type` 控制項的 `valueChanges` 轉成 signal；修改時取 `data.accountType`。
- `showNature = path === 'categories' && categoryKind === 'Expense' && isMain`，而且 nature 必填。

送出時如果表單無效，就 `markAllAsTouched()` 並 return。否則呼叫 `dialogRef.close(result)`：`name` 去掉前後空白，`openingBalance` 轉成 number，不顯示的欄位不放進 result（`nature` 不顯示時是 `null`，帳戶修改時 `countsAsAvailableCash` 一律帶出，非現金帳戶是 `false`）。選項文字使用 `ACCOUNT_TYPE_LABELS`、`NATURE_LABELS`。

測試的期望中，新增帳戶的 result 有 `nature: null`，修改分類用的是 `objectContaining`，這兩點都依照上面的規則。

- [ ] **Step 4**：單檔 4 passed。**Step 5**：全部 Expected 218 passed。
- [ ] **Step 6：Commit**：`feat(web): 設定的新增與修改對話框`。

---

## Task J18：設定頁、路由與導覽

**Files:** Create `web/src/app/features/settings/settings.page.{ts,html,scss}`、`settings.page.spec.ts`／Modify `web/src/app/app.routes.ts`、`web/src/app/app.html`

這個頁面負責把各組資料、`SettingList`、對話框與 API 串起來。每個寫入動作都走同一個 `run()`：成功時顯示訊息並 `currentBook.reload()`；失敗時，domain 錯誤顯示後端的 `message`（D2），notFound 錯誤顯示「此項目已不存在」並重新載入，重新排序失敗時再加上 `revert()`。通用錯誤（離線、XSRF）已經由 interceptor 顯示，這裡不重複。刪除前要先用 `confirm()` 確認（`shared/confirm-dialog.ts`）。

- [ ] **Step 1：寫失敗測試**

```ts
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatTabGroupHarness } from '@angular/material/tabs/testing';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { BrowserLocation } from '../../core/browser-location';
import { CurrentBook } from '../../core/book/current-book';
import { apiErrorInterceptor } from '../../core/errors/error-interceptor';
import { Notifier } from '../../core/errors/notifier';
import { BOOK } from '../transactions/testing/book-fixture';
import { SettingsPage } from './settings.page';

async function setup() {
  const notifier = { show: vi.fn() };
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([apiErrorInterceptor])), provideHttpClientTesting(),
      { provide: Notifier, useValue: notifier },
      { provide: BrowserLocation, useValue: { assign: vi.fn(), currentPath: () => '/' } },
    ],
  });
  const httpTesting = TestBed.inject(HttpTestingController);
  const loaded = TestBed.inject(CurrentBook).load(BOOK.id);
  httpTesting.expectOne(`/api/books/${BOOK.id}`).flush(BOOK);
  await loaded;
  const fixture = TestBed.createComponent(SettingsPage);
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  const el = fixture.nativeElement as HTMLElement;
  const openTab = async (label: string) => {
    await (await loader.getHarness(MatTabGroupHarness)).selectTab({ label });
    await fixture.whenStable();
  };
  const rowLabels = (selector: string) =>
    [...el.querySelectorAll(`${selector} .active .label`)].map(n => n.textContent!.trim());
  return { fixture, loader, el, httpTesting, notifier, openTab, rowLabels };
}

const ACCOUNTS_ORDER = { method: 'PUT', url: `/api/books/${BOOK.id}/accounts/order` };

describe('SettingsPage', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('lists_expense_mains_in_sort_order', async () => {
    const { rowLabels } = await setup();
    expect(rowLabels('[data-group="main-Expense"]')).toEqual(['飲食']);
    expect(rowLabels('[data-group="main-Income"]')).toEqual(['薪資']);
  });

  it('moving_account_sends_order_and_reloads_book', async () => {
    const { el, fixture, httpTesting, openTab, notifier } = await setup();
    await openTab('帳戶');
    el.querySelectorAll<HTMLButtonElement>('[data-group="accounts"] .active button.up')[1].click();
    await fixture.whenStable();

    const request = httpTesting.expectOne(ACCOUNTS_ORDER);
    expect(request.request.body).toEqual({ ids: ['acc-bank', 'acc-cash', 'acc-card', 'acc-ewallet', 'acc-loan'] });
    request.flush(null, { status: 204, statusText: '' });
    await fixture.whenStable();
    httpTesting.expectOne(`/api/books/${BOOK.id}`).flush(BOOK);
    await fixture.whenStable();
    expect(notifier.show).not.toHaveBeenCalled();   // 排序成功不打擾
  });

  it('failed_reorder_reverts_and_shows_backend_message', async () => {
    const { el, fixture, httpTesting, openTab, notifier, rowLabels } = await setup();
    await openTab('帳戶');
    el.querySelectorAll<HTMLButtonElement>('[data-group="accounts"] .active button.up')[1].click();
    await fixture.whenStable();

    httpTesting.expectOne(ACCOUNTS_ORDER).flush(
      { detail: '帳戶的排序清單與目前的項目不一致，請重新載入後再試。', code: 'rule' },
      { status: 422, statusText: 'Unprocessable Entity' });
    await fixture.whenStable();

    expect(rowLabels('[data-group="accounts"]')).toEqual(['現金', '銀行', '信用卡', '悠遊卡', '房貸']);
    expect(notifier.show).toHaveBeenCalledWith('帳戶的排序清單與目前的項目不一致，請重新載入後再試。');
  });

  it('archive_from_menu_posts_and_reloads', async () => {
    const { loader, fixture, httpTesting, openTab, notifier } = await setup();
    await openTab('帳戶');
    await (await loader.getHarness(MatMenuHarness.with({ selector: '[data-menu="acc-card"]' }))).clickItem({ text: '封存' });

    httpTesting.expectOne({ method: 'POST', url: `/api/books/${BOOK.id}/accounts/acc-card/archive` })
      .flush(null, { status: 204, statusText: '' });
    await fixture.whenStable();
    httpTesting.expectOne(`/api/books/${BOOK.id}`).flush(BOOK);
    await fixture.whenStable();
    expect(notifier.show).toHaveBeenCalledWith('已封存「信用卡」');
  });

  it('saves_lock_date_and_offers_downloads', async () => {
    const { el, fixture, httpTesting, openTab } = await setup();
    await openTab('鎖帳日與資料');
    const input = el.querySelector<HTMLInputElement>('input[type=date]')!;
    input.value = '2026-03-31';
    input.dispatchEvent(new Event('input'));
    el.querySelector<HTMLButtonElement>('button.save-lock-date')!.click();
    await fixture.whenStable();

    const request = httpTesting.expectOne({ method: 'PUT', url: `/api/books/${BOOK.id}/lock-date` });
    expect(request.request.body).toEqual({ lockDate: '2026-03-31' });
    request.flush(null, { status: 204, statusText: '' });
    await fixture.whenStable();
    httpTesting.expectOne(`/api/books/${BOOK.id}`).flush(BOOK);

    const links = [...el.querySelectorAll<HTMLAnchorElement>('a[download]')].map(a => a.getAttribute('href'));
    expect(links).toEqual([
      `/api/books/${BOOK.id}/export/transactions.csv`,
      `/api/books/${BOOK.id}/export/transactions.xlsx`,
      `/api/books/${BOOK.id}/export/backup.json`,
    ]);
  });
});
```

`BOOK` 的帳戶順序是現金、銀行、信用卡、悠遊卡、房貸。按下第 2 列（銀行）的 ▲ 之後，順序變成銀行、現金……。

- [ ] **Step 2：跑測試確認失敗**：`Cannot find module './settings.page'`。
- [ ] **Step 3：最小實作**

`settings.page.ts` 的重點（完整的 template 依下面的結構寫）：

```ts
@Component({
  selector: 'app-settings-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatTabsModule, MatButtonModule, MatFormFieldModule, MatInputModule, SettingList],
  templateUrl: './settings.page.html',
  styleUrl: './settings.page.scss',
})
export class SettingsPage {
  private readonly currentBook = inject(CurrentBook);
  private readonly api = inject(BookApi);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);
  private readonly destroyRef = inject(DestroyRef);

  // bookGuard 已經載入帳本
  protected readonly book = computed(() => this.currentBook.book()!);
  protected readonly categoryGroups = computed(() => categoryGroups(this.book().categories));
  protected readonly accounts = computed(() => accountGroup(this.book().accounts));
  protected readonly funds = computed(() => fundGroup(this.book().planningFunds));
  protected readonly lockDate = linkedSignal(() => this.book().lockDate ?? '');

  protected exportUrl(file: string): string {
    return `/api/books/${this.book().id}/export/${file}`;
  }

  protected onReorder(group: SettingGroup, request: ReorderRequest): void {
    this.run(this.api.reorder(this.book().id, group.path, reorderRequest(group, request.ids)), null, () => request.revert());
  }

  protected onArchive(group: SettingGroup, id: string): void {
    this.run(this.api.archive(this.book().id, group.path, id), `已封存「${labelOf(group, id)}」`);
  }
  // onUnarchive、onRemove（先 confirm(this.dialog, `確定要刪除「…」？`)）、onAdd、onEdit（開 SettingDialog，依 group.path 組 data、呼叫對應的 add／update）

  protected saveLockDate(): void {
    this.run(this.api.setLockDate(this.book().id, this.lockDate() || null), '已更新鎖帳日');
  }

  private run(request: Observable<unknown>, success: string | null, onError?: () => void): void {
    const bookId = this.book().id;
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        if (success) {
          this.notifier.show(success);
        }
        void this.currentBook.reload(bookId);
      },
      error: (error: ApiError) => {
        onError?.();
        if (error.kind === 'domain') {
          this.notifier.show(error.message);
        } else if (error.kind === 'notFound') {
          this.notifier.show('此項目已不存在');
          void this.currentBook.reload(bookId);
        }
      },
    });
  }
}
```

`settings.page.html` 的結構：

```html
<mat-tab-group>
  <mat-tab label="分類">
    @for (group of categoryGroups(); track group.key) {
      <section [attr.data-group]="group.key">
        <header><h3>{{ group.title }}</h3><button mat-button type="button" (click)="onAdd(group)">新增</button></header>
        <app-setting-list [rows]="group.rows" (reorder)="onReorder(group, $event)" (edit)="onEdit(group, $event)"
          (archive)="onArchive(group, $event)" (unarchive)="onUnarchive(group, $event)" (remove)="onRemove(group, $event)" />
      </section>
    }
  </mat-tab>
  <mat-tab label="帳戶"> <!-- 同上，單一組 accounts() --> </mat-tab>
  <mat-tab label="財務規劃帳戶"> <!-- funds() --> </mat-tab>
  <mat-tab label="鎖帳日與資料">
    <mat-form-field><mat-label>鎖帳日</mat-label>
      <input matInput type="date" [value]="lockDate()" (input)="lockDate.set($any($event.target).value)" />
    </mat-form-field>
    <button mat-flat-button type="button" class="save-lock-date" (click)="saveLockDate()">儲存</button>
    <button mat-button type="button" (click)="lockDate.set(''); saveLockDate()">清除</button>
    <h3>下載</h3>
    <a mat-stroked-button [href]="exportUrl('transactions.csv')" download>交易明細 CSV</a>
    <a mat-stroked-button [href]="exportUrl('transactions.xlsx')" download>交易明細 Excel</a>
    <a mat-stroked-button [href]="exportUrl('backup.json')" download>完整備份</a>
  </mat-tab>
</mat-tab-group>
```

空的子分類組也要顯示標題與「新增」按鈕，這樣才能新增第一個子分類（J15）。

`app.routes.ts` 在 `summary` 之後加上：

```ts
      {
        path: 'settings',
        loadComponent: () => import('./features/settings/settings.page').then((m) => m.SettingsPage),
      },
```

`app.html` 在「總覽」之後加上 `<a mat-button [routerLink]="['/books', book.id, 'settings']">設定</a>`。這裡**不**保留 query params，因為 month 參數對設定頁沒有意義。

- [ ] **Step 4**：單檔 5 passed。**Step 5**：全部 Expected 223 passed；`npx ng build` 沒有新的 budget warning，`settings` 是獨立的 lazy chunk。
- [ ] **Step 6：Commit**：`feat(web): 帳本設定頁（分類、帳戶、財務規劃帳戶、鎖帳日與下載）`，`git add` 列出三個 page 檔、spec、`app.routes.ts`、`app.html`。

---

## Task J19：設定頁 E2E

**Files:** Create `web/e2e/settings.spec.ts`

用真實瀏覽器確認兩件事：路由與 lazy chunk 能正常載入；▲ 送出的 PUT 帶有 XSRF header 和正確的 body。drag-drop 的手勢不在 E2E 測試（spec §8：元件層級已經測過，而且 ▲▼ 走同一條路徑）。

- [ ] **Step 1：寫失敗測試**

```ts
import { expect, test } from '@playwright/test';
import { BOOK_ID, mockApi } from './fixtures';

test('settings_reorders_account_with_up_button', async ({ page }) => {
  await mockApi(page);
  const bodies: unknown[] = [];
  const headers: (string | null)[] = [];
  await page.route(`**/api/books/${BOOK_ID}/accounts/order`, async route => {
    bodies.push(route.request().postDataJSON());
    headers.push(await route.request().headerValue('x-xsrf-token'));
    await route.fulfill({ status: 204 });
  });

  await page.goto(`/books/${BOOK_ID}/settings`);
  await page.getByRole('tab', { name: '帳戶' }).click();
  const accounts = page.locator('[data-group="accounts"] .active .row');
  const secondName = (await accounts.nth(1).locator('.label').textContent())!.trim();
  await page.getByRole('button', { name: `${secondName} 上移` }).click();

  await expect.poll(() => bodies.length).toBe(1);
  expect((bodies[0] as { ids: string[] }).ids[0]).toBeDefined();
  expect(headers[0]).not.toBeNull();
});
```

`e2e/fixtures.ts` 的 BOOK 帳戶 id 與名稱以該檔為準，這裡刻意用「第 2 列的名稱」來點擊，避免把 fixture 內容寫死。

- [ ] **Step 2：跑測試確認失敗**：在 J18 之前寫這個測試會因為路由不存在而失敗；在 J18 之後寫，第一次就可能通過。這時請暫時把 `app.routes.ts` 的 settings 路由註解掉，確認測試會失敗（找不到 tab），再改回來。
- [ ] **Step 3**：不需要新的實作。如果失敗，修正 J18 的對應部分。
- [ ] **Step 4–5**：`npx playwright test` Expected 5 passed；`npx ng test --watch=false` 223 passed。
- [ ] **Step 6：Commit**：`test(web): 設定頁排序的 E2E`。

### ═══ 前端 checkpoint ═══

見下一節。

---

## 完成後的驗證

- [ ] `dotnet build` 0 warning；`dotnet test` 約 423，失敗 0；`SixJars.AcceptanceTests` 全綠，數字與基準相同。
- [ ] `web/`：`npx ng test --watch=false` 約 223 passed；`npx playwright test` 5 passed；`npx ng build` 成功。
- [ ] `git status` 乾淨；`git log --oneline master..` 在既有的 3 個 docs commit 之外，新增 19 個 Task commit，加上 checkpoint 的 `docs(plans):` 回寫。
- [ ] `git diff master -- . ':!docs'` 沒有連線字串、金鑰。

## 手動驗證（自動測試涵蓋不到）

由使用者在本機以真實資料庫的副本執行（P3 `deploy.md` 的本機 production build 流程）：

1. 套用 migration 之後打開設定頁，確認各組的排序**大致**符合 Excel，並記下需要調整的組（D1 的已知限制）。
2. 用滑鼠拖曳一列，以及在手機寬度下用 ▲▼ 各操作一次，重新整理後順序仍然保留。
3. 封存一個餘額為 0 的帳戶：記帳頁的帳戶選單不再出現它；找一筆使用這個帳戶的舊交易進入編輯，帳戶欄仍然顯示原本的帳戶。
4. 嘗試封存有餘額的帳戶、刪除有交易的分類，畫面顯示後端的中文訊息。
5. 把「其他」之類的特別支出主分類改成浮動（若沒有預定支出），總覽的月可用餘額依 ADR 0008 的規則變化合理。
6. 下載 CSV、xlsx、備份三個檔案；把備份用 CLI 還原到空資料庫，確認封存狀態與排序都還在。
7. `SIXJARS_LEGACY_WORKBOOK` 指向真實的 xlsm，跑 `dotnet test tests/SixJars.AcceptanceTests`，確認全綠（spec §9 J）。

## 後續（不在本計畫內）

- K：週期預定支出（會擴充 `SettingReferences` 加入週期項目，並新增 `CategoryArchived`、`AccountArchived` 略過原因）。
- L：`ChangeExpenseNature` 與 `RemoveCategory` 加上預算檢查（`has-budget` code）。
- 修改帳戶期初餘額、修改帳戶類型、子分類搬到別的主分類（spec §11 排除）。
- 鍵盤拖曳（Q13 決定不做）。
- 後端拒絕在新交易中使用已封存的項目（D7）。

---

## 附錄：核准前事實查核（2026-10-06）

| 引用 | 存在？ | 證據 | 修正 |
|---|---|---|---|
| `Category`／`Account`／`PlanningFund` 沒有排序與封存欄位 | ✓ | `src/SixJars.Domain/Books/*.cs` | — |
| 匯入程式依 Excel 順序 `Add*` | ✓ | `MappingSession.cs:26–106` | — |
| Id 是 UUIDv7 | ✓ | `Ids.cs`：`Guid.CreateVersion7()` | D1：只能大致還原順序 |
| 交易與預定支出有軟刪除的 query filter | ✓ | `TransactionConfiguration.cs:16`、`PlannedExpenseConfiguration.cs:16` | D3 要用 `IgnoreQueryFilters()` |
| 交易與設定之間沒有 FK | ✓ | `TransactionConfiguration.cs` 只有 owned Postings | 參照檢查只能在 Application 做 |
| DomainException 對應到 422＋`code`；409 只用於並行衝突 | ✓ | `ApiExceptionHandler.cs:24–36`；前端 `api-error.ts:37` 把 409 視為 `conflict` | D2 |
| 預定支出只允許固定、貸款、特別 | ✓ | `PlannedExpense.cs:133` | D6 |
| `BackupJson` 的 `RespectRequiredConstructorParameters = true`；格式版本 1；還原會比對 DTO | ✓ | `BackupJson.cs:21`、`BackupDocument.cs:23`、`RestoreBackup.cs:133–139` | D5、D8、J12 |
| `BackupExportTests` 斷言版本是 1 | ✓ | `BackupExportTests.cs:98` | J12 修改 |
| `ILedgerSummaryQuery.PostingTotalsAsync`、`FundDeltaTotalsAsync`、`BalanceCutoff.AsOf`、`LedgerBalances` | ✓ | `Application/Ledger/*.cs` | J9 |
| `DatabaseFacade.MigrateAsync(string targetMigration, CancellationToken)` | ✓ | EF Core Relational 10.0.12 的 XML doc | J6 |
| 最新 migration 是 `20261004050139_AddDataProtectionKeys` | ✓ | `Persistence/Migrations/` | J6 的 `BeforeSettingsOrder` |
| `ApiFactory` 是 `WebApplicationFactory<Program>`；`ISixJarsDbContext` 註冊為 scoped | ✓ | `ApiFactory.cs:17–21`、`Infrastructure/DependencyInjection.cs:24` | J7、J11 可以用 `factory.Services` |
| `TimeProvider` 已註冊 | ✓ | `Program.cs:17` | J9 |
| `AuditAction.Update`／`Delete`、`AuditEntityTypes.Account` 等 | ✓ | `AuditAction.cs`、`AuditEntry.cs:31–41` | — |
| `PlannedExpensesEndpointsTests.CreateAsync`、`InsuranceInput` 是 internal static | ✓ | `PlannedExpensesEndpointsTests.cs:485–491` | J8、J11 重用 |
| 交易 DELETE 需要 `?version=` | ✓ | `TransactionsEndpoints.cs:26–31` | J11 |
| 匯出路由 `/export/transactions.csv`、`.xlsx`、`/export/backup.json` | ✓ | `ExportsEndpoints.cs:13–22` | J18 |
| `@angular/cdk` 22.2 有 `CdkDropList`、`CdkDrag`、`moveItemInArray` | ✓ | `node_modules/@angular/cdk/types/drag-drop.d.ts` | J15、J16 |
| `applyKindRule()` 會清掉不在選項裡的值 | ✓ | `transaction-form.ts:314–331` | J14 的 keep 集合 |
| 財務規劃帳戶選項由 template 直接讀 `book().planningFunds` | ✓ | `transaction-form.html:133` | J14 |
| FormDriver 有 `edit`、`selectOptions`、`selectedText`；`setup()` 沒有參數 | ✓ | `transaction-form.spec.ts:66–158` | J14 把 setup 改成接受 book |
| 前端 422 → `{ kind: 'domain', message }`；interceptor 只處理通用錯誤 | ✓ | `api-error.ts:39`、`error-interceptor.ts` | J18 的 `run()` |
| `CurrentBook` 只有 `load()`、沒有 reload | ✓ | `current-book.ts` | J13 |
| `ids.Contains(p.CategoryId)` 在強型別 Id 加上 value converter 時能否翻譯 | **未證實** | 現有程式只有 `IQueryable.Contains` 的用法（`ListBooks.cs:25`） | J8 Step 5 附有替代寫法 |
| `LedgerSummaryDto`、`PlannedExpenseDto.Version`、稽核查詢路由 | **未逐一查證** | — | J7、J8 的步驟中已註明「以實際檔案為準」 |

## 執行結果與偏差

（每個 checkpoint 結束後以 `docs(plans):` 回寫：測試數字、與計畫不同的型別或簽章、為什麼改。）
