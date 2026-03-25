using System.Data;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace backend_api.Persistence;

public interface IMySqlConnectionFactory
{
    IDbConnection CreateConnection();
}

/// <summary>
/// Creates MySqlConnector connections using environment-derived configuration.
/// </summary>
public sealed class MySqlConnectionFactory : IMySqlConnectionFactory
{
    private readonly DbOptions _options;

    public MySqlConnectionFactory(IOptions<DbOptions> options)
    {
        _options = options.Value;
    }

    public IDbConnection CreateConnection()
    {
        // Prefer MYSQL_URL if provided as mysql://host:port/db (per platform).
        // Fall back to individual vars.
        var (host, port, database) = ParseMysqlUrl(_options.MysqlUrl);

        var finalHost = !string.IsNullOrWhiteSpace(host) ? host : "localhost";
        var finalPort = port ?? TryParsePort(_options.MysqlPort) ?? 3306;
        var finalDb = !string.IsNullOrWhiteSpace(database) ? database : _options.MysqlDb;

        var csb = new MySqlConnectionStringBuilder
        {
            Server = finalHost,
            Port = (uint)finalPort,
            Database = finalDb,
            UserID = _options.MysqlUser,
            Password = _options.MysqlPassword,
            // MVP defaults
            SslMode = MySqlSslMode.None,
            AllowUserVariables = true,
        };

        return new MySqlConnection(csb.ConnectionString);
    }

    private static int? TryParsePort(string? portRaw)
        => int.TryParse(portRaw, out var p) ? p : null;

    private static (string host, int? port, string db) ParseMysqlUrl(string? mysqlUrl)
    {
        if (string.IsNullOrWhiteSpace(mysqlUrl))
            return (string.Empty, null, string.Empty);

        // Expected: mysql://host:port/db
        // Use Uri to parse; if scheme isn't recognized, normalize.
        try
        {
            var normalized = mysqlUrl.StartsWith("mysql://", StringComparison.OrdinalIgnoreCase)
                ? mysqlUrl
                : $"mysql://{mysqlUrl}";

            var uri = new Uri(normalized);
            var db = uri.AbsolutePath.Trim('/');
            return (uri.Host, uri.Port > 0 ? uri.Port : null, db);
        }
        catch
        {
            return (string.Empty, null, string.Empty);
        }
    }
}
