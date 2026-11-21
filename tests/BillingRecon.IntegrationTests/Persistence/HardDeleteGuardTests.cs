using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Payers;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// Nothing is ever deleted (CLAUDE.md section 4, rule 10; BR-005): the guard refuses a save that would delete a row,
/// unless the entity is marked as deletable, which in production only a line of a draft bill is. Tested without a
/// database: the guard looks at the change tracker.
/// </summary>
public sealed class HardDeleteGuardTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 6, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Deleting_a_patient_a_payer_a_service_item_a_bill_or_a_tenant_is_refused()
    {
        object[] neverDeleted =
        [
            Patient.Register(Mrn.Of("MRN-1"), new PatientDetails { FullName = "Priya Sharma" }),
            Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "Rahul Sharma" }, patientId: null).Value,
            ServiceItem.Create(ServiceItemCode.Of("CONS-OPD"), new ServiceItemDetails { Name = "OPD consultation", DefaultUnitPrice = UnitPrice.Of(500m), TaxRate = Percent.Of(18m) }),
            NewBill(),
            Tenant.Create("city-hospital", "City Hospital", Now).Value,
        ];

        foreach (var entity in neverDeleted)
        {
            using var context = TestContexts.CreateProduction(FixedIdentity.Random());
            context.Entry(entity).State = EntityState.Deleted;

            var exception = Should.Throw<InvalidOperationException>(() => HardDeleteGuardInterceptor.Apply(context));

            exception.Message.ShouldContain($"Cannot delete {entity.GetType().Name}");
        }
    }

    [Fact]
    public void The_message_names_the_entity_and_its_key_and_nothing_else_of_it()
    {
        using var context = TestContexts.CreateProduction(FixedIdentity.Random());
        context.Entry(Patient.Register(Mrn.Of("MRN-SECRET"), new PatientDetails { FullName = "Priya Sharma" })).State = EntityState.Deleted;

        var message = Should.Throw<InvalidOperationException>(() => HardDeleteGuardInterceptor.Apply(context)).Message;

        message.ShouldContain("Patient (Id ");
        message.ShouldNotContain("MRN-SECRET");
        message.ShouldNotContain("Priya");
    }

    [Fact]
    public void A_line_of_a_bill_is_the_one_row_that_may_be_deleted()
    {
        using var context = TestContexts.CreateProduction(FixedIdentity.Random());
        var bill = NewBill();
        context.Entry(bill.Lines[0]).State = EntityState.Deleted;

        Should.NotThrow(() => HardDeleteGuardInterceptor.Apply(context));
        typeof(IHardDeletable).IsAssignableFrom(typeof(BillLine)).ShouldBeTrue();
    }

    [Fact]
    public void In_the_production_model_only_the_lines_of_drafts_and_an_idempotency_key_are_deletable()
    {
        using var context = TestContexts.CreateProduction();

        var deletable = context.Model.GetEntityTypes().Select(entityType => entityType.ClrType).Where(type => typeof(IHardDeletable).IsAssignableFrom(type));

        // A line of a draft bill (ADR 0019) and, since Prompt 4.3, a line of a draft credit note (ADR 0025), and the record of an idempotency key, which is a record of requests and
        // not of money: a reservation that came to nothing is removed, and old keys are swept (ADR 0006). Since Prompt 5.1, a role a
        // user no longer holds (ADR 0030): the user's audit trail keeps every change.
        deletable.ShouldBe([typeof(BillLine), typeof(CreditNoteLine), typeof(IdempotencyKey), typeof(UserRole)], ignoreOrder: true);
    }

    [Fact]
    public void Added_modified_and_unchanged_entities_are_none_of_the_guards_business()
    {
        foreach (var state in new[] { EntityState.Added, EntityState.Modified, EntityState.Unchanged })
        {
            using var context = TestContexts.CreateProduction(FixedIdentity.Random());
            context.Entry(Patient.Register(Mrn.Of("MRN-1"), new PatientDetails { FullName = "Priya Sharma" })).State = state;

            Should.NotThrow(() => HardDeleteGuardInterceptor.Apply(context));
        }

        Should.NotThrow(() => HardDeleteGuardInterceptor.Apply(null));
    }

    [Fact]
    public void The_guard_is_part_of_every_save_of_the_context_however_it_was_created()
    {
        // A context built by hand, as a test or a script would: the guard runs before any command reaches a database,
        // so the connection string nothing listens on is never used. The payer is the current tenant's own, so the
        // tenant guard has nothing to say and this guard is the one that answers.
        var identity = FixedIdentity.Random();
        using var context = TestContexts.CreateProduction(identity);
        var payer = context.TrackExisting(Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "Rahul Sharma" }, patientId: null).Value, identity.TenantId!.Value);
        context.Remove(payer);

        Should.Throw<InvalidOperationException>(() => context.SaveChanges()).Message.ShouldContain("Cannot delete Payer");
    }

    private static Bill NewBill() => Bill.Create(
        new PatientId(Guid.NewGuid()),
        new PayerId(Guid.NewGuid()),
        new BillHeader { BillDate = Today, DueDate = Today },
        [new BillLineDetails { Description = "Dressing", Quantity = Quantity.Of(2m), UnitPrice = UnitPrice.Of(60m), TaxRate = Percent.Of(5m) }],
        new BusinessDateWindow(Today, 7)).Value;
}
