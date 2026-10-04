using System.CommandLine;
using System.Data.Common;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application;
using SixJars.Application.Common;
using SixJars.Application.Members;
using SixJars.Domain.Common;
using SixJars.Infrastructure;

namespace SixJars.Cli;

/// <summary>
/// CLI 的 composition root（spec §8.1）：與 API 共用 Application 的 command，不另寫一套寫入邏輯。
/// 環境變數與輸出都由呼叫端傳入，測試直接在 process 內呼叫，不必啟動子 process。
/// </summary>
/// <remarks>
/// exit code：0 成功；1 業務或輸入錯誤（帳本不存在、成員重複、validation 失敗、資料庫錯誤）；2 未設定連線字串。
/// 輸出一律不含連線字串：錯誤只印訊息，不印 stack trace，訊息中出現連線字串或密碼時先遮蔽。
/// </remarks>
public static class CliApp
{
    /// <summary>連線字串只讀這個環境變數；沒有設定就失敗，不預設連到任何地方。</summary>
    public const string ConnectionStringVariable = "ConnectionStrings__SixJars";

    private const string MediatRLicenseKeyVariable = "MediatR__LicenseKey";
    private const string Redacted = "***";

    /// <param name="testServices">測試用：在建立 host 之後覆寫服務（例如固定時鐘）。</param>
    public static Task<int> RunAsync(
        string[] args,
        IReadOnlyDictionary<string, string?> environment,
        TextWriter output,
        CancellationToken cancellationToken,
        Action<IServiceCollection>? testServices = null)
    {
        var host = new Host(environment, output, testServices);
        var root = new RootCommand("六罐子記帳本的管理工具（直接寫入 ConnectionStrings__SixJars 指定的資料庫）。")
        {
            AddMemberCommand(host),
        };

        return root.Parse(args).InvokeAsync(new InvocationConfiguration { Output = output, Error = output }, cancellationToken);
    }

    private static Command AddMemberCommand(Host host)
    {
        var book = new Option<Guid>("--book") { Description = "帳本 Id", Required = true };
        var email = new Option<string>("--email") { Description = "成員的 Google email", Required = true };
        var command = new Command("add-member", "把 email 加為帳本的擁有者；第一次以這個 email 登入 Google 時才綁定帳號。") { book, email };
        command.SetAction((parseResult, ct) => host.RunAsync(async (sender, output) =>
        {
            var bookId = parseResult.GetValue(book);
            var memberId = await sender.Send(new AddOwner(bookId, parseResult.GetValue(email)!), ct);
            await output.WriteLineAsync($"已加入擁有者（成員 Id {memberId}，帳本 {bookId}）。");
            return 0;
        }, ct));
        return command;
    }

    /// <summary>每個子命令各建立一次 DI container 與 scope，執行完就釋放。</summary>
    private sealed class Host(IReadOnlyDictionary<string, string?> environment, TextWriter output, Action<IServiceCollection>? testServices)
    {
        public async Task<int> RunAsync(Func<ISender, TextWriter, Task<int>> action, CancellationToken cancellationToken)
        {
            if (!environment.TryGetValue(ConnectionStringVariable, out var connectionString) || string.IsNullOrWhiteSpace(connectionString))
            {
                await output.WriteLineAsync($"錯誤：未設定資料庫連線字串，請設定環境變數 {ConnectionStringVariable}。");
                return 2;
            }

            var services = new ServiceCollection();
            // MediatR 需要 ILoggerFactory；刻意不加任何 provider：EF Core 的記錄可能帶出連線資訊，CLI 只印自己的訊息。
            services.AddLogging();
            services.AddSixJarsApplication(environment.GetValueOrDefault(MediatRLicenseKeyVariable));
            services.AddSixJarsInfrastructure(connectionString);
            services.AddSingleton<ICurrentUser, CliCurrentUser>();
            services.AddSingleton(TimeProvider.System);
            testServices?.Invoke(services);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using var scope = provider.CreateAsyncScope();
            try
            {
                return await action(scope.ServiceProvider.GetRequiredService<ISender>(), output);
            }
            catch (ValidationException ex)
            {
                foreach (var failure in ex.Errors)
                {
                    await output.WriteLineAsync($"錯誤：{failure.PropertyName}：{failure.ErrorMessage}");
                }

                return 1;
            }
            catch (Exception ex) when (ex is DomainException or NotFoundException)
            {
                await output.WriteLineAsync($"錯誤：{ex.Message}");
                return 1;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 非預期的錯誤（多半是連線或資料庫）：印出例外鏈的訊息，但遮蔽連線字串與密碼，也不印 stack trace。
                for (var e = ex; e is not null; e = e.InnerException)
                {
                    await output.WriteLineAsync($"錯誤：{e.GetType().Name}：{Redact(e.Message, connectionString)}");
                }

                return 1;
            }
        }

        private static string Redact(string message, string connectionString)
        {
            var redacted = message.Replace(connectionString, Redacted, StringComparison.Ordinal);
            foreach (var secret in Secrets(connectionString))
            {
                redacted = redacted.Replace(secret, Redacted, StringComparison.Ordinal);
            }

            return redacted;
        }

        private static IEnumerable<string> Secrets(string connectionString)
        {
            DbConnectionStringBuilder builder;
            try
            {
                builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            }
            catch (ArgumentException)
            {
                // 格式不合法的連線字串無法拆出密碼；整串已在上面遮蔽。
                return [];
            }

            return builder.Keys.Cast<string>()
                .Where(key => key.Equals("password", StringComparison.OrdinalIgnoreCase) || key.Equals("pwd", StringComparison.OrdinalIgnoreCase))
                .Select(key => builder[key]?.ToString())
                .OfType<string>()
                .Where(value => value.Length > 0)
                .ToList();
        }
    }
}
