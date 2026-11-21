using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Audit;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Infrastructure.Audit;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.Infrastructure.Payers;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The unit of work dispatching domain events to the audit subscriber, on SQL Server and without the API around it
/// (BR-701 to BR-703, ADR 0020): the record is written in the transaction of the change, with the instant of the
/// commit, and not at all when the commit fails; and the AuditRecords table as the migrations create it, whose rows
/// the context refuses to change.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class AuditOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Guid Correlation = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3305");

    [Fact]
    public async Task The_table_has_the_documented_key_indexes_and_foreign_key()
    {
        var primaryKey = await NamesAsync(
            """
            SELECT STRING_AGG(c.[name], ',') WITHIN GROUP (ORDER BY ic.key_ordinal) + ':' + MIN(i.type_desc COLLATE DATABASE_DEFAULT) AS [Value]
            FROM sys.indexes AS i
            JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.AuditRecords') AND i.is_primary_key = 1
            """);
        var indexes = await NamesAsync(
            """
            SELECT i.[name] + ':' + STRING_AGG(c.[name], ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS [Value]
            FROM sys.indexes AS i
            JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.AuditRecords') AND i.is_primary_key = 0
            GROUP BY i.[name]
            """);

        primaryKey.ShouldBe(["Id:CLUSTERED"]);
        indexes.ShouldBe(
            [
                "IX_AuditRecords_TenantId_OccurredAtUtc:TenantId,OccurredAtUtc",
                "IX_AuditRecords_TenantId_CorrelationId:TenantId,CorrelationId",
                "IX_AuditRecords_TenantId_EntityType_EntityId_Id:TenantId,EntityType,EntityId,Id",
            ],
            ignoreOrder: true);
        (await NamesAsync("SELECT [name] + ':' + CAST(delete_referential_action AS varchar(1)) AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.AuditRecords')"))
            .ShouldBe(["FK_AuditRecords_TenantId:0", "FK_AuditRecords_UserId:0"], ignoreOrder: true);
        (await NamesAsync("SELECT c.[name] AS [Value] FROM sys.identity_columns AS c WHERE c.object_id = OBJECT_ID(N'dbo.AuditRecords')"))
            .ShouldBe(["Id"]);
    }

    [Fact]
    public async Task BR701_a_commit_writes_the_change_and_its_audit_record_together()
    {
        var clerk = await NewApiTenantAsync();

        var id = await RegisterAsync(clerk, "MRN-1");

        var record = (await RecordsAsync(clerk)).ShouldHaveSingleItem();
        record.Id.ShouldBeGreaterThan(0);
        record.TenantId.ShouldBe(clerk.TenantId!.Value);
        record.UserId.ShouldBe(clerk.UserId);
        record.CorrelationId.ShouldBe(Correlation);
        record.EntityType.ShouldBe("Patient");
        record.EntityId.ShouldBe(id.Value, "the event of a registration carries no id; the unit of work knows it");
        record.Action.ShouldBe("Registered");
        record.BeforeJson.ShouldBeNull();
        record.AfterJson.ShouldContain("\"Mrn\":\"MRN-1\"");
        record.OccurredAtUtc.ShouldBe(Now);
    }

    [Fact]
    public async Task BR003_the_clock_is_read_once_per_commit_and_the_record_carries_the_instant_of_the_audit_columns()
    {
        // A clock that moves a second with every reading: two readings in one commit would show as two instants.
        var clerk = await NewApiTenantAsync();
        var clock = new TickingClock(Now);
        await using var context = ApiContext(clerk, clock);
        var patient = Patient.Register(Mrn.Of("MRN-1"), Details());
        new PatientRepository(context).Add(patient);

        (await UnitOfWork(context, clerk).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        clock.Readings.ShouldBe(1);
        patient.CreatedAtUtc.ShouldBe(Now);
        patient.UpdatedAtUtc.ShouldBe(Now);
        (await RecordsAsync(clerk)).ShouldHaveSingleItem().OccurredAtUtc.ShouldBe(Now);

        // The next commit is another use case: it reads the clock again, once.
        patient.UpdateDemographics(Details() with { FullName = "Priya S. Rao" });
        (await UnitOfWork(context, clerk).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        clock.Readings.ShouldBe(2);
        patient.UpdatedAtUtc.ShouldBe(Now.AddSeconds(1));
        (await RecordsAsync(clerk)).Select(record => record.OccurredAtUtc).ShouldBe([Now, Now.AddSeconds(1)]);
    }

    [Fact]
    public async Task An_update_records_the_row_before_and_after_and_each_event_is_dispatched_once()
    {
        var clerk = await NewApiTenantAsync();
        var id = await RegisterAsync(clerk, "MRN-1");
        await using var context = ApiContext(clerk);
        var patient = (await new PatientRepository(context).GetAsync(id, CancellationToken.None)).ShouldNotBeNull();

        patient.UpdateDemographics(Details() with { FullName = "Priya S. Rao" });
        (await UnitOfWork(context, clerk).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        patient.DomainEvents.ShouldBeEmpty("the commit took the events off the aggregate");

        // A second commit of the same context has nothing to say: no event, no record.
        (await UnitOfWork(context, clerk).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        var records = await RecordsAsync(clerk);
        records.Select(record => record.Action).ShouldBe(["Registered", "Updated"]);
        records[1].BeforeJson.ShouldNotBeNull().ShouldContain("\"FullName\":\"Priya Sharma\"");
        records[1].AfterJson.ShouldContain("\"FullName\":\"Priya S. Rao\"");
        records[1].Id.ShouldBeGreaterThan(records[0].Id, "records are numbered in the order they are written");
    }

    [Fact]
    public async Task A_commit_that_loses_to_a_unique_index_writes_no_audit_record()
    {
        var clerk = await NewApiTenantAsync();
        await RegisterAsync(clerk, "MRN-1");

        // The same MRN again, without the use case's check: the index refuses the patient, and the record goes with it.
        await using var context = ApiContext(clerk);
        new PatientRepository(context).Add(Patient.Register(Mrn.Of("MRN-1"), Details()));
        var result = await UnitOfWork(context, clerk).CommitAsync(CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.DuplicateKeyCode);
        (await RecordsAsync(clerk)).ShouldHaveSingleItem().Action.ShouldBe("Registered");
    }

    [Fact]
    public async Task BR804_a_commit_that_loses_to_a_newer_row_version_writes_no_audit_record()
    {
        var clerk = await NewApiTenantAsync();
        var id = await RegisterAsync(clerk, "MRN-1");
        await using var first = ApiContext(clerk);
        await using var second = ApiContext(clerk);
        var firstCopy = (await new PatientRepository(first).GetAsync(id, CancellationToken.None)).ShouldNotBeNull();
        var secondCopy = (await new PatientRepository(second).GetAsync(id, CancellationToken.None)).ShouldNotBeNull();

        firstCopy.UpdateDemographics(Details() with { FullName = "First" });
        (await UnitOfWork(first, clerk).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        secondCopy.UpdateDemographics(Details() with { FullName = "Second" });
        var result = await UnitOfWork(second, clerk).CommitAsync(CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        var records = await RecordsAsync(clerk);
        records.Select(record => record.Action).ShouldBe(["Registered", "Updated"]);
        records[1].AfterJson.ShouldContain("\"FullName\":\"First\"");
    }

    [Fact]
    public async Task A_commit_that_fails_with_a_fault_writes_neither_the_change_nor_its_audit_record()
    {
        // A tenant without a tenant row: the foreign key refuses the patient, which is a fault and throws.
        var unknown = FixedIdentity.Random();
        await using var context = ApiContext(unknown);
        new PatientRepository(context).Add(Patient.Register(Mrn.Of("MRN-1"), Details()));

        await Should.ThrowAsync<DbUpdateException>(() => UnitOfWork(context, unknown).CommitAsync(CancellationToken.None));

        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.AuditRecords")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Patients")).ShouldBe(0);
    }

    [Fact]
    public async Task A_subscriber_that_throws_fails_the_commit_and_nothing_is_written()
    {
        var clerk = await NewApiTenantAsync();
        await using var context = ApiContext(clerk);
        new PatientRepository(context).Add(Patient.Register(Mrn.Of("MRN-1"), Details()));
        var unitOfWork = new UnitOfWork(context, requestTransaction: null, [new ThrowingSubscriber()]);

        await Should.ThrowAsync<InvalidOperationException>(() => unitOfWork.CommitAsync(CancellationToken.None));

        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Patients")).ShouldBe(0);
    }

    [Fact]
    public async Task A_unit_of_work_without_subscribers_saves_the_change_and_clears_the_events()
    {
        var clerk = await NewApiTenantAsync();
        await using var context = ApiContext(clerk);
        var patient = Patient.Register(Mrn.Of("MRN-1"), Details());
        new PatientRepository(context).Add(patient);

        (await new UnitOfWork(context).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        patient.DomainEvents.ShouldBeEmpty();
        (await RecordsAsync(clerk)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BR702_an_audit_record_cannot_be_updated_or_deleted_through_the_context(bool delete)
    {
        var clerk = await NewApiTenantAsync();
        await RegisterAsync(clerk, "MRN-1");
        await using var context = ApiContext(clerk);
        var record = await context.Set<AuditRecord>().SingleAsync();

        if (delete)
        {
            context.Remove(record);
        }
        else
        {
            context.Entry(record).Property(candidate => candidate.Action).CurrentValue = "Rewritten";
        }

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        exception.Message.ShouldContain("append-only");
        (await RecordsAsync(clerk)).ShouldHaveSingleItem().Action.ShouldBe("Registered");
    }

    [Fact]
    public async Task BR002_a_tenant_sees_its_own_audit_records_and_no_other()
    {
        var clerk = await NewApiTenantAsync();
        var other = await NewApiTenantAsync();
        await RegisterAsync(clerk, "MRN-1");
        await RegisterAsync(other, "MRN-1");
        await RegisterAsync(other, "MRN-2");

        (await RecordsAsync(clerk)).ShouldHaveSingleItem().TenantId.ShouldBe(clerk.TenantId!.Value);
        (await RecordsAsync(other)).Select(record => record.TenantId).ShouldBe([other.TenantId!.Value, other.TenantId!.Value]);
    }

    [Fact]
    public async Task System_work_cannot_write_an_audit_record_because_it_has_no_tenant()
    {
        var clerk = await NewApiTenantAsync();
        await using var system = ApiContext(SystemIdentity.Instance);

        // System work has no tenant, and an audit record is a tenant's: it cannot be written without one.
        new AuditWriter(system).Append(AuditRecord.Create(Now, null, Correlation, "Patient", Guid.NewGuid(), "Registered", null, "{}"));

        await Should.ThrowAsync<TenantIsolationException>(() => system.SaveChangesAsync());
        (await RecordsAsync(clerk)).ShouldBeEmpty();
    }

    private static UnitOfWork UnitOfWork(BillingReconDbContext context, FixedIdentity identity)
        => new(context, requestTransaction: null, [new AuditSubscriber(new AuditWriter(context), identity, new FixedCorrelation(Correlation))]);

    private async Task<PatientId> RegisterAsync(FixedIdentity clerk, string mrn)
    {
        await using var context = ApiContext(clerk);
        var patient = Patient.Register(Mrn.Of(mrn), Details());
        new PatientRepository(context).Add(patient);
        (await UnitOfWork(context, clerk).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        return patient.Id;
    }

    private async Task<List<AuditRecord>> RecordsAsync(FixedIdentity tenant)
    {
        await using var context = ApiContext(tenant);

        return await context.Set<AuditRecord>().AsNoTracking().OrderBy(record => record.Id).ToListAsync();
    }

    private async Task<List<string>> NamesAsync(string sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQueryRaw<string>(sql).ToListAsync();
    }

    private async Task<int> CountAsync(string sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQueryRaw<int>(sql).SingleAsync();
    }

    private static PatientDetails Details() => new() { FullName = "Priya Sharma" };

    private sealed class TickingClock(DateTimeOffset start) : IClock
    {
        private static readonly TimeSpan IndiaOffset = new(5, 30, 0);

        public int Readings { get; private set; }

        public DateTimeOffset UtcNow => start.AddSeconds(Readings++);

        public DateOnly TodayIst => ToIstDate(start);

        public DateOnly ToIstDate(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(IndiaOffset).DateTime);
    }

    private sealed class ThrowingSubscriber : IDomainEventSubscriber
    {
        public Task HandleAsync(DomainEventOccurrence occurrence, CancellationToken ct)
            => throw new InvalidOperationException("The subscriber failed.");
    }
}
