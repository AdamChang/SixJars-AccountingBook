# 遠端 MCP 由 Api 內建的最小 OAuth 授權伺服器保護，client 以 CIMD 辨識

信用卡對帳想讓使用者已付費的 Claude app 讀取帳本，而不是讓系統自己去串 LLM API（那樣有持續費用）。Claude 的網頁版、Desktop、手機版與 Claude Code 都可以連遠端 MCP server，但前提是 server 走 MCP 規範的 OAuth：401 回應帶 `resource_metadata`、RFC 8414 metadata、S256 PKCE，client 以 CIMD 或 DCR 註冊。

做法如下：

- MCP 掛在既有 Api 的 `/mcp`，使用 Streamable HTTP（stateless），只開唯讀 tool。
- 同一個 Api 內建一個範圍刻意收窄的授權伺服器：
  - grant 只有 `authorization_code`（S256 PKCE 必填）與 `refresh_token`。
  - 只支援 public client。
  - client 只接受 **CIMD**：`client_id` 是 HTTPS URL，host 必須在白名單（預設 `claude.ai`）。授權伺服器抓這份文件，比對 `redirect_uri`。
- 使用者身分沿用既有的 Google cookie 登入與白名單（ADR 0005、0011）；能讀哪些帳本，仍由 `BookAccessBehavior` 決定。
- access token 是 DataProtection 自包含的，有效 1 小時。驗證時同時檢查 DB 中的授權（`McpGrant`）沒有被撤銷。
- refresh token 每次使用都輪替。舊 token 再次出現時視為外洩，撤銷整個授權。
- 撤銷授權由 CLI `revoke-mcp-access` 執行。

授權的異動不留稽核記錄，是 ADR 0006 的例外，理由與 ADR 0011 相同：授權不屬於任何帳本。

## Considered Options

- **本機 stdio MCP**：不必做 OAuth，但只有這台電腦的 Claude Desktop 能用，網頁版與手機版都不行。否決。
- **Google 直接當授權伺服器**：
  - Google 不支援 CIMD、DCR 與 RFC 8707 resource 綁定。
  - 發出的 token audience 是 Google，只能靠呼叫 userinfo 驗證。
  - 否決。
- **OpenIddict**：
  - 沒有 CIMD 也沒有 DCR，仍要自己補。
  - 會帶進 4 張資料表與大量設定。
  - 我們只需要兩種 grant。
  - 否決。
- **Auth0／WorkOS 等外部授權服務**：個人專案多一個廠商與帳號。否決。
- **Authless 或 static header**：財務資料不能交給「知道 URL 就能存取」；static header 還是組織共用的 credential。否決。
- **DCR**：
  - Claude 每次重新連線都會註冊一個新 client。
  - 要多一張資料表與一個匿名寫入端點。
  - Claude 的文件把 CIMD 列為優先，MCP 規範也在降級 DCR。
  - 否決，留作後備（只有在 Claude 不送 CIMD 的情況出現時才做）。
- **JWT access token**：沒有第三方需要驗證，簽章金鑰管理是多餘的；而且撤銷仍然要查 DB。否決。

## Consequences

- Api 多了 `McpBearer` 與 MCP challenge 兩個 authentication scheme。`/mcp` 只接受 bearer、`/api` 只接受 cookie，彼此不互通，由 `SecurityConventionTests` 檢查。
- 匿名端點白名單加入 `/oauth/**` 與 `/.well-known/**`；SPA fallback 也要放過這些路徑。
- 授權伺服器會對外發出 HTTP 請求（抓 CIMD），只限白名單 host，並且限制 timeout、大小、不跟隨 redirect，以免成為 SSRF 跳板。
- 結論翻轉的條件：日後要開放給多個第三方 client、支援 confidential client，或需要寫入類 scope 的細緻授權時，改用成熟的授權伺服器套件較划算。
