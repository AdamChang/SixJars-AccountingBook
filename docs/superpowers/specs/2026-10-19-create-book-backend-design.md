# 在 Web 建立帳本與建立者名單（後端）設計

- 需求與 grilling 結論：GitHub issue [AdamChang/SixJars-AccountingBook#7](https://github.com/AdamChang/SixJars-AccountingBook/issues/7) 的五則決定留言（2026-10-19，A1–A4、Q5–Q26）
- 前端設計：[`2026-10-19-create-book-frontend-design.md`](2026-10-19-create-book-frontend-design.md)
- 相關 ADR：[`0005`](../../adr/0005-bff-cookie-auth-with-book-member-whitelist.md)（白名單即成員表，本設計部分取代）、[`0006`](../../adr/0006-explicit-snapshot-audit-and-soft-delete.md)（稽核快照）、[`0010`](../../adr/0010-delete-book-is-hard-delete.md)（刪除帳本）；本設計新增 ADR 0011

## 目標

被管理者放進**建立者名單**的人，可以在 Web 建立空白帳本，並自動成為擁有者。建立者即使沒有任何帳本也能登入。每人最多擁有 5 本帳本，同一位擁有者底下不能有同名帳本。

今天新帳本只能由 CLI `import-legacy`／`restore-backup` 產生；刪掉最後一本帳本的人，下次登入還會被拒絕（#8 已在刪除對話框警告）。

## 範圍

**包含**

- 新的 Domain 實體 `BookCreator`、資料表 `BookCreators`、migration。
- CLI：`add-creator --email`、`remove-creator --email`。
- 登入：`GoogleSignInValidator` 也比對、綁定建立者名單。
- `POST /api/books`：建立空白帳本。
- `GET /api/me` 新增 `canCreateBooks`、`remainingBookQuota`。
- 同名檢查改為「同一位擁有者」，Web 與 CLI `import-legacy` 共用。
- `ForbiddenException` 對應 403。
- ADR 0011、ADR 0005 標註部分取代、`CONTEXT.md` 新增用語。

**排除**

- 在 Web 匯入 Excel（Q12：p2 spec §8.1 的否決理由仍成立）、在 Web 還原備份（Q12：之後另外處理）。
- 帳本範本、從現有帳本複製結構（Q13）。
- 在 Web 維護建立者名單、系統管理者角色（Q5）。
- 從名單移除後立即撤銷既有 cookie（Q20）。
- 建立者名單異動的稽核記錄（Q24）。
- 修改開帳日（Q14）。

## 決策

Q 編號是 issue #7 的 grilling 結論，這裡只摘錄後端相關的部分。D 編號是展開設計時新增的。

| # | 決定 | 理由 | 被否決的選項 |
|---|---|---|---|
| A1／Q6 | 只有建立者名單裡的人能建立帳本，成員身分只決定能存取哪些帳本 | 「誰能建立」只有一個來源，5 本上限也只對名單裡的人計算 | 任何成員都能建立（名單形同虛設）；開放任何 Google 帳號（等於開放註冊） |
| Q5 | 名單是新資料表 `BookCreators`，由 CLI `add-creator`／`remove-creator` 維護 | 與 ADR 0005「名單在 DB、由 CLI 建立第一位擁有者」一致 | 環境變數（ADR 0005 已否決：改名單要重新部署）；Web 維護（需要系統管理者角色） |
| Q7／Q25 | 登入條件改為「成員**或**建立者」。建立者也以 email 綁定 sub；已綁定其他 sub 的列不比對、不擲例外 | 與成員的現行規則一致；`GoogleSignInValidator` 的註解寫明不能讓 callback 變成 500 | 綁到不同 sub 時拒絕登入（要連成員規則一起改） |
| Q8 | 上限是「目前身為擁有者的帳本數」，最多 5 本。只在 `CreateBook` 檢查，CLI 不受限；被 `add-member` 加成擁有者而超過時不擋 | 刪除帳本後名額會空出來；不需要另外記錄建立者是誰 | 計算「曾經建立過的數量」（需要新欄位，刪除也不釋出名額） |
| Q9／Q26 | 同名檢查改為只比對同一位擁有者的帳本，ordinal 比對 trim 後的名稱。Web 以 sub 認人，CLI 以 `--owner-email` 認人，共用一個 helper | Web 與 CLI 用同一條規則；CLI 拿不到 sub | 維持全系統比對（Web 能建、CLI 不能匯，規則矛盾）；兩邊都用 email |
| Q16 | 在建立的 transaction 裡以 `pg_advisory_xact_lock` 鎖住建立者再計數 | 讓「最多 5 本」真的成立，成本只多一個 SQL | 接受競態；`Serializable`（衝突以例外冒出，需要重試） |
| Q17 | `POST /api/books`，body `{ name, openingDate }`，回 `201`＋`{ id }`＋`Location` | 沿用 `BooksEndpoints.Created` 的寫法 | — |
| Q18 | 不在名單 403；名額用完 422 `book-quota-exceeded`；同名 422 `duplicate-name` | 不在名單是權限問題；端點不屬於特定帳本，不需要用 404 隱藏存在與否 | 不在名單也回 422 |
| Q19 | 建立帳本留一筆 `Create`／`Book`，`After` 是名稱與開帳日 | ADR 0006；與 #6 的刪除摘要成對 | 不留 |
| Q20 | `remove-creator` 不影響已建立的帳本；之後不能再建立；若也不是任何成員，下次登入被拒 | 與移除成員的現行行為一致 | 立即撤銷 cookie |
| Q24 | 建立者名單的異動不留稽核記錄，是 ADR 0006 的例外 | `AuditEntry.BookId` 是 `required Guid`，名單不屬於任何帳本；只有能直接連 DB 的管理者能異動 | `BookId` 改 nullable；另開系統稽核表 |
| D1 | `BookCreator` 放在 `Domain/Members`，email 正規化沿用 `BookMember.NormalizeEmail`，`BindSubject` 規則與 `BookMember` 相同 | 兩者的身分綁定是同一個概念，規則應該一致 | 放在 Application（它有不變條件，屬於 Domain） |
| D2 | `BookCreators` 以 `Email` 為唯一索引，`GoogleSubject` 一般索引 | 同一個 email 只出現一次；登入與 `/me` 以 sub 查詢 | — |
| D3 | `ISixJarsDbContext` 新增 `AcquireTransactionLockAsync(string key, ct)`，內部執行 `SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))` | 依介面的慣例，把 provider 特有的細節藏在方法後面；`hashtextextended` 把任意字串轉成 bigint | 在 Application 直接寫 raw SQL；`hashtext`（只有 32 bit，碰撞機率較高） |
| D4 | `CreateBook` 不實作 `IBookScoped`（沒有 `BookId`），權限在 handler 內檢查 | `BookAccessBehavior` 只處理帳本範圍；建立者檢查需要的是名單 | 新增一個 pipeline behavior（只有一個使用者，不值得） |
| D5 | 新增 `Application/Common/ForbiddenException`，`ApiExceptionHandler` 對應 403 | 目前沒有任何 403 的例外 | 沿用 `DomainException`（422） |
| D6 | `CreateBook` 的 handler 順序：開 transaction → 鎖 → 建立者檢查 → 上限 → 同名 → 寫入帳本、擁有者成員、稽核 → 一次 `SaveChanges` → commit | 鎖要在任何計數之前；建立者檢查放在上限之前，非建立者看不到「名額」相關訊息 | 先查再鎖（鎖之前的計數沒有意義） |
| D7 | 擁有者成員由 `BookMember.Create(…, user.Email, Owner)` 後立即 `BindSubject(user.Subject)`；`user.Email` 為 null 時擲 `ForbiddenException` | 建立者一定是以 email 綁定進來的，email 不會是 null；防禦性檢查不必做成新的錯誤碼 | 允許沒有 email 的成員（違反 `NormalizeEmail` 的前提） |
| D8 | `MeDto` 新增 `CanCreateBooks`、`RemainingBookQuota`；非建立者的 `RemainingBookQuota` 為 0，建立者為 `max(0, 5 − 擁有數)` | 前端只負責顯示，不需要知道上限是 5 | 回傳 `MaxBooks` 讓前端自己減 |
| D9 | `DomainException` 新增 `DuplicateNameCode = "duplicate-name"`、`BookQuotaExceededCode = "book-quota-exceeded"`；`ImportLegacyBook` 的同名錯誤也改用 `duplicate-name` | 前端靠 code 判斷，CLI 也統一 | — |
| D10 | 上限常數放在 `CreateBook.MaxOwnedBooks = 5` | 只有建立帳本與 `/me` 用得到 | 放在設定檔（沒有要調的需求） |

## API

### `POST /api/books`

```json
{ "name": "家庭帳本", "openingDate": "2026-10-19" }
```

| 狀況 | 狀態碼 | body |
|---|---|---|
| 成功 | 201 | `{ "id": "…" }`，`Location: /api/books/{id}` |
| name 空白、超過 100 字 | 400 | ValidationProblemDetails（`errors.name`） |
| 不在建立者名單 | 403 | ProblemDetails |
| 已擁有 5 本 | 422 | `code: "book-quota-exceeded"` |
| 同一位擁有者已有同名帳本 | 422 | `code: "duplicate-name"` |

`/api` group 既有的登入要求與 `AntiforgeryFilter` 都適用。

### `GET /api/me`

```json
{ "subject": "…", "email": "…", "books": [ … ], "canCreateBooks": true, "remainingBookQuota": 4 }
```

## 對既有程式的影響

| 位置 | 改動 |
|---|---|
| `GoogleSignInValidator` | 同時綁定未綁定的建立者（不留稽核，Q24），通過條件加上「有建立者綁定這個 sub」 |
| `ImportLegacyBook` | 同名檢查改為只比對 `--owner-email` 擁有的帳本，錯誤 code 改為 `duplicate-name`。**行為變更**：不同擁有者現在可以匯入同名帳本 |
| `GetMe`／`MeDto` | 新增兩個欄位 |
| `ApiExceptionHandler` | 新增 `ForbiddenException` → 403 |
| `ISixJarsDbContext`／`SixJarsDbContext` | 新增 `BookCreators`、`AcquireTransactionLockAsync` |
| `DeleteBook` | 不動。`BookCreators` 沒有 `BookId`，不在 `DeleteBookEndpointsTests.BookScopedTables` 的 convention test 範圍內 |
| DB 權限 | 新資料表由 migrator 建立，會套用 deploy.md §3.3 的 `ALTER DEFAULT PRIVILEGES`，不必手動 GRANT |

## 已知限制

- Google 帳號改過 email 之後，CLI `import-legacy --owner-email <新 email>` 會把他當成另一個人，同名檢查不會擋（Q26）。
- 被移除的建立者，既有 cookie 在過期前仍可呼叫 `POST /api/books`，但 handler 每次都查名單，所以會得到 403。

## 測試策略

- Domain：`BookCreator` 的建立、正規化與綁定規則。
- Api（Testcontainers）：`POST /api/books` 的各種狀態碼、寫入內容、稽核、上限與同時送出、`/me` 欄位；`GoogleSignInValidator` 的建立者情境。
- Infrastructure：`ImportLegacyBook` 的同名規則改變。
- Cli：`add-creator`、`remove-creator`。
- 同時送出的測試以 `Task.WhenAll` 送兩個請求。沒有鎖時不一定每次都重現，所以這個測試的變異檢查**不保證**變紅，計畫裡要明講，不能把「沒變紅」解讀成鎖沒作用。
