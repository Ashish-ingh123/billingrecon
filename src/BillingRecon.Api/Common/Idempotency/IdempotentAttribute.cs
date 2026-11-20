using Microsoft.AspNetCore.Mvc.Filters;

namespace BillingRecon.Api.Common.Idempotency;

/// <summary>
/// Marks an action as a financial POST that must carry an <c>Idempotency-Key</c> (CLAUDE.md section 4, rule 5;
/// BR-803; ADR 0006). The action then runs at most once per key: a retry is answered with the stored response of the
/// first request, and the response of a success is stored in the same transaction as the work it reports.
/// <para>
/// Opt-in per action. An architecture test lists the controllers whose POSTs are financial and fails for one that
/// lacks the attribute.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IFilterFactory
{
    /// <summary>False: the filter holds the state of one request.</summary>
    public bool IsReusable => false;

    /// <inheritdoc />
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) => serviceProvider.GetRequiredService<IdempotencyFilter>();
}
