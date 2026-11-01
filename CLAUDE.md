# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

SixJars 記帳本：從「富朋友理財記帳本」Excel 移植的個人／家庭記帳 Web API。領域用語一律以 `CONTEXT.md` 為準（帳本、分錄、歸屬月份、財務規劃帳戶…），寫程式與文件前先讀；設計決策在 `docs/adr/`，各階段 spec／plan 在 `docs/superpowers/`。

## Commands

.NET 10（`global.json` 鎖 SDK 10.0.401），測試 runner 是 Microsoft.Testing.Platform + xUnit v3，所以 `dotnet test` 用 `--project`，filter 用 xUnit v3 語法。

```powershell
dotnet build SixJars.slnx
dotnet test --solution SixJars.slnx
dotnet test --project tests/SixJars.Domain.Tests
dotnet test --project tests/SixJars.Domain.Tests --filter-class "*BalanceCalculator*"
dotnet test --project tests/SixJars.Api.Tests --filter-method "*Delete*"

# 新增 migration（DesignTimeDbContextFactory 不實際連線）
dotnet ef migrations add <Name> --project src/SixJars.Infrastructure --startup-project src/SixJars.Infrastructure --output-dir Persistence/Migrations

# CLI（管理工具：import-legacy / add-member / add-creator / remove-creator / revoke-mcp-access / restore-backup），連線字串只讀環境變數 ConnectionStrings__SixJars
dotnet run --project src/SixJars.Cli -- --help
```

- Api / Application / Infrastructure / Acceptance 測試透過 Testcontainers 啟動 `postgres:17-alpine`（assembly 共用一個容器、每個測試各自建資料庫並套 migration），**需要 Docker 正在執行**。Domain 測試不需要。
- Acceptance 測試讀真實舊帳本 `reference/2026帳本v1.xlsm`（個資，已 gitignore；可用 `SIXJARS_LEGACY_WORKBOOK` 覆寫路徑）。檔案不存在時這些測試會 **skip 而非 fail**，回報結果時要說明。
- 本機跑 API 與部署（Neon、Cloud Run、migration bundle、DDL/DML 角色分離）見 `docs/deploy.md`。App 啟動時**不會**自動 migrate。
- 正式部署由 push `v*.*.*` tag 觸發 `.github/workflows/deploy.yml`（重跑 `ci.yml` → build／push image → `gcloud run deploy`，ADR 0012）；migration 仍由使用者在本機執行，`/health/ready`（有未套用的 migration 時回 503）是 Cloud Run 的 startup probe。一次性 GCP 設定在 `scripts/setup-gcp.ps1`。

## Architecture

Clean Architecture：`Domain` ← `Application` ← `Infrastructure` ← `Api` / `Cli`（兩個 composition root 共用同一組 Application command，不另寫寫入邏輯）。

- **Domain**：交易（`Transaction`）依 `TransactionKind` 由 `TransactionFactory` 展開成多筆 `Posting`（ADR 0001）；餘額、可用現金、月可用餘額由 `Domain/Ledger` 的 calculator 從 `LedgerSnapshot` 計算。財務規劃帳戶是信封不是帳戶（ADR 0003）。帳本是連續時間軸、無年度結轉（ADR 0002）。強型別 Id 在 `Domain/Common/Ids.cs`。
- **Application**：每個 use case 一個檔案，內含 `record` request + `internal` handler（MediatR），依功能資料夾分（Books、Transactions、Planning、Ledger、Auditing、Backup、LegacyImport…）。Pipeline 依序為 `BookAccessBehavior` → `ValidationBehavior`。
  - 帶 `Guid BookId` 的 request **必須**實作 `IBookScoped`：非成員一律 404（不是 403，也不先回 400）。唯一例外是 `ICliOnlyRequest`（必須是 `internal`，Api 因此參考不到）。`BookScopeConventionTests` 會檢查。
  - 寫入操作的慣例（參考 `Transactions/DeleteTransaction.cs`）：查詢同時帶 `BookId` 條件、先檢查鎖帳日（`EnsureUnlocked`）、以 `ExpectVersion` 做樂觀並行（409）、用 `IAuditTrail.Record` 留前後快照（ADR 0006）、軟刪除，全部在同一次 `SaveChangesAsync`。
  - 例外對應：`NotFoundException` → 404、`ForbiddenException` → 403、`DomainException` → 422、`DbUpdateConcurrencyException` → 409、validation → 400（`Api/Infrastructure/ApiExceptionHandler.cs`）。`ForbiddenException` 只用在不屬於帳本範圍的權限拒絕（例如不在建立者名單，ADR 0011）；帳本範圍的拒絕仍一律 404。
  - 「今天」一律用注入的 `TimeProvider` 與 `TaipeiTime`，不要用 `DateTime.Now`。
  - 唯讀的帳本一律用 `IBookReader`（`GetReadOnlyBookAsync`）：in-memory 快取、以 Books 的 xmin 判斷是否過期，回傳的實例會跨 request 共用，**不可修改**；要修改用 `GetBookForUpdateAsync`。DbContext 已設全域 SplitQuery。
- **Infrastructure**：EF Core + Npgsql（PostgreSQL / Neon，ADR 0007），configuration 與 migration 在 `Persistence/`；月報彙總用 SQL（`Ledger/SqlLedgerSummaryQuery.cs`）；舊 Excel 讀取（ExcelDataReader）與 CSV/XLSX 匯出（ClosedXML）。
- **Api**：Minimal API，endpoint 依資源分檔在 `Endpoints/`，`/api` group 一律需登入並掛 `AntiforgeryFilter`。BFF 模式：Cookie + Google OIDC，白名單即帳本成員表（ADR 0005）；cookie 用 `__Host-` 前綴，本機必須跑 https。
- **遠端 MCP**（ADR 0013）：`Api/Mcp`。
  - `/mcp` 是唯讀的 Streamable HTTP（stateless），tool 在 `LedgerTools`，透過 `ISender` 呼叫既有 query。
  - 保護 `/mcp` 的是同一個 Api 內建的最小 OAuth 授權伺服器（`Api/Mcp/OAuth`）：只支援 CIMD、S256 PKCE 與 public client，`client_id` 必須在 `Mcp:AllowedClientIdHosts` 白名單內。
  - `/mcp` 只接受 `McpBearer` scheme，`/api` 只接受 cookie，兩者不互通。
  - 授權記錄在 `McpGrant`，可以撤銷，refresh token 每次使用都會輪替。
- **測試共用**：`tests/Shared`（`PostgresFixture`、`ApiFactory`、`TestAuthHandler`、`ApiSeed` 等）以 linked file 方式被各測試專案引用。

## Agent skills

### Issue tracker

Issue 追蹤在本 repo 的 GitHub Issues（`AdamChang/SixJars-AccountingBook`），一律使用 `gh` CLI 操作。詳見 `docs/agents/issue-tracker.md`。

### Triage labels

使用五個預設 label：`needs-triage`、`needs-info`、`ready-for-agent`、`ready-for-human`、`wontfix`。詳見 `docs/agents/triage-labels.md`。

### Domain docs

Single-context：repo 根目錄的 `CONTEXT.md` 與 `docs/adr/`。詳見 `docs/agents/domain.md`。
