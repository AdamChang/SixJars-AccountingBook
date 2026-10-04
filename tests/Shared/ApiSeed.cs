using Microsoft.Extensions.DependencyInjection;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Members;
using SixJars.Infrastructure.Persistence;

namespace SixJars.Tests.Shared;

internal static class ApiSeed
{
    private static readonly DateTimeOffset MemberAddedAt = new(2025, 12, 31, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 在 factory 的資料庫寫入一本含現金、銀行、信用卡、電子錢包、貸款、一個財務規劃帳戶與基本分類的帳本，
    /// 並以 <paramref name="ownerSubject"/> 加入已綁定的擁有者成員（ADR 0005）；傳 null 則不加成員。
    /// </summary>
    public static async Task<Book> SeedBookAsync(
        this ApiFactory factory, CancellationToken cancellationToken, string? ownerSubject = ApiFactory.DefaultSubject)
    {
        var book = new Book("測試帳本", new DateOnly(2025, 12, 31));
        book.AddAccount("現金", AccountType.Cash, 1000m);
        book.AddAccount("國泰世華銀行", AccountType.Bank, 50000m);
        book.AddAccount("國泰Combo卡", AccountType.CreditCard, -2000m);
        book.AddAccount("悠遊卡", AccountType.EWallet, 300m);
        book.AddAccount("房屋貸款", AccountType.Loan, -1000000m);
        book.AddPlanningFund("財務自由帳戶", 10000m);
        book.AddIncomeCategory("工作薪資");
        var food = book.AddExpenseCategory("主食", ExpenseNature.Floating);
        book.AddSubCategory(food.Id, "午餐");
        var fixedExpense = book.AddExpenseCategory("固定支出", ExpenseNature.Fixed);
        book.AddSubCategory(fixedExpense.Id, "保險費");
        var loan = book.AddExpenseCategory("貸款支出", ExpenseNature.Loan);
        book.AddSubCategory(loan.Id, "房屋貸款");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        db.Books.Add(book);
        if (ownerSubject is not null)
        {
            db.BookMembers.Add(Owner(book.Id, ownerSubject));
        }

        await db.SaveChangesAsync(cancellationToken);
        return book;
    }

    /// <summary>把 <paramref name="subject"/> 加為帳本的擁有者（已綁定 sub，email 與 <see cref="TestAuthHandler"/> 發出的一致）。</summary>
    public static async Task AddOwnerAsync(this ApiFactory factory, BookId bookId, string subject, CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        db.BookMembers.Add(Owner(bookId, subject));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static BookMember Owner(BookId bookId, string subject)
    {
        var member = BookMember.Create(bookId, TestAuthHandler.EmailOf(subject), BookRole.Owner, MemberAddedAt);
        member.BindSubject(subject);
        return member;
    }
}
