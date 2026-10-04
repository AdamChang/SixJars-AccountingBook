using FluentAssertions;
using SixJars.Application.Auditing;
using SixJars.Domain.Transactions;
using Xunit;

namespace SixJars.Application.Tests.Auditing;

public class AuditSnapshotsTests
{
    [Fact]
    public void Snapshot_keeps_chinese_readable_and_enums_as_names()
    {
        // jsonb 存檔時會把 \uXXXX 還原成字元，API 測試看不出差別，所以在序列化這一層鎖住設定。
        var json = AuditSnapshots.Serialize(new { Note = "午餐<加蛋>", Kind = TransactionKind.Expense });

        json.Should().Be("""{"note":"午餐<加蛋>","kind":"Expense"}""");
    }
}
