using FluentAssertions;
using SixJars.Application.Transactions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;
using Xunit;

namespace SixJars.Application.Tests.Transactions;

public class TransactionBuilderTests
{
    private static readonly DateOnly Jan30 = new(2026, 1, 30);
    private const int Feb = 202602;
    private const string Note = "備註";

    private readonly Book _book = new("測試帳本", new DateOnly(2025, 12, 31));
    private readonly Account _cash;
    private readonly Account _bank;
    private readonly Account _card;
    private readonly Account _wallet;
    private readonly Account _loan;
    private readonly PlanningFund _fund;
    private readonly Category _salary;
    private readonly Category _food;
    private readonly Category _mortgage;
    private readonly TransactionFactory _factory;

    public TransactionBuilderTests()
    {
        _cash = _book.AddAccount("現金", AccountType.Cash, 1000m);
        _bank = _book.AddAccount("國泰世華銀行", AccountType.Bank, 50000m);
        _card = _book.AddAccount("國泰Combo卡", AccountType.CreditCard, -2000m);
        _wallet = _book.AddAccount("悠遊卡", AccountType.EWallet, 300m);
        _loan = _book.AddAccount("房屋貸款", AccountType.Loan, -1000000m);
        _fund = _book.AddPlanningFund("財務自由帳戶", 10000m);
        _salary = _book.AddIncomeCategory("工作薪資");
        _food = _book.AddExpenseCategory("主食", ExpenseNature.Floating);
        var loanExpense = _book.AddExpenseCategory("貸款支出", ExpenseNature.Loan);
        _mortgage = _book.AddSubCategory(loanExpense.Id, "房屋貸款");
        _factory = new TransactionFactory(_book);
    }

    public static TheoryData<TransactionKind> AllKinds => new(Enum.GetValues<TransactionKind>());

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Build_matches_factory(TransactionKind kind)
    {
        var (input, expected) = Case(kind);

        var actual = TransactionBuilder.Build(_book, input);

        actual.Should().BeEquivalentTo(expected, o => o.Excluding(t => t.Id));

        // DTO 轉回輸入模型（供 T42 還原）再建一次，結果仍要相同。
        var rebuilt = TransactionBuilder.Build(_book, TransactionDto.From(actual, version: 0).ToInput());
        rebuilt.Should().BeEquivalentTo(expected, o => o.Excluding(t => t.Id));
    }

    private (TransactionInput Input, Transaction Expected) Case(TransactionKind kind)
    {
        var month = BudgetMonth.FromKey(Feb);
        return kind switch
        {
            TransactionKind.Income => (
                Input(kind, 5000m, _bank.Id, categoryId: _salary.Id),
                _factory.Income(Jan30, _bank.Id, _salary.Id, 5000m, Note, month)),
            TransactionKind.Expense => (
                Input(kind, -120m, _cash.Id, categoryId: _food.Id),
                _factory.Expense(Jan30, _cash.Id, _food.Id, -120m, Note, month)),
            TransactionKind.Transfer => (
                Input(kind, 500m, _bank.Id, counterAccountId: _cash.Id),
                _factory.Transfer(Jan30, _bank.Id, _cash.Id, 500m, Note, month)),
            TransactionKind.Withdrawal => (
                Input(kind, 1000m, _bank.Id, counterAccountId: _cash.Id),
                _factory.Withdrawal(Jan30, _bank.Id, _cash.Id, 1000m, Note, month)),
            TransactionKind.CashDeposit => (
                Input(kind, 300m, _bank.Id, counterAccountId: _cash.Id),
                _factory.CashDeposit(Jan30, _bank.Id, _cash.Id, 300m, Note, month)),
            TransactionKind.TopUp => (
                Input(kind, 200m, _wallet.Id, counterAccountId: _card.Id),
                _factory.TopUp(Jan30, _wallet.Id, _card.Id, 200m, Note, month)),
            TransactionKind.CardPayment => (
                Input(kind, 3000m, _bank.Id, counterAccountId: _card.Id),
                _factory.CardPayment(Jan30, _bank.Id, _card.Id, 3000m, Note, month)),
            TransactionKind.LoanDisbursement => (
                Input(kind, 100000m, _bank.Id, counterAccountId: _loan.Id),
                _factory.LoanDisbursement(Jan30, _bank.Id, _loan.Id, 100000m, Note, month)),
            TransactionKind.LoanPayment => (
                Input(kind, 20000m, _bank.Id, counterAccountId: _loan.Id, categoryId: _mortgage.Id, loanPrincipal: 15000m),
                _factory.LoanPayment(Jan30, _bank.Id, _loan.Id, 20000m, 15000m, _mortgage.Id, Note, month)),
            TransactionKind.FundAllocation => (
                Input(kind, 800m, _bank.Id, counterAccountId: _cash.Id, planningFundId: _fund.Id),
                _factory.FundAllocation(Jan30, _fund.Id, _bank.Id, _cash.Id, 800m, Note, month)),
            TransactionKind.FundWithdrawal => (
                Input(kind, 600m, _bank.Id, categoryId: _food.Id, planningFundId: _fund.Id),
                _factory.FundWithdrawal(Jan30, _fund.Id, _bank.Id, 600m, _food.Id, Note, month)),
            TransactionKind.FundReturn => (
                Input(kind, 50m, _bank.Id, planningFundId: _fund.Id),
                _factory.FundReturn(Jan30, _fund.Id, _bank.Id, 50m, Note, month)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static TransactionInput Input(
        TransactionKind kind,
        decimal amount,
        AccountId accountId,
        AccountId? counterAccountId = null,
        CategoryId? categoryId = null,
        PlanningFundId? planningFundId = null,
        decimal? loanPrincipal = null) =>
        new(kind, Jan30, Feb, amount, accountId.Value, counterAccountId?.Value, categoryId?.Value, planningFundId?.Value, loanPrincipal, Note);
}
