# P2 後端（API、認證、稽核、部署）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL：以 TDD（紅 → 綠 → 重構）逐 Task 執行，每個 Task 的 Step 2「確認失敗且原因正確」不可省略。由 subagent 交付的 Task，主控者在標記完成前必須：重跑全部測試與 build、做變異測試、比對 diff 與本計畫、掃描機密。

**Goal**：讓 P1 帳務核心可以透過登入後的 HTTP API 使用。具體要求：
- 餘額改由 SQL 彙總，並以 P1 計算器為 oracle 驗證結果一致。
- 每次寫入都留下稽核記錄；刪除一律軟刪除；鎖帳日強制執行。
- 用 Google OIDC 登入，以帳本成員表作為白名單。
- 提供 CLI 匯入舊 Excel 與還原備份；提供 JSON 備份與 CSV／xlsx 匯出；附上 Dockerfile 與部署文件。

**Architecture**：沿用 P1 的 Clean Architecture，新增 `SixJars.Api`（Minimal API + MediatR）與 `SixJars.Cli`（System.CommandLine）兩個 composition root，兩者共用 Application 的 command。

**Tech Stack**：P1 全部，加上 MediatR 14.2.0、FluentValidation 12.1.1、ASP.NET Core 10（OpenIdConnect、DataProtection.EntityFrameworkCore 10.0.12）、Microsoft.AspNetCore.Mvc.Testing 10.0.12、ClosedXML 0.105.1、System.CommandLine 2.0.12。

**相關文件**
- 設計：[docs/superpowers/specs/2026-10-04-p2-backend-api-design.md](../specs/2026-10-04-p2-backend-api-design.md)（已核准）
- 術語：[CONTEXT.md](../../../CONTEXT.md)
- 決策：[docs/adr/](../../adr/)，特別是 0005（認證）與 0006（稽核、軟刪除）
- P1 計畫：[2026-10-04-p1-ledger-core.md](2026-10-04-p1-ledger-core.md)。Task 編號接續 P1，從 **T17** 開始。

---

## 執行前必讀

### 環境

| 項目 | 值 |
|---|---|
| 路徑 | 使用者本機 `F:\VibeCode\SixJars-AccountingBook`，或雲端 container `/home/user/SixJars-AccountingBook` |
| 分支 | `claude/p2-planning-83al3h`（雲端 session 指定）。在本機執行時，依使用者指示另開 `feat/p2-backend` |
| 全部測試 | `dotnet test`（repo 根目錄） |
| 單一測試類別 | `dotnet test --project tests/<專案> -- --filter-class "<完整類別名>"` |
| Build | `dotnet build`，期望 0 warning、0 error |
| 前置條件 | Docker 執行中。雲端 container 要先 `dockerd &`，SDK 位於 `$HOME/.dotnet`，見附錄 |

**雲端 container 的注意事項**：
- `builds.dotnet.microsoft.com` 被 proxy 擋住，SDK 改從 `mcr.microsoft.com/dotnet/sdk:10.0` image 複製出來（版本恰好是 10.0.401）。
- 主控台輸出是英文：`total / failed / succeeded / skipped`。在本機上則是中文：`總計 / 失敗 / 已成功 / 已略過`。下文的 Expected 一律寫成「總計 N、略過 M」。

### 基準線

`master` 7dcb832，`reference/` **存在**時：總計 98、失敗 0、略過 0。
`reference/` 不存在時（例如雲端 container）：總計 98、失敗 0、略過 12。

每個 Task 結束時的期望總數寫在各 Task 的最後一個 Step。總數低於期望值，就代表弄壞了東西。
本計畫新增的 Excel 驗收有 6 個（T27 3 個、T40 3 個），在 `reference/` 不存在時會略過，所以完成時的「略過」數是 12 + 6 = 18。

### 絕對不要碰的檔案

- `reference/**`：個資，**永遠不可 `git add`**。
- `CONTEXT.md`、`docs/adr/**`：只有在術語或決策真的改變時才修改，而且要用獨立的 commit。
- 任何真實的連線字串、client secret、license key：只能放在環境變數或 Secret Manager。repo 裡只放 `.env.example` 的 placeholder。
- 每個 Task 都要用**明確路徑**執行 `git add`，**禁止 `git add .` 與 `git add -A`**。

### 慣例（P1 全部沿用，以下是新增的部分）

- **Application 直接使用 EF Core**：handler 透過 `ISixJarsDbContext`（`DbSet<T>` 加上 `SaveChangesAsync`）讀寫資料，不另外包 repository。
  - **否決：每個聚合各寫一個 repository。** 查詢本來就需要 EF 的 LINQ，包一層只是多出轉送用的程式碼。
  - Provider 特有的細節（`xmin`）一律藏在 `ISixJarsDbContext` 的方法後面。
- **API DTO**：
  - Id 一律用 `Guid`，歸屬月份用 `int`（yyyymm，即 `BudgetMonth.Key`）。
  - 列舉序列化成字串（`JsonStringEnumConverter`）。
  - 金額沿用 P1 的符號慣例。
- **錯誤**：
  - `DomainException` 對應 422，ProblemDetails 的 `extensions.code` 放 `DomainException.Code`。
  - `ValidationException` 對應 400，`NotFoundException` 對應 404，`DbUpdateConcurrencyException` 對應 409。
- **API 測試**：`ApiFactory`（`WebApplicationFactory<Program>`）。每個測試都用 `PostgresFixture` 建立獨立的資料庫，並用 `TestAuthHandler` 以 `X-Test-Sub` header 模擬登入身分。
  - 段 E 加入 antiforgery 之後，改用 `ApiFactory.CreateMemberClientAsync()`：這個方法會建立成員、取得 XSRF token，並設定好 header。
- **一個 Task 一個 commit**，格式為 `<type>(<scope>): <繁中摘要>`，結尾加上：
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  ```
  在雲端 session 執行時，還要再加一行 session 指定的 `Claude-Session:`。
- **Migration**：每個改到 schema 的 Task，用 `dotnet ef migrations add <Name> --project src/SixJars.Infrastructure --output-dir Persistence/Migrations` 產生 migration，產生的三個檔案（含 ModelSnapshot）一併 commit。工具：`dotnet tool install --global dotnet-ef --version 10.0.12`（雲端 container 第一次使用時要先安裝）。

---

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `src/SixJars.Api/{SixJars.Api.csproj,Program.cs}` | composition root、middleware、endpoint 註冊 |
| `src/SixJars.Api/Endpoints/{Books,Transactions,PlannedExpenses,Summary,Exports,Audit,Auth}Endpoints.cs` | Minimal API endpoint 群組（只負責轉交） |
| `src/SixJars.Api/Infrastructure/{ApiExceptionHandler,HttpCurrentUser,AntiforgeryFilter,GoogleSignInValidator}.cs` | 錯誤對應、目前使用者、XSRF 驗證、白名單檢查 |
| `src/SixJars.Application/DependencyInjection.cs` | 註冊 MediatR、validator 與 behavior |
| `src/SixJars.Application/Common/{ISixJarsDbContext,ICurrentUser,NotFoundException,ValidationBehavior,BookAccessBehavior,IBookScoped}.cs` | 共用基礎 |
| `src/SixJars.Application/Books/**` | 帳本設定的 query 與 command |
| `src/SixJars.Application/Transactions/**` | `TransactionInput`、`TransactionBuilder`、`TransactionDto`，以及 CRUD |
| `src/SixJars.Application/Planning/**` | 預定支出的 CRUD 與付款 |
| `src/SixJars.Application/Ledger/{ILedgerSummaryQuery,LedgerSummary,GetLedgerSummary}.cs` | 餘額彙總 |
| `src/SixJars.Application/Auditing/{AuditEntry,IAuditTrail,AuditTrail,GetAuditHistory}.cs` | 稽核記錄 |
| `src/SixJars.Application/Members/**` | 帳本成員 command 與 `GetMe` |
| `src/SixJars.Application/Backup/**`、`Exports/**`、`LegacyImport/ImportLegacyBook.cs` | 備份、匯出、正式匯入 |
| `src/SixJars.Domain/Members/{BookMember,BookRole}.cs` | 帳本成員 |
| `src/SixJars.Infrastructure/DependencyInjection.cs` | `AddSixJarsInfrastructure(connectionString)` |
| `src/SixJars.Infrastructure/Ledger/SqlLedgerSummaryQuery.cs` | SQL 彙總 |
| `src/SixJars.Infrastructure/Persistence/{AuditEntryConfiguration,BookMemberConfiguration}.cs` 與 migration | 持久層 |
| `src/SixJars.Cli/{SixJars.Cli.csproj,Program.cs,CliApp.cs}` | `import-legacy`、`restore-backup`、`add-member` |
| `tests/SixJars.Api.Tests/**`、`tests/SixJars.Cli.Tests/**` | 測試 |
| `tests/Shared/{ApiFactory,TestAuthHandler,CommandCounter}.cs` | 共用的測試基礎 |
| `Dockerfile`、`.dockerignore`、`.env.example`、`docs/deploy.md` | 部署 |

### 修改

| 檔案 | 改動 |
|---|---|
| `Directory.Packages.props` | 新增套件版本，見 T17、T18、T36、T39、T43 |
| `SixJars.slnx` | 加入新專案 |
| `src/SixJars.Domain/Common/DomainException.cs` | 新增 `Code` |
| `src/SixJars.Domain/Books/Book.cs` | 可選的 Id 參數（還原用）、`LockDate` |
| `src/SixJars.Domain/Transactions/{Transaction,TransactionFactory}.cs` | `ReplaceWith`、`Delete`、固定 Id（還原用） |
| `src/SixJars.Domain/Planning/PlannedExpense.cs` | `Update`、`Delete`、`MarkUnpaid`、可選的 Id |
| `src/SixJars.Infrastructure/Persistence/*Configuration.cs`、`SixJarsDbContext.cs` | `xmin`、query filter、新表 |
| `src/SixJars.Infrastructure/Persistence/LedgerSnapshotLoader.cs` | 排序（P1 交接的注意事項） |
| `tests/Shared/PostgresFixture.cs` | 新增 `CreateConnectionStringAsync` |

### Task 相依順序

```
段 C（API 與帳務讀寫）
T17 ─► T18 ─► T19
T17 ─► T20 ─► T21 ─► T22 ─► T23
T21 ─► T24 ─► T25
T17 ─► T26 ─► T27（需 T21）            ◄── checkpoint C
段 D（寫入路徑完整）
T23 ─► T28 ─► T29（需 T25）
T29 ─► T30 ─► T31
T30 ─► T32 ─► T33                      ◄── checkpoint D
段 E（安全）
T33 ─► T34 ─► T35 ─► T36 ─► T37 ─► T38 ◄── checkpoint E
段 F（搬家與部署）
T35 ─► T39 ─► T40
T30 ─► T41 ─► T42（需 T39）
T22 ─► T43
T38 ─► T44 ─► T45                      ◄── checkpoint F
```

---
## 段 C：API 與帳務讀寫

> 本段結束時，在本機可以透過 HTTP 記帳並查詢餘額。授權暫時只要求「已登入」；帳本成員的檢查到 T35 才加上。在這之前**不可部署**。

## Task 17：Api 骨架、`/health`、Infrastructure 的 DI

**Files**
- Create: `src/SixJars.Api/SixJars.Api.csproj`、`src/SixJars.Api/Program.cs`、`src/SixJars.Api/Endpoints/HealthEndpoints.cs`
- Create: `src/SixJars.Infrastructure/DependencyInjection.cs`
- Create: `tests/SixJars.Api.Tests/{SixJars.Api.Tests.csproj,AssemblyInfo.cs,HealthEndpointTests.cs}`
- Create: `tests/Shared/{ApiFactory,TestAuthHandler}.cs`
- Modify: `Directory.Packages.props`、`SixJars.slnx`、`tests/Shared/PostgresFixture.cs`

**Step 0：套件與專案**

在 `Directory.Packages.props` 加入下列版本（MediatR 等套件到 T18 才會用到，這裡一次加入）：

```xml
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.12" />
    <PackageVersion Include="MediatR" Version="14.2.0" />
    <PackageVersion Include="FluentValidation" Version="12.1.1" />
    <PackageVersion Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
```

`src/SixJars.Api/SixJars.Api.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <ProjectReference Include="..\SixJars.Application\SixJars.Application.csproj" />
    <ProjectReference Include="..\SixJars.Infrastructure\SixJars.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

`tests/SixJars.Api.Tests/SixJars.Api.Tests.csproj`：沿用 `SixJars.Infrastructure.Tests.csproj`，再做三處修改：
- 加入 `<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />`。
- ProjectReference 改指向 `..\..\src\SixJars.Api\SixJars.Api.csproj`。
- Shared 的 link 保持原樣（`..\Shared\**\*.cs`）。

`AssemblyInfo.cs` 與 Infrastructure.Tests 相同（`[assembly: AssemblyFixture(typeof(PostgresFixture))]`）。

**注意**：`ApiFactory.cs` 與 `TestAuthHandler.cs` 放在 `tests/Shared`，但 Infrastructure.Tests 與 AcceptanceTests 沒有參考 ASP.NET Core。所以那兩個專案的 link 要改成 `<Compile Include="..\Shared\**\*.cs" Exclude="..\Shared\Api*.cs;..\Shared\TestAuthHandler.cs" LinkBase="Shared" />`。

最後把兩個新專案加入 `SixJars.slnx`：`dotnet sln add src/SixJars.Api tests/SixJars.Api.Tests`。

**Step 1：失敗測試**

`tests/Shared/PostgresFixture.cs`：把建立資料庫的邏輯抽成 `CreateConnectionStringAsync`。`CreateDatabaseAsync` 改為呼叫它，P1 的測試完全不需要修改。

```csharp
    /// <summary>建立一個已套用 migration 的獨立資料庫，回傳連線字串（API 測試用）。</summary>
    public async Task<string> CreateConnectionStringAsync(CancellationToken cancellationToken)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = $"t_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var db = new SixJarsDbContext(Options(connectionString));
        await db.Database.MigrateAsync(cancellationToken);
        return connectionString;
    }

    public async Task<Func<SixJarsDbContext>> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        var options = Options(await CreateConnectionStringAsync(cancellationToken));
        return () => new SixJarsDbContext(options);
    }

    private static DbContextOptions<SixJarsDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<SixJarsDbContext>().UseNpgsql(connectionString).Options;
```

`tests/Shared/TestAuthHandler.cs`（spike S4 已驗證）：

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SixJars.Tests.Shared;

/// <summary>以 <c>X-Test-Sub</c> header 模擬已登入的 Google 身分；沒有這個 header 就視為未登入。</summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string SubjectHeader = "X-Test-Sub";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(SubjectHeader, out var subject))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim("sub", subject.ToString()), new Claim("email", $"{subject}@example.com")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
```

`tests/Shared/ApiFactory.cs`：

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace SixJars.Tests.Shared;

/// <summary>每個測試一個 factory、一個獨立的資料庫；以 <see cref="TestAuthHandler"/> 取代 Google 登入。</summary>
public sealed class ApiFactory(string? connectionString) : WebApplicationFactory<Program>
{
    public const string DefaultSubject = "owner-sub";

    public static async Task<ApiFactory> CreateAsync(PostgresFixture postgres, CancellationToken cancellationToken) =>
        new(await postgres.CreateConnectionStringAsync(cancellationToken));

    /// <summary>已登入（<see cref="DefaultSubject"/>）的 client。T37 之後會改由 CreateMemberClientAsync 取代。</summary>
    public HttpClient CreateSignedInClient(string subject = DefaultSubject)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, subject);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (connectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:SixJars", connectionString);
        }

        builder.ConfigureTestServices(services =>
            services.AddAuthentication(o =>
                {
                    o.DefaultScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
    }
}
```

`tests/SixJars.Api.Tests/HealthEndpointTests.cs`：

```csharp
using System.Net;
using FluentAssertions;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

public class HealthEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Health_checks_the_database_without_signing_in()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ApiFactory.CreateAsync(postgres, ct);

        var response = await factory.CreateClient().GetAsync("/health", ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void Startup_fails_without_a_connection_string()
    {
        // 前提：執行測試的環境沒有設定 ConnectionStrings__SixJars
        using var factory = new ApiFactory(connectionString: null);

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionStrings__SixJars*");
    }
}
```

**Step 2：確認失敗**

`dotnet test --project tests/SixJars.Api.Tests`：會出現編譯錯誤，因為 `Program` 還不存在。這是預期中的失敗。

**Step 3：最小實作**

`src/SixJars.Infrastructure/DependencyInjection.cs`：

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Infrastructure.Persistence;

namespace SixJars.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// 連線字串只從呼叫端傳入（API 與 CLI 都讀環境變數 ConnectionStrings__SixJars），這裡不設任何預設值。
    /// 測試可以註冊 <see cref="IInterceptor"/>（例如計算資料庫往返次數），會自動掛到 DbContext 上。
    /// </summary>
    public static IServiceCollection AddSixJarsInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SixJarsDbContext>((sp, options) => options
            .UseNpgsql(connectionString)
            .AddInterceptors(sp.GetServices<IInterceptor>()));
        return services;
    }
}
```

`src/SixJars.Api/Endpoints/HealthEndpoints.cs`：

```csharp
using SixJars.Infrastructure.Persistence;

namespace SixJars.Api.Endpoints;

internal static class HealthEndpoints
{
    /// <summary>Cloud Scheduler 定時 ping（spec §5）：會實際連到資料庫，讓 Supabase 不會因為閒置而暫停。</summary>
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/health", async (SixJarsDbContext db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct) ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
}
```

`src/SixJars.Api/Program.cs`：

```csharp
using SixJars.Api.Endpoints;
using SixJars.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("SixJars")
    ?? throw new InvalidOperationException("未設定資料庫連線字串：請設定環境變數 ConnectionStrings__SixJars。");

builder.Services.AddSixJarsInfrastructure(connectionString);
builder.Services.AddProblemDetails();

var app = builder.Build();
app.MapHealthEndpoints();
app.Run();

public partial class Program;
```

**Step 4：單檔測試**

`dotnet test --project tests/SixJars.Api.Tests`：2 個測試都通過。

**Step 5：全部測試**

執行 `dotnet build` 與 `dotnet test`，Expected：總計 100、失敗 0（`reference/` 不存在時略過 12）。

**Step 6：commit**

`git add` 以下路徑：
- `Directory.Packages.props`、`SixJars.slnx`
- `src/SixJars.Api`、`src/SixJars.Infrastructure/DependencyInjection.cs`
- `tests/Shared`、`tests/SixJars.Api.Tests`
- `tests/SixJars.Infrastructure.Tests/SixJars.Infrastructure.Tests.csproj`、`tests/SixJars.AcceptanceTests/SixJars.AcceptanceTests.csproj`

commit 訊息：`feat(api): API 骨架與 /health`

---

## Task 18：MediatR pipeline、錯誤對應、讀取帳本設定

**Files**
- Create: `src/SixJars.Application/{DependencyInjection.cs}`
- Create: `src/SixJars.Application/Common/{ISixJarsDbContext,ICurrentUser,NotFoundException,ValidationBehavior}.cs`
- Create: `src/SixJars.Application/Books/{BookDto,GetBook,ListBooks}.cs`
- Create: `src/SixJars.Api/Infrastructure/{ApiExceptionHandler,HttpCurrentUser}.cs`、`src/SixJars.Api/Endpoints/BooksEndpoints.cs`
- Modify: `src/SixJars.Domain/Common/DomainException.cs`、`src/SixJars.Infrastructure/Persistence/SixJarsDbContext.cs`、`src/SixJars.Application/SixJars.Application.csproj`、`src/SixJars.Api/Program.cs`
- Test: `tests/SixJars.Application.Tests/Common/ValidationBehaviorTests.cs`、`tests/SixJars.Api.Tests/BooksEndpointsTests.cs`、`tests/Shared/ApiSeed.cs`

**Step 1：失敗測試**

`ValidationBehaviorTests`（純單元測試）：

```csharp
using FluentAssertions;
using FluentValidation;
using MediatR;
using SixJars.Application.Common;
using Xunit;

namespace SixJars.Application.Tests.Common;

public class ValidationBehaviorTests
{
    private sealed record Probe(string Name) : IRequest<string>;

    private sealed class ProbeValidator : AbstractValidator<Probe>
    {
        public ProbeValidator() => RuleFor(p => p.Name).NotEmpty();
    }

    [Fact]
    public async Task Passes_valid_requests_to_the_handler()
    {
        var behavior = new ValidationBehavior<Probe, string>([new ProbeValidator()]);

        var result = await behavior.Handle(new Probe("ok"), _ => Task.FromResult("handled"), TestContext.Current.CancellationToken);

        result.Should().Be("handled");
    }

    [Fact]
    public async Task Throws_without_calling_the_handler_when_invalid()
    {
        var behavior = new ValidationBehavior<Probe, string>([new ProbeValidator()]);
        var called = false;

        var act = () => behavior.Handle(new Probe(""), _ => { called = true; return Task.FromResult(""); }, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "Name");
        called.Should().BeFalse();
    }
}
```

`tests/Shared/ApiSeed.cs`：直接透過 DbContext 寫入測試資料，給 API 測試共用。

```csharp
using Microsoft.Extensions.DependencyInjection;
using SixJars.Domain.Books;
using SixJars.Infrastructure.Persistence;

namespace SixJars.Tests.Shared;

internal static class ApiSeed
{
    /// <summary>在 factory 的資料庫寫入一本含現金、銀行、信用卡、電子錢包、貸款、一個財務規劃帳戶與基本分類的帳本。</summary>
    public static async Task<Book> SeedBookAsync(this ApiFactory factory, CancellationToken cancellationToken)
    {
        var book = new Book("測試帳本", new DateOnly(2025, 12, 31));
        book.AddAccount("現金", AccountType.Cash, 1000m);
        book.AddAccount("國泰世華銀行", AccountType.Bank, 50000m);
        book.AddAccount("國泰Combo卡", AccountType.CreditCard, -2000m);
        book.AddAccount("悠遊卡", AccountType.EWallet, 300m);
        book.AddAccount("房屋貸款", AccountType.Loan, -1000000m);
        book.AddPlanningFund("財務自由帳戶", 10000m);
        book.AddIncomeCategory("工作薪資");
        var food = book.AddExpenseCategory("主食", ExpenseNature.Floating);
        book.AddSubCategory(food.Id, "午餐");
        var fixedExpense = book.AddExpenseCategory("固定支出", ExpenseNature.Fixed);
        book.AddSubCategory(fixedExpense.Id, "保險費");
        var loan = book.AddExpenseCategory("貸款支出", ExpenseNature.Loan);
        book.AddSubCategory(loan.Id, "房屋貸款");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        db.Books.Add(book);
        await db.SaveChangesAsync(cancellationToken);
        return book;
    }
}
```

`BooksEndpointsTests`：

```csharp
public class BooksEndpointsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Get_book_returns_accounts_funds_and_categories()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ApiFactory.CreateAsync(postgres, ct);
        var book = await factory.SeedBookAsync(ct);

        var dto = await factory.CreateSignedInClient().GetFromJsonAsync<BookDto>($"/api/books/{book.Id.Value}", ApiJson.Options, ct);

        dto!.Name.Should().Be("測試帳本");
        dto.Accounts.Should().ContainSingle(a => a.Name == "悠遊卡" && a.Type == AccountType.EWallet);
        dto.PlanningFunds.Should().ContainSingle(f => f.Name == "財務自由帳戶" && f.OpeningBalance == 10000m);
        dto.Categories.Should().ContainSingle(c => c.Name == "午餐" && c.ParentId != null);
        dto.LockDate.Should().BeNull();
    }

    [Fact]
    public async Task Unknown_book_is_404_problem_details()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ApiFactory.CreateAsync(postgres, ct);

        var response = await factory.CreateSignedInClient().GetAsync($"/api/books/{Guid.NewGuid()}", ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }
}
```

`ApiJson.Options` 放在 `tests/Shared/ApiJson.cs`：`new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } }`。

**Step 2：確認失敗**：編譯失敗，因為 `ValidationBehavior`、`BookDto` 都還不存在。

**Step 3：實作**

- `DomainException`：新增 `Code` 屬性，預設值為 `"rule"`。

  ```csharp
  /// <summary>違反領域規則時拋出。<see cref="Code"/> 會原樣放進 API 的 ProblemDetails，前端可據此判斷錯誤種類。</summary>
  public sealed class DomainException(string message, string code = DomainException.RuleCode) : Exception(message)
  {
      public const string RuleCode = "rule";
      public const string LockedCode = "locked";
      public string Code { get; } = code;
  }
  ```

- `ISixJarsDbContext`：

  ```csharp
  public interface ISixJarsDbContext
  {
      DbSet<Book> Books { get; }
      DbSet<Transaction> Transactions { get; }
      DbSet<PlannedExpense> PlannedExpenses { get; }
      /// <summary>樂觀並行版本（PostgreSQL xmin）；回應給前端，修改時帶回來。</summary>
      uint GetVersion(object entity);
      /// <summary>以前端帶回的版本做並行檢查，並強制整筆標為 Modified（只改 owned 分錄時 EF 不會檢查版本，見 spike S2b）。</summary>
      void ExpectVersion(object entity, uint version);
      Task<int> SaveChangesAsync(CancellationToken cancellationToken);
  }
  ```

  `SixJarsDbContext` 實作這個介面：

  ```csharp
  public uint GetVersion(object entity) => Entry(entity).Property<uint>("xmin").CurrentValue;

  public void ExpectVersion(object entity, uint version)
  {
      var entry = Entry(entity);
      entry.Property<uint>("xmin").OriginalValue = version;
      entry.State = EntityState.Modified;
  }
  ```

  `xmin` 屬性在 T23 才加入 configuration。這個 Task 先只實作 `GetVersion`，`ExpectVersion` 暫時擲出 `NotImplementedException`，在 T23 用測試驅動完成。

- `NotFoundException(string message) : Exception`。
- `ICurrentUser { string Subject { get; } string? Email { get; } }`。`HttpCurrentUser` 從 `IHttpContextAccessor` 讀取 `sub` 與 `email` claim。
- `ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull`：依序執行所有 validator，收集錯誤後一次擲出 `ValidationException`。
- `Application/DependencyInjection.cs`：

  ```csharp
  public static IServiceCollection AddSixJarsApplication(this IServiceCollection services, string? mediatRLicenseKey)
  {
      services.AddMediatR(cfg =>
      {
          // 沒有 key 時 MediatR 只記一筆 warning，開發與測試不受影響（spike S1）；正式環境由 Secret Manager 提供 key。
          cfg.LicenseKey = mediatRLicenseKey;
          cfg.RegisterServicesFromAssemblyContaining<ISixJarsDbContext>();
          cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
      });
      services.AddValidatorsFromAssemblyContaining<ISixJarsDbContext>(includeInternalTypes: true);
      return services;
  }
  ```

- `Application.csproj`：加入 `Microsoft.EntityFrameworkCore`、`MediatR`、`FluentValidation`、`FluentValidation.DependencyInjectionExtensions` 的 PackageReference。
- `Infrastructure/DependencyInjection.cs`：再加一行 `services.AddScoped<ISixJarsDbContext>(sp => sp.GetRequiredService<SixJarsDbContext>());`。
- `BookDto`：

  ```csharp
  public sealed record BookDto(Guid Id, string Name, DateOnly OpeningDate, DateOnly? LockDate,
      IReadOnlyList<AccountDto> Accounts, IReadOnlyList<PlanningFundDto> PlanningFunds, IReadOnlyList<CategoryDto> Categories)
  {
      public static BookDto From(Book book) => ...;
  }
  public sealed record AccountDto(Guid Id, string Name, AccountType Type, decimal OpeningBalance, bool CountsAsAvailableCash);
  public sealed record PlanningFundDto(Guid Id, string Name, decimal OpeningBalance);
  public sealed record CategoryDto(Guid Id, string Name, CategoryKind Kind, ExpenseNature? Nature, Guid? ParentId);
  ```

  `LockDate` 在 T32 之前一律回傳 `null`，T32 再接上 `Book.LockDate`。
- `GetBook(Guid BookId) : IRequest<BookDto>`：handler 用 `AsNoTracking` 查詢，找不到就擲 `NotFoundException`。
- `ListBooks() : IRequest<IReadOnlyList<BookSummaryDto>>`，`BookSummaryDto(Guid Id, string Name)`。T35 之後改為只列出成員所屬的帳本。
- `ApiExceptionHandler : IExceptionHandler`：

  | 例外 | 狀態碼 | 內容 |
  |---|---|---|
  | `ValidationException` | 400 | `ValidationProblemDetails`，依 PropertyName 分組 |
  | `DomainException` | 422 | `extensions["code"] = Code` |
  | `NotFoundException` | 404 | |
  | `DbUpdateConcurrencyException` | 409 | |

  其他例外交給預設的 500 處理。
- `BooksEndpoints`：

  ```csharp
  internal static class BooksEndpoints
  {
      public static RouteGroupBuilder MapBooksEndpoints(this RouteGroupBuilder api)
      {
          api.MapGet("/books", (ISender sender, CancellationToken ct) => sender.Send(new ListBooks(), ct));
          var book = api.MapGroup("/books/{bookId:guid}");
          book.MapGet("/", (Guid bookId, ISender sender, CancellationToken ct) => sender.Send(new GetBook(bookId), ct));
          return book;
      }
  }
  ```

- `Program.cs`：加入下列設定。
  - `AddSixJarsApplication(builder.Configuration["MediatR:LicenseKey"])`
  - `AddHttpContextAccessor()`，並註冊 `AddScoped<ICurrentUser, HttpCurrentUser>()`
  - `AddExceptionHandler<ApiExceptionHandler>()`
  - `ConfigureHttpJsonOptions`：加入 `JsonStringEnumConverter`
  - 認證：`AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; })`。T36 會換成完整設定。
  - `AddAuthorization()`
  - middleware：`app.UseExceptionHandler(); app.UseAuthentication(); app.UseAuthorization();`
  - endpoint：`var api = app.MapGroup("/api").RequireAuthorization(); api.MapBooksEndpoints();`

**Step 4–5**：執行單檔測試，接著跑全部測試。Expected：總計 104、失敗 0。

**Step 6**：commit 訊息：`feat(api): MediatR pipeline、錯誤對應與帳本設定查詢`

---

## Task 19：新增帳戶、財務規劃帳戶、分類

**Files**
- Create: `src/SixJars.Application/Books/{AddAccount,AddPlanningFund,AddCategory}.cs`。每個檔案包含 command、validator、handler。
- Modify: `BooksEndpoints.cs`
- Test: `tests/SixJars.Api.Tests/BookSettingsEndpointsTests.cs`

**Step 1：失敗測試**（5 個）

```csharp
[Fact] Add_account_returns_201_and_shows_up_in_book()
// POST /api/books/{id}/accounts { name: "郵局", type: "Bank", openingBalance: 123 }
// → 201、Location 指向帳本；GET 之後 Accounts 包含「郵局」

[Fact] Blank_account_name_is_400_validation_problem()
// name: "" → 400，errors 含 "Name"

[Fact] Duplicate_account_name_is_422_with_rule_code()
// name: "現金"（seed 已存在）→ 422，code = "rule"

[Fact] Add_sub_category_under_main()
// POST /categories { name: "晚餐", parentId: <主食 Id> } → 201；GET 之後「晚餐」的 ParentId = 主食

[Fact] Add_planning_fund()
// POST /planning-funds { name: "旅遊基金", openingBalance: 0 } → 201
```

測試寫法沿用 T18：`ApiFactory`、`SeedBookAsync`，再用 `PostAsJsonAsync(..., ApiJson.Options)` 送出請求。

**Step 3：實作重點**
- `AddAccount(Guid BookId, string Name, AccountType Type, decimal OpeningBalance, bool CountsAsAvailableCash = true) : IRequest<Guid>`。
  - validator 規則：`Name` NotEmpty、MaximumLength(100)；`Type` IsInEnum。
  - handler 流程：載入 Book（要追蹤變更，不可 AsNoTracking）→ `book.AddAccount(...)` → `SaveChangesAsync` → 回傳新帳戶的 Id。
- `AddCategory(Guid BookId, string Name, CategoryKind Kind, ExpenseNature? Nature, Guid? ParentId)`：
  - 有 `ParentId` 時呼叫 `AddSubCategory`。
  - 沒有時，`Kind == Income` 呼叫 `AddIncomeCategory`；否則呼叫 `AddExpenseCategory`，此時 `Nature` 必填，由 validator 檢查。
- endpoint 回應：`Results.Created($"/api/books/{bookId}", new { id })`。

**Step 5**：Expected：總計 109、失敗 0。

**Step 6**：commit 訊息：`feat(api): 新增帳戶、財務規劃帳戶與分類`

---

## Task 20：交易輸入模型、`TransactionBuilder`、`Transaction.ReplaceWith`

本 Task 不碰 HTTP，只建立「平面 DTO → Domain」的轉換（spec §5），以及交易修改的 Domain 能力。

**Files**
- Create: `src/SixJars.Application/Transactions/{TransactionInput,TransactionInputValidator,TransactionBuilder,TransactionDto}.cs`
- Modify: `src/SixJars.Domain/Transactions/Transaction.cs`
- Test: `tests/SixJars.Application.Tests/Transactions/{TransactionBuilderTests,TransactionInputValidatorTests}.cs`、`tests/SixJars.Domain.Tests/Transactions/TransactionReplaceTests.cs`

**欄位對應**：`TransactionInput` 的欄位名稱沿用 `Transaction` 的屬性名稱。

| Kind | AccountId | CounterAccountId | CategoryId | PlanningFundId | LoanPrincipal | Amount |
|---|---|---|---|---|---|---|
| Income／Expense | 方式帳戶 | — | 必填 | — | — | 非 0，帶正負號 |
| Transfer | 從 | 到（必填） | — | — | — | > 0 |
| Withdrawal／CashDeposit | 銀行 | 現金（必填） | — | — | — | > 0 |
| TopUp | 電子錢包 | 來源（必填） | — | — | — | > 0 |
| CardPayment | 付款帳戶 | 信用卡（必填） | — | — | — | > 0 |
| LoanDisbursement | 入帳帳戶 | 貸款（必填） | — | — | — | > 0 |
| LoanPayment | 付款帳戶 | 貸款（必填） | 選填（利息分類） | — | 必填 | > 0（總額） |
| FundAllocation | 轉入帳戶 | 轉出帳戶（選填） | — | 必填 | — | > 0 |
| FundWithdrawal | 方式帳戶 | — | 選填 | 必填 | — | > 0 |
| FundReturn | 方式帳戶 | — | — | 必填 | — | > 0 |

`TransactionInput(TransactionKind Kind, DateOnly Date, int? BudgetMonth, decimal Amount, Guid AccountId, Guid? CounterAccountId, Guid? CategoryId, Guid? PlanningFundId, decimal? LoanPrincipal, string? Note)`

validator **只檢查形狀**：
- 依上表檢查必填欄位。
- 表中標為「—」的欄位必須為 null，避免前端送了沒有作用的欄位卻以為已經存檔。
- `Note` MaximumLength(500)。
- `BudgetMonth` 若有值，月份必須介於 1 到 12。

正負號與帳戶類型屬於業務規則，交給 `TransactionFactory` 處理，validator 不重複檢查。

**Step 1：失敗測試**

`TransactionBuilderTests`：以 `[Theory]` 加上 `TheoryData<TransactionKind>`，涵蓋全部 12 種類型。每個 case 的流程：
1. 用 Application.Tests 的帳本（若有現成的 builder 就重用；沒有就在測試類別內建一本含所有帳戶類型的 Book）組出合法的 `TransactionInput`。
2. 斷言 `TransactionBuilder.Build(book, input)` 的結果，與直接呼叫對應 `TransactionFactory` 方法的結果，在 `Kind`、`Amount`、`AccountId`、`CounterAccountId`、`CategoryId`、`PlanningFundId`、`LoanPrincipal`、`Postings` 上相等。可用 `BeEquivalentTo(expected, o => o.Excluding(t => t.Id))`。

共 12 個 case。

`TransactionInputValidatorTests`：`[Theory]` 共 6 個 case。
- Transfer 缺 CounterAccountId。
- Income 缺 CategoryId。
- LoanPayment 缺 LoanPrincipal。
- FundReturn 缺 PlanningFundId。
- Expense 卻帶了 PlanningFundId。
- Note 長度 501。

每個 case 都斷言錯誤包含對應的 PropertyName。

`TransactionReplaceTests`（Domain）：

```csharp
[Fact]
public void Replace_keeps_id_and_regenerates_postings()
{
    var s = new SampleBook();
    var original = s.Factory.Expense(new DateOnly(2026, 1, 5), s.Cash.Id, s.Food.Id, -100m);
    var draft = s.Factory.Transfer(new DateOnly(2026, 1, 6), s.Bank.Id, s.Cash.Id, 500m);

    original.ReplaceWith(draft);

    original.Id.Should().NotBe(draft.Id);
    original.Kind.Should().Be(TransactionKind.Transfer);
    original.Date.Should().Be(new DateOnly(2026, 1, 6));
    original.CategoryId.Should().BeNull();
    original.Postings.Should().Equal(new Posting(s.Bank.Id, -500m), new Posting(s.Cash.Id, 500m));
}

[Fact]
public void Replace_with_draft_from_another_book_throws()
{
    var mine = new SampleBook();
    var other = new SampleBook();
    var original = mine.Factory.Expense(new DateOnly(2026, 1, 5), mine.Cash.Id, mine.Food.Id, -100m);

    var act = () => original.ReplaceWith(other.Factory.Expense(new DateOnly(2026, 1, 5), other.Cash.Id, other.Food.Id, -1m));

    act.Should().Throw<DomainException>();
}
```

**Step 3：實作**
- `Transaction.ReplaceWith(Transaction draft)`：
  - 檢查 `draft.BookId == BookId`。
  - 複製 `Kind`、`Date`、`BudgetMonth`、`Amount`、`AccountId`、`CounterAccountId`、`CategoryId`、`PlanningFundId`、`LoanPrincipal`、`Note`。
  - 分錄：`_postings.Clear(); _postings.AddRange(draft.Postings);`。
  - XML doc 註明：draft 必須由 `TransactionFactory` 產生，展開規則因此只存在一處（spec §3.2）。
- `TransactionBuilder.Build(Book book, TransactionInput input)`：`switch (input.Kind)` 呼叫對應的 factory 方法。`BudgetMonth` 以 `BudgetMonth.FromKey` 轉換。必填欄位已由 validator 保證，可以直接 `!.Value`；但仍要加上 `?? throw new DomainException(...)`，因為 CLI 還原時不經過 validator（T42）。
- `TransactionDto(Guid Id, TransactionKind Kind, DateOnly Date, int BudgetMonth, decimal Amount, Guid AccountId, Guid? CounterAccountId, Guid? CategoryId, Guid? PlanningFundId, decimal? LoanPrincipal, decimal? LoanInterest, string? Note, IReadOnlyList<PostingDto> Postings, uint Version)`
  - 另有 `PostingDto(Guid AccountId, decimal Amount)`。
  - 工廠方法 `static TransactionDto From(Transaction t, uint version)`，再加一個 `ToInput()` 方法，供 T42 還原使用。

**Step 5**：Expected：總計 129、失敗 0。

**Step 6**：commit 訊息：`feat(app): 交易輸入模型、TransactionBuilder 與 Transaction.ReplaceWith`

---

## Task 21：新增交易與讀取單筆

**Files**
- Create: `src/SixJars.Application/Transactions/{CreateTransaction,GetTransaction}.cs`、`src/SixJars.Api/Endpoints/TransactionsEndpoints.cs`
- Test: `tests/SixJars.Api.Tests/TransactionsEndpointsTests.cs`

**Step 1：失敗測試**（3 個）
- `Create_expense_returns_201_and_get_returns_postings`：
  - POST `{ kind: "Expense", date: "2026-01-05", amount: -120, accountId: 現金, categoryId: 午餐 }`。
  - 斷言回應 201，body 含 `id` 與 `version`。
  - GET `/transactions/{id}`，斷言 `postings` 等於 `[{現金, -120}]`，`budgetMonth = 202601`。
- `Transfer_to_credit_card_is_422`：轉帳的轉入端是信用卡，違反 P1 規則，斷言 422 且 `code = "rule"`。
- `Transfer_without_counter_account_is_400`。

**Step 3：實作**
- `CreateTransaction(Guid BookId, TransactionInput Input) : IRequest<TransactionDto>`。validator 用 `RuleFor(c => c.Input).SetValidator(new TransactionInputValidator())`。
- handler 流程：
  1. 載入 Book（AsNoTracking 即可，這裡不會修改 Book）。
  2. `TransactionBuilder.Build`。
  3. `db.Transactions.Add`，接著 `SaveChangesAsync`。
  4. 回傳 `TransactionDto.From(t, db.GetVersion(t))`。這裡 `xmin` 還沒建模，先回傳 0；T23 接上。
- `GetTransaction(Guid BookId, Guid TransactionId)`：用 `BookId` 與 `TransactionId` 兩個條件一起查詢，查不到就 404。不可只用 Id 查詢，否則能讀到別本帳的交易。
- endpoint：`MapPost("/transactions", ...)` 回傳 `Results.Created($".../transactions/{dto.Id}", dto)`；`MapGet("/transactions/{transactionId:guid}", ...)`。

**Step 5**：Expected：總計 132、失敗 0。

**Step 6**：commit 訊息：`feat(api): 新增交易與讀取單筆交易`

---

## Task 22：交易清單（排序與篩選）

**Files**
- Create: `src/SixJars.Application/Transactions/ListTransactions.cs`
- Modify: `TransactionsEndpoints.cs`、`src/SixJars.Infrastructure/Persistence/LedgerSnapshotLoader.cs`
- Test: `TransactionsEndpointsTests.cs`（加 3 個）、`tests/SixJars.Infrastructure.Tests/Persistence/LedgerSnapshotLoaderTests.cs`（加 1 個）

**Step 1：失敗測試**
- `List_is_ordered_by_date_then_creation`：
  - 依序新增 1/10、1/05、1/05 三筆交易，第三筆的 note 為 `"後建立"`。
  - 斷言清單順序為：1/05（第二筆）→ 1/05（後建立）→ 1/10。
- `List_filters_by_budget_month`：`?budgetMonth=202602` 只回傳歸屬 2 月的交易，包括日期是 1/30、但歸屬 2 月的那筆。
- `List_filters_by_account_including_counter_account`：`?accountId=現金` 會同時回傳現金作為方式帳戶的支出，以及現金作為轉入端的轉帳。也就是篩選條件是「分錄中有動到該帳戶」。
- `LedgerSnapshotLoaderTests.Transactions_are_ordered_by_date`：P1 交接的注意事項。

**Step 3：實作**
- `ListTransactions(Guid BookId, DateOnly? From, DateOnly? To, int? BudgetMonth, Guid? AccountId)`。
- 帳戶篩選寫成 `t.Postings.Any(p => p.AccountId == id) || t.AccountId == id || t.CounterAccountId == id`。後兩個條件涵蓋「同帳戶圈存」這類沒有分錄的交易。
- 排序：`OrderBy(t => t.Date).ThenBy(t => t.Id)`。`Guid.CreateVersion7` 依時間遞增，PostgreSQL 的 uuid 以位元組比較，結果與建立順序一致。這點要在程式註解中說明。
- 回傳 `IReadOnlyList<TransactionDto>`。版本要用 `db.GetVersion`，所以這裡**不可**用 AsNoTracking；也可以改用投影直接帶出 `EF.Property<uint>(t, "xmin")`，在 T23 之後擇一。P2 不做分頁：個人帳本一年約 2,000 筆，前端依月份查詢即可。
- `LedgerSnapshotLoader`：交易加上 `.OrderBy(t => t.Date).ThenBy(t => t.Id)`。

**Step 5**：Expected：總計 136、失敗 0。

**Step 6**：commit 訊息：`feat(api): 交易清單依日期排序並支援篩選`

---

## Task 23：修改交易與樂觀並行（`xmin`）

**Files**
- Create: `src/SixJars.Application/Transactions/UpdateTransaction.cs`
- Modify: `TransactionConfiguration.cs`、`PlannedExpenseConfiguration.cs`（兩者都加 `xmin`）、`SixJarsDbContext.cs`（`ExpectVersion`），以及 `TransactionsEndpoints.cs`
- Create: migration `AddConcurrencyTokens`
- Test: `TransactionsEndpointsTests.cs`（加 3 個）、`tests/SixJars.Infrastructure.Tests/Persistence/ConcurrencyTests.cs`（1 個）

**Step 1：失敗測試**
- `Update_changes_kind_and_postings_and_bumps_version`：
  - 先建立一筆 Expense，再 PUT 成 Transfer（附上 `version`）。
  - 斷言回應 200、分錄已改為轉帳的兩筆、新的 `version` 與舊的不同。
- `Update_with_stale_version_is_409`：用同一個舊 `version` 連續 PUT 兩次，第二次應回 409。
- `Update_unknown_transaction_is_404`。
- `ConcurrencyTests.Changing_only_postings_still_checks_version`：spike S2b、S2c 的回歸測試。
  1. 載入一筆交易，記下 version。
  2. 用另一個 context 修改這筆交易。
  3. 回到原本的 context，呼叫 `ExpectVersion(t, 舊 version)`，再用 `ReplaceWith` 換成金額相同、只有分錄不同的 draft（例如 FundAllocation 的轉出帳戶從 null 改成銀行）。
  4. 斷言 `SaveChangesAsync` 擲出 `DbUpdateConcurrencyException`。

**Step 3：實作**
- configuration 加入 `builder.Property<uint>("xmin").IsRowVersion();`（spike S2 已驗證）。
- 產生 migration：`dotnet ef migrations add AddConcurrencyTokens ...`。產生的 C# 檔**會**有 `AddColumn<uint>("xmin", type: "xid", rowVersion: true)`，這是正常的。事實查核已確認，Npgsql 產生 SQL 時會略過系統欄位，`dotnet ef migrations script InitialLedger AddConcurrencyTokens` 只會輸出 `__EFMigrationsHistory` 的 INSERT。
  - 驗證方式：執行上面的 script 指令。如果輸出中有 `ALTER TABLE ... ADD xmin`，就停下來回報。
- `SixJarsDbContext.ExpectVersion`：照 T18 的程式碼完成。
- `UpdateTransaction(Guid BookId, Guid TransactionId, uint Version, TransactionInput Input) : IRequest<TransactionDto>`，流程：
  1. 載入要追蹤變更的交易（找不到就 404）。
  2. 載入 Book（AsNoTracking）。
  3. `var draft = TransactionBuilder.Build(book, input)`。
  4. `db.ExpectVersion(transaction, version)`。
  5. `transaction.ReplaceWith(draft)`。
  6. `SaveChangesAsync`。
  7. 回傳新的 DTO 與新的 version。
- 補上 T21、T22 先前回傳 0 的 version。
- endpoint：`MapPut("/transactions/{transactionId:guid}", ...)`，request body 為 `UpdateTransactionBody(uint Version, TransactionInput Input)`。

**Step 5**：Expected：總計 140、失敗 0。

**Step 6**：commit 訊息：`feat(api): 修改交易，以 xmin 做樂觀並行控制`

---

## Task 24：預定支出的建立、清單與修改

**Files**
- Modify: `src/SixJars.Domain/Planning/PlannedExpense.cs`（加入 `Update`）
- Create: `src/SixJars.Application/Planning/{PlannedExpenseDto,CreatePlannedExpense,UpdatePlannedExpense,ListPlannedExpenses}.cs`、`src/SixJars.Api/Endpoints/PlannedExpensesEndpoints.cs`
- Test: `tests/SixJars.Domain.Tests/Planning/PlannedExpenseUpdateTests.cs`（2 個）、`tests/SixJars.Api.Tests/PlannedExpensesEndpointsTests.cs`（3 個）

**Step 1：失敗測試**
- Domain：
  - `Update_changes_unpaid_plan`：月份、分類、帳戶、金額、備註都改得動。
  - `Update_after_paid_throws`：付款之後，金額要以實際交易為準，所以禁止修改預定支出。
- API：
  - `Create_and_list_by_month`：建立 2026-02「保險費」−3000，`GET ?budgetMonth=202602` 回傳 1 筆，且 `isPaid = false`。
  - `Create_with_floating_category_is_422`：沿用 P1 的規則，預定支出只限固定、貸款、特別支出。
  - `Update_with_stale_version_is_409`。

**Step 3：實作**
- `PlannedExpense.Update(Book book, BudgetMonth month, CategoryId categoryId, AccountId? accountId, decimal estimatedAmount, string? note)`：重用 `Create` 中的驗證，把它抽成 private static 方法 `Validate(book, categoryId, accountId)`。已付款時擲出 `DomainException`。
- `PlannedExpenseDto(Guid Id, int BudgetMonth, Guid CategoryId, Guid? AccountId, decimal EstimatedAmount, string? Note, Guid? PaidTransactionId, bool IsPaid, uint Version)`。
- 並行控制與 T23 相同。

**Step 5**：Expected：總計 145、失敗 0。

**Step 6**：commit 訊息：`feat(api): 預定支出的建立、清單與修改`

---

## Task 25：預定支出付款

**Files**
- Create: `src/SixJars.Application/Planning/PayPlannedExpense.cs`
- Modify: `PlannedExpensesEndpoints.cs`
- Test: `PlannedExpensesEndpointsTests.cs`（加 3 個）

**付款請求**：`PayPlannedExpenseBody(uint Version, DateOnly Date, Guid AccountId, decimal Amount, Guid? LoanAccountId, decimal? LoanPrincipal)`
- `Amount` 是實際金額，沿用預定支出的符號慣例（負數）。
- 支出性質是貸款時，`LoanAccountId` 與 `LoanPrincipal` 必填，handler 建立 `LoanPayment(total = -Amount, principal)`。這與 P1 匯入制式表格的規則相同（`MappingSession.Templates`）。
- 其他性質建立 `Expense(Amount)`，分類用預定支出的分類。
- 交易的歸屬月份 = 預定支出的歸屬月份，**不是**付款日期所在的月份。

**Step 1：失敗測試**
- `Pay_fixed_expense_creates_expense_and_links_it`：
  - 付款後，預定支出的 `isPaid = true`、`paidTransactionId` 指向新建立的交易。
  - 該交易是 Expense，金額等於實際金額，`budgetMonth` 等於預定支出的歸屬月份。
- `Pay_loan_creates_loan_payment_with_principal`：
  - 實際金額 −30000、本金 20000。
  - 斷言交易為 LoanPayment，`amount = 30000`，`loanInterest = 10000`。
- `Pay_twice_is_422`。

**Step 3：實作**

handler 在同一次 `SaveChangesAsync` 內完成兩件事：`db.Transactions.Add(transaction)` 與 `planned.MarkPaid(transaction)`，因此兩者會一起成功或一起失敗。支出性質要經由 `book.GetCategory(planned.CategoryId).Nature` 判斷。

**Step 5**：Expected：總計 148、失敗 0。

**Step 6**：commit 訊息：`feat(api): 預定支出付款，建立並連結交易`

---

## Task 26：SQL 餘額彙總與 oracle 測試

spec §6 是 P2 最主要的正確性保證：**SQL 算出的每個數字，都必須與 P1 計算器一致**。

**Files**
- Create: `src/SixJars.Application/Ledger/{ILedgerSummaryQuery,BalanceCutoff}.cs`、`src/SixJars.Infrastructure/Ledger/SqlLedgerSummaryQuery.cs`
- Modify: `Infrastructure/DependencyInjection.cs`（註冊查詢）
- Test: `tests/SixJars.Infrastructure.Tests/Ledger/SqlLedgerSummaryQueryTests.cs`（5 個）
- Modify: `tests/SixJars.Infrastructure.Tests/SixJars.Infrastructure.Tests.csproj`，加入 `<Compile Include="..\SixJars.Domain.Tests\SampleBook.cs" Link="Shared\SampleBook.cs" />`。`SampleBook` 是 `internal`，以 link 方式編譯進來，不需要改成 public。

**介面**

```csharp
namespace SixJars.Application.Ledger;

/// <summary>餘額截止方式：依日期（資產負債表）或依歸屬月份（月報表與驗收；P1 spec §4.4）。</summary>
public abstract record BalanceCutoff
{
    public sealed record AsOf(DateOnly Date) : BalanceCutoff;
    public sealed record ThroughBudgetMonth(BudgetMonth Month) : BalanceCutoff;
}

/// <summary>在資料庫端彙總分錄；回傳的是「分錄加總」，期初餘額由呼叫端從 Book 加上。</summary>
public interface ILedgerSummaryQuery
{
    Task<IReadOnlyDictionary<AccountId, decimal>> PostingTotalsAsync(BookId bookId, BalanceCutoff cutoff, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<PlanningFundId, decimal>> FundDeltaTotalsAsync(BookId bookId, BalanceCutoff cutoff, CancellationToken cancellationToken);
    /// <summary>每個歸屬月份的月可用餘額（含未付預定支出）；沒有資料的月份為 0。</summary>
    Task<IReadOnlyDictionary<BudgetMonth, decimal>> MonthlyDisposableAsync(Book book, BudgetMonth from, BudgetMonth to, CancellationToken cancellationToken);
}
```

**Step 1：失敗測試**

測試類別內建立一本 `SampleBook`，涵蓋全部 12 種交易類型。月份分布：
- 1 月、2 月都有交易。
- 至少一筆的歸屬月份與日期所在月份不同（1/30 領薪，歸屬 2 月）。
- 有電子錢包的消費。
- 有同帳戶圈存的入新資金。
- 一筆已付款、一筆未付款的預定支出。

全部寫進 DB 後：
- **SQL 路徑**：透過 `SqlLedgerSummaryQuery` 計算。
- **Oracle 路徑**：`LedgerSnapshotLoader` 載入快照，再交給 P1 的計算器。

測試案例（5 個）：
1. `Posting_totals_as_of_date_match_balance_calculator`：分別以 1/15、1/31、2/28 截止，每個帳戶的 `opening + total` 都等於 `AccountBalanceAsOf`。
2. `Posting_totals_through_budget_month_match_balance_calculator`：以 2026-01 與 2026-02 截止。
3. `Fund_totals_match_balance_calculator`：兩種截止方式都比對。
4. `Monthly_disposable_matches_calculator`：1 月到 3 月逐月比對，3 月沒有資料，應為 0。
5. `Totals_only_include_the_requested_book`：另建一本帳寫入交易，確認不會被算進來。

**Step 3：實作**（`SqlLedgerSummaryQuery(SixJarsDbContext db)`）
- **分錄加總**：

  ```csharp
  db.Transactions.Where(t => t.BookId == bookId).Where(cutoff 條件)
    .SelectMany(t => t.Postings)
    .GroupBy(p => p.AccountId)
    .Select(g => new { g.Key, Total = g.Sum(p => p.Amount) })
  ```

  spike S3 已確認這會翻成單一條 `GROUP BY`。cutoff 條件依類型分別為 `t.Date <= d` 或 `t.BudgetMonth <= m`。`BudgetMonth` 經過 value converter，比較運算子要翻成 SQL 的 `<=`；若翻譯失敗，改成比較 key 的 shadow 欄位，並把這個偏差記錄下來。
- **財務規劃帳戶**：`Where(t => t.PlanningFundId != null)`，再依 `PlanningFundId` 與 `Kind` 分組，在 C# 端套用 `FundDelta` 的符號：`FundAllocation` 與 `FundReturn` 為正，`FundWithdrawal` 為負。
- **月可用餘額**：
  1. 先在 C# 端從 `book.Accounts` 取出電子錢包的 Id 清單 `walletIds`。
  2. 交易查詢：`Where(BookId 相符且 BudgetMonth 介於 from 與 to)`，再依 `BudgetMonth` 與 `Kind` 分組，`Sum(t => t.Kind == Expense && walletIds.Contains(t.AccountId) ? 0 : t.Amount)`，在 C# 端依 `Kind` 套用 P1 §4.1 的正負號。
  3. 預定支出查詢：`Where(!IsPaid)`，依月份分組加總。
  4. 合併兩者的結果，並把範圍內沒有資料的月份補 0。

  `IsPaid` 是計算屬性，EF 翻譯不了，要改寫成 `p.PaidTransactionId == null`。
- 合計 4 個 SQL 命令。T27 的往返次數測試以此為準。

**Step 5**：Expected：總計 153、失敗 0。

**Step 6**：commit 訊息：`feat(infra): SQL 餘額彙總，以 P1 計算器為 oracle 驗證`

---

## Task 27：`/summary` endpoint 與 Excel 驗收（SQL 版）

**Files**
- Create: `src/SixJars.Application/Ledger/{LedgerSummaryDto,GetLedgerSummary}.cs`、`src/SixJars.Api/Endpoints/SummaryEndpoints.cs`
- Create: `tests/Shared/CommandCounter.cs`
- Test: `tests/SixJars.Api.Tests/SummaryEndpointTests.cs`（2 個）、`tests/SixJars.AcceptanceTests/SqlLedgerAcceptanceTests.cs`（3 個，`reference/` 不存在時略過）

**DTO**

```csharp
public sealed record LedgerSummaryDto(
    int BudgetMonth, DateOnly AsOf,
    decimal MonthlyDisposable, decimal YearToDate,
    decimal AvailableCash, decimal AvailableCashWithEWallets,
    IReadOnlyList<BalanceDto> Accounts, IReadOnlyList<BalanceDto> PlanningFunds);
public sealed record BalanceDto(Guid Id, string Name, decimal Balance);
```

`GET /summary?budgetMonth=202603&asOf=2026-03-31`：
- `asOf` 省略時，以 `Asia/Taipei` 的今天為準（`TimeProvider` 注入，方便測試）。
- 年累計 = `MonthlyDisposableAsync(book, 該年 1 月, budgetMonth)` 各月加總。月可用餘額直接取同一份結果中的當月數字，不必再查一次。

**Step 1：失敗測試**
- `Summary_matches_domain_calculators`：
  - 透過 API 建立數筆交易與一筆未付的預定支出。
  - 斷言 `/summary` 的每個欄位，都等於「`LedgerSnapshotLoader` 載入的快照 → P1 計算器」得到的數字。
- `Summary_uses_at_most_five_database_round_trips`：
  - `CommandCounter : DbCommandInterceptor`，覆寫 `ReaderExecutingAsync` 與 `ScalarExecutingAsync` 來計數。
  - 在 factory 的 `ConfigureTestServices` 中以 `services.AddSingleton<IInterceptor>(counter)` 註冊，會被 T17 的 DI 自動掛上。
  - 呼叫 `/summary` 前先歸零，呼叫後斷言計數 ≤ 5：Book 1 次 + 分錄 1 次 + 財務規劃帳戶 1 次 + 交易 1 次 + 預定支出 1 次。

  > 與 spec 的偏差：spec §5 寫「預期 ≤ 3」。實際上需要 5 次，因為 Book 的設定、兩種餘額、月可用餘額的兩個來源各需一次查詢。每次約數十毫秒，總計約 150ms，仍然可以接受。執行後用 `docs(plans):` 回寫這個偏差。
- `SqlLedgerAcceptanceTests.Sql_summary_matches_excel(month)`：
  - 複製 P1 `PersistedLedgerAcceptanceTests` 的寫入流程。
  - 新增 `MonthFigureComparison.CompareSql(...)`：以 `SqlLedgerSummaryQuery` 的 `ThroughBudgetMonth` 結果加上期初，與 Excel 比對。比對的指標與 `KnownExcelDifferences` 的調整都和 P1 相同。
  - **不可**放寬容許誤差。

**Step 5**：Expected：總計 158、失敗 0（`reference/` 不存在時略過 15）。

**Step 6**：commit 訊息：`feat(api): 帳務摘要 endpoint 與 SQL 版 Excel 驗收`

### Checkpoint C

- 主控者重跑全部測試與 build。期望：總計 158、失敗 0。
- **變異測試**，至少做以下三項，每項都必須讓某個測試失敗：
  - 把 SQL 月可用餘額中 TopUp 的正負號反轉。
  - 拿掉 `ExpectVersion` 中的 `State = Modified`。
  - 拿掉 `GetTransaction` 的 BookId 條件。
- 比對 diff 與本計畫；以 `git diff master --stat` 與 grep 掃描機密（`Password=`、`ClientSecret`、`LicenseKey`）。
- 用 `docs(plans):` 回寫偏差，**停下來讓使用者檢視**。

### 段 C 執行紀錄（2026-10-04，雲端 container）

執行方式：每批由一個 subagent 執行，依序為 T17｜T18–19｜T20–21｜T22–23｜T24–25｜T26–27。每批交付後，主控者都獨立做一次審查：
- 重跑 build 與全部測試。
- 比對 diff 與計畫。
- 做變異測試。
- 補上缺漏的測試。

Checkpoint C 結束時：總計 **190**、失敗 0、略過 15（`reference/` 不存在：P1 的 12 個，加上 T27 的 3 個）；build 0 warning；機密掃描乾淨。

**測試總數與計畫不同**：審查與 subagent 都額外加了測試，所以實際總數比計畫多。後續段落的 Expected 一律以「計畫值 + 32」為準，T28 起算，例如 T28 為 196。

| Task | 計畫 | 實際 | 差異的來源 |
|---|---|---|---|
| T17 | 100 | 101 | 審查補測：`/health` 在資料庫無法連線時回 503 |
| T18–19 | 109 | 111 | 審查補測：支出主分類缺 Nature 時回 400 |
| T20–21 | 132 | 144 | 審查補測：跨帳本讀取回 404、不存在回 404、validator 7 個案例、選填欄位可以留空 |
| T22–23 | 140 | 157 | subagent 加 2 個（跨帳本篩選、跨帳本 PUT）；審查補 3 個（日期區間、同帳戶圈存、修改後的同日排序） |
| T24–25 | 148 | 176 | subagent 加 8 個；審查補 3 個（付款版本過舊回 409、兩個清單的月份不合法回 400） |
| T26–27 | 158 | 190 | subagent 在 `/summary` 加 4 個（時區、月份不合法、帳本不存在） |

**審查時抓到、已經修正的問題**
- `GET /transactions` 與 `GET /planned-expenses` 帶 `?budgetMonth=202613` 時，`BudgetMonth.FromKey` 擲例外，回 500。已補上 query validator，改回 400（`5b677f3`）。
- 下列變異在審查前沒有測試能抓到，現在都已經補測：
  - `GetTransaction` 拿掉 BookId 條件。
  - validator 中「不使用的欄位必須為 null」與月份範圍兩條規則。
  - 清單的 from／to 篩選，以及同帳戶圈存的篩選。
  - 付款時的 `ExpectVersion`。
  - `/health` 的資料庫檢查。

**與計畫的偏離**（以 committed code 為準）
- **`GetVersion`／`ExpectVersion`**：改用非泛型的 `Property("xmin")`。`Entry(object)` 回傳的是非泛型的 `EntityEntry`，照計畫的泛型寫法會出現 CS0308。
- **T23 的 `ConcurrencyTests`**：計畫的例子是「把 FundAllocation 的轉出帳戶從 null 改成銀行」，但轉出帳戶是交易本身的欄位，EF 本來就會檢查版本，這個例子鎖不住 S2b。實作改成用與現有內容完全相同的 draft 去 `ReplaceWith`，讓變更只發生在分錄上；另一個 context 用 `ExecuteUpdateAsync` 改 Note。已確認拿掉 `State = Modified` 時，這個測試會失敗。
- **讀取 version**：Get 與 List 改成追蹤查詢，再呼叫 `db.GetVersion`。不用 `EF.Property(t, "xmin")` 投影，這樣 Application 層不會出現 provider 專屬的字串。
- **新增檔案**：
  - `Books/BookLoading.cs`：`GetBookForUpdateAsync` 與 `GetBookAsNoTrackingAsync`。
  - `Planning/PlannedExpenseInput.cs`：Create 與 Update 共用的輸入與 validator。
  - `Ledger/LedgerBalances.cs`：「期初 + 加總」與可用現金的篩選，handler 與 SQL 版驗收共用。
- **endpoint 的 body 形狀**：
  - 帳本設定類 endpoint 直接綁定 command，再以 `with { BookId = 路由值 }` 覆寫。
  - 新增交易的 body 是平面的 `TransactionInput`。
  - PUT 一律是 `{ version, input }`。
- **付款**：
  - 回 201，body 是 `PayPlannedExpenseResult(PlannedExpense, Transaction)`，兩者都帶最新版本。
  - 貸款性質卻缺少貸款帳戶或本金時，回 422，code 為 `rule`。
  - 非貸款性質卻帶了貸款欄位時，也回 422。這條規則計畫沒有寫，是比照交易輸入「不使用的欄位必須留空」的慣例。
- **`/summary`**：
  - 實測 5 次資料庫往返，與計畫 T27 的預估一致；spec §5 寫的是 ≤ 3。
  - 帳戶、財務規劃帳戶、可用現金依 `asOf` 日期截止；月可用餘額與年累計依歸屬月份。
  - `asOf` 省略時取 `Asia/Taipei` 的今天，時間由注入的 `TimeProvider` 提供；測試用自寫的 `FixedTimeProvider`，沒有新增套件。
- **`MonthFigureComparison`**：重構成 `Compare` 與 `CompareSqlAsync` 共用同一個比對核心。指標、`KnownExcelDifferences` 的調整、取到小數兩位的比對都不變；主控者已逐行核對。這條路徑在雲端跑不到，**要在有 `reference/` 的本機跑過一次才算驗證完成**。
- **`ApiFactory`**：加入可選的 `testServices` 參數，用來注入 `CommandCounter` 與固定時鐘。

**等價變異**：拿掉 `ListTransactions` 的 `ThenBy(Id)` 不會讓任何測試失敗。這不是測試缺口：交易一併載入 owned 分錄集合時，EF 會自動在 SQL 加上 `ORDER BY t."Id"`，已檢查 SQL 確認。仍保留排序測試，日後改成投影查詢時可以抓到問題。

**留給後續段落的注意事項**
- **T28**：軟刪除後，SQL 彙總必須排除已刪除的資料。T26 的 oracle 資料要加上已刪除的交易與預定支出（計畫已有這個測試）。
- **T44**：省略 `asOf` 時需要 `Asia/Taipei` 時區。Docker image 若改用 chiseled 或 alpine，要確認有 tzdata。
- **前端 spec**：validation 錯誤 `errors` 的 key 是 PascalCase，而且新增或修改交易時帶有 `Input.` 前綴，例如 `Input.CounterAccountId`。**這點待使用者決定**。
- 建立預定支出時，回應的 `Location` 指向 `/planned-expenses/{id}`，但目前沒有讀取單筆的 GET。

---
## 段 D：軟刪除、稽核記錄、鎖帳日

## Task 28：軟刪除（Domain 與 query filter）

**Files**
- Modify: `Transaction.cs`、`PlannedExpense.cs`、`TransactionConfiguration.cs`、`PlannedExpenseConfiguration.cs`
- Create: migration `AddSoftDelete`
- Test: `tests/SixJars.Domain.Tests/Transactions/TransactionDeleteTests.cs`（3 個）、`tests/SixJars.Domain.Tests/Planning/PlannedExpenseDeleteTests.cs`（1 個）、`tests/SixJars.Infrastructure.Tests/Persistence/SoftDeleteTests.cs`（1 個）、`SqlLedgerSummaryQueryTests.cs`（加 1 個）

**Step 1：失敗測試**
- `Delete_sets_deleted_at`：`t.Delete(at)` 之後 `IsDeleted` 為 true，且 `DeletedAt == at`。
- `Delete_twice_throws`。
- `Replace_deleted_transaction_throws`：spec §3.3 規定，已刪除的交易不能修改。
- `PlannedExpense_delete_sets_deleted_at`：已付款的預定支出也可以刪除，因為刪除的是「計畫」本身，不影響已建立的交易。
- `SoftDeleteTests.Deleted_rows_are_hidden_by_default_but_kept`：
  - 一般查詢看不到已刪除的資料。
  - `IgnoreQueryFilters()` 看得到。
  - `LedgerSnapshotLoader` 也看不到。
- `SqlLedgerSummaryQueryTests.Deleted_transactions_and_plans_are_excluded`：
  - 在 T26 的資料上各刪除一筆交易與一筆未付的預定支出。
  - SQL 結果仍然等於 oracle。快照會套用 query filter，所以 oracle 本來就看不到已刪除的資料。

**Step 3：實作**
- 在 `Transaction` 與 `PlannedExpense` 加入：
  - 屬性：`public DateTimeOffset? DeletedAt { get; private set; }`、`public bool IsDeleted => DeletedAt is not null;`
  - 方法 `Delete(DateTimeOffset at)`：已刪除的資料再刪一次就擲出 `DomainException`。
  - `ReplaceWith` 與 `PlannedExpense.Update` 開頭都加上同樣的已刪除檢查。
- configuration 加入 `builder.HasQueryFilter(t => t.DeletedAt == null);`。不使用 EF 10 的 named filter，因為目前只有一個 filter。
- `SqlLedgerSummaryQuery` 都經由 `DbSet` 查詢，所以會自動套用 filter。在類別的 XML doc 註明：**如果將來改用 `FromSql`，必須自行加上 `"DeletedAt" IS NULL`**（ADR 0006）。

**Step 5**：Expected：總計 164、失敗 0。

**Step 6**：commit 訊息：`feat(domain): 交易與預定支出的軟刪除`

---

## Task 29：DELETE endpoints，以及刪除付款交易時解除連結（O2）

**Files**
- Modify: `PlannedExpense.cs`（加入 `MarkUnpaid`）
- Create: `src/SixJars.Application/Transactions/DeleteTransaction.cs`、`src/SixJars.Application/Planning/DeletePlannedExpense.cs`
- Modify: 兩個 endpoint 檔案
- Test: `tests/SixJars.Domain.Tests/Planning/PlannedExpenseUnpayTests.cs`（1 個）、API 測試（4 個）

**Step 1：失敗測試**
- Domain：`MarkUnpaid_clears_link`。
- API（`DELETE` 一律以 `?version=` query string 帶入版本）：
  - `Delete_transaction_is_204_and_disappears_from_list_and_summary`。
  - `Delete_transaction_with_stale_version_is_409`。
  - `Deleting_payment_transaction_reverts_plan_to_unpaid`：
    - 先付款，再刪除那筆交易。
    - 預定支出回到 `isPaid = false`。
    - `/summary` 的月可用餘額回到以預估金額計算（O2，spec §9）。
  - `Delete_planned_expense_is_204`。

**Step 3：實作**

`DeleteTransaction` handler：
1. 載入交易，`ExpectVersion`。
2. `transaction.Delete(timeProvider.GetUtcNow())`。
3. 查出 `PaidTransactionId == 這筆交易` 的預定支出，呼叫 `MarkUnpaid()`。
4. 在同一次 `SaveChangesAsync` 寫入。

`TimeProvider` 在 Api 與 CLI 註冊 `TimeProvider.System`；測試用 `FakeTimeProvider` 或固定時間。

**Step 5**：Expected：總計 169、失敗 0。

**Step 6**：commit 訊息：`feat(api): 刪除交易與預定支出；刪除付款交易時預定支出回到未付`

---

## Task 30：稽核記錄（ADR 0006）

**Files**
- Create: `src/SixJars.Application/Auditing/{AuditEntry,AuditAction,IAuditTrail,AuditTrail,AuditSnapshots}.cs`、`src/SixJars.Infrastructure/Persistence/AuditEntryConfiguration.cs`
- Modify: `ISixJarsDbContext`（加入 `DbSet<AuditEntry> AuditEntries`）、`SixJarsDbContext`，以及所有寫入用的 handler（T19、T21、T23、T24、T25、T29）
- Create: migration `AddAuditEntries`
- Test: `tests/SixJars.Api.Tests/AuditTrailTests.cs`（12 個）

**模型**

```csharp
namespace SixJars.Application.Auditing;

public enum AuditAction { Create, Update, Delete, Import, Restore, LockDateChanged }

/// <summary>一次寫入的不可變紀錄（ADR 0006）；Before／After 是 Domain 物件的完整 JSON 快照。</summary>
public sealed class AuditEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid BookId { get; init; }
    public required DateTimeOffset At { get; init; }
    public required string ActorSubject { get; init; }
    public required AuditAction Action { get; init; }
    public required string EntityType { get; init; }
    public required Guid EntityId { get; init; }
    public string? Before { get; init; }
    public string? After { get; init; }
}

public interface IAuditTrail
{
    /// <summary>加入 DbContext，與業務資料在同一次 SaveChanges 寫入；不會自己存檔。</summary>
    void Record<T>(Guid bookId, AuditAction action, string entityType, Guid entityId, T? before, T? after);
}
```

- `AuditEntry` 放在 Application，不放 Domain：它不是業務概念，而是寫入用的副產品。
- `AuditTrail(ISixJarsDbContext db, ICurrentUser user, TimeProvider clock)`：用 `JsonSerializer.Serialize(value, AuditSnapshots.Options)` 序列化快照。`AuditSnapshots.Options` 要設定 `JsonStringEnumConverter`，並設 `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`，讓中文直接可讀，不轉成 `\uXXXX`。
- 快照型別直接重用 API DTO：`TransactionDto`（含分錄）、`PlannedExpenseDto`、`AccountDto`、`PlanningFundDto`、`CategoryDto`。DTO 中的 `Version` 欄位也一併寫入，沒有影響。
- configuration：`Before` 與 `After` 都設為 `HasColumnType("jsonb")`（spike S5），`Action` 存成字串，並建立索引 `(BookId, EntityId)`。

**Step 1：失敗測試**
- `Each_write_endpoint_leaves_exactly_one_entry`：`[Theory]` 共 10 個 case，每個 case 指定一組（操作、預期的 Action、預期的 EntityType）：

  | 操作 | Action | EntityType |
  |---|---|---|
  | 新增帳戶 | Create | Account |
  | 新增財務規劃帳戶 | Create | PlanningFund |
  | 新增分類 | Create | Category |
  | 新增交易 | Create | Transaction |
  | 修改交易 | Update | Transaction |
  | 刪除交易 | Delete | Transaction |
  | 新增預定支出 | Create | PlannedExpense |
  | 修改預定支出 | Update | PlannedExpense |
  | 刪除預定支出 | Delete | PlannedExpense |
  | 預定支出付款 | Update | PlannedExpense |

  - 預定支出付款會留下兩筆記錄：交易的 Create 與預定支出的 Update。這個 case 改為斷言「該預定支出恰好有一筆記錄」。
  - 刪除付款交易時，因為解除連結，還會多一筆預定支出的 Update。這屬於 O2 的行為，在 T29 的情境中另外斷言。
  - 斷言方式：用 DbContext `IgnoreQueryFilters` 讀取 `AuditEntries`，計算操作前後新增的筆數與內容。`ActorSubject` 必須等於 `ApiFactory.DefaultSubject`。
- `Failed_write_leaves_no_entry`：送出一筆會回 422 的交易，`AuditEntries` 的筆數不變。
- `Update_entry_has_before_and_after_snapshots`：
  - 把交易金額從 −100 改成 −150。
  - 解析 `Before.amount` 應為 −100，`After.amount` 應為 −150。
  - `After.postings[0].amount` 應為 −150：快照必須包含分錄。

**Step 3：實作**

在每個寫入 handler 的 `SaveChangesAsync` 之前呼叫 `audit.Record(...)`。

- Update：`before` 必須在 `ReplaceWith` **之前**用 `TransactionDto.From(t, oldVersion)` 取得，否則會拿到修改後的內容。
- Delete：`after` 為 null。

**Step 5**：Expected：總計 181、失敗 0。

**Step 6**：commit 訊息：`feat(app): 稽核記錄，保存每次寫入的修改前後快照`

---

## Task 31：查詢修改歷史

**Files**
- Create: `src/SixJars.Application/Auditing/GetAuditHistory.cs`、`src/SixJars.Api/Endpoints/AuditEndpoints.cs`
- Test: `AuditTrailTests.cs`（加 1 個）

**Step 1**：`History_lists_entries_oldest_first`：新增、修改、刪除同一筆交易後，`GET /audit?entityId=` 依序回傳 Create、Update、Delete。

**Step 3**：`AuditEntryDto(Guid Id, DateTimeOffset At, string ActorSubject, AuditAction Action, string EntityType, Guid EntityId, JsonElement? Before, JsonElement? After)`。
- 查詢條件是 `BookId` 加上 `EntityId`，依 `At` 排序，再依 `Id` 排序。
- `Before`／`After` 用 `JsonDocument.Parse` 轉成 `JsonElement` 回傳，前端拿到的是物件，不是跳脫過的字串。

**Step 5**：Expected：總計 182、失敗 0。

**Step 6**：commit 訊息：`feat(api): 查詢單筆資料的修改歷史`

---

## Task 32：鎖帳日（Domain）

**Files**
- Modify: `Book.cs`、`BookConfiguration.cs`、`BookDto`（接上 `LockDate`）
- Create: migration `AddLockDate`
- Test: `tests/SixJars.Domain.Tests/Books/LockDateTests.cs`（6 個）

**Step 1：失敗測試**
- `EnsureUnlocked(date)`：`[Theory]` 3 個 case，鎖帳日都是 1/31。
  - 1/30：擲出例外，`Code == "locked"`。
  - 1/31：擲出例外（含當日）。
  - 2/1：通過。
- `Month_ending_on_lock_date_is_locked`：鎖帳日 1/31 時，2026-01 被鎖住。
- `Month_after_lock_date_is_open`：鎖帳日 1/30 時，2026-01 仍然開放，因為 1/31 還沒鎖。
- `Clearing_lock_date_unlocks_everything`：`SetLockDate(null)` 之後，任何日期都可以寫入。

**Step 3：實作**

```csharp
/// <summary>此日（含）以前的交易不可再新增、修改或刪除（CONTEXT.md 鎖帳日）。可前移、後移或清除（spec §9 O3）。</summary>
public DateOnly? LockDate { get; private set; }

public void SetLockDate(DateOnly? lockDate) => LockDate = lockDate;

public void EnsureUnlocked(DateOnly date)
{
    if (LockDate is { } lockDate && date <= lockDate)
    {
        throw new DomainException($"{date:yyyy-MM-dd} 在鎖帳日 {lockDate:yyyy-MM-dd}（含）以前，不可異動。", DomainException.LockedCode);
    }
}

/// <summary>預定支出以歸屬月份的最後一天判斷（spec §3.1）。</summary>
public void EnsureUnlocked(BudgetMonth month) =>
    EnsureUnlocked(new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month)));
```

**Step 5**：Expected：總計 188、失敗 0。

**Step 6**：commit 訊息：`feat(domain): 鎖帳日`

---

## Task 33：強制執行鎖帳日，以及設定鎖帳日的 endpoint

**Files**
- Create: `src/SixJars.Application/Books/SetLockDate.cs`
- Modify: 交易與預定支出的所有寫入 handler、`BooksEndpoints.cs`
- Test: `tests/SixJars.Api.Tests/LockDateEndpointsTests.cs`（6 個）

**Step 1：失敗測試**（設定鎖帳日 2026-01-31）
- `Set_lock_date_is_recorded_in_book_and_audit`：`PUT /lock-date { lockDate: "2026-01-31" }`，之後 GET 帳本的 `lockDate` 為 2026-01-31，並且留下一筆 `LockDateChanged` 的稽核記錄，EntityType 為 `Book`。
- `Create_transaction_in_locked_period_is_422_locked`。
- `Moving_transaction_into_locked_period_is_422`：原日期 2/5，改成 1/20。
- `Moving_transaction_out_of_locked_period_is_422`：原日期 1/20，改成 2/5。這筆交易要在設定鎖帳日之前建立。
- `Deleting_locked_transaction_is_422`。
- `Planned_expense_in_locked_month_cannot_be_paid_or_changed`：預定支出歸屬 2026-01。

**Step 3：實作**

每個寫入 handler 在載入 Book 之後、修改之前呼叫：
- 交易：`book.EnsureUnlocked(input.Date)`；修改與刪除時，再加上 `book.EnsureUnlocked(existing.Date)`。
- 預定支出：`book.EnsureUnlocked(month)`；修改時，原本的月份與新的月份都要檢查。
- 付款：檢查交易日期，以及預定支出的歸屬月份。

`SetLockDate(Guid BookId, DateOnly? LockDate)`：稽核記錄的 Before／After 為 `{ lockDate }`。

**Step 5**：Expected：總計 194、失敗 0。

**Step 6**：commit 訊息：`feat(api): 強制執行鎖帳日並提供設定 endpoint`

### Checkpoint D

- 全綠，總計 194。
- 變異測試，每項都必須讓某個測試失敗：
  - 拿掉 query filter。
  - 拿掉修改交易時對「原日期」的鎖帳日檢查。
  - 把 Update 的 `before` 快照改到 `ReplaceWith` 之後才取得。
- 用 `docs(plans):` 回寫偏差，停下來讓使用者檢視。

### 段 D 執行紀錄（2026-10-04，雲端 container）

**開始前，先依使用者對段 C 待決事項的決定修正**（`46e7b20`）：
- validation 錯誤 `errors` 的 key 改成 body 欄位名稱：拿掉 `Input.` 前綴，每段轉 camelCase（`ApiExceptionHandler.ToBodyFieldName`）。新增與修改共用同一套 key。
- 新增 `GET /planned-expenses/{id}`，建立時回傳的 `Location` 可讀；別本帳回 404。
- 付款 API 的設計（201、貸款欄位的 422 規則）使用者確認照現狀。

執行方式：每批一個 subagent，依序為 T28–29｜T30–31｜T32–33；每批交付後主控者重跑測試、比對 diff、做變異測試、補缺漏的測試。

Checkpoint D 結束時：總計 **243**、失敗 0、略過 15；build 0 warning；機密掃描乾淨，沒有 `reference/` 檔案。段 E 起的 Expected 一律以「計畫值 + 49」為準，例如 T34 的 Expected 要加 49。

| Task | 計畫 | 實際 | 差異的來源 |
|---|---|---|---|
| 段 C 收尾 | — | 193 | 錯誤 key、單筆 GET 共 3 個 |
| T28 | 164 | 201 | subagent 加 2 個：已刪除的預定支出不可修改、不可付款 |
| T29 | 169 | 211 | subagent 加 6 個（`MarkUnpaid` 兩種擲例外、跨帳本刪除 404 等）；審查補 1 個：刪除預定支出時版本過舊回 409 |
| T30–31 | 182 | 227 | subagent 加 2 個（失敗寫入的 409 case、快照序列化單元測試）；審查補 1 個：操作者取自登入的使用者 |
| T32–33 | 194 | 243 | subagent 加 4 個：清除鎖帳日、預定支出搬進已鎖月份、付款日期已鎖但月份開放、刪除連結到已鎖月份預定支出的付款交易 |

**審查時抓到、已補測的缺口**
- 拿掉 `DeletePlannedExpense` 的 `ExpectVersion`，原本沒有測試失敗（`8273c0c`）。
- 把 `AuditTrail` 的 `ActorSubject` 寫死成 `DefaultSubject`，原本沒有測試失敗，因為所有測試都用預設使用者登入（`3cf0ef7`）。

**Checkpoint D 變異測試**（主控者獨立執行，全部有測試失敗）：拿掉 Transaction 的 query filter（4 個失敗）、拿掉修改交易時的原日期檢查、`before` 改到 `ReplaceWith` 之後取得、鎖帳日邊界 `<=` 改成 `<`（6 個失敗）。subagent 另外對每個鎖帳日檢查點各做一次變異，全部被抓到。

**等價變異**（不是測試缺口）
- `DeleteTransaction` 解除連結時拿掉 `p.BookId == bookId`：交易 Id 全域唯一，付款時也已要求同一本帳，保留當防護。
- 鎖帳日檢查挪到修改或 `audit.Record` 之後：例外發生在 `SaveChanges` 之前，DbContext 又是 scoped，從 HTTP 層觀察不到差別，只靠程式碼順序保證。
- 拿掉 `AuditSnapshots` 的 `UnsafeRelaxedJsonEscaping`：jsonb 存檔時會把 `\uXXXX` 還原成字元，API 測試抓不到；改由 `AuditSnapshotsTests` 單元測試把關。

**與計畫的偏離**（以 committed code 為準）
- **T28／T29 的 Domain 規則**：
  - 已刪除的預定支出不可付款（`MarkPaid` 也擋）。
  - `MarkUnpaid` 在已刪除或本來就未付時擲例外。
  - 已付款的預定支出刪除後，付款交易與 `PaidTransactionId` 都保留。
- **DELETE** 版本由 `?version=` 帶入，成功回 204；沒帶 `version` 時由 minimal API 綁定回 400（未寫測試）。
- **T30 稽核**：
  - 新增 `AuditEntityTypes` 常數類別，`AccountDto`／`PlanningFundDto`／`CategoryDto` 各加 `From`。
  - 新增與修改後的快照 `version` 一律記 0（`AuditSnapshots.UnknownVersion`），因為 xmin 要到 `SaveChanges` 才產生；修改前的快照帶讀到的版本。
  - 刪除付款交易時（O2），預定支出另外記一筆 Update。
  - `AuditEntries` 沒有 FK 指向 Books；欄位長度自訂：ActorSubject 255、Action 32、EntityType 64。
  - 同一次 `SaveChanges` 的多筆記錄 `At` 相同，彼此依 Id 排序；單一實體的歷史不受影響。
- **T31**：`GET /audit?entityId=` 依路由的 BookId 過濾，別本帳的路徑回空陣列。
- **T32**：`BookConfiguration.cs` 沒有修改，EF 依慣例對應 `DateOnly?`。
- **T33**：
  - `PUT /lock-date` 回 204、沒有樂觀並行控制（Book 沒有 xmin），同時設定時以最後一次為準。
  - **衍生規則**：刪除付款交易時，若連結的預定支出所在月份已鎖，回 422 `locked`，即使交易日期是開放的。**待使用者確認。**
  - `DeleteTransaction` 與 `DeletePlannedExpense` 各多一次載入帳本的資料庫往返。
  - 錯誤優先順序：資料已鎖時，即使版本過舊或輸入有其他業務錯誤，都回 422 `locked`。

**待使用者決定**
- **鎖帳日只看交易日期，不看歸屬月份**：依 CONTEXT.md，鎖帳日是「此日（含）以前的**交易**」。所以鎖到 1/31 後，仍可新增日期為 2/3、歸屬月份為 2026-01 的交易，或修改 2/5 付款的 1 月預定支出交易金額，1 月的月可用餘額因此還會變動。是否要再加上「歸屬月份已鎖時也不可異動」，需要使用者決定。

---
## 段 E：帳本成員、Google 登入、授權、antiforgery（ADR 0005）

## Task 34：帳本成員（Domain 與持久層）

**Files**
- Create: `src/SixJars.Domain/Members/{BookMember,BookRole}.cs`、`src/SixJars.Infrastructure/Persistence/BookMemberConfiguration.cs`
- Modify: `ISixJarsDbContext`、`SixJarsDbContext`（加入 `BookMembers`）
- Create: migration `AddBookMembers`
- Test: `tests/SixJars.Domain.Tests/Members/BookMemberTests.cs`（4 個）、`tests/SixJars.Infrastructure.Tests/Persistence/BookMemberPersistenceTests.cs`（1 個）

**Step 1：失敗測試**
- `Owner_email_is_normalized`：`"  Adam@Example.COM "` 正規化為 `"adam@example.com"`。
- `Bind_subject_on_first_sign_in`：`BindSubject("sub-1")` 之後，`GoogleSubject == "sub-1"`；再用同一個 sub 綁定一次也通過。
- `Binding_a_different_subject_throws`：已綁定 `sub-1` 的成員，再用 `sub-2` 綁定會擲出 `DomainException`。如果允許，代表某個人換了 Google 帳號，卻沿用同一個 email 拿到存取權。
- `Only_owner_role_is_supported_in_p2`：`BookMember.Create(bookId, email, BookRole.ReadOnly)` 擲出 `DomainException`。
- 持久層：同一本帳本加入同一個 email 兩次，會因唯一索引擲出 `DbUpdateException`。

**Step 3：實作**

```csharp
public enum BookRole { Owner, Bookkeeper, ReadOnly }

/// <summary>帳本成員；白名單即此表（ADR 0005）。P2 只支援擁有者。</summary>
public sealed class BookMember
{
    private BookMember(BookId bookId, string email, BookRole role, DateTimeOffset addedAt) { ... Id = Guid.CreateVersion7(); }

    public Guid Id { get; private set; }
    public BookId BookId { get; private set; }
    public string Email { get; private set; }
    public string? GoogleSubject { get; private set; }
    public BookRole Role { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }

    public static BookMember Create(BookId bookId, string email, BookRole role, DateTimeOffset addedAt) { /* 正規化 email、只允許 Owner */ }
    public void BindSubject(string subject) { /* 尚未綁定就設定；已綁定時必須相同 */ }
}
```

configuration：
- `Role` 存成字串。
- 唯一索引 `(BookId, Email)`。
- 一般索引 `GoogleSubject`。
- `Email` 的 MaxLength 設為 320。

**Step 5**：Expected：總計 199、失敗 0。

**Step 6**：commit 訊息：`feat(domain): 帳本成員`

---

## Task 35：成員授權（`BookAccessBehavior`）、`/api/me`

**Files**
- Create: `src/SixJars.Application/Common/{IBookScoped,BookAccessBehavior}.cs`、`src/SixJars.Application/Members/{GetMe,MeDto}.cs`
- Modify: 所有帶 `BookId` 的 request（實作 `IBookScoped`）、`ListBooks`（改為只列出成員所屬的帳本）、`DependencyInjection`（註冊 behavior，順序在 Validation **之前**）、`tests/Shared/ApiSeed.cs`（seed 時加入擁有者成員，`GoogleSubject = ApiFactory.DefaultSubject`）
- Test: `tests/SixJars.Application.Tests/Common/BookScopeConventionTests.cs`（1 個）、`tests/SixJars.Api.Tests/MembershipTests.cs`（3 個）

**Step 1：失敗測試**
- `Every_request_with_BookId_is_book_scoped`（反射測試，避免日後新增 request 時忘了加授權）：
  - 掃描 Application 組件中所有實作 `IBaseRequest`、並且有 `Guid BookId` 屬性的型別。
  - 斷言它們都實作 `IBookScoped`。
- `Every_book_endpoint_is_404_for_non_members`：
  - 從 `factory.Services.GetRequiredService<EndpointDataSource>()` 列舉所有 `RouteEndpoint`，挑出 `RoutePattern.RawText` 以 `/api/books/{bookId` 開頭的。
  - 依 `HttpMethodMetadata` 決定 HTTP 方法；路徑中的參數一律代入 seed 帳本的 Id，其他 Id 參數代入 `Guid.NewGuid()`。
  - 非 GET 的請求送出 `{}` 作為 body。
  - 以**非成員**的 sub 呼叫，收集所有不是 404 的結果，最後斷言收集到的清單為空。
  - 用單一 `[Fact]` 迴圈，而不是 Theory：T41、T43 新增的匯出 endpoint 會自動被涵蓋。
  - 補充：`BookAccessBehavior` 在 Validation 之前執行，所以即使 body 是空的，也會先回 404，不會回 400。
- `Member_can_read_book`：成員以 GET 讀取帳本，回 200。
- `Me_lists_only_member_books`：建立兩本帳本，只有一本有成員關係；`/api/me` 與 `/api/books` 都只回傳那一本。

**Step 3：實作**

```csharp
public interface IBookScoped { Guid BookId { get; } }

/// <summary>帳本範圍的 request 一律先檢查登入者是否為擁有者；不是就當作不存在（404，不透露帳本存在與否）。</summary>
public sealed class BookAccessBehavior<TRequest, TResponse>(ISixJarsDbContext db, ICurrentUser user)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IBookScoped scoped)
        {
            var bookId = new BookId(scoped.BookId);
            var allowed = await db.BookMembers.AnyAsync(
                m => m.BookId == bookId && m.GoogleSubject == user.Subject && m.Role == BookRole.Owner, cancellationToken);
            if (!allowed)
            {
                throw new NotFoundException($"找不到帳本 {scoped.BookId}。");
            }
        }

        return await next(cancellationToken);
    }
}
```

- CLI 在執行時沒有 HTTP 使用者，所以 CLI 的 command **不實作 `IBookScoped`**，改由 CLI 自行指定 `ICurrentUser`（T39）。
- `MeDto(string Subject, string? Email, IReadOnlyList<BookSummaryDto> Books)`。

**Step 5**：Expected：總計 203、失敗 0。

**Step 6**：commit 訊息：`feat(app): 以帳本成員授權所有帳本範圍的 request`

---

## Task 36：Google OIDC、cookie、白名單檢查、DataProtection key 持久化

**Files**
- Modify: `Directory.Packages.props`，加入：
  - `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.12
  - `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10.0.12
- Create: `src/SixJars.Api/Infrastructure/{GoogleSignInValidator,ReturnUrl,AuthenticationSetup}.cs`、`src/SixJars.Api/Endpoints/AuthEndpoints.cs`
- Modify: `SixJarsDbContext`（實作 `IDataProtectionKeyContext`）、`Infrastructure.csproj`、`Program.cs`
- Create: migration `AddDataProtectionKeys`
- Test: `tests/SixJars.Api.Tests/Auth/{GoogleSignInValidatorTests,ReturnUrlTests,AuthenticationOptionsTests}.cs`（7 個）

**Step 1：失敗測試**
- `GoogleSignInValidatorTests`（4 個）：直接建立 `ClaimsPrincipal`，用 DbContext 準備成員資料，**不連 Google**。
  - `Unverified_email_is_rejected`：`email_verified = "false"`。
  - `Unknown_email_is_rejected`。
  - `Known_email_binds_subject_on_first_sign_in`：成功之後，DB 中該成員的 `GoogleSubject` 等於該 sub。
  - `Bound_subject_signs_in_even_if_email_changed`：sub 已綁定，但 email claim 換成新的，仍然可以登入。
- `ReturnUrlTests`（2 個）：
  - `"/transactions?m=1"` 原樣保留。
  - `"https://evil.example"` 與 `"//evil.example"` 都改成 `"/"`，防止 open redirect。
- `AuthenticationOptionsTests`（1 個）：從 `IOptionsMonitor` 取出設定後斷言（spike S4 的寫法），不需要真的走一次登入流程：
  - `OpenIdConnectOptions.Get("Google")`：Authority 為 `https://accounts.google.com`、ResponseType 為 `code`、Scope 包含 `email`。
  - `CookieAuthenticationOptions.Get(Cookies)`：HttpOnly、`SecurePolicy == Always`、`SameSite == Lax`、`ExpireTimeSpan == 14 天`、`SlidingExpiration`。

**Step 3：實作**
- `AuthenticationSetup.AddSixJarsAuthentication(this IServiceCollection, IConfiguration)`：照 spike S4 的寫法。
  - 設定 `MapInboundClaims = false`，讓 claim 名稱保持 `sub`、`email`。
  - client id 與 secret 讀自 `Authentication:Google:ClientId` 與 `Authentication:Google:ClientSecret`，也就是環境變數 `Authentication__Google__ClientId` 與 `Authentication__Google__ClientSecret`。
  - 開發與測試環境沒有設定也能啟動，因為 options 是延遲驗證的。
- `OnTokenValidated`：從 request scope 取得 `GoogleSignInValidator`，呼叫 `ValidateAsync(principal)`。結果為 false 時呼叫 `ctx.Fail("不在白名單")`。
- `OnRemoteFailure`：導向 `/auth/denied`，並呼叫 `HandleResponse()`。
- `GoogleSignInValidator.ValidateAsync`：
  1. 要求 `email_verified == "true"`。
  2. 找出 `GoogleSubject == sub` 的成員；有就通過。
  3. 否則找出 `Email == 正規化 email && GoogleSubject == null` 的成員，全部呼叫 `BindSubject(sub)`，再 `SaveChangesAsync`；至少有一筆就通過。
  4. 都沒有就拒絕。
- `AuthEndpoints`：
  - `GET /auth/login?returnUrl=`：`Results.Challenge(new() { RedirectUri = ReturnUrl.Sanitize(returnUrl) }, ["Google"])`。
  - `POST /auth/logout`：清除 cookie，回 204。這個 endpoint 掛上 T37 的 antiforgery filter。
  - `GET /auth/denied`：回 403 ProblemDetails，標題為「此 Google 帳號不在白名單」。
- `builder.Services.AddDataProtection().PersistKeysToDbContext<SixJarsDbContext>()`：讓 Cloud Run 換 instance 時，使用者不會被登出。
- 每次登入不另外寫稽核記錄（不屬於資料異動）。第一次綁定 sub 時，寫一筆 `EntityType = "BookMember"` 的 Update，`ActorSubject` 為該 sub。

**Step 5**：Expected：總計 210、失敗 0。

**Step 6**：commit 訊息：`feat(api): Google OIDC 登入、白名單檢查與 cookie 設定`

---

## Task 37：Antiforgery（XSRF）

**Files**
- Create: `src/SixJars.Api/Infrastructure/AntiforgeryFilter.cs`
- Modify: `Program.cs`（`AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN")`；`/api` 群組與 `/auth/logout` 掛上 filter；新增 `GET /api/antiforgery/token`）、`tests/Shared/ApiFactory.cs`
- Test: `tests/SixJars.Api.Tests/AntiforgeryTests.cs`（4 個）

**Step 1：失敗測試**
- `Post_without_token_is_400`。
- `Post_with_token_succeeds`。
- `Token_of_another_user_is_400`：token 與登入身分綁定（spike S4）。
- `Get_does_not_require_token`。

**Step 3：實作**
- filter 程式碼照 spike S4：非 GET／HEAD 的請求呼叫 `IAntiforgery.ValidateRequestAsync`；驗證失敗回 400 ProblemDetails，標題為「缺少或無效的 XSRF token」。
- `GET /api/antiforgery/token`：呼叫 `GetAndStoreTokens`，寫入 `XSRF-TOKEN` cookie，屬性為 `HttpOnly = false`、`Secure`、`SameSite = Strict`，回 204。Angular `HttpClient` 會自動讀這個 cookie，放進 `X-XSRF-TOKEN` header。
- `ApiFactory.CreateMemberClientAsync(subject = DefaultSubject)`：
  1. 建立已登入的 client。
  2. 呼叫 `/api/antiforgery/token`。
  3. 從 `Set-Cookie` 取出 `XSRF-TOKEN` 與 `.AspNetCore.Antiforgery.*`，設為 `DefaultRequestHeaders` 的 `X-XSRF-TOKEN` 與 `Cookie`（spike S4 的寫法）。

  之後，**把既有測試中的 `CreateSignedInClient()` 全部換成 `await CreateMemberClientAsync()`**。這是機械式的替換，與本 Task 一起 commit。

**Step 5**：Expected：總計 214、失敗 0。

**Step 6**：commit 訊息：`feat(api): 非 GET 請求一律驗證 XSRF token`

---

## Task 38：整組安全性測試

**Files**
- Test: `tests/SixJars.Api.Tests/SecurityConventionTests.cs`（2 個）

**Step 1**（列舉 endpoint 的寫法與 T35 相同）
- `Every_api_endpoint_requires_sign_in`：`/api/**` 的每個 endpoint 在沒有 `X-Test-Sub` 時都回 401。排除清單只有 `/health` 與 `/auth/**`，兩者都不在 `/api` 底下。
- `Every_unsafe_api_endpoint_requires_xsrf_token`：以成員身分、但不帶 XSRF token 的方式，呼叫 `/api/**` 中所有非 GET 的 endpoint，全部都要回 400。

**Step 2**：這兩個測試在 T35–T37 正確實作之後，應該**一開始就通過**，所以 Step 2 改成用變異確認它們真的會抓到問題：暫時拿掉 `/api` 群組的 `RequireAuthorization()`，確認第一個測試失敗；改回後，暫時拿掉 antiforgery filter，確認第二個測試失敗。

**Step 5**：Expected：總計 216、失敗 0。

**Step 6**：commit 訊息：`test(api): 所有 endpoint 都要求登入與 XSRF token 的慣例測試`

### Checkpoint E

- 全綠，總計 216。
- 變異測試，每項都必須讓某個測試失敗：
  - `BookAccessBehavior` 拿掉 Role 條件。
  - `GoogleSignInValidator` 拿掉 email_verified 檢查。
  - `ReturnUrl` 允許 `//` 開頭的網址。
- **安全審查**：對段 E 的 diff 執行 `/security-review`（若可用），回寫結論。
- 停下來讓使用者檢視。

---
## 段 F：CLI、備份、匯出、部署

## Task 39：CLI 骨架與 `add-member`

**Files**
- Modify: `Directory.Packages.props`（加入 `System.CommandLine` 2.0.12）、`SixJars.slnx`
- Create: `src/SixJars.Cli/{SixJars.Cli.csproj,Program.cs,CliApp.cs,CliCurrentUser.cs}`、`src/SixJars.Application/Members/AddOwner.cs`
- Test: `tests/SixJars.Cli.Tests/{SixJars.Cli.Tests.csproj,AssemblyInfo.cs,CliAppTests.cs}`（2 個）

**設計**
- `CliApp.RunAsync(string[] args, IReadOnlyDictionary<string, string?> environment, TextWriter output, CancellationToken ct) : Task<int>`：
  - 測試可以直接在 process 內呼叫，不必啟動子 process。
  - `Program.cs` 只做一件事：以 `Environment.GetEnvironmentVariables()` 呼叫 `CliApp.RunAsync`。
- 連線字串只讀 `ConnectionStrings__SixJars`。沒有設定時，輸出錯誤訊息並回傳 exit code 2。
- `CliCurrentUser : ICurrentUser`，`Subject = "cli"`。CLI 寫入的稽核記錄因此看得出是 CLI 做的。
- 建立 host：`ServiceCollection` → `AddSixJarsApplication(environment["MediatR__LicenseKey"])` → `AddSixJarsInfrastructure(cs)` → `ICurrentUser` 使用 `CliCurrentUser`，並註冊 `TimeProvider.System`。
- `add-member --book <guid> --email <email>` 送出 `AddOwner(Guid BookId, string Email)`：
  - 這個 command **不實作 `IBookScoped`**（T35），因為 CLI 是管理工具。
  - 寫一筆 `Create`／`BookMember` 的稽核記錄。

**Step 1：失敗測試**
- `Missing_connection_string_exits_with_2`：輸出包含 `ConnectionStrings__SixJars`。
- `Add_member_creates_owner_and_audit_entry`：
  - 準備資料：用 `PostgresFixture.CreateConnectionStringAsync` 建立資料庫，再寫入一本帳本。
  - 執行 CLI 後，斷言 exit code 為 0、DB 中有該成員，且 `Role == Owner`。

**Step 5**：Expected：總計 218、失敗 0。

**Step 6**：commit 訊息：`feat(cli): CLI 骨架與 add-member`

---

## Task 40：`import-legacy`

**Files**
- Create: `src/SixJars.Application/LegacyImport/ImportLegacyBook.cs`
- Modify: `CliApp.cs`
- Test: `tests/SixJars.Infrastructure.Tests/LegacyExcel/ImportLegacyBookTests.cs`（3 個）、`tests/SixJars.AcceptanceTests/CliImportAcceptanceTests.cs`（3 個，`reference/` 不存在時略過）

**command**：`ImportLegacyBook(LegacyWorkbook Workbook, string BookName, string OwnerEmail, bool DryRun) : IRequest<ImportLegacyBookResult>`
- `ImportLegacyBookResult(Guid? BookId, ImportReport Report, int Transactions, int PlannedExpenses)`。
- handler 流程：
  1. 執行 `LegacyWorkbookMapper.Map`。
  2. `Report.Errors` 不為空 → 不寫入，直接回傳報告。
  3. 已有同名帳本 → 擲出 `DomainException`，且不寫入。
  4. `DryRun` → 不寫入，回傳統計數字。
  5. 否則依序加入 Book、交易、預定支出、`BookMember.Create(Owner)`，以及一筆 `Import` 稽核記錄。稽核記錄的 After 為 `{ fileName, transactions, plannedExpenses, warnings }`。
  6. 以 `db.Database.BeginTransactionAsync` 包住整個寫入，確保全部成功或全部不寫入。這需要在 `ISixJarsDbContext` 加入 `BeginTransactionAsync`。
- 匯入的 Book 名稱用 `--book-name` 指定，不使用 mapper 產生的名稱。若要改名，在 `Book` 加入 internal 的 `Rename`，由 Application 透過 `InternalsVisibleTo` 呼叫；或者讓 mapper 接受名稱參數。兩種做法擇一，並記錄為偏差。
- CLI：`import-legacy --file <xlsm> --book-name <名稱> --owner-email <email> [--dry-run]`。
  - 讀檔用 P1 的 `ExcelLegacyWorkbookReader`。
  - 報告的錯誤、警告與修正逐條印出。
  - 有錯誤時，exit code 為 1。

**Step 1：失敗測試**

`ImportLegacyBookTests` 使用合成的 `LegacyWorkbook`。若 `tests/SixJars.Application.Tests` 已有 builder，以 `<Compile Link>` 重用；沒有就在測試內組一個只有 1 個月、2 列流水帳的最小 workbook。
- `Report_errors_write_nothing`：放一列無法分類的流水帳 → 回傳的錯誤不為空，DB 中沒有任何 Book。
- `Duplicate_book_name_is_rejected`。
- `Successful_import_writes_book_ledger_owner_and_one_audit_entry`。

`CliImportAcceptanceTests.Imported_book_summary_matches_excel(month)`：
- 透過 `CliApp.RunAsync("import-legacy", ...)` 匯入真實的 xlsm。
- 以 `SqlLedgerSummaryQuery` 的結果與 Excel 比對（重用 T27 的 `CompareSql`）。

**Step 5**：Expected：總計 224、失敗 0（`reference/` 不存在時略過 18）。

**Step 6**：commit 訊息：`feat(cli): import-legacy 把舊 Excel 匯入資料庫`

---

## Task 41：JSON 備份匯出

**Files**
- Create: `src/SixJars.Application/Backup/{BackupDocument,ExportBackup,BackupJson}.cs`、`src/SixJars.Api/Endpoints/ExportsEndpoints.cs`
- Test: `tests/SixJars.Api.Tests/BackupExportTests.cs`（2 個）

**格式**（`FormatVersion = 1`）

```csharp
public sealed record BackupDocument(
    int FormatVersion, DateTimeOffset ExportedAt,
    BookDto Book,
    IReadOnlyList<BackupTransaction> Transactions,
    IReadOnlyList<BackupPlannedExpense> PlannedExpenses,
    IReadOnlyList<BackupMember> Members,
    IReadOnlyList<AuditEntryDto> AuditEntries);
public sealed record BackupTransaction(TransactionDto Transaction, DateTimeOffset? DeletedAt);
public sealed record BackupPlannedExpense(PlannedExpenseDto PlannedExpense, DateTimeOffset? DeletedAt);
public sealed record BackupMember(string Email, string? GoogleSubject, BookRole Role, DateTimeOffset AddedAt);
```

- 序列化：`BackupJson.Options` 使用 `JsonSerializerDefaults.Web`、`JsonStringEnumConverter`、`WriteIndented = true`、`UnsafeRelaxedJsonEscaping`。
- 以 `IgnoreQueryFilters()` 讀取，**包含已軟刪除的資料**（CONTEXT.md「備份」）。
- 交易依 `Date`、`Id` 排序，稽核記錄依 `At`、`Id` 排序，讓同一份資料每次匯出的結果都相同（T42 的往返比對需要這個性質）。

**Step 1：失敗測試**
- `Backup_contains_deleted_transactions_members_and_audit`：
  - 新增兩筆交易、刪除其中一筆，然後 `GET /export/backup.json`。
  - 斷言：`Transactions` 有 2 筆，其中 1 筆的 `DeletedAt` 有值；`Members` 有 1 筆；`AuditEntries` 有 3 筆以上。
  - 回應的 `Content-Disposition` 檔名為 `sixjars-backup-<yyyyMMdd>.json`。
- `Backup_format_version_is_1`。

**Step 5**：Expected：總計 226、失敗 0。

**Step 6**：commit 訊息：`feat(api): JSON 完整備份匯出`

---

## Task 42：`restore-backup` 與往返測試

**Files**
- Modify: `Book.cs`、`TransactionFactory.cs`、`Transaction.cs`（internal 建構子加上 Id 參數）、`PlannedExpense.cs`、`BookMember.cs`。全部只加**可選的 Id 參數**，所以 P1 的呼叫端不需要修改：
  - `new Book(name, openingDate, BookId? id = null)`
  - `AddAccount(..., AccountId? id = null)`
  - `AddPlanningFund(..., PlanningFundId? id = null)`
  - `AddIncomeCategory(name, CategoryId? id = null)`、`AddExpenseCategory(..., id)`、`AddSubCategory(..., id)`
  - `new TransactionFactory(book, TransactionId? fixedId = null)`
  - `PlannedExpense.Create(..., PlannedExpenseId? id = null)`
  - `BookMember.Restore(...)`：以完整欄位還原，包含已綁定的 sub。
- Create: `src/SixJars.Application/Backup/RestoreBackup.cs`
- Modify: `CliApp.cs`
- Test: `tests/SixJars.Infrastructure.Tests/Backup/RestoreBackupTests.cs`（3 個）、`CliAppTests.cs`（加 1 個）

**還原流程**（handler）：**一律經過 Domain 重新驗證**。損毀或被手動改壞的備份會在這一步失敗，不會寫入不一致的資料。
1. 資料庫中已有相同 BookId → 擲出 `DomainException`。
2. 依備份建立 Book：先建主分類，再建子分類，並保留所有 Id。最後 `SetLockDate`。
3. 交易：`TransactionDto.ToInput()` → `TransactionBuilder.Build(book, input)`，factory 使用 `fixedId`。有 `DeletedAt` 的就呼叫 `Delete(deletedAt)`。
   - **注意**：鎖帳日要在交易全部建立**之後**才設定。`TransactionBuilder` 不檢查鎖帳日（那是 handler 的責任），所以順序不影響結果；但在程式註解中寫明這個順序，避免日後有人誤加檢查。
4. 預定支出：`Create`；有付款連結的呼叫 `MarkPaid`；有 `DeletedAt` 的呼叫 `Delete`。
5. 成員 → `BookMember.Restore`。稽核記錄原樣寫回，Before 與 After 由 `JsonElement` 轉回字串。
6. 再寫一筆 `Restore` 稽核記錄。
7. 整個流程包在一個 DB transaction 中。

**Step 1：失敗測試**
- `Export_restore_export_round_trips`：
  1. 在 DB1 建立涵蓋多種交易類型的資料，含刪除、付款、鎖帳日。
  2. 匯出得到 A。
  3. 還原到空的 DB2，再匯出得到 B。
  4. A 與 B 比對時排除 `ExportedAt`、`Version` 與 `AuditEntries` 的最後一筆（B 多了一筆 Restore），其餘必須完全相同。
- `Restore_into_database_with_same_book_is_rejected`。
- `Restored_book_has_same_summary`：DB1 與 DB2 以 `/summary` 或 `SqlLedgerSummaryQuery` 計算的數字相同。
- CLI：`restore-backup --file <json>`，成功時 exit code 為 0。

**Step 5**：Expected：總計 230、失敗 0。

**Step 6**：commit 訊息：`feat(cli): restore-backup，經 Domain 重新驗證後還原備份`

---

## Task 43：CSV 與 xlsx 交易明細匯出

**Files**
- Modify: `Directory.Packages.props`（加入 `ClosedXML` 0.105.1）、`Infrastructure.csproj`
- Create: `src/SixJars.Application/Exports/{TransactionExportRow,ExportTransactions,ITransactionSheetWriter}.cs`、`src/SixJars.Infrastructure/Exports/{CsvTransactionWriter,XlsxTransactionWriter}.cs`
- Modify: `ExportsEndpoints.cs`
- Test: `tests/SixJars.Api.Tests/TransactionExportTests.cs`（3 個）

**欄位**（spec §8.3）依序為：日期、歸屬月份、交易類型、帳戶、對方帳戶、主分類、子分類、財務規劃帳戶、金額、本金、利息、備註。

- 名稱由 Book 解析。分類若是子分類，主分類欄填 parent 的名稱；若本身就是主分類，子分類欄留空。
- 交易類型顯示 CONTEXT.md 的中文名稱，例如 `收入`、`入新資金`。對照表放在 `TransactionKindNames`。

**Step 1：失敗測試**
- `Csv_has_bom_header_and_rows_excluding_deleted`：
  - 位元組開頭為 `EF BB BF`。
  - 第一列等於上列欄位。
  - 已刪除的交易不出現。
  - 金額使用 invariant culture 格式，例如 `-120.5`。
- `Csv_respects_date_range`：`?from=&to=`。
- `Xlsx_round_trips_through_closedxml`：用 ClosedXML 讀回，工作表「交易」的 A2 是日期，I2 是 −120（spike S4）。

**Step 3**：xlsx 在記憶體中產生，回傳的 MIME 類型為 `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`。

**Step 5**：Expected：總計 233、失敗 0。

**Step 6**：commit 訊息：`feat(api): 交易明細匯出 CSV 與 xlsx`

---

## Task 44：Dockerfile、Supabase 連線設定、`.env.example`

**Files**
- Create: `Dockerfile`、`.dockerignore`、`.env.example`
- Modify: `.gitignore`（加入 `.env`）

**內容**
- `Dockerfile`：
  - 建置階段：`mcr.microsoft.com/dotnet/sdk:10.0` 執行 `dotnet publish src/SixJars.Api -c Release -o /app`。
  - 執行階段：`mcr.microsoft.com/dotnet/aspnet:10.0`，`USER $APP_UID`（非 root），`ENV ASPNETCORE_HTTP_PORTS=8080`。Cloud Run 預設會注入 `PORT=8080`，兩者一致即可。
  - `ENTRYPOINT ["dotnet", "SixJars.Api.dll"]`。
- `.dockerignore`：排除 `reference/`（**必須**）、`**/bin`、`**/obj`、`tests/`、`.git`、`docs/`、`.env`。
- `.env.example`：只放 placeholder，例如：

  ```
  # Supabase：使用 Supavisor session mode（pooler host、port 5432）
  ConnectionStrings__SixJars=Host=<pooler-host>;Port=5432;Database=postgres;Username=<user>;Password=<password>;SSL Mode=Require
  Authentication__Google__ClientId=<client-id>
  Authentication__Google__ClientSecret=<client-secret>
  MediatR__LicenseKey=<community-license-key>
  ```

**驗證**（手動，沒有自動化測試）
1. `docker build -t sixjars-api .` 成功。
2. 用 Testcontainers 的 Postgres 或本機 postgres 執行 `docker run -e ConnectionStrings__SixJars=... -p 8080:8080 sixjars-api`，再 `curl localhost:8080/health` 回 200。
3. `docker run --rm sixjars-api ls /app` 的結果中**沒有** `reference`，也沒有任何 `.xlsm`。

**Step 5**：測試總數不變，仍為 233。

**Step 6**：commit 訊息：`build: Dockerfile 與環境變數範本`

---

## Task 45：部署文件與 migration bundle

**Files**
- Create: `docs/deploy.md`

文件內容，每個步驟都要附上指令。所有實際操作都由使用者執行，agent 不執行任何部署指令。

1. **Supabase**：
   - 建立專案，選首爾區域。
   - 從 Connect 頁面取得 Supavisor 的 **session mode** 連線字串（port 5432）。
   - 建立 app 專用的 DB 角色，只給 DML 權限。DDL 權限只給執行 migration 的帳號。
2. **Migration**：
   - `dotnet ef migrations bundle --project src/SixJars.Infrastructure --startup-project src/SixJars.Api -o efbundle`。
   - 部署前在本機執行 `./efbundle --connection "<admin 連線字串>"`。
   - **不在 app 啟動時自動 migrate**（spec §8.4）。
3. **Google OAuth**：
   - 建立 OAuth client（Web application）。
   - Authorized redirect URI 為 `https://<cloud-run-url>/signin-oidc`。如果 `CallbackPath` 有改，這裡也要一起改。
4. **Secret Manager**：存放連線字串、Google client secret、MediatR license key。部署時以 `--set-secrets` 掛成環境變數。
5. **Cloud Run**：
   - `gcloud run deploy sixjars --region asia-east1 --source .`，或先 build image 再部署。
   - 設定 `--min-instances 0`，保持免費方案。
6. **Cloud Scheduler**：每 10 分鐘 `GET https://<url>/health`，讓服務保持 warm，也讓 Supabase 不會因為閒置而暫停。
7. **搬家**：
   1. `ConnectionStrings__SixJars=... dotnet run --project src/SixJars.Cli -- import-legacy --file reference/2026帳本v1.xlsm --book-name 家庭帳本 --owner-email <email> --dry-run`
   2. 確認報告無誤後，拿掉 `--dry-run` 再執行一次。
8. **備份**：從 `/api/books/{id}/export/backup.json` 下載備份；還原時用 `restore-backup`。

**Step 6**：commit 訊息：`docs: Cloud Run 與 Supabase 部署步驟`

### Checkpoint F（P2 後端完成）

- 全綠：總計 233、失敗 0（`reference/` 存在時略過 0）。
- **使用者本機手動驗證**：
  1. 用真實 xlsm 執行 `import-legacy --dry-run`，再正式匯入到本機的 postgres。
  2. 啟動 API，以 `.http` 檔或 curl 搭配 cookie 確認 `/summary` 1–3 月的數字與 Excel 一致。
  3. Google 登入要等前端完成後再一起驗證。
- 變異測試：
  - 備份時不使用 `IgnoreQueryFilters`，往返測試必須失敗。
  - 還原時不呼叫 `Delete`，往返測試也必須失敗。
- 用 `docs(plans):` 回寫偏差，停下來讓使用者檢視。push 與部署交由使用者執行。

---

## 後續（不在本計畫內）

- 前端 PWA：另寫一份 spec 與 plan。內容包括 Angular 21、快速記帳、Angular 建置成果放進 Api 的 `wwwroot`、離線不支援。
- P3 以後：
  - 30 天備份提醒（需要記錄最後一次匯出的時間）、記帳範本。
  - 預算、報表、提醒事項、信用卡對帳、房貸試算、月備忘錄、外部資產淨值。
  - 記帳者與唯讀角色。
  - 帳本設定的改名、封存與刪除；軟刪除的還原。
- P1 留下的孤兒子分類瑕疵：維持現狀。

---

## 附錄：核准前事實查核（2026-10-04，雲端 container）

環境：Ubuntu 24.04 container，Docker 29.6.2（需要手動執行 `dockerd &`），.NET SDK 10.0.401（從 `mcr.microsoft.com/dotnet/sdk:10.0` 複製出來）。
基線：`dotnet build` 0 warning；`dotnet test` 總計 98、失敗 0、略過 12（沒有 `reference/`）。

| 引用 | 存在？ | 證據 | 修正 |
|---|---|---|---|
| MediatR 14.2.0：`AddMediatR(cfg => cfg.LicenseKey …)`、`AddOpenBehavior`、`RequestHandlerDelegate<T>(ct)` | ✓ | spike S1：沒有 key 時只記一筆 `LuckyPennySoftware.MediatR.License` warning，功能正常；`LicenseKey` 屬性可以編譯 | — |
| FluentValidation 12.1.1：`AddValidatorsFromAssemblyContaining<T>(…, includeInternalTypes)` | ✓ | 從 nupkg 的 XML doc 確認簽章，最後一個參數是 bool | — |
| Npgsql 10.0.3 `Property<uint>("xmin").IsRowVersion()` 衝突時擲出 `DbUpdateConcurrencyException` | ✓ | spike S2 | — |
| 只改 owned 分錄時是否會檢查 `xmin` | ✗ | spike S2b：EF 不更新 owner，也不檢查版本 | `ExpectVersion` 強制 `State = Modified`（S2c 驗證會擲出衝突例外），並以 T23 的回歸測試鎖住 |
| `xmin` migration | 有條件 | C# 檔產生了 `AddColumn xmin`，但 `migrations script` 的輸出中沒有 DDL；既有的 Infrastructure 測試在套用 migration 後全部通過 | T23 改寫驗證方式 |
| query filter 套用到 `SelectMany(Postings)`；`IgnoreQueryFilters` | ✓ | spike S3：SQL 帶有 `WHERE i."DeletedAt" IS NULL` | — |
| 真實 `SixJarsDbContext` 上的 LINQ 翻譯：`BudgetMonth <=`（value converter）、`walletIds.Contains`、條件式 `Sum`、`GroupBy(BudgetMonth, Kind)`、`Postings.Any`、`PaidTransactionId == null`、`OrderBy(Date).ThenBy(Id)` | ✓ | spike：全部翻成單一 SQL（`<= @month`、`= ANY (@walletIds)`、`CASE WHEN`、`EXISTS`、`IS NULL`） | — |
| `jsonb` 欄位對應 `string`（含中文） | ✓ | spike S5：寫入再讀回，內容一致 | — |
| `WebApplicationFactory<Program>` 搭配 `UseSetting`；`Program` 頂層程式碼讀得到 | ✓ | spike：在頂層程式碼讀取設定，缺少時擲出例外，測試證實讀得到 | — |
| `TestAuthHandler`、未登入的 API 回 401（`OnRedirectToLogin`） | ✓ | spike S4 | 常數改名為 `SchemeName`，避免與基底類別成員同名造成 CS0108 |
| 用 endpoint filter 對 JSON API 做 antiforgery 驗證；token 綁定身分 | ✓ | spike S4：沒有 token 回 400、有 token 回 200、帶別人的 token 回 400 | — |
| `IOptionsMonitor<OpenIdConnectOptions>.Get("Google")` 不需要連網 | ✓ | spike S4；預設 Scope 包含 openid、profile | — |
| `PersistKeysToDbContext<T>`（`IDataProtectionKeyContext`） | ✓（只驗證到編譯） | spike S4 | 實際建表由 T36 的 migration 驗證 |
| ClosedXML 0.105.1 在記憶體中寫入 xlsx、讀回 decimal 與中文工作表名稱 | ✓ | spike S4 | — |
| System.CommandLine 2.0.12：`Option<T>{ Required }`、`SetAction(async (parseResult, ct) => int)`、`Parse(args).InvokeAsync()` | ✓ | spike：exit code 正確傳回；缺少必填參數時印出錯誤 | — |
| 套件版本（2026-10-04 的 NuGet 最新穩定版） | ✓ | MediatR 14.2.0、FluentValidation 12.1.1、Mvc.Testing、OpenIdConnect、DataProtection.EFCore 都是 10.0.12、ClosedXML 0.105.1、System.CommandLine 2.0.12 | — |
| 雲端 container 的網路 | 有條件 | `builds.dotnet.microsoft.com` 與 `dc.services.visualstudio.com` 被 proxy 擋住；nuget.org、mcr.microsoft.com、Docker Hub 可以連線 | SDK 改從 image 複製；遙測要設定 `DOTNET_CLI_TELEMETRY_OPTOUT=1` |
| 計畫中各 **Create** 檔案目前都不存在；**Modify** 檔案都存在 | ✓ | 已用 `ls` 核對 `src/`、`tests/` | — |
| `Book` 已有鎖帳日欄位（交接文件的說法） | ✗ | `grep -ri lock src/` 沒有任何結果 | 由 T32 從零實作（spec §3.1 已註明） |

### 尚未驗證，留到執行時用測試確認

- **`/summary` 的往返次數**：T27 斷言 ≤ 5，這是推算的數字，還沒實測。
- **Google 真實登入**：無法自動化，要等前端完成後由使用者手動驗證。
- **Supavisor session mode 與 Npgsql 的相容性**：理論上和一般 PostgreSQL 連線相同。部署時，由使用者以 `/health` 確認。
