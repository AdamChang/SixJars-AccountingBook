# Web

SixJars 前端：Angular 22 PWA（zoneless、Angular Material）。

## 開發

需要 Node.js 24.15.0 以上（Angular 22 CLI 的需求）。

### 啟動順序

1. **先啟動後端**，監聽 `https://localhost:5001`（環境變數與 `dotnet dev-certs` 見根目錄 [README](../README.md#本機執行-api)）。
2. **再啟動前端**：

   ```bash
   npm start            # ng serve，https://localhost:4300
   ```

3. 開啟 `https://localhost:4300`。dev server 使用自簽憑證，瀏覽器第一次需要接受。

### Port 4300

`angular.json` 把 serve 設成 `port: 4300`、`ssl: true`：

- 不用 Angular 預設的 4200：這台 Windows 機器把 4150–4249 保留給系統，4200 會以 `EACCES` 啟動失敗（用 `netsh interface ipv4 show excludedportrange protocol=tcp` 檢查）。
- 一定要 https：登入與 antiforgery cookie 都用 `__Host-` 前綴，瀏覽器只接受 https 發出的這類 cookie。
- Google OAuth client 必須註冊 redirect URI `https://localhost:4300/auth/callback`。若改了 port，redirect URI 要一起改。

### Proxy

`proxy.conf.json` 把 `/api` 與 `/auth` 轉給 `https://localhost:5001`，`secure: false` 是因為後端用 dev-certs 自簽憑證。

- **不要設 `changeOrigin`**：後端依請求的 Host 組出 OAuth 的 `redirect_uri`。保留 Host 為 `localhost:4300`，Google 才會把 callback 送回 4300，再經 proxy 轉給後端，cookie 落在前端的 origin；設了 `changeOrigin: true`，cookie 會落在 5001，前端永遠是未登入。
- **改了 `proxy.conf.json` 要重啟 `ng serve`**，dev server 不會熱載入 proxy 設定。

### 建置

```bash
npx ng build         # 產出 dist/web/browser
```

production build 由後端提供（Docker image 會放進 `wwwroot`）。要在本機驗證 production build 的登入與 service worker，見 [`docs/deploy.md`](../docs/deploy.md#本機驗證-production-build-的登入與-service-worker)。

## 單元測試

```bash
npx ng test --watch=false
```

## E2E 測試

Playwright，API 以 mock 取代，並自動啟動 `ng serve`（http://localhost:4301，不需要後端，也不和開發用的 4300 衝突）。第一次使用先安裝瀏覽器：

```bash
npx playwright install chromium
npm run e2e
```
