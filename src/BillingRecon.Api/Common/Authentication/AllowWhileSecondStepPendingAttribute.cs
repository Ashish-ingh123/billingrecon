namespace BillingRecon.Api.Common.Authentication;

/// <summary>Marks an action a token may reach that waits for the code of a second step (ADR 0061): giving the code, and signing out.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AllowWhileSecondStepPendingAttribute : Attribute;
