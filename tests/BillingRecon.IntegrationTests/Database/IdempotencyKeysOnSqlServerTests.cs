using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Idempotency;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Persistence;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The IdempotencyKeys table as the migrations create it on SQL Server 2022 (docs/database.md, sections 3.6 and 6),
/// and the store on it without the API around it: what each step makes permanent, and what it takes back (ADR 0006).
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class IdempotencyKeysOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly byte[] Hash = [.. Enumerable.Range(1, 32).Select(value => (byte)value)];
    private static readonly byte[] OtherHash = [.. Enumerable.Range(2, 32).Select(value => (byte)value)];
    private static readonly StoredResponse Created = new(201, "application/json; charset=utf-8", "/api/v1/payments/1", "\"AAAAAAAAB9E=\"", """{"id":1}""");

    [Fact]
    public async Task The_table_has_the_documented_key_constraints_index_and_foreign_key()
    {
        var primaryKey = await NamesAsync(
            """
            SELECT STRING_AGG(c.[name], ',') WITHIN GROUP (ORDER BY ic.key_ordinal) + ':' + MIN(i.type_desc COLLATE DATABASE_DEFAULT) AS [Value]
            FROM sys.indexes AS i
            JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.IdempotencyKeys') AND i.is_primary_key = 1
            """);
        var indexes = await NamesAsync(
            """
            SELECT i.[name] + ':' + STRING_AGG(c.[name], ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS [Value]
            FROM sys.indexes AS i
            JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.IdempotencyKeys') AND i.is_primary_key = 0
            GROUP BY i.[name]
            """);

        primaryKey.ShouldBe(["TenantId,Key:CLUSTERED"]);
        indexes.ShouldBe(["IX_IdempotencyKeys_TenantId_ExpiresAtUtc:TenantId,ExpiresAtUtc"]);
        (await NamesAsync("SELECT [name] AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.IdempotencyKeys')"))
            .ShouldBe(["CK_IdempotencyKeys_Status", "CK_IdempotencyKeys_Completed"], ignoreOrder: true);
        (await NamesAsync("SELECT [name] + ':' + CAST(delete_referential_action AS varchar(1)) AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.IdempotencyKeys')"))
            .ShouldBe(["FK_IdempotencyKeys_TenantId:0", "FK_IdempotencyKeys_UserId:0"], ignoreOrder: true);
    }

    [Fact]
    public async Task Reserving_makes_the_key_permanent_at_once_and_opens_and_holds_the_business_transaction()
    {
        var tenant = await NewApiTenantAsync();
        var key = Guid.NewGuid();
        await using var context = ApiContext(tenant);
        var held = new RequestTransaction();

        var reservation = await Store(context, tenant, held).ReserveAsync(key, "POST", "/api/v1/payments", Hash, CancellationToken.None);

        reservation.Outcome.ShouldBe(IdempotencyOutcome.Reserved);
        held.IsHeld.ShouldBeTrue();
        context.Database.CurrentTransaction.ShouldNotBeNull();

        // Visible to everyone else already: the reservation was committed on a connection of its own.
        await using var other = ApiContext(tenant);
        (await other.Set<IdempotencyKey>().SingleAsync()).Status.ShouldBe(IdempotencyStatus.InProgress);
    }

    [Fact]
    public async Task The_reserved_row_is_read_before_the_use_case_runs_so_completing_it_reads_nothing_while_a_series_is_held()
    {
        // ADR 0046: a posting holds its number series from its number to the commit; the read of the key's row is not
        // part of that, because the reservation has it in the request's context already.
        var tenant = await NewApiTenantAsync();
        var key = Guid.NewGuid();
        await using var context = ApiContext(tenant);
        var store = Store(context, tenant, new RequestTransaction());

        await store.ReserveAsync(key, "POST", "/api/v1/payments", Hash, CancellationToken.None);

        var tracked = context.ChangeTracker.Entries<IdempotencyKey>().ShouldHaveSingleItem();
        tracked.Entity.Key.ShouldBe(key);
        tracked.State.ShouldBe(EntityState.Unchanged);

        await store.CompleteAsync(key, Created, CancellationToken.None);

        await using var reader = ApiContext(tenant);
        (await reader.Set<IdempotencyKey>().SingleAsync()).Status.ShouldBe(IdempotencyStatus.Completed);
    }

    [Fact]
    public async Task A_second_reservation_of_the_key_is_told_in_progress_reuse_or_given_the_stored_answer()
    {
        var tenant = await NewApiTenantAsync();
        var key = Guid.NewGuid();
        await using var first = ApiContext(tenant);
        var store = Store(first, tenant, new RequestTransaction());
        await store.ReserveAsync(key, "POST", "/api/v1/payments", Hash, CancellationToken.None);

        (await ReserveElsewhereAsync(tenant, key, Hash)).Outcome.ShouldBe(IdempotencyOutcome.InProgress);
        (await ReserveElsewhereAsync(tenant, key, OtherHash)).Outcome.ShouldBe(IdempotencyOutcome.Reuse);

        await store.CompleteAsync(key, Created, CancellationToken.None);

        var replay = await ReserveElsewhereAsync(tenant, key, Hash);
        replay.Outcome.ShouldBe(IdempotencyOutcome.Replay);
        replay.Response.ShouldBe(Created);
        (await ReserveElsewhereAsync(tenant, key, OtherHash)).Outcome.ShouldBe(IdempotencyOutcome.Reuse);
    }

    [Fact]
    public async Task BR007_completing_commits_the_answer_and_the_work_of_the_request_together()
    {
        var tenant = await NewApiTenantAsync();
        var key = Guid.NewGuid();
        await using var context = ApiContext(tenant);
        var held = new RequestTransaction();
        var store = Store(context, tenant, held);
        await store.ReserveAsync(key, "POST", "/api/v1/payments", Hash, CancellationToken.None);

        // The use case: it saves through its unit of work, which leaves the commit to the store.
        context.Add(Domain.Payers.Patient.Register(Domain.Payers.Mrn.Of("MRN-1"), new Domain.Payers.PatientDetails { FullName = "Priya Sharma" }));
        (await new UnitOfWork(context, held).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        context.Database.CurrentTransaction.ShouldNotBeNull("the unit of work saved into the held transaction and did not end it");

        await using (var other = ApiContext(tenant))
        {
            (await other.Set<Domain.Payers.Patient>().CountAsync()).ShouldBe(0, "nothing of the request is visible before the answer is stored");
        }

        await store.CompleteAsync(key, Created, CancellationToken.None);

        held.IsHeld.ShouldBeFalse();
        context.Database.CurrentTransaction.ShouldBeNull();
        await using var reader = ApiContext(tenant);
        (await reader.Set<Domain.Payers.Patient>().CountAsync()).ShouldBe(1);
        var row = await reader.Set<IdempotencyKey>().SingleAsync();
        row.Status.ShouldBe(IdempotencyStatus.Completed);
        row.StatusCode.ShouldBe(201);
        row.ResponseBody.ShouldBe(Created.Body);
    }

    [Fact]
    public async Task Rejecting_rolls_the_work_back_and_keeps_the_refusal()
    {
        var tenant = await NewApiTenantAsync();
        var key = Guid.NewGuid();
        var refusal = new StoredResponse(422, "application/problem+json", null, null, """{"errorCode":"bill_not_draft"}""");
        await using var context = ApiContext(tenant);
        var held = new RequestTransaction();
        var store = Store(context, tenant, held);
        await store.ReserveAsync(key, "POST", "/api/v1/bills/1/void", Hash, CancellationToken.None);
        context.Add(Domain.Payers.Patient.Register(Domain.Payers.Mrn.Of("MRN-1"), new Domain.Payers.PatientDetails { FullName = "Priya Sharma" }));
        (await new UnitOfWork(context, held).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        await store.RejectAsync(key, refusal, CancellationToken.None);

        held.IsHeld.ShouldBeFalse();
        context.Database.CurrentTransaction.ShouldBeNull();
        await using var reader = ApiContext(tenant);
        (await reader.Set<Domain.Payers.Patient>().CountAsync()).ShouldBe(0);
        (await reader.Set<IdempotencyKey>().SingleAsync()).StatusCode.ShouldBe(422);
        (await ReserveElsewhereAsync(tenant, key, Hash)).Response.ShouldBe(refusal);
    }

    [Fact]
    public async Task Releasing_rolls_the_work_back_and_forgets_the_key_but_never_a_completed_one()
    {
        var tenant = await NewApiTenantAsync();
        var key = Guid.NewGuid();
        await using var context = ApiContext(tenant);
        var held = new RequestTransaction();
        var store = Store(context, tenant, held);
        await store.ReserveAsync(key, "POST", "/api/v1/payments", Hash, CancellationToken.None);
        context.Add(Domain.Payers.Patient.Register(Domain.Payers.Mrn.Of("MRN-1"), new Domain.Payers.PatientDetails { FullName = "Priya Sharma" }));
        (await new UnitOfWork(context, held).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        await store.ReleaseAsync(key, CancellationToken.None);

        held.IsHeld.ShouldBeFalse();
        await using (var reader = ApiContext(tenant))
        {
            (await reader.Set<Domain.Payers.Patient>().CountAsync()).ShouldBe(0);
            (await reader.Set<IdempotencyKey>().CountAsync()).ShouldBe(0);
        }

        // The key is free again; once it has its answer, a release takes nothing away.
        await using var again = ApiContext(tenant);
        var second = Store(again, tenant, new RequestTransaction());
        (await second.ReserveAsync(key, "POST", "/api/v1/payments", OtherHash, CancellationToken.None)).Outcome.ShouldBe(IdempotencyOutcome.Reserved);
        await second.CompleteAsync(key, Created, CancellationToken.None);
        await second.ReleaseAsync(key, CancellationToken.None);

        await using var after = ApiContext(tenant);
        (await after.Set<IdempotencyKey>().SingleAsync()).Status.ShouldBe(IdempotencyStatus.Completed);
    }

    [Fact]
    public async Task A_scope_without_a_tenant_cannot_reserve_a_key()
    {
        await using var system = ApiContext(SystemIdentity.Instance);
        var store = new IdempotencyStore(
            system, TestContexts.Options<BillingReconDbContext>(SqlServer.ApiConnectionString), SystemIdentity.Instance, SystemIdentity.Instance, new FakeClock(Now), new RequestTransaction());

        await Should.ThrowAsync<InvalidOperationException>(() => store.ReserveAsync(Guid.NewGuid(), "POST", "/x", Hash, CancellationToken.None));
    }

    [Theory]
    [InlineData("UPDATE [dbo].[IdempotencyKeys] SET [Status] = 'Completed'")] // completed without an answer
    [InlineData("UPDATE [dbo].[IdempotencyKeys] SET [StatusCode] = 200")] // an answer on a key in progress
    [InlineData("UPDATE [dbo].[IdempotencyKeys] SET [Status] = 'Abandoned'")]
    public async Task A_value_the_check_constraints_forbid_is_refused_by_the_database(string update)
    {
        var tenant = await NewApiTenantAsync();
        await using var context = ApiContext(tenant);
        await Store(context, tenant, new RequestTransaction()).ReserveAsync(Guid.NewGuid(), "POST", "/api/v1/payments", Hash, CancellationToken.None);

        await using var writer = ApiContext(tenant);
        var exception = await Should.ThrowAsync<SqlException>(() => writer.Database.ExecuteSqlRawAsync(update));

        // 547: a CHECK constraint refused the row.
        exception.Number.ShouldBe(547);
    }

    private IdempotencyStore Store(BillingReconDbContext context, FixedIdentity tenant, RequestTransaction held)
        => new(context, TestContexts.Options<BillingReconDbContext>(SqlServer.ApiConnectionString), tenant, tenant, new FakeClock(Now), held);

    // Another request with the key: its own context, and nothing held afterwards.
    private async Task<IdempotencyReservation> ReserveElsewhereAsync(FixedIdentity tenant, Guid key, byte[] hash)
    {
        await using var context = ApiContext(tenant);
        var held = new RequestTransaction();

        var reservation = await Store(context, tenant, held).ReserveAsync(key, "POST", "/api/v1/payments", hash, CancellationToken.None);

        held.IsHeld.ShouldBeFalse("a request that did not get the key holds no transaction");
        context.Database.CurrentTransaction.ShouldBeNull();

        return reservation;
    }

    private async Task<List<string>> NamesAsync(string sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQueryRaw<string>(sql).ToListAsync();
    }
}
