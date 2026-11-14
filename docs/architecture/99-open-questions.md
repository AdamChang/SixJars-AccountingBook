# 待確認問題

> 階段 3 整合，commit `f181aaf`，2026-11-14。來源是 `10`～`17` 各模組的「待確認問題」：已在其他模組找到答案的問題移到最後的「已解答」表，其餘去重後依「該問誰」分組。
> 編號規則：**P** 是正式環境的事實（查一下就知道），**B** 是業務規則（產品擁有者決定），**D** 是設計意圖（原開發者），**T** 要讀測試或實際執行才能確認。
> 「關聯」欄指向 `90-tech-debt.md` 的項目；答案可能改變那一項的嚴重度或建議方向。

## 優先處理

| # | 問題 | 為什麼優先 | 關聯 |
|---|---|---|---|
| Q-P13 | 正式資料庫已套用未合併分支 `feat/mcp-write` 的 migration `McpBookkeeping`：是刻意先套用（準備發版），還是測試時誤套用？它與線上的 `v1.1.4` 相容嗎？ | 線上程式正跑在它不認得的 schema 上，`/health/ready` 偵測不到 | TD-09 |
| Q-P2 | 最近一次下載備份是什麼時候？存放在哪裡？是否做過還原演練？Neon 方案的時間點還原保留幾天？ | 決定 TD-05 的實際暴露程度 | TD-05 |
| Q-D1 | `Transactions` 不對 `Books`／`Accounts`／`Categories`／`PlanningFunds` 建外鍵，是受 EF「不能指向 owned type」的限制，還是對 `BookId` 也是刻意不建？有沒有 ADR 記錄？ | 決定 TD-04 要用 DB 約束還是應用層的鎖來處理 | TD-04、TD-05 |

## P：正式環境的事實（問營運者）

| # | 問題 | 來源 | 關聯 |
|---|---|---|---|
| Q-P2 | 見「優先處理」 | `12` Q1、`17` Q5 | TD-05 |
| Q-P3 | 正式環境的 DataProtection 金鑰是否需要靜態加密？目前有哪些人或工具持有可以讀取 `DataProtectionKeys` 的資料庫連線（包括 Neon console、備份、MCP 的唯讀連線）？ | `13` Q1、`14` Q2 | TD-03 |
| Q-P4 | 目前輪替資料庫密碼的實際做法是什麼？secret 要不要改成固定版本號？ | `17` Q3 | TD-08 |
| Q-P5 | 正式環境一份備份的實際大小與稽核記錄筆數是多少？ | `12` Q3 | TD-14 |
| Q-P6 | GitHub 上的 `master` 有沒有 branch protection（必須經 PR、必須通過 CI）？有沒有 tag ruleset 限制誰能 push `v*`？ | `17` Q6 | TD-30 |
| Q-P7 | Cloud Run 服務目前的 CPU、concurrency、request timeout、execution environment 實際值是多少？是否曾在 console 手動調整過？ | `17` Q7 | TD-31 |
| Q-P8 | 至今是否發生過「migrate 之後舊版出錯」或「回滾後舊版與新 schema 不相容」？ | `17` Q4 | TD-09 |
| Q-P9 | 正式環境是否出現過「Claude 無預警要求重新連線」？ | `14` Q4 | TD-28 |
| Q-P10 | 是否遇過「部署後舊分頁呼叫新 API 出錯」？ | `16` Q5 | TD-36 |
| Q-P11 | `docs/deploy.md` §2.1 的 image 大小、§2.4「Cloud Run 是否自動壓縮靜態檔」尚未實測，是否已經量過？ | `17` Q9 | TD-33 |
| Q-P13 | 見「優先處理」 | `17` Q1a | TD-09 |
| Q-P14 | 實際環境沒有 `sixjars_migrator`，資料表屬於 `neondb_owner`：migration 實際上用哪個角色執行？手冊 §3.1、§3.3 要改成符合現況，還是把環境改成符合手冊？ | `17`「對資料庫的唯讀查證」 | TD-02、TD-33 |
| Q-P12 | 2026 年以前的舊帳本，版面（列數、欄位位置）是否與 2026 年版完全相同？實際補匯過哪些年度？有沒有月份的流水帳超過第 172 列？ | `15` Q1 | TD-18 |

## B：業務規則（問產品擁有者）

| # | 問題 | 來源 | 關聯 |
|---|---|---|---|
| Q-B1 | 預定支出付款之後，是否允許自由修改那筆付款交易（類型、分類、歸屬月份、金額）？若允許，預定支出應維持「已付」嗎？ | `11` Q1 | TD-11 |
| Q-B2 | 手動新增的預定支出允許正數或 0 的預估金額，是刻意的（例如預期退款），還是漏了與週期項目相同的檢查？ | `11` Q2 | TD-13 |
| Q-B3 | 已封存的帳戶與分類在伺服器端仍可用於新交易與新預定支出（只有前端擋），這符合預期嗎？ | `11` Q4 | TD-26 |
| Q-B4 | 移除成員是刻意不做（家庭帳本不需要），還是尚未排入？若要做，走 CLI 還是 Web？要不要留稽核？ | `13` Q2 | TD-16 |
| Q-B5 | 稽核記錄的使用情境是什麼？需要整本帳的時間軸或畫面嗎？下載備份或匯出要不要留稽核？登入成功與被拒絕要不要記錄？ | `12` Q4、`13` Q4、`16` Q6 | TD-12、TD-38 |
| Q-B6 | 預定支出、週期項目、付款、期初餘額是否允許超過兩位小數？若不允許，在前端擋還是後端擋？ | `16` Q3、`10` 風險 #10 | TD-24 |
| Q-B7 | 是否需要支援在台灣以外的時區使用？ | `16` Q4 | TD-37 |
| Q-B8 | 使用者是否需要在 Web 上看到並撤銷自己的 MCP 授權？refresh token 沒有絕對效期是刻意的嗎？ | `14` Q3、`14` Q5 | TD-17 |
| Q-B9 | 是否需要最低限度的告警（例如 5xx 比例的 log-based alert），還是維持「使用者自己發現」？ | `17` Q8 | TD-12 |
| Q-B10 | 補匯前若在系統裡改過帳戶或財務規劃帳戶名稱，預期怎麼處理？先改回原名，還是接受歷史分成兩個帳戶？ | `15` Q3 | TD-19 |

## D：設計意圖（問原開發者）

| # | 問題 | 來源 | 關聯 |
|---|---|---|---|
| Q-D1 | 見「優先處理」 | `10` Q1 | TD-04 |
| Q-D2 | `SetLockDate` 同時設定時的預期行為，是「後者得到 409」還是「以最後一次為準」？類別註解與 `BookConfiguration.cs` 的註解互相矛盾 | `10` Q2 | TD-23 |
| Q-D3 | 是否有測試逐一驗證「A 帳本的成員以 A 的 `bookId` 搭配 B 帳本的實體 Id」會得到 404？ | `13` Q5 | TD-29 |
| Q-D4 | Domain 的三個 calculator 與 `LedgerSnapshotLoader` 是否確定只給測試使用（oracle、驗收）？ | `10` Q4 | TD-06 |
| Q-D5 | 是否要把 MCP 白名單從 host 收緊成完整的 `client_id` URL？實際觀察到的 Claude CIMD URL 是什麼（`docs/superpowers/plans/2026-10-30-mcp-server.md`「手動驗證」一節，未讀）？ | `14` Q1 | TD-15 |
| Q-D6 | `DbUpdateException`（唯一索引、FK 違反）要統一對應成 409 或 422 嗎？ | `13` Q3 | TD-10 |
| Q-D7 | Domain 規則變更時，舊備份的相容性由誰把關？是否保存舊版備份樣本做回歸測試？ | `12` Q2 | TD-05 |
| Q-D8 | 是否要把資料庫角色授權納入可重複執行的腳本或測試（例如 Api 測試改用只有 DML 權限的角色）？ | `17` Q2 | TD-02 |
| Q-D9 | 記帳、總覽、預定支出、預算四頁在換月後載入失敗時保留舊資料，是刻意的，還是尚未比照報表頁修正？ | `16` Q1 | TD-20 |
| Q-D10 | 是否考慮由後端產生 OpenAPI 再產生前端型別，或加契約測試？ | `16` Q2 | TD-21 |
| Q-D11 | 是否考慮在 `--dry-run` 時把轉換結果與 Excel 自己算出的數字（`LegacyMonthFigures`）比對並印出差異？ | `15` Q2 | TD-18 |
| Q-D12 | 程式碼註解大量引用 spec 的決策代號（R2-Q4、R3-Q1、M3-1、D4…），對應文件在 `docs/superpowers/`。要不要把其中的架構級決策整理成 ADR？ | `15` Q6 | — |
| Q-D13 | `docs/adr/` 沒有編號 0014 的 ADR（0013 之後直接是 0015），是被撤回、合併，還是遺漏？ | 階段 3 發現（`00-inventory.md` 的 ADR 清單） | — |

## T：要讀測試或實際執行才能確認

以下是模組文件中依賴【推論】的結論。若要據此修改程式碼，建議先補讀測試或寫一個驗證用的測試。

| # | 要確認的推論 | 建議做法 | 來源 | 關聯 |
|---|---|---|---|---|
| Q-T1 | 修改 `IBookReader` 回傳的 `Book` 時，是否有測試或其他機制擋下 | 讀 `tests/SixJars.Infrastructure.Tests` 的快取相關測試 | `10` Q3 | TD-07 |
| Q-T2 | 未設定 Google 時 `/auth/login` 回 500；登入 callback 的資料庫錯誤會導向 `/denied` | 各寫一個 Api 測試 | `13` Q7 | TD-40 |
| Q-T3 | 超過 4 位小數的金額在 PostgreSQL 被四捨五入，造成回應、稽核快照與資料庫不一致 | Infrastructure 測試 | `10` 風險 #10 | TD-24 |
| Q-T4 | SDK 對 MCP tool 內未處理例外的實際回應內容（是否帶出例外訊息） | 讀 `LedgerToolsTests` 或 SDK 原始碼 | `14` Q6 | TD-28 |
| Q-T5 | `tests/SixJars.Api.Tests/Mcp/` 的 8 個測試檔涵蓋了哪些情境（重放、並行輪替、redirect 比對、端到端） | 補讀 | `14` Q7 | TD-15、TD-17、TD-28 |
| Q-T6 | 匯入與 CLI 測試（`CliAppTests`、`PrependLegacyYearTests`、`LegacyJunctionTests`、各 `LegacyWorkbookMapper*Tests`）涵蓋的範圍，特別是 TD-19 的改名情境與逐列 catch 漏掉的例外 | 補讀 | `15` Q5 | TD-19、TD-35 |
| Q-T7 | `web/e2e/` 的 11 個 spec 能涵蓋多少 DTO 欄位；未讀的樣板、對話框與 `*-rules.ts` | 補讀 | `16` Q2、Q7 | TD-21 |
| Q-T8 | 平台行為：`:latest` 的解析時機、`gcloud run deploy` 對沒帶的旗標沿用現值、對不在 master 的 commit 打 tag 能繞過 `verify-tag` | 對照 GCP／GitHub 官方文件 | `17` Q10 | TD-08、TD-30、TD-31 |
| Q-T9 | `AuditTrailTests` 是否就是 ADR 0006 指定的「每個寫入 endpoint 恰好留下一筆記錄」測試 | 補讀 | `13`「稽核」 | — |

## 已解答（模組之間互相回答）

| 原問題 | 答案 | 答案出處 |
|---|---|---|
| Q-P1（`17` Q1）：正式環境的 `sixjars_app` 對 `AuditEntries` 有沒有 DELETE 權限？ | **沒有**。TD-01 成立，正式環境的「刪除帳本」目前不可用。還沒確認的部分：實際回應是否為 500、正式環境是否有人試過刪除 | 使用者在正式環境查詢 `has_table_privilege`（2026-11-14） |
| `10` Q5：前端呼叫 `GET /transactions` 時是否都帶月份？ | 是，`TransactionApi.list` 的 `budgetMonth` 是必要參數 | `16`「呼叫的後端端點」 |
| `11` Q3：`DbUpdateException` 回什麼？ | 500（`ApiExceptionHandler` 沒有對應）；後續的處理方式轉為 Q-D6 | `13`「例外對應」 |
| `11` Q5：付款規則與匯入的 `MappingSession.Templates` 是否一致？ | 一致（貸款 → `LoanPayment`，否則 `Expense`）；只在補匯時主分類性質不同的情況下會分歧 | `15`「轉換規則」 |
| `12` Q5：災難復原的完整步驟有沒有記錄？ | 只記錄到 `restore-backup`；後續步驟沒有寫（併入 TD-05） | `17` 風險 #5 |
| `12` Q6：CLI 如何呈現 `RestoreBackup` 的錯誤？ | `Host.RunAsync` 集中處理，印訊息後 exit 1 | `13`「例外對應」、`15` |
| `13` Q6：HTTPS／HSTS 由誰負責？`/health` 有沒有外部監控？ | 沒有 load balancer 與自訂網域；repo 內沒有 HSTS 設定，【推論】TLS 由 Cloud Run 終結；`/health` 沒有排程監控 | `17`「對其他模組待確認問題的回應」 |
| `13` Q8：`McpTokens` 是否依賴 DataProtection？同意頁是否防 clickjacking？ | 是（併入 TD-03）；同意頁有 CSP `frame-ancestors 'none'` | `14`「Token 與憑證」「同意頁」 |
| `14` 風險 #1：正式環境的 `AllowedClientIdHosts` 是否加了其他 host？ | 以目前的 `deploy.yml` 而言沒有，生效的是預設值 `claude.ai` | `17`「設定項的來源與去向」 |
| `15` Q4：CLI 連正式資料庫用哪個角色？ | app 角色 `sixjars_app`（DML） | `17`「對其他模組待確認問題的回應」 |
