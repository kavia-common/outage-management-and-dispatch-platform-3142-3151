using backend_api.Models;
using backend_api.Persistence;
using NSwag;
using NSwag.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Ensure we bind to the port expected by the preview platform.
// Most preview systems inject PORT; ASP.NET can also use ASPNETCORE_URLS.
// If neither is set, default to 3001 for local/dev parity.
var portRaw = Environment.GetEnvironmentVariable("PORT");
if (int.TryParse(portRaw, out var port) && port > 0)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}
else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://0.0.0.0:3001");
}

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument(options =>
{
    options.Title = "Smart Outage MVP API";
    options.Version = "0.1.0";
    options.Description = "Backend API for outage intake, dispatch/job management, customer lookup, and audit trails.";
    options.PostProcess = document =>
    {
        document.Info.Contact = new OpenApiContact
        {
            Name = "Smart Outage MVP",
        };
    };

    options.AddSecurity("ApiKey", Array.Empty<string>(), new OpenApiSecurityScheme
    {
        Type = OpenApiSecuritySchemeType.ApiKey,
        Name = "Authorization",
        In = OpenApiSecurityApiKeyLocation.Header,
        Description = "MVP: provide an Authorization header if your deployment requires it."
    });
});

// Persistence config from environment variables (platform-managed)
builder.Services.Configure<DbOptions>(_ =>
{
    _.MysqlUrl = Environment.GetEnvironmentVariable("MYSQL_URL") ?? string.Empty;
    _.MysqlUser = Environment.GetEnvironmentVariable("MYSQL_USER") ?? string.Empty;
    _.MysqlPassword = Environment.GetEnvironmentVariable("MYSQL_PASSWORD") ?? string.Empty;
    _.MysqlDb = Environment.GetEnvironmentVariable("MYSQL_DB") ?? string.Empty;
    _.MysqlPort = Environment.GetEnvironmentVariable("MYSQL_PORT") ?? string.Empty;
});

builder.Services.AddSingleton<IMySqlConnectionFactory, MySqlConnectionFactory>();
builder.Services.AddScoped<IOutageRepository, OutageRepository>();
builder.Services.AddScoped<IJobRepository, JobRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        // Prefer explicit origins in preview/production environments.
        // This avoids the brittle combination of AllowCredentials + wildcard origins.
        var allowedOriginsRaw = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? string.Empty;
        var allowedOrigins = allowedOriginsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else
        {
            // Development fallback if no env is provided.
            policy.SetIsOriginAllowed(_ => true)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

var app = builder.Build();

// Use CORS
app.UseCors("AllowAll");

// Configure OpenAPI/Swagger
app.UseOpenApi();
app.UseSwaggerUi(config =>
{
    config.Path = "/docs";
});

// --------------------
// Endpoints
// --------------------

// Health check endpoint
app.MapGet("/", () => Results.Ok(new { message = "Healthy" }))
   .WithName("Health")
   .WithTags("Health");

// Outages
app.MapGet("/api/outages", async (string organizationId, string? status, string? severity, int? limit, int? offset, IOutageRepository repo) =>
{
    limit ??= 50;
    offset ??= 0;
    var result = await repo.ListOutagesAsync(organizationId, status, severity, limit.Value, offset.Value);
    return Results.Ok(result);
})
.WithName("ListOutages")
.WithSummary("List outages for an organization")
.WithDescription("Returns paged outages filtered by optional status/severity.")
.WithTags("Outages");

app.MapGet("/api/outages/{outageId}", async (string outageId, IOutageRepository repo) =>
{
    var outage = await repo.GetOutageAsync(outageId);
    return outage is null ? Results.NotFound() : Results.Ok(outage);
})
.WithName("GetOutage")
.WithSummary("Get outage by id")
.WithTags("Outages");

app.MapPost("/api/outages", async (CreateOutageRequest request, IOutageRepository repo) =>
{
    var (outage, _) = await repo.CreateOutageAsync(request);
    return Results.Created($"/api/outages/{outage.Id}", outage);
})
.WithName("CreateOutage")
.WithSummary("Create a new outage intake record")
.WithDescription("Creates a location + outage and inserts an initial timeline update ('Outage reported').")
.WithTags("Outages");

app.MapPatch("/api/outages/{outageId}/status", async (string outageId, UpdateOutageStatusRequest request, IOutageRepository repo) =>
{
    var updated = await repo.UpdateOutageStatusAsync(outageId, request);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
})
.WithName("UpdateOutageStatus")
.WithSummary("Update outage status (with audit update)")
.WithTags("Outages", "Audit");

app.MapPost("/api/outages/{outageId}/notes", async (string outageId, AddOutageNoteRequest request, IOutageRepository repo) =>
{
    // Ensure outage exists
    var outage = await repo.GetOutageAsync(outageId);
    if (outage is null) return Results.NotFound();

    var note = await repo.AddOutageUpdateAsync(outageId, OutageUpdateType.note, request.Message, request.CreatedByUserId);
    return Results.Created($"/api/outages/{outageId}/updates/{note.Id}", note);
})
.WithName("AddOutageNote")
.WithSummary("Add a note to the outage timeline")
.WithTags("Outages", "Audit");

app.MapGet("/api/outages/{outageId}/updates", async (string outageId, int? limit, int? offset, IOutageRepository repo) =>
{
    limit ??= 50;
    offset ??= 0;
    var items = await repo.GetOutageUpdatesAsync(outageId, limit.Value, offset.Value);
    return Results.Ok(new PagedResult<OutageUpdateDto>(items, limit.Value, offset.Value, items.Count));
})
.WithName("ListOutageUpdates")
.WithSummary("List outage timeline updates")
.WithTags("Outages", "Audit");

// Jobs
app.MapGet("/api/jobs", async (string organizationId, string? status, string? outageId, int? limit, int? offset, IJobRepository repo) =>
{
    limit ??= 50;
    offset ??= 0;
    var result = await repo.ListJobsAsync(organizationId, status, outageId, limit.Value, offset.Value);
    return Results.Ok(result);
})
.WithName("ListJobs")
.WithSummary("List jobs for an organization")
.WithTags("Jobs");

app.MapGet("/api/jobs/{jobId}", async (string jobId, IJobRepository repo) =>
{
    var job = await repo.GetJobAsync(jobId);
    return job is null ? Results.NotFound() : Results.Ok(job);
})
.WithName("GetJob")
.WithSummary("Get job by id")
.WithTags("Jobs");

app.MapPost("/api/jobs", async (CreateJobRequest request, IJobRepository repo, IOutageRepository outageRepo) =>
{
    // Validate outage exists to avoid foreign key errors.
    var outage = await outageRepo.GetOutageAsync(request.OutageId);
    if (outage is null) return Results.BadRequest(new { error = "Invalid outageId" });

    var job = await repo.CreateJobAsync(request);
    return Results.Created($"/api/jobs/{job.Id}", job);
})
.WithName("CreateJob")
.WithSummary("Create a new job for an outage")
.WithTags("Jobs");

app.MapPatch("/api/jobs/{jobId}/assign", async (string jobId, AssignJobRequest request, IJobRepository repo) =>
{
    var job = await repo.AssignJobAsync(jobId, request);
    return job is null ? Results.NotFound() : Results.Ok(job);
})
.WithName("AssignJob")
.WithSummary("Assign job to crew/user (transitions to assigned)")
.WithTags("Jobs", "Dispatch", "Audit");

app.MapPatch("/api/jobs/{jobId}/status", async (string jobId, UpdateJobStatusRequest request, IJobRepository repo) =>
{
    var job = await repo.UpdateJobStatusAsync(jobId, request);
    return job is null ? Results.NotFound() : Results.Ok(job);
})
.WithName("UpdateJobStatus")
.WithSummary("Update job status (writes job_status_events audit)")
.WithTags("Jobs", "Audit");

app.MapGet("/api/jobs/{jobId}/events", async (string jobId, int? limit, int? offset, IJobRepository repo) =>
{
    limit ??= 50;
    offset ??= 0;
    var items = await repo.GetJobStatusEventsAsync(jobId, limit.Value, offset.Value);
    return Results.Ok(new PagedResult<JobStatusEventDto>(items, limit.Value, offset.Value, items.Count));
})
.WithName("ListJobStatusEvents")
.WithSummary("List job status audit events")
.WithTags("Jobs", "Audit");

// Customers
app.MapGet("/api/customers/{customerUserId}", async (string customerUserId, ICustomerRepository repo) =>
{
    var customer = await repo.GetCustomerByIdAsync(customerUserId);
    return customer is null ? Results.NotFound() : Results.Ok(customer);
})
.WithName("GetCustomer")
.WithSummary("Get a customer by user id")
.WithTags("Customers");

app.MapGet("/api/customers", async (string organizationId, string q, int? limit, int? offset, ICustomerRepository repo) =>
{
    limit ??= 50;
    offset ??= 0;
    var items = await repo.SearchCustomersAsync(organizationId, q, limit.Value, offset.Value);
    return Results.Ok(new PagedResult<CustomerDto>(items, limit.Value, offset.Value, items.Count));
})
.WithName("SearchCustomers")
.WithSummary("Search customers by name/email/phone")
.WithTags("Customers");

app.Run();
