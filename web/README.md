# Web

SixJars 前端：Angular 22 PWA（zoneless、Angular Material）。

## 開發

```bash
npm start            # ng serve
npx ng build         # 產出 dist/
```

## 單元測試

```bash
npx ng test --watch=false
```

## E2E 測試

Playwright，API 以 mock 取代，並自動啟動 `ng serve`（http://localhost:4301）。第一次使用先安裝瀏覽器：

```bash
npx playwright install chromium
npm run e2e
```
