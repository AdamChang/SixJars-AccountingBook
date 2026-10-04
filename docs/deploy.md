# 部署：Cloud Run 與 Neon

本文件列出把 SixJars API 部署到 Google Cloud Run（asia-east1）與 Neon PostgreSQL 的完整步驟（spec §8.4、ADR 0004、ADR 0007）。
**所有指令都由使用者自己執行**；repo 與 image 裡沒有任何真實的連線字串、client secret 或 license key。

指令以 Windows PowerShell 撰寫（行尾的 `` ` `` 是換行接續）。在 bash 中，環境變數改寫成 `export 名稱=值`，行尾接續改用 `\`。

## 目錄

1. [前置準備](#1-前置準備)
2. [本機驗證 Docker image](#2-本機驗證-docker-image)
3. [Neon 資料庫](#3-neon-資料庫)
4. [Migration bundle](#4-migration-bundle)
5. [Google OAuth client](#5-google-oauth-client)
6. [Secret Manager](#6-secret-manager)
7. [Cloud Run 部署](#7-cloud-run-部署)
8. [閒置與喚醒：預設不設定 Cloud Scheduler](#8-閒置與喚醒預設不設定-cloud-scheduler)
9. [首次搬家：匯入舊 Excel](#9-首次搬家匯入舊-excel)
10. [新增成員](#10-新增成員)
11. [備份與還原](#11-備份與還原)
12. [安全注意事項](#12-安全注意事項)

---

## 1. 前置準備

| 工具 | 用途 |
|---|---|
| .NET SDK 10.0.401 以上（`global.json`） | 產生 migration bundle、執行 CLI |
| `dotnet-ef` 10.0.12 | `dotnet tool install --global dotnet-ef --version 10.0.12` |
| Docker Desktop | 本機驗證 image（第 2 節） |
| Google Cloud SDK（`gcloud`） | Secret Manager、Cloud Run |
| `psql`，或 Neon console 的 SQL Editor | 建立資料庫角色 |

```powershell
gcloud auth login
gcloud config set project <gcp-project-id>
gcloud config set run/region asia-east1
gcloud services enable run.googleapis.com secretmanager.googleapis.com artifactregistry.googleapis.com cloudbuild.googleapis.com
```

環境變數一覽（範本見 repo 根目錄的 `.env.example`）：

| 變數 | 內容 |
|---|---|
| `ConnectionStrings__SixJars` | Neon 直連 endpoint 的連線字串，帶 `SSL Mode=Require`，使用 app 角色 |
| `Authentication__Google__ClientId`、`Authentication__Google__ClientSecret` | Google OAuth client |
| `MediatR__LicenseKey` | MediatR 授權金鑰（沒有設定時只記一筆 warning） |
| `ASPNETCORE_ENVIRONMENT` | 正式環境為 `Production`（未設定時的預設值）。Development 以外缺少 Google 設定時，app 會在啟動時失敗（T36） |

---

## 2. 本機驗證 Docker image

雲端 container 中的 `docker build` 因 proxy 的 CA 無法 restore NuGet 套件，image 尚未驗證過。**第一次部署前，請在自己的電腦上完成以下檢查。**

### 2.1 建置

```powershell
docker build -t sixjars-api .
docker image ls sixjars-api
```

預期：build 成功，`docker image ls` 列出 `sixjars-api`。image 大小由 ASP.NET Core runtime image 加上發佈成果組成，尚未實測。

### 2.2 時區 `Asia/Taipei`

`/summary` 的「今天」與匯出檔名的日期都用台北時間（`TaipeiTime`），在 image 裡找不到時區資料時，這兩個功能會回 500。

```powershell
docker run --rm --entrypoint ls sixjars-api -l /usr/share/zoneinfo/Asia/Taipei
```

預期：列出該檔案。若出現 `No such file or directory`，在 `Dockerfile` 的執行階段、`USER $APP_UID` 之前加入下列內容後重新 build：

```dockerfile
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata \
    && rm -rf /var/lib/apt/lists/*
```

### 2.3 image 內沒有個資

`reference/`（舊 Excel 帳本）已由 `.dockerignore` 排除，而且 Dockerfile 只複製 `src/`。用以下指令再確認一次：

```powershell
docker run --rm --entrypoint sh sixjars-api -c "ls /app; find / -xdev \( -name reference -o -name '*.xlsm' \) 2>/dev/null"
```

預期：`ls /app` 只有 `SixJars.*.dll` 等發佈成果，`find` 沒有任何輸出。

若 image 裡沒有 `sh`，改把檔案系統匯出後檢查：

```powershell
docker create --name sixjars-check sixjars-api
docker export sixjars-check | tar -t | Select-String -Pattern 'reference/|\.xlsm$'
docker rm sixjars-check
```

預期：`Select-String` 沒有任何輸出（在 bash 中把 `Select-String -Pattern` 換成 `grep -E`）。

### 2.4 `/health`

用一個暫時的 PostgreSQL 啟動 API。沒有 Google 設定時只有 Development 能啟動，所以這裡設 `ASPNETCORE_ENVIRONMENT=Development`。
`/health` 只檢查資料庫連得上（`CanConnectAsync`），不需要 schema，所以這一步不必先跑 migration。若要用這個資料庫測試其他 API，必須先依第 4 節執行 migration bundle。

```powershell
docker network create sixjars-check
docker run -d --name sixjars-pg --network sixjars-check -e POSTGRES_PASSWORD=localonly postgres:17-alpine
docker run -d --name sixjars-api-check --network sixjars-check -p 8080:8080 `
  -e ASPNETCORE_ENVIRONMENT=Development `
  -e "ConnectionStrings__SixJars=Host=sixjars-pg;Port=5432;Database=postgres;Username=postgres;Password=localonly" `
  sixjars-api
curl.exe -i http://localhost:8080/health
docker logs sixjars-api-check
```

預期：`HTTP/1.1 200 OK`。PostgreSQL 剛啟動的幾秒內可能回 503，稍等後再試一次。

清除：

```powershell
docker rm -f sixjars-api-check sixjars-pg
docker network rm sixjars-check
```

---

## 3. Neon 資料庫

ADR 0007：Neon 只當成純 PostgreSQL 使用（EF Core 連線），**不使用 Neon 的 Data API、Auth 或其他附加功能**。

### 3.1 建立專案

1. 在 <https://console.neon.tech> 建立專案，Region 選離台灣最近的區域（例如 AWS Asia Pacific (Singapore)，實際可選的區域以 console 顯示為準）。PostgreSQL 版本選 17。
2. 建立資料庫 `sixjars`（或使用預設的 `neondb`，以下指令中的資料庫名稱要跟著改）。
3. **不要啟用 Data API**（專案設定裡的 Data API／REST API）。理由見第 12 節。

### 3.2 取得直連 endpoint

在 Connect 對話框中**關閉 Connection pooling**，取得直連 host（`ep-xxx.<region>.aws.neon.tech`，**不含** `-pooler`）。
app 與 migration 都使用直連 endpoint：Cloud Run 的 instance 很少，Npgsql 自己的連線池就夠用；PgBouncer 的 transaction mode 則有 session 狀態與 prepared statement 的限制（ADR 0007）。

Npgsql 格式的連線字串：

```text
Host=<ep-xxx.region.aws.neon.tech>;Port=5432;Database=sixjars;Username=<角色>;Password=<密碼>;SSL Mode=Require
```

### 3.3 建立角色：migration 用 DDL、app 只用 DML

以 Neon 預設的擁有者角色（`neondb_owner`），在 SQL Editor 或 `psql` 中執行。密碼請自行產生（例如 `openssl rand -base64 32`），不要寫進任何檔案。

```sql
-- 執行 migration 的角色：擁有 schema 物件，可以 DDL。只在部署前於本機使用，不放進 Cloud Run。
CREATE ROLE sixjars_migrator WITH LOGIN PASSWORD '<migrator 密碼>';
-- app（Cloud Run 與 CLI）使用的角色：只能讀寫資料，不能改 schema。
CREATE ROLE sixjars_app WITH LOGIN PASSWORD '<app 密碼>';

GRANT CONNECT ON DATABASE sixjars TO sixjars_migrator, sixjars_app;
-- PostgreSQL 15 起，public schema 預設不給 CREATE：只有 migrator 可以建表。
GRANT USAGE, CREATE ON SCHEMA public TO sixjars_migrator;
GRANT USAGE ON SCHEMA public TO sixjars_app;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
```

接著執行第 4 節的 migration（以 `sixjars_migrator` 連線），表才會存在。**migration 完成後**，再以 `sixjars_migrator` 連線執行下列授權：

```sql
-- 既有的表：Books、Accounts、PlanningFunds、Categories、Transactions、Postings、PlannedExpenses、
-- AuditEntries、BookMembers、DataProtectionKeys、__EFMigrationsHistory。
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO sixjars_app;
-- identity 欄位（例如 DataProtectionKeys.Id）的 sequence。
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO sixjars_app;

-- 稽核記錄是 append-only（ADR 0006）：app 只需要新增與讀取。
REVOKE UPDATE, DELETE ON "AuditEntries" FROM sixjars_app;
-- migration 歷史只給 app 讀。
REVOKE INSERT, UPDATE, DELETE ON "__EFMigrationsHistory" FROM sixjars_app;

-- 日後 migration 新建的表與 sequence，自動給 app 同樣的 DML 權限。
ALTER DEFAULT PRIVILEGES FOR ROLE sixjars_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO sixjars_app;
ALTER DEFAULT PRIVILEGES FOR ROLE sixjars_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO sixjars_app;
```

確認 app 角色不能改 schema（以 `sixjars_app` 連線執行，預期出現 `permission denied for schema public`）：

```sql
CREATE TABLE should_fail (id int);
```

---

## 4. Migration bundle

**不在 app 啟動時自動 migrate**（spec §8.4）：Cloud Run 多個 instance 同時啟動會互相競爭，而且 app 角色本來就沒有 DDL 權限。每次部署含 schema 變更的版本前，都先在本機執行 bundle。

```powershell
# 產生自含執行檔（Windows；在 Linux／macOS 上把 -r 改成 linux-x64／osx-arm64）
dotnet ef migrations bundle `
  --project src/SixJars.Infrastructure --startup-project src/SixJars.Infrastructure `
  --self-contained -r win-x64 -o efbundle.exe --force

# 以 migrator 角色套用到 Neon（連線字串只放在這個 PowerShell session 的變數中）
$migrator = "Host=<neon-host>;Port=5432;Database=sixjars;Username=sixjars_migrator;Password=<migrator 密碼>;SSL Mode=Require"
.\efbundle.exe --connection $migrator
```

預期：依序套用 `InitialLedger` 到 `AddDataProtectionKeys`；已套用的 migration 會略過，所以可以重複執行。
第一次執行完後，回到第 3.3 節執行「migration 完成後」的授權。

---

## 5. Google OAuth client

1. Google Cloud console → APIs & Services → OAuth consent screen：User type 選 External；Publishing status 維持 Testing，並把家庭成員的 Gmail 加進 Test users。
   - 能不能登入，最終由 app 的白名單（帳本成員表，ADR 0005）決定；Test users 只是 Google 那一層的限制。
2. Credentials → Create credentials → OAuth client ID → Application type 選 **Web application**。
3. Authorized redirect URIs 加入：

   ```text
   https://<cloud-run-host>/auth/callback
   ```

   `/auth/callback` 是 T36 設定的 `CallbackPath`。Cloud Run 的網址在第一次部署（第 7 節）後才會確定；格式通常是 `https://sixjars-<專案編號>.asia-east1.run.app`。可以先建立 client，部署後再回來補上 redirect URI。
   一定要是 `https`：app 依 `X-Forwarded-Proto` 判斷原始請求的 scheme（T44 的 ForwardedHeaders），組出的 `redirect_uri` 是 https。
4. 記下 Client ID 與 Client secret，放進第 6 節的 Secret Manager。

**本機開發的登入**：登入 cookie（`__Host-sixjars-auth`）與 antiforgery cookie（`__Host-sixjars-af`）都使用 `__Host-` 前綴，瀏覽器只接受 https 發出的這類 cookie，所以本機也必須用 https 執行：

```powershell
dotnet dev-certs https --trust
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "https://localhost:5001"
$env:ConnectionStrings__SixJars = "Host=localhost;Port=5432;Database=sixjars;Username=postgres;Password=<本機密碼>"
$env:Authentication__Google__ClientId = "<client-id>"
$env:Authentication__Google__ClientSecret = "<client-secret>"
dotnet run --project src/SixJars.Api
```

並在同一個 OAuth client 加入 redirect URI `https://localhost:5001/auth/callback`。

---

## 6. Secret Manager

把三個機密存成 secret。用 PowerShell 時以暫存檔傳入，避免值出現在命令列歷史中，用完立即刪除暫存檔。

```powershell
# 每個 secret 各做一次：sixjars-db（app 角色的連線字串）、sixjars-google-client-secret、sixjars-mediatr-license
notepad secret.txt     # 貼上值後存檔，結尾不要有換行
gcloud secrets create sixjars-db --replication-policy=automatic --data-file=secret.txt
Remove-Item secret.txt
```

- `sixjars-db`：`Host=<neon-host>;Port=5432;Database=sixjars;Username=sixjars_app;Password=<app 密碼>;SSL Mode=Require`（**app 角色**，不是 migrator）。
- `sixjars-google-client-secret`：OAuth client secret。
- `sixjars-mediatr-license`：MediatR license key。
- Google Client ID 不是機密，以一般環境變數提供即可。

更新值時加一個新版本：`gcloud secrets versions add sixjars-db --data-file=secret.txt`，再重新部署（或部署新 revision），新的 instance 才會讀到。

讓 Cloud Run 的 service account 讀得到 secret（預設是 Compute Engine default service account；若另外建立專用 service account，改用它的 email）：

```powershell
$projectNumber = gcloud projects describe <gcp-project-id> --format="value(projectNumber)"
foreach ($s in "sixjars-db", "sixjars-google-client-secret", "sixjars-mediatr-license") {
  gcloud secrets add-iam-policy-binding $s `
    --member="serviceAccount:$projectNumber-compute@developer.gserviceaccount.com" `
    --role="roles/secretmanager.secretAccessor"
}
```

---

## 7. Cloud Run 部署

由 Cloud Build 以 repo 根目錄的 `Dockerfile` 建置後部署：

```powershell
gcloud run deploy sixjars `
  --source . `
  --region asia-east1 `
  --allow-unauthenticated `
  --min-instances 0 `
  --max-instances 2 `
  --memory 512Mi `
  --set-env-vars "ASPNETCORE_ENVIRONMENT=Production,Authentication__Google__ClientId=<client-id>.apps.googleusercontent.com" `
  --set-secrets "ConnectionStrings__SixJars=sixjars-db:latest,Authentication__Google__ClientSecret=sixjars-google-client-secret:latest,MediatR__LicenseKey=sixjars-mediatr-license:latest"
```

- `--allow-unauthenticated`：讓瀏覽器連得到，登入與授權由 app 自己處理（Google 登入加上帳本成員白名單）。
- `--min-instances 0`：閒置時不計費，維持在免費方案內。代價是冷啟動，見第 8 節。
- container 監聽 8080（`ASPNETCORE_HTTP_PORTS`），與 Cloud Run 預設的 `PORT=8080` 一致；**不要**另外指定 `--port`。
- `--source .` 會上傳 build context。`.dockerignore` 已排除 `reference/`、`.env`、tests、docs，不會上傳個資。

部署完成後：

```powershell
$url = gcloud run services describe sixjars --region asia-east1 --format="value(status.url)"
curl.exe -i "$url/health"
```

預期：`200 OK`。若是 503，代表連不上資料庫：檢查連線字串（直連 host、`SSL Mode=Require`）與 secret 權限。若 revision 根本起不來，用 `gcloud run services logs read sixjars --region asia-east1` 查看；缺少 Google 設定時，啟動訊息會指出缺少哪個環境變數。

接著回到第 5 節，把 `$url/auth/callback` 加進 OAuth client 的 redirect URI，再以瀏覽器開啟 `$url/auth/login` 驗證登入（真實的 Google 登入只能手動驗證）。

---

## 8. 閒置與喚醒：預設不設定 Cloud Scheduler

ADR 0007：**預設不設定定時 ping**（待使用者確認）。

| | 不 ping（預設） | 定時 ping `/health` |
|---|---|---|
| Neon compute | 閒置數分鐘後自動暫停，下次連線時自動喚醒 | 一直醒著，持續消耗免費方案的 compute 額度，可能用完 |
| Cloud Run | 縮到 0 個 instance | 每次 ping 都會喚醒一個 instance（間隔太短時幾乎不縮到 0） |
| 閒置後的第一個請求 | 較慢：Cloud Run 冷啟動加上 Neon 喚醒，約數秒 | 快 |

個人記帳一天只用幾次，數秒的冷啟動可以接受，所以預設不 ping。Supabase 時代 ping 是為了避免專案被暫停，在 Neon 上這個理由已不成立（Neon 的暫停會自動恢復）。

如果日後仍想在固定時段（例如每天早上）預先喚醒，可以只在那個時段 ping，不要整天 ping：

```powershell
gcloud services enable cloudscheduler.googleapis.com
gcloud scheduler jobs create http sixjars-wake `
  --location asia-east1 `
  --schedule "50 7 * * *" --time-zone "Asia/Taipei" `
  --uri "$url/health" --http-method GET
```

刪除：`gcloud scheduler jobs delete sixjars-wake --location asia-east1`。

---

## 9. 首次搬家：匯入舊 Excel

在本機執行 CLI，直接寫入 Neon（使用 app 角色的連線字串即可，匯入只寫資料）。整個匯入在單一 DB transaction 中，失敗時不會留下任何資料。

```powershell
$env:ConnectionStrings__SixJars = "Host=<neon-host>;Port=5432;Database=sixjars;Username=sixjars_app;Password=<app 密碼>;SSL Mode=Require"

# 1. dry run：只轉換與檢查，印出錯誤、警告、修正與筆數，不寫入
dotnet run --project src/SixJars.Cli -- import-legacy `
  --file reference/2026帳本v1.xlsm --book-name 家庭帳本 --owner-email <你的 Gmail> --dry-run

# 2. 確認報告無誤後，拿掉 --dry-run 正式匯入
dotnet run --project src/SixJars.Cli -- import-legacy `
  --file reference/2026帳本v1.xlsm --book-name 家庭帳本 --owner-email <你的 Gmail>
```

- 報告有錯誤時 exit code 為 1，不寫入任何資料；修正 Excel 後重新執行。
- 已有同名帳本時拒絕匯入。
- 成功時印出 `已匯入帳本 <BookId>`，請記下 BookId（第 10 節會用到）。
- 擁有者第一次以 Google 登入時才綁定帳號。
- 匯入後以瀏覽器登入，確認 `/api/books/<BookId>/summary?budgetMonth=202601` 等月份的數字與 Excel 一致。

用完後清除變數：`Remove-Item Env:ConnectionStrings__SixJars`。

---

## 10. 新增成員

把另一個 Google 帳號加為同一本帳的擁有者（白名單即帳本成員表，ADR 0005）：

```powershell
dotnet run --project src/SixJars.Cli -- add-member --book <BookId> --email <對方的 Gmail>
```

對方第一次登入時綁定 Google 帳號；稽核記錄中的操作者為 `cli`。
若使用 OAuth consent screen 的 Testing 模式，也要把對方加進 Test users（第 5 節）。

---

## 11. 備份與還原

**備份**（ADR 0004：備份由使用者手動執行）：登入後，在瀏覽器開啟

```text
https://<cloud-run-host>/api/books/<BookId>/export/backup.json
```

會下載 `sixjars-backup-<yyyyMMdd>.json`。內容是整本帳，包含已刪除的資料、成員與稽核記錄，請存放在安全的地方。
Neon 內建的時間點還原只是額外保障，不能取代這份備份。

交易明細（給 Excel 看，**不能還原**）：`/api/books/<BookId>/export/transactions.xlsx` 或 `transactions.csv`，可加上 `?from=2026-01-01&to=2026-12-31`。

**還原**：只能還原到「沒有這本帳」的資料庫，例如新的 Neon branch 或新的資料庫。

```powershell
# 1. 對空的資料庫先跑 migration（第 4 節），並完成第 3.3 節的授權
.\efbundle.exe --connection $migrator

# 2. 還原（經 Domain 重新驗證，損毀的備份整個不寫入）
$env:ConnectionStrings__SixJars = "<目標資料庫的 app 連線字串>"
dotnet run --project src/SixJars.Cli -- restore-backup --file <備份檔路徑>.json
```

建議定期在 Neon branch 上做一次還原演練，確認備份可用。

---

## 12. 安全注意事項

- **不要啟用 Neon Data API**。DataProtection 金鑰（用來加密登入 cookie）以明文存在 `DataProtectionKeys` 表；任何能讀這張表的人都能偽造登入 cookie（段 E 安全審查 Medium）。同樣的理由，備份檔、Neon 連線字串與 DB 帳密都要妥善保管。
- **DataProtection 金鑰存在 DB**（spec §8.4）：Cloud Run 更換 instance 時，使用者不會被登出。不要刪除 `DataProtectionKeys` 表的資料，否則所有人都會被登出。
- **登入 cookie 無法從伺服器端撤銷**：cookie 有效 14 天，且會隨使用自動延長（sliding）。被偷的 cookie 只要對方仍是成員就有效。
  - 把成員從帳本移除後，該帳號立刻拿不到帳本資料：每個請求都會查成員表。
  - 懷疑 cookie 外洩時，最徹底的做法是清空 `DataProtectionKeys` 表，讓所有 cookie 失效（所有人都要重新登入）。
- **角色分離**：Cloud Run 只拿到 `sixjars_app`（DML）的連線字串；`sixjars_migrator`（DDL）只在本機執行 bundle 時使用，不放進 Secret Manager。
- **Forwarded headers**：app 信任任何來源的 `X-Forwarded-Proto`／`X-Forwarded-For`，因為 Cloud Run 的 container 只能經由 Google 前端連到。若改成其他可以直接連到 app 的部署方式，必須改回只信任該 proxy 的 IP（見 `ForwardedHeadersSetup`）。
- **機密不進 repo**：`.env` 已列入 `.gitignore`，repo 只放 `.env.example`；`reference/`（舊 Excel，個資）永遠不可 commit，也不會進 image。
- **個資**：舊 Excel 只在本機匯入時使用；匯入完成後不需要上傳到任何地方。
