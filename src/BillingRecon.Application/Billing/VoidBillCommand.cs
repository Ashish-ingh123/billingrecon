using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>Voids a draft bill, with the reason (US-23, BR-204). <c>ExpectedVersion</c> is the version the client read (BR-804).</summary>
public sealed record VoidBillCommand(BillId Id, string ExpectedVersion, string Reason);
