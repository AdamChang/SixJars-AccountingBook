using System.CommandLine;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
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
            AddCreatorCommand(host),
            RemoveCreatorCommand(host),
            RevokeMcpAccessCommand(host),
            RestoreBackupCommand(host),
        };

        return root.Parse(args).InvokeAsync(new InvocationConfiguration { Output = output, Error = output }, cancellationToken);
    }

    /// <summary>
    /// 兩種模式：匯入成新帳本（<c>--book-name</c>＋<c>--owner-email</c>），或補匯舊年度到既有帳本最前面（<c>--into</c>，spec R2-Q4）。
    /// 報告的錯誤、警告與修正逐條印出；有錯誤時 exit code 為 1，而且沒有寫入任何資料。
    /// </summary>
    private static Command ImportLegacyCommand(Host host)
    {
        var file = new Option<FileInfo>("--file") { Description = "舊 Excel 記帳本（.xlsm）", Required = true };
        var bookName = new Option<string>("--book-name") { Description = "新帳本的名稱；同一位擁有者已有同名帳本時拒絕匯入" };
        var ownerEmail = new Option<string>("--owner-email") { Description = "擁有者的 Google email" };
        var into = new Option<Guid?>("--into") { Description = "補匯舊年度：接到這本既有帳本的最前面（與 --book-name／--owner-email 互斥）" };
        var dryRun = new Option<bool>("--dry-run") { Description = "只轉換與檢查，印出報告與筆數，不寫入" };
        var adjust = new Option<bool>("--adjust") { Description = "只用於 --into：銜接差額以「餘額調整」補在原期初日，而不是整批拒絕（先用 --dry-run 檢查）" };
        var command = new Command("import-legacy", "把舊 Excel 記帳本匯入成一本新帳本，或補匯舊年度到既有帳本（單一 DB transaction）。")
        {
            file, bookName, ownerEmail, into, dryRun, adjust,
        };
        command.SetAction(async (parseResult, ct) =>
        {
            var (name, email, target) = (parseResult.GetValue(bookName), parseResult.GetValue(ownerEmail), parseResult.GetValue(into));
            var isNewBook = target is null && name is not null && email is not null;
            var isInto = target is not null && name is null && email is null;
            if (!isNewBook && !isInto)
            {
                // 兩種模式互斥：不用 Required，改在這裡檢查，才能同時表達「擇一」與「不可混用」；不需要連線字串就能判斷。
                await host.Output.WriteLineAsync("錯誤：請指定 --into <帳本 Id>，或同時指定 --book-name 與 --owner-email；兩種模式不能混用。");
                return 1;
            }

            var isAdjust = parseResult.GetValue(adjust);
            if (isAdjust && !isInto)
            {
                await host.Output.WriteLineAsync("錯誤：--adjust 只能搭配 --into（補匯舊年度）使用。");
                return 1;
            }

            var source = parseResult.GetValue(file)!;
            var isDryRun = parseResult.GetValue(dryRun);
            return await host.RunAsync(async (IServiceProvider services, TextWriter output) =>
            {
                if (!source.Exists)
                {
                    await output.WriteLineAsync($"錯誤：找不到檔案 {source.FullName}。");
                    return 1;
                }

                LegacyWorkbook workbook;
                await using (var stream = source.OpenRead())
                {
                    workbook = await services.GetRequiredService<ILegacyWorkbookReader>().ReadAsync(stream, ct);
                }

                var sender = services.GetRequiredService<ISender>();
                return isInto
                    ? await PrependAsync(sender, output, new PrependLegacyYear(workbook, source.Name, target!.Value, isDryRun, isAdjust), ct)
                    : await ImportNewBookAsync(sender, output, new ImportLegacyBook(workbook, source.Name, name!, email!, isDryRun), ct);
            }, ct);
        });
        return command;
    }

    private static async Task<int> ImportNewBookAsync(ISender sender, TextWriter output, ImportLegacyBook request, CancellationToken ct)
    {
        var result = await sender.Send(request, ct);
        await WriteReportAsync(output, result.Report);

        if (result.Report.Errors.Count > 0)
        {
            await output.WriteLineAsync($"匯入報告有 {result.Report.Errors.Count} 個錯誤，沒有寫入任何資料。");
            return 1;
        }

        var counts = $"交易 {result.Transactions} 筆、預定支出 {result.PlannedExpenses} 筆、警告 {result.Report.Warnings.Count} 個";
        await output.WriteLineAsync(request.DryRun
            ? $"dry run：可以匯入（{counts}），沒有寫入任何資料。"
            : $"已匯入帳本 {result.BookId}（{counts}）；擁有者第一次以 Google 登入時綁定帳號。");
        return 0;
    }

    /// <summary>補匯舊年度：報告之後印出期初日的變化與銜接摘要（spec「報告」），讓使用者看出哪個帳戶對不起來。</summary>
    private static async Task<int> PrependAsync(ISender sender, TextWriter output, PrependLegacyYear request, CancellationToken ct)
    {
        PrependLegacyYearResult result;
        try
        {
            result = await sender.Send(request, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // spec D2：衝突機率極低，不加鎖，請使用者重跑。
            await output.WriteLineAsync("錯誤：補匯期間帳本被修改（例如有人同時在 Web 改設定），沒有寫入任何資料；請重新執行。");
            return 1;
        }

        await WriteReportAsync(output, result.Report);
        if (result.Junction.Count > 0)
        {
            await output.WriteLineAsync($"期初日：{result.PreviousOpeningDate:yyyy-MM-dd} → {result.NewOpeningDate:yyyy-MM-dd}");
            await output.WriteLineAsync("銜接摘要（名稱｜舊年度期初｜變動｜推算期末｜帳本期初｜差額｜調整）：");
            foreach (var line in result.Junction)
            {
                var difference = line.Difference is { } d ? Amount(d) : "不檢查";
                await output.WriteLineAsync(
                    $"  {(line.IsPlanningFund ? "[財務規劃] " : "")}{line.Name}｜{Amount(line.PreviousOpening)}｜{Amount(line.Movement)}｜" +
                    $"{Amount(line.PreviousClosing)}｜{Amount(line.ExpectedOpening)}｜{difference}｜{Amount(line.Adjustment)}");
            }
        }

        if (result.Report.Errors.Count > 0)
        {
            await output.WriteLineAsync($"補匯報告有 {result.Report.Errors.Count} 個錯誤，沒有寫入任何資料。");
            return 1;
        }

        var counts = $"交易 {result.Transactions} 筆、預定支出 {result.PlannedExpenses} 筆、" +
            $"新增並封存帳戶 {result.ArchivedAccounts} 個、財務規劃帳戶 {result.ArchivedPlanningFunds} 個、分類 {result.ArchivedCategories} 個、" +
            $"餘額調整 {result.Adjustments} 筆、警告 {result.Report.Warnings.Count} 個";
        await output.WriteLineAsync(request.DryRun
            ? $"dry run：可以補匯（{counts}），沒有寫入任何資料。"
            : $"已把 {request.FileName} 補匯到帳本 {request.BookId}（{counts}）。");
        return 0;
    }

    private static string Amount(decimal value) => value.ToString("#,0.##", CultureInfo.InvariantCulture);

    /// <summary>沒有來源列號的項目（設定、銜接檢查）Row 為 0，不印「第 0 列」。</summary>
    private static async Task WriteReportAsync(TextWriter output, ImportReport report)
    {
        foreach (var (label, issues) in new[] { ("錯誤", report.Errors), ("警告", report.Warnings), ("修正", report.Corrections) })
        {
            foreach (var issue in issues)
            {
                var location = issue.Row == 0 ? issue.Sheet : $"{issue.Sheet} 第 {issue.Row} 列";
                await output.WriteLineAsync($"[{label}] {location}：{issue.Message}");
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

    private static Command AddCreatorCommand(Host host)
    {
        var email = new Option<string>("--email") { Description = "建立者的 Google email", Required = true };
        var command = new Command("add-creator", "把 email 加進建立者名單：可以在 Web 建立帳本，沒有帳本也能登入（ADR 0011）。") { email };
        command.SetAction((parseResult, ct) => host.RunAsync(async (sender, output) =>
        {
            var id = await sender.Send(new AddBookCreator(parseResult.GetValue(email)!), ct);
            await output.WriteLineAsync($"已加入建立者名單（Id {id}）。");
            return 0;
        }, ct));
        return command;
    }

    private static Command RemoveCreatorCommand(Host host)
    {
        var email = new Option<string>("--email") { Description = "建立者的 Google email", Required = true };
        var command = new Command("remove-creator", "從建立者名單移除 email；已建立的帳本不受影響。") { email };
        command.SetAction((parseResult, ct) => host.RunAsync(async (sender, output) =>
        {
            await sender.Send(new RemoveBookCreator(parseResult.GetValue(email)!), ct);
            await output.WriteLineAsync("已從建立者名單移除。");
            return 0;
        }, ct));
        return command;
    }

    /// <summary>撤銷某人所有的 MCP 授權（ADR 0013）；沒有有效的授權不算錯誤。</summary>
    private static Command RevokeMcpAccessCommand(Host host)
    {
        var email = new Option<string>("--email") { Description = "授權者的 Google email", Required = true };
        var command = new Command("revoke-mcp-access", "撤銷這個 email 的所有 MCP 授權（Claude 等 AI client 的唯讀存取）；立即生效。") { email };
        command.SetAction((parseResult, ct) => host.RunAsync(async (sender, output) =>
        {
            var revoked = await sender.Send(new RevokeMcpAccess(parseResult.GetValue(email)!), ct);
            await output.WriteLineAsync(revoked == 0 ? "沒有有效的授權。" : $"已撤銷 {revoked} 筆授權。");
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
        public TextWriter Output => output;

        public Task<int> RunAsync(Func<ISender, TextWriter, Task<int>> action, CancellationToken cancellationToken) =>
            RunAsync((IServiceProvider services, TextWriter writer) => action(services.GetRequiredService<ISender>(), writer), cancellationToken);

        /// <summary>需要 <see cref="ISender"/> 以外服務的子命令（例如 Excel 讀取，測試可以換成假的）。</summary>
        public async Task<int> RunAsync(Func<IServiceProvider, TextWriter, Task<int>> action, CancellationToken cancellationToken)
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
            services.AddSingleton<ILegacyWorkbookReader, ExcelLegacyWorkbookReader>();
            testServices?.Invoke(services);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using var scope = provider.CreateAsyncScope();
            try
            {
                return await action(scope.ServiceProvider, output);
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
