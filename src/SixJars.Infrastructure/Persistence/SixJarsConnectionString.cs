using Npgsql;

namespace SixJars.Infrastructure.Persistence;

public static class SixJarsConnectionString
{
    /// <summary>
    /// DbContext 實際使用的連線字串。Neon 不支援 GSSAPI；Npgsql 預設先試 GSS 加密，
    /// 在沒有 libgssapi_krb5 的 aspnet image 裡會印出誤導的 Error，所以一律關閉。
    /// Npgsql 依連線字串分連線池：要清 app 的連線池時，也必須用這裡轉換後的字串。
    /// </summary>
    public static string ForNpgsql(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString)
        {
            GssEncryptionMode = GssEncryptionMode.Disable,
        }.ConnectionString;
}
