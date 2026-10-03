using FluentAssertions;
using SixJars.Application.LegacyImport;
using SixJars.Domain.Books;
using SixJars.Domain.Transactions;
using Xunit;
using static SixJars.Application.Tests.LegacyImport.LegacyWorkbookFactory;

namespace SixJars.Application.Tests.LegacyImport;

public class LegacyWorkbookMapperTemplateTests
{
    private static LegacyTemplateRow Template(int row, LegacyTemplateSection section, string item, string? method, decimal amount, DateOnly? paidDate) =>
        new(row, section, item, method, amount, paidDate, null);

    [Fact]
    public void Paid_fixed_template_creates_expense_and_paid_plan()
    {
        var result = LegacyWorkbookMapper.Map(Workbook(Month(1, templates:
            [Template(52, LegacyTemplateSection.Fixed, "行動電話費", "國泰世華銀行", -599m, new DateOnly(2026, 1, 16))])));

        var tx = result.Transactions.Single();
        tx.Kind.Should().Be(TransactionKind.Expense);
        tx.Amount.Should().Be(-599m);
        tx.Date.Should().Be(new DateOnly(2026, 1, 16));
        var planned = result.PlannedExpenses.Single();
        planned.PaidTransactionId.Should().Be(tx.Id);
        planned.EstimatedAmount.Should().Be(-599m);
    }

    [Fact]
    public void Template_without_paid_date_is_unpaid_plan_only()
    {
        var result = LegacyWorkbookMapper.Map(Workbook(Month(3, templates:
            [Template(52, LegacyTemplateSection.Fixed, "行動電話費", "國泰世華銀行", -599m, null)])));

        result.Transactions.Should().BeEmpty();
        var planned = result.PlannedExpenses.Single();
        planned.IsPaid.Should().BeFalse();
        planned.AccountId.Should().Be(result.Book.FindAccount("國泰世華銀行")!.Id);
    }

    [Fact]
    public void Paid_date_without_method_stays_unpaid_with_warning()
    {
        var result = LegacyWorkbookMapper.Map(Workbook(Month(2, templates:
            [Template(66, LegacyTemplateSection.Fixed, "保險費", null, -130m, new DateOnly(2026, 2, 27))])));

        result.Transactions.Should().BeEmpty();
        result.PlannedExpenses.Single().AccountId.Should().BeNull();
        result.Report.Warnings.Should().ContainSingle(i => i.Row == 66 && i.Sheet == "2月");
    }

    [Fact]
    public void Loan_template_takes_principal_from_loan_area()
    {
        var result = LegacyWorkbookMapper.Map(Workbook(Month(1,
            templates: [Template(76, LegacyTemplateSection.Loan, "房屋貸款", "華南銀行", -32503m, new DateOnly(2026, 1, 9))],
            loanPrincipals: [new("房屋貸款", -28085m)])));

        var tx = result.Transactions.Single();
        tx.Kind.Should().Be(TransactionKind.LoanPayment);
        tx.Amount.Should().Be(32503m);
        tx.LoanPrincipal.Should().Be(28085m);
    }

    [Fact]
    public void Journal_prepayment_takes_full_principal_and_rest_goes_to_template()
    {
        var result = LegacyWorkbookMapper.Map(Workbook(Month(2,
            journal: [Row(129, new DateOnly(2026, 2, 24), "華南銀行", "貸款支出", "房屋貸款", -60000m)],
            templates: [Template(76, LegacyTemplateSection.Loan, "房屋貸款", "華南銀行", -32503m, new DateOnly(2026, 2, 9))],
            loanPrincipals: [new("房屋貸款", -88131m)])));

        result.Report.Errors.Should().BeEmpty();
        result.Transactions.Select(t => t.LoanPrincipal).Should().Equal(60000m, 28131m);
    }

    [Fact]
    public void Principal_that_cannot_be_allocated_is_an_error()
    {
        var result = LegacyWorkbookMapper.Map(Workbook(Month(1,
            templates: [Template(76, LegacyTemplateSection.Loan, "房屋貸款", "華南銀行", -32503m, new DateOnly(2026, 1, 9))],
            loanPrincipals: [new("房屋貸款", -40000m)])));

        result.Transactions.Should().BeEmpty();
        result.PlannedExpenses.Should().BeEmpty();
        result.Report.Errors.Should().ContainSingle(i => i.Row == 76);
    }

    [Fact]
    public void Special_template_creates_sub_category_on_demand()
    {
        var result = LegacyWorkbookMapper.Map(Workbook(Month(1, templates:
            [Template(86, LegacyTemplateSection.Special, "牙齒矯正", "現金", -5000m, new DateOnly(2026, 1, 20))])));

        result.Book.FindCategory("特別支出", "牙齒矯正")!.Nature.Should().Be(ExpenseNature.Special);
        result.Transactions.Single().Kind.Should().Be(TransactionKind.Expense);
    }
}
