using FluentValidation;
using SixJars.Domain.Transactions;

namespace SixJars.Application.Transactions;

/// <summary>
/// MCP 記帳輸入（<see cref="RecordTransaction"/> 與 <see cref="PreviewTransaction"/>）共用的驗證規則。
/// 試算必須和正式記帳拒絕同樣的輸入，否則試算通過、記帳卻失敗；規則只寫一份，兩個 validator 不會各自漂移。
/// </summary>
internal static class BookkeepingInputRules
{
    /// <remarks>selector 不是成員運算式，FluentValidation 推不出屬性名稱，所以明確指定，錯誤訊息的欄位名與原本一致。</remarks>
    public static void AddBookkeepingInputRules<T>(
        this AbstractValidator<T> validator, Func<T, TransactionInput?> input, Func<T, string?> externalRef)
    {
        validator.RuleFor(c => input(c)).NotNull().SetValidator(new TransactionInputValidator()!).OverridePropertyName("Input");
        validator.RuleFor(c => input(c)!.Kind).Must(RecordTransaction.SupportedKinds.Contains)
            .When(c => input(c) is not null)
            .OverridePropertyName("Input.Kind")
            .WithMessage($"不支援的交易種類，只能記 {string.Join("、", RecordTransaction.SupportedKinds)}。");
        validator.RuleFor(c => externalRef(c)).NotEmpty().MaximumLength(RecordTransaction.ExternalRefMaxLength)
            .When(c => externalRef(c) is not null).OverridePropertyName("ExternalRef");
    }
}
