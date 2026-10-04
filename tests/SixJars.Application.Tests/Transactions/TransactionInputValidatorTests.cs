using FluentAssertions;
using SixJars.Application.Transactions;
using SixJars.Domain.Transactions;
using Xunit;

namespace SixJars.Application.Tests.Transactions;

public class TransactionInputValidatorTests
{
    private static readonly DateOnly Jan5 = new(2026, 1, 5);
    private static readonly Guid Cash = Guid.CreateVersion7();
    private static readonly Guid Bank = Guid.CreateVersion7();
    private static readonly Guid Loan = Guid.CreateVersion7();
    private static readonly Guid Food = Guid.CreateVersion7();
    private static readonly Guid Fund = Guid.CreateVersion7();

    // 以案例名稱當 theory 參數：TransactionInput 不是 xUnit 可序列化的型別，直接放進 TheoryData 會讓 6 個案例併成一個。
    public static TheoryData<string, string> Cases => new()
    {
        { "Transfer 缺 CounterAccountId", nameof(TransactionInput.CounterAccountId) },
        { "Income 缺 CategoryId", nameof(TransactionInput.CategoryId) },
        { "LoanPayment 缺 LoanPrincipal", nameof(TransactionInput.LoanPrincipal) },
        { "FundReturn 缺 PlanningFundId", nameof(TransactionInput.PlanningFundId) },
        { "Expense 帶了 PlanningFundId", nameof(TransactionInput.PlanningFundId) },
        { "Note 長度 501", nameof(TransactionInput.Note) },
        // 以下為 T20 審查補充（變異測試：拿掉「不使用欄位必須為 null」與月份檢查後，原本的案例全部通過）
        { "Expense 缺 CategoryId", nameof(TransactionInput.CategoryId) },
        { "TopUp 缺 CounterAccountId", nameof(TransactionInput.CounterAccountId) },
        { "FundAllocation 缺 PlanningFundId", nameof(TransactionInput.PlanningFundId) },
        { "Income 帶了 CounterAccountId", nameof(TransactionInput.CounterAccountId) },
        { "Transfer 帶了 CategoryId", nameof(TransactionInput.CategoryId) },
        { "Expense 帶了 LoanPrincipal", nameof(TransactionInput.LoanPrincipal) },
        { "BudgetMonth 月份 13", nameof(TransactionInput.BudgetMonth) },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Invalid_input_reports_property(string caseName, string propertyName)
    {
        var result = new TransactionInputValidator().Validate(Input(caseName));

        result.Errors.Should().Contain(e => e.PropertyName == propertyName);
    }

    [Fact]
    public void Optional_fields_may_be_omitted()
    {
        var validator = new TransactionInputValidator();

        // 同帳戶圈存（沒有轉出帳戶）、不指定利息分類的貸款繳款、不指定分類的出資金
        validator.Validate(new TransactionInput(TransactionKind.FundAllocation, Jan5, null, 100m, Bank, null, null, Fund, null, null))
            .IsValid.Should().BeTrue();
        validator.Validate(new TransactionInput(TransactionKind.LoanPayment, Jan5, null, 20000m, Bank, Loan, null, null, 15000m, null))
            .IsValid.Should().BeTrue();
        validator.Validate(new TransactionInput(TransactionKind.FundWithdrawal, Jan5, 202602, 30m, Cash, null, null, Fund, null, null))
            .IsValid.Should().BeTrue();
    }

    private static TransactionInput Input(string caseName) => caseName switch
    {
        "Transfer 缺 CounterAccountId" =>
            new(TransactionKind.Transfer, Jan5, null, 500m, Bank, null, null, null, null, null),
        "Income 缺 CategoryId" =>
            new(TransactionKind.Income, Jan5, null, 5000m, Bank, null, null, null, null, null),
        "LoanPayment 缺 LoanPrincipal" =>
            new(TransactionKind.LoanPayment, Jan5, null, 20000m, Bank, Loan, null, null, null, null),
        "FundReturn 缺 PlanningFundId" =>
            new(TransactionKind.FundReturn, Jan5, null, 50m, Bank, null, null, null, null, null),
        "Expense 帶了 PlanningFundId" =>
            new(TransactionKind.Expense, Jan5, null, -120m, Cash, null, Food, Fund, null, null),
        "Note 長度 501" =>
            new(TransactionKind.Expense, Jan5, null, -120m, Cash, null, Food, null, null, new string('x', 501)),
        "Expense 缺 CategoryId" =>
            new(TransactionKind.Expense, Jan5, null, -120m, Cash, null, null, null, null, null),
        "TopUp 缺 CounterAccountId" =>
            new(TransactionKind.TopUp, Jan5, null, 500m, Cash, null, null, null, null, null),
        "FundAllocation 缺 PlanningFundId" =>
            new(TransactionKind.FundAllocation, Jan5, null, 100m, Bank, null, null, null, null, null),
        "Income 帶了 CounterAccountId" =>
            new(TransactionKind.Income, Jan5, null, 5000m, Bank, Cash, Food, null, null, null),
        "Transfer 帶了 CategoryId" =>
            new(TransactionKind.Transfer, Jan5, null, 500m, Bank, Cash, Food, null, null, null),
        "Expense 帶了 LoanPrincipal" =>
            new(TransactionKind.Expense, Jan5, null, -120m, Cash, null, Food, null, 10m, null),
        "BudgetMonth 月份 13" =>
            new(TransactionKind.Expense, Jan5, 202613, -120m, Cash, null, Food, null, null, null),
        _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null),
    };
}
