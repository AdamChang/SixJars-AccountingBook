using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SixJars.Application.Auditing;

/// <summary>稽核快照的 JSON 格式：與 API 相同的 camelCase 與列舉名稱，中文不轉成 <c>\uXXXX</c>。</summary>
public static class AuditSnapshots
{
    /// <summary>
    /// 新增與修改後的快照裡 DTO 的 Version 一律記為 0：xmin 要到 SaveChanges 才由資料庫產生，
    /// 而快照必須在同一次 SaveChanges 之前加入，不為了它多一次資料庫往返。修改前的快照則帶讀到的版本。
    /// </summary>
    public const uint UnknownVersion = 0;

    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
