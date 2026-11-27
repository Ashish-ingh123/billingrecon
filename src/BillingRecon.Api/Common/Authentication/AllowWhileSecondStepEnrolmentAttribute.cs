namespace BillingRecon.Api.Common.Authentication;

/// <summary>Marks an action a token may reach that must enrol a second step first (ADR 0061): the enrolment, and signing out.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AllowWhileSecondStepEnrolmentAttribute : Attribute;
