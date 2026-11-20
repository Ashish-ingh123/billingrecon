using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Payers;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Tenant isolation of bills at the API (BR-002, ADR 0009): a tenant cannot see or change another tenant's bills, even
/// with their ids and ETags, and cannot raise a bill that refers to another tenant's patient, payer or service item.
/// Every attempt answers 404, exactly as for an id nobody has (docs/api.md).
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class BillsTenantIsolationApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    [Fact]
    public async Task Tenant_B_reading_tenant_As_bill_gets_404_as_for_an_id_nobody_has()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (asA, asB) = await TwoTenantsAsync(factory);
        var bill = await BillOfAsync(asA);

        using var theirs = await asB.GetAsync(BillingApi.Bill(bill.Id));
        using var nobodys = await asB.GetAsync(BillingApi.Bill(Guid.NewGuid()));

        var theirProblem = await theirs.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        var nobodysProblem = await nobodys.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        theirProblem.GetProperty("detail").GetString().ShouldBe(nobodysProblem.GetProperty("detail").GetString());
        theirProblem.GetProperty("title").GetString().ShouldBe(nobodysProblem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Tenant_B_cannot_correct_reline_or_void_tenant_As_bill_even_with_its_etag()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (asA, asB) = await TwoTenantsAsync(factory);
        var bill = await BillOfAsync(asA);
        var etag = PayersApi.ETag(bill.Version);

        using var header = await asB.SendWithIfMatchAsync(HttpMethod.Patch, BillingApi.Bill(bill.Id), new UpdateBillHeaderRequest { BillDate = bill.BillDate, DueDate = bill.DueDate, Notes = "Hijacked" }, etag);
        using var lines = await asB.SendWithIfMatchAsync(HttpMethod.Put, BillingApi.Bill(bill.Id, "lines"), new ReplaceBillLinesRequest { Lines = [] }, etag);
        using var voided = await asB.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "void"), new VoidBillRequest { Reason = "Hijacked" }, etag);

        await header.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        await lines.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        await voided.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);

        using var after = await asA.GetAsync(BillingApi.Bill(bill.Id));
        (await after.ReadAsAsync<BillDto>()).ShouldBeEquivalentTo(bill);
    }

    [Fact]
    public async Task Tenant_B_lists_none_of_tenant_As_bills_even_when_it_filters_by_their_patient_or_payer()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (asA, asB) = await TwoTenantsAsync(factory);
        var bill = await BillOfAsync(asA);

        using var all = await asB.GetAsync(BillingApi.Bills);
        using var byPatient = await asB.GetAsync(BillingApi.Bills.WithQuery($"patientId={bill.PatientId}"));
        using var byPayer = await asB.GetAsync(BillingApi.Bills.WithQuery($"payerId={bill.PayerId}"));

        (await all.ReadAsAsync<PageOf<BillSummaryDto>>()).TotalCount.ShouldBe(0);
        (await byPatient.ReadAsAsync<PageOf<BillSummaryDto>>()).TotalCount.ShouldBe(0);
        (await byPayer.ReadAsAsync<PageOf<BillSummaryDto>>()).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Tenant_B_cannot_raise_a_bill_for_tenant_As_patient_owed_by_tenant_As_payer_or_priced_from_tenant_As_catalogue()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (asA, asB) = await TwoTenantsAsync(factory);
        var (theirPatient, theirPayer) = await asA.NewBillPartiesAsync();
        var theirItem = await asA.CreateServiceItemAsync();
        var (ownPatient, ownPayer) = await asB.NewBillPartiesAsync();

        using var forTheirPatient = await asB.PostJsonAsync(BillingApi.Bills, new CreateBillRequest { PatientId = theirPatient.Id, PayerId = ownPayer.Id });
        using var owedByTheirPayer = await asB.PostJsonAsync(BillingApi.Bills, new CreateBillRequest { PatientId = ownPatient.Id, PayerId = theirPayer.Id });
        using var fromTheirCatalogue = await asB.PostJsonAsync(BillingApi.Bills, new CreateBillRequest
        {
            PatientId = ownPatient.Id,
            PayerId = ownPayer.Id,
            Lines = [new BillLineRequest { ServiceItemId = theirItem.Id, Quantity = 1m }],
        });

        await forTheirPatient.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PatientNotFoundCode);
        await owedByTheirPayer.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PayerNotFoundCode);
        await fromTheirCatalogue.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.ServiceItemNotFoundCode);
        using var own = await asB.GetAsync(BillingApi.Bills);
        (await own.ReadAsAsync<PageOf<BillSummaryDto>>()).TotalCount.ShouldBe(0);
    }

    private static async Task<BillDto> BillOfAsync(HttpClient client)
    {
        var (patient, payer) = await client.NewBillPartiesAsync();

        return await client.CreateBillAsync(new CreateBillRequest
        {
            PatientId = patient.Id,
            PayerId = payer.Id,
            Lines = [BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m)],
        });
    }

    private async Task<(HttpClient AsA, HttpClient AsB)> TwoTenantsAsync(DatabaseApiFactory factory)
        => (factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk"), factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk"));
}
