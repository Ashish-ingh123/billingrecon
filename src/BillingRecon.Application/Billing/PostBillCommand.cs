using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>Posts a draft bill (US-21, BR-206). <c>ExpectedVersion</c> is the version the client read (BR-804).</summary>
public sealed record PostBillCommand(BillId Id, string ExpectedVersion);
