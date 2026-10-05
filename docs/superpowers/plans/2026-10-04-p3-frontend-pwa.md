# P3 前端 PWA（記帳與總覽）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `web/` 建立 Angular 22 PWA，讓擁有者以 Google 帳號登入後用鍵盤快速記帳、修改與刪除當月交易，並查看月度總覽；由 Api 的 `wwwroot` 同源提供。

**Architecture:** standalone components＋signals。跨畫面狀態只有兩個 root service（`SessionService` 快取 `/api/me`、`CurrentBook` 快取 `BookDto`），其餘狀態留在 component。HTTP 錯誤由 interceptor 統一分類成 `ApiError`（401 整頁導向登入、網路錯誤與 5xx 跳 snackbar），畫面只處理和自己有關的種類。交易類型的欄位、帳戶篩選、正負號規則集中在一個純函式模組，以表格驅動的測試鎖住。

**Tech Stack:** Angular 22.2（CLI 22.2.1，zoneless，`@angular/build:unit-test`＋Vitest 5＋jsdom）、Angular Material／CDK 22.2（含 test harness）、typed Reactive Forms、`@angular/service-worker`、Playwright 1.63（E2E，API 以 `page.route` 模擬）、Node ≥ 24.15.0（本機 24.21.0）。

**Spec:** `docs/superpowers/specs/2026-10-04-p3-frontend-pwa-design.md`（段 G、H、I）。後端前置修正另見 `2026-10-04-p3-backend-prereq.md`，**必須先完成**。

## Global Constraints

- 前端放在 `web/`，Angular 專案名稱 `web`，production 輸出在 `web/dist/web/browser`，**不可以輸出到 `src/SixJars.Api/wwwroot`**（後端的 `Without_web_root_frontend_paths_are_404_and_health_still_works` 依賴該目錄不存在；image 內由 Dockerfile 複製到 `/app/wwwroot`）。`dotnet build`／`dotnet test` 不可依賴 Node。
- 本機開發：`ng serve` 用 `https://localhost:4300`（`--ssl`），proxy 把 `/api`、`/auth` 轉給 `https://localhost:5001`（`secure: false`）。Google redirect URI 為 `https://localhost:4300/auth/callback`。
- 單元測試一律在 `TZ=Asia/Taipei` 下執行（由 `web/src/test-setup.ts` 設定）；Playwright 用 `timezoneId: 'Asia/Taipei'`。
- **日期轉 `yyyy-MM-dd` 一律用本地時間的年月日組字串，禁止 `toISOString()`。**
- 金額：使用者輸入正數、最多 2 位小數。支出送 `-金額`、收入送 `+金額`，勾「退款／沖回」時反過來；其他類型送正數。顯示用 `Intl.NumberFormat('zh-TW')`。
- 隱藏的欄位送出時一律是 `null`；`budgetMonth` 只有使用者改過時才送，否則 `null`；空白備註送 `null`。
- XSRF：登入後呼叫一次 `GET /api/antiforgery/token`；非 GET 請求靠 `provideHttpClient()` 內建的 XSRF 支援（cookie `XSRF-TOKEN`、header `X-XSRF-TOKEN`，都是 Angular 預設值，不需設定）。
- 401：整頁導向 `/auth/login?returnUrl=<encodeURIComponent(pathname+search)>`。寫入不自動重試。進度條在請求超過 300ms 才顯示，不設 timeout。
- 畫面文字只用繁體中文；固定文案見下方「文案」。
- 前端與後端在每個 Task 結束時都要全綠；後端數量不可低於後端前置 plan 完成時的基準線。

### 文案（照抄）

| 常數（`core/errors/messages.ts`） | 文字 |
|---|---|
| `LOCKED_MESSAGE` | 此日期已鎖帳，不能新增、修改或刪除 |
| `CONFLICT_MESSAGE` | 這筆交易已在其他裝置修改 |
| `NOT_FOUND_MESSAGE` | 這筆交易已不存在 |
| `UNAVAILABLE_MESSAGE` | 暫時無法連線，請稍後再試 |
| `ANTIFORGERY_MESSAGE` | 安全驗證失敗，請重新整理頁面 |
| `DENIED_MESSAGE` | 登入未完成：可能是此 Google 帳號不在白名單，或登入時取消了授權 |
| `NO_BOOKS_MESSAGE` | 沒有可存取的帳本 |

## Review Focus

1. **午夜到早上 8 點記帳**：在台北時間 00:30 開啟記帳頁，日期預設與送出的 `date` 都必須是當天，不能是前一天（`toISOString()` 會錯）。→ H1 的 `toDateString` 案例＋H3 的 `defaults_date_to_local_today_after_midnight`。
2. **切換類型後殘留的隱藏欄位**：先選「轉帳」填了對方帳戶，再切回「支出」送出，`counterAccountId` 必須是 `null`，否則後端 validator 回 400，使用者看不懂。→ H2 的 `toTransactionInput` 案例＋H3 的 `hidden_fields_are_sent_as_null_after_switching_kind`。
3. **連按兩次 Enter**：請求進行中再按 Enter 不可以送出第二筆（會重複記帳）。→ H3 的 `does_not_send_twice_while_pending`。
4. **金額輸入 0、負數、3 位小數**：不送出、欄位顯示錯誤。→ H3 的 `rejects_invalid_amounts_without_request`。
5. **網址的 `month` 不合法**（`?month=abc`、`202613`）：退回今天所在的月份，不可以白畫面。→ H1 的 `parseBudgetMonth` 案例＋H6 的 `falls_back_to_current_month_for_invalid_query`。

---

## 執行前必讀

### 環境

- 工作目錄：`F:\VibeCode\SixJars-AccountingBook`（Windows）。分支：`claude/p3-frontend-pwa`（**不是 master**）。
- Node ≥ 24.15.0（`node -v`）。前端指令都在 `web/` 內執行。
- 前端全部測試：`npx ng test --watch=false`
- 前端單一檔案：`npx ng test --watch=false --include src/app/<path>.spec.ts`
- 後端全部測試：`dotnet test`（需要 Docker Desktop）。
- E2E（段 I 之後）：`npx playwright test`

### 基準線

- 後端：後端前置 plan 完成時的 `dotnet test` 總數（**369**，2026-10-04 實測），失敗 0。
- 前端：G1 完成時記錄 `ng test` 的數字，之後每個 Task 只增不減。

### 絕對不要碰的東西

- `.env`、`.env.gcp`（機密）、`reference/`（個資）、`efbundle.exe`。**`.env.gcp` 裡的 Neon 連線字串一律不用**；本機開發只用本機 docker 的 postgres（`sixjars-local`），Google client id／secret 由使用者在自己的 PowerShell session 設定環境變數。
- 每個 Task 都用明確路徑 `git add`（可以是 `web/src/app/features/transactions/` 這種目錄），**禁止 `git add .`／`git add -A`**；commit 前看 `git diff --cached --stat`，確認沒有 `node_modules`、`dist`、`.angular`、`test-results`。
- 部署、`git push`、`docker` 指令由使用者執行；agent 不連 Neon、不部署。

### 慣例

- 照 TDD：先寫失敗測試、確認它**以正確的理由**失敗，再實作。一個 Task 一個 commit，commit 訊息用繁體中文，結尾加上 `Co-Authored-By` 行（依當下 session 的 attribution 設定）。
- 測試檔與被測檔放在同一目錄（`foo.ts` ↔ `foo.spec.ts`）。Vitest 的 `describe`／`it`／`expect`／`vi` 是 global。
- HTTP 測試用 `provideHttpClient(withInterceptors([...]))`＋`provideHttpClientTesting()`＋`HttpTestingController`；component 測試用 TestBed＋Material harness（`TestbedHarnessEnvironment`）。spike 已確認 harness 在 zoneless＋Vitest 下可用。
- 表單用 **typed Reactive Forms**（`FormGroup`／`FormControl<T>`，`nonNullable`）。否決 signal forms：Material 的 harness 與 `matInput`／`matAutocomplete` 的整合以 Reactive Forms 最成熟，這期不冒險。
- 時間相關測試用 `vi.useFakeTimers()`＋`vi.setSystemTime(new Date(2026, 0, 1, 0, 30))`（本地時間），測完 `vi.useRealTimers()`。

## 檔案結構

```
web/                         ng new 產生；以下只列本 plan 新增或改動的檔案
  angular.json               serve：ssl、port 4300、proxyConfig；test：setupFiles
  proxy.conf.json            /api、/auth → https://localhost:5001
  ngsw-config.json           navigationUrls 排除 /api/**、/auth/**
  playwright.config.ts、e2e/*.spec.ts
  src/test-setup.ts          TZ=Asia/Taipei
  src/app/
    app.ts / app.html        app shell（app bar、進度條、router-outlet）
    app.config.ts、app.routes.ts
    core/browser-location.ts BrowserLocation：包住 window.location，測試可替換
    core/api/                dto.ts、session-api.ts、book-api.ts、transaction-api.ts、summary-api.ts
    core/errors/             api-error.ts（classifyError）、error-interceptor.ts、messages.ts、notifier.ts
    core/loading/            loading-indicator.ts（service＋interceptor）
    core/auth/               session.service.ts、auth.guard.ts
    core/book/               current-book.ts、book.guard.ts
    features/home/           home.page.ts
    features/denied/         denied.page.ts
    features/transactions/   transaction-rules.ts、transaction-form.ts、transaction-list.ts、transactions.page.ts
    features/summary/        summary.page.ts
    shared/                  dates.ts、money.ts、month-nav.ts、confirm-dialog.ts
Dockerfile、.dockerignore、docs/deploy.md
```

### Task 相依順序

```
G1 → G2 → G3 → G4 → G5 ─(checkpoint G)→ H1 → H2 → H3 → H4 → H5 → H6 ─(checkpoint H)→ I1 → I2 → I3 → I4 ─(checkpoint I)
```

H1（純函式）與 G2–G5 不相依，但依序執行即可。

---

## 段 G：專案、API 層、登入

### Task G1：建立 `web/` 專案

**Files:**
- Create: `web/`（`ng new` 產生）、`web/proxy.conf.json`、`web/src/test-setup.ts`、`web/src/app/timezone.spec.ts`
- Modify: `web/angular.json`、`web/src/app/app.html`、`web/src/app/app.spec.ts`、`web/src/index.html`

**Interfaces:**
- Produces：可執行的 `ng serve`／`ng test`／`ng build`；`provideNativeDateAdapter()` 與 `MAT_DATE_LOCALE = 'zh-TW'` 已在 `app.config.ts`。

- [ ] **Step 1：建立專案**（在 repo 根目錄）

```bash
npx @angular/cli@22 new web --directory web --style=scss --ssr=false --skip-git --defaults
cd web
npx ng add @angular/material --skip-confirmation --defaults
```

刪除 `web/.vscode/`（repo 不收 IDE 設定）。確認 `web/.gitignore` 存在且排除 `/dist`、`/node_modules`、`/.angular`。

- [ ] **Step 2：寫失敗測試 `src/app/timezone.spec.ts`**

```ts
describe('test environment', () => {
  it('runs_in_asia_taipei', () => {
    expect(new Date(2026, 0, 1, 0, 30).getTimezoneOffset()).toBe(-480);
  });
});
```

- [ ] **Step 3：確認失敗**：若本機時區剛好是台北會直接通過；這時把 Step 4 的 setup 暫時改成 `'UTC'` 跑一次，看到 `expected +0 to be -480` 再改回（spike 已驗證這個 mutation）。

- [ ] **Step 4：實作**
  - `src/test-setup.ts`：`declare const process: { env: Record<string, string | undefined> };` 然後 `process.env['TZ'] = 'Asia/Taipei';`（`tsconfig.spec.json` 沒有 node 型別，所以自己宣告）。
  - `angular.json`：`test.options.setupFiles = ["src/test-setup.ts"]`；`serve.options = { "ssl": true, "port": 4300, "proxyConfig": "proxy.conf.json" }`。
  - `proxy.conf.json`：`"/api"` 與 `"/auth"` 各為 `{ "target": "https://localhost:5001", "secure": false }`。**不要**設 `changeOrigin`：要保留瀏覽器的 `Host`，後端才會組出 `https://localhost:4300/auth/callback`。
  - `app.html` 只留 `<router-outlet />`；`app.spec.ts` 改為只斷言 component 建立成功。
  - `index.html`：`lang="zh-Hant-TW"`、`<title>SixJars 記帳</title>`。
  - `app.config.ts`：加上 `provideNativeDateAdapter()`、`{ provide: MAT_DATE_LOCALE, useValue: 'zh-TW' }`。

- [ ] **Step 5：驗證**
  - `npx ng test --watch=false` 全綠，記下數字作為前端基準線。
  - `npx ng build` 成功，`dist/web/browser/index.html` 存在，主程式檔名形如 `main-XXXXXXXX.js`。
  - `npx ng serve` 啟動在 `https://localhost:4300`（若 `EACCES`，執行 `netsh interface ipv4 show excludedportrange protocol=tcp` 回報給使用者，不要自己改 port）。

- [ ] **Step 6：Commit**

```bash
git add web/ ; git status --short   # 確認沒有 node_modules、dist、.angular
git commit -m "feat(web): 建立 Angular 22 專案（Material、Vitest、proxy、台北時區）"
```

### Task G2：DTO 型別與 API service

**Files:**
- Create: `web/src/app/core/api/{dto,session-api,book-api,transaction-api,summary-api}.ts` 與各自的 `.spec.ts`（`dto.ts` 除外）
- Modify: `web/src/app/app.config.ts`（`provideHttpClient(withInterceptors([]))`，G3 會填入 interceptor）

**Interfaces:**
- Produces（`dto.ts`，手寫，對照後端 record；`Guid` 是 `string`，`DateOnly` 是 `'yyyy-MM-dd'` 字串，`decimal` 是 `number`）：
  - `type TransactionKind = 'Income' | 'Expense' | 'Transfer' | 'Withdrawal' | 'CashDeposit' | 'TopUp' | 'CardPayment' | 'LoanDisbursement' | 'LoanPayment' | 'FundAllocation' | 'FundWithdrawal' | 'FundReturn'`
  - `type AccountType = 'Cash' | 'Bank' | 'CreditCard' | 'EWallet' | 'Loan'`；`type CategoryKind = 'Income' | 'Expense'`；`type ExpenseNature = 'Floating' | 'Fixed' | 'Loan' | 'Special'`
  - `MeDto { subject; email: string | null; books: BookSummaryDto[] }`、`BookSummaryDto { id; name }`
  - `BookDto { id; name; openingDate; lockDate: string | null; accounts: AccountDto[]; planningFunds: PlanningFundDto[]; categories: CategoryDto[] }`
  - `AccountDto { id; name; type: AccountType; openingBalance; countsAsAvailableCash: boolean }`、`PlanningFundDto { id; name; openingBalance }`、`CategoryDto { id; name; kind: CategoryKind; nature: ExpenseNature | null; parentId: string | null }`
  - `TransactionDto { id; kind; date; budgetMonth: number; amount; accountId; counterAccountId: string | null; categoryId: string | null; planningFundId: string | null; loanPrincipal: number | null; loanInterest: number | null; note: string | null; postings: { accountId; amount }[]; version: number }`
  - `TransactionInput { kind; date; budgetMonth: number | null; amount; accountId; counterAccountId: string | null; categoryId: string | null; planningFundId: string | null; loanPrincipal: number | null; note: string | null }`
  - `LedgerSummaryDto { budgetMonth; asOf; monthlyDisposable; yearToDate; availableCash; availableCashWithEWallets; accounts: BalanceDto[]; planningFunds: BalanceDto[] }`、`BalanceDto { id; name; balance }`
- Produces（service，皆 `@Injectable({ providedIn: 'root' })`，回傳 `Observable`）：
  - `SessionApi.me(): Observable<MeDto>` → `GET /api/me`；`fetchAntiforgeryToken(): Observable<void>` → `GET /api/antiforgery/token`；`logout(): Observable<void>` → `POST /auth/logout`（body `null`）
  - `BookApi.get(bookId: string): Observable<BookDto>` → `GET /api/books/{bookId}`
  - `TransactionApi.list(bookId, budgetMonth: number): Observable<TransactionDto[]>` → `GET /api/books/{bookId}/transactions?budgetMonth=`；`create(bookId, input): Observable<TransactionDto>` → `POST …/transactions`；`update(bookId, id, version: number, input): Observable<TransactionDto>` → `PUT …/transactions/{id}`，body `{ version, input }`；`delete(bookId, id, version: number): Observable<void>` → `DELETE …/transactions/{id}?version=`
  - `SummaryApi.get(bookId, budgetMonth: number): Observable<LedgerSummaryDto>` → `GET /api/books/{bookId}/summary?budgetMonth=`（不帶 `asOf`）

- [ ] **Step 1：寫失敗測試**：每個 service 一個 spec，每個方法一個 `it`，以 `httpTesting.expectOne({ method, url })` 斷言 method 與完整 URL（含 query），並對 PUT 斷言 `req.request.body` 等於 `{ version: 3, input }`。另外在 `transaction-api.spec.ts` 加：

```ts
it('sends_xsrf_header_on_unsafe_requests', () => {
  document.cookie = 'XSRF-TOKEN=abc';
  api.create('b1', input).subscribe();
  expect(httpTesting.expectOne('/api/books/b1/transactions').request.headers.get('X-XSRF-TOKEN')).toBe('abc');
});
```

- [ ] **Step 2：確認失敗**（模組不存在）。
- [ ] **Step 3：實作** `dto.ts` 與四個 service。用 `HttpParams` 組 query；URL 一律以 `/` 開頭的相對路徑（Angular 的 XSRF interceptor 只對相對 URL 加 header）。
- [ ] **Step 4：確認全綠**（`sends_xsrf_header_on_unsafe_requests` 若失敗，代表 TestBed 沒用 `provideHttpClient()`，不要自己加 header）。
- [ ] **Step 5：Commit** `feat(web): API DTO 與 service（me、帳本、交易、總覽）`

### Task G3：錯誤分類、錯誤 interceptor、進度條

**Files:**
- Create: `web/src/app/core/browser-location.ts`、`core/errors/{api-error,error-interceptor,messages,notifier}.ts`、`core/loading/loading-indicator.ts`，以及 `api-error.spec.ts`、`error-interceptor.spec.ts`、`loading-indicator.spec.ts`
- Modify: `web/src/app/app.config.ts`

**Interfaces:**
- Produces：
  - `BrowserLocation { assign(url: string): void; currentPath(): string }`（root service；`currentPath` 回傳 `location.pathname + location.search`）
  - `type ApiError = { kind: 'validation'; fieldErrors: Record<string, string[]> } | { kind: 'domain'; code: string; message: string } | { kind: 'conflict' } | { kind: 'notFound' } | { kind: 'antiforgery' } | { kind: 'unauthorized' } | { kind: 'unavailable' }`
  - `classifyError(error: HttpErrorResponse): ApiError`
  - `Notifier.show(message: string): void`（root service，包 `MatSnackBar`，4 秒）
  - `apiErrorInterceptor: HttpInterceptorFn`：把 `HttpErrorResponse` 換成 `ApiError` 再 `throwError`；所以**之後所有 component 收到的錯誤都是 `ApiError`**。
  - `LoadingIndicator { readonly visible: Signal<boolean> }`、`loadingInterceptor: HttpInterceptorFn`
  - `messages.ts`：上方「文案」表的常數。

分類規則（`classifyError`）：

| 條件 | 結果 |
|---|---|
| 400 且 body 有 `errors` 物件 | `validation`，`fieldErrors = body.errors`（key 已是 camelCase，例如 `counterAccountId`） |
| 400 且 `body.title === '缺少或無效的 XSRF token'`（`AntiforgeryFilter.InvalidTokenTitle`） | `antiforgery` |
| 401 | `unauthorized` |
| 404 | `notFound` |
| 409 | `conflict` |
| 422 | `domain`，`code = body.code ?? 'rule'`，`message = body.detail ?? ''` |
| 其他（0、5xx、其他 400） | `unavailable` |

interceptor 的副作用：`unauthorized` → `BrowserLocation.assign('/auth/login?returnUrl=' + encodeURIComponent(currentPath()))`，並回傳 `NEVER`（整頁要離開了，不讓畫面再處理）；`antiforgery` → `Notifier.show(ANTIFORGERY_MESSAGE)` 後 throw；`unavailable` → `Notifier.show(UNAVAILABLE_MESSAGE)` 後 throw；其餘只 throw。

- [ ] **Step 1：寫失敗測試**
  - `api-error.spec.ts`：表格驅動，上表每列一個案例，另加「422 沒有 `code` → `rule`」與「status 0 → `unavailable`」。
  - `error-interceptor.spec.ts`（`BrowserLocation`、`Notifier` 用 `{ provide, useValue: { assign: vi.fn(), currentPath: () => '/books/b1/transactions?month=202601' } }` 替換）：
    - `redirects_to_login_on_401`：`assign` 收到 `'/auth/login?returnUrl=%2Fbooks%2Fb1%2Ftransactions%3Fmonth%3D202601'`，且訂閱者的 `error` 沒有被呼叫。
    - `notifies_and_rethrows_on_network_error`：`req.error(new ProgressEvent('error'))` → `Notifier.show(UNAVAILABLE_MESSAGE)`，訂閱者收到 `{ kind: 'unavailable' }`。
    - `rethrows_validation_without_notifying`。
  - `loading-indicator.spec.ts`（fake timers）：請求進行 299ms 時 `visible()` 為 false；300ms 時為 true；回應後為 false；兩個重疊的請求要等兩個都完成才變 false；300ms 內完成的請求從頭到尾都不會變 true。
- [ ] **Step 2：確認失敗。**
- [ ] **Step 3：實作。** `LoadingIndicator` 用一個進行中計數；計數從 0 變 1 時啟動 300ms timer，回到 0 時清掉 timer 並設為 false。interceptor 以 `finalize` 遞減。`app.config.ts` 註冊順序：`withInterceptors([loadingInterceptor, apiErrorInterceptor])`。
- [ ] **Step 4：確認全綠。**
- [ ] **Step 5：Commit** `feat(web): 錯誤分類、401 導向登入、延遲顯示的進度條`

### Task G4：SessionService、authGuard、CurrentBook、bookGuard

**Files:**
- Create: `web/src/app/core/auth/{session.service,auth.guard}.ts`、`core/book/{current-book,book.guard}.ts` 與各自的 `.spec.ts`

**Interfaces:**
- Consumes：G2 的 `SessionApi`、`BookApi`；G3 的 `BrowserLocation`、`ApiError`。
- Produces：
  - `SessionService { readonly me: Signal<MeDto | undefined>; load(): Promise<MeDto>; logout(): Promise<void> }`：`load()` 第一次呼叫 `me()`，成功後**再**呼叫一次 `fetchAntiforgeryToken()`，結果快取；之後的呼叫直接回傳快取（並行呼叫只發一次請求）。`logout()` 呼叫 `SessionApi.logout()` 後 `BrowserLocation.assign('/')`。
  - `authGuard: CanActivateFn`：`await session.load()` 後回傳 `true`（401 由 interceptor 處理，promise 不會 resolve）。
  - `CurrentBook { readonly book: Signal<BookDto | undefined>; load(bookId: string): Promise<BookDto> }`：同一個 `bookId` 只請求一次；換帳本時重新請求。
  - `bookGuard: CanActivateFn`：讀 `route.paramMap.get('bookId')` 呼叫 `CurrentBook.load`；`ApiError.kind === 'notFound'` 時回傳 `router.parseUrl('/')`。

- [ ] **Step 1：寫失敗測試**：
  - `loads_me_then_fetches_antiforgery_token_once`：兩次 `load()` 只有一個 `/api/me` 與一個 `/api/antiforgery/token`，且 token 請求在 me 回應之後才發出。
  - `logout_posts_then_reloads_root`。
  - `book_guard_redirects_home_when_book_not_found`：`BookApi` 回 404 → guard 結果是 `UrlTree('/')`。
  - `current_book_reuses_cached_book_for_same_id`。
- [ ] **Step 2：確認失敗。** - [ ] **Step 3：實作**（以 `firstValueFrom`）。 - [ ] **Step 4：確認全綠。**
- [ ] **Step 5：Commit** `feat(web): 登入狀態、XSRF token、帳本快取與 route guard`

### Task G5：路由、app shell、首頁分流、`/denied`

**Files:**
- Create: `web/src/app/features/home/home.page.ts`、`features/denied/denied.page.ts` 與 spec
- Modify: `web/src/app/app.routes.ts`、`app.ts`、`app.html`、`app.scss`、`app.spec.ts`

**Interfaces:**
- Consumes：G3 `LoadingIndicator`，G4 全部。
- Produces（路由）。`features/transactions/transactions.page.ts` 與 `features/summary/summary.page.ts` 在本 Task 先建立 placeholder（`export class TransactionsPage`／`SummaryPage`，template 只顯示頁名），H6／I1 再換成真正的內容：

```ts
[
  { path: '', component: HomePage, canActivate: [authGuard] },
  { path: 'denied', component: DeniedPage },
  { path: 'books/:bookId', canActivate: [authGuard, bookGuard], children: [
    { path: 'transactions', loadComponent: () => import('./features/transactions/transactions.page').then(m => m.TransactionsPage) },
    { path: 'summary', loadComponent: () => import('./features/summary/summary.page').then(m => m.SummaryPage) },
    { path: '', pathMatch: 'full', redirectTo: 'transactions' },
  ] },
  { path: '**', redirectTo: '' },
]
```

- app shell（`App`）：`LoadingIndicator.visible()` 時在最上方顯示 `mat-progress-bar mode="indeterminate"`。`SessionService.me()` 有值時顯示 `mat-toolbar`：帳本名稱（`CurrentBook.book()?.name`）、「記帳」「總覽」兩個 `routerLink`（`queryParamsHandling: 'preserve'`，只有帳本已載入時顯示）、email、「登出」按鈕（呼叫 `SessionService.logout()`）。寬度 < 600px 時 toolbar 內容換行排列（CSS 即可）。
- `HomePage`：讀 `SessionService.me()!.books`；0 本 → 顯示 `NO_BOOKS_MESSAGE`；1 本 → `router.navigate(['/books', id, 'transactions'], { replaceUrl: true })`；多本 → 帳本名稱的連結清單。
- `DeniedPage`：顯示 `DENIED_MESSAGE` 與 `<a href="/auth/login">重新登入</a>`（**一般連結，不用 `routerLink`**：要整頁導向後端）。

- [ ] **Step 1：寫失敗測試**：`home_redirects_to_only_book`、`home_lists_books_when_many`、`home_shows_message_when_none`、`denied_links_to_backend_login`（斷言 `href` 為 `/auth/login` 且沒有 `routerLink` 屬性）、`shell_hides_toolbar_before_sign_in`、`shell_shows_book_name_email_and_logout`。
- [ ] **Step 2：確認失敗。** - [ ] **Step 3：實作。** - [ ] **Step 4：確認全綠**，`npx ng build` 成功。
- [ ] **Step 5：Commit** `feat(web): app shell、首頁分流與登入未完成頁`

### Checkpoint G（停下來讓使用者檢視）

1. 使用者在自己的 PowerShell 照 `docs/deploy.md` §5 設定環境變數（`ASPNETCORE_URLS=https://localhost:5001`、本機 docker 的連線字串、Google client id／secret），執行 `dotnet run --project src/SixJars.Api`。後端前置 plan 必須已合入本分支。
2. `cd web; npx ng serve`，瀏覽器開 `https://localhost:4300`。
3. 手動驗證（同時完成 P2 交接文件的「Google 登入手動驗證」）：
   - 未登入開 `/` → 轉到 Google → 回到 `https://localhost:4300/`，看到帳本名稱與 email。
   - DevTools 確認 `__Host-sixjars-auth`、`XSRF-TOKEN` 兩個 cookie 存在。
   - 用不在白名單的 Google 帳號登入 → 停在 `/denied`，看到文案而不是 JSON。
   - 登出 → 回到 `/` 並重新走登入。
4. 不通時：先看後端收到的 `redirect_uri` 是否為 `https://localhost:4300/auth/callback`（Google 的錯誤頁會顯示）。回報給使用者，不要改 Google 設定。
5. 用 `docs(plans):` 回寫本段的偏差。

---

## 段 H：記帳頁

### Task H1：日期、歸屬月份、金額的純函式

**Files:**
- Create: `web/src/app/shared/{dates,money}.ts`、`dates.spec.ts`、`money.spec.ts`

**Interfaces:**
- Produces：
  - `toDateString(date: Date): string`（本地年月日，`yyyy-MM-dd`）
  - `parseDateString(value: string): Date`（本地午夜）
  - `budgetMonthOf(date: Date | string): number`（`202601`）
  - `formatBudgetMonth(budgetMonth: number): string`（`'2026/01'`）
  - `addMonths(budgetMonth: number, delta: number): number`
  - `parseBudgetMonth(value: string | null): number | null`（只接受 6 位數、月份 1–12、年份 1900–9999；否則 `null`）
  - `formatAmount(amount: number): string`（`Intl.NumberFormat('zh-TW', { maximumFractionDigits: 2 })`）

- [ ] **Step 1：寫失敗測試**（表格驅動，`it.each`）：
  - `toDateString(new Date(2026, 0, 1, 0, 30))` → `'2026-01-01'`；`new Date(2026, 11, 31, 23, 59)` → `'2026-12-31'`；`new Date(2026, 1, 5)` → `'2026-02-05'`（補零）。
  - `parseDateString('2026-03-01')` 的 `getDate()` 為 1、`getHours()` 為 0。
  - `budgetMonthOf('2026-01-31')` → `202601`；`budgetMonthOf(new Date(2026, 0, 1, 0, 30))` → `202601`。
  - `addMonths(202601, -1)` → `202512`；`addMonths(202612, 1)` → `202701`；`addMonths(202601, 13)` → `202702`。
  - `parseBudgetMonth`：`'202601'` → `202601`；`'202613'`、`'202600'`、`'abc'`、`'20261'`、`''`、`null` → `null`。
  - `formatBudgetMonth(202601)` → `'2026/01'`。
  - `formatAmount(-1234.5)` → `'-1,234.5'`；`formatAmount(1000)` → `'1,000'`；`formatAmount(0.125)` → `'0.13'`。
- [ ] **Step 2：確認失敗。** - [ ] **Step 3：實作。** - [ ] **Step 4：確認全綠。**
- [ ] **Step 5：Commit** `feat(web): 日期、歸屬月份與金額格式的純函式`

### Task H2：交易類型規則（純函式）

**Files:**
- Create: `web/src/app/features/transactions/transaction-rules.ts`、`transaction-rules.spec.ts`

**Interfaces:**
- Consumes：G2 `dto.ts`；H1 `toDateString`、`budgetMonthOf`。
- Produces：

```ts
interface AccountSlot { label: string; types: AccountType[] }
interface KindRule {
  label: string;
  account: AccountSlot;
  counter: (AccountSlot & { required: boolean }) | null;
  category: { required: boolean } | null;     // 分類種類由 categoryKindOf 決定
  planningFund: boolean;                       // true = 必填
  loanPrincipal: boolean;                      // true = 必填
  signed: boolean;                             // 收入、支出：帶正負號
  flow: 'accountToCounter' | 'counterToAccount' | null;   // 列表的「從 → 到」
}
const KIND_RULES: Record<TransactionKind, KindRule>;
const PRIMARY_KINDS: TransactionKind[];   // ['Expense', 'Income', 'Transfer']
const MORE_KINDS: TransactionKind[];      // 其餘 9 種，順序同 spec §4.1
interface TransactionFormValue {
  kind: TransactionKind; date: Date; accountId: string; counterAccountId: string | null;
  categoryId: string | null; planningFundId: string | null; amount: number; refund: boolean;
  loanPrincipal: number | null; note: string; budgetMonthOverride: number | null;
}
categoryKindOf(kind: TransactionKind): CategoryKind                // Income → 'Income'，其餘 'Expense'
signedAmount(kind: TransactionKind, amount: number, refund: boolean): number
toTransactionInput(value: TransactionFormValue): TransactionInput
fromTransaction(dto: TransactionDto): TransactionFormValue
accountsFor(accounts: AccountDto[], slot: AccountSlot): AccountDto[]
categoryOptions(categories: CategoryDto[], kind: CategoryKind): { id: string; label: string }[]   // 子分類為「主 / 子」，主分類為「主」
isLocked(date: string, lockDate: string | null): boolean           // date <= lockDate
```

`KIND_RULES` 的內容（來源：P2 plan Task 20 欄位表、`TransactionFactory.RequireAccount`、`RequireCategory`）：

| Kind | label | account（label：types） | counter（label：types，必填？） | category | fund | principal | flow |
|---|---|---|---|---|---|---|---|
| Income | 收入 | 帳戶：Cash, Bank, EWallet | — | 必填 | — | — | — |
| Expense | 支出 | 帳戶：Cash, Bank, CreditCard, EWallet | — | 必填 | — | — | — |
| Transfer | 轉帳 | 從：Cash, Bank | 到：Cash, Bank，必填 | — | — | — | accountToCounter |
| Withdrawal | 提款 | 銀行：Bank | 現金：Cash，必填 | — | — | — | accountToCounter |
| CashDeposit | 現金存入 | 銀行：Bank | 現金：Cash，必填 | — | — | — | counterToAccount |
| TopUp | 加值 | 電子錢包：EWallet | 來源：Cash, Bank, CreditCard，必填 | — | — | — | counterToAccount |
| CardPayment | 繳卡費 | 付款帳戶：Cash, Bank | 信用卡：CreditCard，必填 | — | — | — | accountToCounter |
| LoanDisbursement | 新增貸款 | 入帳帳戶：Cash, Bank | 貸款：Loan，必填 | — | — | — | counterToAccount |
| LoanPayment | 貸款繳款 | 付款帳戶：Cash, Bank | 貸款：Loan，必填 | 選填（利息） | — | 必填 | accountToCounter |
| FundAllocation | 入新資金 | 轉入帳戶：Cash, Bank, EWallet | 轉出帳戶：Cash, Bank，選填 | — | 必填 | — | counterToAccount |
| FundWithdrawal | 出資金 | 帳戶：Cash, Bank, EWallet | — | 選填 | 必填 | — | — |
| FundReturn | 資金回流 | 帳戶：Cash, Bank, EWallet | — | — | 必填 | — | — |

規則細節：
- `signedAmount`：Expense → `refund ? amount : -amount`；Income → `refund ? -amount : amount`；其他 → `amount`（`refund` 忽略）。
- `toTransactionInput`：`date = toDateString(value.date)`；規則中為 `null`／`false` 的欄位一律送 `null`；`note.trim() === ''` → `null`；`budgetMonth = value.budgetMonthOverride`。
- `fromTransaction`：`amount = Math.abs(dto.amount)`；`refund = (Expense && dto.amount > 0) || (Income && dto.amount < 0)`；`budgetMonthOverride = dto.budgetMonth === budgetMonthOf(dto.date) ? null : dto.budgetMonth`。
- 這些規則只是 UX 篩選；後端 422 仍是判定依據（spec §4.1）。

- [ ] **Step 1：寫失敗測試**：
  - `it.each` 12 種 kind：斷言 `accountsFor(sampleAccounts, KIND_RULES[kind].account)` 與 counter 的帳戶類型集合等於上表（`sampleAccounts` 每種 AccountType 各一個）。
  - `it.each` 12 種 kind：以一個「每個欄位都填了值」的 `TransactionFormValue` 呼叫 `toTransactionInput`，斷言規則外的欄位為 `null`、規則內的保留（Review Focus 2）。
  - `signedAmount`：Expense 100 → -100；Expense 退款 → 100；Income 100 → 100；Income 沖回 → -100；Transfer 100（refund true）→ 100。
  - `fromTransaction` 與 `toTransactionInput` 來回：Expense -100（未覆寫月份）、Income -50（沖回）、budgetMonth 與日期月份不同時保留覆寫。
  - `categoryOptions`：主分類「食」含子分類「早餐」→ 選項含 `'食'` 與 `'食 / 早餐'`；`kind` 不符的分類不出現。
  - `isLocked('2026-01-31', '2026-01-31')` → true；`'2026-02-01'` → false；`lockDate` 為 `null` → false。
  - `PRIMARY_KINDS.length + MORE_KINDS.length === 12` 且無重複。
- [ ] **Step 2：確認失敗。** - [ ] **Step 3：實作。** - [ ] **Step 4：確認全綠。**
- [ ] **Step 5：Commit** `feat(web): 交易類型的欄位、帳戶篩選與正負號規則`

### Task H3：記帳表單（新增）

**Files:**
- Create: `web/src/app/features/transactions/transaction-form.ts`（＋`.html`、`.scss`）、`transaction-form.spec.ts`

**Interfaces:**
- Consumes：H2 全部；G2 `TransactionApi`；G3 `ApiError`、`messages.ts`。
- Produces：`TransactionForm` component，selector `app-transaction-form`：
  - inputs：`book = input.required<BookDto>()`
  - outputs：`saved = output<TransactionDto>()`
  - 本 Task 只做新增；H4 加上編輯與錯誤對應。

畫面與行為（spec §4.1）：
- 欄位順序：日期（`matDatepicker`）→ 交易類型（`mat-button-toggle-group` 放 `PRIMARY_KINDS`，加一個「更多」`mat-menu` 放 `MORE_KINDS`；選了更多裡的類型時，「更多」按鈕顯示該類型的 label）→ 帳戶（`mat-select`，選項 `accountsFor`，label 取 `KIND_RULES[kind].account.label`）→ 對方帳戶（規則有 counter 才顯示）→ 金額（`input type="text" inputmode="decimal"`）＋「退款／沖回」checkbox（只有 `signed` 時顯示）→ 本金與唯讀的利息（LoanPayment）→ 分類（`matAutocomplete`，選項 `categoryOptions(book.categories, categoryKindOf(kind))`，可打字篩選，`displayWith` 把 id 轉成 label）→ 財務規劃帳戶（`mat-select`，規則有 fund 才顯示）→ 備註 →「進階」（`mat-expansion-panel`，內含歸屬月份，預設顯示日期所在月份，改過才算覆寫）。
- 整個表單是 `<form (ngSubmit)>`，送出鈕 `type="submit"`，Enter 送出由瀏覽器原生行為負責；autocomplete 展開時 Material 會攔下 Enter（E2E 驗證）。
- client 驗證：金額必填、> 0、最多 2 位小數（`/^\d+(\.\d{1,2})?$/`）；帳戶必填；規則要求的 counter／category／fund／principal 必填；本金 ≥ 0；分類必須是選項中的 id（打了字沒選取 → 「請從清單選擇分類」）。不合法時 `markAllAsTouched()`，不發請求。
- 切換類型時：清掉新類型不允許的帳戶與分類選擇（例如從支出切到提款，原本選的信用卡帳戶要清掉）。
- 送出：請求進行中 `pending` 為 true，送出鈕 `disabled`，`onSubmit` 開頭若 `pending` 就 return。
- 成功：`saved.emit(dto)`；保留日期、類型、帳戶；其餘清空（`refund` 回 false、進階月份回到未覆寫）；`amountInput.nativeElement.focus()`。
- 日期預設：建立 component 時的 `new Date()`（本地）。

- [ ] **Step 1：寫失敗測試**（`book` 用一個 fixture：每種帳戶類型各一個、收入與支出分類各有主／子、一個財務規劃帳戶；HTTP 用 `HttpTestingController`）：
  - `defaults_to_expense_with_expense_fields_only`：看得到帳戶、金額、退款、分類、備註；看不到對方帳戶、財務規劃帳戶、本金。
  - `transfer_shows_counter_and_filters_accounts`：切到「轉帳」，帳戶選單只有 Cash、Bank 兩個帳戶，分類欄位消失。
  - `more_menu_lists_nine_kinds`。
  - `posts_negative_amount_for_expense`：填 100 → 請求 body `amount: -100`、`budgetMonth: null`、`counterAccountId: null`、`note: null`。
  - `posts_positive_amount_for_refund`。
  - `defaults_date_to_local_today_after_midnight`：`vi.setSystemTime(new Date(2026, 0, 1, 0, 30))` 後建立 component，送出的 `date` 為 `'2026-01-01'`（Review Focus 1）。
  - `hidden_fields_are_sent_as_null_after_switching_kind`：轉帳選了對方帳戶 → 切回支出 → 送出 → `counterAccountId: null`（Review Focus 2）。
  - `keeps_date_kind_account_and_focuses_amount_after_save`：回應 201 後，日期／類型／帳戶不變，金額與備註為空，`document.activeElement` 是金額 input。
  - `does_not_send_twice_while_pending`：送出兩次，`httpTesting.match(...)` 長度為 1（Review Focus 3）。
  - `rejects_invalid_amounts_without_request`：`it.each(['0', '-5', '1.234', 'abc'])`，`httpTesting.expectNone`，金額欄位顯示錯誤（Review Focus 4）。
  - `shows_interest_for_loan_payment`：總額 1000、本金 800 → 利息欄位顯示 `200`，body `amount: 1000`、`loanPrincipal: 800`。
  - `sends_budget_month_only_when_overridden`。
- [ ] **Step 2：確認失敗。** - [ ] **Step 3：實作。** - [ ] **Step 4：確認全綠。**
- [ ] **Step 5：Commit** `feat(web): 記帳表單（類型切換、帳戶篩選、連續記帳）`

### Task H4：記帳表單（編輯與伺服器錯誤）

**Files:**
- Modify: `web/src/app/features/transactions/transaction-form.{ts,html}`、`transaction-form.spec.ts`

**Interfaces:**
- Produces（在 H3 的基礎上新增）：
  - input：`editing = input<TransactionDto | null>(null)`
  - outputs：`cancelled = output<void>()`、`stale = output<void>()`（409 或 404：資料已過期，交給頁面重新載入）

行為：
- `editing` 變成非 null：以 `fromTransaction` 填入表單，顯示「編輯中」標示與「取消」按鈕；送出改用 `TransactionApi.update(book.id, editing.id, editing.version, input)`。成功後 `saved.emit`；頁面會把 `editing` 設回 null，表單此時重設為新增狀態（日期、類型、帳戶沿用剛編輯的那筆，其餘清空，與新增成功後的規則相同）。
- 「取消」：`cancelled.emit()`。
- 錯誤（表單內容一律保留）：
  - `validation`：每個 key 對到可見欄位（`accountId`、`counterAccountId`、`categoryId`、`planningFundId`、`amount`、`loanPrincipal`、`note`、`date`、`budgetMonth`）時，`control.setErrors({ server: messages[0] })` 並顯示在 `mat-error`；對不到的 key 顯示在表單頂端的錯誤清單（`${key}: ${message}`）。
  - `domain`：`code === 'locked'` 顯示 `LOCKED_MESSAGE`，否則顯示 `message`，都在表單頂端。
  - `conflict` → `Notifier.show(CONFLICT_MESSAGE)`＋`stale.emit()`；`notFound` → `Notifier.show(NOT_FOUND_MESSAGE)`＋`stale.emit()`。
  - 其餘種類 interceptor 已處理，表單只結束 `pending`。

- [ ] **Step 1：寫失敗測試**：
  - `loads_editing_transaction_into_form`（Expense -100 → 金額顯示 100、未勾退款）。
  - `puts_with_version_when_editing`（body `{ version: 7, input }`）。
  - `cancel_emits_cancelled`。
  - `maps_validation_errors_to_fields`：400 `{ errors: { counterAccountId: ['必須是現金帳戶'] } }`（轉帳時）→ 對方帳戶欄位下方顯示該訊息。
  - `shows_unmapped_validation_keys_at_top`：`{ errors: { foo: ['bar'] } }` → 頂端顯示 `foo: bar`。
  - `shows_locked_message_for_locked_domain_error` 與 `shows_detail_for_rule_domain_error`。
  - `emits_stale_on_conflict`、`emits_stale_on_not_found`。
  - `keeps_form_values_after_error`。
- [ ] **Step 2：確認失敗。** - [ ] **Step 3：實作。** - [ ] **Step 4：確認全綠。**
- [ ] **Step 5：Commit** `feat(web): 交易修改與伺服器錯誤對回欄位`

### Task H5：交易列表與確認對話框

**Files:**
- Create: `web/src/app/features/transactions/transaction-list.ts`（＋`.html`、`.scss`）、`transaction-list.spec.ts`、`web/src/app/shared/confirm-dialog.ts`、`confirm-dialog.spec.ts`

**Interfaces:**
- Consumes：H1 `formatAmount`；H2 `KIND_RULES`、`isLocked`、`categoryOptions`。
- Produces：
  - `TransactionList`，selector `app-transaction-list`；inputs：`transactions = input.required<TransactionDto[]>()`、`book = input.required<BookDto>()`、`editingId = input<string | null>(null)`；outputs：`edit = output<TransactionDto>()`、`remove = output<TransactionDto>()`。
  - `confirm(dialog: MatDialog, message: string): Observable<boolean>`（`ConfirmDialog` component：訊息＋「取消」「刪除」兩個按鈕，取消或關閉都回傳 `false`）。

畫面：`mat-table`，欄位：日期（`MM/dd`）、類型 label、帳戶（有 `flow` 時依方向顯示 `從 → 到`；FundAllocation 沒有轉出帳戶時只顯示轉入帳戶）、分類 label（「主 / 子」，沒有就空白）、金額（`formatAmount`，負數加上 `negative` class）、備註、刪除按鈕。順序照後端回傳，不重新排序。點一列 → `edit.emit`；`editingId` 那一列加上 `editing` class。`isLocked(t.date, book.lockDate)` 的列顯示鎖頭圖示（`mat-icon` `lock`），點列不 emit，刪除按鈕 `disabled`。沒有資料時顯示「這個月還沒有交易」。

- [ ] **Step 1：寫失敗測試**：`shows_transfer_as_from_to`、`shows_cash_deposit_as_cash_to_bank`（counterToAccount）、`shows_category_with_parent`、`formats_negative_amount`、`emits_edit_on_row_click`、`locked_rows_cannot_be_edited_or_deleted`、`shows_empty_message`；`confirm-dialog.spec.ts`：`returns_true_only_when_confirmed`（用 `MatDialogHarness`）。
- [ ] **Step 2：確認失敗。** - [ ] **Step 3：實作。** - [ ] **Step 4：確認全綠。**
- [ ] **Step 5：Commit** `feat(web): 交易列表（鎖帳、從→到）與刪除確認對話框`

### Task H6：記帳頁

**Files:**
- Create: `web/src/app/features/transactions/transactions.page.ts`（＋`.html`、`.scss`）、`transactions.page.spec.ts`、`web/src/app/shared/month-nav.ts`、`month-nav.spec.ts`
- Modify: `web/src/app/app.routes.ts`（若 G5 用的是 placeholder）

**Interfaces:**
- Consumes：G4 `CurrentBook`；H1 `parseBudgetMonth`、`budgetMonthOf`、`addMonths`、`formatBudgetMonth`；H3／H4 `TransactionForm`；H5 `TransactionList`、`confirm`；G2 `TransactionApi`。
- Produces：
  - `MonthNav`，selector `app-month-nav`；input `month = input.required<number>()`；output `monthChange = output<number>()`；顯示 `◀ 2026/01 ▶`（I1 也會用）。
  - `TransactionsPage`（`export class TransactionsPage`，路由 `books/:bookId/transactions`）。

行為：
- `month = parseBudgetMonth(query 'month') ?? budgetMonthOf(new Date())`；`MonthNav` 改月份時 `router.navigate([], { queryParams: { month }, queryParamsHandling: 'merge' })`。
- 月份或帳本改變時重新載入 `TransactionApi.list(bookId, month)`（`switchMap`，舊請求取消）。
- 版面：上方 `TransactionForm`（`book = CurrentBook.book()`、`editing`），下方 `TransactionList`。
- `saved` → `editing.set(null)`＋重新載入；`cancelled` → `editing.set(null)`；`stale` → `editing.set(null)`＋重新載入；`edit(t)` → `editing.set(t)` 並捲到表單頂端。
- `remove(t)`：`confirm(dialog, \`確定要刪除 ${t.date} ${KIND_RULES[t.kind].label} ${formatAmount(t.amount)}？\`)` 為 true 時 `TransactionApi.delete(bookId, t.id, t.version)`；成功 → `Notifier.show('已刪除')`＋重新載入；`conflict` → `CONFLICT_MESSAGE`＋重新載入；`notFound` → `NOT_FOUND_MESSAGE`＋重新載入；`domain` locked → `LOCKED_MESSAGE`；其他 domain → `message`。刪除的正是編輯中那筆時，`editing.set(null)`。

- [ ] **Step 1：寫失敗測試**：
  - `loads_transactions_for_query_month`（`?month=202603` → `GET …?budgetMonth=202603`）。
  - `falls_back_to_current_month_for_invalid_query`（`?month=202613`，`vi.setSystemTime(new Date(2026, 4, 10))` → `budgetMonth=202605`）（Review Focus 5）。
  - `month_nav_navigates_with_query`（`month-nav.spec.ts`：按 ◀ emit `202512`，從 `202601` 開始）。
  - `reloads_after_save`。
  - `deletes_with_version_after_confirm` 與 `does_not_delete_when_cancelled`。
  - `reloads_on_delete_conflict`。
- [ ] **Step 2：確認失敗。** - [ ] **Step 3：實作。** - [ ] **Step 4：確認全綠**，`npx ng build` 成功。
- [ ] **Step 5：Commit** `feat(web): 記帳頁（月份切換、修改、刪除）`

### Checkpoint H

使用者在本機（同 Checkpoint G 的啟動方式）接 CLI 匯入的真實資料：
1. 翻到幾個有資料的月份，筆數、金額、分類與 Excel 對得上。
2. 只用鍵盤連續記 3 筆支出（Tab 移動、Enter 送出、焦點回到金額）。
3. 修改一筆、刪除一筆；在另一個分頁先改同一筆，再回來送出 → 看到「這筆交易已在其他裝置修改」並重新載入。
4. 若帳本有鎖帳日：鎖帳日之前的列顯示鎖頭。
5. 發現 DTO 不同步（欄位名稱、型別）時記錄下來，用 `docs(plans):` 回寫。

---

## 段 I：總覽、PWA、E2E、部署

### Task I1：總覽頁

**Files:**
- Create: `web/src/app/features/summary/summary.page.ts`（＋`.html`、`.scss`）、`summary.page.spec.ts`

**Interfaces:**
- Consumes：G2 `SummaryApi`；G4 `CurrentBook`；H1 `parseBudgetMonth`、`budgetMonthOf`、`formatAmount`；H6 `MonthNav`。
- Produces：`SummaryPage`（路由 `books/:bookId/summary`）。

畫面（spec §5）：月份處理同 H6。三張 `mat-card`：「月可用餘額」`monthlyDisposable`、「年累計餘額」`yearToDate`、「可用現金」`availableCash`，並在同一張卡片下方小字顯示「含電子錢包 {availableCashWithEWallets}」。帳戶餘額依 `CurrentBook` 的帳戶類型分組，組的順序與標題：現金（Cash）、銀行（Bank）、電子錢包（EWallet）、信用卡（CreditCard）、貸款（Loan）；餘額照後端數字顯示，不改正負號（負債本來就是負數，Checkpoint I 確認）；`CurrentBook` 找不到的帳戶放在「其他」組。財務規劃帳戶另列一張表。

- [ ] **Step 1：寫失敗測試**：`loads_summary_for_query_month`、`shows_three_cards_with_formatted_amounts`、`groups_accounts_by_type_in_fixed_order`、`puts_unknown_accounts_in_other_group`、`lists_planning_funds`。
- [ ] **Step 2：確認失敗。** - [ ] **Step 3：實作。** - [ ] **Step 4：確認全綠。**
- [ ] **Step 5：Commit** `feat(web): 總覽頁`

### Task I2：PWA

**Files:**
- Create: `web/src/app/pwa-config.spec.ts`
- Modify：`ng add @angular/pwa` 產生與修改的檔案（`ngsw-config.json`、`public/manifest.webmanifest`、`public/icons/*`、`angular.json`、`app.config.ts`、`index.html`、`package.json`、`package-lock.json`）

**Interfaces:**
- Produces：production build 輸出 `ngsw-worker.js`、`ngsw.json`、`manifest.webmanifest`。

- [ ] **Step 1：寫失敗測試 `pwa-config.spec.ts`**（`import config from '../../ngsw-config.json'`；`module: preserve` 已開啟 JSON import）：
  - `navigation_excludes_auth_and_api`：`config.navigationUrls` 包含 `'!/auth/**'` 與 `'!/api/**'`，且保留 Angular 預設的 `'/**'`、`'!/**/*.*'`、`'!/**/*__*'`、`'!/**/*__*/**'`。
  - `does_not_cache_api_data`：`config.dataGroups` 不存在或為空陣列。
- [ ] **Step 2：確認失敗**（檔案不存在）。
- [ ] **Step 3：實作**：`npx ng add @angular/pwa --skip-confirmation`；在 `ngsw-config.json` 加上上述 `navigationUrls`；`manifest.webmanifest` 的 `name` 為 `SixJars 記帳`、`short_name` 為 `SixJars`、`lang` 為 `zh-Hant-TW`。`provideServiceWorker` 保留 `enabled: !isDevMode()`（`ng serve` 不啟用，開發時不會被快取干擾）。
- [ ] **Step 4：確認全綠**；`npx ng build` 後 `dist/web/browser` 內有 `ngsw.json`，且 `ngsw.json` 的 `navigationUrls` 含有 `/auth/` 的排除規則。
- [ ] **Step 5：Commit** `feat(web): PWA（只快取 app shell，排除 /auth 與 /api 導覽）`

### Task I3：Playwright E2E

**Files:**
- Create: `web/playwright.config.ts`、`web/e2e/fixtures.ts`、`web/e2e/transactions.spec.ts`、`web/e2e/auth.spec.ts`
- Modify: `web/package.json`（devDependency `@playwright/test`、script `"e2e": "playwright test"`）、`web/.gitignore`（`/test-results`、`/playwright-report`）

**Interfaces:**
- Consumes：整個 app；API 全部以 `page.route` 模擬，**不接後端**。
- Produces：`e2e/fixtures.ts` 的 `mockApi(page, options?)`：模擬 `/api/me`（1 本帳本）、`/api/antiforgery/token`（回 200 並在回應設定 `XSRF-TOKEN` cookie）、`/api/books/b1`、`/api/books/b1/transactions*`（GET 回空陣列，POST 回傳以 request body 組出的 `TransactionDto` 並記錄請求）；`options` 可覆寫個別路由。

設定：`webServer: { command: 'npx ng serve --port 4301 --ssl false', url: 'http://localhost:4301', reuseExistingServer: true }`、`use: { baseURL: 'http://localhost:4301', timezoneId: 'Asia/Taipei', locale: 'zh-TW' }`、只用 chromium。安裝瀏覽器（`npx playwright install chromium`，約 150 MB）前先告知使用者。

- [ ] **Step 1：寫失敗測試**：
  - `transactions.spec.ts` › `keyboard_only_continuous_entry`：進入記帳頁 → 焦點在金額 → 輸入 `100` → Tab 到分類 → 輸入「早」→ **按 Enter 選取選項**（斷言此時沒有 POST）→ 再按 Enter → 斷言恰好 1 個 POST、body `amount: -100` → 斷言焦點回到金額 → 重複第二筆 → 共 2 個 POST。
  - `transactions.spec.ts` › `shows_locked_message`：POST 回 422 `{ code: 'locked', detail: '…' }` → 頁面顯示 `此日期已鎖帳，不能新增、修改或刪除`。
  - `auth.spec.ts` › `redirects_to_backend_login_on_401`：`/api/me` 回 401，`/auth/login**` 回一段 HTML → 開 `/books/b1/transactions?month=202601` → URL 變成 `/auth/login?returnUrl=%2Fbooks%2Fb1%2Ftransactions%3Fmonth%3D202601`。
- [ ] **Step 2：確認失敗**（在 H 段全部完成後，第一個測試應該能通過才對；若一開始就全綠，用「把 `onSubmit` 的 pending 檢查拿掉」與「把 autocomplete 換成一般 input」做 mutation 確認測試抓得到）。
- [ ] **Step 3：補齊 fixture 與設定。** - [ ] **Step 4：`npx playwright test` 全綠**，`npx ng test --watch=false` 仍全綠。
- [ ] **Step 5：Commit** `test(web): Playwright E2E（鍵盤連續記帳、鎖帳訊息、401 導向）`

### Task I4：Dockerfile、部署文件、檔名規則確認

**Files:**
- Modify: `Dockerfile`、`.dockerignore`、`docs/deploy.md`（§2.1、§5，並新增「前端本機開發」小節）

**Interfaces:**
- Consumes：後端前置 plan 的 `UseSpaHosting`（讀 `/app/wwwroot`）；I2 的 build 輸出。

- [ ] **Step 1：確認雜湊檔名**：`npx ng build` 後列出 `dist/web/browser`，每個 `.js`／`.css`（`ngsw-worker.js`、`safety-worker.js`、`worker-basic.min.js` 除外）都符合 `-[A-Z2-7]{8}\.[a-z0-9]+$`（esbuild 的 base32 雜湊）。不符合時停下來回報（要回頭改後端 `SpaHosting.IsHashedAsset`）。
- [ ] **Step 2：Dockerfile** 加一個 stage：

```dockerfile
FROM node:24-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npx ng build
```

  runtime stage 加 `COPY --from=web /web/dist/web/browser /app/wwwroot`。`.dockerignore` 加 `web/node_modules/`、`web/dist/`、`web/.angular/`、`web/test-results/`、`web/playwright-report/`、`web/e2e/`。`reference/` 與 `.env*` 的排除不變。
- [ ] **Step 3：`docs/deploy.md`**：§2.1 說明 image 內含前端；§5 改成兩種本機登入方式（直接用後端 `https://localhost:5001` 與透過 `ng serve` 的 `https://localhost:4300`，各自的 redirect URI），並寫下 4200 被 Windows 保留的事實與 `netsh` 檢查指令；新增「前端本機開發」小節：啟動順序、proxy 不要設 `changeOrigin`、proxy 設定改了要重啟。
- [ ] **Step 4：驗證**（agent 能做的）：`dotnet test` 數量 ≥ 基準線、`npx ng test --watch=false` 全綠、`npx playwright test` 全綠。`docker build` 與 `deploy.md` §2 的檢查（含 §2.3「image 內沒有個資」、`/`、deep link、`/health`、`Cache-Control`）**交給使用者執行**。
- [ ] **Step 5：Commit** `build: Docker image 內含前端，更新部署與本機開發文件`

### Checkpoint I

使用者執行：`docker build`、`deploy.md` §2 全部檢查、在本機 image 內開 `/` 與 deep link、Chrome 的「安裝應用程式」出現、DevTools › Application › Service Workers 已註冊；**登入流程在 service worker 啟用後仍正常**（`/auth/login` 不會被導回快取的 `index.html`）。之後由使用者決定部署時機。

---

## 附錄：核准前事實查核（2026-10-04，本機 scratchpad spike，已刪除）

| 引用 | 結果 | 證據 | 影響 |
|---|---|---|---|
| Angular CLI 22 需 Node ≥ 24.15.0 | ✓ | 使用者升到 24.21.0 後 `@angular/cli@22.2.1` 正常執行 | — |
| `ng new` 預設 | zoneless、Vitest 5＋jsdom、`@angular/build:unit-test`、TypeScript 6.0 | spike 的 `package.json`、`angular.json` | 測試指令與 harness 寫法以此為準 |
| `setupFiles` 設定 TZ | ✓ | 改成 `'UTC'` 時 `getTimezoneOffset()` 斷言失敗，改回通過 | G1 |
| `--include`／`--filter` 單獨執行測試 | ✓ | 兩者都只跑指定的測試 | 「執行前必讀」的指令 |
| Material harness（button toggle、autocomplete）在 zoneless＋Vitest | ✓ | spike 測試通過 | H3–H5 |
| port 4200 | ✗ `EACCES` | `netsh … excludedportrange` 顯示 4150–4249 被保留 | 使用者決定改用 4300，並已在 Google client 加上 redirect URI |
| dev-server proxy 保留 `Host` | ✓ | echo server 收到 `host: localhost:4500`；沒有 `X-Forwarded-*` | proxy 目標用後端的 https endpoint，不需 forwarded header |
| proxy 的 `headers` 選項 | ✓，但改設定要重啟 `ng serve` | 熱重載時沒生效、重啟後生效 | 寫進 deploy.md |
| 未知路徑由 dev server 回 `index.html` | ✓ | `/books/abc/transactions` 回 200 並含 `app-root` | deep link 在開發時可用 |
| production 雜湊檔名 | `main-DISDLN5L.js`、`styles-OPUTW5UJ.css` | `ng build` | 符合後端 `IsHashedAsset`；I4 再確認一次 |
| `@angular/pwa` 預設 `ngsw-config.json` | 沒有 `navigationUrls`、沒有 `dataGroups` | spike | I2 必須自己加排除規則 |
| `@angular/material`、`@angular/cdk`、`@angular/service-worker` | 22.2.x | `ng add` 後的 `package.json` | — |
| `@playwright/test` 最新版 | 1.63.0 | `npm view` | I3 |
| 後端契約 | ✓ | `TransactionsEndpoints.cs`（PUT body `{version,input}`、DELETE `?version=`、list `budgetMonth`）、`SummaryEndpoints.cs`、`AuthEndpoints.cs`、`AntiforgeryFilter.InvalidTokenTitle`、`ApiExceptionHandler`（422 帶 `code`） | G2、G3 |
| 帳戶與分類規則 | ✓ | `TransactionFactory.RequireAccount`／`RequireCategory`（只檢查分類種類，主分類也可用）、`TransactionBuilder` 的欄位對應 | H2 的表格 |
| 後端本機啟動方式 | 環境變數，`ASPNETCORE_URLS=https://localhost:5001` | `docs/deploy.md` §5；`SixJars.Api.csproj` 沒有 `UserSecretsId` | spec 原本寫 user-secrets，改為環境變數 |

## 執行結果與偏差（2026-10-05，段 G–I 完成後回寫）

| 項目 | 計畫原文 | 實際 | 理由 |
|---|---|---|---|
| 測試數量 | — | 前端 194 個單元測試（24 檔）、Playwright 4 個、後端 375 個 | 後端比基準 369 多 6 個：lazy chunk 雜湊規則 |
| lazy chunk 檔名（I4 Step 1） | 所有 `.js`／`.css` 都是 base32 雜湊 | `main-*`、`styles-*` 是 base32；lazy chunk 是 `chunk-BvxS2djg.js`（大小寫混合 8 字元） | 後端 `IsHashedAsset` 另外接受 `^chunk-[A-Za-z0-9_-]{8}\.js$`，只對 `chunk-` 前綴放寬，`logo-20261004.png` 仍不算雜湊檔 |
| initial bundle 警告門檻（I2） | Angular 預設 500 kB | 700 kB（錯誤門檻仍 1 MB），實際約 591 kB | Material 外框與記帳頁共用元件；精簡外框沒有使用者看得到的效益。壓縮是否生效列入 deploy.md §2.4 實測 |
| 記帳頁初始焦點（I3） | 「進入記帳頁 → 焦點在金額」 | 不自動 focus；只有送出成功後焦點回到金額（spec §4） | 手機上自動 focus 會在每次進入時彈出鍵盤；E2E 改為先以鍵盤選帳戶再 Tab 到金額 |
| E2E 斷言 | 3 個測試 | 另加 `does_not_post_twice_on_rapid_enter`，並斷言 POST 帶 `X-XSRF-TOKEN` | 原本 3 個測試在拿掉 pending 檢查時仍通過 |
| service worker 導覽排除 | `!/auth/**`、`!/api/**` | 再加 `!/health` | 與後端 SPA fallback 的排除清單一致 |
| 換帳本（H6） | — | 表單依帳本重建；編輯中的交易以 `linkedSignal` 保存被點的那筆物件 | 避免沿用舊帳本的規則；避免列表重新載入時清掉編輯中的內容 |
| 總覽空分組（I1） | 未規定 | 沒有帳戶的分組、沒有財務規劃帳戶時的表格都不顯示 | — |
| `.dockerignore` | `.env*` 排除不變 | 改為 `**/.env`、`**/.env.*` | 新增的 `COPY web/ ./` 會把 `web/.env*` 帶進 build context |
| 422 沒有 `detail` | — | 顯示「無法完成此操作，請檢查輸入後再試」 | 避免空白錯誤訊息 |
