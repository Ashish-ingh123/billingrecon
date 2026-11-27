using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// US-43 for a bill (BR-701 to BR-703): a Viewer reads the audit trail of one bill through its whole life (drafted,
/// corrected, sent to a checker and returned, sent again and approved, partly paid, the payment taken back, settled) and
/// finds every state change, in order, each with who made it, the correlation id of the request that made it, and the
/// bill before and after. The trail has no hole: each record's "before" is the previous record's "after".
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class BillAuditTrailApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly DateOnly Today = new(2026, 10, 17);

    [Fact]
    public async Task US43_a_viewer_reads_every_state_change_of_a_bill_with_before_after_and_the_correlation_id_of_its_request()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var maker = await TenantWithThresholdAsync(100m);
        var checker = new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid()));
        var auditor = new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid()));
        using var clerk = factory.CreateClientFor(maker, "BillingClerk", "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        using var viewer = factory.CreateClientFor(auditor, "Viewer");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var expected = new List<(string Action, Guid CorrelationId, Guid UserId, string Status)>();
        var makerId = maker.UserId!.Value.Value;
        var checkerId = checker.UserId!.Value.Value;

        // Drafted, its lines replaced, its header corrected.
        using var drafted = await clerk.PostJsonAsync(BillingApi.Bills, new CreateBillRequest { PatientId = patient.Id, PayerId = payer.Id, BillDate = Today, DueDate = Today.AddDays(15), Lines = [BillingApi.FreeTextLine("Consultation", 1m, 200m, 0m)] });
        var bill = await ExpectBill(drafted, HttpStatusCode.Created, "Drafted", makerId, "Draft");
        using var lines = await clerk.SendWithIfMatchAsync(HttpMethod.Put, BillingApi.Bill(bill.Id, "lines"), new ReplaceBillLinesRequest { Lines = [BillingApi.FreeTextLine("Specialist consultation", 1m, 500m, 0m)] }, PayersApi.ETag(bill.Version));
        bill = await ExpectBill(lines, HttpStatusCode.OK, "LinesReplaced", makerId, "Draft");
        using var header = await clerk.SendWithIfMatchAsync(HttpMethod.Patch, BillingApi.Bill(bill.Id), new UpdateBillHeaderRequest { BillDate = Today, DueDate = Today.AddDays(30), EncounterReference = "OPD-2026-1017", Notes = "Referred by the ward" }, PayersApi.ETag(bill.Version));
        bill = await ExpectBill(header, HttpStatusCode.OK, "HeaderUpdated", makerId, "Draft");

        // At the threshold it waits for a checker, who returns it; sent again, it is approved, and that is the posting.
        using var submitted = await clerk.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "post"), body: null, PayersApi.ETag(bill.Version));
        bill = await ExpectBill(submitted, HttpStatusCode.Accepted, "SubmittedForApproval", makerId, "PendingApproval");
        using var rejected = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "reject"), new RejectBillRequest { Comment = "Attach the referral" }, PayersApi.ETag(bill.Version));
        bill = await ExpectBill(rejected, HttpStatusCode.OK, "ReturnedToDraft", checkerId, "Draft");
        using var resubmitted = await clerk.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "post"), body: null, PayersApi.ETag(bill.Version));
        bill = await ExpectBill(resubmitted, HttpStatusCode.Accepted, "SubmittedForApproval", makerId, "PendingApproval");
        using var approved = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "approve"), body: null, PayersApi.ETag(bill.Version));
        bill = await ExpectBill(approved, HttpStatusCode.OK, "Posted", checkerId, "Posted");

        // Partly paid; the allocation taken back on approval; then settled.
        var payment = await RecordedAsync(clerk, payer, 300m);
        using var allocated = await AllocateAsync(clerk, payment, bill.Id, 300m);
        var allocation = (await Expect<PaymentAllocationsDto>(allocated, HttpStatusCode.OK, "CreditApplied", makerId, "PartiallyPaid")).Allocations.ShouldHaveSingleItem();
        var waiting = await ReversalRequestedAsync(clerk, payment.Id, allocation.Id);
        using var reversed = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, new Uri($"/api/v1/payments/{payment.Id}/allocations/{allocation.Id}/approve-reversal", UriKind.Relative), body: null, PayersApi.ETag(waiting.Payment.Version));
        await Expect<PaymentAllocationsDto>(reversed, HttpStatusCode.OK, "CreditReversed", checkerId, "Posted");
        var settlement = await RecordedAsync(clerk, payer, 500m);
        using var settled = await AllocateAsync(clerk, settlement, bill.Id, 500m);
        await Expect<PaymentAllocationsDto>(settled, HttpStatusCode.OK, "CreditApplied", makerId, "Settled");

        // The Viewer's read of the trail.
        using var response = await viewer.GetAsync(new Uri($"/api/v1/audit-records?entityType=Bill&entityId={bill.Id}&pageSize=100", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var trail = (await response.ReadAsAsync<PageOf<AuditRecordDto>>()).Items;

        trail.Select(record => (record.Action, record.CorrelationId, record.UserId!.Value, record.After.GetProperty("Status").GetString()!))
            .ShouldBe(expected);
        trail.ShouldAllBe(record => record.EntityType == "Bill" && record.EntityId == bill.Id);
        trail[0].Before.ShouldBeNull();
        for (var index = 1; index < trail.Count; index++)
        {
            trail[index].Before.ShouldNotBeNull($"record {index} ({trail[index].Action}) has no before");
            // Compared by value: a new row's "after" holds the decimals as the application wrote them (1, 200), a stored
            // row's "before" as the column holds them (1.000, 200.0000).
            System.Text.Json.JsonElement.DeepEquals(trail[index].Before!.Value, trail[index - 1].After).ShouldBeTrue(
                $"a change between record {index - 1} ({trail[index - 1].Action}) and record {index} ({trail[index].Action}) has no record:\n{trail[index - 1].After.GetRawText()}\n{trail[index].Before!.Value.GetRawText()}");
        }

        trail.Take(6).ShouldAllBe(record => record.DocumentNumber == null);
        trail.Skip(6).ShouldAllBe(record => record.DocumentNumber == "BIL/26-27/000001");
        trail[1].After.GetProperty("Lines").EnumerateArray().ShouldHaveSingleItem().GetProperty("Description").GetString().ShouldBe("Specialist consultation");
        trail[2].After.GetProperty("Notes").GetString().ShouldBe("Referred by the ward");
        trail[^1].After.GetProperty("BalanceDue").GetDecimal().ShouldBe(0m);

        // Each step's own answer named the correlation id the trail records for it.
        async Task<BillDto> ExpectBill(HttpResponseMessage answer, HttpStatusCode status, string action, Guid userId, string billStatus)
            => await Expect<BillDto>(answer, status, action, userId, billStatus);

        async Task<T> Expect<T>(HttpResponseMessage answer, HttpStatusCode status, string action, Guid userId, string billStatus)
            where T : class
        {
            answer.StatusCode.ShouldBe(status, await answer.Content.ReadAsStringAsync());
            expected.Add((action, answer.GetCorrelationId(), userId, billStatus));
            factory.Clock.Advance(TimeSpan.FromMinutes(1));

            return await answer.ReadAsAsync<T>();
        }
    }

    // Bills at or above the threshold wait for a checker (BR-207); the tenant's row is set as system work.
    private async Task<FixedIdentity> TenantWithThresholdAsync(decimal threshold)
    {
        var maker = await NewApiTenantAsync();
        await using var system = ApiContext(SystemIdentity.Instance);
        var row = await system.Set<Tenant>().SingleAsync(candidate => candidate.Id == maker.TenantId!.Value);
        system.Entry(row).Property(candidate => candidate.PostingApprovalThreshold).CurrentValue = Money.Of(threshold);
        await system.SaveChangesAsync();

        return maker;
    }

    private static async Task<PaymentDto> RecordedAsync(HttpClient client, PayerDto payer, decimal amount)
    {
        await client.EnsureShiftAsync();
        using var response = await client.PostJsonAsync(
            new Uri("/api/v1/payments", UriKind.Relative),
            new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.Cash, Amount = amount });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PaymentDto>();
    }

    private static Task<HttpResponseMessage> AllocateAsync(HttpClient client, PaymentDto payment, Guid billId, decimal amount)
        => client.SendWithIfMatchAsync(
            HttpMethod.Post,
            new Uri($"/api/v1/payments/{payment.Id}/allocations", UriKind.Relative),
            new AllocatePaymentRequest { Allocations = [new AllocationRequest { BillId = billId, Amount = amount }] },
            PayersApi.ETag(payment.Version));

    private static async Task<PaymentAllocationsDto> ReversalRequestedAsync(HttpClient maker, Guid paymentId, Guid allocationId)
    {
        using var read = await maker.GetAsync(new Uri($"/api/v1/payments/{paymentId}", UriKind.Relative));
        var payment = await read.ReadAsAsync<PaymentDto>();
        using var asked = await maker.SendWithIfMatchAsync(
            HttpMethod.Post,
            new Uri($"/api/v1/payments/{paymentId}/allocations/{allocationId}/reverse", UriKind.Relative),
            new RequestReversalRequest { Reason = "Applied before the referral was attached" },
            PayersApi.ETag(payment.Version));

        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());

        return await asked.ReadAsAsync<PaymentAllocationsDto>();
    }
}
