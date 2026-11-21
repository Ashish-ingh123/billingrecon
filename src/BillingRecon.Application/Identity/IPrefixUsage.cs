using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>
/// Whether a prefix belongs to another kind of document (ADR 0033, point 6): a prefix names one document type for good,
/// so that two documents of different kinds never carry the same number.
/// </summary>
public interface IPrefixUsage
{
    /// <summary>
    /// True when a document type other than <paramref name="documentType"/> has <paramref name="prefix"/> as its default,
    /// had it on any of its series, or had it, has it or waits for it in a prefix change.
    /// </summary>
    public Task<bool> IsTakenAsync(DocumentType documentType, string prefix, CancellationToken ct);
}
