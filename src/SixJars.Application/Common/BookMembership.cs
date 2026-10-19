using Microsoft.EntityFrameworkCore;
using SixJars.Domain.Common;
using SixJars.Domain.Members;

namespace SixJars.Application.Common;

/// <summary>「誰能存取哪些帳本」的唯一定義；授權、帳本清單與 <c>/api/me</c> 共用，規則才不會分歧。</summary>
internal static class BookMembership
{
    /// <summary>以 Google sub 辨識的使用者所擁有的帳本。P2 只授權擁有者（spec §3.4）。</summary>
    public static IQueryable<BookId> OwnedBookIds(this ISixJarsDbContext db, string subject) =>
        db.BookMembers
            .Where(m => m.GoogleSubject == subject && m.Role == BookRole.Owner)
            .Select(m => m.BookId);

    /// <summary>以 email 認定的擁有者擁有的帳本（不論是否已綁定 sub）；CLI 只拿得到 email（issue #7 Q26）。</summary>
    public static IQueryable<BookId> OwnedBookIdsByEmail(this ISixJarsDbContext db, string normalizedEmail) =>
        db.BookMembers
            .Where(m => m.Email == normalizedEmail && m.Role == BookRole.Owner)
            .Select(m => m.BookId);

    /// <summary>
    /// 同一位擁有者底下是否已有這個名稱（issue #7 Q9）。<paramref name="name"/> 傳 <c>Book.Name</c>（已 trim），以 ordinal 比對。
    /// Web 傳 <see cref="OwnedBookIds"/>（以 sub 認人），CLI 傳 <see cref="OwnedBookIdsByEmail"/>。
    /// </summary>
    public static Task<bool> OwnerHasBookNamedAsync(
        this ISixJarsDbContext db, IQueryable<BookId> ownedBookIds, string name, CancellationToken cancellationToken) =>
        db.Books.AnyAsync(b => ownedBookIds.Contains(b.Id) && b.Name == name, cancellationToken);
}
