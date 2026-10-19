using FluentAssertions;
using SixJars.Domain.Common;
using SixJars.Domain.Members;
using Xunit;

namespace SixJars.Domain.Tests.Members;

/// <summary>建立者名單（ADR 0011）：email 正規化與 sub 綁定的規則與 <see cref="BookMember"/> 相同。</summary>
public class BookCreatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalizes_email_and_starts_unbound()
    {
        var creator = BookCreator.Create(" Adam@Example.com ", Now);

        creator.Email.Should().Be("adam@example.com");
        creator.GoogleSubject.Should().BeNull();
        creator.AddedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_email_is_rejected(string email)
    {
        var act = () => BookCreator.Create(email, Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Binding_the_same_subject_again_is_allowed()
    {
        var creator = BookCreator.Create("adam@example.com", Now);
        creator.BindSubject("sub-1");

        creator.BindSubject("sub-1");

        creator.GoogleSubject.Should().Be("sub-1");
    }

    [Fact]
    public void Binding_another_subject_is_rejected()
    {
        var creator = BookCreator.Create("adam@example.com", Now);
        creator.BindSubject("sub-1");

        var act = () => creator.BindSubject("sub-2");

        act.Should().Throw<DomainException>().WithMessage("*adam@example.com*");
        creator.GoogleSubject.Should().Be("sub-1");
    }
}
