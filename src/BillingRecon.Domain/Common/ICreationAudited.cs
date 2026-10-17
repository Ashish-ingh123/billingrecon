namespace BillingRecon.Domain.Common;

/// <summary>
/// An entity that records when and by whom it was created. The persistence layer fills both members when the entity
/// is added and never writes them again (ADR 0015); application code never assigns them. Append-only tables
/// implement this interface only. The implementing class declares the properties under the same names, with
/// non-public setters.
/// </summary>
public interface ICreationAudited
{
    /// <summary>The instant the entity was created, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>The user who created it. Null when no user did: seeding, a migration or a background job.</summary>
    public UserId? CreatedByUserId { get; }
}
