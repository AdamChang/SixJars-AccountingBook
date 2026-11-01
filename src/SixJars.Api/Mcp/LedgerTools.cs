using System.ComponentModel;
using MediatR;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Application.Transactions;

namespace SixJars.Api.Mcp;

/// <summary>
/// 唯讀 ledger tools（ADR 0013、spec §7）：全部透過 <see cref="ISender"/> 呼叫既有 query，帳本權限由 BookAccessBehavior 判斷。
/// </summary>
/// <remarks>
/// 回傳已解析好的名稱與「這個帳戶上」的帶號金額，而不是 Id 與交易金額：比對時由 Claude 自己去對照 Id、推算正負號，最容易出錯。
/// 錯誤一律轉成 <see cref="McpException"/>（tool error，HTTP 仍是 200），訊息不透露帳本是否存在（與 API 的 404 慣例一致）。
/// </remarks>
[McpServerToolType]
internal sealed class LedgerTools(ISender sender)
{
    [McpServerTool(Name = "list_books", ReadOnly = true, Destructive = false)]
    [Description("列出目前使用者可以存取的帳本（id 與名稱）。其他 tool 都需要帳本 id。")]
    public async Task<IReadOnlyList<BookItem>> ListBooksAsync(CancellationToken cancellationToken)
    {
        var books = await sender.Send(new ListBooks(), cancellationToken);
        return [.. books.Select(b => new BookItem(b.Id, b.Name))];
    }

    [McpServerTool(Name = "list_accounts", ReadOnly = true, Destructive = false)]
    [Description("列出帳本的帳戶（含已封存）。type 為 Cash、Bank、CreditCard、EWallet、Loan；信用卡對帳時用 type 為 CreditCard 的帳戶 id。")]
    public async Task<IReadOnlyList<AccountItem>> ListAccountsAsync(
        [Description("帳本 id（list_books 取得）")] Guid bookId, CancellationToken cancellationToken)
    {
        var book = await McpBooks.GetBookAsync(sender, bookId, cancellationToken);
        return [.. book.Accounts.Select(a => new AccountItem(a.Id, a.Name, a.Type.ToString(), a.ArchivedAt is not null))];
    }

    [McpServerTool(Name = "list_account_transactions", ReadOnly = true, Destructive = false)]
    [Description("""
        列出某個帳戶在一段期間（依交易日期，含頭尾，最多 120 天）的交易，依日期排序。
        amount 是這筆交易在「這個帳戶」上的帶號變動：信用卡刷卡為負數；退款、點數折抵、繳卡費為正數。
        mainCategory／subCategory 是分類（轉帳類交易沒有分類）；counterAccount 是轉帳的另一端帳戶（例如繳卡費的扣款銀行）。
        total 是期間內 amount 的合計。
        """)]
    public async Task<AccountTransactions> ListAccountTransactionsAsync(
        [Description("帳本 id")] Guid bookId,
        [Description("帳戶 id（list_accounts 取得）")] Guid accountId,
        [Description("起日（含），yyyy-MM-dd")] DateOnly from,
        [Description("迄日（含），yyyy-MM-dd")] DateOnly to,
        CancellationToken cancellationToken)
    {
        McpBooks.EnsureValidPeriod(from, to);

        var book = await McpBooks.GetBookAsync(sender, bookId, cancellationToken);
        var account = book.Accounts.FirstOrDefault(a => a.Id == accountId) ?? throw new McpException("找不到帳戶。");
        var transactions = await sender.Send(new ListTransactions(bookId, from, to, null, accountId), cancellationToken);

        var accountNames = book.Accounts.ToDictionary(a => a.Id, a => a.Name);
        var categories = book.Categories.ToDictionary(c => c.Id);
        var tagNames = (book.Tags ?? []).ToDictionary(t => t.Id, t => t.Name);
        var items = transactions.Select(t =>
        {
            var (main, sub) = McpBooks.CategoryNames(t.CategoryId, categories);
            // 帳戶是交易的主帳戶時，另一端是對方帳戶；反之（例如看卡片上的繳卡費）另一端是主帳戶。
            var counterId = t.AccountId == accountId ? t.CounterAccountId : t.AccountId;
            return new AccountTransactionItem(
                t.Date,
                t.Kind.ToString(),
                t.Postings.Where(p => p.AccountId == accountId).Sum(p => p.Amount),
                main,
                sub,
                counterId is { } id ? accountNames.GetValueOrDefault(id) : null,
                t.Note,
                [.. (t.TagIds ?? []).Select(id => tagNames.GetValueOrDefault(id, "?"))]);
        }).ToList();
        return new AccountTransactions(account.Name, from, to, items.Count, items.Sum(i => i.Amount), items);
    }
}

internal sealed record BookItem(Guid Id, string Name);

internal sealed record AccountItem(Guid Id, string Name, string Type, bool Archived);

internal sealed record AccountTransactions(string Account, DateOnly From, DateOnly To, int Count, decimal Total, IReadOnlyList<AccountTransactionItem> Transactions);

internal sealed record AccountTransactionItem(DateOnly Date, string Kind, decimal Amount, string? MainCategory, string? SubCategory,
    string? CounterAccount, string? Note, IReadOnlyList<string> Tags);
