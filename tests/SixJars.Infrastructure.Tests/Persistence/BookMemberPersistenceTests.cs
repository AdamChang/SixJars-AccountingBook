using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SixJars.Domain.Common;
using SixJars.Domain.Members;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Persistence;

public class BookMemberPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset AddedAt = new(2026, 10, 4, 1, 2, 3, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>白名單以 (BookId, Email) 唯一；email 已正規化，大小寫不同也視為同一人。</summary>
    [Fact]
    public async Task Same_email_twice_in_a_book_violates_unique_index()
    {
        var bookId = BookId.New();
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using (var db = createContext())
        {
            var member = BookMember.Create(bookId, "adam@example.com", BookRole.Owner, AddedAt);
            member.BindSubject("sub-1");
            db.BookMembers.Add(member);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = createContext())
        {
            var reloaded = await db.BookMembers.AsNoTracking().SingleAsync(Ct);
            reloaded.Email.Should().Be("adam@example.com");
            reloaded.GoogleSubject.Should().Be("sub-1");
            reloaded.Role.Should().Be(BookRole.Owner);
            reloaded.AddedAt.Should().Be(AddedAt);
        }

        await using (var db = createContext())
        {
            db.BookMembers.Add(BookMember.Create(bookId, "ADAM@example.com", BookRole.Owner, AddedAt));

            var act = () => db.SaveChangesAsync(Ct);

            await act.Should().ThrowAsync<DbUpdateException>();
        }
    }
}
