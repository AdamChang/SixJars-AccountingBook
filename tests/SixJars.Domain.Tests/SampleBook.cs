using SixJars.Domain.Books;
using SixJars.Domain.Transactions;

namespace SixJars.Domain.Tests;

/// <summary>以真實帳本結構縮小而成的測試帳本，數字取自 2026 年 1 月 Excel。</summary>
internal sealed class SampleBook
{
    public SampleBook()
    {
        Book = new Book("測試帳本", new DateOnly(2025, 12, 30));
        Cash = Book.AddAccount("現金", AccountType.Cash, 2071m);
        ForeignCash = Book.AddAccount("外幣現鈔", AccountType.Cash, 10000m, countsAsAvailableCash: false);
        Bank = Book.AddAccount("國泰世華銀行", AccountType.Bank, 21610m);
        OtherBank = Book.AddAccount("華南銀行", AccountType.Bank, 11637m);
        Investment = Book.AddAccount("國泰投資帳戶", AccountType.Bank, 20m);
        Card = Book.AddAccount("國泰Combo卡", AccountType.CreditCard, -22668m);
        Wallet = Book.AddAccount("悠遊卡", AccountType.EWallet, 152m);
        Loan = Book.AddAccount("房屋貸款", AccountType.Loan, -2658876m);
        FreedomFund = Book.AddPlanningFund("財務自由帳戶", 9874.3m);
        Salary = Book.AddIncomeCategory("工作薪資1");
        Food = Book.AddExpenseCategory("主食", ExpenseNature.Floating);
        Fixed = Book.AddExpenseCategory("固定支出", ExpenseNature.Fixed);
        Insurance = Book.AddSubCategory(Fixed.Id, "保險費");
        Phone = Book.AddSubCategory(Fixed.Id, "行動電話費");
        LoanExpense = Book.AddExpenseCategory("貸款支出", ExpenseNature.Loan);
        Mortgage = Book.AddSubCategory(LoanExpense.Id, "房屋貸款");
        Factory = new TransactionFactory(Book);
    }

    public Book Book { get; }
    public Account Cash { get; }
    public Account ForeignCash { get; }
    public Account Bank { get; }
    public Account OtherBank { get; }
    public Account Investment { get; }
    public Account Card { get; }
    public Account Wallet { get; }
    public Account Loan { get; }
    public PlanningFund FreedomFund { get; }
    public Category Salary { get; }
    public Category Food { get; }
    public Category Fixed { get; }
    public Category Insurance { get; }
    public Category Phone { get; }
    public Category LoanExpense { get; }
    public Category Mortgage { get; }
    public TransactionFactory Factory { get; }
}
