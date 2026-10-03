using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Domain.Transactions;

/// <summary>依交易類型驗證帳戶與分類，並把交易展開成分錄。</summary>
public sealed class TransactionFactory(Book book)
{
    public Transaction Income(DateOnly date, AccountId accountId, CategoryId categoryId, decimal amount, string? note = null, BudgetMonth? budgetMonth = null)
    {
        RequireNonZero(amount);
        RequireAccount(accountId, AccountType.Cash, AccountType.Bank, AccountType.EWallet);
        RequireCategory(categoryId, CategoryKind.Income);
        return Create(TransactionKind.Income, date, budgetMonth, amount, accountId, null, categoryId, note, [new(accountId, amount)]);
    }

    public Transaction Expense(DateOnly date, AccountId accountId, CategoryId categoryId, decimal amount, string? note = null, BudgetMonth? budgetMonth = null)
    {
        RequireNonZero(amount);
        RequireAccount(accountId, AccountType.Cash, AccountType.Bank, AccountType.CreditCard, AccountType.EWallet);
        RequireCategory(categoryId, CategoryKind.Expense);
        return Create(TransactionKind.Expense, date, budgetMonth, amount, accountId, null, categoryId, note, [new(accountId, amount)]);
    }

    /// <summary>轉帳以「從 → 到」表達，金額恆為正。</summary>
    public Transaction Transfer(DateOnly date, AccountId fromId, AccountId toId, decimal amount, string? note = null, BudgetMonth? budgetMonth = null)
    {
        RequirePositive(amount);
        if (fromId == toId)
        {
            throw new DomainException("轉帳的轉出與轉入帳戶不可相同。");
        }

        RequireAccount(fromId, AccountType.Cash, AccountType.Bank);
        RequireAccount(toId, AccountType.Cash, AccountType.Bank);
        return Create(TransactionKind.Transfer, date, budgetMonth, amount, fromId, toId, null, note, Move(fromId, toId, amount));
    }

    public Transaction Withdrawal(DateOnly date, AccountId bankId, AccountId cashId, decimal amount, string? note = null, BudgetMonth? budgetMonth = null)
    {
        RequirePositive(amount);
        RequireAccount(bankId, AccountType.Bank);
        RequireAccount(cashId, AccountType.Cash);
        return Create(TransactionKind.Withdrawal, date, budgetMonth, amount, bankId, cashId, null, note, Move(bankId, cashId, amount));
    }

    public Transaction CashDeposit(DateOnly date, AccountId bankId, AccountId cashId, decimal amount, string? note = null, BudgetMonth? budgetMonth = null)
    {
        RequirePositive(amount);
        RequireAccount(bankId, AccountType.Bank);
        RequireAccount(cashId, AccountType.Cash);
        return Create(TransactionKind.CashDeposit, date, budgetMonth, amount, bankId, cashId, null, note, Move(cashId, bankId, amount));
    }

    public Transaction TopUp(DateOnly date, AccountId walletId, AccountId sourceId, decimal amount, string? note = null, BudgetMonth? budgetMonth = null)
    {
        RequirePositive(amount);
        RequireAccount(walletId, AccountType.EWallet);
        RequireAccount(sourceId, AccountType.Cash, AccountType.Bank, AccountType.CreditCard);
        return Create(TransactionKind.TopUp, date, budgetMonth, amount, walletId, sourceId, null, note, Move(sourceId, walletId, amount));
    }

    public Transaction CardPayment(DateOnly date, AccountId payerId, AccountId cardId, decimal amount, string? note = null, BudgetMonth? budgetMonth = null)
    {
        RequirePositive(amount);
        RequireAccount(payerId, AccountType.Cash, AccountType.Bank);
        RequireAccount(cardId, AccountType.CreditCard);
        return Create(TransactionKind.CardPayment, date, budgetMonth, amount, payerId, cardId, null, note, Move(payerId, cardId, amount));
    }

    public Transaction LoanDisbursement(DateOnly date, AccountId receiverId, AccountId loanId, decimal amount, string? note = null, BudgetMonth? budgetMonth = null)
    {
        RequirePositive(amount);
        RequireAccount(receiverId, AccountType.Cash, AccountType.Bank);
        RequireAccount(loanId, AccountType.Loan);
        return Create(TransactionKind.LoanDisbursement, date, budgetMonth, amount, receiverId, loanId, null, note, Move(loanId, receiverId, amount));
    }

    /// <summary>
    /// 貸款繳款：付款帳戶付出總額，貸款餘額只減少本金；利息 = 總額 − 本金，是不進任何帳戶的支出。
    /// 提前還本即本金等於總額。
    /// </summary>
    public Transaction LoanPayment(
        DateOnly date,
        AccountId payerId,
        AccountId loanId,
        decimal total,
        decimal principal,
        CategoryId? interestCategoryId = null,
        string? note = null,
        BudgetMonth? budgetMonth = null)
    {
        RequirePositive(total);
        if (principal < 0m || principal > total)
        {
            throw new DomainException($"本金必須介於 0 與繳款總額之間（本金 {principal}，總額 {total}）。");
        }

        RequireAccount(payerId, AccountType.Cash, AccountType.Bank);
        RequireAccount(loanId, AccountType.Loan);
        if (interestCategoryId is { } categoryId)
        {
            RequireCategory(categoryId, CategoryKind.Expense);
        }

        Posting[] postings = principal > 0m ? [new(payerId, -total), new(loanId, principal)] : [new(payerId, -total)];
        return Create(TransactionKind.LoanPayment, date, budgetMonth, total, payerId, loanId, interestCategoryId, note, postings, loanPrincipal: principal);
    }

    private Transaction Create(
        TransactionKind kind,
        DateOnly date,
        BudgetMonth? budgetMonth,
        decimal amount,
        AccountId accountId,
        AccountId? counterAccountId,
        CategoryId? categoryId,
        string? note,
        IEnumerable<Posting> postings,
        PlanningFundId? planningFundId = null,
        decimal? loanPrincipal = null) =>
        new(book.Id, kind, date, budgetMonth ?? BudgetMonth.Of(date), amount, accountId, counterAccountId, categoryId, planningFundId, loanPrincipal, note, postings);

    private Account RequireAccount(AccountId id, params AccountType[] allowed)
    {
        var account = book.GetAccount(id);
        if (!allowed.Contains(account.Type))
        {
            throw new DomainException($"帳戶「{account.Name}」的類型為 {account.Type}，此交易只接受 {string.Join("/", allowed)}。");
        }

        return account;
    }

    private Category RequireCategory(CategoryId id, CategoryKind kind)
    {
        var category = book.GetCategory(id);
        if (category.Kind != kind)
        {
            throw new DomainException($"分類「{category.Name}」的種類為 {category.Kind}，此交易需要 {kind}。");
        }

        return category;
    }

    private static void RequirePositive(decimal amount)
    {
        if (amount <= 0m)
        {
            throw new DomainException($"金額必須大於 0，實際為 {amount}。");
        }
    }

    private static void RequireNonZero(decimal amount)
    {
        if (amount == 0m)
        {
            throw new DomainException("金額不可為 0。");
        }
    }

    private static Posting[] Move(AccountId from, AccountId to, decimal amount) => [new(from, -amount), new(to, amount)];
}
