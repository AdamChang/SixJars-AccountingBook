using FluentAssertions;
using SixJars.Application.Exports;
using SixJars.Domain.Transactions;
using Xunit;

namespace SixJars.Application.Tests.Exports;

public class TransactionKindNamesTests
{
    /// <summary>匯出顯示 CONTEXT.md 的中文名稱；日後新增交易類型卻忘了補名稱，這裡就會失敗，而不是匯出時擲例外。</summary>
    [Fact]
    public void Every_kind_has_the_context_md_name()
    {
        Enum.GetValues<TransactionKind>().Select(TransactionKindNames.Of).Should().Equal(
            "收入", "支出", "轉帳", "提款", "現金存入", "加值", "繳卡費",
            "新增貸款", "貸款繳款", "入新資金", "出資金", "資金回流");
    }
}
