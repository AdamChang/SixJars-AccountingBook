namespace SixJars.Application.LegacyImport;

/// <summary>匯入報告：錯誤（該列未匯入）、警告（已匯入但需留意）、修正（已自動修正）。</summary>
public sealed class ImportReport
{
    private readonly List<ImportIssue> _errors = [];
    private readonly List<ImportIssue> _warnings = [];
    private readonly List<ImportIssue> _corrections = [];

    public IReadOnlyList<ImportIssue> Errors => _errors;
    public IReadOnlyList<ImportIssue> Warnings => _warnings;
    public IReadOnlyList<ImportIssue> Corrections => _corrections;

    internal void AddError(string sheet, int row, string message) => _errors.Add(new(sheet, row, message));

    internal void AddWarning(string sheet, int row, string message) => _warnings.Add(new(sheet, row, message));

    internal void AddCorrection(string sheet, int row, string message) => _corrections.Add(new(sheet, row, message));
}

public sealed record ImportIssue(string Sheet, int Row, string Message);
