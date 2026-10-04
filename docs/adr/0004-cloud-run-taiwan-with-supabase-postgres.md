# 應用部署在 Cloud Run (asia-east1)，資料庫使用 Supabase PostgreSQL (韓國)

> **資料庫部分已由 [ADR 0007](0007-neon-postgres-instead-of-supabase.md) 取代**：正式環境改用 Neon。Cloud Run、不採用 Cloud SQL、使用者自行備份等決定不變。

應用程式跑在 GCP Cloud Run（台灣區域），資料庫使用 Supabase 託管的 PostgreSQL（首爾區域），不採用 Cloud SQL。主要考量是成本：Cloud SQL 最小規格每月約 US$10–15，對個人記帳系統偏高。資料備份由使用者手動執行，不依賴託管服務的自動備份。

## Consequences

- Supabase 只當作純 PostgreSQL 使用（透過 EF Core 連線），不使用 Supabase Auth、RLS、Realtime 或 Storage，保留日後搬到任何 PostgreSQL 的可攜性。
- 應用與資料庫跨國，每次 DB round trip 約數十毫秒；查詢設計要避免 N+1 與多次往返。
- 備份責任在使用者身上，因此系統必須提供可匯回的完整備份匯出。
