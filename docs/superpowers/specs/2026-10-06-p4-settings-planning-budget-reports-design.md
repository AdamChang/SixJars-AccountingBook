# P4：帳本設定、預定支出、預算、報表 設計

- 日期：2026-10-06
- 範圍：後端＋前端。分四段 J、K、L、M，每段一份 plan。
- 依據：[CONTEXT.md](../../../CONTEXT.md)、[ADR 0008](../../adr/0008-reports-show-spending-not-disposable-balance.md)、[ADR 0009](../../adr/0009-recurring-planned-expenses-generated-explicitly.md)、[P3 設計](2026-10-04-p3-frontend-pwa-design.md) §10 的排除清單、`reference/2026帳本v1.xlsm` 的結構盤點（2026-10-06）
- 狀態：**設計已經在 2026-10-06 的 grilling（Q1–Q21）由使用者逐項確認**；本文件是那些決定的整理，待使用者檢視

## 1. 目標

完成之後，日常使用不再需要 Excel，也不再需要直接呼叫 API：

1. **帳本設定頁**：分類、帳戶、財務規劃帳戶可以新增、改名、封存、排序，在沒有被參照時可以刪除；分類的支出性質可以修改；可以設定鎖帳日；可以下載交易明細與備份。
2. **預定支出頁**：某個歸屬月份的預定支出可以列出、新增、修改、刪除、付款；可以從**週期預定支出**產生該月的預定支出，或以週期項目的現值更新該月未付的預定支出。
3. **預算頁**：浮動支出主分類的預算對實際；可以編輯預設值與該月覆寫值。總覽頁加一張預算摘要卡。
4. **月報表與年報表**：依 CONTEXT.md 與 ADR 0008 的歸組規則，附圖表。

### 1.1 使用者已確認的決定（2026-10-06 grilling）

| # | 項目 | 決定 | 被否決的選項 |
|---|---|---|---|
| Q1 | 「範本」指什麼 | **週期預定支出**（Excel 制式表格的主清單），不是記帳範本 | 記帳範本（Entry Template）：留到之後 |
| Q2 | 預定支出前端 | 納入本期，排在預算之前 | 繼續排除（預算與月報會說不通） |
| Q3 | 設定範圍 | 完整帳本設定頁：分類、帳戶、財務規劃帳戶、鎖帳日；新增、改名、封存、排序、有條件刪除 | 只做分類；允許把子分類搬到別的主分類（會悄悄改寫歷史小計） |
| Q4 | 改支出性質 | 可以改，**回溯生效**；有預算的分類必須先清除預算 | 不可改（同一筆費用會拆成兩個分類）；依生效月份（性質要變成有時間軸的屬性） |
| Q5 | 週期項目內容 | 分類、預設帳戶、預設金額、備註 | 只有分類（金額 0 時沒有占用月可用餘額，失去預定支出的意義） |
| Q6 | 產生時機 | 使用者對某月**明確**按「產生」，`(來源, 月)` 唯一且包含已刪除的（ADR 0009） | 讀取時自動產生；排程產生 |
| Q7 | 週期規則 | 每月，或每年的指定月份（可多選）；開始月份＋可選的結束月份；只限固定與貸款性質 | 只支援每月（年繳稅費、保費很常見） |
| Q8 | 預算定義 | 依 CONTEXT.md：浮動主分類，預設值＋月份覆寫，不累積；實際支出**包含**電子錢包消費 | 依 Excel（每月各自手填，固定支出也設預算） |
| Q9 | 預算 UI | 獨立預算頁；總覽頁只放摘要卡 | 放在設定頁；放進月報 |
| Q10 | 報表種類 | 月報＋年報；年 = 歸屬月份的曆年；月平均的分母 = 已過去的月份數 | 財務比率、淨值（依賴外部資產淨值）；Excel 的「排除尚未分配為 0 的月份」 |
| Q11 | 聚合位置 | **後端** `/reports/monthly`、`/reports/yearly` | 前端拿整年交易自己算（計算規則會在 TS 再寫一份） |
| Q12 | 分段 | J→K→L→M，一份 spec，**每段一份 plan，內部先後端再前端，兩個 checkpoint** | 每段前後端各一份 plan（8 份） |
| Q13 | 排序 | 新增 `SortOrder`；UI 用 CDK drag-drop＋每列 ▲▼ 按鈕；**不另外做鍵盤拖曳** | 只用 ▲▼；只用 drag-drop |
| Q14 | 封存邊界 | 主分類連帶子分類（不改子分類狀態）；帳戶與財務規劃帳戶餘額不為 0 不能封存；封存的分類只在該月有實際支出或覆寫值時出現在預算頁；週期項目參照已封存的設定時略過並回報 | 允許封存有餘額的帳戶 |
| Q15 | 週期項目的刪除與修改 | 產生過就只能設結束月份；提供「以現值更新該月未付的」明確動作 | 只能逐筆修改 |
| Q16 | 報表歸組 | 見 §6.1（ADR 0008） | 沿用月可用餘額的規則 |
| Q17 | 未付預定支出 | 月報在固定與貸款區塊另列「未付預定」小計，不計入合計；年報不列 | 不列 |
| Q18 | 圖表 | **Chart.js 直接用**，自己寫 standalone wrapper，報表路由 lazy load | ng2-charts（新 Angular major 支援延遲）、ECharts（體積）、手寫 SVG |
| Q19 | 驗收 | 沿用 `MonthFigureComparison`，以 Excel 1–3 月比對（§9） | 只做手動比對 |
| Q20 | 付款對話框 | 貸款本金**手動輸入**，不預填 | 預填上月本金（容易沒改就送出） |
| Q21 | 匯出、備份 | 兩個下載按鈕放進 J 段的設定頁 | 繼續排除 |

## 2. 對既有程式的影響

事實查核（2026-10-06）：

- `Category`：只有 `Id、Name、Kind、Nature、ParentId`，**沒有**排序或封存欄位。`Book.AddSubCategory` 禁止子分類再掛子分類。
- `Account`：`Id、Name、Type、OpeningBalance、CountsAsAvailableCash`；`PlanningFund`：`Id、Name、OpeningBalance`。兩者都**沒有**排序或封存欄位。
- `PlannedExpense`：`BudgetMonth、CategoryId、AccountId?、EstimatedAmount、Note、PaidTransactionId、DeletedAt`，分類限定固定、貸款、特別性質；**沒有**來源欄位。
- `LoanPayment` 的分類是**可空的利息分類**，金額分成總額與本金。
- 預算、報表、週期預定支出：Domain、Application、API 都**完全沒有**。
- API 端點：`BooksEndpoints`（設定，只能新增）、`PlannedExpensesEndpoints`（CRUD＋pay）、`ExportsEndpoints`、`SummaryEndpoints` 等已存在。
- 前端：`features/` 只有 `home、denied、transactions、summary`；`core/api` 有 `book-api、transaction-api、summary-api、session-api`；已安裝 `@angular/cdk` 22.2.x（drag-drop 可用），**沒有**圖表套件。
- 驗收：`tests/SixJars.AcceptanceTests/MonthFigureComparison.cs` 讀真實的 Excel（`SIXJARS_LEGACY_WORKBOOK`，預設 `reference/`，已被 gitignore），逐月比對 `LegacyMonthFigures`。

## 3. J：帳本設定

### 3.1 Domain

- `Category`、`Account`、`PlanningFund` 新增 `SortOrder`（int）與 `ArchivedAt`（`DateTimeOffset?`）。
  - `SortOrder` 的範圍：分類在**同一個父層**內（主分類以「同一種類」為父層，也就是收入主分類、支出主分類各自一組），帳戶與財務規劃帳戶各自是一組。
- `Book` 新增以下操作，規則都放在 Domain：
  - `Rename*`：名稱唯一性沿用新增時的規則。
  - `Archive*`／`Unarchive*`：帳戶與財務規劃帳戶要求**餘額為 0**。餘額要從分錄計算，所以這條規則由 Application 先查出餘額再傳給 Domain，Domain 不直接查詢。封存主分類**不**修改子分類的狀態；「子分類是否可以選用」= 自己與主分類都沒有被封存。
  - `Reorder*(parent, orderedIds)`：傳入的 Id 集合必須**恰好等於**該父層現有成員，否則拒絕。這樣可以擋掉過時的畫面造成的漏項或重複。
  - `ChangeNature(categoryId, nature)`：只限支出主分類。該分類**有任何預算紀錄**（預設值或覆寫值）時拒絕。因為這需要 L 段的預算資料，J 段先實作不帶預算檢查的版本，L 段再補上檢查與對應的測試。
  - `Remove*`：只有在沒有被交易、預定支出、週期項目、預算參照時才可以刪除。參照檢查由 Application 查詢資料庫。
- migration 依「目前的插入順序」回填 `SortOrder`。**開放問題（寫 J plan 時查證）**：目前的資料表與 import 程式碼能不能還原出 Excel 清單的順序？查不到就向使用者報告，不要自己猜。

### 3.2 API

所有路徑都在 `/api/books/{bookId}` 底下，寫入一律寫稽核記錄。

| 方法 | 路徑 | 說明 |
|---|---|---|
| PUT | `/categories/{id}`、`/accounts/{id}`、`/planning-funds/{id}` | 改名（帳戶另外可以改「計入可用現金」）；分類另外可以改支出性質 |
| POST | `/…/{id}/archive`、`/…/{id}/unarchive` | |
| DELETE | `/…/{id}` | 被參照時回 409 ProblemDetails |
| PUT | `/categories/order`、`/accounts/order`、`/planning-funds/order` | body：`{ parentId?, kind?, ids: [] }`，一次送整個父層的新順序 |

`GET /` 的 `BookDto` 加上 `sortOrder` 與 `archived`，並依 `SortOrder` 排序。記帳頁的下拉選單改成排除已封存的項目。

### 3.3 前端

- 新路由 `books/:bookId/settings`，用分頁（tab）分開分類、帳戶、財務規劃帳戶，另外一個分頁放鎖帳日與資料（匯出 CSV、xlsx 與備份下載，都是 `<a href download>`）。
- 列表：CDK drag-drop＋每列 ▲▼ 按鈕。放下後一次送出新順序；失敗時還原畫面順序並顯示錯誤。
- 已封存的項目預設收起，可以展開顯示（灰色），並提供「解除封存」。
- `CurrentBook` 在設定變更後重新載入，讓記帳頁的選項同步更新。

## 4. K：預定支出與週期預定支出

### 4.1 Domain

- 新 aggregate `RecurringPlannedExpense`：`BookId、CategoryId、AccountId?、DefaultAmount、Note、Recurrence、StartMonth、EndMonth?`。
  - `Recurrence`：`Monthly`，或 `Yearly(months: 1–12 的集合，至少一個)`。
  - 分類必須是固定或貸款性質。
  - `IsDueIn(BudgetMonth)`：月份落在 `[StartMonth, EndMonth]` 內，且符合週期。
- 選擇獨立 aggregate，而不是放進 `Book`：週期項目和 `PlannedExpense` 一樣是逐月操作的資料，不是帳本設定；放進 `Book` 會讓每次載入帳本都帶上它。
- `PlannedExpense` 新增 `SourceId: RecurringPlannedExpenseId?`。資料庫在 `(SourceId, BudgetMonth)` 建立唯一索引，條件是 `SourceId IS NOT NULL`，**不**過濾已刪除的資料（ADR 0009）。
- 週期項目只要有任何 `PlannedExpense` 參照它（包含已刪除的），就不能刪除。

### 4.2 Application／API

| 方法 | 路徑 | 說明 |
|---|---|---|
| GET／POST | `/recurring-planned-expenses` | |
| PUT／DELETE | `/recurring-planned-expenses/{id}` | DELETE 被參照時回 409 |
| POST | `/planned-expenses/generate?budgetMonth=` | 冪等。回傳 `{ created: [], skipped: [{ recurringId, reason }] }`，reason 是 `AlreadyGenerated` 或 `CategoryArchived`、`AccountArchived` |
| POST | `/planned-expenses/refresh?budgetMonth=` | 只更新**未付、未刪除、有來源**的預定支出，改用週期項目的現值（分類、帳戶、金額、備註）；週期項目當月已不適用時不更動，列在結果中 |

- `generate` 與 `refresh` 都會寫稽核記錄（每筆建立或修改的預定支出各一筆）。
- 鎖帳日：比照現有的預定支出規則，以歸屬月份的最後一天判斷；已鎖的月份兩個動作都拒絕。

### 4.3 前端

- `books/:bookId/planned-expenses`：依月份切換，用 ◀ ▶ 換月，與記帳頁一致。
  - 列表依性質分組（固定、貸款、特別），每列顯示已付或未付。
  - 「產生本月」「以現值更新」兩個按鈕，結果用 snackbar 摘要（例如「建立 5 筆，略過 1 筆：分類已封存」）。
  - 付款對話框：日期（今天）、金額（預估金額）、帳戶（預定支出的帳戶）；貸款另外要填本金（必填，不預填）。
- 週期項目管理：放在同一頁的第二個分頁。

## 5. L：預算

### 5.1 Domain

- 新 aggregate `CategoryBudget`（每個浮動主分類一個）：`CategoryId、DefaultAmount?`，加上 `Overrides: (BudgetMonth → Amount)`。
  - 預算額以**正數**表示可以花的上限。
  - 只允許浮動性質的支出**主**分類。
- 否決「預算放進 `Book`」：預算會隨月份累積覆寫值，和設定的生命週期不同。
- 回到 J 段：`ChangeNature` 在該分類有 `CategoryBudget` 時拒絕；`Remove` 的參照檢查也要納入預算。

### 5.2 查詢

`GET /budgets?budgetMonth=` 回傳每個浮動主分類的一列：`categoryId、budget（null 表示未設）、source（Default | Override）、actual、remaining`。

- **actual** = 該主分類及其子分類，在該歸屬月份的**支出**交易合計，包含電子錢包消費，排除軟刪除（CONTEXT.md「預算」、ADR 0008）。
- 已封存的分類：只有在該月 actual ≠ 0 或有覆寫值時才列出。
- 資料庫往返目標：≤ 4 次（成員授權、帳本設定、預算、彙總）。

寫入：`PUT /budgets/{categoryId}/default`、`PUT /budgets/{categoryId}/overrides/{budgetMonth}`，以及對應的 DELETE。

### 5.3 前端

- `books/:bookId/budgets`：依月份切換。
  - 每列顯示主分類、預算（標示「預設」或「本月」）、實際、剩餘、使用率進度條；超支時剩餘與進度條改用警示色。
  - 行內編輯本月覆寫值與預設值。
- 總覽頁加一張卡：本月浮動預算合計、已用、剩餘。資料另外呼叫 `/budgets`，不擴充 `/summary`，以免 `/summary` 的往返次數斷言失效。

## 6. M：報表

### 6.1 歸組規則（ADR 0008）

| 交易類型 | 位置 | 金額 |
|---|---|---|
| 收入 | 收入 → 主 → 子 | 金額 |
| 支出（含電子錢包消費） | 依支出性質 → 主 → 子 | 金額 |
| 貸款繳款 | 「貸款」性質，依**貸款帳戶**分列，附本金與利息兩欄 | 總額 |
| 入新資金 | 財務規劃區塊，依財務規劃帳戶 | 金額 |
| 出資金、資金回流 | 財務規劃區塊的另外兩欄，不計入支出合計 | 金額 |
| 電子錢包加值、轉帳、提款、現金存入、繳卡費、新增貸款 | 不出現 | — |

- 交易直接掛在主分類（沒有子分類）時，在該主分類底下列為「（未分子類）」。
- 月份一律依**歸屬月份**。

### 6.2 API

- `GET /reports/monthly?budgetMonth=`：樹狀結果，包含收入、四種支出性質、財務規劃，以及固定與貸款區塊的未付預定支出小計（不計入合計）。另外附上 `/summary` 的月可用餘額，供頁面最上方對照。
- `GET /reports/yearly?year=`：每一列帶 12 個月的值、年總計、佔收入比、月平均。
  - 月平均的分母：今年 = 到本月為止的月份數（以台北時間計算），過去的年份 = 12，未來的年份不計算。
  - 只列實際交易。
- 聚合在資料庫以 `GROUP BY` 完成。往返目標：月報 ≤ 6 次，年報 ≤ 4 次。實作後如需調整，比照 P2 的做法，先徵得使用者同意。

### 6.3 前端

- `books/:bookId/reports/monthly`、`books/:bookId/reports/yearly`。兩個路由都 lazy load，Chart.js 只在這裡載入。
- 月報：可展開的樹狀表格，附兩張甜甜圈圖（支出依性質、浮動支出依主分類）。最上方顯示月可用餘額與差異說明。
- 年報：月份 × 分類的表格，附每月支出依性質的堆疊長條圖。
- `ChartComponent`：standalone，輸入 `ChartConfiguration`，負責建立與 `destroy` Chart.js 實例。只 import 需要用到的 Chart.js controller 與 element（tree-shake）。

## 7. 前端共通

- 新增 `core/api`：`settings-api`（或擴充 `book-api`）、`planned-expense-api`、`recurring-planned-expense-api`、`budget-api`、`report-api`，DTO 一樣手寫。
- app shell 的導覽列加上：預定支出、預算、報表、設定。
- 沿用 P3 的錯誤分類與 snackbar；409（被參照、餘額不為 0、性質有預算）要在畫面上顯示後端的訊息。

## 8. 測試

沿用 P2、P3 的分層：Domain 單元測試、Application handler 測試、API 整合測試（`TestAuthHandler`）、前端 Vitest（service 與純函式）、少量 Playwright（以 `page.route` 模擬 API）。

- 每個新的查詢 endpoint 都要斷言資料庫往返次數。
- drag-drop 只做元件層級的測試，驗證放下之後送出的 Id 順序；▲▼ 按鈕走同一條程式路徑，用按鈕來測。

## 9. 驗收（對照 Excel 1–3 月）

擴充 `LegacyWorkbook` reader，讀出更多「只供驗收」的數字，比照 `LegacyMonthFigures`，不參與匯入。

| 段 | 比對 | Excel 來源 |
|---|---|---|
| J | 改名、封存、排序之後，既有的比對全部不變 | 既有 |
| K | 沒有 Excel 對照，以單元與整合測試涵蓋 | — |
| L | 各浮動主分類每月的 actual = `預算` 工作表的實際支出欄 | `預算!E4:E28`（每月一組欄） |
| M | 月報的浮動各主分類、固定、貸款、特別小計、各收入項目 = 月工作表；年報 1–3 月與年總計 = 總表 | 月表 `J34`、`X` 欄、`X46`、`X70`、`X82`、`X92`、`J10:J17`；`總表` |

- 已知可能的落差：貸款（Excel `X82` 是「制式表格的手填金額＋流水帳」），以及特別支出的來源。對不上時**逐筆列出差異回報給使用者**，不要為了讓數字相同去調整規則。
- 總表「支出圖」的系列錯位是 Excel 本身的錯誤，圖表不納入比對。

## 10. 分段與 checkpoint

| 段 | 後端 checkpoint | 前端 checkpoint |
|---|---|---|
| **J** | 設定的改名、封存、排序、刪除、改性質 API 全綠；既有驗收不變 | 設定頁可以完成所有操作；記帳頁的選項排除封存項目並依序排列；可以下載備份 |
| **K** | 週期項目 CRUD、generate、refresh 全綠；唯一索引的 migration | 預定支出頁可以產生、更新、付款 |
| **L** | 預算 CRUD 與查詢全綠；1–3 月 actual 對上 Excel；J 的改性質與刪除補上預算檢查 | 預算頁＋總覽卡 |
| **M** | 月報、年報全綠；1–3 月對上 Excel（或已回報差異） | 兩個報表頁＋圖表 |

- tddplan 慣例是前後端分開成兩份 plan。本期依使用者在 Q12 的決定，每段一份 plan，plan 內先寫後端 task、再寫前端 task；後端全綠是前端開始的條件，兩邊的基準線與測試指令分別列出。
- 每段結束時：前後端都全綠、一個 Task 一個 commit，停下來讓使用者檢視，並用 `docs(plans):` 回寫偏差。部署與 push 由使用者執行。

## 11. 明確排除（不在 P4）

- 記帳範本（Entry Template）。
- 記帳者與唯讀角色：設定、預算、週期項目都只限擁有者操作。
- 財務比率、資產負債、淨值追蹤、外部資產淨值。
- 信用卡對帳、房貸試算與貸款分段、提醒事項、月備忘錄、語錄。
- 稽核歷史的前端、軟刪除的還原、30 天未備份的提醒。
- 報表匯出成 xlsx 或 PDF、手機專屬版面、離線、深色模式、i18n。
- 子分類搬移到別的主分類、依生效月份改變支出性質、預算的副分類粒度與累積。
