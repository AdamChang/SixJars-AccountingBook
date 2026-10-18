# 刪除帳本（前端）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL：逐 Task 執行時使用 superpowers:test-driven-development；分派子代理時使用 superpowers:subagent-driven-development；inline 執行時使用 superpowers:executing-plans。每個 Task 的流程是「失敗測試 → 確認失敗原因 → 最小實作 → 全綠 → 一個 commit」。

**Goal**：帳本設定頁的「鎖帳日與資料」分頁新增「刪除帳本」區塊。對話框會先顯示刪除前的統計和備份下載連結，必須輸入完全相同的帳本名稱才能刪除；刪除成功後重新載入 `me`，並回到首頁。

**Architecture**：`BookApi` 新增 `deletionPreview`／`delete`。`SessionService` 新增 `reload()`，`CurrentBook` 新增 `clear()`。新增 `DeleteBookDialog` 元件，負責載入統計、比對輸入的名稱，並以 `afterClosed()` 回傳輸入的名稱；`SettingsPage` 負責送出刪除與後續導頁，做法比照既有的 `SettingDialog`。

**Tech Stack**：Angular 22.2、Angular Material／CDK 22.2、Vitest 5、Playwright。

- 設計文件：[`docs/superpowers/specs/2026-10-19-delete-book-frontend-design.md`](../specs/2026-10-19-delete-book-frontend-design.md)（F 編號）
- 後端計畫：[`2026-10-19-delete-book-backend.md`](2026-10-19-delete-book-backend.md)。**後端 checkpoint 全綠才能開工**（API 形狀以後端 spec 的「介面」一節為準）。
- 需求：GitHub issue [AdamChang/SixJars-AccountingBook#6](https://github.com/AdamChang/SixJars-AccountingBook/issues/6)

---

## 執行前必讀

### 環境

- 工作目錄：`F:\VibeCode\SixJars-AccountingBook\web`。分支：`claude/delete-book`，與後端同一條分支，**不是 master**。
- 全部單元測試：`npx ng test --watch=false`。單一檔案：`npx ng test --watch=false --include src/app/<path>.spec.ts`。
- E2E：`npx playwright test`（mock API，不需要後端）。建置：`npx ng build`。

### 基準線（2026-10-19 本機實測，`claude/delete-book` = `1fc9196`）

- `ng test`：**303 passed（38 個檔案）**。
- Playwright：**9 passed**。
- 開工前再跑一次，確認數字相同（後端計畫不會動到 `web/`）。數字低於基準線，就代表弄壞了東西。Expected 是預測值，以實際結果為準，只要只增不減即可。

### 絕對不要碰的檔案

- `web/.angular/`、`web/dist/`、`web/test-results/`、`web/node_modules/`；Angular CLI 可能改動 `angular.json` 裡的 analytics 設定，**不要 commit 這類變更**。
- 每個 Task 都用明確的路徑 `git add`，**禁止 `git add .`／`git add -A`**。

### 慣例

- 註解與 commit 訊息用繁體中文；元件一律 `ChangeDetectionStrategy.OnPush`，狀態用 signal；測試名稱用 snake_case 英文（比照既有 spec）。
- 對話框只回傳結果，由頁面送出請求（比照 `SettingDialog`）。
- 寫入錯誤只處理與這個動作相關的種類；離線、XSRF 等由 interceptor 統一處理（比照 `SettingsPage.run()` 的註解）。
- 每個 Task 至少做一次變異檢查；一個 Task 一個 commit；commit 結尾加上 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`。

---

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `web/src/app/features/settings/delete-book-dialog.ts` | 刪除確認對話框：統計、備份連結、名稱比對 |
| `web/src/app/features/settings/delete-book-dialog.spec.ts` | 對話框的單元測試 |
| `web/e2e/delete-book.spec.ts` | 刪除流程 e2e |

### 修改

| 檔案 | 改動 |
|---|---|
| `web/src/app/core/api/dto.ts` | 新增 `BookDeletionPreviewDto` |
| `web/src/app/core/api/book-api.ts`／`.spec.ts` | `deletionPreview`、`delete` |
| `web/src/app/core/auth/session.service.ts`／`.spec.ts` | `reload()` |
| `web/src/app/core/book/current-book.ts`／`.spec.ts` | `clear()` |
| `web/src/app/features/settings/settings.page.ts`／`.html`／`.scss`／`.spec.ts` | 「刪除帳本」區塊與 `onDeleteBook()` |

### Task 相依順序

```
Task 1 (BookApi) ──┬─→ Task 3 (DeleteBookDialog) ─┬─→ Task 4 (SettingsPage) ─→ Task 5 (e2e) ─→ 前端 checkpoint
Task 2 (reload/clear) ─────────────────────────────┘
```

Task 1 與 Task 2 互相獨立，可以並行；Task 3 只依賴 Task 1。

---

## Task 1: F1 BookApi 的 deletionPreview 與 delete

**Files:** Modify: `web/src/app/core/api/dto.ts`、`book-api.ts`、`book-api.spec.ts`

`delete` 要把名稱放進 **body**（後端 D1）。`HttpClient.delete` 預設不帶 body，必須用 `{ body }` 選項。如果寫成 query string，後端會回 400，而且單元測試只看 URL 的話抓不到這個錯，所以測試要明確斷言 body。

- [ ] Step 1：在 `book-api.spec.ts` 的 describe 內新增：

```ts
  it('deletion_preview_gets_counts', () => {
    const preview: BookDeletionPreviewDto = {
      name: '帳本', openingDate: '2026-01-01', lockDate: null, transactions: 3, plannedExpenses: 1,
      recurringPlannedExpenses: 0, categoryBudgets: 2, members: 1, auditEntries: 9,
    };
    let result: BookDeletionPreviewDto | undefined;
    api.deletionPreview('b1').subscribe((v) => (result = v));
    httpTesting.expectOne({ method: 'GET', url: '/api/books/b1/deletion-preview' }).flush(preview);
    expect(result).toEqual(preview);
  });

  it('delete_sends_confirm_name_in_body', async () => {
    const done = firstValueFrom(api.delete('b1', '帳本'));
    const request = httpTesting.expectOne({ method: 'DELETE', url: '/api/books/b1' });
    expect(request.request.body).toEqual({ confirmName: '帳本' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await done;
  });
```

並把檔頭 import 改成 `import { BookDeletionPreviewDto, BookDto } from './dto';`。

- [ ] Step 2：執行 `npx ng test --watch=false --include src/app/core/api/book-api.spec.ts`，預期失敗原因是 `BookDeletionPreviewDto` 不存在，或 `api.deletionPreview is not a function`。
- [ ] Step 3：在 `dto.ts` 的 `BookDto` 附近新增：

```ts
// 刪除帳本前的統計；transactions、plannedExpenses 只算未刪除的（後端 spec D3）
export interface BookDeletionPreviewDto {
  name: string; openingDate: string; lockDate: string | null;
  transactions: number; plannedExpenses: number; recurringPlannedExpenses: number;
  categoryBudgets: number; members: number; auditEntries: number;
}
```

在 `BookApi` 的 `get` 之後新增（import 補上 `BookDeletionPreviewDto`）：

```ts
  deletionPreview(bookId: string): Observable<BookDeletionPreviewDto> {
    return this.http.get<BookDeletionPreviewDto>(`/api/books/${bookId}/deletion-preview`);
  }

  // 名稱放在 body（後端 spec D1）；必須與帳本名稱完全相同，否則 422 name-mismatch
  delete(bookId: string, confirmName: string): Observable<void> {
    return this.http.delete<void>(`/api/books/${bookId}`, { body: { confirmName } });
  }
```

- [ ] Step 4：執行單一檔案，所有測試應全數通過，比原本多 2 個。
- [ ] 變異檢查：把 `{ body: { confirmName } }` 拿掉，`delete_sends_confirm_name_in_body` 應該失敗；確認後還原。
- [ ] Step 5：執行 `npx ng test --watch=false`。Expected：**305 passed（38 個檔案）**。
- [ ] Step 6：Commit

```powershell
git add web/src/app/core/api/dto.ts web/src/app/core/api/book-api.ts web/src/app/core/api/book-api.spec.ts
git commit -m "feat(web): BookApi 新增刪除帳本與刪除前統計" -m "Refs #6" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 2: F2 SessionService.reload 與 CurrentBook.clear

**Files:** Modify: `web/src/app/core/auth/session.service.ts`／`.spec.ts`、`web/src/app/core/book/current-book.ts`／`.spec.ts`

`SessionService.load()` 會永久快取第一次的 promise（F4），`CurrentBook` 則快取目前這一本帳本（F5）。沒有這兩個方法的話，刪除後首頁還會列出、甚至自動導回已刪除的帳本；按返回鍵時，`bookGuard` 也會用快取放行。

- [ ] Step 1：在 `session.service.spec.ts` 新增：

```ts
  it('reload_fetches_me_again_and_updates_signal', async () => {
    const loaded = service.load();
    httpTesting.expectOne('/api/me').flush(me);
    await vi.waitFor(() => httpTesting.expectOne('/api/antiforgery/token').flush(null));
    await loaded;

    const updated: MeDto = { ...me, books: [{ id: 'b2', name: '另一本' }] };
    const reloaded = service.reload();
    httpTesting.expectOne('/api/me').flush(updated);
    await vi.waitFor(() => httpTesting.expectOne('/api/antiforgery/token').flush(null));

    expect(await reloaded).toEqual(updated);
    expect(service.me()).toEqual(updated);
  });
```

在 `current-book.spec.ts` 新增：

```ts
  it('clear_drops_cached_book', async () => {
    const first = current.load('b1');
    httpTesting.expectOne('/api/books/b1').flush(book('b1'));
    await first;

    current.clear();
    expect(current.book()).toBeUndefined();

    // 快取也清掉了：同一個 id 會重新請求
    const again = current.load('b1');
    httpTesting.expectOne('/api/books/b1').flush(book('b1'));
    expect((await again).id).toBe('b1');
  });
```

- [ ] Step 2：分別執行這兩個 spec 檔，確認失敗原因是 `reload`／`clear is not a function`。
- [ ] Step 3：`SessionService` 在 `load()` 之後新增：

```ts
  // 成員關係改變後呼叫（例如刪除帳本）：丟掉快取重新取得 me
  reload(): Promise<MeDto> {
    this.pending = undefined;
    return this.load();
  }
```

`CurrentBook` 在 `reload()` 之後新增：

```ts
  // 帳本已刪除時呼叫：丟掉快取與目前的帳本，避免返回鍵讓 bookGuard 用快取放行
  clear(): void {
    this.cached = undefined;
    this.bookState.set(undefined);
  }
```

- [ ] Step 4：執行兩個單一檔案，全數通過。
- [ ] 變異檢查：把 `reload()` 裡的 `this.pending = undefined;` 拿掉，`reload_fetches_me_again...` 應該失敗（`expectOne('/api/me')` 找不到請求）；確認後還原。
- [ ] Step 5：執行 `npx ng test --watch=false`。Expected：**307 passed（38 個檔案）**。
- [ ] Step 6：Commit

```powershell
git add web/src/app/core/auth/session.service.ts web/src/app/core/auth/session.service.spec.ts web/src/app/core/book/current-book.ts web/src/app/core/book/current-book.spec.ts
git commit -m "feat(web): SessionService.reload 與 CurrentBook.clear，供刪除帳本後更新狀態" -m "Refs #6" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 3: F3 DeleteBookDialog

**Files:** Create: `web/src/app/features/settings/delete-book-dialog.ts`、`delete-book-dialog.spec.ts`

對話框只負責顯示資訊和確認，不送出刪除請求。容易出錯的地方有兩個：名稱必須**完全相同**才能按刪除（尾端多一個空白也不行，與後端 D2 一致）；統計載入失敗時刪除鈕要保持停用，避免使用者在沒看到統計的情況下刪除。

- [ ] Step 1：建立 `delete-book-dialog.spec.ts`：

```ts
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatButtonHarness } from '@angular/material/button/testing';
import { MatInputHarness } from '@angular/material/input/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { BookDeletionPreviewDto } from '../../core/api/dto';
import { DeleteBookDialog } from './delete-book-dialog';

const PREVIEW: BookDeletionPreviewDto = {
  name: '我的帳本', openingDate: '2026-01-01', lockDate: '2026-03-31', transactions: 12, plannedExpenses: 3,
  recurringPlannedExpenses: 2, categoryBudgets: 4, members: 1, auditEntries: 40,
};

async function open() {
  const close = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: MAT_DIALOG_DATA, useValue: { bookId: 'book-1', name: '我的帳本' } },
      { provide: MatDialogRef, useValue: { close } },
    ],
  });
  const fixture = TestBed.createComponent(DeleteBookDialog);
  const httpTesting = TestBed.inject(HttpTestingController);
  const loader = TestbedHarnessEnvironment.loader(fixture);
  const el = fixture.nativeElement as HTMLElement;
  const flushPreview = async () => {
    httpTesting.expectOne('/api/books/book-1/deletion-preview').flush(PREVIEW);
    await fixture.whenStable();
  };
  const deleteButton = () => loader.getHarness(MatButtonHarness.with({ text: '永久刪除' }));
  return { fixture, httpTesting, loader, el, close, flushPreview, deleteButton };
}

describe('DeleteBookDialog', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('shows_counts_lock_date_and_backup_link_after_preview_loads', async () => {
    const { el, flushPreview, fixture } = await open();
    await fixture.whenStable();
    expect(el.textContent).toContain('載入中');

    await flushPreview();
    const text = el.textContent!;
    expect(text).toContain('12 筆');
    expect(text).toContain('2026-03-31');
    expect(text).toContain('40 筆');
    expect(el.querySelector<HTMLAnchorElement>('a.backup')!.getAttribute('href')).toBe('/api/books/book-1/export/backup.json');
  });

  it('delete_is_enabled_only_when_name_matches_exactly', async () => {
    const { loader, flushPreview, deleteButton, close } = await open();
    await flushPreview();
    const input = await loader.getHarness(MatInputHarness.with({ selector: '.confirm-name input' }));
    expect(await (await deleteButton()).isDisabled()).toBe(true);

    await input.setValue('我的帳本 ');
    expect(await (await deleteButton()).isDisabled()).toBe(true);

    await input.setValue('我的帳本');
    await (await deleteButton()).click();
    expect(close).toHaveBeenCalledWith('我的帳本');
  });

  it('failed_preview_keeps_delete_disabled', async () => {
    const { httpTesting, fixture, el, deleteButton } = await open();
    httpTesting.expectOne('/api/books/book-1/deletion-preview').flush(null, { status: 503, statusText: '' });
    await fixture.whenStable();

    expect(el.textContent).toContain('無法載入');
    expect(await (await deleteButton()).isDisabled()).toBe(true);
  });
});
```

- [ ] Step 2：執行 `npx ng test --watch=false --include src/app/features/settings/delete-book-dialog.spec.ts`，確認失敗原因是 `./delete-book-dialog` 不存在。
- [ ] Step 3：建立 `delete-book-dialog.ts`：

```ts
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { BookApi } from '../../core/api/book-api';
import { BookDeletionPreviewDto } from '../../core/api/dto';

export interface DeleteBookDialogData { bookId: string; name: string }

// 刪除帳本的確認：先看統計、可以先下載備份，輸入完全相同的名稱才能刪除（spec F2）。
// 只回傳輸入的名稱，由頁面送出刪除；取消時 afterClosed 回 undefined。
@Component({
  selector: 'app-delete-book-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>刪除帳本「{{ data.name }}」</h2>
    <mat-dialog-content>
      @if (preview(); as p) {
        <p class="warning">刪除後，這本帳本的所有交易、預定支出、預算與設定都會永久移除，無法復原。</p>
        <dl class="counts">
          <dt>開帳日</dt><dd>{{ p.openingDate }}</dd>
          @if (p.lockDate) {
            <dt>鎖帳日</dt><dd>{{ p.lockDate }}</dd>
          }
          <dt>交易</dt><dd>{{ p.transactions }} 筆</dd>
          <dt>預定支出</dt><dd>{{ p.plannedExpenses }} 筆</dd>
          <dt>週期預定支出</dt><dd>{{ p.recurringPlannedExpenses }} 筆</dd>
          <dt>預算</dt><dd>{{ p.categoryBudgets }} 個分類</dd>
          <dt>成員</dt><dd>{{ p.members }} 位</dd>
          <dt>稽核記錄</dt><dd>{{ p.auditEntries }} 筆</dd>
        </dl>
        <a matButton="outlined" class="backup" [href]="backupUrl" download>下載完整備份</a>
        <mat-form-field class="confirm-name">
          <mat-label>輸入帳本名稱以確認</mat-label>
          <input matInput autocomplete="off" [value]="typed()" (input)="typed.set($any($event.target).value)" />
        </mat-form-field>
      } @else if (failed()) {
        <p class="error">無法載入帳本統計，請稍後再試。</p>
      } @else {
        <p class="loading">載入中…</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton type="button" mat-dialog-close>取消</button>
      <button matButton="filled" type="button" class="confirm-delete" [disabled]="!matches()" (click)="confirm()">永久刪除</button>
    </mat-dialog-actions>
  `,
  styles: `
    .counts { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; margin: 12px 0; }
    .counts dd { margin: 0; }
    .confirm-name { display: block; margin-top: 16px; }
    .warning, .error { color: var(--mat-sys-error); }
  `,
})
export class DeleteBookDialog {
  protected readonly data = inject<DeleteBookDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<DeleteBookDialog, string>>(MatDialogRef);

  protected readonly preview = signal<BookDeletionPreviewDto | undefined>(undefined);
  protected readonly failed = signal(false);
  protected readonly typed = signal('');
  // 完全相同才算（不 trim，與後端 D2 一致）；統計沒載入成功時不能刪
  protected readonly matches = computed(() => this.preview() !== undefined && this.typed() === this.data.name);
  protected readonly backupUrl = `/api/books/${this.data.bookId}/export/backup.json`;

  constructor() {
    inject(BookApi).deletionPreview(this.data.bookId).pipe(takeUntilDestroyed()).subscribe({
      next: preview => this.preview.set(preview),
      error: () => this.failed.set(true),
    });
  }

  protected confirm(): void {
    this.ref.close(this.typed());
  }
}
```

- [ ] Step 4：執行單一檔案，**3 passed**。
- [ ] 變異檢查：把 `matches` 裡的 `===` 改成 `this.typed().trim() === this.data.name`，`delete_is_enabled_only_when_name_matches_exactly` 應該失敗；確認後還原。
- [ ] Step 5：執行 `npx ng test --watch=false`。Expected：**310 passed（39 個檔案）**。
- [ ] Step 6：Commit

```powershell
git add web/src/app/features/settings/delete-book-dialog.ts web/src/app/features/settings/delete-book-dialog.spec.ts
git commit -m "feat(web): 刪除帳本的確認對話框（統計、備份連結、輸入名稱確認）" -m "Refs #6" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 4: F4 設定頁的刪除帳本區塊

**Files:** Modify: `web/src/app/features/settings/settings.page.ts`、`.html`、`.scss`、`.spec.ts`

這個 Task 要注意 F3 的順序：先 `await session.reload()`，再 `await router.navigate(['/'])`，最後才 `currentBook.clear()`。如果先 clear，設定頁的 `book()!` 會在離開前拿到 undefined 而報錯；如果先導頁，首頁會讀到舊的 `me`，在只剩一本時被自動導回已刪除的帳本。404 代表帳本已經不在了，走一樣的離開流程，但不顯示成功訊息。

- [ ] Step 1：在 `settings.page.spec.ts` 的 describe 內新增。`setup()` 不用改，因為 Router 是 root provider；每個測試各自 spy `navigate`。檔頭補上 `import { Router } from '@angular/router';`：

```ts
  const PREVIEW = {
    name: BOOK.name, openingDate: '2026-01-01', lockDate: null, transactions: 0, plannedExpenses: 0,
    recurringPlannedExpenses: 0, categoryBudgets: 0, members: 1, auditEntries: 0,
  };

  // 開啟刪除對話框、輸入名稱並按「永久刪除」，回傳送出的 DELETE 請求
  async function confirmDelete(ctx: Awaited<ReturnType<typeof setup>>) {
    await ctx.openTab('鎖帳日與資料', '.delete-book');
    ctx.el.querySelector<HTMLButtonElement>('.delete-book button')!.click();
    await ctx.fixture.whenStable();
    ctx.httpTesting.expectOne(`/api/books/${BOOK.id}/deletion-preview`).flush(PREVIEW);
    await ctx.fixture.whenStable();
    await (await ctx.rootLoader.getHarness(MatInputHarness.with({ selector: '.confirm-name input' }))).setValue(BOOK.name);
    await (await ctx.rootLoader.getHarness(MatButtonHarness.with({ text: '永久刪除' }))).click();
    await ctx.fixture.whenStable();
    return ctx.httpTesting.expectOne({ method: 'DELETE', url: `/api/books/${BOOK.id}` });
  }

  async function expectMeReload(httpTesting: HttpTestingController) {
    await vi.waitFor(() => httpTesting.expectOne('/api/me').flush({ subject: 's', email: null, books: [] }));
    await vi.waitFor(() => httpTesting.expectOne('/api/antiforgery/token').flush(null));
  }

  it('delete_book_reloads_session_then_goes_home_and_clears_current_book', async () => {
    const ctx = await setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    const request = await confirmDelete(ctx);
    expect(request.request.body).toEqual({ confirmName: BOOK.name });
    request.flush(null, { status: 204, statusText: '' });
    await expectMeReload(ctx.httpTesting);

    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith(['/']));
    await vi.waitFor(() => expect(TestBed.inject(CurrentBook).book()).toBeUndefined());
    expect(ctx.notifier.show).toHaveBeenCalledWith(`已刪除帳本「${BOOK.name}」`);
  });

  it('delete_book_domain_error_shows_backend_message_and_stays', async () => {
    const ctx = await setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    (await confirmDelete(ctx)).flush(
      { code: 'name-mismatch', detail: '輸入的名稱與帳本名稱不同，沒有刪除。' }, { status: 422, statusText: '' });
    await ctx.fixture.whenStable();

    expect(ctx.notifier.show).toHaveBeenCalledWith('輸入的名稱與帳本名稱不同，沒有刪除。');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('delete_book_not_found_leaves_without_success_message', async () => {
    const ctx = await setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    (await confirmDelete(ctx)).flush(null, { status: 404, statusText: '' });
    await expectMeReload(ctx.httpTesting);

    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith(['/']));
    expect(ctx.notifier.show).not.toHaveBeenCalled();
  });
```

（`MatButtonHarness`、`MatInputHarness`、`HttpTestingController` 這幾個 import 已經存在，不必再加。）

- [ ] Step 2：執行 `npx ng test --watch=false --include src/app/features/settings/settings.page.spec.ts`，確認 3 個新測試失敗，原因是 `openTab` 等不到 `.delete-book`（`vi.waitFor` 逾時）。既有測試必須維持綠燈。
- [ ] Step 3：最小實作。

`settings.page.html` 在 `<section class="downloads">…</section>` 之後、`</mat-tab>` 之前新增：

```html
    <section class="delete-book">
      <h3>刪除帳本</h3>
      <p class="hint">刪除後，這本帳本的所有交易、預定支出、預算與設定都會永久移除，無法復原。</p>
      <button matButton="outlined" type="button" (click)="onDeleteBook()">刪除帳本…</button>
    </section>
```

`settings.page.ts`：import 補上 `Router`（`@angular/router`）、`switchMap`（`rxjs`）、`SessionService`、`DeleteBookDialog, DeleteBookDialogData`；注入 `private readonly session = inject(SessionService);`、`private readonly router = inject(Router);`；在 `clearLockDate()` 之後新增：

```ts
  protected onDeleteBook(): void {
    const book = this.book();
    this.dialog.open<DeleteBookDialog, DeleteBookDialogData, string>(DeleteBookDialog, { data: { bookId: book.id, name: book.name } })
      .afterClosed()
      .pipe(
        filter((name): name is string => name !== undefined),
        switchMap(name => this.api.delete(book.id, name)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => void this.leaveDeletedBook(`已刪除帳本「${book.name}」`),
        error: (error: ApiError) => {
          switch (error.kind) {
            case 'notFound':
              // 帳本已經不存在（例如在其他分頁刪掉了），結果與刪除成功相同
              void this.leaveDeletedBook(null);
              break;
            case 'domain':
              this.notifier.show(error.message);
              break;
          }
        },
      });
  }

  // spec F3：先重新載入 me，首頁才不會列出（或自動導回）已刪除的帳本；
  // clear 放在導頁之後，否則這一頁的 book()! 會在離開前拿到 undefined
  private async leaveDeletedBook(message: string | null): Promise<void> {
    await this.session.reload();
    await this.router.navigate(['/']);
    this.currentBook.clear();
    if (message) {
      this.notifier.show(message);
    }
  }
```

`settings.page.scss` 讓 `.delete-book` 沿用 `.downloads`／`.lock-date` 的區塊間距，並把標題改用錯誤色。先讀現有的 scss，以同樣的選擇器寫法加上去。

- [ ] Step 4：執行單一檔案，全數通過，比原本多 3 個。
- [ ] 變異檢查：把 `leaveDeletedBook` 的前兩行對調（先導頁、後 reload），`delete_book_reloads_session_then_goes_home...` 仍會通過，因為 spy 不會真的導頁。所以這項順序改用**另一個**變異來驗證：把 `this.currentBook.clear();` 拿掉，該測試應該失敗；確認後還原。導頁與 reload 的順序由 Task 5 的 e2e 把關（只剩一本時會被導回去）。
- [ ] Step 5：執行 `npx ng test --watch=false`。Expected：**313 passed（39 個檔案）**。
- [ ] Step 6：Commit

```powershell
git add web/src/app/features/settings/settings.page.ts web/src/app/features/settings/settings.page.html web/src/app/features/settings/settings.page.scss web/src/app/features/settings/settings.page.spec.ts
git commit -m "feat(web): 設定頁可以刪除帳本，刪除後重新載入成員關係並回到首頁" -m "Refs #6" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 5: F5 刪除帳本 e2e

**Files:** Create: `web/e2e/delete-book.spec.ts`

這條 e2e 用真實瀏覽器確認四件事：DELETE 帶著 body 與 XSRF header 送出；刪除後 `me` 有重新載入；首頁顯示「沒有可存取的帳本」，而不是被導回已刪除的帳本。這最後一點是 Task 4 單元測試驗證不到的順序問題。

- [ ] Step 1：建立 `web/e2e/delete-book.spec.ts`：

```ts
import { expect, test } from '@playwright/test';
import { BOOK, BOOK_ID, mockApi } from './fixtures';

// 刪除後 me 不再含這本帳本：首頁必須顯示「沒有可存取的帳本」，而不是用舊的 me 自動導回已刪除的帳本（spec F3）
test('settings_deletes_book_after_typing_its_name', async ({ page }) => {
  let deleteBody: unknown;
  let deleteToken: string | undefined;
  await mockApi(page, {
    me: route => route.fulfill({
      json: { subject: 'e2e', email: null, books: deleteBody ? [] : [{ id: BOOK_ID, name: BOOK.name }] },
    }),
    book: async route => {
      if (route.request().method() === 'DELETE') {
        deleteBody = route.request().postDataJSON();
        deleteToken = route.request().headers()['x-xsrf-token'];
        await route.fulfill({ status: 204 });
        return;
      }
      await route.fulfill({ json: BOOK });
    },
  });
  await page.route(`**/api/books/${BOOK_ID}/deletion-preview`, route => route.fulfill({
    json: {
      name: BOOK.name, openingDate: '2026-01-01', lockDate: null, transactions: 3, plannedExpenses: 0,
      recurringPlannedExpenses: 0, categoryBudgets: 0, members: 1, auditEntries: 5,
    },
  }));

  await page.goto(`/books/${BOOK_ID}/settings`);
  await page.getByRole('tab', { name: '鎖帳日與資料' }).click();
  await page.getByRole('button', { name: '刪除帳本…' }).click();
  await expect(page.getByText('3 筆')).toBeVisible();
  const confirmButton = page.getByRole('button', { name: '永久刪除' });
  await expect(confirmButton).toBeDisabled();

  await page.getByLabel('輸入帳本名稱以確認').fill(BOOK.name);
  await confirmButton.click();

  await expect(page.getByText('沒有可存取的帳本')).toBeVisible();
  expect(deleteBody).toEqual({ confirmName: BOOK.name });
  expect(deleteToken).toBe('e2e-token');
});
```

- [ ] Step 2：在 Task 4 已完成的情況下，這條 e2e 一寫完就會是綠的，所以改用變異檢查證明它有效：暫時把 `leaveDeletedBook` 裡的 `await this.session.reload();` 移到 `navigate` 之後，執行 `npx playwright test e2e/delete-book.spec.ts`，應該會失敗（舊的 `me` 只有一本帳本，首頁會導回 `/books/b1/transactions`），確認後還原。
- [ ] Step 3：（不需要實作）
- [ ] Step 4：執行 `npx playwright test e2e/delete-book.spec.ts`，**1 passed**。
- [ ] Step 5：執行 `npx playwright test`。Expected：**10 passed**。另外執行 `npx ng build`，確認成功且沒有 warning。
- [ ] Step 6：Commit

```powershell
git add web/e2e/delete-book.spec.ts
git commit -m "test(web): 刪除帳本 e2e：送出名稱與 XSRF，刪除後首頁不再導回已刪除的帳本" -m "Refs #6" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## 完成後的驗證（前端 checkpoint）

- [ ] `npx ng test --watch=false`：**313 passed（39 個檔案）**左右。
- [ ] `npx playwright test`：**10 passed**。
- [ ] `npx ng build`：成功，沒有 warning。
- [ ] 後端再跑一次 `dotnet test --solution SixJars.slnx`，數字與後端 checkpoint 相同。
- [ ] `git status`：工作目錄乾淨，`angular.json` 沒有被改動。
- [ ] 本計畫產生 **5 個 commit**。

## 手動驗證

需要真實的後端、Google 登入，以及實際的對話框操作：

1. 本機以 https 啟動 API 與前端（`docs/deploy.md`），用 CLI 匯入一本 `刪除測試`。
2. 進入設定頁 →「鎖帳日與資料」→「刪除帳本…」，確認統計數字與匯入報告相符。
3. 點「下載完整備份」，確認下載的 JSON 檔可以開啟。
4. 名稱打錯一個字，確認「永久刪除」維持停用；打對後刪除，確認回到首頁，帳本清單裡沒有這本。
5. 按瀏覽器返回鍵，確認不會看到已刪除帳本的設定頁（應該被導回首頁）。
6. 在另一個分頁開著同一本帳本的記帳頁，刪除後在那個分頁新增一筆交易，觀察顯示的錯誤（Q13 沿用現狀，只要記錄下行為，不必修）。

## 後續（不在本計畫內）

- 其他分頁遇到帳本層級 404 時的處理（Q13）。如果手動驗證第 6 步的行為不好，另外開 issue。
- 首頁沒有帳本時提供建立或匯入帳本的入口。
- 稽核查詢 UI（Q8）。

## 附錄：核准前事實查核（2026-10-19）

| 引用 | 存在? | 證據 | 修正 |
|---|---|---|---|
| `BookApi`（`http.get/post/put/delete`，`remove` 用 `http.delete`） | ✅ | `core/api/book-api.ts` | — |
| `SessionService.load()` 永久快取 `pending`、`fetch()` 會再要一次 antiforgery token | ✅ | `core/auth/session.service.ts` | `reload()` 會多一次 token 請求，測試已涵蓋 |
| `CurrentBook.cached`／`bookState`／`reload()` | ✅ | `core/book/current-book.ts` | — |
| 首頁在建構時讀取 `me()!.books`，只有一本時自動導向 | ✅ | `features/home/home.page.ts` | 導頁前必須先 reload（F3） |
| `bookGuard` 用 `CurrentBook.load`，`notFound` 時導回 `/` | ✅ | `core/book/book.guard.ts` | — |
| 設定頁是 `mat-tab-group`，「鎖帳日與資料」分頁裡有 `.downloads` 區塊與 `完整備份` 連結 | ✅ | `features/settings/settings.page.html` | spec Q11 改為放在這個分頁（F1） |
| `SettingsPage.book = computed(() => currentBook.book()!)` | ✅ | `settings.page.ts` | clear 放在導頁之後（F3） |
| `ApiError` 的 `domain`（`message` 取自 `detail`）／`notFound` | ✅ | `core/errors/api-error.ts` | — |
| `NO_BOOKS_MESSAGE = '沒有可存取的帳本'` | ✅ | `core/errors/messages.ts` | — |
| 設定頁 spec 的 `setup()` 有 `openTab`、`rootLoader`、`notifier`、`httpTesting`；`BOOK` 來自 `transactions/testing/book-fixture`（id `book-1`、名稱 `我的帳本`） | ✅ | `settings.page.spec.ts`、`book-fixture.ts` | — |
| 對話框 spec 寫法（`MAT_DIALOG_DATA`＋假的 `MatDialogRef.close`） | ✅ | `setting-dialog.spec.ts` | — |
| e2e `mockApi` 的 `me`／`book` 覆寫；`**/api/books/b1` 不會攔到 `/deletion-preview` | ✅ | `e2e/fixtures.ts`（glob 沒有萬用字元結尾） | preview 另外註冊 route |
| XSRF：mock 的 token 是 `e2e-token` | ✅ | `e2e/fixtures.ts` | — |
| Vitest 有 `vi` 全域變數（既有 spec 直接使用） | ✅ | `settings.page.spec.ts` | — |
| `--mat-sys-error` CSS 變數 | ✅ | `pay-dialog.ts:83`、`transaction-form.scss:52` 已在使用；`styles.scss` 以 `mat.theme` 產生 | — |
| Create 的檔案尚不存在 | ✅ | `features/settings/` 沒有 `delete-book-dialog*`；`e2e/` 沒有 `delete-book.spec.ts` | — |
| 測試碼競態：`reload` 之後 me 與 token 依序到達，用 `vi.waitFor` 等待；`leaveDeletedBook` 是 async，斷言都包在 `vi.waitFor` 裡 | ✅ | — | — |

## 執行結果與偏差（2026-10-19，前端 checkpoint）

- Commit：`900851c`（F1）、`f09ef68`（F2）、`4858f69`（F3）、`31ce2c9`（F4）、`5607d56`（F5）。
- 測試數字與預測相同：305 → 307 → 310（39 個檔案）→ 313；Playwright 9 → 10；`ng build` 成功、沒有 warning；後端仍是 549，失敗 0、略過 0。
- 變異檢查都以預期的理由變紅：F1 拿掉 `{ body }`；F2 `reload()` 不清 `pending`；F3 改成 `trim()` 比對；F4 拿掉 `currentBook.clear()`；F5 把 `reload` 移到 `navigate` 之後，首頁被舊的 `me` 導回已刪除的帳本。
- **偏差（測試碼的競態）**：Task 4 的 `confirmDelete` 原本在 `whenStable()` 之後直接 `expectOne(DELETE)`。但 `afterClosed()` 要等對話框的關閉動畫結束才會發出，所以 DELETE 送出的時間比 `whenStable` 晚，3 個測試因此找不到請求。修正方式是改用 `vi.waitFor(() => expectOne(...))`，並在註解寫明原因。這是計畫附錄「測試碼競態」那一列漏掉的地方。
- Task 3 的 `--mat-sys-error` 已在事實查核時確認存在，沒有用到備案。
