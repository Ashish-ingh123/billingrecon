using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// Gives a new entity its id at the moment it is added to the context: a sequential GUID, generated in the
/// application, wrapped in the entity's typed id (docs/database.md, section 1). The GUIDs come from Entity
/// Framework's generator, whose values increase in the order SQL Server sorts <c>uniqueidentifier</c>, so new rows go
/// to the end of the clustered index. An entity that already has an id keeps it. Entity Framework creates the
/// instances, which is why the class is public and has a parameterless constructor.
/// </summary>
/// <typeparam name="TId">The typed id.</typeparam>
public sealed class TypedIdValueGenerator<TId> : ValueGenerator<TId>
    where TId : struct, ITypedId
{
    private readonly SequentialGuidValueGenerator _guids = new();

    /// <summary>False: the generated id is the real one and is sent to the database.</summary>
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc />
    public override TId Next(EntityEntry entry) => TypedIdFactory<TId>.FromGuid(_guids.Next(entry));
}
