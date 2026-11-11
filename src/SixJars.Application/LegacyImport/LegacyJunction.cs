using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Application.LegacyImport;

/// <summary>銜接摘要的一列：舊年度推算的期末，與帳本目前期初（或新增項目的 0）比對。</summary>
/// <param name="ExpectedOpening">舊年度期末應該等於的值：既有項目是帳本目前的期初，只在舊年度出現的項目是 0。</param>
/// <param name="CurrentOpening">比對用的帳本期初；null 表示只在帳本出現、不檢查（R3-Q1）。</param>
public sealed record JunctionLine(string Name, bool IsPlanningFund, decimal PreviousOpening, decimal Movement, decimal ExpectedOpening, decimal? CurrentOpening)
{
    public decimal PreviousClosing => PreviousOpening + Movement;

    /// <summary>null：只在帳本出現、不檢查（R3-Q1）。</summary>
    public decimal? Difference => CurrentOpening is null ? null : PreviousClosing - ExpectedOpening;
}

/// <summary>
/// 補匯舊年度的年度、日期與銜接檢查（spec R2-Q1、R2-Q2、R2-Q5、R3-Q1、D5）。純函式。
/// public 是為了與 <see cref="LegacyWorkbookMapper"/> 一樣能直接在 Application 測試裡驗證。
/// </summary>
public static class LegacyJunction
{
    /// <summary>檢查錯誤沒有來源列號，一律記在這個虛擬工作表第 0 列。</summary>
    public const string Sheet = "銜接檢查";

    /// <summary>
    /// 帳本開始的年度（spec M1）：最早一筆交易的歸屬月份的年度。舊 Excel 依月表分年，
    /// 12/31 入帳、歸屬隔年 1 月的交易很常見，看日期會錯判。帳本沒有交易時，以期初日的隔天（帳本第一天）為準。
    /// </summary>
    public static int BookStartYear(DateOnly bookOpeningDate, BudgetMonth? earliestBudgetMonth) =>
        earliestBudgetMonth?.Year ?? bookOpeningDate.AddDays(1).Year;

    /// <summary>年度必須剛好是帳本開始那年的前一年（spec M1）；回傳錯誤訊息，通過時為 null。對應前呼叫，不符就不必往下。</summary>
    public static string? CheckYear(int bookStartYear, int excelYear) =>
        excelYear == bookStartYear - 1
            ? null
            : $"舊帳本是 {excelYear} 年，帳本從 {bookStartYear} 年開始，只能補匯 {bookStartYear - 1} 年。";

    /// <summary>
    /// 日期檢查與銜接檢查；錯誤寫進 <paramref name="report"/>，回傳每個帳戶與財務規劃帳戶的摘要。
    /// </summary>
    /// <param name="book">對應之後的帳本：既有項目的期初未被改寫，只在舊年度出現的項目已加入。</param>
    /// <param name="bookOpeningDate">對應前的帳本期初日。</param>
    public static IReadOnlyList<JunctionLine> Check(Book book, DateOnly bookOpeningDate, LegacyPrependMapping mapping, ImportReport report)
    {
        // spec M2：舊年度的交易都要落在帳本第一天（含）之前。第一天起每天的餘額都會把它們全數計入，
        // 加上下面的銜接檢查，餘額必然不變；晚於第一天的交易會讓第一天起的某些天少算。
        var firstDay = bookOpeningDate.AddDays(1);
        foreach (var transaction in mapping.Transactions.Where(t => t.Date > firstDay))
        {
            var source = mapping.Sources[transaction.Id];
            report.AddError(source.Sheet, source.Row,
                $"日期 {transaction.Date:yyyy-MM-dd}（金額 {transaction.Amount:#,0.##}）晚於帳本第一天 {firstDay:yyyy-MM-dd}，會改變帳本既有的餘額。");
        }

        // 與 BalanceCalculator 相同的算法：帳戶加總分錄，財務規劃帳戶加總 FundDelta。
        var accountMovements = mapping.Transactions.SelectMany(t => t.Postings)
            .GroupBy(p => p.AccountId).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var fundMovements = mapping.Transactions.Where(t => t.PlanningFundId is not null)
            .GroupBy(t => t.PlanningFundId!.Value).ToDictionary(g => g.Key, g => g.Sum(t => t.FundDelta));

        var lines = new List<JunctionLine>();
        foreach (var account in book.Accounts)
        {
            lines.Add(Line(account.Name, isPlanningFund: false, account.OpeningBalance,
                mapping.AccountOpenings.TryGetValue(account.Id, out var opening) ? opening : null,
                accountMovements.GetValueOrDefault(account.Id), mapping.AddedAccounts.Contains(account.Id)));
        }

        foreach (var fund in book.PlanningFunds)
        {
            lines.Add(Line(fund.Name, isPlanningFund: true, fund.OpeningBalance,
                mapping.FundOpenings.TryGetValue(fund.Id, out var opening) ? opening : null,
                fundMovements.GetValueOrDefault(fund.Id), mapping.AddedPlanningFunds.Contains(fund.Id)));
        }

        // R3-Q1 的前提是舊年度沒動過這個項目：期初不改、交易卻寫入，之後每天的餘額都會差這個數。
        foreach (var line in lines.Where(l => l.CurrentOpening is null && l.Movement != 0m))
        {
            var kind = line.IsPlanningFund ? "財務規劃帳戶" : "帳戶";
            report.AddError(Sheet, 0,
                $"{kind}「{line.Name}」不在舊年度的設定裡，但舊年度有 {line.Movement:#,0.##} 的異動，無法銜接。");
        }

        // 金額用 decimal 精確相等，不設容差：差 1 元就代表帳對不起來。
        foreach (var line in lines.Where(l => l.Difference is not null and not 0m))
        {
            var kind = line.IsPlanningFund ? "財務規劃帳戶" : "帳戶";
            report.AddError(Sheet, 0,
                $"{kind}「{line.Name}」舊年度期末 {line.PreviousClosing:#,0.##}，應為 {line.ExpectedOpening:#,0.##}，差 {line.Difference:#,0.##}。");
        }

        return lines;
    }

    /// <param name="bookOpening">帳本裡的期初；新增項目是對應時帶入的舊年度期初，不拿來比對。</param>
    /// <param name="previousOpening">舊年度期初；null 表示只在帳本出現。</param>
    private static JunctionLine Line(string name, bool isPlanningFund, decimal bookOpening, decimal? previousOpening, decimal movement, bool added)
    {
        if (previousOpening is null)
        {
            // R3-Q1：只在帳本出現的項目，期初餘額不變、不檢查。
            return new JunctionLine(name, isPlanningFund, 0m, movement, bookOpening, null);
        }

        // D5：只在舊年度出現的項目，在銜接點的餘額必須是 0，否則這筆錢在帳本裡憑空消失。
        var expected = added ? 0m : bookOpening;
        return new JunctionLine(name, isPlanningFund, previousOpening.Value, movement, expected, expected);
    }
}
