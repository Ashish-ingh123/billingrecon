using BillingRecon.Domain.Billing;

namespace BillingRecon.Application.Billing;

/// <summary>What the credit note use cases share when they read the bill of a credit note.</summary>
internal static class CreditNoteBills
{
    /// <summary>The bill <paramref name="creditNote"/> is against. It exists: the credit note has a foreign key to it.</summary>
    /// <exception cref="InvalidOperationException">The bill is not there, which the foreign key forbids.</exception>
    public static async Task<Bill> GetOfAsync(this IBillRepository bills, CreditNote creditNote, CancellationToken ct)
        => await bills.GetAsync(creditNote.BillId, ct)
            ?? throw new InvalidOperationException($"Credit note {creditNote.Id} is against bill {creditNote.BillId}, which does not exist.");
}
