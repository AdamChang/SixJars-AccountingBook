namespace SixJars.Api.Infrastructure;

/// <summary>登入後要回到的頁面；只接受站內的相對路徑，其他一律回首頁，防止 open redirect。</summary>
internal static class ReturnUrl
{
    private const string Root = "/";

    /// <summary>
    /// 規則比照 ASP.NET Core 的 <c>IsLocalUrl</c>：必須以單一個 <c>/</c> 開頭，第二個字元不可以是 <c>/</c> 或 <c>\</c>
    /// （瀏覽器把兩者都當成 <c>//host</c>），也不可以含控制字元（瀏覽器會忽略 tab 與換行，<c>/\t/evil</c> 等於 <c>//evil</c>）。
    /// 不支援 <c>~/</c>：前端不會送出這種路徑。
    /// </summary>
    public static string Sanitize(string? url) => IsLocalPath(url) ? url! : Root;

    private static bool IsLocalPath(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/')
        {
            return false;
        }

        if (url.Length == 1)
        {
            return true;
        }

        return url[1] is not ('/' or '\\') && !url.Any(char.IsControl);
    }
}
