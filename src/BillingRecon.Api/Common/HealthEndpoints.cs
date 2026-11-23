namespace BillingRecon.Api.Common;

/// <summary>
/// The two health endpoints, which an orchestrator, the image's HEALTHCHECK and the CI smoke run call from inside, over
/// plain HTTP and by whatever host name reaches the container. So they need no token (ADR 0031) and are answered for any
/// host and without HTTPS (ADR 0035); they tell nothing but "Healthy" or "Unhealthy".
/// </summary>
public static class HealthEndpoints
{
    /// <summary>Liveness: the process answers.</summary>
    public const string Live = "/health/live";

    /// <summary>Readiness: the application database is reachable and has no pending migration.</summary>
    public const string Ready = "/health/ready";

    private static readonly PathString LivePath = new(Live);
    private static readonly PathString ReadyPath = new(Ready);

    /// <summary>True for a request to one of the two health endpoints (the comparison ignores case, as routing does).</summary>
    public static bool IsProbe(PathString path) => path.Equals(LivePath) || path.Equals(ReadyPath);
}
