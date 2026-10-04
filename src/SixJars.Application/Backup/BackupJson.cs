using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SixJars.Application.Common;

namespace SixJars.Application.Backup;

/// <summary>備份檔的 JSON 格式：與 API 相同的 camelCase 與列舉名稱，縮排、中文不轉成 <c>\uXXXX</c>，方便人工檢視。</summary>
public static class BackupJson
{
    /// <summary>
    /// 讀取時嚴格檢查：缺少建構子參數、或不可為 null 的欄位是 null，都擲 <see cref="JsonException"/>，
    /// 不讓被手動改壞的備份以 null 的形式進到還原流程。
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public static byte[] SerializeToUtf8Bytes(BackupDocument backup) => JsonSerializer.SerializeToUtf8Bytes(backup, Options);

    /// <summary>下載的檔名 <c>sixjars-backup-yyyyMMdd.json</c>；日期是匯出時間在台北的日期（使用者在台灣，伺服器是 UTC）。</summary>
    public static string FileName(DateTimeOffset exportedAt) => $"sixjars-backup-{TaipeiTime.DateOf(exportedAt):yyyyMMdd}.json";
}
