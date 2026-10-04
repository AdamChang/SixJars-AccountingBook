using FluentAssertions;
using SixJars.Domain.Common;
using SixJars.Domain.Members;
using Xunit;

namespace SixJars.Domain.Tests.Members;

/// <summary>帳本成員（CONTEXT.md、ADR 0005）：白名單即此表，首次登入時以 email 比對並綁定 Google sub。</summary>
public class BookMemberTests
{
    private static readonly BookId BookId = BookId.New();
    private static readonly DateTimeOffset AddedAt = new(2026, 10, 4, 1, 2, 3, TimeSpan.Zero);

    [Fact]
    public void Owner_email_is_normalized()
    {
        var member = BookMember.Create(BookId, "  Adam@Example.COM ", BookRole.Owner, AddedAt);

        member.Email.Should().Be("adam@example.com");
        member.BookId.Should().Be(BookId);
        member.Role.Should().Be(BookRole.Owner);
        member.AddedAt.Should().Be(AddedAt);
        member.GoogleSubject.Should().BeNull("首次登入才綁定");
    }

    [Fact]
    public void Bind_subject_on_first_sign_in()
    {
        var member = BookMember.Create(BookId, "adam@example.com", BookRole.Owner, AddedAt);

        member.BindSubject("sub-1");
        member.BindSubject("sub-1");

        member.GoogleSubject.Should().Be("sub-1");
    }

    /// <summary>若允許，代表某人換了 Google 帳號，卻沿用同一個 email 拿到存取權。</summary>
    [Fact]
    public void Binding_a_different_subject_throws()
    {
        var member = BookMember.Create(BookId, "adam@example.com", BookRole.Owner, AddedAt);
        member.BindSubject("sub-1");

        var act = () => member.BindSubject("sub-2");

        act.Should().Throw<DomainException>();
        member.GoogleSubject.Should().Be("sub-1");
    }

    /// <summary>spec §3.4：Bookkeeper 與 ReadOnly 已定義，但 P2 只建立 Owner。</summary>
    [Theory]
    [InlineData(BookRole.Bookkeeper)]
    [InlineData(BookRole.ReadOnly)]
    public void Only_owner_role_is_supported_in_p2(BookRole role)
    {
        var act = () => BookMember.Create(BookId, "adam@example.com", role, AddedAt);

        act.Should().Throw<DomainException>();
    }
}
