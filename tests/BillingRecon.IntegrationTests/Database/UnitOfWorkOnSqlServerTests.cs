using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Payers;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The unit of work against SQL Server: the two races a request can lose come back as failed results, without the
/// check of a use case in front of them, and every other failure is a fault that throws.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class UnitOfWorkOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    [Fact]
    public async Task A_row_a_unique_index_refuses_is_duplicate_key_and_not_an_exception()
    {
        var clerk = await NewApiTenantAsync();
        await RegisterAsync(clerk, "MRN-1");

        // The same MRN in other letters, added without the use case's check: only the index can refuse it (BR-101).
        await using var context = ApiContext(clerk);
        new PatientRepository(context).Add(Patient.Register(Mrn.Of("mrn-1"), Details()));
        var result = await new UnitOfWork(context).CommitAsync(CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(CommonErrors.DuplicateKeyCode);
        result.Error.Message.ShouldNotContain("mrn-1", Case.Insensitive);
    }

    [Fact]
    public async Task BR804_a_row_changed_since_it_was_read_is_concurrency_conflict()
    {
        var clerk = await NewApiTenantAsync();
        var id = await RegisterAsync(clerk, "MRN-1");
        await using var first = ApiContext(clerk);
        await using var second = ApiContext(clerk);
        var firstCopy = (await new PatientRepository(first).GetAsync(id, CancellationToken.None)).ShouldNotBeNull();
        var secondCopy = (await new PatientRepository(second).GetAsync(id, CancellationToken.None)).ShouldNotBeNull();

        firstCopy.UpdateDemographics(Details() with { FullName = "First" });
        (await new UnitOfWork(first).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        secondCopy.UpdateDemographics(Details() with { FullName = "Second" });
        var result = await new UnitOfWork(second).CommitAsync(CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        await using var reader = ApiContext(clerk);
        (await new PatientRepository(reader).GetAsync(id, CancellationToken.None)).ShouldNotBeNull().FullName.ShouldBe("First");
    }

    [Fact]
    public async Task A_row_of_a_tenant_without_a_tenant_row_is_a_fault_and_throws()
    {
        // The foreign key on TenantId (ADR 0015): such a row is a bug, not a business outcome.
        var unknown = FixedIdentity.Random();
        await using var context = ApiContext(unknown);
        new PatientRepository(context).Add(Patient.Register(Mrn.Of("MRN-1"), Details()));

        var exception = await Should.ThrowAsync<DbUpdateException>(() => new UnitOfWork(context).CommitAsync(CancellationToken.None));

        // 547: a foreign key refused the row.
        exception.InnerException.ShouldBeOfType<SqlException>().Number.ShouldBe(547);
    }

    private async Task<PatientId> RegisterAsync(FixedIdentity clerk, string mrn)
    {
        await using var context = ApiContext(clerk);
        var patient = Patient.Register(Mrn.Of(mrn), Details());
        new PatientRepository(context).Add(patient);
        (await new UnitOfWork(context).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        return patient.Id;
    }

    private static PatientDetails Details() => new() { FullName = "Priya Sharma" };
}
