using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SixJars.Application.Ledger;
using SixJars.Application.Planning;
using SixJars.Application.Transactions;
using SixJars.Domain.Books;
using SixJars.Domain.Transactions;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

public class PlannedExpensesEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_and_list_by_month()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();

        var response = await client.PostAsJsonAsync(Url(book), InsuranceInput(book, 202602, -3000m, "年繳"), ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<PlannedExpenseDto>(ApiJson.Options, Ct))!;
        var list = await ListAsync(client, book, "?budgetMonth=202602");
        var item = list.Should().ContainSingle().Subject;
        item.Should().BeEquivalentTo(created);
        item.BudgetMonth.Should().Be(202602);
        item.CategoryId.Should().Be(book.FindCategory("固定支出", "保險費")!.Id.Value);
        item.EstimatedAmount.Should().Be(-3000m);
        item.Note.Should().Be("年繳");
        item.IsPaid.Should().BeFalse();
        item.PaidTransactionId.Should().BeNull();
    }

    [Fact]
    public async Task List_by_month_excludes_other_months_and_other_books()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        await CreateAsync(client, mine, InsuranceInput(mine, 202601, -1000m, "1 月"));
        var february = await CreateAsync(client, mine, InsuranceInput(mine, 202602, -2000m, "2 月"));
        await CreateAsync(client, mine, InsuranceInput(mine, 202603, -3000m, "3 月"));
        await CreateAsync(client, other, InsuranceInput(other, 202602, -9999m, "別人的 2 月"));

        var list = await ListAsync(client, mine, "?budgetMonth=202602");

        list.Select(p => p.Id).Should().Equal(february.Id);
    }

    [Fact]
    public async Task Create_with_floating_category_is_422()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await factory.CreateSignedInClient().PostAsJsonAsync(Url(book),
            new { budgetMonth = 202602, categoryId = book.FindCategory("主食", "午餐")!.Id.Value, estimatedAmount = -3000m },
            ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("rule");
    }

    [Fact]
    public async Task Update_with_stale_version_is_409()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var created = await CreateAsync(client, book, InsuranceInput(book, 202602, -3000m, "原本"));
        var url = $"{Url(book)}/{created.Id}";

        var first = await client.PutAsJsonAsync(url,
            new { version = created.Version, input = InsuranceInput(book, 202602, -3100m, "第一次修改") }, ApiJson.Options, Ct);
        var second = await client.PutAsJsonAsync(url,
            new { version = created.Version, input = InsuranceInput(book, 202602, -3200m, "第二次修改") }, ApiJson.Options, Ct);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await first.Content.ReadFromJsonAsync<PlannedExpenseDto>(ApiJson.Options, Ct))!;
        updated.EstimatedAmount.Should().Be(-3100m);
        updated.Version.Should().NotBe(created.Version);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var current = (await ListAsync(client, book, "")).Single();
        current.Note.Should().Be("第一次修改");
        current.Version.Should().Be(updated.Version);
    }

    [Fact]
    public async Task Location_of_created_planned_expense_can_be_read()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var response = await client.PostAsJsonAsync(Url(book), InsuranceInput(book, 202602, -3000m, "年繳"), ApiJson.Options, Ct);
        var created = (await response.Content.ReadFromJsonAsync<PlannedExpenseDto>(ApiJson.Options, Ct))!;

        var read = await client.GetAsync(response.Headers.Location, Ct);

        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await read.Content.ReadFromJsonAsync<PlannedExpenseDto>(ApiJson.Options, Ct)).Should().BeEquivalentTo(created);
    }

    [Fact]
    public async Task Get_planned_expense_of_another_book_is_404()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var theirs = await CreateAsync(client, other, InsuranceInput(other, 202602, -3000m, "別人的"));

        (await client.GetAsync($"{Url(mine)}/{theirs.Id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"{Url(mine)}/{Guid.NewGuid()}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_planned_expense_of_another_book_is_404_and_leaves_it_unchanged()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var theirs = await CreateAsync(client, other, InsuranceInput(other, 202602, -3000m, "別人的"));

        // 用自己帳本的路徑、自己帳本的分類，去改別本帳的預定支出：只用預定支出 Id 查詢就會改到。
        var response = await client.PutAsJsonAsync($"{Url(mine)}/{theirs.Id}",
            new { version = theirs.Version, input = InsuranceInput(mine, 202603, -1m, "被改掉了") }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListAsync(client, other, "")).Should().ContainSingle().Which.Should().BeEquivalentTo(theirs);
    }

    [Fact]
    public async Task Pay_fixed_expense_creates_expense_and_links_it()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var bank = book.FindAccount("國泰世華銀行")!.Id.Value;
        var planned = await CreateAsync(client, book, InsuranceInput(book, 202602, -3000m, "年繳"));

        var response = await client.PostAsJsonAsync($"{Url(book)}/{planned.Id}/pay",
            new { version = planned.Version, date = "2026-02-20", accountId = bank, amount = -2950m }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var paid = (await response.Content.ReadFromJsonAsync<PayPlannedExpenseResult>(ApiJson.Options, Ct))!;
        response.Headers.Location!.ToString().Should().Be($"/api/books/{book.Id.Value}/transactions/{paid.Transaction.Id}");
        var tx = await client.GetFromJsonAsync<TransactionDto>(
            $"/api/books/{book.Id.Value}/transactions/{paid.Transaction.Id}", ApiJson.Options, Ct);
        tx!.Kind.Should().Be(TransactionKind.Expense);
        tx.Amount.Should().Be(-2950m);
        tx.AccountId.Should().Be(bank);
        tx.CategoryId.Should().Be(planned.CategoryId);
        tx.BudgetMonth.Should().Be(202602);
        tx.Postings.Should().Equal(new PostingDto(bank, -2950m));
        var linked = (await ListAsync(client, book, "")).Single();
        linked.IsPaid.Should().BeTrue();
        linked.PaidTransactionId.Should().Be(tx.Id);
        linked.EstimatedAmount.Should().Be(-3000m, "預估金額保留原值，實際金額以交易為準");
    }

    [Fact]
    public async Task Pay_in_next_month_keeps_planned_budget_month()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var planned = await CreateAsync(client, book, InsuranceInput(book, 202602, -3000m, "2 月的保險費"));

        // 付款日期在 3 月，但交易的歸屬月份必須是預定支出的 2 月，而不是付款日期所在的月份。
        var paid = await PayAsync(client, book, planned,
            new { version = planned.Version, date = "2026-03-05", accountId = book.FindAccount("國泰世華銀行")!.Id.Value, amount = -3000m });

        paid.Transaction.Date.Should().Be(new DateOnly(2026, 3, 5));
        paid.Transaction.BudgetMonth.Should().Be(202602);
        var february = await client.GetFromJsonAsync<List<TransactionDto>>(
            $"/api/books/{book.Id.Value}/transactions?budgetMonth=202602", ApiJson.Options, Ct);
        february!.Select(t => t.Id).Should().Equal(paid.Transaction.Id);
    }

    [Fact]
    public async Task Pay_loan_creates_loan_payment_with_principal()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var bank = book.FindAccount("國泰世華銀行")!.Id.Value;
        var loan = book.FindAccount("房屋貸款")!.Id.Value;
        var planned = await CreateAsync(client, book, MortgageInput(book));

        var paid = await PayAsync(client, book, planned, new
        {
            version = planned.Version, date = "2026-02-10", accountId = bank, amount = -30000m,
            loanAccountId = loan, loanPrincipal = 20000m,
        });

        var tx = paid.Transaction;
        tx.Kind.Should().Be(TransactionKind.LoanPayment);
        tx.Amount.Should().Be(30000m);
        tx.LoanPrincipal.Should().Be(20000m);
        tx.LoanInterest.Should().Be(10000m);
        tx.AccountId.Should().Be(bank);
        tx.CounterAccountId.Should().Be(loan);
        tx.CategoryId.Should().Be(planned.CategoryId, "利息記在預定支出的分類（同 P1 制式表格匯入）");
        tx.BudgetMonth.Should().Be(202602);
        tx.Postings.Should().BeEquivalentTo([new PostingDto(bank, -30000m), new PostingDto(loan, 20000m)]);
        paid.PlannedExpense.PaidTransactionId.Should().Be(tx.Id);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Pay_loan_without_loan_account_or_principal_is_422(bool withLoanAccount, bool withPrincipal)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var planned = await CreateAsync(client, book, MortgageInput(book));

        var response = await client.PostAsJsonAsync($"{Url(book)}/{planned.Id}/pay", new
        {
            version = planned.Version, date = "2026-02-10", accountId = book.FindAccount("國泰世華銀行")!.Id.Value, amount = -30000m,
            loanAccountId = withLoanAccount ? book.FindAccount("房屋貸款")!.Id.Value : (Guid?)null,
            loanPrincipal = withPrincipal ? 20000m : (decimal?)null,
        }, ApiJson.Options, Ct);

        // 是否必填取決於分類的支出性質，要讀帳本才知道，屬於業務規則（422），不是輸入形狀（400）。
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("rule");
        await ShouldBeUnpaidWithoutTransactionsAsync(client, book);
    }

    [Fact]
    public async Task Pay_fixed_expense_with_loan_fields_is_422()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var planned = await CreateAsync(client, book, InsuranceInput(book, 202602, -3000m, "保險費"));

        // 非貸款性質用不到這兩個欄位；沿用交易輸入的慣例，送了沒有作用的欄位就拒絕，避免前端以為已經存檔。
        var response = await client.PostAsJsonAsync($"{Url(book)}/{planned.Id}/pay", new
        {
            version = planned.Version, date = "2026-02-10", accountId = book.FindAccount("國泰世華銀行")!.Id.Value, amount = -3000m,
            loanAccountId = book.FindAccount("房屋貸款")!.Id.Value, loanPrincipal = 1000m,
        }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await ShouldBeUnpaidWithoutTransactionsAsync(client, book);
    }

    [Fact]
    public async Task Pay_twice_is_422()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var bank = book.FindAccount("國泰世華銀行")!.Id.Value;
        var planned = await CreateAsync(client, book, InsuranceInput(book, 202602, -3000m, "保險費"));
        var first = await PayAsync(client, book, planned,
            new { version = planned.Version, date = "2026-02-20", accountId = bank, amount = -3000m });

        // 帶最新的版本，排除 409，確認擋下的是「已付款」這條規則。
        var second = await client.PostAsJsonAsync($"{Url(book)}/{planned.Id}/pay",
            new { version = first.PlannedExpense.Version, date = "2026-02-21", accountId = bank, amount = -3000m }, ApiJson.Options, Ct);

        second.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var transactions = await client.GetFromJsonAsync<List<TransactionDto>>($"/api/books/{book.Id.Value}/transactions", ApiJson.Options, Ct);
        transactions!.Select(t => t.Id).Should().Equal(first.Transaction.Id);
    }

    [Fact]
    public async Task Pay_planned_expense_of_another_book_is_404_and_creates_no_transaction()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var theirs = await CreateAsync(client, other, InsuranceInput(other, 202602, -3000m, "別人的"));

        // 用自己帳本的路徑、自己帳本的帳戶，去付別本帳的預定支出：只用預定支出 Id 查詢就會付到。
        var response = await client.PostAsJsonAsync($"{Url(mine)}/{theirs.Id}/pay",
            new { version = theirs.Version, date = "2026-02-20", accountId = mine.FindAccount("國泰世華銀行")!.Id.Value, amount = -3000m },
            ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await ShouldBeUnpaidWithoutTransactionsAsync(client, other);
        (await client.GetFromJsonAsync<List<TransactionDto>>($"/api/books/{mine.Id.Value}/transactions", ApiJson.Options, Ct))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Pay_returns_current_versions_of_transaction_and_planned_expense()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var bank = book.FindAccount("國泰世華銀行")!.Id.Value;
        var planned = await CreateAsync(client, book, InsuranceInput(book, 202602, -3000m, "保險費"));

        var paid = await PayAsync(client, book, planned,
            new { version = planned.Version, date = "2026-02-20", accountId = bank, amount = -3000m });

        // 回應的版本必須與重新讀取的一致，前端才能直接拿來修改。
        paid.PlannedExpense.Version.Should().NotBe(planned.Version);
        (await ListAsync(client, book, "")).Single().Version.Should().Be(paid.PlannedExpense.Version);
        var txUrl = $"/api/books/{book.Id.Value}/transactions/{paid.Transaction.Id}";
        (await client.GetFromJsonAsync<TransactionDto>(txUrl, ApiJson.Options, Ct))!.Version.Should().Be(paid.Transaction.Version);
        var edit = await client.PutAsJsonAsync(txUrl, new
        {
            version = paid.Transaction.Version,
            input = new
            {
                kind = "Expense", date = "2026-02-20", budgetMonth = 202602, amount = -3010m, accountId = bank,
                categoryId = planned.CategoryId,
            },
        }, ApiJson.Options, Ct);
        edit.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pay_with_stale_version_is_409_and_creates_no_transaction()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var planned = await CreateAsync(client, book, InsuranceInput(book, 202602, -3000m, "保險費"));
        // 另一台裝置先改了預估金額，版本因此前進
        (await client.PutAsJsonAsync($"{Url(book)}/{planned.Id}",
            new { version = planned.Version, input = InsuranceInput(book, 202602, -3500m, "保險費") }, ApiJson.Options, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.PostAsJsonAsync($"{Url(book)}/{planned.Id}/pay",
            new { version = planned.Version, date = "2026-02-20", accountId = book.FindAccount("國泰世華銀行")!.Id.Value, amount = -3000m },
            ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await ShouldBeUnpaidWithoutTransactionsAsync(client, book);
    }

    /// <summary>spec §9 O2：刪除付款交易時，預定支出自動回到未付，月可用餘額改回以預估金額計算。</summary>
    [Fact]
    public async Task Deleting_payment_transaction_reverts_plan_to_unpaid()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var planned = await CreateAsync(client, book, InsuranceInput(book, 202602, -3000m, "保險費"));
        // 實際金額與預估金額不同，才分辨得出月可用餘額是以哪一個計算。
        var paid = await PayAsync(client, book, planned,
            new { version = planned.Version, date = "2026-02-20", accountId = book.FindAccount("國泰世華銀行")!.Id.Value, amount = -2950m });
        (await SummaryAsync(client, book)).MonthlyDisposable.Should().Be(-2950m);

        var response = await client.DeleteAsync(
            $"/api/books/{book.Id.Value}/transactions/{paid.Transaction.Id}?version={paid.Transaction.Version}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var reverted = (await ListAsync(client, book, "")).Single();
        reverted.IsPaid.Should().BeFalse();
        reverted.PaidTransactionId.Should().BeNull();
        reverted.EstimatedAmount.Should().Be(-3000m);
        reverted.Version.Should().NotBe(paid.PlannedExpense.Version, "預定支出與交易在同一次 SaveChanges 寫入");
        (await client.GetFromJsonAsync<List<TransactionDto>>($"/api/books/{book.Id.Value}/transactions", ApiJson.Options, Ct))
            .Should().BeEmpty();
        (await SummaryAsync(client, book)).MonthlyDisposable.Should().Be(-3000m);
        // 回到未付之後可以再付一次。
        await PayAsync(client, book, reverted,
            new { version = reverted.Version, date = "2026-02-21", accountId = book.FindAccount("國泰世華銀行")!.Id.Value, amount = -2900m });
    }

    [Fact]
    public async Task Delete_planned_expense_is_204()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var unpaid = await CreateAsync(client, book, InsuranceInput(book, 202602, -3000m, "未付"));
        var toPay = await CreateAsync(client, book, InsuranceInput(book, 202602, -500m, "已付"));
        var paid = await PayAsync(client, book, toPay,
            new { version = toPay.Version, date = "2026-02-20", accountId = book.FindAccount("國泰世華銀行")!.Id.Value, amount = -450m });
        (await SummaryAsync(client, book)).MonthlyDisposable.Should().Be(-3450m);

        var deleteUnpaid = await client.DeleteAsync($"{Url(book)}/{unpaid.Id}?version={unpaid.Version}", Ct);
        // 已付款的也可以刪除：刪除的是計畫本身，付款交易保留。
        var deletePaid = await client.DeleteAsync($"{Url(book)}/{toPay.Id}?version={paid.PlannedExpense.Version}", Ct);

        deleteUnpaid.StatusCode.Should().Be(HttpStatusCode.NoContent);
        deletePaid.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListAsync(client, book, "")).Should().BeEmpty();
        (await client.GetAsync($"{Url(book)}/{unpaid.Id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetFromJsonAsync<List<TransactionDto>>($"/api/books/{book.Id.Value}/transactions", ApiJson.Options, Ct))!
            .Select(t => t.Id).Should().Equal(paid.Transaction.Id);
        (await SummaryAsync(client, book)).MonthlyDisposable.Should().Be(-450m);
        (await client.DeleteAsync($"{Url(book)}/{unpaid.Id}?version={unpaid.Version}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_planned_expense_of_another_book_is_404_and_leaves_it()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var theirs = await CreateAsync(client, other, InsuranceInput(other, 202602, -3000m, "別人的"));

        var response = await client.DeleteAsync($"{Url(mine)}/{theirs.Id}?version={theirs.Version}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListAsync(client, other, "")).Should().ContainSingle().Which.Should().BeEquivalentTo(theirs);
    }

    [Theory]
    [InlineData("planned-expenses")]
    [InlineData("transactions")]
    public async Task List_with_invalid_budget_month_is_400(string resource)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await factory.CreateSignedInClient().GetAsync($"/api/books/{book.Id.Value}/{resource}?budgetMonth=202613", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    internal static string Url(Book book) => $"/api/books/{book.Id.Value}/planned-expenses";

    private static object MortgageInput(Book book) => new
    {
        budgetMonth = 202602, estimatedAmount = -30000m, note = "房貸",
        categoryId = book.FindCategory("貸款支出", "房屋貸款")!.Id.Value, accountId = book.FindAccount("國泰世華銀行")!.Id.Value,
    };

    private static async Task<PayPlannedExpenseResult> PayAsync(HttpClient client, Book book, PlannedExpenseDto planned, object body)
    {
        var response = await client.PostAsJsonAsync($"{Url(book)}/{planned.Id}/pay", body, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<PayPlannedExpenseResult>(ApiJson.Options, Ct))!;
    }

    private static async Task<LedgerSummaryDto> SummaryAsync(HttpClient client, Book book) =>
        (await client.GetFromJsonAsync<LedgerSummaryDto>(
            $"/api/books/{book.Id.Value}/summary?budgetMonth=202602&asOf=2026-02-28", ApiJson.Options, Ct))!;

    private static async Task ShouldBeUnpaidWithoutTransactionsAsync(HttpClient client, Book book)
    {
        (await ListAsync(client, book, "")).Should().OnlyContain(p => !p.IsPaid && p.PaidTransactionId == null);
        (await client.GetFromJsonAsync<List<TransactionDto>>($"/api/books/{book.Id.Value}/transactions", ApiJson.Options, Ct))
            .Should().BeEmpty();
    }

    internal static object InsuranceInput(Book book, int budgetMonth, decimal amount, string note) => new
    {
        budgetMonth, estimatedAmount = amount, note,
        categoryId = book.FindCategory("固定支出", "保險費")!.Id.Value, accountId = book.FindAccount("國泰世華銀行")!.Id.Value,
    };

    internal static async Task<PlannedExpenseDto> CreateAsync(HttpClient client, Book book, object input)
    {
        var response = await client.PostAsJsonAsync(Url(book), input, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<PlannedExpenseDto>(ApiJson.Options, Ct))!;
    }

    internal static async Task<IReadOnlyList<PlannedExpenseDto>> ListAsync(HttpClient client, Book book, string query)
    {
        var response = await client.GetAsync($"{Url(book)}{query}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<PlannedExpenseDto>>(ApiJson.Options, Ct))!;
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
