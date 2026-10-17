using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Common;

public sealed class AggregateRootTests
{
    [Fact]
    public void A_new_aggregate_has_no_events()
    {
        new Counter().DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Events_are_collected_in_the_order_they_were_raised()
    {
        var counter = new Counter();

        counter.Increment();
        counter.Increment();

        counter.DomainEvents.ShouldBe([new Incremented(1), new Incremented(2)]);
    }

    [Fact]
    public void Clearing_forgets_the_collected_events()
    {
        var counter = new Counter();
        counter.Increment();

        counter.ClearDomainEvents();

        counter.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void The_collected_events_cannot_be_changed_from_outside()
    {
        var counter = new Counter();
        counter.Increment();

        var events = (ICollection<IDomainEvent>)counter.DomainEvents;

        events.IsReadOnly.ShouldBeTrue();
        Should.Throw<NotSupportedException>(events.Clear);
    }

    [Fact]
    public void Raising_no_event_is_a_bug_and_throws()
    {
        Should.Throw<ArgumentNullException>(() => new Counter().RaiseNothing());
    }

    private sealed record Incremented(int Value) : IDomainEvent;

    private sealed class Counter : AggregateRoot<TenantId>
    {
        private int _value;

        public void Increment() => AddDomainEvent(new Incremented(++_value));

        public void RaiseNothing() => AddDomainEvent(null!);
    }
}
