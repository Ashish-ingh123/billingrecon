using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Audit;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using NSubstitute;

namespace BillingRecon.Application.Tests.Audit;

/// <summary>
/// The generic audit subscriber (BR-701, BR-703): one record per domain event, built from what the unit of work hands
/// over and from the scope, without knowing any event by name; and the action it derives from the name of the event.
/// </summary>
public sealed class AuditSubscriberTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 20, 6, 30, 0, TimeSpan.Zero);
    private static readonly UserId Clerk = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3304"));
    private static readonly Guid Correlation = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3305");
    private static readonly Guid Entity = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3306");

    [Fact]
    public async Task BR701_one_event_becomes_one_record_with_who_when_the_request_the_entity_the_action_and_both_images()
    {
        var written = new List<AuditRecord>();
        var subscriber = Subscriber(written, Clerk);
        var occurrence = new DomainEventOccurrence(
            new PatientDeactivated(new PatientId(Entity), Mrn.Of("MRN-1")), "Patient", Entity, At, """{"Status":"Active"}""", """{"Status":"Inactive"}""");

        await subscriber.HandleAsync(occurrence, CancellationToken.None);

        var record = written.ShouldHaveSingleItem();
        record.OccurredAtUtc.ShouldBe(At);
        record.UserId.ShouldBe(Clerk);
        record.CorrelationId.ShouldBe(Correlation);
        record.EntityType.ShouldBe("Patient");
        record.EntityId.ShouldBe(Entity);
        record.Action.ShouldBe("Deactivated");
        record.BeforeJson.ShouldBe("""{"Status":"Active"}""");
        record.AfterJson.ShouldBe("""{"Status":"Inactive"}""");
        record.Reason.ShouldBeNull();
        record.DocumentNumber.ShouldBeNull();
    }

    [Fact]
    public async Task An_event_that_carries_a_reason_gives_it_to_its_record()
    {
        var written = new List<AuditRecord>();
        var voided = new BillVoided(new BillId(Entity), new PatientId(Guid.NewGuid()), new PayerId(Guid.NewGuid()), Money.Of(590m), "Entered for the wrong patient");

        await Subscriber(written, Clerk).HandleAsync(new DomainEventOccurrence(voided, "Bill", Entity, At, "{}", "{}"), CancellationToken.None);

        var record = written.ShouldHaveSingleItem();
        record.Action.ShouldBe("Voided");
        record.Reason.ShouldBe("Entered for the wrong patient");
    }

    [Fact]
    public async Task System_work_has_no_user_and_a_creation_no_before_image()
    {
        var written = new List<AuditRecord>();

        await Subscriber(written, user: null).HandleAsync(
            new DomainEventOccurrence(new PatientRegistered(Mrn.Of("MRN-1")), "Patient", Entity, At, BeforeJson: null, "{}"),
            CancellationToken.None);

        var record = written.ShouldHaveSingleItem();
        record.UserId.ShouldBeNull();
        record.BeforeJson.ShouldBeNull();
        record.Action.ShouldBe("Registered");
    }

    [Theory]
    [InlineData(typeof(PatientRegistered), "Patient", "Registered")]
    [InlineData(typeof(PatientUpdated), "Patient", "Updated")]
    [InlineData(typeof(PayerCreated), "Payer", "Created")]
    [InlineData(typeof(ServiceItemReactivated), "ServiceItem", "Reactivated")]
    [InlineData(typeof(BillDrafted), "Bill", "Drafted")]
    [InlineData(typeof(BillHeaderUpdated), "Bill", "HeaderUpdated")]
    [InlineData(typeof(BillLinesReplaced), "Bill", "LinesReplaced")]
    [InlineData(typeof(BillVoided), "Bill", "Voided")]
    public void The_action_is_the_name_of_the_event_without_the_name_of_its_aggregate(Type raised, string entityType, string action)
    {
        AuditAction.For(raised, entityType).ShouldBe(action);
    }

    [Fact]
    public void An_event_that_is_not_named_after_its_aggregate_keeps_its_whole_name()
    {
        AuditAction.For(typeof(PatientRegistered), "Bill").ShouldBe("PatientRegistered");
        AuditAction.For(typeof(BillVoided), "BillVoided").ShouldBe("BillVoided", "an action is never empty");
    }

    [Fact]
    public void An_action_longer_than_its_column_is_a_bug_and_throws()
    {
        Should.Throw<InvalidOperationException>(() => AuditAction.For(typeof(AnEventWhoseNameIsFarTooLongToFitTheActionColumnOfTheAuditTable), "Bill"))
            .Message.ShouldContain("shorter name");
    }

    [Fact]
    public void Every_event_of_the_domain_gives_an_action_that_fits_its_column()
    {
        var domain = typeof(Domain.AssemblyMarker).Assembly;
        var aggregates = domain.GetTypes().Where(type => typeof(IHasDomainEvents).IsAssignableFrom(type) && !type.IsAbstract).Select(type => type.Name).ToArray();
        var events = domain.GetTypes().Where(type => typeof(IDomainEvent).IsAssignableFrom(type) && !type.IsInterface).ToArray();

        events.ShouldNotBeEmpty();
        foreach (var raised in events)
        {
            // The longest aggregate name the event starts with is its own: ServiceItem before any shorter one.
            var aggregate = aggregates.Where(name => raised.Name.StartsWith(name, StringComparison.Ordinal)).OrderByDescending(name => name.Length).First();

            AuditAction.For(raised, aggregate).Length.ShouldBeInRange(1, AuditRecord.ActionMaxLength, raised.Name);
        }
    }

    private static AuditSubscriber Subscriber(List<AuditRecord> written, UserId? user)
    {
        var writer = Substitute.For<IAuditWriter>();
        writer.When(port => port.Append(Arg.Any<AuditRecord>())).Do(call => written.Add(call.Arg<AuditRecord>()));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(user);

        var correlation = Substitute.For<ICorrelationContext>();
        correlation.CorrelationId.Returns(Correlation);

        return new AuditSubscriber(writer, currentUser, correlation);
    }

    private sealed record AnEventWhoseNameIsFarTooLongToFitTheActionColumnOfTheAuditTable : IDomainEvent;
}
