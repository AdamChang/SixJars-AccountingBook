using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SixJars.Tests.Shared;

/// <summary>每個測試一個 factory、一個獨立的資料庫；預設以 <see cref="TestAuthHandler"/> 取代 Google 登入。</summary>
/// <param name="testServices">額外替換的服務，例如 <see cref="CommandCounter"/> 或固定時間的 <see cref="TimeProvider"/>。</param>
/// <param name="useTestAuthentication">
/// false 時保留正式的 cookie＋Google OIDC 設定，用來測試認證本身（未登入回 401、登入轉址）；其他測試一律用預設的 true。
/// </param>
/// <param name="settings">額外的組態值（例如假的 Google client id），以 <c>UseSetting</c> 寫入。</param>
public sealed class ApiFactory(
    string? connectionString,
    Action<IServiceCollection>? testServices = null,
    bool useTestAuthentication = true,
    IReadOnlyDictionary<string, string>? settings = null) : WebApplicationFactory<Program>
{
    public const string DefaultSubject = "owner-sub";

    /// <param name="testServices">額外替換的服務，例如 <see cref="CommandCounter"/> 或固定時間的 <see cref="TimeProvider"/>。</param>
    public static async Task<ApiFactory> CreateAsync(
        PostgresFixture postgres, CancellationToken cancellationToken, Action<IServiceCollection>? testServices = null) =>
        new(await postgres.CreateConnectionStringAsync(cancellationToken), testServices);

    /// <summary>
    /// 已登入、但<b>沒有</b> XSRF token 的 client：只用來測試 antiforgery 本身與唯讀請求；一般測試用 <see cref="CreateMemberClientAsync"/>。
    /// </summary>
    /// <remarks>cookie 一律由測試明確設定（<see cref="ApiXsrfTokens.ApplyTo"/>），不讓 client 自動保存，否則會與手動設定的 Cookie header 重複。</remarks>
    public HttpClient CreateSignedInClient(string subject = DefaultSubject)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, subject);
        return client;
    }

    /// <summary>
    /// 已登入、而且帶好 XSRF token 的 client（ADR 0005：非 GET 的 API 都要驗證）：
    /// 以該身分呼叫 <c>/api/antiforgery/token</c>，再把取得的 cookie 與 <c>X-XSRF-TOKEN</c> header 設為預設值。
    /// </summary>
    public async Task<HttpClient> CreateMemberClientAsync(string subject = DefaultSubject)
    {
        var client = CreateSignedInClient(subject);
        (await ApiXsrfTokens.FetchAsync(client)).ApplyTo(client);
        return client;
    }

    /// <summary>
    /// 每個測試一個資料庫，Npgsql 就為每個連線字串各留一個連線池，閒置連線預設 5 分鐘才關閉；
    /// 測試一多就超過 PostgreSQL 的 max_connections（100）。factory 結束時直接清掉這個資料庫的連線池。
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (connectionString is not null)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            NpgsqlConnection.ClearPool(connection);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (connectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:SixJars", connectionString);
        }

        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            if (useTestAuthentication)
            {
                // 只覆寫 DefaultScheme 與 DefaultChallengeScheme：正式設定也只設 DefaultScheme（cookie），
                // 所以 authenticate／challenge／forbid 全部落到 Test scheme，未登入回 401 而不是轉址到 Google。
                services.AddAuthentication(o =>
                    {
                        o.DefaultScheme = TestAuthHandler.SchemeName;
                        o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            }

            testServices?.Invoke(services);
        });
    }
}
