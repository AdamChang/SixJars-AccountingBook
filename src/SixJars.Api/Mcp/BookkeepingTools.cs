using System.ComponentModel;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Application.Transactions;
using SixJars.Domain.Common;

namespace SixJars.Api.Mcp;

/// <summary>
/// 記帳用的 tools（spec §6）：讀取 tool 讓 Claude 記帳前先查到分類與標籤的 Id、找出已記過的交易，並試算（validate_transaction）；
/// 唯一的寫入 tool 是 add_transaction，限有 ledger:write 的授權。
/// </summary>
/// <remarks>
/// 與 <see cref="LedgerTools"/> 分開：該類別的契約是「唯讀 ledger tools」，寫入 tool 要另外掛權限。
/// 寫入的把關有三層：授權（唯讀 token 看不到也叫不動）、每日上限（<see cref="McpWriteQuota"/>）、金額方向由 tool 層決定（<see cref="McpTransactionArgs"/>）。
/// 帳戶、分類、標籤一律用 Id（D8），所以回傳都帶 Id；search_transactions 則反過來回傳解析好的名稱，讓 Claude 能直接拿來跟使用者對話。
/// </remarks>
[McpServerToolType]
internal sealed class BookkeepingTools(ISender sender, ISixJarsDbContext db, McpWriteQuota quota)
{
    [McpServerTool(Name = "list_categories", ReadOnly = true, Destructive = false)]
    [Description("""
        列出帳本的分類樹（含已封存），記帳時的 categoryId 從這裡取得。
        每個元素是大類：kind 為 Income（收入）或 Expense（支出）；nature 是支出的性質（收入為空）；children 是子分類。
        categoryId 可以填子分類或大類的 id；已封存（archived 為 true）的分類不能用來新增交易。
        """)]
    public async Task<IReadOnlyList<CategoryItem>> ListCategoriesAsync(
        [Description("帳本 id（list_books 取得）")] Guid bookId, CancellationToken cancellationToken)
    {
        var book = await McpBooks.GetBookAsync(sender, bookId, cancellationToken);
        var childrenByParent = book.Categories.Where(c => c.ParentId is not null).ToLookup(c => c.ParentId!.Value);
        return
        [
            .. book.Categories.Where(c => c.ParentId is null).OrderBy(c => c.SortOrder).Select(c => new CategoryItem(
                c.Id, c.Name, c.Kind.ToString(), c.Nature?.ToString(), c.ArchivedAt is not null,
                [.. childrenByParent[c.Id].OrderBy(child => child.SortOrder).Select(child => new SubCategoryItem(child.Id, child.Name, child.ArchivedAt is not null))])),
        ];
    }

    [McpServerTool(Name = "list_tags", ReadOnly = true, Destructive = false)]
    [Description("列出帳本的標籤（含已封存），記帳時的 tagIds 從這裡取得。已封存（archived 為 true）的標籤不能用在新交易。")]
    public async Task<IReadOnlyList<TagItem>> ListTagsAsync(
        [Description("帳本 id（list_books 取得）")] Guid bookId, CancellationToken cancellationToken)
    {
        var book = await McpBooks.GetBookAsync(sender, bookId, cancellationToken);
        return [.. (book.Tags ?? []).OrderBy(t => t.SortOrder).Select(t => new TagItem(t.Id, t.Name, t.ArchivedAt is not null))];
    }

    [McpServerTool(Name = "search_transactions", ReadOnly = true, Destructive = false)]
    [Description("""
        在一段期間（依交易日期，含頭尾，最多 120 天）內搜尋交易，記帳前用來確認有沒有已經記過的。amount、keyword、sourceId、accountId 都是選填，有填的條件全部成立才會列出。
        amount 比對交易金額的絕對值，所以直接填收據上的數字即可（不分支出或退款）；keyword 比對備註，不分大小寫；sourceId 是完全相同比對（信件的 Message-ID 等）；accountId 同時比對主帳戶與對方帳戶。
        回傳的 amount 是交易金額：支出為負、退款或折抵為正，轉帳類為正。account 是主帳戶，counterAccount 是轉帳的另一端。
        sourceId 是記帳時帶入的來源識別碼，createdVia 是建立來源（例如 mcp:claude.ai；網頁或 CLI 建立的為空）。
        """)]
    public async Task<TransactionSearchResult> SearchTransactionsAsync(
        [Description("帳本 id")] Guid bookId,
        [Description("起日（含），yyyy-MM-dd")] DateOnly from,
        [Description("迄日（含），yyyy-MM-dd")] DateOnly to,
        [Description("選填：金額（填正數；比對交易金額的絕對值）")] decimal? amount = null,
        [Description("選填：備註關鍵字（不分大小寫）")] string? keyword = null,
        [Description("選填：來源識別碼（完全相同）")] string? sourceId = null,
        [Description("選填：帳戶 id（list_accounts 取得）")] Guid? accountId = null,
        CancellationToken cancellationToken = default)
    {
        McpBooks.EnsureValidPeriod(from, to);

        var book = await McpBooks.GetBookAsync(sender, bookId, cancellationToken);
        var transactions = await sender.Send(new ListTransactions(bookId, from, to, null, accountId), cancellationToken);

        var rows = new TransactionRows(book);
        var items = transactions
            .Where(t => amount is not { } a || Math.Abs(t.Amount) == Math.Abs(a))
            .Where(t => string.IsNullOrEmpty(keyword) || (t.Note?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
            .Where(t => string.IsNullOrEmpty(sourceId) || t.ExternalRef == sourceId)
            .Select(rows.ToItem).ToList();
        return new TransactionSearchResult(items.Count, items);
    }

    [McpServerTool(Name = "validate_transaction", ReadOnly = true, Destructive = false)]
    [Description("""
        記帳前的試算，不會存檔：回報這筆交易會建出什麼分錄，以及是否落在鎖帳日內、來源識別碼是否已記過、有沒有疑似重複。
        參數與 add_transaction 完全相同。add_transaction 之前一律先呼叫這個，把結果（尤其 locked、existing、suspectedDuplicates）整理給使用者確認。
        amount 一律填正數（收據上的數字），不要自己加負號；只有支出的退款或點數折抵才設 refund 為 true。
        回傳的 amount 是交易金額：支出為負、退款為正、轉帳類為正；postings 是各帳戶的帶號變動。
        existing 非空表示這個 sourceId 已經記過（add_transaction 會直接回傳既有的那筆）；suspectedDuplicates 是同日、同金額、動到同一帳戶的既有交易。
        """)]
    public async Task<ValidatedTransaction> ValidateTransactionAsync(
        [Description("帳本 id（list_books 取得）")] Guid bookId,
        [Description("交易種類：Expense（支出）、Income（收入）、Transfer（轉帳）、CardPayment（繳信用卡費）、Withdrawal（提款）")] string kind,
        [Description("交易日期，yyyy-MM-dd")] DateOnly date,
        [Description("金額，一律填正數（收據上的數字）")] decimal amount,
        [Description("帳戶 id（list_accounts 取得）。支出／收入填付款或入帳的帳戶；轉帳填轉出的帳戶；繳卡費填扣款的銀行帳戶；提款填被提款的帳戶")] Guid accountId,
        [Description("選填：refund 只對 Expense 有效，true 表示退款或點數折抵（金額仍填正數）")] bool refund = false,
        [Description("選填：對方帳戶 id。Transfer（轉入）、CardPayment（信用卡帳戶）、Withdrawal（收到現金的帳戶）必填")] Guid? counterAccountId = null,
        [Description("選填：分類 id（list_categories 取得，子分類或大類皆可）。Expense、Income 必填")] Guid? categoryId = null,
        [Description("選填：歸屬月份 yyyymm（例如 202603）；沒填就照現行規則推算")] int? budgetMonth = null,
        [Description("選填：備註。請寫上來源摘要，例如「Uber Eats 訂單 #123」")] string? note = null,
        [Description("選填：標籤 id 陣列（list_tags 取得，最多 4 個）")] Guid[]? tagIds = null,
        [Description("選填：來源識別碼（信件請用 Message-ID 這類穩定值；對話來源留空），用來偵測同一來源是否已記過")] string? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        var input = McpTransactionArgs.ToInput(kind, date, amount, refund, accountId, counterAccountId, categoryId, budgetMonth, note, tagIds);
        var book = await McpBooks.GetBookAsync(sender, bookId, cancellationToken);
        var preview = await McpBooks.SendAsync(sender, new PreviewTransaction(bookId, input, NormalizeSourceId(sourceId)), cancellationToken);

        var rows = new TransactionRows(book);
        return new ValidatedTransaction(
            rows.ToPreviewItem(preview.Transaction),
            preview.Locked,
            preview.ExistingByExternalRef is { } existing ? rows.ToItem(existing) : null,
            [.. preview.SuspectedDuplicates.Select(rows.ToItem)]);
    }

    [McpServerTool(Name = "add_transaction", ReadOnly = false, Destructive = false, Idempotent = false)]
    [Authorize(Policy = McpSetup.WritePolicyName)]
    [Description("""
        新增一筆交易到帳本（會真的寫入）。呼叫前必須先用 validate_transaction 試算，並把內容讓使用者確認；沒有使用者明確同意，不要呼叫。
        參數與 validate_transaction 相同。amount 一律填正數（收據上的數字），不要自己加負號：支出會由系統轉成負數；只有支出的退款或點數折抵才設 refund 為 true。
        帶 sourceId 時是冪等的：同一個 sourceId 已記過就不會重複寫入，回傳 duplicate 為 true 與既有的那筆。
        每天有寫入筆數上限，達到上限會回錯誤，請告知使用者改在網頁上記帳。
        """)]
    public async Task<AddedTransaction> AddTransactionAsync(
        [Description("帳本 id（list_books 取得）")] Guid bookId,
        [Description("交易種類：Expense（支出）、Income（收入）、Transfer（轉帳）、CardPayment（繳信用卡費）、Withdrawal（提款）")] string kind,
        [Description("交易日期，yyyy-MM-dd")] DateOnly date,
        [Description("金額，一律填正數（收據上的數字）")] decimal amount,
        [Description("帳戶 id（list_accounts 取得）。支出／收入填付款或入帳的帳戶；轉帳填轉出的帳戶；繳卡費填扣款的銀行帳戶；提款填被提款的帳戶")] Guid accountId,
        [Description("選填：refund 只對 Expense 有效，true 表示退款或點數折抵（金額仍填正數）")] bool refund = false,
        [Description("選填：對方帳戶 id。Transfer（轉入）、CardPayment（信用卡帳戶）、Withdrawal（收到現金的帳戶）必填")] Guid? counterAccountId = null,
        [Description("選填：分類 id（list_categories 取得，子分類或大類皆可）。Expense、Income 必填")] Guid? categoryId = null,
        [Description("選填：歸屬月份 yyyymm（例如 202603）；沒填就照現行規則推算")] int? budgetMonth = null,
        [Description("選填：備註。請寫上來源摘要，例如「Uber Eats 訂單 #123」")] string? note = null,
        [Description("選填：標籤 id 陣列（list_tags 取得，最多 4 個）")] Guid[]? tagIds = null,
        [Description("選填：來源識別碼（信件請用 Message-ID 這類穩定值；對話來源留空），同一個 sourceId 不會重複記帳")] string? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        var input = McpTransactionArgs.ToInput(kind, date, amount, refund, accountId, counterAccountId, categoryId, budgetMonth, note, tagIds);
        // 先確認是帳本成員（非成員一律「找不到帳本」），之後才用 BookId 查詢重複，免得洩漏別人帳本的資料。
        var book = await McpBooks.GetBookAsync(sender, bookId, cancellationToken);

        var externalRef = NormalizeSourceId(sourceId);
        var bookKey = new BookId(bookId);
        var alreadyRecorded = externalRef is not null
            && await db.Transactions.AnyAsync(t => t.BookId == bookKey && t.ExternalRef == externalRef, cancellationToken);

        // 重複的呼叫不寫入、不佔額度，所以不檢查上限；其他情況先檢查上限再送出。
        if (!alreadyRecorded)
        {
            await quota.EnsureAvailableAsync(cancellationToken);
        }

        var recorded = await McpBooks.SendAsync(sender, new RecordTransaction(bookId, input, externalRef), cancellationToken);
        return new AddedTransaction(recorded.Duplicate, new TransactionRows(book).ToItem(recorded.Transaction));
    }

    private static string? NormalizeSourceId(string? sourceId) => string.IsNullOrWhiteSpace(sourceId) ? null : sourceId.Trim();
}

internal sealed record CategoryItem(Guid Id, string Name, string Kind, string? Nature, bool Archived, IReadOnlyList<SubCategoryItem> Children);

internal sealed record SubCategoryItem(Guid Id, string Name, bool Archived);

internal sealed record TagItem(Guid Id, string Name, bool Archived);

internal sealed record TransactionSearchResult(int Count, IReadOnlyList<TransactionItem> Transactions);

internal sealed record TransactionItem(Guid Id, DateOnly Date, string Kind, decimal Amount, string? Account, string? CounterAccount,
    string? MainCategory, string? SubCategory, string? Note, IReadOnlyList<string> Tags, string? SourceId, string? CreatedVia);

internal sealed record PostingItem(string? Account, decimal Amount);

/// <summary>validate_transaction 的交易：欄位同 <see cref="TransactionItem"/>（尚未存檔，沒有 id），加上分錄。</summary>
internal sealed record PreviewItem(DateOnly Date, string Kind, decimal Amount, string? Account, string? CounterAccount,
    string? MainCategory, string? SubCategory, string? Note, IReadOnlyList<string> Tags, string? SourceId, IReadOnlyList<PostingItem> Postings);

internal sealed record ValidatedTransaction(PreviewItem Transaction, bool Locked, TransactionItem? Existing, IReadOnlyList<TransactionItem> SuspectedDuplicates);

internal sealed record AddedTransaction(bool Duplicate, TransactionItem Transaction);

/// <summary>把 <see cref="TransactionDto"/> 的 Id 轉成帳戶、分類、標籤的名稱；讀取與寫入 tool 回傳同一種形狀。</summary>
internal sealed class TransactionRows(BookDto book)
{
    private readonly Dictionary<Guid, string> accountNames = book.Accounts.ToDictionary(a => a.Id, a => a.Name);
    private readonly Dictionary<Guid, CategoryDto> categories = book.Categories.ToDictionary(c => c.Id);
    private readonly Dictionary<Guid, string> tagNames = (book.Tags ?? []).ToDictionary(t => t.Id, t => t.Name);

    public TransactionItem ToItem(TransactionDto t)
    {
        var (main, sub) = McpBooks.CategoryNames(t.CategoryId, categories);
        return new TransactionItem(
            t.Id, t.Date, t.Kind.ToString(), t.Amount,
            AccountName(t.AccountId),
            t.CounterAccountId is { } counterId ? AccountName(counterId) : null,
            main, sub, t.Note, TagNamesOf(t), t.ExternalRef, t.CreatedVia);
    }

    public PreviewItem ToPreviewItem(TransactionDto t)
    {
        var (main, sub) = McpBooks.CategoryNames(t.CategoryId, categories);
        return new PreviewItem(
            t.Date, t.Kind.ToString(), t.Amount,
            AccountName(t.AccountId),
            t.CounterAccountId is { } counterId ? AccountName(counterId) : null,
            main, sub, t.Note, TagNamesOf(t), t.ExternalRef,
            [.. t.Postings.Select(p => new PostingItem(AccountName(p.AccountId), p.Amount))]);
    }

    private string? AccountName(Guid id) => accountNames.GetValueOrDefault(id);

    private IReadOnlyList<string> TagNamesOf(TransactionDto t) => [.. (t.TagIds ?? []).Select(id => tagNames.GetValueOrDefault(id, "?"))];
}
