# 刪除帳本（前端）設計

- 需求與 grilling 結論：GitHub issue [AdamChang/SixJars-AccountingBook#6](https://github.com/AdamChang/SixJars-AccountingBook/issues/6)
- 後端設計（API 形狀以此為準）：[`2026-10-19-delete-book-backend-design.md`](2026-10-19-delete-book-backend-design.md)
- **前置條件**：後端計畫已完成（兩個 endpoint 已存在）。前端測試全部用 mock，不依賴後端，但手動驗證需要後端。

## 目標

Owner 在帳本設定頁可以刪除這本帳本：先看到刪除前的統計，可以先下載備份，必須輸入帳本名稱才能刪除；刪除成功後回到首頁。

## 範圍

**包含**：`BookApi` 新增兩個方法、`SessionService.reload()`、`CurrentBook.clear()`、刪除對話框元件、設定頁的入口、一條 Playwright e2e。

**排除**：其他分頁遇到 404 時的特別處理（Q13，沿用現狀）、首頁沒有帳本時提供建立帳本的入口。

## 決策

| # | 決定 | 理由 | 被否決的選項 |
|---|---|---|---|
| Q11 | 入口放在設定頁。設定頁是分頁（tab）結構，沒有「頁面最下方」，所以入口放在既有的「鎖帳日與資料」分頁最下面，新增一個「刪除帳本」區塊，緊接在「下載」區塊之後（F1） | 下載備份就在正上方，使用者會自然看到；首頁只有一本帳本時會自動導向，在首頁放入口會找不到 | 首頁的帳本列表 |
| Q12 | 成功後導回 `/`，由首頁既有邏輯處理 0 本、1 本、多本的情況 | 沿用既有流程 | 另做結果頁 |
| Q5 | 對話框裡放「下載完整備份」連結，使用者可以略過 | 前端強制擋不住 API；伺服器端備份的成本太高 | 強制先下載 |
| F1 | 分頁名稱維持「鎖帳日與資料」 | 刪除帳本也屬於「資料」，改名會影響既有的 spec 與 e2e 選擇器 | 新增「危險區域」分頁（只放一顆按鈕的分頁過於空洞） |
| F2 | 新增 `DeleteBookDialog` 元件：開啟時呼叫 `deletionPreview`，載入期間顯示「載入中」；之後依序顯示帳本名稱、開帳日、鎖帳日（沒有就不顯示）、各類筆數、「下載完整備份」連結，以及名稱輸入框。輸入值與名稱**完全相同**時，「刪除」按鈕才可以按。按下後 `afterClosed()` 回傳輸入的名稱，取消時回傳 `undefined` | 確認、輸入與統計集中在同一個元件，可以單獨測試；比照 `SettingDialog` 由頁面負責送出，對話框只回傳結果 | 擴充共用的 `ConfirmDialog`（它只有一段訊息，會被撐成特例） |
| F3 | 刪除請求由 `SettingsPage` 送出：成功時依序執行 `await SessionService.reload()`、`await router.navigate(['/'])`、`CurrentBook.clear()`，並顯示「已刪除帳本「X」」。`clear()` 必須排在導頁**之後**：設定頁的 `book = computed(() => currentBook.book()!)` 假設帳本一定存在，先清掉的話，頁面在離開前會用 undefined 重新渲染而報錯。失敗時：`domain`（含 `name-mismatch`）顯示後端訊息；`notFound` 視為已經被刪除，走成功流程但不顯示成功訊息 | 首頁在建構時讀取 `me().books`，導頁前必須先重新載入 `me`，否則會看到已刪除的帳本，或在只剩一本時被導回已刪除的帳本。404 代表帳本已經不存在，結果與刪除成功相同 | 用整頁重新整理（`location.assign('/')`）：可行，但會丟掉 SPA 狀態，而且這是頁面層級的副作用，比較難測 |
| F4 | `SessionService.reload()`：清掉 `pending` 後重新 `load()` | `load()` 永久快取第一次的 promise，沒有其他方式可以拿到新的 `me` | 在刪除後手動從 `me` 裡移除這本帳本（狀態由前端自己維護，與後端可能不一致） |
| F5 | `CurrentBook.clear()`：清掉快取與 signal | 避免返回上一頁時 `bookGuard` 用快取放行已刪除的帳本 | 不清（返回鍵會看到已刪除帳本的舊畫面） |
| F6 | `BookApi.delete(bookId, confirmName)` 呼叫 `http.delete(url, { body: { confirmName } })` | 後端 D1：名稱放在 body | — |

## 文案

- 區塊標題「刪除帳本」，說明文字「刪除後，這本帳本的所有交易、預定支出、預算與設定都會永久移除，無法復原。」，按鈕「刪除帳本…」。
- 對話框標題「刪除帳本「X」」，輸入框標籤「輸入帳本名稱以確認」，按鈕「取消」與「永久刪除」。
