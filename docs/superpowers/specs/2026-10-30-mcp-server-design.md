# 遠端 MCP Server（唯讀）設計

> 狀態：已核准（2026-10-30）。ADR 0013 記錄授權伺服器的取捨。

## 1. 目標

使用者目前用 Copilot 讀信用卡 PDF 帳單，再和 Excel 記帳比對，結果分成七組（參考 `reference/2026帳本v3.xlsm` 的「國泰Combo卡消費 8.14-9.15」）：

- A：完全吻合
- B：日期差 1 日
- C：合併／拆分
- D：帳單有、記帳沒有
- E：國外交易手續費
- F：記帳有、帳單沒有
- G：金額空白

如果在系統內串 LLM API，會一直產生費用。所以改讓 SixJars **當遠端 MCP server**，由使用者已付費的 Claude app（claude.ai 網頁、Desktop、手機、Claude Code）連進來：

- Claude 自己讀 PDF。
- Claude 透過 MCP 查帳本。
- Claude 自己配對並解說差異。

## 2. 已確認的決策

| # | 決策 | 選擇 | 被否決的替代方案 |
|---|---|---|---|
| D1 | 部署 | 遠端 Streamable HTTP，掛在既有 Api（同一個 Cloud Run 服務），路徑 `/mcp` | 本機 stdio：只有本機的 Desktop 能用，網頁與手機版用不到 |
| D2 | 寫入 | **唯讀**：只註冊查詢 tool，token scope 只有 `ledger:read` | 補記漏記明細：AI 誤寫入的風險，留待之後 |
| D3 | 配對 | **由 Claude 比對**。server 只提供已解析名稱的交易資料與比對指引 | server 端確定性演算法：使用者選擇保持簡單 |
| D4 | 保存 | 不保存帳單明細與比對結果；`CONTEXT.md` 的「帳單明細」「商家規則」這次不實作 | 新增 StatementLine 資料表 |
| D5 | 授權伺服器 | **在 Api 內自寫最小 OAuth 2.1 授權伺服器** | 見 §3 |
| D6 | client 註冊 | **CIMD**（Client ID Metadata Document），不做 DCR | DCR：Claude 每次重新連線都會註冊新 client，MCP 規範也在降級它；而且要多一張表與一個匿名寫入端點 |
| D7 | Token | 短效、自包含的 access token（DataProtection）＋DB 中可撤銷、會輪替的 refresh token | JWT：沒有第三方需要驗證，簽章金鑰管理是多餘的 |

## 3. 授權伺服器的取捨（D5）

Claude 連遠端 MCP server 有幾個硬性要求：必須走 OAuth；PKCE 只接受 S256；`resource_metadata` 必須放在 401 回應裡；必須有 RFC 8414 metadata。

- **Google 直接當 AS（否決）**：
  - 不支援 CIMD 與 DCR。
  - 不支援 RFC 8707 的 resource 綁定。
  - 發出的 access token audience 是 Google 自己，我們只能靠 userinfo 驗證。
- **OpenIddict（否決）**：
  - 沒有 CIMD，也沒有 DCR。
  - 會帶進 4 張資料表與大量設定。
  - 我們只需要兩種 grant。
- **Auth0／WorkOS（否決）**：個人專案多一個外部廠商。
- **Authless 或 static header（否決）**：
  - 財務資料不能交給「知道 URL 就能存取」。
  - static header 是 beta，而且是組織共用的 credential，無法辨識個人。
- **自寫（採用）**，刻意把範圍收窄：
  - grant 只有 `authorization_code`（S256 PKCE 必填）與 `refresh_token`。
  - 只支援 public client（`token_endpoint_auth_methods_supported: ["none"]`）。
  - `client_id` 必須是 HTTPS URL，而且 host 在白名單內（`Mcp:AllowedClientIdHosts`，預設 `claude.ai`）。主機不在白名單就不會發出請求，避免 SSRF。
  - 抓 CIMD 文件時：timeout 5 秒、大小上限 64 KB、不跟隨 redirect。文件中的 `client_id` 必須等於 URL 本身。
  - 請求的 `redirect_uri` 必須在文件的 `redirect_uris` 裡：
    - 一般 URI 精確比對。
    - loopback（`localhost`、`127.0.0.1`、`[::1]`）比對時忽略 port，因為 Claude Code 使用臨時 port。
  - 同意頁顯示 `client_id` 的 host，而不是文件自己宣稱的 `client_name`。

**結論會翻轉的條件**：日後若要開放給多個第三方 client，或支援 confidential client，改用成熟的授權伺服器套件較划算。

## 4. 流程

```
Claude ──POST /mcp (無 token)──────────────▶ 401  WWW-Authenticate: Bearer resource_metadata=".../.well-known/oauth-protected-resource/mcp"
Claude ──GET  /.well-known/oauth-protected-resource/mcp ─▶ { resource: "{base}/mcp", authorization_servers: ["{base}"], scopes_supported: ["ledger:read"] }
Claude ──GET  /.well-known/oauth-authorization-server ──▶ RFC 8414 metadata（CIMD、S256、none）
瀏覽器 ──GET  /oauth/authorize?client_id=https://claude.ai/...&redirect_uri=...&code_challenge=...&state=...&resource=...
          ├ 未登入 → 302 /auth/login?returnUrl=/oauth/authorize?...（沿用 Google 登入與白名單）
          └ 已登入 → 驗 client_id／redirect_uri → 同意頁（antiforgery、frame-ancestors 'none'）
瀏覽器 ──POST /oauth/authorize（同意）─────▶ 302 redirect_uri?code=...&state=...&iss=...
Claude ──POST /oauth/token grant_type=authorization_code（form-urlencoded）─▶ { access_token, refresh_token, expires_in, token_type: "Bearer", scope }
Claude ──POST /mcp Authorization: Bearer ...─▶ tool 結果
Claude ──POST /oauth/token grant_type=refresh_token ─▶ 新 access＋新 refresh（舊 refresh 失效）
```

- `{base}` 取自請求的 scheme 與 host。Cloud Run 已經透過 `UseForwardedHeaders` 還原為 https；測試時是 `https://localhost`。
- `/oauth/authorize` 是否登入，以 **cookie scheme** 判斷（測試時是 Test scheme，同樣是預設 scheme）。任何通過 Google 白名單的人都能授權；能看到哪些帳本，交給 `BookAccessBehavior` 決定。

## 5. Token 與資料

**Authorization code**
- DataProtection purpose `SixJars.Mcp.AuthorizationCode`，用 time-limited protector，TTL 60 秒。
- payload：`codeId`、`sub`、`email`、`clientId`、`redirectUri`、`codeChallenge`、`scope`、`resource`。
- **一次性**：`McpGrant.AuthorizationCodeId` 加 unique index。同一個 code 第二次兌換時插入會違反唯一性，此時回 `invalid_grant`，並撤銷第一次兌換建立的 grant（RFC 6749 §4.1.2 的建議）。

**Access token**
- purpose `SixJars.Mcp.AccessToken`，TTL 1 小時。
- payload：`grantId`、`sub`、`email`、`scope`、`audience`。
- 驗證時要同時滿足：未過期、`audience == {base}/mcp`、grant 存在且 `RevokedAt is null`。每次請求多一次以主鍵查詢，換到撤銷立即生效。

**Refresh token**
- 32 bytes 亂數，以 base64url 表示。DB 只存 SHA-256 hash，期限 30 天。
- **每次使用都輪替**。已經被輪替掉的舊 token 再次出現時，視為外洩，撤銷整個 grant（OAuth 2.1 對 public client 的要求）。
- 為了分辨「舊 token」和「亂猜的 token」，DB 也保存上一個 token 的 hash（`PreviousRefreshTokenHash`）。

**`McpGrant`**（`Domain/Members`，和 `BookCreator` 一樣是不屬於任何帳本的存取資料）：

| 欄位 | 說明 |
|---|---|
| `Id` | Guid v7 |
| `Subject` | Google sub（255） |
| `Email` | 授權當下的 email，供 CLI 以 email 撤銷 |
| `ClientId` | CIMD URL（2048） |
| `Scope` | `ledger:read` |
| `AuthorizationCodeId` | unique，防止 code 重放 |
| `RefreshTokenHash`／`PreviousRefreshTokenHash` | SHA-256 hex（64） |
| `RefreshTokenExpiresAt`、`CreatedAt`、`LastRefreshedAt`、`RevokedAt` | |

- 撤銷是更新 `RevokedAt`，不刪除資料列，事後可以追查。
- 不留稽核記錄：稽核記錄的範圍是帳本資料的寫入（ADR 0006），授權這件事不屬於帳本；做法和建立者名單一致（ADR 0011）。

## 6. 認證 scheme 的隔離

| 路徑 | 接受的 scheme | 其他 scheme 帶過來 |
|---|---|---|
| `/api/**` | cookie（預設） | bearer 不被讀取 → 401 |
| `/mcp` | `McpBearer` only（policy `Mcp` 指定 `AuthenticationSchemes`） | 只帶 cookie → 401（避免瀏覽器 CSRF 打 MCP） |
| `/oauth/authorize` | cookie | — |
| `/oauth/token`、`/.well-known/*` | 匿名 | — |

- `McpBearer` scheme：自寫的 handler，只負責 authenticate。challenge 轉給 SDK 的 `McpAuthenticationHandler`，由它在 401 加上 `resource_metadata`，並提供 `/.well-known/oauth-protected-resource/mcp`。
- 驗證成功後產生的 principal 帶 `sub`、`email`、`scope` claim，所以既有的 `HttpCurrentUser` 不用改就能用。

## 7. MCP tools

- 全部透過 `ISender` 呼叫既有 query，不新增 Application query。
- 名稱解析、帶號金額這類 mapping 寫在 tool 類別裡（手動 mapping）。

| Tool | 參數 | 呼叫 | 回傳 |
|---|---|---|---|
| `list_books` | — | `ListBooks` | `[{ id, name }]` |
| `list_accounts` | `bookId` | `GetBook` | `[{ id, name, type, archived }]` |
| `list_account_transactions` | `bookId, accountId, from, to` | `GetBook` ＋ `ListTransactions(AccountId)` | `{ account, from, to, count, total, transactions: [{ date, kind, amount, mainCategory, subCategory, counterAccount, note, tags }] }` |

- `amount` 是該筆交易**在這個帳戶上的分錄合計**（帶正負號）：
  - 信用卡刷卡 → 負數；退款、折抵、繳卡費 → 正數。
  - 沒有分錄的交易（同帳戶圈存）為 0。
  - 這樣 Claude 不用自己推算交易類型。
- 期間上限 120 天，`from` 不可晚於 `to`。違反時回 tool error。
- `accountId` 不屬於這本帳時回 tool error「找不到帳戶」。
- 帳本不存在或不是成員：`NotFoundException` → tool error「找不到帳本」。不洩漏帳本是否存在（與 404 慣例一致）。
- Server `instructions` 與 prompt `reconcile_credit_card_statement` 寫入比對指引：
  - 依帳單消費日配對，允許日期差 1 日。
  - 一筆帳單可能對應多筆記帳，反之亦然。
  - 國外交易手續費通常沒有記帳。
  - 點數折抵與退款是正數。
  - 上一期已核對的交易要排除。
  - 輸出依 A–G 分組，附差異分解與「可解釋差異／未解釋差異」。

## 8. 影響面

- `SpaHosting.BackendPrefixes` 加入 `mcp`、`oauth`、`.well-known`。否則 GET 會被改寫成 index.html，metadata 永遠拿不到。
- `SecurityConventionTests` 的匿名白名單：
  - 加入 `/oauth/**`、`/.well-known/**`。
  - `/mcp` 另外驗證「只接受 bearer」。
- `Program.cs`：加入 `AddMcpServer`、`AddMcpOAuth` 與兩個 `Map*`。
- 新 migration `AddMcpGrants`。部署後由使用者在本機套用；未套用時 `/health/ready` 回 503，這是既有行為。
- CLI 新增 `revoke-mcp-access --email`。
- 文件：ADR 0013、`CONTEXT.md` 的「MCP 授權」、`docs/deploy.md` 的「連接 Claude」、`CLAUDE.md` 的 Architecture。

## 9. 明確排除

- 寫入類 tool（補記、改日期）。
- 帳單明細與商家規則的保存、對帳狀態。
- DCR、confidential client、Web 版的授權管理頁。
- server 端配對演算法。
