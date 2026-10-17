using BillingRecon.Api.Common.Correlation;
using BillingRecon.Api.Common.Errors;
using BillingRecon.Api.Common.Logging;
using BillingRecon.Infrastructure.Common;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Options validated at startup, structured logging, the clock, the identity of the request, the EF Core context and
// the readiness check. Configuration comes from appsettings, environment variables and, in Development, user-secrets.
builder.Services.AddInfrastructure(builder.Environment);

// A body that cannot be read as JSON is answered with a generic message per JSON path. The serializer's own
// messages name CLR types and line numbers, which are internals.
builder.Services.AddControllers().AddJsonOptions(options => options.AllowInputFormatterExceptionMessages = false);

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
