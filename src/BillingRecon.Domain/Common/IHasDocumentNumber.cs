namespace BillingRecon.Domain.Common;

/// <summary>
/// A domain event about a document that has its number: a posting, and everything that happens to the document
/// afterwards. The audit record of the event carries the number in a column of its own (BR-701), so that a person can
/// look a change up by the number printed on the document.
/// </summary>
public interface IHasDocumentNumber
{
    /// <summary>The number of the document, as it is printed: <c>BIL/26-27/000123</c>.</summary>
    public string DocumentNumber { get; }
}
