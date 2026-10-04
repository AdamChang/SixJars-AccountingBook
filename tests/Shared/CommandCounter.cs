using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SixJars.Tests.Shared;

/// <summary>
/// 計算資料庫往返次數（spec §5：ADR 0004 的跨國延遲）。以 <c>IInterceptor</c> 註冊到 DI，
/// 會被 <c>AddSixJarsInfrastructure</c> 自動掛到 DbContext 上。
/// </summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}
