using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Payers;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Payers;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Serilog.Events;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The bill endpoints end to end, on the pipeline that ships and a real SQL Server (US-20, US-23; BR-102, BR-105,
/// BR-201 to BR-205, BR-804): drafts are created, edited and voided, and have no number.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class BillsApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    // The business date the API's clock shows: 17 October 2026 in India.
    private static readonly DateOnly Today = new(2026, 10, 17);

    [Fact]
    public async Task US20_a_draft_with_a_catalogue_line_and_a_free_text_line_answers_201_with_its_arithmetic_address_and_version()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var consultation = await client.CreateServiceItemAsync(TestData.NewServiceItem() with { Name = "OPD consultation", DefaultUnitPrice = 500m, TaxRatePercent = 18m });

        using var response = await client.PostJsonAsync(BillingApi.Bills, new CreateBillRequest
        {
            PatientId = patient.Id,
            PayerId = payer.Id,
            EncounterReference = "OPD-7781",
            Lines =
            [
                new BillLineRequest { ServiceItemId = consultation.Id, Quantity = 2m, DiscountAmount = 100m },
                new BillLineRequest { Description = "Dressing", Quantity = 3m, UnitPrice = 333.3333m, DiscountAmount = 0.50m, TaxRatePercent = 5m },
            ],
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var bill = await response.ReadAsAsync<BillDto>();
        bill.Status.ShouldBe(BillStatus.Draft);
        bill.PatientId.ShouldBe(patient.Id);
        bill.PayerId.ShouldBe(payer.Id);
        bill.EncounterReference.ShouldBe("OPD-7781");
        bill.Lines.Select(line => line.LineNo).ShouldBe([1, 2]);

        // 2 x 500.00 = 1000.00, less 100.00, 18% of 900.00 = 162.00.
        var fromCatalogue = bill.Lines[0];
        fromCatalogue.ServiceItemId.ShouldBe(consultation.Id);
        fromCatalogue.Description.ShouldBe("OPD consultation");
        fromCatalogue.UnitPrice.ShouldBe(500m);
        fromCatalogue.TaxRatePercent.ShouldBe(18m);
        fromCatalogue.LineAmount.ShouldBe(1000.00m);
        fromCatalogue.TaxAmount.ShouldBe(162.00m);
        fromCatalogue.LineTotal.ShouldBe(1062.00m);

        // The example of BR-202: 3 x 333.3333 = 1000.00, less 0.50, 5% of 999.50 = 49.98.
        var freeText = bill.Lines[1];
        freeText.ServiceItemId.ShouldBeNull();
        freeText.LineAmount.ShouldBe(1000.00m);
        freeText.TaxAmount.ShouldBe(49.98m);
        freeText.LineTotal.ShouldBe(1049.48m);

        bill.SubTotal.ShouldBe(2000.00m);
        bill.DiscountTotal.ShouldBe(100.50m);
        bill.TaxTotal.ShouldBe(211.98m);
        bill.Total.ShouldBe(2111.48m);
        bill.BalanceDue.ShouldBe(2111.48m);
        response.Headers.Location.ShouldNotBeNull().ToString().ShouldEndWith($"/api/v1/bills/{bill.Id}");
        response.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(bill.Version));

        using var read = await client.GetAsync(BillingApi.Bill(bill.Id));
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(bill.Version));
        (await read.ReadAsAsync<BillDto>()).ShouldBeEquivalentTo(bill);
    }

    [Fact]
    public async Task A_draft_has_no_number_in_any_form()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var bill = await client.CreateBillAsync(Draft(patient.Id, payer.Id, Dressing()));

        using var read = await client.GetAsync(BillingApi.Bill(bill.Id));
        var json = await read.Content.ReadAsStringAsync();

        json.ShouldNotContain("number", Case.Insensitive);
        json.ShouldNotContain("fiscalYear", Case.Insensitive);
    }

    [Fact]
    public async Task BR205_a_draft_without_dates_is_dated_today_in_India_and_due_after_the_tenants_default_days()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();

        var bill = await client.CreateBillAsync(Draft(patient.Id, payer.Id));

        bill.BillDate.ShouldBe(Today);
        bill.DueDate.ShouldBe(Today, "a new tenant has zero default due days");
        bill.Lines.ShouldBeEmpty();
        bill.Total.ShouldBe(0m);
    }

    [Fact]
    public async Task BR205_today_is_the_day_in_India_not_the_day_in_utc()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();

        // 20:00 UTC on the 17th is 01:30 on the 18th in India.
        factory.Clock.Set(new DateTimeOffset(2026, 10, 17, 20, 0, 0, TimeSpan.Zero));

        var bill = await client.CreateBillAsync(Draft(patient.Id, payer.Id));
        using var tomorrowThere = await client.PostJsonAsync(BillingApi.Bills, Draft(patient.Id, payer.Id) with { BillDate = new DateOnly(2026, 10, 19) });

        bill.BillDate.ShouldBe(new DateOnly(2026, 10, 18));
        await tomorrowThere.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.BillDateInFutureCode);
    }

    [Theory]
    [InlineData(1, null, BillingErrors.BillDateInFutureCode)]
    [InlineData(-8, null, BillingErrors.BillDateTooOldCode)]
    [InlineData(-2, -3, BillingErrors.BillDueDateBeforeBillDateCode)]
    public async Task BR205_a_date_that_is_not_allowed_answers_422_and_creates_nothing(int billDateFromToday, int? dueDateFromToday, string errorCode)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var request = Draft(patient.Id, payer.Id) with
        {
            BillDate = Today.AddDays(billDateFromToday),
            DueDate = dueDateFromToday is { } days ? Today.AddDays(days) : null,
        };

        using var response = await client.PostJsonAsync(BillingApi.Bills, request);

        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, errorCode);
        (await ListAsync(client, string.Empty)).ShouldBeEmpty();
    }

    [Fact]
    public async Task BR205_a_bill_may_be_dated_back_to_the_last_day_the_tenant_allows()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();

        var bill = await client.CreateBillAsync(Draft(patient.Id, payer.Id) with { BillDate = Today.AddDays(-7) });

        bill.BillDate.ShouldBe(Today.AddDays(-7));
        bill.DueDate.ShouldBe(Today.AddDays(-7));
    }

    [Fact]
    public async Task BR102_BR105_a_bill_for_an_unknown_or_inactive_patient_or_payer_is_refused()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var (inactivePatient, _) = await client.NewBillPartiesAsync();
        var inactivePayer = await client.CreatePayerAsync(new CreatePayerRequest { Kind = PayerKind.SelfPay, Name = "Rahul Sharma" });
        using var patientOff = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Patient(inactivePatient.Id, "deactivate"), body: null, PayersApi.ETag(inactivePatient.Version));
        using var payerOff = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Payer(inactivePayer.Id, "deactivate"), body: null, PayersApi.ETag(inactivePayer.Version));
        patientOff.StatusCode.ShouldBe(HttpStatusCode.OK);
        payerOff.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var unknownPatient = await client.PostJsonAsync(BillingApi.Bills, Draft(Guid.NewGuid(), payer.Id));
        using var unknownPayer = await client.PostJsonAsync(BillingApi.Bills, Draft(patient.Id, Guid.NewGuid()));
        using var forInactivePatient = await client.PostJsonAsync(BillingApi.Bills, Draft(inactivePatient.Id, payer.Id));
        using var toInactivePayer = await client.PostJsonAsync(BillingApi.Bills, Draft(patient.Id, inactivePayer.Id));

        await unknownPatient.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PatientNotFoundCode);
        await unknownPayer.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PayerNotFoundCode);
        await forInactivePatient.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PayersErrors.PatientInactiveCode);
        await toInactivePayer.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PayersErrors.PayerInactiveCode);
        (await ListAsync(client, string.Empty)).ShouldBeEmpty();
    }

    [Fact]
    public async Task BR106_a_guarantor_may_owe_the_bill_of_a_patient_they_are_not_linked_to()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var patient = await client.RegisterPatientAsync();
        var guarantor = await client.CreatePayerAsync(new CreatePayerRequest { Kind = PayerKind.SelfPay, Name = "Rahul Sharma" });

        var bill = await client.CreateBillAsync(Draft(patient.Id, guarantor.Id, Dressing()));

        bill.PayerId.ShouldBe(guarantor.Id);
    }

    [Fact]
    public async Task A_line_for_an_unknown_service_item_answers_404_and_for_an_inactive_one_422()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var retired = await client.CreateServiceItemAsync();
        using var deactivation = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.ServiceItem(retired.Id, "deactivate"), body: null, PayersApi.ETag(retired.Version));
        deactivation.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var unknown = await client.PostJsonAsync(BillingApi.Bills, Draft(patient.Id, payer.Id, new BillLineRequest { ServiceItemId = Guid.NewGuid(), Quantity = 1m }));
        using var inactive = await client.PostJsonAsync(BillingApi.Bills, Draft(patient.Id, payer.Id, new BillLineRequest { ServiceItemId = retired.Id, Quantity = 1m }));

        await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.ServiceItemNotFoundCode);
        await inactive.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.ServiceItemInactiveCode);
    }

    [Fact]
    public async Task A_bill_keeps_its_own_price_when_the_catalogue_changes_afterwards()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var item = await client.CreateServiceItemAsync(TestData.NewServiceItem() with { Name = "OPD consultation", DefaultUnitPrice = 500m, TaxRatePercent = 18m });
        var bill = await client.CreateBillAsync(Draft(patient.Id, payer.Id, new BillLineRequest { ServiceItemId = item.Id, Quantity = 1m }));

        using var repriced = await client.SendWithIfMatchAsync(
            HttpMethod.Patch,
            BillingApi.ServiceItem(item.Id),
            new UpdateServiceItemRequest { Name = "OPD consultation (revised)", DefaultUnitPrice = 900m, TaxRatePercent = 5m },
            PayersApi.ETag(item.Version));
        repriced.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var read = await client.GetAsync(BillingApi.Bill(bill.Id));
        var line = (await read.ReadAsAsync<BillDto>()).Lines.ShouldHaveSingleItem();
        line.Description.ShouldBe("OPD consultation");
        line.UnitPrice.ShouldBe(500m);
        line.TaxRatePercent.ShouldBe(18m);
        line.LineTotal.ShouldBe(590.00m);
    }

    [Fact]
    public async Task BR201_a_discount_larger_than_its_line_answers_422_and_a_malformed_line_400_under_its_position()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();

        using var discount = await client.PostJsonAsync(BillingApi.Bills, Draft(patient.Id, payer.Id, Dressing() with { DiscountAmount = 120.01m }));
        using var malformed = await client.PostJsonAsync(BillingApi.Bills, Draft(patient.Id, payer.Id, Dressing(), Dressing() with { Quantity = 0m, TaxRatePercent = 100.01m }));

        await discount.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.BillLineDiscountExceedsAmountCode);
        var problem = await malformed.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.GetProperty("errors").TryGetProperty("lines[1].quantity", out _).ShouldBeTrue();
        problem.GetProperty("errors").TryGetProperty("lines[1].taxRatePercent", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task A_draft_needs_a_patient_and_a_payer_and_refuses_members_it_does_not_know()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        using var unknownMember = new HttpRequestMessage(HttpMethod.Post, BillingApi.Bills)
        {
            Content = PayersApi.RawJson($$"""{"patientId":"{{Guid.NewGuid()}}","payerId":"{{Guid.NewGuid()}}","status":"Posted"}"""),
        };
        unknownMember.Headers.Add(PayersApi.IdempotencyKeyHeader, Guid.NewGuid().ToString());

        using var empty = await client.PostJsonAsync(BillingApi.Bills, new CreateBillRequest());
        using var withStatus = await client.SendAsync(unknownMember);

        var problem = await empty.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.GetProperty("errors").TryGetProperty("patientId", out _).ShouldBeTrue();
        problem.GetProperty("errors").TryGetProperty("payerId", out _).ShouldBeTrue();
        await withStatus.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task BR204_replacing_the_lines_with_the_etag_recomputes_the_bill_and_without_one_or_from_a_stale_one_changes_nothing()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var bill = await client.CreateBillAsync(Draft(patient.Id, payer.Id, Dressing(), Dressing(), Dressing()));
        var lines = new ReplaceBillLinesRequest { Lines = [BillingApi.FreeTextLine("Suture", 1m, 250m, 12m), Dressing()] };

        using var withoutIfMatch = await client.SendWithIfMatchAsync(HttpMethod.Put, BillingApi.Bill(bill.Id, "lines"), lines, ifMatch: null);
        using var replaced = await client.SendWithIfMatchAsync(HttpMethod.Put, BillingApi.Bill(bill.Id, "lines"), lines, PayersApi.ETag(bill.Version));
        using var stale = await client.SendWithIfMatchAsync(HttpMethod.Put, BillingApi.Bill(bill.Id, "lines"), new ReplaceBillLinesRequest { Lines = [] }, PayersApi.ETag(bill.Version));

        await withoutIfMatch.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        replaced.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await replaced.ReadAsAsync<BillDto>();
        after.Lines.Select(line => (line.LineNo, line.Description)).ShouldBe([(1, "Suture"), (2, "Dressing")]);
        after.SubTotal.ShouldBe(250.00m + 120.00m);
        after.TaxTotal.ShouldBe(30.00m + 6.00m);
        after.Total.ShouldBe(406.00m);
        after.BalanceDue.ShouldBe(406.00m);
        after.Version.ShouldNotBe(bill.Version);
        replaced.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(after.Version));
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");

        using var read = await client.GetAsync(BillingApi.Bill(bill.Id));
        (await read.ReadAsAsync<BillDto>()).ShouldBeEquivalentTo(after);
        (await LineRowsAsync(bill.Id)).ShouldBe(2, "the third line was deleted, not kept");
    }

    [Fact]
    public async Task BR804_a_change_of_the_lines_alone_gives_the_bill_a_new_version_and_a_new_update_stamp()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var bill = await client.CreateBillAsync(Draft(patient.Id, payer.Id, Dressing()));
        factory.Clock.Advance(TimeSpan.FromMinutes(5));

        // Only the description of the one line changes: no column of the bill's own row would.
        using var replaced = await client.SendWithIfMatchAsync(
            HttpMethod.Put,
            BillingApi.Bill(bill.Id, "lines"),
            new ReplaceBillLinesRequest { Lines = [Dressing() with { Description = "Dressing, large" }] },
            PayersApi.ETag(bill.Version));

        var after = await replaced.ReadAsAsync<BillDto>();
        after.Total.ShouldBe(bill.Total);
        after.Version.ShouldNotBe(bill.Version);
        after.UpdatedAtUtc.ShouldBe(bill.UpdatedAtUtc.AddMinutes(5));
        after.CreatedAtUtc.ShouldBe(bill.CreatedAtUtc);
    }

    [Fact]
    public async Task BR804_of_ten_simultaneous_changes_of_the_lines_from_one_version_exactly_one_succeeds()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var bill = await client.CreateBillAsync(Draft(patient.Id, payer.Id, Dressing()));

        var responses = await Task.WhenAll(Enumerable.Range(1, 10).Select(number => client.SendWithIfMatchAsync(
            HttpMethod.Put,
            BillingApi.Bill(bill.Id, "lines"),
            new ReplaceBillLinesRequest { Lines = [Dressing() with { Description = $"Dressing, clerk {number}" }] },
            PayersApi.ETag(bill.Version))));

        try
        {
            responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
            foreach (var refused in responses.Where(response => response.StatusCode != HttpStatusCode.OK))
            {
                await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");
            }

            factory.Logs.Events.ShouldNotContain(logEvent => logEvent.Level >= LogEventLevel.Error);
            (await LineRowsAsync(bill.Id)).ShouldBe(1);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_service_item_deactivated_since_stays_on_the_draft_that_has_it_and_cannot_join_another()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var item = await client.CreateServiceItemAsync();
        var withItem = await client.CreateBillAsync(Draft(patient.Id, payer.Id, new BillLineRequest { ServiceItemId = item.Id, Quantity = 1m }));
        var withoutItem = await client.CreateBillAsync(Draft(patient.Id, payer.Id, Dressing()));
        using var deactivation = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.ServiceItem(item.Id, "deactivate"), body: null, PayersApi.ETag(item.Version));
        deactivation.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lines = new ReplaceBillLinesRequest { Lines = [new BillLineRequest { ServiceItemId = item.Id, Quantity = 4m }] };

        using var stays = await client.SendWithIfMatchAsync(HttpMethod.Put, BillingApi.Bill(withItem.Id, "lines"), lines, PayersApi.ETag(withItem.Version));
        using var joins = await client.SendWithIfMatchAsync(HttpMethod.Put, BillingApi.Bill(withoutItem.Id, "lines"), lines, PayersApi.ETag(withoutItem.Version));

        (await stays.ReadAsAsync<BillDto>()).Lines.ShouldHaveSingleItem().Quantity.ShouldBe(4m);
        await joins.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.ServiceItemInactiveCode);
    }

    [Fact]
    public async Task BR204_correcting_the_header_replaces_the_dates_and_texts_and_cannot_touch_the_parties_the_status_or_the_lines()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var bill = await client.CreateBillAsync(Draft(patient.Id, payer.Id, Dressing()) with { EncounterReference = "OPD-1", Notes = "First visit" });
        var correction = new UpdateBillHeaderRequest { BillDate = Today.AddDays(-2), DueDate = Today.AddDays(28), Notes = "Follow-up" };

        using var withoutIfMatch = await client.SendWithIfMatchAsync(HttpMethod.Patch, BillingApi.Bill(bill.Id), correction, ifMatch: null);
        using var corrected = await client.SendWithIfMatchAsync(HttpMethod.Patch, BillingApi.Bill(bill.Id), correction, PayersApi.ETag(bill.Version));
        var after = await corrected.ReadAsAsync<BillDto>();
        using var stale = await client.SendWithIfMatchAsync(HttpMethod.Patch, BillingApi.Bill(bill.Id), correction, PayersApi.ETag(bill.Version));
        using var withoutDates = await client.SendWithIfMatchAsync(HttpMethod.Patch, BillingApi.Bill(bill.Id), new UpdateBillHeaderRequest { Notes = "No dates" }, PayersApi.ETag(after.Version));
        using var tooOld = await client.SendWithIfMatchAsync(HttpMethod.Patch, BillingApi.Bill(bill.Id), correction with { BillDate = Today.AddDays(-8) }, PayersApi.ETag(after.Version));
        using var otherPatient = new HttpRequestMessage(HttpMethod.Patch, BillingApi.Bill(bill.Id))
        {
            Content = PayersApi.RawJson($$"""{"billDate":"2026-10-17","dueDate":"2026-10-17","patientId":"{{Guid.NewGuid()}}"}"""),
        };
        otherPatient.Headers.TryAddWithoutValidation("If-Match", PayersApi.ETag(after.Version));
        using var refusedMember = await client.SendAsync(otherPatient);

        await withoutIfMatch.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        after.BillDate.ShouldBe(Today.AddDays(-2));
        after.DueDate.ShouldBe(Today.AddDays(28));
        after.EncounterReference.ShouldBeNull("a text the correction leaves out is cleared");
        after.Notes.ShouldBe("Follow-up");
        after.PatientId.ShouldBe(patient.Id);
        after.Lines.ShouldHaveSingleItem();
        after.Total.ShouldBe(bill.Total);
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");
        var missing = await withoutDates.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        missing.GetProperty("errors").TryGetProperty("billDate", out _).ShouldBeTrue();
        missing.GetProperty("errors").TryGetProperty("dueDate", out _).ShouldBeTrue();
        await tooOld.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.BillDateTooOldCode);
        await refusedMember.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");

        using var read = await client.GetAsync(BillingApi.Bill(bill.Id));
        (await read.ReadAsAsync<BillDto>()).ShouldBeEquivalentTo(after);
    }

    [Fact]
    public async Task US23_a_draft_is_voided_with_a_reason_keeps_its_content_and_refuses_every_change_afterwards()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var bill = await client.CreateBillAsync(Draft(patient.Id, payer.Id, Dressing(), Dressing()));
        var etag = PayersApi.ETag(bill.Version);

        using var withoutIfMatch = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "void"), new VoidBillRequest { Reason = "Entered twice" }, ifMatch: null);
        using var withoutReason = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "void"), new VoidBillRequest { Reason = "  " }, etag);
        using var voided = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "void"), new VoidBillRequest { Reason = " Entered twice " }, etag);
        var after = await voided.ReadAsAsync<BillDto>();
        var voidedETag = PayersApi.ETag(after.Version);
        using var stale = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "void"), new VoidBillRequest { Reason = "Stale" }, etag);
        using var again = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "void"), new VoidBillRequest { Reason = "Again" }, voidedETag);
        using var lines = await client.SendWithIfMatchAsync(HttpMethod.Put, BillingApi.Bill(bill.Id, "lines"), new ReplaceBillLinesRequest { Lines = [] }, voidedETag);
        using var header = await client.SendWithIfMatchAsync(HttpMethod.Patch, BillingApi.Bill(bill.Id), new UpdateBillHeaderRequest { BillDate = Today, DueDate = Today }, voidedETag);

        await withoutIfMatch.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        var problem = await withoutReason.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.GetProperty("errors").TryGetProperty("reason", out _).ShouldBeTrue();
        voided.StatusCode.ShouldBe(HttpStatusCode.OK);
        after.Status.ShouldBe(BillStatus.Voided);
        after.VoidReason.ShouldBe("Entered twice");
        after.VoidedAtUtc.ShouldBe(DatabaseApiFactory.InitialInstant);
        after.Lines.Count.ShouldBe(2);
        after.Total.ShouldBe(bill.Total);
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");
        await again.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.BillNotDraftCode);
        await lines.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.BillNotDraftCode);
        await header.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.BillNotDraftCode);

        using var read = await client.GetAsync(BillingApi.Bill(bill.Id));
        (await read.ReadAsAsync<BillDto>()).ShouldBeEquivalentTo(after);
        (await LineRowsAsync(bill.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task BR005_a_bill_cannot_be_deleted_and_an_unknown_one_is_not_found()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var bill = await client.CreateBillAsync(Draft(patient.Id, payer.Id, Dressing()));

        using var deleted = await client.DeleteAsync(BillingApi.Bill(bill.Id));
        using var unknown = await client.GetAsync(BillingApi.Bill(Guid.NewGuid()));
        using var read = await client.GetAsync(BillingApi.Bill(bill.Id));

        deleted.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Listing_bills_newest_date_first_with_filters_for_the_patient_the_payer_the_status_and_the_dates()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (priya, priyasPayer) = await client.NewBillPartiesAsync();
        var (anil, anilsPayer) = await client.NewBillPartiesAsync();
        var oldest = await client.CreateBillAsync(Draft(priya.Id, priyasPayer.Id, Dressing()) with { BillDate = Today.AddDays(-5) });
        var middle = await client.CreateBillAsync(Draft(anil.Id, anilsPayer.Id, Dressing()) with { BillDate = Today.AddDays(-2) });
        var newest = await client.CreateBillAsync(Draft(priya.Id, anilsPayer.Id, Dressing()));
        using var voiding = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(middle.Id, "void"), new VoidBillRequest { Reason = "Wrong patient" }, PayersApi.ETag(middle.Version));
        voiding.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ListAsync(client, string.Empty)).ShouldBe([newest.Id, middle.Id, oldest.Id]);
        (await ListAsync(client, $"patientId={priya.Id}")).ShouldBe([newest.Id, oldest.Id]);
        (await ListAsync(client, $"payerId={anilsPayer.Id}")).ShouldBe([newest.Id, middle.Id]);
        (await ListAsync(client, "status=Voided")).ShouldBe([middle.Id]);
        (await ListAsync(client, "status=Draft")).ShouldBe([newest.Id, oldest.Id]);
        (await ListAsync(client, $"billDateFrom={Today.AddDays(-2):yyyy-MM-dd}")).ShouldBe([newest.Id, middle.Id]);
        (await ListAsync(client, $"billDateTo={Today.AddDays(-2):yyyy-MM-dd}")).ShouldBe([middle.Id, oldest.Id]);
        (await ListAsync(client, $"billDateFrom={Today.AddDays(-2):yyyy-MM-dd}&billDateTo={Today.AddDays(-2):yyyy-MM-dd}&status=Draft")).ShouldBeEmpty();

        using var page = await client.GetAsync(BillingApi.Bills.WithQuery("page=2&pageSize=2"));
        using var badRange = await client.GetAsync(BillingApi.Bills.WithQuery("billDateFrom=2026-10-17&billDateTo=2026-10-16"));
        var second = await page.ReadAsAsync<PageOf<BillSummaryDto>>();
        second.Items.ShouldHaveSingleItem().Id.ShouldBe(oldest.Id);
        second.TotalCount.ShouldBe(3);
        await badRange.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    private static async Task<Guid[]> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(query.Length == 0 ? BillingApi.Bills : BillingApi.Bills.WithQuery(query));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return [.. (await response.ReadAsAsync<PageOf<BillSummaryDto>>()).Items.Select(bill => bill.Id)];
    }

    private static CreateBillRequest Draft(Guid patientId, Guid payerId, params BillLineRequest[] lines)
        => new() { PatientId = patientId, PayerId = payerId, Lines = lines };

    // 2 x 60.00 at 5%: 120.00 + 6.00 = 126.00.
    private static BillLineRequest Dressing() => BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m);

    // The rows in the table, whatever the API says: counted without the tenant filter, as system work.
    private async Task<int> LineRowsAsync(Guid billId)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [dbo].[BillLines] WHERE [BillId] = {billId}").SingleAsync();
    }
}
