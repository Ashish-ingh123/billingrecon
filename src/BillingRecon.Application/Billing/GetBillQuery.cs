using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>Reads one bill of the tenant, with its lines.</summary>
public sealed record GetBillQuery(BillId Id);
