using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Application.Transactions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests.Mcp;

/// <summary>
/// MCP 記帳 command 與唯讀試算（spec §5、D5、D7）：以 ISender 走完整 pipeline（需要真的 PostgreSQL 與 BookAccessBehavior）。
/// 重點是冪等：同一個來源識別碼重複送出，不能多一筆交易，也不能多一筆稽核記錄（會佔掉每日額度）。
/// </summary>
public class RecordTransactionTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Via = "mcp:claude.ai";
    private const string Ref = "<a@m>";

    private sealed class McpUser : ICurrentUser
    {
        public string Subject => ApiFactory.DefaultSubject;
        public string? Email => TestAuthHandler.EmailOf(ApiFactory.DefaultSubject);
        public string? Via => RecordTransactionTests.Via;
    }

    private Task<ApiFactory> CreateFactoryAsync() =>
        ApiFactory.CreateAsync(postgres, Ct, s => s.AddScoped<ICurrentUser, McpUser>());

    private static async Task<T> SendAsync<T>(ApiFactory factory, IRequest<T> request)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, Ct);
    }

    private static async Task SendAsync(ApiFactory factory, IRequest request)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, Ct);
    }

    private static TransactionInput Lunch(Book book, decimal amount = -120m) => new(
        TransactionKind.Expense, new DateOnly(2026, 1, 5), null, amount, book.FindAccount("現金")!.Id.Value,
        null, book.FindCategory("主食", "午餐")!.Id.Value, null, null, "Subway");

    private static async Task<T> WithDbAsync<T>(ApiFactory factory, Func<SixJarsDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<SixJarsDbContext>());
    }

    private static Task<int> CountTransactionsAsync(ApiFactory factory) =>
        WithDbAsync(factory, db => db.Transactions.CountAsync(Ct));

    [Fact]
    public async Task Record_sets_created_via_and_external_ref_and_audits_via()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);

        var recorded = await SendAsync(factory, new RecordTransaction(book.Id.Value, Lunch(book), Ref));

        recorded.Duplicate.Should().BeFalse();
        recorded.Transaction.CreatedVia.Should().Be(Via);
        recorded.Transaction.ExternalRef.Should().Be(Ref);
        var entries = await WithDbAsync(factory, db => db.AuditEntries.AsNoTracking()
            .Where(e => e.EntityId == recorded.Transaction.Id).ToListAsync(Ct));
        entries.Should().ContainSingle(e => e.Action == AuditAction.Create).Which.Via.Should().Be(Via);
    }

    [Fact]
    public async Task Record_with_same_external_ref_returns_existing_as_duplicate()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);
        var first = await SendAsync(factory, new RecordTransaction(book.Id.Value, Lunch(book), Ref));

        var second = await SendAsync(factory, new RecordTransaction(book.Id.Value, Lunch(book, -999m), Ref));

        second.Duplicate.Should().BeTrue();
        second.Transaction.Id.Should().Be(first.Transaction.Id);
        (await CountTransactionsAsync(factory)).Should().Be(1);
        (await WithDbAsync(factory, db => db.AuditEntries.CountAsync(e => e.Action == AuditAction.Create, Ct))).Should().Be(1);
    }

    [Fact]
    public async Task Record_after_deleting_same_ref_creates_new()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);
        var first = await SendAsync(factory, new RecordTransaction(book.Id.Value, Lunch(book), Ref));
        await SendAsync(factory, new DeleteTransaction(book.Id.Value, first.Transaction.Id, first.Transaction.Version));

        var again = await SendAsync(factory, new RecordTransaction(book.Id.Value, Lunch(book), Ref));

        again.Duplicate.Should().BeFalse();
        again.Transaction.Id.Should().NotBe(first.Transaction.Id);
    }

    [Fact]
    public async Task Record_without_external_ref_always_creates()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);

        var first = await SendAsync(factory, new RecordTransaction(book.Id.Value, Lunch(book), null));
        var second = await SendAsync(factory, new RecordTransaction(book.Id.Value, Lunch(book), null));

        first.Duplicate.Should().BeFalse();
        second.Duplicate.Should().BeFalse();
        (await CountTransactionsAsync(factory)).Should().Be(2);
    }

    [Theory]
    [InlineData(TransactionKind.FundAllocation)]
    [InlineData(TransactionKind.LoanPayment)]
    [InlineData(TransactionKind.CashDeposit)]
    [InlineData(TransactionKind.TopUp)]
    public async Task Record_rejects_unsupported_kind(TransactionKind kind)
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);
        var input = Lunch(book) with { Kind = kind };

        var act = () => SendAsync(factory, new RecordTransaction(book.Id.Value, input, Ref));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("256")]
    public async Task Record_rejects_blank_or_too_long_external_ref(string value)
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);
        var externalRef = value == "256" ? new string('x', 256) : value;

        var act = () => SendAsync(factory, new RecordTransaction(book.Id.Value, Lunch(book), externalRef));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Record_on_locked_date_throws_domain_exception()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);
        await SendAsync(factory, new SetLockDate(book.Id.Value, new DateOnly(2026, 1, 31)));

        var act = () => SendAsync(factory, new RecordTransaction(book.Id.Value, Lunch(book), Ref));

        await act.Should().ThrowAsync<DomainException>();
        (await CountTransactionsAsync(factory)).Should().Be(0);
    }

    [Fact]
    public async Task Preview_does_not_write()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);

        var preview = await SendAsync(factory, new PreviewTransaction(book.Id.Value, Lunch(book), Ref));

        (await CountTransactionsAsync(factory)).Should().Be(0);
        (await WithDbAsync(factory, db => db.AuditEntries.CountAsync(Ct))).Should().Be(0);
        preview.Transaction.Postings.Should().ContainSingle().Which.Amount.Should().Be(-120m);
        preview.Locked.Should().BeFalse();
    }

    [Fact]
    public async Task Preview_reports_locked_instead_of_throwing()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);
        await SendAsync(factory, new SetLockDate(book.Id.Value, new DateOnly(2026, 1, 31)));

        var preview = await SendAsync(factory, new PreviewTransaction(book.Id.Value, Lunch(book), Ref));

        preview.Locked.Should().BeTrue();
    }

    [Fact]
    public async Task Preview_reports_existing_by_external_ref_and_suspected_duplicates()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);
        var recorded = await SendAsync(factory, new RecordTransaction(book.Id.Value, Lunch(book), Ref));

        var differentRef = await SendAsync(factory, new PreviewTransaction(book.Id.Value, Lunch(book), "<b@m>"));
        var sameRef = await SendAsync(factory, new PreviewTransaction(book.Id.Value, Lunch(book), Ref));

        differentRef.ExistingByExternalRef.Should().BeNull();
        differentRef.SuspectedDuplicates.Should().ContainSingle().Which.Id.Should().Be(recorded.Transaction.Id);
        sameRef.ExistingByExternalRef.Should().NotBeNull().And.Match<TransactionDto>(t => t.Id == recorded.Transaction.Id);
    }

    [Fact]
    public async Task Non_member_gets_not_found()
    {
        await using var factory = await CreateFactoryAsync();
        var others = await factory.SeedBookAsync(Ct, ownerSubject: "someone-else");

        var act = () => SendAsync(factory, new RecordTransaction(others.Id.Value, Lunch(others), Ref));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
