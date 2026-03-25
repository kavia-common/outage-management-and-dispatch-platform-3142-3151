namespace backend_api.Persistence;

/// <summary>
/// Database connection settings sourced from environment variables.
/// </summary>
public sealed class DbOptions
{
    /// <summary>
    /// Full MySQL connection URL. Example: mysql://host:port/db
    /// </summary>
    public string MysqlUrl { get; set; } = string.Empty;

    public string MysqlUser { get; set; } = string.Empty;
    public string MysqlPassword { get; set; } = string.Empty;
    public string MysqlDb { get; set; } = string.Empty;
    public string MysqlPort { get; set; } = string.Empty;
}
