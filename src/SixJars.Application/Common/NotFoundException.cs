namespace SixJars.Application.Common;

/// <summary>找不到要求的資源；API 對應成 404。</summary>
public sealed class NotFoundException(string message) : Exception(message);
