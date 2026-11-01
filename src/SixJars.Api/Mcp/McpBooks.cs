using MediatR;
using ModelContextProtocol;
using SixJars.Application.Books;
using SixJars.Application.Common;

namespace SixJars.Api.Mcp;

/// <summary>讀取帳本與解析分類名稱的共用邏輯：<see cref="LedgerTools"/> 與 <see cref="BookkeepingTools"/> 共用，錯誤訊息保持一致。</summary>
internal static class McpBooks
{
    /// <summary>非成員與不存在的帳本都是同一句話（BookAccessBehavior 一律擲 NotFoundException）。</summary>
    public static async Task<BookDto> GetBookAsync(ISender sender, Guid bookId, CancellationToken cancellationToken)
    {
        try
        {
            return await sender.Send(new GetBook(bookId), cancellationToken);
        }
        catch (NotFoundException)
        {
            throw new McpException("找不到帳本。");
        }
    }

    public static (string? Main, string? Sub) CategoryNames(Guid? categoryId, Dictionary<Guid, CategoryDto> categories)
    {
        if (categoryId is not { } id || !categories.TryGetValue(id, out var category))
        {
            return (null, null);
        }

        return category.ParentId is { } parentId && categories.TryGetValue(parentId, out var parent)
            ? (parent.Name, category.Name)
            : (category.Name, null);
    }
}
