using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

public class HealthEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Health_checks_the_database_without_signing_in()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ApiFactory.CreateAsync(postgres, ct);

        var response = await factory.CreateClient().GetAsync("/health", ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_is_503_when_the_database_is_unreachable()
    {
        var ct = TestContext.Current.CancellationToken;
        // port 1 沒有任何服務；Timeout 縮短讓測試不必等預設的 15 秒
        await using var factory = new ApiFactory("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2");

        var response = await factory.CreateClient().GetAsync("/health", ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Ready_when_every_migration_is_applied()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ApiFactory.CreateAsync(postgres, ct);

        var response = await factory.CreateClient().GetAsync("/health/ready", ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Not_ready_while_a_migration_is_pending()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new CapturedWarnings();
        // 只套用到第一個 migration：模擬「部署了新版、但忘了先在本機跑 migration bundle」
        await using var factory = new ApiFactory(await postgres.CreateConnectionStringAtAsync("InitialLedger", ct), logs.Register);

        var response = await factory.CreateClient().GetAsync("/health/ready", ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        // Cloud Run 上看不到 probe 的回應內容，只能靠 log 知道漏了哪幾個 migration
        logs.Messages.Should().ContainSingle(m => m.Contains("migration 未套用") && m.Contains("AddTags"));
    }

    [Fact]
    public async Task Not_ready_when_the_database_is_unreachable()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new CapturedWarnings();
        await using var factory = new ApiFactory("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2", logs.Register);

        var response = await factory.CreateClient().GetAsync("/health/ready", ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        logs.Messages.Should().ContainSingle(m => m.Contains("連不上資料庫") && m.Contains("Failed to connect"));
    }

    [Fact]
    public void Startup_fails_without_a_connection_string()
    {
        // 前提：執行測試的環境沒有設定 ConnectionStrings__SixJars
        using var factory = new ApiFactory(connectionString: null);

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionStrings__SixJars*");
    }

    /// <summary>收集 SixJars.Api.Health 的 warning（含例外訊息），驗證 /health/ready 未就緒時有留下原因。</summary>
    private sealed class CapturedWarnings : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public void Register(IServiceCollection services) => services.AddSingleton<ILoggerProvider>(this);

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName == "SixJars.Api.Health" ? _messages : null);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<string>? messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => messages is not null && logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    messages!.Enqueue($"{formatter(state, exception)} {exception?.Message}");
                }
            }
        }
    }
}
