using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.Infrastructure.Payers;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The Patients and Payers tables as the migrations create them on SQL Server 2022 (docs/database.md, section 3.2):
/// their constraints, indexes and foreign keys, and the filtered unique index of BR-104 at work.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PayersOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    [Fact]
    public async Task The_tables_have_the_documented_check_constraints()
    {
        (await NamesAsync("SELECT [name] AS [Value] FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID(N'dbo.Patients'), OBJECT_ID(N'dbo.Payers'))"))
            .ShouldBe(["CK_Patients_Sex", "CK_Patients_Status", "CK_Payers_Kind", "CK_Payers_Status"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_tables_have_the_documented_indexes_each_led_by_the_tenant_and_no_other()
    {
        var indexes = await NamesAsync(
            """
            SELECT i.[name] + ':' + c.[name] + ':' + CAST(i.is_unique AS varchar(1)) + ':' + CAST(i.has_filter AS varchar(1)) AS [Value]
            FROM sys.indexes AS i
            JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1
            JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id IN (OBJECT_ID(N'dbo.Patients'), OBJECT_ID(N'dbo.Payers')) AND i.is_primary_key = 0
            """);

        indexes.ShouldBe(
            [
                "UX_Patients_TenantId_Mrn:TenantId:1:0",
                "IX_Patients_TenantId_FullName:TenantId:0:0",
                "IX_Payers_TenantId_Name:TenantId:0:0",
                "UX_Payers_TenantId_PatientId:TenantId:1:1",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task The_foreign_keys_exist_and_none_deletes_anything()
    {
        // delete_referential_action 0 is NO ACTION.
        (await NamesAsync("SELECT [name] + ':' + CAST(delete_referential_action AS varchar(1)) AS [Value] FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID(N'dbo.Patients'), OBJECT_ID(N'dbo.Payers'))"))
            .ShouldBe(["FK_Patients_TenantId:0", "FK_Payers_TenantId:0", "FK_Payers_PatientId:0"], ignoreOrder: true);
    }

    [Fact]
    public async Task BR104_the_filtered_unique_index_allows_any_number_of_guarantors_and_one_payer_per_patient()
    {
        var clerk = await NewApiTenantAsync();
        PatientId patientId;
        await using (var context = ApiContext(clerk))
        {
            var patient = Patient.Register(Mrn.Of("MRN-1"), new PatientDetails { FullName = "Priya Sharma" });
            context.Add(patient);
            context.Add(Guarantor());
            context.Add(Guarantor());
            await context.SaveChangesAsync();
            patientId = patient.Id;
        }

        await using (var context = ApiContext(clerk))
        {
            context.Add(Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "Priya Sharma" }, patientId).Value);
            (await new UnitOfWork(context).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }

        await using var second = ApiContext(clerk);
        second.Add(Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "Priya again" }, patientId).Value);
        (await new UnitOfWork(second).CommitAsync(CancellationToken.None)).Error.Code.ShouldBe(CommonErrors.DuplicateKeyCode);
    }

    [Fact]
    public async Task A_kind_the_check_constraint_does_not_list_is_refused_by_the_database()
    {
        var clerk = await NewApiTenantAsync();
        await using var context = ApiContext(clerk);
        var payer = Guarantor();
        context.Entry(payer).Property(candidate => candidate.Kind).CurrentValue = (PayerKind)42;
        context.Add(payer);

        var exception = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());

        exception.InnerException.ShouldBeOfType<Microsoft.Data.SqlClient.SqlException>().Number.ShouldBe(547);
    }

    [Fact]
    public async Task A_payer_round_trips_with_the_patient_it_is_and_the_reads_find_it()
    {
        var clerk = await NewApiTenantAsync();
        await using (var context = ApiContext(clerk))
        {
            var patient = Patient.Register(Mrn.Of("MRN-1"), new PatientDetails { FullName = "Priya Sharma", Sex = Sex.Female });
            context.Add(patient);
            await context.SaveChangesAsync();
            context.Add(Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "Priya Sharma" }, patient.Id).Value);
            await context.SaveChangesAsync();
        }

        await using var reader = ApiContext(clerk);
        var stored = await reader.Set<Payer>().SingleAsync();
        var patients = new PatientReadService(reader);

        stored.Kind.ShouldBe(PayerKind.SelfPay);
        var patientId = stored.PatientId.ShouldNotBeNull();
        (await patients.GetAsync(patientId, CancellationToken.None)).ShouldNotBeNull().Sex.ShouldBe(Sex.Female);
        (await new PayerReadService(reader).ExistsForPatientAsync(patientId, CancellationToken.None)).ShouldBeTrue();
        (await patients.MrnExistsAsync(Mrn.Of("mrn-1"), CancellationToken.None)).ShouldBeTrue();
    }

    private static Payer Guarantor() => Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "Rahul Sharma" }, patientId: null).Value;

    private async Task<List<string>> NamesAsync(string sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQueryRaw<string>(sql).ToListAsync();
    }
}
