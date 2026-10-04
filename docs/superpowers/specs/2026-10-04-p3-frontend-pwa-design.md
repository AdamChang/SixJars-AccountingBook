# P3 前端：Angular PWA（記帳與總覽）設計

- 日期：2026-10-04
- 範圍：**前端**（Angular PWA），加上前端必須依賴的**後端前置修正**（§8）。兩者拆成兩份 plan（tddplan 慣例：前後端的基準線與測試指令不混在一起）。
- 依據：[CONTEXT.md](../../../CONTEXT.md)、[ADR 0005](../../adr/0005-bff-cookie-auth-with-book-member-whitelist.md)、[P2 設計](2026-10-04-p2-backend-api-design.md)、P2 計畫的「後續」清單
- 狀態：**草稿，待使用者審閱**

> **階段命名**：P1、P2 文件中的「P3 以後」指的是預算、報表等後續功能。本文件把前端定為 **P3**，那些功能順延到 P3 之後，內容不變。

## 1. 目標

讓擁有者用瀏覽器（桌機優先）登入後記帳並查看餘額，取代 Excel 的日常記帳。完成時：

1. 以 Google 帳號登入（BFF cookie，ADR 0005）；不在白名單時看到清楚的說明頁，而不是一段 JSON。
2. **記帳頁**：以鍵盤快速連續記帳；可以檢視、修改、刪除當月（歸屬月份）的交易；鎖帳日之前的交易不可操作。
3. **總覽頁**：某個歸屬月份的月可用餘額、年累計餘額、可用現金（含與不含電子錢包），以及各帳戶與財務規劃帳戶的餘額。
4. 前端由同一個 Cloud Run 服務提供（Api 的 `wwwroot`），同源、不需要 CORS；可安裝為 PWA，但**不支援離線**。

### 1.1 使用者已確認的決定（2026-10-04）

| 項目 | 決定 | 被否決的選項 |
|---|---|---|
| 範圍 | 只做登入、記帳、總覽 | 完整對應 P2 API；核心＋預定支出 |
| 裝置 | **桌機優先**；手機能看、能用，不另外設計版面 | 手機優先；兩者並重 |
| 元件庫 | **Angular Material** | PrimeNG（新 major 常延遲支援、沒有官方 test harness）；只用 CDK（日期選擇器、對話框都要自己做） |
| 前端狀態 | **standalone components＋signals＋薄 API service** | NgRx SignalStore、NgRx Store（4 個畫面撐不起這層抽象） |
| validation 錯誤的 key | **由後端提供 camelCase 且不帶 `input.` 前綴的 key**。事實查核時發現 P2 已經實作（`46e7b20`，`ApiExceptionHandler.ToBodyFieldName`，新增與修改都有測試），不需要再修正 | 前端自己轉換（前端會依賴後端 command 的內部結構） |
| Node／Angular 版本 | **使用者把 Node 升到 ≥ 24.15.0，使用 Angular 22**（事實查核：Angular 22 CLI 要求 `^22.22.3 \|\| ^24.15.0 \|\| >=26.0.0`，本機原本是 24.13.1） | Angular 21.2（已進入 LTS，新專案一開始就落後一個 major） |
| 本機登入 | **真的 Google OAuth client**（localhost 專用） | 後端加開發用的假登入（一個要提防的後門，也驗證不到真正的登入流程） |
| 測試 | **Vitest＋少量 Playwright**（API 以 `page.route` 模擬） | 只用 Vitest；Playwright 接真後端（需要測試專用的登入捷徑） |

## 2. 架構

### 2.1 技術與版本

- Angular **22.x**（2026-10-04 的最新穩定版為 22.2.1，符合「21 以上」），Angular Material 同一版，Node **≥ 24.15.0**（Angular 22 CLI 的最低需求）。
- 單元與元件測試用 Vitest（Angular CLI 的預設 test runner），E2E 用 Playwright。
- 實際版本在事實查核時以 `package.json` 與 lock file 為準。

### 2.2 目錄

前端放在 repo 根目錄的 `web/`，與 `src/`（.NET）平行。`dotnet build`／`dotnet test` 不需要 Node。

```
web/src/app/
  core/            跨畫面共用：只放 root service、interceptor、guard
    api/           每個 API 資源一個薄 service（SessionApi、BookApi、TransactionApi、SummaryApi）＋ DTO 型別
    auth/          SessionService（/api/me 快取、登入導向、登出）、authGuard
    book/          CurrentBook（目前帳本的 BookDto 快取，表單的下拉選單用）
    errors/        ProblemDetails 分類（ApiError）、錯誤 interceptor、全域 snackbar
  features/
    summary/       總覽頁
    transactions/  記帳頁（表單＋當月列表）、欄位與金額規則的純函式
  shared/          格式化（金額、歸屬月份 202601 ↔ 2026/01）
```

- standalone components，不使用 NgModule。
- 跨畫面共用的狀態只有兩個：登入者（`/api/me`）與目前帳本的設定（`BookDto`），各由一個 root service 以 signal 快取。其餘狀態留在 component。
- **DTO 型別手寫**，對照後端的 record。否決 OpenAPI codegen：後端目前沒有產生 OpenAPI 文件，本期只用到約 6 個 DTO，codegen 的工具鏈成本比手寫高。P3 之後 API 變多時再評估。
- JSON 的列舉是字串（後端設定了 `JsonStringEnumConverter`），TypeScript 用字串 union 型別表示。

### 2.3 本機開發

- 後端：`dotnet run --project src/SixJars.Api`，照 `docs/deploy.md` §5 的方式以環境變數設定，`ASPNETCORE_URLS=https://localhost:5001`（HTTPS，Development）。
- 前端：`ng serve --ssl`，port **4300**，透過 proxy 把 `/api`、`/auth` 轉給 `https://localhost:5001`。瀏覽器只看到 `https://localhost:4300` 一個 origin，cookie 與 XSRF 的行為和正式環境相同。
- Google OAuth client（localhost 專用）的 redirect URI 是 `https://localhost:4300/auth/callback`。client id 與 secret 只放在本機 PowerShell session 的環境變數，不進 repo。
- **事實查核（2026-10-04 實測）**：
  - 本機 Windows 把 TCP 4150–4249 保留給 Hyper-V／WinNAT（`netsh interface ipv4 show excludedportrange protocol=tcp`），`ng serve` 在 4200 會 `EACCES`。使用者決定改用 4300，並已在 Google client 加上對應的 redirect URI。這類保留是動態的，哪天 4300 也被保留時，以系統管理員執行 `netsh int ipv4 add excludedportrange protocol=tcp startport=4300 numberofports=1` 把它固定保留給自己。
  - Angular 22 的 dev-server proxy **保留瀏覽器的 `Host`**（後端收到 `localhost:4300`），不送 `X-Forwarded-*`。proxy 的目標是後端的 **https** endpoint，後端看到的 scheme 本來就是 https，所以 OIDC 的 `redirect_uri` 會是 `https://localhost:4300/auth/callback`，不需要 forwarded header。
  - proxy 設定檔改了要重啟 `ng serve` 才生效（不會熱重載）。

### 2.4 正式建置

- Dockerfile 加一個 `node` build stage 執行 `ng build`，把輸出（`dist/web/browser`）複製到 runtime image 的 `/app/wwwroot`。
- **否決：在 Api 的 csproj 加 MSBuild target 自動執行 npm。** 每次 `dotnet build`、`dotnet test` 都要等 Node，後端測試也會因此依賴 Node。
- Dockerfile 只複製 `web/` 需要的檔案；`.dockerignore` 排除 `web/node_modules`、`web/dist`。`reference/` 的排除規則不變，image 內不可以有個資（`deploy.md` 2.3 的檢查照舊）。

### 2.5 PWA

- 用 `@angular/pwa` 提供 manifest 與 service worker，**只快取 app shell**（靜態資源）。
- `/api/**`、`/auth/**` 一律不經過 service worker，資料永遠即時，這就是「不支援離線」的具體意義。
- **陷阱**：service worker 預設會把導覽請求導回快取的 `index.html`。`/auth/login`、`/auth/callback`、`/auth/logout` 必須在 `ngsw-config.json` 的 `navigationUrls` 中排除，否則登入流程會被攔截。這點要寫進手動驗證清單。

## 3. 路由與登入

| 路徑 | 畫面 |
|---|---|
| `/` | 讀 `/api/me` 後分流：只有 1 本帳本時，直接進該帳本的記帳頁；多本時顯示選擇清單；0 本時顯示「沒有可存取的帳本」 |
| `/books/:bookId/transactions?month=YYYYMM` | **記帳頁**（落地頁）。省略 `month` 時用今天所在的月份 |
| `/books/:bookId/summary?month=YYYYMM` | **總覽頁** |
| `/denied` | 「登入未完成：可能是此 Google 帳號不在白名單，或登入時取消了授權」，附上「重新登入」的連結（§8 第 3 項） |

- 版面：桌機上方是 app bar，顯示帳本名稱、「記帳／總覽」切換、登入的 email 與登出。寬度不足時同一套版面縱向排列。
- **月份放在 query string**，重新整理或加書籤都會回到同一個月。
- `authGuard` 呼叫 `/api/me`（由 SessionService 快取）。收到 401 時整頁導向 `/auth/login?returnUrl=<目前路徑>`，不在 SPA 裡做登入畫面。
- 登入後 SessionService 呼叫一次 `GET /api/antiforgery/token`。之後非 GET 請求由 Angular `HttpClient` 內建的 XSRF 支援，自動讀取 `XSRF-TOKEN` cookie 並帶上 `X-XSRF-TOKEN` header。
- 登出：`POST /auth/logout`（需要 XSRF token），成功後整頁導向 `/`，也就是重新走一次登入流程。

## 4. 記帳頁

表單在上、當月列表在下。記完一筆列表就會更新，不用切換畫面。

### 4.1 表單

- **欄位順序**：日期 → 交易類型 → 帳戶 → 金額 → 分類 → 備註。Tab 依序移動，**Enter 送出**。例外：分類的 autocomplete 選單展開時，Enter 是選取選項，不送出；選單收起後再按 Enter 才送出。
- **送出成功後**：保留日期、交易類型、帳戶，清空其他欄位，焦點移回金額。這是為了連續記同一天、同一帳戶的多筆消費。
- **交易類型**：「支出、收入、轉帳」直接顯示成按鈕組，其餘 9 種（提款、現金存入、加值、繳卡費、新增貸款、貸款繳款、入新資金、出資金、資金回流）收在「更多」選單。
- **各交易類型會用到的欄位**照 P2 計畫 Task 20 的欄位表。切換類型時只顯示會用到的欄位；隱藏的欄位送出時一律是 `null`，因為後端的 validator 要求這些欄位必須是 null。
- **帳戶下拉依交易類型篩選**，例如支出不會列出貸款帳戶，繳卡費的對方帳戶只列信用卡。
  - 這是照抄 Domain（`TransactionFactory.RequireAccount`）規則的 **UX 篩選**，後端的 422 仍然是唯一的判定依據。
  - 代價是規則有兩份；換來的是不會出現「選了才被拒絕」的挫折。規則集中在一個純函式，並以表格驅動的測試鎖住。
- **金額**：使用者一律輸入正數，最多 2 位小數。
  - 支出送 `-金額`，收入送 `+金額`。勾選「退款／沖回」時正負號反過來（Domain 規定收入與支出的金額是帶正負號的非 0 值）。
  - 其他交易類型都送正數。
  - 貸款繳款要填「總額」與「本金」，利息即時顯示為 `總額 − 本金`（唯讀）。
- **分類**：用一個 autocomplete，選項顯示為「主分類 / 子分類」，可以打字篩選，並依交易類型只列收入或支出分類。比兩層下拉快，也適合鍵盤操作。
- **歸屬月份**：預設是日期所在的月份，收在「進階」區塊裡，需要時才展開修改。只有使用者改過時才送出 `budgetMonth`，否則送 `null`，由後端決定。
- **日期**：預設為瀏覽器時間的「今天」。

### 4.2 列表

- 依**歸屬月份**篩選（`GET /transactions?budgetMonth=`），與月可用餘額一致；用 ◀ ▶ 切換月份。
- 排序照後端（日期，再依建立順序）。
- 欄位：日期、交易類型、帳戶（轉帳類顯示「從 → 到」）、分類（「主 / 子」）、金額、備註。
- **修改**：點列表的一列，資料會載入上方表單，進入「編輯中」狀態（顯示標示與「取消」按鈕）。送出時用 `PUT`，並帶上 `version`。
- **刪除**：每一列都有刪除按鈕，按下後跳確認視窗，送出 `DELETE ?version=`。
- **鎖帳**：日期 ≤ 鎖帳日的列顯示鎖頭，修改與刪除都停用。這只是 UX，後端的 422 `locked` 仍是判定依據。

### 4.3 刻意不做

- 在記帳頁顯示總覽數字：每記一筆就多一次 `/summary`（約 6 次跨國的 DB 往返）。
- 記帳後的「復原」：刪除就能達到同樣效果。
- 分頁：個人帳本一個月約 200 筆以內，P2 已決定不做分頁。

## 5. 總覽頁

- 可以切換月份（`GET /summary?budgetMonth=`）；`asOf` 省略，由後端用今天（台北時間）。
- 三張數字卡：月可用餘額、年累計餘額、可用現金。可用現金同時列出含與不含電子錢包的數字。
- 帳戶餘額依帳戶類型分組（現金、銀行、電子錢包、信用卡、貸款）。`BalanceDto` 沒有帳戶類型，所以用 `CurrentBook` 的帳戶資料補上；信用卡與貸款是負債，以負數顯示。
- 財務規劃帳戶餘額另列一張表。

## 6. 錯誤處理

原則：**由 interceptor 統一分類，畫面只處理和自己有關的錯誤**。寫入失敗時，表單內容一律保留。

| 回應 | 處理 |
|---|---|
| 400 `ValidationProblemDetails` | 依 `errors` 的 key（camelCase 的 body 欄位名，例如 `counterAccountId`）設定對應表單欄位的 server error，顯示在欄位下方。對不到可見欄位的 key，顯示在表單頂端，不默默吞掉。 |
| 422（`code`） | 事實查核：`DomainException` 只有兩種 code，`rule`（一般業務規則）與 `locked`（鎖帳），`detail` 已是中文訊息。`locked` 顯示「此日期已鎖帳，不能新增、修改或刪除」；其他 code 一律顯示後端的 `detail`。 |
| 409 | 「這筆交易已在其他裝置修改」：結束編輯狀態並重新載入列表，由使用者看過最新內容後再改。不做自動合併。 |
| 404 | 操作列表時：「這筆交易已不存在」，重新載入列表。帳本本身 404 時回到 `/`。 |
| 401 | interceptor 整頁導向 `/auth/login?returnUrl=…`。尚未送出的內容會遺失；cookie 是 14 天滑動期限，日常使用碰不到，所以接受這個代價。 |
| 400（antiforgery 失敗） | 登入後就會先取得 token，正常不會發生。發生時提示「請重新整理頁面」，不自動重試。如何與 validation 的 400 區分，事實查核時確認回應格式。 |
| 網路錯誤、5xx | snackbar「暫時無法連線，請稍後再試」。**寫入不自動重試**，避免重複記帳。 |

- **防止重複送出**：請求進行中時停用送出按鈕與 Enter。
- **冷啟動**：Cloud Run 與 Neon 閒置後各要冷啟動一次（ADR 0007），第一個請求可能要好幾秒。請求超過 300ms 才顯示頂部進度條；不設 timeout。

### 6.1 兩個要用測試鎖住的陷阱

1. **日期與時區**：Material datepicker 給的是 `Date` 物件。轉成 `yyyy-MM-dd` 時，**必須用本地時間的年月日組字串，不能用 `toISOString()`**。台灣是 UTC+8，`toISOString()` 會把午夜轉成前一天，在 00:00–08:00 之間記帳就會記到前一天。「今天」的預設值也一樣。測試執行時固定 `TZ=Asia/Taipei`，並以 00:30 這類時間驗證。
2. **金額**：後端的 `decimal` 在 JSON 是 number。新台幣金額遠小於 2^53，JS number 不會失真。顯示用 `Intl.NumberFormat('zh-TW')`。

## 7. 測試策略

| 層 | 工具 | 測什麼 |
|---|---|---|
| 純函式 | Vitest | 各交易類型會用到的欄位、金額正負號、帳戶類型篩選、日期轉字串、歸屬月份格式。用表格驅動，案例取自 P2 Task 20 的欄位表與 Domain 的 `RequireAccount`。覆蓋率要求最高。 |
| service、interceptor | Vitest＋`HttpTestingController` | URL、query string、body 形狀（隱藏欄位送 `null`、支出送負數）、401 導向、錯誤分類 |
| component | Vitest＋TestBed＋Material harness | 切換類型時欄位顯示或隱藏、送出後保留的欄位與焦點、400 錯誤對回欄位、鎖帳的列停用、總覽的分組 |
| E2E（3–5 個） | Playwright＋`page.route` 模擬 API | 只測 jsdom 測不到的部分：真實鍵盤連續記帳（Tab、Enter、焦點回到金額）、401 時整頁導向 `/auth/login`、422 `locked` 的訊息 |

- 測試執行時固定 `TZ=Asia/Taipei`。
- **已知風險：手寫的 DTO 與後端不同步。** 對策：每段 checkpoint 都在本機接真的後端做手動驗證。否決「後端測試輸出 JSON 樣本給前端測試讀取」：兩套測試會互相綁住，以約 6 個 DTO 的規模不值得。
- 後端的測試數量不可低於前置修正完成後的基準線。

## 8. 後端前置修正（另一份 plan，先做）

這些修正都是因為前端而需要，所以設計寫在這裡；plan 獨立成一份，照 P2 的慣例執行（TDD、一個修正一個 commit、`dotnet test` 全綠）。

> 原本的第 1 項「validation 錯誤的 key 改成 camelCase 並拿掉 `input.`」：事實查核時發現 P2 已經完成（`46e7b20`），刪除。

1. **靜態檔與 SPA fallback**：`UseStaticFiles` 加上 fallback 到 `index.html`，但**只限不屬於 `/api`、`/auth`、`/health` 的路徑**。
   - 要用測試鎖住：打錯的 API 路徑（例如 `/api/nope`）仍回 404，不能回 `index.html` 加 200。否則前端拿到 HTML 會解析失敗，問題很難查。
   - `wwwroot` 不存在時（例如只跑 API 測試、或沒有 build 前端）不可以讓 app 啟動失敗。
2. **快取 header**：`index.html` 設 `Cache-Control: no-cache`；檔名帶 hash 的 js／css 設長期快取（`immutable`）。這樣部署新版後，使用者不會一直拿到舊的 `index.html`。
3. **登入失敗改為轉址到 SPA 的 `/denied`**：OIDC 的 `OnRemoteFailure`（白名單拒絕、使用者在 Google 取消同意）目前轉址到 `/auth/denied`，回 403 的 ProblemDetails JSON，使用者會直接看到一段 JSON。改為轉址到 `/denied`，並刪除 `/auth/denied` endpoint。因為原因可能是被拒絕，也可能是使用者自己取消，頁面文案寫成「登入未完成：可能是此 Google 帳號不在白名單，或登入時取消了授權」。

## 9. 分段

延續 P1 的 A–B、P2 的 C–F 命名。

| 段 | 內容 | checkpoint 時可交付的成果 |
|---|---|---|
| **後端前置** | §8 的 3 項 | 後端全綠，數量 ≥ 345 |
| **G** | 建立 Angular 專案（Material、Vitest、Playwright）、proxy 與 `ng serve --ssl`、API service 與 DTO、SessionService／authGuard／interceptor、錯誤分類、app shell 與路由（`/`、`/denied`） | 在本機用真的 Google 帳號登入，看到 app shell 與帳本名稱。同時完成 P2 交接文件留下的「Google 登入手動驗證」 |
| **H** | 記帳頁：先做純函式，再做表單、列表、修改、刪除、鎖帳 | 在本機可以完全用前端記帳，並和 CLI 匯入的真實資料對得上 |
| **I** | 總覽頁、PWA（manifest、ngsw 排除 `/auth`）、Dockerfile 的 Node stage、Playwright E2E、更新 `docs/deploy.md` | 可以部署：image 內含前端，`deploy.md` 第 2 節的檢查重跑全部通過 |

每段結束時：前端與後端都全綠、一個 Task 一個 commit，然後停下來讓使用者檢視，並用 `docs(plans):` 回寫偏差。部署與 push 由使用者執行。

## 10. 明確排除（不在 P3）

- 預定支出（含付款）、帳本設定（新增帳戶、分類、財務規劃帳戶，鎖帳日設定）、交易明細匯出與備份下載、稽核歷史。這些功能的 API 都已完成，留待下一期的前端。CLI 沒有對應的指令，所以期間要新增帳戶、分類或設定鎖帳日，只能在登入狀態下直接呼叫 API，或者暫緩。
- 離線記帳、背景同步、推播通知。
- 多帳本管理（建立、切換以外的操作），記帳者與唯讀角色。
- i18n（只做繁體中文）、深色模式、手機專屬版面。
- OpenAPI 與 DTO codegen。
- P2 spec §12 已排除的項目維持排除。
