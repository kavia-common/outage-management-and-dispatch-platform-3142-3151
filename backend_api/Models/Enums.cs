namespace backend_api.Models;

/// <summary>
/// Outage severity levels (matches DB ENUM).
/// </summary>
public enum OutageSeverity
{
    low,
    medium,
    high,
    critical
}

/// <summary>
/// Outage lifecycle status (matches DB ENUM).
/// </summary>
public enum OutageStatus
{
    new_status, // reserved keyword workaround - mapping handled in code
    triaged,
    dispatched,
    in_progress,
    resolved,
    cancelled
}

/// <summary>
/// Job lifecycle status (matches DB ENUM).
/// </summary>
public enum JobStatus
{
    pending,
    assigned,
    en_route,
    on_site,
    completed,
    cancelled
}

/// <summary>
/// Job priority levels (matches DB ENUM).
/// </summary>
public enum JobPriority
{
    low,
    normal,
    high,
    urgent
}

/// <summary>
/// Outage update message type (matches DB ENUM).
/// </summary>
public enum OutageUpdateType
{
    note,
    status_change,
    system
}
