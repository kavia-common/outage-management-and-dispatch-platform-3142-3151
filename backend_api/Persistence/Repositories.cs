using Dapper;
using backend_api.Models;

namespace backend_api.Persistence;

public interface ICustomerRepository
{
    Task<CustomerDto?> GetCustomerByIdAsync(string customerUserId);
    Task<IReadOnlyList<CustomerDto>> SearchCustomersAsync(string organizationId, string query, int limit, int offset);
}

public interface IOutageRepository
{
    Task<OutageDto?> GetOutageAsync(string outageId);
    Task<PagedResult<OutageDto>> ListOutagesAsync(string organizationId, string? status, string? severity, int limit, int offset);
    Task<(OutageDto outage, string locationId)> CreateOutageAsync(CreateOutageRequest request);
    Task<OutageDto?> UpdateOutageStatusAsync(string outageId, UpdateOutageStatusRequest request);
    Task<OutageUpdateDto> AddOutageUpdateAsync(string outageId, OutageUpdateType type, string message, string? createdByUserId);
    Task<IReadOnlyList<OutageUpdateDto>> GetOutageUpdatesAsync(string outageId, int limit, int offset);
}

public interface IJobRepository
{
    Task<JobDto?> GetJobAsync(string jobId);
    Task<PagedResult<JobDto>> ListJobsAsync(string organizationId, string? status, string? outageId, int limit, int offset);
    Task<JobDto> CreateJobAsync(CreateJobRequest request);
    Task<JobDto?> AssignJobAsync(string jobId, AssignJobRequest request);
    Task<JobDto?> UpdateJobStatusAsync(string jobId, UpdateJobStatusRequest request);
    Task<IReadOnlyList<JobStatusEventDto>> GetJobStatusEventsAsync(string jobId, int limit, int offset);
}

public sealed class CustomerRepository : ICustomerRepository
{
    private readonly IMySqlConnectionFactory _cf;

    public CustomerRepository(IMySqlConnectionFactory cf) => _cf = cf;

    public async Task<CustomerDto?> GetCustomerByIdAsync(string customerUserId)
    {
        const string sql = """
SELECT id, email, full_name AS FullName, phone, role, is_active AS IsActive
FROM users
WHERE id = @customerUserId AND role = 'customer';
""";
        using var conn = _cf.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<CustomerDto>(sql, new { customerUserId });
    }

    public async Task<IReadOnlyList<CustomerDto>> SearchCustomersAsync(string organizationId, string query, int limit, int offset)
    {
        const string sql = """
SELECT id, email, full_name AS FullName, phone, role, is_active AS IsActive
FROM users
WHERE organization_id = @organizationId
  AND role = 'customer'
  AND (email LIKE @q OR full_name LIKE @q OR phone LIKE @q)
ORDER BY full_name ASC
LIMIT @limit OFFSET @offset;
""";
        using var conn = _cf.CreateConnection();
        var items = await conn.QueryAsync<CustomerDto>(sql, new { organizationId, q = $"%{query}%", limit, offset });
        return items.ToList();
    }
}

public sealed class OutageRepository : IOutageRepository
{
    private readonly IMySqlConnectionFactory _cf;

    public OutageRepository(IMySqlConnectionFactory cf) => _cf = cf;

    public async Task<OutageDto?> GetOutageAsync(string outageId)
    {
        const string sql = """
SELECT
  id,
  organization_id AS OrganizationId,
  reported_by_user_id AS ReportedByUserId,
  customer_user_id AS CustomerUserId,
  location_id AS LocationId,
  title,
  description,
  severity,
  status,
  started_at AS StartedAt,
  resolved_at AS ResolvedAt,
  created_at AS CreatedAt,
  updated_at AS UpdatedAt
FROM outages
WHERE id = @outageId;
""";
        using var conn = _cf.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<OutageDto>(sql, new { outageId });
    }

    public async Task<PagedResult<OutageDto>> ListOutagesAsync(string organizationId, string? status, string? severity, int limit, int offset)
    {
        var where = "WHERE organization_id = @organizationId";
        if (!string.IsNullOrWhiteSpace(status)) where += " AND status = @status";
        if (!string.IsNullOrWhiteSpace(severity)) where += " AND severity = @severity";

        var sql = $"""
SELECT
  id,
  organization_id AS OrganizationId,
  reported_by_user_id AS ReportedByUserId,
  customer_user_id AS CustomerUserId,
  location_id AS LocationId,
  title,
  description,
  severity,
  status,
  started_at AS StartedAt,
  resolved_at AS ResolvedAt,
  created_at AS CreatedAt,
  updated_at AS UpdatedAt
FROM outages
{where}
ORDER BY updated_at DESC
LIMIT @limit OFFSET @offset;
""";
        using var conn = _cf.CreateConnection();
        var items = (await conn.QueryAsync<OutageDto>(sql, new { organizationId, status, severity, limit, offset })).ToList();
        return new PagedResult<OutageDto>(items, limit, offset, items.Count);
    }

    public async Task<(OutageDto outage, string locationId)> CreateOutageAsync(CreateOutageRequest request)
    {
        var locationId = Guid.NewGuid().ToString();
        var outageId = Guid.NewGuid().ToString();

        using var conn = _cf.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        const string insertLocation = """
INSERT INTO locations (
  id, organization_id, address_line1, address_line2, city, state, postal_code, country, latitude, longitude
) VALUES (
  @id, @organizationId, @addressLine1, @addressLine2, @city, @state, @postalCode, @country, @latitude, @longitude
);
""";

        await conn.ExecuteAsync(insertLocation, new
        {
            id = locationId,
            organizationId = request.OrganizationId,
            addressLine1 = request.Location.AddressLine1,
            addressLine2 = request.Location.AddressLine2,
            city = request.Location.City,
            state = request.Location.State,
            postalCode = request.Location.PostalCode,
            country = request.Location.Country,
            latitude = request.Location.Latitude,
            longitude = request.Location.Longitude
        }, tx);

        const string insertOutage = """
INSERT INTO outages (
  id, organization_id, reported_by_user_id, customer_user_id, location_id,
  title, description, severity, status, started_at
) VALUES (
  @id, @organizationId, @reportedByUserId, @customerUserId, @locationId,
  @title, @description, @severity, 'new', @startedAt
);
""";

        await conn.ExecuteAsync(insertOutage, new
        {
            id = outageId,
            organizationId = request.OrganizationId,
            reportedByUserId = request.ReportedByUserId,
            customerUserId = request.CustomerUserId,
            locationId,
            title = request.Title,
            description = request.Description,
            severity = request.Severity,
            startedAt = request.StartedAt
        }, tx);

        // Initial timeline note
        const string insertUpdate = """
INSERT INTO outage_updates(outage_id, update_type, message, created_by_user_id)
VALUES(@outageId, 'system', @message, @createdByUserId);
""";
        await conn.ExecuteAsync(insertUpdate, new
        {
            outageId,
            message = "Outage reported",
            createdByUserId = request.ReportedByUserId
        }, tx);

        tx.Commit();

        var created = await GetOutageAsync(outageId);
        if (created is null) throw new InvalidOperationException("Failed to create outage.");
        return (created, locationId);
    }

    public async Task<OutageDto?> UpdateOutageStatusAsync(string outageId, UpdateOutageStatusRequest request)
    {
        using var conn = _cf.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        const string updateSql = """
UPDATE outages
SET status = @status,
    resolved_at = CASE WHEN @status = 'resolved' THEN COALESCE(@resolvedAt, NOW()) ELSE resolved_at END
WHERE id = @outageId;
""";
        var rows = await conn.ExecuteAsync(updateSql, new { outageId, status = request.Status, resolvedAt = request.ResolvedAt }, tx);
        if (rows == 0)
        {
            tx.Rollback();
            return null;
        }

        const string insertUpdate = """
INSERT INTO outage_updates(outage_id, update_type, message, created_by_user_id)
VALUES(@outageId, 'status_change', @message, @createdByUserId);
""";
        await conn.ExecuteAsync(insertUpdate, new
        {
            outageId,
            message = string.IsNullOrWhiteSpace(request.Message) ? $"Status changed to {request.Status}" : request.Message,
            createdByUserId = request.ChangedByUserId
        }, tx);

        tx.Commit();
        return await GetOutageAsync(outageId);
    }

    public async Task<OutageUpdateDto> AddOutageUpdateAsync(string outageId, OutageUpdateType type, string message, string? createdByUserId)
    {
        const string sql = """
INSERT INTO outage_updates(outage_id, update_type, message, created_by_user_id)
VALUES(@outageId, @updateType, @message, @createdByUserId);
SELECT LAST_INSERT_ID();
""";
        using var conn = _cf.CreateConnection();
        // MySQL returns decimal sometimes; cast via Convert.ToInt64
        var newIdObj = await conn.ExecuteScalarAsync<object>(sql, new { outageId, updateType = type.ToString(), message, createdByUserId });
        var newId = Convert.ToInt64(newIdObj);

        const string fetch = """
SELECT
  id,
  outage_id AS OutageId,
  update_type AS UpdateType,
  message,
  created_by_user_id AS CreatedByUserId,
  created_at AS CreatedAt
FROM outage_updates
WHERE id = @id;
""";
        return await conn.QuerySingleAsync<OutageUpdateDto>(fetch, new { id = newId });
    }

    public async Task<IReadOnlyList<OutageUpdateDto>> GetOutageUpdatesAsync(string outageId, int limit, int offset)
    {
        const string sql = """
SELECT
  id,
  outage_id AS OutageId,
  update_type AS UpdateType,
  message,
  created_by_user_id AS CreatedByUserId,
  created_at AS CreatedAt
FROM outage_updates
WHERE outage_id = @outageId
ORDER BY created_at DESC
LIMIT @limit OFFSET @offset;
""";
        using var conn = _cf.CreateConnection();
        var items = await conn.QueryAsync<OutageUpdateDto>(sql, new { outageId, limit, offset });
        return items.ToList();
    }
}

public sealed class JobRepository : IJobRepository
{
    private readonly IMySqlConnectionFactory _cf;

    public JobRepository(IMySqlConnectionFactory cf) => _cf = cf;

    public async Task<JobDto?> GetJobAsync(string jobId)
    {
        const string sql = """
SELECT
  id,
  organization_id AS OrganizationId,
  outage_id AS OutageId,
  assigned_crew_id AS AssignedCrewId,
  assigned_to_user_id AS AssignedToUserId,
  status,
  priority,
  notes,
  created_at AS CreatedAt,
  updated_at AS UpdatedAt
FROM jobs
WHERE id = @jobId;
""";
        using var conn = _cf.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<JobDto>(sql, new { jobId });
    }

    public async Task<PagedResult<JobDto>> ListJobsAsync(string organizationId, string? status, string? outageId, int limit, int offset)
    {
        var where = "WHERE organization_id = @organizationId";
        if (!string.IsNullOrWhiteSpace(status)) where += " AND status = @status";
        if (!string.IsNullOrWhiteSpace(outageId)) where += " AND outage_id = @outageId";

        var sql = $"""
SELECT
  id,
  organization_id AS OrganizationId,
  outage_id AS OutageId,
  assigned_crew_id AS AssignedCrewId,
  assigned_to_user_id AS AssignedToUserId,
  status,
  priority,
  notes,
  created_at AS CreatedAt,
  updated_at AS UpdatedAt
FROM jobs
{where}
ORDER BY updated_at DESC
LIMIT @limit OFFSET @offset;
""";
        using var conn = _cf.CreateConnection();
        var items = (await conn.QueryAsync<JobDto>(sql, new { organizationId, status, outageId, limit, offset })).ToList();
        return new PagedResult<JobDto>(items, limit, offset, items.Count);
    }

    public async Task<JobDto> CreateJobAsync(CreateJobRequest request)
    {
        var jobId = Guid.NewGuid().ToString();
        const string sql = """
INSERT INTO jobs(
  id, organization_id, outage_id, status, priority, notes
) VALUES (
  @id, @organizationId, @outageId, 'pending', @priority, @notes
);
""";
        using var conn = _cf.CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            id = jobId,
            organizationId = request.OrganizationId,
            outageId = request.OutageId,
            priority = request.Priority,
            notes = request.Notes
        });

        // Initial event
        await InsertJobStatusEventAsync(conn, jobId, "pending", null, "Job created");
        var created = await GetJobAsync(jobId);
        return created ?? throw new InvalidOperationException("Failed to create job.");
    }

    public async Task<JobDto?> AssignJobAsync(string jobId, AssignJobRequest request)
    {
        using var conn = _cf.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        const string sql = """
UPDATE jobs
SET assigned_crew_id = @assignedCrewId,
    assigned_to_user_id = @assignedToUserId,
    status = 'assigned'
WHERE id = @jobId;
""";
        var rows = await conn.ExecuteAsync(sql, new
        {
            jobId,
            assignedCrewId = request.AssignedCrewId,
            assignedToUserId = request.AssignedToUserId
        }, tx);

        if (rows == 0)
        {
            tx.Rollback();
            return null;
        }

        await InsertJobStatusEventAsync(conn, jobId, "assigned", request.ChangedByUserId,
            string.IsNullOrWhiteSpace(request.Message) ? "Job assigned" : request.Message, tx);

        tx.Commit();
        return await GetJobAsync(jobId);
    }

    public async Task<JobDto?> UpdateJobStatusAsync(string jobId, UpdateJobStatusRequest request)
    {
        using var conn = _cf.CreateConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        const string sql = """
UPDATE jobs
SET status = @status
WHERE id = @jobId;
""";
        var rows = await conn.ExecuteAsync(sql, new { jobId, status = request.Status }, tx);
        if (rows == 0)
        {
            tx.Rollback();
            return null;
        }

        await InsertJobStatusEventAsync(conn, jobId, request.Status, request.ChangedByUserId,
            string.IsNullOrWhiteSpace(request.Message) ? $"Status changed to {request.Status}" : request.Message, tx);

        tx.Commit();
        return await GetJobAsync(jobId);
    }

    public async Task<IReadOnlyList<JobStatusEventDto>> GetJobStatusEventsAsync(string jobId, int limit, int offset)
    {
        const string sql = """
SELECT
  id,
  job_id AS JobId,
  status,
  changed_by_user_id AS ChangedByUserId,
  message,
  created_at AS CreatedAt
FROM job_status_events
WHERE job_id = @jobId
ORDER BY created_at DESC
LIMIT @limit OFFSET @offset;
""";
        using var conn = _cf.CreateConnection();
        var items = await conn.QueryAsync<JobStatusEventDto>(sql, new { jobId, limit, offset });
        return items.ToList();
    }

    private static async Task InsertJobStatusEventAsync(System.Data.IDbConnection conn, string jobId, string status, string? changedByUserId, string? message, System.Data.IDbTransaction? tx = null)
    {
        const string sql = """
INSERT INTO job_status_events(job_id, status, changed_by_user_id, message)
VALUES(@jobId, @status, @changedByUserId, @message);
""";
        await conn.ExecuteAsync(sql, new { jobId, status, changedByUserId, message }, tx);
    }
}
