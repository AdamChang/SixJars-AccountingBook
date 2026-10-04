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
}
