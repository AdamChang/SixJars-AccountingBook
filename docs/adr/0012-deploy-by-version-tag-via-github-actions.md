# 以版本 tag 經 GitHub Actions 部署到 Cloud Run，migration 仍在本機執行

使用者於 2026-10-26 決定：正式環境改由 GitHub Actions 部署，取代 `docs/deploy.md` 原本在本機執行 `gcloud run deploy --source .` 的流程。ADR 0004／0007 的其餘決定不變：仍是 Cloud Run（asia-east1）加上 Neon，只有一個 production 環境，網址使用 Cloud Run 提供的 `*.run.app`。

- **觸發**：push 符合 `v*.*.*` 的 git tag 才部署（第一版是 `v1.0.0`）。tag 指向的 commit 必須已在 `master` 上；workflow 以 `workflow_call` 重跑一次 `ci.yml`，綠燈後才進入部署。
- **認證**：GitHub Actions 透過 Workload Identity Federation 換取 GCP 的短期憑證，repo 與 GitHub 都不存 service account key。WIF provider 只信任本 repo、`production` environment 的 `refs/tags/v*`。
- **身分**：deployer SA（`sixjars-deploy`）負責推 image 與部署；app 以專用的 runtime SA（`sixjars-run`）執行，只能讀 `sixjars-db` 與 `sixjars-google-client-secret` 兩個 secret。不再使用權限過大的 Compute Engine default service account。
- **image**：在 runner 上 build，推到 Artifact Registry `asia-east1/sixjars`，同時打上版本 tag 與 commit SHA；cleanup policy 只保留最近 3 個版本。推送前檢查 image 裡有 `Asia/Taipei` 時區檔、沒有舊 Excel。
- **migration 不進 pipeline**：仍由使用者在本機以 `sixjars_migrator` 執行 migration bundle（spec §8.4），DDL 角色的憑證不放進 Secret Manager 或 GitHub。發版順序固定為：merge 到 `master`、本機套用 migration、push tag。
- **schema 檢查**：新增 `GET /health/ready`，資料庫有這個版本認得、但尚未套用的 migration 時回 503，作為 Cloud Run 的 HTTP startup probe。漏跑 migration 時新 revision 起不來、拿不到流量，舊版繼續服務，workflow 失敗。
- **MediatR license**：以 GitHub Actions secret 保存，部署時以一般環境變數交給 Cloud Run，不放進 Secret Manager。

## Considered Options

- **Cloud Build trigger（Cloud Run 的「從存放區持續部署」）**：設定最少，但不會等 GitHub 上的 CI 通過，紅燈也會部署。否決。
- **merge 到 `master` 就部署**：使用者希望 `master` 可以先累積改動，再決定何時上線。否決。
- **pipeline 在部署前跑 migration（GitHub secret 或 Cloud Run Job）**：不會漏跑，但 DDL 憑證要離開本機，還要多一個 secret 與 service account。使用者選擇維持本機執行，以 startup probe 擋下漏跑的情況。
- **pipeline 直接連 Neon 比對 `__EFMigrationsHistory`**：deployer 也要拿到 DB 憑證，runner 要能直接連資料庫；startup probe 在 app 內檢查，不需要額外憑證，手動部署也一樣受保護。否決。
- **MediatR license 放 Secret Manager**：使用者的帳單帳戶已用滿免費的 secret version 額度，不再增加 secret；license key 不是存取憑證，外洩的影響只有授權問題。
- **staging 環境、自訂網域**：使用者只有家庭成員，CI 已有 Testcontainers 與 Playwright；asia-east1 不支援 Cloud Run domain mapping，自訂網域要另外架 load balancer。否決。

## Consequences

- **migration 必須向前相容**：發版順序是先 migrate、再部署，新 schema 上線到新 revision 接手之間，舊版仍在服務；要刪欄位或改名時，分成「先加、後刪」兩次發版。
- **回滾**：新 revision 起不來時，Cloud Run 本來就不會把流量切過去；上線後才發現問題，以 `gcloud run services update-traffic --to-revisions` 手動切回舊 revision，migration 不回滾。
- **MediatR license 以明碼出現**在 Cloud Run revision 的設定裡，專案內有 Viewer 權限的人都看得到。
- **startup probe 最多等 130 秒**（10 秒初始延遲加上 12 次、每次間隔 10 秒），涵蓋 Neon 從暫停中喚醒的時間；`/health` 維持只檢查資料庫連線。
- app 角色（`sixjars_app`）原本就有 `__EFMigrationsHistory` 的 SELECT 權限（`docs/deploy.md` §3.3），`/health/ready` 不需要新的授權。
