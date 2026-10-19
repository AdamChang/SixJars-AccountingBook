# 在 Web 建立帳本（前端）設計

- 需求與 grilling 結論：GitHub issue [AdamChang/SixJars-AccountingBook#7](https://github.com/AdamChang/SixJars-AccountingBook/issues/7) 的五則決定留言（2026-10-19）
- 後端設計：[`2026-10-19-create-book-backend-design.md`](2026-10-19-create-book-backend-design.md)。本設計依賴它的 `POST /api/books` 與 `/api/me` 新欄位
- 相關：#8（刪除最後一本帳本的警告，5842d76）

## 目標

建立者可以從兩個地方建立空白帳本：沒有帳本時的首頁，以及工具列的帳本選單。建立後直接進到新帳本的設定頁帳戶分頁，開始新增帳戶。工具列的帳本選單同時補上「切換帳本」：目前多本帳本的人在 UI 上沒有切換的入口。

## 範圍

**包含**

- `MeDto` 新增 `canCreateBooks`、`remainingBookQuota`；`BookApi.create`。
- `classifyError` 新增 `forbidden`（403）。
- 建立帳本對話框，以及共用的建立流程。
- 工具列帳本名稱改成選單：切換帳本＋「新增帳本…」。
- 首頁空狀態：建立者看到「建立帳本」。
- 設定頁 `?tab=accounts` deep link。
- 交易頁在帳本沒有帳戶時的提示。
- #8 的警告條件加上 `!canCreateBooks`。
- `/denied` 的說明文字。

**排除**

- 匯入 Excel、還原備份、帳本範本（後端 spec 的排除項目）。
- 成員管理 UI。

## 決策

| # | 決定 | 理由 | 被否決的選項 |
|---|---|---|---|
| Q23 | 工具列的帳本名稱改成 `mat-menu`：列出 `me.books` 可切換（目前這本標為選取），最後一項「新增帳本…」 | 只有一本帳本時首頁會自動導入，只有一本帳本的建立者需要一個帳本內的入口；同時補上切換帳本 | 放在設定頁（位置不合理）；首頁不再自動導入（每次多點一下） |
| Q15 | 「新增帳本…」只有 `canCreateBooks` 時才出現；`remainingBookQuota === 0` 時停用，並在選項下方顯示「已達 5 本上限」 | 非建立者看到一個永遠不能用的選項只會困惑；建立者需要知道為什麼不能用 | 名額用完時隱藏（使用者不知道為什麼不見了） |
| Q15 | 首頁空狀態：`canCreateBooks` 時顯示說明＋「建立帳本」按鈕；否則維持 `NO_BOOKS_MESSAGE` | 非建立者仍然只能找管理者 | — |
| Q21 | 建立成功後：`session.reload()` → 導向 `/books/{id}/settings?tab=accounts` → 顯示「已建立帳本「名稱」」 | `me.books` 要先包含新帳本，`bookGuard`、工具列選單才看得到它；與 #6 刪除的順序相同、方向相反 | 先導頁再 reload（`bookGuard` 不依賴 `me`，但工具列選單會短暫少一本） |
| F1 | 建立流程放在 `core/book/book-creation.ts` 的 `BookCreation.start()`，首頁與工具列共用 | 兩個入口的對話框、錯誤處理、導頁完全相同 | 各寫一份（重複） |
| F2 | 對話框欄位：名稱（必填，最多 100 字）、開帳日（預設 `toDateString(new Date())`）。對話框不呼叫 API，也不自己關閉：以 `submitted` output 回報輸入，由 `BookCreation` 送出、決定是否關閉，失敗時呼叫對話框的 `showError()`，送出中設定 `busy` | 與 `DeleteBookDialog` 相同的分工（對話框不碰 API）；`toDateString` 已是 app 內的「今天」寫法。不能沿用 `afterClosed()` 回傳結果，因為 F3 要求 422 時對話框不關閉（事實查核時修正） | 對話框自己呼叫 API；`afterClosed()` 回傳輸入（422 時對話框已經關了） |
| F3 | 錯誤：`forbidden` 顯示「你不在建立者名單，無法建立帳本」並 `session.reload()`（名單可能剛被移除）；`domain` 顯示後端訊息、對話框不關，讓使用者改名稱；`validation` 顯示在欄位下 | 名額用完、同名都是使用者可以理解的訊息，後端已經寫好 | 422 時關閉對話框（使用者要重打一次） |
| F4 | `classifyError` 的 403 回傳 `{ kind: 'forbidden' }` | 目前 403 落到 `default`，會被當成 `unavailable`，顯示「暫時無法連線」 | — |
| F5 | 設定頁讀 `?tab=` query param 決定 `mat-tab-group` 的 `selectedIndex`，值為 `categories`／`accounts`／`funds`／`tags`／`data`（依分頁順序；「標籤」分頁在 rebase 到 master 後加入）；不認得的值用第 0 個。換帳本時重新套用（在設定頁建立另一本帳本時，路由會重用同一個 `SettingsPage`），但同一本帳本重新載入（每次寫入後的 `currentBook.reload`）時不重設 | 目前沒有 deep link；只讀不寫，切換分頁不改網址。重新套用的條件是事實查核時補上的：只在建構時讀一次，路由重用時新帳本不會落在帳戶分頁；每次 `book()` 變動都套用，則每次儲存都會把使用者拉回帳戶分頁 | 切換時同步改網址（範圍外，返回鍵行為也會改變） |
| F6 | 交易頁在 `book.accounts` 沒有任何未封存的帳戶時，在表單上方顯示提示與「前往設定」連結（`?tab=accounts`） | 從其他入口進到空帳本的人也知道怎麼開始 | 只在建立後導頁（從其他入口進來時沒有提示） |
| #8 | 刪除對話框的 `isLastBook` 改為 `me.books.length === 1 && !me.canCreateBooks` | 建立者沒有帳本也能登入，不需要警告 | — |
| F7 | `DENIED_MESSAGE` 改為「登入未完成：可能是此 Google 帳號不是任何帳本的成員、也不在建立者名單，或登入時取消了授權」 | 白名單的定義改了，說明要跟著改 | — |

## 畫面

### 工具列帳本選單

```
[ 家庭帳本 ▾ ]  記帳  總覽  預定支出  預算  設定          a@b.c  登出
 ├ ✓ 家庭帳本
 ├   我的帳本
 ├ ──────────
 └   新增帳本…            （名額用完時停用，下方小字「已達 5 本上限」）
```

沒有選取帳本時（首頁），工具列不顯示選單，維持現況。

### 建立帳本對話框

```
建立帳本
  帳本名稱 [__________]
  開帳日   [2026-10-19]
  所有帳戶的期初餘額都以開帳日結束時為準；建立後可以在設定頁新增帳戶。
                                       [取消] [建立]
```

## 對既有程式的影響

| 位置 | 改動 |
|---|---|
| `core/api/dto.ts` | `MeDto` 新增兩個欄位；所有建立 `MeDto` 的測試與 e2e fixture 都要補上 |
| `core/api/book-api.ts` | `create` |
| `core/book/create-book-dialog.ts`、`core/book/book-creation.ts`（新增） | 對話框與共用的建立流程（F1、F2） |
| `core/errors/api-error.ts` | 新增 `forbidden` |
| `app.html`／`app.ts` | 帳本名稱改成選單；`app.spec.ts` 要 mock `BookCreation`（它依賴 `HttpClient`） |
| `features/home/home.page.ts` | 空狀態依 `canCreateBooks` 顯示按鈕（跟著 `me` 變，403 後 reload 時按鈕要消失）；spec 同樣要 mock `BookCreation` |
| `features/settings/settings.page.{ts,html}` | `?tab=` deep link；`isLastBook` 條件。頁面改為注入 `ActivatedRoute`，而 `settings.page.spec.ts` 沒有 `provideRouter`，spec 要提供假的 `ActivatedRoute` |
| `features/transactions/transactions.page.{ts,html}` | 沒有帳戶時的提示 |
| `core/errors/messages.ts` | `DENIED_MESSAGE` |
| `e2e/fixtures.ts` | `me` 預設值補上新欄位 |

## 測試策略

- 單元（Vitest）：`classifyError` 403、`BookCreation` 的成功與各種錯誤、對話框驗證、工具列選單的顯示與停用、首頁空狀態、設定頁 deep link、交易頁提示、`isLastBook` 新條件。
- e2e（Playwright mock API）：從工具列選單建立帳本，送出內容正確，建立後落在設定頁的帳戶分頁。
- 每個 Task 確認 `ng test` 的 exit code 為 0，不只看通過數（教訓：31ce2c9 的 unhandled error）。
