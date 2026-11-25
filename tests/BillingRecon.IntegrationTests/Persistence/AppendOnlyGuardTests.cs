using BillingRecon.Domain.Audit;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// Audit records are written once (BR-702; CLAUDE.md section 4, rule 11): the guard refuses a save that would update
/// or delete a row of an append-only entity. Tested without a database: the guard looks at the change tracker.
/// </summary>
public sealed class AppendOnlyGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 6, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(EntityState.Modified, "Cannot update AuditRecord")]
    [InlineData(EntityState.Deleted, "Cannot delete AuditRecord")]
    public void BR702_updating_or_deleting_an_audit_record_is_refused(EntityState state, string message)
    {
        using var context = TestContexts.CreateProduction(FixedIdentity.Random());
        context.Entry(NewRecord()).State = state;

        Should.Throw<InvalidOperationException>(() => AppendOnlyGuardInterceptor.Apply(context)).Message.ShouldContain(message);
    }

    [Fact]
    public void The_message_names_the_entity_and_its_key_and_nothing_else_of_it()
    {
        using var context = TestContexts.CreateProduction(FixedIdentity.Random());
        var entry = context.Entry(NewRecord());
        entry.Property(record => record.Id).CurrentValue = 42;
        entry.State = EntityState.Modified;

        var message = Should.Throw<InvalidOperationException>(() => AppendOnlyGuardInterceptor.Apply(context)).Message;

        message.ShouldContain("AuditRecord (Id 42)");
        message.ShouldNotContain("Priya");
        message.ShouldNotContain("Entered twice");
    }

    [Fact]
    public void Adding_a_record_and_reading_one_are_none_of_the_guards_business()
    {
        foreach (var state in new[] { EntityState.Added, EntityState.Unchanged })
        {
            using var context = TestContexts.CreateProduction(FixedIdentity.Random());
            context.Entry(NewRecord()).State = state;

            Should.NotThrow(() => AppendOnlyGuardInterceptor.Apply(context));
        }

        Should.NotThrow(() => AppendOnlyGuardInterceptor.Apply(null));
    }

    [Fact]
    public void An_entity_that_is_not_append_only_may_be_updated()
    {
        using var context = TestContexts.CreateProduction(FixedIdentity.Random());
        context.Entry(Patient.Register(Mrn.Of("MRN-1"), new PatientDetails { FullName = "Priya Sharma" })).State = EntityState.Modified;

        Should.NotThrow(() => AppendOnlyGuardInterceptor.Apply(context));
    }

    [Fact]
    public void In_the_production_model_only_audit_records_and_the_ledger_are_append_only()
    {
        using var context = TestContexts.CreateProduction();

        var appendOnly = context.Model.GetEntityTypes().Select(entityType => entityType.ClrType).Where(type => typeof(IAppendOnly).IsAssignableFrom(type));

        // The audit trail (BR-702) and the ledger (BR-402). The cached balances of a payer are not: the ledger moves them.
        // Since Prompt 10.4 (ADR 0054) the record of a print too: it is its own trail.
        appendOnly.ShouldBe([typeof(AuditRecord), typeof(LedgerPosting), typeof(LedgerEntry), typeof(DocumentPrint)], ignoreOrder: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_guard_is_part_of_every_save_of_the_context_however_it_was_created(bool delete)
    {
        // A context built by hand, as a test or a script would: the guard runs before any command reaches a database,
        // so the connection string nothing listens on is never used. The record is the current tenant's own, so the
        // tenant guard has nothing to say and this guard is the one that answers.
        var identity = FixedIdentity.Random();
        using var context = TestContexts.CreateProduction(identity);
        var entry = context.Entry(NewRecord());
        entry.Property(record => record.Id).CurrentValue = 7;
        entry.Property(record => record.TenantId).CurrentValue = identity.TenantId!.Value;
        entry.State = EntityState.Unchanged;

        if (delete)
        {
            context.Remove(entry.Entity);
        }
        else
        {
            entry.Property(record => record.Reason).CurrentValue = "Rewritten";
        }

        Should.Throw<InvalidOperationException>(() => context.SaveChanges()).Message.ShouldContain("append-only");
    }

    private static AuditRecord NewRecord()
        => AuditRecord.Create(Now, new UserId(Guid.NewGuid()), Guid.NewGuid(), "Patient", Guid.NewGuid(), "Registered", null, """{"FullName":"Priya Sharma"}""", reason: "Entered twice");
}
