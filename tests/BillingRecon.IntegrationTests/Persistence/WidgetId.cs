using BillingRecon.Domain.Common;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>A typed id that lives in the test assembly, to prove that typed ids are converted by convention.</summary>
public readonly record struct WidgetId(Guid Value) : ITypedId;
