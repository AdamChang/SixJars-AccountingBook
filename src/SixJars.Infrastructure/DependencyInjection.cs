using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Common;
using SixJars.Application.Exports;
using SixJars.Application.Ledger;
using SixJars.Infrastructure.Exports;
using SixJars.Infrastructure.Ledger;
using SixJars.Infrastructure.Persistence;

namespace SixJars.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// 連線字串只從呼叫端傳入（API 與 CLI 都讀環境變數 ConnectionStrings__SixJars），這裡不設任何預設值。
    /// 測試可以註冊 <see cref="IInterceptor"/>（例如計算資料庫往返次數），會自動掛到 DbContext 上。
    /// 全域 SplitQuery：帳本的四個 owned collection 在單一查詢會 LEFT JOIN 成笛卡兒積（實測一次 32,832 列、約 10 MB），
    /// 拆開後每個集合各一條查詢，代價是載入帳本多 4 次往返（使用者 2026-10-31 同意放寬往返上限）。
    /// </summary>
    public static IServiceCollection AddSixJarsInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SixJarsDbContext>((sp, options) => options
            .UseNpgsql(
                SixJarsConnectionString.ForNpgsql(connectionString),
                npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .AddInterceptors(sp.GetServices<IInterceptor>()));
        services.AddScoped<ISixJarsDbContext>(sp => sp.GetRequiredService<SixJarsDbContext>());
        services.AddScoped<ILedgerSummaryQuery, SqlLedgerSummaryQuery>();
        // 交易明細匯出：每種格式一個 writer，handler 依 ExportTransactions.Format 挑選。
        services.AddSingleton<ITransactionSheetWriter, CsvTransactionWriter>();
        services.AddSingleton<ITransactionSheetWriter, XlsxTransactionWriter>();
        return services;
    }
}
