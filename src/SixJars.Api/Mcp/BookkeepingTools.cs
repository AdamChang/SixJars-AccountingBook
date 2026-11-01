using System.ComponentModel;
using MediatR;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SixJars.Application.Transactions;

namespace SixJars.Api.Mcp;

/// <summary>
/// 記帳用的 tools（spec §6）：目前只有三個讀取 tool，讓 Claude 記帳前先查到分類與標籤的 Id，並能找出已記過的交易。
/// </summary>
/// <remarks>
/// 與 <see cref="LedgerTools"/> 分開：該類別的契約是「唯讀 ledger tools」，之後的寫入 tool 要另外掛權限。
/// 帳戶、分類、標籤一律用 Id（D8），所以回傳都帶 Id；search_transactions 則反過來回傳解析好的名稱，讓 Claude 能直接拿來跟使用者對話。
/// </remarks>
[McpServerToolType]
internal sealed class BookkeepingTools(ISender sender)
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

        var accountNames = book.Accounts.ToDictionary(a => a.Id, a => a.Name);
        var categories = book.Categories.ToDictionary(c => c.Id);
        var tagNames = (book.Tags ?? []).ToDictionary(t => t.Id, t => t.Name);
        var items = transactions
            .Where(t => amount is not { } a || Math.Abs(t.Amount) == Math.Abs(a))
            .Where(t => string.IsNullOrEmpty(keyword) || (t.Note?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
            .Where(t => string.IsNullOrEmpty(sourceId) || t.ExternalRef == sourceId)
            .Select(t =>
            {
                var (main, sub) = McpBooks.CategoryNames(t.CategoryId, categories);
                return new TransactionItem(
                    t.Id,
                    t.Date,
                    t.Kind.ToString(),
                    t.Amount,
                    accountNames.GetValueOrDefault(t.AccountId),
                    t.CounterAccountId is { } counterId ? accountNames.GetValueOrDefault(counterId) : null,
                    main,
                    sub,
                    t.Note,
                    [.. (t.TagIds ?? []).Select(id => tagNames.GetValueOrDefault(id, "?"))],
                    t.ExternalRef,
                    t.CreatedVia);
            }).ToList();
        return new TransactionSearchResult(items.Count, items);
    }
}

internal sealed record CategoryItem(Guid Id, string Name, string Kind, string? Nature, bool Archived, IReadOnlyList<SubCategoryItem> Children);

internal sealed record SubCategoryItem(Guid Id, string Name, bool Archived);

internal sealed record TagItem(Guid Id, string Name, bool Archived);

internal sealed record TransactionSearchResult(int Count, IReadOnlyList<TransactionItem> Transactions);

internal sealed record TransactionItem(Guid Id, DateOnly Date, string Kind, decimal Amount, string? Account, string? CounterAccount,
    string? MainCategory, string? SubCategory, string? Note, IReadOnlyList<string> Tags, string? SourceId, string? CreatedVia);
