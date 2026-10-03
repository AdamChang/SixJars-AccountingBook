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
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Invalid_input_reports_property(string caseName, string propertyName)
    {
        var result = new TransactionInputValidator().Validate(Input(caseName));

        result.Errors.Should().Contain(e => e.PropertyName == propertyName);
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
        _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null),
    };
}
