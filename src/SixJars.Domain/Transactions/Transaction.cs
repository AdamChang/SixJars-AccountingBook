using SixJars.Domain.Common;

namespace SixJars.Domain.Transactions;

/// <summary>
/// 使用者記下的一筆金錢事件，展開成一或多筆分錄（ADR 0001）。只能經由 <see cref="TransactionFactory"/> 建立。
/// </summary>
/// <remarks>
/// <see cref="AccountId"/> 是 Excel 的「方式」帳戶，<see cref="CounterAccountId"/> 是對方帳戶：
/// <list type="bullet">
/// <item>收入、支出：方式帳戶；無對方帳戶。</item>
/// <item>轉帳：唯一的例外，AccountId 為「從」、CounterAccountId 為「到」。</item>
/// <item>提款、現金存入：銀行／現金。</item>
/// <item>加值：電子錢包／加值來源。</item>
/// <item>繳卡費：付款帳戶／信用卡。</item>
/// <item>新增貸款：撥款入帳帳戶／貸款；貸款繳款：付款帳戶／貸款。</item>
/// <item>入新資金：轉入帳戶／轉出帳戶（同帳戶圈存時為 null）；出資金、資金回流：方式帳戶，無對方帳戶。</item>
/// </list>
/// </remarks>
public sealed class Transaction
{
    private readonly List<Posting> _postings;

    // EF Core 專用：internal 建構子含分錄集合，EF 無法綁定。
    private Transaction() => _postings = [];

    internal Transaction(
        BookId bookId,
        TransactionKind kind,
        DateOnly date,
        BudgetMonth budgetMonth,
        decimal amount,
        AccountId accountId,
        AccountId? counterAccountId,
        CategoryId? categoryId,
        PlanningFundId? planningFundId,
        decimal? loanPrincipal,
        string? note,
        IEnumerable<Posting> postings)
    {
        Id = TransactionId.New();
        BookId = bookId;
        Kind = kind;
        Date = date;
        BudgetMonth = budgetMonth;
        Amount = amount;
        AccountId = accountId;
        CounterAccountId = counterAccountId;
        CategoryId = categoryId;
        PlanningFundId = planningFundId;
        LoanPrincipal = loanPrincipal;
        Note = note;
        _postings = [.. postings];
    }

    public TransactionId Id { get; private set; }
    public BookId BookId { get; private set; }
    public TransactionKind Kind { get; private set; }
    public DateOnly Date { get; private set; }
    public BudgetMonth BudgetMonth { get; private set; }
    /// <summary>收入、支出為帶號金額（沿用 Excel，支出為負）；其餘類型恆為正。</summary>
    public decimal Amount { get; private set; }
    public AccountId AccountId { get; private set; }
    public AccountId? CounterAccountId { get; private set; }
    public CategoryId? CategoryId { get; private set; }
    public PlanningFundId? PlanningFundId { get; private set; }
    public decimal? LoanPrincipal { get; private set; }
    public string? Note { get; private set; }
    public IReadOnlyList<Posting> Postings => _postings;

    /// <summary>對財務規劃帳戶的影響：入新資金與資金回流為正、出資金為負。</summary>
    public decimal FundDelta => Kind switch
    {
        TransactionKind.FundAllocation or TransactionKind.FundReturn => Amount,
        TransactionKind.FundWithdrawal => -Amount,
        _ => 0m,
    };

    public decimal? LoanInterest => Kind == TransactionKind.LoanPayment ? Amount - LoanPrincipal : null;
}
