# 刪除帳本（後端）設計

- 需求與 grilling 結論：GitHub issue [AdamChang/SixJars-AccountingBook#6](https://github.com/AdamChang/SixJars-AccountingBook/issues/6)（2026-10-19）
- 前端設計：[`2026-10-19-delete-book-frontend-design.md`](2026-10-19-delete-book-frontend-design.md)
- 相關 ADR：[`0005`](../../adr/0005-bff-cookie-auth-with-book-member-whitelist.md)（成員白名單、非成員 404）、[`0006`](../../adr/0006-explicit-snapshot-audit-and-soft-delete.md)（稽核快照、軟刪除）；本設計新增 ADR 0010

## 目標

Owner 能從 Web 刪除整本帳本，帳本與底下所有資料都從資料庫移除，只留一筆「誰、何時、刪了哪本、刪了多少」的稽核記錄。典型情境：匯入的資料有錯，刪掉後用 CLI `import-legacy` 以同一個名稱重新匯入。這條路目前被同名檢查擋住。

## 範圍

**包含**

- `GET /api/books/{bookId}/deletion-preview`：列出刪除前的統計。
- `DELETE /api/books/{bookId}`：body `{ confirmName }`，回 204。
- ADR 0010、`CONTEXT.md` 新增「刪除帳本」用語。

**排除**

- CLI 的刪除指令（Q1）。
- 伺服器端自動備份（Q5）。由前端提示使用者先下載既有的 JSON 備份。
- 查詢稽核記錄的 UI／API（Q8，之後再做）。
- 帳本建立者的概念（Q6）。
- 為 `Book` 加 concurrency token（Q7）。

## 決策

Q1–Q17 是 grilling 的結論，詳見 issue #6。這裡只摘錄後端相關的部分。D1–D7 是展開設計時新增的。

| # | 決定 | 理由 | 被否決的選項 |
|---|---|---|---|
| Q2 | 硬刪除，是 ADR 0006 的例外 | 主要情境是「清掉後重匯」。軟刪除會一直佔著名稱，擋住重匯；Web 也沒有救回的入口，只留下成本 | 軟刪除；軟刪除加 purge |
| Q6 | 任一位 Owner 都能刪 | Owner 權限對等，刪除只是最重的一種寫入；要區分建立者得引入新的 domain 概念 | 只有建立者；只剩一位成員時才能刪 |
| Q7 | body 必須帶 `confirmName`，與帳本名稱不一致回 422 | 防護放在伺服器端，前端 bug 或手動呼叫 API 都繞不過 | 只靠 UI 確認；`xmin` 並行控制（它防的是並行修改，不是刪錯帳本） |
| Q8 | 舊的稽核記錄全部刪除，另寫一筆 `Delete` 摘要 | 舊快照包含完整交易內容，留著就等於沒刪；至少要追得到誰刪的 | 全刪；舊記錄全留 |
| Q9 | 另開 `deletion-preview` endpoint | 用途單一，不必在每次 `GET /books/{id}` 都多跑 count | 併入 `BookDto`；前端自己算 |
| Q10 | 鎖帳日不擋刪除，preview 顯示鎖帳日 | 鎖帳日保護的是個別分錄；就算擋，使用者清掉鎖帳日就能刪，只是多一步 | 有鎖帳日就擋 |
| Q14 | 在 handler 內以 `ExecuteDeleteAsync`＋`IgnoreQueryFilters()` 逐表刪除，全部包在單一 DB transaction 裡 | 刪除範圍明確寫在程式裡，測試能直接表達；已軟刪除的資料不會被 query filter 漏掉 | 補 FK `ON DELETE CASCADE`。往後任何誤刪 `Book` 都會清空整本帳本，而且是 DB 的隱性行為 |
| Q15 | `AuditAction.Delete`、`EntityType = Book`，`Before` 只存摘要 | 動作就是刪除 Book 實體；摘要不含交易內容，理由同 Q8 | 新增 `AuditAction.DeleteBook` |
| D1 | `confirmName` 放在 DELETE 的 JSON body，endpoint 參數加明確的 `[FromBody]` | 帳本名稱是中文。放在 query string 要做 URL 編碼，還會進到 Cloud Run 的 request log。Minimal API 對 DELETE 不推斷 body，所以要明確標註 | query string（`DeleteTransaction` 的 `version` 用的是這種）；改用 `POST /books/{id}/delete`（不符合 REST 慣例） |
| D2 | 名稱以 ordinal 完全比對，不 trim；不一致時擲 `DomainException`，code 為新增的 `name-mismatch`（422）；空字串是 validation 錯誤（400） | 「輸入名稱確認」的意義就是逐字確認；422＋code 讓前端能分辨 | 忽略大小寫或空白（中文名稱沒有大小寫問題，trim 反而會讓尾端空白不同的兩本帳本都比對成功） |
| D3 | preview 的交易與預定支出筆數只算**未刪除**的（query filter 照常套用）；稽核記錄算全部 | 使用者在 UI 看得到的才有意義；已軟刪除的資料一樣會被清掉，但對判斷「這本帳有沒有用過」沒有幫助 | 連同已軟刪除的一起算 |
| D4 | 稽核摘要直接重用 preview 的 DTO `BookDeletionPreview(Name, OpeningDate, LockDate, 各類筆數)` | 沿用「快照型別直接重用 API DTO」的慣例（`IAuditTrail`）；刪除當下的筆數就是使用者在對話框裡看到的數字 | 另定義一個 snapshot record |
| D5 | 刪除期間不鎖定帳本。若另一個請求在刪除途中寫入同一本帳本，可能留下 `BookId` 指向不存在帳本的孤兒資料，這個風險接受 | 家庭帳本幾乎沒有並行情境。孤兒資料不會出現在任何查詢裡（`BookAccessBehavior` 先擋 404）。要鎖得對，所有寫入 command 都要先鎖 Book 列，範圍過大 | 刪除前 `SELECT ... FOR UPDATE` 鎖 Book 列（其他寫入不會去搶這把鎖，所以擋不住） |
| D6 | Convention test：從 EF model 找出所有帶 `BookId` 且非 owned 的 entity，資料表清單必須等於測試裡寫死的清單；同一個測試類別裡，行為測試要為清單上的每張表都 seed 資料 | 新增帶 `BookId` 的表時，測試會失敗並提醒開發者同時更新 handler 與 seed。只用反射比對 handler 內部的清單，會與實際的刪除程式碼脫鉤 | 反射比對 handler 公開的型別清單 |
| D7 | 刪除順序：PlannedExpenses → RecurringPlannedExpenses（兩者之間有 Restrict FK）→ CategoryBudgets（Overrides 由 DB cascade 處理）→ Transactions（Postings 由 cascade 處理）→ BookMembers → AuditEntries → Books（Accounts／Categories／PlanningFunds 由 cascade 處理），最後 `audit.Record` 並 `SaveChangesAsync`、commit | 除了 Restrict FK 之外，其他表與 Books 之間沒有 FK，順序可以自由安排；既有的 cascade 都在 owned 表上，已經在 migration 裡確認 | — |

## 介面

```csharp
// Application/Books/DeleteBook.cs
public sealed record DeleteBook(Guid BookId, string ConfirmName) : IRequest, IBookScoped;

// Application/Books/GetBookDeletionPreview.cs
public sealed record GetBookDeletionPreview(Guid BookId) : IRequest<BookDeletionPreview>, IBookScoped;
public sealed record BookDeletionPreview(
    string Name, DateOnly OpeningDate, DateOnly? LockDate,
    int Transactions, int PlannedExpenses, int RecurringPlannedExpenses,
    int CategoryBudgets, int Members, int AuditEntries);
```

| 方法與路徑 | Request | 成功 | 錯誤 |
|---|---|---|---|
| `GET /api/books/{bookId}/deletion-preview` | — | 200 `BookDeletionPreview` | 404 非成員或帳本不存在 |
| `DELETE /api/books/{bookId}` | `{ "confirmName": "..." }` | 204 | 404 非成員；400 `confirmName` 空白；422 `name-mismatch`；400 缺少 XSRF token |

## 對既有程式的影響

- `DomainException`：新增 `NameMismatchCode`。
- `BooksEndpoints`：新增兩條路由。既有的 `SecurityConventionTests`、`MembershipTests` 用列舉方式涵蓋所有 endpoint，所以新路由會自動被檢查非成員 404、需要登入與 XSRF。
- 不新增 migration，也不改動既有的 handler。
- `import-legacy` 的同名檢查不需要修改：帳本刪除後名稱就空出來了。

## 風險

- `ExecuteDeleteAsync` 不經過 change tracker，也不觸發 EF 的 cascade，完全依賴 DB 上的 FK cascade。owned 表的 FK 都已經是 `Cascade`，計畫的事實查核會再確認一次。
- D5 的孤兒資料風險已接受，寫進 ADR 0010。
