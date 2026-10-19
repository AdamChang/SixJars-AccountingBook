# 標籤（後端）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL：逐 Task 執行時使用 TDD（mattpocock-skills:tdd 或 superpowers:test-driven-development）。每個 Task 的流程都是：失敗測試 → 確認失敗原因 → 最小實作 → 全綠 → 一個 commit。

**Goal**：帳本可以自訂標籤（名稱＋顏色），交易可以選填最多 4 個標籤；交易清單能依標籤篩選；匯出與備份帶著標籤。

**Architecture**：`Tag` 是 `Book` 的 owned entity，與 `PlanningFund` 同構；交易以 owned 集合 `TransactionTag` 記錄掛了哪些標籤（表 `TransactionTags`）。`TransactionFactory.WithTags` 負責「標籤屬於本帳本、≤ 4、不重複」，「不可新加封存標籤」放在 Create／Update handler（spec D2）。設定的封存、刪除、排序沿用 `SettingKind` 與 `MapSettingActions`。

**Tech Stack**：.NET 10、EF Core 10（Npgsql）、MediatR、FluentValidation、xUnit v3、FluentAssertions、Testcontainers（postgres:17-alpine）。

- 設計文件：[`docs/superpowers/specs/2026-10-19-tags-backend-design.md`](../specs/2026-10-19-tags-backend-design.md)（G 編號來自 grilling，D 編號來自 spec）
- ADR：[`0011`](../../adr/0011-tags-are-multi-select-and-not-additive.md)；`CONTEXT.md` 的「標籤」「標籤統計」**已在撰寫 spec 時一併提交**，不必再做
- 後續計畫：[`2026-10-19-tags-frontend.md`](2026-10-19-tags-frontend.md)（本計畫的 checkpoint 全綠之後才能開工）

---

## 執行前必讀

### 環境

- 分支：`claude/cool-newton-xpixf5`。
- 後端全部測試：`dotnet test --solution SixJars.slnx`（需要 Docker）。單一類別：`dotnet test --project tests/SixJars.Api.Tests --filter-class "*TagEndpointsTests*"`。
- 新增 migration：`dotnet ef migrations add AddTags --project src/SixJars.Infrastructure --startup-project src/SixJars.Infrastructure --output-dir Persistence/Migrations`。

### 基準線

- 撰寫計畫的環境沒有 .NET SDK 與 Docker，**沒有實測**。開工前先跑 `dotnet build` 與 `dotnet test --solution SixJars.slnx`，把錯誤數、警告數（NuGet 弱點警告 NU1902／NU1903 不算）與測試總數記在這裡，之後只增不減。
- `reference/` 不存在時 Acceptance 會 skip，回報時要說明。

### 絕對不要碰的檔案

- `.env*`、`reference/`、`efbundle.exe`、`TestResults/`、`web/`（前端另有計畫）。
- 每個 Task 都用明確路徑 `git add`，**禁止 `git add .`／`git add -A`**。

### 慣例

- 註解與 commit 訊息用繁體中文，識別字用英文；commit 結尾加 `Co-Authored-By`（同 repo 既有 commit）。
- 每個 use case 一個檔案（或併入既有的同類檔案，例如 `UpdateSettings.cs`），record request＋validator＋`internal` handler。
- 寫入慣例照 `DeleteTransaction.cs`：查詢帶 `BookId`、`EnsureUnlocked`、`ExpectVersion`、`IAuditTrail.Record`、同一次 `SaveChangesAsync`。
- 每個 Task 至少做一次變異檢查：改壞實作，看到測試變紅，再還原。

---

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `src/SixJars.Domain/Books/Tag.cs`、`TagColor.cs` | 標籤實體與色盤 enum |
| `src/SixJars.Domain/Transactions/TransactionTag.cs` | 交易上的標籤（owned） |
| `src/SixJars.Application/Books/AddTag.cs` | 新增標籤 |
| `src/SixJars.Infrastructure/Persistence/Migrations/<timestamp>_AddTags.cs` | `Tags`、`TransactionTags` 兩張表 |
| `tests/SixJars.Domain.Tests/Books/BookTagTests.cs` | Book 的標籤規則 |
| `tests/SixJars.Domain.Tests/Transactions/TransactionTagTests.cs` | factory 與 `ReplaceWith` 的標籤規則 |
| `tests/SixJars.Api.Tests/TagEndpointsTests.cs` | 標籤設定 endpoint |
| `tests/SixJars.Api.Tests/TransactionTagEndpointsTests.cs` | 交易掛標籤、篩選、鎖帳日、並行 |

### 修改

| 檔案 | 改動 |
|---|---|
| `src/SixJars.Domain/Common/Ids.cs` | `TagId` |
| `src/SixJars.Domain/Books/Book.cs` | `Tags` 與 `AddTag`／`UpdateTag`／`ArchiveTag`／`UnarchiveTag`／`ReorderTags`／`RemoveTag`／`GetTag` |
| `src/SixJars.Domain/Transactions/Transaction.cs`、`TransactionFactory.cs` | 標籤集合、`WithTags`、`ReplaceWith` |
| `src/SixJars.Infrastructure/Persistence/BookConfiguration.cs`、`TransactionConfiguration.cs`、`SixJarsDbContext.cs` | owned 對應、`TagId` converter |
| `src/SixJars.Application/Books/BookDto.cs`、`SettingKind.cs`、`UpdateSettings.cs`、`ArchiveSetting.cs`、`RemoveSetting.cs`、`ReorderSettings.cs`、`SettingReferences.cs` | 標籤 |
| `src/SixJars.Application/Auditing/AuditEntry.cs` | `AuditEntityTypes.Tag` |
| `src/SixJars.Application/Transactions/TransactionInput.cs`、`TransactionDto.cs`、`TransactionInputValidator.cs`、`TransactionBuilder.cs`、`CreateTransaction.cs`、`UpdateTransaction.cs`、`ListTransactions.cs` | `TagIds` |
| `src/SixJars.Application/Exports/TransactionExportRow.cs`、`ExportTransactions.cs` | 「標籤」欄 |
| `src/SixJars.Application/Backup/BackupDocument.cs`、`RestoreBackup.cs` | v5 |
| `src/SixJars.Api/Endpoints/BooksEndpoints.cs`、`TransactionsEndpoints.cs` | 路由與 `tagId` query |

### Task 相依順序

```
Task 1 (Domain: Tag/Book) ─→ Task 2 (Domain: Transaction) ─→ Task 3 (EF + migration)
                                                                  │
                     ┌────────────────────────────────────────────┤
                     ▼                                            ▼
          Task 4 (標籤設定 API)                       Task 5 (交易掛標籤) ─→ Task 6 (篩選)
                     │                                            │
                     └──────────────┬─────────────────────────────┘
                                    ▼
                    Task 7 (匯出) ─→ Task 8 (備份 v5) ─→ checkpoint
```

Task 4 與 Task 5 互相獨立，可以對調。

---

## Task 1: Domain — `Tag` 與 `Book` 的標籤規則

**Files:** Create: `Domain/Books/Tag.cs`、`Domain/Books/TagColor.cs`、`tests/SixJars.Domain.Tests/Books/BookTagTests.cs`／Modify: `Domain/Common/Ids.cs`、`Domain/Books/Book.cs`

- [ ] Step 1：寫失敗測試 `BookTagTests`，至少包含：
  - `AddTag` 去頭尾空白；SortOrder 從 0 連續。
  - 名稱重複擋下（422）：`"Trip"` 之後 `" trip "` 也算重複（G4、D7）；已封存的標籤也算。
  - 名稱長度：8 個中文字可以、9 個不行；`"✈️日本旅遊"`（emoji＋4 字）是 5 個字可以；8 個 emoji 可以（D8）。
  - `UpdateTag` 改成自己原本的名字不算重複；改成另一個標籤的名字（不分大小寫）擋下。
  - `ArchiveTag` 重複封存保留第一次時間；`UnarchiveTag`。
  - `ReorderTags` 清單不一致擋下（沿用 `Reorder` helper）。
  - `RemoveTag(id, isReferenced: true)` 擲 `InUseCode`；未參照時移除並 `Compact`。
  - `GetTag` 找不到擲 `DomainException`。
- [ ] Step 2：確認測試因為型別不存在而編譯失敗。
- [ ] Step 3：實作。`Tag` 照 `PlanningFund` 的結構（`internal` 建構子與 mutator），加 `Color` 與 `Recolor`。`Book` 的唯一性與長度檢查：

```csharp
// Book.cs
private readonly List<Tag> _tags = [];
public IReadOnlyList<Tag> Tags => _tags;

public Tag AddTag(string name, TagColor color, TagId? id = null)
{
    name = RequireTagName(name);
    EnsureTagNameUnique(name, except: null);
    var tag = new Tag(id ?? TagId.New(), name, color, NextSortOrder(_tags, t => t.SortOrder));
    _tags.Add(tag);
    return tag;
}

// 標籤名稱不分大小寫（spec G4、D7）；其他設定項目維持 ordinal。
private void EnsureTagNameUnique(string name, TagId? except)
{
    if (_tags.Exists(t => t.Id != except && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
    {
        throw new DomainException($"標籤名稱「{name}」已存在。");
    }
}

private static string RequireTagName(string name)
{
    name = RequireName(name);
    return new StringInfo(name).LengthInTextElements <= Tag.MaxNameLength
        ? name
        : throw new DomainException($"標籤名稱最多 {Tag.MaxNameLength} 個字。");
}
```

  `TagColor` 是否合法由 Application 的 validator 擋（`IsInEnum`）；Domain 也加一行 `Enum.IsDefined` 防 CLI 還原時的壞資料。
- [ ] Step 4：全綠；變異檢查：把 `OrdinalIgnoreCase` 改成 `Ordinal`，確認大小寫測試變紅。
- [ ] Step 5：Commit `feat(domain): 標籤是帳本的設定項目（名稱不分大小寫唯一、最多 8 字、固定色盤）`。

---

## Task 2: Domain — 交易上的標籤

**Files:** Create: `Domain/Transactions/TransactionTag.cs`、`tests/SixJars.Domain.Tests/Transactions/TransactionTagTests.cs`／Modify: `Transaction.cs`、`TransactionFactory.cs`

- [ ] Step 1：失敗測試，至少包含：
  - 12 種交易類型都可以 `WithTags`（G3；用 `[Theory]` 列舉，每種用 factory 建一筆最小交易）。
  - 0 個與 4 個可以；5 個擲 `DomainException`；重複擲 `DomainException`（D3）。
  - 別本帳的標籤 Id → `GetTag` 擲 `DomainException`（D1）。
  - **封存的標籤可以掛**（factory 不檢查，D2）。
  - `ReplaceWith(draft)` 以 draft 的標籤取代原本的標籤（包括清空）。
  - `TagIds` 不保留順序：以集合比較。
- [ ] Step 2：確認失敗。
- [ ] Step 3：實作。`TransactionTag` 比照 `Posting`（public sealed class、`TagId` 屬性、EF 用的 private 建構子）。`Transaction` 新增 `private readonly List<TransactionTag> _tags`、`IReadOnlyList<TransactionTag> Tags`、`IReadOnlyCollection<TagId> TagIds`，以及 `internal void SetTags(IReadOnlyCollection<TagId>)`（再檢查一次 ≤ 4 與不重複）。`ReplaceWith` 加上：

```csharp
_tags.Clear();
_tags.AddRange(draft.Tags.Select(t => new TransactionTag(t.TagId)));
```

  `TransactionFactory`：

```csharp
/// <summary>
/// 掛上標籤：每個標籤都必須屬於本帳本（找不到時 422）。不檢查封存：還原備份要能重建掛著封存標籤的交易，
/// 「不可新加封存標籤」由寫入 command 檢查（spec D2）。
/// </summary>
public Transaction WithTags(Transaction transaction, IReadOnlyCollection<TagId> tagIds)
{
    foreach (var id in tagIds)
    {
        book.GetTag(id);
    }

    transaction.SetTags(tagIds);
    return transaction;
}
```

- [ ] Step 4：全綠；變異檢查：把 `ReplaceWith` 裡的 `_tags.Clear()` 拿掉，確認清空的測試變紅。
- [ ] Step 5：Commit `feat(domain): 交易可掛最多 4 個標籤，修改交易時一併替換`。

---

## Task 3: Infrastructure — EF 對應與 migration

**Files:** Modify: `BookConfiguration.cs`、`TransactionConfiguration.cs`、`SixJarsDbContext.cs`（`TagId` converter，比照其他 Id）／Create: migration `AddTags`、`tests/SixJars.Infrastructure.Tests` 內的持久化測試

- [ ] Step 1：失敗測試（Infrastructure.Tests，用既有的 `PostgresFixture`）：新增帳本與兩個標籤（其中一個封存），新增一筆掛兩個標籤的交易，換一個 `DbContext` 讀回，標籤的名稱、顏色、SortOrder、ArchivedAt 與交易的 `TagIds` 都相同；再 `ReplaceWith` 成只有一個標籤，存檔後讀回只剩一個。
- [ ] Step 2：設定 EF：

```csharp
// BookConfiguration
builder.OwnsMany(b => b.Tags, tag =>
{
    tag.ToTable("Tags");
    tag.WithOwner().HasForeignKey("BookId");
    tag.HasKey(t => t.Id);
    tag.Property(t => t.Id).ValueGeneratedNever();
    // 8 個 text element 可能超過 8 個 UTF-16 字元（spec D8），欄位長度沿用 NameMaxLength。
    tag.Property(t => t.Name).HasMaxLength(NameMaxLength);
    tag.Property(t => t.Color).HasConversion<string>().HasMaxLength(16);
});
builder.Navigation(b => b.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);

// TransactionConfiguration
builder.OwnsMany(t => t.Tags, tag =>
{
    tag.ToTable("TransactionTags");
    tag.WithOwner().HasForeignKey("TransactionId");
    tag.HasKey("TransactionId", nameof(TransactionTag.TagId));
    tag.HasIndex(x => x.TagId);
});
builder.Navigation(t => t.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);
```

- [ ] Step 3：產生 migration，**檢查產生的程式碼**：`FK_Tags_Books_BookId` 與 `FK_TransactionTags_Transactions_TransactionId` 都是 `ReferentialAction.Cascade`（spec D15，`DeleteBook` 依賴這兩個 cascade）；沒有指向 `Tags` 的 FK（D1）；沒有動到既有的表。
- [ ] Step 4：全綠（含既有的 `DeleteBookEndpointsTests` convention test，它只列非 owned 的表，應該不受影響）。
- [ ] Step 5：Commit `feat(infra): 新增 Tags 與 TransactionTags 資料表`。

---

## Task 4: 標籤設定的 use case 與 endpoint

**Files:** Create: `Application/Books/AddTag.cs`、`tests/SixJars.Api.Tests/TagEndpointsTests.cs`／Modify: `BookDto.cs`、`SettingKind.cs`、`UpdateSettings.cs`、`ArchiveSetting.cs`、`RemoveSetting.cs`、`ReorderSettings.cs`、`SettingReferences.cs`、`AuditEntry.cs`、`BooksEndpoints.cs`

- [ ] Step 1：失敗測試 `TagEndpointsTests`（比照既有設定 endpoint 的測試寫法），至少包含：
  - `POST /tags` → 201；`GET /books/{id}` 的 `tags` 依 SortOrder 列出，`color` 是字串（`"Teal"`）。
  - 400：名稱空白、9 個字、`color` 不是 12 色之一。
  - 422：名稱與既有標籤只差大小寫。
  - `PUT /tags/{id}` 改名、改色；稽核記錄有 `Update`／`Tag` 與前後快照。
  - archive／unarchive；`PUT /tags/order`；排序只為移動的標籤寫稽核記錄。
  - `DELETE /tags/{id}`：未參照 → 204；被**已軟刪除**的交易參照 → 422 `in-use`（D14）。這個測試要等 Task 5 才能建立掛標籤的交易，可以先用 `ISixJarsDbContext` 直接 seed（比照 `ApiSeed`），或把這一個測試移到 Task 5。
- [ ] Step 2：實作。
  - `TagDto` 與 `BookDto.Tags`（參數放最後、預設 null，`From` 一律帶出，見 spec D5 的理由）。
  - `AddTag`：validator `Name` NotEmpty＋`Must(n => new StringInfo(n.Trim()).LengthInTextElements <= Tag.MaxNameLength)`，`Color` `IsInEnum()`。
  - `UpdateTag` 放進 `UpdateSettings.cs`（與 `UpdatePlanningFund` 並列）。
  - `SettingKind.Tag`；`ArchiveSetting`／`UnarchiveSetting`／`RemoveSetting`／`ReorderSettings` 的 `switch` 加 `case SettingKind.Tag`。**注意**：這幾個 handler 的 `default:` 分支目前代表分類，新增 case 時要放在 `default` 之前，不要把標籤落進分類的分支。
  - `ReorderSettings.RecordMoved` 加上 `before.Tags!`／`after.Tags!`。
  - `SettingReferences.IsTagReferencedAsync`（D14）。
  - `AuditEntityTypes.Tag = "Tag"`。
  - `BooksEndpoints`：`POST /tags`、`PUT /tags/{id:guid}`（body 用 `UpdateTagBody(string Name, TagColor Color)` 或直接綁 command，比照 `UpdatePlanningFund` 的寫法），以及 `MapSettingActions(book, "tags", SettingKind.Tag)`。
- [ ] Step 3：全綠（既有的 `SecurityConventionTests`、`MembershipTests` 會自動涵蓋新路由）；變異檢查：把 `RemoveSetting` 的標籤分支改成 `isReferenced: false`，確認 `in-use` 測試變紅。
- [ ] Step 4：Commit `feat(api): 標籤的新增、修改、封存、刪除與排序`。

---

## Task 5: 交易掛標籤

**Files:** Create: `tests/SixJars.Api.Tests/TransactionTagEndpointsTests.cs`／Modify: `TransactionInput.cs`、`TransactionDto.cs`、`TransactionInputValidator.cs`、`TransactionBuilder.cs`、`CreateTransaction.cs`、`UpdateTransaction.cs`；`tests/SixJars.Application.Tests/Transactions/TransactionInputValidatorTests.cs`

- [ ] Step 1：失敗測試：
  - Validator：`TagIds` 5 個 → 400；有重複 → 400；含 `Guid.Empty` → 400；null 與 `[]` 都可以。
  - `POST /transactions` 帶兩個標籤 → 201，回傳的 `tagIds` 依 Guid 排序（D5）；沒帶 `tagIds` → `tagIds` 為 `[]`。
  - 轉帳、加值、出資金等非收支類型也能掛（G3，挑兩三種即可，Domain 已涵蓋全部）。
  - 別本帳的標籤 Id → 422。
  - 新增時帶封存標籤 → 422；**修改時保留原本已封存的標籤 → 200**；修改時新加另一個封存標籤 → 422（G7、D2）。
  - 修改時不帶 `tagIds` → 標籤被清空（D4，把行為釘住）。
  - 交易日期在鎖帳日（含）以前：修改標籤 → 422 `locked`（G12）。
  - **只改標籤**、用舊的 `version` → 409（spec 風險：owned 集合的並行檢查）。
  - 稽核記錄的 `Before`／`After` 含 `tagIds`。
- [ ] Step 2：實作。
  - `TransactionInput` 最後加 `IReadOnlyList<Guid>? TagIds = null`，註解寫明「null 視為沒有標籤；修改交易時沒帶就是清空（spec D4）」。
  - `TransactionDto` 在 `Version` 之後加 `IReadOnlyList<Guid>? TagIds = null`；`From` 帶 `[.. t.TagIds.Select(id => id.Value).Order()]`；`ToInput` 帶回 `TagIds`。
  - Validator：

```csharp
RuleFor(i => i.TagIds)
    .Must(ids => ids!.Count <= Transaction.MaxTags).WithMessage($"一筆交易最多 {Transaction.MaxTags} 個標籤。")
    .Must(ids => ids!.Distinct().Count() == ids!.Count).WithMessage("標籤不可重複。")
    .When(i => i.TagIds is not null);
RuleForEach(i => i.TagIds).NotEmpty();
```

  - `TransactionBuilder.Build`：`return factory.WithTags(transaction, [.. (input.TagIds ?? []).Select(id => new TagId(id))]);`（把現有的 `return input.Kind switch …` 改成先存到區域變數）。
  - `CreateTransaction`／`UpdateTransaction` handler 在 `Build` 之後呼叫同一個 helper（放在 `TransactionBuilder` 旁，例如 `TransactionTagRules.EnsureNoNewArchivedTags(book, before, after)`）：

```csharp
/// <summary>封存的標籤不可新加；修改交易時原本就掛著的可以保留（spec G7、D2）。</summary>
internal static void EnsureNoNewArchivedTags(Book book, IReadOnlyCollection<TagId> existing, IReadOnlyCollection<TagId> requested)
{
    var archived = requested.Except(existing).Select(book.GetTag).FirstOrDefault(t => t.IsArchived);
    if (archived is not null)
    {
        throw new DomainException($"標籤「{archived.Name}」已封存，不能再加到交易上。");
    }
}
```

    新增時 `existing` 為空集合；修改時是 `transaction.TagIds`（**在 `ReplaceWith` 之前**取得）。
- [ ] Step 3：全綠；變異檢查：把 `requested.Except(existing)` 改成 `requested`，確認「保留封存標籤」測試變紅。
- [ ] Step 4：Commit `feat(api): 新增與修改交易時可帶最多 4 個標籤`。

---

## Task 6: 交易清單依標籤篩選

**Files:** Modify: `ListTransactions.cs`、`TransactionsEndpoints.cs`、`TransactionTagEndpointsTests.cs`

- [ ] Step 1：失敗測試：同月三筆交易（A 標籤、A＋B、無標籤），`?budgetMonth=…&tagId=A` 回兩筆、`tagId=B` 回一筆；`tagId` 是別本帳的標籤 → 空清單（不是 404：與 `accountId` 篩選相同的行為，先確認 `accountId` 的既有行為並對齊）。
- [ ] Step 2：`ListTransactions` 加 `Guid? TagId = null`；`query = query.Where(t => t.Tags.Any(x => x.TagId == tagId))`；endpoint 多綁一個 `Guid? tagId` query。
- [ ] Step 3：全綠；Commit `feat(api): 交易清單可依標籤篩選`。

---

## Task 7: 匯出「標籤」欄

**Files:** Modify: `TransactionExportRow.cs`、`ExportTransactions.cs`、既有的匯出測試

- [ ] Step 1：失敗測試：標籤設定排序為「工作、旅遊」，交易掛「旅遊、工作」→ 匯出「工作、旅遊」；沒有標籤 → 空白；`Headers` 的「標籤」在「備註」之前。CSV 與 xlsx 各一個斷言。
- [ ] Step 2：`TransactionExportRow` 在 `Note` 前加 `string? Tags`；`ToRow` 以 `book.Tags` 的 SortOrder 排序後 `string.Join("、", …)`，空集合時為 null（D16）。
- [ ] Step 3：全綠；Commit `feat(export): 交易明細匯出新增標籤欄`。

---

## Task 8: 備份格式 v5

**Files:** Modify: `BackupDocument.cs`、`RestoreBackup.cs`、既有的備份往返測試

- [ ] Step 1：失敗測試：
  - 往返：帳本有 3 個標籤（其中 1 個封存、排序非建立順序），交易有掛 0、1、4 個標籤的，以及一筆**已軟刪除且掛著封存標籤**的；匯出 → 還原到新資料庫 → 再匯出，兩份 JSON（扣除 `ExportedAt` 與 `Version`）相同。
  - v4 的既有測試備份（沒有 `tags`、沒有 `tagIds`）照樣還原成功，交易沒有標籤。
  - 手動把備份裡某筆交易的 `tagIds` 改成不存在的 Id → 還原失敗（422），不寫入任何資料。
- [ ] Step 2：實作。
  - `CurrentFormatVersion = 5`；`BackupDocument` 的摘要註解補上「v5 起帶有標籤」。
  - `RestoreBook`：依 DTO 順序 `AddTag(name, color, new TagId(id))`，再依 `ArchivedAt` 呼叫 `ArchiveTag`；一致性檢查加上 `restored.Tags!.SequenceEqual(expected.Tags ?? [])`（v1 時同樣忽略 SortOrder）。
  - `RestoreTransaction` 的比較改用 spec D6（**必做**，否則所有 v5 備份都會還原失敗）：

```csharp
// record 相等對清單比的是參考，TagIds 另外以排序後的內容比較；null 與空集合視為相同（spec D6）。
private static bool SameInput(TransactionInput a, TransactionInput b) =>
    a with { TagIds = null } == b with { TagIds = null }
    && (a.TagIds ?? []).Order().SequenceEqual((b.TagIds ?? []).Order());
```

- [ ] Step 3：全綠；變異檢查：改回 `!=`，確認往返測試變紅。
- [ ] Step 4：Commit `feat(backup): 備份格式 v5 帶有標籤`。

---

## 後端 checkpoint

- [ ] `dotnet build`：0 錯誤、0 編譯器警告。
- [ ] `dotnet test --solution SixJars.slnx`：全綠，總數 ≥ 基準線＋本計畫新增的測試；回報時說明 Acceptance 是否有實際執行。
- [ ] 本機跑 API（`docs/deploy.md`），用 `.http` 或 Swagger 手動走一次：建立標籤 → 記一筆掛兩個標籤的交易 → 篩選 → 下載 CSV 看到標籤欄 → 封存標籤後修改那筆交易（保留可、新加不可）。
- [ ] 確認 `git status` 沒有誤加的檔案，再交給前端計畫。
