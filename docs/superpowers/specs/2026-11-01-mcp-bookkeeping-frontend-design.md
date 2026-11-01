# MCP 記帳（寫入）前端設計

日期：2026-11-01｜分支：`feat/mcp-write`｜後端：`2026-11-01-mcp-bookkeeping-backend-design.md`（D10）

## 1. 目標

讓使用者在網頁上分辨哪些交易是 Claude 經由 MCP 記的，並看到它的來源識別碼。這是事後追查「Agent 依外部內容寫入」的入口。

前提修正（2026-11-01 查證）：grilling Q19 原本假設在「稽核歷史」上顯示經由。但前端沒有稽核歷史畫面，後端的 `GET /audit` 也沒有任何 UI 使用。所以改為在交易本身顯示，資料來自後端新增的 `TransactionDto.createdVia`。

## 2. 設計

- `dto.ts`：
  - `TransactionDto` 加 `createdVia: string | null` 與 `externalRef: string | null`。
  - 後端序列化時省略 null 欄位，所以前端型別寫成 `createdVia?: string | null`，讀取時用 `?? null`。
  - `TransactionInput` 不變，網頁不能寫入來源。
- 交易清單（`transaction-list`）：`createdVia` 以 `mcp:` 開頭時，在備註欄最前面、標籤之前顯示一個小標記「Claude」。
  - 用 `mat-icon` `smart_toy` 加上文字，`aria-label="由 Claude 記錄"`，`title` 顯示完整的 `createdVia`。
  - 判斷集中在一個純函式 `recordedByAgent(dto)`，放在 `shared/`，方便測試，也方便日後擴充其他 client。
- 編輯交易（`transaction-form`，編輯模式）：`editing()` 有 `createdVia` 或 `externalRef` 時，在表單底部顯示唯讀資訊：
  - 「由 Claude 記錄（mcp:claude.ai）」。
  - 「來源識別碼：…」（只在有值時顯示，長字串可換行）。
- 不加篩選條件（例如「只看 Claude 記的」），列為後續。

## 3. 被否決的方案

- **新做稽核歷史畫面**：資訊最完整，但它是一個獨立功能（需要列出前後快照的差異），不在這次範圍。
- **只顯示來源識別碼**：對話來源的交易沒有識別碼，看不出是 Claude 記的。

## 4. 測試

- `recordedByAgent`：`mcp:` 開頭回 true；`null`、`undefined`、其他值回 false。
- 交易清單：Claude 記的那一列有標記，一般的列沒有。
- 表單：編輯模式下有來源時顯示兩行資訊；新增模式，或編輯沒有來源的交易時，不顯示。

## 5. 排除

篩選「Claude 記的交易」、稽核歷史畫面、在儀表板顯示 Agent 記帳統計。
