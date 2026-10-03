using System.Text.RegularExpressions;

namespace SixJars.Application.LegacyImport;

/// <summary>舊記帳本中有特殊語意的名稱。</summary>
internal static partial class LegacyNames
{
    public const string Cash = "現金";
    public const string NonHandCash = "非手上現金";
    public const string ForeignCash = "外幣現鈔";
    public const string ForeignCashFund = "外幣現金";
    public const string Transfer = "轉帳";
    public const string Withdrawal = "提款";
    public const string CashDeposit = "現金存入";
    public const string TopUp = "加值";
    public const string CardPayment = "繳信用卡款";
    public const string LoanDisbursement = "新增貸款";
    public const string LoanExpense = "貸款支出";
    public const string BankFee = "手續費";
    public const string FixedExpense = "固定支出";
    public const string SpecialExpense = "特別支出";
    public const string NewFunds = "入新資金";
    public const string FundWithdrawal = "出資金";
    public const string FundReturn = "資金回流";
    public const string Financial = "金融交易";
    public const string OtherIncome = "其它收入";

    /// <summary>Excel 預留但未使用的名稱（自定浮支NN、固定支出NN、貸款備用NN），不匯入。</summary>
    public static bool IsPlaceholder(string name) => PlaceholderPattern().IsMatch(name);

    [GeneratedRegex(@"^(自定浮支|固定支出|貸款備用)\d+$")]
    private static partial Regex PlaceholderPattern();
}
