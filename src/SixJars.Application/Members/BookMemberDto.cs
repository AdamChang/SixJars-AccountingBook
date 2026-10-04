using SixJars.Domain.Members;

namespace SixJars.Application.Members;

/// <summary>帳本成員的快照；目前只用在稽核記錄（第一次登入時綁定 Google sub）。</summary>
public sealed record BookMemberDto(Guid Id, Guid BookId, string Email, string? GoogleSubject, BookRole Role, DateTimeOffset AddedAt)
{
    public static BookMemberDto From(BookMember m) => new(m.Id, m.BookId.Value, m.Email, m.GoogleSubject, m.Role, m.AddedAt);
}
