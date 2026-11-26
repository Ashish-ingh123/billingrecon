using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Identity;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// What a printed document needs, end to end on the pipeline that ships and a real SQL Server (Prompt 10.4, ADR 0054):
/// the hospital's letterhead, set by its administrator and read by anyone who is signed in; the SAC code of a service
/// item, which a bill line keeps as it was when the line was written; and the record of each print, whose first is the
/// original and whose every later one is a duplicate.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PrintedDocumentsApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly Uri Settings = new("/api/v1/settings", UriKind.Relative);
    private static readonly Uri Letterhead = new("/api/v1/settings/letterhead", UriKind.Relative);

    private static readonly UpdateLetterheadRequest Northfield = new()
    {
        LegalName = " Northfield Care Hospital Private Limited ",
        AddressLine1 = "12 Example Road",
        City = "Pune",
        State = "Maharashtra",
        PinCode = "411001",
        Phone = "+91 00000 00000",
        Email = "accounts@northfield.example",
        Gstin = "27aaaaa0000a1z5",
        Signatory = "Authorised signatory",
    };

    [Fact]
    public async Task ADR0054_the_administrator_sets_the_letterhead_and_anyone_signed_in_reads_it()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        using var cashier = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "Cashier");

        using var before = await cashier.GetAsync(Letterhead);
        var empty = await before.ReadAsAsync<LetterheadDto>();
        empty.Name.ShouldNotBeNullOrWhiteSpace();
        (empty.LegalName, empty.State, empty.Gstin, empty.Signatory).ShouldBe((null, null, null, null));

        using var read = await administrator.GetAsync(Settings);
        var settings = await read.ReadAsAsync<TenantSettingsDto>();

        // Without the version, and with a GSTIN of the wrong shape, nothing is written.
        using (var noVersion = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Letterhead, Northfield, ifMatch: null))
        {
            noVersion.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        }

        using (var wrong = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Letterhead, Northfield with { Gstin = "27AAAAA0000A1Z" }, PayersApi.ETag(settings.Version)))
        {
            var problem = await wrong.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
            problem.GetProperty("errors").EnumerateObject().Select(field => field.Name).ShouldBe(["gstin"]);
        }

        using (var notTheirs = await cashier.SendWithIfMatchAsync(HttpMethod.Put, Letterhead, Northfield, PayersApi.ETag(settings.Version)))
        {
            await notTheirs.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        using var saved = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Letterhead, Northfield, PayersApi.ETag(settings.Version));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var after = await saved.ReadAsAsync<TenantSettingsDto>();
        after.Version.ShouldNotBe(settings.Version, "the letterhead shares the version of the settings");
        after.Letterhead.ShouldBe(new LetterheadDto(
            empty.Name, "Northfield Care Hospital Private Limited", "12 Example Road", null, "Pune", "Maharashtra", "411001", "+91 00000 00000",
            "accounts@northfield.example", "27AAAAA0000A1Z5", "Authorised signatory", null, null, null));

        // The version that was read before is stale now, and the cashier reads what was set.
        using (var stale = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Letterhead, Northfield with { City = "Nashik" }, PayersApi.ETag(settings.Version)))
        {
            await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");
        }

        using var seen = await cashier.GetAsync(Letterhead);
        (await seen.ReadAsAsync<LetterheadDto>()).ShouldBe(after.Letterhead);

        using var trail = await administrator.GetAsync(new Uri($"/api/v1/audit-records?entityType=Tenant&entityId={admin.TenantId!.Value.Value}", UriKind.Relative));
        var records = (await trail.ReadAsAsync<PageOf<AuditRecordDto>>()).Items;
        var record = records.Where(entry => entry.Action == "LetterheadChanged").ShouldHaveSingleItem();
        record.After.GetProperty("Gstin").GetString().ShouldBe("27AAAAA0000A1Z5");
    }

    [Fact]
    public async Task ADR0054_a_bill_line_keeps_the_sac_code_its_service_had_when_the_line_was_written()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");
        var (patient, payer) = await client.NewBillPartiesAsync();

        using (var wrong = await client.PostJsonAsync(BillingApi.ServiceItems, TestData.NewServiceItem() with { SacCode = "99931" }))
        {
            var problem = await wrong.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
            problem.GetProperty("errors").EnumerateObject().Select(field => field.Name).ShouldBe(["sacCode"]);
        }

        var item = await client.CreateServiceItemAsync(TestData.NewServiceItem() with { DefaultUnitPrice = 350m, TaxRatePercent = 0m, SacCode = " 999311 " });
        item.SacCode.ShouldBe("999311");
        var plain = await client.CreateServiceItemAsync();
        plain.SacCode.ShouldBeNull();

        var draft = await client.CreateBillAsync(new CreateBillRequest
        {
            PatientId = patient.Id,
            PayerId = payer.Id,
            BillDate = Today,
            DueDate = Today,
            Lines =
            [
                new BillLineRequest { ServiceItemId = item.Id, Quantity = 1m },
                BillingApi.FreeTextLine("Consultation", 1m, 500m, 0m),
            ],
        });
        draft.Lines.Select(line => line.SacCode).ShouldBe(["999311", null]);

        using var posted = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());

        // The catalogue changes afterwards; the posted bill prints the code it was raised under.
        using var changed = await client.SendWithIfMatchAsync(
            HttpMethod.Patch,
            BillingApi.ServiceItem(item.Id),
            new UpdateServiceItemRequest { Name = item.Name, Category = item.Category, DefaultUnitPrice = 350m, TaxRatePercent = 0m, SacCode = "999312" },
            PayersApi.ETag(item.Version));
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        (await changed.ReadAsAsync<ServiceItemDto>()).SacCode.ShouldBe("999312");

        using var read = await client.GetAsync(BillingApi.Bill(draft.Id));
        (await read.ReadAsAsync<BillDto>()).Lines.Select(line => line.SacCode).ShouldBe(["999311", null]);
    }

    [Fact]
    public async Task ADR0054_the_first_print_of_a_document_is_the_original_and_every_later_one_a_duplicate()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");
        using var cashier = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "Cashier");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var draft = await client.CreateBillAsync(new CreateBillRequest
        {
            PatientId = patient.Id,
            PayerId = payer.Id,
            BillDate = Today,
            DueDate = Today,
            Lines = [BillingApi.FreeTextLine("Consultation", 1m, 500m, 0m)],
        });
        var prints = new Uri($"/api/v1/bills/{draft.Id}/prints", UriKind.Relative);

        // A draft is not a document yet.
        using (var early = await client.PostJsonAsync(prints, new { }))
        {
            await early.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, CommonErrors.DocumentNotPrintableCode);
        }

        using (var posted = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version)))
        {
            posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());
        }

        // The original; the same request sent again is the same print; a new request is a duplicate.
        var key = Guid.NewGuid();
        var first = await PrintAsync(client, prints, key);
        (first.CopyNumber, first.Original, first.PrintedAtUtc).ShouldBe((1, true, DatabaseApiFactory.InitialInstant));
        (await PrintAsync(client, prints, key)).ShouldBe(first);

        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var second = await PrintAsync(cashier, prints, Guid.NewGuid());
        (second.CopyNumber, second.Original, second.PrintedAtUtc).ShouldBe((2, false, DatabaseApiFactory.InitialInstant.AddMinutes(5)));
        (await PrintAsync(client, prints, Guid.NewGuid())).CopyNumber.ShouldBe(3);

        // A payment and its receipt; each document counts its own prints.
        using var recorded = await cashier.PostJsonAsync(new Uri("/api/v1/payments", UriKind.Relative), new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.Cash, Amount = 500m });
        var payment = await recorded.ReadAsAsync<PaymentDto>();
        var receipt = await PrintAsync(cashier, new Uri($"/api/v1/payments/{payment.Id}/prints", UriKind.Relative), Guid.NewGuid());
        (receipt.CopyNumber, receipt.Original).ShouldBe((1, true));

        // What does not exist, and what is another hospital's.
        using (var missing = await client.PostJsonAsync(new Uri($"/api/v1/refunds/{Guid.NewGuid()}/prints", UriKind.Relative), new { }))
        {
            await missing.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.RefundNotFoundCode);
        }

        var elsewhere = await NewApiTenantAsync();
        using var other = factory.CreateClientFor(elsewhere, "TenantAdmin");
        using (var foreign = await other.PostJsonAsync(prints, new { }))
        {
            await foreign.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        }

        await using var context = ApiContext(admin);
        var rows = await context.Set<DocumentPrint>().AsNoTracking().ToListAsync();
        rows = [.. rows.OrderBy(print => print.DocumentType).ThenBy(print => print.CopyNumber)];
        rows.Select(row => (row.DocumentType, row.DocumentId, row.CopyNumber)).ShouldBe(
        [
            (PrintedDocumentType.Bill, draft.Id, 1),
            (PrintedDocumentType.Bill, draft.Id, 2),
            (PrintedDocumentType.Bill, draft.Id, 3),
            (PrintedDocumentType.Payment, payment.Id, 1),
        ]);
        rows[0].PrintedByUserId.ShouldBe(admin.UserId);
        rows[1].PrintedByUserId.ShouldNotBe(admin.UserId, "the cashier printed the second");
    }

    private static async Task<DocumentPrintDto> PrintAsync(HttpClient client, Uri prints, Guid key)
    {
        using var response = await client.PostJsonAsync(prints, new { }, key);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<DocumentPrintDto>();
    }
}
