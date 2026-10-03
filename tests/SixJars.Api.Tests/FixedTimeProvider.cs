namespace SixJars.Api.Tests;

/// <summary>固定在某個 UTC 時間的 <see cref="TimeProvider"/>，讓「今天」可以測試。</summary>
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
