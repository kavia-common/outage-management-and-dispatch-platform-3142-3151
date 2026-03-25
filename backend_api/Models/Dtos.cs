using System.ComponentModel.DataAnnotations;

namespace backend_api.Models;

public sealed record LocationDto(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? State,
    string? PostalCode,
    string Country,
    decimal? Latitude,
    decimal? Longitude
);

public sealed record CustomerDto(
    string Id,
    string Email,
    string FullName,
    string? Phone,
    string Role,
    bool IsActive
);

public sealed record CrewDto(
    string Id,
    string Name,
    string? LeadUserId,
    bool IsActive
);

public sealed record OutageDto(
    string Id,
    string OrganizationId,
    string? ReportedByUserId,
    string? CustomerUserId,
    string LocationId,
    string Title,
    string? Description,
    string Severity,
    string Status,
    DateTime? StartedAt,
    DateTime? ResolvedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record JobDto(
    string Id,
    string OrganizationId,
    string OutageId,
    string? AssignedCrewId,
    string? AssignedToUserId,
    string Status,
    string Priority,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record JobStatusEventDto(
    long Id,
    string JobId,
    string Status,
    string? ChangedByUserId,
    string? Message,
    DateTime CreatedAt
);

public sealed record OutageUpdateDto(
    long Id,
    string OutageId,
    string UpdateType,
    string Message,
    string? CreatedByUserId,
    DateTime CreatedAt
);

public sealed record CreateOutageRequest
{
    [Required]
    public string OrganizationId { get; init; } = default!;

    public string? ReportedByUserId { get; init; }
    public string? CustomerUserId { get; init; }

    [Required]
    public string Title { get; init; } = default!;

    public string? Description { get; init; }

    /// <summary>low|medium|high|critical</summary>
    [Required]
    public string Severity { get; init; } = default!;

    public DateTime? StartedAt { get; init; }

    // Inline location create for MVP intake
    [Required]
    public LocationDto Location { get; init; } = default!;
}

public sealed record UpdateOutageStatusRequest
{
    /// <summary>new|triaged|dispatched|in_progress|resolved|cancelled</summary>
    [Required]
    public string Status { get; init; } = default!;

    public string? Message { get; init; }
    public string? ChangedByUserId { get; init; }

    public DateTime? ResolvedAt { get; init; }
}

public sealed record AddOutageNoteRequest
{
    [Required]
    public string Message { get; init; } = default!;

    public string? CreatedByUserId { get; init; }
}

public sealed record CreateJobRequest
{
    [Required]
    public string OrganizationId { get; init; } = default!;

    [Required]
    public string OutageId { get; init; } = default!;

    /// <summary>low|normal|high|urgent</summary>
    [Required]
    public string Priority { get; init; } = default!;

    public string? Notes { get; init; }
}

public sealed record AssignJobRequest
{
    public string? AssignedCrewId { get; init; }
    public string? AssignedToUserId { get; init; }

    public string? Message { get; init; }
    public string? ChangedByUserId { get; init; }
}

public sealed record UpdateJobStatusRequest
{
    /// <summary>pending|assigned|en_route|on_site|completed|cancelled</summary>
    [Required]
    public string Status { get; init; } = default!;

    public string? Message { get; init; }
    public string? ChangedByUserId { get; init; }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Limit, int Offset, int Returned);
