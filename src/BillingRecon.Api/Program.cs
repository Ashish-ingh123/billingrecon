using System.Text.Json.Serialization;
using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Correlation;
using BillingRecon.Api.Common.Errors;
using BillingRecon.Api.Common.Idempotency;
using BillingRecon.Api.Common.Logging;
using BillingRecon.Application.Common;
using BillingRecon.Infrastructure.Audit;
using BillingRecon.Infrastructure.Billing;
using BillingRecon.Infrastructure.Common;
using BillingRecon.Infrastructure.Payers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Options validated at startup, structured logging, the clock, the identity of the request, the EF Core context with
// its unit of work, and the readiness check. Configuration comes from appsettings, environment variables and, in
// Development, user-secrets.
builder.Services.AddInfrastructure(builder.Environment);

// The ports of each business module, then every use case and validator of the Application layer (ADR 0018).
builder.Services.AddPayers();
builder.Services.AddBilling();
builder.Services.AddApplication();

// The audit trail: one record for every domain event, written by the unit of work in the transaction of the change
// (BR-701, ADR 0020), with the correlation id of the request (BR-703), which only the API knows.
builder.Services.AddAudit();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();

// The filter behind [Idempotent]: one per request, over the request's idempotency store (BR-803, ADR 0006).
builder.Services.AddScoped<IdempotencyFilter>();

builder.Services.AddControllers().AddJsonOptions(options =>
{
    // A body that cannot be read as JSON is answered with a generic message per JSON path. The serializer's own
    // messages name CLR types and line numbers, which are internals.
    options.AllowInputFormatterExceptionMessages = false;

    // Enums travel by name, in both directions. A number is refused: it depends on the order of the members, and one
    // the enum does not define would bind to a value nobody can name (docs/api.md).
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
});

// Error contract: every error is an RFC 9457 ProblemDetails with errorCode, traceId and correlationId.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsEnricher.Enrich);
builder.Services.AddSingleton<IProblemDetailsWriter, FallbackProblemDetailsWriter>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

// Outermost, so the correlation id exists for everything that follows, including error responses and the request log.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

// Authentication and authorisation go here when the Identity slice lands; the identity log scope must stay after them.
app.UseMiddleware<RequestIdentityLoggingMiddleware>();

if (app.Environment.IsDevelopment())
{
    // Built-in OpenAPI document at /openapi/v1.json and the Scalar UI at /scalar. Development only.
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapControllers();

// Liveness runs no checks: the process answers, so it is alive. Readiness runs the checks tagged for it: the
// application database is reachable and has no pending migrations.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains(InfrastructureServiceCollectionExtensions.ReadinessTag),
});

app.Run();
