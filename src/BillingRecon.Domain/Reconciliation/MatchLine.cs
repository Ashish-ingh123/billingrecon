using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>
/// A statement line inside a match, with the part of its amount the match takes (all of it in R2). <see cref="IsActive"/>
/// copies the match's status onto the row, so that a unique index on the active rows keeps a line in one active match
/// (BR-1029): an index can filter only on its own table's columns.
/// </summary>
public sealed class MatchLine : Entity<MatchLineId>, ITenantScoped
{
    // Entity Framework creates a line with this constructor and then sets the properties.
    private MatchLine()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The match. Set when the match is saved.</summary>
    public MatchId MatchId { get; private set; }

    /// <summary>The statement line.</summary>
    public StatementLineId StatementLineId { get; private set; }

    /// <summary>The amount of the line the match takes.</summary>
    public Money Amount { get; private set; }

    /// <summary>Whether the match stands; false once it is ended.</summary>
    public bool IsActive { get; private set; }

    internal static MatchLine Of(StatementLineId line, Money amount) => new() { StatementLineId = line, Amount = amount, IsActive = true };
}
