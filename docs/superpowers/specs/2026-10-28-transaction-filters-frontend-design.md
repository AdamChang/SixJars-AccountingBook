# 交易清單篩選：分類與帳戶（前端）設計

- 需求與 grilling 結論：2026-10-28 與使用者的 grilling（Q1–Q16，兩輪），本文件「決策」一節完整收錄
- 後端設計：[`2026-10-28-transaction-filters-backend-design.md`](2026-10-28-transaction-filters-backend-design.md)（`categoryId` 參數）
- 前一版篩選：[`2026-10-19-tags-frontend-design.md`](2026-10-19-tags-frontend-design.md) 的 F8（標籤篩選只篩當月、換月份保留）
- 用語：`CONTEXT.md` 的「分類」「帳戶」「標籤」「封存」

## 目標

記帳頁右上角現在只有標籤篩選，這次再加上「分類（主分類）」和「帳戶」兩個篩選，三個條件可以組合使用，並提供一鍵清除。

## 範圍

**包含**

- `TransactionApi.list` 改成接受篩選物件，可以帶 `tagId`、`categoryId`、`accountId`。
- 記帳頁工具列新增分類、帳戶兩個下拉選單，以及「清除篩選」按鈕。
- 修正：query param 對不到選項時，視為沒有這個條件（標籤也一起修）。
- 清單為空時的說明文字改成通用的寫法。
- e2e：新增一條組合篩選後清除的情境；修正因新選單而變得不唯一的既有 locator；手機寬度的溢出檢查把新選單也算進去。

**排除**

- 篩選子分類、多選（Q1、Q5）。
- 篩選條件同步到其他頁面。工具列導覽的 `queryParamsHandling="preserve"` 本來就會把 query params 帶到別的頁面，其他頁面會忽略它們，這個行為不變。
- 匯出。

## 決策

### Grilling 結論（Q）

| # | 決定 | 理由 | 被否決的選項 |
|---|---|---|---|
| Q1 | 分類篩選只能選主分類；選了之後也包含它的子分類（後端處理） | 交易大多掛在子分類上；口徑跟報表、預算一致 | 可以選到子分類 |
| Q2 | 收入和支出放在**同一個**「分類」下拉選單，用 `mat-optgroup` 分成「收入」「支出」兩組 | 工具列只多一個控制項；「收入、支出都選」沒有意義 | 先切換收入或支出再選分類；收入、支出各一個選單 |
| Q3 | 不限交易類型（後端處理） | — | — |
| Q4 | 帳戶篩選沿用後端既有的語意（主帳戶、對方帳戶、分錄帳戶，任一個符合就算） | 「這個帳戶這個月有哪些進出」應該包含轉入 | 只比對主帳戶 |
| Q5 | 三個條件之間是 AND，每個條件一次只選一個；各自一個 query param：`tag`、`category`、`account`；換月份時保留 | 跟 F8 的標籤篩選一致 | 條件內可以多選（OR） |
| Q6 | 在後端篩選 | 跟標籤篩選一致 | 前端自己過濾當月清單 |
| Q7 | 已封存的分類、帳戶也出現在選單，名稱後面加「（已封存）」 | 不然篩不到舊交易 | 只列出未封存的 |
| Q8 | 工具列的版面：月份導覽、三個下拉選單（空間不夠時換行），最後是「清除篩選」按鈕，只在有條件生效時顯示 | 三個選單還不算擠；逐一改回「全部」很麻煩 | 把選單收進「篩選」按鈕 |
| Q9 | 有任一條件生效時，空清單顯示「這個月沒有符合篩選條件的交易」；沒有條件時維持「這個月還沒有交易」 | 條件已經顯示在選單上了，組成一長串句子沒有額外價值 | 把條件組合成一句話 |
| Q11 | query param 對不到帳本裡的選項時（id 不存在、已刪除、別本帳的 id，或分類參數是子分類），就當作沒有這個條件，不送給後端；選單顯示「全部…」 | 網址壞掉時，畫面要跟「全部」一致，不能出現「選單顯示全部、清單卻是空的」這種矛盾 | 照樣送給後端，結果是空清單 |
| Q12 | 分類選單：先放「全部分類」，接著「收入」「支出」兩組，組內依 `sortOrder` 排 | — | 支出再依性質細分 |
| Q13 | 帳戶選單：先放「全部帳戶」，接著所有帳戶平鋪列出，依 `sortOrder` 排 | 沿用使用者在設定頁排好的順序 | 依帳戶類型分組 |
| Q14 | 「清除篩選」一次清掉 `tag`、`category`、`account`，月份保留 | 月份是正在看的位置，不算篩選條件 | 月份也一起重設 |
| Q16 | e2e 新增一條「三個條件組合篩選，再清除」的情境 | query param、選單、請求三者的同步，單元測試抓不到 | — |

### 展開設計時新增（F）

| # | 決定 | 理由 | 被否決的選項 |
|---|---|---|---|
| F1 | `TransactionApi.list(bookId, budgetMonth, filter: TransactionFilter = {})`，其中 `TransactionFilter = { tagId?, categoryId?, accountId? }`；沒有值的條件不帶參數；參數順序固定是 `budgetMonth`、`tagId`、`categoryId`、`accountId` | 三個 optional 的位置參數很容易傳錯位置；固定順序讓測試可以直接比對 URL 字串。唯一的呼叫端是記帳頁 | 再加兩個位置參數 |
| F2 | 頁面上每個條件都分成兩個 signal：原始的 query param，以及**有效的選項**（`filterTag`／`filterCategory`／`filterAccount`，對不到時為 `null`）。送給後端的、選單綁定的、判斷「有沒有篩選」的，都只看有效的那個 | 實現 Q11；三個地方一致，不會出現畫面和請求不同步 | 讀到無效的 query param 時直接改寫網址（會多一次導覽，也會留下歷史紀錄） |
| F3 | 分類的有效選項只認**主分類**（`parentId === null`）；網址帶子分類 id 時當作沒有篩選 | Q1 規定 UI 只能選主分類，網址也照同樣的規則；後端雖然接受子分類（後端 Q10），但前端不使用 | 前端也接受子分類（選單裡找不到對應的選項，沒辦法顯示目前選了什麼） |
| F4 | `TransactionList` 的 `filterTag: TagDto \| null` 輸入改成 `filtered: boolean` | 說明文字已經不提標籤名稱了（Q9），清單只需要知道有沒有篩選 | 改傳三個有效選項（清單用不到） |
| F5 | 選單的 `aria-label` 是「分類篩選」「帳戶篩選」，placeholder 是「全部分類」「全部帳戶」，跟既有的「標籤篩選」「全部標籤」對稱 | — | — |
| F6 | 既有 e2e 裡的 `getByRole('combobox', { name: '帳戶' })`、`{ name: '分類' }` 改成 `exact: true` | Playwright 的 name 預設是子字串比對，會同時抓到表單的「帳戶」和新的「帳戶篩選」，觸發 strict mode 錯誤。表單的標籤欄位早就因為「標籤篩選」用了 `exact: true`（`tags.spec.ts`） | 改用 CSS selector |
| F7 | 選單選項依 `sortOrder` 排序，排序時複製陣列，不改到 `CurrentBook` 的資料 | 沿用 `tagOptions` 的寫法 | — |
| F8 | 選擇條件時呼叫 `router.navigate([], { queryParams: { [name]: id }, queryParamsHandling: 'merge' })`；`null` 時移除參數。三個選單共用同一個 `changeFilter(name, id)` | 跟現在的 `changeTag` 一樣，只是一般化 | 每個選單各寫一個方法 |

## 已知的副作用

- 切換帳本時，`CurrentBook` 會比路由的 `bookId` 先更新，所以有一瞬間是「舊 bookId ＋ 新帳本的選項」：舊的條件對不到新帳本的選項，會先送出一次沒有篩選的請求，緊接著才是新 bookId 的請求，前一個會被 `switchMap` 取消。這只多出一次被取消的請求，畫面上看不出來。而且帳本選單的連結本來就不帶 query params，切換帳本時條件也會清掉。

## 對既有程式的影響

| 檔案 | 改動 |
|---|---|
| `web/src/app/core/api/transaction-api.ts` | F1 |
| `web/src/app/features/transactions/transactions.page.{ts,html,scss}` | 兩個新選單、清除鍵、F2、F3、F8 |
| `web/src/app/features/transactions/transaction-list.ts` | F4、Q9 |
| `web/e2e/tags.spec.ts`、`web/e2e/transactions.spec.ts` | F6；手機寬度測試加入新的條件 |
| `web/e2e/transaction-filters.spec.ts`（新增） | Q16 |
