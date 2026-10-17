namespace BillingRecon.Domain.Common;

/// <summary>
/// An entity that records when and by whom it was last changed. The persistence layer fills both members on every
/// save that inserts or updates the row (ADR 0015), so on a new row they equal the creation values. Application
/// code never assigns them. This is the "last touched" stamp; the full history is in the audit records (BR-701).
/// The implementing class declares the properties under the same names, with non-public setters.
/// </summary>
public interface IModificationAudited
{
    /// <summary>The instant of the last insert or update of the row, in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; }

    /// <summary>The user who made that change. Null when no user did: seeding, a migration or a background job.</summary>
    public UserId? UpdatedByUserId { get; }
}
