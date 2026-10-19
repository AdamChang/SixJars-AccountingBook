# 標籤（後端）設計

- 需求與 grilling 結論：2026-10-19 與使用者的 grilling（G 編號，三輪問答），本文件「決策」一節完整收錄
- 前端設計：[`2026-10-19-tags-frontend-design.md`](2026-10-19-tags-frontend-design.md)
- 相關 ADR：[`0006`](../../adr/0006-explicit-snapshot-audit-and-soft-delete.md)（稽核快照、軟刪除）、[`0008`](../../adr/0008-reports-show-spending-not-disposable-balance.md)（報表口徑）；本設計新增 [`0011`](../../adr/0011-tags-are-multi-select-and-not-additive.md)
- 用語：`CONTEXT.md` 的「標籤 (Tag)」「標籤統計 (Tag Summary)」

## 目標

使用者可以在帳本設定裡自訂標籤（名稱＋顏色），記帳時替任何交易選填最多 4 個標籤，交易清單能依標籤篩選，匯出與備份都帶著標籤。每月標籤統計的**規則**這次定案（ADR 0011），API 留到報表階段實作。

## 範圍

**包含**

- 標籤的新增、修改（名稱、顏色）、封存／解除封存、刪除（未被參照時）、排序。沿用既有設定項目的 endpoint 形狀。
- `TransactionInput`／`TransactionDto` 帶 `TagIds`；新增、修改交易時一起寫入。
- `GET /transactions?tagId=` 篩選。
- 交易明細匯出（CSV／xlsx）新增「標籤」欄。
- 備份格式 v5（標籤定義＋交易的 `TagIds`），還原接受 v1–v5。
- `CONTEXT.md`、ADR 0011。

**排除**

- 標籤統計 API（G8：這次選 A，規則已定，見「後續：標籤統計」）。
- 批次貼／撕標籤（G14）。
- 預定支出、週期預定支出、記帳範本帶標籤（G15）。
- 舊 Excel 匯入：舊帳本沒有標籤，`import-legacy` 不需修改。
- 一本帳的標籤數量上限（G19）。

## 決策

### Grilling 結論（G）

| # | 決定 | 理由 | 被否決的選項 |
|---|---|---|---|
| G1 | 標籤屬於**帳本**，是帳本設定項目，所有成員共用 | 與分類一致，可沿用設定項目機制；報表只有一份數字；目前成員只有擁有者 | 每位成員各自的私人標籤 |
| G2 | 一筆交易可掛**多個**標籤，上限 **4** 個 | 標籤的價值在於與分類正交、可疊加；單選就只是第三種分類 | 單選 |
| G3 | **所有交易類型**都可以掛 | 一件事的相關交易（提款、加值、出資金）都要能串在一起 | 只限收入、支出 |
| G4 | 名稱在同一本帳內不可重複：去頭尾空白後比對，英文不分大小寫，已封存的也算 | 避免封存後再建同名標籤，報表上分不出來 | 允許重複；只比對未封存的 |
| G5 | 名稱最多 **8 個字**，以 text element（`StringInfo`）計算，不分中英文，一個 emoji 算 1 個字 | 規則單純，前後端算法一致；標籤以 chip 顯示，太長會破版 | 依顯示寬度（中文 2、英數 1，上限 16） |
| G6 | 顏色用**固定色盤**的代號，不接受任意色碼 | 對比與報表配色由系統控制；之後要開放任意色碼很容易，反向很難 | 任意 `#RRGGBB` |
| G7 | 生命週期比照設定項目：可改名、改色（回溯生效）、排序；被參照（含已軟刪除的交易）只能封存不能刪除；可解除封存；修改交易時原本的封存標籤可以保留，但**不可新加**封存標籤 | 沿用 `CONTEXT.md`「封存」的規則與 `SettingReferences` 的判定 | — |
| G8 | 這次範圍是 A：CRUD、掛標籤、清單篩選、匯出、備份；統計 API 晚點再做，但規則現在定案 | 使用者選擇 | 這次一併做統計 API |
| G9 | 統計依**歸屬月份**歸組 | 與月報表、預算一致 | 依交易日期 |
| G10 | 統計只算收入合計、支出合計（口徑同 ADR 0008），另列筆數；其他類型不進金額 | 非收支類型的金額不是花費，加總會重複計算 | 依類型逐類加總；只算支出 |
| G11 | 統計不列「無標籤」 | 複選下各標籤加總本來就不等於總額，「無標籤」沒有對帳意義 | 列出無標籤 |
| G12 | 遵守**鎖帳日**：鎖帳日（含）以前的交易不能改標籤 | `CONTEXT.md` 鎖帳日定義是「不可再修改」；開例外要多一條寫入路徑 | 標籤不受鎖帳日限制 |
| G13 | 標籤寫在 `TransactionInput.TagIds`，新增／修改交易時一起送；沿用 `ExpectVersion` 與稽核快照 | 不新增寫入路徑，稽核自動涵蓋 | 另開只改標籤的 endpoint |
| G14 | 不做批次貼標籤 | 尚無需求 | — |
| G15 | 預定支出不帶標籤 | 付款後在實際交易上貼即可 | 預定支出帶標籤並在付款時帶入 |
| G16 | 清單篩選一次一個 `tagId` | 與既有 `accountId` 篩選同形；之後加 `tagIds` 不衝突 | 多標籤 OR／AND |
| G17 | 重複標籤、超過 4 個 → **400**；新加封存標籤、標籤不屬於本帳本 → **422** | 形狀錯誤是 validation；業務規則是 Domain | 默默去重 |
| G18 | 資料模型用關聯表 `TransactionTags(TransactionId, TagId)`，標籤是 `Book` 的 owned entity | 統計可直接 `JOIN … GROUP BY`；篩選與參照檢查可用索引 | PostgreSQL `uuid[]` 欄位 |
| G19 | 一本帳的標籤數不設上限 | 分類、帳戶也沒有；個人帳本不會多到有問題 | — |
| G20 | 交易上的標籤依**標籤的設定排序**顯示；Domain 視為集合，不記錄使用者勾選的順序 | 每筆交易的 chip 排列一致，匯出的串接順序固定 | 保留勾選順序 |
| G21 | 匯出在「備註」前加「標籤」欄，多個以「、」串接；備份帶標籤定義與交易的 `TagIds`，舊格式還原時視為無標籤 | — | — |
| G22 | 色盤 12 色：Red、Orange、Amber、Yellow、Lime、Green、Teal、Cyan、Blue、Indigo、Purple、Pink；新增時**必須**指定顏色 | — | — |

### 展開設計時新增（D）

| # | 決定 | 理由 | 被否決的選項 |
|---|---|---|---|
| D1 | **`TagId` 不建 DB FK**，參照完整性由 Domain 保證（`Book.GetTag` 找不到時擲 `DomainException`，422） | 更正 grilling 時的說法：標籤是 `Book` 的 owned entity，EF 不允許非 owner 的實體建立指向 owned type 的關聯；既有的 `Transactions.CategoryId` 也同樣沒有指向 `Categories` 的 FK。`TransactionTags.TransactionId` 仍有 cascade FK（`Transaction` 的 owned 集合） | 把 `Tag` 改成獨立 aggregate（與其他設定項目不一致，`Book` 的唯一性檢查也拿不到全部標籤）；在 migration 手寫 FK（EF model 不知道這個 FK，`DeleteBook` 的刪除順序會被它卡住） |
| D2 | 「不可新加封存標籤」放在 `CreateTransaction`／`UpdateTransaction` handler，**不放在 factory** | 事實：伺服器目前完全不擋封存的分類與帳戶，factory 也沒有這項檢查；而且還原備份要能重建掛著封存標籤的交易，factory 一擋就無法還原。只有 handler 知道「原本有哪些標籤」 | 在 `TransactionFactory` 檢查（會擋下還原與修改時保留封存標籤） |
| D3 | 個數 ≤ 4 與不重複：validator 擋（400），Domain 的 `Transaction` 也再檢查一次（422） | CLI 還原不經過 validator（`TransactionBuilder` 的既有慣例） | 只在 validator 檢查 |
| D4 | `TransactionInput.TagIds` 為**選填**（`IReadOnlyList<Guid>? TagIds = null`），null 視為空集合。修改交易時沒帶就是**清空**，不是保留 | `TransactionInput` 是平面輸入模型，其他欄位也都是「送什麼存什麼」；唯一的 client 是本專案的前端，會一律帶上。放在最後並給預設值，既有的建構呼叫（`TransactionDto.ToInput`、測試）不必改 | null 代表保留原有標籤（同一個欄位兩種語意，修改與新增行為不一致） |
| D5 | `TransactionDto.TagIds` 一律輸出（無標籤時為 `[]`），依 **Guid 值**排序；顯示時的設定排序由前端與匯出自行套用 | `TransactionDto.From` 拿不到 `Book`，無法依設定排序；Guid 排序讓備份往返的輸出穩定。參數放在 `Version` 之後並給預設值（`BackupJson` 要求建構子參數，舊備份沒有這個欄位） | `From` 多收一個 `Book` 參數（所有呼叫端都要改） |
| D6 | 備份還原的一致性檢查：`ToInput() != input` 是 record 相等，**清單欄位比的是參考**，一定不相等。改為自訂比較：其他欄位用 record 相等（`with { TagIds = null }`），`TagIds` 以排序後 `SequenceEqual` 比較，null 與空集合視為相同 | 不處理的話，所有有標籤（甚至 v5 的空陣列）的交易都會被判定為損毀 | 讓 `TagIds` 改用自訂的值相等集合型別（侵入 API DTO） |
| D7 | 名稱唯一性比對用 `string.Equals(a, b, StringComparison.OrdinalIgnoreCase)`；其他設定項目維持原本的 ordinal 比對 | G4 只適用於標籤；改動其他設定項目的規則超出範圍 | 全部設定項目改成不分大小寫 |
| D8 | 名稱長度在 Domain 與 validator 各檢查一次，都用 `new StringInfo(name.Trim()).LengthInTextElements <= 8`。DB 欄位沿用 `NameMaxLength`（100） | 8 個 text element 可能超過 8 個 UTF-16 字元（組合 emoji），DB 欄位不能用 8 | DB 欄位 `HasMaxLength(8)` |
| D9 | 顏色是 Domain enum `TagColor`（12 個值），DB 以字串存，API 以 `JsonStringEnumConverter` 輸出（`"Red"`），與 `AccountType` 等既有 enum 相同 | 不合法的值由 `IsInEnum()` 擋成 400 | 小寫代號字串（與既有 enum 的 PascalCase 不一致） |
| D10 | `SettingKind` 新增 `Tag`，API 路徑 `tags`，封存、解除封存、刪除、排序沿用 `MapSettingActions`；新增 `POST /tags`、`PUT /tags/{id}` | 與另外三種設定共用 endpoint 形狀與前端的 `SettingList` | 另開一組標籤專用 endpoint |
| D11 | 標籤的封存不檢查餘額（同分類） | 標籤不影響餘額 | — |
| D12 | 稽核 `EntityType = "Tag"`，快照用 `TagDto`；`ReorderSettings` 的 `RecordMoved` 加上標籤 | 沿用設定項目的稽核方式 | — |
| D13 | 篩選：`ListTransactions` 加 `Guid? TagId`，條件 `t.Tags.Any(x => x.TagId == tagId)`；`TransactionTags` 建 `(TagId)` 索引 | — | — |
| D14 | `IsTagReferencedAsync`：`db.Transactions.IgnoreQueryFilters().AnyAsync(t => t.BookId == bookId && t.Tags.Any(x => x.TagId == id))` | 與其他設定一樣，已軟刪除的交易也算（還原要能重建它們） | — |
| D15 | `DeleteBook` **不需修改**：`Tags` 隨 `Books` cascade，`TransactionTags` 隨 `Transactions` cascade（兩者都是 owned）；`DeleteBookEndpointsTests` 的 convention test 只列非 owned 的表，不受影響。計畫裡要在 migration 確認兩個 FK 都是 `Cascade` | — | — |
| D16 | 匯出把標籤依設定排序，以「、」串接；沒有標籤時為空字串（CSV）／空儲存格（xlsx），與其他選填欄位的處理方式一致 | — | — |

## 介面

### Domain

```csharp
// Domain/Common/Ids.cs
public readonly record struct TagId(Guid Value) { public static TagId New() => new(Guid.CreateVersion7()); }

// Domain/Books/TagColor.cs
public enum TagColor { Red, Orange, Amber, Yellow, Lime, Green, Teal, Cyan, Blue, Indigo, Purple, Pink }

// Domain/Books/Tag.cs（結構同 PlanningFund）
public sealed class Tag
{
    public const int MaxNameLength = 8;   // text element
    public TagId Id { get; }  public string Name { get; }  public TagColor Color { get; }
    public int SortOrder { get; }  public DateTimeOffset? ArchivedAt { get; }  public bool IsArchived { get; }
    internal void Rename(string name); internal void Recolor(TagColor color);
    internal void Archive(DateTimeOffset at); internal void Unarchive(); internal void MoveTo(int sortOrder);
}

// Domain/Books/Book.cs 新增
public IReadOnlyList<Tag> Tags { get; }
public Tag AddTag(string name, TagColor color, TagId? id = null);
public void UpdateTag(TagId id, string name, TagColor color);
public void ArchiveTag(TagId id, DateTimeOffset at);
public void UnarchiveTag(TagId id);
public void ReorderTags(IReadOnlyList<TagId> ids);
public void RemoveTag(TagId id, bool isReferenced);
public Tag GetTag(TagId id);           // 找不到擲 DomainException（422）

// Domain/Transactions/TransactionTag.cs（owned，比照 Posting）
public sealed class TransactionTag { public TagId TagId { get; } }

// Domain/Transactions/Transaction.cs 新增
public const int MaxTags = 4;
public IReadOnlyList<TransactionTag> Tags { get; }
public IReadOnlyCollection<TagId> TagIds { get; }    // 便利屬性
// ReplaceWith 一併換掉標籤

// Domain/Transactions/TransactionFactory.cs 新增
/// 驗證標籤都屬於本帳本、個數 ≤ 4、不重複，掛到 transaction 上（不檢查封存，見 D2）。
public Transaction WithTags(Transaction transaction, IReadOnlyCollection<TagId> tagIds);
```

### Application

```csharp
// Books/AddTag.cs、Books/UpdateTag.cs（或併入 UpdateSettings.cs，比照既有檔案）
public sealed record AddTag(Guid BookId, string Name, TagColor Color) : IRequest<Guid>, IBookScoped;
public sealed record UpdateTag(Guid BookId, Guid TagId, string Name, TagColor Color) : IRequest, IBookScoped;

// Books/BookDto.cs
public sealed record BookDto(..., IReadOnlyList<CategoryDto> Categories, IReadOnlyList<TagDto>? Tags = null);
public sealed record TagDto(Guid Id, string Name, TagColor Color, int SortOrder = 0, DateTimeOffset? ArchivedAt = null);

// Transactions
public sealed record TransactionInput(..., string? Note, IReadOnlyList<Guid>? TagIds = null);
public sealed record TransactionDto(..., uint Version, IReadOnlyList<Guid>? TagIds = null);
public sealed record ListTransactions(Guid BookId, DateOnly? From, DateOnly? To, int? BudgetMonth, Guid? AccountId, Guid? TagId = null);
```

`BookDto.From` 一律帶出 `Tags`（依 SortOrder），`TransactionDto.From` 一律帶出 `TagIds`（D5）；預設值 null 只為了讀取舊備份。

### HTTP

| 方法與路徑 | Request | 成功 | 錯誤 |
|---|---|---|---|
| `POST /api/books/{bookId}/tags` | `{ name, color }` | 201 `{ id }` | 400 名稱空白／超過 8 字／color 不合法；422 名稱重複 |
| `PUT /api/books/{bookId}/tags/{id}` | `{ name, color }` | 204 | 同上；422 找不到 |
| `POST /api/books/{bookId}/tags/{id}/archive`、`/unarchive` | — | 204 | 422 找不到 |
| `DELETE /api/books/{bookId}/tags/{id}` | — | 204 | 422 `in-use`（被交易參照，含已刪除的） |
| `PUT /api/books/{bookId}/tags/order` | `{ ids }` | 204 | 422 清單與現有標籤不一致 |
| `GET /api/books/{bookId}` | — | `BookDto` 多了 `tags` | — |
| `POST`／`PUT /transactions` | `TransactionInput` 多了 `tagIds` | `TransactionDto` 多了 `tagIds` | 400 重複或超過 4 個；422 標籤不屬於本帳本、新加的標籤已封存、`locked` |
| `GET /transactions?tagId=` | — | 只列出掛著該標籤的交易 | — |

所有新路由都在 `/api/books/{bookId}` group 下，既有的 `SecurityConventionTests`、`MembershipTests` 會自動檢查需要登入、XSRF 與非成員 404。

## 後續：標籤統計（這次不實作）

規則已定案（G9–G11、ADR 0011），報表階段實作時依此：

- 以歸屬月份與標籤歸組；只看未刪除的交易。
- `收入合計`：收入類交易金額。`支出合計`：支出類交易金額（含電子錢包消費）＋貸款繳款總額，與月報表的支出口徑相同。
- `筆數`：掛著該標籤的所有交易（含不進金額的類型）。
- 不列「無標籤」；各標籤加總不等於總收支。
- 實作位置比照 `Infrastructure/Ledger/SqlLedgerSummaryQuery.cs`，以 SQL `JOIN "TransactionTags" … GROUP BY "TagId", "BudgetMonth"` 計算。

## 對既有程式的影響

- Migration `AddTags`：新增 `Tags`（owned by Books，`BookId` FK cascade）、`TransactionTags`（PK `(TransactionId, TagId)`，`TransactionId` FK cascade，`TagId` 索引）。只新增表，不改既有欄位，不需要資料轉換。
- `Transaction` 的建構子多一個集合；`ReplaceWith` 一併替換標籤。`TransactionBuilder.Build` 在 factory 建立交易後呼叫 `WithTags`。
- `PayPlannedExpense`、`LegacyImport` 直接使用 factory，不帶標籤，不必修改。
- 備份 `CurrentFormatVersion` 5；`RestoreBook` 依序加入標籤並還原封存狀態；`RestoreTransaction` 的比較改用 D6。
- `ExportTransactions.ToRow` 與 `TransactionExportRow.Headers` 新增「標籤」欄。

## 風險

- D6 的 record 相等陷阱：沒有處理的話，**所有** v5 備份都會還原失敗。計畫的備份往返測試必須包含有標籤與沒有標籤的交易。
- D4：之後若有其他 client 修改交易卻沒帶 `tagIds`，會把標籤清空。目前唯一的 client 是本專案的前端；在 `TransactionInput` 的註解寫明。
- `ExpectVersion` 會強制整筆 `UPDATE`，所以只改標籤（只動 owned 集合）時一樣會檢查 `xmin`（spike S2c 的既有機制）。計畫要有「只改標籤也會 409」的測試。
