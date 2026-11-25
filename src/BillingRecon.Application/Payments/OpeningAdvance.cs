using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>
/// Money of a payer the system before held at the cut-over and had not applied: an advance, or a credit balance, which is
/// brought in the same way and says so in its notes. <see cref="Method"/> is one of <see cref="IOpeningAdvances.Methods"/>.
/// </summary>
public sealed record OpeningAdvance(string LegacyNumber, PayerId PayerId, DateOnly ReceivedDate, string Method, Money Amount, bool CreditBalance);
