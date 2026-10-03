# 以 BFF cookie 接 Google OIDC，白名單就是帳本成員表

登入採 BFF 模式：ASP.NET Core 自己執行 Google OIDC 的 authorization code flow，登入成功後發出同源的 HttpOnly、Secure、SameSite=Lax cookie。Angular PWA 由同一個 Cloud Run 服務提供，前端完全不經手 token。

白名單不另外維護，直接使用帳本成員（Book Member）表。第一次登入時以已驗證的 email 比對，比對成功後綁定 Google `sub`，之後都以 `sub` 辨識。不是成員的身分會被拒絕登入。所有帳本範圍的 API 都要求登入者是該帳本的成員；不是成員時回 404，不透露帳本是否存在。

## Considered Options

- **SPA 取得 Google ID token，API 驗證 JWT**：前後端可以分開部署，但 token 會存在瀏覽器，XSS 可以偷走；Google ID token 一小時就過期，前端還要處理續期。否決。
- **白名單放在環境變數**：改名單要重新部署，而且加入記帳者、唯讀角色時，還是得搬進資料庫。否決。

## Consequences

- 非 GET 的 API 請求要做 antiforgery 檢查（`XSRF-TOKEN` cookie 搭配 `X-XSRF-TOKEN` header，也就是 Angular 內建的慣例）。
- cookie 的加密金鑰（Data Protection key）必須持久化到資料庫，否則 Cloud Run 換 instance 時，所有人都會被登出。
- 第一位擁有者只能由 CLI 建立，系統沒有開放註冊。
