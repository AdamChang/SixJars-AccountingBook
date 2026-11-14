# 橫切關注點

> 階段 3 整合，commit `f181aaf`，2026-11-14。本文把散在各模組的同一類機制並排比較，找出不一致之處；個別機制的細節與證據見括號內的模組文件。

## 1. 認證

### 三種身分來源

| 來源 | 入口 | 驗證方式 | `ICurrentUser.Subject` | 模組 |
|---|---|---|---|---|
| 瀏覽器 | `/api/**`、`/oauth/authorize` | Cookie `__Host-sixjars-auth`（HttpOnly、Secure、Lax、14 天 sliding），由 Google OIDC 登入後發出 | Google `sub` | `13` |
| MCP client | `/mcp` | `McpBearer`：DataProtection 加密的自包含 access token，1 小時效期，每次請求查一次 `McpGrant` 是否撤銷 | 授權當下的 `sub` | `14` |
| CLI | 本機 process | 沒有認證，能連資料庫就能操作 | 固定字串 `cli` | `15` |

三者都產生同名的 claim（`sub`、`email`），所以 `ICurrentUser` 之後的程式碼不必區分來源。證據：`13`「三種身分來源」。

### Scheme 隔離

- `/api` 用預設 scheme（cookie）。`/mcp` 的 policy 指定只認 `McpBearer`，並要求 `scope = ledger:read`。
- 結果是瀏覽器帶 cookie 打不進 `/mcp`，bearer token 也打不進 `/api`。
- 由 `SecurityConventionTests.Every_mcp_endpoint_requires_mcp_bearer_policy` 守住。證據：`13`「認證設定重點」、`14`「Authentication scheme 的分工」。

### 白名單

- 登入白名單 = `BookMembers` ∪ `BookCreators`。第一次登入以已驗證的 email 綁定 `sub`，之後只認 `sub`（`13`「白名單與 sub 綁定規則」）。
- 名單只能由 CLI 維護。可以新增成員，但沒有移除成員的入口（`90-tech-debt.md` TD-16）。
- Cookie 不會重新驗證白名單（刻意的取捨，ADR 0011）。實際影響有限，因為每次存取帳本都會重新查 `BookMembers`。

### 共同的信任根：DataProtection 金鑰

Cookie、OAuth authorization code、MCP access token 三者的完整性都靠 `DataProtectionKeys` 表裡的金鑰。目前沒有設定 `ProtectKeysWith*`；階段 3 以 grep 確認 `src/` 內沒有出現過。所以能讀這張表的人可以偽造任何使用者的 cookie 與 access token（`90-tech-debt.md` TD-03）。

## 2. 授權

| 層 | 規則 | 執行位置 | 守門的測試 |
|---|---|---|---|
| 是否登入 | `/api` group `RequireAuthorization`；匿名白名單只有 `/health*`、`/auth/**`、`/oauth/**`、`/.well-known/**`、`/mcp` | `Program.cs` | `Every_api_endpoint_requires_sign_in` |
| 能碰哪本帳 | `OwnedBookIds(sub)`：`BookMembers` 中 `GoogleSubject == sub && Role == Owner`；非成員一律 404 | `BookAccessBehavior`（pipeline 第一個） | `BookScopeConventionTests` |
| 資料屬於哪本帳 | handler 的查詢必須同時帶 `BookId` 條件 | 每個 handler 自己負責 | 是否有逐 endpoint 的跨帳本測試，未確認（`99` Q-D3） |
| 不屬於帳本範圍的權限 | `ForbiddenException` → 403；目前只有 `CreateBook` 用到 | handler | — |
| CLI 例外 | `ICliOnlyRequest` 不做成員檢查；靠 `internal` 讓 Api 參考不到 | 編譯期 | `Cli_only_requests_are_not_public` 等 |

- **角色**：`BookRole` 定義了 `Owner`、`Bookkeeper`、`ReadOnly`，但實際上只會建立和認可 `Owner`，目前沒有讀寫之分。
- `OwnedBookIds` 同時用在授權、帳本清單、建立額度三個地方。日後要開放其他角色時必須拆開（`13` 風險 #7）。

## 3. XSRF 與瀏覽器安全

| 項目 | 現況 | 模組 |
|---|---|---|
| XSRF | `AntiforgeryFilter` 掛在 `/api` 與 `POST /auth/logout`，非安全方法必須帶 `X-XSRF-TOKEN`；前端靠 Angular `HttpClient` 預設行為附加 header，【推論】未明確設定 | `13`、`16` |
| MCP 同意頁 | 伺服器端組的 HTML，帶 CSP `frame-ancestors 'none'`、`no-store`；表單有 antiforgery | `14` |
| SPA 與其他回應 | 沒有 HSTS、CSP、`X-Frame-Options`；【推論】TLS 與 HTTPS 轉址由 Cloud Run 前端負責 | `13` 風險 #8、`17` |
| Rate limiting | 沒有；匿名的 `/health` 會連資料庫，`/oauth/authorize` 會對外抓 CIMD | `13`、`14` |
| Forwarded headers | 信任任何來源，前提是「container 只能經由 Google 前端連到」 | `13` 風險 #11 |
| Issuer／audience | 取自請求的 Host，不另外設定 | `14` 風險 #11 |

最後兩項都是「換部署平台就要重新檢查」的隱含前提，程式碼本身無法驗證。

## 4. 錯誤處理

### 端到端對應

| 情況 | 後端例外 | HTTP | 前端 `ApiError.kind` | 使用者看到 |
|---|---|---|---|---|
| 欄位格式錯 | `ValidationException` | 400＋`errors` | `validation` | 記帳表單逐欄顯示；其他頁顯示第一則 |
| XSRF 失敗 | （filter） | 400＋固定標題 | `antiforgery` | 提示重新整理 |
| 未登入 | （authorization） | 401 | `unauthorized` | 整頁導向登入；未送出的輸入會遺失 |
| 不在建立者名單 | `ForbiddenException` | 403 | `forbidden` | 建立帳本對話框的訊息 |
| 非成員、不存在 | `NotFoundException` | 404 | `notFound` | 提示已不存在並重新載入；`bookGuard` 導回首頁 |
| 並行衝突 | `DbUpdateConcurrencyException` | 409 | `conflict` | 「已在其他裝置修改」並重新載入 |
| 業務規則 | `DomainException` | 422＋`code` | `domain` | `locked` 用固定文案，其餘顯示後端訊息 |
| 唯一索引、FK、權限不足 | `DbUpdateException`、`PostgresException` | **500** | `unavailable` | 「暫時無法連線，請稍後再試」 |

證據：`13`「例外對應」、`16`「HTTP 與錯誤處理」。

最後一列是三個模組各自觀察到的同一個缺口：
- `11` 風險 #3：同時產生同一個月、同時第一次設定同一分類的預算。
- `13` 風險 #3：同時執行 `add-member`。
- `17` 風險 #1：正式環境的 app 角色沒有 `AuditEntries` 的 DELETE 權限（已確認），所以刪除帳本會在這裡失敗。

資料本身都不會受損（約束擋住、transaction 回滾），但使用者會被引導去「稍後重試」，而重試結果還是一樣。合併為 `90-tech-debt.md` TD-10。

### 其他執行環境

| 環境 | 做法 | 模組 |
|---|---|---|
| CLI | `Host.RunAsync` 集中處理。validation、`DomainException`、`NotFoundException`、並行衝突印訊息後 exit 1；其他例外印例外鏈（遮蔽連線字串與密碼）、不印 stack trace，exit 1；缺連線字串 exit 2 | `15` |
| 匯入轉換 | 每一列各自 catch `DomainException` 與 `RowRejected`，以便一次回報全部錯誤；只要有一筆錯誤，整本都不寫入 | `15` |
| OAuth 端點 | 自己轉成 RFC 6749 錯誤碼；`/oauth/token` 把所有 `DbUpdateException` 都當成重複兌換 | `14` |
| MCP tool | 只有 `GetBook` 的 `NotFoundException` 轉成 `McpException`；其他交給 SDK 處理 | `14` |
| 登入 callback | `OnRemoteFailure` 一律導向 `/denied`；【推論】資料庫錯誤也會顯示成「被拒絕」 | `13` 風險 #10 |

**重試**：整個系統沒有 Polly 或應用層重試。唯一的重試迴圈在部署流程（startup probe 12 次、smoke test 6 次），用來容忍 Neon 喚醒的時間（`17`）。

## 5. 一致性與並行控制

| 機制 | 用在 | 不用在（刻意或缺漏） |
|---|---|---|
| `xmin` 樂觀並行，前端帶回 `version` | 交易、預定支出、週期項目 | — |
| `xmin`，但不帶前端版本（只擋同時存檔） | 帳本設定、Refresh、MCP refresh 輪替、CLI 補匯 | 帳本設定因此擋不住「看到舊畫面後才送出」（`10` 風險 #4） |
| 不做並行控制 | 預算（刻意，`11`）、成員與建立者（沒有 row version） | — |
| Advisory lock | 建立帳本 | CLI 新帳本匯入（`15` 風險 #7） |
| 唯一索引作為最後防線 | 產生預定支出、成員 `(BookId, Email)`、建立者 email、MCP code | 違反時回 500（見上節） |
| DB 外鍵 | 只有 `PlannedExpenses.SourceId` | 交易對帳本、帳戶、分類、財務規劃帳戶、標籤都沒有 |

「先檢查、再寫入」而中間沒有鎖的模式反覆出現：
- 刪除設定 vs. 新增交易（`10` 風險 #1）
- 刪除帳本 vs. 寫入（`10` 風險 #2）
- 設定鎖帳日 vs. 新增交易（`10` 風險 #3）
- 預算 vs. 改分類性質（`11` 風險 #5）

在個人／家庭帳本的併發量下機率很低。但這些競態留下的孤兒參照，會在報表（靜默漏算）、匯出（整份失敗）、還原（整份拒絕）三個地方以不同方式出現（`12` 風險 #5），合併為 TD-04。

## 6. 快取

| 快取 | 位置 | 失效方式 | 風險 |
|---|---|---|---|
| 帳本唯讀快取 | 各 instance 的 `IMemoryCache`，`book:{bookId}`，sliding 1 小時，無容量上限 | 不在寫入時失效；每次讀取先比對 `Books.xmin`。只改 owned 設定時由 `TouchBooksWithChangedSettings` 強制更新 `Books` 列 | 回傳的 `Book` 跨 request 共用、而且是可變物件，「不可修改」只靠註解約束（TD-07） |
| 前端 `CurrentBook` | 記憶體，只存目前這一本 | 本頁寫入後 `reload()`；其他裝置的變更要整頁重新載入 | `16` 風險 #4 |
| 前端 `SessionService.me` | 記憶體 | 成員關係改變時 `reload()` | — |
| Service worker | 只快取靜態資源 | ngsw 版本機制；沒有 `SwUpdate` 提示 | `16` 風險 #7 |
| CIMD 文件 | **不快取**，每次 authorize 的 GET 與 POST 各抓一次 | — | `14` 風險 #4 |
| 成員檢查 | **不快取** | — | 好處是移除成員後立即生效 |

## 7. 稽核

| 面向 | 現況 | 模組 |
|---|---|---|
| 寫入方式 | handler 明確呼叫 `IAuditTrail.Record`，與業務資料同一次存檔；不用 interceptor（ADR 0006） | `13` |
| 快照格式 | 直接重用 API DTO，存 `jsonb`；修改後的 `Version` 恆為 0 | `13` |
| 覆蓋範圍 | 所有帳本範圍的 Web 寫入、首次登入綁定成員、CLI `add-member`、匯入（一筆摘要）、還原 | `13`、`15` |
| **不**留稽核 | 建立者名單異動（ADR 0011）、MCP 授權的建立／輪替／撤銷與 tool 呼叫（ADR 0013）、登入成功與拒絕、備份與匯出下載、匯入的逐筆交易 | `12`、`13`、`14`、`15` |
| 讀取 | 只能依單一實體查詢；前端沒有畫面 | `13`、`16` |
| append-only 的例外 | `DeleteBook` 清除該帳本全部稽核記錄（ADR 0010），只留一筆從 API 讀不到的摘要 | `13` 風險 #9 |

DTO 同時擔任 API 回應、稽核快照與備份格式三個角色（`10` 風險 #13、`11` 風險 #10、`12` 風險 #4）。改 API 欄位會同時改變備份 schema 和既有快照的可讀性，目前靠「新欄位一律給預設值」維持相容。

## 8. Logging 與可觀測性

| 項目 | 現況 | 證據 |
|---|---|---|
| 應用程式 log | `src/` 只有 4 個檔案用到 `ILogger`：`HealthEndpoints`、`ClientMetadataFetcher`、`McpBearerHandler`、`CliApp`（階段 3 以 grep 確認）。業務流程、登入結果、授權決策都沒有 log | `13` 風險 #4 |
| CLI | `AddLogging()` 但不加任何 provider，避免 EF Core 帶出連線資訊 | `15` |
| 可診斷性的特例 | `/health/ready` 在 log 中區分「連不上」「有 pending migration」「讀不到 migration 歷史」三種原因（`v1.0.1` 事故後加入） | `17` |
| 監控、告警 | 沒有；與「不 ping」的閒置策略（ADR 0007）互相牽連 | `17` 風險 #11 |
| 結構化 log、trace、metrics | 沒有設定（沒有 OpenTelemetry 或 Serilog）；【推論】只有 Cloud Run 收集的 stdout 與平台指標 | `00-inventory.md` 套件清單 |

合併為 TD-12：出了問題時，能用來排查的資料幾乎只有框架預設的 log。

## 9. 設定管理

| 設定鍵 | 用途 | 本機 | 正式環境 | 缺少時 |
|---|---|---|---|---|
| `ConnectionStrings__SixJars` | 資料庫（`<REDACTED>`） | 環境變數或 user secrets | Secret Manager `sixjars-db:latest` | Api、CLI 啟動即失敗（CLI exit 2） |
| `Authentication__Google__ClientId` | OIDC | 環境變數 | GitHub variable → `--set-env-vars` | 非 Development 啟動即失敗；Development 不註冊 Google scheme |
| `Authentication__Google__ClientSecret` | OIDC（`<REDACTED>`） | 同上 | Secret Manager `:latest` | 同上 |
| `MediatR__LicenseKey` | MediatR 授權（`<REDACTED>`） | 選填 | GitHub secret → `--set-env-vars`（在 revision 設定中為明碼） | 只記一筆 warning |
| `Mcp__AllowedClientIdHosts__N` | 可信任的 CIMD host | 選填 | **未設定** → 預設 `claude.ai` | 使用預設值 |
| `ASPNETCORE_ENVIRONMENT` | 環境 | — | workflow 寫死 `Production` | — |
| `ASPNETCORE_HTTP_PORTS` | 監聽埠 | — | image 寫死 `8080` | — |

證據：`.env.example`、`17`「設定項的來源與去向」。

原則與例外：
- repo 內沒有 `appsettings*.json`，也沒有任何真實值，全部來自環境變數。
- Cloud Run 的環境變數每次部署整組覆寫。但 CPU、concurrency、timeout 等平台設定沿用現值，不在 repo 裡（`17` 風險 #9）。
- 兩個 secret 以 `:latest` 參照，值的變更不經過部署關卡（TD-08）。
- 資料庫角色與授權完全不在版本控制內（TD-02）。

## 10. 時間

| 位置 | 「今天」的來源 | 證據 |
|---|---|---|
| 後端 | 注入的 `TimeProvider` 搭配 `TaipeiTime`（`Asia/Taipei`）；deploy 時檢查 image 內有時區檔 | 專案 `CLAUDE.md`、`17` |
| MCP token 效期 | payload 內的到期時間，以注入的 `TimeProvider` 判斷（不用 time-limited protector） | `14` |
| 前端 | 瀏覽器時鐘與時區（`new Date()`）；日期字串一律以本機時間組成，不經 `toISOString` | `16` 風險 #6 |

在台灣以外的時區使用時，前端的預設月份可能和後端差一天（月初月底時差一個月）。

## 11. 背景工作

沒有。`src/` 內沒有 `IHostedService`／`BackgroundService`，也沒有排程。原本可能做成背景工作的事情，各有替代做法：

| 工作 | 做法 | 依據 |
|---|---|---|
| 週期預定支出 | 使用者對某月按「產生」或「以現值更新」 | ADR 0009 |
| Migration | 使用者在本機執行 `efbundle` | ADR 0012 |
| 備份 | 使用者以瀏覽器下載 | ADR 0010（否決伺服器端自動備份） |
| 保持喚醒 | 不做 | ADR 0007 |
| 清理 `McpGrants`、過期快取 | 沒有清理；快取靠 sliding 到期 | `14` 風險 #7 |
| Artifact Registry 舊映像 | GCP cleanup policy（平台端的背景工作） | `17` |
