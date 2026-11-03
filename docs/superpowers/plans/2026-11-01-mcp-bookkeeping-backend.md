# MCP 記帳（寫入）後端實作計畫

> For agentic workers: REQUIRED SUB-SKILL — superpowers:test-driven-development；逐 Task 執行，每個 Task 一個 commit。

**Goal**：在既有唯讀 `/mcp` 上加入記帳 tool。使用者在同意頁勾選「允許記帳」後，Claude 可以把對話或信件裡的消費，經使用者確認後寫成交易。

**Architecture**：
- 授權：`ledger:write` scope 由同意頁的核取方塊決定是否授予；access token 的每個 scope 各一個 claim；新 policy `McpWrite`。
- tool：新類別 `BookkeepingTools`，寫入 tool 掛 `[Authorize(Policy = McpWrite)]`，搭配 SDK 的 `AddAuthorizationFilters()`。
- Application：新 command `RecordTransaction`（以來源識別碼冪等）與 query `PreviewTransaction`，和 `CreateTransaction` 共用建立步驟。
- 資料：交易加 `ExternalRef`（partial unique index）與 `CreatedVia`；稽核記錄加 `Via`；備份格式 v6。

**Tech Stack**：.NET 10、ModelContextProtocol.AspNetCore 2.3.0、EF Core＋Npgsql、xUnit v3＋FluentAssertions＋Testcontainers。

設計文件：`docs/superpowers/specs/2026-11-01-mcp-bookkeeping-backend-design.md`（下稱 spec，決策編號 D1–D14）。前端另見 `plans/2026-11-01-mcp-bookkeeping-frontend.md`。

## 執行前必讀

### 環境
- 工作目錄 `F:\VibeCode\SixJars-AccountingBook`，分支 **`feat/mcp-write`**（不是 master）。目前疊在 `feat/mcp-server`（PR #23）上，#23 合併後要 rebase 到 master。
- 全部測試：`dotnet test --solution SixJars.slnx`（需 Docker）。
- 單一類別：`dotnet test --project tests/SixJars.Api.Tests --filter-class "*BookkeepingTools*"`。

### 基準線（2026-11-01，`feat/mcp-write` = a28541b，親自執行）
- `dotnet test --solution SixJars.slnx`：**總計 777、成功 749、略過 28、失敗 0**。略過的是讀 `reference/2026帳本v1.xlsm` 的 Acceptance 測試，檔案不存在，這是既有狀態。
- build 只有一個既有 warning：`DeleteBookEndpointsTests.cs(189)` 的 EF1002。
- 任何時候測試失敗數 > 0，或出現新的 warning，就是弄壞了東西。

### 絕對不要碰的檔案
- `reference/**`（個資）、`efbundle.exe`、`TestResults/`。
- 每個 Task 都用**明確路徑** `git add`，禁止 `git add .`。

### 慣例
- 註解與 commit message 用繁體中文，識別字用英文；`/// <summary>` 寫「為什麼」，密度比照 `LedgerTools.cs`。
- DTO 與 tool 回傳用 `record`，mapping 手動寫。
- **DTO 新增欄位一律加在最後、有預設值**：舊的備份 JSON 與既有的建構呼叫都不用改。
- 「今天」用注入的 `TimeProvider` 與 `TaipeiTime`。
- 測試分層：
  - Domain 用純單元測試。
  - Persistence 與備份放 Infrastructure.Tests。
  - command 與 query 放 Api.Tests，透過 `ISender` 執行，並以 `testServices` 換掉 `ICurrentUser`。
  - OAuth 與 tool 放 Api.Tests，走真的 HTTP 與 MCP protocol。
- 每個 Task 依 TDD 順序：紅 → 確認失敗原因 → 綠 → 全測 → commit。

## 檔案結構

### 新增
| 檔案 | 責任 |
|---|---|
| `docs/adr/0014-mcp-bookkeeping-write-scope.md` | 開放寫入的取捨，以及為什麼仍不改用 OpenIddict |
| `src/SixJars.Application/Transactions/TransactionCreation.cs` | `CreateTransaction` 與 `RecordTransaction` 共用的建立步驟 |
| `src/SixJars.Application/Transactions/RecordTransaction.cs` | MCP 記帳 command：Kind 白名單、來源識別碼冪等、`SetOrigin` |
| `src/SixJars.Application/Transactions/PreviewTransaction.cs` | 唯讀試算：會建立的交易、鎖帳、同來源、疑似重複 |
| `src/SixJars.Infrastructure/Persistence/Migrations/<ts>_McpBookkeeping.cs` | 三個欄位與 partial unique index |
| `src/SixJars.Api/Mcp/BookkeepingTools.cs` | 五個 tool |
| `src/SixJars.Api/Mcp/McpTransactionArgs.cs` | tool 參數 → `TransactionInput`（正數金額與 refund 的轉換，D14） |
| `src/SixJars.Api/Mcp/McpWriteQuota.cs` | 每日寫入上限（D12） |
| `src/SixJars.Api/Mcp/BookkeepingPrompt.cs` | `record_transactions` prompt |
| `tests/SixJars.Domain.Tests/Transactions/TransactionOriginTests.cs` | |
| `tests/SixJars.Infrastructure.Tests/Persistence/TransactionOriginPersistenceTests.cs` | |
| `tests/SixJars.Api.Tests/Mcp/RecordTransactionTests.cs` | command 與 query |
| `tests/SixJars.Api.Tests/Mcp/BookkeepingToolsTests.cs` | tool |
| `tests/SixJars.Api.Tests/Mcp/McpScopeTests.cs` | 同意頁與 scope、policy |

### 修改
| 檔案 | 改動 |
|---|---|
| `src/SixJars.Domain/Transactions/Transaction.cs` | `ExternalRef`、`CreatedVia`、`SetOrigin` |
| `src/SixJars.Application/Auditing/AuditEntry.cs`、`AuditTrail.cs`、`GetAuditHistory.cs`（`AuditEntryDto`） | `Via` |
| `src/SixJars.Application/Common/ICurrentUser.cs` | `string? Via => null;`（預設實作，見下方說明） |
| `src/SixJars.Application/Common/TaipeiTime.cs` | `StartOfDayUtc(DateOnly)` |
| `src/SixJars.Application/Transactions/TransactionDto.cs` | `CreatedVia`、`ExternalRef`（加在最後） |
| `src/SixJars.Application/Transactions/CreateTransaction.cs` | 改用 `TransactionCreation` |
| `src/SixJars.Application/Backup/BackupDocument.cs`、`RestoreBackup.cs` | v6；還原 origin 與 Via |
| `src/SixJars.Infrastructure/Persistence/TransactionConfiguration.cs`、AuditEntry 的 configuration、`SixJarsDbContextModelSnapshot.cs` | 欄位與 index |
| `src/SixJars.Api/Infrastructure/HttpCurrentUser.cs` | 讀 `via` claim |
| `src/SixJars.Api/Mcp/McpSetup.cs` | `McpWrite` policy、`BookkeepingTools`、`BookkeepingPrompt`、`AddAuthorizationFilters()` |
| `src/SixJars.Api/Mcp/OAuth/McpOAuth.cs`；`McpOAuthOptions`（定義在 `AuthorizeRequestValidator.cs`，不是獨立檔案） | `ReadScope`／`WriteScope`、`MaxWritesPerDay` |
| `src/SixJars.Api/Mcp/OAuth/AuthorizeRequestValidator.cs`、`ConsentPage.cs`、`OAuthEndpoints.cs`、`McpBearerHandler.cs` | scope 子集、核取方塊、授予的 scope、多 claim 與 via |
| `src/SixJars.Api/Mcp/ReconcilePrompt.cs` | server instructions 最後一句 |
| `tests/SixJars.Api.Tests/Mcp/McpTestTokens.cs`、`OAuthTestClient.cs`、`McpEndToEndTests.cs`、`McpDiscoveryTests.cs` | scope 參數、勾選寫入、端到端 |
| `tests/SixJars.Infrastructure.Tests/Backup/RestoreBackupTests.cs` | v6 往返 |
| `CONTEXT.md`、`CLAUDE.md`、`docs/deploy.md`、`.env.example` | 文件 |

`ICurrentUser.Via` 用介面預設實作的原因：`CliCurrentUser` 與兩個測試的 `FixedUser`（`RestoreBackupTests`、`ImportLegacyBookTests`）都實作這個介面，它們都不是經由 MCP。用預設實作，只有 `HttpCurrentUser` 需要覆寫，不必為了一個永遠是 null 的值改三個不相關的檔案。

### Task 相依順序
```
T1 (ADR+用語) ─▶ T2 (Domain origin) ─▶ T3 (persistence+DTO+Via) ─┬─▶ T4 (備份 v6)
                                                                 ├─▶ T5 (Record/Preview command) ─┐
                                                                 └─▶ T6 (scope+同意頁+via claim) ──┼─▶ T8 (validate/add+上限) ─▶ T9 (prompt) ─▶ T10 (端到端) ─▶ T11 (文件)
                                                                     T7 (讀取 tool，只依賴 T3) ───┘
```
- T4、T5、T6、T7 彼此獨立，可以並行（但都會改 `McpSetup.cs` 或測試 helper 的只有 T6、T7，兩者要依序做）。
- Checkpoint（測試全綠、可以收工）：
  - **C1** T3 後：資料層完成。
  - **C2** T7 後：授權與讀取 tool 完成，唯讀版的行為不變。
  - **C3** T10 後：功能完整。
  - T11 是純文件。

## 事實查核（2026-11-01，執行前）
| 引用 | 存在？ | 證據 | 修正 |
|---|---|---|---|
| `AddAuthorizationFilters()`、`[Authorize]` 用在 tool 上 | ✅ | ModelContextProtocol.AspNetCore 2.3.0 XML doc `HttpMcpServerBuilderExtensions.AddAuthorizationFilters` | — |
| `AddAuthorizationFilters` 會過濾 `tools/list` | ⚠️ 推定 | doc 只寫「support AuthorizeAttribute on tools」與 call 的 checkpoint | T8 Step 1 的 `Read_only_grant_does_not_list_write_tools` 是查證點；不成立就改用 `WithRequestFilters(b => b.AddListToolsFilter(...))` 加上 `MessageContext.User`（兩者 2.3.0 都有） |
| stateless 下 `ConfigureSessionOptions` 每個 request 呼叫一次 | ✅ | 同上 XML doc | 本計畫不用，留作上一列的後備 |
| `McpServerToolAttribute.ReadOnly/Destructive/Idempotent` | ✅ | ModelContextProtocol.Core 2.3.0 XML doc | — |
| `/mcp` policy 是 `RequireClaim("scope", McpOAuth.Scope)`，`McpBearerHandler` 只放一個 scope claim | ✅ | `McpSetup.cs`、`McpBearerHandler.cs` | T6 改成每個 scope 一個 claim |
| `AuthorizeRequestValidator` 遇到 read 以外的 scope 回 `invalid_scope` | ✅ | `AuthorizeRequestValidator.cs:76-81` | T6 改為接受子集 |
| 同意頁沒有 JS、CSP 是 `default-src 'none'` | ✅ | `ConsentPage.Page` | 提醒直接寫在頁面上，不做勾選後才顯示 |
| Income／Expense 金額帶號（`RequireNonZero`），轉帳類 `RequirePositive` | ✅ | `TransactionFactory.cs` | spec D14：tool 層轉換 |
| `TransactionDto` 是備份 `BackupTransaction` 的內容 | ✅ | `BackupDocument.cs:31` | 加欄位 = 備份自動帶上；`CurrentFormatVersion` 5 → 6 |
| `AuditEntryDto` 也進備份；還原時逐欄複製 | ✅ | `BackupDocument.cs:23`、`RestoreBackup.cs:97` | T4 複製 `Via` |
| `UpdateTransaction` 用 `ReplaceWith` 修改既有實體 | ✅ | `UpdateTransaction.cs:39-` | `ReplaceWith` 不動 origin（T2 測試） |
| `ApiFactory(connectionString, testServices, useTestAuthentication, settings)` 可設定組態 | ✅ | `tests/Shared/ApiFactory.cs` | 寫入上限的測試用 `settings` 設 `Mcp:MaxWritesPerDay=2` |
| `ApiFactory.CreateAsync` 只接受 `testServices` | ✅ | 同上 | 需要 settings 時用 `new ApiFactory(await postgres.CreateConnectionStringAsync(ct), settings: ...)` |
| 前端沒有稽核歷史畫面 | ✅ | `web/src` 沒有呼叫 `/audit` | spec D10：交易加 `CreatedVia` |
| Create 的檔案尚未存在 | ✅ | `docs/adr/0014*`、`BookkeepingTools.cs`、`RecordTransaction.cs`、`PreviewTransaction.cs`、`TransactionCreation.cs` 都不存在 | — |
| `SampleBook` 有 `Cash`、`Food`、`Factory` | ✅ | `tests/SixJars.Domain.Tests/SampleBook.cs`、`TransactionDeleteTests.cs` | — |
| `McpOAuthOptions` 是獨立檔案 | ❌ | 定義在 `AuthorizeRequestValidator.cs:6` | 修改清單已更正 |
| `Book.FindCategory(name)` | ⚠️ | 簽章是 `FindCategory(string mainName, string? subName = null)` | T5 範例改成 `("主食", "午餐")` |
| seed 帳本有標籤、鎖帳日 | ❌ | `ApiSeed.SeedBookAsync` 只建帳戶與分類 | T5、T7 的測試自己加 |
| `MutableTimeProvider` 可在 Mcp 測試中重用 | ✅ | `TokenEndpointTests.cs:234`，`internal`、頂層類別 | — |
| `Book.EnsureUnlocked(DateOnly)` 的判斷是 `date <= LockDate` | ✅ | `Book.cs:106-112` | Preview 的 `Locked` 用同一個條件 |
| AuditEntry 的 configuration 檔 | ✅ | `src/SixJars.Infrastructure/Persistence/AuditEntryConfiguration.cs` | 修改清單改為明確路徑 |
| T6、T8 的測試數 | ⚠️ | 初稿算錯 | 已重算：T5 是 15、T6 是 8、T8 是 13，後續預期總數一併修正 |

## 執行偏差（回寫，committed code 才是權威）

### C1（2026-11-02，T1–T3：8e27d45、c09b9c9、d753176）
- **T2 多改一個檔：`TransactionConfiguration.cs` 暫時 `Ignore` 兩個來源屬性。** 原因：EF 依慣例會把 public 屬性納入 model，T2 沒有 migration，整套測試因 `PendingModelChangesWarning` 失敗（398 個紅燈）。計畫原本沒料到這點。這樣做讓每個 commit 都維持全綠，也守住 spec「只有一個 migration」與「每個 Task 一個 commit」。T3 已移除 `Ignore`，改為真正對應欄位。
- **T3 多改 `IAuditTrail.cs`**：`RecordAs` 的 `string? via = null` 參數必須同時加在介面上才能編譯；計畫的修改清單漏列。唯一的外部呼叫端 `GoogleSignInValidator.cs:64` 不傳 via。
- Migration 實際名稱 `20261101161200_McpBookkeeping`；`Up` 只有三個 `AddColumn` 和一個帶 filter 的 `CreateIndex`（已審查）。
- 測試數與計畫一致：C1 後 **總計 787、成功 759、略過 28、失敗 0**（controller 親自重跑）。
- 變異檢查（controller）：拿掉 `SetOrigin` 的防重複 → `Set_origin_twice_throws` 失敗；拿掉 index filter 的 `DeletedAt` → `Same_external_ref_is_allowed_after_soft_delete` 失敗。
- 對 T4 的影響：T3 已讓 `TransactionDto`／`AuditEntryDto` 帶新欄位，所以匯出已包含它們；還原時仍會遺失，正是 T4 要補的。

### C2（2026-11-02，T4–T7：9da12ba、c7eaf34＋c65e3b5、743b818、f65c160＋b7ca48e）
- **T4 多改 `tests/SixJars.Api.Tests/BackupExportTests.cs`**：既有測試寫死格式版本 5，改為 `..._is_6`。
- **T5 新增 `BookkeepingInputRules.cs`**：審查指出 Kind 與 ExternalRef 的驗證規則在 Record／Preview 兩個 validator 中逐字重複，修正第 1 輪抽成 internal extension `AddBookkeepingInputRules(input, externalRef)`。因為 selector 是 delegate，用 `OverridePropertyName` 保持錯誤欄位名稱（`Input`、`Input.Kind`、`ExternalRef`）。
- **T5 的 Preview 查詢是追蹤查詢**，不是 `AsNoTracking`：`db.GetVersion` 需要追蹤。Preview 從不 `SaveChanges`，也不把試算出的交易加進 context，已審查確認不會外洩。
- **T6 改了既有 `AuthorizeEndpointTests` 的一列 theory**：`scope=ledger:write` 現在是合法 scope，改成 `ledger:admin`，仍測 `invalid_scope` 分支。
- **T6 給 T8 的介面**：
  - `McpOAuth.ReadScope`／`WriteScope`／`SupportedScopes`。
  - `McpSetup.WritePolicyName = "McpWrite"`（已定義、尚未使用）。
  - `McpOAuthOptions.MaxWritesPerDay`（定義在 `AuthorizeRequestValidator.cs`）。**T8 的 `McpWriteQuota` 必須讀同一個值**，同意頁顯示的數字才會與實際上限一致。
  - `McpTestTokens.IssueAsync(..., scope)`、`OAuthTestClient.ConsentAsync(..., allowWrite)`。
- **T7 新增 `McpBooks.cs`**，內含 `GetBookAsync(sender, …)`、`CategoryNames`、`EnsureValidPeriod(from, to)` 與 `MaxPeriodDays`；`LedgerTools.MaxPeriodDays` 已移除。期間驗證在修正第 1 輪才集中。
- **T7 新增測試 helper `tests/SixJars.Api.Tests/Mcp/McpToolClient.cs`**（`ConnectAsync(factory, subject, scope)`、`CallAsync`、`Text`），`LedgerToolsTests` 改用它。T8 的寫入測試請用 `scope: "ledger:read ledger:write"` 連線。
- **T7 改了既有 `LedgerToolsTests` 對 tool 清單的斷言**：原本寫死 3 個 tool，現在多了 3 個。T8 加入寫入 tool 後，要再更新這個斷言。
- **MCP tool 的選填參數必須有 C# 預設值**（`= null`，最後的 `CancellationToken = default`），否則 SDK binding 會失敗並回傳一般性的 tool error。T8 的 `add_transaction`／`validate_transaction` 也適用。
- 測試數與計畫一致：C2 後 **總計 819、成功 791、略過 28、失敗 0**（controller 親自重跑）。
- 變異檢查（controller）：
  - scope 改回單一 claim → `Read_write_token_still_passes_read_policy` 失敗。
  - 授予的 scope 改取自請求 → `Requesting_write_scope_without_checkbox_grants_read_only` 等 2 個測試失敗。
  - 冪等查詢弄壞 → `Record_with_same_external_ref_returns_existing_as_duplicate` 失敗。

### C3（2026-11-02，T8–T10：9172e3d、8ed1f0e、f43689d）
- **事實查核表的 ⚠️ 推定已證實**：單靠 `AddAuthorizationFilters()`，就會把沒有權限的 `[Authorize]` tool 從 `tools/list` 隱藏，並在呼叫時擋下，不需要自訂 `AddListToolsFilter`。只掛 `[Authorize]` 而沒有註冊 filters 時，`tools/list` 會擲 `InvalidOperationException`（fail-closed）。
- **T8 新增共用 helper**：
  - `TransactionRows`：search／validate／add 三個 tool 回傳同一種交易列的形狀。
  - `McpBooks.SendAsync`：例外轉成 `McpException`，NotFound／Validation／Domain 三種。
- **T8 更新既有 `LedgerToolsTests` 的 tool 清單**：唯讀 token 看得到 `validate_transaction`、看不到 `add_transaction`；`ReadOnlyHint == true` 的斷言不變，因此兼作過濾行為的第二個查證點。
- **T8 的已知取捨**（審查列為 Minor，留待最終審查判斷）：
  - 額度檢查與寫入不是原子操作，並行時可能超出幾筆。
  - 參數檢查（kind、amount）先於 `GetBookAsync`，所以非成員帶錯參數時會先看到參數錯誤。這和「非成員一律 404」的慣例有出入，但不洩漏任何資訊。
- **T9 改了 server instructions 開頭**：「唯讀帳本資料」改為「帳本資料」。寫入授權下已經不是唯讀。
- **T10 在 `McpEndToEndTests` 內抽出 `ConnectAsync`**；沒有改產品程式。
- 測試數與計畫一致：C3 後 **總計 835、成功 807、略過 28、失敗 0**（controller 親自重跑）。
- 變異檢查（controller）：
  - 拿掉 `add_transaction` 的 `[Authorize]` → 唯讀端到端與兩個 `Read_only_grant_*` 測試失敗。
  - 拿掉支出轉負數 → `Add_expense_stores_negative_amount_and_created_via`、`Validate_returns_postings_with_names_and_does_not_write` 失敗。

### 最終審查（2026-11-02，opus，a28541b..e69def0 → 修正 82b94e0）
- **Critical（已修）**：同意頁的「也允許新增交易」核取方塊在 `<form>` 之外，真實瀏覽器永遠不會送出 `allow_write`，正式環境拿不到 `ledger:write`。測試沒抓到的原因是 `OAuthTestClient.ConsentAsync` 自己塞欄位。修正：
  - 核取方塊移入表單。
  - 測試 client 改成只從 `<form>` 內取欄位，表單內沒有 `allow_write` 時直接失敗。
  - 新增回歸測試，斷言核取方塊位於表單內且預設不勾。
- **Important（已修）**：
  - 防釣魚警語補上「若勾選寫入，還能替你新增交易」。
  - `PreviewTransaction` 補呼叫 `EnsureNoNewArchivedTags`，試算與記帳拒絕同樣的輸入。
  - 新增還原測試：同一個 ExternalRef 一筆已刪除、一筆有效。
- 文件修正：
  - `CLAUDE.md` 列出完整的 tool 與 prompt。
  - ADR 0014 改正「並行寫入回 500」的說法（MCP 路徑是一般 tool error）。
  - `list_categories` 的描述不再暗示伺服器會擋封存分類。
- 最終 **總計 837、成功 809、略過 28、失敗 0**（controller 親自重跑）。變異檢查：還原時拿掉 `Delete` → 新的還原測試失敗。
- **暫緩、留作後續**（審查判定 can-wait）：
  - 額度檢查在 Api 層直接查 DbContext，且與寫入不是原子操作。
  - `HttpCurrentUser.Via` 沒有限定 scheme。
  - `mcp:{host}` 可能超過欄位長度 100。
  - `Mcp:MaxWritesPerDay` 沒有驗證必須 > 0。
  - 參數檢查先於 `GetBookAsync`（不洩漏資訊；要一致的話把 `ToInput` 移到後面即可）。
  - 測試缺口：台北日界線、非成員帶既有 sourceId、跨帳本同一個 ref、refund 用在非 Expense、web 寫入不計額度。
  - prompt 沒要求呈現 `locked`，唯讀授權時也沒說要停在表格。
  - 舊 grant 在重新同意後仍然有效（PR #23 的 grant 生命週期，不在本計畫範圍）。

---

## Task 1：ADR 0014 與領域用語
**Files:**
- Create：`docs/adr/0014-mcp-bookkeeping-write-scope.md`
- Modify：`docs/adr/0013-remote-mcp-with-embedded-oauth.md`（開頭加「部分被 ADR 0014 取代：寫入」）、`CONTEXT.md`

ADR 寫 spec 的 D2–D5、D12 與被否決的方案，並正面回應 0013 的翻轉條件：寫入只多一個 scope，由使用者在同意頁勾選，不需要「細緻授權」（每個帳本、每種操作各自授權），所以仍然不需要 OpenIddict。翻轉條件更新為：需要依帳本或操作分別授權，或要開放修改與刪除時。

`CONTEXT.md`：
- 「MCP 授權」改為：允許 AI client 讀取帳本，**經使用者勾選後**也能新增交易（記帳）。
- 「交易」段落新增「**來源識別碼 (External Reference)**」：Agent 記帳時，來源的穩定識別碼（例如信件的 Message-ID）；同一本帳本的未刪除交易不可重複，用來避免同一個來源記兩次。只能經由 MCP 寫入。_Avoid_：外部 Id、訊息 Id。
- 「帳本成員與輔助功能」新增「**經由 (Via)**」：一次寫入經由哪個 AI client（例如 `mcp:claude.ai`）；網頁與 CLI 的寫入沒有。記在交易（建立時）與稽核記錄上。_Avoid_：來源（易和來源識別碼混淆）、channel。

純文件，沒有測試。
- [ ] `dotnet build SixJars.slnx` 通過，warning 與基準線相同。
- [ ] `git add docs/adr/0014-mcp-bookkeeping-write-scope.md docs/adr/0013-remote-mcp-with-embedded-oauth.md CONTEXT.md`
- [ ] Commit：`docs: ADR 0014 MCP 開放記帳（ledger:write），加入來源識別碼與經由用語`

## Task 2：交易的來源（Domain）
**Files:**
- Create：`tests/SixJars.Domain.Tests/Transactions/TransactionOriginTests.cs`
- Modify：`src/SixJars.Domain/Transactions/Transaction.cs`

來源只能在建立時設定一次：記帳的 command 與還原備份都會呼叫。容易錯的是 `ReplaceWith`：修改交易時如果把 origin 一起換掉，Claude 記的交易被使用者在網頁上修改後，就會失去來源，也失去重複保護。

- [ ] Step 1：寫失敗測試
```csharp
using FluentAssertions;
using SixJars.Domain.Common;
using Xunit;

namespace SixJars.Domain.Tests.Transactions;

public class TransactionOriginTests
{
    private readonly SampleBook _s = new();

    [Fact]
    public void New_transaction_has_no_origin()
    {
        var transaction = _s.Factory.Expense(new DateOnly(2026, 1, 5), _s.Cash.Id, _s.Food.Id, -100m);

        transaction.CreatedVia.Should().BeNull();
        transaction.ExternalRef.Should().BeNull();
    }

    [Fact]
    public void Set_origin_records_via_and_external_ref()
    {
        var transaction = _s.Factory.Expense(new DateOnly(2026, 1, 5), _s.Cash.Id, _s.Food.Id, -100m);

        transaction.SetOrigin("mcp:claude.ai", "<abc@mail>");

        transaction.CreatedVia.Should().Be("mcp:claude.ai");
        transaction.ExternalRef.Should().Be("<abc@mail>");
    }

    [Fact]
    public void Set_origin_twice_throws()
    {
        var transaction = _s.Factory.Expense(new DateOnly(2026, 1, 5), _s.Cash.Id, _s.Food.Id, -100m);
        transaction.SetOrigin("mcp:claude.ai", null);

        var act = () => transaction.SetOrigin("mcp:other", "x");

        act.Should().Throw<DomainException>();
        transaction.CreatedVia.Should().Be("mcp:claude.ai");
    }

    [Fact]
    public void Set_empty_origin_is_no_op_and_allows_later_set()
    {
        var transaction = _s.Factory.Expense(new DateOnly(2026, 1, 5), _s.Cash.Id, _s.Food.Id, -100m);

        transaction.SetOrigin(null, null);
        transaction.SetOrigin("mcp:claude.ai", null);

        transaction.CreatedVia.Should().Be("mcp:claude.ai");
    }

    [Fact]
    public void Replace_keeps_origin()
    {
        var transaction = _s.Factory.Expense(new DateOnly(2026, 1, 5), _s.Cash.Id, _s.Food.Id, -100m);
        transaction.SetOrigin("mcp:claude.ai", "<abc@mail>");

        transaction.ReplaceWith(_s.Factory.Expense(new DateOnly(2026, 1, 6), _s.Cash.Id, _s.Food.Id, -200m));

        transaction.CreatedVia.Should().Be("mcp:claude.ai");
        transaction.ExternalRef.Should().Be("<abc@mail>");
    }
}
```
- [ ] Step 2：`dotnet test --project tests/SixJars.Domain.Tests --filter-class "*TransactionOrigin*"`。預期是**編譯失敗**，原因是 `CreatedVia`／`SetOrigin` 不存在；不是 namespace 打錯。
- [ ] Step 3：實作。`Transaction` 加 `public string? CreatedVia { get; private set; }`、`public string? ExternalRef { get; private set; }`：
```csharp
/// <summary>
/// 記下這筆交易是經由哪個 AI client、依據哪個來源建立的（spec D5、D10）。只在建立時設定一次：
/// 記帳 command 與還原備份會呼叫；修改交易（<see cref="ReplaceWith"/>）不改變來源。
/// </summary>
public void SetOrigin(string? createdVia, string? externalRef)
{
    if (createdVia is null && externalRef is null)
    {
        return;
    }

    if (CreatedVia is not null || ExternalRef is not null)
    {
        throw new DomainException("交易的來源只能設定一次。");
    }

    CreatedVia = createdVia;
    ExternalRef = externalRef;
}
```
  確認 `ReplaceWith` 沒有複製這兩個欄位（它逐欄複製，不要加進去）。
- [ ] Step 4：單一類別，預期 **5 passed**。
- [ ] Step 5：全部測試，預期 **總計 782、成功 754、略過 28**。
- [ ] Step 6：`git add src/SixJars.Domain/Transactions/Transaction.cs tests/SixJars.Domain.Tests/Transactions/TransactionOriginTests.cs`
  Commit：`feat(transactions): 交易記錄來源（經由的 client 與來源識別碼），只能在建立時設定一次`

## Task 3：資料層、DTO 與經由
**Files:**
- Create：`tests/SixJars.Infrastructure.Tests/Persistence/TransactionOriginPersistenceTests.cs`、migration `McpBookkeeping`
- Modify：`TransactionConfiguration.cs`、`AuditEntryConfiguration.cs`、`AuditEntry.cs`、`AuditTrail.cs`、`GetAuditHistory.cs`、`ICurrentUser.cs`、`TransactionDto.cs`

partial unique index 是重複保護的最後一道防線，必須用真的 PostgreSQL 驗證：EF 的 `HasFilter` 是原生 SQL 字串，欄位名稱的大小寫或引號寫錯，migration 仍會成功，只是 index 永遠不生效。

- [ ] Step 1：寫失敗測試。測試骨架照抄同資料夾的 `ConcurrencyTests.cs`，用它建立 DbContext 與 seed 帳本的方式；下面只寫斷言重點。
```csharp
[Fact] Origin_round_trips
// SetOrigin("mcp:claude.ai", "<a@m>") → 存檔 → 用新的 context 讀回 → 兩個欄位相同。

[Fact] Same_external_ref_in_same_book_is_rejected
// 兩筆交易都 SetOrigin(null, "<a@m>") → 第二次 SaveChanges 擲 DbUpdateException，
// InnerException 是 PostgresException，SqlState 為 "23505"。

[Fact] Same_external_ref_is_allowed_after_soft_delete
// 第一筆 Delete(now) 存檔後，第二筆用同一個 ref 可以存檔。

[Fact] Same_external_ref_in_another_book_is_allowed
```
  另外在 `tests/SixJars.Api.Tests/AuditTrailTests.cs` 加 `Web_write_has_no_via`：照 `Actor_is_the_signed_in_user` 的寫法，從網頁新增一筆交易，`GET /audit` 的那筆 `via` 是 null（或省略）。
- [ ] Step 2：執行兩個測試類別，預期因為 `ExternalRef` 不是 mapped 欄位，以及 `AuditEntryDto` 沒有 `Via` 而**編譯失敗**。
- [ ] Step 3：實作
  - `TransactionConfiguration`：
    - `Property(t => t.ExternalRef).HasMaxLength(255)`。
    - `Property(t => t.CreatedVia).HasMaxLength(100)`。
    - `HasIndex(t => new { t.BookId, t.ExternalRef }).IsUnique().HasFilter("\"ExternalRef\" IS NOT NULL AND \"DeletedAt\" IS NULL")`。
  - `AuditEntry` 加 `public string? Via { get; init; }`，configuration `HasMaxLength(100)`。
  - `ICurrentUser` 加 `/// <summary>經由哪個 AI client（例如 mcp:claude.ai）；網頁與 CLI 為 null。</summary> string? Via => null;`。
  - `AuditTrail.Record` 寫 `Via = user.Via`。`RecordAs` 是 CLI 與登入綁定在用，那裡沒有 MCP，所以不設定。實作方式：`RecordAs` 多一個 `string? via` 參數，預設 null，`Record` 傳 `user.Via` 進去。
  - `AuditEntryDto` 最後加 `string? Via = null`，`From` 帶入。
  - `TransactionDto` 最後加 `string? CreatedVia = null, string? ExternalRef = null`，`From` 帶入。
  - Migration：
    ```bash
    dotnet ef migrations add McpBookkeeping --project src/SixJars.Infrastructure --startup-project src/SixJars.Infrastructure --output-dir Persistence/Migrations
    ```
    檢查產生的 `Up` 只有三個 `AddColumn` 和一個 `CreateIndex`（帶 filter）。
- [ ] Step 4：兩個類別都通過（4 + 1）。
- [ ] Step 5：全部測試，預期 **總計 787、成功 759、略過 28**。`Export_restore_export_round_trips` 應該仍然通過，因為新欄位都是 null。
- [ ] Step 6：明確路徑 `git add` 上列檔案，加上 migration 的三個檔案。
  Commit：`feat(db): 交易的來源識別碼與經由、稽核記錄的經由；來源識別碼在帳本內（未刪除）唯一`

**Checkpoint C1。**

## Task 4：備份 v6
**Files:** Modify：`BackupDocument.cs`、`RestoreBackup.cs`、`tests/SixJars.Infrastructure.Tests/Backup/RestoreBackupTests.cs`

備份是「整本帳本」的承諾（`CONTEXT.md`）。如果還原後少了來源識別碼，同一封信就會被再記一次。

- [ ] Step 1：在 `RestoreBackupTests` 加兩個測試，照 `Export_restore_export_round_trips` 的寫法。
  - `Restores_transaction_origin_and_audit_via`：
    1. seed 一筆交易，呼叫 `SetOrigin("mcp:claude.ai", "<a@m>")`。
    2. 直接寫一筆 `Via = "mcp:claude.ai"` 的 `AuditEntry`。
    3. 匯出 → 還原 → 斷言交易的兩個欄位與稽核的 `Via` 都還在。
  - `Restores_v5_backup_without_origin`：拿 v5 格式的 JSON（照 `Restores_v3_backup_without_budgets` 的做法，把 `FormatVersion` 改成 5、刪掉新欄位），還原成功，新欄位是 null。
- [ ] Step 2：執行，預期第一個測試失敗，原因是還原後 `ExternalRef` 是 null；不是 seed 寫錯。
- [ ] Step 3：
  - `CurrentFormatVersion = 6`，類別的註解補一行「v6 起交易帶來源識別碼與經由，稽核記錄帶經由」。
  - 還原交易時，在 `TransactionBuilder.Build(..., fixedId)` 之後呼叫 `transaction.SetOrigin(dto.CreatedVia, dto.ExternalRef)`。
  - 還原稽核記錄時加 `Via = e.Via`。
- [ ] Step 4：`RestoreBackupTests` 全部通過。
- [ ] Step 5：全部測試，預期 **總計 789、成功 761、略過 28**。
- [ ] Step 6：Commit：`feat(backup): 備份格式 v6，保留交易來源與稽核記錄的經由`

## Task 5：RecordTransaction 與 PreviewTransaction
**Files:**
- Create：`TransactionCreation.cs`、`RecordTransaction.cs`、`PreviewTransaction.cs`、`tests/SixJars.Api.Tests/Mcp/RecordTransactionTests.cs`
- Modify：`CreateTransaction.cs`

測試放在 Api.Tests，透過 `factory.Services` 的 `ISender` 送出：需要真的 PostgreSQL 與 `BookAccessBehavior`，而 Application.Tests 沒有 DB。`ICurrentUser` 用 `testServices` 換成固定值（帶 Via）。

容易錯的地方：
- 冪等查詢要帶 `BookId`，而且只看未刪除的交易。`HasQueryFilter` 已經排除刪除的資料，不要加 `IgnoreQueryFilters`。
- 重複時直接回傳，**不能**寫稽核記錄。寫了就會佔掉 D12 的每日額度。

- [ ] Step 1：測試骨架與案例
```csharp
public class RecordTransactionTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Via = "mcp:claude.ai";

    private sealed class McpUser : ICurrentUser
    {
        public string Subject => ApiFactory.DefaultSubject;
        public string? Email => TestAuthHandler.EmailOf(ApiFactory.DefaultSubject);
        public string? Via => RecordTransactionTests.Via;
    }

    private static Task<ApiFactory> CreateFactoryAsync() =>
        ApiFactory.CreateAsync(postgres, Ct, s => s.AddScoped<ICurrentUser, McpUser>());

    private static async Task<T> SendAsync<T>(ApiFactory factory, IRequest<T> request)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, Ct);
    }

    private static TransactionInput Lunch(Book book, decimal amount = -120m) => new(
        TransactionKind.Expense, new DateOnly(2026, 1, 5), null, amount, book.FindAccount("現金")!.Id.Value,
        null, book.FindCategory("主食", "午餐")!.Id.Value, null, null, "Subway");
}
```
  - 帳戶與分類名稱照 `ApiSeed.SeedBookAsync` 實際建立的：現金、國泰世華銀行、國泰Combo卡、主食／午餐、工作薪資。seed 沒有標籤，也沒有鎖帳日；需要的測試自己在 seed 後用 `GetBookForUpdateAsync` 加上。
  - 測試案例：
    - `Record_sets_created_via_and_external_ref_and_audits_via`：交易的 `CreatedVia == Via`、`ExternalRef == "<a@m>"`；稽核記錄的 Create 那筆 `Via == Via`。
    - `Record_with_same_external_ref_returns_existing_as_duplicate`：第二次回傳 `Duplicate == true`，`Transaction.Id` 相同；資料庫只有 1 筆交易、1 筆 Create 稽核記錄。
    - `Record_after_deleting_same_ref_creates_new`
    - `Record_without_external_ref_always_creates`：同樣的輸入呼叫兩次，得到 2 筆。
    - `Record_rejects_unsupported_kind`（Theory：`FundAllocation`、`LoanPayment`、`CashDeposit`、`TopUp`）：擲 `ValidationException`。
    - `Record_rejects_blank_or_too_long_external_ref`（Theory：`" "`、256 個字元）。
    - `Record_on_locked_date_throws_domain_exception`
    - `Preview_does_not_write`：呼叫後交易筆數不變，回傳的 `Transaction.Postings` 有 1 筆 -120。
    - `Preview_reports_locked_instead_of_throwing`
    - `Preview_reports_existing_by_external_ref_and_suspected_duplicates`：先 Record 一筆 -120 的午餐，再用同日、同金額、同帳戶、不同 ref 呼叫 Preview → `SuspectedDuplicates` 有 1 筆，`ExistingByExternalRef` 是 null。用相同 ref 呼叫 → `ExistingByExternalRef` 不是 null。
    - `Non_member_gets_not_found`：另一本帳本 → `NotFoundException`（由 `BookAccessBehavior` 擲出）。
- [ ] Step 2：預期因為型別不存在而**編譯失敗**。
- [ ] Step 3：實作
  - `TransactionCreation.Create(Book book, TransactionInput input)`：依序 `EnsureUnlocked` → `TransactionBuilder.Build` → `EnsureNoNewArchivedTags(book, [], ...)`，回傳 `Transaction`。`CreateTransactionHandler` 改用它，行為不變，既有測試要全部維持通過。
  - `RecordTransaction` validator：
    - `Input` 套用 `TransactionInputValidator`。
    - `Input.Kind` 必須屬於 `RecordTransaction.SupportedKinds`（static，`McpTransactionArgs` 也會引用）。
    - `ExternalRef` 不是 null 時：不可空白，`MaximumLength(255)`。
  - `RecordTransaction` handler：
    1. 有 ref 時，先查 `db.Transactions.Where(t => t.BookId == bookId && t.ExternalRef == ref)`，查到就回傳 `new RecordedTransaction(TransactionDto.From(existing, db.GetVersion(existing)), Duplicate: true)`。
    2. 沒查到就 `TransactionCreation.Create` → `SetOrigin(user.Via, ref)` → Add → 稽核 → SaveChanges。
    3. 稽核的快照在 `SetOrigin` **之後**取得，快照才會包含來源。
  - `PreviewTransaction`（唯讀，不追蹤）：
    - `Build`，不檢查鎖帳日。
    - `Locked = book.LockDate is { } lockDate && input.Date <= lockDate`，和 `Book.EnsureUnlocked(DateOnly)` 的判斷一致（已查證）。
    - 查同 ref 的交易。
    - 疑似重複：同 `BookId`、同 `Date`、同 `Amount`，而且 `AccountId` 或 `CounterAccountId` 等於輸入的 `AccountId`。
- [ ] Step 4：`--filter-class "*RecordTransactionTests*"`，預期 **15 passed**（Theory 展開後：一般 9 個、Kind 4 個、ref 2 個）。
- [ ] Step 5：全部測試，預期 **總計 804、成功 776、略過 28**。`TransactionsEndpointsTests` 必須全部維持通過，用來證明 `CreateTransaction` 的行為沒有變。
- [ ] Step 6：Commit：`feat(transactions): MCP 記帳 command（來源識別碼冪等）與唯讀試算`

## Task 6：寫入 scope、同意頁與經由 claim
**Files:**
- Create：`tests/SixJars.Api.Tests/Mcp/McpScopeTests.cs`
- Modify：`McpOAuth.cs`、`McpOAuthOptions.cs`、`AuthorizeRequestValidator.cs`、`ConsentPage.cs`、`OAuthEndpoints.cs`、`McpBearerHandler.cs`、`HttpCurrentUser.cs`、`McpSetup.cs`、`OAuthTestClient.cs`、`McpTestTokens.cs`、`McpDiscoveryTests.cs`

這個 Task 最容易錯的有三點：
- **單一 claim 存多個 scope**：policy `RequireClaim` 是整值比對，授予讀寫兩個 scope 後，唯讀的 tool 反而會全部被擋（401）。
- **授予的 scope 必須來自核取方塊**，而不是請求參數：同意頁的 hidden `scope` 欄位使用者可以改，所以只能用來重新驗證請求本身。
- `Valid` 已經帶有 `ClientHost`；提醒裡的 host 要用它，不要用 CIMD 文件裡的 `client_name`。

- [ ] Step 1：測試
  - `OAuthTestClient.ConsentAsync` 加參數 `bool allowWrite = false`，為 true 時 `fields["allow_write"] = "1"`。
  - `McpTestTokens.IssueAsync` 加參數 `string scope = "ledger:read"`。
  - `McpScopeTests`：
    - `Consent_page_offers_write_checkbox_unchecked_with_warnings`：HTML 包含 `name="allow_write"`，**不**包含 `checked`；包含「不能修改或刪除」、「每天最多 15 筆」，以及 `claude.ai`。
    - `Consent_without_write_grants_read_only`：code → token 回應的 `scope == "ledger:read"`。
    - `Consent_with_write_grants_read_and_write`：`scope == "ledger:read ledger:write"`；DB 裡 grant 的 `Scope` 相同。
    - `Requesting_write_scope_without_checkbox_grants_read_only`：authorize 帶 `scope=ledger:read ledger:write`，但不勾 → 只授予 read。
    - `Unknown_scope_is_rejected`：`scope=ledger:admin` → 轉址帶 `error=invalid_scope`（既有行為）。
    - `Read_write_token_still_passes_read_policy`：用 scope 是 `"ledger:read ledger:write"` 的 token 呼叫 `list_books` 會成功。這是單一 claim 那個 bug 的回歸測試。
    - `Http_current_user_reads_via_claim` 與 `Http_current_user_via_is_null_without_claim`：用 `DefaultHttpContext` 加 `HttpContextAccessor`，直接測 `HttpCurrentUser`。
    - bearer token 帶出的 via，是否真的一路寫到交易，由 T8 的 `Add_expense_stores_negative_amount_and_created_via` 驗證。在這裡驗證需要一個只給測試用的 endpoint，不值得。
  - `McpDiscoveryTests`：兩份 metadata 的 `scopes_supported` 都等於 `["ledger:read", "ledger:write"]`。
- [ ] Step 2：預期失敗原因：
  - 同意頁沒有核取方塊。
  - 授予的 scope 永遠是 read。
  - discovery 只列出一個 scope。
  - `Read_write_token_still_passes_read_policy` 回 401。這一項**一定要親眼看到它失敗**，它證明多 claim 修正確實有必要。
- [ ] Step 3：實作
  - `McpOAuth`：`ReadScope = "ledger:read"`、`WriteScope = "ledger:write"`、`SupportedScopes = [ReadScope, WriteScope]`；原本引用 `Scope` 的地方全部改名。
  - `McpOAuthOptions.MaxWritesPerDay = 15`，從 `Mcp:MaxWritesPerDay` 繫結。照既有 `AllowedClientIdHosts` 的寫法，在 `AddSixJarsMcp` 的 `Configure` 裡讀。
  - Validator：scopes 必須是 `SupportedScopes` 的子集（不能空）。`Valid.Scope` 維持「請求的 scope」，同意頁的 hidden 欄位用它。
  - `ConsentAsync`：`var granted = form["allow_write"] == "1" ? $"{McpOAuth.ReadScope} {McpOAuth.WriteScope}" : McpOAuth.ReadScope;`，寫進 `AuthorizationCode` 的 `Scope`。
  - `ConsentPage.Render` 多一個參數 `int maxWritesPerDay`：
    - 唯讀說明改成「預設只能讀取」。
    - 新增核取方塊區塊，內容：`<label><input type="checkbox" name="allow_write" value="1"> 也允許 <strong>{host}</strong> 新增交易（記帳）</label>`。
    - 底下列出：只能新增、不能修改或刪除；每天最多 N 筆；依你提供的信件或對話內容記帳，請只連線你信任的 client；撤銷方式。
  - `McpBearerHandler`：
    - 改成 `Select(g => new { g.ClientId }).FirstOrDefaultAsync(g => g.Id == ... && g.RevokedAt == null)`。
    - claims：`token.Scope.Split(' ')` 每個 scope 一個 claim，再加 `new("via", $"mcp:{new Uri(grant.ClientId).Host}")`。
  - `HttpCurrentUser.Via => accessor.HttpContext?.User.FindFirst("via")?.Value;`
  - `McpSetup`：
    - `ResourceMetadata.ScopesSupported = McpOAuth.SupportedScopes`。
    - 新 policy `WritePolicyName = "McpWrite"`（`McpBearer` scheme、`RequireClaim(scope, WriteScope)`）。
    - 授權伺服器 metadata（`OAuthEndpoints.cs:25`）也改用 `SupportedScopes`。
- [ ] Step 4：`--filter-class "*McpScope*"`、`*McpDiscovery*`、`*AuthorizeEndpoint*`、`*TokenEndpoint*` 都通過。
- [ ] Step 5：全部測試，預期 **總計 812、成功 784、略過 28**（McpScope 8 個；discovery 是改寫既有測試，不增加數量）。
- [ ] Step 6：Commit：`feat(mcp): ledger:write scope 由同意頁勾選授予，access token 每個 scope 一個 claim，並帶經由`

## Task 7：讀取用的記帳 tool
**Files:**
- Create：`BookkeepingTools.cs`（先只有 3 個讀取 tool）、`tests/SixJars.Api.Tests/Mcp/BookkeepingToolsTests.cs`
- Modify：`McpSetup.cs`（`.WithTools<BookkeepingTools>()`）

`BookkeepingTools` 是新類別，不放進 `LedgerTools`：後者的 remarks 寫的是「唯讀 ledger tools」，而寫入 tool 要另外掛權限。共用的 `GetBookAsync` 與 `CategoryNames` 從 `LedgerTools` 抽成 `internal static class McpBooks`，避免複製貼上；`LedgerTools` 改成呼叫它，行為不變。

- [ ] Step 1：測試。`ConnectAsync` 與 `CallAsync` helper 從 `LedgerToolsTests` 複製並加 `scope` 參數；如果第三個測試類別也要用，就抽成 `McpToolClient`。
  - `List_categories_returns_tree_with_kind`：「主食」的 `kind == "Expense"`，`children` 包含「午餐」；「工作薪資」的 `kind == "Income"`。
  - `List_tags_returns_archived_flag`：seed 沒有標籤，測試自己用 `book.AddTag(...)` 加兩個，並封存其中一個。
  - `Search_by_amount_matches_absolute_value`：seed 一筆 -120 的支出，用 `amount: 120` 查得到；`amount` 回傳 -120。
  - `Search_by_keyword_is_case_insensitive_on_note`
  - `Search_by_source_id_returns_created_via`：用 `ISender` 送 `RecordTransaction`（帶 McpUser）seed 一筆，用 `sourceId` 查回，`createdVia == "mcp:claude.ai"`。
  - `Search_period_over_120_days_is_rejected`
  - `Read_tools_work_with_read_only_token`
- [ ] Step 2：預期 tool 不存在，`CallToolAsync` 回 `IsError` 且錯誤是「unknown tool」；不是連線失敗。
- [ ] Step 3：實作三個 tool（spec §6.2），回傳 record 照 spec 的欄位命名（camelCase 由 SDK 處理）。
- [ ] Step 4：`*BookkeepingTools*` 7 passed；`*LedgerTools*` 維持全部通過（McpBooks 抽取後）。
- [ ] Step 5：全部測試，預期 **總計 819、成功 791、略過 28**。
- [ ] Step 6：Commit：`feat(mcp): 記帳用的讀取 tool（分類樹、標籤、交易搜尋）`

**Checkpoint C2。** 唯讀授權的行為與先前相同，只多了三個讀取 tool。

## Task 8：validate_transaction、add_transaction 與寫入上限
**Files:**
- Create：`McpTransactionArgs.cs`、`McpWriteQuota.cs`
- Modify：`BookkeepingTools.cs`、`McpSetup.cs`（`.AddAuthorizationFilters()`）、`TaipeiTime.cs`、`BookkeepingToolsTests.cs`

這是整個功能的核心，也是安全邊界所在：
- **權限**：唯讀 token 既不能**看到**也不能**呼叫** `add_transaction`。第一個測試同時是 SDK 行為的查證點（見事實查核）。
- **D14 轉換**：Expense 金額是 `refund ? amount : -amount`。`amount <= 0` 時直接拒絕，不要讓 Claude 用負數繞過。
- **上限**：計數不能把重複（duplicate）算進去。重複時 command 不寫稽核記錄，而計數本來就是從稽核記錄來的，所以這點會自動成立；用測試鎖住。

- [ ] Step 1：測試（`BookkeepingToolsTests` 擴充）
  - `Read_only_grant_does_not_list_write_tools`：`ListToolsAsync` 的名稱**不含** `add_transaction`，但含 `validate_transaction`。
  - `Read_only_grant_cannot_call_add_transaction`：直接 `CallToolAsync("add_transaction", ...)`，可能回 `IsError`，也可能擲 `McpException`。兩種都接受，但交易筆數必須是 0。
  - `Write_grant_lists_add_transaction`
  - `Add_expense_stores_negative_amount_and_created_via`：`amount: 120` → DB 裡是 -120，`CreatedVia == "mcp:claude.ai"`。這同時驗證 T6 的 via claim 一路傳到交易。
  - `Add_refund_stores_positive_amount`
  - `Add_rejects_non_positive_amount`（Theory：0、-5）
  - `Add_rejects_unsupported_kind`：`kind: "FundAllocation"` → `IsError`，訊息列出支援的種類。
  - `Add_same_source_twice_returns_duplicate`：第二次 `duplicate == true`，`id` 相同。
  - `Add_card_payment_requires_counter_account`：缺 `counterAccountId` → `IsError`（訊息來自 validator）。
  - `Validate_returns_postings_with_names_and_does_not_write`
  - `Write_quota_is_enforced_per_day`：factory 設定 `Mcp:MaxWritesPerDay=2`，`TimeProvider` 固定在台北某日 → 第 3 筆 `IsError`，訊息包含「上限」；把時間推進到隔天（用 `MutableTimeProvider`，Mcp 測試資料夾已經有）→ 可以再寫。
  - `Duplicates_do_not_count_toward_quota`：上限 1；寫入 1 筆，再用同一個 `sourceId` 呼叫 → 回傳 `duplicate` 而不是錯誤。
- [ ] Step 2：預期失敗原因：tool 不存在（unknown tool）。**重點**：等 `add_transaction` 實作完、還沒加 `AddAuthorizationFilters` 之前，再跑一次，確認 `Read_only_grant_*` 兩個測試會失敗。這證明測試確實鎖住權限，而不是剛好通過。
- [ ] Step 3：實作
  - `McpTransactionArgs.ToInput(kind, date, amount, refund, accountId, counterAccountId, categoryId, budgetMonth, note, tagIds)`：
    - 解析 `kind`（必須屬於 `RecordTransaction.SupportedKinds`，否則 `McpException` 並列出支援的種類）。
    - `amount <= 0` → `McpException`。
    - `refund` 只對 Expense 有效，其他種類帶 `refund: true` → `McpException`。
    - 依 D14 決定正負號。
  - `TaipeiTime.StartOfDayUtc(DateOnly date)`：台北當日 00:00 換算成 UTC 的 `DateTimeOffset`。
  - `McpWriteQuota.EnsureAvailableAsync(ICurrentUser user, CancellationToken)`：
    - 計數條件：`AuditEntries`、`ActorSubject == user.Subject`、`Via == user.Via`、`Action == Create`、`EntityType == Transaction`、`At >= StartOfDayUtc(DateOf(now))`。
    - 筆數 ≥ 上限時擲 `McpException("今天經由 Claude 記帳已達上限 N 筆，明天再試或改在網頁上記帳。")`。
  - `add_transaction`：
    - 標 `[Authorize(Policy = McpSetup.WritePolicyName)]`、`ReadOnly = false, Destructive = false, Idempotent = false`。
    - 有 `sourceId` 時，先查是否已存在，存在就直接回傳 duplicate，**不檢查上限**。
    - 否則先檢查上限，再送 `RecordTransaction`。
    - 錯誤轉換照 spec §6.1。
  - `validate_transaction`：送 `PreviewTransaction`，把 Id 轉成名稱。
  - `McpSetup`：`.WithTools<BookkeepingTools>()` 之後加 `.AddAuthorizationFilters()`。
- [ ] Step 4：`*BookkeepingTools*` 全部通過（7 + 13 = 20，Theory 展開後）。
- [ ] Step 5：全部測試，預期 **總計 832、成功 804、略過 28**。
- [ ] Step 6：Commit：`feat(mcp): add_transaction 與 validate_transaction，限寫入授權、每日上限，金額一律正數`

## Task 9：記帳 prompt 與 server instructions
**Files:**
- Create：`BookkeepingPrompt.cs`
- Modify：`ReconcilePrompt.cs`、`McpSetup.cs`、`BookkeepingToolsTests.cs`

- [ ] Step 1：測試
  - `Record_transactions_prompt_lists_confirm_before_add`：`GetPromptAsync("record_transactions")` 的文字依序包含 `validate_transaction`、「確認」、`add_transaction`，也包含「當作資料」（防 injection 的提醒）。
  - `Server_instructions_mention_bookkeeping_requires_confirmation`：`client.ServerInstructions` 不再包含「你只能讀取，不能修改帳本」。
- [ ] Step 2：確認 prompt 不存在而失敗。
- [ ] Step 3：實作 spec §6.5，並 `.WithPrompts<BookkeepingPrompt>()`。
- [ ] Step 4、5：全部測試，預期 **總計 834、成功 806、略過 28**。
- [ ] Step 6：Commit：`feat(mcp): record_transactions prompt，固定「試算 → 確認 → 新增」流程`

## Task 10：端到端
**Files:** Modify：`tests/SixJars.Api.Tests/Mcp/McpEndToEndTests.cs`

既有的端到端測試是走唯讀授權。這次新增一條「勾選寫入」的完整路徑：同意頁 → code → token → MCP 記帳 → 用 `/api` 讀回。這證明每一層的 scope、claim、via 都接得起來，T6 與 T8 各自的測試證明不了這點。

- [ ] Step 1：
  - `Write_consent_to_add_and_read_back_via_api`：`ConsentAsync(allowWrite: true)` → token 的 scope 含 write → `add_transaction` → 用 cookie client 呼叫 `GET /api/books/{id}/transactions` → 那筆的 `createdVia == "mcp:claude.ai"`、`externalRef` 正確。
  - 既有的唯讀端到端測試加一行斷言：`ListToolsAsync` 不含 `add_transaction`。
- [ ] Step 2：先把 `ConsentAsync` 呼叫裡的 `allowWrite: true` 拿掉跑一次，確認測試會因為看不到 tool 而失敗，再加回來。
- [ ] Step 3：通常不需要改產品程式；如果需要，表示 T6 或 T8 有漏洞，回頭補測試。
- [ ] Step 4、5：全部測試，預期 **總計 835、成功 807、略過 28**。
- [ ] Step 6：Commit：`test(mcp): 勾選允許記帳到 API 讀回的端到端測試`

**Checkpoint C3。**

## Task 11：文件
**Files:** Modify：`CLAUDE.md`、`docs/deploy.md`、`.env.example`

- `CLAUDE.md` 的「遠端 MCP」：
  - `/mcp` 改為「預設唯讀；授權時勾選允許記帳才有 `ledger:write`」。
  - 記帳 tool 在 `BookkeepingTools`，寫入一律經 `RecordTransaction`（ADR 0014）。
- `docs/deploy.md` §13：
  - 新 migration `McpBookkeeping`。
  - `Mcp__MaxWritesPerDay`（可省略，預設 15）。
  - 既有連線要在 Claude 移除後重新連線，並勾選允許記帳。
  - 首次上線的檢查清單加一項：用一封真實信件試記一筆，再到網頁確認有「Claude」標記。
- `.env.example`：`Mcp__MaxWritesPerDay=15`（註解寫明可省略）。
- [ ] `dotnet build` 通過。
- [ ] Commit：`docs: MCP 記帳的部署設定與連線方式`

## 完成後的驗證
- [ ] `dotnet test --solution SixJars.slnx`：失敗 0、略過 28；總數約 835（預測值，以實際為準，但失敗數必須是 0）。
- [ ] `dotnet build SixJars.slnx`：只有既有的 EF1002 warning。
- [ ] `git status`：乾淨。
- [ ] `git log --oneline feat/mcp-server..`：11 個 commit，加上計畫回寫的 `docs(plans):` commit。
- [ ] `git diff feat/mcp-server -- '*.cs' | grep -i "connectionstring\|password"`：沒有任何敏感資料。
- [ ] Migration `McpBookkeeping` 的 `Up` 只有三個 `AddColumn` 和一個帶 filter 的 `CreateIndex`。

## 手動驗證（需要真實的 Claude 與部署環境）
1. 本機套用 migration；部署後在 claude.ai 移除舊的 connector，重新連線。同意頁應該出現「也允許新增交易」核取方塊，預設不勾。
2. **不勾**直接允許 → Claude 的 tool 清單裡沒有 `add_transaction`；請它記帳時，它應該說做不到。
3. 重新連線並**勾選** → 貼上一封電子發票信件，使用 `record_transactions` prompt。Claude 應該先列表格請你確認，確認後才新增。
4. 網頁上那筆交易有「Claude」標記，編輯頁顯示來源識別碼（前端計畫完成後）。
5. 同一封信再處理一次 → Claude 回報重複、略過，帳本沒有多一筆。
6. 確認 Claude 實際請求的 scope：查 Cloud Run log 中 `/oauth/authorize` 的 query。結果回寫到 ADR 0014 的「未驗證前提」。

## 後續（不在本計畫內）
- 修改、刪除交易的 tool；批次寫入；草稿匣。
- 財務規劃帳戶與貸款類交易的記帳。
- 依名稱解析帳戶與分類。
- 同一個 ExternalRef 並行寫入時回 409（目前是 500）。
- step-up 授權（`insufficient_scope`）。
- 前端「只看 Claude 記的交易」篩選、稽核歷史畫面。
