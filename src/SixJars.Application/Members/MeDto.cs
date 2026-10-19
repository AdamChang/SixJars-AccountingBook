using SixJars.Application.Books;

namespace SixJars.Application.Members;

/// <summary>
/// 目前登入的使用者，以及他所屬的帳本（<c>GET /api/me</c>，spec §4）。
/// <paramref name="CanCreateBooks"/>：是否在建立者名單；<paramref name="RemainingBookQuota"/>：還能建立幾本，非建立者為 0（issue #7）。
/// </summary>
public sealed record MeDto(string Subject, string? Email, IReadOnlyList<BookSummaryDto> Books, bool CanCreateBooks, int RemainingBookQuota);
