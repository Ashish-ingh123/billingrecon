using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// Stores a typed id as its <see cref="Guid"/> (<c>uniqueidentifier</c>). <see cref="BillingReconDbContext"/>
/// registers one for every typed id by convention, so no entity configuration mentions it. Entity Framework creates
/// the instances, which is why the class is public and has a parameterless constructor.
/// </summary>
/// <typeparam name="TId">The typed id.</typeparam>
public sealed class TypedIdConverter<TId> : ValueConverter<TId, Guid>
    where TId : struct, ITypedId
{
    /// <summary>Creates the converter.</summary>
    public TypedIdConverter()
        : base(TypedIdFactory<TId>.ToGuidExpression, TypedIdFactory<TId>.FromGuidExpression)
    {
    }
}
