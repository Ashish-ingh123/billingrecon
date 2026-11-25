using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>What a payer still owed at the cut-over on one bill of the system before.</summary>
public sealed record OpeningReceivable(string LegacyNumber, PatientId PatientId, PayerId PayerId, DateOnly BillDate, DateOnly DueDate, Money Outstanding);
