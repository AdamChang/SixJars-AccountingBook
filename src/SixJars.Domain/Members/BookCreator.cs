using SixJars.Domain.Common;

namespace SixJars.Domain.Members;

/// <summary>
/// 建立者名單的一列（ADR 0011）：可以在 Web 建立帳本，沒有任何帳本也能登入。由 CLI <c>add-creator</c>／<c>remove-creator</c> 維護。
/// email 正規化與 sub 綁定的規則與 <see cref="BookMember"/> 相同。
/// </summary>
public sealed class BookCreator
{
    // 參數名稱必須與屬性名稱一致，EF Core 的建構子綁定依賴這一點。
    private BookCreator(string email, DateTimeOffset addedAt)
    {
        Id = Guid.CreateVersion7();
        Email = email;
        AddedAt = addedAt;
    }

    public Guid Id { get; private set; }
    /// <summary>正規化後的 email；首次登入時以此比對已驗證的 Google email。</summary>
    public string Email { get; private set; }
    /// <summary>Google 的 sub；首次登入時綁定。null 表示尚未登入過。</summary>
    public string? GoogleSubject { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }

    public static BookCreator Create(string email, DateTimeOffset addedAt) => new(BookMember.NormalizeEmail(email), addedAt);

    /// <summary>首次登入時綁定 Google sub；已綁定時必須是同一個 sub（理由同 <see cref="BookMember.BindSubject"/>）。</summary>
    public void BindSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new DomainException("Google sub 不可為空白。");
        }

        if (GoogleSubject is not null && GoogleSubject != subject)
        {
            throw new DomainException($"建立者 {Email} 已綁定其他 Google 帳號。");
        }

        GoogleSubject = subject;
    }
}
