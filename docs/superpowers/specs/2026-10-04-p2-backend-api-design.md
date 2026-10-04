# P2 後端：API、認證、稽核與部署設計

- 日期：2026-10-04
- 範圍：**後端**（.NET API、CLI、部署設定）。Angular PWA 另寫一份前端 spec 與 plan（tddplan 慣例）。
- 依據：[CONTEXT.md](../../../CONTEXT.md)、[ADR 0001–0006](../../adr/)、[P1 設計](2026-10-04-p1-ledger-core-design.md)、P1 計畫的「後續」清單
- 狀態：**已核准（2026-10-04）**。§9 的 O1–O3 都照建議定案。

## 1. 目標

讓 P1 的帳務核心可以透過登入後的 HTTP API 使用，並部署到 Cloud Run + Neon（ADR 0007，原為 Supabase）。完成時：

1. 擁有者以 Google 帳號登入（白名單），可以讀寫帳本設定、交易、預定支出，並查詢月可用餘額、年累計餘額、可用現金、帳戶餘額與財務規劃帳戶餘額。
2. 餘額由 **SQL 彙總**算出，對同一份資料，結果與 Domain 計算器完全一致（以 P1 的計算器作為 oracle）。
3. 每次寫入都留下稽核記錄（修改前與修改後的快照）；刪除是軟刪除；鎖帳日之前（含當日）的資料不可異動。
4. 可以用 CLI 把舊 xlsm 匯入正式資料庫，也可以匯出 JSON 備份（能用 CLI 還原），並匯出交易明細的 CSV／xlsx。
5. 提供 Dockerfile 與部署文件；實際部署、push、設定 Secret 由使用者執行。

### 階段歸屬（2026-10-04 使用者確認）

| 功能 | 階段 |
|---|---|
| API、SQL 餘額、Neon（原 Supabase）、Google OIDC＋白名單、稽核記錄、軟刪除、CLI 匯入、JSON 備份、Cloud Run | **P2** |
| 鎖帳日強制執行、CSV／xlsx 匯出 | **P2**（使用者加入） |
| 超過 30 天沒備份的提醒、記帳範本 | P3 以後 |
| 預算、提醒事項、信用卡對帳、房貸試算、月備忘錄、外部資產淨值、報表 | P3 以後（之後分配） |
| 記帳者、唯讀角色 | P3 以後（P2 只實作**擁有者**，但資料模型已經保留角色欄位） |

## 2. 架構

### 2.1 專案切分

```
src/SixJars.Domain           ＋ 鎖帳日、交易修改、軟刪除、帳本成員
src/SixJars.Application      ＋ MediatR command/query、FluentValidation、稽核介面、匯出與備份格式
src/SixJars.Infrastructure   ＋ SQL 彙總查詢、稽核寫入、匯出實作、ConnectionString 組態
src/SixJars.Api       （新）  Minimal API endpoints、認證（BFF cookie）、ProblemDetails、/health
src/SixJars.Cli       （新）  import-legacy、restore-backup
tests/SixJars.Api.Tests（新）  WebApplicationFactory + Testcontainers，端到端 HTTP 測試
```

- **Minimal API + MediatR**（使用者 2026-10-04 確認）：endpoint 只負責綁定請求、呼叫 `ISender.Send`，再把結果轉成 HTTP 回應。業務邏輯一律放在 handler。endpoint 依功能分組（`MapGroup("/api/books/{bookId}/transactions")`）。
  - **否決：Controllers。** 檔案較多，而且這個專案沒有用到 filter 生態系的需求。
- **FluentValidation**：以 MediatR pipeline behavior 執行，驗證失敗時回 400 `ValidationProblemDetails`。FluentValidation 只做**輸入形狀**檢查（必填、正負號、長度）；**業務規則**（帳戶類型限制、鎖帳日）仍由 Domain 擲出 `DomainException`。這樣規則不會分散在兩個地方。
- **CLI 與 API 共用 Application 的 command**：CLI 只是另一個 composition root，不另寫一套寫入邏輯。

### 2.2 錯誤對應

| 來源 | HTTP |
|---|---|
| FluentValidation 失敗 | 400 `ValidationProblemDetails` |
| `DomainException`（違反業務規則） | 422 ProblemDetails，`code` 放錯誤代碼 |
| 違反鎖帳日 | 422，`code = "locked"` |
| 不是帳本成員，或資源不存在 | **404**（不透露帳本是否存在） |
| 未登入 | 401（API 不轉址；登入轉址只在 `/auth/login` 發生） |
| 樂觀並行衝突 | 409 |

### 2.3 並行控制

交易與預定支出都用 PostgreSQL 的 `xmin` 作為並行 token。PUT／DELETE 必須帶上讀取時拿到的 `version`，不一致就回 409。

理由：同一位擁有者可能同時開著手機 PWA 和桌機，最後一次寫入覆蓋前一次會造成無聲的資料遺失。`xmin` 不需要額外欄位，也不需要 migration 維護。

## 3. Domain 增補

P1 的 Domain 是建立後就不能改的。P2 需要支援以下變更，而且都在 Domain 裡用測試鎖住：

### 3.1 鎖帳日

> 實際狀況：P1 spec §7 說鎖帳日「只建模欄位」，但 `Book` 實際上**沒有**這個欄位。P2 從零開始做。

- `Book.LockDate : DateOnly?`，以及 `Book.SetLockDate(DateOnly?)`。可以往前移，也可以往後移，或清除；每次變更都會寫入稽核記錄。
- **交易**：新增、修改、刪除時，若 `Date ≤ LockDate` 就擲出 `DomainException("locked")`。修改時，**原本**的日期與**新的**日期都要檢查，所以不能把交易搬進或搬出鎖定區間。
- **預定支出**：以歸屬月份的最後一天判斷；最後一天 ≤ LockDate 的月份就被鎖住。
- 檢查放在 Domain（`Book.EnsureUnlocked(date)`），由 handler 在寫入前呼叫，CLI 與 API 都會經過這個檢查。

### 3.2 交易修改

- 修改等於「用 `TransactionFactory` 依新內容產生一筆交易，再把它的欄位與分錄搬進原本的實體」：`Transaction.ReplaceWith(Transaction draft)`。Id 不變，分錄整組重新產生（P1 D3 已經這樣定義）。
- 可以修改交易類型。分錄展開規則仍然只寫在 `TransactionFactory` 一個地方。
- **否決：以「刪除舊交易＋新增一筆」作為修改。** Id 會變，稽核記錄的歷史因此斷成兩段，預定支出的付款連結也得跟著搬。

### 3.3 軟刪除

- `Transaction`、`PlannedExpense` 加上 `DeletedAt : DateTimeOffset?`。EF 設定 global query filter，`SQL 彙總查詢`也必須明確排除已刪除的資料，並由測試鎖住。
- 已刪除的資料不能修改，也不能還原。還原留到 P3；P2 想救回資料時，看稽核記錄裡的快照。
- 帳本設定（帳戶、財務規劃帳戶、分類）在 P2 **只能新增，不能刪除**，因為它們已經被分錄參照。改名與封存屬於 P3。
- **刪除已付款預定支出所連結的交易**：見 §9 O2。

### 3.4 帳本成員

`BookMember(BookId, Email, GoogleSubject?, Role, AddedAt)`，`Role` 列舉為 `Owner | Bookkeeper | ReadOnly`。P2 只建立、也只授權 `Owner`；其他兩個值已經定義，但任何 endpoint 都不接受。

- 白名單就是這張表（使用者 2026-10-04 確認）。第一位擁有者由 CLI 建立（`import-legacy --owner-email`，或 `add-member`）。
- 以 email 比對，但第一次登入成功時會綁定 Google `sub`。之後以 `sub` 為主：比 email 穩定，Google 帳號改 email 也不會失去存取權。
- **否決：把白名單放在環境變數。** 改名單要重新部署，而且一加入記帳者角色就得搬進 DB。

## 4. 認證（ADR 0005）

- **BFF + HttpOnly cookie**（使用者 2026-10-04 確認）。ASP.NET Core 用 `AddOpenIdConnect` 跑 Google 的 authorization code flow（Authority `https://accounts.google.com`），登入成功後發 cookie：`HttpOnly`、`Secure`、`SameSite=Lax`，滑動期限 14 天。
- Angular 由**同一個 Cloud Run 服務**的 `wwwroot` 提供（前端 spec 決定建置方式），因此同源，不需要 CORS。
- `OnTokenValidated`：要求 `email_verified = true`，並依 §3.4 比對帳本成員。比對不到就拒絕登入（導向 `/auth/denied`），不發 cookie。
- **Antiforgery**：所有非 GET 的 `/api/**` 請求都要帶 `X-XSRF-TOKEN` header，值取自 `XSRF-TOKEN` cookie。Angular `HttpClient` 內建支援這個慣例，前端不用另外寫程式。
- **授權**：每個 `/api/books/{bookId}/**` 請求都會經過 `BookMemberAuthorizationHandler`，以登入者的 `sub` 查詢他在該帳本的角色，必須是 Owner。查不到一律回 404。
- endpoint：`GET /auth/login?returnUrl=`、`POST /auth/logout`、`GET /api/me`（回傳 email、名稱，以及登入者所屬的帳本清單）。
- 測試：
  - API 測試用 `TestAuthHandler` 注入指定的 `sub`。
  - OIDC 本身只測組態，以及 `OnTokenValidated` 的白名單邏輯（直接用 `ClaimsPrincipal` 呼叫，不連 Google）。
  - 真正走一次 Google 登入屬於**手動驗證**。
- **否決：由 SPA 取得 Google ID token，API 驗 JWT。** token 會存在瀏覽器，XSS 可以偷走；Google ID token 一小時就過期，前端還得處理續期。

## 5. API 介面

所有路徑都在 `/api/books/{bookId}` 底下。DTO 命名使用 CONTEXT.md 的英文詞彙。

| 方法 | 路徑 | 說明 |
|---|---|---|
| GET | `/` | 帳本設定：帳戶（含類型、計入可用現金）、財務規劃帳戶、兩層分類、鎖帳日 |
| POST | `/accounts`、`/planning-funds`、`/categories` | 新增設定項目（只能新增） |
| PUT | `/lock-date` | 設定或清除鎖帳日 |
| GET | `/transactions?from=&to=&budgetMonth=&accountId=` | 依 `Date`、建立順序排序（P1 留下的 OrderBy 問題在這裡處理）；回傳 `version` |
| POST | `/transactions` | 平面 DTO：`kind` 加上各類型會用到的可選欄位，由 validator 依 `kind` 檢查必填欄位 |
| PUT／DELETE | `/transactions/{id}` | 修改與軟刪除，都要帶 `version` |
| GET | `/planned-expenses?budgetMonth=` | |
| POST、PUT、DELETE | `/planned-expenses[/{id}]` | |
| POST | `/planned-expenses/{id}/pay` | 依預定支出建立 Expense 或 LoanPayment 交易，並建立付款連結（同一個 DB transaction） |
| GET | `/summary?budgetMonth=&asOf=` | 月可用餘額、年累計餘額、可用現金（含與不含電子錢包）、各帳戶餘額、各財務規劃帳戶餘額 |
| GET | `/export/transactions.csv`、`.xlsx`（`?from=&to=`） | 交易明細 |
| GET | `/export/backup.json` | 完整備份 |
| GET | `/audit?entityId=` | 某一筆資料的修改歷史（唯讀） |

另外有 `GET /health`，**不需要登入**，會執行 `SELECT 1`，供監控或部署後確認使用。原本規劃以 Cloud Scheduler 定時 ping 避免 Supabase 閒置暫停；改用 Neon 後不再需要（ADR 0007）。

- **交易 DTO 用平面結構而不是多型 JSON**：Angular 的表單本來就是平面欄位，切換類型時只要顯示或隱藏欄位。多型 `$type` 會讓前後端都多一層序列化設定。
- `/summary` 一次回傳所有數字。ADR 0004 提到應用程式與資料庫跨國，每次往返約數十毫秒，所以把**一次請求的資料庫往返次數**也列為測試斷言（以 EF 的命令計數，≤ 6 次：成員授權 1 次、帳本設定 1 次、彙總 4 次。原本預期 ≤ 3，實作後經使用者同意調整）。

## 6. SQL 餘額彙總

- 計算改由 SQL 執行：帳戶與財務規劃帳戶的餘額是期初加上 `SUM(Postings.Amount)`；月可用餘額以 `Kind` 與帳戶類型做條件加總，規則照搬 P1 §4.1；可用現金以「計入可用現金」篩選。
- 實作：`ILedgerSummaryQuery` 放在 Application，Infrastructure 以 EF LINQ 實作，並先確認 LINQ 會翻成單一條 `GROUP BY`。翻不出來的地方才改用 `FromSql`。
- **Oracle 測試**（P2 最主要的正確性保證）：
  1. **合成資料**：用 Domain 測試的 `SampleBook` 產生涵蓋所有交易類型的交易，寫入 DB 後，比對 SQL 彙總結果與 P1 計算器（讀 `LedgerSnapshot`）的結果。每個指標都必須相等，**包含已軟刪除的資料**（SQL 必須排除，計算器原本就看不到）。
  2. **真實資料**（驗收；`reference/` 不存在時略過）：匯入 xlsm 後，SQL 版本的 1–3 月數字與 Excel 一致，`KnownExcelDifferences` 的調整照舊。
- P1 的計算器保留，作為 oracle 與單元測試的規格，不刪除。
- **否決：維持載入整個快照、在記憶體中計算。** 帳本是連續的，資料只會越來越多；每次查 summary 都把整本帳搬過跨國網路，成本會一直上升。

## 7. 稽核記錄（ADR 0006）

- `AuditEntries` 表是 append-only，欄位為 `Id, BookId, At, ActorSubject, Action(Create|Update|Delete|Import|Restore|LockDateChanged), EntityType, EntityId, Before jsonb?, After jsonb?`。
- **在 Application 層明確記錄**：每個寫入用的 command handler 呼叫 `IAuditTrail.Record(...)`，傳入 Domain 物件序列化後的快照（交易連同分錄）。記錄和業務資料在**同一次** `SaveChanges` 寫入，兩者會一起成功、一起失敗。
- 涵蓋所有寫入：設定、交易、預定支出、鎖帳日、成員、匯入、還原。匯入與還原各只寫一筆摘要記錄（筆數與檔名），不會逐筆展開。
- **否決：用 EF `SaveChangesInterceptor` 自動記錄。** Postings 是 owned collection，change tracker 會把一次修改拆成好幾個實體變更，難以還原成「一筆交易的修改前與修改後」；而且自動攔截的行為很難用測試表達「這個 command 應該留下什麼記錄」。
- 由測試鎖住：每個寫入 endpoint 都要留下恰好一筆記錄，而且 Before／After 內容正確；寫入失敗（422 或 409）時沒有記錄。

## 8. 匯入、備份、匯出、部署

### 8.1 CLI（`SixJars.Cli`）

- `import-legacy --file <xlsm> --book-name <名稱> --owner-email <email> [--dry-run]`：
  - 重用 P1 的 `ExcelLegacyWorkbookReader` 與 `LegacyWorkbookMapper`。
  - 匯入報告有錯誤時，**不寫入任何資料**。
  - 同名的帳本已經存在時拒絕執行，避免重複匯入。
  - 建立擁有者成員。
  - 以單一 DB transaction 寫入，並寫一筆 Import 稽核記錄。
- `restore-backup --file <json> --owner-email <email>`：只能還原到**不存在同 Id 帳本**的資料庫。
- `add-member --book <id> --email <email>`：P2 只能加入擁有者。
- 連線字串只讀環境變數 `ConnectionStrings__SixJars`。沒有設定就直接失敗，不會預設連到任何地方。
- **否決：API 上傳匯入**（使用者 2026-10-04 確認）。搬家只做一次，不值得處理檔案上傳、大小限制、Cloud Run 逾時與更大的攻擊面。

### 8.2 JSON 備份

- 內容：帳本設定、所有交易（含分錄，**包含已軟刪除的交易**）、預定支出、帳本成員、稽核記錄，以及 `formatVersion` 與 `exportedAt`。
- **往返測試**：匯出 → 還原到空資料庫 → 再匯出，兩份 JSON 除了 `exportedAt` 之外完全相同；summary 數字也一致。ADR 0004 要求「可匯回」，這個測試就是在驗證這點。
- 格式由 `System.Text.Json` source generator 定義，加上 `formatVersion = 1`。之後格式若有變更，由還原端負責升級舊格式。

### 8.3 CSV／xlsx 匯出

- 欄位：日期、歸屬月份、交易類型、帳戶、對方帳戶、主分類、子分類、財務規劃帳戶、金額、本金、利息、備註。只匯出未刪除的交易。
- CSV 用 UTF-8 with BOM，Excel 開啟繁體中文才不會亂碼。xlsx 的套件候選是 ClosedXML（MIT），在事實查核時確認版本與授權。

### 8.4 資料庫（Neon，ADR 0007）與部署

- 連線：環境變數 `ConnectionStrings__SixJars`，指向 Neon 的**直連 endpoint**（不含 `-pooler` 的 host），`SSL Mode=Require`。repo 附上 `.env.example` placeholder，內容不含任何帳密。
- **Migration**：用 `dotnet ef migrations bundle` 產生執行檔，由使用者在部署前手動執行。**不在 app 啟動時自動 migrate**：Cloud Run 多個 instance 同時啟動會互相競爭，而且 app 的 DB 帳號不需要 DDL 權限。
- **Dockerfile**：multi-stage、`mcr.microsoft.com/dotnet/aspnet:10.0`、非 root 使用者、監聽 `$PORT`。
- **部署文件** `docs/deploy.md` 涵蓋：
  - Cloud Run（asia-east1）
  - 以 Secret Manager 提供連線字串、Google client secret 與 MediatR license key
  - Google OAuth client 的 redirect URI
  - 不設定 Cloud Scheduler 定時 ping：Neon 閒置時自動暫停、下次連線自動喚醒，定時 ping 反而會耗用 compute 額度（ADR 0007，使用者已確認）
- Data Protection key（cookie 加密用）存在 DB 的 `DataProtectionKeys` 表（EF Core 提供者）。否則 Cloud Run instance 一換，所有人就會被登出。

## 9. 核准時定案的項目（2026-10-04，全部照建議）

| # | 問題 | 決定 |
|---|---|---|
| O1 | MediatR 從 v13 起改為商業授權，個人與小型組織可以用免費的 Community license，但需要設定 license key。 | 用 MediatR 最新版，搭配 Community license（P1 的 FluentAssertions 8 也是同樣的取捨），key 從環境變數讀取。事實查核時確認授權條款與沒有 key 時的行為。替代方案：鎖在 12.x（Apache，已停止維護），或改用 `Mediator`（source generator，MIT）。 |
| O2 | 某筆交易已經連結為某預定支出的付款，刪除這筆交易時該怎麼處理？ | 預定支出自動回到「未付」，並在同一筆稽核記錄裡注記。否決「禁止刪除」，因為使用者會無法修正記錯的付款。 |
| O3 | 鎖帳日能否往前移（解鎖已鎖的期間）？ | 可以。只有擁有者能操作，而且會留下稽核記錄。鎖帳日是保護使用者避免誤改，不是法定關帳。 |

## 10. 分段（使用者 2026-10-04 確認：四段，延續 P1 的命名）

| 段 | 內容 | checkpoint 時可交付的成果 |
|---|---|---|
| **C** | Api 骨架、`/health`、ProblemDetails、MediatR 與 validation pipeline、帳本設定 API、交易 CRUD（含修改與 `xmin`）、預定支出與付款、SQL 彙總與 oracle 測試。此段的認證先用開發用的 `TestAuthHandler`。 | 在本機，可以透過 HTTP 記帳與查詢餘額 |
| **D** | 軟刪除、稽核記錄、鎖帳日 | 寫入路徑完整 |
| **E** | 帳本成員、BFF cookie＋Google OIDC、授權 handler、antiforgery、安全性測試（401、404、缺少 XSRF token） | 可以安全地對外開放 |
| **F** | CLI（import-legacy、restore-backup、add-member）、JSON 備份往返、CSV／xlsx 匯出、Neon 連線設定、Dockerfile、部署文件 | 使用者可以自行部署 |

每段結束時都要全綠、一個 Task 一個 commit，然後停下來讓使用者檢視，並用 `docs(plans):` 回寫偏差。

## 11. 測試策略

- `Domain.Tests`：鎖帳日、`ReplaceWith`、軟刪除、帳本成員規則（純單元測試）。
- `Application.Tests`：validator 與 handler，使用 in-memory 的假 repository（只在 handler 有分支邏輯時才寫）。
- `Infrastructure.Tests`：SQL 彙總的 oracle 測試、query filter、稽核記錄寫入、`xmin` 衝突（Testcontainers）。
- `Api.Tests`：`WebApplicationFactory<Program>`，加上 Testcontainers 與 `TestAuthHandler`。每個 endpoint 都測成功與失敗路徑；E 段再加上整組安全性測試。
- `AcceptanceTests`：新增「xlsm → CLI 匯入流程 → SQL summary」與 Excel 的比對。
- 每段結束後，主控者都要獨立審查：重跑全部測試與 build、做變異測試、比對 diff 與計畫，並掃描機密。

## 12. 明確排除（不在 P2）

前端 PWA（另一份 spec）；預算、報表、提醒事項、記帳範本、信用卡對帳、房貸試算、月備忘錄、外部資產淨值；30 天備份提醒；記帳者與唯讀角色的授權；帳本設定的改名、封存與刪除；軟刪除的還原；離線支援；P1 留下的孤兒子分類瑕疵（數字不受影響，維持現狀）。
