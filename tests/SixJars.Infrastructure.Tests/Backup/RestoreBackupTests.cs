using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Api.Tests;
using SixJars.Application;
using SixJars.Application.Auditing;
using SixJars.Application.Backup;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Application.Members;
using SixJars.Application.Planning;
using SixJars.Application.Transactions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Backup;

/// <summary>
/// 備份還原（CLI <c>restore-backup</c>）：資料以 API 建立、以 API 匯出，還原到另一個空的資料庫後再匯出，兩份必須相同。
/// 還原一律經過 Domain 重新驗證，損毀的備份整個不寫入。
/// </summary>
public class RestoreBackupTests(PostgresFixture postgres)
{
    private const string CliSubject = "cli";
    private static readonly DateTimeOffset BuiltAt = new(2026, 2, 1, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RestoredAt = new(2026, 10, 4, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExportedAgainAt = new(2026, 10, 5, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly LockDate = new(2026, 1, 31);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Export_restore_export_round_trips()
    {
        var source = await Scenario.BuildAsync(postgres);
        await using var _ = source;
        var a = await source.ExportAsync();
        Scenario.ShouldCoverTrickyStates(a);
        var target = await postgres.CreateConnectionStringAsync(Ct);

        (await RestoreAsync(target, a)).Should().Be(a.Book.Id);

        await using var restored = new ApiFactory(target, Clock(ExportedAgainAt));
        var b = await ExportAsync(await restored.CreateMemberClientAsync(), a.Book.Id);
        // B 多了最後一筆 Restore 記錄，操作者是 CLI。
        var restore = b.AuditEntries[^1];
        restore.Action.Should().Be(AuditAction.Restore);
        restore.ActorSubject.Should().Be(CliSubject);
        restore.EntityType.Should().Be(AuditEntityTypes.Book);
        restore.EntityId.Should().Be(a.Book.Id);
        restore.At.Should().Be(RestoredAt);
        restore.Before.Should().BeNull();
        restore.After!.Value.GetProperty("fileName").GetString().Should().Be("backup.json");
        restore.After.Value.GetProperty("transactions").GetInt32().Should().Be(a.Transactions.Count);
        b.ExportedAt.Should().Be(ExportedAgainAt);

        // 排除 ExportedAt、xmin 版本與 B 的 Restore 記錄，其餘（含 Id、刪除時間、付款連結、稽核記錄原文）必須完全相同。
        Comparable(b with { AuditEntries = b.AuditEntries.SkipLast(1).ToList() }).Should().Be(Comparable(a));
    }

    [Fact]
    public async Task Version_1_backup_without_sort_order_is_restored()
    {
        var source = await Scenario.BuildAsync(postgres);
        await using var _ = source;
        var a = await source.ExportAsync();
        // 模擬 P3 的 v1 備份：沒有 sortOrder、archivedAt 欄位，清單順序是當時資料庫讀出的任意順序
        var json = JsonNode.Parse(BackupJson.SerializeToUtf8Bytes(a))!;
        json["formatVersion"] = 1;
        foreach (var list in new[] { "accounts", "planningFunds", "categories" })
        {
            foreach (var item in json["book"]![list]!.AsArray())
            {
                item!.AsObject().Remove("sortOrder");
                item.AsObject().Remove("archivedAt");
            }
        }

        var accounts = json["book"]!["accounts"]!.AsArray();
        var first = accounts[0]!;
        accounts.RemoveAt(0);
        accounts.Add(first);
        var v1 = json.Deserialize<BackupDocument>(BackupJson.Options)!;
        var target = await postgres.CreateConnectionStringAsync(Ct);

        (await RestoreAsync(target, v1)).Should().Be(a.Book.Id);
    }

    [Fact]
    public async Task Restores_v2_backup_without_recurring_items()
    {
        var source = await Scenario.BuildAsync(postgres);
        await using var _ = source;
        var a = await source.ExportAsync();
        // 模擬 P4 J 的 v2 備份：沒有週期項目，預定支出沒有 sourceId。
        var json = JsonNode.Parse(BackupJson.SerializeToUtf8Bytes(a))!;
        json["formatVersion"] = 2;
        json.AsObject().Remove("recurringPlannedExpenses");
        foreach (var item in json["plannedExpenses"]!.AsArray())
        {
            item!["plannedExpense"]!.AsObject().Remove("sourceId");
        }

        var v2 = json.Deserialize<BackupDocument>(BackupJson.Options)!;
        var target = await postgres.CreateConnectionStringAsync(Ct);

        (await RestoreAsync(target, v2)).Should().Be(a.Book.Id);
    }

    [Fact]
    public async Task Restores_v3_backup_without_budgets()
    {
        var source = await Scenario.BuildAsync(postgres);
        await using var _ = source;
        var a = await source.ExportAsync();
        // 模擬 P4 K 的 v3 備份：沒有預算。
        var json = JsonNode.Parse(BackupJson.SerializeToUtf8Bytes(a))!;
        json["formatVersion"] = 3;
        json.AsObject().Remove("budgets");

        var v3 = json.Deserialize<BackupDocument>(BackupJson.Options)!;
        var target = await postgres.CreateConnectionStringAsync(Ct);

        (await RestoreAsync(target, v3)).Should().Be(a.Book.Id);
    }

    [Fact]
    public async Task Restore_into_database_with_same_book_is_rejected()
    {
        var source = await Scenario.BuildAsync(postgres);
        await using var _ = source;
        var backup = await source.ExportAsync();

        var act = () => RestoreAsync(source.ConnectionString, backup);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Contain(backup.Book.Id.ToString());
        // 原本的資料原封不動，也沒有多出 Restore 記錄。
        var after = await source.ExportAsync();
        Comparable(after).Should().Be(Comparable(backup));
    }

    [Fact]
    public async Task Restored_book_has_same_summary()
    {
        var source = await Scenario.BuildAsync(postgres);
        await using var _ = source;
        var backup = await source.ExportAsync();
        var target = await postgres.CreateConnectionStringAsync(Ct);
        await RestoreAsync(target, backup);
        await using var restored = new ApiFactory(target, Clock(ExportedAgainAt));
        var restoredClient = await restored.CreateMemberClientAsync();

        foreach (var query in new[] { "budgetMonth=202601&asOf=2026-01-31", "budgetMonth=202602&asOf=2026-02-28" })
        {
            var url = $"/api/books/{backup.Book.Id}/summary?{query}";
            var expected = await source.Client.GetStringAsync(url, Ct);
            var actual = await restoredClient.GetStringAsync(url, Ct);
            actual.Should().Be(expected);
        }
    }

    /// <summary>
    /// 被手動改壞的備份：Domain 重新驗證（或格式檢查）時失敗，整本都不寫入。
    /// 每個案例都是 API 建不出來的狀態，只能從檔案進來。
    /// </summary>
    [Theory]
    [InlineData("format-version")]
    [InlineData("unknown-account")]
    [InlineData("missing-paid-transaction")]
    [InlineData("unpaid-plan-linked-to-deleted-transaction")]
    [InlineData("transaction-paid-twice")]
    [InlineData("paid-flag-without-link")]
    [InlineData("transaction-normalized-by-domain")]
    [InlineData("sub-category-kind-mismatch")]
    [InlineData("sort-order-gap")]
    [InlineData("planned-expense-unknown-source")]
    [InlineData("budget-on-non-floating-category")]
    [InlineData("empty-budget")]
    public async Task Corrupted_backup_writes_nothing(string corruption)
    {
        var source = await Scenario.BuildAsync(postgres);
        await using var _ = source;
        var backup = Corrupt(await source.ExportAsync(), corruption);
        var target = await postgres.CreateConnectionStringAsync(Ct);

        var act = () => RestoreAsync(target, backup);

        if (corruption == "format-version")
        {
            (await act.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain("1");
        }
        else
        {
            await act.Should().ThrowAsync<DomainException>();
        }

        await ShouldBeEmptyAsync(target);
    }

    /// <summary>寫入分成多次 SaveChanges；最後一次失敗時，整個 DB transaction 回滾，前面寫入的帳本與交易也不留下。</summary>
    [Fact]
    public async Task Failure_midway_leaves_nothing_behind()
    {
        var source = await Scenario.BuildAsync(postgres);
        await using var _ = source;
        var backup = await source.ExportAsync();
        var target = await postgres.CreateConnectionStringAsync(Ct);

        var act = () => RestoreAsync(target, backup, new FailOnThirdSave());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(FailOnThirdSave.Message);
        await ShouldBeEmptyAsync(target);
    }

    private static BackupDocument Corrupt(BackupDocument backup, string corruption)
    {
        var plans = backup.PlannedExpenses.ToList();
        var transactions = backup.Transactions.ToList();
        var paidIndex = plans.FindIndex(p => p.PlannedExpense.PaidTransactionId is not null && p.DeletedAt is null);
        switch (corruption)
        {
            case "format-version":
                return backup with { FormatVersion = BackupDocument.CurrentFormatVersion + 1 };
            case "sort-order-gap":
            {
                // D5：同一組的 SortOrder 一定連續；有空號表示檔案被改過，還原會重新編號而與備份不同。
                var accounts = backup.Book.Accounts.ToList();
                accounts[^1] = accounts[^1] with { SortOrder = accounts[^1].SortOrder + 1 };
                return backup with { Book = backup.Book with { Accounts = accounts } };
            }
            case "budget-on-non-floating-category":
            {
                // 預算只限浮動主分類（CategoryBudget.Create）：只可能是檔案被改過。
                var budgets = backup.Budgets!.ToList();
                var fixedMain = backup.Book.Categories.Single(c => c.Name == "固定支出" && c.ParentId == null).Id;
                budgets[0] = budgets[0] with { CategoryId = fixedMain };
                return backup with { Budgets = budgets };
            }
            case "empty-budget":
            {
                // 清到全空就整筆刪除（P4 L plan Q1c），API 不會留下空的預算。
                var budgets = backup.Budgets!.ToList();
                budgets[0] = budgets[0] with { DefaultAmount = null, Overrides = [] };
                return backup with { Budgets = budgets };
            }
            case "planned-expense-unknown-source":
                // 預定支出的來源週期項目不在備份中：API 不會產生沒有來源的參照（FK），只可能是檔案被改過。
                plans[0] = plans[0] with { PlannedExpense = plans[0].PlannedExpense with { SourceId = Guid.NewGuid() } };
                return backup with { PlannedExpenses = plans };
            case "unknown-account":
                transactions[0] = transactions[0] with { Transaction = transactions[0].Transaction with { AccountId = Guid.NewGuid() } };
                return backup with { Transactions = transactions };
            case "missing-paid-transaction":
                plans[paidIndex] = plans[paidIndex] with { PlannedExpense = plans[paidIndex].PlannedExpense with { PaidTransactionId = Guid.NewGuid() } };
                return backup with { PlannedExpenses = plans };
            case "unpaid-plan-linked-to-deleted-transaction":
            {
                // 未刪除的預定支出連結到已刪除的交易：API 刪除付款交易時一定會解除連結（O2），所以只可能是檔案被改過。
                var paymentId = plans[paidIndex].PlannedExpense.PaidTransactionId;
                var index = transactions.FindIndex(t => t.Transaction.Id == paymentId);
                transactions[index] = transactions[index] with { DeletedAt = RestoredAt };
                return backup with { Transactions = transactions };
            }
            case "transaction-paid-twice":
            {
                // 付款一律新增一筆交易，所以一筆交易不會是兩筆預定支出的付款。
                var unpaid = plans.FindIndex(p => p.PlannedExpense.PaidTransactionId is null && p.DeletedAt is null);
                plans[unpaid] = plans[unpaid] with
                {
                    PlannedExpense = plans[unpaid].PlannedExpense with
                    {
                        PaidTransactionId = plans[paidIndex].PlannedExpense.PaidTransactionId, IsPaid = true,
                    },
                };
                return backup with { PlannedExpenses = plans };
            }
            case "paid-flag-without-link":
            {
                // IsPaid 由付款連結推導；兩者不一致就是被改過。
                var unpaid = plans.FindIndex(p => p.PlannedExpense.PaidTransactionId is null && p.DeletedAt is null);
                plans[unpaid] = plans[unpaid] with { PlannedExpense = plans[unpaid].PlannedExpense with { IsPaid = true } };
                return backup with { PlannedExpenses = plans };
            }
            case "transaction-normalized-by-domain":
            {
                // 同帳戶圈存：factory 會把與轉入帳戶相同的轉出帳戶改成 null；備份與 Domain 重建的結果不同就拒絕，不默默改資料。
                var index = transactions.FindIndex(t => t.Transaction.Kind == TransactionKind.FundAllocation && t.Transaction.CounterAccountId is null);
                transactions[index] = transactions[index] with
                {
                    Transaction = transactions[index].Transaction with { CounterAccountId = transactions[index].Transaction.AccountId },
                };
                return backup with { Transactions = transactions };
            }
            case "sub-category-kind-mismatch":
            {
                // 子分類的種類與性質沿用主分類；備份寫成別的值就是被改過。
                var categories = backup.Book.Categories.ToList();
                var index = categories.FindIndex(c => c.ParentId is not null && c.Kind == CategoryKind.Expense);
                categories[index] = categories[index] with { Kind = CategoryKind.Income, Nature = null };
                return backup with { Book = backup.Book with { Categories = categories } };
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption), corruption, null);
        }
    }

    /// <summary>可比對的 JSON：排除 ExportedAt 與交易、預定支出、週期項目的 xmin 版本（不同資料庫必然不同）。</summary>
    private static string Comparable(BackupDocument backup)
    {
        var node = JsonSerializer.SerializeToNode(backup, BackupJson.Options)!.AsObject();
        node.Remove("exportedAt");
        foreach (var item in node["transactions"]!.AsArray())
        {
            item!["transaction"]!.AsObject().Remove("version");
        }

        foreach (var item in node["plannedExpenses"]!.AsArray())
        {
            item!["plannedExpense"]!.AsObject().Remove("version");
        }

        if (node["recurringPlannedExpenses"] is JsonArray recurring)
        {
            foreach (var item in recurring)
            {
                item!.AsObject().Remove("version");
            }
        }

        return node.ToJsonString(BackupJson.Options);
    }

    private static async Task<Guid> RestoreAsync(string connectionString, BackupDocument backup, IInterceptor? interceptor = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSixJarsApplication(mediatRLicenseKey: null);
        services.AddSixJarsInfrastructure(connectionString);
        services.AddSingleton<ICurrentUser>(new FixedUser(CliSubject));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(RestoredAt));
        if (interceptor is not null)
        {
            services.AddSingleton(interceptor);
        }

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RestoreBackup(backup, "backup.json"), Ct);
    }

    private static async Task<BackupDocument> ExportAsync(HttpClient client, Guid bookId)
    {
        var response = await client.GetAsync($"/api/books/{bookId}/export/backup.json", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<BackupDocument>(BackupJson.Options, Ct))!;
    }

    private static async Task ShouldBeEmptyAsync(string connectionString)
    {
        await using var db = Open(connectionString);
        (await db.Books.CountAsync(Ct)).Should().Be(0);
        (await db.Transactions.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
        (await db.PlannedExpenses.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
        (await db.RecurringPlannedExpenses.CountAsync(Ct)).Should().Be(0);
        (await db.CategoryBudgets.CountAsync(Ct)).Should().Be(0);
        (await db.BookMembers.CountAsync(Ct)).Should().Be(0);
        (await db.AuditEntries.CountAsync(Ct)).Should().Be(0);
    }

    private static SixJarsDbContext Open(string connectionString) =>
        new(new DbContextOptionsBuilder<SixJarsDbContext>().UseNpgsql(connectionString).Options);

    private static Action<IServiceCollection> Clock(DateTimeOffset now) =>
        services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));

    private sealed class FixedUser(string subject) : ICurrentUser
    {
        public string Subject => subject;
        public string? Email => null;
    }

    /// <summary>第三次（最後一次：成員與稽核記錄）SaveChanges 前擲出例外，模擬寫到一半失敗。</summary>
    private sealed class FailOnThirdSave : SaveChangesInterceptor
    {
        public const string Message = "模擬還原中途失敗";
        private int _saves;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            ++_saves == 3 ? throw new InvalidOperationException(Message) : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// 來源資料庫：全部經由 API 建立（成員除外），涵蓋各種交易類型與 API 走得到的特殊狀態；
    /// 同一個資料庫裡還有另一本帳，確認不會混進備份。
    /// </summary>
    private sealed class Scenario : IAsyncDisposable
    {
        private const string UnboundEmail = "spouse@example.com";

        private Scenario(ApiFactory factory, string connectionString, HttpClient client, Guid bookId)
        {
            Factory = factory;
            ConnectionString = connectionString;
            Client = client;
            BookId = bookId;
        }

        public ApiFactory Factory { get; }
        public string ConnectionString { get; }
        public HttpClient Client { get; }
        public Guid BookId { get; }

        public static async Task<Scenario> BuildAsync(PostgresFixture postgres)
        {
            var connectionString = await postgres.CreateConnectionStringAsync(Ct);
            var factory = new ApiFactory(connectionString, Clock(BuiltAt));
            var book = await factory.SeedBookAsync(Ct);
            var other = await factory.SeedBookAsync(Ct);
            var client = await factory.CreateMemberClientAsync();
            var scenario = new Scenario(factory, connectionString, client, book.Id.Value);
            await scenario.FillAsync(book);
            await scenario.ArchiveAndReorderAsync();
            await scenario.Post(other.Id.Value, "transactions", Input(TransactionKind.Expense, "2026-01-06", -77m, other.FindAccount("現金")!,
                category: other.FindCategory("主食")!.Id.Value));

            // (g) 未綁定的成員：以 CLI 的 add-member 加入（API 沒有加入成員的 endpoint）。
            await using (var provider = CliServices(connectionString))
            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new AddOwner(book.Id.Value, UnboundEmail), Ct);
            }

            return scenario;
        }

        public Task<BackupDocument> ExportAsync() => RestoreBackupTests.ExportAsync(Client, BookId);

        /// <summary>防止情境本身退化（例如匯出漏掉已刪除的資料，A 與 B 就會一起少掉而照樣相等）。</summary>
        public static void ShouldCoverTrickyStates(BackupDocument a)
        {
            var transactions = a.Transactions.ToDictionary(t => t.Transaction.Id);
            // (a) 已刪除的交易：一般刪除 1 筆、付款後刪除 2 筆。
            a.Transactions.Count(t => t.DeletedAt is not null).Should().Be(3);
            a.Transactions.Select(t => t.Transaction.Kind).Distinct().Should().HaveCount(12, "涵蓋全部 12 種交易類型");
            // (b) 付款交易被刪除後回到未付。
            a.PlannedExpenses.Should().Contain(p => p.PlannedExpense.Note == "付款被刪除" && !p.PlannedExpense.IsPaid && p.DeletedAt == null);
            // (c) 已刪除、已付款，付款交易仍在。
            a.PlannedExpenses.Should().Contain(p => p.PlannedExpense.Note == "付款後刪除計畫" && p.DeletedAt != null
                && transactions[p.PlannedExpense.PaidTransactionId!.Value].DeletedAt == null);
            // (d) 已刪除、已付款，付款交易之後也被刪除：連結保留到已刪除的交易。
            a.PlannedExpenses.Should().Contain(p => p.PlannedExpense.Note == "計畫與付款都刪除" && p.DeletedAt != null
                && transactions[p.PlannedExpense.PaidTransactionId!.Value].DeletedAt != null);
            a.PlannedExpenses.Should().Contain(p => p.PlannedExpense.Note == "貸款" && p.PlannedExpense.IsPaid);
            a.PlannedExpenses.Should().Contain(p => p.PlannedExpense.Note == "未付後刪除" && p.DeletedAt != null && !p.PlannedExpense.IsPaid);
            // (e) 鎖帳日，而且之前有交易。
            a.Book.LockDate.Should().Be(LockDate);
            a.Transactions.Should().Contain(t => t.Transaction.Date <= LockDate && t.DeletedAt == null);
            // (f) 子分類，含經由 API 新增的。
            a.Book.Categories.Should().Contain(c => c.Name == "計程車" && c.ParentId != null);
            // (g) 已綁定與未綁定的成員。
            a.Members.Should().HaveCount(2);
            a.Members.Should().Contain(m => m.GoogleSubject == ApiFactory.DefaultSubject);
            a.Members.Should().Contain(m => m.Email == UnboundEmail && m.GoogleSubject == null);
            // 稽核記錄：修改（有 before 與 after）、鎖帳日、CLI 加入成員都在。
            a.AuditEntries.Should().Contain(e => e.Action == AuditAction.Update && e.EntityType == AuditEntityTypes.Transaction);
            a.AuditEntries.Should().Contain(e => e.Action == AuditAction.LockDateChanged);
            a.AuditEntries.Should().Contain(e => e.EntityType == AuditEntityTypes.BookMember && e.ActorSubject == CliSubject);
            // (h) 封存的帳戶與子分類，且帳戶順序與建立順序（UUIDv7 遞增）不同。
            a.Book.Accounts.Should().Contain(x => x.Name == "舊存摺" && x.ArchivedAt != null);
            a.Book.Categories.Should().Contain(x => x.Name == "計程車" && x.ArchivedAt != null);
            a.Book.Accounts.Select(x => x.Id).Should().NotBeInAscendingOrder();
            // (i) 週期項目：產生後刪除與未刪除的預定支出都帶著來源；另有已設結束月份的每月項目。
            var recurring = a.RecurringPlannedExpenses!;
            recurring.Should().HaveCount(2);
            recurring.Should().Contain(r => r.Frequency == RecurrenceFrequency.Monthly && r.EndMonth != null);
            var yearly = recurring.Single(r => r.Frequency == RecurrenceFrequency.Yearly).Id;
            a.PlannedExpenses.Should().Contain(p => p.PlannedExpense.SourceId == yearly && p.PlannedExpense.BudgetMonth == 202601 && p.DeletedAt != null);
            a.PlannedExpenses.Should().Contain(p => p.PlannedExpense.SourceId == yearly && p.PlannedExpense.BudgetMonth == 202607 && p.DeletedAt == null);
            // (j) 預算：有預設值＋覆寫值的、只有覆寫值的。
            a.Budgets!.Should().HaveCount(2);
            a.Budgets.Should().Contain(b => b.DefaultAmount == null && b.Overrides.Count == 1);
        }

        /// <summary>(h) 新增一個餘額為 0 的帳戶並封存、封存一個子分類，再把帳戶順序整個倒過來。</summary>
        private async Task ArchiveAndReorderAsync()
        {
            var oldPassbook = await PostForId("accounts", new { name = "舊存摺", type = "Bank", openingBalance = 0m });
            (await Client.PostAsync($"/api/books/{BookId}/accounts/{oldPassbook}/archive", null, Ct)).EnsureSuccessStatusCode();
            var current = (await Client.GetFromJsonAsync<BookDto>($"/api/books/{BookId}", ApiJson.Options, Ct))!;
            var taxi = current.Categories.Single(c => c.Name == "計程車").Id;
            (await Client.PostAsync($"/api/books/{BookId}/categories/{taxi}/archive", null, Ct)).EnsureSuccessStatusCode();
            (await Client.PutAsJsonAsync($"/api/books/{BookId}/accounts/order",
                new { ids = current.Accounts.Select(a => a.Id).Reverse() }, ApiJson.Options, Ct)).EnsureSuccessStatusCode();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }

        private async Task FillAsync(Book book)
        {
            var cash = book.FindAccount("現金")!;
            var bank = book.FindAccount("國泰世華銀行")!;
            var card = book.FindAccount("國泰Combo卡")!;
            var wallet = book.FindAccount("悠遊卡")!;
            var loan = book.FindAccount("房屋貸款")!;
            var freedom = book.FindPlanningFund("財務自由帳戶")!.Id.Value;
            var salary = book.FindCategory("工作薪資")!.Id.Value;
            var food = book.FindCategory("主食")!.Id.Value;
            var lunch = book.FindCategory("主食", "午餐")!.Id.Value;
            var insurance = book.FindCategory("固定支出", "保險費")!.Id.Value;
            var fixedExpense = book.FindCategory("固定支出")!.Id.Value;
            var mortgage = book.FindCategory("貸款支出", "房屋貸款")!.Id.Value;

            // 帳本設定也經由 API 新增：帳戶、財務規劃帳戶、主分類與子分類。
            var postOffice = await PostForId("accounts", new { name = "郵局", type = "Bank", openingBalance = 1234.5m });
            var travel = await PostForId("planning-funds", new { name = "旅遊基金", openingBalance = 0m });
            var transport = await PostForId("categories", new { name = "交通", kind = "Expense", nature = "Floating" });
            await PostForId("categories", new { name = "計程車", kind = "Expense", parentId = transport });

            // 12 種交易類型；一筆刷卡的歸屬月份與日期不同月。
            await Tx(Input(TransactionKind.LoanDisbursement, "2026-01-02", 100000m, bank, counter: loan.Id.Value));
            await Tx(Input(TransactionKind.Income, "2026-01-05", 84000m, bank, category: salary));
            var lunchTx = await Tx(Input(TransactionKind.Expense, "2026-01-06", -120m, cash, category: lunch, note: "午餐"));
            await Tx(Input(TransactionKind.Expense, "2026-01-31", -500m, card, category: food, budgetMonth: 202602));
            await Tx(Input(TransactionKind.Withdrawal, "2026-01-07", 3000m, bank, counter: cash.Id.Value));
            await Tx(Input(TransactionKind.CashDeposit, "2026-01-08", 1000m, bank, counter: cash.Id.Value));
            await Tx(Input(TransactionKind.Transfer, "2026-01-08", 5000m, bank, counter: postOffice));
            await Tx(Input(TransactionKind.CardPayment, "2026-01-10", 2000m, bank, counter: card.Id.Value));
            await Tx(Input(TransactionKind.TopUp, "2026-01-11", 500m, wallet, counter: cash.Id.Value));
            await Tx(Input(TransactionKind.LoanPayment, "2026-01-12", 20000m, bank, counter: loan.Id.Value, principal: 15000m, category: mortgage));
            await Tx(Input(TransactionKind.FundAllocation, "2026-01-05", 10000m, bank, fund: freedom));
            await Tx(Input(TransactionKind.FundAllocation, "2026-01-12", 1000m, bank, counter: cash.Id.Value, fund: travel));
            await Tx(Input(TransactionKind.FundWithdrawal, "2026-01-15", 2000m, bank, fund: freedom, category: transport));
            await Tx(Input(TransactionKind.FundReturn, "2026-01-20", 300m, bank, fund: freedom));
            // 鎖帳日之後的日期、歸屬月份仍是 1 月（使用者確認允許）。
            await Tx(Input(TransactionKind.Expense, "2026-02-03", -60m, cash, category: lunch, budgetMonth: 202601));

            // 修改一筆（稽核記錄有 before 與 after）。
            var updated = await Client.PutAsJsonAsync(TransactionsUrl(lunchTx.Id),
                new { version = lunchTx.Version, input = Input(TransactionKind.Expense, "2026-01-06", -150m, cash, category: lunch, note: "午餐加飲料") },
                ApiJson.Options, Ct);
            updated.StatusCode.Should().Be(HttpStatusCode.OK);

            // (a) 一般的已刪除交易。
            var mistake = await Tx(Input(TransactionKind.Expense, "2026-01-09", -999m, cash, category: lunch, note: "記錯"));
            await DeleteAsync(TransactionsUrl(mistake.Id), mistake.Version);

            // 預定支出：付款後保留連結、(b)、(c)、(d)、貸款付款、未付、未付後刪除。
            var stillPaid = await Plan(202601, insurance, -3000m, "已付款");
            await Pay(stillPaid, "2026-01-16", bank, -3000m);
            var unpaidAgain = await Plan(202602, insurance, -2000m, "付款被刪除");
            var unpaidAgainPayment = await Pay(unpaidAgain, "2026-01-17", cash, -2000m);
            await DeleteAsync(TransactionsUrl(unpaidAgainPayment.Transaction.Id), unpaidAgainPayment.Transaction.Version);
            var deletedPlan = await Plan(202601, fixedExpense, -1500m, "付款後刪除計畫");
            var deletedPlanPayment = await Pay(deletedPlan, "2026-01-18", bank, -1500m);
            await DeleteAsync(PlansUrl(deletedPlan.Id), deletedPlanPayment.PlannedExpense.Version);
            var bothDeleted = await Plan(202601, fixedExpense, -800m, "計畫與付款都刪除");
            var bothDeletedPayment = await Pay(bothDeleted, "2026-01-19", bank, -800m);
            await DeleteAsync(PlansUrl(bothDeleted.Id), bothDeletedPayment.PlannedExpense.Version);
            await DeleteAsync(TransactionsUrl(bothDeletedPayment.Transaction.Id), bothDeletedPayment.Transaction.Version);
            var loanPlan = await Plan(202601, mortgage, -20000m, "貸款");
            await Pay(loanPlan, "2026-01-25", bank, -20000m, loan.Id.Value, 15000m);
            await Plan(202602, fixedExpense, -999m, "未付");
            var cancelled = await Plan(202602, fixedExpense, -100m, "未付後刪除");
            await DeleteAsync(PlansUrl(cancelled.Id), cancelled.Version);

            // (i) 週期項目：每年 1、7 月的項目在 1 月產生後刪除、7 月產生後保留；已設結束月份的每月項目（1 月也會產生）。
            var yearly = await PostForId("recurring-planned-expenses", new RecurringPlannedExpenseInput(
                insurance, null, -3000m, "年繳保費", RecurrenceFrequency.Yearly, [1, 7], 202601, null));
            await PostForId("recurring-planned-expenses", new RecurringPlannedExpenseInput(
                fixedExpense, bank.Id.Value, -500m, "月租", RecurrenceFrequency.Monthly, [], 202601, 202603));
            var generatedYearly = (await Generate(202601)).Created.Single(p => p.SourceId == yearly);
            await DeleteAsync(PlansUrl(generatedYearly.Id), generatedYearly.Version);
            await Generate(202607);

            // (j) 預算：主食有預設值與 2 月覆寫值；交通只有 1 月覆寫值（P4 L plan Q1a 的形狀）。
            await PutBudget($"{food}/default", 6000m);
            await PutBudget($"{food}/overrides/202602", 8000m);
            await PutBudget($"{transport}/overrides/202601", 1500m);

            // (e) 最後才鎖帳：之前的寫入都在開放期間。
            var locked = await Client.PutAsJsonAsync($"{BookUrl}/lock-date", new { lockDate = LockDate }, ApiJson.Options, Ct);
            locked.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        private string BookUrl => $"/api/books/{BookId}";

        private string TransactionsUrl(Guid id) => $"{BookUrl}/transactions/{id}";

        private string PlansUrl(Guid id) => $"{BookUrl}/planned-expenses/{id}";

        private async Task PutBudget(string path, decimal amount)
        {
            var response = await Client.PutAsJsonAsync($"{BookUrl}/budgets/{path}", new { amount }, ApiJson.Options, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        }

        private async Task<Guid> PostForId(string resource, object body)
        {
            var response = await Client.PostAsJsonAsync($"{BookUrl}/{resource}", body, ApiJson.Options, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
            return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        }

        private Task<TransactionDto> Tx(TransactionInput input) => Post(BookId, "transactions", input);

        private async Task<TransactionDto> Post(Guid bookId, string resource, TransactionInput input)
        {
            var response = await Client.PostAsJsonAsync($"/api/books/{bookId}/{resource}", input, ApiJson.Options, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
            return (await response.Content.ReadFromJsonAsync<TransactionDto>(ApiJson.Options, Ct))!;
        }

        private async Task<PlannedExpenseDto> Plan(int budgetMonth, Guid categoryId, decimal amount, string note)
        {
            var response = await Client.PostAsJsonAsync($"{BookUrl}/planned-expenses",
                new PlannedExpenseInput(budgetMonth, categoryId, null, amount, note), ApiJson.Options, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
            return (await response.Content.ReadFromJsonAsync<PlannedExpenseDto>(ApiJson.Options, Ct))!;
        }

        private async Task<GeneratePlannedExpensesResult> Generate(int budgetMonth)
        {
            var response = await Client.PostAsync($"{BookUrl}/planned-expenses/generate?budgetMonth={budgetMonth}", null, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
            return (await response.Content.ReadFromJsonAsync<GeneratePlannedExpensesResult>(ApiJson.Options, Ct))!;
        }

        private async Task<PayPlannedExpenseResult> Pay(
            PlannedExpenseDto planned, string date, Account payer, decimal amount, Guid? loanAccountId = null, decimal? loanPrincipal = null)
        {
            var response = await Client.PostAsJsonAsync($"{PlansUrl(planned.Id)}/pay",
                new { version = planned.Version, date, accountId = payer.Id.Value, amount, loanAccountId, loanPrincipal }, ApiJson.Options, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
            return (await response.Content.ReadFromJsonAsync<PayPlannedExpenseResult>(ApiJson.Options, Ct))!;
        }

        private async Task DeleteAsync(string url, uint version) =>
            (await Client.DeleteAsync($"{url}?version={version}", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        private static TransactionInput Input(
            TransactionKind kind, string date, decimal amount, Account account, Guid? counter = null, Guid? category = null,
            Guid? fund = null, decimal? principal = null, int? budgetMonth = null, string? note = null) =>
            new(kind, DateOnly.Parse(date), budgetMonth, amount, account.Id.Value, counter, category, fund, principal, note);

        private static ServiceProvider CliServices(string connectionString)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSixJarsApplication(mediatRLicenseKey: null);
            services.AddSixJarsInfrastructure(connectionString);
            services.AddSingleton<ICurrentUser>(new FixedUser(CliSubject));
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(BuiltAt));
            return services.BuildServiceProvider();
        }
    }
}
