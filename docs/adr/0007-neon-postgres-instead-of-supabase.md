# 正式環境資料庫改用 Neon PostgreSQL，取代 Supabase

使用者於 2026-10-04 決定：正式環境的 PostgreSQL 改用 Neon 託管，取代 ADR 0004 的 Supabase。ADR 0004 其餘決定不變：應用程式仍部署在 Cloud Run（asia-east1），不採用 Cloud SQL，備份由使用者手動執行。

Neon 同樣只當作純 PostgreSQL 使用（透過 EF Core 連線），不使用 Neon 的 Data API、Auth 或其他附加功能，保留搬到任何 PostgreSQL 的可攜性。程式碼不需要修改，差異只在連線設定與部署文件。

## Consequences

- **區域**：建立專案時選離台灣最近的區域，實際可選的區域在部署時於 Neon console 確認。應用與資料庫仍然跨國，ADR 0004 對往返次數的要求不變。
- **連線**：app 與 migration 都使用 Neon 的直連 endpoint（不含 `-pooler` 的 host），並設定 `SSL Mode=Require`。Cloud Run 預設 `--min-instances 0`、instance 數量少，Npgsql 自己的連線池就足夠；不經過 Neon 的 PgBouncer（transaction mode），避免 session 狀態與 prepared statement 的限制。
- **閒置**：Neon 的 compute 閒置一段時間後會自動暫停（scale to zero），下一次連線時自動喚醒，代價是第一個請求多一次冷啟動延遲。這和 Supabase 閒置過久會暫停專案、需要手動恢復不同，所以**原本「Cloud Scheduler 每 10 分鐘 ping `/health`，避免資料庫暫停」的目的不再成立**。反而定時 ping 會讓 compute 一直醒著，可能耗盡免費方案的 compute 額度。部署文件不設定定時 ping（使用者於 2026-10-04 確認），`/health` 本身維持檢查資料庫連線。
- **安全**：段 E 安全審查的 Medium 項目（DataProtection 金鑰以明文存在 DB，Supabase 預設開放 Data API 時可能外洩）在 Neon 上風險較低：Neon 的 Data API 預設不啟用。**不要啟用 Neon Data API**；app 使用只有 DML 權限的角色，DDL 只給執行 migration 的角色。
- **備份**：Neon 內建的時間點還原只是額外保障，系統仍然必須提供可匯回的完整備份匯出（ADR 0004）。
