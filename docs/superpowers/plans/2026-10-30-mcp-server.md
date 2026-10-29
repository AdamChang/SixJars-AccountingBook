# 遠端 MCP Server（唯讀）實作計畫

> For agentic workers: REQUIRED SUB-SKILL — superpowers:test-driven-development；逐 Task 執行，每個 Task 一個 commit。

**Goal**：讓 Claude app 透過遠端 MCP 連進 SixJars，用唯讀 tool 查帳本，再由 Claude 自己比對信用卡帳單。

**Architecture**：
- MCP 掛在既有 Api 的 `/mcp`，使用 Streamable HTTP（stateless）。
- 同一個 Api 內建最小 OAuth 2.1 授權伺服器：CIMD、S256 PKCE、public client。使用者登入沿用 Google cookie 登入。
- access token 是 DataProtection 自包含的；refresh token 存在 DB，可撤銷、會輪替。
- tool 透過 `ISender` 呼叫既有 query。

**Tech Stack**：.NET 10、ModelContextProtocol.AspNetCore 2.3.0、ASP.NET Core DataProtection（金鑰已存 DB）、EF Core＋Npgsql、xUnit v3＋FluentAssertions＋Testcontainers。

設計文件：`docs/superpowers/specs/2026-10-30-mcp-server-design.md`；ADR 0013（Task 1 建立）。

## 執行前必讀

### 環境
- 工作目錄 `F:\VibeCode\SixJars-AccountingBook`，分支 **`feat/mcp-server`**（不是 master）。
- 全部測試：`dotnet test --solution SixJars.slnx`（需 Docker）。
- 單一專案：`dotnet test --project tests/SixJars.Api.Tests --filter-class "*Mcp*"`。

### 基準線（2026-10-30，master c791fea）
- `dotnet test --solution SixJars.slnx`：**總計 694、成功 666、略過 28、失敗 0**。略過的都是讀 `reference/2026帳本v1.xlsm` 的測試，檔案不存在，這是既有狀態。
- build 只有一個既有 warning：`DeleteBookEndpointsTests.cs(189)` 的 EF1002。
- 任何時候測試失敗數 > 0，或出現新的 warning，就是弄壞了東西。

### 絕對不要碰的檔案
- `reference/**`（個資，已 gitignore）。
- 每個 Task 都用**明確路徑** `git add`，禁止 `git add .`。

### 慣例
- 註解與 commit message 用繁體中文，識別字用英文。
- 每個檔案的 `/// <summary>` 寫「為什麼」，和既有程式的密度一致。
- DTO／response 用 `record`，mapping 手動寫。
- 「今天」與現在時間一律用注入的 `TimeProvider`。
- 測試分層：
  - Domain 純單元測試。
  - OAuth／MCP 走 `ApiFactory` 的整合測試，用 Testcontainers。
- 每個 Task 依 TDD 順序：紅 → 確認失敗原因 → 綠 → 全測。

## 檔案結構

### 新增
| 檔案 | 責任 |
|---|---|
| `docs/adr/0013-remote-mcp-with-embedded-oauth.md` | 授權伺服器與 CIMD 的取捨 |
| `src/SixJars.Domain/Members/McpGrant.cs` | 一次授權：refresh 輪替、重放偵測、撤銷 |
| `src/SixJars.Infrastructure/Persistence/McpGrantConfiguration.cs`＋migration `AddMcpGrants` | 資料表與 unique index |
| `src/SixJars.Api/Mcp/McpSetup.cs` | `AddSixJarsMcp`／`MapSixJarsMcp`：MCP server、scheme、policy、OAuth 端點 |
| `src/SixJars.Api/Mcp/OAuth/McpOAuthOptions.cs` | `Mcp:AllowedClientIdHosts` 等設定與常數 |
| `src/SixJars.Api/Mcp/OAuth/McpTokens.cs` | code／access token 的 protect 與 unprotect、refresh token 的產生與 hash |
| `src/SixJars.Api/Mcp/OAuth/McpBearerHandler.cs` | 驗證 access token 與 grant 未撤銷 |
| `src/SixJars.Api/Mcp/OAuth/ClientMetadataFetcher.cs` | 抓 CIMD 文件：host 白名單、timeout、大小上限、不跟隨 redirect |
| `src/SixJars.Api/Mcp/OAuth/RedirectUris.cs` | redirect_uri 比對（loopback 不比對 port） |
| `src/SixJars.Api/Mcp/OAuth/OAuthEndpoints.cs` | 兩份 metadata、authorize（GET／POST）、token |
| `src/SixJars.Api/Mcp/OAuth/ConsentPage.cs` | 同意頁 HTML（HtmlEncoder） |
| `src/SixJars.Api/Mcp/LedgerTools.cs` | 三個 tool |
| `src/SixJars.Api/Mcp/ReconcilePrompt.cs` | prompt 與 server instructions |
| `src/SixJars.Application/Members/RevokeMcpAccess.cs` | CLI 專用的撤銷 command |
| `tests/SixJars.Domain.Tests/Members/McpGrantTests.cs` | |
| `tests/SixJars.Api.Tests/Mcp/*.cs` | discovery、authorize、token、tools、端到端 |

### 修改
| 檔案 | 改動 |
|---|---|
| `Directory.Packages.props`、`src/SixJars.Api/SixJars.Api.csproj` | 加入 `ModelContextProtocol.AspNetCore` 2.3.0；測試專案加入 `ModelContextProtocol.Core`（McpClient） |
| `src/SixJars.Application/Common/ISixJarsDbContext.cs`、`SixJarsDbContext.cs` | `DbSet<McpGrant> McpGrants` |
| `src/SixJars.Api/Program.cs` | `AddSixJarsMcp`、`MapSixJarsMcp` |
| `src/SixJars.Api/Infrastructure/SpaHosting.cs` | `BackendPrefixes` 加入 `mcp`、`oauth`、`.well-known` |
| `tests/SixJars.Api.Tests/SecurityConventionTests.cs` | 匿名白名單加 `/oauth/`、`/.well-known/`、`/mcp` 另外驗證 |
| `src/SixJars.Cli/CliApp.cs` | `revoke-mcp-access` |
| `CONTEXT.md`、`CLAUDE.md`、`docs/deploy.md`、`.env.example` | 文件 |

### Task 相依順序
```
T1 (docs+套件) ─▶ T2 (McpGrant+migration) ─▶ T3 (scheme+discovery+/mcp 骨架) ─▶ T4 (authorize) ─▶ T5 (token) ─▶ T7 (端到端)
                                     │                                   └▶ T6 (tools，可與 T4/T5 並行)
                                     └▶ T8 (CLI 撤銷，只依賴 T2)
T9 (文件) 最後
```
Checkpoint：
- T3 後：/mcp 有保護、discovery 可讀。
- T5 後：OAuth 完整。
- T7 後：功能完整。

### 事實查核（2026-10-30，執行前）
| 引用 | 存在？ | 證據 | 修正 |
|---|---|---|---|
| `ModelContextProtocol.AspNetCore` 2.3.0 | ✅ | nuget.org 最新版；XML doc 有 `AddMcp`、`McpAuthenticationOptions.ResourceMetadata`／`Events.OnResourceMetadataRequest`、`MapMcp`、`HttpServerTransportOptions.Stateless` | — |
| `McpClient.CreateAsync`／`CallToolAsync`／`ServerInstructions`、`HttpClientTransport(options, HttpClient, …)` | ✅ | ModelContextProtocol.Core 2.3.0 XML doc | 測試專案參考 `ModelContextProtocol.Core` |
| metadata 預設路徑 `/.well-known/oauth-protected-resource/<path>` | ✅ | `ResourceMetadataUri` 的 doc 寫明「mirrors the requested resource path」 | T3 以測試再確認一次 |
| `McpAuthenticationHandler` 只負責 challenge | ⚠️ 推定 | doc 只說「adds resource metadata to challenge responses」 | T3 的查證點 |
| Claude 的 CIMD 判斷條件、callback、loopback | ✅ | claude.com/docs/connectors/building/authentication | 原計畫的 DCR → 改為 CIMD |
| 支出金額帶正負號、信用卡不能記收入 | ✅ | `TransactionFactory.Expense`／`Income` | T6 的 seed 用正數支出代表折抵 |
| `FixedTimeProvider` 不能前進 | ✅ | `tests/SixJars.Api.Tests/FixedTimeProvider.cs` | T5 新增 `MutableTimeProvider`（放在 Mcp 測試資料夾） |
| Create 的檔案尚未存在 | ✅ | `docs/adr/0013*`、`src/SixJars.Api/Mcp`、`McpGrant.cs` 都不存在 | — |
| `AntiforgeryFilter` 沒有改 `FormFieldName` | ✅ | 只設定 `HeaderName` | 同意頁用預設的 form field |

---

## Task 1：ADR、用語與套件
**Files:**
- Create：`docs/adr/0013-remote-mcp-with-embedded-oauth.md`
- Modify：`CONTEXT.md`、`Directory.Packages.props`、`src/SixJars.Api/SixJars.Api.csproj`、`tests/SixJars.Api.Tests/SixJars.Api.Tests.csproj`

ADR 記錄 spec §3 的取捨，以及「結論翻轉條件」。`CONTEXT.md` 在「帳本成員與輔助功能」下加「**MCP 授權 (MCP Grant)**」：使用者允許某個 AI client（例如 Claude）以唯讀方式讀取自己可存取的帳本；可撤銷；不屬於任何帳本。_Avoid_：API key、連線。

純文件與套件，沒有測試。
- [ ] `dotnet build SixJars.slnx` 通過，warning 數量與基準線相同。
- [ ] Commit：`docs: ADR 0013 遠端 MCP 與內建 OAuth 授權伺服器，加入 MCP SDK 套件`

## Task 2：McpGrant 與 migration
**Files:**
- Create：`McpGrant.cs`、`McpGrantConfiguration.cs`、migration、`tests/SixJars.Domain.Tests/Members/McpGrantTests.cs`
- Modify：`ISixJarsDbContext.cs`、`SixJarsDbContext.cs`

refresh 輪替與重放偵測是整個設計最容易錯的地方，所以放在 Domain，用純單元測試鎖住。容易錯的是：重放時撤銷整個 grant，必須在同一次 `SaveChanges` 內寫入，不能先回錯誤再寫。

API（Domain 不碰 crypto，只收 hash）：
```csharp
public static McpGrant Issue(string subject, string? email, string clientId, string scope, Guid authorizationCodeId,
    string refreshTokenHash, DateTimeOffset now, TimeSpan refreshLifetime);
public RefreshOutcome Refresh(string presentedHash, string newHash, DateTimeOffset now, TimeSpan refreshLifetime);
// RefreshOutcome: Rotated | Reused (→ 已撤銷) | Invalid (過期、已撤銷、hash 不符)
public void Revoke(DateTimeOffset now);   // 冪等
public bool IsActive => RevokedAt is null;
```

- [ ] Step 1：測試
  - `Refresh_with_current_hash_rotates`：Rotated；current = new；previous = 舊值；期限延長。
  - `Refresh_with_previous_hash_revokes_grant`：Reused；`RevokedAt == now`。
  - `Refresh_with_unknown_hash_is_invalid_and_keeps_grant`
  - `Refresh_after_expiry_is_invalid`
  - `Refresh_on_revoked_grant_is_invalid`
  - `Revoke_is_idempotent`：第二次呼叫不覆寫時間。
- [ ] Step 2：確認因為型別不存在而編譯失敗。
- [ ] Step 3：實作 entity、configuration：
  - `Subject` 255、`Email` 320、`ClientId` 2048、hash 64。
  - `AuthorizationCodeId` unique。
  - `RefreshTokenHash` 建 index（refresh 以 hash 查詢）、`PreviousRefreshTokenHash` 建 index、`Subject` 建 index。
  - DbSet 與 migration：`dotnet ef migrations add AddMcpGrants --project src/SixJars.Infrastructure --startup-project src/SixJars.Infrastructure --output-dir Persistence/Migrations`
- [ ] Step 4／5：Domain tests 全綠；全部測試通過。Infrastructure 的 migration 測試會自動套用新的 migration。
- [ ] Step 6：Commit `feat(mcp): McpGrant 授權與 refresh token 輪替、重放偵測`

## Task 3：Bearer scheme、discovery 與 `/mcp` 骨架
**Files:**
- Create：`McpSetup.cs`、`McpOAuthOptions.cs`、`McpTokens.cs`、`McpBearerHandler.cs`、`OAuthEndpoints.cs`（只有 metadata）、`tests/SixJars.Api.Tests/Mcp/McpDiscoveryTests.cs`、`tests/SixJars.Api.Tests/Mcp/McpTestTokens.cs`
- Modify：`Program.cs`、`SpaHosting.cs`、`SecurityConventionTests.cs`、`SpaHostingTests.cs`

這個 Task 先把「門」做好，tool 留到 T6。容易錯的地方有三個：
- SPA 的 rewrite 會在 authentication 之前吃掉 `/.well-known/*` 的 GET。
- `McpAuthenticationHandler` 只負責 challenge 與 metadata，authenticate 要交給自己的 handler。
- 測試的 `ApiFactory` 把 DefaultScheme 換成 Test，所以 `/mcp` 的 policy **必須明確指定 scheme**，否則 cookie／Test 身分會被當成合法。

`McpTestTokens.IssueAsync(factory, subject)`：直接在 DB 建立 grant，再用 `McpTokens` 產生 access token。這是測試 helper，讓 T3／T6 不必依賴 T4／T5。

- [ ] Step 1：測試
  - `Mcp_without_token_returns_401_with_resource_metadata`：POST `/mcp` 帶 initialize JSON-RPC，期望 401，`WWW-Authenticate` 含 `resource_metadata="https://localhost/.well-known/oauth-protected-resource/mcp"`。
  - `Mcp_with_cookie_identity_only_returns_401`：`CreateSignedInClient()` 打 `/mcp` 得 401。
  - `Mcp_with_valid_token_initializes`：期望 200。
  - `Mcp_with_revoked_grant_returns_401`、`Mcp_with_token_for_other_audience_returns_401`
  - `Bearer_token_is_not_accepted_on_api`：bearer 打 `GET /api/books` 得 401。
  - `Protected_resource_metadata_names_resource_and_issuer`：`resource == https://localhost/mcp`、`authorization_servers == ["https://localhost"]`、`scopes_supported == ["ledger:read"]`。
  - `Authorization_server_metadata_advertises_cimd_and_s256`：
    - `client_id_metadata_document_supported: true`
    - `token_endpoint_auth_methods_supported` 含 `"none"`
    - `code_challenge_methods_supported == ["S256"]`
    - `grant_types_supported` 剛好是兩種
    - **沒有** `registration_endpoint`
  - SpaHostingTests：`/.well-known/x`、`/oauth/x`、`/mcp` 都不是前端路由。
  - SecurityConventionTests：白名單加 `/oauth/`、`/.well-known/`、`/mcp`，並新增斷言「`/mcp` 的 endpoint metadata 帶有指定 `McpBearer` scheme 的 `IAuthorizeData`」。
- [ ] Step 2：確認以 404 或 200 而失敗，不是編譯錯誤。
- [ ] Step 3：實作
  - `AddAuthentication().AddScheme<…, McpBearerHandler>("McpBearer").AddMcp(o => o.Events.OnResourceMetadataRequest = 依請求填 Resource／AuthorizationServers／ScopesSupported)`
  - policy `Mcp`：`AuthenticationSchemes = ["McpBearer"]`、`ForwardChallenge` 轉給 `McpAuthenticationDefaults.AuthenticationScheme`，並要求 `scope` claim 含 `ledger:read`。
  - `AddMcpServer(o => o.ServerInstructions = …).WithHttpTransport(o => o.Stateless = true)`
  - `app.MapMcp("/mcp").RequireAuthorization("Mcp")`
  - **查證點**：先用測試確認 `McpAuthenticationHandler.HandleAuthenticateAsync` 回 `NoResult`，以及 metadata 路徑真的是 `/.well-known/oauth-protected-resource/mcp`。不符時回報再調整。
- [ ] Step 4／5：`--filter-class "*McpDiscovery*"` 全綠；全部測試通過。
- [ ] Step 6：Commit `feat(mcp): /mcp 以 bearer token 保護，提供 OAuth discovery metadata`

## Task 4：`/oauth/authorize`（CIMD＋同意頁）
**Files:**
- Create：`ClientMetadataFetcher.cs`、`RedirectUris.cs`、`ConsentPage.cs`、`tests/SixJars.Api.Tests/Mcp/AuthorizeEndpointTests.cs`、`tests/SixJars.Api.Tests/Mcp/RedirectUrisTests.cs`、`tests/SixJars.Api.Tests/Mcp/FakeClientMetadataFetcher.cs`
- Modify：`OAuthEndpoints.cs`、`McpSetup.cs`

驗證失敗時分兩種處理（RFC 6749 §4.1.2.1）：
- `client_id` 或 `redirect_uri` 不可信 → **不能 redirect**，回 400 頁面。否則就變成 open redirect。
- 其他參數錯誤 → redirect 回 client，帶 `error=invalid_request`。

`IClientMetadataFetcher` 介面讓測試換成假的實作；正式實作用具名 `HttpClient`，`AllowAutoRedirect=false`、timeout 5 秒、讀取上限 64 KB。

- [ ] Step 1：測試
  - RedirectUris（單元）：
    - 精確比對。
    - `http://localhost:3118/callback` 符合 `http://localhost/callback`；`http://127.0.0.1:5000/callback` 同理。
    - `http://evil.com:3118/callback` 不符合。
    - path 不同不符合。
    - `https://claude.ai/api/mcp/auth_callback/` 多了斜線不符合。
  - Authorize（整合，`FakeClientMetadataFetcher` 回傳 Claude 的文件）：
    - 未登入 → 302 到 `/auth/login?returnUrl=%2Foauth%2Fauthorize%3F...`，且 returnUrl 經 `ReturnUrl.Sanitize` 仍原樣保留。
    - 已登入 → 200 HTML：含 `claude.ai`、antiforgery hidden field，以及 `Content-Security-Policy: frame-ancestors 'none'`。
    - `client_id` host 不在白名單 → 400，**而且 fetcher 沒有被呼叫**（斷言呼叫次數 0）。
    - `redirect_uri` 不在文件中 → 400、不 redirect。
    - 文件的 `client_id` 與 URL 不同 → 400。
    - `code_challenge_method=plain` 或缺少 `code_challenge` → 302 回 redirect_uri，帶 `error=invalid_request&state=…`。
    - `response_type` 不是 `code` → `unsupported_response_type`。
    - `resource` 不等於 `https://localhost/mcp` → `invalid_target`。
    - POST 同意且帶 token → 302 到 `redirect_uri?code=…&state=…&iss=https%3A%2F%2Flocalhost`。
    - POST 不帶 antiforgery token → 400。
    - POST 選「拒絕」→ `error=access_denied`。
- [ ] Step 2：確認以 404 失敗。
- [ ] Step 3：實作。POST 重新驗證全部參數，不信任 hidden field；code 用 `McpTokens.ProtectCode`。
- [ ] Step 4／5、Step 6：Commit `feat(mcp): OAuth authorize 端點，以 CIMD 辨識 client 並顯示同意頁`

## Task 5：`/oauth/token`
**Files:**
- Create：`tests/SixJars.Api.Tests/Mcp/TokenEndpointTests.cs`
- Modify：`OAuthEndpoints.cs`

細節：
- body 只接受 `application/x-www-form-urlencoded`。
- 錯誤一律回 RFC 6749 的 JSON `{error, error_description}` 與 400，失效的 refresh 用 `invalid_grant`。
- 回應加 `Cache-Control: no-store`。
- 不掛 antiforgery，因為這是 server-to-server 呼叫。

- [ ] Step 1：測試。用 helper `AuthorizeAsync` 走完 T4 的 GET＋POST 取得 code。
  - code 換 token 成功：有 `access_token`、`refresh_token`、`token_type == "Bearer"`、`expires_in == 3600`、`scope == "ledger:read"`；access token 能打 `/mcp`。
  - `code_verifier` 錯誤 → `invalid_grant`。
  - `redirect_uri` 與授權時不同 → `invalid_grant`。
  - `client_id` 不同 → `invalid_grant`。
  - code 超過 60 秒（`FixedTimeProvider` 前進）→ `invalid_grant`。注意 DataProtection 的 time-limited protector 使用自己的時鐘，所以 code 的到期時間要寫進 payload，由 `TimeProvider` 判斷。
  - 同一個 code 第二次兌換 → `invalid_grant`，而且第一次發出的 access token 變成 401。
  - refresh → 新 access 與新 refresh；舊 access 在到期前仍可用。
  - 已被輪替的舊 refresh 再次使用 → `invalid_grant`，新 refresh 也失效（grant 已撤銷）。
  - `grant_type=client_credentials` → `unsupported_grant_type`。
  - JSON body → 400 或 415。
- [ ] Step 2：確認失敗。
- [ ] Step 3：實作。code 重放靠 `AuthorizationCodeId` unique；捕捉 `DbUpdateException`（unique violation），撤銷既有 grant 後回 `invalid_grant`。
- [ ] Step 4／5、Step 6：Commit `feat(mcp): OAuth token 端點，refresh token 每次輪替並偵測重放`

## Task 6：Ledger tools 與 prompt
**Files:**
- Create：`LedgerTools.cs`、`ReconcilePrompt.cs`、`tests/SixJars.Api.Tests/Mcp/LedgerToolsTests.cs`
- Modify：`McpSetup.cs`、`tests/SixJars.Api.Tests/SixJars.Api.Tests.csproj`

用 SDK 的 `McpClient`＋`HttpClientTransport`，傳入 factory 的 `HttpClient` 並帶 Authorization header，讓測試走真的 protocol。

容易錯的地方：
- `amount` 必須是**該帳戶的分錄合計**，不是 `Transaction.Amount`。轉帳到信用卡（繳卡費）在卡上是正數，從卡支出是負數。
- `NotFoundException` 不可以變成 500 或洩漏訊息細節。

- [ ] Step 1：測試。seed 一本帳，在「國泰Combo卡」記：
  - 支出 -99（主食／午餐，備註「Subway」）
  - 點數折抵 +250：記成**正數的支出**。Domain 的支出金額帶正負號；收入不允許記在信用卡上（`TransactionFactory.Income` 限制了帳戶類型）
  - 從銀行繳卡費 500
  - 另一個帳戶的支出 1 筆

  然後驗證：
  - `list_books` 只回傳自己的帳本；另一個 subject 的帳本不出現。
  - `list_accounts` 包含 `國泰Combo卡`、`type == "CreditCard"`。
  - `list_account_transactions(卡, 2026-01-01, 2026-01-31)`：
    - 3 筆依日期排序；支出 `amount == -99`、`mainCategory == "主食"`、`subCategory == "午餐"`、`note == "Subway"`。
    - 繳卡費 `amount == +500`、`counterAccount == "國泰世華銀行"`。
    - `total` 等於三者合計。
    - 不含別的帳戶的交易。
  - 期間 121 天 → `IsError == true`，訊息含「120」。
  - `from > to` → `IsError`。
  - 別人的帳本 → `IsError`，訊息為「找不到帳本」。
  - 不屬於這本帳的 `accountId` → `IsError`。
  - `ListPromptsAsync` 包含 `reconcile_credit_card_statement`；`client.ServerInstructions` 含「消費日」。
- [ ] Step 2：確認以「tool 不存在」失敗。
- [ ] Step 3：實作
  - `[McpServerToolType] internal sealed class LedgerTools(ISender sender)`，tool 名稱用 snake_case。
  - description 用繁體中文，明確寫出金額正負號的意義。
  - 例外：`NotFoundException` 與 validation 例外轉成 `McpException`（tool error）。
  - 回傳 record，由 SDK 序列化。
- [ ] Step 4／5、Step 6：Commit `feat(mcp): 唯讀 ledger tools 與信用卡對帳 prompt`

## Task 7：端到端
**Files:**
- Create：`tests/SixJars.Api.Tests/Mcp/McpEndToEndTests.cs`

只寫一條測試，串起完整流程：
1. 未帶 token 打 `/mcp`，得 401，讀出 `resource_metadata`。
2. 抓 protected resource metadata，再抓 AS metadata。
3. 用 authorize 的 GET 與 POST 取得 code，再換 token。
4. 用 `McpClient` 呼叫 `list_account_transactions`。
5. refresh 之後再呼叫一次。

全部依 metadata 裡的 URL 前進，不寫死路徑，證明 discovery 能讓 Claude 自己找到每一步。
- [ ] Step 1–5、Step 6：Commit `test(mcp): OAuth discovery 到 tool 呼叫的端到端測試`

## Task 8：CLI `revoke-mcp-access`
**Files:**
- Create：`src/SixJars.Application/Members/RevokeMcpAccess.cs`、`tests/SixJars.Cli.Tests/RevokeMcpAccessTests.cs`
- Modify：`CliApp.cs`

`internal sealed record RevokeMcpAccess(string Email) : IRequest<int>, ICliOnlyRequest`

撤銷 `Email` 等於正規化 email、**或** `Subject` 屬於該 email 已綁定成員或建立者的所有有效 grant，回傳撤銷的筆數。0 筆時印出「沒有有效的授權」，exit 0。
- [ ] 測試：
  - 兩筆有效 grant 加一筆別人的，只撤銷兩筆，輸出含「2」。
  - email 大小寫與空白會正規化。
  - `BookScopeConventionTests` 仍然通過（internal＋ICliOnlyRequest）。
- [ ] Commit `feat(cli): revoke-mcp-access 撤銷某人所有 MCP 授權`

## Task 9：文件
**Files:**
- Modify：`docs/deploy.md`（新增「13. 連接 Claude」：URL `https://<service>/mcp`、claude.ai → Customize → Connectors → Add custom connector、Claude Code `claude mcp add --transport http sixjars https://<service>/mcp`、撤銷方式、migration 必須先套用）
- Modify：`CLAUDE.md`（Architecture 的 Api 段落補 MCP 一行、CLI 指令清單）
- Modify：`.env.example`（`Mcp__AllowedClientIdHosts__0=claude.ai` 註解範例）
- [ ] Commit `docs: 連接 Claude 的步驟與 MCP 架構說明`

## 完成後的驗證
- [ ] `dotnet build SixJars.slnx`：warning 只剩基準線那一個。
- [ ] `dotnet test --solution SixJars.slnx`：全部通過；Acceptance 一筆 skip（既有）。
- [ ] `git status` 乾淨；分支上有 9 個 commit（加上計畫回寫的 `docs(plans):`）。
- [ ] `git diff master` 中沒有連線字串、client secret 或 token。

## 手動驗證（自動測試涵蓋不到）
1. 本機 https 跑 Api（`docs/deploy.md` 的本機段落），執行 `npx @modelcontextprotocol/inspector`，連 `https://localhost:<port>/mcp`，走完 OAuth 後呼叫三個 tool。需要真的 Google client，Inspector 的 CIMD／redirect 也要加入白名單或臨時設定。
2. 部署並套用 migration 後，在 claude.ai 新增 custom connector，確認 Connect 會跳出 Google 登入與同意頁。
3. 上傳 `reference/信用卡電子帳單消費明細_11509_解密.pdf`，請 Claude 比對國泰 Combo 卡 8/14–9/15。結果應與 Excel sheet 的 A–G 大致一致；不一致的地方記錄下來，作為調整 prompt 的依據。
4. 用 `revoke-mcp-access` 撤銷後，Claude 下一次呼叫應該要求重新連線。

## 後續（不在本計畫內）
- 寫入類 tool：把漏記明細轉成交易、修正日期。
- 保存帳單明細與商家規則、對帳狀態。
- DCR 後備：只有在 Claude 不送 CIMD 的情況出現時才做。
- Web 版的授權管理頁。
- 冷啟動優化（min-instances）。
