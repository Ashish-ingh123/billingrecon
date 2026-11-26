using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Payments;

/// <summary>
/// The opening advances of an opening batch (ADR 0053): posted payments of the opening series, added to the context of
/// the approval, which saves them with the batch.
/// </summary>
public sealed class OpeningAdvances(BillingReconDbContext context) : IOpeningAdvances
{
    private const string AdvanceNote = "Opening advance, brought forward at the cut-over.";
    private const string CreditNote = "Opening credit balance, brought forward at the cut-over.";

    /// <inheritdoc />
    public IReadOnlyList<string> Methods { get; } = Enum.GetNames<PaymentMethod>();

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> FindBroughtForwardAsync(IReadOnlyCollection<string> legacyNumbers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(legacyNumbers);

        var wanted = legacyNumbers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var found = await context.Set<Payment>()
            .AsNoTracking()
            .Where(payment => payment.LegacyNumber != null && wanted.Contains(payment.LegacyNumber))
            .Select(payment => payment.LegacyNumber!)
            .ToListAsync(ct);

        return found.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public PaymentId BringForward(OpeningAdvance advance, DocumentNumber number)
    {
        ArgumentNullException.ThrowIfNull(advance);

        var payment = Payment.BringForward(
            advance.PayerId,
            advance.ReceivedDate,
            Enum.Parse<PaymentMethod>(advance.Method),
            advance.Amount,
            advance.LegacyNumber,
            advance.CreditBalance ? CreditNote : AdvanceNote,
            number);

        // Adding the payment gives it its id.
        context.Set<Payment>().Add(payment);

        return payment.Id;
    }

    /// <inheritdoc />
    public void AttachPosting(PaymentId paymentId, LedgerPostingId postingId)
    {
        var payment = context.Set<Payment>().Local.SingleOrDefault(candidate => candidate.Id == paymentId)
            ?? throw new InvalidOperationException($"Opening advance {paymentId} was not added in this unit of work.");

        payment.AttachPosting(postingId);
    }
}
