using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reconciliation;

/// <summary>Reads one import of the tenant.</summary>
public sealed record GetStatementImportQuery(StatementImportId Id);
