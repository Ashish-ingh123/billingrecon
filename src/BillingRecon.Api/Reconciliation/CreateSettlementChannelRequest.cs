using System.Text.Json.Serialization;
using BillingRecon.Application.Reconciliation;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Api.Reconciliation;

/// <summary>
/// The body of <c>POST /api/v1/settlement-channels</c> (BR-1001, BR-1002). A bank sends its mapping; a gateway sends its
/// gateway and the bank channel it settles into. A member the API does not know is refused with 400.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateSettlementChannelRequest
{
    /// <summary>Bank or Gateway. It cannot be changed later.</summary>
    public SettlementChannelKind? Kind { get; init; }

    /// <summary>How people name the account, unique in the hospital.</summary>
    public string? Name { get; init; }

    /// <summary>At most the last four digits of the account or merchant number, never the full number.</summary>
    public string? AccountLabel { get; init; }

    /// <summary>Days before a line an item may be dated and still match it, 0 to 10; 3 when left out.</summary>
    public int? DateWindowDays { get; init; }

    /// <summary>The gateway of a gateway channel: Razorpay. It cannot be changed later.</summary>
    public PaymentGateway? Gateway { get; init; }

    /// <summary>The bank channel a gateway settles into.</summary>
    public Guid? BankChannelId { get; init; }

    /// <summary>How a bank's CSV is read.</summary>
    public ColumnMappingDto? Mapping { get; init; }

    /// <summary>The command; a missing name becomes empty, which the validator reports.</summary>
    public CreateSettlementChannelCommand ToCommand() => new()
    {
        Kind = Kind,
        Name = Name ?? string.Empty,
        AccountLabel = AccountLabel,
        DateWindowDays = DateWindowDays,
        Gateway = Gateway,
        BankChannelId = BankChannelId,
        Mapping = Mapping,
    };
}
