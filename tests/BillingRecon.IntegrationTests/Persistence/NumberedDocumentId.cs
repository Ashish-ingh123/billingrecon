using BillingRecon.Domain.Common;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>The typed id of the <see cref="NumberedDocument"/> probe.</summary>
public readonly record struct NumberedDocumentId(Guid Value) : ITypedId;
