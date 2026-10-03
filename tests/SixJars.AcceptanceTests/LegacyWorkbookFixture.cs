using SixJars.Application.LegacyImport;
using SixJars.Infrastructure.LegacyExcel;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.AcceptanceTests;

/// <summary>讀一次真實記帳本供整個測試類別共用；檔案不存在時，測試會被略過。</summary>
public sealed class LegacyWorkbookFixture : IAsyncLifetime
{
    private LegacyWorkbook? _workbook;

    public async ValueTask InitializeAsync()
    {
        if (!File.Exists(RepoPaths.LegacyWorkbook))
        {
            return;
        }

        await using var stream = RepoPaths.OpenLegacyWorkbook();
        _workbook = await new ExcelLegacyWorkbookReader().ReadAsync(stream, TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public LegacyWorkbook Require()
    {
        Assert.SkipUnless(_workbook is not null, $"找不到 {RepoPaths.LegacyWorkbook}，略過驗收");
        return _workbook!;
    }
}
