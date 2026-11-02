# 遠端 MCP 開放記帳：新增 ledger:write scope，由同意頁勾選授予，只能新增交易

> 部分取代 [ADR 0013](0013-remote-mcp-with-embedded-oauth.md)：「只開唯讀 tool」不再成立。授權伺服器、CIMD、PKCE、refresh token 輪替等其餘決定不變。

除了信用卡對帳，使用者也想讓 Claude 把收據、電子發票、轉帳通知裡的消費直接記進帳本。做法是在既有 `/mcp` 加記帳用的 tool，由 Claude 解析內容、使用者確認後寫入。

做法如下：

- 新增 scope `ledger:write`。**是否授予由同意頁的核取方塊決定**（預設不勾），不依 client 請求的 scope。
- 沒有寫入 scope 的 token，`tools/list` 看不到寫入 tool，呼叫時也再擋一次。
- 只開放新增交易，不開放修改與刪除；一次一筆，不做批次。
- `add_transaction` 直接寫入正式交易；另提供唯讀的 `validate_transaction` 讓 Claude 先試算。
- 交易新增 **來源識別碼 (`ExternalRef`)**：同帳本、未刪除的交易內唯一。重複時回傳既有交易並標 `duplicate: true`，不寫稽核記錄。
- 交易新增 `CreatedVia`、稽核記錄新增 `Via`（例如 `mcp:claude.ai`），網頁與 CLI 的寫入為 null。
- 每位使用者經由同一個 client，每個台北日最多寫入 `Mcp:MaxWritesPerDay` 筆（預設 15），超過回 tool error。
- tool 的 `amount` 一律正數；Expense 由 tool 層轉成負數，退款或折抵用 `refund: true`。轉換只在 Api 層做，Domain 與 `TransactionInput` 不動。
- `add_transaction` 只支援 Expense、Income、Transfer、CardPayment、Withdrawal。

ADR 0013 的翻轉條件是「需要寫入類 scope 的細緻授權時，改用成熟的授權伺服器套件較划算」。寫入在這裡只多一個 scope，由使用者在同意頁勾選一次，並沒有「每個帳本、每種操作各自授權」的需求，所以條件尚未成立，仍然不需要 OpenIddict。

## Considered Options

- **只依 client 請求的 scope 授予**：Claude 會帶什麼 scope 未經查證，不帶就永遠拿不到寫入。否決。
- **Step-up（403 `insufficient_scope` 後再授權）**：依賴 Claude 有實作，沒有把握。否決。
- **不分 scope**：既有授權會在使用者不知情下變成可寫。否決。
- **只在呼叫時擋寫入 tool**：tool 仍然出現在 Claude 的清單裡，容易被 prompt 誘導呼叫。否決。
- **草稿匣**：要新的 domain 概念、資料表與 UI，代價高；等實際常改錯再升級。否決。
- **`add_transaction` 加 `dryRun`**：Claude 的權限設定以 tool 為單位，只有唯讀 tool 才能設成自動允許。否決。
- **只靠 tool 描述要求 Claude 先查有無重複**：不保證會做。否決。
- **已刪除的交易也算重複**：刪掉記錯的那筆後，就不能用同一封信重記。否決。
- **每次記帳都不設上限**：prompt injection 失控時沒有硬上限。否決。
- **沿用 Domain 的帶號金額**：Income／Expense 的金額是帶號的，Claude 很容易把收據上的正數直接填入，結果記成退款。否決。

## Consequences

- 既有授權的 `Scope` 仍是 `ledger:read`，維持唯讀；要寫入就得重新連線並勾選。
- access token 的 `scope` 要改成每個 scope 一個 claim，否則 `"ledger:read ledger:write"` 這種空白分隔的值無法通過 `RequireClaim`。
- `McpGrant` 授予的 scope 不再等於 client 請求的 scope，同意頁必須明確說明寫入的範圍、每日上限與撤銷方式。
- 單日上限以「使用者＋client host」計數（稽核記錄沒有 grant id），重新連線不會讓計數歸零。
- 資料庫多一個 migration（`ExternalRef`、`CreatedVia`、`Via` 與 partial unique index）；備份格式版本 5 升為 6，舊備份照樣能還原。
- 同一個 `ExternalRef` 並行寫入會撞上 unique index 而失敗（MCP 路徑上是 SDK 的一般 tool error，HTTP 仍為 200），不額外處理：Claude 逐筆呼叫，實際不會發生。
- 結論翻轉的條件：需要依帳本或操作分別授權，或要開放修改與刪除時，改用成熟的授權伺服器套件較划算。
