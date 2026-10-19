# 在 Web 建立帳本（前端）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL：逐 Task 執行時使用 superpowers:test-driven-development；分派子代理時使用 superpowers:subagent-driven-development；inline 執行時使用 superpowers:executing-plans。每個 Task 的流程都是：失敗測試 → 確認失敗原因 → 最小實作 → 全綠（**而且 exit code 為 0**）→ 一個 commit。

**Goal**：建立者可以從兩個地方建立空白帳本：沒有帳本時的首頁，以及工具列的帳本選單。建立後重新載入 `me`，導向新帳本設定頁的帳戶分頁。工具列的帳本名稱改成選單，同時補上切換帳本的入口。交易頁在帳本沒有可用帳戶時提示先去設定頁；刪除最後一本帳本的警告不再對建立者顯示。

**Architecture**：`classifyError` 新增 `forbidden`（403）。`MeDto` 新增 `canCreateBooks`、`remainingBookQuota`，`BookApi` 新增 `create`。新增 `CreateBookDialog`（只收集輸入、以 `submitted` output 回報，不呼叫 API）與 `BookCreation` service（開對話框、送出、依錯誤種類決定關不關對話框、成功後 reload → 導頁 → 通知），首頁與工具列共用。設定頁以 `?tab=` 決定初始分頁，交易頁以 `currentBook.book()` 判斷是否有未封存帳戶。

**Tech Stack**：Angular 22.2.1、Angular Material／CDK 22.2.1、Vitest 5（`@angular/build:unit-test`）、Playwright 1.63。

- 設計文件：[`docs/superpowers/specs/2026-10-19-create-book-frontend-design.md`](../specs/2026-10-19-create-book-frontend-design.md)（Q 編號來自 issue #7，F 編號來自 spec）
- 後端計畫：[`2026-10-19-create-book-backend.md`](2026-10-19-create-book-backend.md)。**後端 checkpoint 全綠才能開工**；API 形狀以後端 spec 的「API」一節為準。
- 需求：GitHub issue [AdamChang/SixJars-AccountingBook#7](https://github.com/AdamChang/SixJars-AccountingBook/issues/7)

---

## 執行前必讀

### 環境

- 工作目錄：`F:\VibeCode\SixJars-AccountingBook\web`。分支：`claude/create-book`（從 `claude/last-book-warning` 的 `5842d76` 開出），與後端同一條分支，**不是 master**。
- 全部單元測試：`npx ng test --watch=false; "exit=$LASTEXITCODE"`。單一檔案：`npx ng test --watch=false --include src/app/<path>.spec.ts; "exit=$LASTEXITCODE"`。
- E2E：`npx playwright test`（mock API，不需要後端）。單一檔案：`npx playwright test e2e/<name>.spec.ts`。建置：`npx ng build`。

### 基準線（2026-10-19 本機實測，`5842d76`）

- `ng test`：**317 passed（39 個檔案）**，exit code 0。
- Playwright：**10 passed**。
- 開工前再跑一次，確認數字相同（後端計畫不會動到 `web/`）。數字低於基準線，就代表弄壞了東西。各 Task 的 Expected 是**預測**，以實際結果為準，只要只增不減即可。

### `ng test` 一律看 exit code

31ce2c9 的教訓：當時 Vitest 回報全部通過，但輸出裡有 2 個 **Unhandled Errors**，exit code 是 1，卻沒被發現。所以本計畫每一個「跑測試」的步驟都必須同時確認：

1. 輸出最後一行是 `exit=0`；
2. 輸出裡**沒有** `Unhandled Error` 區塊。

任何一項不成立，即使通過數正確也算失敗，要先找出原因（常見原因：非同步動作在測試結束、元件銷毀之後才跑完）。

### 絕對不要碰的檔案

- `web/.angular/`、`web/dist/`、`web/test-results/`、`web/node_modules/`；Angular CLI 可能改動 `angular.json` 的 analytics 設定，**不要 commit 這類變更**。
- `docs/superpowers/` 底下這次 issue 的四份文件（兩份 spec、兩份計畫）由使用者核准後另外 commit，Task 的 commit 不要夾帶。
- 每個 Task 都用明確的路徑 `git add`，**禁止 `git add .`／`git add -A`**。

### 慣例

- 註解與 commit 訊息用繁體中文；元件一律 `ChangeDetectionStrategy.OnPush`，狀態用 signal；測試名稱用 snake_case 英文（比照既有 spec）。
- 對話框不呼叫 API（比照 `DeleteBookDialog`、`SettingDialog`）。本計畫的建立對話框因為「422 時不關閉」（spec F3），改用 `submitted` output 回報輸入，由 `BookCreation` 送出並決定是否關閉。
- 寫入錯誤只處理與這個動作相關的種類；離線、XSRF 等由 interceptor 統一處理（比照 `SettingsPage.run()` 的註解）。
- 每個 Task 至少做一次變異檢查：改壞實作，看到測試以預期的理由變紅，再還原。一個 Task 一個 commit；commit 結尾加上 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`。

---

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `web/src/app/core/book/create-book-dialog.ts`／`.spec.ts` | 建立帳本對話框：名稱、開帳日（預設今天）、顯示送出失敗的訊息 |
| `web/src/app/core/book/book-creation.ts`／`.spec.ts` | 建立流程（spec F1）：開對話框、送出、錯誤處理、reload → 導頁 → 通知 |
| `web/e2e/create-book.spec.ts` | 從工具列選單建立帳本的 e2e |

### 修改

| 檔案 | 改動 |
|---|---|
| `web/src/app/core/errors/api-error.ts`／`api-error.spec.ts` | `forbidden`（403） |
| `web/src/app/core/errors/messages.ts` | `DENIED_MESSAGE` 改寫；新增 `CREATE_FORBIDDEN_MESSAGE`、`NO_BOOKS_CREATOR_MESSAGE` |
| `web/src/app/features/denied/denied.page.spec.ts` | 釘住新的 `DENIED_MESSAGE` |
| `web/src/app/core/api/dto.ts` | `MeDto` 兩個新欄位、`CreateBookBody` |
| `web/src/app/core/api/book-api.ts`／`.spec.ts` | `create` |
| `web/src/app/core/api/session-api.spec.ts`、`core/auth/session.service.spec.ts` | `MeDto` 字面值補上新欄位 |
| `web/src/app/app.ts`／`.html`／`.scss`／`.spec.ts` | 工具列帳本選單 |
| `web/src/app/features/home/home.page.ts`／`.spec.ts` | 建立者的空狀態與「建立帳本」按鈕 |
| `web/src/app/features/settings/settings.page.ts`／`.html`／`.spec.ts` | `?tab=` deep link；`isLastBook` 加上 `!canCreateBooks` |
| `web/src/app/features/transactions/transactions.page.ts`／`.html`／`.scss`／`.spec.ts` | 沒有可用帳戶時的提示 |
| `web/e2e/fixtures.ts` | `me` 預設值補上新欄位 |

### Task 相依順序

```
Task 1 (errors/forbidden) ──┐
Task 2 (MeDto/BookApi) ─────┼─→ Task 4 (BookCreation) ─┬─→ Task 5 (工具列選單) ─┐
Task 3 (CreateBookDialog) ──┘                          └─→ Task 6 (首頁)        ├─→ Task 9 (e2e) ─→ 前端 checkpoint
Task 2 ─────────────────────→ Task 7 (設定頁 ?tab 與 #8) ─────────────────────┤
Task 8 (交易頁提示) ── 獨立 ─────────────────────────────────────────────────┘
```

Task 1、2、3、8 互相獨立；Task 3 只用到既有的 `ApiError` 型別。Task 7 只依賴 Task 2 的 `MeDto` 欄位。Task 5、6 都依賴 Task 4，彼此獨立，但兩者都改到 `messages.ts` 以外的不同檔案，可以並行。

---

## Task 1：`classifyError` 的 403 與登入拒絕說明（F4、F7）

**Files:** Modify: `web/src/app/core/errors/api-error.ts`、`api-error.spec.ts`、`messages.ts`、`web/src/app/features/denied/denied.page.spec.ts`

目前 403 落到 `default`，被當成 `unavailable`：interceptor 會顯示「暫時無法連線」，對「你不在建立者名單」完全是誤導。容易錯的地方：`forbidden` **不要**加進 interceptor 的全域處理，403 目前只有建立帳本會出現，由 `BookCreation` 自己顯示訊息（Task 4）；加進 interceptor 會讓使用者看到兩則通知。`DENIED_MESSAGE` 的現有測試只做 `toContain(DENIED_MESSAGE)`，改文字不會讓它變紅，所以要另外把文字釘住（比照 `home.page.spec.ts` 對 `NO_BOOKS_MESSAGE` 的寫法）。

- [ ] Step 1：寫失敗測試
  - `api-error.spec.ts` 的 `cases` 在 `['401', …]` 之後加一列：

    ```ts
    ['403 -> forbidden', res(403), { kind: 'forbidden' }],
    ```

  - `denied.page.spec.ts` 的 `denied_links_to_backend_login` 結尾加上：

    ```ts
    // ADR 0011：建立者沒有帳本也能登入，說明要涵蓋兩種名單（spec F7）
    expect(DENIED_MESSAGE).toBe('登入未完成：可能是此 Google 帳號不是任何帳本的成員、也不在建立者名單，或登入時取消了授權');
    ```

- [ ] Step 2：分別執行 `npx ng test --watch=false --include src/app/core/errors/api-error.spec.ts; "exit=$LASTEXITCODE"` 與 `…/features/denied/denied.page.spec.ts`。預期失敗原因：
  - api-error：TypeScript 錯誤 `Type '"forbidden"' is not assignable to type …`（`ApiError` 還沒有這個種類）。這是正確的失敗原因；其他編譯錯誤代表測試打錯字。
  - denied：`toBe` 比對失敗，實際值是舊的「不在白名單」文字。
- [ ] Step 3：最小實作
  - `api-error.ts` 的 `ApiError` 在 `{ kind: 'unauthorized' }` 之後加 `| { kind: 'forbidden' }`；`switch` 在 `case 401` 之後加：

    ```ts
        case 403:
          // 已登入但沒有權限：目前只有建立帳本（不在建立者名單，後端 spec Q18）。帳本範圍的拒絕仍是 404
          return { kind: 'forbidden' };
    ```

  - `messages.ts`：

    ```ts
    export const DENIED_MESSAGE = '登入未完成：可能是此 Google 帳號不是任何帳本的成員、也不在建立者名單，或登入時取消了授權';
    ```

- [ ] Step 4：兩個單一檔案全綠，`exit=0`。
- [ ] 變異檢查：拿掉 `case 403`，`403 -> forbidden` 必須變紅（實際值是 `{ kind: 'unavailable' }`）；確認後還原。
- [ ] Step 5：`npx ng test --watch=false; "exit=$LASTEXITCODE"`。Expected：**318 passed（39 個檔案）**、`exit=0`、沒有 Unhandled Errors。
- [ ] Step 6：Commit

```powershell
git add web/src/app/core/errors/api-error.ts web/src/app/core/errors/api-error.spec.ts web/src/app/core/errors/messages.ts web/src/app/features/denied/denied.page.spec.ts
git commit -m "feat(web): 403 分類為 forbidden，登入拒絕說明涵蓋建立者名單" -m "Refs #7" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 2：`MeDto` 新欄位與 `BookApi.create`

**Files:** Modify: `web/src/app/core/api/dto.ts`、`book-api.ts`、`book-api.spec.ts`、`session-api.spec.ts`、`web/src/app/core/auth/session.service.spec.ts`、`web/src/app/app.spec.ts`、`web/src/app/features/home/home.page.spec.ts`、`web/e2e/fixtures.ts`

新欄位是**必填**的（後端一定會回），所以加上之後，所有以 `MeDto` 型別寫的字面值都會編譯失敗，必須在同一個 Task 補齊；否則 `ng test` 整個無法編譯。已查到的位置是下面 Step 3 列出的四個 spec。`settings.page.spec.ts` 的 `flush({...})` 沒有標型別，不會編譯失敗（Task 7 再補）；e2e 是獨立的 TS program，`fixtures.ts` 的預設 `me` 要手動補上，否則 e2e 只是剛好把 `undefined` 當成 `false`。

- [ ] Step 1：在 `book-api.spec.ts` 的 describe 內新增：

```ts
  it('create_posts_name_and_opening_date', async () => {
    const created = firstValueFrom(api.create({ name: '家庭帳本', openingDate: '2026-10-19' }));
    const request = httpTesting.expectOne({ method: 'POST', url: '/api/books' });
    expect(request.request.body).toEqual({ name: '家庭帳本', openingDate: '2026-10-19' });
    request.flush({ id: 'b9' }, { status: 201, statusText: 'Created' });
    expect(await created).toEqual({ id: 'b9' });
  });
```

- [ ] Step 2：`npx ng test --watch=false --include src/app/core/api/book-api.spec.ts; "exit=$LASTEXITCODE"`。預期失敗原因：TypeScript 錯誤 `Property 'create' does not exist on type 'BookApi'`。
- [ ] Step 3：最小實作
  - `dto.ts`：`MeDto` 改為

    ```ts
    // canCreateBooks：是否在建立者名單；remainingBookQuota：還能建立幾本，非建立者為 0（後端 spec D8，前端只負責顯示）
    export interface MeDto {
      subject: string; email: string | null; books: BookSummaryDto[]; canCreateBooks: boolean; remainingBookQuota: number;
    }
    ```

    並在 `BookDto` 之後新增 `export interface CreateBookBody { name: string; openingDate: string }`。
  - `book-api.ts`：import 補上 `CreateBookBody`，在 `get` 之前新增：

    ```ts
      // 建立者建立空白帳本（issue #7）；403 不在建立者名單、422 book-quota-exceeded／duplicate-name
      create(body: CreateBookBody): Observable<{ id: string }> {
        return this.http.post<{ id: string }>('/api/books', body);
      }
    ```

  - 補齊 `MeDto` 字面值（都加上 `canCreateBooks: false, remainingBookQuota: 0`）：
    - `app.spec.ts:34`：`{ subject: 's', email: 'a@b.c', books: [], canCreateBooks: false, remainingBookQuota: 0 }`
    - `session-api.spec.ts:19`：`{ subject: 's', email: null, books: [{ id: 'b1', name: '帳本' }], canCreateBooks: false, remainingBookQuota: 0 }`
    - `session.service.spec.ts:12`：`{ subject: 's1', email: 'a@b.c', books: [], canCreateBooks: false, remainingBookQuota: 0 }`（第 34 行用 spread，不必改）
    - `home.page.spec.ts` 的 `setup`：`{ subject: 's', email: 'a@b.c', books, canCreateBooks: false, remainingBookQuota: 0 }`
  - `e2e/fixtures.ts` 的預設 `me`：

    ```ts
      // 預設不是建立者：既有 e2e 不會看到「新增帳本…」（issue #7）
      await page.route('**/api/me', options.me ?? (route => route.fulfill({
        json: { subject: 'e2e', email: null, books: [{ id: BOOK_ID, name: BOOK.name }], canCreateBooks: false, remainingBookQuota: 0 },
      })));
    ```

- [ ] Step 4：單一檔案全綠，比原本多 1 個，`exit=0`。
- [ ] 變異檢查：把 `create` 的 body 改成 `{ name: body.name }`，`create_posts_name_and_opening_date` 必須變紅；確認後還原。
- [ ] Step 5：`npx ng test --watch=false; "exit=$LASTEXITCODE"`。Expected：**319 passed（39 個檔案）**、`exit=0`。另外執行 `npx playwright test`，Expected：**10 passed**（只動了 fixture）。
- [ ] Step 6：Commit

```powershell
git add web/src/app/core/api/dto.ts web/src/app/core/api/book-api.ts web/src/app/core/api/book-api.spec.ts web/src/app/core/api/session-api.spec.ts web/src/app/core/auth/session.service.spec.ts web/src/app/app.spec.ts web/src/app/features/home/home.page.spec.ts web/e2e/fixtures.ts
git commit -m "feat(web): MeDto 新增 canCreateBooks／remainingBookQuota，BookApi 新增 create" -m "Refs #7" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 3：`CreateBookDialog`（F2）

**Files:** Create: `web/src/app/core/book/create-book-dialog.ts`、`create-book-dialog.spec.ts`

放在 `core/book/`，因為首頁與工具列（`App`）都會用到，不屬於任何一個 feature。容易錯的地方有三個：
1. 開帳日的預設值要用 `toDateString(new Date())`，**不可以**用 `toISOString()`：台北 00:00–08:00 之間會得到前一天。測試把時間固定在台北 07:30 就能抓到這個錯。
2. 只有空白的名稱不能送出（後端的 `NotEmpty` 也會擋，但前端不該送出必定失敗的請求）；送出的名稱要先 trim，成功訊息與同名比對才會一致。
3. 這個對話框**不自己關閉**：送出後由 `BookCreation` 決定要關閉（成功、403）還是留著讓使用者修改（422、400），所以送出用 `submitted` output，錯誤由 `showError()` 顯示，`busy` 由 `BookCreation` 設定以防重複送出。

- [ ] Step 1：建立 `create-book-dialog.spec.ts`：

```ts
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatButtonHarness } from '@angular/material/button/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { MatInputHarness } from '@angular/material/input/testing';
import { CreateBookDialog, NewBookInput } from './create-book-dialog';

async function open() {
  TestBed.configureTestingModule({ providers: [{ provide: MatDialogRef, useValue: { close: vi.fn() } }] });
  const fixture = TestBed.createComponent(CreateBookDialog);
  const submitted: NewBookInput[] = [];
  fixture.componentInstance.submitted.subscribe(input => submitted.push(input));
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  return {
    fixture, submitted, el: fixture.nativeElement as HTMLElement,
    createButton: () => loader.getHarness(MatButtonHarness.with({ text: '建立' })),
    nameInput: () => loader.getHarness(MatInputHarness.with({ selector: '.name input' })),
    dateInput: () => loader.getHarness(MatInputHarness.with({ selector: '.opening-date input' })),
  };
}

describe('CreateBookDialog', () => {
  afterEach(() => vi.useRealTimers());

  it('defaults_opening_date_to_local_today_and_requires_a_name', async () => {
    // 台北 07:30 是 UTC 前一天 23:30：用 toISOString 會得到 2026-10-18
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 19, 7, 30));
    const { createButton, nameInput, dateInput } = await open();

    expect(await (await dateInput()).getValue()).toBe('2026-10-19');
    expect(await (await createButton()).isDisabled()).toBe(true);

    await (await nameInput()).setValue('   ');
    expect(await (await createButton()).isDisabled()).toBe(true);

    await (await nameInput()).setValue(' 家庭帳本 ');
    expect(await (await createButton()).isDisabled()).toBe(false);
  });

  it('create_emits_trimmed_name_and_opening_date', async () => {
    const { createButton, nameInput, dateInput, submitted } = await open();
    await (await nameInput()).setValue(' 家庭帳本 ');
    await (await dateInput()).setValue('2026-01-01');

    await (await createButton()).click();

    expect(submitted).toEqual([{ name: '家庭帳本', openingDate: '2026-01-01' }]);
  });

  it('busy_disables_create_to_prevent_double_submit', async () => {
    const { fixture, createButton, nameInput } = await open();
    await (await nameInput()).setValue('家庭帳本');

    fixture.componentInstance.busy.set(true);
    await fixture.whenStable();

    expect(await (await createButton()).isDisabled()).toBe(true);
  });

  it('show_error_displays_backend_messages_and_keeps_input', async () => {
    const { fixture, el, nameInput } = await open();
    await (await nameInput()).setValue('家庭帳本');

    fixture.componentInstance.showError({ kind: 'domain', code: 'duplicate-name', message: '你已經有名稱為「家庭帳本」的帳本。' });
    await fixture.whenStable();
    expect(el.querySelector('.server-error')!.textContent).toContain('你已經有名稱為「家庭帳本」的帳本。');
    expect(await (await nameInput()).getValue()).toBe('家庭帳本');

    fixture.componentInstance.showError({ kind: 'validation', fieldErrors: { name: ['名稱不可超過 100 個字。'] } });
    await fixture.whenStable();
    expect(el.querySelector('.name-error')!.textContent).toContain('名稱不可超過 100 個字。');
    expect(el.querySelector('.server-error')).toBeNull();
  });
});
```

- [ ] Step 2：`npx ng test --watch=false --include src/app/core/book/create-book-dialog.spec.ts; "exit=$LASTEXITCODE"`。預期失敗原因：找不到模組 `./create-book-dialog`。
- [ ] Step 3：建立 `create-book-dialog.ts`：

```ts
import { ChangeDetectionStrategy, Component, computed, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { toDateString } from '../../shared/dates';
import { ApiError } from '../errors/api-error';

export interface NewBookInput { name: string; openingDate: string }

// 與後端 CreateBookValidator（BookConfiguration 的名稱長度）一致
const NAME_MAX_LENGTH = 100;

// 建立帳本的輸入（spec F2）。不呼叫 API、也不自己關閉：由 BookCreation 送出，
// 失敗時呼叫 showError 讓使用者修改後再送（spec F3：422 不關閉對話框）。
@Component({
  selector: 'app-create-book-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>建立帳本</h2>
    <mat-dialog-content>
      <mat-form-field class="name">
        <mat-label>帳本名稱</mat-label>
        <input matInput autocomplete="off" [attr.maxlength]="nameMaxLength" [value]="name()"
               (input)="name.set($any($event.target).value)" />
      </mat-form-field>
      @if (nameError(); as message) {
        <p class="error name-error">{{ message }}</p>
      }
      <mat-form-field class="opening-date">
        <mat-label>開帳日</mat-label>
        <input matInput type="date" [value]="openingDate()" (input)="openingDate.set($any($event.target).value)" />
      </mat-form-field>
      <p class="hint">所有帳戶的期初餘額都以開帳日結束時為準；建立後可以在設定頁新增帳戶。</p>
      @if (serverError(); as message) {
        <p class="error server-error">{{ message }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton type="button" mat-dialog-close>取消</button>
      <button matButton="filled" type="button" class="create" [disabled]="!canSubmit()" (click)="submit()">建立</button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-form-field { display: block; }
    .hint { font-size: 0.875rem; opacity: 0.75; }
    .error { margin: 0 0 12px; color: var(--mat-sys-error); }
  `,
})
export class CreateBookDialog {
  readonly submitted = output<NewBookInput>();
  // 送出中由 BookCreation 設定，避免重複送出
  readonly busy = signal(false);

  protected readonly nameMaxLength = NAME_MAX_LENGTH;
  protected readonly name = signal('');
  // 不可用 toISOString：UTC+8 在 00:00–08:00 會得到前一天（shared/dates）
  protected readonly openingDate = signal(toDateString(new Date()));
  protected readonly nameError = signal<string | null>(null);
  protected readonly serverError = signal<string | null>(null);
  protected readonly canSubmit = computed(() => {
    const name = this.name().trim();
    return !this.busy() && name.length > 0 && name.length <= NAME_MAX_LENGTH && this.openingDate() !== '';
  });

  // 送出失敗、對話框不關：名稱的 validation 錯誤顯示在欄位下，其餘（同名、名額用完）顯示在對話框下方
  showError(error: ApiError): void {
    this.nameError.set(null);
    this.serverError.set(null);
    switch (error.kind) {
      case 'domain':
        this.serverError.set(error.message);
        break;
      case 'validation': {
        const nameErrors = error.fieldErrors['name'];
        if (nameErrors?.length) {
          this.nameError.set(nameErrors[0]);
        } else {
          this.serverError.set(Object.values(error.fieldErrors)[0]?.[0] ?? '輸入的資料不正確');
        }
        break;
      }
    }
  }

  protected submit(): void {
    this.nameError.set(null);
    this.serverError.set(null);
    this.submitted.emit({ name: this.name().trim(), openingDate: this.openingDate() });
  }
}
```

- [ ] Step 4：單一檔案，**4 passed**，`exit=0`。
- [ ] 變異檢查：把 `canSubmit` 的 `this.name().trim()` 改成 `this.name()`，`defaults_opening_date_to_local_today_and_requires_a_name` 必須在「只有空白」那一步變紅；確認後還原。（選做：把預設值改成 `new Date().toISOString().slice(0, 10)`，同一個測試應在日期那一步變紅。）
- [ ] Step 5：`npx ng test --watch=false; "exit=$LASTEXITCODE"`。Expected：**323 passed（40 個檔案）**、`exit=0`。
- [ ] Step 6：Commit

```powershell
git add web/src/app/core/book/create-book-dialog.ts web/src/app/core/book/create-book-dialog.spec.ts
git commit -m "feat(web): 建立帳本對話框（名稱、開帳日預設今天，送出失敗時不關閉）" -m "Refs #7" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 4：`BookCreation` 建立流程（F1、F3、Q21）

**Files:** Create: `web/src/app/core/book/book-creation.ts`、`book-creation.spec.ts` / Modify: `web/src/app/core/errors/messages.ts`

首頁與工具列共用的流程。容易錯的地方：
1. **順序**（Q21）：先 `await session.reload()`，再導頁，最後顯示訊息。先導頁的話，工具列選單會短暫少了新帳本；測試用 `mock.invocationCallOrder` 直接比對順序（和刪除帳本不同，這裡 spy 的呼叫順序就是要驗證的東西）。
2. **403**：關閉對話框、顯示「不在建立者名單」，並 reload `me`（名單可能剛被移除，`canCreateBooks` 要跟著變成 false，入口才會消失）。Task 1 已經讓 interceptor 不會再額外顯示「暫時無法連線」，測試用 `toHaveBeenCalledTimes(1)` 守住這一點。
3. **422／400**：對話框不關，訊息交給 `showError`；**離線等其他錯誤**也不關（interceptor 已通知），讓使用者可以直接再按一次。所有錯誤都要把 `busy` 設回 false，否則按鈕永遠停用。

測試直接 mock `MatDialog`，回傳假的對話框實例（`submitted` 用 `Subject`、`busy` 用真的 signal）。對話框本身的行為已在 Task 3 測過，這裡只驗證流程，也避開 overlay 動畫造成的時序問題。

- [ ] Step 1：建立 `book-creation.spec.ts`：

```ts
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Router, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { MeDto } from '../api/dto';
import { SessionService } from '../auth/session.service';
import { BrowserLocation } from '../browser-location';
import { apiErrorInterceptor } from '../errors/error-interceptor';
import { CREATE_FORBIDDEN_MESSAGE, UNAVAILABLE_MESSAGE } from '../errors/messages';
import { Notifier } from '../errors/notifier';
import { BookCreation } from './book-creation';
import { NewBookInput } from './create-book-dialog';

const INPUT: NewBookInput = { name: '家庭帳本', openingDate: '2026-10-19' };
const ME: MeDto = { subject: 's', email: 'a@b.c', books: [], canCreateBooks: true, remainingBookQuota: 5 };

function setup() {
  const dialog = { submitted: new Subject<NewBookInput>(), busy: signal(false), showError: vi.fn() };
  const close = vi.fn();
  const notifier = { show: vi.fn() };
  const reload = vi.fn(async () => ME);
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([apiErrorInterceptor])), provideHttpClientTesting(),
      provideRouter([]),
      { provide: MatDialog, useValue: { open: vi.fn(() => ({ componentInstance: dialog, close })) } },
      { provide: Notifier, useValue: notifier },
      { provide: SessionService, useValue: { reload } },
      { provide: BrowserLocation, useValue: { assign: vi.fn(), currentPath: () => '/' } },
    ],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  const httpTesting = TestBed.inject(HttpTestingController);
  TestBed.inject(BookCreation).start();
  // 使用者在對話框按「建立」，回傳送出的 POST
  const submit = () => {
    dialog.submitted.next(INPUT);
    return httpTesting.expectOne({ method: 'POST', url: '/api/books' });
  };
  return { dialog, close, notifier, reload, navigate, httpTesting, submit };
}

describe('BookCreation', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('success_reloads_session_then_opens_accounts_tab_of_new_book', async () => {
    const { submit, dialog, close, notifier, reload, navigate } = setup();

    const request = submit();
    expect(request.request.body).toEqual(INPUT);
    expect(dialog.busy()).toBe(true);
    request.flush({ id: 'b9' }, { status: 201, statusText: 'Created' });

    await vi.waitFor(() => expect(notifier.show).toHaveBeenCalledWith('已建立帳本「家庭帳本」'));
    expect(close).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/books', 'b9', 'settings'], { queryParams: { tab: 'accounts' } });
    // Q21：me 要先包含新帳本，工具列選單才看得到它
    expect(reload.mock.invocationCallOrder[0]).toBeLessThan(navigate.mock.invocationCallOrder[0]);
  });

  it('domain_error_keeps_dialog_open_with_backend_message', () => {
    const { submit, dialog, close, navigate } = setup();

    submit().flush({ code: 'duplicate-name', detail: '你已經有名稱為「家庭帳本」的帳本。' }, { status: 422, statusText: '' });

    expect(dialog.showError).toHaveBeenCalledWith({ kind: 'domain', code: 'duplicate-name', message: '你已經有名稱為「家庭帳本」的帳本。' });
    expect(dialog.busy()).toBe(false);
    expect(close).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('forbidden_closes_dialog_and_reloads_session', () => {
    const { submit, close, notifier, reload } = setup();

    submit().flush(null, { status: 403, statusText: '' });

    expect(close).toHaveBeenCalled();
    // interceptor 不會再額外顯示「暫時無法連線」（Task 1）
    expect(notifier.show).toHaveBeenCalledTimes(1);
    expect(notifier.show).toHaveBeenCalledWith(CREATE_FORBIDDEN_MESSAGE);
    expect(reload).toHaveBeenCalled();
  });

  it('unavailable_keeps_dialog_open_for_retry', () => {
    const { submit, dialog, close, notifier } = setup();

    submit().flush(null, { status: 503, statusText: '' });

    expect(notifier.show).toHaveBeenCalledWith(UNAVAILABLE_MESSAGE);
    expect(dialog.busy()).toBe(false);
    expect(dialog.showError).not.toHaveBeenCalled();
    expect(close).not.toHaveBeenCalled();
  });
});
```

- [ ] Step 2：`npx ng test --watch=false --include src/app/core/book/book-creation.spec.ts; "exit=$LASTEXITCODE"`。預期失敗原因：找不到模組 `./book-creation`，以及 `CREATE_FORBIDDEN_MESSAGE` 不存在。
- [ ] Step 3：最小實作
  - `messages.ts` 新增：

    ```ts
    export const CREATE_FORBIDDEN_MESSAGE = '你不在建立者名單，無法建立帳本';
    ```

  - 建立 `book-creation.ts`：

```ts
import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { BookApi } from '../api/book-api';
import { SessionService } from '../auth/session.service';
import { ApiError } from '../errors/api-error';
import { CREATE_FORBIDDEN_MESSAGE } from '../errors/messages';
import { Notifier } from '../errors/notifier';
import { CreateBookDialog, NewBookInput } from './create-book-dialog';

// 建立帳本的流程（spec F1）：首頁的空狀態與工具列選單共用
@Injectable({ providedIn: 'root' })
export class BookCreation {
  private readonly dialog = inject(MatDialog);
  private readonly api = inject(BookApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly notifier = inject(Notifier);

  start(): void {
    const ref = this.dialog.open<CreateBookDialog, void, void>(CreateBookDialog);
    const dialog = ref.componentInstance;
    // output 在對話框銷毀時自動結束訂閱
    dialog.submitted.subscribe((input: NewBookInput) => {
      dialog.busy.set(true);
      this.api.create(input).subscribe({
        next: ({ id }) => {
          ref.close();
          void this.enter(id, input.name);
        },
        error: (error: ApiError) => {
          dialog.busy.set(false);
          switch (error.kind) {
            case 'forbidden':
              // 名單可能剛被移除：重新載入 me，canCreateBooks 變成 false，入口跟著消失
              ref.close();
              this.notifier.show(CREATE_FORBIDDEN_MESSAGE);
              void this.session.reload();
              break;
            case 'domain':
            case 'validation':
              // 同名、名額用完、名稱過長：對話框不關，讓使用者修改後再送（spec F3）
              dialog.showError(error);
              break;
            // 離線、XSRF 等已由 interceptor 通知；對話框留著，可以直接再按一次
          }
        },
      });
    });
  }

  // spec Q21：先重新載入 me（工具列選單與首頁才看得到新帳本），再進入新帳本的帳戶分頁
  private async enter(bookId: string, name: string): Promise<void> {
    await this.session.reload();
    await this.router.navigate(['/books', bookId, 'settings'], { queryParams: { tab: 'accounts' } });
    this.notifier.show(`已建立帳本「${name}」`);
  }
}
```

- [ ] Step 4：單一檔案，**4 passed**，`exit=0`。
- [ ] 變異檢查：
  - 把 `enter` 的前兩行對調（先導頁再 reload），`success_reloads_session_then_opens_accounts_tab_of_new_book` 必須在 `invocationCallOrder` 那一行變紅。
  - 把 `error` 裡的 `dialog.busy.set(false);` 拿掉，`domain_error_keeps_dialog_open_with_backend_message` 必須變紅。
  - 各自還原。
- [ ] Step 5：`npx ng test --watch=false; "exit=$LASTEXITCODE"`。Expected：**327 passed（41 個檔案）**、`exit=0`。
- [ ] Step 6：Commit

```powershell
git add web/src/app/core/book/book-creation.ts web/src/app/core/book/book-creation.spec.ts web/src/app/core/errors/messages.ts
git commit -m "feat(web): 建立帳本流程（成功後 reload me 再進入帳戶分頁，422 不關對話框）" -m "Refs #7" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 5：工具列的帳本選單（Q23、Q15）

**Files:** Modify: `web/src/app/app.ts`、`app.html`、`app.scss`、`app.spec.ts`

容易錯的地方：
1. `App` 會注入 `BookCreation`，而 `BookCreation` 依賴 `BookApi`→`HttpClient`；`app.spec.ts` 沒有提供 `HttpClient`，**既有的兩個測試會因 NullInjectorError 變紅**。所以 setup 要提供 `BookCreation` 的 mock。
2. 選單內容在 overlay 裡，不在 `mat-toolbar` 底下：既有測試 `shell_shows_book_name_email_and_logout` 只看 toolbar 裡的 `<a>`，不受影響；新測試要從 `document` 找選單項目。
3. 「目前這本」的勾號用 CSS `::before`，不寫進文字，`MatMenuItemHarness.getText()` 才會是乾淨的帳本名稱。選單元素宣告在 `App` 的 template，emulated encapsulation 的樣式仍套得到；但**不要**用 `mat-menu` 的 panel class 當選擇器的一部分（panel 是 Material 產生的，沒有 `App` 的屬性）。
4. 「已達 5 本上限」的 5 是複製後端 `CreateBook.MaxOwnedBooks` 的文字（spec Q15 指定的文案）。後端改上限時要一起改，註解寫明。

- [ ] Step 1：改寫 `app.spec.ts`：

```ts
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { provideRouter } from '@angular/router';
import { BookDto, MeDto } from './core/api/dto';
import { SessionService } from './core/auth/session.service';
import { BookCreation } from './core/book/book-creation';
import { CurrentBook } from './core/book/current-book';
import { LoadingIndicator } from './core/loading/loading-indicator';
import { App } from './app';

const BOOK = { id: 'b1', name: '我的帳本' };
const me = (overrides: Partial<MeDto> = {}): MeDto => ({
  subject: 's', email: 'a@b.c', canCreateBooks: false, remainingBookQuota: 0,
  books: [BOOK, { id: 'b2', name: '另一本' }], ...overrides,
});

function setup(user?: MeDto, book?: Partial<BookDto>) {
  const logout = vi.fn();
  const start = vi.fn();
  TestBed.configureTestingModule({
    imports: [App],
    providers: [
      provideRouter([]),
      { provide: SessionService, useValue: { me: signal(user), logout } },
      { provide: CurrentBook, useValue: { book: signal(book) } },
      { provide: LoadingIndicator, useValue: { visible: signal(false) } },
      // 真的 BookCreation 需要 HttpClient 與 MatDialog；流程本身在 book-creation.spec 測
      { provide: BookCreation, useValue: { start } },
    ],
  });
  const fixture = TestBed.createComponent(App);
  fixture.detectChanges();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  const openBookMenu = async () => {
    const menu = await loader.getHarness(MatMenuHarness.with({ selector: '.book-name' }));
    await menu.open();
    return menu;
  };
  return { fixture, logout, start, openBookMenu, el: fixture.nativeElement as HTMLElement };
}

describe('App', () => {
  it('shell_hides_toolbar_before_sign_in', () => {
    const { el } = setup();
    expect(el.querySelector('mat-toolbar')).toBeNull();
  });

  it('shell_shows_book_name_email_and_logout', () => {
    const { el, logout, fixture } = setup(me({ books: [] }), BOOK);
    const toolbar = el.querySelector('mat-toolbar')!;
    expect(toolbar.textContent).toContain('我的帳本');
    expect(toolbar.textContent).toContain('a@b.c');
    const hrefs = Array.from(toolbar.querySelectorAll('a')).map((a) => a.getAttribute('href'));
    expect(hrefs).toEqual(['/books/b1/transactions', '/books/b1/summary', '/books/b1/planned-expenses', '/books/b1/budgets', '/books/b1/settings']);
    const button = Array.from(toolbar.querySelectorAll('button')).find((b) => b.textContent?.includes('登出'))!;
    button.click();
    fixture.detectChanges();
    expect(logout).toHaveBeenCalled();
  });

  // Q23：帳本名稱是選單，可以切換帳本；非建立者看不到「新增帳本…」（Q15）
  it('book_menu_lists_books_and_marks_current_one', async () => {
    const { openBookMenu } = setup(me(), BOOK);
    const menu = await openBookMenu();

    const items = await menu.getItems();
    expect(await Promise.all(items.map(item => item.getText()))).toEqual(['我的帳本', '另一本']);
    const links = [...document.querySelectorAll<HTMLAnchorElement>('a.book-item')];
    expect(links.map(a => a.getAttribute('href'))).toEqual(['/books/b1/transactions', '/books/b2/transactions']);
    expect(links.map(a => a.getAttribute('aria-current'))).toEqual(['true', null]);
  });

  it('creator_starts_book_creation_from_menu', async () => {
    const { openBookMenu, start } = setup(me({ canCreateBooks: true, remainingBookQuota: 3 }), BOOK);
    const menu = await openBookMenu();
    expect(document.querySelector('.quota-hint')).toBeNull();

    await menu.clickItem({ text: '新增帳本…' });

    expect(start).toHaveBeenCalledTimes(1);
  });

  // Q15：名額用完時停用並說明原因，而不是隱藏
  it('create_is_disabled_with_hint_when_quota_is_used_up', async () => {
    const { openBookMenu } = setup(me({ canCreateBooks: true, remainingBookQuota: 0 }), BOOK);
    const menu = await openBookMenu();

    const [create] = await menu.getItems({ text: '新增帳本…' });
    expect(await create.isDisabled()).toBe(true);
    expect(document.querySelector('.quota-hint')!.textContent!.trim()).toBe('已達 5 本上限');
  });
});
```

- [ ] Step 2：`npx ng test --watch=false --include src/app/app.spec.ts; "exit=$LASTEXITCODE"`。預期失敗原因：3 個新測試失敗在 `MatMenuHarness` 找不到 `.book-name` 的選單觸發器（目前是 `<span>`）。既有的 2 個測試必須維持綠燈（`BookCreation` 已 mock）。
- [ ] Step 3：最小實作
  - `app.ts`：import `MatMenuModule`（`@angular/material/menu`）、`MatDividerModule`（`@angular/material/divider`）、`BookCreation`，加進 `imports`；注入 `protected readonly bookCreation = inject(BookCreation);`。
  - `app.html`：把 `<span class="book-name">{{ book.name }}</span>` 換成：

    ```html
          <!-- Q23：帳本名稱是選單，切換帳本與「新增帳本…」 -->
          <button mat-button type="button" class="book-name" [matMenuTriggerFor]="bookMenu">{{ book.name }} ▾</button>
          <mat-menu #bookMenu="matMenu">
            @for (item of me.books; track item.id) {
              <a mat-menu-item class="book-item" [routerLink]="['/books', item.id, 'transactions']"
                 [attr.aria-current]="item.id === book.id ? 'true' : null">{{ item.name }}</a>
            }
            @if (me.canCreateBooks) {
              <mat-divider />
              <button mat-menu-item type="button" class="create-book" [disabled]="me.remainingBookQuota === 0"
                      (click)="bookCreation.start()">新增帳本…</button>
              @if (me.remainingBookQuota === 0) {
                <!-- 5 與後端 CreateBook.MaxOwnedBooks 相同（spec Q15 的文案）；後端改上限時一起改 -->
                <p class="quota-hint">已達 5 本上限</p>
              }
            }
          </mat-menu>
    ```

  - `app.scss` 在 `.book-name` 之後新增：

    ```scss
    // 帳本選單：目前這本前面打勾。選單在 overlay 裡，但元素宣告在本元件的 template，樣式仍套得到
    .book-item[aria-current='true']::before {
      content: '✓';
      margin-right: 8px;
    }

    .quota-hint {
      margin: 0;
      padding: 0 16px 8px;
      font-size: 0.75rem;
      opacity: 0.7;
    }
    ```

- [ ] Step 4：單一檔案，**5 passed**，`exit=0`。
- [ ] 變異檢查：把 `[disabled]="me.remainingBookQuota === 0"` 改成 `[disabled]="false"`，`create_is_disabled_with_hint_when_quota_is_used_up` 必須變紅；把 `@if (me.canCreateBooks)` 改成 `@if (true)`，`book_menu_lists_books_and_marks_current_one` 必須變紅（多出「新增帳本…」）。各自還原。
- [ ] Step 5：`npx ng test --watch=false; "exit=$LASTEXITCODE"`。Expected：**330 passed（41 個檔案）**、`exit=0`。另外執行 `npx ng build`：`App` 與首頁是 eager 載入，這個 Task 讓 initial bundle 多了 dialog／menu／form-field；確認沒有超過 `angular.json` 的 initial budget（warning 700kB）。**出現 budget warning 就停下來回報**，備案是在 `BookCreation.start()` 裡以 `await import('./create-book-dialog')` 延遲載入對話框。
- [ ] Step 6：Commit

```powershell
git add web/src/app/app.ts web/src/app/app.html web/src/app/app.scss web/src/app/app.spec.ts
git commit -m "feat(web): 工具列帳本名稱改成選單，可以切換帳本與新增帳本" -m "Refs #7" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 6：首頁空狀態的「建立帳本」（Q15）

**Files:** Modify: `web/src/app/features/home/home.page.ts`、`home.page.spec.ts`、`web/src/app/core/errors/messages.ts`

建立者沒有帳本也能登入（ADR 0011），首頁就是他唯一的入口。容易錯的地方：`books` 是建構時讀一次的值（現狀，給「只有一本就導入」用），但 `canCreateBooks` 要**跟著 `me` 變**：建立時收到 403，`BookCreation` 會 reload `me`，按鈕要跟著消失、改回請管理者協助的說明。所以 `canCreateBooks` 用 `computed`，測試也要改一次 `me` 驗證這件事。

- [ ] Step 1：`home.page.spec.ts`
  - import 補上 `BookCreation`（`../../core/book/book-creation`）與 `NO_BOOKS_CREATOR_MESSAGE`。
  - `setup` 改為：

    ```ts
    function setup(books: MeDto['books'], canCreateBooks = false) {
      const me = signal<MeDto | undefined>({
        subject: 's', email: 'a@b.c', books, canCreateBooks, remainingBookQuota: canCreateBooks ? 5 : 0,
      });
      const start = vi.fn();
      TestBed.configureTestingModule({
        providers: [provideRouter([]), { provide: SessionService, useValue: { me } }, { provide: BookCreation, useValue: { start } }],
      });
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      const fixture = TestBed.createComponent(HomePage);
      fixture.detectChanges();
      return { fixture, navigate, start, me, el: fixture.nativeElement as HTMLElement };
    }
    ```

  - 新增：

    ```ts
      // 建立者沒有帳本也能登入（ADR 0011）：首頁直接給建立的入口（spec Q15）
      it('home_offers_book_creation_to_creators_without_books', async () => {
        const { el, start, me, fixture } = setup([], true);
        expect(el.textContent).toContain(NO_BOOKS_CREATOR_MESSAGE);
        expect(el.textContent).not.toContain(NO_BOOKS_MESSAGE);
        el.querySelector<HTMLButtonElement>('button.create-book')!.click();
        expect(start).toHaveBeenCalledTimes(1);

        // 建立時收到 403 會重新載入 me：被移出名單後回到請管理者協助的說明
        me.update(value => ({ ...value!, canCreateBooks: false, remainingBookQuota: 0 }));
        await fixture.whenStable();
        expect(el.querySelector('button.create-book')).toBeNull();
        expect(el.textContent).toContain(NO_BOOKS_MESSAGE);
      });
    ```

- [ ] Step 2：`npx ng test --watch=false --include src/app/features/home/home.page.spec.ts; "exit=$LASTEXITCODE"`。預期失敗原因：`NO_BOOKS_CREATOR_MESSAGE` 不存在（編譯錯誤）；補上常數後再跑一次，新測試失敗在「畫面沒有 `NO_BOOKS_CREATOR_MESSAGE`」。既有 3 個測試必須維持綠燈。
- [ ] Step 3：最小實作
  - `messages.ts` 新增：

    ```ts
    export const NO_BOOKS_CREATOR_MESSAGE = '你還沒有任何帳本。建立一本空白帳本，再到設定頁新增帳戶，就可以開始記帳。';
    ```

  - `home.page.ts`：import 補上 `computed`、`MatButtonModule`、`BookCreation`、`NO_BOOKS_CREATOR_MESSAGE`；`imports` 加上 `MatButtonModule`。template 的空狀態改為：

    ```html
        @if (books.length === 0) {
          @if (canCreateBooks()) {
            <p class="message">{{ creatorMessage }}</p>
            <button matButton="filled" type="button" class="create-book" (click)="bookCreation.start()">建立帳本</button>
          } @else {
            <p class="message">{{ noBooksMessage }}</p>
          }
        } @else if (books.length > 1) {
    ```

    styles 加上 `.create-book { margin-left: 16px; }`。類別改為：

    ```ts
    export class HomePage {
      private readonly session = inject(SessionService);
      protected readonly bookCreation = inject(BookCreation);
      protected readonly noBooksMessage = NO_BOOKS_MESSAGE;
      protected readonly creatorMessage = NO_BOOKS_CREATOR_MESSAGE;
      // authGuard 已確保 me 載入完成
      protected readonly books = this.session.me()!.books;
      // 跟著 me 變：建立時 403 會重新載入 me，被移出名單後按鈕要消失
      protected readonly canCreateBooks = computed(() => this.session.me()?.canCreateBooks ?? false);

      constructor() { /* 原本只有一本時直接進入的邏輯不變 */ }
    }
    ```

- [ ] Step 4：單一檔案，**4 passed**，`exit=0`。
- [ ] 變異檢查：把 `canCreateBooks` 改成建構時讀一次的值 `protected readonly canCreateBooks = signal(this.session.me()!.canCreateBooks);`（需 import `signal`），新測試必須在後半段（按鈕仍在）變紅；確認後還原。
- [ ] Step 5：`npx ng test --watch=false; "exit=$LASTEXITCODE"`。Expected：**331 passed（41 個檔案）**、`exit=0`。
- [ ] Step 6：Commit

```powershell
git add web/src/app/features/home/home.page.ts web/src/app/features/home/home.page.spec.ts web/src/app/core/errors/messages.ts
git commit -m "feat(web): 建立者沒有帳本時，首頁提供建立帳本的按鈕" -m "Refs #7" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 7：設定頁 `?tab=` deep link 與 #8 的警告條件（F5、#8）

**Files:** Modify: `web/src/app/features/settings/settings.page.ts`、`settings.page.html`、`settings.page.spec.ts`

容易錯的地方：
1. **`ActivatedRoute`**：`settings.page.spec.ts` 沒有 `provideRouter`，而 Angular 22.2.1 的 `ActivatedRoute` 只由 `provideRouter`／`RouterModule` 提供（`_router_module-chunk.mjs`）；頁面一注入它，**既有的 12 個設定頁測試全部會 NullInjectorError**。setup 改為提供一個假的 `ActivatedRoute`（只有 `queryParamMap`）。
2. **路由重用**：在帳本 A 的設定頁用工具列建立帳本 B，會從 `/books/A/settings` 導到 `/books/B/settings?tab=accounts`，Angular 會**重用同一個 `SettingsPage`**。只在建構時讀一次 `?tab=` 的話，B 不會落在帳戶分頁。所以初始分頁要在「換帳本」時重新套用。
3. **但不能在重新載入同一本時套用**：每次寫入後 `run()` 都會 `currentBook.reload()`，`book()` 會換成新物件。來源如果是 `{ bookId, tab }` 物件，每次儲存都會把使用者拉回 `?tab=` 的分頁。所以來源 `computed` 要自訂 `equal`，只比較 `bookId` 與 `tab`。
4. 分頁要雙向綁定 `[(selectedIndex)]`：單向綁定時，使用者手動換分頁後 signal 的值沒變，換帳本時重新算出同一個值也不會觸發更新。

- [ ] Step 1：`settings.page.spec.ts`
  - import：`ActivatedRoute, Router, convertToParamMap` 自 `@angular/router`；`of` 自 `rxjs`。
  - `setup` 改為接受參數，providers 加上假的 `ActivatedRoute`，回傳值加上 `selectedTab`：

    ```ts
    async function setup(options: { tab?: string } = {}) {
      const notifier = { show: vi.fn() };
      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(withInterceptors([apiErrorInterceptor])), provideHttpClientTesting(),
          { provide: Notifier, useValue: notifier },
          { provide: BrowserLocation, useValue: { assign: vi.fn(), currentPath: () => '/' } },
          // 頁面只讀 queryParamMap（spec F5）；不用 provideRouter，避免真的路由介入
          { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap(options.tab ? { tab: options.tab } : {})) } },
        ],
      });
      // …其餘不變…
      const selectedTab = async () => (await (await loader.getHarness(MatTabGroupHarness)).getSelectedTab()).getLabel();
      return { fixture, loader, rootLoader, el, httpTesting, notifier, openTab, rowLabels, expectReload, selectedTab };
    }
    ```

  - `signIn` 改為可指定是否為建立者：

    ```ts
      async function signIn(ctx: Awaited<ReturnType<typeof setup>>, books: MeDto['books'], canCreateBooks = false) {
        const loaded = TestBed.inject(SessionService).load();
        const me: MeDto = { subject: 's', email: null, books, canCreateBooks, remainingBookQuota: canCreateBooks ? 4 : 0 };
        ctx.httpTesting.expectOne('/api/me').flush(me);
        await vi.waitFor(() => ctx.httpTesting.expectOne('/api/antiforgery/token').flush(null));
        await loaded;
      }
    ```

  - 新增四個測試：

    ```ts
      it('tab_query_param_opens_that_tab', async () => {
        const { selectedTab } = await setup({ tab: 'accounts' });
        expect(await selectedTab()).toBe('帳戶');
      });

      it('unknown_tab_query_param_opens_first_tab', async () => {
        const { selectedTab } = await setup({ tab: 'nope' });
        expect(await selectedTab()).toBe('分類');
      });

      // 寫入後重新載入同一本，不能把使用者拉回 ?tab= 的分頁；
      // 在設定頁建立另一本帳本時路由會重用這個元件，新帳本要落在 ?tab= 指定的分頁（spec F5）
      it('tab_query_param_reapplies_only_when_book_changes', async () => {
        const { openTab, selectedTab, httpTesting, fixture } = await setup({ tab: 'accounts' });
        await openTab('財務規劃帳戶', '[data-group="planning-funds"]');

        // flush 新物件：book() 換成不同的參考，才能證明「同一本」不會重設
        const reloaded = TestBed.inject(CurrentBook).reload(BOOK.id);
        httpTesting.expectOne(`/api/books/${BOOK.id}`).flush({ ...BOOK });
        await reloaded;
        await fixture.whenStable();
        expect(await selectedTab()).toBe('財務規劃帳戶');

        const other = TestBed.inject(CurrentBook).load('book-2');
        httpTesting.expectOne('/api/books/book-2').flush({ ...BOOK, id: 'book-2', name: '新帳本' });
        await other;
        await fixture.whenStable();
        expect(await selectedTab()).toBe('帳戶');
      });

      // 建立者沒有帳本也能登入（ADR 0011），刪掉最後一本不需要警告（#8）
      it('delete_dialog_has_no_login_warning_for_a_creator', async () => {
        const ctx = await setup();
        await signIn(ctx, [{ id: BOOK.id, name: BOOK.name }], true);

        expect(await openDeleteDialog(ctx)).toBeNull();
      });
    ```

- [ ] Step 2：`npx ng test --watch=false --include src/app/features/settings/settings.page.spec.ts; "exit=$LASTEXITCODE"`。預期：`tab_query_param_opens_that_tab` 與 `tab_query_param_reapplies…` 失敗（選中的是「分類」）；`delete_dialog_has_no_login_warning_for_a_creator` 失敗（警告存在）；`unknown_tab…` 此時會**通過**（現況本來就是第 0 個分頁），它守的是實作之後的退回行為。既有 12 個測試必須維持綠燈（假的 `ActivatedRoute` 還沒有人用）。
- [ ] Step 3：最小實作
  - `settings.page.ts`：
    - import：`computed` 已有；`toSignal` 加進 `@angular/core/rxjs-interop` 那一行；`ActivatedRoute` 加進 `@angular/router` 那一行；`map` 加進 `rxjs` 那一行。
    - 檔案上方常數區加上：

      ```ts
      // ?tab= 的值，依分頁順序（spec F5）；不認得的值用第一個分頁
      const TAB_KEYS = ['categories', 'accounts', 'funds', 'data'];
      ```

    - 類別內（`lockDate` 之後）加上：

      ```ts
        private readonly tabParam = toSignal(inject(ActivatedRoute).queryParamMap.pipe(map(params => params.get('tab'))), {
          requireSync: true,
        });
        // 只讀不寫：切換分頁不改網址（spec F5）。路由會重用這個元件（在設定頁建立另一本帳本），所以換帳本時重新套用 ?tab=；
        // 但寫入後 currentBook.reload 會換掉 book 物件，同一本帳本不能因此跳回 ?tab= 的分頁，所以只比較 id 與 tab
        private readonly tabTarget = computed(
          () => ({ bookId: this.book().id, tab: this.tabParam() }),
          { equal: (a, b) => a.bookId === b.bookId && a.tab === b.tab },
        );
        protected readonly selectedTab = linkedSignal({
          source: this.tabTarget,
          computation: ({ tab }) => Math.max(0, TAB_KEYS.indexOf(tab ?? '')),
        });
      ```

    - `onDeleteBook()` 的 `isLastBook` 改為：

      ```ts
          // 只剩這一本、而且不是建立者時，才警告刪除後無法再登入（#8）；建立者沒有帳本也能登入（ADR 0011）
          const me = this.session.me();
          const isLastBook = me?.books.length === 1 && !me.canCreateBooks;
      ```

  - `settings.page.html` 第一行改為 `<mat-tab-group class="ledger-sheet" [(selectedIndex)]="selectedTab">`。
- [ ] Step 4：單一檔案，**16 passed**（原本 12＋4），`exit=0`。
- [ ] 變異檢查：
  - 拿掉 `tabTarget` 的 `equal` 選項，`tab_query_param_reapplies_only_when_book_changes` 必須在「同一本重新載入」那一步變紅（被拉回「帳戶」）。
  - 拿掉 `&& !me.canCreateBooks`，`delete_dialog_has_no_login_warning_for_a_creator` 必須變紅。
  - 各自還原。
- [ ] Step 5：`npx ng test --watch=false; "exit=$LASTEXITCODE"`。Expected：**335 passed（41 個檔案）**、`exit=0`。
- [ ] Step 6：Commit

```powershell
git add web/src/app/features/settings/settings.page.ts web/src/app/features/settings/settings.page.html web/src/app/features/settings/settings.page.spec.ts
git commit -m "feat(web): 設定頁支援 ?tab= 開啟指定分頁；建立者刪除最後一本帳本不再警告" -m "Refs #7" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 8：交易頁沒有可用帳戶時的提示（F6）

**Files:** Modify: `web/src/app/features/transactions/transactions.page.ts`、`.html`、`.scss`、`.spec.ts`

從首頁、書籤或切換帳本進到一本空帳本時，表單沒有帳戶可選，使用者不知道下一步。容易錯的地方：判斷的是「**未封存**的帳戶」，帳戶全部封存的舊帳本也一樣不能記帳；測試用全部封存的帳本，就能同時抓到「只判斷 `accounts.length === 0`」的寫法。若 `TransactionForm` 在沒有可用帳戶時拋錯（`Unhandled Error` 或 exit code 1），**停下來回報**，不要在這個 Task 裡改表單。

- [ ] Step 1：`transactions.page.spec.ts`
  - dto import 補上 `BookDto`：`import { BookDto, TransactionDto } from '../../core/api/dto';`
  - `setup` 加第三個參數 `book: BookDto = BOOK`，`CurrentBook` 的 mock 改為 `{ provide: CurrentBook, useValue: { book: signal(book) } }`。
  - `loads_transactions_for_query_month` 結尾加上 `expect(page.el.querySelector('.no-accounts')).toBeNull();`
  - 新增：

    ```ts
      // 新建立的帳本沒有帳戶，封存的也不能用：提示到設定頁的帳戶分頁新增（spec F6）
      it('hints_settings_accounts_tab_when_no_active_account', async () => {
        const archived = BOOK.accounts.map(account => ({ ...account, archivedAt: '2026-01-01T00:00:00Z' }));
        const page = await setup('/books/book-1/transactions?month=202603', true, { ...BOOK, accounts: archived });
        await page.flushList(202603, []);

        const link = page.el.querySelector<HTMLAnchorElement>('.no-accounts a')!;
        expect(link.getAttribute('href')).toBe('/books/book-1/settings?tab=accounts');
      });
    ```

- [ ] Step 2：`npx ng test --watch=false --include src/app/features/transactions/transactions.page.spec.ts; "exit=$LASTEXITCODE"`。預期失敗原因：新測試在 `link.getAttribute` 拋出 `Cannot read properties of null`（還沒有 `.no-accounts`）。其餘測試維持綠燈。
- [ ] Step 3：最小實作
  - `transactions.page.ts`：`@angular/router` 那一行加上 `RouterLink`，加進 `imports`；在 `currentBookAsList` 之後新增：

    ```ts
      // 新建立的帳本沒有帳戶（或全部封存），表單無從選起：提示先到設定頁新增（spec F6）
      protected readonly noActiveAccount = computed(() => {
        const book = this.currentBook.book();
        return book !== undefined && !book.accounts.some(account => account.archivedAt === null);
      });
    ```

  - `transactions.page.html` 在 `@for (book of currentBookAsList(); track book.id) {` 的下一行插入：

    ```html
      @if (noActiveAccount()) {
        <p class="no-accounts ledger-sheet">
          這本帳本還沒有可用的帳戶，記帳前請先新增帳戶。
          <a [routerLink]="['/books', book.id, 'settings']" [queryParams]="{ tab: 'accounts' }">前往設定</a>
        </p>
      }
    ```

  - `transactions.page.scss` 新增 `.no-accounts { margin: 0; padding: 12px 16px; }`。
- [ ] Step 4：單一檔案全綠，比原本多 1 個，`exit=0`。
- [ ] 變異檢查：把 `!book.accounts.some(account => account.archivedAt === null)` 改成 `book.accounts.length === 0`，新測試必須變紅；確認後還原。
- [ ] Step 5：`npx ng test --watch=false; "exit=$LASTEXITCODE"`。Expected：**336 passed（41 個檔案）**、`exit=0`。
- [ ] Step 6：Commit

```powershell
git add web/src/app/features/transactions/transactions.page.ts web/src/app/features/transactions/transactions.page.html web/src/app/features/transactions/transactions.page.scss web/src/app/features/transactions/transactions.page.spec.ts
git commit -m "feat(web): 交易頁在帳本沒有可用帳戶時，提示到設定頁新增" -m "Refs #7" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 9：建立帳本 e2e

**Files:** Create: `web/e2e/create-book.spec.ts`

用真實瀏覽器確認單元測試涵蓋不到的整段：選單在 overlay 裡可以點、POST 帶 body 與 XSRF header、`me` 有重新載入（工具列選單出現新帳本）、新帳本落在設定頁的帳戶分頁（`?tab=` 經過真的路由）。

- [ ] Step 1：建立 `web/e2e/create-book.spec.ts`：

```ts
import { expect, test } from '@playwright/test';
import { BOOK, BOOK_ID, mockApi } from './fixtures';

const NEW_ID = 'b2';
const NEW_BOOK = { id: NEW_ID, name: '新帳本', openingDate: '2026-10-19', lockDate: null, accounts: [], planningFunds: [], categories: [] };

// 從工具列的帳本選單建立：送出名稱、開帳日與 XSRF；reload me 後選單出現新帳本，並落在新帳本設定頁的帳戶分頁（spec Q21、F5）
test('toolbar_menu_creates_book_and_opens_its_accounts_tab', async ({ page }) => {
  let createBody: unknown;
  let createToken: string | undefined;
  await mockApi(page, {
    me: route => route.fulfill({
      json: {
        subject: 'e2e', email: null, canCreateBooks: true, remainingBookQuota: createBody ? 3 : 4,
        books: [{ id: BOOK_ID, name: BOOK.name }, ...(createBody ? [{ id: NEW_ID, name: NEW_BOOK.name }] : [])],
      },
    }),
  });
  await page.route('**/api/books', async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    createBody = route.request().postDataJSON();
    createToken = route.request().headers()['x-xsrf-token'];
    await route.fulfill({ status: 201, json: { id: NEW_ID } });
  });
  await page.route(`**/api/books/${NEW_ID}`, route => route.fulfill({ json: NEW_BOOK }));

  await page.goto(`/books/${BOOK_ID}/transactions`);
  await page.getByRole('button', { name: BOOK.name }).click();
  await page.getByRole('menuitem', { name: '新增帳本…' }).click();
  await page.getByLabel('帳本名稱').fill('新帳本');
  await page.getByLabel('開帳日').fill('2026-10-19');
  await page.getByRole('button', { name: '建立', exact: true }).click();

  await expect(page).toHaveURL(/\/books\/b2\/settings\?tab=accounts$/);
  await expect(page.getByRole('tab', { name: '帳戶', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('已建立帳本「新帳本」')).toBeVisible();
  expect(createBody).toEqual({ name: '新帳本', openingDate: '2026-10-19' });
  expect(createToken).toBe('e2e-token');

  // me 已重新載入：工具列選單列出新帳本
  await page.getByRole('button', { name: NEW_BOOK.name }).click();
  await expect(page.getByRole('menuitem', { name: NEW_BOOK.name })).toBeVisible();
});
```

- [ ] Step 2：Task 1–8 完成後這條 e2e 一寫完就會是綠的，所以用變異檢查證明它有效：暫時拿掉 `BookCreation.enter` 裡的 `await this.session.reload();`，執行 `npx playwright test e2e/create-book.spec.ts`，應該在最後一個斷言失敗（`me` 沒有重新載入，選單沒有「新帳本」）；確認後還原。
- [ ] Step 3：（不需要實作）
- [ ] Step 4：`npx playwright test e2e/create-book.spec.ts`，**1 passed**。
- [ ] Step 5：`npx playwright test`，Expected：**11 passed**。`npx ng test --watch=false; "exit=$LASTEXITCODE"`，Expected：**336 passed（41 個檔案）**、`exit=0`。`npx ng build`：成功、沒有 warning。
- [ ] Step 6：Commit

```powershell
git add web/e2e/create-book.spec.ts
git commit -m "test(web): 建立帳本 e2e：選單送出名稱與 XSRF，reload me 後落在新帳本的帳戶分頁" -m "Refs #7" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## 完成後的驗證（前端 checkpoint）

- [ ] `npx ng test --watch=false; "exit=$LASTEXITCODE"`：**336 passed（41 個檔案）**左右、`exit=0`、沒有 Unhandled Errors。
- [ ] `npx playwright test`：**11 passed**。
- [ ] `npx ng build`：成功，沒有 warning（含 budget）。
- [ ] 後端再跑一次 `dotnet test --solution SixJars.slnx`，數字與後端 checkpoint 相同。
- [ ] `git status --short`：只剩使用者尚未核准 commit 的 `docs/superpowers/` 文件（若已另外 commit 則為乾淨）；`angular.json` 沒有被改動。
- [ ] 本計畫產生 **9 個 commit**。
- [ ] 計畫末尾新增「執行結果與偏差」一節，用獨立的 `docs(plans):` commit 回寫。

## 手動驗證

需要真實的後端、Google 登入與 migration：

1. 本機以 https 啟動 API 與前端（`docs/deploy.md`），套用後端 migration，用 CLI `add-creator --email <一個沒有任何帳本的 Google 帳號>`。
2. 用那個帳號登入：應該進得去（不是 `/denied`），首頁顯示 `NO_BOOKS_CREATOR_MESSAGE` 與「建立帳本」。
3. 建立「手動測試」，開帳日留預設：確認落在設定頁的「帳戶」分頁、顯示「已建立帳本「手動測試」」，工具列名稱變成選單。
4. 新增一個帳戶後切到「記帳」：提示消失；把帳戶封存後回到「記帳」：提示出現，點「前往設定」落在帳戶分頁。
5. 在設定頁（任一分頁）用工具列選單再建立一本：確認新帳本落在帳戶分頁（路由重用的情境）。
6. 建立同名帳本：對話框不關、顯示後端訊息，改名後可以直接再送。
7. 用 CLI 在 DB 補到 5 本後重新整理：「新增帳本…」停用並顯示「已達 5 本上限」。
8. `remove-creator` 後（不登出）再按「新增帳本…」送出：顯示「你不在建立者名單…」，選單裡的「新增帳本…」消失。
9. 帳號只剩一本時刪除帳本：建立者看不到「無法再登入」的警告；刪除後回到首頁，看到「建立帳本」。

## 後續（不在本計畫內）

- 首頁在已有多本帳本時沒有「新增帳本」的入口（spec Q23：工具列只在選取帳本時顯示選單）。若使用者反映不便再開 issue。
- 回到首頁時 `CurrentBook` 不會清除，工具列仍顯示上一本帳本（既有行為，與本 issue 無關）。
- 成員管理 UI、Web 匯入 Excel、還原備份、帳本範本（spec 排除項目）。

## 附錄：核准前事實查核（2026-10-19）

| 引用 | 存在？ | 證據 | 修正 |
|---|---|---|---|
| Angular／Material／CDK／build 版本 | ✅ 22.2.1 | `node_modules/@angular/*/package.json` | Tech Stack 寫實際版本 |
| `classifyError` 的 403 目前落到 `default` → `unavailable` | ✅ | `core/errors/api-error.ts` | Task 1 |
| interceptor 只處理 `unauthorized`／`antiforgery`／`unavailable`，其餘原樣丟給呼叫端 | ✅ | `core/errors/error-interceptor.ts` | `forbidden` 不加進 interceptor |
| `ApiError` 沒有任何 exhaustive（`never`）檢查，新增種類不會讓既有 switch 編譯失敗 | ✅ | `grep never src/app`（非 spec 無結果） | — |
| `MeDto` 字面值的位置：`app.spec.ts:34`、`session-api.spec.ts:19`、`session.service.spec.ts:12`、`home.page.spec.ts` setup | ✅ | `grep "books: \["`、`grep -l MeDto` | Task 2 一次補齊；`settings.page.spec.ts` 的 flush 未標型別 |
| `BookApi` 的寫法（`http.post<{ id: string }>`）與 `book-api.spec.ts` 已 import `firstValueFrom` | ✅ | `core/api/book-api.ts`、`book-api.spec.ts:4` | — |
| `SessionService.reload()` | ✅ | `core/auth/session.service.ts`（#6 加入） | — |
| `toDateString` 用本地時間；`test-setup.ts` 把 TZ 設為 `Asia/Taipei` | ✅ | `shared/dates.ts`、`src/test-setup.ts` | Task 3 的固定時間測試 |
| `output()` 的 `OutputEmitterRef.subscribe` | ✅ | `@angular/core` types：`subscribe(callback): OutputRefSubscription` | — |
| `MatMenuHarness.open/getItems/clickItem`、`MatMenuItemHarness.getText/isDisabled`，item filter 有 `text` | ✅ | `material/types/menu-testing.d.ts` | — |
| `MatTabGroupHarness.getSelectedTab()`／`selectTab()`；`MatTabGroup` 有 `selectedIndex` input 與 `selectedIndexChange` output（可雙向綁定） | ✅ | `material/types/tabs-testing.d.ts:72,78`、`tabs.d.ts:635,708` | — |
| `@angular/material/divider` | ✅ | `node_modules/@angular/material/divider` | — |
| 選單在測試裡不需要 animations provider | ✅ | `settings.page.spec.ts` 已用 `MatMenuHarness`，沒有提供 animations | — |
| **`ActivatedRoute` 沒有 `providedIn: 'root'`，只由 `provideRouter`／`RouterModule` 提供** | ⚠️ | `router/fesm2022/_router_module-chunk.mjs:1619,1836` | 設定頁 spec 沒有 `provideRouter`：Task 7 setup 改為提供假的 `ActivatedRoute`，否則既有測試全數 NullInjectorError |
| `App`、`HomePage` 的 spec 沒有 `HttpClient`，注入真的 `BookCreation` 會失敗 | ⚠️ | `app.spec.ts`、`home.page.spec.ts` 的 providers | Task 5、6 的 setup 提供 `BookCreation` mock |
| 設定頁分頁順序：分類／帳戶／財務規劃帳戶／鎖帳日與資料 | ✅ | `settings.page.html` | `TAB_KEYS` 對應 0–3 |
| 財務規劃帳戶分組的 `data-group` 是 `planning-funds` | ✅ | `settings-rules.ts:29` | Task 7 測試用這個選擇器 |
| `SettingsPage.book` 是 `computed(() => currentBook.book()!)`；`run()` 成功後 `currentBook.reload()` | ✅ | `settings.page.ts` | Task 7 的 `equal`（重新載入不重設分頁） |
| 路由 `books/:bookId` 底下的子路由會在換 `bookId` 時重用元件；`bookGuard` 換帳本時重新 `load` | ✅ | `app.routes.ts`、`book.guard.ts` | Task 7 換帳本時重新套用 `?tab=` |
| `onDeleteBook()` 的 `isLastBook` 是 `me()?.books.length === 1`；對話框的 `.last-book` 警告 | ✅ | `settings.page.ts`、`delete-book-dialog.ts`（#8，5842d76） | Task 7 |
| `TransactionsPage` 用 `currentBookAsList` 以 `book.id` 重建表單；spec 的 `CurrentBook` mock 是 `signal(BOOK)` | ✅ | `transactions.page.ts`、`transactions.page.spec.ts` | Task 8 setup 加 `book` 參數 |
| `TransactionForm`／`transaction-rules` 沒有 `accounts[0]` 之類的索引存取 | ✅ | `grep "\[0\]"`：無結果 | 仍保留「若拋錯就停下來回報」 |
| e2e：`mockApi` 的 `me` 覆寫；`**/api/books/b1` 不會攔到 `**/api/books`；token 是 `e2e-token`；`baseURL` 是 `http://localhost:4301` | ✅ | `e2e/fixtures.ts`、`playwright.config.ts` | `toHaveURL` 用 regex，不依賴 baseURL 解析 |
| `angular.json` initial budget：warning 700kB、error 1MB；目前 `main` 約 204kB | ✅ | `angular.json:37-42`、`dist/web/browser`（5842d76 的 build） | Task 5 跑 `ng build` 檢查 |
| Create 的檔案尚不存在 | ✅ | `core/book/` 只有 `book.guard*`、`current-book*`；`e2e/` 沒有 `create-book.spec.ts` | — |
| 測試碼競態：`BookCreation.enter` 是 async，斷言包在 `vi.waitFor`；對話框以 mock 取代，不受關閉動畫影響（#6 的教訓）；`flush({ ...BOOK })` 產生新參考，避免「同一本重新載入」的測試因為參考相同而空轉 | ✅ | — | — |
