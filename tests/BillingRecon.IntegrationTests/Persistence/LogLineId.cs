using BillingRecon.Domain.Common;

namespace BillingRecon.IntegrationTests.Persistence;

public readonly record struct LogLineId(Guid Value) : ITypedId;
