# 刪除帳本（後端）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL：逐 Task 執行時使用 superpowers:test-driven-development；分派子代理時使用 superpowers:subagent-driven-development；inline 執行時使用 superpowers:executing-plans。每個 Task 的流程都是：失敗測試 → 確認失敗原因 → 最小實作 → 全綠 → 一個 commit。

**Goal**：Owner 可以透過 `DELETE /api/books/{bookId}`（body `{ confirmName }`）硬刪除整本帳本，刪除前可以用 `GET /api/books/{bookId}/deletion-preview` 看到統計。刪除後只留一筆刪除摘要的稽核記錄。

**Architecture**：Application 層新增兩個 use case，`GetBookDeletionPreview` 與 `DeleteBook`。兩者共用 `BookDeletionPreviews.CreateAsync` 計算筆數，刪除時順便把這組數字當成稽核記錄的 `Before`。`DeleteBook` 在單一 DB transaction 裡，以 `ExecuteDeleteAsync`＋`IgnoreQueryFilters()` 逐表刪除；owned 表靠 DB 既有的 FK cascade 帶走。不新增 migration。另外加一個 convention test，把「帶 `BookId` 的資料表清單」和 EF model 綁在一起。

**Tech Stack**：.NET 10、EF Core 10.0.12（Npgsql 10.0.3）、MediatR、FluentValidation、xUnit v3、FluentAssertions、Testcontainers（postgres:17-alpine）。

- 設計文件：[`docs/superpowers/specs/2026-10-19-delete-book-backend-design.md`](../specs/2026-10-19-delete-book-backend-design.md)（Q 編號來自 issue #6，D 編號來自 spec）
- 需求：GitHub issue [AdamChang/SixJars-AccountingBook#6](https://github.com/AdamChang/SixJars-AccountingBook/issues/6)
- 後續計畫：[`2026-10-19-delete-book-frontend.md`](2026-10-19-delete-book-frontend.md)（本計畫的 checkpoint 全綠之後才能開工）

---

## 執行前必讀

### 環境

- 工作目錄：`F:\VibeCode\SixJars-AccountingBook`（Windows）。分支：`claude/delete-book`，從 `origin/master` `1fc9196` 開出，**不是 master**。注意：本機 `master` 落後 `origin/master`；`claude/p4-m-reports` 尚未合併，本分支不包含它。
- 後端全部測試：`dotnet test --solution SixJars.slnx`（需要 Docker Desktop）。
- 單一類別：`dotnet test --project tests/SixJars.Api.Tests --filter-class "*DeleteBookEndpointsTests*"`。
- 建置：`dotnet build SixJars.slnx`。

### 基準線（2026-10-19 本機實測，`claude/delete-book` = `1fc9196`）

- `dotnet build`：0 個錯誤，**0 個編譯器警告**。輸出裡的 60 個警告全部是 NuGet 套件弱點（NU1902／NU1903），與本計畫無關，不要去動套件版本。
- `dotnet test`：總計 **541**，失敗 0，略過 0（`reference/` 存在，Acceptance 實際有執行）。
- 任何時候數字低於基準線，就代表弄壞了東西。各 Task 的「Expected」是**預測**，以實際結果為準，只要只增不減即可。
- **開工前**再跑一次全部測試，確認基準線沒有變。

### 絕對不要碰的檔案

- `.env*`、`reference/`（個資）、`efbundle.exe`、`TestResults/`、`web/`（前端另有計畫）。
- 每個 Task 都用明確路徑執行 `git add`，**禁止 `git add .`／`git add -A`**。

### 慣例

- 註解與 commit 訊息用繁體中文，識別字用英文；commit 結尾加上 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`。
- 每個 use case 一個檔案（record request＋validator＋`internal` handler），放在 `Application/Books/`。
- 寫入慣例與 `DeleteTransaction.cs` **刻意不同**：不檢查鎖帳日（Q10）、不做 `ExpectVersion`（Q7）、硬刪除（ADR 0010）。這些差異都要在 handler 的註解裡寫清楚。
- TDD：先寫失敗測試，確認它以正確的理由失敗。每個 Task 至少做一次變異檢查：改壞實作，看到測試變紅，再還原。一個 Task 一個 commit。
- Commit 訊息格式比照 repo：`feat(api): ...`、`docs(adr): ...`、`test(api): ...`。

---

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `docs/adr/0010-delete-book-is-hard-delete.md` | 整本帳本刪除採硬刪除，是 ADR 0006 的例外 |
| `src/SixJars.Application/Books/GetBookDeletionPreview.cs` | preview 的 request／DTO／handler，以及共用的 `BookDeletionPreviews.CreateAsync` |
| `src/SixJars.Application/Books/DeleteBook.cs` | 刪除的 request／validator／handler |
| `tests/SixJars.Api.Tests/DeleteBookEndpointsTests.cs` | preview、刪除、稽核與 convention test，加上 seed helper |

### 修改

| 檔案 | 改動 |
|---|---|
| `CONTEXT.md` | 「帳本成員與輔助功能」底下新增「刪除帳本 (Delete Book)」；「軟刪除」補一句例外 |
| `src/SixJars.Domain/Common/DomainException.cs` | 新增 `NameMismatchCode = "name-mismatch"` |
| `src/SixJars.Api/Endpoints/BooksEndpoints.cs` | 新增 `GET /deletion-preview`、`DELETE /`，以及 `DeleteBookBody` record |

### Task 相依順序

```
Task 1 (ADR/CONTEXT, 文件) ─┐
                            ├─→ Task 2 (preview) ─→ Task 3 (DeleteBook) ─→ Task 4 (convention test)
                            │                                                   │
（Task 1 與 2 互相獨立，可以對調）                                                 └─→ 後端 checkpoint
```

Task 3 依賴 Task 2 的 `BookDeletionPreviews` 與 seed helper；Task 4 依賴 Task 3 的 `BookScopedTables` 常數。

---

## Task 1: B1 ADR 0010 與 CONTEXT.md

**Files:** Create: `docs/adr/0010-delete-book-is-hard-delete.md`／Modify: `CONTEXT.md`

這個 Task 只改文件，沒有測試。它排在最前面，是因為硬刪除推翻了 ADR 0006 的「一律軟刪除」。如果執行到一半被叫停，至少決策已經留下紀錄。

- [ ] Step 1：建立 `docs/adr/0010-delete-book-is-hard-delete.md`，格式比照 0009（標題一句話＋本文、`## Considered Options`、`## Consequences`）：

```markdown
# 刪除整本帳本採硬刪除，只留一筆摘要稽核記錄

擁有者可以從 Web 刪除整本帳本（issue #6）。帳本、帳戶、分類、財務規劃帳戶、交易（含已軟刪除的）、預定支出、週期預定支出、預算、帳本成員，以及這本帳本**所有既有的稽核記錄**，都會從資料庫實際移除。最後只寫一筆稽核記錄：`Delete`／`Book`，`Before` 是刪除當下的摘要（名稱、開帳日、鎖帳日、各類筆數），不含任何交易內容。這是 ADR 0006「刪除一律採軟刪除」的例外：0006 處理的是**單筆**資料的誤刪，整本帳本的刪除是使用者經過確認（輸入帳本名稱）後，明確表示要清掉這本帳本，典型情境是匯入錯誤後要以同一個名稱重新匯入。

## Considered Options

- **軟刪除帳本（`Book.DeletedAt`）**：資料留著，名稱也一直被佔用，同名重匯會被擋下；Web 上又沒有救回的入口，只剩成本。
- **保留舊的稽核記錄**：舊快照裡有完整的交易內容，留著就等於沒有刪除。
- **補 FK `ON DELETE CASCADE`，只刪 `Book`**：之後任何誤刪 `Book` 的程式碼都會清空整本帳本，而且是 DB 的隱性行為，測試不容易表達。改為在 handler 裡逐表明確刪除，由 convention test 確保新增帶 `BookId` 的資料表時不會漏掉。
- **伺服器端自動備份**：部署上沒有 object storage，保留期限與清理也是額外負擔。改為在刪除前提示使用者下載既有的 JSON 備份。

## Consequences

- 刪除後無法救回，除非使用者事先下載了備份，再用 CLI `restore-backup` 還原。
- 刪除期間不鎖定帳本：同時有其他寫入時，可能留下 `BookId` 指向不存在帳本的孤兒資料。這種資料不會出現在任何查詢中（非成員一律 404）。家庭帳本幾乎沒有這種並行情境，所以接受這個風險。
- 新增帶 `BookId` 的資料表時，必須同時更新 `DeleteBookHandler`；`DeleteBookEndpointsTests` 的 convention test 會擋下遺漏。
- 刪除摘要的稽核記錄沒有 UI 可以查看，因為帳本已經不存在、稽核 API 會回 404。之後的稽核查詢介面要另外處理這類記錄。
```

- [ ] Step 2：修改 `CONTEXT.md`。在「帳本成員與輔助功能」區的 **軟刪除 (Soft Delete)** 條目最後補一句「例外：刪除帳本是硬刪除（ADR 0010）。」，並在這一區的最後新增：

```markdown
**刪除帳本 (Delete Book)**:
擁有者輸入帳本名稱確認後，把整本帳本與底下所有資料（含稽核記錄）永久移除，只留一筆刪除摘要；無法復原（ADR 0010）。
_Avoid_: 封存帳本、關閉帳本
```

（新增前先讀一次「軟刪除」條目的現有文字，確認補上的句子讀起來通順，不要重複。）

- [ ] Step 3：Commit

```powershell
git add docs/adr/0010-delete-book-is-hard-delete.md CONTEXT.md
git commit -m "docs(adr): 刪除整本帳本採硬刪除，只留一筆摘要稽核記錄（ADR 0010）" -m "Refs #6" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 2: B2 GET /deletion-preview

**Files:** Create: `src/SixJars.Application/Books/GetBookDeletionPreview.cs`、`tests/SixJars.Api.Tests/DeleteBookEndpointsTests.cs`／Modify: `src/SixJars.Api/Endpoints/BooksEndpoints.cs`

這個 Task 同時建立整個測試類別的骨架。`SeedEverythingAsync` 要在每張帶 `BookId` 的表都放進資料，包括已軟刪除的交易與預定支出、有來源的預定支出（Restrict FK）、第二位 Owner，以及鎖帳日。Task 3、4 都會重用它。容易出錯的地方是 D3：交易與預定支出只算未刪除的筆數，所以預期值是 1 筆交易、2 筆預定支出（手動新增一筆，加上週期產生的一筆），不是 2 和 3。

- [ ] Step 1：寫失敗測試。建立 `tests/SixJars.Api.Tests/DeleteBookEndpointsTests.cs`：

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SixJars.Application.Books;
using SixJars.Domain.Books;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

/// <summary>刪除帳本（issue #6、ADR 0010）：硬刪除整本帳本，只留一筆刪除摘要的稽核記錄。</summary>
public class DeleteBookEndpointsTests(PostgresFixture postgres)
{
    private const string SecondOwner = "second-owner-sub";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Preview_counts_active_rows_members_and_audit_entries()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await SeedEverythingAsync(factory, client, book);

        var preview = await client.GetFromJsonAsync<BookDeletionPreview>(PreviewUrl(book), ApiJson.Options, Ct);

        // D3：已軟刪除的交易與預定支出不列入；預定支出是手動一筆＋週期產生一筆。
        preview.Should().Be(new BookDeletionPreview(
            "測試帳本", new DateOnly(2025, 12, 31), new DateOnly(2026, 1, 31),
            Transactions: 1, PlannedExpenses: 2, RecurringPlannedExpenses: 1, CategoryBudgets: 1, Members: 2,
            AuditEntries: (await AuditTrailTests.EntriesAsync(factory)).Count(e => e.BookId == book.Id.Value)));
    }

    /// <summary>
    /// 每張帶 BookId 的資料表都放資料（D6）：已軟刪除的交易與預定支出、由週期項目產生的預定支出（Restrict FK）、
    /// 預算的預設值與覆寫值（owned 表）、第二位擁有者，以及鎖帳日（Q10：鎖帳日不擋刪除）。
    /// </summary>
    internal static async Task SeedEverythingAsync(ApiFactory factory, HttpClient client, Book book)
    {
        await AuditTrailTests.CreateTransactionAsync(client, book, AuditTrailTests.LunchInput(book, -100m, "留著"));
        var deleted = await AuditTrailTests.CreateTransactionAsync(client, book, AuditTrailTests.LunchInput(book, -50m, "刪掉"));
        await ExpectNoContentAsync(client.DeleteAsync(
            $"/api/books/{book.Id.Value}/transactions/{deleted.Id}?version={deleted.Version}", Ct));

        await PlannedExpensesEndpointsTests.CreateAsync(
            client, book, PlannedExpensesEndpointsTests.InsuranceInput(book, 202602, -3000m, "留著"));
        var plannedDeleted = await PlannedExpensesEndpointsTests.CreateAsync(
            client, book, PlannedExpensesEndpointsTests.InsuranceInput(book, 202603, -1000m, "刪掉"));
        await ExpectNoContentAsync(client.DeleteAsync(
            $"{PlannedExpensesEndpointsTests.Url(book)}/{plannedDeleted.Id}?version={plannedDeleted.Version}", Ct));

        await RecurringPlannedExpensesEndpointsTests.CreateAsync(client, book, RecurringPlannedExpensesEndpointsTests.Insurance(book));
        await PlannedExpenseGenerationEndpointsTests.GenerateAsync(client, book, 202601);

        var food = book.FindCategory("主食")!.Id.Value;
        await BudgetsEndpointsTests.SetDefaultAsync(client, book, food, 5000m);
        await BudgetsEndpointsTests.SetOverrideAsync(client, book, food, 202602, 3000m);

        await factory.AddOwnerAsync(book.Id, SecondOwner, Ct);
        await ExpectNoContentAsync(client.PutAsJsonAsync($"/api/books/{book.Id.Value}/lock-date", new { lockDate = "2026-01-31" }, Ct));
    }

    private static string PreviewUrl(Book book) => $"/api/books/{book.Id.Value}/deletion-preview";

    private static async Task ExpectNoContentAsync(Task<HttpResponseMessage> request) =>
        (await request).StatusCode.Should().Be(HttpStatusCode.NoContent);
}
```

- [ ] Step 2：執行 `dotnet test --project tests/SixJars.Api.Tests --filter-class "*DeleteBookEndpointsTests*"`。預期結果是**編譯失敗**：`BookDeletionPreview` 不存在。確認錯誤訊息只有這一項，不是 helper 的簽章打錯（例如 `LunchInput` 的參數順序）。如果 helper 簽章有錯，先修正測試再繼續。

- [ ] Step 3：最小實作。建立 `src/SixJars.Application/Books/GetBookDeletionPreview.cs`：

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Books;

namespace SixJars.Application.Books;

/// <summary>刪除帳本前的統計（issue #6 Q9）。同一組數字也是刪除稽核記錄的 Before（spec D4）。</summary>
public sealed record GetBookDeletionPreview(Guid BookId) : IRequest<BookDeletionPreview>, IBookScoped;

/// <param name="Transactions">未刪除的交易筆數（spec D3）；已軟刪除的交易一樣會被刪除，但不列入。</param>
/// <param name="PlannedExpenses">未刪除的預定支出筆數（同上）。</param>
/// <param name="AuditEntries">這本帳本的全部稽核記錄筆數；刪除後只剩一筆刪除摘要。</param>
public sealed record BookDeletionPreview(
    string Name, DateOnly OpeningDate, DateOnly? LockDate,
    int Transactions, int PlannedExpenses, int RecurringPlannedExpenses,
    int CategoryBudgets, int Members, int AuditEntries);

internal sealed class GetBookDeletionPreviewHandler(ISixJarsDbContext db)
    : IRequestHandler<GetBookDeletionPreview, BookDeletionPreview>
{
    public async Task<BookDeletionPreview> Handle(GetBookDeletionPreview request, CancellationToken cancellationToken) =>
        await BookDeletionPreviews.CreateAsync(db, await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken), cancellationToken);
}

internal static class BookDeletionPreviews
{
    /// <summary>交易與預定支出套用 query filter（只算未刪除的，D3）。依序 await：同一個 DbContext 不能並行查詢。</summary>
    public static async Task<BookDeletionPreview> CreateAsync(ISixJarsDbContext db, Book book, CancellationToken cancellationToken)
    {
        var id = book.Id;
        return new BookDeletionPreview(
            book.Name, book.OpeningDate, book.LockDate,
            await db.Transactions.CountAsync(t => t.BookId == id, cancellationToken),
            await db.PlannedExpenses.CountAsync(p => p.BookId == id, cancellationToken),
            await db.RecurringPlannedExpenses.CountAsync(r => r.BookId == id, cancellationToken),
            await db.CategoryBudgets.CountAsync(b => b.BookId == id, cancellationToken),
            await db.BookMembers.CountAsync(m => m.BookId == id, cancellationToken),
            await db.AuditEntries.CountAsync(e => e.BookId == id.Value, cancellationToken));
    }
}
```

在 `BooksEndpoints.MapBooksEndpoints` 的 `book.MapGet("/", ...)` 下一行新增：

```csharp
        book.MapGet("/deletion-preview", (Guid bookId, ISender sender, CancellationToken ct) =>
            sender.Send(new GetBookDeletionPreview(bookId), ct));
```

- [ ] Step 4：執行單一類別。Expected：**1 passed**。
- [ ] 變異檢查：把 `db.Transactions.CountAsync` 改成 `db.Transactions.IgnoreQueryFilters().CountAsync`，確認測試失敗（Transactions 會變成 2），然後還原。
- [ ] Step 5：執行 `dotnet test --solution SixJars.slnx`。Expected：**542**，失敗 0。`MembershipTests`、`SecurityConventionTests` 會自動涵蓋新的 GET（非成員 404、需要登入）。
- [ ] Step 6：Commit

```powershell
git add src/SixJars.Application/Books/GetBookDeletionPreview.cs src/SixJars.Api/Endpoints/BooksEndpoints.cs tests/SixJars.Api.Tests/DeleteBookEndpointsTests.cs
git commit -m "feat(api): 刪除帳本前的統計 GET /books/{id}/deletion-preview" -m "交易與預定支出只算未刪除的；同一組數字之後當作刪除稽核記錄的 Before。Refs #6" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 3: B3 DELETE /books/{id}

**Files:** Create: `src/SixJars.Application/Books/DeleteBook.cs`／Modify: `src/SixJars.Domain/Common/DomainException.cs`、`src/SixJars.Api/Endpoints/BooksEndpoints.cs`、`tests/SixJars.Api.Tests/DeleteBookEndpointsTests.cs`

這是本計畫的核心。有三個地方容易出錯：

1. `ExecuteDeleteAsync` 預設會套用 query filter。漏掉 `IgnoreQueryFilters()` 的話，已軟刪除的交易和預定支出會留下來。測試透過 raw SQL 計數，raw SQL 不套 filter，所以抓得到這個問題。
2. PlannedExpenses 以 Restrict FK 指向 RecurringPlannedExpenses。順序顛倒的話，`DbUpdateException` 會變成 500。
3. Minimal API 對 DELETE 不推斷 body。沒有標 `[FromBody]` 的話，`DeleteBookBody` 會被當成 query 或 service 參數，造成啟動失敗或 400。

- [ ] Step 1：寫失敗測試。在 `DeleteBookEndpointsTests` 類別裡，`Preview_...` 測試之後加入：

```csharp
    /// <summary>D6：有 BookId 欄位、不是 owned 的資料表（依 ordinal 排序）。新增這類資料表時，Task 4 的 convention test 會失敗：要同時更新 DeleteBookHandler 與 SeedEverythingAsync。</summary>
    internal static readonly string[] BookScopedTables =
        ["AuditEntries", "BookMembers", "CategoryBudgets", "PlannedExpenses", "RecurringPlannedExpenses", "Transactions"];

    /// <summary>Book 的 owned 集合，由 FK cascade 刪除。Postings、CategoryBudgetOverrides 沒有 BookId，由 FK 保證不會成為孤兒。</summary>
    private static readonly string[] OwnedTables = ["Accounts", "Categories", "PlanningFunds"];

    [Fact]
    public async Task Delete_removes_every_book_scoped_row_and_keeps_other_books()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        // 另一本同名、同擁有者的帳本：刪除拿掉 BookId 條件、或改用名稱比對，都會連帶刪到它。
        var other = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await SeedEverythingAsync(factory, client, book);
        await SeedEverythingAsync(factory, client, other);
        var otherBefore = await RowCountsAsync(factory, other);
        otherBefore.Values.Should().OnlyContain(count => count > 0, "seed 必須涵蓋每張表，否則「刪光」可能是空轉通過");

        var response = await DeleteAsync(client, book, "測試帳本");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RowCountsAsync(factory, book)).Should().OnlyContain(
            pair => pair.Value == (pair.Key == "AuditEntries" ? 1 : 0), "只留下一筆刪除摘要；已軟刪除的資料也要刪掉");
        (await RowCountsAsync(factory, other)).Should().Equal(otherBefore);
    }

    [Fact]
    public async Task Delete_leaves_a_single_summary_entry_by_the_signed_in_owner()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await SeedEverythingAsync(factory, client, book);
        var preview = await client.GetFromJsonAsync<BookDeletionPreview>(PreviewUrl(book), ApiJson.Options, Ct);

        (await DeleteAsync(client, book, "測試帳本")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var entry = (await AuditTrailTests.EntriesAsync(factory)).Should().ContainSingle(e => e.BookId == book.Id.Value).Subject;
        (entry.Action, entry.EntityType, entry.EntityId, entry.ActorSubject)
            .Should().Be((AuditAction.Delete, AuditEntityTypes.Book, book.Id.Value, ApiFactory.DefaultSubject));
        entry.After.Should().BeNull();
        // Before 只放摘要，不放交易內容（Q8、Q15）；就是刪除前 preview 的那組數字（D4）。
        JsonSerializer.Deserialize<BookDeletionPreview>(entry.Before!, ApiJson.Options).Should().Be(preview);
    }

    [Fact]
    public async Task Any_owner_can_delete()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await factory.AddOwnerAsync(book.Id, SecondOwner, Ct);

        var response = await DeleteAsync(await factory.CreateMemberClientAsync(SecondOwner), book, "測試帳本");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("測試帳本 ")]
    [InlineData("別的帳本")]
    public async Task Delete_with_a_different_name_is_422_and_deletes_nothing(string confirmName)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();

        var response = await DeleteAsync(client, book, confirmName);

        // D2：ordinal 完全比對、不 trim。
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("name-mismatch");
        (await RowCountsAsync(factory, book))["Books"].Should().Be(1);
    }

    [Fact]
    public async Task Delete_without_confirm_name_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Delete, BookUrl(book)) { Content = JsonContent.Create(new { }) };

        var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("confirmName", out _).Should().BeTrue();
    }

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, Book book, string confirmName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, BookUrl(book)) { Content = JsonContent.Create(new { confirmName }) };
        return await client.SendAsync(request, Ct);
    }

    /// <summary>以 raw SQL 計數：不套 query filter，已軟刪除的資料也算在內。表名來自上面的常數清單，不是外部輸入。</summary>
    private static async Task<Dictionary<string, int>> RowCountsAsync(ApiFactory factory, Book book)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        var counts = new Dictionary<string, int>();
        foreach (var table in BookScopedTables.Concat(OwnedTables))
        {
            counts[table] = await db.Database
                .SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM \"{table}\" WHERE \"BookId\" = {{0}}", book.Id.Value)
                .SingleAsync(Ct);
        }

        counts["Books"] = await db.Database
            .SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM \"Books\" WHERE \"Id\" = {0}", book.Id.Value)
            .SingleAsync(Ct);
        return counts;
    }

    private static string BookUrl(Book book) => $"/api/books/{book.Id.Value}";
```

檔頭另外加上 `using System.Text.Json;`、`using Microsoft.EntityFrameworkCore;`、`using Microsoft.Extensions.DependencyInjection;`、`using SixJars.Application.Auditing;`、`using SixJars.Infrastructure.Persistence;`。

- [ ] Step 2：執行單一類別。測試只透過 HTTP 呼叫，不引用 `DeleteBook` 型別，所以**可以編譯**。6 個新案例（4 個 Fact＋Theory 2 個）應該以 **405 Method Not Allowed** 失敗：`/api/books/{id}` 已經有 GET 路由，但還沒有 DELETE。確認失敗原因是 405，不是 seed 階段的 assertion 失敗，也不是 `otherBefore` 的「seed 必須涵蓋每張表」失敗（那代表 `SeedEverythingAsync` 有問題）。
- [ ] Step 3：最小實作。

`DomainException.cs` 在 `InUseCode` 之後新增：

```csharp
    /// <summary>確認用的名稱與實際名稱不同（刪除帳本，issue #6）。</summary>
    public const string NameMismatchCode = "name-mismatch";
```

建立 `src/SixJars.Application/Books/DeleteBook.cs`：

```csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Common;
using SixJars.Domain.Common;

namespace SixJars.Application.Books;

/// <summary>
/// 硬刪除整本帳本（issue #6、ADR 0010）：帳本與所有帶 BookId 的資料都會移除，只留一筆刪除摘要的稽核記錄。
/// <see cref="ConfirmName"/> 必須與帳本名稱完全相同（ordinal、不 trim，spec D2），否則擲 <see cref="DomainException"/>（422 <c>name-mismatch</c>）。
/// 與 DeleteTransaction 的慣例刻意不同：鎖帳日不擋（Q10）、不做樂觀並行（Q7）、任一位擁有者都能刪（Q6）。
/// </summary>
public sealed record DeleteBook(Guid BookId, string ConfirmName) : IRequest, IBookScoped;

internal sealed class DeleteBookValidator : AbstractValidator<DeleteBook>
{
    public DeleteBookValidator() => RuleFor(c => c.ConfirmName).NotEmpty();
}

internal sealed class DeleteBookHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<DeleteBook>
{
    public async Task Handle(DeleteBook request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        if (!string.Equals(book.Name, request.ConfirmName, StringComparison.Ordinal))
        {
            throw new DomainException($"輸入的名稱與帳本名稱「{book.Name}」不同，沒有刪除。", DomainException.NameMismatchCode);
        }

        // 摘要在刪除前取得（D4），就是使用者在確認對話框裡看到的數字。
        var summary = await BookDeletionPreviews.CreateAsync(db, book, cancellationToken);
        var id = book.Id;

        // ExecuteDelete 不經過 change tracker，也不套用 EF 的 cascade；owned 表（Accounts、Categories、PlanningFunds、
        // Postings、CategoryBudgetOverrides）靠 DB 上的 FK cascade。IgnoreQueryFilters：已軟刪除的交易與預定支出也要刪除（Q14）。
        // 新增帶 BookId 的資料表時，DeleteBookEndpointsTests 的 convention test 會失敗，提醒在這裡補一行（D6）。
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        // PlannedExpenses 以 Restrict FK 指向 RecurringPlannedExpenses，必須先刪（D7）。
        await db.PlannedExpenses.IgnoreQueryFilters().Where(p => p.BookId == id).ExecuteDeleteAsync(cancellationToken);
        await db.RecurringPlannedExpenses.Where(r => r.BookId == id).ExecuteDeleteAsync(cancellationToken);
        await db.CategoryBudgets.Where(b => b.BookId == id).ExecuteDeleteAsync(cancellationToken);
        await db.Transactions.IgnoreQueryFilters().Where(t => t.BookId == id).ExecuteDeleteAsync(cancellationToken);
        await db.BookMembers.Where(m => m.BookId == id).ExecuteDeleteAsync(cancellationToken);
        // 舊的稽核記錄含有完整快照，留著就等於沒刪（Q8）。
        await db.AuditEntries.Where(e => e.BookId == request.BookId).ExecuteDeleteAsync(cancellationToken);
        await db.Books.Where(b => b.Id == id).ExecuteDeleteAsync(cancellationToken);

        audit.Record<BookDeletionPreview>(request.BookId, AuditAction.Delete, AuditEntityTypes.Book, request.BookId, summary, null);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
```

在 `BooksEndpoints.cs` 檔頭加上 `using Microsoft.AspNetCore.Mvc;`，並在 `deletion-preview` 那一行之後新增：

```csharp
        // 帳本名稱放在 body（spec D1）：中文名稱放在 query string 要做 URL 編碼，也會進到 request log。
        // minimal API 對 DELETE 不推斷 body，所以必須標 [FromBody]。
        book.MapDelete("/", (Guid bookId, [FromBody] DeleteBookBody body, ISender sender, CancellationToken ct) =>
            NoContent(sender.Send(new DeleteBook(bookId, body.ConfirmName ?? ""), ct)));
```

檔尾新增：

```csharp
/// <summary>刪除帳本的 body；<see cref="ConfirmName"/> 必須與帳本名稱完全相同。</summary>
internal sealed record DeleteBookBody(string? ConfirmName);
```

- [ ] Step 4：執行單一類別。Expected：**7 passed**（Preview 1、Delete 4 個 Fact、Theory 2 個案例）。
  - 如果 `ExecuteDeleteAsync` 在 `Books`（有 OwnsMany）或 `Transactions` 上擲出 EF 的 translation 例外：改成在 transaction 內 `db.Books.Remove(await db.GetBookForUpdateAsync(...))`，讓 owned 集合由 EF 刪除；其他表維持原寫法。在計畫的「執行結果與偏差」記一行原因。事實查核時已確認這兩個 entity 只有 OwnsMany（分開的表），沒有 table splitting，所以預期不會發生。
- [ ] 變異檢查（三項，每項看到變紅後都要還原）：
  1. 拿掉 Transactions 那行的 `IgnoreQueryFilters()` → `Delete_removes_every_book_scoped_row...` 失敗（Transactions 剩 1）。
  2. 把 PlannedExpenses 與 RecurringPlannedExpenses 兩行對調 → 同一個測試以 500 失敗（Restrict FK）。
  3. 把 `StringComparison.Ordinal` 的比對改成 `book.Name == request.ConfirmName.Trim()` → `"測試帳本 "` 案例失敗。
- [ ] Step 5：執行 `dotnet test --solution SixJars.slnx`。Expected：**548**，失敗 0。`SecurityConventionTests` 會檢查新的 DELETE 需要 XSRF token、需要登入；`MembershipTests` 會檢查非成員送空 body 得到 404（`BookAccessBehavior` 在 validation 之前執行）。
- [ ] Step 6：Commit

```powershell
git add src/SixJars.Domain/Common/DomainException.cs src/SixJars.Application/Books/DeleteBook.cs src/SixJars.Api/Endpoints/BooksEndpoints.cs tests/SixJars.Api.Tests/DeleteBookEndpointsTests.cs
git commit -m "feat(api): DELETE /books/{id} 硬刪除整本帳本" -m "body 的 confirmName 必須與帳本名稱完全相同（422 name-mismatch）；單一 DB transaction 逐表 ExecuteDelete，含已軟刪除的資料；舊稽核記錄一併刪除，只留一筆刪除摘要（ADR 0010）。Refs #6" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 4: B4 帶 BookId 資料表的 convention test

**Files:** Modify: `tests/SixJars.Api.Tests/DeleteBookEndpointsTests.cs`

Task 3 的刪除測試只驗證了清單**上**的表，沒有辦法知道清單本身是否完整。這個 Task 把清單和 EF model 綁在一起：之後有人新增帶 `BookId` 的 entity，這個測試會失敗，訊息會提醒他同時更新 handler 與 seed（D6）。這個測試一寫出來就是綠的，所以 Step 2 改用變異檢查來證明它有效。

- [ ] Step 1：在 `BookScopedTables` 宣告之後加入：

```csharp
    [Fact]
    public async Task Book_scoped_tables_match_the_model()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();

        var tables = db.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.FindProperty("BookId") is not null)
            .Select(t => t.GetTableName()!)
            .Order(StringComparer.Ordinal);

        tables.Should().Equal(BookScopedTables, "新增帶 BookId 的資料表時，DeleteBookHandler 與 SeedEverythingAsync 都要一起更新");
    }
```

- [ ] Step 2：用變異檢查證明測試有效。暫時從 `BookScopedTables` 拿掉 `"Transactions"`，執行單一類別，確認 `Book_scoped_tables_match_the_model` 失敗，而且失敗訊息裡列出了 `Transactions`。然後還原。
- [ ] Step 3：（不需要實作）
- [ ] Step 4：執行單一類別。Expected：**8 passed**。
- [ ] Step 5：執行 `dotnet test --solution SixJars.slnx`。Expected：**549**，失敗 0。
- [ ] Step 6：Commit

```powershell
git add tests/SixJars.Api.Tests/DeleteBookEndpointsTests.cs
git commit -m "test(api): 帶 BookId 的資料表清單與 EF model 一致" -m "新增這類資料表時提醒同時更新 DeleteBookHandler 與 seed（spec D6）。Refs #6" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## 完成後的驗證（後端 checkpoint）

- [ ] `dotnet build SixJars.slnx`：0 個錯誤，0 個編譯器警告（NU1902／NU1903 不算）。
- [ ] `dotnet test --solution SixJars.slnx`：**549** 左右（541＋8），失敗 0，略過 0。
- [ ] `git status`：工作目錄乾淨。
- [ ] `git log --oneline 1fc9196..HEAD`：本計畫產生 **4 個 commit**（加上計畫文件本身的 `docs(plans)` commit）。
- [ ] `git diff 1fc9196 -- src/SixJars.Infrastructure/Persistence/Migrations`：**沒有變更**（不新增 migration）。
- [ ] 確認 diff 裡沒有連線字串或憑證。

## 手動驗證

自動測試涵蓋不到真實的 Neon／PostgreSQL 環境與實際的 cookie 登入，以下逐條手動確認（本機依 `docs/deploy.md` 用 https 跑 API）：

1. 用 CLI 匯入一本測試帳本，名稱自訂，例如 `刪除測試`：`dotnet run --project src/SixJars.Cli -- import-legacy --file <xlsm> --book-name 刪除測試 --owner-email <你的 email>`。
2. 登入 Web，在瀏覽器 DevTools 對 `/api/books/{id}/deletion-preview` 發 GET，確認筆數合理。
3. 用 DevTools 或 REST client 送 `DELETE /api/books/{id}`，body 為 `{"confirmName":"刪除測試"}`，並帶上 `X-XSRF-TOKEN` header，預期回 204。
4. 用同一個名稱再執行一次步驟 1，這次應該匯入成功（同名檢查不再擋）。
5. 直接查 DB：`SELECT "Action","EntityType","Before" FROM "AuditEntries" WHERE "BookId" = '<舊 id>'`，預期只有一筆 `Delete`／`Book`。

## 後續（不在本計畫內）

- 前端：[`2026-10-19-delete-book-frontend.md`](2026-10-19-delete-book-frontend.md)。
- 稽核查詢介面：要能查到已刪除帳本的那筆摘要（Q8）。
- 其他分頁遇到帳本層級 404 時的處理（Q13）。
- CLI 的 `delete-book`（Q1 決定不做）。

## 附錄：核准前事實查核（2026-10-19）

| 引用 | 存在? | 證據 | 修正 |
|---|---|---|---|
| `IBookScoped`、`BookAccessBehavior` 在 validation 之前 | ✅ | `Application/Common/IBookScoped.cs`、`BookAccessBehavior.cs` 的註解 | — |
| `ISixJarsDbContext` 有 Books／Transactions／PlannedExpenses／RecurringPlannedExpenses／CategoryBudgets／AuditEntries／BookMembers、`BeginTransactionAsync` | ✅ | `Application/Common/ISixJarsDbContext.cs` | — |
| `GetBookAsNoTrackingAsync` | ✅ | `Application/Books/BookLoading.cs` | — |
| `IAuditTrail.Record<T>(bookId, action, entityType, entityId, before, after)` | ✅ | `Application/Auditing/IAuditTrail.cs` | — |
| `AuditAction.Delete`、`AuditEntityTypes.Book` | ✅ | `AuditAction.cs`、`AuditEntry.cs` | — |
| `DomainException(message, code)`，code 會進 ProblemDetails 的 `code` | ✅ | `Domain/Common/DomainException.cs`、`Api/Infrastructure/ApiExceptionHandler.cs` | — |
| 帶 BookId 的 entity：Transaction／PlannedExpense／RecurringPlannedExpense／CategoryBudget／BookMember（`BookId BookId`）、AuditEntry（`Guid BookId`） | ✅ | 各 entity 的 grep | AuditEntries 的條件用 `request.BookId`（Guid），其他用 `BookId` |
| FK：Accounts／Categories／PlanningFunds → Books 為 Cascade；Postings → Transactions 為 Cascade；CategoryBudgetOverrides → CategoryBudgets 為 Cascade；PlannedExpenses → RecurringPlannedExpenses 為 **Restrict**；其他表與 Books 之間沒有 FK | ✅ | `Persistence/Migrations/*.cs` 的 `onDelete` | 刪除順序依 D7 |
| 只有 OwnsMany，沒有 OwnsOne／ComplexProperty／ToJson（ExecuteDelete 不支援 table splitting） | ✅ | `Persistence/*Configuration.cs` 的 grep | Task 3 Step 4 留有備案 |
| Query filter 只在 Transactions、PlannedExpenses 上 | ✅ | `TransactionConfiguration.cs:16`、`PlannedExpenseConfiguration.cs:16` | 只有這兩個加 `IgnoreQueryFilters()` |
| EF Core 10.0.12（`ExecuteDeleteAsync` 自 EF 7 起提供） | ✅ | `Directory.Packages.props` | — |
| Minimal API 對 DELETE 不推斷 body | ⚠️ 依框架行為推定 | ASP.NET Core 7+ 的 RequestDelegateFactory 對 GET／DELETE／HEAD／OPTIONS 停用 body 推斷 | 計畫已明確標上 `[FromBody]`；就算推定有誤，標註也無害 |
| 測試 helper：`AuditTrailTests.LunchInput(book, amount, note)`／`CreateTransactionAsync`／`EntriesAsync`、`PlannedExpensesEndpointsTests.InsuranceInput(book, month, amount, note)`／`CreateAsync`／`Url`、`RecurringPlannedExpensesEndpointsTests.Insurance`／`CreateAsync`、`PlannedExpenseGenerationEndpointsTests.GenerateAsync(client, book, month)`、`BudgetsEndpointsTests.SetDefaultAsync`／`SetOverrideAsync`、`ApiSeed.AddOwnerAsync`、`SettingsMaintenanceEndpointsTests.ReadProblemAsync` | ✅ | 各測試檔的 `internal static` 宣告（`ReadProblemAsync` 的用法見 `RecurringPlannedExpensesEndpointsTests.cs:131`） | — |
| `SeedBookAsync` 的名稱固定是「測試帳本」，開帳日 2025-12-31 | ✅ | `tests/Shared/ApiSeed.cs` | 兩本帳本同名，正好拿來驗證刪除是依 Id 而不是依名稱 |
| 週期項目 `Insurance` 產生 202601 的結果恰好一筆 | ✅ | `BackupExportTests.Backup_contains_recurring_items_and_planned_expense_sources` | — |
| `MembershipTests`／`SecurityConventionTests` 以列舉方式涵蓋新的 endpoint，非 GET 送 `{}` body | ✅ | `ApiEndpoints.cs`、兩個測試檔 | 不必另寫 404／XSRF 測試 |
| Create 的檔案尚不存在 | ✅ | `docs/adr` 只到 0009；`Application/Books` 沒有 Delete*／*Preview；`tests/SixJars.Api.Tests` 沒有 DeleteBook* | — |
| 測試碼競態：所有 HTTP 呼叫都依序 await；`RowCountsAsync` 依序查詢 | ✅ | — | — |

## 執行結果與偏差（2026-10-19，後端 checkpoint）

- Commit：`d7245d6`（B1）、`b3ce55a`（B2）、`1632ef5`（B3）、`ae229d1`（B4）。
- 測試數字與預測完全相同：542 → 548 → 549，失敗 0、略過 0；`dotnet build` 0 個錯誤、0 個編譯器警告（NU1902／NU1903 仍在，與本計畫無關）。
- 變異檢查都以預期的理由變紅：B2 Transactions 計數變成 2；B3 拿掉 `IgnoreQueryFilters` 時 Transactions 剩 1，對調 Planned／Recurring 的刪除順序回 500（Restrict FK），改成 Trim 比對時 `"測試帳本 "` 回 204；B4 從清單拿掉 Transactions 時，失敗訊息點名它。
- `ExecuteDeleteAsync` 在有 OwnsMany 的 `Books`、`Transactions`、`CategoryBudgets` 上都正常運作，**Task 3 Step 4 的備案沒有用到**。
- 偏差：B1 的「刪除帳本」條目在 `CONTEXT.md` 裡改放在「軟刪除」條目後面，而不是區段末尾。原因是兩者互為例外，放在一起讀者比較容易對照；內容與計畫相同。
