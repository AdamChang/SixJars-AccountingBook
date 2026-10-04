using SixJars.Application.Common;

namespace SixJars.Cli;

/// <summary>CLI 的操作者：稽核記錄的 ActorSubject 一律是 <see cref="CliSubject"/>，看得出是 CLI 做的。</summary>
public sealed class CliCurrentUser : ICurrentUser
{
    public const string CliSubject = "cli";

    public string Subject => CliSubject;
    public string? Email => null;
}
