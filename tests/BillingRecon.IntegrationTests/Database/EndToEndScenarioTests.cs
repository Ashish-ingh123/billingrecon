using System.Net;
using BillingRecon.Api.Adjustments;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Adjustments;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.Infrastructure.Receivables;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// One payer's month, end to end through every financial document of R1 (Prompt 4.6): two bills, a part payment, a
/// credit note, an adjustment, the reversal of an allocation and a refund. Each step is checked against what it should
/// leave, and the end against the ledger: no drift (BR-404), and every series of numbers gapless (BR-801,
/// docs/database.md section 4.3).
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class EndToEndScenarioTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Uri Refunds = new("/api/v1/refunds", UriKind.Relative);
    private static readonly Uri Adjustments = new("/api/v1/adjustments", UriKind.Relative);

    // 17 October 2026 in India: fiscal year 2026-27.
    private static readonly DateOnly Today = new(2026, 10, 17);

    // The tables of the documents that take a gapless number, with the document type of their series.
    private static readonly (string Table, string DocumentType, int Documents)[] Series =
    [
        ("Bills", "Bill", 2),
        ("CreditNotes", "CreditNote", 1),
        ("Payments", "Payment", 1),
        ("Adjustments", "Adjustment", 1),
        ("Refunds", "Refund", 1),
    ];

    [Fact]
    public async Task A_bill_part_payment_credit_note_adjustment_allocation_reversal_and_refund_end_with_no_drift_and_gapless_series()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var maker = await NewApiTenantAsync();
        var checker = new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid()));
        using var clerk = factory.CreateClientFor(maker, "BillingClerk", "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();

        // Two bills: the admission of 716.00 (a consultation of 590.00 and a dressing of 126.00), and a dressing of 126.00.
        var admission = await PostedBillAsync(clerk, patient, payer, BillingApi.FreeTextLine("OPD consultation", 1m, 500m, 18m), BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m));
        var dressing = await PostedBillAsync(clerk, patient, payer, BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m));
        await StepAsync(maker, payer, receivable: 842m, unapplied: 0m, credit: 0m);

        // A part payment of 500.00: 400.00 to the admission, 100.00 to the dressing.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var payment = await RecordedAsync(clerk, payer, 500m);
        var allocated = await AllocatedAsync(clerk, payment, (admission.Id, 400m), (dressing.Id, 100m));
        await StepAsync(maker, payer, receivable: 342m, unapplied: 0m, credit: 0m);
        (await BillAsync(clerk, admission.Id)).BalanceDue.ShouldBe(316.00m);

        // A credit note for the dressing of the admission: all of its 126.00 goes to the admission, which owes 190.00.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var creditNote = await ApprovedAsync(clerk, supervisor, CreditNote, await DraftedCreditNoteAsync(clerk, admission));
        creditNote.AvailableAmount.ShouldBe(0.00m);
        (await BillAsync(clerk, admission.Id)).BalanceDue.ShouldBe(190.00m);
        await StepAsync(maker, payer, receivable: 216m, unapplied: 0m, credit: 0m);

        // A discount of 90.00 on the admission, which owes 100.00.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        using var drafted = await clerk.PostJsonAsync(Adjustments, new CreateAdjustmentRequest { BillId = admission.Id, Kind = AdjustmentKind.Discount, Amount = 90m, Reason = "Staff discount" });
        drafted.StatusCode.ShouldBe(HttpStatusCode.Created, await drafted.Content.ReadAsStringAsync());
        var adjustment = await ApprovedAsync(clerk, supervisor, Adjustment, await drafted.ReadAsAsync<AdjustmentDto>());
        adjustment.Status.ShouldBe(AdjustmentStatus.Posted);
        (await BillAsync(clerk, admission.Id)).BalanceDue.ShouldBe(100.00m);
        await StepAsync(maker, payer, receivable: 126m, unapplied: 0m, credit: 0m);

        // The 100.00 on the dressing bill were the wrong bill: the allocation is reversed, the bill owes 126.00 again.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var wrong = allocated.Allocations.Single(allocation => allocation.BillId == dressing.Id);
        var reversed = await AllocationReversedAsync(clerk, supervisor, payment.Id, wrong.Id, "Applied to the wrong bill");
        reversed.Payment.UnappliedAmount.ShouldBe(100.00m);
        var owed = await BillAsync(clerk, dressing.Id);
        (owed.Status, owed.BalanceDue).ShouldBe((BillStatus.Posted, 126.00m));
        await StepAsync(maker, payer, receivable: 226m, unapplied: 100m, credit: 0m);

        // The payer asks for the 100.00 back: a refund of the unapplied cash.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        using var asked = await clerk.PostJsonAsync(Refunds, new CreateRefundRequest { PaymentId = payment.Id, Amount = 100m, Method = PaymentMethod.BankTransfer, Reference = "UTR2026101700042", Reason = "Paid on the wrong bill; payer wants it back" });
        asked.StatusCode.ShouldBe(HttpStatusCode.Created, await asked.Content.ReadAsStringAsync());
        var refund = await ApprovedAsync(clerk, supervisor, Refund, await asked.ReadAsAsync<RefundDto>());
        refund.Number.ShouldBe("REF/26-27/000001");
        await StepAsync(maker, payer, receivable: 226m, unapplied: 0m, credit: 0m);

        // The end: the admission partly paid, the dressing bill owed in full, every number in its series once.
        var admitted = await BillAsync(clerk, admission.Id);
        (admitted.Status, admitted.BalanceDue).ShouldBe((BillStatus.PartiallyPaid, 100.00m));
        (await BillAsync(clerk, dressing.Id)).BalanceDue.ShouldBe(126.00m);
        foreach (var (table, documentType, documents) in Series)
        {
            var tenant = maker.TenantId!.Value.Value;
            (await CountAsync($"SELECT COUNT(*) AS [Value] FROM dbo.{table} WHERE [TenantId] = '{tenant}' AND [SequenceNumber] IS NOT NULL")).ShouldBe(documents, table);
            (await CountAsync($"SELECT ISNULL(MAX([SequenceNumber]), 0) AS [Value] FROM dbo.{table} WHERE [TenantId] = '{tenant}'")).ShouldBe(documents, $"{table}: a gap or a duplicate");
            (await CountAsync($"SELECT [NextNumber] AS [Value] FROM dbo.NumberSeries WHERE [TenantId] = '{tenant}' AND [DocumentType] = '{documentType}' AND [FiscalYear] = '2026-27'")).ShouldBe(documents + 1, documentType);
        }

        (await CountAsync(
            """
            SELECT COUNT(*) AS [Value] FROM (
                SELECT [TenantId] FROM dbo.Bills       WHERE [SequenceNumber] IS NOT NULL GROUP BY [TenantId], [FiscalYear] HAVING COUNT(*) <> MAX([SequenceNumber])
                UNION ALL
                SELECT [TenantId] FROM dbo.CreditNotes WHERE [SequenceNumber] IS NOT NULL GROUP BY [TenantId], [FiscalYear] HAVING COUNT(*) <> MAX([SequenceNumber])
                UNION ALL
                SELECT [TenantId] FROM dbo.Payments                                       GROUP BY [TenantId], [FiscalYear] HAVING COUNT(*) <> MAX([SequenceNumber])
                UNION ALL
                SELECT [TenantId] FROM dbo.Adjustments WHERE [SequenceNumber] IS NOT NULL GROUP BY [TenantId], [FiscalYear] HAVING COUNT(*) <> MAX([SequenceNumber])
                UNION ALL
                SELECT [TenantId] FROM dbo.Refunds     WHERE [SequenceNumber] IS NOT NULL GROUP BY [TenantId], [FiscalYear] HAVING COUNT(*) <> MAX([SequenceNumber])
            ) AS [Gaps]
            """)).ShouldBe(0, "the gapless verification of docs/database.md section 4.3 finds nothing");
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [Status] = 'Pending'")).ShouldBe(0);
    }

    private static Uri CreditNote(Guid id, string action) => new($"/api/v1/credit-notes/{id}/{action}", UriKind.Relative);

    private static Uri Adjustment(Guid id, string action) => new($"/api/v1/adjustments/{id}/{action}", UriKind.Relative);

    private static Uri Refund(Guid id, string action) => new($"/api/v1/refunds/{id}/{action}", UriKind.Relative);

    // Every step leaves the payer's balances where they should be, and nothing out of line with the ledger.
    private async Task StepAsync(FixedIdentity tenant, PayerDto payer, decimal receivable, decimal unapplied, decimal credit)
    {
        await using var context = ApiContext(tenant);
        var id = new PayerId(payer.Id);
        var balance = await context.Set<PayerBalance>().AsNoTracking().SingleAsync(candidate => candidate.PayerId == id);

        (balance.Receivable, balance.UnappliedCash, balance.CreditAvailable).ShouldBe((Money.Of(receivable), Money.Of(unapplied), Money.Of(credit)));
        (await new ReconciliationQuery(context, tenant).FindDriftAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    // A maker-checker document handed over by its maker and approved by the checker; the answer of the approval.
    private static async Task<T> ApprovedAsync<T>(HttpClient maker, HttpClient checker, Func<Guid, string, Uri> uri, T draft)
        where T : class
    {
        var (id, version) = IdAndVersion(draft);
        using var submitted = await maker.SendWithIfMatchAsync(HttpMethod.Post, uri(id, "post"), body: null, PayersApi.ETag(version));
        submitted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await submitted.Content.ReadAsStringAsync());
        var (_, waiting) = IdAndVersion(await submitted.ReadAsAsync<T>());

        using var approved = await checker.SendWithIfMatchAsync(HttpMethod.Post, uri(id, "approve"), body: null, PayersApi.ETag(waiting));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());

        return await approved.ReadAsAsync<T>();
    }

    private static (Guid Id, string Version) IdAndVersion<T>(T document) => document switch
    {
        CreditNoteDto creditNote => (creditNote.Id, creditNote.Version),
        AdjustmentDto adjustment => (adjustment.Id, adjustment.Version),
        RefundDto refund => (refund.Id, refund.Version),
        _ => throw new ArgumentException($"{typeof(T).Name} is not a maker-checker document of this scenario.", nameof(document)),
    };

    private static async Task<PaymentAllocationsDto> AllocationReversedAsync(HttpClient maker, HttpClient checker, Guid paymentId, Guid allocationId, string reason)
    {
        var current = await PaymentAsync(maker, paymentId);
        var uri = new Uri($"/api/v1/payments/{paymentId}/allocations/{allocationId}/reverse", UriKind.Relative);
        using var asked = await maker.SendWithIfMatchAsync(HttpMethod.Post, uri, new RequestReversalRequest { Reason = reason }, PayersApi.ETag(current.Version));
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());
        var waiting = await asked.ReadAsAsync<PaymentAllocationsDto>();

        using var approved = await checker.SendWithIfMatchAsync(
            HttpMethod.Post, new Uri($"/api/v1/payments/{paymentId}/allocations/{allocationId}/approve-reversal", UriKind.Relative), body: null, PayersApi.ETag(waiting.Payment.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());

        return await approved.ReadAsAsync<PaymentAllocationsDto>();
    }

    // A draft that credits the whole dressing line of the bill.
    private static async Task<CreditNoteDto> DraftedCreditNoteAsync(HttpClient client, BillDto bill)
    {
        var line = bill.Lines.Single(candidate => candidate.Description == "Dressing");
        using var response = await client.PostJsonAsync(
            new Uri("/api/v1/credit-notes", UriKind.Relative),
            new CreateCreditNoteRequest { BillId = bill.Id, CreditNoteDate = Today, Reason = "Dressing not done", Lines = [new CreditNoteLineRequest { BillLineId = line.Id, Quantity = line.Quantity }] });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<CreditNoteDto>();
    }

    private static async Task<PaymentDto> RecordedAsync(HttpClient client, PayerDto payer, decimal amount)
    {
        using var response = await client.PostJsonAsync(
            new Uri("/api/v1/payments", UriKind.Relative),
            new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.Cheque, Reference = "000123", Amount = amount });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PaymentDto>();
    }

    private static async Task<PaymentDto> PaymentAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/payments/{id}", UriKind.Relative));

        return await response.ReadAsAsync<PaymentDto>();
    }

    private static async Task<PaymentAllocationsDto> AllocatedAsync(HttpClient client, PaymentDto payment, params (Guid BillId, decimal Amount)[] lines)
    {
        using var response = await client.SendWithIfMatchAsync(
            HttpMethod.Post,
            new Uri($"/api/v1/payments/{payment.Id}/allocations", UriKind.Relative),
            new AllocatePaymentRequest { Allocations = [.. lines.Select(line => new AllocationRequest { BillId = line.BillId, Amount = line.Amount })] },
            PayersApi.ETag(payment.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PaymentAllocationsDto>();
    }

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

    private async Task<int> CountAsync(string sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQueryRaw<int>(sql).SingleAsync();
    }
}
