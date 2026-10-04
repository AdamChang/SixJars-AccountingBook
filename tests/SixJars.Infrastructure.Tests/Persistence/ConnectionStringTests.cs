using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SixJars.Infrastructure.Persistence;
using Xunit;

namespace SixJars.Infrastructure.Tests.Persistence;

public class ConnectionStringTests
{
    /// <summary>
    /// Neon 不支援 GSSAPI；Npgsql 預設會先試 GSS 加密，在沒有 libgssapi_krb5 的 aspnet image 裡會印出誤導的 Error。
    /// 一律關閉 GSS 加密，其他設定照呼叫端傳入的保留。
    /// </summary>
    [Fact]
    public void Disables_gss_encryption_and_keeps_other_settings()
    {
        using var provider = new ServiceCollection()
            .AddSixJarsInfrastructure("Host=db.example;Port=6543;Database=sixjars;Username=app;SSL Mode=Require")
            .BuildServiceProvider();
        using var scope = provider.CreateScope();

        var actual = new NpgsqlConnectionStringBuilder(
            scope.ServiceProvider.GetRequiredService<SixJarsDbContext>().Database.GetConnectionString());

        actual.GssEncryptionMode.Should().Be(GssEncryptionMode.Disable);
        actual.Host.Should().Be("db.example");
        actual.Port.Should().Be(6543);
        actual.Database.Should().Be("sixjars");
        actual.Username.Should().Be("app");
        actual.SslMode.Should().Be(SslMode.Require);
    }
}
