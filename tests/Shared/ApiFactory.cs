using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace SixJars.Tests.Shared;

/// <summary>每個測試一個 factory、一個獨立的資料庫；以 <see cref="TestAuthHandler"/> 取代 Google 登入。</summary>
public sealed class ApiFactory(string? connectionString) : WebApplicationFactory<Program>
{
    public const string DefaultSubject = "owner-sub";

    public static async Task<ApiFactory> CreateAsync(PostgresFixture postgres, CancellationToken cancellationToken) =>
        new(await postgres.CreateConnectionStringAsync(cancellationToken));

    /// <summary>已登入（<see cref="DefaultSubject"/>）的 client。T37 之後會改由 CreateMemberClientAsync 取代。</summary>
    public HttpClient CreateSignedInClient(string subject = DefaultSubject)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, subject);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (connectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:SixJars", connectionString);
        }

        builder.ConfigureTestServices(services =>
            services.AddAuthentication(o =>
                {
                    o.DefaultScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
    }
}
