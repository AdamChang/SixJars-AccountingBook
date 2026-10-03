namespace SixJars.Domain.Common;

/// <summary>違反領域規則時拋出。</summary>
public sealed class DomainException(string message) : Exception(message);
