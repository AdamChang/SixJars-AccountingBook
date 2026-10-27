# 交易清單篩選：分類（後端）設計

- 需求與 grilling 結論：2026-10-28 與使用者的 grilling（Q1–Q16，兩輪），本文件只收錄與後端有關的決策；完整清單見前端設計
- 前端設計：[`2026-10-28-transaction-filters-frontend-design.md`](2026-10-28-transaction-filters-frontend-design.md)
- 前一版篩選：[`2026-10-19-tags-backend-design.md`](2026-10-19-tags-backend-design.md) 的 G16、D13（`tagId`）
- 用語：`CONTEXT.md` 的「分類 (Category)」

## 目標

`GET /api/books/{bookId}/transactions` 新增 `categoryId` 篩選，讓記帳頁可以依主分類篩選。帳戶篩選（`accountId`）後端已經有了，這次不需要改。

## 範圍

**包含**

- `ListTransactions` 新增 `Guid? CategoryId`，endpoint 新增 `categoryId` query 參數。
- Api 整合測試。

**排除**

- `accountId` 的語意不變（Q4：主帳戶、對方帳戶、分錄帳戶任一個符合就算）。
- 匯出（`ExportTransactions`）不加篩選：記帳頁沒有匯出功能，匯出只有日期條件。
- 多值篩選（`categoryIds`）：Q5 決定每個條件一次只選一個，跟 G16 一樣。
- Domain、migration：都不會動到。分類是 `Book` 的 owned entity，`Transactions.CategoryId` 已經是欄位，也不需要新的索引（見 D3）。

## 決策

### Grilling 結論（Q，與後端有關的部分）

| # | 決定 | 理由 | 被否決的選項 |
|---|---|---|---|
| Q1 | 選主分類時，也比對它底下的子分類 | 交易的 `CategoryId` 大多存子分類，只比對主分類本身幾乎篩不到東西；跟月報表、預算「主分類＋子分類」的合計口徑一致 | 只比對本身；UI 開放選到子分類 |
| Q3 | 不限交易類型：`CategoryId` 落在範圍內就列出（含貸款繳款的利息分類、出資金） | 篩選要回答的是「哪些交易用了這個分類」，不是預算的實際支出；清單不顯示合計，所以口徑差異沒有影響 | 只列收入、支出兩種類型 |
| Q5 | 和其他條件（`budgetMonth`、`accountId`、`tagId`…）是 AND | 跟既有的篩選組合方式一樣 | — |
| Q6 | 在後端篩選 | 跟 `tagId` 一致；分類展開需要帳本的分類樹，放在 handler 處理最自然 | 前端拿到當月清單後自己過濾 |
| Q10 | `categoryId` 的語意是「這個分類以及它的下層」：傳主分類就比對它和子分類，傳子分類就只比對它自己 | 一句話講得完，不必多一條驗證；以後 UI 要開放篩子分類時，後端不必改 | 只接受主分類，傳子分類回 400 |
| Q11 | id 不存在、或屬於別本帳本時，回 200 加空清單 | 跟 `accountId`、`tagId` 的行為一致（`List_by_account_of_another_book_returns_nothing`、`List_filters_by_tag`） | 404／400 |

### 展開設計時新增（D）

| # | 決定 | 理由 | 被否決的選項 |
|---|---|---|---|
| D1 | 分兩步查：先從這本帳的分類裡取出 `Id == categoryId \|\| ParentId == categoryId` 的 id 清單，再用 `ids.Contains(t.CategoryId)` 篩交易 | 寫法與 `SettingReferences.HasPlannedExpensesAsync` 一致，好讀；多一次查詢的成本可以忽略（一本帳的分類只有數十個）。id 清單只取自**這本帳**的分類，所以別本帳的 id 自然會得到空清單（Q11），不必特別處理 | 用相關子查詢一次完成（可讀性較差，且要依賴 EF 能不能翻譯 owned collection 的 `Any`）；把整個 `Book` 載入記憶體（會多讀帳戶、標籤等不需要的資料） |
| D2 | id 清單的型別是 `List<CategoryId?>`，因為 `Transaction.CategoryId` 是 nullable | `Contains` 兩邊型別要一致才能交給 EF 翻譯；寫成 `t.CategoryId!.Value` 要多一層 null 處理，而且沒有先例 | `t.CategoryId != null && ids.Contains(t.CategoryId.Value)` |
| D3 | 不新增 `Transactions(CategoryId)` 索引 | 查詢一定帶 `BookId`，前端也一定帶 `budgetMonth`，掃描範圍只有一個月（約 200 筆） | 新增索引（需要 migration，換到的效能沒有意義） |
| D4 | 參數加在 `ListTransactions` 最後，給預設值 `= null` | 跟 `TagId` 一樣，既有的建構呼叫不必改 | — |

## 對既有程式的影響

| 檔案 | 改動 |
|---|---|
| `src/SixJars.Application/Transactions/ListTransactions.cs` | record 新增 `Guid? CategoryId = null`、`<param>` 註解、handler 新增 D1 的篩選 |
| `src/SixJars.Api/Endpoints/TransactionsEndpoints.cs` | `MapGet("/transactions")` 新增 `Guid? categoryId` 並傳進去 |
| `tests/SixJars.Api.Tests/TransactionsEndpointsTests.cs` | 新增篩選測試 |

`BookScopeConventionTests` 不受影響，因為 `ListTransactions` 本來就實作了 `IBookScoped`。
