# MCP 記帳（寫入）前端實作計畫

> For agentic workers: REQUIRED SUB-SKILL — superpowers:test-driven-development；逐 Task 執行，每個 Task 一個 commit。

**Goal**：在交易清單標示 Claude 記的交易，在編輯頁唯讀顯示經由與來源識別碼。

**Architecture**：`TransactionDto` 多兩個選填欄位。判斷集中在純函式 `recordedByAgent`。清單的 row 多一個布林值，表單在編輯模式顯示唯讀資訊。不新增 API 呼叫。

**Tech Stack**：Angular 21、Angular Material、Vitest（`ng test`）。

設計文件：`docs/superpowers/specs/2026-11-01-mcp-bookkeeping-frontend-design.md`。後端計畫：`plans/2026-11-01-mcp-bookkeeping-backend.md`。**前提是後端 T3 已完成**，`TransactionDto` 才會回傳這兩個欄位；不過前端測試用的是 fixture，可以先做。

## 執行前必讀

### 環境
- 工作目錄 `F:\VibeCode\SixJars-AccountingBook\web`，分支 **`feat/mcp-write`**（不是 master）。
- 全部測試：`npx ng test --watch=false`。
- 單一檔案：`npx ng test --watch=false --include src/app/features/transactions/transaction-list.spec.ts`。

### 基準線（2026-11-01，親自執行）
- `npx ng test --watch=false`：**Test Files 49 passed、Tests 429 passed**。
- 任何時候數字低於基準線，就是弄壞了東西。

### 絕對不要碰的檔案
- `web/dist/`、`web/test-results/`、`node_modules/`；Angular CLI 可能寫進 `angular.json` 的 analytics 設定。
- 每個 Task 都用明確路徑 `git add`，禁止 `git add .`。

### 慣例
- 註解與 commit message 用繁體中文。
- signal 與 `input()`、`@if`／`@for` 控制流程，照既有元件的寫法。
- 測試資料用 `features/transactions/testing/book-fixture.ts` 的 `BOOK` 與 `transactionDtoFrom(input, overrides)`。

## 檔案結構

### 新增
| 檔案 | 責任 |
|---|---|
| `src/app/shared/agent-origin.ts`、`agent-origin.spec.ts` | `recordedByAgent(dto)`：判斷交易是不是 Agent 記的 |

### 修改
| 檔案 | 改動 |
|---|---|
| `src/app/core/api/dto.ts` | `TransactionDto` 加 `createdVia?: string \| null`、`externalRef?: string \| null` |
| `features/transactions/transaction-list.ts`、`.html`、`.scss`、`.spec.ts` | row 加 `byAgent`、`createdVia`；備註欄顯示標記 |
| `features/transactions/transaction-form.ts`、`.html`、`.scss`、`.spec.ts` | 編輯模式顯示唯讀的來源資訊 |

### Task 相依順序
```
T1 (dto + recordedByAgent) ─┬─▶ T2 (清單標記)
                            └─▶ T3 (表單來源資訊)    T2、T3 彼此獨立
```

## 事實查核（2026-11-01）
| 引用 | 存在？ | 證據 | 修正 |
|---|---|---|---|
| `TransactionDto`、`TransactionInput` 在 `core/api/dto.ts` | ✅ | `dto.ts:53-65` | — |
| `transactionDtoFrom(input, overrides)` | ✅ | `testing/book-fixture.ts:33` | overrides 用來帶 `createdVia` |
| 清單的 `TransactionRow` 介面、備註欄裡用 `@for` 顯示標籤 | ✅ | `transaction-list.ts:11-22`、`transaction-list.html:25-32` | 標記放在 `@for` 之前 |
| 表單的 `editing = input<TransactionDto \| null>(null)` | ✅ | `transaction-form.ts:104` | — |
| 表單結尾是 `.actions` 區塊 | ✅ | `transaction-form.html` 最後幾行 | 唯讀資訊放在 `.actions` 之前 |
| `shared/agent-origin.ts` 尚未存在 | ✅ | `ls src/app/shared` | — |
| 後端省略 null 欄位 | ⚠️ 推定 | 從 MCP 回應的行為推得；`/api` 的 JSON 選項未逐一確認 | 型別寫成 optional，讀取時 `?? null`，兩種情況都安全 |

---

## Task 1：DTO 與 recordedByAgent
**Files:** Create：`shared/agent-origin.ts`、`shared/agent-origin.spec.ts`｜Modify：`core/api/dto.ts`

判斷規則只寫在一處。日後如果有其他 client（不是 `mcp:` 開頭），只需要改這裡。

- [ ] Step 1：
```ts
import { describe, expect, it } from 'vitest';
import { recordedByAgent } from './agent-origin';

describe('recordedByAgent', () => {
  it('mcp: 開頭視為 Agent 記帳', () => {
    expect(recordedByAgent({ createdVia: 'mcp:claude.ai' })).toBe(true);
  });

  it.each([null, undefined, '', 'web'])('%s 不是 Agent 記帳', createdVia => {
    expect(recordedByAgent({ createdVia })).toBe(false);
  });
});
```
  如果既有的 spec 不是明確 import vitest（例如用 globals），就照既有寫法。先打開 `shared/tags.spec.ts` 確認。
- [ ] Step 2：執行 `--include src/app/shared/agent-origin.spec.ts`，預期因為模組不存在而失敗。
- [ ] Step 3：
```ts
import { TransactionDto } from '../core/api/dto';

/** 交易是否由 AI client 經由 MCP 記帳（後端 spec D10）；判斷集中在這裡，日後加入其他 client 只改一處。 */
export function recordedByAgent(dto: Pick<TransactionDto, 'createdVia'>): boolean {
  return dto.createdVia?.startsWith('mcp:') ?? false;
}
```
  `dto.ts` 的 `TransactionDto` 加：`createdVia?: string | null;  // 經由（例如 mcp:claude.ai），只有 Agent 記帳才有` 與 `externalRef?: string | null;  // 來源識別碼，唯讀`。
- [ ] Step 4：單一檔案 5 passed。
- [ ] Step 5：全部測試，預期 **Test Files 50、Tests 434 passed**。
- [ ] Step 6：`git add web/src/app/shared/agent-origin.ts web/src/app/shared/agent-origin.spec.ts web/src/app/core/api/dto.ts`
  Commit：`feat(web): 交易 DTO 加入經由與來源識別碼，判斷是否為 Agent 記帳`

## Task 2：交易清單的「Claude」標記
**Files:** Modify：`transaction-list.ts`、`.html`、`.scss`、`.spec.ts`

- [ ] Step 1：在 `transaction-list.spec.ts` 照既有建立元件的寫法新增兩個測試：
  - `Agent 記的交易顯示 Claude 標記`：清單有兩筆交易，一筆 `transactionDtoFrom(input, { createdVia: 'mcp:claude.ai' })`、一筆一般交易。只有第一列有 `[aria-label="由 Claude 記錄"]`，而且它的 `title` 是 `mcp:claude.ai`。
  - `一般交易沒有標記`
- [ ] Step 2：預期找不到元素而失敗。
- [ ] Step 3：
  - `TransactionRow` 加 `byAgent: boolean; createdVia: string | null;`，map 時用 `recordedByAgent(dto)` 和 `dto.createdVia ?? null`。
  - html 的備註欄，在 `@for (tag ...)` 之前加：
```html
@if (row.byAgent) {
  <span class="agent" aria-label="由 Claude 記錄" [title]="row.createdVia"><mat-icon inline>smart_toy</mat-icon>Claude</span>
}
```
  - 確認元件的 `imports` 已經有 `MatIconModule`（actions 欄已經在用 `mat-icon`）。
  - scss 加小字樣式，顏色用既有的主題變數。
- [ ] Step 4：單一檔案通過。
- [ ] Step 5：全部測試，預期 **Tests 436 passed**。
- [ ] Step 6：Commit：`feat(web): 交易清單標示由 Claude 記錄的交易`

## Task 3：編輯頁的來源資訊
**Files:** Modify：`transaction-form.ts`、`.html`、`.scss`、`.spec.ts`

- [ ] Step 1：測試
  - `編輯 Agent 記的交易時顯示經由與來源識別碼`：`editing` 設定成 `{ createdVia: 'mcp:claude.ai', externalRef: '<a@m>' }` → 畫面包含「由 Claude 記錄（mcp:claude.ai）」與「來源識別碼：<a@m>」。
  - `沒有來源識別碼時只顯示經由`
  - `新增模式不顯示來源資訊`
- [ ] Step 2：確認失敗。
- [ ] Step 3：
  - ts 加一個 `computed`：`origin = computed(() => { const e = this.editing(); return e && (e.createdVia || e.externalRef) ? { via: e.createdVia ?? null, ref: e.externalRef ?? null } : null; })`。
  - html 在 `.actions` 之前加 `@if (origin(); as o) { <p class="origin">…</p> }`。
  - 識別碼用 `<code>`，css 設 `overflow-wrap: anywhere`，長的 Message-ID 才會換行。
  - 沒有經由、只有識別碼時（目前不會發生，防禦用），只顯示識別碼那一行。
- [ ] Step 4：單一檔案通過。
- [ ] Step 5：全部測試，預期 **Tests 439 passed**。
- [ ] Step 6：Commit：`feat(web): 編輯交易時唯讀顯示經由與來源識別碼`

## 完成後的驗證
- [ ] `npx ng test --watch=false`：Test Files 50、Tests 約 439 passed（預測值），沒有失敗。
- [ ] `npx ng build`：沒有新的 warning。
- [ ] `git status`：只剩預期內的檔案，沒有 `angular.json` 的 analytics 變更。
- [ ] 3 個 commit。

## 手動驗證
1. 後端完成並套用 migration 後，`ng serve`（https，cookie 有 `__Host-` 前綴）。用 CLI 或 MCP 記一筆，清單上有「Claude」標記，滑鼠移過去顯示 `mcp:claude.ai`。
2. 打開那筆交易的編輯頁，看得到來源識別碼；修改金額並儲存後，標記與識別碼仍然在（後端 `ReplaceWith` 不動 origin）。
3. 手機寬度下，標記不會把備註擠到換行失控。

## 後續（不在本計畫內）
- 「只看 Claude 記的交易」篩選。
- 稽核歷史畫面。
