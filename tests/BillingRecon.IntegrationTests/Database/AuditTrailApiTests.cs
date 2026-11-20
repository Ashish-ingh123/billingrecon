using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Common.Correlation;
using BillingRecon.Api.Payers;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Audit;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The audit trail end to end, on the pipeline that ships and a real SQL Server (US-43; BR-701 to BR-703): every
/// state change of a patient, a payer, a service item and a bill writes exactly one record, with who, when, the
/// correlation id of its request and the record before and after; a request that changes nothing writes none; and
/// the trail is read through <c>GET /api/v1/audit-records</c>.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class AuditTrailApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly DateOnly Today = new(2026, 10, 17);

    [Fact]
    public async Task US43_the_audit_trail_of_a_patient_shows_register_update_and_deactivate_with_their_correlation_ids()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var registration = TestData.NewPatient();

        using var registered = await client.PostJsonAsync(PayersApi.Patients, registration);
        var patient = await registered.ReadAsAsync<PatientDto>();
        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        using var updated = await client.SendWithIfMatchAsync(HttpMethod.Patch, PayersApi.Patient(patient.Id), Correction(registration) with { FullName = "Priya S. Rao" }, PayersApi.ETag(patient.Version));
        var afterUpdate = await updated.ReadAsAsync<PatientDto>();
        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        using var deactivated = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Patient(patient.Id, "deactivate"), body: null, PayersApi.ETag(afterUpdate.Version));
        deactivated.StatusCode.ShouldBe(HttpStatusCode.OK);

        var trail = await TrailAsync(client, "Patient", patient.Id);

        trail.TotalCount.ShouldBe(3);
        trail.Items.Select(record => record.Action).ShouldBe(["Registered", "Updated", "Deactivated"]);
        trail.Items.Select(record => record.CorrelationId).ShouldBe([registered.GetCorrelationId(), updated.GetCorrelationId(), deactivated.GetCorrelationId()]);
        trail.Items.Select(record => record.CorrelationId).Distinct().Count().ShouldBe(3, "every request has its own correlation id");
        trail.Items.ShouldAllBe(record => record.UserId == clerk.UserId!.Value.Value && record.EntityType == "Patient" && record.EntityId == patient.Id);
        trail.Items.Select(record => record.OccurredAtUtc).ShouldBe(
            [DatabaseApiFactory.InitialInstant, DatabaseApiFactory.InitialInstant.AddMinutes(5), DatabaseApiFactory.InitialInstant.AddMinutes(10)]);
        trail.Items.Select(record => record.Id).ShouldBeInOrder();

        trail.Items[0].Before.ShouldBeNull("a registration has nothing before it");
        trail.Items[0].After.GetProperty("Mrn").GetString().ShouldBe(registration.Mrn);
        trail.Items[0].After.GetProperty("FullName").GetString().ShouldBe(registration.FullName);
        trail.Items[1].Before.ShouldNotBeNull().GetProperty("FullName").GetString().ShouldBe(registration.FullName);
        trail.Items[1].After.GetProperty("FullName").GetString().ShouldBe("Priya S. Rao");
        trail.Items[2].Before.ShouldNotBeNull().GetProperty("Status").GetString().ShouldBe("Active");
        trail.Items[2].After.GetProperty("Status").GetString().ShouldBe("Inactive");
    }

    [Fact]
    public async Task BR703_a_correlation_id_the_client_sends_is_the_one_in_the_audit_record()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var sent = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Post, PayersApi.Patients) { Content = System.Net.Http.Json.JsonContent.Create(TestData.NewPatient(), options: TestJson.Options) };
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, sent.ToString());

        using var response = await client.SendAsync(request);
        var patient = await response.ReadAsAsync<PatientDto>();

        response.GetCorrelationId().ShouldBe(sent);
        (await TrailAsync(client, "Patient", patient.Id)).Items.ShouldHaveSingleItem().CorrelationId.ShouldBe(sent);
    }

    [Fact]
    public async Task BR701_every_state_change_of_a_patient_writes_exactly_one_record()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var registration = TestData.NewPatient();

        var patient = await client.RegisterPatientAsync(registration);
        patient = await ChangeAsync<PatientDto>(client, HttpMethod.Patch, PayersApi.Patient(patient.Id), Correction(registration) with { City = "Pune" }, patient.Version);
        patient = await ChangeAsync<PatientDto>(client, HttpMethod.Post, PayersApi.Patient(patient.Id, "deactivate"), null, patient.Version);
        await ChangeAsync<PatientDto>(client, HttpMethod.Post, PayersApi.Patient(patient.Id, "reactivate"), null, patient.Version);

        (await AllRecordsAsync(clerk)).Select(record => $"{record.EntityType}:{record.Action}")
            .ShouldBe(["Patient:Registered", "Patient:Updated", "Patient:Deactivated", "Patient:Reactivated"]);
    }

    [Fact]
    public async Task BR701_every_state_change_of_a_payer_writes_exactly_one_record()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");

        var payer = await client.CreatePayerAsync(new CreatePayerRequest { Kind = PayerKind.SelfPay, Name = "Rahul Sharma" });
        payer = await ChangeAsync<PayerDto>(client, HttpMethod.Patch, PayersApi.Payer(payer.Id), new UpdatePayerRequest { Name = "Rahul K. Sharma" }, payer.Version);
        payer = await ChangeAsync<PayerDto>(client, HttpMethod.Post, PayersApi.Payer(payer.Id, "deactivate"), null, payer.Version);
        await ChangeAsync<PayerDto>(client, HttpMethod.Post, PayersApi.Payer(payer.Id, "reactivate"), null, payer.Version);

        (await AllRecordsAsync(clerk)).Select(record => $"{record.EntityType}:{record.Action}")
            .ShouldBe(["Payer:Created", "Payer:Updated", "Payer:Deactivated", "Payer:Reactivated"]);
        var trail = await TrailAsync(client, "Payer", payer.Id);
        trail.Items[1].Before.ShouldNotBeNull().GetProperty("Name").GetString().ShouldBe("Rahul Sharma");
        trail.Items[1].After.GetProperty("Name").GetString().ShouldBe("Rahul K. Sharma");
    }

    [Fact]
    public async Task BR701_every_state_change_of_a_service_item_writes_exactly_one_record()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var creation = TestData.NewServiceItem() with { DefaultUnitPrice = 500m, TaxRatePercent = 18m };

        var item = await client.CreateServiceItemAsync(creation);
        var repriced = new UpdateServiceItemRequest { Name = creation.Name, Category = creation.Category, DefaultUnitPrice = 550.5m, TaxRatePercent = 18m };
        item = await ChangeAsync<ServiceItemDto>(client, HttpMethod.Patch, BillingApi.ServiceItem(item.Id), repriced, item.Version);
        item = await ChangeAsync<ServiceItemDto>(client, HttpMethod.Post, BillingApi.ServiceItem(item.Id, "deactivate"), null, item.Version);
        await ChangeAsync<ServiceItemDto>(client, HttpMethod.Post, BillingApi.ServiceItem(item.Id, "reactivate"), null, item.Version);

        (await AllRecordsAsync(clerk)).Select(record => $"{record.EntityType}:{record.Action}")
            .ShouldBe(["ServiceItem:Created", "ServiceItem:Updated", "ServiceItem:Deactivated", "ServiceItem:Reactivated"]);
        var trail = await TrailAsync(client, "ServiceItem", item.Id);
        trail.Items[1].Before.ShouldNotBeNull().GetProperty("DefaultUnitPrice").GetDecimal().ShouldBe(500m);
        trail.Items[1].After.GetProperty("DefaultUnitPrice").GetDecimal().ShouldBe(550.5m);
    }

    [Fact]
    public async Task BR701_every_state_change_of_a_bill_writes_exactly_one_record_with_its_lines_and_the_reason_of_a_void()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var partiesRecords = (await AllRecordsAsync(clerk)).Count;

        var bill = await client.CreateBillAsync(NewBill(patient, payer, BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m)));
        bill = await ChangeAsync<BillDto>(client, HttpMethod.Patch, BillingApi.Bill(bill.Id), new UpdateBillHeaderRequest { BillDate = Today, DueDate = Today.AddDays(30), Notes = "Second visit" }, bill.Version);
        var twoLines = new ReplaceBillLinesRequest { Lines = [BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m), BillingApi.FreeTextLine("OPD consultation", 1m, 500m, 18m)] };
        bill = await ChangeAsync<BillDto>(client, HttpMethod.Put, BillingApi.Bill(bill.Id, "lines"), twoLines, bill.Version);
        bill = await ChangeAsync<BillDto>(client, HttpMethod.Put, BillingApi.Bill(bill.Id, "lines"), new ReplaceBillLinesRequest { Lines = [BillingApi.FreeTextLine("OPD consultation", 1m, 500m, 18m)] }, bill.Version);
        await ChangeAsync<BillDto>(client, HttpMethod.Post, BillingApi.Bill(bill.Id, "void"), new VoidBillRequest { Reason = " Entered twice " }, bill.Version);

        partiesRecords.ShouldBe(2, "the patient and the payer of the bill each have their own record");
        (await AllRecordsAsync(clerk)).Skip(partiesRecords).Select(record => $"{record.EntityType}:{record.Action}")
            .ShouldBe(["Bill:Drafted", "Bill:HeaderUpdated", "Bill:LinesReplaced", "Bill:LinesReplaced", "Bill:Voided"]);

        var trail = await TrailAsync(client, "Bill", bill.Id);
        trail.Items[0].Before.ShouldBeNull();
        trail.Items[0].After.GetProperty("Total").GetDecimal().ShouldBe(126.00m);
        trail.Items[0].After.GetProperty("Lines").GetArrayLength().ShouldBe(1);
        trail.Items[1].Before.ShouldNotBeNull().GetProperty("Notes").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        trail.Items[1].After.GetProperty("Notes").GetString().ShouldBe("Second visit");
        trail.Items[2].Before.ShouldNotBeNull().GetProperty("Lines").GetArrayLength().ShouldBe(1);
        trail.Items[2].After.GetProperty("Lines").GetArrayLength().ShouldBe(2);
        trail.Items[2].After.GetProperty("Total").GetDecimal().ShouldBe(716.00m);

        // A line that was removed is in the image before and gone from the image after.
        trail.Items[3].Before.ShouldNotBeNull().GetProperty("Lines").GetArrayLength().ShouldBe(2);
        trail.Items[3].After.GetProperty("Lines").EnumerateArray().ShouldHaveSingleItem().GetProperty("Description").GetString().ShouldBe("OPD consultation");
        trail.Items[4].Before.ShouldNotBeNull().GetProperty("Status").GetString().ShouldBe("Draft");
        trail.Items[4].After.GetProperty("Status").GetString().ShouldBe("Voided");
        trail.Items[4].Reason.ShouldBe("Entered twice");
        trail.Items.Take(4).ShouldAllBe(record => record.Reason == null);
    }

    [Fact]
    public async Task A_correction_that_changes_nothing_succeeds_and_writes_no_record()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var registration = TestData.NewPatient();
        var patient = await client.RegisterPatientAsync(registration);

        using var same = await client.SendWithIfMatchAsync(HttpMethod.Patch, PayersApi.Patient(patient.Id), Correction(registration), PayersApi.ETag(patient.Version));

        same.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await same.ReadAsAsync<PatientDto>()).Version.ShouldBe(patient.Version, "nothing was written, so the version stays");
        (await AllRecordsAsync(clerk)).ShouldHaveSingleItem().Action.ShouldBe("Registered");
    }

    [Fact]
    public async Task A_request_that_is_refused_writes_no_record()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var registration = TestData.NewPatient();
        var patient = await client.RegisterPatientAsync(registration);
        var etag = PayersApi.ETag(patient.Version);

        using var duplicate = await client.PostJsonAsync(PayersApi.Patients, TestData.NewPatient() with { Mrn = registration.Mrn });
        using var invalid = await client.SendWithIfMatchAsync(HttpMethod.Patch, PayersApi.Patient(patient.Id), Correction(registration) with { FullName = " " }, etag);
        using var alreadyActive = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Patient(patient.Id, "reactivate"), body: null, etag);
        using var stale = await client.SendWithIfMatchAsync(HttpMethod.Patch, PayersApi.Patient(patient.Id), Correction(registration) with { City = "Pune" }, PayersApi.ETag("AAAAAAAAAAE="));
        using var unknown = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Patient(Guid.NewGuid(), "deactivate"), body: null, etag);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        alreadyActive.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AllRecordsAsync(clerk)).ShouldHaveSingleItem().Action.ShouldBe("Registered");
    }

    [Fact]
    public async Task BR803_a_replayed_post_writes_no_second_record_and_a_refused_one_rolls_its_record_back()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var request = NewBill(patient, payer, BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m));
        var key = Guid.NewGuid();

        using var first = await client.PostJsonAsync(BillingApi.Bills, request, key);
        using var replay = await client.PostJsonAsync(BillingApi.Bills, request, key);
        var bill = await first.ReadAsAsync<BillDto>();

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.Headers.Contains("Idempotent-Replayed").ShouldBeTrue();
        (await TrailAsync(client, "Bill", bill.Id)).Items.ShouldHaveSingleItem().Action.ShouldBe("Drafted");

        // Voided once; the second void is refused with 422, inside a transaction the idempotency store rolls back.
        var voided = await ChangeAsync<BillDto>(client, HttpMethod.Post, BillingApi.Bill(bill.Id, "void"), new VoidBillRequest { Reason = "Entered twice" }, bill.Version);
        using var again = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "void"), new VoidBillRequest { Reason = "Again" }, PayersApi.ETag(voided.Version));

        again.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await TrailAsync(client, "Bill", bill.Id)).Items.Select(record => record.Action).ShouldBe(["Drafted", "Voided"]);
    }

    [Fact]
    public async Task BR002_another_tenants_record_has_an_empty_trail_exactly_like_an_id_nobody_has()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        using var stranger = factory.CreateClientFor(await NewApiTenantAsync(), "Viewer");
        var patient = await client.RegisterPatientAsync();

        var theirs = await TrailAsync(stranger, "Patient", patient.Id);
        var nobodys = await TrailAsync(stranger, "Patient", Guid.NewGuid());

        theirs.Items.ShouldBeEmpty();
        theirs.TotalCount.ShouldBe(0);
        nobodys.Items.ShouldBeEmpty();
        nobodys.TotalCount.ShouldBe(0);
        (await TrailAsync(client, "Patient", patient.Id)).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_trail_is_paged_oldest_first_and_names_one_kind_of_record()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var registration = TestData.NewPatient();
        var patient = await client.RegisterPatientAsync(registration);
        patient = await ChangeAsync<PatientDto>(client, HttpMethod.Post, PayersApi.Patient(patient.Id, "deactivate"), null, patient.Version);
        await ChangeAsync<PatientDto>(client, HttpMethod.Post, PayersApi.Patient(patient.Id, "reactivate"), null, patient.Version);

        using var secondPage = await client.GetAsync(Trail("Patient", patient.Id, "&page=2&pageSize=2"));
        var page = await secondPage.ReadAsAsync<PageOf<AuditRecordDto>>();

        page.Page.ShouldBe(2);
        page.PageSize.ShouldBe(2);
        page.TotalCount.ShouldBe(3);
        page.Items.ShouldHaveSingleItem().Action.ShouldBe("Reactivated");

        // The id of the patient under another kind of record is another record: its trail is empty.
        (await TrailAsync(client, "Payer", patient.Id)).Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("entityId=3f2504e0-4f89-11d3-9a0c-0305e82c3301", "entityType")]
    [InlineData("entityType=Patient", "entityId")]
    [InlineData("entityType=Patient&entityId=3f2504e0-4f89-11d3-9a0c-0305e82c3301&pageSize=101", "pageSize")]
    public async Task A_query_without_a_record_to_show_or_with_a_page_outside_the_limits_answers_400(string query, string field)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "Viewer");

        using var response = await client.GetAsync(new Uri($"/api/v1/audit-records?{query}", UriKind.Relative));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);
        problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe([field]);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task BR702_the_api_has_no_way_to_write_change_or_delete_an_audit_record(string method)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");

        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri("/api/v1/audit-records", UriKind.Relative)) { Content = PayersApi.RawJson("{}") };
        using var response = await client.SendAsync(request);
        using var byId = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), new Uri("/api/v1/audit-records/1", UriKind.Relative)) { Content = PayersApi.RawJson("{}") });

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        byId.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static Uri Trail(string entityType, Guid entityId, string more = "")
        => new($"/api/v1/audit-records?entityType={entityType}&entityId={entityId}{more}", UriKind.Relative);

    private static async Task<PageOf<AuditRecordDto>> TrailAsync(HttpClient client, string entityType, Guid entityId)
    {
        using var response = await client.GetAsync(Trail(entityType, entityId));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PageOf<AuditRecordDto>>();
    }

    // One change of an existing record: sent with the version the client read, it must succeed.
    private static async Task<T> ChangeAsync<T>(HttpClient client, HttpMethod method, Uri uri, object? body, string version)
        where T : class
    {
        using var response = await client.SendWithIfMatchAsync(method, uri, body, PayersApi.ETag(version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<T>();
    }

    // Every audit record of the tenant, straight from the table, in the order written: what "exactly one" is counted on.
    private async Task<List<AuditRecord>> AllRecordsAsync(FixedIdentity tenant)
    {
        await using var context = ApiContext(tenant);

        return await context.Set<AuditRecord>().AsNoTracking().OrderBy(record => record.Id).ToListAsync();
    }

    private static UpdatePatientRequest Correction(RegisterPatientRequest registration) => new()
    {
        FullName = registration.FullName,
        DateOfBirth = registration.DateOfBirth,
        Sex = registration.Sex,
        Phone = registration.Phone,
        Email = registration.Email,
        AddressLine1 = registration.AddressLine1,
        AddressLine2 = registration.AddressLine2,
        City = registration.City,
        State = registration.State,
        PinCode = registration.PinCode,
    };

    private static CreateBillRequest NewBill(PatientDto patient, PayerDto payer, params BillLineRequest[] lines)
        => new() { PatientId = patient.Id, PayerId = payer.Id, BillDate = Today, DueDate = Today.AddDays(15), Lines = lines };
}
