# 在 Web 建立帳本與建立者名單（後端）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL：逐 Task 執行時使用 superpowers:test-driven-development；分派子代理時使用 superpowers:subagent-driven-development；inline 執行時使用 superpowers:executing-plans。每個 Task 的流程都是：失敗測試 → 確認失敗原因 → 最小實作 → 全綠 → 一個 commit。

**Goal**：建立者名單（`BookCreators`）裡的人可以用 `POST /api/books` 建立空白帳本並成為擁有者，沒有帳本也能登入；每人最多擁有 5 本，同一位擁有者底下不能同名。`/api/me` 回報能否建立與剩餘名額。CLI 新增 `add-creator`／`remove-creator`。

**Architecture**：Domain 新增 `BookCreator`（身分綁定規則與 `BookMember` 相同）。Infrastructure 新增資料表、migration，以及藏在 `ISixJarsDbContext.AcquireTransactionLockAsync` 後面的 PostgreSQL advisory lock。Application 新增 `CreateBook`（不是 `IBookScoped`，權限在 handler 內檢查，不在名單擲新的 `ForbiddenException` → 403）、CLI 專用的 `AddBookCreator`／`RemoveBookCreator`，並把同名檢查抽成 `BookMembership` 的 helper，讓 `ImportLegacyBook` 共用。`GoogleSignInValidator` 也綁定與比對建立者。

**Tech Stack**：.NET 10、EF Core 10.0.12（Npgsql 10.0.3）、MediatR、FluentValidation、System.CommandLine、xUnit v3、FluentAssertions、Testcontainers（postgres:17-alpine）。

- 設計文件：[`docs/superpowers/specs/2026-10-19-create-book-backend-design.md`](../specs/2026-10-19-create-book-backend-design.md)（Q 編號來自 issue #7，D 編號來自 spec）
- 需求：GitHub issue [AdamChang/SixJars-AccountingBook#7](https://github.com/AdamChang/SixJars-AccountingBook/issues/7)
- 後續計畫：[`2026-10-19-create-book-frontend.md`](2026-10-19-create-book-frontend.md)（本計畫的 checkpoint 全綠之後才能開工）

---

## 執行前必讀

### 環境

- 工作目錄：`F:\VibeCode\SixJars-AccountingBook`（Windows）。分支：`claude/create-book`，從 `claude/last-book-warning`（`5842d76`，#8，尚未合併）開出，**不是 master**。#8 先合併時，本分支 rebase 到 master 即可，兩者沒有重疊的後端檔案。
- 後端全部測試：`dotnet test --solution SixJars.slnx`（需要 Docker Desktop）。
- 單一類別：`dotnet test --project tests/SixJars.Api.Tests --filter-class "*CreateBookEndpointsTests*"`。
- 建置：`dotnet build SixJars.slnx`。
- Migration：`dotnet ef migrations add <Name> --project src/SixJars.Infrastructure --startup-project src/SixJars.Infrastructure --output-dir Persistence/Migrations`。
- **本機若有執行中的 `SixJars.Api`，先停掉**：它會鎖住 `bin` 底下的 DLL，build 會以 MSB3027 失敗（2026-10-19 實際發生過）。

### 基準線

- `dotnet build`：0 個錯誤，**0 個編譯器警告**。60 個 NU1902／NU1903 是既有的 NuGet 弱點警告，與本計畫無關，不要動套件版本。
- `dotnet test`：總計 **549**，失敗 0，略過 0（`ec0ab26` 實測；`5842d76` 只改前端，數字不變）。2026-10-19 寫計畫時重跑被本機的 API 程序擋下，**開工前必須重跑一次**確認。
- 任何時候數字低於基準線，就代表弄壞了東西。各 Task 的「Expected」是**預測**，以實際結果為準，只要只增不減即可。

### 絕對不要碰的檔案

- `.env*`、`reference/`（個資）、`efbundle.exe`、`TestResults/`、`web/`（前端另有計畫）。
- 每個 Task 都用明確路徑執行 `git add`，**禁止 `git add .`／`git add -A`**。

### 慣例

- 註解與 commit 訊息用繁體中文，識別字用英文；commit 結尾加上 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`。
- 每個 use case 一個檔案（record request＋validator＋`internal` handler）。CLI 專用的 request 必須是 `internal` 並實作 `ICliOnlyRequest`（`BookScopeConventionTests` 會檢查）。
- 「今天」用注入的 `TimeProvider`，不要用 `DateTime.Now`。
- TDD：先寫失敗測試，確認它以正確的理由失敗（不是編譯錯誤或打錯字）。每個 Task 至少做一次變異檢查：改壞實作，看到測試變紅，再還原。一個 Task 一個 commit。
- Commit 訊息格式比照 repo：`feat(api): ...`、`feat(cli): ...`、`docs(adr): ...`。

---

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `docs/adr/0011-book-creators-may-create-books.md` | 建立者名單、登入條件、上限、名單異動不稽核 |
| `src/SixJars.Domain/Members/BookCreator.cs` | 建立者名單的一列：正規化 email、綁定 sub |
| `src/SixJars.Infrastructure/Persistence/BookCreatorConfiguration.cs` | `BookCreators` 資料表與索引 |
| `src/SixJars.Infrastructure/Persistence/Migrations/<時間戳>_AddBookCreators.cs`（＋Designer） | migration（由 `dotnet ef` 產生） |
| `src/SixJars.Application/Members/AddBookCreator.cs` | CLI `add-creator` |
| `src/SixJars.Application/Members/RemoveBookCreator.cs` | CLI `remove-creator` |
| `src/SixJars.Application/Common/ForbiddenException.cs` | 403 |
| `src/SixJars.Application/Books/CreateBook.cs` | 建立空白帳本 |
| `tests/SixJars.Domain.Tests/Members/BookCreatorTests.cs` | Domain 規則 |
| `tests/SixJars.Api.Tests/CreateBookEndpointsTests.cs` | `POST /api/books` 與 `/api/me` |

### 修改

| 檔案 | 改動 |
|---|---|
| `docs/adr/0005-bff-cookie-auth-with-book-member-whitelist.md` | 開頭標註「部分被 ADR 0011 取代」 |
| `CONTEXT.md` | 新增「建立者 (Book Creator)」「建立帳本 (Create Book)」 |
| `src/SixJars.Application/Common/ISixJarsDbContext.cs` | `BookCreators`、`AcquireTransactionLockAsync` |
| `src/SixJars.Infrastructure/Persistence/SixJarsDbContext.cs` | 同上的實作 |
| `src/SixJars.Cli/CliApp.cs` | `add-creator`、`remove-creator` 子命令 |
| `src/SixJars.Api/Infrastructure/GoogleSignInValidator.cs` | 綁定與比對建立者 |
| `src/SixJars.Application/Common/BookMembership.cs` | `OwnedBookIdsByEmail`、`OwnerHasBookNamedAsync` |
| `src/SixJars.Application/LegacyImport/ImportLegacyBook.cs` | 同名檢查改為同一位擁有者 |
| `src/SixJars.Domain/Common/DomainException.cs` | `DuplicateNameCode`、`BookQuotaExceededCode` |
| `src/SixJars.Api/Infrastructure/ApiExceptionHandler.cs` | `ForbiddenException` → 403 |
| `src/SixJars.Api/Endpoints/BooksEndpoints.cs` | `POST /books` |
| `src/SixJars.Application/Members/MeDto.cs`、`GetMe.cs` | `CanCreateBooks`、`RemainingBookQuota` |
| `tests/Shared/ApiSeed.cs` | `AddCreatorAsync` |
| `tests/SixJars.Cli.Tests/CliAppTests.cs` | 名單指令的測試 |
| `tests/SixJars.Api.Tests/Auth/GoogleSignInValidatorTests.cs` | 建立者登入的測試 |
| `tests/SixJars.Infrastructure.Tests/LegacyExcel/ImportLegacyBookTests.cs` | 同名規則的測試 |

### Task 相依順序

```
Task 1 (ADR/CONTEXT) ── 獨立，可最後做
Task 2 (BookCreator Domain) ─→ Task 3 (資料表＋CLI) ─┬─→ Task 4 (登入)
                                                    └─→ Task 6 (CreateBook) ─→ Task 7 (/me)
Task 5 (同名 helper＋import) ───────────────────────────────┘
```

Task 4 與 Task 6 都依賴 Task 3 的資料表，彼此獨立；Task 6 依賴 Task 5 的 helper；Task 7 依賴 Task 6 的 `MaxOwnedBooks`。

---

## Task 1：ADR 0011 與 CONTEXT.md

**Files:** Create: `docs/adr/0011-book-creators-may-create-books.md` / Modify: `docs/adr/0005-bff-cookie-auth-with-book-member-whitelist.md`、`CONTEXT.md`

ADR 0005 有兩個前提被推翻：「白名單就是帳本成員表」與「第一位擁有者只能由 CLI 建立」。保留 0005 原文、另寫 0011，是為了讓當初的理由仍可追溯（Q11）。文件 Task 沒有測試，以「內容涵蓋 spec 決策表的 A1、Q5、Q7／Q25、Q8、Q24」為完成條件。

- [ ] Step 1：寫 ADR 0011，格式比照 `0010-delete-book-is-hard-delete.md`（標題、內文、`## Considered Options`、`## Consequences`）：

```markdown
# 建立者名單：名單裡的人可以在 Web 建立帳本，沒有帳本也能登入

ADR 0005 的白名單就是帳本成員表，第一位擁有者只能由 CLI 建立。這讓 Web 沒有建立帳本的入口，刪掉最後一本帳本的人也再也登不進來（issue #7）。

新增一份建立者名單（`BookCreators`），由 CLI `add-creator`／`remove-creator` 維護。登入條件改為「是帳本成員，**或**在建立者名單裡」。建立者首次登入時同樣以已驗證的 email 綁定 Google `sub`；已綁定其他 sub 的列不比對，規則與成員相同。只有建立者能在 Web 建立帳本，建立者自動成為擁有者；每人最多擁有 5 本（刪除後名額釋出），CLI 不受此限。同一位擁有者底下的帳本不能同名。

名單異動不留稽核記錄，是 ADR 0006 的例外：名單不屬於任何帳本（`AuditEntry.BookId` 是必填），而且只有能直接連資料庫的管理者能執行。建立者之後建立的帳本仍會留 Create 記錄。

## Considered Options

- **任何帳本成員都能建立帳本**：被加進家庭帳本的成員都能無限開帳本，名單形同虛設；上限也沒有明確的計算對象。否決。
- **開放任何 Google 帳號建立**：等於開放註冊，超出個人／家庭記帳本的範圍。否決。
- **名單放在環境變數**：ADR 0005 已否決（改名單要重新部署）。
- **在 Web 維護名單**：需要系統管理者角色，範圍過大。否決。
- **名單異動寫進現有稽核表（`BookId` 改 nullable）或另開系統稽核表**：為了很少發生的 CLI 操作，改動以帳本為中心的稽核模型。否決。

## Consequences

- ADR 0005 的「白名單即成員表」「第一位擁有者只能由 CLI 建立」由本 ADR 取代；cookie、OIDC、非成員 404 等其餘內容不變。
- 從名單移除建立者不影響他已建立的帳本，也不會立即撤銷既有 cookie。
- CLI `import-legacy` 的同名檢查改為只比對 `--owner-email` 擁有的帳本：不同擁有者現在可以匯入同名帳本。
```

- [ ] Step 2：ADR 0005 標題下一行加上：

```markdown
> 部分被 [ADR 0011](0011-book-creators-may-create-books.md) 取代：白名單不再只是成員表，第一位擁有者也可以由建立者在 Web 建立。
```

- [ ] Step 3：`CONTEXT.md` 在「刪除帳本 (Delete Book)」之後新增兩個詞條（寫法比照該詞條）：
  - **建立者 (Book Creator)**：在建立者名單裡、可以在 Web 建立帳本的人；沒有帳本也能登入。由 CLI `add-creator`／`remove-creator` 維護（ADR 0011）。
  - **建立帳本 (Create Book)**：建立者建立一本空白帳本，自動成為擁有者。每人最多擁有 5 本，同一位擁有者底下不能同名。

- [ ] Step 4：Commit

```powershell
git add docs/adr/0011-book-creators-may-create-books.md docs/adr/0005-bff-cookie-auth-with-book-member-whitelist.md CONTEXT.md
git commit -m "docs(adr): 建立者名單可以在 Web 建立帳本（ADR 0011，部分取代 ADR 0005）" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 2：Domain `BookCreator`

**Files:** Create: `src/SixJars.Domain/Members/BookCreator.cs`、`tests/SixJars.Domain.Tests/Members/BookCreatorTests.cs`

綁定規則必須和 `BookMember.BindSubject` 一字不差：同一個 sub 可以重複綁定（登入多次），不同 sub 擲例外。容易錯的是 email 正規化：一定要呼叫 `BookMember.NormalizeEmail`，不要另寫一份，否則名單與成員表的 email 會在大小寫上對不起來。

- [ ] Step 1：寫失敗測試 `tests/SixJars.Domain.Tests/Members/BookCreatorTests.cs`

```csharp
using FluentAssertions;
using SixJars.Domain.Common;
using SixJars.Domain.Members;
using Xunit;

namespace SixJars.Domain.Tests.Members;

/// <summary>建立者名單（ADR 0011）：email 正規化與 sub 綁定的規則與 <see cref="BookMember"/> 相同。</summary>
public class BookCreatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalizes_email_and_starts_unbound()
    {
        var creator = BookCreator.Create(" Adam@Example.com ", Now);

        creator.Email.Should().Be("adam@example.com");
        creator.GoogleSubject.Should().BeNull();
        creator.AddedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_email_is_rejected(string email)
    {
        var act = () => BookCreator.Create(email, Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Binding_the_same_subject_again_is_allowed()
    {
        var creator = BookCreator.Create("adam@example.com", Now);
        creator.BindSubject("sub-1");

        creator.BindSubject("sub-1");

        creator.GoogleSubject.Should().Be("sub-1");
    }

    [Fact]
    public void Binding_another_subject_is_rejected()
    {
        var creator = BookCreator.Create("adam@example.com", Now);
        creator.BindSubject("sub-1");

        var act = () => creator.BindSubject("sub-2");

        act.Should().Throw<DomainException>().WithMessage("*adam@example.com*");
        creator.GoogleSubject.Should().Be("sub-1");
    }
}
```

- [ ] Step 2：`dotnet test --project tests/SixJars.Domain.Tests --filter-class "*BookCreatorTests*"`。Expected：**編譯失敗**，`BookCreator` 不存在。這是唯一可接受的「失敗原因」；若是其他編譯錯誤，先修測試。

- [ ] Step 3：實作 `src/SixJars.Domain/Members/BookCreator.cs`

```csharp
using SixJars.Domain.Common;

namespace SixJars.Domain.Members;

/// <summary>
/// 建立者名單的一列（ADR 0011）：可以在 Web 建立帳本，沒有任何帳本也能登入。由 CLI <c>add-creator</c>／<c>remove-creator</c> 維護。
/// email 正規化與 sub 綁定的規則與 <see cref="BookMember"/> 相同。
/// </summary>
public sealed class BookCreator
{
    // 參數名稱必須與屬性名稱一致，EF Core 的建構子綁定依賴這一點。
    private BookCreator(string email, DateTimeOffset addedAt)
    {
        Id = Guid.CreateVersion7();
        Email = email;
        AddedAt = addedAt;
    }

    public Guid Id { get; private set; }
    /// <summary>正規化後的 email；首次登入時以此比對已驗證的 Google email。</summary>
    public string Email { get; private set; }
    /// <summary>Google 的 sub；首次登入時綁定。null 表示尚未登入過。</summary>
    public string? GoogleSubject { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }

    public static BookCreator Create(string email, DateTimeOffset addedAt) => new(BookMember.NormalizeEmail(email), addedAt);

    /// <summary>首次登入時綁定 Google sub；已綁定時必須是同一個 sub（理由同 <see cref="BookMember.BindSubject"/>）。</summary>
    public void BindSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new DomainException("Google sub 不可為空白。");
        }

        if (GoogleSubject is not null && GoogleSubject != subject)
        {
            throw new DomainException($"建立者 {Email} 已綁定其他 Google 帳號。");
        }

        GoogleSubject = subject;
    }
}
```

- [ ] Step 4：單檔測試。Expected：5 passed。變異檢查：把 `GoogleSubject != subject` 改成 `false`，`Binding_another_subject_is_rejected` 必須變紅，再還原。
- [ ] Step 5：全部測試。Expected：總計約 **554**（549＋5），失敗 0。
- [ ] Step 6：Commit

```powershell
git add src/SixJars.Domain/Members/BookCreator.cs tests/SixJars.Domain.Tests/Members/BookCreatorTests.cs
git commit -m "feat(domain): 建立者名單 BookCreator（email 正規化與 sub 綁定同 BookMember）" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 3：`BookCreators` 資料表與 CLI `add-creator`／`remove-creator`

**Files:** Create: `BookCreatorConfiguration.cs`、migration、`AddBookCreator.cs`、`RemoveBookCreator.cs` / Modify: `ISixJarsDbContext.cs`、`SixJarsDbContext.cs`、`CliApp.cs`、`CliAppTests.cs`

CLI 是名單唯一的寫入途徑，所以資料表和 CLI 放同一個 Task：沒有 CLI 就無法從外部驗證資料表。容易錯的地方：重複加入要在 handler 先查一次並擲 `DomainException`，不能只靠唯一索引（否則 CLI 會印出原始的 `DbUpdateException`，見既有的 `Add_member_twice_fails_with_clear_message`）；名單異動**不留稽核記錄**（Q24），測試要斷言稽核表是空的，防止日後有人「好心」補上卻寫進錯的 `BookId`。

- [ ] Step 1：在 `tests/SixJars.Cli.Tests/CliAppTests.cs` 的 `Add_member_twice_fails_with_clear_message` 之後加入：

```csharp
    [Fact]
    public async Task Add_creator_creates_unbound_entry_without_audit()
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        var output = new StringWriter();

        var exitCode = await RunAsync(connectionString, output, "add-creator", "--email", " Creator@Example.com ");

        exitCode.Should().Be(0, output.ToString());
        await using var db = Open(connectionString);
        var creator = await db.BookCreators.SingleAsync(Ct);
        creator.Email.Should().Be("creator@example.com");
        creator.GoogleSubject.Should().BeNull();
        creator.AddedAt.Should().Be(Now);
        // ADR 0011：名單不屬於任何帳本，異動不留稽核記錄（Q24）。
        (await db.AuditEntries.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Add_creator_twice_fails_with_clear_message()
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        (await RunAsync(connectionString, new StringWriter(), "add-creator", "--email", "creator@example.com")).Should().Be(0);
        var output = new StringWriter();

        var exitCode = await RunAsync(connectionString, output, "add-creator", "--email", "CREATOR@example.com");

        exitCode.Should().Be(1);
        output.ToString().Should().Contain("creator@example.com").And.Contain("已在").And.NotContain("   at ");
        await using var db = Open(connectionString);
        (await db.BookCreators.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Remove_creator_deletes_the_entry()
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        (await RunAsync(connectionString, new StringWriter(), "add-creator", "--email", "creator@example.com")).Should().Be(0);
        var output = new StringWriter();

        var exitCode = await RunAsync(connectionString, output, "remove-creator", "--email", " Creator@Example.com ");

        exitCode.Should().Be(0, output.ToString());
        await using var db = Open(connectionString);
        (await db.BookCreators.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Remove_unknown_creator_fails_with_clear_message()
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        var output = new StringWriter();

        var exitCode = await RunAsync(connectionString, output, "remove-creator", "--email", "nobody@example.com");

        exitCode.Should().Be(1);
        output.ToString().Should().Contain("nobody@example.com").And.Contain("不在");
    }
```

- [ ] Step 2：`dotnet test --project tests/SixJars.Cli.Tests --filter-method "*creator*"`。Expected：**編譯失敗**，`SixJarsDbContext.BookCreators` 不存在。
- [ ] Step 3：最小實作
  - `ISixJarsDbContext` 在 `BookMembers` 之後加：

    ```csharp
        /// <summary>建立者名單（ADR 0011）；由 CLI 維護，異動不留稽核記錄。</summary>
        DbSet<BookCreator> BookCreators { get; }
    ```

  - `SixJarsDbContext` 加 `public DbSet<BookCreator> BookCreators => Set<BookCreator>();`
  - `src/SixJars.Infrastructure/Persistence/BookCreatorConfiguration.cs`：

    ```csharp
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using SixJars.Domain.Members;

    namespace SixJars.Infrastructure.Persistence;

    internal sealed class BookCreatorConfiguration : IEntityTypeConfiguration<BookCreator>
    {
        public void Configure(EntityTypeBuilder<BookCreator> builder)
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Id).ValueGeneratedNever();
            // 與 BookMemberConfiguration 一致：RFC 5321 的 email 上限、sub 255。
            builder.Property(c => c.Email).HasMaxLength(320);
            builder.Property(c => c.GoogleSubject).HasMaxLength(255);
            // 同一個 email 只出現一次（email 已在 Domain 正規化）。
            builder.HasIndex(c => c.Email).IsUnique();
            // 登入與 /api/me 以登入者的 sub 查詢。
            builder.HasIndex(c => c.GoogleSubject);
        }
    }
    ```

  - 產生 migration：`dotnet ef migrations add AddBookCreators --project src/SixJars.Infrastructure --startup-project src/SixJars.Infrastructure --output-dir Persistence/Migrations`。**檢查產生的 `Up`**：只能有 `CreateTable("BookCreators")` 與兩個 `CreateIndex`，有其他變動就停下來回報。
  - `src/SixJars.Application/Members/AddBookCreator.cs`：

    ```csharp
    using FluentValidation;
    using MediatR;
    using Microsoft.EntityFrameworkCore;
    using SixJars.Application.Common;
    using SixJars.Domain.Common;
    using SixJars.Domain.Members;

    namespace SixJars.Application.Members;

    /// <summary>
    /// 把 email 加進建立者名單（CLI <c>add-creator</c>，ADR 0011），回傳 Id。尚未綁定 sub，第一次以這個 email 登入時才綁定。
    /// 名單不屬於任何帳本，不留稽核記錄（issue #7 Q24）。CLI 專用，必須保持 internal。
    /// </summary>
    internal sealed record AddBookCreator(string Email) : IRequest<Guid>, ICliOnlyRequest;

    internal sealed class AddBookCreatorValidator : AbstractValidator<AddBookCreator>
    {
        public AddBookCreatorValidator() => RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(320);
    }

    internal sealed class AddBookCreatorHandler(ISixJarsDbContext db, TimeProvider clock) : IRequestHandler<AddBookCreator, Guid>
    {
        public async Task<Guid> Handle(AddBookCreator request, CancellationToken cancellationToken)
        {
            var creator = BookCreator.Create(request.Email, clock.GetUtcNow());
            // 先查一次，給出看得懂的訊息；Email 的唯一索引仍是最後一道防線。
            if (await db.BookCreators.AnyAsync(c => c.Email == creator.Email, cancellationToken))
            {
                throw new DomainException($"{creator.Email} 已在建立者名單。");
            }

            db.BookCreators.Add(creator);
            await db.SaveChangesAsync(cancellationToken);
            return creator.Id;
        }
    }
    ```

  - `src/SixJars.Application/Members/RemoveBookCreator.cs`：

    ```csharp
    using MediatR;
    using Microsoft.EntityFrameworkCore;
    using SixJars.Application.Common;
    using SixJars.Domain.Members;

    namespace SixJars.Application.Members;

    /// <summary>
    /// 從建立者名單移除 email（CLI <c>remove-creator</c>，ADR 0011）。已建立的帳本不受影響，既有 cookie 也不會立即失效（issue #7 Q20）。
    /// 不留稽核記錄（Q24）。CLI 專用，必須保持 internal。
    /// </summary>
    internal sealed record RemoveBookCreator(string Email) : IRequest, ICliOnlyRequest;

    internal sealed class RemoveBookCreatorHandler(ISixJarsDbContext db) : IRequestHandler<RemoveBookCreator>
    {
        public async Task Handle(RemoveBookCreator request, CancellationToken cancellationToken)
        {
            var email = BookMember.NormalizeEmail(request.Email);
            var creator = await db.BookCreators.SingleOrDefaultAsync(c => c.Email == email, cancellationToken)
                ?? throw new NotFoundException($"{email} 不在建立者名單。");
            db.BookCreators.Remove(creator);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
    ```

  - `CliApp.cs`：`RootCommand` 加上 `AddCreatorCommand(host)`、`RemoveCreatorCommand(host)`，寫法比照 `AddMemberCommand`：

    ```csharp
    private static Command AddCreatorCommand(Host host)
    {
        var email = new Option<string>("--email") { Description = "建立者的 Google email", Required = true };
        var command = new Command("add-creator", "把 email 加進建立者名單：可以在 Web 建立帳本，沒有帳本也能登入（ADR 0011）。") { email };
        command.SetAction((parseResult, ct) => host.RunAsync(async (sender, output) =>
        {
            var id = await sender.Send(new AddBookCreator(parseResult.GetValue(email)!), ct);
            await output.WriteLineAsync($"已加入建立者名單（Id {id}）。");
            return 0;
        }, ct));
        return command;
    }

    private static Command RemoveCreatorCommand(Host host)
    {
        var email = new Option<string>("--email") { Description = "建立者的 Google email", Required = true };
        var command = new Command("remove-creator", "從建立者名單移除 email；已建立的帳本不受影響。") { email };
        command.SetAction((parseResult, ct) => host.RunAsync(async (sender, output) =>
        {
            await sender.Send(new RemoveBookCreator(parseResult.GetValue(email)!), ct);
            await output.WriteLineAsync("已從建立者名單移除。");
            return 0;
        }, ct));
        return command;
    }
    ```

- [ ] Step 4：`dotnet test --project tests/SixJars.Cli.Tests`。Expected：新增 4 個全綠。變異檢查：拿掉 `AddBookCreatorHandler` 的 `AnyAsync` 檢查，`Add_creator_twice_fails_with_clear_message` 必須變紅（輸出會是 `DbUpdateException`），再還原。
- [ ] Step 5：全部測試。Expected：總計約 **558**，失敗 0。`BookScopeConventionTests` 必須仍綠（兩個新 request 都是 internal 的 `ICliOnlyRequest`）。
- [ ] Step 6：Commit（migration 的兩個檔名以實際產生的為準）

```powershell
git add src/SixJars.Application/Common/ISixJarsDbContext.cs src/SixJars.Infrastructure/Persistence/SixJarsDbContext.cs src/SixJars.Infrastructure/Persistence/BookCreatorConfiguration.cs src/SixJars.Infrastructure/Persistence/Migrations/<時間戳>_AddBookCreators.cs src/SixJars.Infrastructure/Persistence/Migrations/<時間戳>_AddBookCreators.Designer.cs src/SixJars.Infrastructure/Persistence/Migrations/SixJarsDbContextModelSnapshot.cs src/SixJars.Application/Members/AddBookCreator.cs src/SixJars.Application/Members/RemoveBookCreator.cs src/SixJars.Cli/CliApp.cs tests/SixJars.Cli.Tests/CliAppTests.cs
git commit -m "feat(cli): 建立者名單資料表與 add-creator／remove-creator（異動不留稽核記錄）" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 4：登入時比對與綁定建立者

**Files:** Modify: `src/SixJars.Api/Infrastructure/GoogleSignInValidator.cs`、`tests/SixJars.Api.Tests/Auth/GoogleSignInValidatorTests.cs`

這是 ADR 0005 被取代的核心。容易錯的地方：已綁定其他 sub 的建立者必須**不比對、不擲例外**（Q25），否則 OIDC callback 會變成 500；綁定建立者不留稽核記錄（成員綁定有留，兩者刻意不同，註解要寫清楚）。

- [ ] Step 1：在 `GoogleSignInValidatorTests` 加入 helper 與三個測試（helper 放在既有的 `AddMemberAsync` 旁邊；`using SixJars.Domain.Members;` 已存在）：

```csharp
    [Fact]
    public async Task Creator_without_books_signs_in_and_binds_subject()
    {
        await using var factory = await CreateFactoryAsync();
        var creator = await AddCreatorAsync(factory, Email);

        var accepted = await ValidateAsync(factory, Principal(Subject, Email, "true"));

        accepted.Should().BeTrue("ADR 0011：建立者沒有帳本也能登入");
        (await ReloadCreatorAsync(factory, creator.Id)).GoogleSubject.Should().Be(Subject);
        (await EntriesAsync(factory)).Should().BeEmpty("名單不屬於任何帳本，綁定不留稽核記錄（Q24）");
    }

    [Fact]
    public async Task Bound_creator_signs_in_even_if_email_changed()
    {
        await using var factory = await CreateFactoryAsync();
        await AddCreatorAsync(factory, Email, Subject);

        var accepted = await ValidateAsync(factory, Principal(Subject, "renamed@example.com", "true"));

        accepted.Should().BeTrue();
    }

    [Fact]
    public async Task Creator_bound_to_another_subject_is_not_rebound()
    {
        await using var factory = await CreateFactoryAsync();
        var creator = await AddCreatorAsync(factory, Email, "another-sub");

        var accepted = await ValidateAsync(factory, Principal(Subject, Email, "true"));

        // Q25：與成員相同，換了 Google 帳號不能沿用 email；不擲例外，callback 才不會變成 500。
        accepted.Should().BeFalse();
        (await ReloadCreatorAsync(factory, creator.Id)).GoogleSubject.Should().Be("another-sub");
    }

    private static async Task<BookCreator> AddCreatorAsync(ApiFactory factory, string email, string? subject = null)
    {
        var creator = BookCreator.Create(email, Now.AddDays(-1));
        if (subject is not null)
        {
            creator.BindSubject(subject);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        db.BookCreators.Add(creator);
        await db.SaveChangesAsync(Ct);
        return creator;
    }

    private static async Task<BookCreator> ReloadCreatorAsync(ApiFactory factory, Guid creatorId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SixJarsDbContext>().BookCreators.AsNoTracking()
            .SingleAsync(c => c.Id == creatorId, Ct);
    }
```

- [ ] Step 2：`dotnet test --project tests/SixJars.Api.Tests --filter-class "*GoogleSignInValidatorTests*"`。Expected：`Creator_without_books_signs_in_and_binds_subject` 與 `Bound_creator_signs_in_even_if_email_changed` 失敗（`accepted` 為 false）；`Creator_bound_to_another_subject_is_not_rebound` 此時會**通過**，因為目前根本不看名單，這是預期的，它守的是實作之後的行為。
- [ ] Step 3：實作 `GoogleSignInValidator`
  - `ValidateAsync` 在 `BindUnboundMembersAsync` 之後加上 `await BindUnboundCreatorsAsync(normalizedEmail, subject, cancellationToken);`（`BookMember.NormalizeEmail(email)` 抽成區域變數，兩處共用）。
  - 最後的判斷改為：

    ```csharp
        // ADR 0011：成員或建立者都能登入。
        return await db.BookMembers.AnyAsync(m => m.GoogleSubject == subject, cancellationToken)
            || await db.BookCreators.AnyAsync(c => c.GoogleSubject == subject, cancellationToken);
    ```

  - 新增方法：

    ```csharp
    /// <summary>
    /// 與 <see cref="BindUnboundMembersAsync"/> 相同的綁定規則（已綁定其他 sub 的列不比對），但不留稽核記錄：
    /// 名單不屬於任何帳本，異動只由 CLI 執行（ADR 0011、issue #7 Q24）。
    /// </summary>
    private async Task BindUnboundCreatorsAsync(string email, string subject, CancellationToken cancellationToken)
    {
        var unbound = await db.BookCreators
            .Where(c => c.Email == email && c.GoogleSubject == null)
            .ToListAsync(cancellationToken);
        if (unbound.Count == 0)
        {
            return;
        }

        foreach (var creator in unbound)
        {
            creator.BindSubject(subject);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
    ```

  - 更新類別與 `ValidateAsync` 的 `<summary>`，第 3 點改為「最後只要有任何成員**或建立者**綁定這個 sub 就通過」。
- [ ] Step 4：單檔測試全綠。變異檢查：把 `|| await db.BookCreators...` 拿掉，前兩個新測試必須變紅；把 `BindUnboundCreatorsAsync` 的 `GoogleSubject == null` 條件拿掉，`Creator_bound_to_another_subject_is_not_rebound` 必須變紅（`BindSubject` 擲 `DomainException`）。各自還原。
- [ ] Step 5：全部測試。Expected：總計約 **561**，失敗 0。
- [ ] Step 6：Commit

```powershell
git add src/SixJars.Api/Infrastructure/GoogleSignInValidator.cs tests/SixJars.Api.Tests/Auth/GoogleSignInValidatorTests.cs
git commit -m "feat(api): 建立者沒有帳本也能登入，首次登入以 email 綁定 sub（ADR 0011）" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 5：同名檢查改為「同一位擁有者」

**Files:** Modify: `src/SixJars.Application/Common/BookMembership.cs`、`src/SixJars.Application/LegacyImport/ImportLegacyBook.cs`、`src/SixJars.Domain/Common/DomainException.cs`、`tests/SixJars.Infrastructure.Tests/LegacyExcel/ImportLegacyBookTests.cs`

這是**既有行為的變更**：CLI `import-legacy` 原本是全系統比對（`ImportLegacyBook.cs:50`）。容易錯的地方：既有的 `Duplicate_book_name_is_rejected` seed 的帳本**沒有成員**，改規則後它會變成「不同擁有者」而開始匯入成功；必須改寫成 seed 同一個 email 的擁有者，不能只是把斷言改掉。owner 的建立要搬到同名檢查之前，才拿得到正規化後的 email。

- [ ] Step 1：改寫測試
  - 把 `Duplicate_book_name_is_rejected` 改名為 `Duplicate_book_name_of_the_same_owner_is_rejected`，seed 改為同時加入擁有者，並斷言錯誤碼：

    ```csharp
    await using (var db = host.Open())
    {
        var existing = new Book(BookName, new DateOnly(2025, 12, 31));
        db.Books.Add(existing);
        // 大小寫與空白不同仍是同一位擁有者（Import() 的 email 是 " Owner@Example.com "）。
        db.BookMembers.Add(BookMember.Create(existing.Id, "owner@example.com", BookRole.Owner, Now));
        await db.SaveChangesAsync(Ct);
    }

    var act = () => host.SendAsync(Import(ValidWorkbook()));

    var error = (await act.Should().ThrowAsync<DomainException>()).Which;
    error.Message.Should().Contain(BookName);
    error.Code.Should().Be(DomainException.DuplicateNameCode);
    ```

    原本的其餘斷言保留，但 `BookMembers` 的計數改為 1（seed 的那一位）。
  - 新增：

    ```csharp
    [Fact]
    public async Task Same_name_owned_by_someone_else_is_imported()
    {
        await using var host = await Host.CreateAsync(postgres);
        await using (var db = host.Open())
        {
            var existing = new Book(BookName, new DateOnly(2025, 12, 31));
            db.Books.Add(existing);
            db.BookMembers.Add(BookMember.Create(existing.Id, "someone-else@example.com", BookRole.Owner, Now));
            await db.SaveChangesAsync(Ct);
        }

        var result = await host.SendAsync(Import(ValidWorkbook()));

        // issue #7 Q9：同名檢查只比對同一位擁有者的帳本。
        result.BookId.Should().NotBeNull();
        await using var check = host.Open();
        (await check.Books.CountAsync(b => b.Name == BookName, Ct)).Should().Be(2);
    }
    ```

- [ ] Step 2：`dotnet test --project tests/SixJars.Infrastructure.Tests --filter-class "*ImportLegacyBookTests*"`。Expected：**編譯失敗**，`DomainException.DuplicateNameCode` 不存在。先只加上常數（Step 3 的第一項）再跑一次：`Same_name_owned_by_someone_else_is_imported` 失敗（擲出「已有名稱為…」），`Duplicate_book_name_of_the_same_owner_is_rejected` 失敗在 `Code` 是 `rule`。這兩個才是正確的失敗原因。
- [ ] Step 3：實作
  - `DomainException` 新增：

    ```csharp
    /// <summary>同一位擁有者底下已有同名帳本（issue #7 Q9）。</summary>
    public const string DuplicateNameCode = "duplicate-name";

    /// <summary>已擁有的帳本數達到上限（issue #7 Q8）。</summary>
    public const string BookQuotaExceededCode = "book-quota-exceeded";
    ```

  - `BookMembership.cs` 檔頭補上 `using Microsoft.EntityFrameworkCore;`（現在只有 `SixJars.Domain.Common`／`SixJars.Domain.Members`，`AnyAsync` 是 EF Core 的擴充方法，少了會編譯失敗），並新增：

    ```csharp
    /// <summary>以 email 認定的擁有者擁有的帳本（不論是否已綁定 sub）；CLI 只拿得到 email（issue #7 Q26）。</summary>
    public static IQueryable<BookId> OwnedBookIdsByEmail(this ISixJarsDbContext db, string normalizedEmail) =>
        db.BookMembers
            .Where(m => m.Email == normalizedEmail && m.Role == BookRole.Owner)
            .Select(m => m.BookId);

    /// <summary>
    /// 同一位擁有者底下是否已有這個名稱（issue #7 Q9）。<paramref name="name"/> 傳 <c>Book.Name</c>（已 trim），以 ordinal 比對。
    /// Web 傳 <see cref="OwnedBookIds"/>（以 sub 認人），CLI 傳 <see cref="OwnedBookIdsByEmail"/>。
    /// </summary>
    public static Task<bool> OwnerHasBookNamedAsync(
        this ISixJarsDbContext db, IQueryable<BookId> ownedBookIds, string name, CancellationToken cancellationToken) =>
        db.Books.AnyAsync(b => ownedBookIds.Contains(b.Id) && b.Name == name, cancellationToken);
    ```

  - `ImportLegacyBook`：把 `var owner = BookMember.Create(...)` 搬到第 2 步之前（註解一起搬），第 2 步改為：

    ```csharp
        // 2. 同一位擁有者已有同名帳本：拒絕，避免重複匯入（issue #7 Q9：不同擁有者可以同名）。
        if (await db.OwnerHasBookNamedAsync(db.OwnedBookIdsByEmail(owner.Email), book.Name, cancellationToken))
        {
            throw new DomainException($"{owner.Email} 已有名稱為「{book.Name}」的帳本，不重複匯入。", DomainException.DuplicateNameCode);
        }
    ```

  - `CliApp.cs` 的 `--book-name` 說明改為「新帳本的名稱；同一位擁有者已有同名帳本時拒絕匯入」。
- [ ] Step 4：單檔測試全綠。變異檢查：把 helper 的 `ownedBookIds.Contains(b.Id) &&` 拿掉（退回全系統比對），`Same_name_owned_by_someone_else_is_imported` 必須變紅，再還原。
- [ ] Step 5：全部測試。Expected：總計約 **562**，失敗 0。Acceptance 的 `CliImportAcceptanceTests` 若有同名情境，要確認它仍然成立（`reference/` 不存在時會 skip，回報時要說明）。
- [ ] Step 6：Commit

```powershell
git add src/SixJars.Domain/Common/DomainException.cs src/SixJars.Application/Common/BookMembership.cs src/SixJars.Application/LegacyImport/ImportLegacyBook.cs src/SixJars.Cli/CliApp.cs tests/SixJars.Infrastructure.Tests/LegacyExcel/ImportLegacyBookTests.cs
git commit -m "feat(api): 同名檢查改為只比對同一位擁有者的帳本，CLI 匯入一併套用" -m "不同擁有者現在可以匯入同名帳本（issue #7 Q9／Q26）；錯誤碼改為 duplicate-name。" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 6：`POST /api/books` 建立空白帳本

**Files:** Create: `src/SixJars.Application/Books/CreateBook.cs`、`src/SixJars.Application/Common/ForbiddenException.cs`、`tests/SixJars.Api.Tests/CreateBookEndpointsTests.cs` / Modify: `ISixJarsDbContext.cs`、`SixJarsDbContext.cs`、`ApiExceptionHandler.cs`、`BooksEndpoints.cs`、`tests/Shared/ApiSeed.cs`

整個 issue 的主體。容易錯的地方：
1. **順序**（spec D6）：鎖一定要在任何計數之前；建立者檢查在上限之前。
2. advisory lock 只在 transaction 內有效（`pg_advisory_xact_lock` 在 commit／rollback 時釋放），所以 `BeginTransactionAsync` 必須在鎖之前。
3. 擁有者成員要**已綁定 sub**（`BindSubject(user.Subject)`），否則 `/api/me` 列不出新帳本，`BookAccessBehavior` 也會擋。
4. 同時送出的測試在沒有鎖時**不保證**失敗（兩個請求不一定剛好交錯），所以拿掉鎖的變異檢查「沒變紅」不代表鎖沒作用。改用另一個變異（把上限檢查移到鎖之前）也是同樣的問題。這個測試的價值是：有鎖時它必須**每次**都綠。

- [ ] Step 1：先在 `tests/Shared/ApiSeed.cs` 加入（`using SixJars.Domain.Members;` 已存在）：

```csharp
    /// <summary>把 <paramref name="subject"/> 加進建立者名單（已綁定 sub，email 與 <see cref="TestAuthHandler"/> 一致；ADR 0011）。</summary>
    public static async Task AddCreatorAsync(this ApiFactory factory, string subject, CancellationToken cancellationToken)
    {
        var creator = BookCreator.Create(TestAuthHandler.EmailOf(subject), MemberAddedAt);
        creator.BindSubject(subject);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        db.BookCreators.Add(creator);
        await db.SaveChangesAsync(cancellationToken);
    }
```

  然後寫失敗測試 `tests/SixJars.Api.Tests/CreateBookEndpointsTests.cs`：

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Auditing;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Members;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

/// <summary>建立帳本（issue #7、ADR 0011）：建立者建立空白帳本並成為擁有者；每人最多 5 本，同一位擁有者不能同名。</summary>
public class CreateBookEndpointsTests(PostgresFixture postgres)
{
    private const string Creator = "creator-sub";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creator_creates_an_empty_book_and_becomes_its_bound_owner()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        var client = await factory.CreateMemberClientAsync(Creator);

        var response = await CreateAsync(client, " 家庭帳本 ", "2026-10-19");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await ReadIdAsync(response);
        response.Headers.Location!.OriginalString.Should().Be($"/api/books/{id}");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        var book = await db.Books.AsNoTracking().SingleAsync(b => b.Id == new BookId(id), Ct);
        (book.Name, book.OpeningDate).Should().Be(("家庭帳本", new DateOnly(2026, 10, 19)));
        book.Accounts.Should().BeEmpty("Q13：空白帳本，帳戶由使用者在設定頁新增");
        var owner = await db.BookMembers.AsNoTracking().SingleAsync(m => m.BookId == new BookId(id), Ct);
        (owner.Email, owner.GoogleSubject, owner.Role).Should().Be((TestAuthHandler.EmailOf(Creator), Creator, BookRole.Owner));
    }

    [Fact]
    public async Task Create_is_audited_with_name_and_opening_date()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        var client = await factory.CreateMemberClientAsync(Creator);

        var id = await ReadIdAsync(await CreateAsync(client, "家庭帳本", "2026-10-19"));

        var entry = (await AuditTrailTests.EntriesAsync(factory)).Should().ContainSingle().Subject;
        (entry.BookId, entry.Action, entry.EntityType, entry.EntityId, entry.ActorSubject)
            .Should().Be((id, AuditAction.Create, AuditEntityTypes.Book, id, Creator));
        entry.Before.Should().BeNull();
        entry.After.Should().Contain("家庭帳本").And.Contain("2026-10-19");
    }

    [Fact]
    public async Task Member_who_is_not_a_creator_gets_403()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();

        var response = await CreateAsync(client, "家庭帳本", "2026-10-19");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BookCountAsync(factory)).Should().Be(1);
    }

    [Fact]
    public async Task Sixth_owned_book_is_rejected()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        for (var i = 0; i < 5; i++)
        {
            await factory.SeedBookAsync(Ct, ownerSubject: Creator);
        }

        var response = await CreateAsync(await factory.CreateMemberClientAsync(Creator), "第六本", "2026-10-19");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(response)).GetProperty("code").GetString()
            .Should().Be(DomainException.BookQuotaExceededCode);
        (await BookCountAsync(factory)).Should().Be(5);
    }

    [Fact]
    public async Task Same_name_for_the_same_owner_is_rejected()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        await factory.SeedBookAsync(Ct, ownerSubject: Creator);

        var response = await CreateAsync(await factory.CreateMemberClientAsync(Creator), " 測試帳本 ", "2026-10-19");

        // Q9：比對 trim 後的名稱（Book 會 trim）。
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(response)).GetProperty("code").GetString()
            .Should().Be(DomainException.DuplicateNameCode);
    }

    [Fact]
    public async Task Same_name_owned_by_someone_else_is_allowed()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        await factory.SeedBookAsync(Ct);

        var response = await CreateAsync(await factory.CreateMemberClientAsync(Creator), "測試帳本", "2026-10-19");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Blank_name_is_400(string? name)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        var client = await factory.CreateMemberClientAsync(Creator);

        var response = await client.PostAsJsonAsync("/api/books", new { name, openingDate = "2026-10-19" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_creates_cannot_exceed_the_quota()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        for (var i = 0; i < 4; i++)
        {
            await factory.SeedBookAsync(Ct, ownerSubject: Creator);
        }

        var first = await factory.CreateMemberClientAsync(Creator);
        var second = await factory.CreateMemberClientAsync(Creator);

        var responses = await Task.WhenAll(CreateAsync(first, "甲", "2026-10-19"), CreateAsync(second, "乙", "2026-10-19"));

        // Q16：advisory lock 讓第二個請求等第一個 commit 之後才計數。沒有鎖時不一定每次重現，所以這個測試只能保證「有鎖時一定綠」。
        responses.Select(r => r.StatusCode).Order()
            .Should().Equal(HttpStatusCode.Created, HttpStatusCode.UnprocessableEntity);
        (await BookCountAsync(factory)).Should().Be(5);
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string name, string openingDate) =>
        client.PostAsJsonAsync("/api/books", new { name, openingDate }, Ct);

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static async Task<int> BookCountAsync(ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SixJarsDbContext>().Books.CountAsync(Ct);
    }
}
```

- [ ] Step 2：`dotnet test --project tests/SixJars.Api.Tests --filter-class "*CreateBookEndpointsTests*"`。Expected：全部失敗。`POST /api/books` 沒有對應的 endpoint，會回 **405**（`/books` 已有 GET）。確認失敗訊息是 405 而不是編譯錯誤（`AddCreatorAsync` 在 Step 1 已加好）。
- [ ] Step 3：最小實作
  - `src/SixJars.Application/Common/ForbiddenException.cs`（寫法比照同資料夾的 `NotFoundException`）：

    ```csharp
    namespace SixJars.Application.Common;

    /// <summary>已登入但沒有權限執行這個操作（例如不在建立者名單，issue #7 Q18）；對應 403。帳本範圍的拒絕仍是 404（ADR 0005）。</summary>
    public sealed class ForbiddenException(string message) : Exception(message);
    ```

  - `ApiExceptionHandler` 在 `NotFoundException` 那一行之後加：

    ```csharp
            ForbiddenException forbidden => new ProblemDetails { Status = StatusCodes.Status403Forbidden, Detail = forbidden.Message },
    ```

  - `ISixJarsDbContext` 加：

    ```csharp
        /// <summary>
        /// 取得以 <paramref name="key"/> 識別、到 transaction 結束才釋放的鎖（PostgreSQL advisory lock）。
        /// 必須在 <see cref="BeginTransactionAsync"/> 之內呼叫；同一個 key 的其他 transaction 會等到這個 commit 或 rollback。
        /// </summary>
        Task AcquireTransactionLockAsync(string key, CancellationToken cancellationToken);
    ```

  - `SixJarsDbContext` 實作（`ExecuteSqlAsync` 會把插值參數化，不是字串拼接）：

    ```csharp
    public Task AcquireTransactionLockAsync(string key, CancellationToken cancellationToken) =>
        // hashtextextended 把任意字串轉成 bigint；比 hashtext 的 32 bit 不容易碰撞。
        Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    ```

  - `src/SixJars.Application/Books/CreateBook.cs`：

    ```csharp
    using FluentValidation;
    using MediatR;
    using Microsoft.EntityFrameworkCore;
    using SixJars.Application.Auditing;
    using SixJars.Application.Common;
    using SixJars.Domain.Books;
    using SixJars.Domain.Common;
    using SixJars.Domain.Members;

    namespace SixJars.Application.Books;

    /// <summary>
    /// 建立者建立一本空白帳本，自己成為擁有者（issue #7、ADR 0011），回傳帳本 Id。
    /// 沒有 BookId，所以不是 <see cref="IBookScoped"/>；權限在 handler 內檢查：不在建立者名單擲 <see cref="ForbiddenException"/>（403）。
    /// </summary>
    public sealed record CreateBook(string Name, DateOnly OpeningDate) : IRequest<Guid>
    {
        /// <summary>每人最多擁有的帳本數（Q8）：計算目前身為擁有者的帳本，刪除後名額就空出來。CLI 不受此限。</summary>
        public const int MaxOwnedBooks = 5;
    }

    /// <summary>建立帳本的稽核快照（Q19）。</summary>
    public sealed record CreatedBookSnapshot(string Name, DateOnly OpeningDate);

    internal sealed class CreateBookValidator : AbstractValidator<CreateBook>
    {
        // 與 BookConfiguration 的名稱長度一致。
        public CreateBookValidator() => RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
    }

    internal sealed class CreateBookHandler(ISixJarsDbContext db, ICurrentUser user, IAuditTrail audit, TimeProvider clock)
        : IRequestHandler<CreateBook, Guid>
    {
        public async Task<Guid> Handle(CreateBook request, CancellationToken cancellationToken)
        {
            // Q16：先鎖住這位建立者再計數，同時送出的兩個請求不會都看到 4 本。鎖到 commit／rollback 才釋放。
            await using var transaction = await db.BeginTransactionAsync(cancellationToken);
            await db.AcquireTransactionLockAsync($"create-book:{user.Subject}", cancellationToken);

            if (!await db.BookCreators.AnyAsync(c => c.GoogleSubject == user.Subject, cancellationToken))
            {
                throw new ForbiddenException("你不在建立者名單，無法建立帳本。");
            }

            var owned = db.OwnedBookIds(user.Subject);
            if (await owned.CountAsync(cancellationToken) >= CreateBook.MaxOwnedBooks)
            {
                throw new DomainException(
                    $"每人最多擁有 {CreateBook.MaxOwnedBooks} 本帳本，已達上限。", DomainException.BookQuotaExceededCode);
            }

            var book = new Book(request.Name, request.OpeningDate);
            if (await db.OwnerHasBookNamedAsync(owned, book.Name, cancellationToken))
            {
                throw new DomainException($"你已經有名稱為「{book.Name}」的帳本。", DomainException.DuplicateNameCode);
            }

            // D7：建立者一定是以 email 綁定進來的；沒有 email 的身分不能成為成員（NormalizeEmail 的前提）。
            var email = user.Email ?? throw new ForbiddenException("登入身分沒有 email，無法建立帳本。");
            var owner = BookMember.Create(book.Id, email, BookRole.Owner, clock.GetUtcNow());
            // 立即綁定：/api/me 與 BookAccessBehavior 都以 sub 認人。
            owner.BindSubject(user.Subject);

            db.Books.Add(book);
            db.BookMembers.Add(owner);
            audit.Record<CreatedBookSnapshot>(book.Id.Value, AuditAction.Create, AuditEntityTypes.Book, book.Id.Value,
                null, new CreatedBookSnapshot(book.Name, book.OpeningDate));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return book.Id.Value;
        }
    }
    ```

  - `BooksEndpoints` 在 `api.MapGet("/books", ...)` 之後加：

    ```csharp
        // 建立者建立空白帳本（issue #7）；不在 /books/{bookId} 群組底下，因為還沒有帳本。
        api.MapPost("/books", async (CreateBook command, ISender sender, CancellationToken ct) =>
        {
            var id = await sender.Send(command, ct);
            return Results.Created($"/api/books/{id}", new { id });
        });
    ```

- [ ] Step 4：單檔測試全綠，`Concurrent_creates_cannot_exceed_the_quota` **連跑 5 次**都要綠（`--filter-method "*Concurrent*"`）。變異檢查：
  - 把建立者檢查拿掉，`Member_who_is_not_a_creator_gets_403` 必須變紅。
  - 把上限從 `>=` 改成 `>`，`Sixth_owned_book_is_rejected` 必須變紅。
  - 把 `OwnerHasBookNamedAsync(owned, …)` 換成 `db.Books.AnyAsync(b => b.Name == book.Name)`，`Same_name_owned_by_someone_else_is_allowed` 必須變紅。
  - 把 `owner.BindSubject` 拿掉，`Creator_creates_an_empty_book_and_becomes_its_bound_owner` 必須變紅。
  - 拿掉鎖：**不保證**變紅（見本 Task 開頭第 4 點），結果照實記錄即可。
- [ ] Step 5：全部測試。Expected：總計約 **572**，失敗 0。`SecurityConventionTests`（新 endpoint 仍需登入與 antiforgery）、`BookScopeConventionTests`（`CreateBook` 沒有 `BookId`）都必須仍綠。
- [ ] Step 6：Commit

```powershell
git add src/SixJars.Application/Books/CreateBook.cs src/SixJars.Application/Common/ForbiddenException.cs src/SixJars.Application/Common/ISixJarsDbContext.cs src/SixJars.Infrastructure/Persistence/SixJarsDbContext.cs src/SixJars.Api/Infrastructure/ApiExceptionHandler.cs src/SixJars.Api/Endpoints/BooksEndpoints.cs tests/Shared/ApiSeed.cs tests/SixJars.Api.Tests/CreateBookEndpointsTests.cs
git commit -m "feat(api): POST /books 讓建立者建立空白帳本（403／上限 5 本／同名，advisory lock 防止同時送出超過上限）" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 7：`/api/me` 回報能否建立與剩餘名額

**Files:** Modify: `src/SixJars.Application/Members/MeDto.cs`、`GetMe.cs`、`tests/SixJars.Api.Tests/CreateBookEndpointsTests.cs`

前端只負責顯示（spec D8），所以剩餘名額由後端算好。容易錯的地方：非建立者的名額一律是 0（不是 5 − 擁有數）；被 `add-member` 加到超過 5 本時不能出現負數。擁有數直接用 `ListMemberBooksAsync` 的結果（它就是 `OwnedBookIds`），不要再查一次。

- [ ] Step 1：在 `CreateBookEndpointsTests` 加入（需要 `using SixJars.Application.Members;`）：

```csharp
    [Fact]
    public async Task Me_reports_remaining_quota_for_a_creator()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        await factory.SeedBookAsync(Ct, ownerSubject: Creator);
        await factory.SeedBookAsync(Ct, ownerSubject: Creator);

        var me = await (await factory.CreateMemberClientAsync(Creator)).GetFromJsonAsync<MeDto>("/api/me", ApiJson.Options, Ct);

        (me!.CanCreateBooks, me.RemainingBookQuota).Should().Be((true, 3));
    }

    [Fact]
    public async Task Me_reports_no_quota_for_a_member_who_is_not_a_creator()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.SeedBookAsync(Ct);

        var me = await (await factory.CreateMemberClientAsync()).GetFromJsonAsync<MeDto>("/api/me", ApiJson.Options, Ct);

        (me!.CanCreateBooks, me.RemainingBookQuota).Should().Be((false, 0));
    }

    [Fact]
    public async Task Me_quota_is_never_negative()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        // Q8：被 add-member 加成擁有者而超過上限時不擋，只是不能再自己建立。
        for (var i = 0; i < 6; i++)
        {
            await factory.SeedBookAsync(Ct, ownerSubject: Creator);
        }

        var me = await (await factory.CreateMemberClientAsync(Creator)).GetFromJsonAsync<MeDto>("/api/me", ApiJson.Options, Ct);

        (me!.CanCreateBooks, me.RemainingBookQuota).Should().Be((true, 0));
    }
```

- [ ] Step 2：單檔測試。Expected：**編譯失敗**，`MeDto.CanCreateBooks` 不存在。
- [ ] Step 3：實作
  - `MeDto`：

    ```csharp
    /// <summary>
    /// 目前登入的使用者，以及他所屬的帳本（<c>GET /api/me</c>，spec §4）。
    /// <paramref name="CanCreateBooks"/>：是否在建立者名單；<paramref name="RemainingBookQuota"/>：還能建立幾本，非建立者為 0（issue #7）。
    /// </summary>
    public sealed record MeDto(string Subject, string? Email, IReadOnlyList<BookSummaryDto> Books, bool CanCreateBooks, int RemainingBookQuota);
    ```

  - `GetMeHandler`：

    ```csharp
    public async Task<MeDto> Handle(GetMe request, CancellationToken cancellationToken)
    {
        // 所屬的帳本就是身為擁有者的帳本（OwnedBookIds），直接拿來算剩餘名額，不再查一次。
        var books = await db.ListMemberBooksAsync(user.Subject, cancellationToken);
        var canCreate = await db.BookCreators.AnyAsync(c => c.GoogleSubject == user.Subject, cancellationToken);
        var remaining = canCreate ? Math.Max(0, CreateBook.MaxOwnedBooks - books.Count) : 0;
        return new MeDto(user.Subject, user.Email, books, canCreate, remaining);
    }
    ```

    （需要 `using Microsoft.EntityFrameworkCore;`；把原本的 expression-bodied 改成區塊。）
- [ ] Step 4：單檔測試全綠。變異檢查：把 `Math.Max(0, …)` 拿掉，`Me_quota_is_never_negative` 必須變紅；把 `canCreate ? … : 0` 改成不看 `canCreate`，`Me_reports_no_quota_for_a_member_who_is_not_a_creator` 必須變紅。
- [ ] Step 5：全部測試。Expected：總計約 **575**，失敗 0。既有的 `MembershipTests.Me_lists_only_member_books` 必須仍綠（它只讀 `Subject`／`Email`／`Books`）。
- [ ] Step 6：Commit

```powershell
git add src/SixJars.Application/Members/MeDto.cs src/SixJars.Application/Members/GetMe.cs tests/SixJars.Api.Tests/CreateBookEndpointsTests.cs
git commit -m "feat(api): /me 回報 canCreateBooks 與 remainingBookQuota" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## 完成後的驗證（後端 checkpoint）

- [ ] `dotnet build SixJars.slnx`：0 個錯誤、0 個編譯器警告（NU1902／NU1903 不算）。
- [ ] `dotnet test --solution SixJars.slnx`：總計約 575（基準線 549＋26），失敗 0，略過 0。若 `reference/` 不存在，Acceptance 會 skip，回報時要說明。
- [ ] `git status --short`：乾淨。
- [ ] `git log --oneline master..`：本計畫 7 顆 commit（另有 #8 的 `5842d76` 與 docs 的 commit）。
- [ ] `git diff master -- . ':!docs'` 沒有連線字串、金鑰或 `reference/` 的內容。
- [ ] 計畫末尾新增「執行結果與偏差」一節，用獨立的 `docs(plans):` commit 回寫。

## 手動驗證

自動測試涵蓋不到真實的 Google 登入與 migration bundle：

1. 套用 migration 到本機 DB（deploy.md 的 efbundle 流程，`--force` 重建 bundle），確認 `BookCreators` 存在，且 app 角色有 SELECT／INSERT／UPDATE／DELETE（`\dp "BookCreators"`）。
2. `dotnet run --project src/SixJars.Cli -- add-creator --email <一個沒有任何帳本的 Google 帳號>`。
3. 用那個帳號在 https 的本機登入：應該進得去（不是 `/denied`），`GET /api/me` 的 `canCreateBooks` 為 true、`remainingBookQuota` 為 5。
4. 用 `.http` 或瀏覽器 DevTools 送 `POST /api/books`（要帶 XSRF header），應回 201；`/api/me` 的 `books` 出現新帳本、`remainingBookQuota` 變成 4。
5. `remove-creator` 後再送一次，應回 403；登出後重新登入，因為仍是新帳本的成員，應該進得去。

## 後續（不在本計畫內）

- 前端計畫 `2026-10-19-create-book-frontend.md`。
- 在 Web 還原備份、匯入 Excel、帳本範本（spec 的排除項目）。
- 從名單移除時立即撤銷 cookie。

## 附錄：核准前事實查核（2026-10-19）

| 引用 | 存在？ | 證據 | 修正 |
|---|---|---|---|
| EF Core 10.0.12 的 `DatabaseFacade.ExecuteSqlAsync(FormattableString, CancellationToken)`，回傳 `Task<int>`（可直接當 `Task` 回傳） | ✅ | `Directory.Packages.props`（Relational 10.0.12）；套件 XML 文件 `ExecuteSqlAsync(DatabaseFacade, FormattableString, CancellationToken)` | — |
| `SixJarsDbContext` 已 `using Microsoft.EntityFrameworkCore;`，`BeginTransactionAsync` 包 `Database.BeginTransactionAsync` | ✅ | `SixJarsDbContext.cs:2,40` | — |
| `ISixJarsDbContext.BeginTransactionAsync`、`BookMembers`、`AuditEntries` | ✅ | `ISixJarsDbContext.cs:24,27,41` | — |
| 沒有其他類別實作 `ISixJarsDbContext`（新增介面成員不會讓 fake 編譯失敗） | ✅ | `grep ": ISixJarsDbContext"` 只有 DI 註冊 | — |
| `IAuditTrail.Record<T>(…, T? before, T? after)`，`before` 傳 `null` | ✅ | `IAuditTrail.cs:10`；`ImportLegacyBook` 已有 `audit.Record(…, null, new ImportSnapshot(…))` 的寫法 | — |
| `AuditAction.Create`、`AuditEntityTypes.Book` | ✅ | `AuditAction.cs:6`、`AuditEntry.cs:34` | — |
| `AuditTrailTests.EntriesAsync`、`SettingsMaintenanceEndpointsTests.ReadProblemAsync` 可從新測試類別呼叫 | ✅ | 兩者都是 `internal static`（`AuditTrailTests.cs:290`、`SettingsMaintenanceEndpointsTests.cs:453`），同一個測試組件 | — |
| `GoogleSignInValidatorTests` 自己有 private 的 `EntriesAsync`、`CreateFactoryAsync`、`ValidateAsync`、`Principal`、`Now`、`Email`、`Subject`、`Ct`，已 `using SixJars.Domain.Members;`、`Microsoft.EntityFrameworkCore`、`SixJars.Infrastructure.Persistence` | ✅ | `GoogleSignInValidatorTests.cs:1-26,167-217` | — |
| `BookMember.Id` 是 `Guid`（`ReloadCreatorAsync` 比照 `ReloadAsync`） | ✅ | `BookMember.cs` | — |
| `ImportLegacyBookTests` 已有 `using SixJars.Domain.Members;`、`Now`、`Import()` 的 email 是 `" Owner@Example.com "` | ✅ | `ImportLegacyBookTests.cs:14,29,144` | — |
| `ImportLegacyBook` 的同名檢查在第 2 步、owner 在其後建立 | ✅ | `ImportLegacyBook.cs:47-55` | Task 5 已寫明要搬移 |
| **`BookMembership.cs` 的 using** | ❌ | 只有 `SixJars.Domain.Common`、`SixJars.Domain.Members`；新 helper 用到 `AnyAsync` | **已修正**：Task 5 Step 3 補上 `using Microsoft.EntityFrameworkCore;` |
| `BookMembership` 是 `internal static`，`OwnedBookIds` 存在；`ListMemberBooksAsync` 就是 `OwnedBookIds` 的帳本 | ✅ | `BookMembership.cs`、`Books/ListBooks.cs:19-30` | — |
| `BookConfiguration` 的名稱長度 100 | ✅ | `BookConfiguration.cs:9`（`NameMaxLength = 100`） | — |
| `BookMemberConfiguration` 的 email 320、sub 255 | ✅ | `BookMemberConfiguration.cs:14-15` | — |
| `BookMember.NormalizeEmail`（public static，空白擲 `DomainException`）、`BindSubject` 的規則 | ✅ | `BookMember.cs` | 已知小瑕疵：`BookCreator.Create("")` 的訊息會是「成員的 email 不可為空白」，測試只檢查例外型別，不影響；要改文字需另外調整 `NormalizeEmail` |
| `Book` 建構子 trim 名稱、空白擲 `DomainException` | ✅ | `Book.cs:13-16,377` | — |
| `DomainException(message, code)` 與 `Code` | ✅ | `DomainException.cs` | — |
| `NotFoundException`、`IBookScoped`、`ICliOnlyRequest` 在 `SixJars.Application.Common` | ✅ | 對應檔案存在 | — |
| `CliAppTests` 的 using（`Microsoft.EntityFrameworkCore`、`SixJars.Domain.Members`、`SixJars.Infrastructure.Persistence`）、`RunAsync`（固定 `Now` 的 `TimeProvider`）、`Open`、`Add_member_twice_fails_with_clear_message` | ✅ | `CliAppTests.cs:1-15,22,87,249-267` | — |
| CLI 把 `DomainException`／`NotFoundException` 轉成 exit code 1 與訊息；`CliApp` 已 `using SixJars.Application.Members;` | ✅ | `CliApp.cs:11,203` | — |
| `add-member` 的寫法（`Option<string>("--email") { Description, Required = true }`、`SetAction((parseResult, ct) => host.RunAsync(…))`） | ✅ | `CliApp.cs:110-123` | — |
| `--book-name` 的說明文字 | ✅ | `CliApp.cs:57` | — |
| FluentValidation 的 internal validator 會被註冊 | ✅ | `DependencyInjection.cs:25`（`includeInternalTypes: true`） | — |
| **`CreateBook` 的 JSON body 裡 `name` 為 null** 會落到 validator 回 400 | ✅ | `Program.cs:20` 只加 `JsonStringEnumConverter`，沒有開 `RespectNullableAnnotations`、也沒有 `AddValidation()`，null 可以反序列化；`NotEmpty` 擋 null／空白；`ToBodyFieldName("Name")` → `name` | — |
| `POST /api/books` 在實作前回 405（同路徑已有 GET） | ✅ | `BooksEndpoints.cs:13` | — |
| `ApiExceptionHandler` 已 `using SixJars.Application.Common;`，`NotFoundException` 那一行之後可加 403 | ✅ | `ApiExceptionHandler.cs:6,30` | — |
| `ApiSeed`：`MemberAddedAt`、`SeedBookAsync(…, ownerSubject)`（已綁定的擁有者）、`using SixJars.Domain.Members;` | ✅ | `ApiSeed.cs:1-45` | — |
| `ApiFactory.CreateAsync`、`CreateMemberClientAsync(subject)`、`TestAuthHandler.EmailOf`、`ApiJson.Options`；Api 測試以 assembly fixture 注入 `PostgresFixture` | ✅ | `tests/Shared/*`、`AssemblyInfo.cs:4` | — |
| 稽核快照的中文可用 `Contain` 比對 | ✅ | `After` 是 jsonb；`AuditTrailTests.cs:138` 已有 `Contain("午餐便當<加蛋>")` | — |
| `SecurityConventionTests`（登入、XSRF）會自動涵蓋新 endpoint；`BookScopeConventionTests` 只檢查帶 `BookId` 的 request | ✅ | `SecurityConventionTests.cs:25-80`、`BookScopeConventionTests.cs:24-34` | — |
| `MembershipTests.Me_lists_only_member_books` 以 `MeDto` 反序列化 | ✅ | `MembershipTests.cs:68`；新欄位由 API 回傳，不影響 | — |
| ADR 0010 的格式、ADR 0005 的標題、`CONTEXT.md` 的「刪除帳本 (Delete Book)」詞條 | ✅ | `0010-…md`、`0005-…md:1`、`CONTEXT.md:125` | — |
| Create 的檔案尚不存在（`BookCreator.cs`、`BookCreatorConfiguration.cs`、`AddBookCreator.cs`、`RemoveBookCreator.cs`、`ForbiddenException.cs`、`CreateBook.cs`、`BookCreatorTests.cs`、`CreateBookEndpointsTests.cs`、ADR 0011） | ✅ | 逐一 `test -e` | — |
| 測試碼競態：`Concurrent_creates_cannot_exceed_the_quota` 用 `Task.WhenAll`，兩個 client 各自的 scope 與連線；結果以 `.Order()` 比對，不假設哪個先完成 | ✅ | — | 拿掉鎖的變異檢查不保證變紅，Task 6 已寫明 |
