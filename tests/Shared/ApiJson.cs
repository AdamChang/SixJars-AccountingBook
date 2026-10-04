using System.Text.Json;
using System.Text.Json.Serialization;

namespace SixJars.Tests.Shared;

/// <summary>與 API 相同的 JSON 設定：camelCase、列舉序列化成字串。</summary>
internal static class ApiJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
