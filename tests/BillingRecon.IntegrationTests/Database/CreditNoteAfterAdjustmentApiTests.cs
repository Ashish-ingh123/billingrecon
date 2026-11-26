using System.Net;
using BillingRecon.Api.Adjustments;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Adjustments;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// A credit note after an adjustment, end to end on the pipeline that ships and a real SQL Server (review 2026-11,
/// finding R1; ADR 0025, amended 2026-11-26; BR-210): what a bill's adjustments took off it cannot be credited, so a
/// bill written off and never paid leaves no credit to refund as cash. Each step is approved by a second person, as
/// the finding's own sequence was.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class CreditNoteAfterAdjustmentApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Uri CreditNotes = new("/api/v1/credit-notes", UriKind.Relative);
    private static readonly Uri Adjustments = new("/api/v1/adjustments", UriKind.Relative);
    private static readonly Uri Refunds = new("/api/v1/refunds", UriKind.Relative);
    private static readonly DateOnly Today = new(2026, 10, 17);

    // 2 x 60.00 at 5%: 126.00.
    private static readonly BillLineRequest Dressing = BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m);

    // 1 x 500.00 at 18%: 590.00.
    private static readonly BillLineRequest Consultation = BillingApi.FreeTextLine("OPD consultation", 1m, 500m, 18m);

    [Fact]
    public async Task BR210_a_bill_written_off_in_full_cannot_be_credited_so_nothing_of_it_is_ever_refunded()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk", "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();

        // 1. A bill of 126.00 that nobody pays.  2. It is written off in full.
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);
        await WrittenOffAsync(clerk, supervisor, bill, 126m);
        (await BillAsync(clerk, bill.Id)).BalanceDue.ShouldBe(0.00m);

        // 3. A credit note for the whole bill, which the cap let through before: refused when the maker asks.
        var whole = await DraftedAsync(clerk, bill, new CreditNoteLineRequest { BillLineId = bill.Lines.Single().Id, Quantity = 2m });
        using (var refused = await clerk.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(whole.Id, "post"), body: null, PayersApi.ETag(whole.Version)))
        {
            var problem = await refused.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditNoteExceedsBillTotalCode);
            problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("taken off by adjustments");
        }

        // Nor one paisa of it.
        var paisa = await DraftedAsync(clerk, bill, new CreditNoteLineRequest { Description = "Goodwill", Quantity = 1m, UnitPrice = 0.01m, TaxRatePercent = 0m });
        using (var refused = await clerk.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(paisa.Id, "post"), body: null, PayersApi.ETag(paisa.Version)))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditNoteExceedsBillTotalCode);
        }

        // 4. So there is no credit to draw a refund from: the payer holds nothing.
        using var refund = await clerk.PostJsonAsync(Refunds, new CreateRefundRequest { CreditNoteId = whole.Id, Amount = 126m, Method = PaymentMethod.Cash, Reason = "Refund of the credit" });
        refund.IsSuccessStatusCode.ShouldBeFalse("a credit note that was never posted holds no credit to refund");
    }

    [Fact]
    public async Task BR210_a_bill_written_off_in_part_can_be_credited_what_is_left_of_it_and_not_a_paisa_more()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk", "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();

        // A bill of 716.00; 590.00 of it is written off: 126.00 is left to credit.
        var bill = await PostedBillAsync(clerk, patient, payer, Consultation, Dressing);
        await WrittenOffAsync(clerk, supervisor, bill, 590m);

        var over = await DraftedAsync(clerk, bill, new CreditNoteLineRequest { Description = "Goodwill", Quantity = 1m, UnitPrice = 126.01m, TaxRatePercent = 0m });
        using (var refused = await clerk.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(over.Id, "post"), body: null, PayersApi.ETag(over.Version)))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditNoteExceedsBillTotalCode);
        }

        // Exactly what is left is credited, and all of it goes to the bill: nothing stays to be refunded.
        var dressing = bill.Lines.Single(line => line.Description == "Dressing");
        var rest = await ApprovedAsync(clerk, supervisor, await DraftedAsync(clerk, bill, new CreditNoteLineRequest { BillLineId = dressing.Id, Quantity = 2m }));
        (rest.Status, rest.Total, rest.AvailableAmount).ShouldBe((CreditNoteStatus.Posted, 126.00m, 0.00m));
        var settled = await BillAsync(clerk, bill.Id);
        (settled.Status, settled.BalanceDue).ShouldBe((BillStatus.Settled, 0.00m));
    }

    [Fact]
    public async Task BR210_a_credit_note_that_waited_while_its_bill_was_written_off_is_refused_at_its_approval()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk", "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);

        // The credit note fits when its maker asks, and waits for a checker.
        var draft = await DraftedAsync(clerk, bill, new CreditNoteLineRequest { BillLineId = bill.Lines.Single().Id, Quantity = 2m });
        using var submitted = await clerk.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        submitted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await submitted.Content.ReadAsStringAsync());
        var waiting = await submitted.ReadAsAsync<CreditNoteDto>();

        // Meanwhile the bill is written off in full.
        await WrittenOffAsync(clerk, supervisor, bill, 126m);

        // The approval measures the cap again and refuses: no number is taken and nothing is posted.
        using var approval = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));
        await approval.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditNoteExceedsBillTotalCode);

        using var read = await clerk.GetAsync(CreditNote(draft.Id));
        var after = await read.ReadAsAsync<CreditNoteDto>();
        (after.Status, after.Number).ShouldBe((CreditNoteStatus.PendingApproval, null));
    }

    private static Uri CreditNote(Guid id) => new($"/api/v1/credit-notes/{id}", UriKind.Relative);

    private static Uri CreditNote(Guid id, string action) => new($"/api/v1/credit-notes/{id}/{action}", UriKind.Relative);

    private static Uri Adjustment(Guid id, string action) => new($"/api/v1/adjustments/{id}/{action}", UriKind.Relative);

    private static async Task<BillDto> PostedBillAsync(HttpClient client, PatientDto patient, PayerDto payer, params BillLineRequest[] lines)
    {
        var draft = await client.CreateBillAsync(new CreateBillRequest { PatientId = patient.Id, PayerId = payer.Id, BillDate = Today, DueDate = Today.AddDays(15), Lines = lines });
        using var response = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<BillDto>();
    }

    private static async Task<BillDto> BillAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(BillingApi.Bill(id));

        return await response.ReadAsAsync<BillDto>();
    }

    // A write-off drafted and submitted by the maker and approved by the checker, which posts it.
    private static async Task WrittenOffAsync(HttpClient maker, HttpClient checker, BillDto bill, decimal amount)
    {
        using var created = await maker.PostJsonAsync(Adjustments, new CreateAdjustmentRequest { BillId = bill.Id, AdjustmentDate = Today, Kind = AdjustmentKind.WriteOff, Amount = amount, Reason = "Will not be collected" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var draft = await created.ReadAsAsync<AdjustmentDto>();

        using var submitted = await maker.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        submitted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await submitted.Content.ReadAsStringAsync());
        var waiting = await submitted.ReadAsAsync<AdjustmentDto>();

        using var approved = await checker.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(draft.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
    }

    private static async Task<CreditNoteDto> DraftedAsync(HttpClient client, BillDto bill, params CreditNoteLineRequest[] lines)
    {
        using var response = await client.PostJsonAsync(CreditNotes, new CreateCreditNoteRequest { BillId = bill.Id, CreditNoteDate = Today, Reason = "Dressing not done", Lines = lines });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<CreditNoteDto>();
    }

    private static async Task<CreditNoteDto> ApprovedAsync(HttpClient maker, HttpClient checker, CreditNoteDto draft)
    {
        using var submitted = await maker.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        submitted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await submitted.Content.ReadAsStringAsync());
        var waiting = await submitted.ReadAsAsync<CreditNoteDto>();

        using var response = await checker.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<CreditNoteDto>();
    }

    private async Task<(FixedIdentity Maker, FixedIdentity Checker)> NewMakerAndCheckerAsync()
    {
        var maker = await NewApiTenantAsync();

        return (maker, new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid())));
    }
}
