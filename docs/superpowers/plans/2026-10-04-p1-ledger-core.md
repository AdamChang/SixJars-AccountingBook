# P1 帳務核心（Ledger Core）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL：以 TDD（紅 → 綠 → 重構）逐 Task 執行；每個 Task 的 Step 2「確認失敗且原因正確」不可省略。由 subagent 交付的 Task，在標記完成前必須由主控者重跑全部測試並比對 diff 與本計畫。

**Goal**：證明「分錄展開規則」與「月可用餘額」正確。從舊 Excel 匯入 2026 年 1–3 月資料後，月可用餘額、可用現金、各帳戶餘額、尚待繳清、剩餘本金、財務規劃帳戶餘額都與 Excel 一致；匯入結果寫入 PostgreSQL 再讀回後，數字仍然一致。

**Architecture**：Clean Architecture 的 P1 子集。
- `Domain`：純函式，包含實體、分錄展開與計算器。
- `Application`：Excel 語意 → Domain 的轉換，純函式、無 I/O。
- `Infrastructure`：讀取 xlsm（ExcelDataReader）與 EF Core 持久層。

MediatR 與 FluentValidation 等 P2 有 API 時才引入。

**Tech Stack**：.NET 10（SDK 10.0.401）、C# latest、EF Core 10 + Npgsql、xUnit v3（Microsoft Testing Platform）、FluentAssertions 8.11（Xceed 授權；本專案為個人非商業使用，免費）、Testcontainers（postgres:17-alpine）、ExcelDataReader。

**相關文件**
- 設計：[docs/superpowers/specs/2026-10-04-p1-ledger-core-design.md](../specs/2026-10-04-p1-ledger-core-design.md)
- 術語：[CONTEXT.md](../../../CONTEXT.md)；決策：[docs/adr/](../../adr/)

---

## 執行前必讀

### 環境

| 項目 | 值 |
|---|---|
| 路徑 | `F:\VibeCode\SixJars-AccountingBook` |
| 分支 | `feat/p1-ledger-core`（Task 0 從 `master` 建立；**不是 master**） |
| 全部測試 | `dotnet test`（repo 根目錄；`global.json` 已指定 MTP runner） |
| 單一測試類別 | `dotnet test --project tests/<專案> -- --filter-class "<完整類別名>"` |
| Build | `dotnet build`（期望 0 warning、0 error） |
| 前置條件 | Docker Desktop 執行中（段 B 需要）；`reference/2026帳本v1.xlsm` 存在（驗收用，缺檔時相關測試會**略過**） |

MTP 的輸出是中文摘要：`總計: N`、`失敗: 0`、`已成功: N`、`已略過: N`。下文的 `Expected` 都以這組數字表示。

### 基準線

全新專案，起點是 0 個測試。之後每個 Task 的期望總數見各 Step 5，**任何時候數字低於該 Task 的期望值，就代表弄壞了東西**。
`reference/` 缺檔時，「已略過」會增加（Task 9 的 5 個、Task 13 的 4 個、Task 16 的 3 個），但「總計」不變。

### 絕對不要碰的檔案

- `reference/**`：個資，已列在 `.gitignore`，**永遠不可 `git add`**。
- `CONTEXT.md`、`docs/adr/**`：只有在術語或決策真的改變時才修改，而且要用獨立的 commit。
- 每個 Task 都要用**明確路徑**執行 `git add`，**禁止 `git add .` / `git add -A`**。

### 慣例

- **語言**：註解、XML doc、例外訊息、commit message 一律使用繁體中文；識別字使用英文。
- **C#**：file-scoped namespace、`sealed` 預設、primary constructor、collection expression。I/O 一律 async。
- **金額**：`decimal`；分錄符號採資產觀點，負債帳戶的餘額為負。
- **測試分層**：
  - `Domain.Tests`：純單元測試。
  - `Application.Tests`：合成的 `LegacyWorkbook`，不讀真實檔案。
  - `Infrastructure.Tests`：讀真實 xlsm（缺檔就略過），以及 Testcontainers。
  - `AcceptanceTests`：端到端逐月比對。
- **非同步測試**：一律傳入 `TestContext.Current.CancellationToken`，避免 xUnit1051 警告。
- **TDD 順序**：失敗測試 → 確認失敗原因 → 最小實作 → 單檔測試 → 全部測試 → commit。
- **一個 Task 一個 commit**。訊息格式為 `<type>(<scope>): <繁中摘要>`，結尾加上：
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  ```
- **權威來源**：committed code 才是權威，計畫裡的 snippet 是歷史。偏離計畫時，在段落結束時用 `docs(plans):` commit 回寫原因。

---

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `global.json` | 鎖定 SDK、指定 Microsoft Testing Platform |
| `Directory.Build.props` / `Directory.Packages.props` | 共用屬性；集中管理套件版本 |
| `SixJars.slnx` | Solution |
| `src/SixJars.Domain/Common/{Ids,BudgetMonth,DomainException}.cs` | 強型別 Id、歸屬月份、領域例外 |
| `src/SixJars.Domain/Books/{Book,Account,AccountType,PlanningFund,Category,CategoryKind,ExpenseNature}.cs` | 帳本聚合 |
| `src/SixJars.Domain/Transactions/{Transaction,TransactionKind,Posting,TransactionFactory}.cs` | 交易、分錄展開與驗證 |
| `src/SixJars.Domain/Planning/PlannedExpense.cs` | 預定支出 |
| `src/SixJars.Domain/Ledger/{LedgerSnapshot,BalanceCalculator,DisposableBalanceCalculator,AvailableCashCalculator}.cs` | 計算 |
| `src/SixJars.Application/LegacyImport/{LegacyWorkbook,ILegacyWorkbookReader,LegacyDateNormalizer,LegacyNames,ImportReport,LegacyWorkbookMapper,MappingSession*.cs}` | Excel → Domain |
| `src/SixJars.Infrastructure/LegacyExcel/{SheetGrid,ExcelLegacyWorkbookReader}.cs` | 讀 xlsm |
| `src/SixJars.Infrastructure/Persistence/**` | DbContext、mapping、converter、migration、snapshot loader |
| `tests/Shared/{RepoPaths,PostgresFixture}.cs` | 以 link 方式供多個測試專案共用 |
| `tests/SixJars.{Domain,Application,Infrastructure}.Tests/**`、`tests/SixJars.AcceptanceTests/**` | 測試 |

### 修改

| 檔案 | 改動 |
|---|---|
| `.gitignore` | 加入 `TestResults/`、`*.user` |
| `src/SixJars.Domain/Transactions/Transaction.cs`（Task 14） | 加入 EF 專用的 private 無參數建構子 |

### Task 相依順序

```
段 A1（Domain）
T0 ─► T1 ─► T2 ─► T3 ─┬─► T4 ─┐
                      ├─► T5 ─┼─► T7 ─┐
                      └─► T6 ─┴───────┴─► T8   ◄── checkpoint A1
段 A2（匯入與驗收）
T0 ─► T9（讀取器，只依賴 T0，可與 A1 並行）
T2,T9 ─► T10 ─► T11（需 T3–T5）─► T12（需 T6）─► T13（需 T8）  ◄── checkpoint A
段 B（持久層）
T6 ─► T14 ─► T15 ─► T16（需 T13）  ◄── checkpoint B
```

---

## Task 0：分支與 Solution 骨架

**Files：** Create: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `SixJars.slnx`, `src/SixJars.{Domain,Application,Infrastructure}/*.csproj`；Modify: `.gitignore`

沒有測試的骨架 Task。容易出錯的地方有兩個：.NET 10 SDK 的 `dotnet test` 必須在 `global.json` 指定 MTP，否則 xUnit v3 會報錯（已在 spike 驗證過）；`System.Text.Encoding.CodePages` 在 .NET 10 是內建的，額外參考會產生 NU1510 警告。

- [ ] Step 1：建立分支
  ```bash
  git switch -c feat/p1-ledger-core
  ```
- [ ] Step 2：寫 `global.json`
  ```json
  {
    "sdk": { "version": "10.0.401", "rollForward": "latestFeature" },
    "test": { "runner": "Microsoft.Testing.Platform" }
  }
  ```
- [ ] Step 3：寫 `Directory.Build.props`
  ```xml
  <Project>
    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <Nullable>enable</Nullable>
      <ImplicitUsings>enable</ImplicitUsings>
      <LangVersion>latest</LangVersion>
    </PropertyGroup>
  </Project>
  ```
- [ ] Step 4：寫 `Directory.Packages.props`
  ```xml
  <Project>
    <PropertyGroup>
      <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    </PropertyGroup>
    <ItemGroup>
      <PackageVersion Include="ExcelDataReader" Version="3.9.0" />
      <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
      <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12" />
      <!-- 段 B 補上（commit 1d0c270）：Npgsql 只要求 EF Core >= 10.0.4，需明確參考 Relational 才能全面對齊 10.0.12 -->
      <PackageVersion Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.12" />
      <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
      <PackageVersion Include="xunit.v3" Version="4.0.1" />
      <PackageVersion Include="xunit.runner.visualstudio" Version="4.0.0" />
      <PackageVersion Include="FluentAssertions" Version="8.11.0" />
      <PackageVersion Include="Testcontainers.PostgreSql" Version="4.15.0" />
    </ItemGroup>
  </Project>
  ```
- [ ] Step 5：建立三個 src 專案（手寫 csproj，不用 `dotnet new classlib`，以免產生 `Class1.cs`）
  - `src/SixJars.Domain/SixJars.Domain.csproj`：`<Project Sdk="Microsoft.NET.Sdk" />`（空的 PropertyGroup 即可）
  - `src/SixJars.Application/SixJars.Application.csproj`：ProjectReference → Domain
  - `src/SixJars.Infrastructure/SixJars.Infrastructure.csproj`：ProjectReference → Application；PackageReference `ExcelDataReader`
  ```bash
  dotnet new sln -n SixJars
  dotnet sln SixJars.slnx add src/SixJars.Domain src/SixJars.Application src/SixJars.Infrastructure
  ```
  確認產生的是 `SixJars.slnx`；如果是 `.sln`，改用 `dotnet new sln -n SixJars --format slnx`。
- [ ] Step 6：`.gitignore` 加上 `TestResults/` 與 `*.user`（另起新行；原檔最後一行沒有換行字元）
- [ ] Step 7：`dotnet build` → Expected：0 warning、0 error
- [ ] Step 8：Commit
  ```bash
  git add global.json Directory.Build.props Directory.Packages.props SixJars.slnx .gitignore src/SixJars.Domain/SixJars.Domain.csproj src/SixJars.Application/SixJars.Application.csproj src/SixJars.Infrastructure/SixJars.Infrastructure.csproj docs/superpowers/specs/2026-10-04-p1-ledger-core-design.md docs/superpowers/plans/2026-10-04-p1-ledger-core.md
  git commit -m "chore: 建立 P1 solution 骨架與設計、計畫文件" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

## Task 1：歸屬月份、強型別 Id、領域例外

**Files：** Create: `src/SixJars.Domain/Common/{Ids,BudgetMonth,DomainException}.cs`、`tests/SixJars.Domain.Tests/SixJars.Domain.Tests.csproj`、`tests/SixJars.Domain.Tests/Common/BudgetMonthTests.cs`

歸屬月份是整個月可用餘額的時間軸。`Key`（yyyymm）同時是持久化的格式，所以它的往返正確性要在這裡就鎖住。

- [ ] Step 1：建立測試專案並寫失敗測試

  `tests/SixJars.Domain.Tests/SixJars.Domain.Tests.csproj`：
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <OutputType>Exe</OutputType>
      <IsPackable>false</IsPackable>
    </PropertyGroup>
    <ItemGroup>
      <PackageReference Include="Microsoft.NET.Test.Sdk" />
      <PackageReference Include="xunit.v3" />
      <PackageReference Include="xunit.runner.visualstudio" />
      <PackageReference Include="FluentAssertions" />
    </ItemGroup>
    <ItemGroup>
      <ProjectReference Include="..\..\src\SixJars.Domain\SixJars.Domain.csproj" />
    </ItemGroup>
  </Project>
  ```
  `dotnet sln SixJars.slnx add tests/SixJars.Domain.Tests`

  `tests/SixJars.Domain.Tests/Common/BudgetMonthTests.cs`：
  ```csharp
  using FluentAssertions;
  using SixJars.Domain.Common;
  using Xunit;

  namespace SixJars.Domain.Tests.Common;

  public class BudgetMonthTests
  {
      [Fact]
      public void Of_date_takes_year_and_month() =>
          BudgetMonth.Of(new DateOnly(2025, 12, 31)).Should().Be(new BudgetMonth(2025, 12));

      [Theory]
      [InlineData(0)]
      [InlineData(13)]
      public void Rejects_month_out_of_range(int month)
      {
          var act = () => new BudgetMonth(2026, month);
          act.Should().Throw<ArgumentOutOfRangeException>();
      }

      [Fact]
      public void Compares_across_years()
      {
          (new BudgetMonth(2025, 12) < new BudgetMonth(2026, 1)).Should().BeTrue();
          (new BudgetMonth(2026, 3) >= new BudgetMonth(2026, 3)).Should().BeTrue();
      }

      [Fact]
      public void Key_round_trips_as_yyyymm()
      {
          var month = new BudgetMonth(2026, 2);
          month.Key.Should().Be(202602);
          BudgetMonth.FromKey(202602).Should().Be(month);
      }

      [Fact]
      public void Formats_as_iso_month() => new BudgetMonth(2026, 1).ToString().Should().Be("2026-01");
  }
  ```
- [ ] Step 2：`dotnet build` → 確認失敗原因是 **CS0246 找不到 `BudgetMonth`**，而不是套件還原或專案參考錯誤。
- [ ] Step 3：最小實作

  `Common/Ids.cs`：
  ```csharp
  namespace SixJars.Domain.Common;

  public readonly record struct BookId(Guid Value) { public static BookId New() => new(Guid.CreateVersion7()); }
  public readonly record struct AccountId(Guid Value) { public static AccountId New() => new(Guid.CreateVersion7()); }
  public readonly record struct PlanningFundId(Guid Value) { public static PlanningFundId New() => new(Guid.CreateVersion7()); }
  public readonly record struct CategoryId(Guid Value) { public static CategoryId New() => new(Guid.CreateVersion7()); }
  public readonly record struct TransactionId(Guid Value) { public static TransactionId New() => new(Guid.CreateVersion7()); }
  public readonly record struct PlannedExpenseId(Guid Value) { public static PlannedExpenseId New() => new(Guid.CreateVersion7()); }
  ```
  `Common/DomainException.cs`：
  ```csharp
  namespace SixJars.Domain.Common;

  /// <summary>違反領域規則時拋出。</summary>
  public sealed class DomainException(string message) : Exception(message);
  ```
  `Common/BudgetMonth.cs`：
  ```csharp
  namespace SixJars.Domain.Common;

  /// <summary>歸屬月份：交易計入月可用餘額、預算與月報表的月份（可與交易日期不同）。</summary>
  public readonly record struct BudgetMonth : IComparable<BudgetMonth>
  {
      public BudgetMonth(int year, int month)
      {
          ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
          ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
          Year = year;
          Month = month;
      }

      public int Year { get; }
      public int Month { get; }

      /// <summary>yyyymm，供排序與持久化。</summary>
      public int Key => Year * 100 + Month;

      public static BudgetMonth Of(DateOnly date) => new(date.Year, date.Month);
      public static BudgetMonth FromKey(int key) => new(key / 100, key % 100);

      public int CompareTo(BudgetMonth other) => Key.CompareTo(other.Key);
      public static bool operator <(BudgetMonth left, BudgetMonth right) => left.CompareTo(right) < 0;
      public static bool operator >(BudgetMonth left, BudgetMonth right) => left.CompareTo(right) > 0;
      public static bool operator <=(BudgetMonth left, BudgetMonth right) => left.CompareTo(right) <= 0;
      public static bool operator >=(BudgetMonth left, BudgetMonth right) => left.CompareTo(right) >= 0;

      public override string ToString() => $"{Year:D4}-{Month:D2}";
  }
  ```
- [ ] Step 4：`dotnet test --project tests/SixJars.Domain.Tests -- --filter-class "SixJars.Domain.Tests.Common.BudgetMonthTests"` → Expected：總計 6、失敗 0
- [ ] Step 5：`dotnet test` → Expected：總計 6、失敗 0
- [ ] Step 6：Commit
  ```bash
  git add SixJars.slnx src/SixJars.Domain/Common tests/SixJars.Domain.Tests/SixJars.Domain.Tests.csproj tests/SixJars.Domain.Tests/Common/BudgetMonthTests.cs
  git commit -m "feat(domain): 新增歸屬月份、強型別識別碼與領域例外" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

## Task 2：帳本聚合（帳戶、財務規劃帳戶、兩層分類）

**Files：** Create: `src/SixJars.Domain/Books/*.cs`、`tests/SixJars.Domain.Tests/Books/BookTests.cs`

Book 是設定資料的聚合根。容易錯的地方有三個：
- 「計入可用現金」只對現金帳戶有意義，其他類型要強制為 false，否則可用現金會誤算銀行。
- 子分類必須繼承主分類的種類與支出性質。
- 分類只有兩層。

- [ ] Step 1：寫失敗測試 `Books/BookTests.cs`
  ```csharp
  using FluentAssertions;
  using SixJars.Domain.Books;
  using SixJars.Domain.Common;
  using Xunit;

  namespace SixJars.Domain.Tests.Books;

  public class BookTests
  {
      private readonly Book _book = new("測試帳本", new DateOnly(2025, 12, 30));

      [Fact]
      public void AddAccount_keeps_type_and_opening_balance()
      {
          var card = _book.AddAccount("國泰Combo卡", AccountType.CreditCard, -22668m);

          card.Type.Should().Be(AccountType.CreditCard);
          card.OpeningBalance.Should().Be(-22668m);
          card.IsLiability.Should().BeTrue();
          _book.GetAccount(card.Id).Should().BeSameAs(card);
      }

      [Fact]
      public void Rejects_duplicate_account_name()
      {
          _book.AddAccount("現金", AccountType.Cash);
          var act = () => _book.AddAccount("現金", AccountType.Cash);
          act.Should().Throw<DomainException>();
      }

      [Fact]
      public void Only_cash_accounts_can_count_as_available_cash()
      {
          _book.AddAccount("外幣現鈔", AccountType.Cash, countsAsAvailableCash: false).CountsAsAvailableCash.Should().BeFalse();
          _book.AddAccount("現金", AccountType.Cash).CountsAsAvailableCash.Should().BeTrue();
          _book.AddAccount("國泰世華銀行", AccountType.Bank).CountsAsAvailableCash.Should().BeFalse();
      }

      [Fact]
      public void Sub_category_inherits_kind_and_nature()
      {
          var fixedMain = _book.AddExpenseCategory("固定支出", ExpenseNature.Fixed);
          var insurance = _book.AddSubCategory(fixedMain.Id, "保險費");

          insurance.Kind.Should().Be(CategoryKind.Expense);
          insurance.Nature.Should().Be(ExpenseNature.Fixed);
          insurance.ParentId.Should().Be(fixedMain.Id);
      }

      [Fact]
      public void Categories_have_only_two_levels()
      {
          var main = _book.AddExpenseCategory("主食", ExpenseNature.Floating);
          var sub = _book.AddSubCategory(main.Id, "早餐");
          var act = () => _book.AddSubCategory(sub.Id, "燒餅");
          act.Should().Throw<DomainException>();
      }

      [Fact]
      public void FindCategory_by_main_and_sub_name()
      {
          var main = _book.AddIncomeCategory("其它收入");
          var lottery = _book.AddSubCategory(main.Id, "中獎");

          _book.FindCategory("其它收入").Should().BeSameAs(main);
          _book.FindCategory("其它收入", "中獎").Should().BeSameAs(lottery);
          _book.FindCategory("其它收入", "不存在").Should().BeNull();
      }

      [Fact]
      public void GetAccount_with_unknown_id_throws()
      {
          var act = () => _book.GetAccount(AccountId.New());
          act.Should().Throw<DomainException>();
      }
  }
  ```
- [ ] Step 2：`dotnet build tests/SixJars.Domain.Tests` → 確認失敗原因是 **CS0246 找不到 `Book` / `AccountType`**
- [ ] Step 3：最小實作（`namespace SixJars.Domain.Books`）
  ```csharp
  public enum AccountType { Cash, Bank, CreditCard, EWallet, Loan }
  public enum CategoryKind { Income, Expense }
  public enum ExpenseNature { Floating, Fixed, Loan, Special }

  /// <summary>實際持有金錢或負債的地方。餘額採資產觀點：負債帳戶為負數。</summary>
  public sealed class Account
  {
      internal Account(AccountId id, string name, AccountType type, decimal openingBalance, bool countsAsAvailableCash)
      {
          Id = id;
          Name = name;
          Type = type;
          OpeningBalance = openingBalance;
          CountsAsAvailableCash = countsAsAvailableCash;
      }

      public AccountId Id { get; private set; }
      public string Name { get; private set; }
      public AccountType Type { get; private set; }
      public decimal OpeningBalance { get; private set; }
      /// <summary>只有現金帳戶可能為 true（例如外幣現鈔為 false）。</summary>
      public bool CountsAsAvailableCash { get; private set; }
      public bool IsLiability => Type is AccountType.CreditCard or AccountType.Loan;
  }

  /// <summary>財務規劃帳戶：指定用途的虛擬信封，不是帳戶（ADR 0003）。</summary>
  public sealed class PlanningFund
  {
      internal PlanningFund(PlanningFundId id, string name, decimal openingBalance)
      {
          Id = id;
          Name = name;
          OpeningBalance = openingBalance;
      }

      public PlanningFundId Id { get; private set; }
      public string Name { get; private set; }
      public decimal OpeningBalance { get; private set; }
  }

  public sealed class Category
  {
      internal Category(CategoryId id, string name, CategoryKind kind, ExpenseNature? nature, CategoryId? parentId)
      {
          Id = id;
          Name = name;
          Kind = kind;
          Nature = nature;
          ParentId = parentId;
      }

      public CategoryId Id { get; private set; }
      public string Name { get; private set; }
      public CategoryKind Kind { get; private set; }
      /// <summary>支出性質；收入分類為 null。</summary>
      public ExpenseNature? Nature { get; private set; }
      public CategoryId? ParentId { get; private set; }
      public bool IsMain => ParentId is null;
  }
  ```
  `Book`：私有欄位 `_accounts`、`_planningFunds`、`_categories`（`List<T>`），對外暴露 `IReadOnlyList<T>`。對外方法：
  - 建構子 `Book(string name, DateOnly openingDate)`：Id 用 `BookId.New()` 產生。
  - `AddAccount(string name, AccountType type, decimal openingBalance = 0m, bool countsAsAvailableCash = true)`：實際存入 `type == Cash && countsAsAvailableCash`。
  - `AddPlanningFund(string name, decimal openingBalance = 0m)`
  - `AddIncomeCategory(string name)`、`AddExpenseCategory(string name, ExpenseNature nature)`
  - `AddSubCategory(CategoryId parentId, string name)`：父分類不是主分類時拋出 `DomainException`。
  - `GetAccount`、`GetPlanningFund`、`GetCategory`：找不到時拋出 `DomainException`。
  - `FindAccount(string)`、`FindPlanningFund(string)`、`FindCategory(string mainName, string? subName = null)`：找不到時回傳 null。
  - 名稱不可空白，並且在同一個範圍內不可重複：帳戶之間、財務規劃帳戶之間、主分類之間、同一主分類的子分類之間。違反時拋出 `DomainException`，訊息需包含名稱。
- [ ] Step 4：`dotnet test --project tests/SixJars.Domain.Tests -- --filter-class "SixJars.Domain.Tests.Books.BookTests"` → Expected：總計 7、失敗 0
- [ ] Step 5：`dotnet test` → Expected：總計 13、失敗 0
- [ ] Step 6：Commit
  ```bash
  git add src/SixJars.Domain/Books tests/SixJars.Domain.Tests/Books/BookTests.cs
  git commit -m "feat(domain): 新增帳本聚合（帳戶、財務規劃帳戶、兩層分類）" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

## Task 3：交易與分錄展開（收入、支出、轉帳類）

**Files：** Create: `src/SixJars.Domain/Transactions/{Transaction,TransactionKind,Posting,TransactionFactory}.cs`、`tests/SixJars.Domain.Tests/SampleBook.cs`、`tests/SixJars.Domain.Tests/Transactions/TransactionFactoryBasicTests.cs`

這是 ADR 0001 的核心。容易錯的地方有兩個：
- 每種交易類型的「主帳戶／對方帳戶」角色不一致：原則上 `AccountId` 是 Excel 的「方式」帳戶，唯一的例外是轉帳，依 Q16 改成「從 → 到」。
- 帳戶類型限制，例如繳卡費不能由電子錢包支付。

`SampleBook` 會讓 Task 3–8 共用。

- [ ] Step 1：寫測試輔助與失敗測試

  `tests/SixJars.Domain.Tests/SampleBook.cs`：
  ```csharp
  using SixJars.Domain.Books;
  using SixJars.Domain.Transactions;

  namespace SixJars.Domain.Tests;

  /// <summary>以真實帳本結構縮小而成的測試帳本，數字取自 2026 年 1 月 Excel。</summary>
  internal sealed class SampleBook
  {
      public SampleBook()
      {
          Book = new Book("測試帳本", new DateOnly(2025, 12, 30));
          Cash = Book.AddAccount("現金", AccountType.Cash, 2071m);
          ForeignCash = Book.AddAccount("外幣現鈔", AccountType.Cash, 10000m, countsAsAvailableCash: false);
          Bank = Book.AddAccount("國泰世華銀行", AccountType.Bank, 21610m);
          OtherBank = Book.AddAccount("華南銀行", AccountType.Bank, 11637m);
          Investment = Book.AddAccount("國泰投資帳戶", AccountType.Bank, 20m);
          Card = Book.AddAccount("國泰Combo卡", AccountType.CreditCard, -22668m);
          Wallet = Book.AddAccount("悠遊卡", AccountType.EWallet, 152m);
          Loan = Book.AddAccount("房屋貸款", AccountType.Loan, -2658876m);
          FreedomFund = Book.AddPlanningFund("財務自由帳戶", 9874.3m);
          Salary = Book.AddIncomeCategory("工作薪資1");
          Food = Book.AddExpenseCategory("主食", ExpenseNature.Floating);
          Fixed = Book.AddExpenseCategory("固定支出", ExpenseNature.Fixed);
          Insurance = Book.AddSubCategory(Fixed.Id, "保險費");
          Phone = Book.AddSubCategory(Fixed.Id, "行動電話費");
          LoanExpense = Book.AddExpenseCategory("貸款支出", ExpenseNature.Loan);
          Mortgage = Book.AddSubCategory(LoanExpense.Id, "房屋貸款");
          Factory = new TransactionFactory(Book);
      }

      public Book Book { get; }
      public Account Cash { get; }
      public Account ForeignCash { get; }
      public Account Bank { get; }
      public Account OtherBank { get; }
      public Account Investment { get; }
      public Account Card { get; }
      public Account Wallet { get; }
      public Account Loan { get; }
      public PlanningFund FreedomFund { get; }
      public Category Salary { get; }
      public Category Food { get; }
      public Category Fixed { get; }
      public Category Insurance { get; }
      public Category Phone { get; }
      public Category LoanExpense { get; }
      public Category Mortgage { get; }
      public TransactionFactory Factory { get; }
  }
  ```
  `tests/SixJars.Domain.Tests/Transactions/TransactionFactoryBasicTests.cs`：
  ```csharp
  using FluentAssertions;
  using SixJars.Domain.Common;
  using SixJars.Domain.Transactions;
  using Xunit;

  namespace SixJars.Domain.Tests.Transactions;

  public class TransactionFactoryBasicTests
  {
      private static readonly DateOnly Jan5 = new(2026, 1, 5);
      private readonly SampleBook _s = new();

      [Fact]
      public void Income_posts_amount_to_account()
      {
          var tx = _s.Factory.Income(Jan5, _s.Bank.Id, _s.Salary.Id, 84223m);

          tx.Kind.Should().Be(TransactionKind.Income);
          tx.BookId.Should().Be(_s.Book.Id);
          tx.BudgetMonth.Should().Be(new BudgetMonth(2026, 1));
          tx.Postings.Should().Equal(new Posting(_s.Bank.Id, 84223m));
      }

      [Fact]
      public void Budget_month_can_differ_from_date()
      {
          var tx = _s.Factory.Income(new DateOnly(2025, 12, 31), _s.Bank.Id, _s.Salary.Id, 84223m, budgetMonth: new BudgetMonth(2026, 1));

          tx.Date.Should().Be(new DateOnly(2025, 12, 31));
          tx.BudgetMonth.Should().Be(new BudgetMonth(2026, 1));
      }

      [Fact]
      public void Expense_on_card_increases_liability() =>
          _s.Factory.Expense(Jan5, _s.Card.Id, _s.Food.Id, -129m).Postings
              .Should().Equal(new Posting(_s.Card.Id, -129m));

      [Fact]
      public void Expense_rejects_income_category()
      {
          var act = () => _s.Factory.Expense(Jan5, _s.Bank.Id, _s.Salary.Id, -1m);
          act.Should().Throw<DomainException>();
      }

      [Fact]
      public void Transfer_moves_money_from_to()
      {
          var tx = _s.Factory.Transfer(Jan5, _s.Bank.Id, _s.OtherBank.Id, 34000m);

          tx.AccountId.Should().Be(_s.Bank.Id);
          tx.CounterAccountId.Should().Be(_s.OtherBank.Id);
          tx.Postings.Should().Equal(new Posting(_s.Bank.Id, -34000m), new Posting(_s.OtherBank.Id, 34000m));
      }

      [Fact]
      public void Transfer_rejects_non_positive_amount()
      {
          var act = () => _s.Factory.Transfer(Jan5, _s.Bank.Id, _s.OtherBank.Id, -1m);
          act.Should().Throw<DomainException>();
      }

      [Fact]
      public void Transfer_rejects_same_account()
      {
          var act = () => _s.Factory.Transfer(Jan5, _s.Bank.Id, _s.Bank.Id, 1m);
          act.Should().Throw<DomainException>();
      }

      [Fact]
      public void Withdrawal_moves_bank_to_cash() =>
          _s.Factory.Withdrawal(Jan5, _s.Bank.Id, _s.Cash.Id, 3000m).Postings
              .Should().Equal(new Posting(_s.Bank.Id, -3000m), new Posting(_s.Cash.Id, 3000m));

      [Fact]
      public void CashDeposit_moves_cash_to_bank() =>
          _s.Factory.CashDeposit(Jan5, _s.Bank.Id, _s.Cash.Id, 20000m).Postings
              .Should().Equal(new Posting(_s.Cash.Id, -20000m), new Posting(_s.Bank.Id, 20000m));

      [Fact]
      public void TopUp_from_card_increases_wallet_and_card_liability() =>
          _s.Factory.TopUp(Jan5, _s.Wallet.Id, _s.Card.Id, 3000m).Postings
              .Should().Equal(new Posting(_s.Card.Id, -3000m), new Posting(_s.Wallet.Id, 3000m));

      [Fact]
      public void CardPayment_moves_bank_to_card() =>
          _s.Factory.CardPayment(Jan5, _s.Bank.Id, _s.Card.Id, 18197m).Postings
              .Should().Equal(new Posting(_s.Bank.Id, -18197m), new Posting(_s.Card.Id, 18197m));

      [Fact]
      public void CardPayment_rejects_wallet_payer()
      {
          var act = () => _s.Factory.CardPayment(Jan5, _s.Wallet.Id, _s.Card.Id, 100m);
          act.Should().Throw<DomainException>();
      }
  }
  ```
- [ ] Step 2：`dotnet build tests/SixJars.Domain.Tests` → 確認失敗原因是 **CS0246 找不到 `TransactionFactory` / `Posting`**
- [ ] Step 3：最小實作（`namespace SixJars.Domain.Transactions`）
  ```csharp
  public enum TransactionKind
  {
      Income, Expense, Transfer, Withdrawal, CashDeposit, TopUp, CardPayment,
      LoanDisbursement, LoanPayment, FundAllocation, FundWithdrawal, FundReturn,
  }

  /// <summary>交易對單一帳戶的帶號金額變動（資產觀點）。</summary>
  public sealed record Posting(AccountId AccountId, decimal Amount);
  ```
  `Transaction`：
  - 建構子為 `internal`：`(BookId bookId, TransactionKind kind, DateOnly date, BudgetMonth budgetMonth, decimal amount, AccountId accountId, AccountId? counterAccountId, CategoryId? categoryId, PlanningFundId? planningFundId, decimal? loanPrincipal, string? note, IEnumerable<Posting> postings)`。
  - 屬性一律 `{ get; private set; }`，`Id = TransactionId.New()`。
  - `Postings` 由 `List<Posting> _postings` 支撐，對外為 `IReadOnlyList<Posting>`。
  - 計算屬性：
    ```csharp
    /// <summary>對財務規劃帳戶的影響：入新資金與資金回流為正、出資金為負。</summary>
    public decimal FundDelta => Kind switch
    {
        TransactionKind.FundAllocation or TransactionKind.FundReturn => Amount,
        TransactionKind.FundWithdrawal => -Amount,
        _ => 0m,
    };

    public decimal? LoanInterest => Kind == TransactionKind.LoanPayment ? Amount - LoanPrincipal : null;
    ```
  - XML doc 寫明各交易類型的角色：`AccountId` 是「方式」帳戶，唯一例外是轉帳，此時為「從」。

  `TransactionFactory(Book book)`：每個方法的最後三個參數都是 `string? note = null, BudgetMonth? budgetMonth = null`，`budgetMonth` 預設為 `BudgetMonth.Of(date)`。

  | 方法 | 驗證 | `AccountId` / `Counter` | 分錄 |
  |---|---|---|---|
  | `Income(date, accountId, categoryId, amount)` | amount≠0；帳戶 Cash/Bank/EWallet；分類 Income | account / – | `(account, amount)` |
  | `Expense(date, accountId, categoryId, amount)` | amount≠0；帳戶不可為 Loan；分類 Expense | account / – | `(account, amount)` |
  | `Transfer(date, fromId, toId, amount)` | amount>0；from≠to；兩端 Cash/Bank | from / to | `(from,−x)(to,+x)` |
  | `Withdrawal(date, bankId, cashId, amount)` | amount>0；Bank、Cash | bank / cash | `(bank,−x)(cash,+x)` |
  | `CashDeposit(date, bankId, cashId, amount)` | 同上 | bank / cash | `(cash,−x)(bank,+x)` |
  | `TopUp(date, walletId, sourceId, amount)` | amount>0；EWallet；來源 Cash/Bank/CreditCard | wallet / source | `(source,−x)(wallet,+x)` |
  | `CardPayment(date, payerId, cardId, amount)` | amount>0；payer Cash/Bank；CreditCard | payer / card | `(payer,−x)(card,+x)` |

  私有輔助方法：
  - `RequireAccount(AccountId id, params AccountType[] allowed)`：訊息需包含帳戶名稱與類型。
  - `RequireCategory(CategoryId id, CategoryKind kind)`
  - `RequirePositive(decimal amount)`、`RequireNonZero(decimal amount)`
  - `static Posting[] Move(AccountId from, AccountId to, decimal amount) => [new(from, -amount), new(to, amount)];`
- [ ] Step 4：`dotnet test --project tests/SixJars.Domain.Tests -- --filter-class "SixJars.Domain.Tests.Transactions.TransactionFactoryBasicTests"` → Expected：總計 12、失敗 0
- [ ] Step 5：`dotnet test` → Expected：總計 25、失敗 0
- [ ] Step 6：Commit
  ```bash
  git add src/SixJars.Domain/Transactions tests/SixJars.Domain.Tests/SampleBook.cs tests/SixJars.Domain.Tests/Transactions/TransactionFactoryBasicTests.cs
  git commit -m "feat(domain): 交易展開為分錄（收入、支出、轉帳、提款、存入、加值、繳卡費）" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

## Task 4：貸款交易（新增貸款、貸款繳款）

**Files：** Modify: `src/SixJars.Domain/Transactions/TransactionFactory.cs`；Create: `tests/SixJars.Domain.Tests/Transactions/TransactionFactoryLoanTests.cs`

貸款繳款是唯一「分錄金額不平衡」的類型：付款帳戶付出總額，貸款餘額只減少本金，利息是消失的支出（Q13）。提前還本就是利息為 0 的繳款。

- [ ] Step 1：寫失敗測試
  ```csharp
  using FluentAssertions;
  using SixJars.Domain.Common;
  using SixJars.Domain.Transactions;
  using Xunit;

  namespace SixJars.Domain.Tests.Transactions;

  public class TransactionFactoryLoanTests
  {
      private static readonly DateOnly Jan9 = new(2026, 1, 9);
      private readonly SampleBook _s = new();

      [Fact]
      public void LoanDisbursement_increases_loan_and_receiver() =>
          _s.Factory.LoanDisbursement(Jan9, _s.Bank.Id, _s.Loan.Id, 500000m).Postings
              .Should().Equal(new Posting(_s.Loan.Id, -500000m), new Posting(_s.Bank.Id, 500000m));

      [Fact]
      public void LoanPayment_splits_principal_and_interest()
      {
          var tx = _s.Factory.LoanPayment(Jan9, _s.OtherBank.Id, _s.Loan.Id, 32503m, 28085m, _s.Mortgage.Id);

          tx.Postings.Should().Equal(new Posting(_s.OtherBank.Id, -32503m), new Posting(_s.Loan.Id, 28085m));
          tx.LoanPrincipal.Should().Be(28085m);
          tx.LoanInterest.Should().Be(4418m);
          tx.CategoryId.Should().Be(_s.Mortgage.Id);
      }

      [Fact]
      public void Prepayment_has_zero_interest() =>
          _s.Factory.LoanPayment(Jan9, _s.OtherBank.Id, _s.Loan.Id, 60000m, 60000m).LoanInterest.Should().Be(0m);

      [Fact]
      public void LoanPayment_rejects_principal_above_total()
      {
          var act = () => _s.Factory.LoanPayment(Jan9, _s.OtherBank.Id, _s.Loan.Id, 100m, 101m);
          act.Should().Throw<DomainException>();
      }

      [Fact]
      public void LoanPayment_rejects_card_payer()
      {
          var act = () => _s.Factory.LoanPayment(Jan9, _s.Card.Id, _s.Loan.Id, 100m, 50m);
          act.Should().Throw<DomainException>();
      }
  }
  ```
- [ ] Step 2：`dotnet build tests/SixJars.Domain.Tests` → 確認失敗原因是 **CS1061 `TransactionFactory` 沒有 `LoanDisbursement` / `LoanPayment`**
- [ ] Step 3：實作
  - `LoanDisbursement(date, receiverId, loanId, amount, note, budgetMonth)`
    - 驗證：amount>0；receiver 為 Cash/Bank；loan 為 Loan。
    - `AccountId=receiver`、`Counter=loan`。
    - 分錄：`Move(loan, receiver, amount)`。
  - `LoanPayment(date, payerId, loanId, total, principal, CategoryId? interestCategoryId = null, note, budgetMonth)`
    - 驗證：total>0；`0 ≤ principal ≤ total`，否則拋出 `DomainException("本金必須介於 0 與繳款總額之間")`；payer 為 Cash/Bank；loan 為 Loan；如果有分類，必須是 Expense。
    - `Amount=total`、`AccountId=payer`、`Counter=loan`、`LoanPrincipal=principal`。
    - 分錄：`(payer, −total)`；principal>0 時加上 `(loan, +principal)`。
- [ ] Step 4：`dotnet test --project tests/SixJars.Domain.Tests -- --filter-class "SixJars.Domain.Tests.Transactions.TransactionFactoryLoanTests"` → Expected：總計 5、失敗 0
- [ ] Step 5：`dotnet test` → Expected：總計 30、失敗 0
- [ ] Step 6：Commit
  ```bash
  git add src/SixJars.Domain/Transactions/TransactionFactory.cs tests/SixJars.Domain.Tests/Transactions/TransactionFactoryLoanTests.cs
  git commit -m "feat(domain): 新增貸款撥款與貸款繳款（本金、利息拆分）" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

## Task 5：財務規劃帳戶交易（入新資金、出資金、資金回流）

**Files：** Modify: `TransactionFactory.cs`；Create: `tests/SixJars.Domain.Tests/Transactions/TransactionFactoryFundTests.cs`

信封模型（ADR 0003）：財務規劃帳戶本身沒有分錄，只有 `FundDelta`。入新資金可以只是同一個帳戶內的圈存，此時沒有任何分錄；from 和 to 相同時也要視為圈存，否則會產生一進一出、互相抵銷的兩筆雜訊分錄。

- [ ] Step 1：寫失敗測試
  ```csharp
  using FluentAssertions;
  using SixJars.Domain.Common;
  using SixJars.Domain.Transactions;
  using Xunit;

  namespace SixJars.Domain.Tests.Transactions;

  public class TransactionFactoryFundTests
  {
      private static readonly DateOnly Jan11 = new(2026, 1, 11);
      private readonly SampleBook _s = new();

      [Fact]
      public void Allocation_with_transfer_moves_money_and_fills_fund()
      {
          var tx = _s.Factory.FundAllocation(Jan11, _s.FreedomFund.Id, _s.Investment.Id, _s.Bank.Id, 8000m);

          tx.Postings.Should().Equal(new Posting(_s.Bank.Id, -8000m), new Posting(_s.Investment.Id, 8000m));
          tx.FundDelta.Should().Be(8000m);
          tx.PlanningFundId.Should().Be(_s.FreedomFund.Id);
      }

      [Fact]
      public void Allocation_within_same_account_has_no_postings()
      {
          var tx = _s.Factory.FundAllocation(Jan11, _s.FreedomFund.Id, _s.Bank.Id, null, 8000m);

          tx.Postings.Should().BeEmpty();
          tx.FundDelta.Should().Be(8000m);
      }

      [Fact]
      public void Allocation_with_from_equal_to_is_treated_as_same_account()
      {
          var tx = _s.Factory.FundAllocation(Jan11, _s.FreedomFund.Id, _s.Bank.Id, _s.Bank.Id, 8000m);

          tx.Postings.Should().BeEmpty();
          tx.CounterAccountId.Should().BeNull();
      }

      [Fact]
      public void Withdrawal_reduces_account_and_fund()
      {
          var tx = _s.Factory.FundWithdrawal(Jan11, _s.FreedomFund.Id, _s.Investment.Id, 5492m);

          tx.Postings.Should().Equal(new Posting(_s.Investment.Id, -5492m));
          tx.FundDelta.Should().Be(-5492m);
      }

      [Fact]
      public void Withdrawal_accepts_optional_category() =>
          _s.Factory.FundWithdrawal(Jan11, _s.FreedomFund.Id, _s.Investment.Id, 5492m, _s.Food.Id)
              .CategoryId.Should().Be(_s.Food.Id);

      [Fact]
      public void Return_increases_account_and_fund()
      {
          var tx = _s.Factory.FundReturn(Jan11, _s.FreedomFund.Id, _s.Investment.Id, 254m);

          tx.Postings.Should().Equal(new Posting(_s.Investment.Id, 254m));
          tx.FundDelta.Should().Be(254m);
      }

      [Fact]
      public void Allocation_rejects_unknown_fund()
      {
          var act = () => _s.Factory.FundAllocation(Jan11, PlanningFundId.New(), _s.Bank.Id, null, 1m);
          act.Should().Throw<DomainException>();
      }
  }
  ```
- [ ] Step 2：`dotnet build tests/SixJars.Domain.Tests` → 確認失敗原因是 **CS1061 沒有 `FundAllocation` 等方法**
- [ ] Step 3：實作
  - `FundAllocation(date, fundId, toId, AccountId? fromId, amount, note, budgetMonth)`
    - 驗證：fund 存在；to 為 Cash/Bank/EWallet；from（若有）為 Cash/Bank。
    - from 等於 to 時視為 null。
    - `AccountId=to`、`Counter=from`。
    - 分錄：from 為 null 時為空，否則 `Move(from, to)`。
  - `FundWithdrawal(date, fundId, accountId, amount, CategoryId? categoryId = null, note, budgetMonth)`
    - 驗證：分類若有，必須是 Expense。
    - 分錄：`(account, −x)`。
  - `FundReturn(date, fundId, accountId, amount, note, budgetMonth)`
    - 分錄：`(account, +x)`。
  - 三者的 amount 都必須 >0；帳戶類型為 Cash/Bank/EWallet。
- [ ] Step 4：`dotnet test --project tests/SixJars.Domain.Tests -- --filter-class "SixJars.Domain.Tests.Transactions.TransactionFactoryFundTests"` → Expected：總計 7、失敗 0
- [ ] Step 5：`dotnet test` → Expected：總計 37、失敗 0
- [ ] Step 6：Commit
  ```bash
  git add src/SixJars.Domain/Transactions/TransactionFactory.cs tests/SixJars.Domain.Tests/Transactions/TransactionFactoryFundTests.cs
  git commit -m "feat(domain): 新增財務規劃帳戶的入新資金、出資金、資金回流" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

## Task 6：預定支出

**Files：** Create: `src/SixJars.Domain/Planning/PlannedExpense.cs`、`tests/SixJars.Domain.Tests/Planning/PlannedExpenseTests.cs`

預定支出的作用是「先佔額度、晚扣款」（Q14）。它只接受固定、貸款、特別支出三種性質，避免和預算重疊。

注意：private 建構子的參數名稱必須和屬性名稱一致（`budgetMonth`，不是 `month`），Task 14 的 EF 建構子綁定依賴這一點。

- [ ] Step 1：寫失敗測試
  ```csharp
  using FluentAssertions;
  using SixJars.Domain.Common;
  using SixJars.Domain.Planning;
  using Xunit;

  namespace SixJars.Domain.Tests.Planning;

  public class PlannedExpenseTests
  {
      private static readonly BudgetMonth March = new(2026, 3);
      private readonly SampleBook _s = new();

      [Fact]
      public void New_planned_expense_is_unpaid()
      {
          var planned = PlannedExpense.Create(_s.Book, March, _s.Phone.Id, _s.Bank.Id, -599m);

          planned.IsPaid.Should().BeFalse();
          planned.EstimatedAmount.Should().Be(-599m);
          planned.BookId.Should().Be(_s.Book.Id);
      }

      [Fact]
      public void Rejects_floating_category()
      {
          var act = () => PlannedExpense.Create(_s.Book, March, _s.Food.Id, null, -3000m);
          act.Should().Throw<DomainException>();
      }

      [Fact]
      public void MarkPaid_links_transaction()
      {
          var planned = PlannedExpense.Create(_s.Book, March, _s.Insurance.Id, _s.Card.Id, -1921m);
          var tx = _s.Factory.Expense(new DateOnly(2026, 3, 21), _s.Card.Id, _s.Insurance.Id, -1921m);

          planned.MarkPaid(tx);

          planned.IsPaid.Should().BeTrue();
          planned.PaidTransactionId.Should().Be(tx.Id);
      }

      [Fact]
      public void MarkPaid_twice_throws()
      {
          var planned = PlannedExpense.Create(_s.Book, March, _s.Insurance.Id, _s.Card.Id, -1921m);
          var tx = _s.Factory.Expense(new DateOnly(2026, 3, 21), _s.Card.Id, _s.Insurance.Id, -1921m);
          planned.MarkPaid(tx);

          var act = () => planned.MarkPaid(tx);
          act.Should().Throw<DomainException>();
      }
  }
  ```
- [ ] Step 2：`dotnet build tests/SixJars.Domain.Tests` → 確認失敗原因是 **CS0246 找不到 `PlannedExpense`**
- [ ] Step 3：實作 `PlannedExpense`（`namespace SixJars.Domain.Planning`）
  - private 建構子：`(BookId bookId, BudgetMonth budgetMonth, CategoryId categoryId, AccountId? accountId, decimal estimatedAmount, string? note)`，`Id = PlannedExpenseId.New()`。
  - `static Create(Book book, BudgetMonth budgetMonth, CategoryId categoryId, AccountId? accountId, decimal estimatedAmount, string? note = null)`
    - 分類必須是 Expense，而且 Nature 為 Fixed/Loan/Special；否則拋出 `DomainException("預定支出只限固定、貸款、特別支出：「…」")`。
    - 如果有 accountId，用 `book.GetAccount` 驗證帳戶存在。
  - 屬性：`PaidTransactionId`（`TransactionId?`，private set）、`IsPaid`。
  - `MarkPaid(Transaction transaction)`：已付時拋出例外；BookId 不同時也拋出例外。
- [ ] Step 4：`dotnet test --project tests/SixJars.Domain.Tests -- --filter-class "SixJars.Domain.Tests.Planning.PlannedExpenseTests"` → Expected：總計 4、失敗 0
- [ ] Step 5：`dotnet test` → Expected：總計 41、失敗 0
- [ ] Step 6：Commit
  ```bash
  git add src/SixJars.Domain/Planning tests/SixJars.Domain.Tests/Planning/PlannedExpenseTests.cs
  git commit -m "feat(domain): 新增預定支出（先佔額度、付款後連結交易）" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

## Task 7：帳戶與財務規劃帳戶餘額

**Files：** Create: `src/SixJars.Domain/Ledger/{LedgerSnapshot,BalanceCalculator}.cs`、`tests/SixJars.Domain.Tests/Ledger/BalanceCalculatorTests.cs`

兩種截止方式（spec §4.4）並存：依日期給資產負債表用，依歸屬月份給驗收和月報表用。最容易錯的就是把兩者混用。測試 4 的期望值 12,360.3 取自 Excel `1月!AA10`。

- [ ] Step 1：寫失敗測試
  ```csharp
  using FluentAssertions;
  using SixJars.Domain.Common;
  using SixJars.Domain.Ledger;
  using Xunit;

  namespace SixJars.Domain.Tests.Ledger;

  public class BalanceCalculatorTests
  {
      private static readonly BudgetMonth January = new(2026, 1);
      private readonly SampleBook _s = new();

      [Fact]
      public void Balance_is_opening_plus_postings_up_to_date()
      {
          var income = _s.Factory.Income(new DateOnly(2026, 1, 5), _s.Bank.Id, _s.Salary.Id, 1000m);
          var expense = _s.Factory.Expense(new DateOnly(2026, 1, 20), _s.Bank.Id, _s.Food.Id, -200m);
          var calculator = new BalanceCalculator(new LedgerSnapshot(_s.Book, [income, expense], []));

          calculator.AccountBalanceAsOf(_s.Bank.Id, new DateOnly(2026, 1, 10)).Should().Be(22610m);
          calculator.AccountBalanceAsOf(_s.Bank.Id, new DateOnly(2026, 1, 31)).Should().Be(22410m);
      }

      [Fact]
      public void Through_budget_month_uses_budget_month_not_date()
      {
          var februarySalary = _s.Factory.Income(new DateOnly(2026, 1, 30), _s.Bank.Id, _s.Salary.Id, 84223m, budgetMonth: new BudgetMonth(2026, 2));
          var calculator = new BalanceCalculator(new LedgerSnapshot(_s.Book, [februarySalary], []));

          calculator.AccountBalanceAsOf(_s.Bank.Id, new DateOnly(2026, 1, 31)).Should().Be(105833m);
          calculator.AccountBalanceThroughBudgetMonth(_s.Bank.Id, January).Should().Be(21610m);
      }

      [Fact]
      public void Card_balance_is_negative_outstanding()
      {
          var spend = _s.Factory.Expense(new DateOnly(2026, 1, 5), _s.Card.Id, _s.Food.Id, -27770m);
          var payment = _s.Factory.CardPayment(new DateOnly(2026, 1, 6), _s.Bank.Id, _s.Card.Id, 18197m);
          var calculator = new BalanceCalculator(new LedgerSnapshot(_s.Book, [spend, payment], []));

          calculator.AccountBalanceThroughBudgetMonth(_s.Card.Id, January).Should().Be(-32241m);
      }

      [Fact]
      public void Fund_balance_tracks_allocation_withdrawal_and_return()
      {
          var date = new DateOnly(2026, 1, 11);
          var allocation = _s.Factory.FundAllocation(date, _s.FreedomFund.Id, _s.Investment.Id, _s.Bank.Id, 8000m);
          var withdrawal = _s.Factory.FundWithdrawal(date, _s.FreedomFund.Id, _s.Investment.Id, 15371m);
          var fundReturn = _s.Factory.FundReturn(date, _s.FreedomFund.Id, _s.Investment.Id, 9857m);
          var calculator = new BalanceCalculator(new LedgerSnapshot(_s.Book, [allocation, withdrawal, fundReturn], []));

          calculator.FundBalanceThroughBudgetMonth(_s.FreedomFund.Id, January).Should().Be(12360.3m);
          calculator.FundBalanceAsOf(_s.FreedomFund.Id, new DateOnly(2026, 1, 10)).Should().Be(9874.3m);
      }
  }
  ```
- [ ] Step 2：`dotnet build tests/SixJars.Domain.Tests` → 確認失敗原因是 **CS0246 找不到 `BalanceCalculator` / `LedgerSnapshot`**
- [ ] Step 3：實作（`namespace SixJars.Domain.Ledger`）
  ```csharp
  /// <summary>計算用的唯讀帳本快照；P1 由記憶體集合提供，P2 由查詢提供。</summary>
  public sealed record LedgerSnapshot(Book Book, IReadOnlyList<Transaction> Transactions, IReadOnlyList<PlannedExpense> PlannedExpenses);

  public sealed class BalanceCalculator(LedgerSnapshot ledger)
  {
      public decimal AccountBalanceAsOf(AccountId accountId, DateOnly date) =>
          ledger.Book.GetAccount(accountId).OpeningBalance + SumPostings(accountId, t => t.Date <= date);

      public decimal AccountBalanceThroughBudgetMonth(AccountId accountId, BudgetMonth month) =>
          ledger.Book.GetAccount(accountId).OpeningBalance + SumPostings(accountId, t => t.BudgetMonth <= month);

      public decimal FundBalanceAsOf(PlanningFundId fundId, DateOnly date) =>
          ledger.Book.GetPlanningFund(fundId).OpeningBalance + SumFund(fundId, t => t.Date <= date);

      public decimal FundBalanceThroughBudgetMonth(PlanningFundId fundId, BudgetMonth month) =>
          ledger.Book.GetPlanningFund(fundId).OpeningBalance + SumFund(fundId, t => t.BudgetMonth <= month);

      private decimal SumPostings(AccountId accountId, Func<Transaction, bool> include) =>
          ledger.Transactions.Where(include).SelectMany(t => t.Postings).Where(p => p.AccountId == accountId).Sum(p => p.Amount);

      private decimal SumFund(PlanningFundId fundId, Func<Transaction, bool> include) =>
          ledger.Transactions.Where(t => t.PlanningFundId == fundId).Where(include).Sum(t => t.FundDelta);
  }
  ```
- [ ] Step 4：`dotnet test --project tests/SixJars.Domain.Tests -- --filter-class "SixJars.Domain.Tests.Ledger.BalanceCalculatorTests"` → Expected：總計 4、失敗 0
- [ ] Step 5：`dotnet test` → Expected：總計 45、失敗 0
- [ ] Step 6：Commit
  ```bash
  git add src/SixJars.Domain/Ledger tests/SixJars.Domain.Tests/Ledger/BalanceCalculatorTests.cs
  git commit -m "feat(domain): 帳戶與財務規劃帳戶餘額（依日期、依歸屬月份截止）" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

## Task 8：月可用餘額、年累計餘額、可用現金

**Files：** Create: `src/SixJars.Domain/Ledger/{DisposableBalanceCalculator,AvailableCashCalculator}.cs`、`tests/SixJars.Domain.Tests/Ledger/{DisposableBalanceCalculatorTests,AvailableCashCalculatorTests}.cs`

月可用餘額的影響表（spec §4.1）是本專案最核心的規則，每一列都要有測試。容易錯的地方有兩個：
- 電子錢包的消費要排除，但加值要扣（Q5）。
- 已付的預定支出不可以再扣一次預估金額。

- [ ] Step 1：寫失敗測試

  `DisposableBalanceCalculatorTests.cs`：
  ```csharp
  using FluentAssertions;
  using SixJars.Domain.Common;
  using SixJars.Domain.Ledger;
  using SixJars.Domain.Planning;
  using SixJars.Domain.Transactions;
  using Xunit;

  namespace SixJars.Domain.Tests.Ledger;

  public class DisposableBalanceCalculatorTests
  {
      private static readonly DateOnly Jan5 = new(2026, 1, 5);
      private static readonly BudgetMonth January = new(2026, 1);
      private readonly SampleBook _s = new();

      private decimal MonthlyOf(BudgetMonth month, IReadOnlyList<Transaction> transactions, IReadOnlyList<PlannedExpense>? planned = null) =>
          new DisposableBalanceCalculator(new LedgerSnapshot(_s.Book, transactions, planned ?? [])).Monthly(month);

      [Fact]
      public void Income_minus_expenses_on_any_non_wallet_account() =>
          MonthlyOf(January,
          [
              _s.Factory.Income(Jan5, _s.Bank.Id, _s.Salary.Id, 84290m),
              _s.Factory.Expense(Jan5, _s.Card.Id, _s.Food.Id, -129m),
              _s.Factory.Expense(Jan5, _s.Bank.Id, _s.Food.Id, -194m),
          ]).Should().Be(83967m);

      [Fact]
      public void Wallet_spending_is_excluded_but_top_up_is_deducted() =>
          MonthlyOf(January,
          [
              _s.Factory.TopUp(Jan5, _s.Wallet.Id, _s.Bank.Id, 500m),
              _s.Factory.Expense(Jan5, _s.Wallet.Id, _s.Food.Id, -100m),
          ]).Should().Be(-500m);

      [Fact]
      public void Fund_allocation_deducts_but_withdrawal_and_return_do_not() =>
          MonthlyOf(January,
          [
              _s.Factory.FundAllocation(Jan5, _s.FreedomFund.Id, _s.Investment.Id, _s.Bank.Id, 8000m),
              _s.Factory.FundWithdrawal(Jan5, _s.FreedomFund.Id, _s.Investment.Id, 5000m),
              _s.Factory.FundReturn(Jan5, _s.FreedomFund.Id, _s.Investment.Id, 254m),
          ]).Should().Be(-8000m);

      [Fact]
      public void Loan_payment_deducts_full_amount() =>
          MonthlyOf(January, [_s.Factory.LoanPayment(Jan5, _s.OtherBank.Id, _s.Loan.Id, 32503m, 28085m, _s.Mortgage.Id)])
              .Should().Be(-32503m);

      [Fact]
      public void Transfers_card_payments_and_disbursements_are_neutral() =>
          MonthlyOf(January,
          [
              _s.Factory.Transfer(Jan5, _s.Bank.Id, _s.OtherBank.Id, 1000m),
              _s.Factory.Withdrawal(Jan5, _s.Bank.Id, _s.Cash.Id, 3000m),
              _s.Factory.CashDeposit(Jan5, _s.Bank.Id, _s.Cash.Id, 2000m),
              _s.Factory.CardPayment(Jan5, _s.Bank.Id, _s.Card.Id, 18197m),
              _s.Factory.LoanDisbursement(Jan5, _s.Bank.Id, _s.Loan.Id, 500000m),
          ]).Should().Be(0m);

      [Fact]
      public void Unpaid_plan_occupies_estimate_and_paid_plan_uses_actual()
      {
          var unpaid = PlannedExpense.Create(_s.Book, January, _s.Phone.Id, _s.Bank.Id, -599m);
          var paid = PlannedExpense.Create(_s.Book, January, _s.Insurance.Id, _s.Card.Id, -1921m);
          var actual = _s.Factory.Expense(new DateOnly(2026, 1, 21), _s.Card.Id, _s.Insurance.Id, -1900m);
          paid.MarkPaid(actual);

          MonthlyOf(January, [actual], [unpaid, paid]).Should().Be(-2499m);
      }

      [Fact]
      public void Uses_budget_month_not_date()
      {
          var salary = _s.Factory.Income(new DateOnly(2026, 1, 30), _s.Bank.Id, _s.Salary.Id, 84223m, budgetMonth: new BudgetMonth(2026, 2));

          MonthlyOf(January, [salary]).Should().Be(0m);
          MonthlyOf(new BudgetMonth(2026, 2), [salary]).Should().Be(84223m);
      }

      [Fact]
      public void Year_to_date_sums_months_of_the_year()
      {
          var ledger = new LedgerSnapshot(_s.Book,
          [
              _s.Factory.Income(new DateOnly(2026, 1, 3), _s.Bank.Id, _s.Salary.Id, 100m),
              _s.Factory.Expense(new DateOnly(2026, 2, 3), _s.Bank.Id, _s.Food.Id, -30m),
              _s.Factory.Income(new DateOnly(2026, 3, 3), _s.Bank.Id, _s.Salary.Id, 5m),
          ], []);
          var calculator = new DisposableBalanceCalculator(ledger);

          calculator.YearToDate(new BudgetMonth(2026, 2)).Should().Be(70m);
          calculator.YearToDate(new BudgetMonth(2026, 3)).Should().Be(75m);
      }
  }
  ```
  `AvailableCashCalculatorTests.cs`：
  ```csharp
  using FluentAssertions;
  using SixJars.Domain.Common;
  using SixJars.Domain.Ledger;
  using Xunit;

  namespace SixJars.Domain.Tests.Ledger;

  public class AvailableCashCalculatorTests
  {
      private static readonly BudgetMonth January = new(2026, 1);
      private readonly SampleBook _s = new();

      [Fact]
      public void Counts_only_cash_accounts_flagged_as_available()
      {
          var withdrawal = _s.Factory.Withdrawal(new DateOnly(2026, 1, 6), _s.Bank.Id, _s.Cash.Id, 3000m);
          var calculator = new AvailableCashCalculator(new LedgerSnapshot(_s.Book, [withdrawal], []));

          calculator.ThroughBudgetMonth(January, includeEWallets: false).Should().Be(5071m);
      }

      [Fact]
      public void Optionally_includes_ewallet_balances()
      {
          var calculator = new AvailableCashCalculator(new LedgerSnapshot(_s.Book, [], []));

          calculator.ThroughBudgetMonth(January, includeEWallets: true).Should().Be(2071m + 152m);
          calculator.AsOf(new DateOnly(2026, 1, 1), includeEWallets: false).Should().Be(2071m);
      }
  }
  ```
- [ ] Step 2：`dotnet build tests/SixJars.Domain.Tests` → 確認失敗原因是 **CS0246 找不到兩個 Calculator**
- [ ] Step 3：實作
  ```csharp
  /// <summary>月可用餘額：純流量，依歸屬月份計算（spec §4.1）。</summary>
  public sealed class DisposableBalanceCalculator(LedgerSnapshot ledger)
  {
      public decimal Monthly(BudgetMonth month) =>
          ledger.Transactions.Where(t => t.BudgetMonth == month).Sum(Impact)
          + ledger.PlannedExpenses.Where(p => p.BudgetMonth == month && !p.IsPaid).Sum(p => p.EstimatedAmount);

      public decimal YearToDate(BudgetMonth month) =>
          Enumerable.Range(1, month.Month).Sum(m => Monthly(new BudgetMonth(month.Year, m)));

      private decimal Impact(Transaction transaction) => transaction.Kind switch
      {
          TransactionKind.Income => transaction.Amount,
          TransactionKind.Expense when ledger.Book.GetAccount(transaction.AccountId).Type == AccountType.EWallet => 0m,
          TransactionKind.Expense => transaction.Amount,
          TransactionKind.TopUp or TransactionKind.FundAllocation or TransactionKind.LoanPayment => -transaction.Amount,
          _ => 0m,
      };
  }

  /// <summary>可用現金：計入可用現金的現金帳戶餘額加總，可選擇加計電子錢包。</summary>
  public sealed class AvailableCashCalculator(LedgerSnapshot ledger)
  {
      private readonly BalanceCalculator _balances = new(ledger);

      public decimal AsOf(DateOnly date, bool includeEWallets) =>
          Included(includeEWallets).Sum(a => _balances.AccountBalanceAsOf(a.Id, date));

      public decimal ThroughBudgetMonth(BudgetMonth month, bool includeEWallets) =>
          Included(includeEWallets).Sum(a => _balances.AccountBalanceThroughBudgetMonth(a.Id, month));

      private IEnumerable<Account> Included(bool includeEWallets) =>
          ledger.Book.Accounts.Where(a => a.CountsAsAvailableCash || (includeEWallets && a.Type == AccountType.EWallet));
  }
  ```
- [ ] Step 4：
  - `dotnet test --project tests/SixJars.Domain.Tests -- --filter-class "SixJars.Domain.Tests.Ledger.DisposableBalanceCalculatorTests"` → Expected：總計 8
  - `dotnet test --project tests/SixJars.Domain.Tests -- --filter-class "SixJars.Domain.Tests.Ledger.AvailableCashCalculatorTests"` → Expected：總計 2
- [ ] Step 5：`dotnet test` → Expected：總計 55、失敗 0
- [ ] Step 6：Commit
  ```bash
  git add src/SixJars.Domain/Ledger/DisposableBalanceCalculator.cs src/SixJars.Domain/Ledger/AvailableCashCalculator.cs tests/SixJars.Domain.Tests/Ledger/DisposableBalanceCalculatorTests.cs tests/SixJars.Domain.Tests/Ledger/AvailableCashCalculatorTests.cs
  git commit -m "feat(domain): 月可用餘額、年累計餘額與可用現金" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

> **Checkpoint A1**：Domain 完成，全綠 55。可以在這裡收工。如果與計畫有偏差，用 `docs(plans):` commit 回寫原因。

> **A1 執行紀錄（2026-10-04）**：`c09a2ae`..`01a07a5`，共 9 個 commit；`dotnet test` 總計 55、失敗 0；`dotnet build` 0 warning。
> - **與計畫的偏差**：沒有 API 或簽章偏差，下游 Task 照原計畫引用即可。
> - **Step 2 的錯誤碼**：Task 1、2、6、7 的 Step 2 出現的是 **CS0234**（命名空間不存在），不是計畫寫的 CS0246。原因是第一次引用新的命名空間；兩者都表示「型別還沒寫」，所以失敗原因正確。
> - **Task 0 的 commit**：spec 與計畫在執行前已經進版控，所以這個 commit 只帶入計畫的 FluentAssertions 8.11 修改。
> - **變異測試**：12 個變異全部被測試抓到。涵蓋的規則有：
>   - 錢包消費排除
>   - 已付預定支出不重扣
>   - 貸款繳款扣總額
>   - 年累計範圍
>   - 出資金符號
>   - 歸屬月份截止
>   - 可用現金只計現金帳戶
>   - 本金上限
>   - 同帳戶圈存
>   - 現金存入方向
>   - 繳卡費付款帳戶類型
>   - 錢包是否計入

---

## Task 9：舊 Excel 讀取器

**Files：** Create: `src/SixJars.Application/LegacyImport/{LegacyWorkbook,ILegacyWorkbookReader}.cs`、`src/SixJars.Infrastructure/LegacyExcel/{SheetGrid,ExcelLegacyWorkbookReader}.cs`、`tests/Shared/RepoPaths.cs`、`tests/SixJars.Infrastructure.Tests/SixJars.Infrastructure.Tests.csproj`、`tests/SixJars.Infrastructure.Tests/LegacyExcel/ExcelLegacyWorkbookReaderTests.cs`

讀取器只做「儲存格 → DTO」，不做任何語意判斷，語意全部交給 Task 10–12。容易錯的地方有四個：
- ExcelDataReader 的列、欄是 0-based，而且會補齊空白列（spike 已驗證 `J6`、`AH7` 對位正確）。
- 數字一律是 `double`，必須轉成 `decimal` 並四捨五入到 4 位，才能消除 `9874.300000000003` 這類浮點雜訊。
- 讀檔時要用 `FileShare.ReadWrite`，你在 Excel 開著這個檔案時才讀得到。
- 這組測試讀的是真實檔案，缺檔時會**略過**。

- [ ] Step 1：DTO、共用路徑與失敗測試

  `LegacyWorkbook.cs`（`namespace SixJars.Application.LegacyImport`）：
  ```csharp
  public sealed record LegacyWorkbook(LegacySettings Settings, IReadOnlyList<LegacyCategoryList> FloatingCategories, IReadOnlyList<LegacyMonthSheet> Months);

  /// <summary>「設定」與「清單」工作表。信用卡與貸款金額為 Excel 原值（尚待繳清、剩餘本金，正數）。</summary>
  public sealed record LegacySettings(
      int Year,
      decimal HandCashOpening,
      IReadOnlyList<LegacyNamedAmount> Banks,
      IReadOnlyList<LegacyNamedAmount> CreditCards,
      IReadOnlyList<LegacyNamedAmount> EWallets,
      IReadOnlyList<LegacyNamedAmount> Loans,
      IReadOnlyList<LegacyNamedAmount> PlanningFunds,
      IReadOnlyList<string> IncomeItems,
      IReadOnlyList<string> OtherIncomeSubItems,
      IReadOnlyList<string> FixedExpenseItems);

  public sealed record LegacyNamedAmount(string Name, decimal Amount);
  public sealed record LegacyCategoryList(string Main, IReadOnlyList<string> Subs);

  /// <summary>一張月工作表。LoanPrincipals 為「本月還本金」原值（Excel 為負數）。</summary>
  public sealed record LegacyMonthSheet(
      int Month,
      IReadOnlyList<LegacyJournalRow> Journal,
      IReadOnlyList<LegacyTemplateRow> Templates,
      IReadOnlyList<LegacyNamedAmount> LoanPrincipals,
      LegacyMonthFigures Figures);

  public sealed record LegacyJournalRow(int Row, DateOnly? Date, string? Method, string? Main, string? Sub, decimal Amount, string? Note);

  public enum LegacyTemplateSection { Fixed, Loan, Special }

  public sealed record LegacyTemplateRow(int Row, LegacyTemplateSection Section, string Item, string? Method, decimal Amount, DateOnly? PaidDate, string? Note);

  /// <summary>Excel 算出的數字，只供驗收比對，不參與匯入。</summary>
  public sealed record LegacyMonthFigures(
      decimal MonthlyDisposable,
      decimal WalletAddBack,
      decimal AvailableCash,
      IReadOnlyList<LegacyNamedAmount> BankBalances,
      IReadOnlyList<LegacyNamedAmount> EWalletBalances,
      IReadOnlyList<LegacyNamedAmount> CardOutstanding,
      IReadOnlyList<LegacyNamedAmount> LoanRemaining,
      IReadOnlyList<LegacyNamedAmount> FundBalances);
  ```
  `ILegacyWorkbookReader.cs`：
  ```csharp
  public interface ILegacyWorkbookReader
  {
      Task<LegacyWorkbook> ReadAsync(Stream stream, CancellationToken cancellationToken);
  }
  ```
  `tests/Shared/RepoPaths.cs`：
  ```csharp
  namespace SixJars.Tests.Shared;

  internal static class RepoPaths
  {
      public static string Root { get; } = FindRoot();

      /// <summary>可用環境變數 SIXJARS_LEGACY_WORKBOOK 覆寫。</summary>
      public static string LegacyWorkbook =>
          Environment.GetEnvironmentVariable("SIXJARS_LEGACY_WORKBOOK") ?? Path.Combine(Root, "reference", "2026帳本v1.xlsm");

      public static FileStream OpenLegacyWorkbook() =>
          new(LegacyWorkbook, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

      private static string FindRoot()
      {
          for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
          {
              if (File.Exists(Path.Combine(dir.FullName, "SixJars.slnx")))
              {
                  return dir.FullName;
              }
          }

          throw new InvalidOperationException("找不到 SixJars.slnx，無法定位 repo 根目錄");
      }
  }
  ```
  `tests/SixJars.Infrastructure.Tests/SixJars.Infrastructure.Tests.csproj`：和 Domain.Tests 相同的套件，另外有：
  - ProjectReference → `src/SixJars.Infrastructure`
  - `<Compile Include="..\Shared\**\*.cs" LinkBase="Shared" />`

  建好後執行 `dotnet sln SixJars.slnx add tests/SixJars.Infrastructure.Tests`。

  `LegacyExcel/ExcelLegacyWorkbookReaderTests.cs`：
  ```csharp
  using FluentAssertions;
  using SixJars.Application.LegacyImport;
  using SixJars.Infrastructure.LegacyExcel;
  using SixJars.Tests.Shared;
  using Xunit;

  namespace SixJars.Infrastructure.Tests.LegacyExcel;

  public class ExcelLegacyWorkbookReaderTests
  {
      private static async Task<LegacyWorkbook> ReadAsync()
      {
          Assert.SkipUnless(File.Exists(RepoPaths.LegacyWorkbook), $"找不到 {RepoPaths.LegacyWorkbook}，略過");
          await using var stream = RepoPaths.OpenLegacyWorkbook();
          return await new ExcelLegacyWorkbookReader().ReadAsync(stream, TestContext.Current.CancellationToken);
      }

      [Fact]
      public async Task Reads_settings_and_openings()
      {
          var settings = (await ReadAsync()).Settings;

          settings.Year.Should().Be(2026);
          settings.HandCashOpening.Should().Be(2071m);
          settings.Banks.Should().Contain(new LegacyNamedAmount("國泰世華銀行", 21610m));
          settings.CreditCards.Should().Contain(new LegacyNamedAmount("國泰Combo卡", 22668m));
          settings.PlanningFunds.Should().Contain(new LegacyNamedAmount("財務自由帳戶", 9874.3m));
          settings.Loans.Should().Contain(new LegacyNamedAmount("房屋貸款", 2658876m));
          settings.FixedExpenseItems.Should().Contain("行動電話費");
      }

      [Fact]
      public async Task Reads_january_journal()
      {
          var journal = (await ReadAsync()).Months.Single(m => m.Month == 1).Journal;

          journal.Should().HaveCount(152);
          journal[0].Should().Be(new LegacyJournalRow(7, new DateOnly(2025, 12, 31), "國泰世華銀行", "工作薪資1", null, 84223m, null));
      }

      [Fact]
      public async Task Reads_templates_and_loan_principal()
      {
          var january = (await ReadAsync()).Months.Single(m => m.Month == 1);

          january.Templates.Should().ContainEquivalentOf(new LegacyTemplateRow(52, LegacyTemplateSection.Fixed, "行動電話費", "國泰世華銀行", -599m, new DateOnly(2026, 1, 16), null));
          january.Templates.Should().Contain(t => t.Section == LegacyTemplateSection.Loan && t.Item == "房屋貸款" && t.Amount == -32503m);
          january.LoanPrincipals.Should().Contain(new LegacyNamedAmount("房屋貸款", -28085m));
      }

      [Fact]
      public async Task Reads_month_figures()
      {
          var figures = (await ReadAsync()).Months.Single(m => m.Month == 1).Figures;

          figures.MonthlyDisposable.Should().Be(-4557m);
          figures.WalletAddBack.Should().Be(562m);
          figures.AvailableCash.Should().Be(1700m);
          figures.BankBalances.Should().Contain(new LegacyNamedAmount("國泰世華銀行", 10773m));
          figures.FundBalances.Should().Contain(new LegacyNamedAmount("財務自由帳戶", 12360.3m));
      }

      [Fact]
      public async Task Reads_floating_categories()
      {
          var floating = (await ReadAsync()).FloatingCategories;

          floating.Single(c => c.Main == "主食").Subs.Should().Equal("早餐", "中餐", "晚餐", "宵夜");
      }
  }
  ```
- [ ] Step 2：`dotnet build tests/SixJars.Infrastructure.Tests` → 確認失敗原因是 **CS0246 找不到 `ExcelLegacyWorkbookReader`**（DTO 已經存在）
- [ ] Step 3：實作
  - `SheetGrid`（internal）：
    - `static IReadOnlyDictionary<string, SheetGrid> LoadAll(Stream)`：用 `ExcelReaderFactory.CreateReader`，對每個工作表把 `reader.GetValues(object[])` 收集成列，並用 `reader.Name` 當 key。
    - `Value(address)`：`DBNull` 視為 null。
    - `Text`：空白回傳 null，並 Trim。
    - `Number`：`double` 用 `Math.Round(Convert.ToDecimal(d), 4)`，其他型別回傳 0。
    - `HasNumber`：值為 double/int/decimal 時為 true。
    - `Date`：`DateTime` 轉成 `DateOnly`。
    - `Texts(column, first, last)`：排除 null 和以 `---` 開頭的分隔線。
    - `NamedAmounts(nameColumn, amountColumn, first, last)`：名稱為空的列跳過。
    - `static Columns(from, to)`；A1 位址解析（欄字母 → 0-based）。
  - `ExcelLegacyWorkbookReader`：
    - static 建構子中呼叫 `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`。
    - `ReadAsync` 先用 `CopyToAsync` 把串流複製到 `MemoryStream`，再解析。
    - 儲存格位址：

    | 欄位 | 來源 |
    |---|---|
    | `Year` | `設定!J4` |
    | `HandCashOpening` | `設定!E54` |
    | `Banks` | `設定!B70:B79` / `D` |
    | `CreditCards` | `設定!G70:G79` / `I` |
    | `EWallets` | `設定!G59:G66` / `I` |
    | `Loans` | `設定!B83:B90` / `D` |
    | `PlanningFunds` | `設定!B59:B66` / `D` |
    | `IncomeItems` | `設定!B11:B18` |
    | `FixedExpenseItems` | `設定!B22:B41` |
    | `OtherIncomeSubItems` | `清單!AU4:AU120` |
    | `FloatingCategories` | `清單` 的 `AV`..`BT` 欄：第 2 列為主選單，第 4–120 列為副選單 |
    | 流水帳 | `AH..AM` 第 7–172 列，只取 `AL` 為數字的列 |
    | 制式表格 | 固定 50–69、貸款 74–81（備註 `AB`）、特別 86–91（備註 `X`）；欄 `E` 項目、`J` 方式、`N` 金額、`T` 支出日；只取 `E` 有值且 `N`≠0 的列 |
    | `LoanPrincipals` | `E124:E131` / `N` |
    | `Figures` | `J6`、`N5`、`E6`；銀行 `E96:E105`/`X`；錢包 `E22:E29`/`M`；信用卡 `E110:E119`/`X`；貸款 `E124:E131`/`X`；財務規劃 `S10:S17`/`AA` |

- [ ] Step 4：`dotnet test --project tests/SixJars.Infrastructure.Tests -- --filter-class "SixJars.Infrastructure.Tests.LegacyExcel.ExcelLegacyWorkbookReaderTests"` → Expected：總計 5、失敗 0（缺檔時為已略過 5）
- [ ] Step 5：`dotnet test` → Expected：總計 60、失敗 0
- [ ] Step 6：Commit
  ```bash
  git add SixJars.slnx src/SixJars.Application/LegacyImport/LegacyWorkbook.cs src/SixJars.Application/LegacyImport/ILegacyWorkbookReader.cs src/SixJars.Infrastructure/LegacyExcel tests/Shared/RepoPaths.cs tests/SixJars.Infrastructure.Tests/SixJars.Infrastructure.Tests.csproj tests/SixJars.Infrastructure.Tests/LegacyExcel/ExcelLegacyWorkbookReaderTests.cs
  git commit -m "feat(infra): 讀取舊記帳本 xlsm 為原始資料列" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

## Task 10：匯入轉換 I —— 日期修正、帳戶、分類、期初

**Files：** Create: `src/SixJars.Application/LegacyImport/{LegacyDateNormalizer,LegacyNames,ImportReport,LegacyWorkbookMapper,MappingSession}.cs`、`tests/SixJars.Application.Tests/SixJars.Application.Tests.csproj`、`tests/SixJars.Application.Tests/LegacyImport/{LegacyWorkbookFactory,LegacyDateNormalizerTests,LegacyWorkbookMapperSettingsTests}.cs`

這個 Task 把 Excel 的隱含規則寫成明確的程式碼。容易錯的地方有三個：
- 信用卡和貸款的期初要變號。
- 外幣現鈔帳戶的期初，取自「外幣現金」財務規劃帳戶的期初（Q10、Q22）。
- 期初日必須用**修正後**的最早日期來計算。

`MappingSession` 是 partial class，Task 11 和 12 各加一個檔案。

這裡刻意對每一列各自做一次 try-catch：批次匯入需要收集**全部**錯誤再一起回報，這是例外處理集中化原則的合理例外。

- [ ] Step 1：測試專案與失敗測試

  `SixJars.Application.Tests.csproj`：套件同 Domain.Tests，ProjectReference → `src/SixJars.Application`。建好後執行 `dotnet sln SixJars.slnx add tests/SixJars.Application.Tests`。

  `LegacyImport/LegacyWorkbookFactory.cs`：
  ```csharp
  using SixJars.Application.LegacyImport;

  namespace SixJars.Application.Tests.LegacyImport;

  /// <summary>合成的舊記帳本；結構與真實檔案相同，但只保留測試需要的項目。</summary>
  internal static class LegacyWorkbookFactory
  {
      private static readonly LegacyMonthFigures NoFigures = new(0m, 0m, 0m, [], [], [], [], []);

      public static LegacySettings Settings(
          IReadOnlyList<LegacyNamedAmount>? loans = null,
          IReadOnlyList<LegacyNamedAmount>? funds = null) => new(
          Year: 2026,
          HandCashOpening: 2071m,
          Banks: [new("國泰世華銀行", 21610m), new("華南銀行", 11637m), new("國泰投資帳戶", 20m), new("國泰外幣帳戶", 16365m)],
          CreditCards: [new("國泰Combo卡", 22668m)],
          EWallets: [new("悠遊卡", 152m)],
          Loans: loans ?? [new("房屋貸款", 2658876m), new("貸款備用01", 0m)],
          PlanningFunds: funds ?? [new("財務自由帳戶", 9874.3m), new("外幣現金", 10000m)],
          IncomeItems: ["工作薪資1", "其它收入"],
          OtherIncomeSubItems: ["中獎"],
          FixedExpenseItems: ["行動電話費", "保險費", "固定支出10"]);

      public static IReadOnlyList<LegacyCategoryList> Floating() =>
          [new("主食", ["早餐", "中餐", "晚餐", "宵夜"]), new("金融交易", ["手續費"]), new("自定浮支09", [])];

      public static LegacyMonthSheet Month(
          int month,
          IReadOnlyList<LegacyJournalRow>? journal = null,
          IReadOnlyList<LegacyTemplateRow>? templates = null,
          IReadOnlyList<LegacyNamedAmount>? loanPrincipals = null) =>
          new(month, journal ?? [], templates ?? [], loanPrincipals ?? [], NoFigures);

      public static LegacyWorkbook Workbook(params LegacyMonthSheet[] months) => new(Settings(), Floating(), months);

      public static LegacyWorkbook Workbook(LegacySettings settings, params LegacyMonthSheet[] months) => new(settings, Floating(), months);

      public static LegacyJournalRow Row(int row, DateOnly date, string method, string main, string? sub, decimal amount) =>
          new(row, date, method, main, sub, amount, null);
  }
  ```
  `LegacyDateNormalizerTests.cs`：
  ```csharp
  using FluentAssertions;
  using SixJars.Application.LegacyImport;
  using SixJars.Domain.Common;
  using Xunit;

  namespace SixJars.Application.Tests.LegacyImport;

  public class LegacyDateNormalizerTests
  {
      [Fact]
      public void Mistyped_year_is_corrected_to_nearest_sheet_month() =>
          LegacyDateNormalizer.Normalize(new DateOnly(2026, 12, 31), new BudgetMonth(2026, 1))
              .Should().Be((new DateOnly(2025, 12, 31), true));

      [Fact]
      public void Previous_month_end_salary_is_kept() =>
          LegacyDateNormalizer.Normalize(new DateOnly(2025, 12, 31), new BudgetMonth(2026, 1))
              .Should().Be((new DateOnly(2025, 12, 31), false));

      [Fact]
      public void Date_within_sheet_month_is_kept() =>
          LegacyDateNormalizer.Normalize(new DateOnly(2026, 1, 30), new BudgetMonth(2026, 2))
              .Should().Be((new DateOnly(2026, 1, 30), false));
  }
  ```
  `LegacyWorkbookMapperSettingsTests.cs`：
  ```csharp
  using FluentAssertions;
  using SixJars.Application.LegacyImport;
  using SixJars.Domain.Books;
  using Xunit;
  using static SixJars.Application.Tests.LegacyImport.LegacyWorkbookFactory;

  namespace SixJars.Application.Tests.LegacyImport;

  public class LegacyWorkbookMapperSettingsTests
  {
      [Fact]
      public void Creates_hand_cash_counted_and_foreign_cash_not_counted()
      {
          var book = LegacyWorkbookMapper.Map(Workbook()).Book;

          var cash = book.FindAccount("現金")!;
          cash.OpeningBalance.Should().Be(2071m);
          cash.CountsAsAvailableCash.Should().BeTrue();
          var foreignCash = book.FindAccount("外幣現鈔")!;
          foreignCash.OpeningBalance.Should().Be(10000m);
          foreignCash.CountsAsAvailableCash.Should().BeFalse();
      }

      [Fact]
      public void Card_and_loan_openings_are_negated()
      {
          var book = LegacyWorkbookMapper.Map(Workbook()).Book;

          book.FindAccount("國泰Combo卡")!.OpeningBalance.Should().Be(-22668m);
          book.FindAccount("房屋貸款")!.OpeningBalance.Should().Be(-2658876m);
          book.FindPlanningFund("財務自由帳戶")!.OpeningBalance.Should().Be(9874.3m);
      }

      [Fact]
      public void Skips_placeholder_names()
      {
          var book = LegacyWorkbookMapper.Map(Workbook()).Book;

          book.FindAccount("貸款備用01").Should().BeNull();
          book.FindCategory("自定浮支09").Should().BeNull();
          book.FindCategory("固定支出", "固定支出10").Should().BeNull();
      }

      [Fact]
      public void Builds_category_tree_with_natures()
      {
          var book = LegacyWorkbookMapper.Map(Workbook()).Book;

          book.FindCategory("主食", "中餐")!.Nature.Should().Be(ExpenseNature.Floating);
          book.FindCategory("固定支出", "行動電話費")!.Nature.Should().Be(ExpenseNature.Fixed);
          book.FindCategory("貸款支出", "房屋貸款")!.Nature.Should().Be(ExpenseNature.Loan);
          book.FindCategory("特別支出")!.Nature.Should().Be(ExpenseNature.Special);
          book.FindCategory("其它收入", "中獎")!.Kind.Should().Be(CategoryKind.Income);
      }

      [Fact]
      public void Opening_date_is_day_before_earliest_corrected_date()
      {
          var january = Month(1, journal:
          [
              Row(8, new DateOnly(2026, 12, 31), "現金", "主食", "晚餐", -180m),
              Row(10, new DateOnly(2026, 1, 2), "現金", "主食", "中餐", -80m),
          ]);

          LegacyWorkbookMapper.Map(Workbook(january)).Book.OpeningDate.Should().Be(new DateOnly(2025, 12, 30));
      }
  }
  ```
- [ ] Step 2：`dotnet build tests/SixJars.Application.Tests` → 確認失敗原因是 **CS0246 找不到 `LegacyDateNormalizer` / `LegacyWorkbookMapper`**
- [ ] Step 3：實作
  - `LegacyDateNormalizer.Normalize(DateOnly date, BudgetMonth sheetMonth) → (DateOnly Date, bool Corrected)`
    - 如果 |日期月份 − 工作表月份| ≤ 6 個月，原樣回傳。
    - 否則在 `sheetMonth.Year ± 1` 這三個年份中，選出月份差最小的那一年；日期超過該月天數時，取月底。
  - `LegacyNames`（internal static partial）：
    - 常數（現金、非手上現金、外幣現鈔、外幣現金、轉帳、提款、現金存入、加值、繳信用卡款、新增貸款、貸款支出、手續費、固定支出、特別支出、入新資金、出資金、資金回流、金融交易、其它收入）。
    - `[GeneratedRegex(@"^(自定浮支|固定支出|貸款備用)\d+$")]`，加上 `IsPlaceholder(string)`。
  - `ImportReport`：
    - `Errors` / `Warnings` / `Corrections` 三個 `IReadOnlyList<ImportIssue>`，以及 internal 的新增方法。
    - `public sealed record ImportIssue(string Sheet, int Row, string Message);`
  - `public sealed record LegacyImportResult(Book Book, IReadOnlyList<Transaction> Transactions, IReadOnlyList<PlannedExpense> PlannedExpenses, ImportReport Report);`
  - `public static class LegacyWorkbookMapper { public static LegacyImportResult Map(LegacyWorkbook workbook) => new MappingSession(workbook).Run(); }`
  - `MappingSession`（`internal sealed partial class MappingSession(LegacyWorkbook workbook)`）：
    - `Run()`：
      1. `_book = new Book("我的帳本", OpeningDate())`
      2. `_factory = new TransactionFactory(_book)`
      3. `AddAccounts()`、`AddPlanningFunds()`、`AddCategories()`
      4. 回傳結果。Task 11 和 12 會在 return 之前插入逐月迴圈。
    - `OpeningDate()`：所有流水帳日期與制式表格支出日經過 `Normalize` 之後取最小值，再減 1 天；如果完全沒有日期，用 `{Year}-01-01` 減 1 天。
    - `AddAccounts()`：
      - 現金（Cash，計入，`HandCashOpening`）。
      - 外幣現鈔（Cash，不計入，期初 = 名為「外幣現金」的財務規劃帳戶期初，沒有則為 0）。
      - 銀行 ×1、信用卡 ×−1、電子錢包 ×1、貸款 ×−1；佔位名稱跳過。
    - `AddCategories()`：
      - 收入項目 → 收入主分類；「其它收入」底下掛上 `OtherIncomeSubItems`。
      - 浮動清單（略過佔位名稱）→ Floating 主分類與子分類。
      - 「固定支出」（Fixed）底下掛上非佔位的固定支出項目。
      - 「貸款支出」（Loan）底下掛上非佔位的貸款名稱。
      - 「特別支出」（Special），子分類之後隨用隨建。
      - 子分類一律先 `Distinct()`。
    - 共用輔助：`MonthOf(sheet)`、`SheetName(sheet) => $"{sheet.Month}月"`、`private sealed class RowRejected(string message) : Exception(message);`
- [ ] Step 4：
  - `dotnet test --project tests/SixJars.Application.Tests -- --filter-class "SixJars.Application.Tests.LegacyImport.LegacyDateNormalizerTests"` → Expected：總計 3
  - `dotnet test --project tests/SixJars.Application.Tests -- --filter-class "SixJars.Application.Tests.LegacyImport.LegacyWorkbookMapperSettingsTests"` → Expected：總計 5
- [ ] Step 5：`dotnet test` → Expected：總計 68、失敗 0
- [ ] Step 6：Commit
  ```bash
  git add SixJars.slnx src/SixJars.Application/LegacyImport/LegacyDateNormalizer.cs src/SixJars.Application/LegacyImport/LegacyNames.cs src/SixJars.Application/LegacyImport/ImportReport.cs src/SixJars.Application/LegacyImport/LegacyWorkbookMapper.cs src/SixJars.Application/LegacyImport/MappingSession.cs tests/SixJars.Application.Tests/SixJars.Application.Tests.csproj tests/SixJars.Application.Tests/LegacyImport/LegacyWorkbookFactory.cs tests/SixJars.Application.Tests/LegacyImport/LegacyDateNormalizerTests.cs tests/SixJars.Application.Tests/LegacyImport/LegacyWorkbookMapperSettingsTests.cs
  git commit -m "feat(import): 匯入帳戶、財務規劃帳戶、分類與期初，並修正年份打錯的日期" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

## Task 11：匯入轉換 II —— 流水帳列 → 交易

**Files：** Create: `src/SixJars.Application/LegacyImport/MappingSession.Journal.cs`、`tests/SixJars.Application.Tests/LegacyImport/LegacyWorkbookMapperJournalTests.cs`；Modify: `MappingSession.cs`（`Run()` 加上逐月呼叫 `MapJournal`）

依 spec §5.2 的規則表逐條實作。容易錯的地方有三個：
- 轉帳的正負號決定方向。
- 財務規劃列要靠「副選單」區分三種形態。
- 「非手上現金」要對應到外幣現鈔帳戶。

所有列的歸屬月份一律是工作表月份。

- [ ] Step 1：寫失敗測試
  ```csharp
  using FluentAssertions;
  using SixJars.Application.LegacyImport;
  using SixJars.Domain.Common;
  using SixJars.Domain.Transactions;
  using Xunit;
  using static SixJars.Application.Tests.LegacyImport.LegacyWorkbookFactory;

  namespace SixJars.Application.Tests.LegacyImport;

  public class LegacyWorkbookMapperJournalTests
  {
      private static readonly DateOnly Jan6 = new(2026, 1, 6);

      private static LegacyImportResult MapJanuary(params LegacyJournalRow[] rows) =>
          LegacyWorkbookMapper.Map(Workbook(Month(1, journal: rows)));

      [Fact]
      public void Maps_income_with_sheet_month_as_budget_month()
      {
          var result = MapJanuary(Row(7, new DateOnly(2025, 12, 31), "國泰世華銀行", "工作薪資1", null, 84223m));

          var tx = result.Transactions.Single();
          tx.Kind.Should().Be(TransactionKind.Income);
          tx.Date.Should().Be(new DateOnly(2025, 12, 31));
          tx.BudgetMonth.Should().Be(new BudgetMonth(2026, 1));
          tx.Postings.Should().Equal(new Posting(result.Book.FindAccount("國泰世華銀行")!.Id, 84223m));
      }

      [Fact]
      public void Corrects_mistyped_year_and_reports_it()
      {
          var result = MapJanuary(Row(8, new DateOnly(2026, 12, 31), "現金", "主食", "晚餐", -180m));

          result.Transactions.Single().Date.Should().Be(new DateOnly(2025, 12, 31));
          result.Report.Corrections.Should().ContainSingle(i => i.Row == 8 && i.Sheet == "1月");
      }

      [Fact]
      public void Negative_transfer_goes_from_method_to_sub()
      {
          var result = MapJanuary(Row(35, Jan6, "國泰世華銀行", "轉帳", "華南銀行", -34000m));

          var tx = result.Transactions.Single();
          tx.Kind.Should().Be(TransactionKind.Transfer);
          tx.AccountId.Should().Be(result.Book.FindAccount("國泰世華銀行")!.Id);
          tx.CounterAccountId.Should().Be(result.Book.FindAccount("華南銀行")!.Id);
          tx.Amount.Should().Be(34000m);
      }

      [Fact]
      public void Positive_transfer_goes_from_sub_to_method()
      {
          var result = MapJanuary(Row(31, Jan6, "國泰投資帳戶", "轉帳", "國泰世華銀行", 9000m));

          var tx = result.Transactions.Single();
          tx.AccountId.Should().Be(result.Book.FindAccount("國泰世華銀行")!.Id);
          tx.CounterAccountId.Should().Be(result.Book.FindAccount("國泰投資帳戶")!.Id);
      }

      [Fact]
      public void Maps_withdrawal_card_payment_and_top_up()
      {
          var result = MapJanuary(
              Row(40, Jan6, "國泰世華銀行", "提款", null, -3000m),
              Row(36, Jan6, "國泰世華銀行", "繳信用卡款", "國泰Combo卡", -18197m),
              Row(59, Jan6, "悠遊卡", "加值", "國泰世華銀行", 500m));

          result.Report.Errors.Should().BeEmpty();
          result.Transactions.Select(t => (t.Kind, t.Amount)).Should().Equal(
              (TransactionKind.Withdrawal, 3000m), (TransactionKind.CardPayment, 18197m), (TransactionKind.TopUp, 500m));
      }

      [Fact]
      public void Maps_three_planning_fund_patterns()
      {
          var result = MapJanuary(
              Row(63, Jan6, "國泰投資帳戶", "財務自由帳戶", "國泰世華銀行", 8000m),
              Row(71, Jan6, "國泰投資帳戶", "財務自由帳戶", "出資金", -5492m),
              Row(33, Jan6, "國泰投資帳戶", "財務自由帳戶", "資金回流", 254m));

          result.Report.Errors.Should().BeEmpty();
          result.Transactions.Select(t => (t.Kind, t.FundDelta)).Should().Equal(
              (TransactionKind.FundAllocation, 8000m), (TransactionKind.FundWithdrawal, -5492m), (TransactionKind.FundReturn, 254m));
          result.Transactions[0].CounterAccountId.Should().Be(result.Book.FindAccount("國泰世華銀行")!.Id);
      }

      [Fact]
      public void Non_hand_cash_maps_to_foreign_cash_account()
      {
          var result = MapJanuary(Row(139, Jan6, "非手上現金", "外幣現金", "國泰外幣帳戶", 10000m));

          result.Transactions.Single().AccountId.Should().Be(result.Book.FindAccount("外幣現鈔")!.Id);
      }

      [Fact]
      public void Bank_fee_maps_to_financial_category()
      {
          var result = MapJanuary(Row(20, Jan6, "國泰世華銀行", "手續費", null, -15m));

          result.Transactions.Single().CategoryId.Should().Be(result.Book.FindCategory("金融交易", "手續費")!.Id);
      }

      [Fact]
      public void Unknown_sub_category_is_created_with_warning()
      {
          var result = MapJanuary(Row(18, Jan6, "現金", "主食", "點心", -50m));

          result.Book.FindCategory("主食", "點心").Should().NotBeNull();
          result.Report.Warnings.Should().ContainSingle(i => i.Row == 18);
      }

      [Fact]
      public void Unknown_method_is_an_error_and_row_is_skipped()
      {
          var result = MapJanuary(Row(14, Jan6, "不存在的銀行", "主食", "中餐", -80m));

          result.Transactions.Should().BeEmpty();
          result.Report.Errors.Should().ContainSingle(i => i.Row == 14);
      }

      [Fact]
      public void Cash_into_planning_fund_is_rejected()
      {
          var result = MapJanuary(Row(90, Jan6, "現金", "財務自由帳戶", "入新資金", 1000m));

          result.Transactions.Should().BeEmpty();
          result.Report.Errors.Should().ContainSingle(i => i.Row == 90);
      }
  }
  ```
- [ ] Step 2：`dotnet test --project tests/SixJars.Application.Tests -- --filter-class "SixJars.Application.Tests.LegacyImport.LegacyWorkbookMapperJournalTests"` → 確認 **11 個都失敗**，而且失敗原因是 `Transactions` 為空（例如 `Sequence contains no elements`、`Expected ... to contain a single item`），不是編譯錯誤。
- [ ] Step 3：實作 `MappingSession.Journal.cs`
  - `MapJournal(LegacyMonthSheet month)`：逐列呼叫 `MapJournalRow`，用 `catch (Exception ex) when (ex is DomainException or RowRejected)` 接住例外後，記入 `Errors`。
  - `MapJournalRow`：
    1. 缺少日期 → 錯誤。
    2. `NormalizeDate`：有修正時記入 `Corrections`，訊息需包含修正前與修正後的日期。
    3. `ResolveAccount(method)`：「現金」→ 現金帳戶；「非手上現金」→ 外幣現鈔；其他依名稱查找。找不到 → 錯誤「未知的方式」。
    4. 依主選單分派交易類型：

    | 主選單 | 處理 |
    |---|---|
    | 轉帳 | `MapTransfer`：x>0 時 from=副、to=方式；x<0 時 from=方式、to=副，金額取 −x |
    | 提款 | `Withdrawal(bank=方式, cash=現金, |x|)` |
    | 現金存入 | `CashDeposit(bank=方式, cash=現金, |x|)` |
    | 加值 | `TopUp(wallet=方式, source=副, |x|)` |
    | 繳信用卡款 | `CardPayment(payer=方式, card=副, |x|)` |
    | 新增貸款 | `LoanDisbursement(receiver=方式, loan=副, |x|)` |
    | 貸款支出 | `LoanPayment(payer=方式, loan=副, total=|x|, principal=|x|, 貸款支出/副)`：流水帳裡的貸款支出一律視為提前還本 |
    | 手續費 | `Expense(方式, 金融交易/手續費, x)` |
    | 固定支出 | `Expense(方式, 固定支出/副, x)` |
    | 財務規劃帳戶名稱 | `MapFund`，見下 |
    | 其他 | `MapIncomeOrExpense`，見下 |

    - `MapFund`：
      - 方式是「現金」，而且副選單是「入新資金」或任何帳戶 → 拒絕（Q26）。
      - 副選單「出資金」且 x<0 → `FundWithdrawal`（金額 −x）。
      - 副選單「資金回流」且 x>0 → `FundReturn`。
      - 副選單是帳戶且 x>0 → `FundAllocation(to=方式, from=副)`。
      - 其餘情況 → 拒絕。
    - `MapIncomeOrExpense`：主分類找不到 → 拒絕；子分類找不到 → 自動新增並記入 `Warnings`。依主分類的 Kind 呼叫 `Income` 或 `Expense`。
    - 每筆交易都傳入 `budgetMonth: MonthOf(month)` 與 `row.Note`。
  - `Run()` 在建立分類之後加上：`foreach (var month in workbook.Months) MapJournal(month);`
- [ ] Step 4：`dotnet test --project tests/SixJars.Application.Tests -- --filter-class "SixJars.Application.Tests.LegacyImport.LegacyWorkbookMapperJournalTests"` → Expected：總計 11、失敗 0
- [ ] Step 5：`dotnet test` → Expected：總計 79、失敗 0
- [ ] Step 6：Commit
  ```bash
  git add src/SixJars.Application/LegacyImport/MappingSession.cs src/SixJars.Application/LegacyImport/MappingSession.Journal.cs tests/SixJars.Application.Tests/LegacyImport/LegacyWorkbookMapperJournalTests.cs
  git commit -m "feat(import): 流水帳列依主選單規則轉為交易" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

## Task 12：匯入轉換 III —— 制式表格與貸款本金分攤

**Files：** Create: `src/SixJars.Application/LegacyImport/MappingSession.Templates.cs`、`tests/SixJars.Application.Tests/LegacyImport/LegacyWorkbookMapperTemplateTests.cs`；Modify: `MappingSession.cs`（逐月迴圈中，`MapJournal` 之後呼叫 `MapTemplates`）

依 spec §5.3–5.4 實作。容易錯的地方有兩個：
- 一定要**先**處理流水帳，才能知道當月已經提前還了多少本金。
- 交易建立失敗時，不可以留下一筆「已付卻沒有連結交易」的預定支出。正確順序是：先建立預定支出物件 → 建立交易 → `MarkPaid` → 最後兩者一起加入結果。

2 月的真實數字（60,000 + 28,131 = 88,131）直接當作測試案例。

- [ ] Step 1：寫失敗測試
  ```csharp
  using FluentAssertions;
  using SixJars.Application.LegacyImport;
  using SixJars.Domain.Books;
  using SixJars.Domain.Transactions;
  using Xunit;
  using static SixJars.Application.Tests.LegacyImport.LegacyWorkbookFactory;

  namespace SixJars.Application.Tests.LegacyImport;

  public class LegacyWorkbookMapperTemplateTests
  {
      private static LegacyTemplateRow Template(int row, LegacyTemplateSection section, string item, string? method, decimal amount, DateOnly? paidDate) =>
          new(row, section, item, method, amount, paidDate, null);

      [Fact]
      public void Paid_fixed_template_creates_expense_and_paid_plan()
      {
          var result = LegacyWorkbookMapper.Map(Workbook(Month(1, templates:
              [Template(52, LegacyTemplateSection.Fixed, "行動電話費", "國泰世華銀行", -599m, new DateOnly(2026, 1, 16))])));

          var tx = result.Transactions.Single();
          tx.Kind.Should().Be(TransactionKind.Expense);
          tx.Amount.Should().Be(-599m);
          tx.Date.Should().Be(new DateOnly(2026, 1, 16));
          var planned = result.PlannedExpenses.Single();
          planned.PaidTransactionId.Should().Be(tx.Id);
          planned.EstimatedAmount.Should().Be(-599m);
      }

      [Fact]
      public void Template_without_paid_date_is_unpaid_plan_only()
      {
          var result = LegacyWorkbookMapper.Map(Workbook(Month(3, templates:
              [Template(52, LegacyTemplateSection.Fixed, "行動電話費", "國泰世華銀行", -599m, null)])));

          result.Transactions.Should().BeEmpty();
          var planned = result.PlannedExpenses.Single();
          planned.IsPaid.Should().BeFalse();
          planned.AccountId.Should().Be(result.Book.FindAccount("國泰世華銀行")!.Id);
      }

      [Fact]
      public void Paid_date_without_method_stays_unpaid_with_warning()
      {
          var result = LegacyWorkbookMapper.Map(Workbook(Month(2, templates:
              [Template(66, LegacyTemplateSection.Fixed, "保險費", null, -130m, new DateOnly(2026, 2, 27))])));

          result.Transactions.Should().BeEmpty();
          result.PlannedExpenses.Single().AccountId.Should().BeNull();
          result.Report.Warnings.Should().ContainSingle(i => i.Row == 66 && i.Sheet == "2月");
      }

      [Fact]
      public void Loan_template_takes_principal_from_loan_area()
      {
          var result = LegacyWorkbookMapper.Map(Workbook(Month(1,
              templates: [Template(76, LegacyTemplateSection.Loan, "房屋貸款", "華南銀行", -32503m, new DateOnly(2026, 1, 9))],
              loanPrincipals: [new("房屋貸款", -28085m)])));

          var tx = result.Transactions.Single();
          tx.Kind.Should().Be(TransactionKind.LoanPayment);
          tx.Amount.Should().Be(32503m);
          tx.LoanPrincipal.Should().Be(28085m);
      }

      [Fact]
      public void Journal_prepayment_takes_full_principal_and_rest_goes_to_template()
      {
          var result = LegacyWorkbookMapper.Map(Workbook(Month(2,
              journal: [Row(129, new DateOnly(2026, 2, 24), "華南銀行", "貸款支出", "房屋貸款", -60000m)],
              templates: [Template(76, LegacyTemplateSection.Loan, "房屋貸款", "華南銀行", -32503m, new DateOnly(2026, 2, 9))],
              loanPrincipals: [new("房屋貸款", -88131m)])));

          result.Report.Errors.Should().BeEmpty();
          result.Transactions.Select(t => t.LoanPrincipal).Should().Equal(60000m, 28131m);
      }

      [Fact]
      public void Principal_that_cannot_be_allocated_is_an_error()
      {
          var result = LegacyWorkbookMapper.Map(Workbook(Month(1,
              templates: [Template(76, LegacyTemplateSection.Loan, "房屋貸款", "華南銀行", -32503m, new DateOnly(2026, 1, 9))],
              loanPrincipals: [new("房屋貸款", -40000m)])));

          result.Transactions.Should().BeEmpty();
          result.PlannedExpenses.Should().BeEmpty();
          result.Report.Errors.Should().ContainSingle(i => i.Row == 76);
      }

      [Fact]
      public void Special_template_creates_sub_category_on_demand()
      {
          var result = LegacyWorkbookMapper.Map(Workbook(Month(1, templates:
              [Template(86, LegacyTemplateSection.Special, "牙齒矯正", "現金", -5000m, new DateOnly(2026, 1, 20))])));

          result.Book.FindCategory("特別支出", "牙齒矯正")!.Nature.Should().Be(ExpenseNature.Special);
          result.Transactions.Single().Kind.Should().Be(TransactionKind.Expense);
      }
  }
  ```
- [ ] Step 2：`dotnet test --project tests/SixJars.Application.Tests -- --filter-class "SixJars.Application.Tests.LegacyImport.LegacyWorkbookMapperTemplateTests"` → 確認失敗原因是 `PlannedExpenses` 為空，或 `Transactions` 中沒有制式表格的交易。注意 `Journal_prepayment...` 會因為只有 1 筆交易而失敗，而不是本金數字錯誤。
- [ ] Step 3：實作 `MappingSession.Templates.cs`
  - `MapTemplates(month)`：逐列處理 `Amount != 0` 的制式表格列，與流水帳相同的 per-row catch。
  - `MapTemplateRow`：
    1. 分類：Fixed → `固定支出/項目`；Loan → `貸款支出/項目`；Special → `特別支出/項目`（不存在就建立，不需要警告）。
    2. 帳戶：方式為 null 時帳戶為 null；方式有值但找不到 → 拒絕。
    3. `planned = PlannedExpense.Create(_book, MonthOf(month), category.Id, account?.Id, row.Amount, row.Note)`
    4. 依付款狀態處理：
       - 沒有支出日 → 只加入 planned。
       - 有支出日、沒有帳戶 → 加入 planned，並記入 `Warnings`。
       - 其餘：
         - 先 `NormalizeDate`，再建立交易：Loan → `LoanPayment(date, account, loan=項目帳戶, total=−Amount, principal=TemplateLoanPrincipal(...), category)`；其他 → `Expense(date, account, category, Amount)`。
         - 然後 `planned.MarkPaid(tx)`，最後把交易和 planned 一起加入結果。
  - `TemplateLoanPrincipal(month, row)`：
    - 同月同貸款若有多筆已付的列 → 拒絕。
    - `monthPrincipal = |LoanPrincipals[項目]|`
    - `journalPrincipal = Σ 已建立的、同歸屬月份、CounterAccountId 為該貸款的 LoanPayment.LoanPrincipal`
    - `remaining = monthPrincipal − journalPrincipal`；如果 `remaining < 0` 或 `remaining > 繳款總額` → 拒絕，訊息需包含三個數字。
- [ ] Step 4：`dotnet test --project tests/SixJars.Application.Tests -- --filter-class "SixJars.Application.Tests.LegacyImport.LegacyWorkbookMapperTemplateTests"` → Expected：總計 7、失敗 0
- [ ] Step 5：`dotnet test` → Expected：總計 86、失敗 0
- [ ] Step 6：Commit
  ```bash
  git add src/SixJars.Application/LegacyImport/MappingSession.cs src/SixJars.Application/LegacyImport/MappingSession.Templates.cs tests/SixJars.Application.Tests/LegacyImport/LegacyWorkbookMapperTemplateTests.cs
  git commit -m "feat(import): 制式表格轉為預定支出，並依貸款區分攤本金" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

## Task 13：端到端驗收（記憶體）

**Files：** Create: `tests/SixJars.AcceptanceTests/SixJars.AcceptanceTests.csproj`、`tests/SixJars.AcceptanceTests/{LegacyWorkbookFixture,MonthFigureComparison,LegacyWorkbookAcceptanceTests}.cs`

這是 P1 的驗收標準（spec §1）。這個 Task 和其他 Task 不同：Step 2 可能**直接通過**，也可能列出不一致。每一筆不一致都代表 Task 3–12 有規則寫錯。

**處理不一致的紀律**：每修正一個不一致，都必須先在對應那一層（Domain 或 Application.Tests）補上一個能重現問題的失敗測試，再修程式。不可以只為了讓驗收通過而改程式；同時要把發現的規則回寫到 spec。

- [ ] Step 1：建立專案與測試

  `SixJars.AcceptanceTests.csproj`：套件同 Domain.Tests，另外：
  - ProjectReference → `src/SixJars.Infrastructure`
  - `<Compile Include="..\Shared\**\*.cs" LinkBase="Shared" />`

  建好後執行 `dotnet sln SixJars.slnx add tests/SixJars.AcceptanceTests`。

  `LegacyWorkbookFixture.cs`：
  ```csharp
  using SixJars.Application.LegacyImport;
  using SixJars.Infrastructure.LegacyExcel;
  using SixJars.Tests.Shared;
  using Xunit;

  namespace SixJars.AcceptanceTests;

  /// <summary>讀一次真實記帳本供整個測試類別共用；檔案不存在時，測試會被略過。</summary>
  public sealed class LegacyWorkbookFixture : IAsyncLifetime
  {
      private LegacyWorkbook? _workbook;

      public async ValueTask InitializeAsync()
      {
          if (!File.Exists(RepoPaths.LegacyWorkbook))
          {
              return;
          }

          await using var stream = RepoPaths.OpenLegacyWorkbook();
          _workbook = await new ExcelLegacyWorkbookReader().ReadAsync(stream, TestContext.Current.CancellationToken);
      }

      public ValueTask DisposeAsync() => ValueTask.CompletedTask;

      public LegacyWorkbook Require()
      {
          Assert.SkipUnless(_workbook is not null, $"找不到 {RepoPaths.LegacyWorkbook}，略過驗收");
          return _workbook!;
      }
  }
  ```
  `MonthFigureComparison.cs`：
  ```csharp
  using SixJars.Application.LegacyImport;
  using SixJars.Domain.Common;
  using SixJars.Domain.Ledger;

  namespace SixJars.AcceptanceTests;

  /// <summary>把本系統算出的數字與 Excel 同月工作表逐項比對（取至小數 2 位）。</summary>
  internal static class MonthFigureComparison
  {
      public static IReadOnlyList<string> Compare(LedgerSnapshot ledger, int year, LegacyMonthSheet sheet)
      {
          var month = new BudgetMonth(year, sheet.Month);
          var figures = sheet.Figures;
          var balances = new BalanceCalculator(ledger);
          var mismatches = new List<string>();

          // Excel 的 J6 含「含電子錢包」加回的存量 N5，本系統是純流量（spec §4.3）
          Check("月可用餘額", new DisposableBalanceCalculator(ledger).Monthly(month), figures.MonthlyDisposable - figures.WalletAddBack);
          Check("可用現金", new AvailableCashCalculator(ledger).ThroughBudgetMonth(month, includeEWallets: false), figures.AvailableCash);
          foreach (var f in figures.BankBalances.Concat(figures.EWalletBalances))
          {
              Check(f.Name, AccountBalance(f.Name), f.Amount);
          }

          foreach (var f in figures.CardOutstanding.Concat(figures.LoanRemaining))
          {
              Check(f.Name, -AccountBalance(f.Name), f.Amount);
          }

          foreach (var f in figures.FundBalances)
          {
              var fund = ledger.Book.FindPlanningFund(f.Name);
              Check(f.Name, fund is null ? 0m : balances.FundBalanceThroughBudgetMonth(fund.Id, month), f.Amount);
          }

          return mismatches;

          decimal AccountBalance(string name) =>
              ledger.Book.FindAccount(name) is { } account ? balances.AccountBalanceThroughBudgetMonth(account.Id, month) : 0m;

          void Check(string label, decimal actual, decimal expected)
          {
              if (Math.Round(actual, 2) != Math.Round(expected, 2))
              {
                  mismatches.Add($"{month} {label}：本系統 {actual:N2}，Excel {expected:N2}，差 {actual - expected:N2}");
              }
          }
      }
  }
  ```
  `LegacyWorkbookAcceptanceTests.cs`：
  ```csharp
  using FluentAssertions;
  using SixJars.Application.LegacyImport;
  using SixJars.Domain.Ledger;
  using Xunit;

  namespace SixJars.AcceptanceTests;

  public class LegacyWorkbookAcceptanceTests(LegacyWorkbookFixture fixture) : IClassFixture<LegacyWorkbookFixture>
  {
      public static TheoryData<int> ImportedMonths => [1, 2, 3];

      [Fact]
      public void Import_has_no_errors()
      {
          var report = LegacyWorkbookMapper.Map(fixture.Require()).Report;

          foreach (var issue in report.Corrections.Concat(report.Warnings))
          {
              TestContext.Current.TestOutputHelper?.WriteLine($"{issue.Sheet} 第 {issue.Row} 列：{issue.Message}");
          }

          report.Errors.Should().BeEmpty();
      }

      [Theory]
      [MemberData(nameof(ImportedMonths))]
      public void Monthly_figures_match_excel(int month)
      {
          var workbook = fixture.Require();
          var result = LegacyWorkbookMapper.Map(workbook);
          var ledger = new LedgerSnapshot(result.Book, result.Transactions, result.PlannedExpenses);

          MonthFigureComparison.Compare(ledger, workbook.Settings.Year, workbook.Months.Single(m => m.Month == month))
              .Should().BeEmpty();
      }
  }
  ```
- [ ] Step 2：`dotnet test --project tests/SixJars.AcceptanceTests` → 記錄結果：
  - 全部通過 → 前進到 Step 4。
  - 有不一致 → 依上面的紀律逐筆修正。每一筆修正都是**獨立的 commit**（`fix(<layer>): …`，內含新增的單元測試）。
- [ ] Step 3：確認 `Import_has_no_errors` 的輸出符合預期：
  - Corrections：1 月第 8、9 列（2026-12-31 → 2025-12-31）。
  - Warnings：2 月第 66 列（Youtube/Google，有支出日但沒有方式）。
  - 如果出現其他項目，要逐一判斷是否合理，並寫進交接或回寫說明。
- [ ] Step 4：`dotnet test --project tests/SixJars.AcceptanceTests` → Expected：總計 4、失敗 0
- [ ] Step 5：`dotnet test` → Expected：總計 90 以上（加上修正不一致時新增的測試）、失敗 0
- [ ] Step 6：Commit
  ```bash
  git add SixJars.slnx tests/SixJars.AcceptanceTests/SixJars.AcceptanceTests.csproj tests/SixJars.AcceptanceTests/LegacyWorkbookFixture.cs tests/SixJars.AcceptanceTests/MonthFigureComparison.cs tests/SixJars.AcceptanceTests/LegacyWorkbookAcceptanceTests.cs
  git commit -m "test(acceptance): 1–3 月數字與 Excel 逐月比對" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

> **Checkpoint A**：規則已用真實資料驗證。回寫 spec 與計畫的偏差（`docs(plans):`、`docs(spec):`）。可以在這裡收工。

> **A2 執行紀錄（2026-10-04）**：`1fce7ae`..`adcce51`，由 subagent 執行 Task 9–12，主控者審查後補 1 個 commit，Task 13 經使用者決策後提交。`dotnet test` 總計 91、失敗 0；`dotnet build` 0 warning。
> - **測試數**：Task 9–12 依序為 60 → 68 → 79 → 86，與計畫相同。審查補了 `5ded9ba` 一個測試，所以 Task 13 結束時是 91（計畫寫 90），段 B 的期望值已各加 1。
> - **已知 Excel 差異（使用者決定採 B）**：3 月月可用餘額本系統 8,299，Excel `J6 − N5` 是 8,326，差 −27。來源是流水帳第 20 列主選單「手續費」：Excel 的 J6 依主選單名稱 DSUM，漏算這筆。本系統維持 spec §5.2，計為支出。驗收新增 `tests/SixJars.AcceptanceTests/KnownExcelDifferences.cs`，以精確金額調整 Excel 期望值，不放寬比對；把調整值改成 −26 會重新失敗。
> - **Step 2 的錯誤碼**：Task 9 是 CS0234（命名空間還不存在），Task 10 是 CS0103（以靜態成員呼叫不存在的類別）。失敗原因都正確。
> - **讀取器**：制式表格的「固定」區沒有備註欄（AB 欄是「預算剩餘」），Note 一律為 null。計畫的儲存格位址全部經真實檔案查證正確；spec §5.1 的位址已改成與計畫一致。
> - **Task 11**：
>   - 主選單「固定支出」沒有另寫分支，而是走 `MapIncomeOrExpense`。差別是子分類不在清單時，會自動新增並記一筆警告。
>   - `MapFund` 的 Q26 檢查只比對字面上的「現金」，「非手上現金」會對應到外幣現鈔，不在拒絕之列。
> - **Task 12**：「同月多筆已付貸款列」的判定，多了「有方式」這個條件，與「有支出日但沒有方式就視為未付」的規則一致。
> - **審查補的測試（`5ded9ba`）**：匯入規則做了 10 個變異，其中 2 個原本沒被抓到，補測試後都能抓到：
>   - 手續費的分錄符號：原本被 3 月驗收的既有失敗遮蔽。
>   - Q26「現金＋銀行」撥入財務規劃帳戶：原本的測試只用「入新資金」組合，這個組合本來就會被兜底分支拒絕。
> - **留意**：3 月 J6 的 DSUM 範圍只到流水帳第 79 列，1、2 月到第 170 列以上。之後的月份若資料超過該月公式範圍，Excel 端會少算，比對時要先排除這種情況。
> - **已知小瑕疵（未處理）**：特別支出的子分類會在建立交易之前先建立。如果該列之後失敗，會留下一個沒有被使用的子分類。不影響任何數字。

---

## Task 14：EF Core 持久層與 migration

**Files：** Create: `src/SixJars.Infrastructure/Persistence/{SixJarsDbContext,ValueConverters,BookConfiguration,TransactionConfiguration,PlannedExpenseConfiguration,DesignTimeDbContextFactory}.cs`、`Persistence/Migrations/*`（由工具產生）、`tests/Shared/PostgresFixture.cs`、`tests/SixJars.Infrastructure.Tests/{AssemblyInfo,Persistence/LedgerPersistenceTests}.cs`；Modify: `src/SixJars.Domain/Transactions/Transaction.cs`、兩個 csproj

D1-b 的實作。容易錯的地方有四個：
- **建構子綁定**：`Transaction` 的 internal 建構子含有分錄集合，EF 無法綁定，需要另外加一個 private 無參數建構子。`Book`、`Account`、`PlanningFund`、`Category`、`PlannedExpense` 的建構子參數名稱都對得上屬性名稱，可以直接綁定。
- **Owned collection 的 backing field**：要設定 `UsePropertyAccessMode(PropertyAccessMode.Field)`。
- **強型別 Id 與歸屬月份**：用 `ConfigureConventions` 統一設定 converter。
- **小數精度**：`decimal` 一律設為 `HasPrecision(18, 4)`，和讀取器的 4 位小數一致。

- [ ] Step 1：套件、fixture 與失敗測試
  - `src/SixJars.Infrastructure.csproj`：加上 `Npgsql.EntityFrameworkCore.PostgreSQL`、`Microsoft.EntityFrameworkCore.Relational`（對齊 10.0.12，見 Checkpoint B 紀錄），以及 `Microsoft.EntityFrameworkCore.Design`（`PrivateAssets="all"`）。
  - `tests/SixJars.AcceptanceTests.csproj`：同樣在此加上 `Testcontainers.PostgreSql`。原因是兩個測試專案都編譯 `tests/Shared/**`，新增 `PostgresFixture.cs` 後，AcceptanceTests 會立刻需要這個套件。
  - `tests/SixJars.Infrastructure.Tests.csproj`：加上 `Testcontainers.PostgreSql`。
  - `tests/SixJars.Infrastructure.Tests/AssemblyInfo.cs`：
    ```csharp
    using SixJars.Tests.Shared;
    using Xunit;

    [assembly: AssemblyFixture(typeof(PostgresFixture))]
    ```
  - `tests/Shared/PostgresFixture.cs`：
    ```csharp
    using Microsoft.EntityFrameworkCore;
    using Npgsql;
    using SixJars.Infrastructure.Persistence;
    using Testcontainers.PostgreSql;
    using Xunit;

    namespace SixJars.Tests.Shared;

    /// <summary>整個測試組件共用一個 PostgreSQL 容器；每個測試各自建立獨立的資料庫。</summary>
    public sealed class PostgresFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

        public async ValueTask InitializeAsync() => await _container.StartAsync(TestContext.Current.CancellationToken);

        public ValueTask DisposeAsync() => _container.DisposeAsync();

        public async Task<Func<SixJarsDbContext>> CreateDatabaseAsync(CancellationToken cancellationToken)
        {
            var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
            {
                Database = $"t_{Guid.NewGuid():N}",
            }.ConnectionString;
            var options = new DbContextOptionsBuilder<SixJarsDbContext>().UseNpgsql(connectionString).Options;
            await using (var db = new SixJarsDbContext(options))
            {
                await db.Database.MigrateAsync(cancellationToken);
            }

            return () => new SixJarsDbContext(options);
        }
    }
    ```
  - `Persistence/LedgerPersistenceTests.cs`：
    ```csharp
    using FluentAssertions;
    using Microsoft.EntityFrameworkCore;
    using SixJars.Domain.Books;
    using SixJars.Domain.Common;
    using SixJars.Domain.Planning;
    using SixJars.Domain.Transactions;
    using SixJars.Tests.Shared;
    using Xunit;

    namespace SixJars.Infrastructure.Tests.Persistence;

    public class LedgerPersistenceTests(PostgresFixture postgres)
    {
        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Book_round_trips_with_accounts_funds_and_categories()
        {
            var book = new Book("我的帳本", new DateOnly(2025, 12, 30));
            var foreignCash = book.AddAccount("外幣現鈔", AccountType.Cash, 10000m, countsAsAvailableCash: false);
            book.AddAccount("國泰Combo卡", AccountType.CreditCard, -22668m);
            book.AddPlanningFund("財務自由帳戶", 9874.3m);
            var fixedMain = book.AddExpenseCategory("固定支出", ExpenseNature.Fixed);
            book.AddSubCategory(fixedMain.Id, "保險費");
            var createContext = await postgres.CreateDatabaseAsync(Ct);
            await using (var db = createContext())
            {
                db.Books.Add(book);
                await db.SaveChangesAsync(Ct);
            }

            await using var readDb = createContext();
            var loaded = await readDb.Books.SingleAsync(b => b.Id == book.Id, Ct);

            loaded.OpeningDate.Should().Be(new DateOnly(2025, 12, 30));
            loaded.Accounts.Should().BeEquivalentTo(book.Accounts);
            loaded.GetAccount(foreignCash.Id).CountsAsAvailableCash.Should().BeFalse();
            loaded.PlanningFunds.Single().OpeningBalance.Should().Be(9874.3m);
            loaded.FindCategory("固定支出", "保險費")!.Nature.Should().Be(ExpenseNature.Fixed);
        }

        [Fact]
        public async Task Transaction_round_trips_with_postings_and_budget_month()
        {
            var book = new Book("我的帳本", new DateOnly(2025, 12, 30));
            var bank = book.AddAccount("華南銀行", AccountType.Bank, 11637m);
            var loan = book.AddAccount("房屋貸款", AccountType.Loan, -2658876m);
            var loanExpense = book.AddExpenseCategory("貸款支出", ExpenseNature.Loan);
            var salary = book.AddIncomeCategory("工作薪資1");
            var factory = new TransactionFactory(book);
            var payment = factory.LoanPayment(new DateOnly(2026, 1, 9), bank.Id, loan.Id, 32503m, 28085m, loanExpense.Id, "房貸");
            var income = factory.Income(new DateOnly(2026, 1, 30), bank.Id, salary.Id, 84223m, budgetMonth: new BudgetMonth(2026, 2));
            var createContext = await postgres.CreateDatabaseAsync(Ct);
            await using (var db = createContext())
            {
                db.Books.Add(book);
                db.Transactions.AddRange(payment, income);
                await db.SaveChangesAsync(Ct);
            }

            await using var readDb = createContext();
            var loaded = await readDb.Transactions.Where(t => t.BookId == book.Id).ToListAsync(Ct);

            var loadedPayment = loaded.Single(t => t.Id == payment.Id);
            loadedPayment.Postings.Should().Equal(payment.Postings);
            loadedPayment.LoanPrincipal.Should().Be(28085m);
            loadedPayment.Note.Should().Be("房貸");
            var loadedIncome = loaded.Single(t => t.Id == income.Id);
            loadedIncome.BudgetMonth.Should().Be(new BudgetMonth(2026, 2));
            loadedIncome.Date.Should().Be(new DateOnly(2026, 1, 30));
        }

        [Fact]
        public async Task Planned_expense_round_trips_paid_link()
        {
            var book = new Book("我的帳本", new DateOnly(2025, 12, 30));
            var card = book.AddAccount("國泰Combo卡", AccountType.CreditCard, -22668m);
            var fixedMain = book.AddExpenseCategory("固定支出", ExpenseNature.Fixed);
            var insurance = book.AddSubCategory(fixedMain.Id, "保險費");
            var tx = new TransactionFactory(book).Expense(new DateOnly(2026, 1, 21), card.Id, insurance.Id, -1921m);
            var planned = PlannedExpense.Create(book, new BudgetMonth(2026, 1), insurance.Id, card.Id, -1921m);
            planned.MarkPaid(tx);
            var createContext = await postgres.CreateDatabaseAsync(Ct);
            await using (var db = createContext())
            {
                db.Books.Add(book);
                db.Transactions.Add(tx);
                db.PlannedExpenses.Add(planned);
                await db.SaveChangesAsync(Ct);
            }

            await using var readDb = createContext();
            var loaded = await readDb.PlannedExpenses.SingleAsync(p => p.Id == planned.Id, Ct);

            loaded.PaidTransactionId.Should().Be(tx.Id);
            loaded.BudgetMonth.Should().Be(new BudgetMonth(2026, 1));
            loaded.AccountId.Should().Be(card.Id);
        }
    }
    ```
- [ ] Step 2：`dotnet build tests/SixJars.Infrastructure.Tests` → 確認失敗原因是 **CS0246 找不到 `SixJarsDbContext`**
- [ ] Step 3：實作
  - `ValueConverters.cs`：每個強型別 Id 各一個 `ValueConverter<XxxId, Guid>`；`BudgetMonthConverter : ValueConverter<BudgetMonth, int>`，使用 `m => m.Key` 與 `BudgetMonth.FromKey`。
  - `SixJarsDbContext`：
    - `DbSet` 有 `Books`、`Transactions`、`PlannedExpenses`。
    - `ConfigureConventions`：註冊上述 converter；`Properties<decimal>().HavePrecision(18, 4)`。
    - `OnModelCreating`：`ApplyConfigurationsFromAssembly`。
  - `BookConfiguration`：
    - `HasKey(Id)`，`ValueGeneratedNever`。
    - `OwnsMany(Accounts)`：資料表 `"Accounts"`、`WithOwner().HasForeignKey("BookId")`、`HasKey(a => a.Id)`、`ValueGeneratedNever`、`Type` 用 `HasConversion<string>()`。
    - `PlanningFunds`（資料表 `"PlanningFunds"`）、`Categories`（資料表 `"Categories"`，`Kind`/`Nature` 存成字串）比照辦理。
    - 三個導覽屬性都設定 `UsePropertyAccessMode(PropertyAccessMode.Field)`。
    - 名稱欄位 `HasMaxLength(100)`。
  - `TransactionConfiguration`：
    - `HasKey(Id)`，`ValueGeneratedNever`；`Kind` 存成字串；`Note` 的 `HasMaxLength(500)`。
    - `HasIndex(t => new { t.BookId, t.BudgetMonth })`。
    - `OwnsMany(Postings)`：資料表 `"Postings"`、`WithOwner().HasForeignKey("TransactionId")`、`Property<int>("Id")`、`HasKey("Id")`、`HasIndex(p => p.AccountId)`。
    - `Navigation(Postings).UsePropertyAccessMode(Field)`。
  - `PlannedExpenseConfiguration`：`HasKey`，`ValueGeneratedNever`；`HasIndex(p => new { p.BookId, p.BudgetMonth })`。
  - `DesignTimeDbContextFactory`：用 `UseNpgsql("Host=localhost;Database=sixjars_design")`，並加註解「只供 dotnet ef 產生 migration，不實際連線、不含任何憑證；正式連線字串於 P2 由環境變數提供」。
  - `Transaction.cs`：加上 `private Transaction() { } // EF Core 專用`。
  - 產生 migration：
    ```bash
    dotnet ef migrations add InitialLedger --project src/SixJars.Infrastructure --output-dir Persistence/Migrations
    ```
    產生後檢查 migration 內容：資料表有 `Books`、`Accounts`、`PlanningFunds`、`Categories`、`Transactions`、`Postings`、`PlannedExpenses`；`BudgetMonth` 欄位是 `integer`；金額是 `numeric(18,4)`。
- [ ] Step 4：`dotnet test --project tests/SixJars.Infrastructure.Tests -- --filter-class "SixJars.Infrastructure.Tests.Persistence.LedgerPersistenceTests"` → Expected：總計 3、失敗 0（第一次執行需要拉取映像檔）
- [ ] Step 5：`dotnet test` → Expected：總計 94 以上、失敗 0（A2 審查多補 1 個測試，原為 93）
- [ ] Step 6：Commit
  ```bash
  git add Directory.Packages.props tests/SixJars.AcceptanceTests/SixJars.AcceptanceTests.csproj src/SixJars.Infrastructure/SixJars.Infrastructure.csproj src/SixJars.Infrastructure/Persistence src/SixJars.Domain/Transactions/Transaction.cs tests/Shared/PostgresFixture.cs tests/SixJars.Infrastructure.Tests/SixJars.Infrastructure.Tests.csproj tests/SixJars.Infrastructure.Tests/AssemblyInfo.cs tests/SixJars.Infrastructure.Tests/Persistence/LedgerPersistenceTests.cs
  git commit -m "feat(infra): EF Core + PostgreSQL 持久層，分錄實體化存檔" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

## Task 15：帳本快照載入器

**Files：** Create: `src/SixJars.Infrastructure/Persistence/LedgerSnapshotLoader.cs`、`tests/SixJars.Infrastructure.Tests/Persistence/LedgerSnapshotLoaderTests.cs`

載入器是持久層與計算器之間的接縫。它只能載入**指定帳本**的資料：Q6 的帳本邊界從這裡開始生效。

- [ ] Step 1：寫失敗測試
  ```csharp
  using FluentAssertions;
  using SixJars.Domain.Books;
  using SixJars.Domain.Transactions;
  using SixJars.Infrastructure.Persistence;
  using SixJars.Tests.Shared;
  using Xunit;

  namespace SixJars.Infrastructure.Tests.Persistence;

  public class LedgerSnapshotLoaderTests(PostgresFixture postgres)
  {
      [Fact]
      public async Task Loads_only_the_requested_book()
      {
          var ct = TestContext.Current.CancellationToken;
          var (mine, myIncome) = BookWithOneIncome("我的帳本");
          var (other, otherIncome) = BookWithOneIncome("別人的帳本");
          var createContext = await postgres.CreateDatabaseAsync(ct);
          await using (var db = createContext())
          {
              db.Books.AddRange(mine, other);
              db.Transactions.AddRange(myIncome, otherIncome);
              await db.SaveChangesAsync(ct);
          }

          await using var readDb = createContext();
          var snapshot = await new LedgerSnapshotLoader(readDb).LoadAsync(mine.Id, ct);

          snapshot.Book.Id.Should().Be(mine.Id);
          snapshot.Transactions.Select(t => t.Id).Should().Equal(myIncome.Id);
          snapshot.PlannedExpenses.Should().BeEmpty();
      }

      private static (Book, Transaction) BookWithOneIncome(string name)
      {
          var book = new Book(name, new DateOnly(2025, 12, 30));
          var bank = book.AddAccount("國泰世華銀行", AccountType.Bank);
          var salary = book.AddIncomeCategory("工作薪資1");
          return (book, new TransactionFactory(book).Income(new DateOnly(2026, 1, 5), bank.Id, salary.Id, 100m));
      }
  }
  ```
- [ ] Step 2：`dotnet build tests/SixJars.Infrastructure.Tests` → 確認失敗原因是 **CS0246 找不到 `LedgerSnapshotLoader`**
- [ ] Step 3：實作
  ```csharp
  public sealed class LedgerSnapshotLoader(SixJarsDbContext db)
  {
      public async Task<LedgerSnapshot> LoadAsync(BookId bookId, CancellationToken cancellationToken)
      {
          var book = await db.Books.AsNoTracking().SingleAsync(b => b.Id == bookId, cancellationToken);
          var transactions = await db.Transactions.AsNoTracking().Where(t => t.BookId == bookId).ToListAsync(cancellationToken);
          var planned = await db.PlannedExpenses.AsNoTracking().Where(p => p.BookId == bookId).ToListAsync(cancellationToken);
          return new LedgerSnapshot(book, transactions, planned);
      }
  }
  ```
- [ ] Step 4：`dotnet test --project tests/SixJars.Infrastructure.Tests -- --filter-class "SixJars.Infrastructure.Tests.Persistence.LedgerSnapshotLoaderTests"` → Expected：總計 1、失敗 0
- [ ] Step 5：`dotnet test` → Expected：總計 95 以上、失敗 0（原為 94）
- [ ] Step 6：Commit
  ```bash
  git add src/SixJars.Infrastructure/Persistence/LedgerSnapshotLoader.cs tests/SixJars.Infrastructure.Tests/Persistence/LedgerSnapshotLoaderTests.cs
  git commit -m "feat(infra): 依帳本載入計算用快照" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

## Task 16：端到端驗收（經資料庫往返）

**Files：** Create: `tests/SixJars.AcceptanceTests/{AssemblyInfo,PersistedLedgerAcceptanceTests}.cs`；Modify: `tests/SixJars.AcceptanceTests/SixJars.AcceptanceTests.csproj`（加上 `Testcontainers.PostgreSql`）

證明持久層沒有破壞任何數字，特別是小數精度、歸屬月份 converter 與 owned 分錄。比對邏輯完全重用 Task 13 的 `MonthFigureComparison`。

- [ ] Step 1：寫測試

  `AssemblyInfo.cs`：和 Infrastructure.Tests 相同的 `[assembly: AssemblyFixture(typeof(PostgresFixture))]`。

  `PersistedLedgerAcceptanceTests.cs`：
  ```csharp
  using FluentAssertions;
  using SixJars.Application.LegacyImport;
  using SixJars.Infrastructure.Persistence;
  using SixJars.Tests.Shared;
  using Xunit;

  namespace SixJars.AcceptanceTests;

  public class PersistedLedgerAcceptanceTests(LegacyWorkbookFixture fixture, PostgresFixture postgres)
      : IClassFixture<LegacyWorkbookFixture>
  {
      [Theory]
      [MemberData(nameof(LegacyWorkbookAcceptanceTests.ImportedMonths), MemberType = typeof(LegacyWorkbookAcceptanceTests))]
      public async Task Persisted_ledger_still_matches_excel(int month)
      {
          var ct = TestContext.Current.CancellationToken;
          var workbook = fixture.Require();
          var result = LegacyWorkbookMapper.Map(workbook);
          var createContext = await postgres.CreateDatabaseAsync(ct);
          await using (var db = createContext())
          {
              db.Books.Add(result.Book);
              db.Transactions.AddRange(result.Transactions);
              db.PlannedExpenses.AddRange(result.PlannedExpenses);
              await db.SaveChangesAsync(ct);
          }

          await using var readDb = createContext();
          var ledger = await new LedgerSnapshotLoader(readDb).LoadAsync(result.Book.Id, ct);

          MonthFigureComparison.Compare(ledger, workbook.Settings.Year, workbook.Months.Single(m => m.Month == month))
              .Should().BeEmpty();
      }
  }
  ```
- [ ] Step 2：`dotnet test --project tests/SixJars.AcceptanceTests -- --filter-class "SixJars.AcceptanceTests.PersistedLedgerAcceptanceTests"` → 這是在既有元件上做的整合驗證，**預期一次通過**。如果失敗，問題一定出在 Task 14 的 mapping，例如精度或 converter。依照 Task 13 的紀律，先在 `LedgerPersistenceTests` 補上能重現問題的測試再修。
- [ ] Step 3：（不需要額外實作）
- [ ] Step 4：同 Step 2 → Expected：總計 3、失敗 0
- [ ] Step 5：`dotnet test` → Expected：總計 98 以上、失敗 0（原為 97）
- [ ] Step 6：Commit
  ```bash
  git add tests/SixJars.AcceptanceTests/AssemblyInfo.cs tests/SixJars.AcceptanceTests/PersistedLedgerAcceptanceTests.cs
  git commit -m "test(acceptance): 匯入結果經 PostgreSQL 往返後仍與 Excel 一致" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

> **Checkpoint B**：P1 完成。

> **B 執行紀錄（2026-10-04）**：`9158853`..`1d0c270`。subagent 執行 Task 14–16，主控者審查後補了 2 個 commit。`dotnet test` 總計 98、失敗 0、已略過 0；`reference/` 暫時移走時，總計 98、已略過 12（5+4+3）。`dotnet build` 0 warning；`has-pending-model-changes` 沒有變更。
> - **Migration `20261003174753_InitialLedger`**：
>   - 共 7 張表：Books、Accounts、PlanningFunds、Categories、Transactions、Postings、PlannedExpenses。
>   - Postings 是 Transactions 的 owned 表，主鍵是影子 int identity。
>   - 歸屬月份以 integer 存 yyyymm。
>   - 所有 decimal 都是 numeric(18,4)。
> - **EF Core 版本**：
>   - 問題：Npgsql 10.0.3 只要求 EF Core ≥10.0.4，與 Design 10.0.12 混用會產生 CS1705 與 MSB3277。
>   - 段 B 先把 Design 降到 10.0.4 暫解。
>   - 使用者決定改為在 Infrastructure 明確參考 `Microsoft.EntityFrameworkCore.Relational` 10.0.12（`1d0c270`），取得最新 patch，並與 dotnet-ef 工具同版。之後升版時，Relational 與 Design 要一起改。
> - **Testcontainers 提前到 Task 14 加入 AcceptanceTests**：原因是兩個測試專案共用編譯 `tests/Shared/**`。Task 14 與 Task 16 的 git add 清單已依此修正。
> - **`Transaction` 的 EF 建構子**：寫成 `private Transaction() => _postings = [];`，因為 `_postings` 是 readonly 而且不可為 null。這是 Domain 唯一的變更。
> - **審查補的測試（`17b75a9`）**：
>   - 持久層做了 5 個變異：歸屬月份讀回錯月、交易不限帳本、預定支出不限帳本、帳戶 Id 讀回錯、無參數建構子殘留分錄。
>   - 其中「預定支出不限帳本」原本沒被抓到，因為原測試的兩本帳本都沒有預定支出。已改為兩本各一筆，現在能抓到。
>   - 測試總數不變。
> - **留意**：`LedgerSnapshotLoader` 讀交易沒有排序。目前的計算器只做加總，與順序無關；P2 若需要逐筆列表，要加 `OrderBy(Date)`。

---

## 完成後的驗證

- [ ] `dotnet build`：0 warning、0 error
- [ ] `dotnet test`：總計 ≥ 98、失敗 0、已略過 0（`reference/` 存在且 Docker 正在執行時）
  - Domain.Tests 55、Application.Tests 26、Infrastructure.Tests 9、AcceptanceTests 7，再加上 Task 13 修正時新增的測試。
- [ ] 暫時把 `reference/` 改名後執行 `dotnet test`：失敗 0，已略過 12（9 + 3 個 xlsm 相關）。結束後改回原名。
- [ ] `git status`：工作區乾淨，`reference/` 不在追蹤清單中（`git ls-files reference` 沒有輸出）。
- [ ] `git log --oneline master..`：17 個 feature commit（Task 0–16），加上 Task 13 的修正 commit 與 `docs(plans)` 回寫 commit。
- [ ] `git log -p master.. | grep -iE "password|secret|Host=.*;Password"`：沒有任何憑證（design-time factory 只有不含帳密的 `Host=localhost`）。

## 手動驗證

1. **匯入報告的內容是否合理**：執行
   ```bash
   dotnet test --project tests/SixJars.AcceptanceTests -- --filter-method "*Import_has_no_errors" --output Detailed
   ```
   逐條看 Corrections 與 Warnings，確認每一條都是你認得的資料狀況。
2. **抽查數字**：從 2 月工作表挑 3 個儲存格（例如 `X99` 華南銀行、`X126` 房貸剩餘本金、`AA11` 長期支出帳戶），對照本系統的計算結果。這是用肉眼確認測試比對的「欄位對位」本身沒有錯。
3. **Excel 開著時也能讀**：用 Excel 打開 xlsm 不要關閉，再執行一次驗收測試，確認不會被檔案鎖擋住。

## 後續（不在本計畫內）

- **P2**：
  - API（MediatR + FluentValidation）與 SQL 端的餘額彙總查詢。
  - Supabase 連線（環境變數、Supavisor session 模式）。
  - Google OIDC 登入加白名單，稽核記錄與軟刪除。
  - 正式的匯入 API 或 CLI（把 xlsm 寫入 Supabase），JSON 備份匯出。
  - 部署到 Cloud Run。
- **鎖帳日**的強制執行。
- 外幣現鈔以外的「實體信封」現金帳戶（Q26 A），目前資料中沒有用到。
- Excel 4–12 月工作表中未付的制式表格列，P1 會照樣匯入成未付的預定支出，但不做驗收（那幾個月沒有流水帳）。

---

## 附錄：核准前事實查核（2026-10-04）

| 引用 | 存在？ | 證據 | 修正 |
|---|---|---|---|
| xUnit v3 4.0.1 + `dotnet test` | ✓（有條件） | spike：未設定時，.NET 10 SDK 會報「VSTest target is no longer supported」 | 加入 `global.json` 的 `test.runner = Microsoft.Testing.Platform`（Task 0） |
| `System.Text.Encoding.CodePages` 套件 | 不需要 | spike 出現 NU1510「會自動提供」 | 從 `Directory.Packages.props` 移除；`CodePagesEncodingProvider` 直接可用 |
| `Assert.Skip` / `Assert.SkipUnless` | ✓ | spike：結果為「已略過」 | — |
| `[assembly: AssemblyFixture]`、`IClassFixture`、`IAsyncLifetime`（ValueTask） | ✓ | spike 編譯並通過 | — |
| `TheoryData<int>` 集合運算式、`MemberData` | ✓ | spike 3 個 case 全部通過 | — |
| `TestContext.Current.TestOutputHelper` / `CancellationToken` | ✓ | spike | — |
| `--filter-class` / `--filter-method` / `--project` | ✓ | spike：總計 1 | — |
| FluentAssertions 8.11.0（使用者 2026-10-04 指定改用 8.x） | ✓ | spike：計畫用到的 `Throw<T>`、`ContainSingle().Which`、`Contain`、`ContainEquivalentOf`、`BeEquivalentTo`、`HaveCount`，以及 xUnit v3 下的失敗回報，7 個測試全部通過；build 與 test 都沒有授權警告。計畫沒有用到 8.x 的破壞性變更（`AssertionScope`、`Execute.Assertion`） | `Directory.Packages.props` 改成 8.11.0 |
| ExcelDataReader 3.9.0 讀 `.xlsm` 快取值 | ✓ | spike：`1月!J6 = -4557`、`AH7` 為 DateTime、`AL7 = 84223`，列與欄對位正確 | — |
| `PostgreSqlBuilder(string image)` + `postgres:17-alpine` | ✓ | spike 容器啟動成功 | — |
| EF Core OwnsMany + 強型別 Id converter + internal 建構子綁定 + record 分錄 + `int` 歸屬月份 | ✓ | spike 往返測試通過（Npgsql 10.0.3） | — |
| `Guid.CreateVersion7`、`GeneratedRegex`（中文樣式） | ✓ | spike | — |
| `dotnet new sln` 產生 `.slnx` | ✓ | spike 產生 `X.slnx` | — |
| dotnet-ef 工具 | ✓ | 全域安裝 10.0.12 | — |
| 各 **Create** 檔案尚不存在 | ✓ | `src/`、`tests/`、`global.json`、`Directory.*.props` 都不存在 | — |
| **Modify** `.gitignore` | ✓ | 存在、CRLF、最後一行 `playwright-report/` 沒有換行；`reference/` 已列入 | Task 0 Step 6 要先補換行（CRLF） |
| spec 引用的儲存格位址 | ✓ | Python 逐一核對：`設定`、`清單`、月表的名稱欄與數值欄全部吻合；流水帳最後一列 ≤ 172 | — |
| 流水帳主選單是否都能對應 | 一筆例外 | 3 月第 20 列「手續費」（銀行的方式選單） | 已加入 spec §5.2 與 Task 11 |
| 貸款本金分攤規則 | ✓ | 2 月：88,131 = 60,000 + 28,131；1 月、3 月各只有一筆制式表格的繳款 | — |
| 測試碼競態 | 無 | 容器由整個組件共用，每個測試各用獨立資料庫；Excel 檔由類別 fixture 唯讀共用 | — |
