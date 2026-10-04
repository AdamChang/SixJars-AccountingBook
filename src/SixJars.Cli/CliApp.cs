using System.CommandLine;
using System.Data.Common;
using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application;
using SixJars.Application.Backup;
using SixJars.Application.Common;
using SixJars.Application.LegacyImport;
using SixJars.Application.Members;
using SixJars.Domain.Common;
using SixJars.Infrastructure;
using SixJars.Infrastructure.LegacyExcel;

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
            ImportLegacyCommand(host),
            AddMemberCommand(host),
            RestoreBackupCommand(host),
        };

        return root.Parse(args).InvokeAsync(new InvocationConfiguration { Output = output, Error = output }, cancellationToken);
    }

    /// <summary>報告的錯誤、警告與修正逐條印出；有錯誤時 exit code 為 1，而且沒有寫入任何資料。</summary>
    private static Command ImportLegacyCommand(Host host)
    {
        var file = new Option<FileInfo>("--file") { Description = "舊 Excel 記帳本（.xlsm）", Required = true };
        var bookName = new Option<string>("--book-name") { Description = "新帳本的名稱；已有同名帳本時拒絕匯入", Required = true };
        var ownerEmail = new Option<string>("--owner-email") { Description = "擁有者的 Google email", Required = true };
        var dryRun = new Option<bool>("--dry-run") { Description = "只轉換與檢查，印出報告與筆數，不寫入" };
        var command = new Command("import-legacy", "把舊 Excel 記帳本匯入成一本新帳本（單一 DB transaction）。")
        {
            file, bookName, ownerEmail, dryRun,
        };
        command.SetAction((parseResult, ct) => host.RunAsync(async (sender, output) =>
        {
            var source = parseResult.GetValue(file)!;
            if (!source.Exists)
            {
                await output.WriteLineAsync($"錯誤：找不到檔案 {source.FullName}。");
                return 1;
            }

            LegacyWorkbook workbook;
            await using (var stream = source.OpenRead())
            {
                workbook = await new ExcelLegacyWorkbookReader().ReadAsync(stream, ct);
            }

            var isDryRun = parseResult.GetValue(dryRun);
            var result = await sender.Send(
                new ImportLegacyBook(workbook, source.Name, parseResult.GetValue(bookName)!, parseResult.GetValue(ownerEmail)!, isDryRun), ct);
            await WriteReportAsync(output, result.Report);

            if (result.Report.Errors.Count > 0)
            {
                await output.WriteLineAsync($"匯入報告有 {result.Report.Errors.Count} 個錯誤，沒有寫入任何資料。");
                return 1;
            }

            var counts = $"交易 {result.Transactions} 筆、預定支出 {result.PlannedExpenses} 筆、警告 {result.Report.Warnings.Count} 個";
            await output.WriteLineAsync(isDryRun
                ? $"dry run：可以匯入（{counts}），沒有寫入任何資料。"
                : $"已匯入帳本 {result.BookId}（{counts}）；擁有者第一次以 Google 登入時綁定帳號。");
            return 0;
        }, ct));
        return command;
    }

    private static async Task WriteReportAsync(TextWriter output, ImportReport report)
    {
        foreach (var (label, issues) in new[] { ("錯誤", report.Errors), ("警告", report.Warnings), ("修正", report.Corrections) })
        {
            foreach (var issue in issues)
            {
                await output.WriteLineAsync($"[{label}] {issue.Sheet} 第 {issue.Row} 列：{issue.Message}");
            }
        }
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

    /// <summary>
    /// 還原 <c>GET /api/books/{id}/export/backup.json</c> 下載的備份（單一 DB transaction）。
    /// 訊息只印檔名，不印完整路徑（本機路徑可能含使用者名稱等個資）。
    /// </summary>
    private static Command RestoreBackupCommand(Host host)
    {
        var file = new Option<FileInfo>("--file") { Description = "備份檔（.json）", Required = true };
        var command = new Command("restore-backup", "把 JSON 備份還原成一本帳；資料庫中已有同一本帳時拒絕。經 Domain 重新驗證，損毀的備份整個不寫入。")
        {
            file,
        };
        command.SetAction((parseResult, ct) => host.RunAsync(async (sender, output) =>
        {
            var source = parseResult.GetValue(file)!;
            if (!source.Exists)
            {
                await output.WriteLineAsync($"錯誤：找不到檔案 {source.Name}。");
                return 1;
            }

            BackupDocument backup;
            try
            {
                await using var stream = source.OpenRead();
                backup = await JsonSerializer.DeserializeAsync<BackupDocument>(stream, BackupJson.Options, ct)
                    ?? throw new JsonException("內容是 null。");
            }
            catch (JsonException ex)
            {
                // System.Text.Json 的訊息只有 JSON 路徑與行號，不含檔案內容或路徑。
                await output.WriteLineAsync($"錯誤：{source.Name} 不是有效的備份檔：{ex.Message}");
                return 1;
            }

            var bookId = await sender.Send(new RestoreBackup(backup, source.Name), ct);
            await output.WriteLineAsync(
                $"已從 {source.Name} 還原帳本 {bookId}（交易 {backup.Transactions.Count} 筆、預定支出 {backup.PlannedExpenses.Count} 筆、" +
                $"成員 {backup.Members.Count} 位、稽核記錄 {backup.AuditEntries.Count} 筆）。");
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
