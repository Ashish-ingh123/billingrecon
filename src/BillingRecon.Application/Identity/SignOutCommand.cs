namespace BillingRecon.Application.Identity;

/// <summary>The signed-in user signs out (ADR 0057). It names nobody: the user is the one of the request.</summary>
public sealed record SignOutCommand;
