using SixJars.Domain.Common;

namespace SixJars.Domain.Members;

/// <summary>帳本成員；白名單即此表（ADR 0005）。P2 只支援擁有者。</summary>
public sealed class BookMember
{
    // 參數名稱必須與屬性名稱一致，EF Core 的建構子綁定依賴這一點。
    private BookMember(BookId bookId, string email, BookRole role, DateTimeOffset addedAt)
    {
        Id = Guid.CreateVersion7();
        BookId = bookId;
        Email = email;
        Role = role;
        AddedAt = addedAt;
    }

    public Guid Id { get; private set; }
    public BookId BookId { get; private set; }
    /// <summary>正規化後（去頭尾空白、小寫）的 email；首次登入時以此比對已驗證的 Google email。</summary>
    public string Email { get; private set; }
    /// <summary>Google 的 sub；首次登入時綁定，之後都以此辨識。null 表示尚未登入過。</summary>
    public string? GoogleSubject { get; private set; }
    public BookRole Role { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }

    public static BookMember Create(BookId bookId, string email, BookRole role, DateTimeOffset addedAt)
    {
        if (role != BookRole.Owner)
        {
            throw new DomainException($"P2 只支援擁有者，不能加入角色為 {role} 的成員。");
        }

        return new BookMember(bookId, NormalizeEmail(email), role, addedAt);
    }

    /// <summary>
    /// 還原備份（T42）：以完整欄位重建成員，保留 Id；已綁定的 sub 一併還原。
    /// 經過與 <see cref="Create"/>、<see cref="BindSubject"/> 相同的檢查（只支援擁有者、email 與 sub 不可空白）。
    /// </summary>
    public static BookMember Restore(Guid id, BookId bookId, string email, string? googleSubject, BookRole role, DateTimeOffset addedAt)
    {
        var member = Create(bookId, email, role, addedAt);
        member.Id = id;
        if (googleSubject is not null)
        {
            member.BindSubject(googleSubject);
        }

        return member;
    }

    /// <summary>去頭尾空白並轉小寫；比對登入者的 email 時也要用同樣的方式正規化。</summary>
    public static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("成員的 email 不可為空白。");
        }

        return email.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// 首次登入時綁定 Google sub；已綁定時必須是同一個 sub。
    /// 不同的 sub 代表換了 Google 帳號卻沿用同一個 email，不能因此取得存取權。
    /// </summary>
    public void BindSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new DomainException("Google sub 不可為空白。");
        }

        if (GoogleSubject is not null && GoogleSubject != subject)
        {
            throw new DomainException($"成員 {Email} 已綁定其他 Google 帳號。");
        }

        GoogleSubject = subject;
    }
}
