using System.Linq.Expressions;
using BillingRecon.Domain.Common;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// Turns a <see cref="Guid"/> into a typed id and back without knowing the concrete type, by way of the constructor
/// and the <c>Value</c> property every typed id has (<c>readonly record struct XId(Guid Value)</c>).
/// </summary>
/// <typeparam name="TId">The typed id.</typeparam>
internal static class TypedIdFactory<TId>
    where TId : struct, ITypedId
{
    /// <summary><c>id =&gt; id.Value</c>, as an expression Entity Framework can use in a value converter.</summary>
    public static readonly Expression<Func<TId, Guid>> ToGuidExpression = BuildToGuid();

    /// <summary><c>value =&gt; new TId(value)</c>, as an expression Entity Framework can use in a value converter.</summary>
    public static readonly Expression<Func<Guid, TId>> FromGuidExpression = BuildFromGuid();

    /// <summary>The compiled form of <see cref="FromGuidExpression"/>.</summary>
    public static readonly Func<Guid, TId> FromGuid = FromGuidExpression.Compile();

    private static Expression<Func<TId, Guid>> BuildToGuid()
    {
        var id = Expression.Parameter(typeof(TId), "id");

        return Expression.Lambda<Func<TId, Guid>>(Expression.Property(id, nameof(ITypedId.Value)), id);
    }

    private static Expression<Func<Guid, TId>> BuildFromGuid()
    {
        var constructor = typeof(TId).GetConstructor([typeof(Guid)])
            ?? throw new InvalidOperationException(
                $"{typeof(TId).Name} is not a valid typed id: it needs a public constructor that takes the Guid (readonly record struct {typeof(TId).Name}(Guid Value)).");
        var value = Expression.Parameter(typeof(Guid), "value");

        return Expression.Lambda<Func<Guid, TId>>(Expression.New(constructor, value), value);
    }
}
