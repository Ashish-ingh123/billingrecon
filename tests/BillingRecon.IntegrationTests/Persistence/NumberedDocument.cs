using BillingRecon.Domain.Common;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// A probe document that carries a number, as bills and payments will: <c>Number</c>, <c>FiscalYear</c> and
/// <c>SequenceNumber</c>, with the two unique indexes of docs/database.md section 3 on them. No production document
/// uses the number series yet (Prompt 3.1), so the tests of the series save one of these in the same unit of work as
/// the number they took, and run the gapless verification query of section 4.3 against its table.
/// </summary>
public sealed class NumberedDocument : AggregateRoot<NumberedDocumentId>, ITenantScoped, IHasRowVersion
{
    private NumberedDocument(string number, string title)
    {
        Number = number;
        Title = title;
        RowVersion = [];
    }

    /// <summary>A document numbered <paramref name="number"/>.</summary>
    public NumberedDocument(DocumentNumber number, string title)
        : this(number.Value, title)
    {
        DocumentType = number.Type;
        FiscalYear = number.FiscalYear;
        SequenceNumber = number.Sequence;
    }

    public TenantId TenantId { get; private set; }

    public DocumentType DocumentType { get; private set; }

    public string Number { get; private set; }

    public FiscalYear FiscalYear { get; private set; }

    public int SequenceNumber { get; private set; }

    public string Title { get; private set; }

    public byte[] RowVersion { get; private set; }
}
