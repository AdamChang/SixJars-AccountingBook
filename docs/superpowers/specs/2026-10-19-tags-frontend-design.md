# 標籤（前端）設計

- 需求與 grilling 結論：見後端設計的「決策」一節（G 編號）
- 後端設計（API 形狀以此為準）：[`2026-10-19-tags-backend-design.md`](2026-10-19-tags-backend-design.md)
- **前置條件**：後端計畫已完成。前端測試全部用 mock，不依賴後端；手動驗證需要後端。

## 目標

使用者在設定頁管理標籤（名稱、顏色、排序、封存、刪除）；記帳時可替任何交易選最多 4 個標籤；交易清單顯示標籤，並能依標籤篩選當月交易。

## 範圍

**包含**：`dto.ts`／`BookApi`／`TransactionApi` 的型別與方法、設定頁「標籤」分頁、`SettingDialog` 的顏色欄位、交易表單的標籤欄位、交易清單的標籤 chip、交易頁的標籤篩選、標籤色盤 token、e2e。

**排除**：標籤統計畫面（後端 API 尚未實作）、跨月份的標籤查詢（見 F8）、批次貼標籤。

## 已確認的事實

- 設定頁是 `mat-tab-group`，目前有「分類」「帳戶」「財務規劃帳戶」「鎖帳日與資料」四個分頁；每組設定都用 `SettingList` 與共用的 `SettingDialog`，頁面依 `SettingPath` 呼叫 `BookApi` 的泛用方法（`add`、`archive`、`unarchive`、`remove`、`reorder`）。
- 交易表單用 `selectable(items, keepIdsOf(editing))` 處理封存項目：封存的設定項目不出現在選項裡，但正在編輯的交易已經用到的會保留。
- `styles.scss` 只有淺色主題（`--ledger-*` token），**沒有深色模式**。grilling 時說「依淺色／深色模式對應色碼」，實際上只需要淺色一組；之後加深色主題時再補。
- `TransactionApi.list(bookId, budgetMonth)` 目前只帶 `budgetMonth`。

## 決策

| # | 決定 | 理由 | 被否決的選項 |
|---|---|---|---|
| F1 | 設定頁新增「標籤」分頁，放在「財務規劃帳戶」之後、「鎖帳日與資料」之前；內容是一組 `SettingGroup`（`path: 'tags'`），沿用 `SettingList` 的排序、修改、封存、刪除 | 與另外三種設定的操作完全相同；`SettingsPage` 的處理流程依 `path` 泛化，幾乎不必新增流程 | 獨立的標籤管理頁 |
| F2 | `SettingRow` 新增選填的 `color?: TagColor`；有值時在名稱前顯示一個色點，`detail` 顯示顏色的中文名稱 | 清單一眼看得出顏色；其他設定不受影響 | 另做 `TagList` 元件 |
| F3 | `SettingDialog` 支援 `path: 'tags'`：只有「名稱」與「顏色」兩個欄位。顏色是 12 個色塊組成的 radio group（`role="radiogroup"`，每個色塊有中文名稱的 `aria-label`），必選；新增時預設選「目前還沒有標籤用過的第一個顏色」，全部用過就選第一個 | 沿用共用對話框；色塊比下拉選單直覺，鍵盤也能操作 | 新增 `TagDialog` 元件；下拉選單 |
| F4 | 名稱長度驗證：共用函式 `textLength(s)` = `[...new Intl.Segmenter('zh-Hant', { granularity: 'grapheme' }).segment(s.trim())].length`，超過 8 顯示「最多 8 個字」；`maxlength` 屬性不用（它算的是 UTF-16 code unit） | 與後端 `StringInfo` 的 text element 算法一致（G5）| `maxlength="8"`（emoji 會被截斷，與後端不一致） |
| F5 | 色盤 token 定義在 `styles.scss` 的 `:root`：每色兩個變數 `--tag-<color>-bg`、`--tag-<color>-fg`，文字對背景對比 ≥ 4.5:1；`shared/tags.ts` 匯出 `TAG_COLORS`（順序同後端 enum）與 `TAG_COLOR_LABELS`（紅、橙、琥珀、黃、萊姆、綠、藍綠、青、藍、靛、紫、粉紅） | 顏色集中一處；chip 與色點共用 | 在各元件寫死色碼 |
| F6 | 共用元件 `TagChip`（`shared/tag-chip.ts`）：輸入 `TagDto`，顯示圓角小色塊與名稱；封存的標籤加上「（已封存）」的 `title` 與降低不透明度 | 表單、清單、設定頁都要用 | — |
| F7 | 交易表單：在「備註」下方新增「標籤」欄位，所有交易類型都顯示（G3）。用 `mat-select multiple`，trigger 顯示已選的 `TagChip`；選項是 `selectable(book.tags, keepTags)` 依設定排序，`keepTags` 為編輯中交易原本的標籤。已選 4 個時其餘選項 `disabled`，欄位下方顯示「最多 4 個」。表單值 `tagIds: string[]`，`toTransactionInput` **一律**送出 `tagIds`（後端 D4：沒帶等於清空） | 標籤是選填、會重複使用的少量選項，下拉多選在手機上最省空間；封存標籤的處理沿用 `selectable` | chip 輸入加 autocomplete（要處理輸入文字與建立新標籤，範圍變大）；放進「進階」摺疊區（使用者會找不到） |
| F8 | 交易頁的篩選：在月份切換旁新增「標籤」下拉選單（「全部標籤」＋本帳本所有標籤，含已封存的，依設定排序）；狀態放在 query param `tag`，與 `month` 並存；`TransactionApi.list(bookId, budgetMonth, tagId?)` 帶上 `tagId`。**只篩選當月**，換月份時保留標籤 | 與現有「依月份瀏覽」的頁面結構一致，不必做分頁；後端已支援組合條件。跨月份查詢（例如「日本旅遊」全部交易）留到標籤統計畫面，屆時從統計點進去 | 篩選時忽略月份、列出全部（目前清單不分頁，資料多時會很長） |
| F9 | 交易清單：標籤以 `TagChip` 顯示在「備註」欄裡，備註文字之前；不新增欄位 | 手機寬度已經很緊；標籤與備註都是補充說明 | 新增「標籤」欄 |
| F10 | 錯誤處理：送出交易遇到 422（標籤已封存、不屬於本帳本）顯示後端訊息，沿用表單既有的 `domain` 錯誤處理；設定頁的 `in-use` 沿用既有「改用封存」提示 | 不新增錯誤種類 | — |
| F11 | `BookDto.tags` 在前端型別為必填（後端一律輸出）；`TransactionDto.tagIds` 同樣必填 | 後端 D5 保證有值；前端不必到處處理 undefined | 選填 |

## 文案

- 分頁「標籤」；群組標題「標籤」；新增按鈕「新增」。
- 對話框標題「新增標籤」「修改標籤」；欄位「名稱」「顏色」；錯誤「請輸入名稱」「最多 8 個字」。
- 交易表單欄位「標籤」，提示「最多 4 個」。
- 篩選下拉「全部標籤」；清單為空時：「這個月沒有掛著「X」的交易」。
