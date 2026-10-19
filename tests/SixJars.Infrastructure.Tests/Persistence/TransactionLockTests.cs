using FluentAssertions;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Persistence;

/// <summary>
/// Q16 的確定性保護：建立帳本的上限檢查靠 <c>AcquireTransactionLockAsync</c> 序列化同一位建立者的請求。
/// Endpoint 層的同時送出測試無法穩定觀察到競態（拿掉鎖也常常綠），所以鎖本身的行為在這裡直接驗證。
/// </summary>
public class TransactionLockTests(PostgresFixture postgres)
{
    private const string KeyX = "create-book:x";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Same_key_waits_until_the_holder_commits()
    {
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using var a = createContext();
        await using var b = createContext();
        await using var transactionA = await a.BeginTransactionAsync(Ct);
        await a.AcquireTransactionLockAsync(KeyX, Ct);
        await using var transactionB = await b.BeginTransactionAsync(Ct);

        var bTask = b.AcquireTransactionLockAsync(KeyX, Ct);
        await Task.WhenAny(bTask, Task.Delay(300, Ct));
        bTask.IsCompleted.Should().BeFalse("A 還沒 commit，同一個 key 的 B 必須等");

        await transactionA.CommitAsync(Ct);
        await bTask.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    [Fact]
    public async Task Different_keys_do_not_wait()
    {
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using var a = createContext();
        await using var b = createContext();
        await using var transactionA = await a.BeginTransactionAsync(Ct);
        await a.AcquireTransactionLockAsync(KeyX, Ct);
        await using var transactionB = await b.BeginTransactionAsync(Ct);

        await b.AcquireTransactionLockAsync("create-book:y", Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }
}
