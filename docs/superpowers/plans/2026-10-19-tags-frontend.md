# 標籤（前端）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL：逐 Task 執行時使用 TDD（mattpocock-skills:tdd 或 superpowers:test-driven-development）。每個 Task 的流程是「失敗測試 → 確認失敗原因 → 最小實作 → 全綠 → 一個 commit」。

**Goal**：設定頁可以管理標籤；交易表單可以選最多 4 個標籤；交易清單顯示標籤 chip，並可依標籤篩選當月交易。

**Architecture**：型別與 API 方法先到位（`TagDto`、`BookDto.tags`、`TransactionDto.tagIds`、`TransactionApi.list` 的 `tagId`）。設定頁把標籤當成第四種 `SettingPath`（`'tags'`），沿用 `SettingList`、`SettingDialog` 與 `SettingsPage` 的泛用流程，只在對話框加上顏色欄位。共用的 `TagChip` 元件與 `shared/tags.ts`（色盤、名稱長度）給表單、清單、設定頁共用。

**Tech Stack**：Angular 22.2、Angular Material／CDK 22.2、Vitest 5、Playwright。

- 設計文件：[`docs/superpowers/specs/2026-10-19-tags-frontend-design.md`](../specs/2026-10-19-tags-frontend-design.md)（F 編號）
- 後端計畫：[`2026-10-19-tags-backend.md`](2026-10-19-tags-backend.md)。**後端 checkpoint 全綠才能開工**；API 形狀以後端 spec 的「介面」一節為準。

---

## 執行前必讀

### 環境

- 工作目錄：`web/`。分支：`claude/cool-newton-xpixf5`（與後端同一條）。
- 全部單元測試：`npx ng test --watch=false`。單一檔案：`npx ng test --watch=false --include src/app/<path>.spec.ts`。
- E2E：`npx playwright test`（mock API，不需要後端）。建置：`npx ng build`。

### 基準線

- 撰寫計畫的環境沒有 `node_modules`，**沒有實測**。開工前跑一次 `ng test` 與 Playwright，把數字記在這裡，之後只增不減。
- **實測（2026-10-19，本地端，HEAD `3a8b59a`）**：`ng test` 39 個檔案、313 個測試全過；`ng build` 成功、無警告；Playwright 10 passed（需先 `npx playwright install chromium`）。

### 絕對不要碰的檔案

- `web/.angular/`、`web/dist/`、`web/test-results/`、`web/node_modules/`；Angular CLI 可能改動 `angular.json` 的 analytics 設定，**不要 commit**。
- 每個 Task 都用明確的路徑 `git add`，**禁止 `git add .`／`git add -A`**。

### 慣例

- 註解與 commit 訊息用繁體中文；元件一律 `ChangeDetectionStrategy.OnPush`，狀態用 signal；測試名稱用 snake_case 英文（比照既有 spec）。
- 對話框只回傳結果，由頁面送出請求（比照 `SettingDialog`）。
- 每個 Task 至少做一次變異檢查；一個 Task 一個 commit。

---

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `web/src/app/shared/tags.ts`／`.spec.ts` | `TAG_COLORS`、`TAG_COLOR_LABELS`、`textLength`、`nextTagColor`、`sortTags` |
| `web/src/app/shared/tag-chip.ts`／`.spec.ts` | 標籤 chip |
| `web/e2e/tags.spec.ts` | 設定標籤 → 記帳掛標籤 → 篩選 |

### 修改

| 檔案 | 改動 |
|---|---|
| `web/src/styles.scss` | `--tag-<color>-bg`／`-fg` token |
| `web/src/app/core/api/dto.ts` | `TagColor`、`TagDto`、`AddTagBody`、`BookDto.tags`、`TransactionDto.tagIds`、`TransactionInput.tagIds`、`SettingPath` 加 `'tags'` |
| `web/src/app/core/api/book-api.ts`／`.spec.ts` | `add(…, 'tags', AddTagBody)` overload、`updateTag` |
| `web/src/app/core/api/transaction-api.ts`／`.spec.ts` | `list(bookId, budgetMonth, tagId?)` |
| `web/src/app/features/settings/settings-rules.ts`／`.spec.ts` | `SettingRow.color`、`tagGroup()` |
| `web/src/app/features/settings/setting-list.ts`／`.spec.ts` | 色點 |
| `web/src/app/features/settings/setting-dialog.ts`／`.spec.ts` | `path: 'tags'` 的名稱＋顏色 |
| `web/src/app/features/settings/settings.page.*` | 「標籤」分頁與 `onAdd`／`onEdit` 的 `'tags'` 分支 |
| `web/src/app/features/transactions/transaction-rules.ts`／`.spec.ts` | `TransactionFormValue.tagIds`、`fromTransaction`、`toTransactionInput`、`keepTagIdsOf` |
| `web/src/app/features/transactions/transaction-form.*` | 標籤欄位 |
| `web/src/app/features/transactions/transaction-list.*` | 備註欄內的 chip |
| `web/src/app/features/transactions/transactions.page.*` | 標籤篩選與 query param `tag` |
| `web/e2e/fixtures.ts` | `BOOK.tags`、交易的 `tagIds` |

### Task 相依順序

```
Task 1 (dto/api) ─┬─→ Task 3 (設定頁) ─────────────────────────────┐
Task 2 (tags.ts/TagChip) ─┤                                         ├─→ Task 6 (e2e) ─→ checkpoint
                  └─→ Task 4 (交易表單) ─→ Task 5 (清單與篩選) ───┘
```

Task 1 與 Task 2 互相獨立；Task 3 與 Task 4 互相獨立。

---

## Task 1: 型別與 API

**Files:** Modify: `dto.ts`、`book-api.ts`／`.spec.ts`、`transaction-api.ts`／`.spec.ts`、`e2e/fixtures.ts`

- [ ] Step 1：失敗測試：`BookApi.add(bookId, 'tags', { name, color })` 打 `POST /api/books/b1/tags`；`updateTag` 打 `PUT /api/books/b1/tags/{id}`，body `{ name, color }`；`archive`／`remove`／`reorder` 用 `'tags'` 時的 URL；`TransactionApi.list(b1, 202610, 't1')` 的 params 同時有 `budgetMonth` 與 `tagId`，沒給 `tagId` 時**不帶**這個 param。
- [ ] Step 2：`dto.ts`：

```ts
export type TagColor =
  | 'Red' | 'Orange' | 'Amber' | 'Yellow' | 'Lime' | 'Green' | 'Teal' | 'Cyan' | 'Blue' | 'Indigo' | 'Purple' | 'Pink';
export interface TagDto { id: string; name: string; color: TagColor; sortOrder: number; archivedAt: string | null }
export interface AddTagBody { name: string; color: TagColor }
export type SettingPath = 'accounts' | 'planning-funds' | 'categories' | 'tags';
// BookDto 加 tags: TagDto[]；TransactionDto 加 tagIds: string[]；TransactionInput 加 tagIds: string[]（F11、後端 D4：一律送出）
```

- [ ] Step 3：修正所有因為新增必填欄位而編譯失敗的測試資料（`BOOK` fixture、各 spec 的 `TransactionDto` 工廠，例如 `features/transactions/testing/`），補上 `tags: []`／`tagIds: []`。
- [ ] Step 4：全綠；Commit `feat(web): 標籤的 DTO 與 API 方法`。

---

## Task 2: 色盤、名稱長度與 `TagChip`

**Files:** Create: `shared/tags.ts`／`.spec.ts`、`shared/tag-chip.ts`／`.spec.ts`／Modify: `styles.scss`

- [ ] Step 1：失敗測試：
  - `textLength('日本旅遊')` = 4、`textLength(' ✈️日本 ')` = 3、`textLength('👨‍👩‍👧')` = 1（F4，與後端 `StringInfo` 一致）。
  - `nextTagColor([])` = `'Red'`；已用 Red、Orange → `'Amber'`；12 色都用過 → `'Red'`（F3）。
  - `sortTags(tags, ids)` 依 `sortOrder` 回傳 ids 對應的 `TagDto`，找不到的 id 略過。
  - `TagChip`：顯示名稱、套用 `--tag-<color>-bg`／`-fg`；`archivedAt` 有值時有 `archived` class 與 `title="（已封存）"`。
- [ ] Step 2：實作。`styles.scss` 在 `:root` 加 12 組 token；色碼挑選時以 WebAIM contrast checker 確認 fg/bg ≥ 4.5:1，結果寫成註解（比照既有 `--ledger-*` 的寫法）。
- [ ] Step 3：全綠；Commit `feat(web): 標籤色盤與 TagChip`。

---

## Task 3: 設定頁「標籤」分頁

**Files:** Modify: `settings-rules.ts`／`.spec.ts`、`setting-list.ts`／`.spec.ts`、`setting-dialog.ts`／`.spec.ts`、`settings.page.ts`／`.html`／`.spec.ts`

- [ ] Step 1：失敗測試：
  - `tagGroup(tags)`：`key: 'tags'`、`path: 'tags'`、rows 依 sortOrder，`row.color` 有值、`detail` 為顏色中文名稱。
  - `SettingList`：row 有 `color` 時名稱前有色點。
  - `SettingDialog`（`path: 'tags'`）：只有名稱與顏色；9 個字時顯示「最多 8 個字」且不能送出；顏色 radio group 有 12 個選項、以方向鍵可切換；新增時預設為 `data.defaultColor`；`afterClosed()` 回傳 `{ name, color }`。
  - `SettingsPage`：出現「標籤」分頁（在「財務規劃帳戶」與「鎖帳日與資料」之間）；新增時以 `nextTagColor(book.tags.map(t => t.color))` 當預設色；送出呼叫 `add(…, 'tags', …)`；修改呼叫 `updateTag`；封存、刪除（含 `in-use` 提示）、排序沿用既有流程（各寫一個測試確認 `'tags'` 路徑正確）。
- [ ] Step 2：實作。`SettingDialogData` 加 `color?: TagColor`、`defaultColor?: TagColor`；`SettingDialogResult` 加 `color?: TagColor`；`TITLES.tags = '標籤'`。名稱欄位在 `path === 'tags'` 時改用 `textLength` 的自訂 validator，**不加** `maxlength`（F4）。
- [ ] Step 3：全綠；變異檢查：把 `onEdit` 的 `'tags'` 分支改成呼叫 `updatePlanningFund`，確認測試變紅。
- [ ] Step 4：Commit `feat(web): 設定頁可以管理標籤`。

---

## Task 4: 交易表單的標籤欄位

**Files:** Modify: `transaction-rules.ts`／`.spec.ts`、`transaction-form.ts`／`.html`／`.scss`／`.spec.ts`

- [ ] Step 1：失敗測試：
  - `fromTransaction(dto)` 帶出 `tagIds`；`toTransactionInput(value)` **一律**有 `tagIds`（空陣列也要送，F7）。
  - `keepTagIdsOf(editing)`：新增時為空集合，修改時為原交易的 `tagIds`。
  - 表單：每種交易類型都看得到「標籤」欄位；選項是未封存的標籤依 sortOrder，加上編輯中交易原本掛著的封存標籤；選了 4 個之後其他選項 disabled；trigger 顯示 `TagChip`；切換交易類型時保留已選的標籤。
  - 送出遇到 422（例如後端回「標籤已封存」）時顯示後端訊息（沿用既有 `domain` 錯誤處理，F10）。
- [ ] Step 2：實作。表單控制項 `tagIds: string[]`，位置在「備註」下方；選項 `computed(() => sortTags(selectable(book().tags, keepTagIds()), …))`。
- [ ] Step 3：全綠；Commit `feat(web): 記帳時可選最多 4 個標籤`。

---

## Task 5: 交易清單的 chip 與標籤篩選

**Files:** Modify: `transaction-list.ts`／`.html`／`.scss`／`.spec.ts`、`transactions.page.ts`／`.html`／`.spec.ts`

- [ ] Step 1：失敗測試：
  - 清單：有標籤的交易在備註欄顯示 chip（依標籤 sortOrder），在備註文字之前；封存標籤也顯示（F9）。
  - 交易頁：URL `?month=202610&tag=t1` 時，下拉選單選中 t1，並以 `list(b1, 202610, 't1')` 載入；選「全部標籤」時 query param 移除 `tag`；換月份時保留 `tag`（F8）。
  - 篩選後清單為空時顯示「這個月沒有掛著「X」的交易」。
  - 下拉選項包含已封存的標籤（才篩得到舊交易）。
- [ ] Step 2：實作。`transactions.page.ts` 現在由 `queryParamMap` 推導月份，以相同方式推導 `tagId`，與 `reload` 一起組成載入的 key。
- [ ] Step 3：全綠；在 375px 寬度（DevTools）確認清單不出現水平捲軸；Commit `feat(web): 交易清單顯示標籤並可依標籤篩選`。

---

## Task 6: E2E

**Files:** Create: `web/e2e/tags.spec.ts`／Modify: `web/e2e/fixtures.ts`

- [ ] Step 1：情境（mock API，比照 `settings.spec.ts` 攔截寫入請求並斷言 body）：
  1. 設定頁「標籤」分頁新增「旅遊」（選 Teal）→ 斷言 `POST /tags` body 為 `{ name: '旅遊', color: 'Teal' }`。
  2. 記帳頁新增一筆支出，選「旅遊」→ 斷言 `POST /transactions` body 的 `tagIds`。
  3. 清單看到「旅遊」chip；選篩選「旅遊」→ 斷言 `GET /transactions` 帶 `tagId`，URL 有 `tag=`。
- [ ] Step 2：全綠；Commit `test(web): 標籤的 e2e`。

---

## 前端 checkpoint

- [ ] `npx ng test --watch=false`、`npx playwright test`、`npx ng build` 全綠，數字 ≥ 基準線。
- [ ] 接真實後端手動驗證：建立 3 個標籤並排序 → 記一筆掛 2 個標籤的轉帳 → 篩選 → 封存其中一個標籤後打開那筆交易，封存標籤仍在、選單裡沒有它 → 刪除一個被用過的標籤，看到改用封存的提示 → 下載 CSV 看到標籤欄。
- [ ] 手機寬度（375px）走一次設定頁與記帳頁。

### 實作紀錄（2026-10-19，本地端）

- Task 1–6 各一個 commit（`eb46172`…`a2008e7`）；Task 2–5 都做了變異檢查並確認變紅（`textLength` 改用 `string.length`、`onEdit` 的標籤分支改呼叫 `updatePlanningFund`、表單選項拿掉 keep、清單載入不帶 `tagId`）。
- 與 plan 不同之處：
  - Task 1 為了讓型別過關，`toTransactionInput` 先暫時一律送 `tagIds: []`，Task 4 才接上表單值。
  - 共用的 `BOOK` fixture（`features/transactions/testing/book-fixture.ts`）在 Task 3 加了三個標籤（排序與陣列順序不同、一個封存），Task 3–5 共用。
  - 顏色色塊用原生 `<input type="radio">`，方向鍵切換由瀏覽器提供；jsdom 不模擬方向鍵，**沒有**單元測試這一點，只測了 12 個選項、`aria-label`、預設值與點選。
  - 篩選下拉裡的封存標籤顯示為「名稱（已封存）」，與未封存的區分。
  - 375px 的檢查寫成 e2e（`tags.spec.ts` 的 narrow viewport），取代 DevTools 手動檢查。
  - 色盤每色的對比值以 WCAG 公式計算，寫在 `styles.scss` 註解（fg 對 bg 皆 ≥ 6.3:1）。
- Checkpoint：`ng test` 347 個全過（基準線 313，+34）；Playwright 13 passed（基準線 10，+3）；`ng build` 成功、無警告。
- 接真實後端手動驗證（2026-10-19）：API 跑在 `https://localhost:5001`，`ng serve` 跑在 4300（用 `dotnet dev-certs` 匯出的 PEM 當 `--ssl-cert`／`--ssl-key`，內建瀏覽器才信任）。使用者登入 Google 後，以內建瀏覽器操作。下列都符合預期：
  - 建立三個標籤：新增時預設色依序是「紅」「橙」（第一個沒用過的顏色）。色塊可用方向鍵切換。用 ▲ 排序後，後端的 SortOrder 也跟著改。
  - 記一筆掛兩個標籤的轉帳：選項依設定排序，trigger 顯示 chip。清單備註欄依設定排序顯示 chip。
  - 篩選：URL 帶 `tag=`，只列出掛著該標籤的交易。
  - 封存「工作」後：新增交易的選項沒有它；那筆轉帳的清單 chip 淡化並有 title；打開編輯時仍保留，改備註後存檔成功，標籤不變。
  - 刪除被用過的標籤：出現「改用封存」提示。
  - CSV 的「標籤」欄為「旅遊、工作」。
  - 375px 下，記帳頁與設定頁都沒有水平捲動。
- 手動驗證發現並修正：交易頁的標籤篩選在沒選標籤時是空白，因為 `mat-select` 把 `null` 當成未選取。改用 `placeholder="全部標籤"`，並加上單元測試。
- 手動驗證發現、**未修正**：
  - 375px 下，設定頁標籤清單的名稱會逐字換行（色點加上 ▲▼⋮ 擠壓名稱欄）。
  - 刪除被用過的標籤時，後端訊息沿用共用的「已有交易或預定支出使用」，但標籤只會被交易使用。
  - 記帳頁連續記帳存檔後，必填欄位立即標紅。這是表單 submitted 狀態沿用的既有行為，與標籤無關。
