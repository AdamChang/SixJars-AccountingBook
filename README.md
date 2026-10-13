# SixJars 記帳本

個人／家庭記帳系統，移植自「富朋友理財記帳本」Excel。以一本連續、不分年度的帳本記錄每筆金錢流動，計算可用現金、月可用餘額與淨值。

> 目前進度：P1 帳務核心、P2 Web API／CLI／部署、P3 Angular PWA（記帳與總覽）、P4 段 J 帳本設定、段 K 預定支出與週期預定支出已完成；P4 段 L 預算、段 M 報表尚未實作（見 [P4 設計](docs/superpowers/specs/2026-10-06-p4-settings-planning-budget-reports-design.md)）。領域用語見 [`CONTEXT.md`](CONTEXT.md)，設計決策見 [`docs/adr/`](docs/adr/)。

## Tech Stack

| 層 | 技術 |
| --- | --- |
| Runtime | .NET 10（SDK 10.0.401，`global.json`）、C# latest |
| API | ASP.NET Core Minimal API、ProblemDetails |
| 應用層 | Clean Architecture、CQRS（MediatR 14）、FluentValidation 12 |
| 資料庫 | PostgreSQL（正式環境 Neon）、EF Core 10 + Npgsql，Code First migration |
| 驗證 | Cookie + Google OpenID Connect（BFF），白名單即帳本成員；Antiforgery（XSRF） |
| 檔案 | ExcelDataReader（讀舊 `.xlsm`）、ClosedXML（匯出 `.xlsx`） |
| CLI | System.CommandLine |
| 測試 | xUnit v3（Microsoft.Testing.Platform）、FluentAssertions、Testcontainers（PostgreSQL 17） |
| 前端 | Angular 22（standalone、zoneless、signals）、Angular Material／CDK、PWA（service worker，不支援離線） |
| 前端測試 | Vitest、Playwright（API 以 mock 取代） |
| 部署 | Docker（前後端同一個 image，由 API 提供前端）、Google Cloud Run（台灣）、Secret Manager |

專案結構：

```text
src/
  SixJars.Domain          交易展開成分錄、餘額／可用現金／月可用餘額計算
  SixJars.Application     use case（MediatR request + handler）、授權與驗證 pipeline
  SixJars.Infrastructure  EF Core、migration、Excel 匯入、CSV/XLSX 匯出
  SixJars.Api             Web API（composition root）
  SixJars.Cli             管理工具（composition root，與 API 共用 Application）
tests/                    Domain / Application / Infrastructure / Api / Cli / Acceptance
web/                      Angular PWA（記帳、總覽、預定支出、帳本設定）
```

## 功能

- **帳本設定**：帳戶（現金、銀行、信用卡、電子錢包、貸款）、財務規劃帳戶（信封）、兩層收支分類，可排序、封存、刪除（使用中者不可刪）；鎖帳日，鎖住日期以前的交易不可異動。
- **交易**：收入、支出、轉帳、提款、現金存入、加值、繳卡費、新增貸款、貸款繳款、財務規劃（入新資金／出資金／資金回流）；每筆交易展開成分錄，可指定歸屬月份。
- **預定支出**：固定／貸款／特別支出的月度預計項目，付款時連結實際交易。
- **週期預定支出**：定義週期項目後依月份產生當月預定支出；未付款的項目可用週期項目現值更新。
- **月摘要**：指定歸屬月份與基準日的帳戶餘額、可用現金、月可用餘額。
- **稽核與軟刪除**：每次寫入留下前後快照；刪除只標記，不參與計算。
- **樂觀並行**：更新／刪除需帶版本號，衝突回 409。
- **匯出與備份**：交易明細 CSV／XLSX；整本帳 JSON 備份（可用 CLI 還原）。
- **舊帳匯入**：CLI 把舊 Excel 記帳本轉成新帳本，附檢查報告與 dry-run。
- **權限**：Google 登入，只有帳本成員可存取；非成員一律 404。目前只實作「擁有者」角色。
- **Web 前端**：記帳頁（鍵盤快速連續記帳、手機兩行清單）、總覽頁、預定支出頁（本月清單、產生、付款、週期項目）、設定頁（含匯出與備份下載）；可安裝為 PWA。

## 使用方式

### 前置需求

- .NET SDK 10.0.401 以上
- Node.js 24.15.0 以上（Angular 22 CLI 的需求）
- Docker（跑整合測試用）
- PostgreSQL（本機執行 API 用）

### 建置與測試

```powershell
dotnet build SixJars.slnx
dotnet test --solution SixJars.slnx

# 單一專案／單一測試類別
dotnet test --project tests/SixJars.Domain.Tests --filter-class "*BalanceCalculator*"
```

- Domain 以外的測試會用 Testcontainers 啟動 PostgreSQL，Docker 必須在執行中。
- Acceptance 測試會讀 `reference/2026帳本v1.xlsm`（含個資，不在 repo 中，可用環境變數 `SIXJARS_LEGACY_WORKBOOK` 指定路徑）。找不到檔案時，這些測試會略過。

前端（在 `web/` 目錄下）：

```powershell
cd web
npm ci
npx ng test --watch=false      # Vitest 單元測試
npx playwright install chromium # 第一次跑 E2E 前
npm run e2e                     # Playwright，自動啟動 ng serve，API 以 mock 取代
```

### 本機執行 API

App 啟動時不會自動跑 migration，請先把 schema 套用到本機資料庫（`dotnet-ef` 10.0.12）：

```powershell
$env:ConnectionStrings__SixJars = "Host=localhost;Port=5432;Database=sixjars;Username=postgres;Password=<密碼>"
dotnet ef database update --project src/SixJars.Infrastructure --startup-project src/SixJars.Infrastructure --connection $env:ConnectionStrings__SixJars
```

登入 cookie 使用 `__Host-` 前綴，本機也必須跑 https：

```powershell
dotnet dev-certs https --trust
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "https://localhost:5001"
$env:Authentication__Google__ClientId = "<client-id>"
$env:Authentication__Google__ClientSecret = "<client-secret>"
dotnet run --project src/SixJars.Api
```

Google OAuth client 需加入 redirect URI `https://localhost:5001/auth/callback`。所有環境變數見 [`.env.example`](.env.example)。

### 本機執行前端

先啟動上面的後端，再啟動前端：

```powershell
cd web
npx ng serve
```

開啟 `https://localhost:4300`。`web/proxy.conf.json` 會把 `/api`、`/auth` 轉給 `https://localhost:5001`，Google OAuth client 需另外加入 redirect URI `https://localhost:4300/auth/callback`。proxy 的注意事項，以及在本機驗證 production build 與 service worker 的步驟，見 [`docs/deploy.md`](docs/deploy.md) 第 5 節。

### API 概覽

| 路徑 | 說明 |
| --- | --- |
| `GET /health` | 資料庫連線檢查 |
| `GET /auth/login`、`POST /auth/logout` | Google 登入／登出 |
| `GET /api/antiforgery/token` | 取得 XSRF token（非 GET 請求須帶 `X-XSRF-TOKEN` header） |
| `GET /api/me`、`GET /api/books` | 登入者資訊、可存取的帳本 |
| `/api/books/{bookId}/…` | `accounts`、`planning-funds`、`categories`（含 `archive`、`unarchive`、`order`）、`lock-date`、`transactions`、`planned-expenses`（含 `generate`、`refresh`、`pay`）、`recurring-planned-expenses`、`summary`、`audit`、`export/*` |
| 其他路徑 | 由 API 提供前端靜態檔，deep link 回 `index.html` |

### CLI 管理工具

連線字串只讀環境變數 `ConnectionStrings__SixJars`。

```powershell
# 匯入舊 Excel 記帳本（先用 --dry-run 檢查報告）
dotnet run --project src/SixJars.Cli -- import-legacy --file <帳本.xlsm> --book-name <名稱> --owner-email <gmail> --dry-run

# 把 Google 帳號加為帳本擁有者（登入白名單）
dotnet run --project src/SixJars.Cli -- add-member --book <bookId> --email <gmail>

# 從 JSON 備份還原一本帳
dotnet run --project src/SixJars.Cli -- restore-backup --file <backup.json>
```

### 部署

Docker image（multi-stage，同時建置前端並放進 `wwwroot`）、Neon 資料庫角色、migration bundle、Cloud Run 與 Secret Manager 設定見 [`docs/deploy.md`](docs/deploy.md)。

```powershell
docker build -t sixjars-api .
docker run --env-file .env -p 8080:8080 sixjars-api
```
