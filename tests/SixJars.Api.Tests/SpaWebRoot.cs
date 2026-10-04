namespace SixJars.Api.Tests;

/// <summary>測試用的假 Angular build 輸出：每次呼叫建立一個全新的暫存目錄，避免測試之間互相影響。</summary>
internal static class SpaWebRoot
{
    /// <summary>放在 index.html 裡的標記，測試以此判斷回應是不是 SPA 的入口頁。</summary>
    public const string IndexMarker = "<!-- sixjars-spa -->";

    public static string Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sixjars-spa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "index.html"), $"<!doctype html><html><body>{IndexMarker}</body></html>");
        File.WriteAllText(Path.Combine(directory, "main-ABCD1234.js"), "console.log('main');");
        File.WriteAllText(Path.Combine(directory, "ngsw.json"), "{}");
        return directory;
    }
}
