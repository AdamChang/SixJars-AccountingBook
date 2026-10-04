# P2 後端：本機驗證與待決事項交接（2026-10-04）

給在使用者本機（Windows）接手的新 session。P2 後端的程式碼已全部完成，雲端 container 做不到的驗證，以及一項待使用者決定的事，都整理在這裡。

## 1. 目前狀態

- **分支**：`claude/p2-planning-83al3h`，最後一個 commit 為 `74cace0`（`docs(plans): 回寫段 F 執行紀錄…`），已 push。尚未建立 PR。
- **計畫**：`docs/superpowers/plans/2026-10-04-p2-backend-api.md` 的 T17–T45 全部完成，四個 Checkpoint（C、D、E、F）都通過。各段的偏差、變異測試與決定，見計畫內的「段 C／D／E／F 執行紀錄」。
- **雲端的測試結果**：總計 344、失敗 0、略過 18，build 0 warning。
  - 略過的 18 個都需要 `reference/`（真實的財務資料，雲端沒有）：P1 的 Excel 驗收 12 個、T27 的 SQL 版 Excel 驗收 3 個、T40 的 CLI 匯入驗收 3 個。**這 18 個測試從來沒有跑過**。
- **相關文件**：
  - 部署步驟：`docs/deploy.md`（Neon 版，指令為 PowerShell）。
  - 正式環境資料庫改用 Neon：`docs/adr/0007-neon-postgres-instead-of-supabase.md`。
  - spec：`docs/superpowers/specs/2026-10-04-p2-backend-api-design.md`。

## 2. 工作規則（延續前一個 session，務必遵守）

- 回覆一律使用繁體中文，技術名詞除外；說明時寫出取捨。
- `git add` 只用明確路徑，**禁止 `git add .`／`git add -A`**。**永遠不可 add `reference/`**（真實財務資料與個資），也不可 add `.env`。
- 連線字串、密碼、Google client secret 等機密不可寫進 repo；只能放環境變數、`.env`（已列入 `.gitignore`）或 Secret Manager。
- 驗證不通過時，**不可放寬驗收標準**；遇到業務規則衝突時停下來，列出選項讓使用者決定。
- 修正程式碼時照 TDD：先寫會失敗的測試，再修正；一個修正一個 commit。
- 部署由使用者自己執行，agent 不執行任何部署指令（gcloud、Neon、對正式資料庫執行 migration 等）。
- 只在 `claude/p2-planning-83al3h` 上工作；使用者沒有要求時不建立 PR。

## 3. 需要在本機做的事

### 前置條件

- .NET SDK 10.0.401 以上（`dotnet --version`）。
- **Docker Desktop 必須在執行中**：所有資料庫測試都用 Testcontainers 啟動 `postgres:17-alpine`。
- `reference/2026帳本v1.xlsm` 放在 repo 根目錄。
  - 檔案放在別處時，可設定環境變數 `SIXJARS_LEGACY_WORKBOOK` 指向該檔案（見 `tests/Shared/RepoPaths.cs`）。
- 先取得最新的分支：

  ```powershell
  git fetch origin claude/p2-planning-83al3h
  git checkout claude/p2-planning-83al3h
  git pull origin claude/p2-planning-83al3h
  ```

### 3.1 跑完整的測試，包含 Excel 驗收

```powershell
dotnet build
dotnet test
```

**預期**：總計 344、失敗 0、**略過 0**。

這次會第一次跑到雲端略過的 18 個測試，涵蓋：
- **P1 的 Excel 驗收**：T27 曾重構它共用的比對程式 `MonthFigureComparison`，主控者逐行核對過語意不變，但沒有實際跑過。
- **`SqlLedgerAcceptanceTests`**：用 SQL 彙總算出 1–3 月的數字，再與 Excel 比對。
- **`CliImportAcceptanceTests`**：透過 CLI 的 `import-legacy` 匯入真實 xlsm，再用 SQL 彙總與 Excel 比對 1–3 月。這也涵蓋計畫 Checkpoint F 原本要手動做的「`/summary` 1–3 月與 Excel 一致」。

**如果有測試失敗**：
- 先判斷是程式錯誤，還是 Excel 本身的已知差異（`tests/SixJars.AcceptanceTests/KnownExcelDifferences.cs`）。
- **不可為了讓測試通過而放寬比對規則或擴大 `KnownExcelDifferences`**，要先列出差異並請使用者決定。
- 回報時不要貼出真實金額以外的個資。

### 3.2 驗證 Docker image

雲端的 `docker build` 在 `dotnet restore` 失敗：build 容器不信任雲端 proxy 的憑證（`NU1301 UntrustedRoot`），與程式碼無關。所以 Dockerfile 從來沒有成功 build 過。

請照 **`docs/deploy.md` 第 2 節**逐項執行：

| 節 | 檢查 | 預期 |
|---|---|---|
| 2.1 | `docker build -t sixjars-api .` | 成功 |
| 2.2 | image 內可以解析 `Asia/Taipei` 時區 | `/usr/share/zoneinfo/Asia/Taipei` 存在 |
| 2.3 | image 內沒有個資 | 沒有 `reference` 目錄，也沒有任何 `.xlsm` |
| 2.4 | 以暫時的 postgres 啟動，`curl localhost:8080/health` | 回 200 |

**特別注意 2.2**：`mcr.microsoft.com/dotnet/aspnet:10.0` 是否內建 tzdata 尚未確認。如果沒有，`/summary`、交易明細與備份的匯出檔名都會回 500。修正方式是在 Dockerfile 的 runtime stage 安裝 `tzdata`（`docs/deploy.md` 2.2 附有寫法）。改完要重新 build 並重跑 2.1–2.4，再以一個 commit 提交。

**2.3 的備案**：image 沒有 `sh` 時，改用文件裡 `docker create` 加 `docker export | tar -t` 的方式檢查。

### 3.3 用真實資料試匯入到本機資料庫

自動化測試用的是暫時的資料庫，跑完就消失。這一步讓使用者親眼看過匯入報告，確認警告與自動修正都合理。

```powershell
# 1. 啟動本機的 postgres（密碼只用於本機）
docker run -d --name sixjars-local -e POSTGRES_PASSWORD=localonly -p 5432:5432 postgres:17-alpine

# 2. 建立 schema：產生 migration bundle 並執行（startup project 必須是 Infrastructure）
dotnet ef migrations bundle --project src/SixJars.Infrastructure --startup-project src/SixJars.Infrastructure -o efbundle.exe --force
.\efbundle.exe --connection "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=localonly"

# 3. 先 dry run，只印報告與筆數，不寫入
$env:ConnectionStrings__SixJars = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=localonly"
dotnet run --project src/SixJars.Cli -- import-legacy --file reference/2026帳本v1.xlsm --book-name 家庭帳本 --owner-email <使用者的 Google email> --dry-run

# 4. 使用者確認報告無誤後，拿掉 --dry-run 正式匯入
dotnet run --project src/SixJars.Cli -- import-legacy --file reference/2026帳本v1.xlsm --book-name 家庭帳本 --owner-email <使用者的 Google email>
```

**預期**：
- dry run 的報告沒有錯誤（Errors）。有錯誤時 exit code 為 1，而且不會寫入。
- 警告與自動修正由使用者逐條確認。
- 正式匯入後印出 BookId。

**注意**：
- `efbundle.exe` 是建置產物，不要 commit。
- 用完可以用 `docker rm -f sixjars-local` 刪掉這個 container。

**目前在本機無法用 API 驗證**：本機透過 API 查 `/summary` 必須先登入。Development 環境沒有設定 Google client id／secret 時，不會註冊 Google 登入，所以沒有辦法取得登入 cookie。Google 登入的驗證要等前端完成、或設定好 Google OAuth client（redirect URI `https://localhost:<port>/auth/callback`）之後再做。`/summary` 的數字已由 3.1 的 `CliImportAcceptanceTests` 自動比對。

### 3.4 記錄結果

- 驗證結果回寫到計畫 `docs/superpowers/plans/2026-10-04-p2-backend-api.md` 的「段 F 執行紀錄」。在「待使用者在本機驗證」下面逐項註明結果與日期，commit 訊息用 `docs(plans):`。
- 若因驗證而修改程式碼或 Dockerfile，每個修正一個 commit，並重跑 `dotnet test`，確認總計 344 以上、失敗 0、略過 0。

## 4. 需要使用者決定的事

### 是否設定 Cloud Scheduler 定時 ping `/health`

**背景**：原本的計畫是讓 Cloud Scheduler 每 10 分鐘 ping 一次 `/health`，避免 Supabase 閒置暫停。正式環境改用 Neon 之後（ADR 0007），這個理由不成立了：
- Neon 閒置時會自動暫停（scale to zero），下一次連線時自動喚醒，不需要手動恢復。
- Neon 依資料庫醒著的時間計算 compute 用量。定時 ping 會讓它一直醒著，可能用完免費方案的額度。

| 選項 | 好處 | 代價 |
|---|---|---|
| **A. 不設定（目前的預設，建議）** | 不耗用額度，維持免費 | 閒置後第一個請求較慢：Cloud Run 與 Neon 各冷啟動一次 |
| B. 設定定時 ping | 回應一直很快 | 資料庫與 Cloud Run 一直醒著，可能超出免費額度 |
| C. 低頻率喚醒（例如每天早上一次） | 每天第一次使用時比較快 | 效果有限，仍有少量用量 |

- **程式碼不需要修改**。`docs/deploy.md` 第 8 節已附上可選的定時喚醒指令（每日一次，改 cron 即可調整頻率）與刪除指令。
- 使用者決定後：
  - 更新 ADR 0007「閒置」那一點的「待使用者確認」字樣。
  - 更新 `docs/deploy.md` 第 8 節。
  - 更新計畫段 F 執行紀錄的「待使用者決定」。
  - 以一個 `docs:` commit 提交。

## 5. 驗證都完成之後

- P2 後端即告完成，接下來可以建立 PR，或開始前端 PWA。前端要另寫 spec 與 plan，見計畫的「後續（不在本計畫內）」。
- 正式部署照 `docs/deploy.md` 由使用者執行。部署時要特別注意：
  - Neon **不要啟用 Data API**。
  - Google OAuth 的 redirect URI 是 `https://<cloud-run-host>/auth/callback`。
  - 正式環境缺少 Google 設定時，app 會在啟動時直接失敗。這是刻意的設計。
