using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using NSubstitute;

namespace BillingRecon.Application.Tests.Support;

/// <summary>Stand-ins for what the persistence layer and the clock provide, for use cases tested without them.</summary>
internal static class TestDoubles
{
    /// <summary>The business date the clocks of these tests show: 17 October 2026 in India.</summary>
    public static readonly DateOnly Today = new(2026, 10, 17);

    /// <summary>A clock that shows <paramref name="today"/> as the business date in India.</summary>
    public static IClock ClockAt(DateOnly today)
    {
        var clock = Substitute.For<IClock>();
        clock.TodayIst.Returns(today);
        clock.UtcNow.Returns(new DateTimeOffset(today.ToDateTime(new TimeOnly(6, 30)), TimeSpan.Zero));

        return clock;
    }

    /// <summary>
    /// <paramref name="entity"/> as if it had been saved: it carries the row version <paramref name="version"/>, which in
    /// the application only the database assigns (ADR 0007).
    /// </summary>
    public static TEntity Saved<TEntity>(this TEntity entity, params byte[] version)
        where TEntity : IHasRowVersion
    {
        typeof(TEntity).GetProperty(nameof(IHasRowVersion.RowVersion))!.SetValue(entity, version);

        return entity;
    }

    /// <summary>
    /// <paramref name="entity"/> as if it had been added to the unit of work: it carries the id <paramref name="id"/>,
    /// which in the application only the persistence layer assigns.
    /// </summary>
    public static TEntity WithId<TEntity, TId>(this TEntity entity, TId id)
        where TEntity : Entity<TId>
        where TId : struct, ITypedId
    {
        typeof(Entity<TId>).GetProperty(nameof(Entity<TId>.Id))!.SetValue(entity, id);

        return entity;
    }

    /// <summary>A unit of work whose commit answers <paramref name="outcome"/>, or succeeds.</summary>
    public static IUnitOfWork UnitOfWork(Result? outcome = null)
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.CommitAsync(Arg.Any<CancellationToken>()).Returns(outcome ?? Result.Success());

        return unitOfWork;
    }
}
