namespace SixJars.Tests.Shared;

internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    /// <summary>可用環境變數 SIXJARS_LEGACY_WORKBOOK 覆寫。</summary>
    public static string LegacyWorkbook =>
        Environment.GetEnvironmentVariable("SIXJARS_LEGACY_WORKBOOK") ?? Path.Combine(Root, "reference", "2026帳本v1.xlsm");

    public static FileStream OpenLegacyWorkbook() =>
        new(LegacyWorkbook, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SixJars.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("找不到 SixJars.slnx，無法定位 repo 根目錄");
    }
}
