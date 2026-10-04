# 稽核記錄保存修改前後的完整快照，由 command 明確寫入；刪除一律採軟刪除

每個寫入用的 command，都會在同一次 SaveChanges 中寫入一筆 append-only 的稽核記錄（Audit Entry），內容包含：
- 操作者、時間、動作、實體類型與 Id。
- 修改前與修改後的完整 JSON 快照（jsonb），交易的快照連同分錄一起存。

交易與預定支出的刪除都採軟刪除，記錄 `DeletedAt`。已刪除的資料不參與任何餘額與報表計算，但仍保留在 JSON 備份中。

## Considered Options

- **只存欄位差異**：省空間，但要額外寫比對邏輯，日後要還原資料也比較麻煩。個人帳本的資料量很小，空間不是問題。否決。
- **EF SaveChangesInterceptor 自動攔截**：分錄是 owned collection，修改一筆交易會被 change tracker 拆成多筆實體變更，很難組回「一筆交易的修改前與修改後」；而且自動行為不容易用測試表達「這個 command 應該留下什麼記錄」。否決。
- **硬刪除，只靠稽核記錄保留**：刪錯的交易無法直接救回，備份也會失去這些資料。否決。

## Consequences

- 每個新增的寫入 command 都必須記得呼叫 `IAuditTrail`；這個要求由 API 測試（每個寫入 endpoint 恰好留下一筆記錄）負責把關。
- SQL 彙總查詢必須明確排除已刪除的資料；EF 的 global query filter 不會套用到 raw SQL。
