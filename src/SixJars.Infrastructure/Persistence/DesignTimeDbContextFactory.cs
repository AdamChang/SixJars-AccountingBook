using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SixJars.Infrastructure.Persistence;

/// <summary>
/// 只供 dotnet ef 產生 migration，不實際連線、不含任何憑證；正式連線字串於 P2 由環境變數提供。
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SixJarsDbContext>
{
    public SixJarsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SixJarsDbContext>().UseNpgsql("Host=localhost;Database=sixjars_design").Options);
}
