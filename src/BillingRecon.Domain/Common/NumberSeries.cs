namespace BillingRecon.Domain.Common;

/// <summary>
/// The row behind one gapless series (BR-801, BR-802; docs/domain-model.md, section 4.11): for one tenant, one
/// document type and one fiscal year, the prefix of its numbers and the next number to hand out.
/// <para>
/// Nothing in the application creates a series or takes a number through this class. The row is created on first use
/// and advanced by the number series itself, in one locked statement inside the transaction of a posting (ADR 0008,
/// docs/database.md section 4.1); going through a loaded entity instead would read, wait and write in three steps and
/// lose the lock that makes the series gapless. The class is here so that the table is part of the model, with its
/// tenant filter, its foreign key and its migration, so that a series can be read, and for the one change made through
/// it: an approved prefix change (<see cref="ChangePrefix"/>, ADR 0033), which its row version guards.
/// </para>
/// </summary>
public sealed class NumberSeries : AggregateRoot<NumberSeriesId>, ITenantScoped, IHasRowVersion
{
    // The constructor Entity Framework uses to load a series; it then sets the remaining properties.
    private NumberSeries(string prefix)
    {
        Prefix = prefix;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The kind of document the series numbers.</summary>
    public DocumentType DocumentType { get; private set; }

    /// <summary>The fiscal year the series belongs to.</summary>
    public FiscalYear FiscalYear { get; private set; }

    /// <summary>The prefix of the numbers taken from now on: the tenant's default when the series was first used, or an approved change since (BR-802).</summary>
    public string Prefix { get; private set; }

    /// <summary>The number the next document gets. It starts at 1 and never decreases (BR-802).</summary>
    public int NextNumber { get; private set; }

    /// <summary>When the series was first used.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>When a number was last taken, or the prefix last changed.</summary>
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>For changes of the prefix only; taking a number relies on the lock, not on this (ADR 0008).</summary>
    public byte[] RowVersion { get; private set; }

    /// <summary>
    /// An approved prefix change (BR-802, ADR 0033): the numbers taken from now on carry <paramref name="prefix"/>; the
    /// numbers taken before keep theirs, and <see cref="NextNumber"/> goes on as it was.
    /// </summary>
    /// <exception cref="ArgumentException">Not a prefix, or the series has it already: bugs in the caller.</exception>
    public void ChangePrefix(string prefix, DateTimeOffset changedAtUtc)
    {
        if (!DocumentNumber.IsPrefix(prefix))
        {
            throw new ArgumentException($"A prefix has 1 to {DocumentNumber.PrefixMaxLength} characters, each a capital letter or a digit (BR-802).", nameof(prefix));
        }

        if (prefix == Prefix)
        {
            throw new ArgumentException("The series has this prefix already.", nameof(prefix));
        }

        var from = Prefix;
        Prefix = prefix;
        UpdatedAtUtc = changedAtUtc;
        AddDomainEvent(new NumberSeriesPrefixChanged(Id, DocumentType, FiscalYear, from, prefix));
    }
}
