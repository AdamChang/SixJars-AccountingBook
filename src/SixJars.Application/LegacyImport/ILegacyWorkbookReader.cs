namespace SixJars.Application.LegacyImport;

/// <summary>把舊 Excel 記帳本讀成 <see cref="LegacyWorkbook"/>。</summary>
public interface ILegacyWorkbookReader
{
    Task<LegacyWorkbook> ReadAsync(Stream stream, CancellationToken cancellationToken);
}
