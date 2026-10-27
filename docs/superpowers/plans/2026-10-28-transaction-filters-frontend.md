# 交易清單篩選：分類與帳戶（前端）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL：superpowers:test-driven-development（每個 Task 依 Step 1–6 執行，Step 2 不可省）；分派 subagent 時用 superpowers:subagent-driven-development。

**Goal：** 記帳頁工具列在標籤篩選之外，再加上「分類（主分類）」與「帳戶」兩個篩選，以及「清除篩選」按鈕。三個條件是 AND，各存成一個 query param，換月份時保留。

**Architecture：** 篩選都在後端做，前端只負責「query param → 有效選項 → API 參數」這段（spec F2）。`TransactionApi.list` 改成接受篩選物件（F1）；`TransactionList` 只需要知道有沒有在篩選（F4）。

**Tech Stack：** Angular 22、Angular Material 22（`mat-select`、`mat-optgroup`）、signals、Vitest 5（`ng test`）、Playwright 1.63（mock API）。

- 設計：[`../specs/2026-10-28-transaction-filters-frontend-design.md`](../specs/2026-10-28-transaction-filters-frontend-design.md)
- 後端計畫：[`2026-10-28-transaction-filters-backend.md`](2026-10-28-transaction-filters-backend.md)。前端測試全部用 mock HTTP，不依賴後端；但手動驗證需要後端 Task 1 先完成。

## 執行前必讀

### 環境

- 工作目錄：`web/`。分支：`feat/transaction-filters`（跟後端同一條，**不是 master**）。
- 全部單元測試：`npx ng test --watch=false`。單一檔案：`npx ng test --watch=false --include src/app/<path>.spec.ts`。
- E2E：`npx playwright test`（mock API，不需要後端）。單一檔案：`npx playwright test e2e/<file>.spec.ts`。
- 建置：`npx ng build`。

### 基準線

- **實測（2026-10-28，本機，HEAD `88b1473`）**：`ng test` 49 個檔案、412 個測試全過；Playwright 19 passed。
- 任何時候數字低於基準線，就代表弄壞了東西。各 Task 的預期數字在 Step 4／5 裡，那是**預測值**，跟實際不同時以實際為準並回寫。

### 絕對不要碰的檔案

- `web/.angular/`、`web/dist/`、`web/test-results/`、`web/node_modules/`；Angular CLI 可能改到 `angular.json` 的 analytics 設定，**不要 commit**。
- 後端的任何檔案（後端有自己的計畫）。
- 每個 Task 都用明確的路徑 `git add`，**禁止 `git add .`／`git add -A`**。

### 慣例

- 註解與 commit 訊息用繁體中文，註解寫「為什麼」，並引用 spec 編號（Q／F）；元件一律 `OnPush`，狀態用 signal；測試名稱用 snake_case 英文（比照既有 spec）。
- 頁面測試沿用 `transactions.page.spec.ts` 的 `setup()`、`flushList()`、`afterEach(verify)`：每個 HTTP 請求都必須被 `expectOne` 消化掉，否則 `verify()` 會失敗。
- 每個 Task 至少做一次變異檢查；一個 Task 一個 commit。

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `web/e2e/transaction-filters.spec.ts` | 三個條件組合篩選後清除（Q16） |

### 修改

| 檔案 | 改動 |
|---|---|
| `web/e2e/tags.spec.ts` | Task 1：locator 加 `exact: true`；Task 8：手機寬度測試帶上三個條件 |
| `web/e2e/transactions.spec.ts` | Task 1：locator 加 `exact: true` |
| `web/src/app/core/api/transaction-api.ts`／`.spec.ts` | Task 2：`TransactionFilter` |
| `web/src/app/features/transactions/transaction-list.ts`／`.spec.ts` | Task 3：`filtered` 輸入與說明文字 |
| `web/src/app/features/transactions/transactions.page.ts`／`.html`／`.scss`／`.spec.ts` | Task 2–7 |

### Task 相依順序

```
Task 1（e2e locator）        獨立，可以最先做或並行
Task 2（API filter 物件）
  └► Task 3（清單 filtered）
       └► Task 4（無效 tag 忽略）── checkpoint A
            └► Task 5（分類篩選）
                 └► Task 6（帳戶篩選）
                      └► Task 7（清除篩選）── checkpoint B
                           └► Task 8（e2e 組合情境）── checkpoint C
```

Task 2–7 都會改到 `transactions.page.*`，**不能並行**。Usage-safe checkpoint 設在 A、B、C。

---

## Task 1：既有 e2e 的 locator 改成精確比對

**Files：** Modify：`web/e2e/tags.spec.ts`、`web/e2e/transactions.spec.ts`

為什麼要先做：Task 5、6 加上「分類篩選」「帳戶篩選」兩個 combobox 之後，`getByRole('combobox', { name: '帳戶' })` 的子字串比對會同時抓到兩個元素，Playwright 會因為 strict mode 報錯（spec F6）。先改成精確比對，後面的 Task 就不會在中途把 e2e 弄壞。這個 Task 不會改變任何行為。

- [ ] **Step 1：寫失敗測試**：這個 Task 是對既有測試做的預防性修改，沒有新測試。修改內容：

  ```diff
  # e2e/tags.spec.ts
  -  await page.getByRole('combobox', { name: '帳戶' }).click();
  +  await page.getByRole('combobox', { name: '帳戶', exact: true }).click();
  ...
  -  await page.getByRole('combobox', { name: '分類' }).fill('早');
  +  await page.getByRole('combobox', { name: '分類', exact: true }).fill('早');
  ```

  ```diff
  # e2e/transactions.spec.ts（第 8、22、39 行）
  -  const account = page.getByRole('combobox', { name: '帳戶' });
  +  const account = page.getByRole('combobox', { name: '帳戶', exact: true });
  -  await expect(page.getByRole('combobox', { name: '分類' })).toBeFocused();
  +  await expect(page.getByRole('combobox', { name: '分類', exact: true })).toBeFocused();
  -  await expect(page.getByRole('combobox', { name: '分類' })).toHaveValue('飲食 / 早餐');
  +  await expect(page.getByRole('combobox', { name: '分類', exact: true })).toHaveValue('飲食 / 早餐');
  ```

- [ ] **Step 2：確認失敗**：改用變異檢查代替。暫時把 `exact: true` 的 name 改成 `'帳'`，跑 `npx playwright test e2e/transactions.spec.ts`，確認測試會變紅（證明 `exact` 真的有作用），然後還原。
- [ ] **Step 3：最小實作**：就是 Step 1 的修改。修改後用 `grep -rn "name: '帳戶'\|name: '分類'" e2e` 確認每一處都已經是 `exact: true`。
- [ ] **Step 4：跑單檔**：`npx playwright test e2e/tags.spec.ts e2e/transactions.spec.ts`。Expected：全過。
- [ ] **Step 5：跑全部**：`npx playwright test`。Expected：19 passed。
- [ ] **Step 6：Commit**

  ```powershell
  git add web/e2e/tags.spec.ts web/e2e/transactions.spec.ts
  git commit -m @'
  test(web): e2e 的帳戶、分類 combobox 改用精確比對

  接下來要加「帳戶篩選」「分類篩選」，子字串比對會同時抓到兩個元素，違反 strict mode。

  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  '@
  ```

---

## Task 2：`TransactionApi.list` 改成接受篩選物件

**Files：** Modify：`web/src/app/core/api/transaction-api.ts`、`transaction-api.spec.ts`、`web/src/app/features/transactions/transactions.page.ts`

容易出錯的地方：URL 的參數順序要固定（`budgetMonth`、`tagId`、`categoryId`、`accountId`），因為頁面測試會直接比對 URL 字串（F1）。

- [ ] **Step 1：寫失敗測試**：`transaction-api.spec.ts`，先把既有測試改成物件寫法，再新增一條：

  ```diff
     it('list_adds_tag_id_only_when_given', () => {
  -    api.list('b1', 202610, 't1').subscribe();
  +    api.list('b1', 202610, { tagId: 't1' }).subscribe();
       httpTesting.expectOne({ method: 'GET', url: '/api/books/b1/transactions?budgetMonth=202610&tagId=t1' }).flush([]);
  -    api.list('b1', 202610, null).subscribe();
  +    api.list('b1', 202610, { tagId: null }).subscribe();
       httpTesting.expectOne({ method: 'GET', url: '/api/books/b1/transactions?budgetMonth=202610' }).flush([]);
     });
  +
  +  it('list_adds_only_given_filters_in_fixed_order', () => {
  +    api.list('b1', 202610, { accountId: 'a1', categoryId: 'c1', tagId: 't1' }).subscribe();
  +    httpTesting
  +      .expectOne({ method: 'GET', url: '/api/books/b1/transactions?budgetMonth=202610&tagId=t1&categoryId=c1&accountId=a1' })
  +      .flush([]);
  +    api.list('b1', 202610, { categoryId: 'c1', accountId: undefined }).subscribe();
  +    httpTesting.expectOne({ method: 'GET', url: '/api/books/b1/transactions?budgetMonth=202610&categoryId=c1' }).flush([]);
  +  });
  ```

- [ ] **Step 2：跑測試，確認失敗**：`npx ng test --watch=false --include src/app/core/api/transaction-api.spec.ts`。
  Expected：兩條測試都失敗。原因可能是型別錯誤（物件不能指定給 `string`），也可能是執行時把物件串成 `tagId=[object Object]` 導致 `expectOne` 對不到。這兩種都是正確的失敗原因。
- [ ] **Step 3：最小實作**

  ```diff
  +// 篩選只套用在當月（tags frontend spec F8、transaction filters Q5）；沒有值的條件不帶參數，順序固定（F1）
  +export interface TransactionFilter {
  +  tagId?: string | null;
  +  categoryId?: string | null;
  +  accountId?: string | null;
  +}
  +
   @Injectable({ providedIn: 'root' })
   export class TransactionApi {
     private readonly http = inject(HttpClient);

  -  // tagId 只篩選當月（tags frontend spec F8）；沒有時不帶這個參數
  -  list(bookId: string, budgetMonth: number, tagId?: string | null): Observable<TransactionDto[]> {
  +  list(bookId: string, budgetMonth: number, filter: TransactionFilter = {}): Observable<TransactionDto[]> {
       let params = new HttpParams().set('budgetMonth', budgetMonth);
  -    if (tagId) {
  -      params = params.set('tagId', tagId);
  +    if (filter.tagId) {
  +      params = params.set('tagId', filter.tagId);
  +    }
  +    if (filter.categoryId) {
  +      params = params.set('categoryId', filter.categoryId);
  +    }
  +    if (filter.accountId) {
  +      params = params.set('accountId', filter.accountId);
       }
  ```

  `transactions.page.ts` 唯一的呼叫端：

  ```diff
  -      switchMap(({ bookId, month, tagId }) => this.transactionApi.list(bookId, month, tagId).pipe(catchError(() => EMPTY))),
  +      switchMap(({ bookId, month, tagId }) => this.transactionApi.list(bookId, month, { tagId }).pipe(catchError(() => EMPTY))),
  ```

- [ ] **Step 4：跑單檔**：Expected：`transaction-api.spec.ts` 全過。
  **變異檢查**：把 `categoryId` 和 `accountId` 兩個 `if` 的順序對調，新測試應該會變紅；確認後還原。
- [ ] **Step 5：跑全部**：`npx ng test --watch=false`。Expected：49 個檔案、413 個測試全過。
- [ ] **Step 6：Commit**

  ```powershell
  git add web/src/app/core/api/transaction-api.ts web/src/app/core/api/transaction-api.spec.ts web/src/app/features/transactions/transactions.page.ts
  git commit -m @'
  refactor(web): 交易清單 API 改成接受篩選物件，可帶標籤、分類、帳戶

  三個選填的位置參數容易傳錯位置；參數順序固定，測試才能直接比對 URL。

  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  '@
  ```

---

## Task 3：清單只需要知道「有沒有在篩選」

**Files：** Modify：`web/src/app/features/transactions/transaction-list.ts`、`transaction-list.spec.ts`、`transactions.page.ts`、`transactions.page.html`、`transactions.page.spec.ts`

為什麼要做：說明文字改成通用的寫法（Q9）之後，清單就不需要知道是哪個標籤（F4）。頁面上先加一個 `hasFilter`，Task 5、6 再擴充它的判斷。

- [ ] **Step 1：寫失敗測試**

  `transaction-list.spec.ts`：

  ```diff
     async function setup(
  -    transactions: TransactionDto[], book: BookDto = BOOK, filterTag: TagDto | null = null,
  +    transactions: TransactionDto[], book: BookDto = BOOK, filtered = false,
     ) {
       const fixture: ComponentFixture<TransactionList> = TestBed.createComponent(TransactionList);
  -    fixture.componentRef.setInput('filterTag', filterTag);
  +    fixture.componentRef.setInput('filtered', filtered);
  ...
  -  it('empty_message_names_filtered_tag', async () => {
  +  it('empty_message_mentions_filters_only_when_filtered', async () => {
       expect((await setup([])).element.textContent).toContain('這個月還沒有交易');
  -    const filtered = await setup([], BOOK, BOOK.tags[0]);
  -    expect(filtered.element.textContent).toContain('這個月沒有掛著「旅遊」的交易');
  +    const filtered = await setup([], BOOK, true);
  +    expect(filtered.element.textContent).toContain('這個月沒有符合篩選條件的交易');
     });
  ```

  `TagDto` 在這個 spec 裡只有 `setup` 用到（已查證），一併從 import 移除。

  `transactions.page.spec.ts` 的 `loads_with_tag_query_and_shows_it_selected`：

  ```diff
  -      expect(page.el.textContent).toContain('這個月沒有掛著「旅遊」的交易');
  +      expect(page.el.textContent).toContain('這個月沒有符合篩選條件的交易');
  ```

- [ ] **Step 2：跑測試，確認失敗**：`npx ng test --watch=false --include src/app/features/transactions/transaction-list.spec.ts --include src/app/features/transactions/transactions.page.spec.ts`
  Expected：`empty_message_mentions_filters_only_when_filtered` 失敗，因為說明文字還是「這個月還沒有交易」或舊的標籤寫法。`setInput('filtered')` 在 input 不存在時會報 NG0303，可能是丟出例外、也可能只是 console error，所以同一個檔案的其他測試也可能跟著失敗，兩種情況都可以接受。頁面測試則是因為說明文字還是舊的而失敗。
- [ ] **Step 3：最小實作**

  `transaction-list.ts`：

  ```diff
  -  // 交易頁正在依標籤篩選時，空清單的說明要點出是哪個標籤（F8）
  -  readonly filterTag = input<TagDto | null>(null);
  +  // 條件已經顯示在選單上，空清單只需要說明是被篩選掉的（transaction filters Q9、F4）
  +  readonly filtered = input(false);
  ...
  -  protected readonly emptyMessage = computed(() => {
  -    const tag = this.filterTag();
  -    return tag ? `這個月沒有掛著「${tag.name}」的交易` : '這個月還沒有交易';
  -  });
  +  protected readonly emptyMessage = computed(() => this.filtered() ? '這個月沒有符合篩選條件的交易' : '這個月還沒有交易');
  ```

  `TagDto` 仍然被 `TransactionRow.tags` 使用，import 保留。

  `transactions.page.ts`，加在 `filterTag` 下面：

  ```ts
  // Task 5、6 會再加上分類與帳戶
  protected readonly hasFilter = computed(() => this.filterTag() !== null);
  ```

  `transactions.page.html`：

  ```diff
  -    [filterTag]="filterTag()"
  +    [filtered]="hasFilter()"
  ```

- [ ] **Step 4：跑單檔**：Expected：這兩個 spec 全過。
  **變異檢查**：把 `hasFilter` 改成 `computed(() => false)`，`loads_with_tag_query_and_shows_it_selected` 應該會變紅；確認後還原。
- [ ] **Step 5：跑全部**：Expected：49 個檔案、413 個測試全過（只有改名或修改，數量不變）。
- [ ] **Step 6：Commit**

  ```powershell
  git add web/src/app/features/transactions/transaction-list.ts web/src/app/features/transactions/transaction-list.spec.ts web/src/app/features/transactions/transactions.page.ts web/src/app/features/transactions/transactions.page.html web/src/app/features/transactions/transactions.page.spec.ts
  git commit -m @'
  refactor(web): 空清單說明改成「沒有符合篩選條件的交易」

  篩選條件會增加到三個，條件已經顯示在選單上，說明文字不再點出標籤名稱。

  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  '@
  ```

---

## Task 4：對不到選項的 `tag` 視為沒有篩選

**Files：** Modify：`web/src/app/features/transactions/transactions.page.ts`、`transactions.page.html`、`transactions.page.spec.ts`

現在的情況：`?tag=<不存在的 id>` 會原樣送給後端，結果選單顯示「全部標籤」，清單卻是空的（Q11）。這個 Task 先把「原始 query param／有效選項」的分法（F2）建立起來，Task 5、6 照同樣的模式加上去。

- [ ] **Step 1：寫失敗測試**：`transactions.page.spec.ts` 的 `describe('tag filter')` 裡：

  ```ts
  it('unknown_tag_query_is_ignored', async () => {
    const page = await setup('/books/book-1/transactions?month=202603&tag=tag-ghost');
    // 不帶 tagId（Q11）
    await page.flushList(202603, []);
    expect(await (await page.tagFilter()).getValueText()).toBe('全部標籤');
    expect(page.el.textContent).toContain('這個月還沒有交易');
  });
  ```

- [ ] **Step 2：跑測試，確認失敗**：`npx ng test --watch=false --include src/app/features/transactions/transactions.page.spec.ts`
  Expected：只有這一條失敗，訊息是 `expectOne` 找不到 `...?budgetMonth=202603`，因為實際的請求帶了 `&tagId=tag-ghost`。
- [ ] **Step 3：最小實作**：`transactions.page.ts`

  ```diff
  +// query param 不存在時為 null
  +function queryParam(route: ActivatedRoute, name: string) {
  +  return toSignal(route.queryParamMap.pipe(map(params => params.get(name))), { requireSync: true });
  +}
  +
  +const bySortOrder = (a: { sortOrder: number }, b: { sortOrder: number }) => a.sortOrder - b.sortOrder;
  +
   @Component({ ... })
   export class TransactionsPage {
  ...
  -  // 標籤篩選只篩當月，換月份時保留（F8）；query param 不存在時不篩選
  -  protected readonly tagId = toSignal(this.route.queryParamMap.pipe(map(params => params.get('tag'))), { requireSync: true });
  +  // 篩選只篩當月，換月份時保留（tags F8、transaction filters Q5）。
  +  // 送給後端、選單綁定、判斷有沒有在篩選，都只看「有效選項」：對不到帳本選項的 query param 等於沒有這個條件（Q11、F2）
  +  private readonly tagQuery = queryParam(this.route, 'tag');
     // 含已封存的標籤，才篩得到舊交易
  -  protected readonly tagOptions = computed(() =>
  -    [...(this.currentBook.book()?.tags ?? [])].sort((a, b) => a.sortOrder - b.sortOrder));
  -  protected readonly filterTag = computed(() => this.tagOptions().find(tag => tag.id === this.tagId()) ?? null);
  +  protected readonly tagOptions = computed(() => [...(this.currentBook.book()?.tags ?? [])].sort(bySortOrder));
  +  protected readonly filterTag = computed(() => this.tagOptions().find(tag => tag.id === this.tagQuery()) ?? null);
  ...
     protected readonly transactions = toSignal(
  -    toObservable(computed(() => ({ bookId: this.bookId(), month: this.month(), tagId: this.tagId(), reload: this.reload() }))).pipe(
  -      switchMap(({ bookId, month, tagId }) => this.transactionApi.list(bookId, month, { tagId }).pipe(catchError(() => EMPTY))),
  +    toObservable(computed(() => ({
  +      bookId: this.bookId(),
  +      month: this.month(),
  +      filter: { tagId: this.filterTag()?.id },
  +      reload: this.reload(),
  +    }))).pipe(
  +      switchMap(({ bookId, month, filter }) => this.transactionApi.list(bookId, month, filter).pipe(catchError(() => EMPTY))),
       ),
  ```

  `transactions.page.html`：

  ```diff
  -    <mat-select class="tag-filter" aria-label="標籤篩選" placeholder="全部標籤" [value]="tagId()" (valueChange)="changeTag($event)">
  +    <mat-select class="tag-filter" aria-label="標籤篩選" placeholder="全部標籤" [value]="filterTag()?.id ?? null" (valueChange)="changeTag($event)">
  ```

- [ ] **Step 4：跑單檔**：Expected：`transactions.page.spec.ts` 全過（既有的 tag filter 測試也要過）。
  **變異檢查**：把 `filter: { tagId: this.filterTag()?.id }` 改回 `{ tagId: this.tagQuery() }`，新測試應該會變紅；確認後還原。
- [ ] **Step 5：跑全部**：Expected：49 個檔案、414 個測試全過。
- [ ] **Step 6：Commit**

  ```powershell
  git add web/src/app/features/transactions/transactions.page.ts web/src/app/features/transactions/transactions.page.html web/src/app/features/transactions/transactions.page.spec.ts
  git commit -m @'
  fix(web): 網址的標籤 id 對不到帳本的標籤時，視為沒有篩選

  原本會原樣送給後端，結果選單顯示「全部標籤」，清單卻是空的。

  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  '@
  ```

**Checkpoint A**：測試全綠、4 個 commit，可以在這裡收工。

---

## Task 5：分類篩選

**Files：** Modify：`web/src/app/features/transactions/transactions.page.ts`、`.html`、`.scss`、`.spec.ts`

容易出錯的地方：

1. 只認主分類，網址帶子分類 id 也要當作沒有篩選（F3）。
2. 某一種類沒有主分類時（例如新帳本沒有收入分類），不要畫出空的 optgroup。
3. `setup()` 的第二個參數傳 `undefined` 會套用預設值（spec 檔頂端的註解有說明），所以 `setup(url, undefined, FILTER_BOOK)` 是安全的寫法。

- [ ] **Step 1：寫失敗測試**：`transactions.page.spec.ts`

  檔案頂端、`TX` 之後：

  ```ts
  // 加上已封存的主分類與帳戶，確認選單會列出它們（Q7）
  const ARCHIVED_AT = '2026-02-01T00:00:00+00:00';
  const FILTER_BOOK: BookDto = {
    ...BOOK,
    accounts: [
      ...BOOK.accounts,
      { id: 'acc-old', name: '舊帳戶', type: 'Cash', openingBalance: 0, countsAsAvailableCash: true, sortOrder: 5, archivedAt: ARCHIVED_AT },
    ],
    categories: [
      ...BOOK.categories,
      { id: 'cat-old', name: '舊分類', kind: 'Expense', nature: 'Floating', parentId: null, sortOrder: 1, archivedAt: ARCHIVED_AT },
    ],
  };
  ```

  `setup()` 回傳的物件加上：

  ```ts
  categoryFilter: () => loader.getHarness(MatSelectHarness.with({ selector: '.category-filter' })),
  ```

  在 `describe('tag filter')` 後面新增：

  ```ts
  describe('category filter', () => {
    const categoryListUrl = (month: number, categoryId: string) => `${listUrl(month)}&categoryId=${categoryId}`;

    it('loads_with_category_query_and_shows_it_selected', async () => {
      const page = await setup('/books/book-1/transactions?month=202603&category=cat-food');
      page.httpTesting.expectOne(categoryListUrl(202603, 'cat-food')).flush([]);
      await page.stable();
      expect(await (await page.categoryFilter()).getValueText()).toBe('飲食');
      expect(page.el.textContent).toContain('這個月沒有符合篩選條件的交易');
    });

    it('options_are_main_categories_grouped_by_kind_including_archived', async () => {
      const page = await setup('/books/book-1/transactions?month=202603', undefined, FILTER_BOOK);
      await page.flushList(202603, []);
      const select = await page.categoryFilter();
      expect(await select.getValueText()).toBe('全部分類');
      await select.open();
      const groups = await select.getOptionGroups();
      expect(await Promise.all(groups.map(group => group.getLabelText()))).toEqual(['收入', '支出']);
      const texts = await Promise.all((await select.getOptions()).map(option => option.getText()));
      expect(texts).toEqual(['全部分類', '薪資', '飲食', '舊分類（已封存）']);
      await select.close();
    });

    it('selecting_category_sets_query_and_month_change_keeps_it', async () => {
      const page = await setup('/books/book-1/transactions?month=202603');
      await page.flushList(202603, []);
      const select = await page.categoryFilter();
      await select.open();
      await select.clickOptions({ text: '飲食' });
      await page.stable();
      expect(TestBed.inject(Router).url).toBe('/books/book-1/transactions?month=202603&category=cat-food');
      page.httpTesting.expectOne(categoryListUrl(202603, 'cat-food')).flush([]);
      await page.stable();

      await (await page.button('上個月')).click();
      await page.stable();
      expect(TestBed.inject(Router).url).toBe('/books/book-1/transactions?month=202602&category=cat-food');
      page.httpTesting.expectOne(categoryListUrl(202602, 'cat-food')).flush([]);
      await page.stable();
    });

    // 子分類（F3）與不存在的 id（Q11）都視為沒有篩選
    it.each(['cat-lunch', 'cat-ghost'])('ignores_category_query_%s', async categoryId => {
      const page = await setup(`/books/book-1/transactions?month=202603&category=${categoryId}`);
      await page.flushList(202603, []);
      expect(await (await page.categoryFilter()).getValueText()).toBe('全部分類');
    });
  });
  ```

- [ ] **Step 2：跑測試，確認失敗**：`npx ng test --watch=false --include src/app/features/transactions/transactions.page.spec.ts`
  Expected：5 條新測試全部失敗。`loads_with…` 和 `selecting…` 會因為請求沒有帶 `categoryId` 或找不到 `.category-filter` 而失敗；`options…` 和 `ignores…` 會因為 `getHarness` 找不到 `.category-filter` 而失敗。既有的測試要繼續全過。
- [ ] **Step 3：最小實作**

  `transactions.page.ts`：

  ```diff
     protected readonly filterTag = computed(() => this.tagOptions().find(tag => tag.id === this.tagQuery()) ?? null);
  -  // Task 5、6 會再加上分類與帳戶
  -  protected readonly hasFilter = computed(() => this.filterTag() !== null);
  +  private readonly categoryQuery = queryParam(this.route, 'category');
  +  // 只列主分類（Q1），含已封存的（Q7）
  +  private readonly mainCategories = computed(() =>
  +    (this.currentBook.book()?.categories ?? []).filter(category => category.parentId === null).sort(bySortOrder));
  +  // 收入、支出各一組（Q2、Q12）；沒有分類的那一組不畫
  +  protected readonly categoryGroups = computed(() =>
  +    [
  +      { label: '收入', categories: this.mainCategories().filter(category => category.kind === 'Income') },
  +      { label: '支出', categories: this.mainCategories().filter(category => category.kind === 'Expense') },
  +    ].filter(group => group.categories.length > 0));
  +  // 子分類的 id 也對不到，等於沒有篩選（F3）
  +  protected readonly filterCategory = computed(() =>
  +    this.mainCategories().find(category => category.id === this.categoryQuery()) ?? null);
  +  // Task 6 會再加上帳戶
  +  protected readonly hasFilter = computed(() => this.filterTag() !== null || this.filterCategory() !== null);
  ...
  -      filter: { tagId: this.filterTag()?.id },
  +      filter: { tagId: this.filterTag()?.id, categoryId: this.filterCategory()?.id },
  ...
  -  // null 時移除 tag query param
  -  protected changeTag(tag: string | null): void {
  -    void this.router.navigate([], { relativeTo: this.route, queryParams: { tag }, queryParamsHandling: 'merge' });
  -  }
  +  // null 時移除該 query param（F8）
  +  protected changeFilter(name: 'tag' | 'category' | 'account', id: string | null): void {
  +    void this.router.navigate([], { relativeTo: this.route, queryParams: { [name]: id }, queryParamsHandling: 'merge' });
  +  }
  ```

  `transactions.page.html`：把工具列換成以下內容（標籤選單改呼叫 `changeFilter('tag', …)`，class 從 `tag-field` 改成 `filter-field`）：

  ```html
  <div class="toolbar">
    <app-month-nav [month]="month()" (monthChange)="changeMonth($event)" />
    <div class="filters">
      <mat-form-field class="filter-field" subscriptSizing="dynamic">
        <!-- mat-select 把 null 視為未選取，不會顯示「全部…」選項的文字，所以用 placeholder 補上 -->
        <mat-select class="tag-filter" aria-label="標籤篩選" placeholder="全部標籤"
                    [value]="filterTag()?.id ?? null" (valueChange)="changeFilter('tag', $event)">
          <mat-option [value]="null">全部標籤</mat-option>
          @for (tag of tagOptions(); track tag.id) {
            <mat-option [value]="tag.id">{{ tag.name }}{{ tag.archivedAt ? '（已封存）' : '' }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <mat-form-field class="filter-field" subscriptSizing="dynamic">
        <mat-select class="category-filter" aria-label="分類篩選" placeholder="全部分類"
                    [value]="filterCategory()?.id ?? null" (valueChange)="changeFilter('category', $event)">
          <mat-option [value]="null">全部分類</mat-option>
          @for (group of categoryGroups(); track group.label) {
            <mat-optgroup [label]="group.label">
              @for (category of group.categories; track category.id) {
                <mat-option [value]="category.id">{{ category.name }}{{ category.archivedAt ? '（已封存）' : '' }}</mat-option>
              }
            </mat-optgroup>
          }
        </mat-select>
      </mat-form-field>
    </div>
  </div>
  ```

  `transactions.page.scss`：

  ```diff
  -.tag-field { width: 180px; }
  +.filters { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
  +.filter-field { width: 160px; }
  ```

  `mat-optgroup` 由 `MatSelectModule` 匯出的 `MatOptionModule` 提供，`imports` 不需要再加東西。

- [ ] **Step 4：跑單檔**：Expected：`transactions.page.spec.ts` 全過。
  **變異檢查**（每項做完都要還原）：
  - 拿掉 `.filter(category => category.parentId === null)`：`ignores_category_query_cat-lunch` 和 `options…` 應該會變紅。
  - 把 `hasFilter` 裡的 `|| this.filterCategory() !== null` 拿掉：`loads_with_category_query…` 應該會變紅。
- [ ] **Step 5：跑全部**：Expected：49 個檔案、419 個測試全過。
- [ ] **Step 6：Commit**

  ```powershell
  git add web/src/app/features/transactions/transactions.page.ts web/src/app/features/transactions/transactions.page.html web/src/app/features/transactions/transactions.page.scss web/src/app/features/transactions/transactions.page.spec.ts
  git commit -m @'
  feat(web): 記帳頁新增主分類篩選，收入、支出分組，含已封存

  只認主分類；網址帶子分類或不存在的 id 時，等於沒有篩選。

  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  '@
  ```

---

## Task 6：帳戶篩選

**Files：** Modify：`web/src/app/features/transactions/transactions.page.ts`、`.html`、`.spec.ts`

- [ ] **Step 1：寫失敗測試**

  `setup()` 回傳的物件加上：

  ```ts
  accountFilter: () => loader.getHarness(MatSelectHarness.with({ selector: '.account-filter' })),
  ```

  新增：

  ```ts
  describe('account filter', () => {
    it('loads_with_account_query_and_shows_it_selected', async () => {
      const page = await setup('/books/book-1/transactions?month=202603&account=acc-bank');
      page.httpTesting.expectOne(`${listUrl(202603)}&accountId=acc-bank`).flush([]);
      await page.stable();
      expect(await (await page.accountFilter()).getValueText()).toBe('銀行');
      expect(page.el.textContent).toContain('這個月沒有符合篩選條件的交易');
    });

    it('options_include_archived_accounts_in_sort_order', async () => {
      const page = await setup('/books/book-1/transactions?month=202603', undefined, FILTER_BOOK);
      await page.flushList(202603, []);
      const select = await page.accountFilter();
      expect(await select.getValueText()).toBe('全部帳戶');
      await select.open();
      const texts = await Promise.all((await select.getOptions()).map(option => option.getText()));
      expect(texts).toEqual(['全部帳戶', '現金', '銀行', '信用卡', '悠遊卡', '房貸', '舊帳戶（已封存）']);
      await select.close();
    });

    it('selecting_account_combines_with_other_filters', async () => {
      const page = await setup('/books/book-1/transactions?month=202603&tag=tag-trip&category=cat-food');
      page.httpTesting.expectOne(`${listUrl(202603)}&tagId=tag-trip&categoryId=cat-food`).flush([]);
      await page.stable();
      const select = await page.accountFilter();
      await select.open();
      await select.clickOptions({ text: '銀行' });
      await page.stable();
      expect(TestBed.inject(Router).url).toBe('/books/book-1/transactions?month=202603&tag=tag-trip&category=cat-food&account=acc-bank');
      page.httpTesting.expectOne(`${listUrl(202603)}&tagId=tag-trip&categoryId=cat-food&accountId=acc-bank`).flush([]);
      await page.stable();
    });

    it('unknown_account_query_is_ignored', async () => {
      const page = await setup('/books/book-1/transactions?month=202603&account=acc-ghost');
      await page.flushList(202603, []);
      expect(await (await page.accountFilter()).getValueText()).toBe('全部帳戶');
    });
  });
  ```

- [ ] **Step 2：跑測試，確認失敗**：Expected：4 條新測試都失敗。`loads…` 是因為請求沒有帶 `accountId`，其他三條是因為找不到 `.account-filter`。
- [ ] **Step 3：最小實作**

  `transactions.page.ts`：

  ```diff
  +  private readonly accountQuery = queryParam(this.route, 'account');
  +  // 平鋪、依設定頁的順序（Q13），含已封存的（Q7）
  +  protected readonly accountOptions = computed(() => [...(this.currentBook.book()?.accounts ?? [])].sort(bySortOrder));
  +  protected readonly filterAccount = computed(() => this.accountOptions().find(account => account.id === this.accountQuery()) ?? null);
  -  // Task 6 會再加上帳戶
  -  protected readonly hasFilter = computed(() => this.filterTag() !== null || this.filterCategory() !== null);
  +  protected readonly hasFilter = computed(() =>
  +    this.filterTag() !== null || this.filterCategory() !== null || this.filterAccount() !== null);
  ...
  -      filter: { tagId: this.filterTag()?.id, categoryId: this.filterCategory()?.id },
  +      filter: { tagId: this.filterTag()?.id, categoryId: this.filterCategory()?.id, accountId: this.filterAccount()?.id },
  ```

  `transactions.page.html`：在 `.filters` 裡、分類選單後面加上：

  ```html
  <mat-form-field class="filter-field" subscriptSizing="dynamic">
    <mat-select class="account-filter" aria-label="帳戶篩選" placeholder="全部帳戶"
                [value]="filterAccount()?.id ?? null" (valueChange)="changeFilter('account', $event)">
      <mat-option [value]="null">全部帳戶</mat-option>
      @for (account of accountOptions(); track account.id) {
        <mat-option [value]="account.id">{{ account.name }}{{ account.archivedAt ? '（已封存）' : '' }}</mat-option>
      }
    </mat-select>
  </mat-form-field>
  ```

- [ ] **Step 4：跑單檔**：Expected：全過。
  **變異檢查**：把 `filter` 裡的 `accountId` 拿掉，`loads_with_account_query…` 和 `selecting_account…` 應該會變紅；確認後還原。
- [ ] **Step 5：跑全部**：Expected：49 個檔案、423 個測試全過。
- [ ] **Step 6：Commit**

  ```powershell
  git add web/src/app/features/transactions/transactions.page.ts web/src/app/features/transactions/transactions.page.html web/src/app/features/transactions/transactions.page.spec.ts
  git commit -m @'
  feat(web): 記帳頁新增帳戶篩選，依設定排序，含已封存

  沿用後端既有的 accountId 篩選（對方帳戶也算），三個條件是 AND。

  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  '@
  ```

---

## Task 7：清除篩選

**Files：** Modify：`web/src/app/features/transactions/transactions.page.ts`、`.html`、`.spec.ts`

容易出錯的地方：按鈕顯示與否要看 `hasFilter()`（有效選項），**不是**看 query param 存不存在。所以 `?tag=tag-ghost` 的時候不應該出現按鈕。

- [ ] **Step 1：寫失敗測試**

  `transactions.page.spec.ts` 的 import 已經有 `MatButtonHarness`。`setup()` 回傳的物件加上：

  ```ts
  clearButtons: () => loader.getAllHarnesses(MatButtonHarness.with({ text: '清除篩選' })),
  ```

  新增：

  ```ts
  describe('clear filters', () => {
    it('clear_button_hidden_when_no_effective_filter', async () => {
      const page = await setup('/books/book-1/transactions?month=202603&tag=tag-ghost');
      await page.flushList(202603, []);
      expect(await page.clearButtons()).toHaveLength(0);
    });

    it('clear_removes_all_filters_and_keeps_month', async () => {
      const page = await setup('/books/book-1/transactions?month=202603&tag=tag-trip&category=cat-food&account=acc-bank');
      page.httpTesting.expectOne(`${listUrl(202603)}&tagId=tag-trip&categoryId=cat-food&accountId=acc-bank`).flush([]);
      await page.stable();
      const [clear] = await page.clearButtons();
      await clear.click();
      await page.stable();
      expect(TestBed.inject(Router).url).toBe('/books/book-1/transactions?month=202603');
      await page.flushList(202603, []);
      expect(await page.clearButtons()).toHaveLength(0);
      expect(page.el.textContent).toContain('這個月還沒有交易');
    });
  });
  ```

- [ ] **Step 2：跑測試，確認失敗**：Expected：`clear_removes…` 失敗，因為 `clear` 是 `undefined`，呼叫 `click()` 會丟出 TypeError。`clear_button_hidden…` 在實作前**本來就會通過**（因為還沒有按鈕），它是用來防止回歸的，靠 Step 4 的變異檢查證明它有效。
- [ ] **Step 3：最小實作**

  `transactions.page.ts`：

  ```ts
  // 月份是正在看的位置，不算篩選條件（Q14）
  protected clearFilters(): void {
    void this.router.navigate([], {
      relativeTo: this.route, queryParams: { tag: null, category: null, account: null }, queryParamsHandling: 'merge',
    });
  }
  ```

  `imports` 加上 `MatButtonModule`（`@angular/material/button`）。

  `transactions.page.html`：在 `.filters` 的最後加上：

  ```html
  @if (hasFilter()) {
    <button mat-button type="button" class="clear-filters" (click)="clearFilters()">清除篩選</button>
  }
  ```

- [ ] **Step 4：跑單檔**：Expected：全過。
  **變異檢查**（每項做完都要還原）：
  - 把 `@if (hasFilter())` 改成 `@if (true)`：`clear_button_hidden…` 應該會變紅。
  - 把 `clearFilters` 裡的 `account: null` 拿掉：`clear_removes…` 應該會變紅。
- [ ] **Step 5：跑全部**：Expected：49 個檔案、425 個測試全過；`npx ng build` 成功且沒有警告。
- [ ] **Step 6：Commit**

  ```powershell
  git add web/src/app/features/transactions/transactions.page.ts web/src/app/features/transactions/transactions.page.html web/src/app/features/transactions/transactions.page.spec.ts
  git commit -m @'
  feat(web): 記帳頁新增「清除篩選」，一次清掉三個條件、保留月份

  只在有條件真的生效時顯示；網址帶著對不到的 id 時不顯示。

  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  '@
  ```

**Checkpoint B**：單元測試全綠，可以在這裡收工。

---

## Task 8：e2e 組合篩選情境與手機寬度

**Files：** Create：`web/e2e/transaction-filters.spec.ts`。Modify：`web/e2e/tags.spec.ts`

這條 e2e 要驗證「選單 → URL → 請求 → 清除」整段同步。因為功能已經在 Task 5–7 完成，新測試一寫好就會通過；所以 Step 2 改用變異檢查，證明這條測試真的會抓到問題。

- [ ] **Step 1：寫測試**：`web/e2e/transaction-filters.spec.ts`

  ```ts
  import { Route, expect, test } from '@playwright/test';
  import { BOOK, BOOK_ID, mockApi } from './fixtures';

  const BOOK_WITH_FILTERS = {
    ...BOOK,
    accounts: [
      ...BOOK.accounts,
      { id: 'acc-bank', name: '銀行', type: 'Bank', openingBalance: 0, countsAsAvailableCash: true, sortOrder: 1, archivedAt: null },
    ],
    tags: [{ id: 'tag-trip', name: '旅遊', color: 'Teal', sortOrder: 0, archivedAt: null }],
  };

  test('filters_combine_then_clear_keeps_month', async ({ page }) => {
    const gets: URL[] = [];
    await mockApi(page, {
      book: route => route.fulfill({ json: BOOK_WITH_FILTERS }),
      transactions: async (route: Route) => {
        gets.push(new URL(route.request().url()));
        await route.fulfill({ json: [] });
      },
    });
    await page.goto(`/books/${BOOK_ID}/transactions?month=202601`);

    await page.getByRole('combobox', { name: '分類篩選' }).click();
    await page.getByRole('option', { name: '飲食' }).click();
    await page.getByRole('combobox', { name: '帳戶篩選' }).click();
    await page.getByRole('option', { name: '銀行' }).click();
    await page.getByRole('combobox', { name: '標籤篩選' }).click();
    await page.getByRole('option', { name: '旅遊' }).click();

    await expect(page).toHaveURL(/[?&]category=cat-food/);
    await expect(page).toHaveURL(/[?&]account=acc-bank/);
    await expect(page).toHaveURL(/[?&]tag=tag-trip/);
    await expect.poll(() => gets.at(-1)?.searchParams.toString())
      .toBe('budgetMonth=202601&tagId=tag-trip&categoryId=cat-food&accountId=acc-bank');
    await expect(page.getByText('這個月沒有符合篩選條件的交易')).toBeVisible();

    await page.getByRole('button', { name: '清除篩選' }).click();
    await expect(page).toHaveURL(new RegExp(`/books/${BOOK_ID}/transactions\\?month=202601$`));
    await expect.poll(() => gets.at(-1)?.searchParams.toString()).toBe('budgetMonth=202601');
    await expect(page.getByRole('button', { name: '清除篩選' })).toHaveCount(0);
    await expect(page.getByText('這個月還沒有交易')).toBeVisible();
  });
  ```

  `web/e2e/tags.spec.ts` 的手機寬度測試，改成帶上三個條件，讓三個選單和清除鍵都出現（`acc-cash`、`cat-food` 在 fixture 的 `BOOK` 裡就有）：

  ```diff
  -    await page.goto(`/books/${BOOK_ID}/transactions?month=202601&tag=tag-trip`);
  +    // 三個條件都生效，三個選單和清除鍵都會出現（transaction filters Q8）
  +    await page.goto(`/books/${BOOK_ID}/transactions?month=202601&tag=tag-trip&category=cat-food&account=acc-cash`);
       await expect(page.locator('td.mat-column-note .tag-chip')).toHaveText('旅遊');
  +    await expect(page.getByRole('button', { name: '清除篩選' })).toBeVisible();
       expect(await overflow()).toBe(0);
  ```

- [ ] **Step 2：確認測試有效（變異檢查）**：暫時把 `clearFilters` 裡的 `account: null` 拿掉，跑 `npx playwright test e2e/transaction-filters.spec.ts`，應該會在 URL 或請求的斷言上失敗；確認後還原。另外把 `.filter-field` 的寬度暫時改成 `400px`，手機寬度測試的 `overflow()` 應該會變成大於 0；確認後還原。
- [ ] **Step 3：實作**：沒有新的產品程式碼。如果手機寬度測試在**沒有做變異的情況下**就失敗了，代表 Task 5 的 SCSS 不夠，要調整 `.filters`／`.filter-field`（例如在窄螢幕改成 `flex: 1 1 140px`），並把調整內容回寫到這份計畫。
- [ ] **Step 4：跑單檔**：`npx playwright test e2e/transaction-filters.spec.ts e2e/tags.spec.ts`。Expected：全過。
- [ ] **Step 5：跑全部**：`npx playwright test`，Expected：20 passed；`npx ng test --watch=false`，Expected：425 passed。
- [ ] **Step 6：Commit**

  ```powershell
  git add web/e2e/transaction-filters.spec.ts web/e2e/tags.spec.ts
  git commit -m @'
  test(web): e2e 涵蓋三個條件組合篩選後清除，以及手機寬度不溢出

  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  '@
  ```

**Checkpoint C**：全部完成。

---

## 完成後的驗證

- [ ] `npx ng test --watch=false`：49 個檔案、425 個測試全過（預測值，以實測為準）
- [ ] `npx playwright test`：20 passed
- [ ] `npx ng build`：成功，沒有警告
- [ ] `git status`：沒有未 commit 的 `web/` 檔案（`angular.json` 的 analytics 變更不算，也不要 commit）
- [ ] 本計畫共 8 個 commit
- [ ] `grep -rn "changeTag\|filterTag\]" web/src`：沒有殘留（`filterTag` 這個 computed 本身仍然保留，這裡檢查的是舊的 input 綁定和舊方法名）

## 手動驗證

後端計畫的 Task 1 完成後，依 `docs/deploy.md` 在本機跑 API 和前端（https），然後：

1. 選一個有子分類的支出主分類：清單只剩這個主分類及其子分類的交易，包含掛著這個分類的貸款繳款。
2. 再選一個帳戶：清單只剩同時符合兩個條件的交易；轉入這個帳戶的轉帳也要出現。
3. 換到上個月：三個條件都保留，選單顯示的值也不變。
4. 手動把網址的 `category=` 改成子分類的 id：選單顯示「全部分類」，清單沒有被篩選。
5. 按「清除篩選」：三個選單都回到「全部…」，月份不變，按鈕消失。
6. 手機寬度（DevTools 375px）：工具列換行後頁面不會橫向捲動；三個選單和按鈕都點得到。
7. 選單裡已封存的分類和帳戶，名稱後面有「（已封存）」。

## 後續（不在本計畫內）

- 篩選子分類、條件內多選。
- 篩選列收進「篩選」按鈕（如果之後條件繼續增加）。
- 篩選後顯示清單的合計（注意：口徑會跟預算的實際支出不同，見後端 spec Q3）。
