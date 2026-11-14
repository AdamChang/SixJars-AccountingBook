# 技術債清單

> 階段 3 整合，commit `f181aaf`，2026-11-14。來源是 `10`～`17` 各模組的「風險與觀察」，在這裡去重、合併同源項目並重新評定嚴重度。
> 本文只寫建議方向，不寫修改後的程式碼。「證據位置」列出最直接的原始碼；完整論證看括號內的模組風險編號，例如 `10`#1 是 `10-module-ledger-core.md` 的風險 #1。
> 標【推論】的項目尚未以測試或正式環境驗證，處理前請先確認（對應問題列在 `99-open-questions.md`）。

## 嚴重度的判定方式

| 等級 | 判定標準 |
|---|---|
| 高 | 現在就可能讓功能完全不可用；或讓唯一的復原手段在需要時失效 |
| 中 | 在合理情境下會造成錯誤資料、安全暴露或明顯的維運負擔；或是多個低風險項目的共同根源 |
| 低 | 只在罕見競態、非預期操作或規模大幅成長時才會出現；或只影響開發與排查體驗 |

和模組文件相比，有兩處等級刻意調整：
- **TD-01 升為高**：`17`#1 原標「中（若正式環境照手冊授權則為高）」。階段 3 以 grep 確認 `docs/deploy.md` 第 231 行的 REVOKE 與 `DeleteBook.cs` 第 47 行的 DELETE 確實並存，也確認測試容器以預設的超級使用者連線，CI 驗證不到。**2026-11-14 使用者已在正式環境確認 `sixjars_app` 沒有 `AuditEntries` 的 DELETE 權限**（Q-P1），條件成立。
- **TD-05 由多個「中」合併升為高**：備份不是 snapshot（`12`#1）、還原以現行規則重驗（`12`#2）、孤兒參照（`10`#1）、備份不含全域資料、沒有演練與切換手冊（`17`#5）。這幾項各自的機率都不高，但它們串在同一條災難復原路徑上，而且到真正需要還原時才會暴露。

全系統的基本前提是「個人或家庭使用、一年約 2,000 筆交易、最多 2 個 instance」。若要開放給更多使用者，標註「規模敏感」的項目要重新評估。

## 依嚴重度索引

- **高**：TD-01、TD-05
- **中**：TD-02、TD-03、TD-04、TD-06、TD-07、TD-08、TD-09、TD-11、TD-12、TD-13、TD-14、TD-15、TD-16、TD-17、TD-18、TD-19、TD-20、TD-21
- **低**：TD-10、TD-22～TD-40

## 清單

### 資料與部署安全網

| # | 項目 | 嚴重度 | 證據位置 | 影響 | 建議方向 |
|---|---|---|---|---|---|
| TD-01 | 維運手冊撤銷了 app 角色對 `AuditEntries` 的 DELETE，但刪除帳本要刪這張表（**正式環境已確認**） | 高 | `docs/deploy.md` 第 231 行（`REVOKE UPDATE, DELETE ON "AuditEntries"`）；`src/SixJars.Application/Books/DeleteBook.cs` 第 47 行（`ExecuteDeleteAsync`）；`tests/Shared/PostgresFixture.cs`（用容器預設帳號）；正式環境 `has_table_privilege` 查詢結果為否（使用者確認，2026-11-14）（`17`#1） | 正式環境的 app 角色沒有權限，所以 `DELETE /api/books/{id}` 執行到第 47 行時會得到 PostgreSQL 的 permission denied。【推論】這個例外不在 `ApiExceptionHandler` 的對應裡，因此回 500；明確的 transaction 會回滾，資料不受損，但功能完全不可用，前端只顯示「暫時無法連線」。500 這段還沒在正式環境實際重現。 | 二選一：調整授權，讓 app 角色可以刪除稽核記錄（並在 ADR 0006 或 0010 記錄這個例外）；或把刪除稽核改成由權限更高的路徑執行。無論選哪個，都同步更新手冊 §3.3。 |
| TD-02 | 資料庫角色與授權不在版本控制內，CI 也不以 app 角色執行 | 中 | `docs/deploy.md` §3.3（手動 SQL）；`ci.yml`；`PostgresFixture.cs`；`17`「對資料庫的唯讀查證」（`17`#2） | TD-01 是第一個實例：程式需要的權限隨程式碼改變，授權卻只存在手冊與記憶裡；`/health/ready` 也只檢查 migration、不檢查權限。唯讀查證又找到更多與手冊不符之處：資料庫中沒有 `sixjars_migrator` 角色，資料表全部屬於 `neondb_owner`；資料庫名稱是 `neondb`、版本是 PostgreSQL 18（手冊寫 `sixjars`、17，CI 用 17）。手冊的角色分離與 `ALTER DEFAULT PRIVILEGES` 在實際環境並不適用。 | 把授權寫成可重複執行的腳本，與 migration 一起版控；讓 Api 測試的 fixture 改以只有 DML 權限的角色連線，權限不足在 CI 就會失敗。 |
| TD-05 | 災難復原鏈可能在需要時失效 | 高 | `Backup/ExportBackup.cs`（6 條獨立查詢、無 transaction）；`Backup/RestoreBackup.cs`（以現行 Domain 重建、不一致即拒絕）；`BackupDocument.cs`（不含 `BookCreators`／`McpGrants`／`DataProtectionKeys`）；`docs/deploy.md` §11（`12`#1、`12`#2、`17`#5、`10`#1） | 備份是唯一由應用程式提供的復原手段，但可能有以下問題：(1) 匯出時剛好有人寫入，產生參照斷裂的備份；(2) 日後收緊 Domain 規則後，舊備份就還原不了；(3) 孤兒參照讓還原整份被拒。這些都要到還原那一刻才會發現。還原後的建立者、MCP 授權、登入狀態，以及「把正式服務切到新資料庫」的步驟都沒有寫；Neon 時間點還原的保留期限也沒有記錄。 | 匯出改在單一 repeatable-read（或 snapshot）transaction 中讀取。提供「驗證備份可還原」的 dry-run，讓下載後就能檢查。保存幾份舊格式的備份樣本做回歸測試。補完手冊的災難復原步驟，並實際演練一次；記錄 Neon PITR 的保留期限作為第二道防線。 |
| TD-08 | 兩個 secret 以 `:latest` 參照，值的變更不經過部署關卡 | 中 | `.github/workflows/deploy.yml` 第 127 行；`docs/deploy.md` §6、§7.3（`17`#3） | 【推論】新增 secret version 後，下一次冷啟動就會直接使用新值。若新值是錯的，所有 revision 都會卡在 startup probe，沒有舊版可以退回（`v1.0.1` 事故的另一種觸發方式）。 | 部署時改為明確指定 secret 的版本號，讓密碼輪替也經過「部署 → probe → 切流量」的流程；或在手冊寫明輪替的標準作業並先驗證新值。 |
| TD-09 | schema 向前相容只靠紀律；migration bundle 由本機 working tree 產生 | 中 | `Endpoints/HealthEndpoints.cs` → `Ready`（只檢查 pending）；ADR 0012「Consequences」；`docs/deploy.md` §4、§7.5（`17`#4） | migrate 之後到部署之前，以及回滾到舊 revision 時，舊程式都跑在新 schema 上，沒有任何工具驗證相容性。本機的 bundle 也不保證與被 tag 的 commit 一致。**已出現實例**：正式資料庫的 `__EFMigrationsHistory` 有一筆 `20261101161200_McpBookkeeping`，只存在於未合併的分支 `feat/mcp-write`；線上的 `v1.1.4` 不認得它，`/health/ready` 照樣回 200（`17`「對資料庫的唯讀查證」）。 | 在 CI 加一個步驟：用新 schema 跑上一個 release tag 的測試。或者至少在 PR 範本列出「是否破壞向前相容」的檢查項。bundle 改由 CI 從 tag 產生 artifact，本機只負責執行。 |

### 資料正確性與一致性

| # | 項目 | 嚴重度 | 證據位置 | 影響 | 建議方向 |
|---|---|---|---|---|---|
| TD-04 | 交易對帳本設定（帳戶、分類、財務規劃帳戶、標籤）與 `Books` 都沒有外鍵，而且「檢查 → 寫入」之間沒有鎖 | 中 | `SixJarsDbContextModelSnapshot.cs`（唯一的非 owned FK 是 `PlannedExpenses.SourceId`）；`Books/RemoveSetting.cs`、`Transactions/CreateTransaction.cs`、`Books/DeleteBook.cs`、`Planning/BudgetCommands.cs`（`10`#1、`10`#2、`11`#5、`12`#5） | 刪除設定或帳本的同時有人寫入，就可能留下孤兒參照。之後報表會靜默漏算、月報 422、匯出整份失敗、還原整份拒絕（TD-05）。機率低，但事後很難修復。 | 先確認不建 FK 是受 EF owned type 限制還是刻意（`99` Q-D1）。可行方向：設定與帳本的刪除改用「鎖住 `Books` 列」（例如 `SELECT … FOR UPDATE` 或 advisory lock）與交易寫入互斥；或讓交易寫入時也 touch `Books` 的 `xmin`。另外提供離線的完整性檢查工具。 |
| TD-06 | 餘額與月可用餘額的計算規則有三份實作 | 中 | `Domain/Ledger/*Calculator.cs`（`src/` 內無呼叫端）；`Infrastructure/Ledger/SqlLedgerSummaryQuery.cs` → `Impact`；`Application/Ledger/LedgerBalances.cs`；`Transaction.FundDelta`（`10`#7） | 新增 `TransactionKind` 或調整口徑時要改三處。`switch` 的 `_ => 0m` 不會讓編譯器提醒，一致性只靠 oracle 測試。 | 明確標示 Domain calculator 的角色（測試用的 oracle，或之後要移除）；把交易類型對各口徑的符號集中成一張表，讓 SQL 與 C# 從同一個來源產生或比對；`switch` 改成窮舉、遇到未處理的類型就擲例外。 |
| TD-07 | 帳本快取回傳可變的 `Book`，跨 request 共用，「不可修改」只靠註解約束 | 中 | `Books/IBookReader.cs` 註解；`Persistence/CachedBookReader.cs` → `FindAsync`；`Book.cs` 的公開 mutator（`10`#6） | 任何 handler 誤改唯讀帳本，都會污染所有 request 看到的資料，而且版本不變所以不會自動修復，最長持續 1 小時。 | 提供唯讀的介面或投影給讀取路徑使用；或加一個 analyzer、架構測試，禁止在 `GetReadOnlyBookAsync` 的結果上呼叫 mutator。 |
| TD-11 | 修改付款交易時不會同步預定支出 | 中 | `Transactions/UpdateTransaction.cs`（不碰 `PlannedExpenses`）對照 `DeleteTransaction.cs`（`11`#1） | 付款交易被改成其他類型、分類或月份後，預定支出仍算「已付」，月可用餘額與「以實際金額為準」的語意就對不上。 | 先確認業務規則（`99` Q-B1）。方向有二：限制可修改的欄位；或修改時偵測連結，必要時自動解除付款。 |
| TD-13 | 手動新增的預定支出不檢查預估金額的正負號 | 中 | `Planning/PlannedExpenseInput.cs` → `PlannedExpenseInputValidator`；對照 `RecurringPlannedExpenseInputValidator`（`11`#2） | 正數會**增加**月可用餘額，與週期項目的檢查不一致。 | 補上與週期項目相同的規則；若正數有業務意義（例如預期退款），就在 Domain 明確建模。 |
| TD-22 | 鎖帳日是「先檢查、再寫入」 | 低 | `Transactions/*` → `EnsureUnlocked`；`Books/SetLockDate.cs`（`10`#3） | 同時設定鎖帳日與新增鎖定區間內的交易，兩者都會成功。 | 與 TD-04 一起處理（交易寫入也讓 `Books` 的版本參與檢查）。 |
| TD-23 | 帳本設定的寫入沒有前端版本；`SetLockDate` 的註解與現況不符 | 低 | `Books/AddAccount.cs` 等 request record、`BookDto.cs`（不輸出版本）；`SetLockDate.cs` 類別註解對照 `BookConfiguration.cs`（`10`#4、`10`#5） | 兩位擁有者先後修改同一項目時，後者會靜默覆寫；註解誤導讀者。 | `BookDto` 輸出帳本版本，設定類 request 帶回版本；更新註解。 |
| TD-24 | 金額沒有小數位數驗證；前端各表單的限制不一致 | 低 | `TransactionInputValidator.cs`、`AddAccount.cs`（無規則）；`HavePrecision(18, 4)`；前端 `transaction-form.ts`（兩位）對照各對話框的 `type="number"`（`10`#10、`16`#5） | 【推論】超過 4 位小數時，資料庫會四捨五入，回應與稽核快照就和實際存入的值不同。 | 後端統一以 validator 限制位數（建議 2 位，與前端記帳表單一致）；前端金額輸入共用同一個元件。 |
| TD-25 | 摘要與報表的多條查詢不在同一個 snapshot | 低 | `Ledger/GetLedgerSummary.cs`、`Reports/GetMonthlyReport.cs`（`10`#12、`12`#7） | 讀取期間有寫入時，畫面上的數字可能來自不同時點；重新整理就會一致。 | 維持現狀即可；規模變大時再改成 repeatable-read 唯讀 transaction。 |
| TD-26 | 已封存的帳戶與分類在伺服器端仍可使用 | 低 | `RecurringPlanner.UnusableReason` 對照 `PlannedExpense.Validate`、`TransactionFactory.RequireAccount`；前端 `selectable`（`11`#6、`16`「記帳表單的規則表」） | 只有標籤在後端擋封存，其餘靠前端不顯示選項；CLI 或直接呼叫 API 時不受限制。 | 確認業務規則（`99` Q-B3）後，在 Domain 統一「新建時不得使用封存項目，既有的可保留」的規則。 |

### 安全與存取控制

| # | 項目 | 嚴重度 | 證據位置 | 影響 | 建議方向 |
|---|---|---|---|---|---|
| TD-03 | DataProtection 金鑰以明文存在資料庫；cookie、OAuth code、MCP access token 共用這個信任根；`McpBearerHandler` 沒有比對 token 的 `sub` 與 grant 的 `Subject` | 中 | `Api/Infrastructure/AuthenticationSetup.cs`（沒有 `ProtectKeysWith*`，階段 3 grep 確認）；`Mcp/OAuth/McpTokens.cs`；`McpBearerHandler.cs` 第 39–45 行（`13`#1、`14`#2） | 【推論】能讀 `DataProtectionKeys` 表的人（任何持有 app 角色連線、或拿到資料庫備份的人）可以偽造任何使用者的 cookie 與 MCP token，等於取得所有帳本。 | 以 KMS 或 Secret Manager 的金鑰保護 key ring；`McpBearerHandler` 補上 `sub` 一致性檢查作為 defense-in-depth；在 ADR 0005 補記這個取捨。 |
| TD-15 | MCP client 的信任單位是整個 host，不是特定的 `client_id` URL | 中 | `Mcp/OAuth/AuthorizeRequestValidator.cs` 第 43–56 行；`McpOAuthOptions.AllowedClientIdHosts`（`14`#1） | 安全性取決於白名單 host 上有沒有使用者可控、能回傳任意 JSON 的路徑，這一點從程式碼無法判斷；每多加一個 host，範圍就跟著擴大。 | 白名單改成完整的 `client_id` URL（或 host＋路徑前綴）；同意頁顯示完整的 `client_id`。 |
| TD-16 | 沒有移除成員的入口 | 中 | `SixJars.Cli/CliApp.cs`（只有 `add-member`）；`BookMember`（沒有移除方法）（`13`#2） | 加錯人或要收回權限時，只能直接改資料庫（不會留稽核）或刪掉整本帳。 | 新增 CLI `remove-member`（留稽核、禁止移除最後一位擁有者）；之後再評估是否開放 Web 介面。 |
| TD-17 | MCP 授權無法自助檢視或撤銷；refresh token 沒有絕對效期 | 中 | `Program.cs`／`OAuthEndpoints.cs`（沒有相關端點）；`Members/RevokeMcpAccess.cs`（只能整批撤銷）；`McpGrant.Refresh`（`14`#3、`14`#6） | 在 Claude 那邊移除連接器不會通知本服務。只要 30 天內用過一次，授權就會無限延長，而且無人察覺。 | 提供「我的 MCP 授權」清單與單筆撤銷（Web 或 RFC 7009 端點）；設定絕對效期（例如 90 天），到期後重新同意。 |
| TD-27 | 沒有安全標頭與 rate limiting；匿名端點會觸發資料庫或外部請求 | 低 | grep `src/` 找不到 `UseHsts`、`AddRateLimiter`、CSP；`HealthEndpoints.cs`；`OAuthEndpoints.AuthorizeAsync`、`AuthorizeRequestValidator.cs` 第 52 行（`13`#8、`14`#4） | SPA 可以被嵌入 iframe。`/health` 可被用來持續喚醒 Neon；`/oauth/authorize` 可被用來讓服務反覆對外連線（不構成 SSRF）。 | 加一組基本的安全標頭（`frame-ancestors`、`nosniff`、HSTS）；匿名端點加 rate limiter；CIMD 加短期快取；`/health` 改成不連資料庫的 liveness，只有 `/ready` 連資料庫。 |
| TD-28 | MCP 的細項 | 低 | `McpGrant.Refresh`（輪替沒有寬限期）；grep `McpGrants`（沒有清理）；`TokenEndpoint.cs` 第 71–75 行（所有 `DbUpdateException` 都當成重複兌換）；`LedgerTools.cs`（只攔 `GetBook` 的例外）；`ClientMetadataFetcher.HttpClientName`（未使用）（`14`#5、#7、#8、#9、#13） | 網路中斷後重試會撤銷整筆授權；資料表只增不減；寫入失敗被誤報為 `invalid_grant`；未使用的常數造成誤導。 | 輪替加短暫寬限期（例如 30 秒內允許上一個 token）；定期清理已撤銷且過期的列；只把唯一索引違反判定為重複兌換；移除未使用的常數。 |
| TD-29 | 授權模型的擴充性 | 低 | `BookScopeConventionTests.cs` 第 27 行（只認屬性名稱 `BookId`）；`BookRole.cs`、`BookMembership.OwnedBookIds`（`13`#6、`13`#7） | 以其他名稱帶帳本 Id 的 request 不會被掃到；日後開放 `Bookkeeper`／`ReadOnly` 時，授權、帳本清單、額度三處要拆開。 | 慣例測試改成以型別或 attribute 判斷；開放角色前，先把 `OwnedBookIds` 拆成「可讀」「可寫」「擁有者額度」三個查詢。 |
| TD-30 | 部署的信任邊界 | 低 | `deploy.yml` 第 20–32 行（`verify-tag` 在 tag 指向的 commit 裡）；`scripts/setup-gcp.ps1` 第 148–149 行（專案層級 `run.admin`）、第 193 行（沒有 required reviewers）（`17`#7、`17`#8） | 【推論】有 push 權限的人可以對不在 master 的 commit 打 tag 並繞過檢查；deployer 的權限範圍是整個專案。單人 repo 下影響有限。 | 設定 tag ruleset 限制誰能 push `v*`；`production` environment 加 required reviewers；`run.admin` 改在服務層級授予。 |

### 可觀測性與維運

| # | 項目 | 嚴重度 | 證據位置 | 影響 | 建議方向 |
|---|---|---|---|---|---|
| TD-12 | 幾乎沒有應用程式 log；安全事件不留稽核；沒有監控與告警 | 中 | grep `ILogger`：`src/` 只有 4 個檔案；`GoogleSignInValidator.cs`（拒絕時不記錄）；`AddBookCreator.cs` 等（明寫不留稽核）；`17`「職責」（沒有監控）（`13`#4、`14`#12、`12`#6、`17`#11） | 登入失敗、權限拒絕、MCP 讀取、備份下載、500 錯誤都無從追查；服務出問題只能靠使用者自己發現。TD-01 這類錯誤也會因此長期不被察覺。 | 至少在登入結果、授權拒絕、未處理例外、MCP tool 呼叫加上結構化 log（不含個資與金額）；在 Cloud Run 設定 5xx 比例的 log-based alert（不需要外部 ping，不違反 ADR 0007）。 |
| TD-14 | 備份與匯出整份在記憶體中組成，並隨帳本無限成長（規模敏感） | 中 | `Backup/ExportBackup.cs`（全部交易、預定支出、稽核一次載入並追蹤）；`Endpoints/ExportsEndpoints.cs`（`SerializeToUtf8Bytes`）；`ITransactionSheetWriter.Write`（`byte[]`）；Cloud Run 512Mi（`12`#3、`12`#4） | 稽核記錄每筆都帶完整快照，成長最快。帳本沒有年度切分，資料只增不減，最終可能 OOM；與一般請求共用 instance，也會影響其他使用者。 | 備份改用串流序列化（`Utf8JsonWriter` 直接寫 response）並以 `AsNoTracking` 讀取；先量測目前備份的大小（`99` Q-P5），再決定急迫性。 |
| TD-10 | `DbUpdateException` 一律回 500，前端顯示成「暫時無法連線」 | 低 | `Api/Infrastructure/ApiExceptionHandler.cs`（只對應 `DbUpdateConcurrencyException`）；`web/src/app/core/errors/api-error.ts` → `classifyError`（`11`#3、`13`#3、`16`#3、`17`#1） | 約束違反與權限不足都會引導使用者「稍後重試」，而重試結果相同。 | 唯一索引違反對應成 409，FK 違反對應成 422，其餘維持 500 並記 log；前端把 5xx 與網路中斷分成兩種訊息。 |
| TD-31 | Cloud Run 設定沒有完整宣告；部署後只驗證健康檢查 | 低 | `deploy.yml` 第 118–145 行（`17`#9、`17`#10） | 【推論】CPU、concurrency、timeout 沿用 console 上的現值，不在 git 歷史裡；前端 build 輸出或 Google 設定壞掉時，部署仍會顯示成功。 | 在 `deploy.yml` 明確帶上所有服務參數；smoke test 加上 `/`（應回 SPA）、`/auth/login`（應 302）、`/mcp`（應 401）。 |
| TD-32 | 建置輸入不固定版本；沒有弱點掃描 | 低 | `Dockerfile` 第 7、16、32 行（浮動 tag）；workflow 的 actions 以 major tag 參照；沒有 `dependabot.yml`（`17`#6、`17`#12） | 同一個 tag 重新部署不保證得到同一個 image；持有 `id-token: write` 的 job 使用浮動參照的 action；套件弱點全靠人工留意。 | base image 以 digest 鎖定，並由 Dependabot 定期提 PR 更新；actions 以 commit SHA 參照；CI 加 `dotnet list package --vulnerable` 與 `npm audit`。 |
| TD-33 | `docs/deploy.md` 與程式碼不同步 | 低 | §4（migration 清單只寫到 `AddDataProtectionKeys`）；§3.3（資料表列表）；§1（`dotnet-ef` 版本）；§2.3（個資檢查規則）（`17`#13） | 手冊是新環境建置與災難復原的唯一依據，TD-01 就是同一類漂移造成的。 | 把手冊中會隨程式改變的清單改成指令（例如「執行 X 查看」）而不是列舉；發版檢查表加上「手冊是否需要更新」。 |
| TD-34 | Acceptance 測試不在任何自動化關卡內；個資檢查以檔名判斷 | 低 | `ci.yml` 第 23 行；`deploy.yml` 第 99–104 行（`17`#14、`17`#15） | CI 綠燈不代表「舊帳本匯入結果與 Excel 一致」。個資檢查擋不住改過副檔名的檔案。 | 在 CI 輸出明確顯示 skip 的數量；發版檢查表加上「本機跑過 Acceptance」。個資檢查維持現狀即可（真正的防線是 `.dockerignore`）。 |

### 舊帳本匯入與 CLI

| # | 項目 | 嚴重度 | 證據位置 | 影響 | 建議方向 |
|---|---|---|---|---|---|
| TD-18 | Excel 讀取位址寫死，超出範圍或版面不同時靜默忽略；匯入時不以 Excel 自己算出的數字自我驗證 | 中 | `Infrastructure/LegacyExcel/ExcelLegacyWorkbookReader.cs`（流水帳固定讀第 7–172 列）；`SheetGrid.Number`（非數字回 0）；`LegacyWorkbook.cs`（`LegacyMonthFigures` 只供驗收測試使用）（`15`#1、`15`#2） | 補匯的對象正是 2026 年以前的檔案。若版面不同，資料會被漏讀或讀錯而沒有任何警告；新帳本匯入也沒有其他把關。 | `--dry-run` 時把轉換後算出的各帳戶期末，與 `LegacyMonthFigures` 比對並列出差異；讀取範圍之外若還有非空白列就發出警告。 |
| TD-19 | `--adjust` 會把「名稱對不上」變成合法的餘額調整 | 中 | `LegacyImport/MappingSession.cs` → `AddAccount`；`LegacyJunction.Check`（`15`#3） | 【推論】帳戶在系統裡改過名之後補匯，歷史會斷成兩個帳戶，而程式不會提示。 | dry run 時偵測「舊年度新增的項目期末，恰好等於某個只在帳本出現的項目期初」的配對，提示可能是改名；`--adjust` 要求先看過 dry run 的報告。 |
| TD-35 | CLI 的細項 | 低 | `ExcelLegacyWorkbookReader.ReadAsync`（缺工作表時擲原始例外）；`MappingSession.Journal.cs` 第 25 行（只攔兩種例外）；`PrependLegacyYear.cs` 第 64 行（靠丟棄 DbContext 來「不寫入」）；`ImportLegacyBook.cs`（同名檢查沒有鎖）；`CliApp.cs`（執行前不顯示目標環境、找不到檔案時兩個指令印法不一致）（`15`#4～#9） | 錯誤訊息不友善；隱含前提一旦改變就可能寫入半套資料；可能誤連到錯誤的資料庫。 | 缺少工作表時改成明確的錯誤訊息；在 handler 註明「必須在獨立 scope 執行」或改成不修改追蹤中的實體；有副作用的指令執行前顯示資料庫 host（不含帳密）並要求 `--yes`。 |

### 前端

| # | 項目 | 嚴重度 | 證據位置 | 影響 | 建議方向 |
|---|---|---|---|---|---|
| TD-20 | 換月份後，載入中或載入失敗時，新月份的標題下仍顯示上個月的資料 | 中 | `features/transactions/transactions.page.ts`、`planned-expenses.page.ts`、`budgets.page.ts`、`summary.page.ts`（`catchError(() => EMPTY)`）；對照已修正的 `monthly-report.page.ts`（`16`#1） | 財務數字和標題不一致，snackbar 4 秒後就消失。報表兩頁已經把這個問題當成缺陷修掉。 | 四頁比照報表頁：回應標上月份，月份不符時不顯示；抽出共用的「依月份載入」helper（也能順便解決 TD-37 的重複程式碼）。 |
| TD-21 | 前後端的 DTO 與規則常數靠手寫同步 | 中 | `web/src/app/core/api/dto.ts`（「手寫對照後端 record」）；`app.html`（5 本上限）、`shared/tags.ts`、`dates.ts`、`transaction-rules.ts` → `KIND_RULES`（`16`#2、`16`#13） | 後端改了而前端沒跟上時，TypeScript 編譯不會報錯；舊分頁搭配新 API 時更容易出錯（TD-36）。 | 由後端產生 OpenAPI，再產生前端型別；規則常數由 `/api/me` 或專用的 config 端點提供，或加契約測試比對兩邊的值。 |
| TD-36 | 沒有 service worker 版本更新流程；lazy chunk 載入失敗沒有處理 | 低 | 搜尋無 `SwUpdate`；`ngsw-config.json`；`core/book/book-creation.ts`（只有這裡處理 chunk 失敗）（`16`#7、`17`「對其他模組待確認問題的回應」） | 【推論】長時間開著的分頁會用舊版前端呼叫新版 API；部署後舊的雜湊檔名不存在，路由切換會失敗。 | 使用 `SwUpdate.versionUpdates` 提示重新載入；路由層攔截 chunk 載入錯誤並自動重新整理。 |
| TD-37 | 前端的其他細項 | 低 | `current-book.ts`（其他裝置的變更不會反映）；`auth.guard.ts`（`/api/me` 非 401 失敗時沒有畫面）；`error-interceptor.ts`（401 會遺失輸入）；`shared/dates.ts`（「今天」取瀏覽器時區）；六個 page 各有重複的 signal 與 `run`；`ngsw-config.json` 的導覽排除少了 `mcp`、`.well-known`、`/health/ready`；`PlannedExpenseApi.pay` 回傳 `unknown`（`16`#4、#6、#8～#11、#13） | 個別影響都小；樣板重複就是 TD-20 只修了兩頁的原因。 | 抽出共用的 page 基底（以 composition 而非繼承）；`me` 載入失敗時顯示重試畫面；「今天」改由後端提供或固定使用台北時區；導覽排除改成與 `SpaHosting.BackendPrefixes` 相同。 |

### 稽核與其他

| # | 項目 | 嚴重度 | 證據位置 | 影響 | 建議方向 |
|---|---|---|---|---|---|
| TD-38 | 稽核記錄難以使用 | 低 | `Auditing/GetAuditHistory.cs`（只能依單一實體查、不分頁）；`AuditSnapshots.UnknownVersion`；前端沒有 `/audit` 畫面；`DeleteBook.cs` 第 46–50 行（`13`#9、`13`#12、`16`#12） | 無法回答「某天誰動過這本帳」；`ActorSubject` 只有 `sub`；刪除帳本後連摘要都無法從 API 讀取。 | 先決定稽核的使用情境（`99` Q-B5），再決定要不要做整本帳的時間軸與畫面。 |
| TD-39 | 查詢效率的細項（規模敏感） | 低 | `Transactions/ListTransactions.cs`、`Planning/ListPlannedExpenses.cs`（不分頁，且為了讀 `xmin` 用追蹤查詢）；`TransactionConfiguration.cs`（缺少以 `Date` 為主的索引）；`BookAccessBehavior`＋`CachedBookReader`（每個 request 多 1～2 次往返）；`GeneratePlannedExpenses.cs`（`10`#8、#9、#11、`11`#8、#9、`14`#10） | 目前資料量下不是問題；前端一律帶月份查詢。 | 版本改用 `AsNoTracking` 投影讀出 `xmin`，就不必追蹤；交易量明顯成長後再加 `(BookId, Date)` 索引與分頁。 |
| TD-40 | 認證的細項 | 低 | `AuthEndpoints.cs` 對照 `AuthenticationSetup.cs`（Development 沒設定 Google 時 `/auth/login` 回 500）；`OnRemoteFailure`（資料庫錯誤也顯示成被拒絕）；`BookMember.BindSubject`（綁定後不再更新 email）（`13`#10、#13、#14） | 【推論】前兩項依賴框架行為，未實際驗證；第三項影響 CLI 以 email 指定擁有者的操作。 | 先以測試確認前兩項；`/auth/login` 在沒有 Google scheme 時回明確的錯誤；登入時記錄 email 的變更。 |

## 刻意的取捨（不列為債）

以下項目在模組文件中列為風險，但程式碼註解或 ADR 已明確記錄取捨理由，而且翻轉條件清楚。列在這裡，避免接手的人誤當成缺陷去修。

| 項目 | 依據 | 什麼情況下要重新考慮 |
|---|---|---|
| Cookie 不重新驗證白名單（`13`#5） | ADR 0011 | 白名單異動需要立即生效時 |
| 信任任何來源的 forwarded headers（`13`#11） | `ForwardedHeadersSetup.cs` remarks | 改成 container 可被直接連到的部署方式時 |
| Issuer 取自請求的 Host（`14`#11） | `McpOAuth.Issuer` | 服務同時有多個網域時 |
| 預算不做樂觀並行（`11`#4） | `BudgetCommands.cs` 檔頭註解 | 出現實際的遺失更新回報時 |
| 閒置時不 ping，接受冷啟動延遲 | ADR 0007 | 改用付費方案，或 MCP client 頻繁逾時時 |
| CSV 公式防護會在內容前加 `'`（`12`#9） | `CsvTransactionWriter` remarks | 有程式化讀取 CSV 的需求時 |
| 匯入的交易不逐筆留稽核；補匯略過鎖帳日（`15`#10） | 程式碼註解引用 spec §7、R3-Q2 | — |
| 轉換規則綁定特定的中文名稱（`15`#11） | 只為單一來源而做的搬家工具 | 要支援其他版本的 Excel 時 |
| 依賴 Excel 公式的快取值（`15`#12） | ExcelDataReader 不計算公式 | 來源檔案可能由其他工具產生時 |
| 刪除帳本時一併清除稽核記錄（`13`#9 的一部分） | ADR 0010 | —（但會引發 TD-01 的權限問題） |
| 年報重複篩選年份（`12`#8） | 無害 | — |
