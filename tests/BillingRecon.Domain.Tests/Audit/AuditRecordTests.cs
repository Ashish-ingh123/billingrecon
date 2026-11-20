using BillingRecon.Domain.Audit;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Audit;

/// <summary>
/// The audit record (BR-701 to BR-703): created complete, with who, when, the request and the record that changed,
/// and marked as a row that is written once.
/// </summary>
public sealed class AuditRecordTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 20, 6, 30, 0, TimeSpan.Zero);
    private static readonly UserId Clerk = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3304"));
    private static readonly Guid Correlation = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3305");
    private static readonly Guid Entity = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3306");

    [Fact]
    public void BR701_a_record_carries_who_when_the_request_the_entity_the_action_and_both_images()
    {
        var record = AuditRecord.Create(At, Clerk, Correlation, "Bill", Entity, "Voided", """{"Status":"Draft"}""", """{"Status":"Voided"}""", "BIL/26-27/000001", " Duplicate ");

        record.OccurredAtUtc.ShouldBe(At);
        record.UserId.ShouldBe(Clerk);
        record.CorrelationId.ShouldBe(Correlation);
        record.EntityType.ShouldBe("Bill");
        record.EntityId.ShouldBe(Entity);
        record.Action.ShouldBe("Voided");
        record.BeforeJson.ShouldBe("""{"Status":"Draft"}""");
        record.AfterJson.ShouldBe("""{"Status":"Voided"}""");
        record.DocumentNumber.ShouldBe("BIL/26-27/000001");
        record.Reason.ShouldBe("Duplicate", "text is stored without surrounding white space");
    }

    [Fact]
    public void A_record_of_a_creation_has_no_before_image_and_system_work_has_no_user()
    {
        var record = AuditRecord.Create(At, userId: null, Correlation, "Patient", Entity, "Registered", beforeJson: null, "{}");

        record.BeforeJson.ShouldBeNull();
        record.UserId.ShouldBeNull();
        record.DocumentNumber.ShouldBeNull();
        record.Reason.ShouldBeNull();
    }

    [Fact]
    public void A_new_record_has_no_number_and_no_tenant_until_it_is_saved()
    {
        var record = AuditRecord.Create(At, Clerk, Correlation, "Patient", Entity, "Registered", null, "{}");

        record.Id.ShouldBe(0);
        record.TenantId.ShouldBe(default);
    }

    [Fact]
    public void BR702_an_audit_record_is_append_only_and_cannot_be_changed_through_its_type()
    {
        typeof(IAppendOnly).IsAssignableFrom(typeof(AuditRecord)).ShouldBeTrue();
        typeof(IHardDeletable).IsAssignableFrom(typeof(AuditRecord)).ShouldBeFalse();

        // Nothing public changes a record once it exists: no public setter, and no method besides the factory.
        typeof(AuditRecord).GetProperties().ShouldAllBe(property => property.SetMethod == null || !property.SetMethod.IsPublic);
        typeof(AuditRecord).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ShouldBeEmpty();
    }

    [Fact]
    public void BR703_a_record_without_a_correlation_id_or_without_an_entity_is_a_bug_and_throws()
    {
        Should.Throw<ArgumentException>(() => AuditRecord.Create(At, Clerk, Guid.Empty, "Patient", Entity, "Registered", null, "{}"));
        Should.Throw<ArgumentException>(() => AuditRecord.Create(At, Clerk, Correlation, "Patient", Guid.Empty, "Registered", null, "{}"));
        Should.Throw<ArgumentException>(() => AuditRecord.Create(At, Clerk, Correlation, " ", Entity, "Registered", null, "{}"));
        Should.Throw<ArgumentException>(() => AuditRecord.Create(At, Clerk, Correlation, "Patient", Entity, " ", null, "{}"));
        Should.Throw<ArgumentException>(() => AuditRecord.Create(At, Clerk, Correlation, "Patient", Entity, "Registered", null, " "));
    }

    [Fact]
    public void A_value_longer_than_its_column_is_a_bug_and_throws()
    {
        Should.Throw<ArgumentException>(() => AuditRecord.Create(At, Clerk, Correlation, new string('E', AuditRecord.EntityTypeMaxLength + 1), Entity, "Registered", null, "{}"));
        Should.Throw<ArgumentException>(() => AuditRecord.Create(At, Clerk, Correlation, "Patient", Entity, new string('A', AuditRecord.ActionMaxLength + 1), null, "{}"));
        Should.Throw<ArgumentException>(() => AuditRecord.Create(At, Clerk, Correlation, "Patient", Entity, "Registered", null, "{}", new string('1', AuditRecord.DocumentNumberMaxLength + 1)));
        Should.Throw<ArgumentException>(() => AuditRecord.Create(At, Clerk, Correlation, "Patient", Entity, "Registered", null, "{}", reason: new string('r', AuditRecord.ReasonMaxLength + 1)));
    }

    [Fact]
    public void Every_domain_event_is_a_sealed_record_named_after_the_aggregate_that_raises_it()
    {
        // The audit trail derives its action from the name: PatientRegistered on a Patient is Registered.
        var domain = typeof(AssemblyMarker).Assembly;
        var aggregates = domain.GetTypes().Where(type => typeof(IHasDomainEvents).IsAssignableFrom(type) && !type.IsAbstract).Select(type => type.Name).ToArray();
        var events = domain.GetTypes().Where(type => typeof(IDomainEvent).IsAssignableFrom(type) && !type.IsInterface).ToArray();

        events.ShouldNotBeEmpty();
        foreach (var raised in events)
        {
            raised.IsSealed.ShouldBeTrue($"{raised.Name} is not sealed");
            aggregates.Any(aggregate => raised.Name.StartsWith(aggregate, StringComparison.Ordinal) && raised.Name.Length > aggregate.Length)
                .ShouldBeTrue($"{raised.Name} does not start with the name of an aggregate");
        }
    }
}
