# P4 段 K：預定支出與週期預定支出 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL：逐 Task 執行時使用 superpowers:test-driven-development；分派子代理時使用 superpowers:subagent-driven-development。每個 Task 都是「失敗測試 → 確認失敗原因 → 最小實作 → 全綠 → 一個 commit」。

**Goal**：完成 P4 段 K。使用者可以維護**週期預定支出**（每月，或每年的指定月份），在預定支出頁對某個歸屬月份明確按「產生」建立該月的預定支出（同一來源同一月只產生一次，包含已刪除的，ADR 0009），也可以按「以現值更新」把該月未付、由週期項目產生的預定支出改成週期項目目前的內容。預定支出頁可以列出、新增、修改、刪除與付款。

**Architecture**：新 aggregate `RecurringPlannedExpense`（獨立於 `Book`，spec §4.1）。「哪些要產生、哪些要略過、哪些要更新」的規則全部放在 Domain 的 `RecurringPlanner`（純函式，輸入帳本、週期項目、該月既有的預定支出），Application 只負責查資料、呼叫 planner、寫稽核與存檔。`PlannedExpense` 新增 `SourceId`，資料庫以 `(SourceId, BudgetMonth)` 的部分唯一索引（`SourceId IS NOT NULL`，**不**排除已刪除）作為冪等的最後一道防線。前端新增 `features/planned-expenses`，一頁兩個分頁：「本月」（換月、產生、更新、付款）與「週期項目」。

**Tech Stack**：.NET 10、EF Core 10（Npgsql）、MediatR、FluentValidation、xUnit v3、FluentAssertions；Angular 22.2、Angular Material／CDK 22.2、Vitest、Playwright。

- 設計文件：[`docs/superpowers/specs/2026-10-06-p4-settings-planning-budget-reports-design.md`](../specs/2026-10-06-p4-settings-planning-budget-reports-design.md) §4、§7、§8、§10
- ADR：[`0009-recurring-planned-expenses-generated-explicitly.md`](../../adr/0009-recurring-planned-expenses-generated-explicitly.md)
- 前一段計畫：[`2026-10-06-p4-j-settings.md`](2026-10-06-p4-j-settings.md)（D2 的 422＋code、D3 參照檢查含軟刪除、D8 備份版本相容、設定頁的 `run()` 錯誤處理）

---

## 執行前必讀

### 環境

- 工作目錄：`F:\VibeCode\SixJars-AccountingBook`（Windows）。分支：`claude/p4-k-planned-expenses`（從 master `bf7a377` 開出，**不是 master**）。
- 後端全部測試：`dotnet test --solution SixJars.slnx`（需要 Docker Desktop）。
- 後端單一類別：`dotnet test --project tests/SixJars.Domain.Tests --filter-class "*RecurringPlannedExpenseTests*"`（xUnit v3 語法，見 CLAUDE.md）。
- Migration：`dotnet ef migrations add <Name> --project src/SixJars.Infrastructure --startup-project src/SixJars.Infrastructure --output-dir Persistence/Migrations`。
- 前端指令都在 `web/` 內執行：
  - 全部：`npx ng test --watch=false`
  - 單檔：`npx ng test --watch=false --include src/app/<path>.spec.ts`
  - E2E：`npx playwright test`

### 基準線（2026-10-07 本機實測，master `bf7a377`）

- 後端：`dotnet test` 總計 **426**，失敗 0，略過 0（`reference/` 存在，Acceptance 有實際執行）。加上事實查核的 `PlanKEfAssumptionTests` 3 條之後是 **429**，K1 從 429 起算（以下各 Task 的預測數字都要再加 3）。
- 前端：`ng test` **232 passed（28 個檔案）**。Playwright 5 個（J 段回寫的數字，本次沒有重跑）。
- 任何時候數字低於基準線，就是弄壞了東西。各 Task 的「Expected」數字是**預測**，以實際為準，只要只增不減即可。

### 絕對不要碰的檔案

- `.env`、`.env.gcp`、`reference/`（個資）、`efbundle.exe`、`web/.angular/`。
- 每個 Task 都用明確路徑 `git add`，**禁止 `git add .`／`git add -A`**。

### 慣例

- 註解、commit 訊息用繁體中文；識別字用英文。commit 訊息結尾加上 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`。
- TDD：先寫失敗測試，確認它**以正確的理由**失敗（編譯錯誤指向「尚未存在的成員」也算正確理由；打錯字不算），再寫實作。每個 Task 至少做一次變異檢查：拿掉關鍵條件，確認測試變紅，再還原。一個 Task 一個 commit。
- 金額符號：預定支出與週期項目的金額都**沿用支出的符號慣例（負數）**；前端輸入正數，送出前轉成負數。
- 後端測試沿用：`ApiFactory.CreateAsync(postgres, Ct)`、`factory.SeedBookAsync(Ct)`（含「固定支出／保險費」「貸款支出／房屋貸款」「主食／午餐」與「房屋貸款」貸款帳戶）、`factory.CreateMemberClientAsync()`、`ApiJson.Options`、`postgres.CreateDatabaseAsync(Ct)`（回傳 `Func<SixJarsDbContext>`）、`TestContext.Current.CancellationToken`。
- 前端：standalone 元件、signals、zoneless、`ChangeDetectionStrategy.OnPush`；HTTP 測試用 `HttpTestingController`；Material 元件用 harness；寫入的錯誤處理沿用設定頁 `run()` 的分類（domain／validation／notFound，另加 conflict）。
- **段落順序**：K1–K9 是後端，K9 完成後是**後端 checkpoint**，停下來讓使用者檢視；K10–K17 是前端，K17 完成後是**前端 checkpoint**。

### 本計畫做的決定（spec 沒有寫死、或與 spec 不同的地方）

使用者已於 2026-10-07 確認 D1、D2、D3、D5、D6、D11；其餘（D4、D7–D10、D12）沿用計畫的預設。

| # | 決定 | 理由 | 被否決的選項 |
|---|---|---|---|
| D1 | 有來源（`SourceId`）的預定支出**不能改歸屬月份**（422 `rule`）；前端的修改對話框也不提供月份欄位 | `(SourceId, BudgetMonth)` 唯一。把 4 月產生的那筆搬到 5 月之後，再對 4 月按「產生」會再建一筆（4 月已沒有這個來源），等於複製；若 5 月本來就有同來源的一筆，存檔時撞唯一索引變成 500 | 搬月份時清掉 `SourceId`：違反 ADR 0009「刪除代表使用者決定這個月沒有這筆」的精神，搬走的那筆在原月份會被重新產生 |
| D2 | 貸款性質的付款對話框**要選貸款帳戶**；只有一個未封存的貸款帳戶時預選它。本金仍然手動輸入、不預填（Q20） | `PayPlannedExpense` 對貸款性質要求 `LoanAccountId`（`PayPlannedExpense.cs:82`），spec §4.3 只列了日期、金額、帳戶、本金，漏了它；週期項目也沒有存貸款帳戶 | 在週期項目與預定支出加「貸款帳戶」欄位：要動 `PlannedExpense` 的不變條件與 migration，K 段不值得；大多數家庭只有一筆房貸，預選已經足夠 |
| D3 | 備份格式升到 **v3**：加入週期項目清單，`PlannedExpenseDto` 加 `SourceId`；還原接受 v1–v3 | spec §4 沒有提到備份。不升版的話，還原後所有產生過的預定支出都失去來源，下個月再按「產生」會重複建立（`(SourceId, 月)` 的唯一性失效） | 不備份週期項目：還原後要手動重建，且已產生的月份會重複 |
| D4 | 週期項目**硬刪除**（只有從未產生過預定支出時才可以，含已刪除的，ADR 0009）；不提供封存，用「結束月份」停用 | 週期項目是設定性質的資料，歷史記錄在它產生的預定支出上；被參照的那一刻起就只能設結束月份（Q15） | 軟刪除：多一個 query filter，且仍然要擋「被參照時不能刪」，沒有好處 |
| D5 | 已產生過的週期項目**所有欄位都可以修改**（分類、帳戶、金額、備註、週期、起訖月），只是不能刪除。修改不回寫已產生的預定支出 | Q15「產生過就只能設結束月份」指的是刪除；ADR 0009 的「以現值更新」本身就預設週期項目的內容會改（例如保費調漲） | 產生過之後只能改結束月份：保費調漲就得結束舊項目、另建新項目，清單會越來越長 |
| D6 | J 段的設定維護補上週期項目：(a) 刪除帳戶、分類時，週期項目也算參照；(b) 支出主分類有週期項目（含子分類）時，**不能改成固定、貸款以外的性質** | 週期項目的不變條件是「分類必須是固定或貸款」（Q7）；不擋的話，改性質之後產生出來的預定支出會違反它，備份也還原不了 | 只擋「改成浮動」（J 的 D6 只考慮了預定支出，預定支出允許特別性質，週期項目不允許） |
| D7 | 產生與更新時，分類（自己或主分類）或帳戶已封存的週期項目**略過並回報**；建立與修改週期項目時**不**擋已封存的設定 | Q14 只規定產生時略過。建立時不擋與 J 的 D7 一致（後端不以封存為驗證條件，只有前端的選項排除封存項目） | 建立時就擋：與交易的處理方式不一致 |
| D8 | 「以現值更新」只回報**有改變**的預定支出；內容已經相同的不寫稽核、不列在結果中 | spec §4.2「每筆建立或修改的預定支出各一筆」稽核；沒有改變的寫一筆 before＝after 的稽核只是雜訊 | 每一筆都寫稽核 |
| D9 | 週期項目的 API 與預定支出一樣用 **xmin 樂觀並行**：PUT body 是 `{ version, input }`，DELETE 用 `?version=` | 與 `PlannedExpensesEndpoints` 的形狀一致，前端可以共用錯誤處理（409 → 重新載入） | 不做並行控制：兩台裝置同時修改時會無聲覆蓋 |
| D10 | 週期規則存成 `Frequency`（字串）＋`Months`（PostgreSQL `integer[]`）；每月時 `Months` 為空陣列；Domain 會排序、去重 | 比「12 位元遮罩」直觀，查資料庫時也讀得懂；Npgsql 原生支援陣列 | 位元遮罩；把每月表示成「1–12 全選」（UI 上分不出「每月」與「每年 12 個月」） |
| D11 | 同時按兩次「產生」造成的唯一索引衝突**不另外處理**（第二個請求會是 500）；前端在請求進行中停用按鈕 | 單一家庭使用，同時從兩台裝置對同一個月按產生的機率極低；正確處理要在 Api 層辨識 Npgsql 的 23505，把 provider 細節帶進例外處理 | 把 23505 對應成 409（列入「後續」） |
| D12 | 週期項目的清單 DTO **不帶**「是否已產生過」；刪除失敗時顯示後端的 422 訊息（「已產生過預定支出，不能刪除；請改設結束月份」） | 少一次查詢，且 DTO 同時用於稽核快照與備份，帶衍生欄位會讓還原時的一致性比對變複雜 | DTO 加 `isInUse`，前端事先隱藏刪除 |

---

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `src/SixJars.Domain/Planning/RecurringPlannedExpense.cs` | aggregate、`RecurrenceFrequency`、`IsDueIn`、`EnsureRemovable` |
| `src/SixJars.Domain/Planning/RecurringPlanner.cs` | 產生與以現值更新的規則；`RecurringSkipReason`、`RecurringSkip`、兩個結果型別 |
| `src/SixJars.Infrastructure/Persistence/RecurringPlannedExpenseConfiguration.cs` | EF 對應（`integer[]`、xmin） |
| `src/SixJars.Infrastructure/Persistence/Migrations/<ts>_AddRecurringPlannedExpenses.cs`（＋Designer、Snapshot 變更） | 新表、`PlannedExpenses.SourceId`、FK（Restrict）、部分唯一索引 |
| `src/SixJars.Application/Planning/RecurringPlannedExpenseInput.cs` | 輸入模型與 validator |
| `src/SixJars.Application/Planning/RecurringPlannedExpenseDto.cs` | 輸出 DTO |
| `src/SixJars.Application/Planning/RecurringPlannedExpenseCommands.cs` | List／Create／Update／Delete 四個 request＋handler |
| `src/SixJars.Application/Planning/GeneratePlannedExpenses.cs` | 產生 |
| `src/SixJars.Application/Planning/RefreshPlannedExpenses.cs` | 以現值更新 |
| `src/SixJars.Api/Endpoints/RecurringPlannedExpensesEndpoints.cs` | 週期項目的 REST 端點 |
| `tests/SixJars.Domain.Tests/Planning/RecurringPlannedExpenseTests.cs` | K1 |
| `tests/SixJars.Domain.Tests/Planning/RecurringPlannerTests.cs` | K3 |
| `tests/SixJars.Infrastructure.Tests/Persistence/RecurringPlannedExpensePersistenceTests.cs` | K2 |
| `tests/SixJars.Api.Tests/RecurringPlannedExpensesEndpointsTests.cs` | K4、K5 |
| `tests/SixJars.Api.Tests/PlannedExpenseGenerationEndpointsTests.cs` | K6、K7 |
| `web/src/app/core/api/planned-expense-api.ts`（＋spec） | 預定支出 API（含 generate、refresh、pay） |
| `web/src/app/core/api/recurring-planned-expense-api.ts`（＋spec） | 週期項目 API |
| `web/src/app/features/planned-expenses/planned-expense-rules.ts`（＋spec） | 分組、選項、摘要訊息、週期描述（純函式） |
| `web/src/app/features/planned-expenses/pay-dialog.ts`（＋spec） | 付款對話框 |
| `web/src/app/features/planned-expenses/planned-expense-dialog.ts`（＋spec） | 預定支出新增／修改對話框 |
| `web/src/app/features/planned-expenses/recurring-dialog.ts`（＋spec） | 週期項目新增／修改對話框 |
| `web/src/app/features/planned-expenses/planned-expenses.page.ts`、`.html`、`.scss`（＋spec） | 預定支出頁（兩個分頁） |
| `web/e2e/planned-expenses.spec.ts` | 產生 → 付款的 E2E |

### 修改

| 檔案 | 改動 |
|---|---|
| `src/SixJars.Domain/Common/Ids.cs` | `RecurringPlannedExpenseId` |
| `src/SixJars.Domain/Planning/PlannedExpense.cs` | `SourceId`、`Create(..., sourceId)`、D1、`RefreshFrom` |
| `src/SixJars.Domain/Books/Book.cs` | `IsCategoryArchived`；`ChangeExpenseNature` 加週期項目檢查（D6） |
| `src/SixJars.Infrastructure/Persistence/{ValueConverters,SixJarsDbContext,PlannedExpenseConfiguration}.cs` | converter、DbSet、FK 與唯一索引 |
| `src/SixJars.Application/Common/ISixJarsDbContext.cs` | `DbSet<RecurringPlannedExpense>` |
| `src/SixJars.Application/Auditing/AuditEntry.cs` | `AuditEntityTypes.RecurringPlannedExpense` |
| `src/SixJars.Application/Planning/PlannedExpenseDto.cs` | `SourceId`（K9） |
| `src/SixJars.Application/Books/{SettingReferences,UpdateSettings}.cs` | D6 |
| `src/SixJars.Application/Backup/{BackupDocument,ExportBackup,RestoreBackup}.cs` | v3（D3） |
| `src/SixJars.Api/Endpoints/PlannedExpensesEndpoints.cs` | `generate`、`refresh` |
| `src/SixJars.Api/Program.cs` | 掛上 `MapRecurringPlannedExpensesEndpoints` |
| `tests/SixJars.Domain.Tests/Books/BookSettingsTests.cs`、`tests/SixJars.Api.Tests/SettingsMaintenanceEndpointsTests.cs` | D6 |
| `tests/SixJars.Api.Tests/BackupExportTests.cs`、`tests/SixJars.Infrastructure.Tests/Backup/RestoreBackupTests.cs` | v3 |
| `web/src/app/core/api/dto.ts` | 預定支出、週期項目、產生／更新結果、付款 body |
| `web/src/app/core/errors/messages.ts` | `PLANNED_CONFLICT_MESSAGE` |
| `web/src/app/app.routes.ts`、`web/src/app/app.html`、`web/src/app/app.spec.ts` | 路由、導覽連結、連結清單斷言 |

### Task 相依順序

```
後端
K1 Domain 週期項目 ─► K2 持久化＋migration ─► K3 RecurringPlanner（Domain）
  K2 必須緊接在「PlannedExpense 加 SourceId」之後：EF 9 起 model 與 snapshot 不一致時
  Migrate 會擲 PendingModelChangesWarning。所以 SourceId 屬性放在 K2 一起加，K3 才加行為。
K3 ─┬► K4 週期項目 CRUD API ─► K5 刪除（被參照 422）
    ├► K6 generate API ─► K7 refresh API
    └► K8 設定維護補上週期項目（D6；只依賴 K2 的 DbSet 與 K1）
K5、K7、K8 ─► K9 PlannedExpenseDto.SourceId＋備份 v3
  （K4–K8 都改 Program.cs 或 PlannedExpensesEndpoints.cs 以外的不同檔案，但為了 commit 乾淨仍建議循序）
                                                                                     ║ 後端 checkpoint
前端
K10 DTO＋兩個 API ─► K11 planned-expense-rules ─┬► K12 PayDialog ─────────────┐
                                                ├► K13 PlannedExpenseDialog ──┼► K15 頁面「本月」分頁 ─► K16 「週期項目」分頁＋路由＋導覽 ─► K17 E2E
                                                └► K14 RecurringDialog ───────┘
  K12–K14 彼此獨立，可並行
                                                                                     ║ 前端 checkpoint
```

---

## Task K1：Domain 的 RecurringPlannedExpense

**Files:** Create: `src/SixJars.Domain/Planning/RecurringPlannedExpense.cs`、`tests/SixJars.Domain.Tests/Planning/RecurringPlannedExpenseTests.cs`／Modify: `src/SixJars.Domain/Common/Ids.cs`

週期項目的規則全部在這裡：只限固定與貸款性質、每年至少一個 1–12 的月份、結束月份不早於開始月份、`IsDueIn` 的區間是**閉區間**。容易錯的是：(1) 修改時要先驗證、再寫入，失敗時不能留下改了一半的狀態；(2) 子分類的性質跟著主分類，所以「固定支出／保險費」也合法；(3) EF 要能建構這個類別，但建構子參數 `months` 對不到任何屬性（屬性是 `Months`、欄位是 `_months`），所以另外提供一個私有的無參數建構子給 EF。

- [ ] **Step 1：寫失敗測試**

```csharp
// tests/SixJars.Domain.Tests/Planning/RecurringPlannedExpenseTests.cs
using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using Xunit;

namespace SixJars.Domain.Tests.Planning;

public class RecurringPlannedExpenseTests
{
    private readonly Book _book = new("測試帳本", new DateOnly(2025, 12, 30));
    private readonly Account _bank;
    private readonly Category _rent;
    private readonly Category _loan;

    public RecurringPlannedExpenseTests()
    {
        _bank = _book.AddAccount("銀行", AccountType.Bank);
        _rent = _book.AddExpenseCategory("房租", ExpenseNature.Fixed);
        _loan = _book.AddExpenseCategory("貸款支出", ExpenseNature.Loan);
        _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        _book.AddExpenseCategory("禮金", ExpenseNature.Special);
    }

    private static BudgetMonth M(int key) => BudgetMonth.FromKey(key);

    private RecurringPlannedExpense Monthly(BudgetMonth start, BudgetMonth? end = null) =>
        RecurringPlannedExpense.Create(_book, _rent.Id, _bank.Id, -15000m, "房租", RecurrenceFrequency.Monthly, [], start, end);

    [Fact]
    public void Monthly_is_due_in_every_month_of_the_closed_range()
    {
        var rent = Monthly(M(202602), M(202605));

        rent.IsDueIn(M(202601)).Should().BeFalse();
        rent.IsDueIn(M(202602)).Should().BeTrue();
        rent.IsDueIn(M(202605)).Should().BeTrue();
        rent.IsDueIn(M(202606)).Should().BeFalse();
    }

    [Fact]
    public void Without_end_month_it_is_due_forever_after_start()
    {
        Monthly(M(202602)).IsDueIn(M(203012)).Should().BeTrue();
    }

    [Fact]
    public void Yearly_is_due_only_in_listed_months_and_months_are_normalized()
    {
        var insurance = RecurringPlannedExpense.Create(
            _book, _rent.Id, null, -3000m, null, RecurrenceFrequency.Yearly, [7, 1, 7], M(202601), null);

        insurance.Months.Should().Equal(1, 7);   // 排序、去重
        insurance.IsDueIn(M(202601)).Should().BeTrue();
        insurance.IsDueIn(M(202602)).Should().BeFalse();
        insurance.IsDueIn(M(202707)).Should().BeTrue();
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { 13 })]
    public void Yearly_requires_at_least_one_month_between_1_and_12(int[] months)
    {
        var act = () => RecurringPlannedExpense.Create(
            _book, _rent.Id, null, -3000m, null, RecurrenceFrequency.Yearly, months, M(202601), null);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.RuleCode);
    }

    [Fact]
    public void Monthly_does_not_accept_months()
    {
        var act = () => RecurringPlannedExpense.Create(
            _book, _rent.Id, null, -3000m, null, RecurrenceFrequency.Monthly, [1], M(202601), null);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("飲食")]
    [InlineData("禮金")]
    public void Only_fixed_or_loan_categories_are_allowed(string name)
    {
        var category = _book.FindCategory(name)!;

        var act = () => RecurringPlannedExpense.Create(
            _book, category.Id, null, -100m, null, RecurrenceFrequency.Monthly, [], M(202601), null);

        act.Should().Throw<DomainException>().WithMessage($"*{name}*");
    }

    [Fact]
    public void Sub_category_of_a_fixed_main_category_is_allowed()
    {
        var insurance = _book.AddSubCategory(_rent.Id, "管理費");

        var act = () => RecurringPlannedExpense.Create(
            _book, insurance.Id, null, -100m, null, RecurrenceFrequency.Monthly, [], M(202601), null);

        act.Should().NotThrow();
    }

    [Fact]
    public void End_month_before_start_month_is_rejected()
    {
        var act = () => Monthly(M(202605), M(202604));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Unknown_account_is_rejected()
    {
        var act = () => RecurringPlannedExpense.Create(
            _book, _rent.Id, AccountId.New(), -100m, null, RecurrenceFrequency.Monthly, [], M(202601), null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Update_replaces_every_field_with_the_same_rules()
    {
        var item = Monthly(M(202601));

        item.Update(_book, _loan.Id, null, -20000m, "房貸", RecurrenceFrequency.Yearly, [3], M(202603), M(202612));

        item.CategoryId.Should().Be(_loan.Id);
        item.AccountId.Should().BeNull();
        item.DefaultAmount.Should().Be(-20000m);
        item.Note.Should().Be("房貸");
        item.Frequency.Should().Be(RecurrenceFrequency.Yearly);
        item.Months.Should().Equal(3);
        (item.StartMonth, item.EndMonth).Should().Be((M(202603), M(202612)));
    }

    [Fact]
    public void Failed_update_leaves_the_item_unchanged()
    {
        var item = Monthly(M(202601));
        var food = _book.FindCategory("飲食")!;

        var act = () => item.Update(_book, food.Id, null, -1m, "x", RecurrenceFrequency.Monthly, [], M(202601), null);

        act.Should().Throw<DomainException>();
        item.CategoryId.Should().Be(_rent.Id);
        item.DefaultAmount.Should().Be(-15000m);
    }

    [Fact]
    public void Cannot_be_removed_after_it_has_generated_planned_expenses()
    {
        var item = Monthly(M(202601));

        item.Invoking(i => i.EnsureRemovable(hasGenerated: false)).Should().NotThrow();
        item.Invoking(i => i.EnsureRemovable(hasGenerated: true))
            .Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.InUseCode);
    }
}
```

- [ ] **Step 2：跑測試確認失敗**

`dotnet test --project tests/SixJars.Domain.Tests --filter-class "*RecurringPlannedExpenseTests*"`
Expected：編譯失敗，`RecurringPlannedExpense`、`RecurrenceFrequency`、`RecurringPlannedExpenseId` 不存在。

- [ ] **Step 3：最小實作**

```diff
--- a/src/SixJars.Domain/Common/Ids.cs
+++ b/src/SixJars.Domain/Common/Ids.cs
@@
 public readonly record struct PlannedExpenseId(Guid Value) { public static PlannedExpenseId New() => new(Guid.CreateVersion7()); }
+public readonly record struct RecurringPlannedExpenseId(Guid Value) { public static RecurringPlannedExpenseId New() => new(Guid.CreateVersion7()); }
```

```csharp
// src/SixJars.Domain/Planning/RecurringPlannedExpense.cs
using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Domain.Planning;

public enum RecurrenceFrequency
{
    Monthly,
    Yearly,
}

/// <summary>
/// 週期預定支出（CONTEXT.md、spec §4.1）：每月，或每年的指定月份，在 [StartMonth, EndMonth] 之間適用。
/// 不會自動變成預定支出；使用者對某月明確「產生」時，由 <see cref="RecurringPlanner"/> 建立（ADR 0009）。
/// </summary>
public sealed class RecurringPlannedExpense
{
    private int[] _months = [];

    // EF Core 用；建構子參數對不到 _months，所以不用建構子綁定。
    private RecurringPlannedExpense()
    {
    }

    public RecurringPlannedExpenseId Id { get; private set; }
    public BookId BookId { get; private set; }
    /// <summary>只限固定或貸款性質（Q7）。</summary>
    public CategoryId CategoryId { get; private set; }
    public AccountId? AccountId { get; private set; }
    /// <summary>預設金額，沿用支出的符號慣例（負數）。</summary>
    public decimal DefaultAmount { get; private set; }
    public string? Note { get; private set; }
    public RecurrenceFrequency Frequency { get; private set; }
    /// <summary>每年的適用月份（1–12，已排序、去重）；每月時為空。</summary>
    public IReadOnlyList<int> Months => _months;
    public BudgetMonth StartMonth { get; private set; }
    /// <summary>最後一個適用的月份（含）；null 表示沒有結束。</summary>
    public BudgetMonth? EndMonth { get; private set; }

    /// <param name="id">只有還原備份時指定；一般新增時省略。</param>
    public static RecurringPlannedExpense Create(
        Book book, CategoryId categoryId, AccountId? accountId, decimal defaultAmount, string? note,
        RecurrenceFrequency frequency, IEnumerable<int> months, BudgetMonth startMonth, BudgetMonth? endMonth,
        RecurringPlannedExpenseId? id = null)
    {
        var item = new RecurringPlannedExpense { Id = id ?? RecurringPlannedExpenseId.New(), BookId = book.Id };
        item.Update(book, categoryId, accountId, defaultAmount, note, frequency, months, startMonth, endMonth);
        return item;
    }

    /// <summary>修改不回寫已產生的預定支出；要套用到某月請用「以現值更新」（ADR 0009）。</summary>
    public void Update(
        Book book, CategoryId categoryId, AccountId? accountId, decimal defaultAmount, string? note,
        RecurrenceFrequency frequency, IEnumerable<int> months, BudgetMonth startMonth, BudgetMonth? endMonth)
    {
        if (book.Id != BookId)
        {
            throw new DomainException("帳本與週期預定支出不屬於同一本帳本。");
        }

        // 先全部驗證完再寫入，失敗時不留下改了一半的狀態。
        var category = book.GetCategory(categoryId);
        if (category.Kind != CategoryKind.Expense || category.Nature is not (ExpenseNature.Fixed or ExpenseNature.Loan))
        {
            throw new DomainException($"週期預定支出只限固定、貸款支出：「{category.Name}」。");
        }

        if (accountId is { } account)
        {
            book.GetAccount(account);
        }

        var normalized = NormalizeMonths(frequency, months);
        if (endMonth is { } end && end < startMonth)
        {
            throw new DomainException("結束月份不能早於開始月份。");
        }

        CategoryId = categoryId;
        AccountId = accountId;
        DefaultAmount = defaultAmount;
        Note = note;
        Frequency = frequency;
        _months = normalized;
        StartMonth = startMonth;
        EndMonth = endMonth;
    }

    public bool IsDueIn(BudgetMonth month) =>
        month >= StartMonth
        && (EndMonth is null || month <= EndMonth.Value)
        && (Frequency == RecurrenceFrequency.Monthly || _months.Contains(month.Month));

    /// <param name="hasGenerated">由呼叫端查詢：是否有任何預定支出（含已刪除）以它為來源。</param>
    public void EnsureRemovable(bool hasGenerated)
    {
        if (hasGenerated)
        {
            throw new DomainException("這個週期項目已產生過預定支出，不能刪除；請改設結束月份。", DomainException.InUseCode);
        }
    }

    private static int[] NormalizeMonths(RecurrenceFrequency frequency, IEnumerable<int> months)
    {
        var normalized = months.Distinct().Order().ToArray();
        if (frequency == RecurrenceFrequency.Monthly)
        {
            return normalized.Length == 0 ? [] : throw new DomainException("每月的週期項目不指定月份。");
        }

        if (normalized.Length == 0 || normalized.Any(m => m is < 1 or > 12))
        {
            throw new DomainException("每年的週期項目至少要指定一個 1 到 12 的月份。");
        }

        return normalized;
    }
}
```

變異檢查：把 `IsDueIn` 的 `<=` 改成 `<`，`Monthly_is_due_in_every_month_of_the_closed_range` 必須變紅；拿掉 `Failed_update` 前的「先驗證」順序（把賦值移到驗證之前）也必須變紅。

- [ ] **Step 4：跑單檔測試** — Expected：14 passed（含 Theory 展開）。
- [ ] **Step 5：跑全部測試** — Expected：總計約 **440**，失敗 0。
- [ ] **Step 6：Commit**

```bash
git add src/SixJars.Domain/Common/Ids.cs src/SixJars.Domain/Planning/RecurringPlannedExpense.cs tests/SixJars.Domain.Tests/Planning/RecurringPlannedExpenseTests.cs
git commit -m "feat(domain): 週期預定支出 aggregate

每月或每年指定月份、起訖月份閉區間、只限固定與貸款性質；
產生過預定支出後不能刪除（ADR 0009）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task K2：持久化、PlannedExpense.SourceId 與部分唯一索引

**Files:** Create: `src/SixJars.Infrastructure/Persistence/RecurringPlannedExpenseConfiguration.cs`、migration、`tests/SixJars.Infrastructure.Tests/Persistence/RecurringPlannedExpensePersistenceTests.cs`／Modify: `PlannedExpense.cs`、`ValueConverters.cs`、`SixJarsDbContext.cs`、`ISixJarsDbContext.cs`、`PlannedExpenseConfiguration.cs`

這個 Task 把 ADR 0009 的冪等性落到資料庫：`(SourceId, BudgetMonth)` 唯一、條件只有 `SourceId IS NOT NULL`，**不能**加上 `DeletedAt IS NULL`。最容易錯的就是「順手」把已刪除的排除掉，所以有一條測試專門驗證「已刪除的那筆仍然擋住」。`SourceId` 屬性放在這個 Task 加（不放 K3），因為只要 Domain 多了屬性，model 就和 snapshot 不一致，所有用資料庫的測試都會因為 `PendingModelChangesWarning` 失敗，直到 migration 產生為止。

- [ ] **Step 1：寫失敗測試**

```csharp
// tests/SixJars.Infrastructure.Tests/Persistence/RecurringPlannedExpensePersistenceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Persistence;

public class RecurringPlannedExpensePersistenceTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly BudgetMonth April = BudgetMonth.FromKey(202604);

    [Fact]
    public async Task Recurring_item_and_source_round_trip()
    {
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        var (book, insurance) = await SeedAsync(createContext);
        var planned = PlannedExpense.Create(book, April, insurance.CategoryId, null, -3000m, null, sourceId: insurance.Id);
        await using (var db = createContext())
        {
            db.PlannedExpenses.Add(planned);
            await db.SaveChangesAsync(Ct);
        }

        await using var readDb = createContext();
        var item = await readDb.RecurringPlannedExpenses.SingleAsync(Ct);
        item.Frequency.Should().Be(RecurrenceFrequency.Yearly);
        item.Months.Should().Equal(1, 7);
        (item.StartMonth, item.EndMonth).Should().Be((BudgetMonth.FromKey(202601), (BudgetMonth?)null));
        (await readDb.PlannedExpenses.SingleAsync(Ct)).SourceId.Should().Be(insurance.Id);
    }

    [Fact]
    public async Task Same_source_and_month_is_rejected_even_when_the_first_is_soft_deleted()
    {
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        var (book, insurance) = await SeedAsync(createContext);
        var first = PlannedExpense.Create(book, April, insurance.CategoryId, null, -3000m, null, sourceId: insurance.Id);
        first.Delete(DateTimeOffset.UtcNow);
        await using (var db = createContext())
        {
            db.PlannedExpenses.Add(first);
            await db.SaveChangesAsync(Ct);
        }

        await using var again = createContext();
        again.PlannedExpenses.Add(PlannedExpense.Create(book, April, insurance.CategoryId, null, -3000m, null, sourceId: insurance.Id));
        var act = () => again.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Planned_expenses_without_source_are_not_constrained()
    {
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        var (book, insurance) = await SeedAsync(createContext);
        await using var db = createContext();
        db.PlannedExpenses.Add(PlannedExpense.Create(book, April, insurance.CategoryId, null, -1m, null));
        db.PlannedExpenses.Add(PlannedExpense.Create(book, April, insurance.CategoryId, null, -2m, null));

        var act = () => db.SaveChangesAsync(Ct);

        await act.Should().NotThrowAsync();
    }

    private async Task<(Book Book, RecurringPlannedExpense Insurance)> SeedAsync(Func<SixJarsDbContext> createContext)
    {
        var book = new Book("測試帳本", new DateOnly(2025, 12, 30));
        var fixedExpense = book.AddExpenseCategory("固定支出", ExpenseNature.Fixed);
        var insurance = RecurringPlannedExpense.Create(
            book, fixedExpense.Id, null, -3000m, "保險", RecurrenceFrequency.Yearly, [7, 1], BudgetMonth.FromKey(202601), null);
        await using var db = createContext();
        db.Books.Add(book);
        db.RecurringPlannedExpenses.Add(insurance);
        await db.SaveChangesAsync(Ct);
        return (book, insurance);
    }
}
```

> `SixJarsDbContext` 的命名空間在 Step 1 前先確認（`src/SixJars.Infrastructure/Persistence/SixJarsDbContext.cs`），補上對應的 `using`。

- [ ] **Step 2：跑測試確認失敗** — Expected：編譯失敗，`PlannedExpense.Create` 沒有 `sourceId` 參數、`RecurringPlannedExpenses` 不存在。

- [ ] **Step 3：最小實作**

```diff
--- a/src/SixJars.Domain/Planning/PlannedExpense.cs
+++ b/src/SixJars.Domain/Planning/PlannedExpense.cs
@@
     public TransactionId? PaidTransactionId { get; private set; }
     public bool IsPaid => PaidTransactionId is not null;
+    /// <summary>由哪個週期項目產生；手動新增的為 null。<c>(SourceId, BudgetMonth)</c> 唯一，含已刪除（ADR 0009）。</summary>
+    public RecurringPlannedExpenseId? SourceId { get; private set; }
@@
-    /// <param name="id">只有還原備份（T42）時指定，保留原本的 Id；一般新增時省略，自動產生。驗證規則完全相同。</param>
+    /// <param name="id">只有還原備份（T42）時指定，保留原本的 Id；一般新增時省略，自動產生。驗證規則完全相同。</param>
+    /// <param name="sourceId">由週期項目產生時的來源；產生由 <see cref="RecurringPlanner"/> 負責，這裡只為了還原備份與測試開放。</param>
     public static PlannedExpense Create(
         Book book, BudgetMonth budgetMonth, CategoryId categoryId, AccountId? accountId, decimal estimatedAmount, string? note = null,
-        PlannedExpenseId? id = null)
+        PlannedExpenseId? id = null, RecurringPlannedExpenseId? sourceId = null)
     {
         Validate(book, categoryId, accountId);
         var planned = new PlannedExpense(book.Id, budgetMonth, categoryId, accountId, estimatedAmount, note);
         if (id is { } fixedId)
         {
             planned.Id = fixedId;
         }
 
+        planned.SourceId = sourceId;
         return planned;
     }
```

```diff
--- a/src/SixJars.Infrastructure/Persistence/ValueConverters.cs
+++ b/src/SixJars.Infrastructure/Persistence/ValueConverters.cs
@@
 internal sealed class PlannedExpenseIdConverter() : ValueConverter<PlannedExpenseId, Guid>(id => id.Value, value => new PlannedExpenseId(value));
+internal sealed class RecurringPlannedExpenseIdConverter()
+    : ValueConverter<RecurringPlannedExpenseId, Guid>(id => id.Value, value => new RecurringPlannedExpenseId(value));
```

```diff
--- a/src/SixJars.Infrastructure/Persistence/SixJarsDbContext.cs
+++ b/src/SixJars.Infrastructure/Persistence/SixJarsDbContext.cs
@@
     public DbSet<PlannedExpense> PlannedExpenses => Set<PlannedExpense>();
+    public DbSet<RecurringPlannedExpense> RecurringPlannedExpenses => Set<RecurringPlannedExpense>();
@@
         configurationBuilder.Properties<PlannedExpenseId>().HaveConversion<PlannedExpenseIdConverter>();
+        configurationBuilder.Properties<RecurringPlannedExpenseId>().HaveConversion<RecurringPlannedExpenseIdConverter>();
```

```diff
--- a/src/SixJars.Application/Common/ISixJarsDbContext.cs
+++ b/src/SixJars.Application/Common/ISixJarsDbContext.cs
@@
     DbSet<PlannedExpense> PlannedExpenses { get; }
+
+    /// <summary>週期預定支出；硬刪除（P4 K plan D4），沒有 query filter。</summary>
+    DbSet<RecurringPlannedExpense> RecurringPlannedExpenses { get; }
```

```csharp
// src/SixJars.Infrastructure/Persistence/RecurringPlannedExpenseConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SixJars.Domain.Planning;

namespace SixJars.Infrastructure.Persistence;

internal sealed class RecurringPlannedExpenseConfiguration : IEntityTypeConfiguration<RecurringPlannedExpense>
{
    public void Configure(EntityTypeBuilder<RecurringPlannedExpense> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        // 樂觀並行版本，同 PlannedExpenseConfiguration（P4 K plan D9）。
        builder.Property<uint>("xmin").IsRowVersion();
        builder.Property(r => r.Frequency).HasConversion<string>().HasMaxLength(16);
        // 週期月份存成 integer[]；公開的 Months 是唯讀檢視，對應到私有欄位（P4 K plan D10）。
        builder.Ignore(r => r.Months);
        builder.Property<int[]>("_months").HasColumnName("Months");
        builder.Property(r => r.Note).HasMaxLength(500);
        builder.HasIndex(r => r.BookId);
    }
}
```

```diff
--- a/src/SixJars.Infrastructure/Persistence/PlannedExpenseConfiguration.cs
+++ b/src/SixJars.Infrastructure/Persistence/PlannedExpenseConfiguration.cs
@@
         builder.HasQueryFilter(p => p.DeletedAt == null);
         builder.HasIndex(p => new { p.BookId, p.BudgetMonth });
+        // ADR 0009：同一來源同一月只產生一次，「包含已刪除」，所以條件只有 SourceId 不為 null。
+        builder.HasIndex(p => new { p.SourceId, p.BudgetMonth }).IsUnique().HasFilter("\"SourceId\" IS NOT NULL");
+        // 被參照的週期項目不能刪除（Domain 先擋，FK 是最後一道防線）。
+        builder.HasOne<RecurringPlannedExpense>().WithMany().HasForeignKey(p => p.SourceId).OnDelete(DeleteBehavior.Restrict);
```

產生 migration：

```bash
dotnet ef migrations add AddRecurringPlannedExpenses --project src/SixJars.Infrastructure --startup-project src/SixJars.Infrastructure --output-dir Persistence/Migrations
```

檢查產生的 migration：(a) `RecurringPlannedExpenses` 表的 `Months` 是 `integer[]`；(b) 唯一索引的 `filter` 是 `"SourceId" IS NOT NULL`，**沒有** `DeletedAt`；(c) FK 是 `ReferentialAction.Restrict`；(d) 沒有對既有資料表做其他變更。

變異檢查：把 `HasFilter` 改成 `"SourceId" IS NOT NULL AND "DeletedAt" IS NULL`（重新產生 migration 前的暫時修改即可，或直接改 migration 的 filter 字串），`Same_source_and_month_is_rejected_even_when_the_first_is_soft_deleted` 必須變紅；還原。

- [ ] **Step 4：跑單檔測試** — Expected：3 passed。
- [ ] **Step 5：跑全部測試** — Expected：總計約 **443**，失敗 0（`dotnet build` 0 warning）。
- [ ] **Step 6：Commit**

```bash
git add src/SixJars.Domain/Planning/PlannedExpense.cs src/SixJars.Application/Common/ISixJarsDbContext.cs src/SixJars.Infrastructure/Persistence/ValueConverters.cs src/SixJars.Infrastructure/Persistence/SixJarsDbContext.cs src/SixJars.Infrastructure/Persistence/PlannedExpenseConfiguration.cs src/SixJars.Infrastructure/Persistence/RecurringPlannedExpenseConfiguration.cs src/SixJars.Infrastructure/Persistence/Migrations/ tests/SixJars.Infrastructure.Tests/Persistence/RecurringPlannedExpensePersistenceTests.cs
git commit -m "feat(infra): 週期預定支出資料表與預定支出來源的唯一索引

(SourceId, BudgetMonth) 部分唯一索引只排除 SourceId 為 null，
已刪除的仍然擋住重新產生（ADR 0009）；FK 為 Restrict。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

> `git add src/SixJars.Infrastructure/Persistence/Migrations/` 會加入新 migration 的兩個檔案與 Snapshot 的變更；commit 前用 `git status` 確認只有這三個檔案。

---

## Task K3：RecurringPlanner（產生與以現值更新的規則）

**Files:** Create: `src/SixJars.Domain/Planning/RecurringPlanner.cs`、`tests/SixJars.Domain.Tests/Planning/RecurringPlannerTests.cs`／Modify: `PlannedExpense.cs`（D1、`RefreshFrom`）、`Book.cs`（`IsCategoryArchived`）

產生與更新的判斷順序是這個 Task 的重點，也是 API 結果裡 `reason` 的來源：

- 產生：不適用的月份**不列出**；適用的依序檢查「該月已有同來源（含已刪除）→ `AlreadyGenerated`」「分類（自己或主分類）已封存 → `CategoryArchived`」「帳戶已封存 → `AccountArchived`」，都沒有才建立。`AlreadyGenerated` 排第一：已經產生過的，即使後來分類被封存，也應該回報「已產生」而不是「封存」。
- 更新：只看該月**未付、未刪除、有來源**的預定支出。來源當月已不適用 → `NotDue`；分類或帳戶已封存 → 同上；否則以現值更新，**有改變才列入 `Updated`**（D8）。

- [ ] **Step 1：寫失敗測試**

```csharp
// tests/SixJars.Domain.Tests/Planning/RecurringPlannerTests.cs
using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;
using Xunit;

namespace SixJars.Domain.Tests.Planning;

public class RecurringPlannerTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 7, 1, 0, 0, TimeSpan.Zero);
    private static readonly BudgetMonth April = BudgetMonth.FromKey(202604);
    private readonly Book _book = new("測試帳本", new DateOnly(2025, 12, 30));
    private readonly Account _bank;
    private readonly Category _fixed;
    private readonly Category _insurance;

    public RecurringPlannerTests()
    {
        _bank = _book.AddAccount("銀行", AccountType.Bank);
        _fixed = _book.AddExpenseCategory("固定支出", ExpenseNature.Fixed);
        _insurance = _book.AddSubCategory(_fixed.Id, "保險費");
    }

    private RecurringPlannedExpense Item(CategoryId? category = null, AccountId? account = null, decimal amount = -1000m,
        RecurrenceFrequency frequency = RecurrenceFrequency.Monthly, int[]? months = null) =>
        RecurringPlannedExpense.Create(_book, category ?? _insurance.Id, account, amount, "保險", frequency, months ?? [],
            BudgetMonth.FromKey(202601), null);

    [Fact]
    public void Generate_creates_due_items_with_source_and_skips_items_not_due()
    {
        var monthly = Item(account: _bank.Id);
        var july = Item(frequency: RecurrenceFrequency.Yearly, months: [7]);

        var result = RecurringPlanner.Generate(_book, April, [monthly, july], alreadyGenerated: new HashSet<RecurringPlannedExpenseId>());

        var created = result.Created.Should().ContainSingle().Subject;
        created.SourceId.Should().Be(monthly.Id);
        (created.BudgetMonth, created.CategoryId, created.AccountId, created.EstimatedAmount, created.Note)
            .Should().Be((April, _insurance.Id, (AccountId?)_bank.Id, -1000m, "保險"));
        result.Skipped.Should().BeEmpty();   // 7 月才適用的不列出
    }

    [Fact]
    public void Generate_reports_already_generated_before_archived()
    {
        var item = Item();
        _book.ArchiveCategory(_insurance.Id, At);

        var result = RecurringPlanner.Generate(_book, April, [item], new HashSet<RecurringPlannedExpenseId> { item.Id });

        result.Created.Should().BeEmpty();
        result.Skipped.Should().Equal(new RecurringSkip(item.Id, null, RecurringSkipReason.AlreadyGenerated));
    }

    [Fact]
    public void Generate_skips_when_the_main_category_is_archived()
    {
        var item = Item();
        _book.ArchiveCategory(_fixed.Id, At);   // 封存主分類，子分類本身未封存

        var result = RecurringPlanner.Generate(_book, April, [item], new HashSet<RecurringPlannedExpenseId>());

        result.Skipped.Should().Equal(new RecurringSkip(item.Id, null, RecurringSkipReason.CategoryArchived));
    }

    [Fact]
    public void Generate_skips_when_the_account_is_archived()
    {
        var item = Item(account: _bank.Id);
        _book.ArchiveAccount(_bank.Id, balance: 0m, At);

        var result = RecurringPlanner.Generate(_book, April, [item], new HashSet<RecurringPlannedExpenseId>());

        result.Skipped.Should().Equal(new RecurringSkip(item.Id, null, RecurringSkipReason.AccountArchived));
    }

    [Fact]
    public void Refresh_updates_unpaid_generated_items_to_current_values_and_reports_only_changes()
    {
        var changed = Item(amount: -1000m);
        var unchanged = Item(amount: -500m);
        var planned = Generated(changed, unchanged);
        changed.Update(_book, _insurance.Id, _bank.Id, -1200m, "保費調漲", RecurrenceFrequency.Monthly, [], BudgetMonth.FromKey(202601), null);

        var result = RecurringPlanner.Refresh(_book, April, [changed, unchanged], planned);

        var updated = result.Updated.Should().ContainSingle().Subject;
        updated.SourceId.Should().Be(changed.Id);
        (updated.AccountId, updated.EstimatedAmount, updated.Note).Should().Be(((AccountId?)_bank.Id, -1200m, "保費調漲"));
        result.Skipped.Should().BeEmpty();
    }

    [Fact]
    public void Refresh_ignores_paid_deleted_and_manual_items()
    {
        var item = Item();
        var paid = Generated(item).Single();
        paid.MarkPaid(new TransactionFactory(_book).Expense(new DateOnly(2026, 4, 5), _bank.Id, _insurance.Id, -1000m, null, April));
        var manual = PlannedExpense.Create(_book, April, _insurance.Id, null, -1m, null);
        item.Update(_book, _insurance.Id, null, -9999m, null, RecurrenceFrequency.Monthly, [], BudgetMonth.FromKey(202601), null);

        var result = RecurringPlanner.Refresh(_book, April, [item], [paid, manual]);

        result.Updated.Should().BeEmpty();
        result.Skipped.Should().BeEmpty();
        paid.EstimatedAmount.Should().Be(-1000m);
    }

    [Fact]
    public void Refresh_reports_items_whose_source_is_no_longer_due()
    {
        var item = Item();
        var planned = Generated(item).Single();
        item.Update(_book, _insurance.Id, null, -1000m, "保險", RecurrenceFrequency.Monthly, [], BudgetMonth.FromKey(202601), BudgetMonth.FromKey(202603));

        var result = RecurringPlanner.Refresh(_book, April, [item], [planned]);

        result.Skipped.Should().Equal(new RecurringSkip(item.Id, planned.Id, RecurringSkipReason.NotDue));
    }

    [Fact]
    public void Generated_planned_expense_cannot_move_to_another_month()
    {
        var planned = Generated(Item()).Single();

        var act = () => planned.Update(_book, BudgetMonth.FromKey(202605), _insurance.Id, null, -1000m, null);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.RuleCode);
    }

    [Fact]
    public void Generated_planned_expense_can_still_be_edited_within_its_month()
    {
        var planned = Generated(Item()).Single();

        planned.Update(_book, April, _insurance.Id, null, -800m, "改金額");

        planned.EstimatedAmount.Should().Be(-800m);
    }

    private List<PlannedExpense> Generated(params RecurringPlannedExpense[] items) =>
        [.. RecurringPlanner.Generate(_book, April, items, new HashSet<RecurringPlannedExpenseId>()).Created];
}
```

> `TransactionFactory.Expense` 的參數順序以 `PayPlannedExpense.cs:93` 為準：`(date, payer, categoryId, amount, note, budgetMonth)`。

- [ ] **Step 2：跑測試確認失敗** — Expected：編譯失敗，`RecurringPlanner`、`RecurringSkip`、`RecurringSkipReason` 不存在。

- [ ] **Step 3：最小實作**

```diff
--- a/src/SixJars.Domain/Books/Book.cs
+++ b/src/SixJars.Domain/Books/Book.cs
@@
     public Category GetCategory(CategoryId id) =>
         _categories.Find(c => c.Id == id) ?? throw new DomainException($"找不到分類 {id.Value}。");
+
+    /// <summary>分類自己或它的主分類已封存（spec §3.1：子分類是否可選 = 自己與主分類都未封存）。</summary>
+    public bool IsCategoryArchived(CategoryId id)
+    {
+        var category = GetCategory(id);
+        return category.IsArchived || (category.ParentId is { } parentId && GetCategory(parentId).IsArchived);
+    }
```

```diff
--- a/src/SixJars.Domain/Planning/PlannedExpense.cs
+++ b/src/SixJars.Domain/Planning/PlannedExpense.cs
@@ public void Update(...)
         if (book.Id != BookId)
         {
             throw new DomainException("帳本與預定支出不屬於同一本帳本。");
         }
 
+        // (SourceId, BudgetMonth) 唯一：搬月份會讓原月份可以再產生一次，或撞上目標月份的同來源（P4 K plan D1）。
+        if (SourceId is not null && budgetMonth != BudgetMonth)
+        {
+            throw new DomainException("由週期項目產生的預定支出不能改歸屬月份；請刪除後手動新增。");
+        }
+
         Validate(book, categoryId, accountId);
@@
+    /// <summary>以來源週期項目的現值更新（ADR 0009「以現值更新」）；回傳內容是否有改變。只由 <see cref="RecurringPlanner"/> 呼叫。</summary>
+    internal bool RefreshFrom(Book book, RecurringPlannedExpense source)
+    {
+        var changed = CategoryId != source.CategoryId || AccountId != source.AccountId
+            || EstimatedAmount != source.DefaultAmount || Note != source.Note;
+        if (changed)
+        {
+            Update(book, BudgetMonth, source.CategoryId, source.AccountId, source.DefaultAmount, source.Note);
+        }
+
+        return changed;
+    }
```

```csharp
// src/SixJars.Domain/Planning/RecurringPlanner.cs
using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Domain.Planning;

public enum RecurringSkipReason
{
    AlreadyGenerated,
    CategoryArchived,
    AccountArchived,
    NotDue,
}

/// <param name="PlannedExpenseId">以現值更新時被略過的預定支出；產生時為 null。</param>
public sealed record RecurringSkip(RecurringPlannedExpenseId RecurringId, PlannedExpenseId? PlannedExpenseId, RecurringSkipReason Reason);

public sealed record GenerationResult(IReadOnlyList<PlannedExpense> Created, IReadOnlyList<RecurringSkip> Skipped);

public sealed record RefreshResult(IReadOnlyList<PlannedExpense> Updated, IReadOnlyList<RecurringSkip> Skipped);

/// <summary>週期項目與某月預定支出之間的規則（spec §4.2、ADR 0009）；不查詢資料庫，資料由呼叫端備妥。</summary>
public static class RecurringPlanner
{
    /// <param name="alreadyGenerated">該月已有預定支出（<b>含已刪除</b>）的來源。</param>
    public static GenerationResult Generate(
        Book book, BudgetMonth month, IEnumerable<RecurringPlannedExpense> items, IReadOnlySet<RecurringPlannedExpenseId> alreadyGenerated)
    {
        var created = new List<PlannedExpense>();
        var skipped = new List<RecurringSkip>();
        foreach (var item in items.Where(i => i.IsDueIn(month)))
        {
            // 已產生過的優先回報，即使之後分類被封存。
            var reason = alreadyGenerated.Contains(item.Id) ? RecurringSkipReason.AlreadyGenerated : UnusableReason(book, item);
            if (reason is { } r)
            {
                skipped.Add(new RecurringSkip(item.Id, null, r));
                continue;
            }

            created.Add(PlannedExpense.Create(book, month, item.CategoryId, item.AccountId, item.DefaultAmount, item.Note, sourceId: item.Id));
        }

        return new GenerationResult(created, skipped);
    }

    /// <param name="planned">該月的預定支出；只處理未付、未刪除、有來源的，其他的忽略。</param>
    public static RefreshResult Refresh(
        Book book, BudgetMonth month, IEnumerable<RecurringPlannedExpense> items, IEnumerable<PlannedExpense> planned)
    {
        var sources = items.ToDictionary(i => i.Id);
        var updated = new List<PlannedExpense>();
        var skipped = new List<RecurringSkip>();
        foreach (var expense in planned.Where(p => p.BudgetMonth == month && !p.IsPaid && !p.IsDeleted && p.SourceId is not null))
        {
            var source = sources[expense.SourceId!.Value];
            var reason = source.IsDueIn(month) ? UnusableReason(book, source) : RecurringSkipReason.NotDue;
            if (reason is { } r)
            {
                skipped.Add(new RecurringSkip(source.Id, expense.Id, r));
            }
            else if (expense.RefreshFrom(book, source))
            {
                updated.Add(expense);
            }
        }

        return new RefreshResult(updated, skipped);
    }

    private static RecurringSkipReason? UnusableReason(Book book, RecurringPlannedExpense item)
    {
        if (book.IsCategoryArchived(item.CategoryId))
        {
            return RecurringSkipReason.CategoryArchived;
        }

        return item.AccountId is { } accountId && book.GetAccount(accountId).IsArchived ? RecurringSkipReason.AccountArchived : null;
    }
}
```

變異檢查：(a) 把 `Generate` 的判斷順序改成先 `UnusableReason`，`Generate_reports_already_generated_before_archived` 必須變紅；(b) 拿掉 `RefreshFrom` 的 `changed` 判斷（一律回傳 true），`..._reports_only_changes` 必須變紅；(c) 拿掉 D1 的檢查，`Generated_planned_expense_cannot_move_to_another_month` 必須變紅。

- [ ] **Step 4：跑單檔測試** — Expected：9 passed。另外跑 `--filter-class "*PlannedExpense*"`，確認既有的預定支出 Domain 測試沒有被 D1 影響。
- [ ] **Step 5：跑全部測試** — Expected：總計約 **452**，失敗 0。
- [ ] **Step 6：Commit**

```bash
git add src/SixJars.Domain/Books/Book.cs src/SixJars.Domain/Planning/PlannedExpense.cs src/SixJars.Domain/Planning/RecurringPlanner.cs tests/SixJars.Domain.Tests/Planning/RecurringPlannerTests.cs
git commit -m "feat(domain): 週期項目的產生與以現值更新規則

已產生（含已刪除）優先回報，其次是分類或帳戶已封存；以現值更新只處理
未付、未刪除、有來源的預定支出，且只回報有改變的。有來源的預定支出
不能改歸屬月份（P4 K plan D1）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task K4：週期項目的清單、新增、修改 API

**Files:** Create: `RecurringPlannedExpenseInput.cs`、`RecurringPlannedExpenseDto.cs`、`RecurringPlannedExpenseCommands.cs`、`RecurringPlannedExpensesEndpoints.cs`、`tests/SixJars.Api.Tests/RecurringPlannedExpensesEndpointsTests.cs`／Modify: `AuditEntry.cs`（`AuditEntityTypes`）、`Program.cs`

形狀比照預定支出：形狀錯誤（缺分類、金額不是負數、月份不合法、頻率不在列舉內）由 validator 回 400；業務規則（性質、月份清單、起訖）由 Domain 回 422。`IReadOnlyList<int>` 在 record 裡是**參考相等**，所以測試比對 DTO 時要用 `BeEquivalentTo`，不要用 `Be`（K9 的還原一致性檢查也會遇到同樣的問題）。

- [ ] **Step 1：寫失敗測試**

```csharp
// tests/SixJars.Api.Tests/RecurringPlannedExpensesEndpointsTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SixJars.Application.Planning;
using SixJars.Domain.Books;
using SixJars.Domain.Planning;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

public class RecurringPlannedExpensesEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_then_list_returns_the_item()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();

        var response = await client.PostAsJsonAsync(Url(book), Insurance(book), ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<RecurringPlannedExpenseDto>(ApiJson.Options, Ct))!;
        var list = (await client.GetFromJsonAsync<List<RecurringPlannedExpenseDto>>(Url(book), ApiJson.Options, Ct))!;
        list.Should().ContainSingle().Which.Should().BeEquivalentTo(created);
        created.Frequency.Should().Be(RecurrenceFrequency.Yearly);
        created.Months.Should().Equal(1, 7);
        (created.StartMonth, created.EndMonth).Should().Be((202601, (int?)null));
    }

    [Fact]
    public async Task Floating_category_is_422_and_positive_amount_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();

        var floating = await client.PostAsJsonAsync(Url(book),
            Insurance(book) with { CategoryId = book.FindCategory("主食", "午餐")!.Id.Value }, ApiJson.Options, Ct);
        var positive = await client.PostAsJsonAsync(Url(book), Insurance(book) with { DefaultAmount = 3000m }, ApiJson.Options, Ct);

        floating.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        positive.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_with_current_version_changes_the_item_and_writes_audit()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var created = await CreateAsync(client, book, Insurance(book));

        var response = await client.PutAsJsonAsync(Url(book, $"/{created.Id}"),
            new { version = created.Version, input = Insurance(book) with { DefaultAmount = -3500m, EndMonth = 202612 } }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<RecurringPlannedExpenseDto>(ApiJson.Options, Ct))!;
        (updated.DefaultAmount, updated.EndMonth).Should().Be((-3500m, (int?)202612));
        var history = await client.GetFromJsonAsync<JsonElement>(
            $"/api/books/{book.Id.Value}/audit?entityId={created.Id}", ApiJson.Options, Ct);
        history.EnumerateArray().Select(e => e.GetProperty("action").GetString()).Should().BeEquivalentTo("Create", "Update");
        history.EnumerateArray().Should().OnlyContain(e => e.GetProperty("entityType").GetString() == "RecurringPlannedExpense");
    }

    [Fact]
    public async Task Update_with_stale_version_is_409()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var created = await CreateAsync(client, book, Insurance(book));
        await client.PutAsJsonAsync(Url(book, $"/{created.Id}"), new { version = created.Version, input = Insurance(book) }, ApiJson.Options, Ct);

        var stale = await client.PutAsJsonAsync(Url(book, $"/{created.Id}"), new { version = created.Version, input = Insurance(book) }, ApiJson.Options, Ct);

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Other_books_item_is_404()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var theirs = await CreateAsync(client, other, Insurance(other));

        var response = await client.PutAsJsonAsync(Url(mine, $"/{theirs.Id}"), new { version = theirs.Version, input = Insurance(mine) }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    internal static RecurringPlannedExpenseInput Insurance(Book book) => new(
        book.FindCategory("固定支出", "保險費")!.Id.Value, null, -3000m, "保險", RecurrenceFrequency.Yearly, [7, 1], 202601, null);

    internal static async Task<RecurringPlannedExpenseDto> CreateAsync(HttpClient client, Book book, RecurringPlannedExpenseInput input)
    {
        var response = await client.PostAsJsonAsync(Url(book), input, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<RecurringPlannedExpenseDto>(ApiJson.Options, Ct))!;
    }

    internal static string Url(Book book, string suffix = "") => $"/api/books/{book.Id.Value}/recurring-planned-expenses{suffix}";
}
```

> `SeedBookAsync` 建立的使用者是否就是 `CreateMemberClientAsync` 的成員、`Other_books_item_is_404` 的寫法，比照 `PlannedExpensesEndpointsTests.List_by_month_excludes_other_months_and_other_books`（同一個 client 可以存取兩本帳時，要改用 `factory.AddOwnerAsync` 的相反情境；Step 1 前先確認）。

- [ ] **Step 2：跑測試確認失敗** — Expected：編譯失敗，`RecurringPlannedExpenseInput`、`RecurringPlannedExpenseDto` 不存在。

- [ ] **Step 3：最小實作**

```csharp
// src/SixJars.Application/Planning/RecurringPlannedExpenseInput.cs
using FluentValidation;
using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

/// <summary>週期預定支出的輸入模型；建立與修改共用。</summary>
/// <param name="DefaultAmount">預設金額，沿用支出的符號慣例（負數）。</param>
/// <param name="Months">每年的適用月份；每月時給空陣列。</param>
/// <param name="StartMonth">開始月份（yyyymm）。</param>
/// <param name="EndMonth">結束月份（yyyymm，含）；null 表示沒有結束。</param>
public sealed record RecurringPlannedExpenseInput(
    Guid CategoryId,
    Guid? AccountId,
    decimal DefaultAmount,
    string? Note,
    RecurrenceFrequency Frequency,
    IReadOnlyList<int> Months,
    int StartMonth,
    int? EndMonth);

/// <summary>只檢查形狀；性質、月份清單與起訖由 Domain 檢查（422）。</summary>
public sealed class RecurringPlannedExpenseInputValidator : AbstractValidator<RecurringPlannedExpenseInput>
{
    private const string MonthMessage = "月份必須是 yyyymm，月份介於 1 到 12。";

    public RecurringPlannedExpenseInputValidator()
    {
        RuleFor(i => i.CategoryId).NotEmpty();
        RuleFor(i => i.AccountId).NotEmpty().When(i => i.AccountId is not null);
        // 金額 0 不占用月可用餘額，週期項目就失去意義（Q5）。
        RuleFor(i => i.DefaultAmount).LessThan(0).WithMessage("預設金額必須小於 0（支出以負數表示）。");
        RuleFor(i => i.Note).MaximumLength(500);
        RuleFor(i => i.Frequency).IsInEnum();
        RuleFor(i => i.Months).NotNull();
        RuleFor(i => i.StartMonth).Must(IsMonthKey).WithMessage(MonthMessage);
        RuleFor(i => i.EndMonth).Must(key => IsMonthKey(key!.Value)).When(i => i.EndMonth is not null).WithMessage(MonthMessage);
    }

    private static bool IsMonthKey(int key) => key % 100 is >= 1 and <= 12;
}
```

```csharp
// src/SixJars.Application/Planning/RecurringPlannedExpenseDto.cs
using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

/// <summary>週期預定支出的 API 輸出：輸入欄位加上 Id 與樂觀並行版本。<see cref="Months"/> 是參考型別，比對時要逐項比。</summary>
public sealed record RecurringPlannedExpenseDto(
    Guid Id,
    Guid CategoryId,
    Guid? AccountId,
    decimal DefaultAmount,
    string? Note,
    RecurrenceFrequency Frequency,
    IReadOnlyList<int> Months,
    int StartMonth,
    int? EndMonth,
    uint Version)
{
    public static RecurringPlannedExpenseDto From(RecurringPlannedExpense r, uint version) => new(
        r.Id.Value, r.CategoryId.Value, r.AccountId?.Value, r.DefaultAmount, r.Note, r.Frequency,
        [.. r.Months], r.StartMonth.Key, r.EndMonth?.Key, version);
}
```

```csharp
// src/SixJars.Application/Planning/RecurringPlannedExpenseCommands.cs
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

/// <summary>週期預定支出清單，依建立順序。包含已結束的（前端自行標示）。</summary>
public sealed record ListRecurringPlannedExpenses(Guid BookId) : IRequest<IReadOnlyList<RecurringPlannedExpenseDto>>, IBookScoped;

public sealed record CreateRecurringPlannedExpense(Guid BookId, RecurringPlannedExpenseInput Input)
    : IRequest<RecurringPlannedExpenseDto>, IBookScoped;

/// <param name="Version">讀取時拿到的版本；期間有人改過就是 409（P4 K plan D9）。</param>
public sealed record UpdateRecurringPlannedExpense(Guid BookId, Guid RecurringPlannedExpenseId, uint Version, RecurringPlannedExpenseInput Input)
    : IRequest<RecurringPlannedExpenseDto>, IBookScoped;

internal sealed class CreateRecurringPlannedExpenseValidator : AbstractValidator<CreateRecurringPlannedExpense>
{
    public CreateRecurringPlannedExpenseValidator() =>
        RuleFor(c => c.Input).NotNull().SetValidator(new RecurringPlannedExpenseInputValidator());
}

internal sealed class UpdateRecurringPlannedExpenseValidator : AbstractValidator<UpdateRecurringPlannedExpense>
{
    public UpdateRecurringPlannedExpenseValidator() =>
        RuleFor(c => c.Input).NotNull().SetValidator(new RecurringPlannedExpenseInputValidator());
}

internal sealed class ListRecurringPlannedExpensesHandler(ISixJarsDbContext db)
    : IRequestHandler<ListRecurringPlannedExpenses, IReadOnlyList<RecurringPlannedExpenseDto>>
{
    public async Task<IReadOnlyList<RecurringPlannedExpenseDto>> Handle(ListRecurringPlannedExpenses request, CancellationToken cancellationToken)
    {
        var bookId = new BookId(request.BookId);
        // 要追蹤變更，db.GetVersion 才讀得到版本（同 ListPlannedExpenses）。
        var items = await db.RecurringPlannedExpenses.Where(r => r.BookId == bookId).OrderBy(r => r.Id).ToListAsync(cancellationToken);
        return [.. items.Select(r => RecurringPlannedExpenseDto.From(r, db.GetVersion(r)))];
    }
}

internal sealed class CreateRecurringPlannedExpenseHandler(ISixJarsDbContext db, IAuditTrail audit)
    : IRequestHandler<CreateRecurringPlannedExpense, RecurringPlannedExpenseDto>
{
    public async Task<RecurringPlannedExpenseDto> Handle(CreateRecurringPlannedExpense request, CancellationToken cancellationToken)
    {
        // 帳本只用來驗證分類與帳戶，不會被修改。週期項目本身不影響金額，所以不檢查鎖帳日。
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        var input = request.Input;
        var item = RecurringPlannedExpense.Create(
            book, new CategoryId(input.CategoryId), input.AccountId is { } a ? new AccountId(a) : null, input.DefaultAmount, input.Note,
            input.Frequency, input.Months, BudgetMonth.FromKey(input.StartMonth), input.EndMonth is { } e ? BudgetMonth.FromKey(e) : null);
        db.RecurringPlannedExpenses.Add(item);
        audit.Record(request.BookId, AuditAction.Create, AuditEntityTypes.RecurringPlannedExpense, item.Id.Value,
            null, RecurringPlannedExpenseDto.From(item, AuditSnapshots.UnknownVersion));
        await db.SaveChangesAsync(cancellationToken);
        return RecurringPlannedExpenseDto.From(item, db.GetVersion(item));
    }
}

internal sealed class UpdateRecurringPlannedExpenseHandler(ISixJarsDbContext db, IAuditTrail audit)
    : IRequestHandler<UpdateRecurringPlannedExpense, RecurringPlannedExpenseDto>
{
    public async Task<RecurringPlannedExpenseDto> Handle(UpdateRecurringPlannedExpense request, CancellationToken cancellationToken)
    {
        var item = await db.FindRecurringPlannedExpenseAsync(request.BookId, request.RecurringPlannedExpenseId, cancellationToken);
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        var input = request.Input;

        var before = RecurringPlannedExpenseDto.From(item, db.GetVersion(item));
        db.ExpectVersion(item, request.Version);
        item.Update(
            book, new CategoryId(input.CategoryId), input.AccountId is { } a ? new AccountId(a) : null, input.DefaultAmount, input.Note,
            input.Frequency, input.Months, BudgetMonth.FromKey(input.StartMonth), input.EndMonth is { } e ? BudgetMonth.FromKey(e) : null);
        audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.RecurringPlannedExpense, item.Id.Value,
            before, RecurringPlannedExpenseDto.From(item, AuditSnapshots.UnknownVersion));
        await db.SaveChangesAsync(cancellationToken);
        return RecurringPlannedExpenseDto.From(item, db.GetVersion(item));
    }
}

internal static class RecurringPlannedExpenseLoading
{
    /// <summary>帳本與 Id 一起當查詢條件（同 FindPlannedExpenseAsync）；找不到擲 <see cref="NotFoundException"/>。</summary>
    public static async Task<RecurringPlannedExpense> FindRecurringPlannedExpenseAsync(
        this ISixJarsDbContext db, Guid bookId, Guid recurringId, CancellationToken cancellationToken)
    {
        var book = new BookId(bookId);
        var id = new RecurringPlannedExpenseId(recurringId);
        return await db.RecurringPlannedExpenses.SingleOrDefaultAsync(r => r.BookId == book && r.Id == id, cancellationToken)
            ?? throw new NotFoundException($"找不到週期預定支出 {recurringId}。");
    }
}
```

```diff
--- a/src/SixJars.Application/Auditing/AuditEntry.cs
+++ b/src/SixJars.Application/Auditing/AuditEntry.cs
@@
     public const string PlannedExpense = "PlannedExpense";
+    public const string RecurringPlannedExpense = "RecurringPlannedExpense";
```

```csharp
// src/SixJars.Api/Endpoints/RecurringPlannedExpensesEndpoints.cs
using MediatR;
using SixJars.Application.Planning;

namespace SixJars.Api.Endpoints;

internal static class RecurringPlannedExpensesEndpoints
{
    /// <summary>週期預定支出的清單、新增、修改、刪除；掛在 <c>/books/{bookId}</c> 群組底下。</summary>
    public static RouteGroupBuilder MapRecurringPlannedExpensesEndpoints(this RouteGroupBuilder book)
    {
        book.MapGet("/recurring-planned-expenses", (Guid bookId, ISender sender, CancellationToken ct) =>
            sender.Send(new ListRecurringPlannedExpenses(bookId), ct));
        book.MapPost("/recurring-planned-expenses", async (Guid bookId, RecurringPlannedExpenseInput input, ISender sender, CancellationToken ct) =>
        {
            var dto = await sender.Send(new CreateRecurringPlannedExpense(bookId, input), ct);
            return Results.Created($"/api/books/{bookId}/recurring-planned-expenses/{dto.Id}", dto);
        });
        book.MapPut("/recurring-planned-expenses/{recurringId:guid}",
            (Guid bookId, Guid recurringId, UpdateRecurringPlannedExpenseBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new UpdateRecurringPlannedExpense(bookId, recurringId, body.Version, body.Input), ct));
        return book;
    }
}

/// <summary>修改週期預定支出的 body：讀取時拿到的版本，加上新的內容。</summary>
internal sealed record UpdateRecurringPlannedExpenseBody(uint Version, RecurringPlannedExpenseInput Input);
```

```diff
--- a/src/SixJars.Api/Program.cs
+++ b/src/SixJars.Api/Program.cs
@@
-api.MapBooksEndpoints().MapTransactionsEndpoints().MapPlannedExpensesEndpoints().MapSummaryEndpoints().MapAuditEndpoints()
+api.MapBooksEndpoints().MapTransactionsEndpoints().MapPlannedExpensesEndpoints().MapRecurringPlannedExpensesEndpoints()
+    .MapSummaryEndpoints().MapAuditEndpoints()
     .MapExportsEndpoints();
```

- [ ] **Step 4：跑單檔測試** — Expected：5 passed。另外跑 `--filter-class "*BookScopeConventionTests*"`，確認三個新 request 都有實作 `IBookScoped`。
- [ ] **Step 5：跑全部測試** — Expected：總計約 **457**，失敗 0。
- [ ] **Step 6：Commit**

```bash
git add src/SixJars.Application/Planning/RecurringPlannedExpenseInput.cs src/SixJars.Application/Planning/RecurringPlannedExpenseDto.cs src/SixJars.Application/Planning/RecurringPlannedExpenseCommands.cs src/SixJars.Application/Auditing/AuditEntry.cs src/SixJars.Api/Endpoints/RecurringPlannedExpensesEndpoints.cs src/SixJars.Api/Program.cs tests/SixJars.Api.Tests/RecurringPlannedExpensesEndpointsTests.cs
git commit -m "feat(api): 週期預定支出的清單、新增、修改

形狀錯誤 400、性質與月份規則 422、版本過期 409，寫入留稽核。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task K5：刪除週期項目（被參照時 422）

**Files:** Modify: `RecurringPlannedExpenseCommands.cs`、`RecurringPlannedExpensesEndpoints.cs`、`RecurringPlannedExpensesEndpointsTests.cs`

「被參照」要包含**已軟刪除**的預定支出（ADR 0009：使用者刪掉產生出來的那筆，來源參照仍然存在），所以查詢必須 `IgnoreQueryFilters()`。不加的話，Domain 會放行，然後在 FK（Restrict）上炸成 500，而不是 422。

- [ ] **Step 1：寫失敗測試**（加到 `RecurringPlannedExpensesEndpointsTests`）

```csharp
    [Fact]
    public async Task Delete_unused_item_is_204_and_writes_audit()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var created = await CreateAsync(client, book, Insurance(book));

        var response = await client.DeleteAsync(Url(book, $"/{created.Id}?version={created.Version}"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<List<RecurringPlannedExpenseDto>>(Url(book), ApiJson.Options, Ct)).Should().BeEmpty();
        var history = await client.GetFromJsonAsync<JsonElement>(
            $"/api/books/{book.Id.Value}/audit?entityId={created.Id}", ApiJson.Options, Ct);
        history.EnumerateArray().Select(e => e.GetProperty("action").GetString()).Should().Contain("Delete");
    }

    [Fact]
    public async Task Delete_is_422_in_use_even_when_the_generated_planned_expense_was_deleted()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var created = await CreateAsync(client, book, Insurance(book));
        await factory.SeedGeneratedPlannedExpenseAsync(book, created.Id, 202601, deleted: true, Ct);

        var response = await client.DeleteAsync(Url(book, $"/{created.Id}?version={created.Version}"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("in-use");
    }
```

測試需要一個 seed helper，因為 generate API 要到 K6 才有。放在同一個測試檔（`internal static`，K6–K9 共用）：

```csharp
internal static class RecurringSeed
{
    /// <summary>直接寫入一筆由週期項目產生的預定支出（繞過 generate API）。</summary>
    public static async Task<Guid> SeedGeneratedPlannedExpenseAsync(
        this ApiFactory factory, Book book, Guid recurringId, int budgetMonth, bool deleted, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        var source = await db.RecurringPlannedExpenses.SingleAsync(r => r.Id == new RecurringPlannedExpenseId(recurringId), ct);
        var planned = PlannedExpense.Create(book, BudgetMonth.FromKey(budgetMonth), source.CategoryId, source.AccountId,
            source.DefaultAmount, source.Note, sourceId: source.Id);
        if (deleted)
        {
            planned.Delete(DateTimeOffset.UtcNow);
        }

        db.PlannedExpenses.Add(planned);
        await db.SaveChangesAsync(ct);
        return planned.Id.Value;
    }
}
```

> 取得 `SixJarsDbContext` 的寫法比照 `SettingsMaintenanceEndpointsTests.ReverseAccountsAndExpenseMainsAsync`（`factory.Services.CreateAsyncScope()`）。

- [ ] **Step 2：跑測試確認失敗** — Expected：兩條都是 405 或 404（DELETE 路由尚未存在；J8 的經驗是路由不存在時回 404）。
- [ ] **Step 3：最小實作**

```csharp
// RecurringPlannedExpenseCommands.cs 追加
/// <summary>刪除週期項目：只有從未產生過預定支出（含已刪除）時可以（ADR 0009、P4 K plan D4），硬刪除。</summary>
public sealed record DeleteRecurringPlannedExpense(Guid BookId, Guid RecurringPlannedExpenseId, uint Version) : IRequest, IBookScoped;

internal sealed class DeleteRecurringPlannedExpenseHandler(ISixJarsDbContext db, IAuditTrail audit)
    : IRequestHandler<DeleteRecurringPlannedExpense>
{
    public async Task Handle(DeleteRecurringPlannedExpense request, CancellationToken cancellationToken)
    {
        var item = await db.FindRecurringPlannedExpenseAsync(request.BookId, request.RecurringPlannedExpenseId, cancellationToken);
        // 已刪除的預定支出也算：來源參照仍然存在，而且 FK 會擋。
        var hasGenerated = await db.PlannedExpenses.IgnoreQueryFilters().AnyAsync(p => p.SourceId == item.Id, cancellationToken);
        item.EnsureRemovable(hasGenerated);

        var before = RecurringPlannedExpenseDto.From(item, db.GetVersion(item));
        db.ExpectVersion(item, request.Version);
        db.RecurringPlannedExpenses.Remove(item);
        audit.Record(request.BookId, AuditAction.Delete, AuditEntityTypes.RecurringPlannedExpense, item.Id.Value, before, null);
        await db.SaveChangesAsync(cancellationToken);
    }
}
```

```diff
--- a/src/SixJars.Api/Endpoints/RecurringPlannedExpensesEndpoints.cs
+++ b/src/SixJars.Api/Endpoints/RecurringPlannedExpensesEndpoints.cs
@@
                 sender.Send(new UpdateRecurringPlannedExpense(bookId, recurringId, body.Version, body.Input), ct));
+        // DELETE 不帶 body，版本由 query string 的 ?version= 帶入（同預定支出）。
+        book.MapDelete("/recurring-planned-expenses/{recurringId:guid}",
+            async (Guid bookId, Guid recurringId, uint version, ISender sender, CancellationToken ct) =>
+            {
+                await sender.Send(new DeleteRecurringPlannedExpense(bookId, recurringId, version), ct);
+                return Results.NoContent();
+            });
         return book;
```

> `ExpectVersion` 之後再 `Remove`，DELETE 仍帶 xmin 條件：已由 `PlanKEfAssumptionTests.Remove_after_expect_version_checks_xmin` 證實（2026-10-07）。

變異檢查：拿掉 `IgnoreQueryFilters()`，`Delete_is_422_in_use_even_when_...` 必須變紅（變成 500）。

- [ ] **Step 4：跑單檔測試** — Expected：7 passed。
- [ ] **Step 5：跑全部測試** — Expected：總計約 **459**，失敗 0。
- [ ] **Step 6：Commit**

```bash
git add src/SixJars.Application/Planning/RecurringPlannedExpenseCommands.cs src/SixJars.Api/Endpoints/RecurringPlannedExpensesEndpoints.cs tests/SixJars.Api.Tests/RecurringPlannedExpensesEndpointsTests.cs
git commit -m "feat(api): 刪除週期預定支出，產生過就回 422 in-use

參照檢查包含已軟刪除的預定支出（ADR 0009）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task K6：產生（POST /planned-expenses/generate）

**Files:** Create: `src/SixJars.Application/Planning/GeneratePlannedExpenses.cs`、`tests/SixJars.Api.Tests/PlannedExpenseGenerationEndpointsTests.cs`／Modify: `PlannedExpensesEndpoints.cs`

冪等與鎖帳日是這裡的重點。「已產生」的集合要用 `IgnoreQueryFilters()` 查，條件是 `BookId`、`BudgetMonth`、`SourceId != null`。`PlannedExpenseDto` 到 K9 才有 `SourceId`，所以這裡驗證來源時直接查資料庫。

- [ ] **Step 1：寫失敗測試**

```csharp
// tests/SixJars.Api.Tests/PlannedExpenseGenerationEndpointsTests.cs
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Planning;
using SixJars.Domain.Books;
using SixJars.Domain.Planning;
using SixJars.Tests.Shared;
using Xunit;
using static SixJars.Api.Tests.RecurringPlannedExpensesEndpointsTests;

namespace SixJars.Api.Tests;

public class PlannedExpenseGenerationEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Generate_creates_due_items_and_second_call_reports_already_generated()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var january = await CreateAsync(client, book, Insurance(book));                               // 1、7 月
        await CreateAsync(client, book, Insurance(book) with { Months = [3] });                        // 1 月不適用

        var first = await GenerateAsync(client, book, 202601);
        var second = await GenerateAsync(client, book, 202601);

        first.Created.Should().ContainSingle().Which.EstimatedAmount.Should().Be(-3000m);
        first.Skipped.Should().BeEmpty();
        second.Created.Should().BeEmpty();
        second.Skipped.Should().Equal(new RecurringSkipDto(january.Id, null, RecurringSkipReason.AlreadyGenerated));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        (await db.PlannedExpenses.SingleAsync(Ct)).SourceId.Should().Be(new RecurringPlannedExpenseId(january.Id));
    }

    [Fact]
    public async Task Deleted_generated_item_is_not_recreated()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var item = await CreateAsync(client, book, Insurance(book));
        var created = (await GenerateAsync(client, book, 202601)).Created.Single();
        await client.DeleteAsync($"/api/books/{book.Id.Value}/planned-expenses/{created.Id}?version={created.Version}", Ct);

        var again = await GenerateAsync(client, book, 202601);

        again.Created.Should().BeEmpty();
        again.Skipped.Single().Reason.Should().Be(RecurringSkipReason.AlreadyGenerated);
    }

    [Fact]
    public async Task Archived_category_is_skipped_with_reason()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var item = await CreateAsync(client, book, Insurance(book));
        var fixedMain = book.FindCategory("固定支出")!.Id.Value;
        (await client.PostAsync($"/api/books/{book.Id.Value}/categories/{fixedMain}/archive", null, Ct)).EnsureSuccessStatusCode();

        var result = await GenerateAsync(client, book, 202601);

        result.Skipped.Should().Equal(new RecurringSkipDto(item.Id, null, RecurringSkipReason.CategoryArchived));
    }

    [Fact]
    public async Task Locked_month_is_422_locked()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await CreateAsync(client, book, Insurance(book));
        (await client.PutAsJsonAsync($"/api/books/{book.Id.Value}/lock-date", new { lockDate = "2026-01-31" }, ApiJson.Options, Ct))
            .EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/api/books/{book.Id.Value}/planned-expenses/generate?budgetMonth=202601", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("locked");
    }

    [Fact]
    public async Task Invalid_month_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync())
            .PostAsync($"/api/books/{book.Id.Value}/planned-expenses/generate?budgetMonth=202613", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    internal static async Task<GeneratePlannedExpensesResult> GenerateAsync(HttpClient client, Book book, int month)
    {
        var response = await client.PostAsync($"/api/books/{book.Id.Value}/planned-expenses/generate?budgetMonth={month}", null, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<GeneratePlannedExpensesResult>(ApiJson.Options, Ct))!;
    }
}
```

> `GeneratePlannedExpensesResult` 的 `Created` 是 `IReadOnlyList<PlannedExpenseDto>`，反序列化成 `List`。`RecurringSkipDto` 是不含集合的 record，可以直接用 `Equal` 比對。

- [ ] **Step 2：跑測試確認失敗** — Expected：編譯失敗（`GeneratePlannedExpensesResult`、`RecurringSkipDto` 不存在）。
- [ ] **Step 3：最小實作**

```csharp
// src/SixJars.Application/Planning/GeneratePlannedExpenses.cs
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

/// <summary>對某個歸屬月份明確產生週期項目的預定支出（ADR 0009）；冪等，已產生（含已刪除）的回報為略過。</summary>
public sealed record GeneratePlannedExpenses(Guid BookId, int BudgetMonth) : IRequest<GeneratePlannedExpensesResult>, IBookScoped;

/// <param name="PlannedExpenseId">以現值更新時被略過的預定支出；產生時為 null。</param>
public sealed record RecurringSkipDto(Guid RecurringId, Guid? PlannedExpenseId, RecurringSkipReason Reason)
{
    public static RecurringSkipDto From(RecurringSkip skip) => new(skip.RecurringId.Value, skip.PlannedExpenseId?.Value, skip.Reason);
}

public sealed record GeneratePlannedExpensesResult(IReadOnlyList<PlannedExpenseDto> Created, IReadOnlyList<RecurringSkipDto> Skipped);

internal sealed class GeneratePlannedExpensesValidator : AbstractValidator<GeneratePlannedExpenses>
{
    public GeneratePlannedExpensesValidator() =>
        RuleFor(c => c.BudgetMonth).Must(key => key % 100 is >= 1 and <= 12).WithMessage("歸屬月份必須是 yyyymm，月份介於 1 到 12。");
}

internal sealed class GeneratePlannedExpensesHandler(ISixJarsDbContext db, IAuditTrail audit)
    : IRequestHandler<GeneratePlannedExpenses, GeneratePlannedExpensesResult>
{
    public async Task<GeneratePlannedExpensesResult> Handle(GeneratePlannedExpenses request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        var month = BudgetMonth.FromKey(request.BudgetMonth);
        book.EnsureUnlocked(month);

        var items = await db.RecurringPlannedExpenses.AsNoTracking()
            .Where(r => r.BookId == book.Id).OrderBy(r => r.Id).ToListAsync(cancellationToken);
        // 含已刪除：刪除代表使用者決定這個月沒有這筆（ADR 0009）。
        var generated = await db.PlannedExpenses.IgnoreQueryFilters()
            .Where(p => p.BookId == book.Id && p.BudgetMonth == month && p.SourceId != null)
            .Select(p => p.SourceId!.Value)
            .ToListAsync(cancellationToken);

        var result = RecurringPlanner.Generate(book, month, items, generated.ToHashSet());
        foreach (var planned in result.Created)
        {
            db.PlannedExpenses.Add(planned);
            audit.Record(request.BookId, AuditAction.Create, AuditEntityTypes.PlannedExpense, planned.Id.Value,
                null, PlannedExpenseDto.From(planned, AuditSnapshots.UnknownVersion));
        }

        await db.SaveChangesAsync(cancellationToken);
        return new GeneratePlannedExpensesResult(
            [.. result.Created.Select(p => PlannedExpenseDto.From(p, db.GetVersion(p)))],
            [.. result.Skipped.Select(RecurringSkipDto.From)]);
    }
}
```

```diff
--- a/src/SixJars.Api/Endpoints/PlannedExpensesEndpoints.cs
+++ b/src/SixJars.Api/Endpoints/PlannedExpensesEndpoints.cs
@@
         book.MapGet("/planned-expenses", (Guid bookId, int? budgetMonth, ISender sender, CancellationToken ct) =>
             sender.Send(new ListPlannedExpenses(bookId, budgetMonth), ct));
+        // 明確的產生動作（ADR 0009）；路徑不是 guid，不會和 /{plannedExpenseId:guid} 衝突。
+        book.MapPost("/planned-expenses/generate", (Guid bookId, int budgetMonth, ISender sender, CancellationToken ct) =>
+            sender.Send(new GeneratePlannedExpenses(bookId, budgetMonth), ct));
```

> `p.SourceId!.Value` 的翻譯：同形狀的 `PlannedExpense.AccountId!.Value` 已由 `PlanKEfAssumptionTests.Nullable_strongly_typed_id_value_is_translated_in_projection` 證實可以翻譯（2026-10-07）。

變異檢查：拿掉 `IgnoreQueryFilters()`，`Deleted_generated_item_is_not_recreated` 必須變紅（變成 500：撞到唯一索引）。這條同時證明了 K2 的索引在 API 層級的效果。

- [ ] **Step 4：跑單檔測試** — Expected：5 passed。
- [ ] **Step 5：跑全部測試** — Expected：總計約 **464**，失敗 0。
- [ ] **Step 6：Commit**

```bash
git add src/SixJars.Application/Planning/GeneratePlannedExpenses.cs src/SixJars.Api/Endpoints/PlannedExpensesEndpoints.cs tests/SixJars.Api.Tests/PlannedExpenseGenerationEndpointsTests.cs
git commit -m "feat(api): 依週期項目產生某月的預定支出

冪等：已產生（含已刪除）的回報 AlreadyGenerated；分類或帳戶已封存的
略過並回報；已鎖帳的月份 422 locked。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task K7：以現值更新（POST /planned-expenses/refresh）

**Files:** Create: `src/SixJars.Application/Planning/RefreshPlannedExpenses.cs`／Modify: `PlannedExpensesEndpoints.cs`、`PlannedExpenseGenerationEndpointsTests.cs`

預定支出要**追蹤變更**載入（樂觀並行靠 xmin，存檔時會自動檢查），週期項目則用 `AsNoTracking`。稽核的 before 快照必須在 `Refresh` 之前取，所以先把候選的快照存成字典。

- [ ] **Step 1：寫失敗測試**（加到 `PlannedExpenseGenerationEndpointsTests`）

```csharp
    [Fact]
    public async Task Refresh_applies_current_values_to_unpaid_generated_items_only()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var item = await CreateAsync(client, book, Insurance(book) with { Frequency = RecurrenceFrequency.Monthly, Months = [] });
        await GenerateAsync(client, book, 202601);
        var paid = (await GenerateAsync(client, book, 202602)).Created.Single();
        await PayAsync(client, book, paid);
        await client.PutAsJsonAsync(RecurringUrl(book, item.Id),
            new { version = item.Version, input = Insurance(book) with { Frequency = RecurrenceFrequency.Monthly, Months = [], DefaultAmount = -3600m } },
            ApiJson.Options, Ct);

        var january = await RefreshAsync(client, book, 202601);
        var february = await RefreshAsync(client, book, 202602);

        january.Updated.Should().ContainSingle().Which.EstimatedAmount.Should().Be(-3600m);
        february.Updated.Should().BeEmpty();   // 已付款的不更新
    }

    [Fact]
    public async Task Refresh_reports_not_due_and_leaves_item_unchanged()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var item = await CreateAsync(client, book, Insurance(book));                  // 1、7 月
        var generated = (await GenerateAsync(client, book, 202601)).Created.Single();
        await client.PutAsJsonAsync(RecurringUrl(book, item.Id),
            new { version = item.Version, input = Insurance(book) with { Months = [7], DefaultAmount = -1m } }, ApiJson.Options, Ct);

        var result = await RefreshAsync(client, book, 202601);

        result.Updated.Should().BeEmpty();
        result.Skipped.Should().Equal(new RecurringSkipDto(item.Id, generated.Id, RecurringSkipReason.NotDue));
    }

    [Fact]
    public async Task Refresh_writes_one_update_audit_per_changed_item()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var item = await CreateAsync(client, book, Insurance(book));
        var generated = (await GenerateAsync(client, book, 202601)).Created.Single();
        await client.PutAsJsonAsync(RecurringUrl(book, item.Id),
            new { version = item.Version, input = Insurance(book) with { Note = "改備註" } }, ApiJson.Options, Ct);

        await RefreshAsync(client, book, 202601);
        await RefreshAsync(client, book, 202601);   // 第二次沒有改變，不寫稽核（D8）

        var history = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/books/{book.Id.Value}/audit?entityId={generated.Id}", ApiJson.Options, Ct);
        history.EnumerateArray().Select(e => e.GetProperty("action").GetString()).Should().BeEquivalentTo("Create", "Update");
    }

    [Fact]
    public async Task Refresh_on_locked_month_is_422_locked()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        (await client.PutAsJsonAsync($"/api/books/{book.Id.Value}/lock-date", new { lockDate = "2026-01-31" }, ApiJson.Options, Ct))
            .EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/api/books/{book.Id.Value}/planned-expenses/refresh?budgetMonth=202601", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private static string RecurringUrl(Book book, Guid id) => $"/api/books/{book.Id.Value}/recurring-planned-expenses/{id}";

    private static async Task<RefreshPlannedExpensesResult> RefreshAsync(HttpClient client, Book book, int month)
    {
        var response = await client.PostAsync($"/api/books/{book.Id.Value}/planned-expenses/refresh?budgetMonth={month}", null, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<RefreshPlannedExpensesResult>(ApiJson.Options, Ct))!;
    }

    private static async Task PayAsync(HttpClient client, Book book, PlannedExpenseDto planned)
    {
        var response = await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/planned-expenses/{planned.Id}/pay",
            new { version = planned.Version, date = "2026-02-05", accountId = book.FindAccount("國泰世華銀行")!.Id.Value, amount = -3000m },
            ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
```

- [ ] **Step 2：跑測試確認失敗** — Expected：編譯失敗（`RefreshPlannedExpensesResult` 不存在）。
- [ ] **Step 3：最小實作**

```csharp
// src/SixJars.Application/Planning/RefreshPlannedExpenses.cs
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

/// <summary>以週期項目的現值更新某月未付、由週期項目產生的預定支出（ADR 0009）；只回報有改變的（P4 K plan D8）。</summary>
public sealed record RefreshPlannedExpenses(Guid BookId, int BudgetMonth) : IRequest<RefreshPlannedExpensesResult>, IBookScoped;

public sealed record RefreshPlannedExpensesResult(IReadOnlyList<PlannedExpenseDto> Updated, IReadOnlyList<RecurringSkipDto> Skipped);

internal sealed class RefreshPlannedExpensesValidator : AbstractValidator<RefreshPlannedExpenses>
{
    public RefreshPlannedExpensesValidator() =>
        RuleFor(c => c.BudgetMonth).Must(key => key % 100 is >= 1 and <= 12).WithMessage("歸屬月份必須是 yyyymm，月份介於 1 到 12。");
}

internal sealed class RefreshPlannedExpensesHandler(ISixJarsDbContext db, IAuditTrail audit)
    : IRequestHandler<RefreshPlannedExpenses, RefreshPlannedExpensesResult>
{
    public async Task<RefreshPlannedExpensesResult> Handle(RefreshPlannedExpenses request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        var month = BudgetMonth.FromKey(request.BudgetMonth);
        book.EnsureUnlocked(month);

        // 追蹤變更：存檔時以 xmin 做並行檢查。
        var candidates = await db.PlannedExpenses
            .Where(p => p.BookId == book.Id && p.BudgetMonth == month && p.SourceId != null && p.PaidTransactionId == null)
            .ToListAsync(cancellationToken);
        var sourceIds = candidates.Select(p => p.SourceId!.Value).Distinct().ToList();
        var items = await db.RecurringPlannedExpenses.AsNoTracking()
            .Where(r => sourceIds.Contains(r.Id)).ToListAsync(cancellationToken);
        // before 快照必須在 Refresh 之前取得。
        var before = candidates.ToDictionary(p => p.Id, p => PlannedExpenseDto.From(p, db.GetVersion(p)));

        var result = RecurringPlanner.Refresh(book, month, items, candidates);
        foreach (var planned in result.Updated)
        {
            audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.PlannedExpense, planned.Id.Value,
                before[planned.Id], PlannedExpenseDto.From(planned, AuditSnapshots.UnknownVersion));
        }

        await db.SaveChangesAsync(cancellationToken);
        return new RefreshPlannedExpensesResult(
            [.. result.Updated.Select(p => PlannedExpenseDto.From(p, db.GetVersion(p)))],
            [.. result.Skipped.Select(RecurringSkipDto.From)]);
    }
}
```

```diff
--- a/src/SixJars.Api/Endpoints/PlannedExpensesEndpoints.cs
+++ b/src/SixJars.Api/Endpoints/PlannedExpensesEndpoints.cs
@@
         book.MapPost("/planned-expenses/generate", (Guid bookId, int budgetMonth, ISender sender, CancellationToken ct) =>
             sender.Send(new GeneratePlannedExpenses(bookId, budgetMonth), ct));
+        book.MapPost("/planned-expenses/refresh", (Guid bookId, int budgetMonth, ISender sender, CancellationToken ct) =>
+            sender.Send(new RefreshPlannedExpenses(bookId, budgetMonth), ct));
```

變異檢查：拿掉 `audit.Record`，`Refresh_writes_one_update_audit_per_changed_item` 必須變紅。
（「已付款的不更新」由 handler 的查詢條件與 Domain 的 `!p.IsPaid` 雙重把關，單拿掉其中一個 API 測試仍會綠，這是預期的；Domain 那一層由 K3 的 `Refresh_ignores_paid_deleted_and_manual_items` 驗證。）

- [ ] **Step 4：跑單檔測試** — Expected：9 passed。
- [ ] **Step 5：跑全部測試** — Expected：總計約 **468**，失敗 0。
- [ ] **Step 6：Commit**

```bash
git add src/SixJars.Application/Planning/RefreshPlannedExpenses.cs src/SixJars.Api/Endpoints/PlannedExpensesEndpoints.cs tests/SixJars.Api.Tests/PlannedExpenseGenerationEndpointsTests.cs
git commit -m "feat(api): 以週期項目現值更新某月未付的預定支出

只處理未付、未刪除、有來源的；當月已不適用的回報 NotDue；只對
有改變的寫稽核（P4 K plan D8）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task K8：設定維護補上週期項目（D6）

**Files:** Modify: `src/SixJars.Domain/Books/Book.cs`、`src/SixJars.Application/Books/{SettingReferences,UpdateSettings}.cs`、`tests/SixJars.Domain.Tests/Books/BookSettingsTests.cs`、`tests/SixJars.Api.Tests/SettingsMaintenanceEndpointsTests.cs`

J 段的刪除與改性質只知道交易與預定支出。不補的話：(a) 週期項目對分類與帳戶**沒有 FK**（兩者都是 `Book` 的 owned entity），刪除被週期項目使用的分類或帳戶會**成功**，留下指向不存在設定的週期項目，下次產生時 `GetCategory` 擲 422「找不到分類」；(b) 支出主分類改成特別或浮動之後，週期項目違反「只限固定、貸款」的不變條件，備份也還原不了。

`ChangeExpenseNature` 新增選用參數，而不是改成必要參數：既有呼叫端與 J 的測試不需要修改。

- [ ] **Step 1：寫失敗測試**

```csharp
// BookSettingsTests 追加
    [Theory]
    [InlineData(ExpenseNature.Special)]
    [InlineData(ExpenseNature.Floating)]
    public void Category_with_recurring_items_can_only_be_fixed_or_loan(ExpenseNature nature)
    {
        var rent = _book.AddExpenseCategory("房租", ExpenseNature.Fixed);

        var act = () => _book.ChangeExpenseNature(rent.Id, nature, hasPlannedExpenses: false, hasRecurringPlannedExpenses: true);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.InUseCode);
        rent.Nature.Should().Be(ExpenseNature.Fixed);
    }

    [Fact]
    public void Category_with_recurring_items_can_switch_between_fixed_and_loan()
    {
        var rent = _book.AddExpenseCategory("房租", ExpenseNature.Fixed);

        _book.ChangeExpenseNature(rent.Id, ExpenseNature.Loan, hasPlannedExpenses: true, hasRecurringPlannedExpenses: true);

        rent.Nature.Should().Be(ExpenseNature.Loan);
    }
```

```csharp
// SettingsMaintenanceEndpointsTests 追加
    [Fact]
    public async Task Removing_category_or_account_used_by_recurring_item_is_422_in_use()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var postOffice = await AddAccountAsync(client, book, "郵局");
        var insurance = book.FindCategory("固定支出", "保險費")!.Id.Value;
        await RecurringPlannedExpensesEndpointsTests.CreateAsync(client, book,
            RecurringPlannedExpensesEndpointsTests.Insurance(book) with { AccountId = postOffice });

        var category = await client.DeleteAsync(Url(book, $"/categories/{insurance}"), Ct);
        var account = await client.DeleteAsync(Url(book, $"/accounts/{postOffice}"), Ct);

        (await ReadProblemAsync(category)).GetProperty("code").GetString().Should().Be("in-use");
        (await ReadProblemAsync(account)).GetProperty("code").GetString().Should().Be("in-use");
    }

    [Fact]
    public async Task Changing_main_category_with_recurring_sub_item_to_special_is_422_in_use()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await RecurringPlannedExpensesEndpointsTests.CreateAsync(client, book, RecurringPlannedExpensesEndpointsTests.Insurance(book));
        var fixedMain = book.FindCategory("固定支出")!.Id.Value;

        var response = await client.PutAsJsonAsync(Url(book, $"/categories/{fixedMain}"),
            new { name = "固定支出", nature = "Special" }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("in-use");
    }
```

> `Url(book, suffix)` 與 `AddAccountAsync` 是 `SettingsMaintenanceEndpointsTests` 既有的 helper（第 376 行）；`Url` 的簽章 Step 1 前先確認。

- [ ] **Step 2：跑測試確認失敗** — Expected：Domain 測試編譯失敗（沒有 `hasRecurringPlannedExpenses` 參數）；API 的刪除測試是 204，不是 422。
- [ ] **Step 3：最小實作**

```diff
--- a/src/SixJars.Domain/Books/Book.cs
+++ b/src/SixJars.Domain/Books/Book.cs
@@
     /// <summary>
     /// 改支出主分類的性質，回溯生效，子分類一起改（spec §3.1、Q4）。
     /// 預定支出只允許固定、貸款、特別，所以有預定支出（含已刪除）時不能改成浮動。
+    /// 週期預定支出只允許固定、貸款，所以有週期項目時只能在這兩者之間切換（P4 K plan D6）。
     /// L 段會再加上「有預算時拒絕」。
     /// </summary>
-    public void ChangeExpenseNature(CategoryId id, ExpenseNature nature, bool hasPlannedExpenses)
+    public void ChangeExpenseNature(CategoryId id, ExpenseNature nature, bool hasPlannedExpenses, bool hasRecurringPlannedExpenses = false)
     {
@@
         if (nature == ExpenseNature.Floating && hasPlannedExpenses)
         {
             throw new DomainException($"「{category.Name}」有預定支出，不能改成浮動支出。", DomainException.InUseCode);
         }
 
+        if (nature is not (ExpenseNature.Fixed or ExpenseNature.Loan) && hasRecurringPlannedExpenses)
+        {
+            throw new DomainException($"「{category.Name}」有週期預定支出，只能是固定或貸款支出。", DomainException.InUseCode);
+        }
+
         category.ChangeNature(nature);
```

```diff
--- a/src/SixJars.Application/Books/SettingReferences.cs
+++ b/src/SixJars.Application/Books/SettingReferences.cs
@@
+    /// <summary>主分類本身或其子分類，是否有任何週期預定支出。</summary>
+    public static Task<bool> HasRecurringPlannedExpensesAsync(this ISixJarsDbContext db, Book book, CategoryId mainId, CancellationToken cancellationToken)
+    {
+        var ids = book.Categories.Where(c => c.Id == mainId || c.ParentId == mainId).Select(c => c.Id).ToList();
+        return db.RecurringPlannedExpenses.AnyAsync(r => r.BookId == book.Id && ids.Contains(r.CategoryId), cancellationToken);
+    }
+
-    /// <summary>交易的帳戶、對方帳戶、分錄，以及預定支出的帳戶。</summary>
+    /// <summary>交易的帳戶、對方帳戶、分錄，以及預定支出與週期預定支出的帳戶。</summary>
     public static async Task<bool> IsAccountReferencedAsync(this ISixJarsDbContext db, BookId bookId, AccountId id, CancellationToken cancellationToken) =>
         await db.Transactions.IgnoreQueryFilters().AnyAsync(t => t.BookId == bookId
             && (t.AccountId == id || t.CounterAccountId == id || t.Postings.Any(p => p.AccountId == id)), cancellationToken)
-        || await db.PlannedExpenses.IgnoreQueryFilters().AnyAsync(p => p.BookId == bookId && p.AccountId == id, cancellationToken);
+        || await db.PlannedExpenses.IgnoreQueryFilters().AnyAsync(p => p.BookId == bookId && p.AccountId == id, cancellationToken)
+        || await db.RecurringPlannedExpenses.AnyAsync(r => r.BookId == bookId && r.AccountId == id, cancellationToken);
 
     public static async Task<bool> IsCategoryReferencedAsync(this ISixJarsDbContext db, BookId bookId, CategoryId id, CancellationToken cancellationToken) =>
         await db.Transactions.IgnoreQueryFilters().AnyAsync(t => t.BookId == bookId && t.CategoryId == id, cancellationToken)
-        || await db.PlannedExpenses.IgnoreQueryFilters().AnyAsync(p => p.BookId == bookId && p.CategoryId == id, cancellationToken);
+        || await db.PlannedExpenses.IgnoreQueryFilters().AnyAsync(p => p.BookId == bookId && p.CategoryId == id, cancellationToken)
+        || await db.RecurringPlannedExpenses.AnyAsync(r => r.BookId == bookId && r.CategoryId == id, cancellationToken);
```

```diff
--- a/src/SixJars.Application/Books/UpdateSettings.cs
+++ b/src/SixJars.Application/Books/UpdateSettings.cs
@@
             var hasPlannedExpenses = nature == ExpenseNature.Floating
                 && await db.HasPlannedExpensesAsync(book, id, cancellationToken);
-            book.ChangeExpenseNature(id, nature, hasPlannedExpenses);
+            var hasRecurring = nature is not (ExpenseNature.Fixed or ExpenseNature.Loan)
+                && await db.HasRecurringPlannedExpensesAsync(book, id, cancellationToken);
+            book.ChangeExpenseNature(id, nature, hasPlannedExpenses, hasRecurring);
```

> `Book.EnsureUnreferenced` 的訊息是「已有交易或預定支出使用」，週期預定支出也屬於預定支出，訊息不改。

變異檢查：拿掉 `IsCategoryReferencedAsync` 新增的那一行，API 的刪除測試必須變紅。

- [ ] **Step 4：跑單檔測試** — Expected：`BookSettingsTests` 與 `SettingsMaintenanceEndpointsTests` 全綠（各多 3 條、2 條）。
- [ ] **Step 5：跑全部測試** — Expected：總計約 **473**，失敗 0。
- [ ] **Step 6：Commit**

```bash
git add src/SixJars.Domain/Books/Book.cs src/SixJars.Application/Books/SettingReferences.cs src/SixJars.Application/Books/UpdateSettings.cs tests/SixJars.Domain.Tests/Books/BookSettingsTests.cs tests/SixJars.Api.Tests/SettingsMaintenanceEndpointsTests.cs
git commit -m "feat(settings): 刪除與改支出性質時納入週期預定支出

被週期項目使用的分類、帳戶不能刪除；有週期項目的支出主分類只能在
固定與貸款之間切換（P4 K plan D6）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task K9：PlannedExpenseDto.SourceId 與備份 v3

**Files:** Modify: `PlannedExpenseDto.cs`、`BackupDocument.cs`、`ExportBackup.cs`、`RestoreBackup.cs`、`BackupExportTests.cs`、`RestoreBackupTests.cs`

前端需要 `sourceId` 才能標示「週期」並決定能不能改月份；備份也要帶上它（D3）。兩件事放在同一個 Task，是因為 `BackupPlannedExpense` 直接重用 `PlannedExpenseDto`，DTO 一加欄位，還原時的一致性檢查（`PlannedExpenseDto.From(planned, dto.Version) != dto`）就要求還原端也能重建 `SourceId`，也就是必須同時還原週期項目。

要注意三件事：

1. `BackupJson` 設定了 `RespectRequiredConstructorParameters = true`（J 的 D8），所以 `PlannedExpenseDto.SourceId` 與 `BackupDocument.RecurringPlannedExpenses` 都必須是**有預設值的選用參數**，v1、v2 的備份才能反序列化。
2. `RecurringPlannedExpenseDto.Months` 是集合，record 的 `==` 不會逐項比；還原的一致性檢查要分開比 `Months`。
3. 寫入順序：週期項目必須在預定支出之前寫入（FK）。放在同一次 `SaveChanges` 時，EF 會依 FK 排序 INSERT；但為了可讀性，仍然把週期項目放在交易之前 `AddRange`。

- [ ] **Step 1：寫失敗測試**

`BackupExportTests`：`CurrentFormatVersion` 的期望值從 2 改成 3（比照 J12：除了斷言本身，同一個測試的名稱與相關行一起改；Step 1 前用 `Select-String -Pattern "FormatVersion|2" tests/SixJars.Api.Tests/BackupExportTests.cs` 找出全部位置），並新增：

```csharp
    [Fact]
    public async Task Backup_contains_recurring_items_and_planned_expense_sources()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var item = await RecurringPlannedExpensesEndpointsTests.CreateAsync(client, book, RecurringPlannedExpensesEndpointsTests.Insurance(book));
        await PlannedExpenseGenerationEndpointsTests.GenerateAsync(client, book, 202601);

        var backup = (await client.GetFromJsonAsync<BackupDocument>(
            $"/api/books/{book.Id.Value}/export/backup.json", BackupJson.Options, Ct))!;

        backup.RecurringPlannedExpenses.Should().ContainSingle().Which.Id.Should().Be(item.Id);
        backup.PlannedExpenses.Should().ContainSingle().Which.PlannedExpense.SourceId.Should().Be(item.Id);
    }
```

> `BackupJson.Options` 的實際名稱與命名空間，Step 1 前在 `BackupExportTests` 既有的測試中確認。

`RestoreBackupTests`：

- 在既有的完整情境中加入 **(i)**：一個每年 1、7 月的週期項目、它在 1 月產生的預定支出（之後刪除）、它在 7 月產生的預定支出（未刪除）、一個已設結束月份的每月項目。還原後以 `ExportBackup` 再匯出一次，與原備份比對（既有的 round-trip 斷言方式）。
- 新增 `Restores_v2_backup_without_recurring_items`：用目前的 v2 測試資料（把 `FormatVersion` 改成 2、移除 `recurringPlannedExpenses` 屬性、移除每筆預定支出的 `sourceId`）還原成功。
- 新增損毀情境 `planned-expense-unknown-source`：預定支出的 `sourceId` 指向備份裡沒有的週期項目 → `DomainException`（「備份可能已損毀」）。
- format-version 損毀情境使用 `BackupDocument.CurrentFormatVersion + 1`（J12 已改成這個寫法，確認仍然成立）。

> `RestoreBackupTests` 的既有結構（情境 (a)–(h)、損毀情境的 Theory 資料、v1 測試的寫法）在 Step 1 前完整讀過再加，**以既有寫法為準**，不要另起一套 helper。

- [ ] **Step 2：跑測試確認失敗** — Expected：編譯失敗（`BackupDocument.RecurringPlannedExpenses`、`PlannedExpenseDto.SourceId` 不存在）。
- [ ] **Step 3：最小實作**

```diff
--- a/src/SixJars.Application/Planning/PlannedExpenseDto.cs
+++ b/src/SixJars.Application/Planning/PlannedExpenseDto.cs
@@
-/// <summary>預定支出的 API 輸出：輸入欄位加上 Id、付款連結與樂觀並行版本（修改、付款時帶回）。</summary>
+/// <summary>預定支出的 API 輸出：輸入欄位加上 Id、付款連結、來源週期項目與樂觀並行版本（修改、付款時帶回）。</summary>
 /// <param name="BudgetMonth">歸屬月份（yyyymm）。</param>
 /// <param name="EstimatedAmount">預估金額，沿用支出的符號慣例（負數）。</param>
+/// <param name="SourceId">產生它的週期項目；手動新增的為 null。選用參數：v1、v2 的備份沒有這個欄位（P4 K plan D3）。</param>
 public sealed record PlannedExpenseDto(
@@
     bool IsPaid,
-    uint Version)
+    uint Version,
+    Guid? SourceId = null)
 {
     public static PlannedExpenseDto From(PlannedExpense p, uint version) => new(
@@
         p.IsPaid,
-        version);
+        version,
+        p.SourceId?.Value);
 }
```

```diff
--- a/src/SixJars.Application/Backup/BackupDocument.cs
+++ b/src/SixJars.Application/Backup/BackupDocument.cs
@@
-/// 格式以 <see cref="FormatVersion"/> 標示；v2 起設定項目帶有 SortOrder 與 ArchivedAt，還原也接受 v1（P4 J plan D8）。
+/// 格式以 <see cref="FormatVersion"/> 標示；v2 起設定項目帶有 SortOrder 與 ArchivedAt（P4 J plan D8），
+/// v3 起帶有週期預定支出與預定支出的來源（P4 K plan D3）；還原接受 v1–v3。
@@
     IReadOnlyList<BackupMember> Members,
-    IReadOnlyList<AuditEntryDto> AuditEntries)
+    IReadOnlyList<AuditEntryDto> AuditEntries,
+    IReadOnlyList<RecurringPlannedExpenseDto>? RecurringPlannedExpenses = null)
 {
-    public const int CurrentFormatVersion = 2;
+    public const int CurrentFormatVersion = 3;
 }
```

`ExportBackup`：比照 `plannedExpenses` 的查詢，加上

```csharp
        // 要追蹤變更，db.GetVersion 才讀得到 xmin（同 ExportBackup.cs:32 的 plannedExpenses，沒有 AsNoTracking）。
        var recurring = await db.RecurringPlannedExpenses
            .Where(r => r.BookId == bookId).OrderBy(r => r.Id).ToListAsync(cancellationToken);
```

並在建構 `BackupDocument` 時傳入 `RecurringPlannedExpenses: [.. recurring.Select(r => RecurringPlannedExpenseDto.From(r, db.GetVersion(r)))]`。

`RestoreBackup`：

```diff
@@ Handle
         var book = RestoreBook(backup.Book, backup.FormatVersion);
+        var recurring = RestoreRecurringPlannedExpenses(book, backup.RecurringPlannedExpenses ?? []);
         var transactions = backup.Transactions.Select(t => RestoreTransaction(book, t)).ToList();
-        var plannedExpenses = RestorePlannedExpenses(book, backup.PlannedExpenses, transactions.ToDictionary(t => t.Transaction.Id));
+        var plannedExpenses = RestorePlannedExpenses(
+            book, backup.PlannedExpenses, transactions.ToDictionary(t => t.Transaction.Id), recurring.Select(r => r.Id).ToHashSet());
@@
+        // 週期項目在預定支出之前（預定支出的 SourceId 有 FK）。
+        db.RecurringPlannedExpenses.AddRange(recurring);
         db.Transactions.AddRange(transactions.Select(t => t.Transaction));
         db.PlannedExpenses.AddRange(plannedExpenses);
```

```csharp
    /// <summary>以 Domain 重建；Months 是集合，record 的 == 不逐項比，所以分開比對。</summary>
    private static List<RecurringPlannedExpense> RestoreRecurringPlannedExpenses(Book book, IReadOnlyList<RecurringPlannedExpenseDto> backups)
    {
        var restored = new List<RecurringPlannedExpense>();
        foreach (var dto in backups)
        {
            var item = RecurringPlannedExpense.Create(
                book, new CategoryId(dto.CategoryId), dto.AccountId is { } a ? new AccountId(a) : null, dto.DefaultAmount, dto.Note,
                dto.Frequency, dto.Months, BudgetMonth.FromKey(dto.StartMonth), dto.EndMonth is { } e ? BudgetMonth.FromKey(e) : null,
                new RecurringPlannedExpenseId(dto.Id));
            var rebuilt = RecurringPlannedExpenseDto.From(item, dto.Version);
            if (rebuilt with { Months = [] } != dto with { Months = [] } || !rebuilt.Months.SequenceEqual(dto.Months))
            {
                throw new DomainException($"週期預定支出 {dto.Id} 經 Domain 重建後與備份不一致，備份可能已損毀。");
            }

            restored.Add(item);
        }

        return restored;
    }
```

`RestorePlannedExpenses` 加上參數 `IReadOnlySet<RecurringPlannedExpenseId> recurringIds`，建立時傳入 `sourceId`，並在找不到來源時擲例外：

```diff
         foreach (var (dto, deletedAt) in backups)
         {
+            RecurringPlannedExpenseId? sourceId = dto.SourceId is { } s ? new RecurringPlannedExpenseId(s) : null;
+            if (sourceId is { } source && !recurringIds.Contains(source))
+            {
+                throw new DomainException($"預定支出 {dto.Id} 的來源週期項目 {dto.SourceId} 不在備份中，備份可能已損毀。");
+            }
+
             var planned = PlannedExpense.Create(
                 book, BudgetMonth.FromKey(dto.BudgetMonth), new CategoryId(dto.CategoryId),
-                dto.AccountId is { } accountId ? new AccountId(accountId) : null, dto.EstimatedAmount, dto.Note, new PlannedExpenseId(dto.Id));
+                dto.AccountId is { } accountId ? new AccountId(accountId) : null, dto.EstimatedAmount, dto.Note, new PlannedExpenseId(dto.Id),
+                sourceId);
```

> 還原時以 Domain 重建週期項目，等於重新套用「只限固定、貸款」的規則；備份當下若已違反（K8 之前產生的資料不可能，因為 K 段之前沒有週期項目），還原會失敗並回報，這是正確的行為。

變異檢查：(a) 把 `Months` 的逐項比對拿掉，改成 `rebuilt != dto`，`(i)` 情境必須變紅（證明 record 的參考相等確實會出事）；還原後 (b) 拿掉 `sourceId` 的傳入，round-trip 必須變紅。

- [ ] **Step 4：跑單檔測試** — Expected：`BackupExportTests`、`RestoreBackupTests` 全綠。
- [ ] **Step 5：跑全部測試** — Expected：總計約 **477**，失敗 0，略過 0；`dotnet build` 0 warning。
- [ ] **Step 6：Commit**

```bash
git add src/SixJars.Application/Planning/PlannedExpenseDto.cs src/SixJars.Application/Backup/BackupDocument.cs src/SixJars.Application/Backup/ExportBackup.cs src/SixJars.Application/Backup/RestoreBackup.cs tests/SixJars.Api.Tests/BackupExportTests.cs tests/SixJars.Infrastructure.Tests/Backup/RestoreBackupTests.cs
git commit -m "feat(backup): 備份 v3 包含週期預定支出與預定支出的來源

PlannedExpenseDto 加上 SourceId；還原接受 v1–v3，來源不在備份中時
視為損毀（P4 K plan D3）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### ═══ 後端 checkpoint ═══

- `dotnet build` 0 warning；`dotnet test` 全綠（預測約 477，略過 0，Acceptance 有實際執行且結果不變）。
- secrets 掃描：`git diff master --stat` 與 `git diff master` 中沒有連線字串、金鑰。
- 停下來讓使用者檢視；用 `docs(plans):` commit 回寫偏差到本文件最後的「執行結果與偏差」。

---

## Task K10：前端 DTO 與兩個 API service

**Files:** Create: `web/src/app/core/api/planned-expense-api.ts`（＋spec）、`web/src/app/core/api/recurring-planned-expense-api.ts`（＋spec）／Modify: `web/src/app/core/api/dto.ts`

URL 必須是 `/` 開頭的相對路徑（`TransactionApi` 的註解：Angular 的 XSRF interceptor 只對相對 URL 加 header）。`generate`、`refresh` 是 POST 加 query string，body 為 `null`。

- [ ] **Step 1：寫失敗測試**

```ts
// web/src/app/core/api/planned-expense-api.spec.ts
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PlannedExpenseApi } from './planned-expense-api';

describe('PlannedExpenseApi', () => {
  let api: PlannedExpenseApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(PlannedExpenseApi);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('lists_by_budget_month', () => {
    api.list('b1', 202604).subscribe();
    http.expectOne({ method: 'GET', url: '/api/books/b1/planned-expenses?budgetMonth=202604' }).flush([]);
  });

  it('generate_and_refresh_post_month_in_query_without_body', () => {
    api.generate('b1', 202604).subscribe();
    api.refresh('b1', 202604).subscribe();
    const generate = http.expectOne({ method: 'POST', url: '/api/books/b1/planned-expenses/generate?budgetMonth=202604' });
    const refresh = http.expectOne({ method: 'POST', url: '/api/books/b1/planned-expenses/refresh?budgetMonth=202604' });
    expect(generate.request.body).toBeNull();
    expect(refresh.request.body).toBeNull();
    generate.flush({ created: [], skipped: [] });
    refresh.flush({ updated: [], skipped: [] });
  });

  it('update_sends_version_and_input', () => {
    const input = { budgetMonth: 202604, categoryId: 'c', accountId: null, estimatedAmount: -100, note: null };
    api.update('b1', 'p1', 7, input).subscribe();
    const request = http.expectOne({ method: 'PUT', url: '/api/books/b1/planned-expenses/p1' });
    expect(request.request.body).toEqual({ version: 7, input });
    request.flush({});
  });

  it('delete_sends_version_in_query', () => {
    api.delete('b1', 'p1', 7).subscribe();
    http.expectOne({ method: 'DELETE', url: '/api/books/b1/planned-expenses/p1?version=7' }).flush(null);
  });

  it('pay_posts_body', () => {
    const body = { version: 3, date: '2026-04-05', accountId: 'a', amount: -100, loanAccountId: null, loanPrincipal: null };
    api.pay('b1', 'p1', body).subscribe();
    const request = http.expectOne({ method: 'POST', url: '/api/books/b1/planned-expenses/p1/pay' });
    expect(request.request.body).toEqual(body);
    request.flush({});
  });
});
```

`recurring-planned-expense-api.spec.ts`：同樣的形狀，涵蓋 `list`（GET）、`create`（POST body 即 input）、`update`（PUT `{ version, input }`）、`delete`（DELETE `?version=`），共 4 條。

- [ ] **Step 2：跑測試確認失敗** — Expected：找不到模組 `./planned-expense-api`。
- [ ] **Step 3：最小實作**

```ts
// dto.ts 追加
export interface PlannedExpenseInput {
  budgetMonth: number; categoryId: string; accountId: string | null; estimatedAmount: number; note: string | null;
}
export interface PlannedExpenseDto extends PlannedExpenseInput {
  id: string; paidTransactionId: string | null; isPaid: boolean; version: number; sourceId: string | null;
}
export interface PayPlannedExpenseBody {
  version: number; date: string; accountId: string; amount: number; loanAccountId: string | null; loanPrincipal: number | null;
}
export type RecurrenceFrequency = 'Monthly' | 'Yearly';
export interface RecurringPlannedExpenseInput {
  categoryId: string; accountId: string | null; defaultAmount: number; note: string | null;
  frequency: RecurrenceFrequency; months: number[]; startMonth: number; endMonth: number | null;
}
export interface RecurringPlannedExpenseDto extends RecurringPlannedExpenseInput { id: string; version: number }
export type RecurringSkipReason = 'AlreadyGenerated' | 'CategoryArchived' | 'AccountArchived' | 'NotDue';
export interface RecurringSkipDto { recurringId: string; plannedExpenseId: string | null; reason: RecurringSkipReason }
export interface GenerateResultDto { created: PlannedExpenseDto[]; skipped: RecurringSkipDto[] }
export interface RefreshResultDto { updated: PlannedExpenseDto[]; skipped: RecurringSkipDto[] }
```

```ts
// web/src/app/core/api/planned-expense-api.ts
import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  GenerateResultDto, PayPlannedExpenseBody, PlannedExpenseDto, PlannedExpenseInput, RefreshResultDto,
} from './dto';

// URL 必須是 '/' 開頭的相對路徑，Angular 的 XSRF interceptor 只對相對 URL 加 header
@Injectable({ providedIn: 'root' })
export class PlannedExpenseApi {
  private readonly http = inject(HttpClient);

  list(bookId: string, budgetMonth: number): Observable<PlannedExpenseDto[]> {
    return this.http.get<PlannedExpenseDto[]>(this.url(bookId), { params: month(budgetMonth) });
  }

  create(bookId: string, input: PlannedExpenseInput): Observable<PlannedExpenseDto> {
    return this.http.post<PlannedExpenseDto>(this.url(bookId), input);
  }

  update(bookId: string, id: string, version: number, input: PlannedExpenseInput): Observable<PlannedExpenseDto> {
    return this.http.put<PlannedExpenseDto>(`${this.url(bookId)}/${id}`, { version, input });
  }

  delete(bookId: string, id: string, version: number): Observable<void> {
    return this.http.delete<void>(`${this.url(bookId)}/${id}`, { params: new HttpParams().set('version', version) });
  }

  pay(bookId: string, id: string, body: PayPlannedExpenseBody): Observable<unknown> {
    return this.http.post(`${this.url(bookId)}/${id}/pay`, body);
  }

  // 明確的產生與以現值更新（ADR 0009）
  generate(bookId: string, budgetMonth: number): Observable<GenerateResultDto> {
    return this.http.post<GenerateResultDto>(`${this.url(bookId)}/generate`, null, { params: month(budgetMonth) });
  }

  refresh(bookId: string, budgetMonth: number): Observable<RefreshResultDto> {
    return this.http.post<RefreshResultDto>(`${this.url(bookId)}/refresh`, null, { params: month(budgetMonth) });
  }

  private url(bookId: string): string {
    return `/api/books/${bookId}/planned-expenses`;
  }
}

function month(budgetMonth: number): HttpParams {
  return new HttpParams().set('budgetMonth', budgetMonth);
}
```

`recurring-planned-expense-api.ts`：同樣形狀，base URL 是 `/api/books/${bookId}/recurring-planned-expenses`。

- [ ] **Step 4：跑單檔測試** — Expected：9 passed（兩個檔案）。
- [ ] **Step 5：跑全部測試** — Expected：**241 passed（30 個檔案）**。
- [ ] **Step 6：Commit**

```bash
git add web/src/app/core/api/dto.ts web/src/app/core/api/planned-expense-api.ts web/src/app/core/api/planned-expense-api.spec.ts web/src/app/core/api/recurring-planned-expense-api.ts web/src/app/core/api/recurring-planned-expense-api.spec.ts
git commit -m "feat(web): 預定支出與週期預定支出的 DTO 與 API

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task K11：planned-expense-rules（純函式）

**Files:** Create: `web/src/app/features/planned-expenses/planned-expense-rules.ts`（＋spec）

頁面與對話框的所有判斷集中在這裡，元件只負責接線。容易錯的地方：

- 分類標籤是「主 › 子」，要從 `book.categories` 找主分類。
- 分組的性質從**分類**查（子分類的 nature 跟主分類相同），不是從預定支出本身。
- 選項排除已封存（自己或主分類），但編輯時要保留目前使用的那一個（J 的 D7 精神，對應 `transaction-rules` 的 `keep`）。
- 摘要訊息要能合併多種略過原因。

- [ ] **Step 1：寫失敗測試**

```ts
// web/src/app/features/planned-expenses/planned-expense-rules.spec.ts
import { BookDto, GenerateResultDto, PlannedExpenseDto, RecurringPlannedExpenseDto } from '../../core/api/dto';
import { BOOK } from '../transactions/testing/book-fixture';
import {
  categoryOptions, describeRecurrence, generationMessage, loanAccounts, plannedGroups, refreshMessage,
} from './planned-expense-rules';

// BOOK 的分類只有浮動（飲食／午餐）與收入；這裡補上固定、貸款、特別各一組
const book: BookDto = {
  ...BOOK,
  categories: [
    ...BOOK.categories,
    { id: 'fix', name: '固定支出', kind: 'Expense', nature: 'Fixed', parentId: null, sortOrder: 1, archivedAt: null },
    { id: 'ins', name: '保險費', kind: 'Expense', nature: 'Fixed', parentId: 'fix', sortOrder: 0, archivedAt: null },
    { id: 'old', name: '舊保單', kind: 'Expense', nature: 'Fixed', parentId: 'fix', sortOrder: 1, archivedAt: '2026-01-01T00:00:00Z' },
    { id: 'loan', name: '貸款支出', kind: 'Expense', nature: 'Loan', parentId: null, sortOrder: 2, archivedAt: null },
    { id: 'gift', name: '禮金', kind: 'Expense', nature: 'Special', parentId: null, sortOrder: 3, archivedAt: null },
  ],
};

const planned = (over: Partial<PlannedExpenseDto>): PlannedExpenseDto => ({
  id: 'p', budgetMonth: 202604, categoryId: 'ins', accountId: null, estimatedAmount: -100, note: null,
  paidTransactionId: null, isPaid: false, version: 1, sourceId: null, ...over,
});

describe('plannedGroups', () => {
  it('groups_by_category_nature_in_fixed_loan_special_order_and_totals_unpaid', () => {
    const groups = plannedGroups([
      planned({ id: 'g', categoryId: 'gift', estimatedAmount: -500 }),
      planned({ id: 'a', categoryId: 'ins', estimatedAmount: -100 }),
      planned({ id: 'b', categoryId: 'ins', estimatedAmount: -200, isPaid: true, paidTransactionId: 't' }),
      planned({ id: 'l', categoryId: 'loan', estimatedAmount: -3000, sourceId: 'r1' }),
    ], book);

    expect(groups.map(g => g.title)).toEqual(['固定支出', '貸款支出', '特別支出']);
    expect(groups[0].rows.map(r => [r.planned.id, r.categoryLabel, r.amount])).toEqual([
      ['a', '固定支出 › 保險費', 100], ['b', '固定支出 › 保險費', 200],
    ]);
    expect(groups[0].unpaidTotal).toBe(100);
    expect(groups[1].rows[0].isRecurring).toBe(true);
  });

  it('omits_empty_groups', () => {
    expect(plannedGroups([planned({ categoryId: 'gift' })], book).map(g => g.title)).toEqual(['特別支出']);
  });
});

describe('categoryOptions', () => {
  it('lists_usable_categories_of_given_natures_and_keeps_current_archived_one', () => {
    expect(categoryOptions(book, ['Fixed', 'Loan']).map(o => o.id)).toEqual(['fix', 'ins', 'loan']);
    expect(categoryOptions(book, ['Fixed'], 'old').map(o => o.id)).toEqual(['fix', 'ins', 'old']);
  });
});

describe('loanAccounts', () => {
  it('lists_unarchived_loan_accounts', () => {
    expect(loanAccounts(BOOK).map(a => a.id)).toEqual(['acc-loan']);
  });
});

describe('generationMessage', () => {
  const result = (created: number, reasons: GenerateResultDto['skipped'][number]['reason'][]): GenerateResultDto => ({
    created: Array.from({ length: created }, (_, i) => planned({ id: `p${i}` })),
    skipped: reasons.map(reason => ({ recurringId: 'r', plannedExpenseId: null, reason })),
  });

  it('summarizes_created_and_skipped_by_reason', () => {
    expect(generationMessage(result(5, ['CategoryArchived']))).toBe('建立 5 筆，略過 1 筆：分類已封存');
    expect(generationMessage(result(0, ['AlreadyGenerated', 'AlreadyGenerated', 'AccountArchived'])))
      .toBe('建立 0 筆，略過 3 筆：已產生過 2、帳戶已封存 1');
  });

  it('says_nothing_to_generate_when_both_are_empty', () => {
    expect(generationMessage(result(0, []))).toBe('本月沒有需要產生的週期項目');
  });
});

describe('refreshMessage', () => {
  it('summarizes_updated_and_not_due', () => {
    expect(refreshMessage({ updated: [planned({})], skipped: [{ recurringId: 'r', plannedExpenseId: 'p', reason: 'NotDue' }] }))
      .toBe('更新 1 筆，略過 1 筆：本月已不適用');
    expect(refreshMessage({ updated: [], skipped: [] })).toBe('沒有需要更新的預定支出');
  });
});

describe('describeRecurrence', () => {
  const item = (over: Partial<RecurringPlannedExpenseDto>): RecurringPlannedExpenseDto => ({
    id: 'r', categoryId: 'ins', accountId: null, defaultAmount: -100, note: null,
    frequency: 'Monthly', months: [], startMonth: 202601, endMonth: null, version: 1, ...over,
  });

  it('describes_frequency_and_range', () => {
    expect(describeRecurrence(item({}))).toBe('每月，2026/01 起');
    expect(describeRecurrence(item({ frequency: 'Yearly', months: [1, 7], endMonth: 202712 })))
      .toBe('每年 1、7 月，2026/01–2027/12');
  });
});
```

> `BOOK` 的帳戶 Id（`acc-loan`）與月份格式（`formatBudgetMonth` 的輸出是不是 `2026/01`）在 Step 1 前對照 `book-fixture.ts` 與 `shared/dates.ts`，以實際值修正期望字串。

- [ ] **Step 2：跑測試確認失敗** — Expected：找不到模組 `./planned-expense-rules`。
- [ ] **Step 3：最小實作**

```ts
// web/src/app/features/planned-expenses/planned-expense-rules.ts
import {
  AccountDto, BookDto, CategoryDto, ExpenseNature, GenerateResultDto, PlannedExpenseDto, RecurringPlannedExpenseDto,
  RecurringSkipDto, RecurringSkipReason, RefreshResultDto,
} from '../../core/api/dto';
import { formatBudgetMonth } from '../../shared/dates';

export interface PlannedRow {
  planned: PlannedExpenseDto;
  categoryLabel: string;
  accountName: string | null;
  amount: number;          // 正數，顯示用
  isRecurring: boolean;
}

export interface PlannedGroup { nature: ExpenseNature; title: string; rows: PlannedRow[]; unpaidTotal: number }

const GROUP_ORDER: { nature: ExpenseNature; title: string }[] = [
  { nature: 'Fixed', title: '固定支出' },
  { nature: 'Loan', title: '貸款支出' },
  { nature: 'Special', title: '特別支出' },
];

const SKIP_LABELS: Record<RecurringSkipReason, string> = {
  AlreadyGenerated: '已產生過',
  CategoryArchived: '分類已封存',
  AccountArchived: '帳戶已封存',
  NotDue: '本月已不適用',
};

export function categoryLabel(book: BookDto, categoryId: string): string {
  const category = book.categories.find(c => c.id === categoryId);
  if (!category) return '';
  const parent = category.parentId ? book.categories.find(c => c.id === category.parentId) : undefined;
  return parent ? `${parent.name} › ${category.name}` : category.name;
}

// 預定支出依分類的支出性質分組，順序固定為固定、貸款、特別；空的組不列出
export function plannedGroups(planned: PlannedExpenseDto[], book: BookDto): PlannedGroup[] {
  const natureOf = (categoryId: string) => book.categories.find(c => c.id === categoryId)?.nature ?? null;
  return GROUP_ORDER
    .map(({ nature, title }) => {
      const rows = planned
        .filter(p => natureOf(p.categoryId) === nature)
        .map(p => ({
          planned: p,
          categoryLabel: categoryLabel(book, p.categoryId),
          accountName: book.accounts.find(a => a.id === p.accountId)?.name ?? null,
          amount: Math.abs(p.estimatedAmount),
          isRecurring: p.sourceId !== null,
        }));
      const unpaidTotal = rows.filter(r => !r.planned.isPaid).reduce((sum, r) => sum + r.amount, 0);
      return { nature, title, rows, unpaidTotal };
    })
    .filter(group => group.rows.length > 0);
}

// 自己與主分類都未封存才可選；keep 是編輯中正在使用的分類，即使已封存也保留
export function categoryOptions(book: BookDto, natures: ExpenseNature[], keep?: string): CategoryDto[] {
  const byId = new Map(book.categories.map(c => [c.id, c]));
  const usable = (c: CategoryDto) =>
    c.archivedAt === null && (c.parentId === null || byId.get(c.parentId)?.archivedAt === null);
  const ordered: CategoryDto[] = [];
  const mains = book.categories
    .filter(c => c.kind === 'Expense' && c.parentId === null && c.nature !== null && natures.includes(c.nature))
    .sort((a, b) => a.sortOrder - b.sortOrder);
  for (const main of mains) {
    ordered.push(main);
    ordered.push(...book.categories.filter(c => c.parentId === main.id).sort((a, b) => a.sortOrder - b.sortOrder));
  }
  return ordered.filter(c => usable(c) || c.id === keep);
}

export function loanAccounts(book: BookDto): AccountDto[] {
  return book.accounts.filter(a => a.type === 'Loan' && a.archivedAt === null);
}

export function generationMessage(result: GenerateResultDto): string {
  if (result.created.length === 0 && result.skipped.length === 0) return '本月沒有需要產生的週期項目';
  return `建立 ${result.created.length} 筆${skippedSuffix(result.skipped)}`;
}

export function refreshMessage(result: RefreshResultDto): string {
  if (result.updated.length === 0 && result.skipped.length === 0) return '沒有需要更新的預定支出';
  return `更新 ${result.updated.length} 筆${skippedSuffix(result.skipped)}`;
}

// 單一原因只寫原因；多種原因時附上各自的筆數
function skippedSuffix(skipped: RecurringSkipDto[]): string {
  if (skipped.length === 0) return '';
  const counts = new Map<RecurringSkipReason, number>();
  for (const { reason } of skipped) counts.set(reason, (counts.get(reason) ?? 0) + 1);
  const reasons = counts.size === 1
    ? SKIP_LABELS[[...counts.keys()][0]]
    : [...counts].map(([reason, count]) => `${SKIP_LABELS[reason]} ${count}`).join('、');
  return `，略過 ${skipped.length} 筆：${reasons}`;
}

export function describeRecurrence(item: RecurringPlannedExpenseDto): string {
  const frequency = item.frequency === 'Monthly' ? '每月' : `每年 ${item.months.join('、')} 月`;
  const range = item.endMonth === null
    ? `${formatBudgetMonth(item.startMonth)} 起`
    : `${formatBudgetMonth(item.startMonth)}–${formatBudgetMonth(item.endMonth)}`;
  return `${frequency}，${range}`;
}
```

變異檢查：(a) `categoryOptions` 拿掉主分類的封存判斷，若測試資料沒有「主分類封存、子分類未封存」的情境就不會變紅——在 spec 加一條 `excludes_sub_categories_of_archived_main` 補上；(b) `plannedGroups` 改用預定支出的分類**自己**的 nature 也應該仍綠（fixture 中子分類的 nature 與主分類相同），所以不用為此另加測試。

- [ ] **Step 4：跑單檔測試** — Expected：約 11 passed。
- [ ] **Step 5：跑全部測試** — Expected：約 **252 passed（31 個檔案）**。
- [ ] **Step 6：Commit**

```bash
git add web/src/app/features/planned-expenses/planned-expense-rules.ts web/src/app/features/planned-expenses/planned-expense-rules.spec.ts
git commit -m "feat(web): 預定支出頁的分組、選項與摘要訊息

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task K12：PayDialog（付款對話框）

**Files:** Create: `web/src/app/features/planned-expenses/pay-dialog.ts`（＋spec）

預設值：日期＝今天、金額＝預估金額的絕對值、付款帳戶＝預定支出的帳戶。貸款性質另外顯示「貸款帳戶」（只有一個未封存的貸款帳戶時預選，D2）與「本金」（**必填、不預填**，Q20）。結果回傳 `Omit<PayPlannedExpenseBody, 'version'>`，金額轉成負數；非貸款時 `loanAccountId`、`loanPrincipal` 一律 `null`（後端要求必須留空）。

對話框的寫法（`MAT_DIALOG_DATA`、reactive form、`matButton`、harness 測試）比照 `features/settings/setting-dialog.ts` 與它的 spec；Step 1 前先讀這兩個檔案。

- [ ] **Step 1：寫失敗測試**（以 `MatDialog.open` 開啟、用 harness 操作；以下列出要涵蓋的 4 條）

1. `defaults_to_today_estimated_amount_and_planned_account`：固定性質，打開後日期是今天（用 `vi.setSystemTime` 固定）、金額 100、帳戶是預定支出的帳戶；按「付款」後 `afterClosed()` 得到 `{ date, accountId, amount: -100, loanAccountId: null, loanPrincipal: null }`。
2. `loan_requires_principal_and_preselects_the_only_loan_account`：貸款性質，貸款帳戶已預選；本金空白時「付款」按鈕 disabled；填 2000 後結果帶 `loanAccountId`、`loanPrincipal: 2000`。
3. `non_loan_hides_loan_fields`：固定性質時畫面上沒有本金欄位。
4. `cancel_closes_without_result`。

- [ ] **Step 2：跑測試確認失敗** — Expected：找不到模組 `./pay-dialog`。
- [ ] **Step 3：最小實作** — `PayDialogData { planned: PlannedExpenseDto; book: BookDto; today: string }`（`today` 由頁面以 `toDateString(new Date())` 傳入，對話框不自己讀時鐘，測試較穩）；`isLoan = book 分類的 nature === 'Loan'`；表單：`date`（required）、`amount`（required, min 0.01）、`accountId`（required，選項為未封存帳戶＋保留目前的）、`loanAccountId`／`loanPrincipal`（只在貸款時加 required）。
- [ ] **Step 4：跑單檔測試** — Expected：4 passed。
- [ ] **Step 5：跑全部測試** — Expected：約 **256 passed**。
- [ ] **Step 6：Commit**

```bash
git add web/src/app/features/planned-expenses/pay-dialog.ts web/src/app/features/planned-expenses/pay-dialog.spec.ts
git commit -m "feat(web): 預定支出付款對話框

貸款性質要選貸款帳戶（唯一時預選）並手動輸入本金（P4 K plan D2、Q20）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task K13：PlannedExpenseDialog（預定支出新增／修改）

**Files:** Create: `web/src/app/features/planned-expenses/planned-expense-dialog.ts`（＋spec）

欄位：分類（`categoryOptions(book, ['Fixed', 'Loan', 'Special'], keep)`）、帳戶（可不選）、金額（正數輸入）、備註。**不提供月份欄位**：新增時用頁面目前的月份，修改時沿用原本的月份（D1；也避免把預定支出搬進鎖定的月份）。結果是 `PlannedExpenseInput`（金額轉負數）。

- [ ] **Step 1：寫失敗測試**（3 條）

1. `add_returns_input_for_page_month_with_negative_amount`。
2. `edit_prefills_and_keeps_archived_current_category`：編輯一筆使用已封存分類的預定支出，分類選單仍包含它。
3. `amount_must_be_positive`：輸入 0 時「儲存」disabled。

- [ ] **Step 2–6**：同前。單檔 3 passed；全部約 **259 passed**。

```bash
git add web/src/app/features/planned-expenses/planned-expense-dialog.ts web/src/app/features/planned-expenses/planned-expense-dialog.spec.ts
git commit -m "feat(web): 預定支出新增與修改對話框

不提供月份欄位：新增用頁面月份，修改沿用原月份（P4 K plan D1）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task K14：RecurringDialog（週期項目新增／修改）

**Files:** Create: `web/src/app/features/planned-expenses/recurring-dialog.ts`（＋spec）

欄位：分類（`categoryOptions(book, ['Fixed', 'Loan'], keep)`）、帳戶、金額、備註、頻率（每月／每年）、月份（`mat-select multiple`，只在「每年」時顯示且至少選一個）、開始月份、結束月份（可空）。月份輸入用 `<input type="month">`，值是 `yyyy-MM`，送出前轉成 yyyymm；轉換函式放在 `planned-expense-rules.ts`（`monthInputToKey`、`keyToMonthInput`）並在該 spec 補兩條測試。

- [ ] **Step 1：寫失敗測試**（3 條＋rules 的 2 條）

1. `monthly_returns_empty_months`：選每月，結果 `months: []`。
2. `yearly_requires_at_least_one_month`：選每年但沒有選月份時「儲存」disabled；選 1、7 月後結果 `months: [1, 7]`。
3. `end_month_before_start_is_invalid`：結束月份早於開始月份時「儲存」disabled（前端先擋，後端仍會回 422）。

- [ ] **Step 2–6**：同前。全部約 **264 passed**。

```bash
git add web/src/app/features/planned-expenses/recurring-dialog.ts web/src/app/features/planned-expenses/recurring-dialog.spec.ts web/src/app/features/planned-expenses/planned-expense-rules.ts web/src/app/features/planned-expenses/planned-expense-rules.spec.ts
git commit -m "feat(web): 週期預定支出新增與修改對話框

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task K15：預定支出頁「本月」分頁

**Files:** Create: `web/src/app/features/planned-expenses/planned-expenses.page.ts`、`.html`、`.scss`（＋spec）／Modify: `web/src/app/core/errors/messages.ts`

月份放在 query string `month`，換月與記帳頁一致（`MonthNav`＋`router.navigate([], { queryParams: { month }, queryParamsHandling: 'merge' })`，參考 `transactions.page.ts:71`）。清單以 `toSignal(toObservable(...).pipe(switchMap(...)))` 依 `bookId`、`month`、`reload` 載入（同 `transactions.page.ts:64`）。

錯誤處理沿用設定頁 `run()` 的結構，另外加上 conflict：

| ApiError | 處理 |
|---|---|
| `domain` | `code === 'locked'` 時顯示「這個月份已鎖帳，不能異動」；其他顯示後端訊息 |
| `validation` | 顯示第一個欄位錯誤 |
| `conflict` | 顯示 `PLANNED_CONFLICT_MESSAGE`（「這筆預定支出已在其他裝置修改」），重新載入 |
| `notFound` | 顯示「這筆預定支出已不存在」，重新載入 |

「產生本月」「以現值更新」在請求進行中 disabled（D11），完成後以 `generationMessage`／`refreshMessage` 顯示 snackbar 並重新載入。

每列：分類標籤、帳戶、金額、備註、「已付／未付」標記、來源為週期項目時的小標記；⋮ 選單：未付時「付款」「修改」「刪除」，已付時只有「刪除」（刪除已付款的預定支出不影響付款交易，`PlannedExpense.Delete` 的註解）。

- [ ] **Step 1：寫失敗測試**（setup 比照 `settings.page.spec.ts`：先 `CurrentBook.load` 並 flush `BOOK`，再建立元件；路由用 `provideRouter([])`＋`ActivatedRoute` 的 query `month=202604`）

1. `loads_month_and_groups_by_nature`：GET `?budgetMonth=202604` 回兩筆（固定、貸款），畫面上依序出現兩個分組標題。
2. `generate_shows_summary_and_reloads`：按「產生本月」→ POST generate → flush `{ created: [x], skipped: [CategoryArchived] }` → notifier 收到「建立 1 筆，略過 1 筆：分類已封存」→ 再次 GET 清單。
3. `generate_button_is_disabled_while_pending`。
4. `pay_opens_dialog_and_posts_with_version`：點某列的「付款」→ 對話框按「付款」→ POST pay 的 body 帶該列的 `version`。
5. `locked_month_shows_locked_message`：generate 回 422 `code: 'locked'`。
6. `conflict_on_delete_reloads`：DELETE 回 409 → 顯示 `PLANNED_CONFLICT_MESSAGE` 並重新 GET。

- [ ] **Step 2：跑測試確認失敗** — Expected：找不到模組 `./planned-expenses.page`。
- [ ] **Step 3：最小實作**：頁面骨架為 `<mat-tab-group>`，第一個分頁「本月」；第二個分頁「週期項目」先放空的 `<mat-tab>`，K16 再填內容。
- [ ] **Step 4：跑單檔測試** — Expected：6 passed。
- [ ] **Step 5：跑全部測試** — Expected：約 **270 passed**。
- [ ] **Step 6：Commit**

```bash
git add web/src/app/features/planned-expenses/planned-expenses.page.ts web/src/app/features/planned-expenses/planned-expenses.page.html web/src/app/features/planned-expenses/planned-expenses.page.scss web/src/app/features/planned-expenses/planned-expenses.page.spec.ts web/src/app/core/errors/messages.ts
git commit -m "feat(web): 預定支出頁的本月清單、產生、更新與付款

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task K16：「週期項目」分頁、路由與導覽

**Files:** Modify: `planned-expenses.page.ts`、`.html`、`.spec.ts`、`web/src/app/app.routes.ts`、`web/src/app/app.html`、`web/src/app/app.spec.ts`

週期項目清單在切到這個分頁時才載入（`MatTabGroup` 的內容延遲掛載，測試要等轉場結束，見 J18 的 `openTab`）。每列顯示分類標籤、`describeRecurrence`、金額、帳戶、備註；結束月份早於本月的標示「已結束」並以灰色顯示。⋮ 選單「修改」「刪除」；刪除被參照時顯示後端的 422 訊息（D12）。

- [ ] **Step 1：寫失敗測試**

`planned-expenses.page.spec.ts` 追加：

1. `recurring_tab_lists_items_with_description`。
2. `delete_in_use_shows_backend_message`：DELETE 回 422 `code: 'in-use'`、`detail: '這個週期項目已產生過預定支出，不能刪除；請改設結束月份。'`，notifier 收到這段訊息。
3. `edit_puts_version_and_input`。

`app.spec.ts`：`shell_shows_book_name_email_and_logout` 的連結清單改成

```ts
expect(hrefs).toEqual(['/books/b1/transactions', '/books/b1/summary', '/books/b1/planned-expenses', '/books/b1/settings']);
```

- [ ] **Step 2：跑測試確認失敗** — Expected：`app.spec.ts` 的連結清單不符；頁面的 3 條找不到週期項目分頁的內容。
- [ ] **Step 3：最小實作**

```diff
--- a/web/src/app/app.routes.ts
+++ b/web/src/app/app.routes.ts
@@
       {
         path: 'summary',
         loadComponent: () => import('./features/summary/summary.page').then((m) => m.SummaryPage),
       },
+      {
+        path: 'planned-expenses',
+        loadComponent: () => import('./features/planned-expenses/planned-expenses.page').then((m) => m.PlannedExpensesPage),
+      },
       {
         path: 'settings',
```

`app.html`：在「總覽」與「設定」之間加「預定支出」連結（寫法比照既有的 `routerLink`；Step 3 前先讀 `app.html`）。

- [ ] **Step 4：跑單檔測試** — Expected：頁面 9 passed、`app.spec.ts` 2 passed。
- [ ] **Step 5：跑全部測試** — Expected：約 **273 passed**。另外跑 `npx ng build`，確認 `planned-expenses-page` 是獨立的 lazy chunk、沒有 warning。
- [ ] **Step 6：Commit**

```bash
git add web/src/app/features/planned-expenses/planned-expenses.page.ts web/src/app/features/planned-expenses/planned-expenses.page.html web/src/app/features/planned-expenses/planned-expenses.page.spec.ts web/src/app/app.routes.ts web/src/app/app.html web/src/app/app.spec.ts
git commit -m "feat(web): 週期項目分頁、預定支出路由與導覽連結

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task K17：預定支出 E2E

**Files:** Create: `web/e2e/planned-expenses.spec.ts`

以 `page.route` 模擬 API（比照 `e2e/settings.spec.ts` 與 `e2e/fixtures.ts`；`BOOK` 已在 J19 export）。一條測試走完主要流程：

1. 進入 `/books/<id>/planned-expenses?month=202604`，GET 清單回空陣列。
2. 按「產生本月」：攔截 POST generate，確認有 `X-XSRF-TOKEN` header，回 `{ created: [固定性質一筆], skipped: [] }`；之後的 GET 回這一筆。
3. snackbar 顯示「建立 1 筆」。
4. 從該列 ⋮ 選「付款」，對話框按「付款」：攔截 POST pay，確認 body 的 `amount` 是負數、`version` 是該筆的版本。

> fixture 的分類只有浮動與收入（J19 的教訓：fixture 不一定有測試需要的資料）。這條測試在 route handler 裡回傳**加上固定分類的帳本**，不修改共用的 `BOOK`。分頁名稱「本月」「週期項目」沒有子字串衝突，但仍然用 `exact: true`。

- [ ] **Step 2：跑測試確認失敗** — 先在 K16 之前的 commit 上跑不到這個頁面（404 導回首頁）即為正確的失敗。
- [ ] **Step 4／5**：`npx playwright test` — Expected：**6 passed**。
- [ ] **Step 6：Commit**

```bash
git add web/e2e/planned-expenses.spec.ts
git commit -m "test(e2e): 預定支出產生與付款流程

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### ═══ 前端 checkpoint ═══

- `npx ng test --watch=false` 全綠（預測約 273）；`npx playwright test` 6 passed；`npx ng build` 0 warning。
- 後端重跑 `dotnet test` 仍全綠（前端階段不應改到後端）。
- 停下來讓使用者檢視；用 `docs(plans):` commit 回寫偏差。

---

## 完成後的驗證

- [ ] `dotnet build SixJars.slnx`：0 warning。
- [ ] `dotnet test --solution SixJars.slnx`：失敗 0、略過 0（預測約 477）。
- [ ] `npx ng test --watch=false`：失敗 0（預測約 273）。
- [ ] `npx playwright test`：6 passed。
- [ ] `npx ng build`：成功，`planned-expenses-page` 是獨立的 lazy chunk。
- [ ] `git status`：乾淨；`git log master..` 有 17 個 Task commit＋本計畫與回寫的 `docs(plans):` commit。
- [ ] secrets 掃描：`git diff master` 沒有連線字串、金鑰。

## 手動驗證（自動測試涵蓋不到）

用真實資料庫的副本，照 P3 的本機 production build 流程執行：

1. 套用 migration `AddRecurringPlannedExpenses`，確認既有的預定支出全部 `SourceId IS NULL`、總覽頁的月可用餘額不變。
2. 建立「房屋貸款，每月」與「保險費，每年 3、9 月」兩個週期項目；對下個月按「產生本月」，確認只產生房貸、總覽頁的月可用餘額減少對應金額。
3. 刪除剛產生的房貸預定支出，再按一次「產生本月」：不應該回來，snackbar 顯示「略過 1 筆：已產生過」。
4. 修改房貸週期項目的金額，按「以現值更新」：未付的那筆金額改變；付款後再改金額、再按更新：已付的不變。
5. 貸款付款：對話框要選貸款帳戶、本金必填；付款後記帳頁出現一筆貸款繳款，總覽頁的貸款餘額正確。
6. 嘗試刪除已產生過的週期項目：顯示「請改設結束月份」；設結束月份為本月後，下個月按產生不再出現。
7. 下載備份，用 CLI 還原到空資料庫，確認週期項目與預定支出的來源都還在，且對已產生的月份再按「產生」仍回報「已產生過」。

## 後續（不在本計畫內）

- 同時產生造成的唯一索引衝突回 409 而不是 500（D11）。
- 舊 Excel 匯入時把「制式表格」轉成週期項目（目前匯入只建立預定支出，`MappingSession.Templates`）。
- 預定支出與週期項目的權限分級（spec §11：記帳者與唯讀角色）。
- L 段：預算；`ChangeExpenseNature` 的預算檢查。M 段：報表的「未付預定」小計會用到本段的預定支出查詢。

---

## 附錄：核准前事實查核（2026-10-07，**部分完成**）

寫計畫時已查證：

| 引用 | 存在? | 證據 | 修正 |
|---|---|---|---|
| `PlannedExpense` 欄位與 `Create(..., id)` | ✅ | `PlannedExpense.cs:41-53` | — |
| `PlannedExpense.Update` 會檢查鎖帳日嗎 | ❌ 由 handler 檢查 | `UpdatePlannedExpense.cs:35-36` | generate／refresh 的 handler 自己呼叫 `EnsureUnlocked(month)` |
| 貸款付款需要 `LoanAccountId` | ✅ | `PayPlannedExpense.cs:82` | spec 漏列 → D2 |
| `BackupPlannedExpense` 重用 `PlannedExpenseDto`；還原以 `!=` 比對 DTO | ✅ | `BackupDocument.cs:30`、`RestoreBackup.cs:219` | → K9 的注意事項 1、2 |
| `BudgetMonth` 有比較運算子 | ✅ | `BudgetMonth.cs:24-27` | — |
| enum 以字串存放 | ✅ | `BookConfiguration.cs:24,43,44` | — |
| `Book.IsCategoryArchived` 或類似 helper | ❌ 不存在 | `Grep IsSelectable|IsArchived` 只找到各實體的 `IsArchived` | K3 新增 |
| `ChangeExpenseNature(id, nature, hasPlannedExpenses)` | ✅ | `Book.cs:240`、`UpdateSettings.cs:74-76` | K8 加選用參數 |
| `SettingReferences` 的三個 `Is*ReferencedAsync` | ✅ | `SettingReferences.cs:22-32` | K8 |
| `DomainException.Code`、`InUseCode`、`RuleCode`、`LockedCode` | ✅ | `DomainException.cs:7-16`、`BookSettingsTests.cs:54` | — |
| `postgres.CreateDatabaseAsync(Ct)` | ✅ | `LedgerPersistenceTests.cs:25` | — |
| `SeedBookAsync` 的分類與帳戶 | ✅ | `ApiSeed.cs:21-33` | — |
| 前端 `MonthNav`、`addMonths`、`formatBudgetMonth`、`ApiError` 的 kind | ✅ | `month-nav.ts`、`dates.ts`、`api-error.ts` | — |
| `CONFLICT_MESSAGE` 的文字寫死「這筆交易」 | ✅ | `messages.ts:2` | K15 新增 `PLANNED_CONFLICT_MESSAGE` |
| 所有標 Create 的檔案尚未存在 | ✅ | `Glob web/src/app/**`、`features/` 只有 denied、home、settings、summary、transactions | — |

**尚未查證、開始執行前必須補完**（本 session 的讀檔數已接近上限，依全域指示停在這裡）：

2026-10-07 補查（使用者要求以 unit test 查核；`tests/SixJars.Infrastructure.Tests/Persistence/PlanKEfAssumptionTests.cs`，對真實 PostgreSQL 17 執行，3 passed，留作回歸測試）：

| 引用 | 結果 | 證據 | 變異檢查 |
|---|---|---|---|
| field-only `int[] _months` → `integer[]`、Ignore 公開的 `Months`、私有無參數建構子、換陣列後偵測得到變更 | ✅ | `Field_only_int_array_maps_to_integer_array_and_tracks_replacement` | 拿掉 `Property<int[]>("_months")` → 變紅 |
| 可空強型別 Id 的 `.Value` 在投影中可翻譯（以 `AccountId!.Value` 代替 `SourceId!.Value`） | ✅ | `Nullable_strongly_typed_id_value_is_translated_in_projection` | —（翻譯成功與否本身就是結果） |
| `ExpectVersion` 後 `Remove`，DELETE 帶 xmin 條件 | ✅ | `Remove_after_expect_version_checks_xmin`（實體在對方修改**之後**才載入，只有 ExpectVersion 能讓它失敗） | 拿掉 `ExpectVersion` → 變紅 |
| `Program.cs` 的串接方式 | ✅ | `Program.cs:41` 是 `api.MapBooksEndpoints()...MapPlannedExpensesEndpoints()...` 的鏈 | K4 已改成具體 diff |
| `BackupJson.Options` | ✅ | `src/SixJars.Application/Backup/BackupJson.cs:15` | — |
| `ExportBackup` 的預定支出有追蹤（為了 `GetVersion`） | ✅ 追蹤，沒有 `AsNoTracking` | `ExportBackup.cs:32` | K9 的週期項目查詢改成同樣追蹤 |

- [x] ~~Npgsql 對 field-only 的 `int[]` 屬性的對應~~（上表）
- [x] ~~EF 能否翻譯 `p.SourceId!.Value`~~（上表）
- [x] ~~`ExpectVersion` 之後再 `Remove`~~（上表）
- [x] ~~`Program.cs`、`BackupJson.Options`、`ExportBackup` 的追蹤~~（上表）

以下是讀檔即可確認的形狀細節，各 Task 的 Step 1 已註明「先確認」，在該 Task 開始時處理：
- [ ] `RestoreBackupTests`、`BackupExportTests` 的既有結構（K9 Step 1 依賴）。
- [ ] `SettingsMaintenanceEndpointsTests.Url` 的簽章；同一個 member client 能不能存取兩本 `SeedBookAsync` 的帳本（K4 的 404 測試）。
- [ ] 前端 `book-fixture.ts` 的帳戶 Id 與分類、`formatBudgetMonth` 的輸出格式、`setting-dialog.ts` 的對話框寫法、`app.html` 的導覽寫法、`e2e/fixtures.ts`。
- [ ] 檢查計畫內的測試碼是否有未 await、順序假設（尤其 K15 的「請求進行中 disabled」與 mat-tab 轉場）。

---

## 執行結果與偏差

（每個 checkpoint 結束時以 `docs(plans):` commit 回寫。）
