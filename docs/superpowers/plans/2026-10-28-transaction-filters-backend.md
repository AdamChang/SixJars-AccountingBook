# 交易清單篩選：分類（後端）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL：superpowers:test-driven-development（每個 Task 依 Step 1–6 執行，Step 2 不可省）；分派 subagent 時用 superpowers:subagent-driven-development。

**Goal：** `GET /transactions` 新增 `categoryId`，語意是「這個分類以及它的子分類」，不限交易類型；id 不屬於這本帳時回空清單。

**Architecture：** 只改 Application（`ListTransactions`）和 Api（endpoint 參數）兩層，不動 Domain 和 migration。handler 先查出這本帳裡符合條件的分類 id 清單，再拿這份清單去篩交易（spec D1）。

**Tech Stack：** .NET 10、EF Core + Npgsql、xUnit v3 + FluentAssertions、Testcontainers（`postgres:17-alpine`）。

- 設計：[`../specs/2026-10-28-transaction-filters-backend-design.md`](../specs/2026-10-28-transaction-filters-backend-design.md)
- 前端計畫：[`2026-10-28-transaction-filters-frontend.md`](2026-10-28-transaction-filters-frontend.md)（相依於本計畫的 Task 1）

## 執行前必讀

### 環境

- 工作目錄：repo 根目錄。分支：`feat/transaction-filters`（從 `master` 切出，**不是 master**；前後端共用同一條分支）。
- 全部測試：`dotnet test --solution SixJars.slnx`。單一類別：`dotnet test --project tests/SixJars.Api.Tests --filter-class "*TransactionsEndpointsTests*"`。
- Api 測試透過 Testcontainers 執行，**Docker Desktop 必須在跑**。
- Build：`dotnet build SixJars.slnx`。

### 基準線

- 撰寫計畫時 Docker 沒有啟動，Api／Application／Infrastructure／Acceptance 測試都**沒有實測**。開工前先跑一次 `dotnet test --solution SixJars.slnx`，把通過數記在這裡；之後數字只能增加。
- 實測（2026-10-28，HEAD `88b1473`）：`dotnet build` 與 Domain 測試的數字見「附錄：基準線實測」。
- Acceptance 測試讀的是 `reference/2026帳本v1.xlsm`，檔案不存在時會 **skip**，回報時要說明。
- 任何時候數字低於基準線，就代表弄壞了東西。

### 絕對不要碰的檔案

- `reference/`（個資）、`bin/`、`obj/`、`web/` 底下的任何檔案（前端有自己的計畫）。
- 每個 Task 都用明確的路徑 `git add`，**禁止 `git add .`／`git add -A`**。

### 慣例

- 註解與 commit 訊息用繁體中文，註解只寫「為什麼」；測試名稱用 `Snake_case_english`（比照既有測試）。
- 測試放 Api 層的整合測試，因為篩選邏輯就是 EF 翻譯成的 SQL，用 in-memory 測不出問題。
- 每個 Task 至少做一次變異檢查（mutation check）：故意改壞實作，確認至少一條新測試會變紅，然後還原。
- 一個 Task 一個 commit。

## 檔案結構

### 新增

（無）

### 修改

| 檔案 | 改動 |
|---|---|
| `src/SixJars.Application/Transactions/ListTransactions.cs` | record 新增 `Guid? CategoryId = null`、`<param>` 註解、handler 新增分類篩選 |
| `src/SixJars.Api/Endpoints/TransactionsEndpoints.cs` | `MapGet("/transactions")` 新增 `Guid? categoryId` |
| `tests/SixJars.Api.Tests/TransactionsEndpointsTests.cs` | 新增 4 個測試 |

### Task 相依順序

```
Task 1（categoryId 篩選）──► 前端計畫 Task 1（API client 帶 categoryId／accountId）
```

只有一個 Task。前端計畫的 Task 1 只用 mock HTTP，技術上可以並行，但要先合併本 Task，e2e 和手動驗證才對得起來。

---

## Task 1：`categoryId` 篩選

**Files：** Modify：`src/SixJars.Application/Transactions/ListTransactions.cs`、`src/SixJars.Api/Endpoints/TransactionsEndpoints.cs`、`tests/SixJars.Api.Tests/TransactionsEndpointsTests.cs`

容易出錯的地方：

1. **ASP.NET Core 會默默忽略不認得的 query 參數**，所以 Step 2 的失敗不會是 400，而是「列出了不符合條件的交易」，這才是正確的失敗原因。如果看到的是編譯錯誤或 400，代表測試寫錯了。
2. `Book.Categories` 是 owned collection。`db.Books.Where(...).SelectMany(b => b.Categories)` 在專案裡沒有先例，要靠 Step 4 確認 EF 翻譯得出來。翻譯失敗時改用備案：`await db.Books.AsNoTracking().SingleAsync(b => b.Id == bookId, ct)` 載入帳本後在記憶體裡過濾。`BookAccessBehavior` 已經保證帳本存在。這個備案會多讀帳戶、標籤等資料，但量很小。
3. `Transaction.CategoryId` 是 nullable，所以 id 清單要宣告成 `List<CategoryId?>`（spec D2）。

- [ ] **Step 1：寫失敗測試**

  加在 `TransactionsEndpointsTests.cs`，放在 `List_by_account_includes_same_account_fund_allocation_without_postings` 之後：

  ```csharp
  [Fact]
  public async Task List_by_main_category_includes_subcategories_and_any_kind()
  {
      await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
      var book = await factory.SeedBookAsync(Ct);
      var client = await factory.CreateMemberClientAsync();
      var bank = book.FindAccount("國泰世華銀行")!.Id.Value;
      var loanMain = book.FindCategory("貸款支出")!.Id.Value;
      var onMain = await CreateAsync(client, book,
          new { kind = "Expense", date = "2026-01-05", amount = -100m, accountId = bank, categoryId = loanMain });
      // 貸款繳款的利息掛子分類：不限交易類型（spec Q3）
      var payment = await CreateAsync(client, book, new
      {
          kind = "LoanPayment", date = "2026-01-06", amount = 20000m, loanPrincipal = 15000m, accountId = bank,
          counterAccountId = book.FindAccount("房屋貸款")!.Id.Value, categoryId = book.FindCategory("貸款支出", "房屋貸款")!.Id.Value,
      });
      await CreateLunchAsync(client, book, "2026-01-07", "別的分類");

      var list = await ListAsync(client, book, $"?categoryId={loanMain}");

      list.Select(t => t.Id).Should().Equal(onMain, payment);
  }

  [Fact]
  public async Task List_by_subcategory_matches_only_itself()
  {
      await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
      var book = await factory.SeedBookAsync(Ct);
      var client = await factory.CreateMemberClientAsync();
      await CreateAsync(client, book, new
      {
          kind = "Expense", date = "2026-01-05", amount = -50m,
          accountId = book.FindAccount("現金")!.Id.Value, categoryId = book.FindCategory("主食")!.Id.Value,
      });
      var lunch = await CreateLunchAsync(client, book, "2026-01-06", "午餐");

      var list = await ListAsync(client, book, $"?categoryId={book.FindCategory("主食", "午餐")!.Id.Value}");

      list.Select(t => t.Id).Should().Equal(lunch);
  }

  [Fact]
  public async Task List_by_category_of_another_book_returns_nothing()
  {
      await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
      var mine = await factory.SeedBookAsync(Ct);
      var other = await factory.SeedBookAsync(Ct);
      var client = await factory.CreateMemberClientAsync();
      await CreateLunchAsync(client, mine, "2026-01-05", "我的");

      // 跟 accountId、tagId 一樣：別本帳的 Id 只是查不到，不是 404（spec Q11）
      var list = await ListAsync(client, mine, $"?categoryId={other.FindCategory("主食")!.Id.Value}");

      list.Should().BeEmpty();
  }

  [Fact]
  public async Task Category_filter_combines_with_account_and_month()
  {
      await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
      var book = await factory.SeedBookAsync(Ct);
      var client = await factory.CreateMemberClientAsync();
      var cash = book.FindAccount("現金")!.Id.Value;
      var food = book.FindCategory("主食")!.Id.Value;
      var match = await CreateLunchAsync(client, book, "2026-01-05", "符合");
      await CreateLunchAsync(client, book, "2026-02-05", "別的月份");
      // 同月份、同帳戶、不同分類：少了這一筆，分類條件被忽略時這條測試也會通過
      await CreateAsync(client, book, new
      {
          kind = "Income", date = "2026-01-07", amount = 1000m, note = "別的分類",
          accountId = cash, categoryId = book.FindCategory("工作薪資")!.Id.Value,
      });
      await CreateAsync(client, book, new
      {
          kind = "Expense", date = "2026-01-06", amount = -80m, note = "別的帳戶",
          accountId = book.FindAccount("國泰世華銀行")!.Id.Value, categoryId = book.FindCategory("主食", "午餐")!.Id.Value,
      });

      var list = await ListAsync(client, book, $"?budgetMonth=202601&accountId={cash}&categoryId={food}");

      list.Select(t => t.Id).Should().Equal(match);
  }
  ```

- [ ] **Step 2：跑測試，確認失敗**

  `dotnet test --project tests/SixJars.Api.Tests --filter-class "*TransactionsEndpointsTests*"`

  Expected：4 條新測試**全部失敗**，失敗原因都是「清單多出不符合條件的交易」（參數被忽略了）；原本的 20 條測試全過。只要有一條是編譯錯誤、400 或 500，就要先修測試。

- [ ] **Step 3：最小實作**

  `ListTransactions.cs`：

  ```diff
   /// <param name="TagId">只列出掛著該標籤的交易（一次一個，tags spec G16）。</param>
  -public sealed record ListTransactions(Guid BookId, DateOnly? From, DateOnly? To, int? BudgetMonth, Guid? AccountId, Guid? TagId = null)
  +/// <param name="CategoryId">只列出該分類及其子分類的交易，不限交易類型（transaction filters spec Q1、Q3、Q10）。</param>
  +public sealed record ListTransactions(
  +    Guid BookId, DateOnly? From, DateOnly? To, int? BudgetMonth, Guid? AccountId, Guid? TagId = null, Guid? CategoryId = null)
       : IRequest<IReadOnlyList<TransactionDto>>, IBookScoped;
  ```

  ```diff
           if (request.TagId is { } tagGuid)
           {
               var tagId = new TagId(tagGuid);
               query = query.Where(t => t.Tags.Any(x => x.TagId == tagId));
           }
  +
  +        if (request.CategoryId is { } categoryGuid)
  +        {
  +            // 只取這本帳的分類：別本帳的 Id 得到空清單，篩出來就是空的（spec Q11、D1）。
  +            // nullable 型別讓 Contains 與 Transaction.CategoryId 的型別一致（D2）。
  +            var categoryId = new CategoryId(categoryGuid);
  +            var categoryIds = await db.Books.Where(b => b.Id == bookId)
  +                .SelectMany(b => b.Categories)
  +                .Where(c => c.Id == categoryId || c.ParentId == categoryId)
  +                .Select(c => (CategoryId?)c.Id)
  +                .ToListAsync(cancellationToken);
  +            query = query.Where(t => categoryIds.Contains(t.CategoryId));
  +        }
  ```

  `TransactionsEndpoints.cs`：

  ```diff
           book.MapGet("/transactions",
  -            (Guid bookId, DateOnly? from, DateOnly? to, int? budgetMonth, Guid? accountId, Guid? tagId, ISender sender, CancellationToken ct) =>
  -                sender.Send(new ListTransactions(bookId, from, to, budgetMonth, accountId, tagId), ct));
  +            (Guid bookId, DateOnly? from, DateOnly? to, int? budgetMonth, Guid? accountId, Guid? tagId, Guid? categoryId,
  +                ISender sender, CancellationToken ct) =>
  +                sender.Send(new ListTransactions(bookId, from, to, budgetMonth, accountId, tagId, categoryId), ct));
  ```

- [ ] **Step 4：跑單一類別的測試**

  `dotnet test --project tests/SixJars.Api.Tests --filter-class "*TransactionsEndpointsTests*"`

  Expected：這個類別原本的 20 個測試加上 4 個新測試，共 24 個全過。如果出現 `InvalidOperationException: ... could not be translated`，就照上面第 2 點的備案改寫，並把改寫內容回寫到 spec D1。

  **變異檢查**（每項做完都要還原）：
  - 拿掉 `|| c.ParentId == categoryId`：`List_by_main_category…` 會變紅。
  - 把 `c.Id == categoryId ||` 拿掉：`List_by_subcategory…`（子分類本身被排除）和 `List_by_main_category…`（直接掛主分類的那筆被排除）會變紅。
  - 把 `db.Books.Where(b => b.Id == bookId)` 改成 `db.Books`：`List_by_category_of_another_book…` 會變紅。

- [ ] **Step 5：跑全部測試**

  `dotnet test --solution SixJars.slnx`

  Expected：基準線 + 4，失敗 0；`dotnet build SixJars.slnx` 0 warnings。

- [ ] **Step 6：Commit**

  ```powershell
  git add src/SixJars.Application/Transactions/ListTransactions.cs src/SixJars.Api/Endpoints/TransactionsEndpoints.cs tests/SixJars.Api.Tests/TransactionsEndpointsTests.cs
  git commit -m @'
  feat(api): 交易清單新增 categoryId 篩選，涵蓋子分類、不限交易類型

  選主分類時也比對它的子分類，因為交易大多掛在子分類上；
  只從這本帳的分類展開，別本帳的 Id 跟 accountId、tagId 一樣回空清單。

  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  '@
  ```

---

## 完成後的驗證

- [ ] `dotnet build SixJars.slnx`：0 warnings、0 errors
- [ ] `dotnet test --solution SixJars.slnx`：基準線 + 4，失敗 0（如果 Acceptance 被 skip，要說明原因）
- [ ] `git status`：沒有未 commit 的後端檔案
- [ ] 本計畫只有 1 個 commit
- [ ] `git diff master -- src/SixJars.Domain src/SixJars.Infrastructure` 是空的（Domain 和 migration 都不應該動到）

## 手動驗證

依 `docs/deploy.md` 在本機跑 API，登入後：

1. `GET /api/books/{id}/transactions?budgetMonth=<有資料的月份>&categoryId=<某個支出主分類>`：回傳的交易 `categoryId` 都是這個主分類或它的子分類。
2. 同一個網址把 `categoryId` 換成子分類：只剩這個子分類的交易。
3. `categoryId=00000000-0000-0000-0000-000000000000`：回 200 和 `[]`。

## 後續（不在本計畫內）

- 多值篩選（`categoryIds`／`accountIds`）。
- 匯出依清單篩選條件輸出。

## 附錄：基準線實測

2026-10-28，HEAD `88b1473`，本機：

- `dotnet build SixJars.slnx`：0 個警告、0 個錯誤
- `dotnet test --project tests/SixJars.Domain.Tests`：228 passed、0 skipped
- Api／Application／Infrastructure／Acceptance：Docker 沒開，**未實測**，開工前補上
