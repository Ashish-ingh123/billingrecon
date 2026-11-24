namespace BillingRecon.Api.Common.Authentication;

/// <summary>
/// Marks the one action a user may call while a password change is due: the change of one's own password (ADR 0051).
/// <see cref="PasswordChangeGateMiddleware"/> answers 403 <c>password_change_required</c> to every other.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AllowWhilePasswordChangeDueAttribute : Attribute;
