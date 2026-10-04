using FluentAssertions;
using SixJars.Api.Infrastructure;
using Xunit;

namespace SixJars.Api.Tests.Auth;

/// <summary>登入後的導向只接受站內的相對路徑，防止 open redirect；規則比照 ASP.NET Core 的 <c>IsLocalUrl</c>。</summary>
public class ReturnUrlTests
{
    [Theory]
    [InlineData("/transactions?m=1")]
    [InlineData("/")]
    [InlineData("/books/1/summary#jars")]
    public void Local_path_is_kept(string url) => ReturnUrl.Sanitize(url).Should().Be(url);

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("\\\\evil.example")]
    [InlineData("\\/evil.example")]
    [InlineData("evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("~/transactions")]
    [InlineData(" /transactions")]
    // 瀏覽器會忽略網址中的 tab 與換行，"/\t/evil.example" 會被當成 "//evil.example"。
    [InlineData("/\t/evil.example")]
    [InlineData("/\r\n/evil.example")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_becomes_root(string? url) => ReturnUrl.Sanitize(url).Should().Be("/");
}
