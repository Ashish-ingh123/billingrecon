using BillingRecon.Domain.Common;

namespace BillingRecon.IntegrationTests.Persistence;

public readonly record struct SettingId(Guid Value) : ITypedId;
