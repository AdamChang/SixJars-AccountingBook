# P3 後端前置修正 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓 Api 能提供 Angular 的建置成果（SPA fallback 與快取 header），並把登入失敗導向前端的說明頁。

**Architecture:** 以一個 middleware 把「不屬於 API 的前端路徑」改寫成 `/index.html`，交給 `UseStaticFiles` 處理；不註冊任何新的 endpoint，所以 `SecurityConventionTests` 的 endpoint 慣例不需要放寬。快取 header 由 `StaticFileOptions.OnPrepareResponse` 依檔名決定。

**Tech Stack:** ASP.NET Core 10（minimal hosting）、xUnit v3、FluentAssertions、`WebApplicationFactory` + Testcontainers（既有的 `ApiFactory`）。

**Spec:** `docs/superpowers/specs/2026-10-04-p3-frontend-pwa-design.md` §8（前端的 plan 另見 `2026-10-04-p3-frontend-pwa.md`）。

## Global Constraints

- 不新增任何 endpoint：`SecurityConventionTests` 的兩個測試與 `ApiEndpoints` 不可修改。
- `wwwroot` 不存在時（只跑 API 測試、沒有 build 前端）app 必須正常啟動，`/health` 仍回 200。
- 前端路徑是指：不以 `/api`、`/auth`、`/health` 為第一個路徑段，且最後一段沒有副檔名。只改寫 GET 與 HEAD。
- 快取：`index.html` 一律 `Cache-Control: no-cache`；檔名符合 Angular 的雜湊格式（`-` 加 8 個 base32 字元（A–Z、2–7；esbuild 內容雜湊的字母表）再接副檔名，例如 `main-ABCD2345.js`）的檔案設 `public, max-age=31536000, immutable`；其他靜態檔（`ngsw.json`、`ngsw-worker.js`、`manifest.webmanifest`、`favicon.ico`）設 `no-cache`。
- 登入失敗（`OnRemoteFailure`）轉址到 `/denied`；刪除 `/auth/denied` endpoint。

## Review Focus

1. `/api/nope`、`/auth/nope` 這類打錯的路徑必須回 404，而且不是 HTML。前端拿到 200 的 HTML 會在解析 JSON 時失敗，很難查。→ Task 1 的 `Unknown_api_and_auth_paths_are_404_not_html`。
2. 有副檔名但不存在的檔案（例如舊版的 `main-OLD12345.js`）必須回 404，不能回 `index.html`，否則瀏覽器會把 HTML 當成 JS 執行，錯誤訊息很難懂。→ Task 1 的 `Missing_file_with_extension_is_404`。
3. 以 deep link 進入（例如 `/books/x/transactions?month=202603`）拿到的 `index.html` 也必須是 `no-cache`；只有直接請求 `/index.html` 才設 header 的話，部署新版後舊頁面會一直留著。→ Task 2 的 `Index_is_no_cache_for_root_and_deep_link`。
4. `ngsw.json` 不可以被長期快取，否則 service worker 永遠偵測不到新版。→ Task 2 的 `Unhashed_files_are_no_cache`。
5. `wwwroot` 不存在時，前端路徑回 404，而不是 500。→ Task 1 的 `Without_web_root_frontend_paths_are_404_and_health_still_works`。

---

## 執行前必讀

### 環境

- 工作目錄：`F:\VibeCode\SixJars-AccountingBook`（Windows，PowerShell）。
- 分支：`claude/p3-frontend-pwa`（**不是 master**）。
- 前置條件：Docker Desktop 執行中（Testcontainers 用 `postgres:17-alpine`）。
- 全部測試：`dotnet test`
- 單一測試類別：`dotnet test tests/SixJars.Api.Tests --filter "FullyQualifiedName~SpaHostingTests"`

### 基準線（2026-10-04 本機實測）

- `dotnet build`：0 warning、0 error。
- `dotnet test`：總計 **345**、失敗 0、略過 0（`reference/` 存在時）。
- 任何時候數字低於基準線，就是弄壞了東西。

### 絕對不要碰的檔案

- `.env`、`.env.gcp`（機密，已被 `.gitignore` 排除）、`reference/`（個資）、`efbundle.exe`。
- 每個 Task 都用明確路徑 `git add`，**禁止 `git add .`／`git add -A`**。

### 慣例

- 程式註解、commit 訊息用繁體中文；識別字用英文。
- 照 TDD：先寫失敗測試、確認它**以正確的理由**失敗，再實作。一個 Task 一個 commit。
- commit 訊息結尾加上 `Co-Authored-By` 行（依當下 session 的 attribution 設定）。
- 測試沿用既有寫法：`ApiFactory`（`tests/Shared/ApiFactory.cs`）、`PostgresFixture`、`TestContext.Current.CancellationToken`。

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `src/SixJars.Api/Infrastructure/SpaHosting.cs` | `UseSpaHosting(this WebApplication app)`：路徑改寫 middleware＋`UseStaticFiles`（含快取 header） |
| `tests/SixJars.Api.Tests/SpaWebRoot.cs` | 測試用：在暫存目錄建立假的 Angular 建置成果 |
| `tests/SixJars.Api.Tests/SpaHostingTests.cs` | SPA fallback 與快取 header 的測試 |

### 修改

| 檔案 | 改動 |
|---|---|
| `src/SixJars.Api/Program.cs` | 在 `UseExceptionHandler()` 之後、`UseAuthentication()` 之前呼叫 `app.UseSpaHosting()` |
| `src/SixJars.Api/Infrastructure/AuthenticationSetup.cs:104` | `OnRemoteFailure` 轉址改為 `/denied` |
| `src/SixJars.Api/Endpoints/AuthEndpoints.cs:26` | 刪除 `/auth/denied` |
| `tests/SixJars.Api.Tests/Auth/AuthEndpointsTests.cs` | 刪除 `Denied_is_403_problem`，改為驗證 `/auth/denied` 已不存在 |
| `tests/SixJars.Api.Tests/Auth/AuthenticationOptionsTests.cs:202` | 預期的 Location 改為 `/denied` |

### Task 相依順序

```
Task 1（SPA fallback）──► Task 2（快取 header）
Task 3（/denied）        獨立，可與 1、2 並行；但 Task 3 的 404 斷言依賴 Task 1 不改寫 /auth/**
```

建議依序執行 1 → 2 → 3。

---

## Task 1：SPA fallback

**Files:**
- Create: `src/SixJars.Api/Infrastructure/SpaHosting.cs`、`tests/SixJars.Api.Tests/SpaWebRoot.cs`、`tests/SixJars.Api.Tests/SpaHostingTests.cs`
- Modify: `src/SixJars.Api/Program.cs`

這個 Task 值得單獨存在，因為最容易出錯的地方正好相反：fallback 收得太寬，把打錯的 API 路徑變成 200 的 HTML；或是收得太窄，deep link 重新整理就 404。

選擇 middleware 而不是 `MapFallbackToFile`：fallback endpoint 會出現在 `EndpointDataSource`，`SecurityConventionTests.Every_api_endpoint_requires_sign_in` 第 33–34 行規定匿名 endpoint 只能是 `/health` 與 `/auth/**`，而且 `ApiEndpoints.Calls` 假設每個 endpoint 都有 `HttpMethodMetadata`（fallback 沒有，會 NRE）。改寫路徑不新增 endpoint，安全慣例不必放寬。

**Interfaces:**
- Produces: `public static WebApplication UseSpaHosting(this WebApplication app)`（`SixJars.Api.Infrastructure.SpaHosting`）；`internal static bool IsFrontendRoute(PathString path)`。
- Produces（測試）：`internal static class SpaWebRoot { public static string Create(); public const string IndexMarker = "<!-- sixjars-spa -->"; }`，建立的目錄內有 `index.html`（內容含 `IndexMarker`）、`main-ABCD2345.js`、`ngsw.json`。
- Consumes: `ApiFactory(string? connectionString, Action<IServiceCollection>? testServices = null, bool useTestAuthentication = true, IReadOnlyDictionary<string, string>? settings = null)`；以 `settings: { ["webroot"] = SpaWebRoot.Create() }` 指定 web root（事實查核 spike 已確認 `UseSetting("webroot", …)` 會生效）。

- [ ] **Step 1：寫失敗測試**（`SpaHostingTests`，以 `PostgresFixture` 建 `ApiFactory`；除了最後一個測試，其餘都用 `SpaWebRoot.Create()` 當 web root，用匿名 client，`BaseAddress = https://localhost`）

```csharp
[Theory]
[InlineData("/")]
[InlineData("/books/7b0c2c1e-0000-0000-0000-000000000001/transactions?month=202603")]
[InlineData("/denied")]
public async Task Frontend_routes_serve_index_html(string url)
// 200、Content-Type text/html、body 包含 SpaWebRoot.IndexMarker

[Theory]
[InlineData("/api/nope")]
[InlineData("/api")]
[InlineData("/auth/nope")]
public async Task Unknown_api_and_auth_paths_are_404_not_html(string url)
// 404，Content-Type 不是 text/html，body 不含 IndexMarker

[Fact]
public async Task Missing_file_with_extension_is_404()
// GET /main-OLD12345.js → 404

[Fact]
public async Task Existing_static_file_is_served()
// GET /main-ABCD2345.js → 200，Content-Type text/javascript

[Fact]
public async Task Post_to_frontend_route_is_not_rewritten()
// POST /books/x/transactions（空 body）→ 404 或 405，body 不含 IndexMarker

[Fact]
public async Task Without_web_root_frontend_paths_are_404_and_health_still_works()
// 不設定 webroot（Api 專案沒有 wwwroot 目錄）：GET /books/x → 404；GET /health → 200
```

- [ ] **Step 2：確認失敗**
  Run: `dotnet test tests/SixJars.Api.Tests --filter "FullyQualifiedName~SpaHostingTests"`
  Expected: `Frontend_routes_serve_index_html` 與 `Existing_static_file_is_served` 失敗（404，因為還沒有 `UseStaticFiles`）；其餘目前就會通過（行為本來就是 404），屬於回歸防護，不是 vacuous。確認失敗訊息是 `Expected … OK, but found NotFound`，而不是編譯錯誤。

- [ ] **Step 3：實作 `SpaHosting.UseSpaHosting`**
  - middleware：`context.GetEndpoint() is null`、方法是 GET 或 HEAD、`IsFrontendRoute(context.Request.Path)` 為 true，而且 `app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists` 時，把 `context.Request.Path` 改成 `/index.html`。
  - `IsFrontendRoute`：第一個路徑段不是 `api`、`auth`、`health`（大小寫不敏感，比對整段；例如 `/apix` 屬於前端），而且最後一段沒有 `.`。
  - 之後呼叫 `app.UseStaticFiles()`（Task 2 再加上 options）。
  - minimal hosting 會在 pipeline 開頭自動加入 `UseRouting`，所以這個 middleware 裡讀得到 `GetEndpoint()`。如果實測讀不到，就在 `Program.cs` 明確呼叫 `app.UseRouting()`，放在 `UseSpaHosting` 之前，並在計畫中回寫。
  - `Program.cs`：在 `app.UseExceptionHandler();` 之後、`app.UseAuthentication();` 之前加上 `app.UseSpaHosting();`。

- [ ] **Step 4：跑單一類別**：同 Step 2 的指令。Expected：全部通過（10 個 case）。
- [ ] **Step 5：跑全部測試**：`dotnet test`。Expected：總計 **355**、失敗 0、略過 0（345＋10）；`SecurityConventionTests` 通過，而且沒有修改。
- [ ] **Step 6：Commit**

```bash
git add src/SixJars.Api/Infrastructure/SpaHosting.cs src/SixJars.Api/Program.cs tests/SixJars.Api.Tests/SpaWebRoot.cs tests/SixJars.Api.Tests/SpaHostingTests.cs
git commit -m "feat(api): 提供前端靜態檔與 SPA fallback（不新增 endpoint）"
```

---

## Task 2：快取 header

**Files:**
- Modify: `src/SixJars.Api/Infrastructure/SpaHosting.cs`、`tests/SixJars.Api.Tests/SpaHostingTests.cs`

容易錯的地方：只對「直接請求 `/index.html`」設 header，忘了 deep link 改寫後的請求；或是把 `ngsw.json` 也設成長期快取。

**Interfaces:**
- Produces: `internal static bool IsHashedAsset(string fileName)`，規則：`-[A-Z2-7]{8}\.[a-z0-9]+$`。實際的 Angular 輸出檔名在前端 plan 的段 I 用真正的 build 驗證；不相符時回到這裡修改規則，並回寫計畫。

- [ ] **Step 1：寫失敗測試**

```csharp
[Theory]
[InlineData("/")]
[InlineData("/books/x/transactions")]
[InlineData("/index.html")]
public async Task Index_is_no_cache_for_root_and_deep_link(string url)
// response.Headers.CacheControl!.NoCache.Should().BeTrue()

[Fact]
public async Task Hashed_assets_are_immutable()
// GET /main-ABCD2345.js：CacheControl.Public 為 true、MaxAge == 365 天、ToString() 包含 "immutable"

[Theory]
[InlineData("/ngsw.json")]
public async Task Unhashed_files_are_no_cache(string url)
// NoCache 為 true，而且沒有 MaxAge
```

另外在 `SpaHostingTests` 裡加一個純函式的 Theory：`IsHashedAsset("main-ABCD2345.js")`、`"chunk-Z7Y6X5W4.js"`、`"styles-ABCDEFGH.css"` 為 true；`"ngsw.json"`、`"ngsw-worker.js"`、`"main-abcd2345.js"`（小寫）、`"logo-20261004.png"`（含 0、1、8、9，非 base32）、`"favicon.ico"` 為 false。（2026-10-05 回寫：Angular 22 的 lazy chunk 實際名稱為 `chunk-BvxS2djg.js` 這類大小寫混合雜湊，`IsHashedAsset` 另外接受 `^chunk-[A-Za-z0-9_-]{8}\.js$`，見前端 plan 的「執行結果與偏差」。）`SixJars.Api.csproj` 已有 `InternalsVisibleTo` 給 `SixJars.Api.Tests`，`internal` 方法可以直接測。

- [ ] **Step 2：確認失敗**：快取的測試失敗（沒有 `Cache-Control` header，`CacheControl` 為 null）；`IsHashedAsset` 的測試以編譯錯誤失敗（方法尚未存在），先加一個 `throw new NotImplementedException()` 的空殼，讓失敗原因變成斷言失敗。
- [ ] **Step 3：實作**：`UseStaticFiles(new StaticFileOptions { OnPrepareResponse = ctx => … })`，依 `ctx.File.Name`：`index.html` 或 `IsHashedAsset` 為 false 時設 `no-cache`；`IsHashedAsset` 為 true 時設 `public, max-age=31536000, immutable`。用 `ctx.Context.Response.GetTypedHeaders().CacheControl`。
- [ ] **Step 4：跑單一類別**：全部通過。
- [ ] **Step 5：跑全部測試**：總計約 **367**、失敗 0、略過 0（Task 1 的 355，加上 5 個快取 case 與 7 個 `IsHashedAsset` case）。**實際：367**（8c00a3e）；整體審查的修正輪（ed8adcb）再加 `/API/nope` 與 `logo-20261004.png` 兩個 case，最終 **369**。
- [ ] **Step 6：Commit**

```bash
git add src/SixJars.Api/Infrastructure/SpaHosting.cs tests/SixJars.Api.Tests/SpaHostingTests.cs
git commit -m "feat(api): 前端靜態檔的快取 header（index.html 不快取、雜湊檔長期快取）"
```
---

## Task 3：登入失敗轉址到前端的 `/denied`

**Files:**
- Modify: `src/SixJars.Api/Infrastructure/AuthenticationSetup.cs`、`src/SixJars.Api/Endpoints/AuthEndpoints.cs`、`tests/SixJars.Api.Tests/Auth/AuthenticationOptionsTests.cs`、`tests/SixJars.Api.Tests/Auth/AuthEndpointsTests.cs`

不在白名單或在 Google 取消授權時，目前使用者會看到一段 403 的 JSON。轉址到前端頁面，由前端顯示說明與「重新登入」。

- [ ] **Step 1：改測試**
  - `AuthenticationOptionsTests.Remote_failure_redirects_to_denied_page`：預期的 Location 改為 `"/denied"`。
  - `AuthEndpointsTests`：刪除 `Denied_is_403_problem`，改為 `Old_denied_endpoint_is_gone`：`GET /auth/denied` 回 404。
- [ ] **Step 2：確認失敗**：兩個測試都失敗（Location 仍是 `/auth/denied`；`/auth/denied` 仍回 403）。
- [ ] **Step 3：實作**：`OnRemoteFailure` 改為 `ctx.Response.Redirect("/denied")`，並修改上方註解；刪除 `AuthEndpoints` 的 `/auth/denied`，類別摘要中的「拒絕頁」一併拿掉。
- [ ] **Step 4：跑單一類別**：`dotnet test tests/SixJars.Api.Tests --filter "FullyQualifiedName~SixJars.Api.Tests.Auth"`，全部通過。
- [ ] **Step 5：跑全部測試**：總計與 Task 2 結束時相同（刪 1 加 1）、失敗 0、略過 0。
- [ ] **Step 6：Commit**

```bash
git add src/SixJars.Api/Infrastructure/AuthenticationSetup.cs src/SixJars.Api/Endpoints/AuthEndpoints.cs tests/SixJars.Api.Tests/Auth/AuthenticationOptionsTests.cs tests/SixJars.Api.Tests/Auth/AuthEndpointsTests.cs
git commit -m "feat(api): 登入失敗轉址到前端的 /denied，移除 /auth/denied"
```

---

## 完成後的驗證

- [ ] `dotnet build`：0 warning。
- [ ] `dotnet test`：總計 **369**、失敗 0、略過 0（2026-10-04 主控者實測：Task 3 後 367，整體審查修正輪後 369）。
- [ ] 變異測試（主控者獨立執行，每項都要有測試失敗，做完還原）：
  - `IsFrontendRoute` 不排除 `api` → `Unknown_api_and_auth_paths_are_404_not_html` 失敗。
  - 拿掉「最後一段有副檔名就不改寫」→ `Missing_file_with_extension_is_404` 失敗。
  - `OnPrepareResponse` 只在請求路徑是 `/index.html` 時設 header（而不是依檔名）→ deep link 的 no-cache 測試失敗。
  - `IsHashedAsset` 改成永遠 true → `Unhashed_files_are_no_cache` 失敗。
- [ ] `git status`：只剩 `.env.gcp` 等被忽略的檔案，沒有未追蹤的原始碼。
- [ ] commit：3 個功能 commit，加上回寫用的 `docs(plans):`（若有偏差）。
- [ ] `git diff master --stat`：沒有 `.env*`、沒有 `reference/`。

## 手動驗證

自動測試已涵蓋行為；真實的 Angular 建置成果與 Google 登入，在前端 plan 的段 G、I 一起驗證。

## 後續（不在本計畫內）

- Dockerfile 的 Node build stage：屬於前端 plan 的段 I（要有 `web/` 才能 build）。
- Content-Security-Policy 等安全 header：前端成形後再評估。
- 壓縮（Brotli／gzip）：Cloud Run 前端不會自動壓縮，可在 P3 之後再評估 `UseResponseCompression`。

---

## 附錄：核准前事實查核（2026-10-04，本機）

| 引用 | 存在？ | 證據 | 修正 |
|---|---|---|---|
| spec §8 原第 1 項（錯誤 key camelCase） | 已完成 | `ApiExceptionHandler.ToBodyFieldName`（`46e7b20`），`TransactionsEndpointsTests` 第 109、256 行 | spec 刪除此項（`119c520`） |
| `ApiFactory` 可用 `settings["webroot"]` 指定 web root | ✓ | spike：`IWebHostEnvironment.WebRootPath` 等於暫存目錄，且讀得到 `index.html`（spike 測試已刪除） | — |
| `SecurityConventionTests` 會擋下新的匿名 endpoint | ✓ | 第 33–34 行；`ApiEndpoints.Calls` 對沒有 `HttpMethodMetadata` 的 endpoint 會 NRE | 改用 middleware，不新增 endpoint |
| `OnRemoteFailure` 轉址 `/auth/denied` | ✓ | `AuthenticationSetup.cs:104`；測試 `AuthenticationOptionsTests.cs:202`、`AuthEndpointsTests.cs:21` | Task 3 |
| `src/SixJars.Api/wwwroot` | 不存在 | `Test-Path` 為 False | 「沒有 web root」的測試直接用 Api 專案的預設值 |
| 各 Create 檔案不存在；各 Modify 檔案存在 | ✓ | `Test-Path`；Modify 的行號已核對 | — |
| `SixJars.Api` 的 `InternalsVisibleTo` | ✓ | `SixJars.Api.csproj:6` 已開給 `SixJars.Api.Tests` | Task 2 不需修改 csproj |
| Angular 雜湊檔名格式 `-[A-Z2-7]{8}`（base32） | ✓ | Angular CLI 22.2.1 的 production build（scratchpad spike）：`main-DISDLN5L.js`、`styles-OPUTW5UJ.css`；`ngsw-worker.js`、`ngsw.json`、`manifest.webmanifest`、`safety-worker.js`、`worker-basic.min.js` 不帶雜湊 | 原規則 `[A-Z0-9]` 在整體審查後收緊為 base32 `[A-Z2-7]`（ed8adcb），避免 `logo-20261004.png` 這類人工檔名被長期快取；前端 plan 段 I 再以 `web/` 的實際 build 確認一次 |
