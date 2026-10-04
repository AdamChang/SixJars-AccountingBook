using System.Net;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Books;
using SixJars.Domain.Books;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

/// <summary>
/// 交易明細匯出（spec §8.3、CONTEXT.md「匯出」）：CSV 與 xlsx，只含未刪除的交易，不可還原。
/// 名稱由帳本設定解析，交易類型顯示中文；文字欄位不讓 Excel 當成公式執行（CSV／formula injection）。
/// </summary>
public class TransactionExportTests(PostgresFixture postgres)
{
    /// <summary>UTC 10/4 17:30 是台北 10/5 01:30：檔名的日期必須是台北的日期。</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 17, 30, 0, TimeSpan.Zero);

    private static readonly string[] Header = ["日期", "歸屬月份", "交易類型", "帳戶", "對方帳戶", "主分類", "子分類", "財務規劃帳戶", "金額", "本金", "利息", "備註"];

    /// <summary>備註含逗號、雙引號與換行：RFC 4180 要整欄以雙引號包住，雙引號寫兩次。</summary>
    private const string TrickyNote = "午餐, \"大碗\"\n加蛋";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Csv_has_bom_header_and_rows_excluding_deleted()
    {
        await using var factory = await CreateFactoryAsync();
        var (book, client) = await SeedLedgerAsync(factory);

        var response = await client.GetAsync($"/api/books/{book.Id.Value}/export/transactions.csv", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be("sixjars-transactions-20261005.csv");
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        text.Should().EndWith("\r\n", "RFC 4180：每一列以 CRLF 結尾");

        // 依日期排序；已刪除的那筆（1/15）不出現；金額與日期不受文化設定影響。
        ParseCsv(text).Should().BeEquivalentTo(
            [
                Header,
                ["2026-01-05", "202601", "收入", "國泰世華銀行", "", "工作薪資", "", "", "50000", "", "", ""],
                ["2026-01-10", "202601", "入新資金", "國泰世華銀行", "", "", "", "財務自由帳戶", "1000", "", "", ""],
                ["2026-01-20", "202601", "支出", "現金", "", "主食", "午餐", "", "-120.5", "", "", TrickyNote],
                ["2026-02-05", "202602", "貸款繳款", "國泰世華銀行", "房屋貸款", "貸款支出", "房屋貸款", "", "20000", "15000", "5000", ""],
            ],
            o => o.WithStrictOrdering());
        text.Should().Contain("\"午餐, \"\"大碗\"\"\n加蛋\"").And.NotContain("刪掉的");
    }

    [Fact]
    public async Task Csv_respects_date_range()
    {
        await using var factory = await CreateFactoryAsync();
        var (book, client) = await SeedLedgerAsync(factory);

        var text = await client.GetStringAsync($"/api/books/{book.Id.Value}/export/transactions.csv?from=2026-01-10&to=2026-01-20", Ct);

        // 兩端都含；1/15 那筆雖在範圍內，但已刪除。
        ParseCsv(text.TrimStart('﻿')).Skip(1).Select(row => row[0]).Should().Equal("2026-01-10", "2026-01-20");
    }

    [Theory]
    [InlineData("transactions.csv?from=2026-02-01&to=2026-01-31")]
    [InlineData("transactions.xlsx?from=not-a-date")]
    public async Task Invalid_date_range_is_400(string query)
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();

        var response = await client.GetAsync($"/api/books/{book.Id.Value}/export/{query}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>用 ClosedXML 讀回（spike S4）：日期與金額是真正的日期、數字，不是文字，Excel 才能直接加總、篩選。</summary>
    [Fact]
    public async Task Xlsx_round_trips_through_closedxml()
    {
        await using var factory = await CreateFactoryAsync();
        var (book, client) = await SeedLedgerAsync(factory);

        var response = await client.GetAsync($"/api/books/{book.Id.Value}/export/transactions.xlsx?from=2026-01-20", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be("sixjars-transactions-20261005.xlsx");
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Ct)));
        var sheet = workbook.Worksheet("交易");
        sheet.Row(1).Cells(1, Header.Length).Select(c => c.GetString()).Should().Equal(Header);

        sheet.Cell("A2").DataType.Should().Be(XLDataType.DateTime);
        sheet.Cell("A2").GetDateTime().Should().Be(new DateTime(2026, 1, 20));
        sheet.Cell("B2").GetValue<int>().Should().Be(202601);
        sheet.Cell("C2").GetString().Should().Be("支出");
        sheet.Cell("I2").DataType.Should().Be(XLDataType.Number);
        sheet.Cell("I2").GetValue<decimal>().Should().Be(-120.5m);
        sheet.Cell("J2").IsEmpty().Should().BeTrue("不是貸款繳款就沒有本金");
        sheet.Cell("L2").GetString().Should().Be(TrickyNote);

        sheet.Cell("C3").GetString().Should().Be("貸款繳款");
        sheet.Cell("J3").GetValue<decimal>().Should().Be(15000m);
        sheet.Cell("K3").GetValue<decimal>().Should().Be(5000m);
        sheet.LastRowUsed()!.RowNumber().Should().Be(3);
    }

    /// <summary>
    /// 以 <c>= + - @</c>、tab 或 CR 開頭的文字，Excel 會當成公式（CSV／formula injection，OWASP）：
    /// CSV 前面加上 <c>'</c>；xlsx 寫成文字並設定 quote prefix，內容不變。金額仍是數字，負號不跳脫。
    /// </summary>
    [Fact]
    public async Task Formula_like_text_is_neutralised_in_csv_and_xlsx()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        (await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/accounts",
            new { name = "@SUM(1+1)", type = "Cash", openingBalance = 0m }, ApiJson.Options, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = (await client.GetFromJsonAsync<BookDto>($"/api/books/{book.Id.Value}", ApiJson.Options, Ct))!;
        var evilAccount = dto.Accounts.Single(a => a.Name == "@SUM(1+1)").Id;
        string[] notes = ["=HYPERLINK(\"http://x\")", "+1", "-1", "@A1", "\tcmd", "\rcmd"];
        for (var i = 0; i < notes.Length; i++)
        {
            await AuditTrailTests.CreateTransactionAsync(client, book, new
            {
                kind = "Expense", date = $"2026-01-{i + 1:00}", amount = -120.5m, note = notes[i],
                accountId = evilAccount, categoryId = book.FindCategory("主食", "午餐")!.Id.Value,
            });
        }

        var csv = await client.GetStringAsync($"/api/books/{book.Id.Value}/export/transactions.csv", Ct);
        var xlsx = await client.GetByteArrayAsync($"/api/books/{book.Id.Value}/export/transactions.xlsx", Ct);

        var rows = ParseCsv(csv.TrimStart('﻿')).Skip(1).ToList();
        rows.Select(r => r[3]).Should().OnlyContain(name => name == "'@SUM(1+1)");
        rows.Select(r => r[11]).Should().Equal(notes.Select(n => "'" + n));
        rows.Select(r => r[8]).Should().OnlyContain(amount => amount == "-120.5", "金額是數字，不跳脫");

        using var workbook = new XLWorkbook(new MemoryStream(xlsx));
        var sheet = workbook.Worksheet("交易");
        for (var row = 2; row <= notes.Length + 1; row++)
        {
            foreach (var column in new[] { "D", "L" })
            {
                var cell = sheet.Cell($"{column}{row}");
                cell.HasFormula.Should().BeFalse();
                cell.DataType.Should().Be(XLDataType.Text);
                cell.Style.IncludeQuotePrefix.Should().BeTrue($"{column}{row} 以公式字元開頭，要設 quote prefix");
            }

            // XML 讀取時把 CR 正規化成 LF（XML 1.0 §2.11），這是格式本身的行為；除此之外內容原樣保留，不加 '。
            sheet.Cell($"L{row}").GetString().Should().Be(notes[row - 2].Replace('\r', '\n'), "xlsx 的內容原樣保留，不加 '");
            sheet.Cell($"I{row}").DataType.Should().Be(XLDataType.Number);
        }

        sheet.Cell("C2").Style.IncludeQuotePrefix.Should().BeFalse("一般文字不加 quote prefix");
    }

    private Task<ApiFactory> CreateFactoryAsync() =>
        ApiFactory.CreateAsync(postgres, Ct, services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now)));

    /// <summary>四筆有效交易（收入、入新資金、支出、貸款繳款），加上一筆已刪除的支出；建立順序刻意與日期順序不同。</summary>
    private static async Task<(Book Book, HttpClient Client)> SeedLedgerAsync(ApiFactory factory)
    {
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var bank = book.FindAccount("國泰世華銀行")!.Id.Value;
        var lunch = book.FindCategory("主食", "午餐")!.Id.Value;

        await AuditTrailTests.CreateTransactionAsync(client, book, new
        {
            kind = "LoanPayment", date = "2026-02-05", amount = 20000m, loanPrincipal = 15000m, accountId = bank,
            counterAccountId = book.FindAccount("房屋貸款")!.Id.Value, categoryId = book.FindCategory("貸款支出", "房屋貸款")!.Id.Value,
        });
        await AuditTrailTests.CreateTransactionAsync(client, book, new
        {
            kind = "Expense", date = "2026-01-20", amount = -120.5m, note = TrickyNote,
            accountId = book.FindAccount("現金")!.Id.Value, categoryId = lunch,
        });
        await AuditTrailTests.CreateTransactionAsync(client, book, new
        {
            kind = "Income", date = "2026-01-05", amount = 50000m, accountId = bank, categoryId = book.FindCategory("工作薪資")!.Id.Value,
        });
        await AuditTrailTests.CreateTransactionAsync(client, book, new
        {
            kind = "FundAllocation", date = "2026-01-10", amount = 1000m, accountId = bank,
            planningFundId = book.FindPlanningFund("財務自由帳戶")!.Id.Value,
        });
        var deleted = await AuditTrailTests.CreateTransactionAsync(client, book, new
        {
            kind = "Expense", date = "2026-01-15", amount = -999m, note = "刪掉的", accountId = bank, categoryId = lunch,
        });
        (await client.DeleteAsync($"/api/books/{book.Id.Value}/transactions/{deleted.Id}?version={deleted.Version}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        return (book, client);
    }

    /// <summary>最小的 RFC 4180 解析器：雙引號包住的欄位可含逗號、換行，<c>""</c> 代表一個雙引號；列以 CRLF 分隔。</summary>
    private static List<string[]> ParseCsv(string text)
    {
        List<string[]> rows = [];
        List<string> fields = [];
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                rows.Add([.. fields]);
                fields.Clear();
                i++;
            }
            else
            {
                field.Append(c);
            }
        }

        quoted.Should().BeFalse("雙引號必須成對");
        fields.Should().BeEmpty("最後一列也要以 CRLF 結尾");
        return rows;
    }
}
