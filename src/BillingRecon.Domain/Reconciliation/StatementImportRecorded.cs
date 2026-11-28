using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>
/// A bank's statement file was imported into a channel (BR-1003, BR-1004): one event per import, with its counts, not
/// one per line, so that the audit trail keeps one record of the import (docs/domain-model.md section 11.2). It carries
/// no id of the import, which is assigned when the import is added to the unit of work.
/// </summary>
public sealed record StatementImportRecorded(SettlementChannelId ChannelId, string FileName, int LinesAdded, int LinesAlreadyHeld, int RowsRefused) : IDomainEvent;
