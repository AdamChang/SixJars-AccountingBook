using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Api.Infrastructure;
using SixJars.Application.Auditing;
using SixJars.Domain.Common;
using SixJars.Domain.Members;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests.Auth;

/// <summary>
/// 白名單檢查（ADR 0005、spec §3.4）：直接以 <see cref="ClaimsPrincipal"/> 呼叫，不連 Google。
/// 第一次登入以已驗證的 email 比對帳本成員並綁定 sub，之後以 sub 辨識。
/// </summary>
public class GoogleSignInValidatorTests(PostgresFixture postgres)
{
    private const string Email = "adam@example.com";
    private const string Subject = "google-sub-1";
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData(null)]
    public async Task Unverified_email_is_rejected(string? emailVerified)
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct, ownerSubject: null);
        var member = await AddMemberAsync(factory, book.Id, Email);

        var accepted = await ValidateAsync(factory, Principal(Subject, Email, emailVerified));

        accepted.Should().BeFalse("email 沒有經過 Google 驗證，任何人都能把別人的 email 填進自己的帳號");
        (await ReloadAsync(factory, member.Id)).GoogleSubject.Should().BeNull();
    }

    [Fact]
    public async Task Missing_subject_is_rejected()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct, ownerSubject: null);
        var member = await AddMemberAsync(factory, book.Id, Email);

        var accepted = await ValidateAsync(factory, Principal(subject: null, Email, "true"));

        accepted.Should().BeFalse();
        (await ReloadAsync(factory, member.Id)).GoogleSubject.Should().BeNull();
    }

    [Fact]
    public async Task Unknown_email_is_rejected()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct, ownerSubject: null);
        await AddMemberAsync(factory, book.Id, Email);

        var accepted = await ValidateAsync(factory, Principal(Subject, "stranger@example.com", "true"));

        accepted.Should().BeFalse();
    }

    [Fact]
    public async Task Known_email_binds_subject_on_first_sign_in()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct, ownerSubject: null);
        var member = await AddMemberAsync(factory, book.Id, Email);

        // email 的大小寫與成員表不同、email_verified 為大寫，仍然算同一個已驗證的 email。
        var accepted = await ValidateAsync(factory, Principal(Subject, "Adam@Example.COM", "True"));

        accepted.Should().BeTrue();
        (await ReloadAsync(factory, member.Id)).GoogleSubject.Should().Be(Subject);
    }

    /// <summary>
    /// 綁定是成員資料的異動，要留下稽核記錄。callback 當下使用者還沒登入，操作者必須是被綁定的 sub，
    /// 不能取自 <c>ICurrentUser</c>；同一個 email 屬於多本帳時，每本各綁定、各記一筆。
    /// </summary>
    [Fact]
    public async Task First_bind_is_audited_per_member_with_the_bound_subject_as_actor()
    {
        await using var factory = await CreateFactoryAsync();
        var first = await factory.SeedBookAsync(Ct, ownerSubject: null);
        var second = await factory.SeedBookAsync(Ct, ownerSubject: null);
        var firstMember = await AddMemberAsync(factory, first.Id, Email);
        var secondMember = await AddMemberAsync(factory, second.Id, Email);

        var accepted = await ValidateAsync(factory, Principal(Subject, Email, "true"));

        accepted.Should().BeTrue();
        (await ReloadAsync(factory, firstMember.Id)).GoogleSubject.Should().Be(Subject);
        (await ReloadAsync(factory, secondMember.Id)).GoogleSubject.Should().Be(Subject);
        var entries = await EntriesAsync(factory);
        entries.Select(e => (e.BookId, e.EntityId)).Should().BeEquivalentTo(
            [(first.Id.Value, firstMember.Id), (second.Id.Value, secondMember.Id)]);
        foreach (var entry in entries)
        {
            entry.ActorSubject.Should().Be(Subject);
            entry.Action.Should().Be(AuditAction.Update);
            entry.EntityType.Should().Be(AuditEntityTypes.BookMember);
            entry.At.Should().Be(Now);
            SubjectIn(entry.Before!).Should().BeNull();
            SubjectIn(entry.After!).Should().Be(Subject);
        }
    }

    [Fact]
    public async Task Bound_subject_signs_in_even_if_email_changed()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct, ownerSubject: null);
        await AddMemberAsync(factory, book.Id, Email, Subject);

        var accepted = await ValidateAsync(factory, Principal(Subject, "renamed@example.com", "true"));

        accepted.Should().BeTrue();
        (await EntriesAsync(factory)).Should().BeEmpty("沒有綁定任何成員，就不是資料異動");
    }

    /// <summary>
    /// email 相同、但成員已綁定其他 sub：代表換了 Google 帳號，不能因此取得存取權，
    /// 也不能讓 <c>BindSubject</c> 的例外在 OIDC callback 變成 500，而是乾淨地拒絕。
    /// </summary>
    [Fact]
    public async Task Member_bound_to_another_subject_is_not_rebound()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct, ownerSubject: null);
        var member = await AddMemberAsync(factory, book.Id, Email, "original-sub");

        var accepted = await ValidateAsync(factory, Principal(Subject, Email, "true"));

        accepted.Should().BeFalse();
        (await ReloadAsync(factory, member.Id)).GoogleSubject.Should().Be("original-sub");
        (await EntriesAsync(factory)).Should().BeEmpty();
    }

    /// <summary>已經登入過的人，之後才以 CLI 加進另一本帳：下次登入時也要綁定新的那本，否則永遠看不到它。</summary>
    [Fact]
    public async Task Book_added_later_is_bound_on_next_sign_in()
    {
        await using var factory = await CreateFactoryAsync();
        var first = await factory.SeedBookAsync(Ct, ownerSubject: null);
        var second = await factory.SeedBookAsync(Ct, ownerSubject: null);
        await AddMemberAsync(factory, first.Id, Email, Subject);
        var later = await AddMemberAsync(factory, second.Id, Email);

        var accepted = await ValidateAsync(factory, Principal(Subject, Email, "true"));

        accepted.Should().BeTrue();
        (await ReloadAsync(factory, later.Id)).GoogleSubject.Should().Be(Subject);
        (await EntriesAsync(factory)).Should().ContainSingle(e => e.EntityId == later.Id);
    }

    private Task<ApiFactory> CreateFactoryAsync() =>
        ApiFactory.CreateAsync(postgres, Ct, services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now)));

    private static ClaimsPrincipal Principal(string? subject, string? email, string? emailVerified)
    {
        List<Claim> claims = [];
        if (subject is not null)
        {
            claims.Add(new Claim("sub", subject));
        }

        if (email is not null)
        {
            claims.Add(new Claim("email", email));
        }

        if (emailVerified is not null)
        {
            claims.Add(new Claim("email_verified", emailVerified));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Google"));
    }

    /// <summary>在 request scope 之外呼叫：沒有 HttpContext，也就沒有登入的使用者，與 OIDC callback 當下相同。</summary>
    private static async Task<bool> ValidateAsync(ApiFactory factory, ClaimsPrincipal principal)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<GoogleSignInValidator>().ValidateAsync(principal, Ct);
    }

    private static async Task<BookMember> AddMemberAsync(ApiFactory factory, BookId bookId, string email, string? subject = null)
    {
        var member = BookMember.Create(bookId, email, BookRole.Owner, Now.AddDays(-1));
        if (subject is not null)
        {
            member.BindSubject(subject);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        db.BookMembers.Add(member);
        await db.SaveChangesAsync(Ct);
        return member;
    }

    private static async Task<BookMember> ReloadAsync(ApiFactory factory, Guid memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SixJarsDbContext>().BookMembers.AsNoTracking()
            .SingleAsync(m => m.Id == memberId, Ct);
    }

    private static async Task<List<AuditEntry>> EntriesAsync(ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SixJarsDbContext>().AuditEntries.AsNoTracking().ToListAsync(Ct);
    }

    private static string? SubjectIn(string snapshot) =>
        JsonDocument.Parse(snapshot).RootElement.GetProperty("googleSubject").GetString();
}
