using SixJars.Application.Books;

namespace SixJars.Application.Members;

/// <summary>目前登入的使用者，以及他所屬的帳本（<c>GET /api/me</c>，spec §4）。</summary>
public sealed record MeDto(string Subject, string? Email, IReadOnlyList<BookSummaryDto> Books);
