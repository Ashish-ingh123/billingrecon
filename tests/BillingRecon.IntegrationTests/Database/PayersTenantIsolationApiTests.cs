using System.Net;
using BillingRecon.Api.Payers;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Payers;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Tenant isolation at the API (BR-002, ADR 0009): a tenant cannot see, change or refer to another tenant's patients
/// and payers, even with their ids and ETags. Every attempt answers 404, exactly as for an id nobody has: a 403 would
/// confirm to the other tenant that the record exists (docs/api.md).
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PayersTenantIsolationApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    [Fact]
    public async Task Tenant_B_reading_tenant_As_patient_or_payer_gets_404_as_for_an_id_nobody_has()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (asA, asB) = await TwoTenantsAsync(factory);
        var patient = await asA.RegisterPatientAsync();
        var payer = await asA.CreatePayerAsync(ForPatient(patient));

        using var theirPatient = await asB.GetAsync(PayersApi.Patient(patient.Id));
        using var theirPayer = await asB.GetAsync(PayersApi.Payer(payer.Id));
        using var nobodysPayer = await asB.GetAsync(PayersApi.Payer(Guid.NewGuid()));

        var patientProblem = await theirPatient.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PatientNotFoundCode);
        var payerProblem = await theirPayer.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PayerNotFoundCode);
        var nobodysProblem = await nobodysPayer.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PayerNotFoundCode);
        payerProblem.GetProperty("detail").GetString().ShouldBe(nobodysProblem.GetProperty("detail").GetString());
        patientProblem.GetProperty("title").GetString().ShouldBe(payerProblem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Tenant_B_cannot_correct_deactivate_or_reactivate_tenant_As_records_even_with_their_etags()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (asA, asB) = await TwoTenantsAsync(factory);
        var patient = await asA.RegisterPatientAsync();
        var payer = await asA.CreatePayerAsync(ForPatient(patient));
        var patientETag = PayersApi.ETag(patient.Version);
        var payerETag = PayersApi.ETag(payer.Version);

        using var patchPatient = await asB.SendWithIfMatchAsync(HttpMethod.Patch, PayersApi.Patient(patient.Id), new UpdatePatientRequest { FullName = "Hijacked" }, patientETag);
        using var deactivatePatient = await asB.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Patient(patient.Id, "deactivate"), body: null, patientETag);
        using var patchPayer = await asB.SendWithIfMatchAsync(HttpMethod.Patch, PayersApi.Payer(payer.Id), new UpdatePayerRequest { Name = "Hijacked" }, payerETag);
        using var deactivatePayer = await asB.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Payer(payer.Id, "deactivate"), body: null, payerETag);
        using var reactivatePayer = await asB.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Payer(payer.Id, "reactivate"), body: null, payerETag);

        await patchPatient.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PatientNotFoundCode);
        await deactivatePatient.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PatientNotFoundCode);
        await patchPayer.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PayerNotFoundCode);
        await deactivatePayer.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PayerNotFoundCode);
        await reactivatePayer.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PayerNotFoundCode);

        using var patientAfter = await asA.GetAsync(PayersApi.Patient(patient.Id));
        using var payerAfter = await asA.GetAsync(PayersApi.Payer(payer.Id));
        (await patientAfter.ReadAsAsync<PatientDto>()).ShouldBe(patient);
        (await payerAfter.ReadAsAsync<PayerDto>()).ShouldBe(payer);
    }

    [Fact]
    public async Task Tenant_B_lists_none_of_tenant_As_records_even_when_it_searches_for_them()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (asA, asB) = await TwoTenantsAsync(factory);
        var patient = await asA.RegisterPatientAsync(TestData.NewPatient() with { Mrn = "MRN-SECRET-1", FullName = "Priya Sharma" });
        await asA.CreatePayerAsync(ForPatient(patient));

        using var patients = await asB.GetAsync(PayersApi.Patients);
        using var byMrn = await asB.GetAsync(PayersApi.Patients.WithQuery("search=MRN-SECRET-1"));
        using var payers = await asB.GetAsync(PayersApi.Payers.WithQuery("search=Sharma"));
        using var byPatient = await asB.GetAsync(PayersApi.Payers.WithQuery($"patientId={patient.Id}"));

        (await patients.ReadAsAsync<PageOf<PatientDto>>()).TotalCount.ShouldBe(0);
        (await byMrn.ReadAsAsync<PageOf<PatientDto>>()).TotalCount.ShouldBe(0);
        (await payers.ReadAsAsync<PageOf<PayerDto>>()).TotalCount.ShouldBe(0);
        (await byPatient.ReadAsAsync<PageOf<PayerDto>>()).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Tenant_B_cannot_link_a_payer_to_tenant_As_patient()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (asA, asB) = await TwoTenantsAsync(factory);
        var patient = await asA.RegisterPatientAsync();

        using var response = await asB.PostJsonAsync(PayersApi.Payers, ForPatient(patient));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PatientNotFoundCode);
    }

    [Fact]
    public async Task BR101_two_tenants_may_each_register_the_same_mrn()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (asA, asB) = await TwoTenantsAsync(factory);

        var inA = await asA.RegisterPatientAsync(TestData.NewPatient() with { Mrn = "MRN-000001" });
        var inB = await asB.RegisterPatientAsync(TestData.NewPatient() with { Mrn = "MRN-000001" });

        inB.Id.ShouldNotBe(inA.Id);
    }

    private async Task<(HttpClient AsA, HttpClient AsB)> TwoTenantsAsync(DatabaseApiFactory factory)
        => (factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk"), factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk"));

    private static CreatePayerRequest ForPatient(PatientDto patient) => new() { Kind = PayerKind.SelfPay, PatientId = patient.Id, Name = patient.FullName };
}
