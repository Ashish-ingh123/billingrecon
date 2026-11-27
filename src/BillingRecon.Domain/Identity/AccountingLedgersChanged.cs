using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A hospital's names for its accountant's ledgers were saved or changed (ADR 0062).</summary>
public sealed record AccountingLedgersChanged : IDomainEvent;
