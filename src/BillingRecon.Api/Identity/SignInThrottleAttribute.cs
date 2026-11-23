using Microsoft.AspNetCore.Mvc.Filters;

namespace BillingRecon.Api.Identity;

/// <summary>
/// Puts the per-account limit of <see cref="SignInThrottleFilter"/> on an action (ADR 0035). The sign-in carries it,
/// together with the rate limiting policy <see cref="SignInLimits.PolicyName"/>; an architecture test keeps both to the
/// sign-in.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SignInThrottleAttribute : Attribute, IFilterFactory
{
    /// <summary>False: the filter is resolved per request.</summary>
    public bool IsReusable => false;

    /// <inheritdoc />
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) => serviceProvider.GetRequiredService<SignInThrottleFilter>();
}
