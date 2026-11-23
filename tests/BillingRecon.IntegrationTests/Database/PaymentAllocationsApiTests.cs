using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Payments;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.Infrastructure.Receivables;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Allocating a payment to bills end to end, on the pipeline that ships and a real SQL Server (US-31, US-25; BR-007,
/// BR-208, BR-303, BR-304, BR-803, BR-804): the allocation, the payment's unapplied amount, the bill's balance due
/// and status, the paired ledger entry, the payer's two cached balances and the audit records are written by one
/// transaction or not at all. Every scenario ends with no drift between the caches and the ledger.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PaymentAllocationsApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Uri Payments = new("/api/v1/payments", UriKind.Relative);

    // 17 October 2026 in India: fiscal year 2026-27.
    private static readonly DateOnly Today = new(2026, 10, 17);

    // 2 x 60.00 at 5%: 126.00.
    private static readonly BillLineRequest Dressing = BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m);

    // 1 x 500.00 at 18%: 590.00.
    private static readonly BillLineRequest Consultation = BillingApi.FreeTextLine("OPD consultation", 1m, 500m, 18m);

    [Fact]
    public async Task US31_a_part_allocation_then_the_rest_moves_the_bill_from_posted_to_partially_paid_to_settled_with_zero_drift_after_each()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var large = await PostedBillAsync(client, patient, payer, Consultation, Dressing);
        var small = await PostedBillAsync(client, patient, payer, Dressing);
        var payment = await RecordedAsync(client, payer, 1000m);
        large.Total.ShouldBe(716.00m);
        await ShouldHaveNoDriftAsync(cashier);

        // The next day: an allocation is dated the day it is made, not the day of the payment.
        factory.Clock.Advance(TimeSpan.FromDays(1));
        using var response = await AllocateAsync(client, payment, (large.Id, 500m));

        // The answer: the payment with less unapplied, a new version, and its one allocation.
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var first = await response.ReadAsAsync<PaymentAllocationsDto>();
        first.Payment.UnappliedAmount.ShouldBe(500.00m);
        first.Payment.Amount.ShouldBe(1000.00m);
        first.Payment.Number.ShouldBe(payment.Number);
        first.Payment.Version.ShouldNotBe(payment.Version);
        response.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(first.Payment.Version));
        var allocation = first.Allocations.ShouldHaveSingleItem();
        allocation.BillId.ShouldBe(large.Id);
        allocation.Amount.ShouldBe(500.00m);
        allocation.Status.ShouldBe(PaymentAllocationStatus.Active);
        allocation.AllocatedAtUtc.ShouldBe(DatabaseApiFactory.InitialInstant.AddDays(1));
        allocation.AllocatedByUserId.ShouldBe(cashier.UserId!.Value.Value);

        // The bill: partially paid, with a new version (BR-208).
        var partlyPaid = await BillAsync(client, large.Id);
        partlyPaid.Status.ShouldBe(BillStatus.PartiallyPaid);
        partlyPaid.BalanceDue.ShouldBe(216.00m);
        partlyPaid.Total.ShouldBe(716.00m);
        partlyPaid.Number.ShouldBe(large.Number);
        partlyPaid.Version.ShouldNotBe(large.Version);

        // The ledger: Dr UnappliedCash(payment) / Cr Receivable(bill), dated the day of the allocation (BR-303).
        await using (var context = ApiContext(cashier))
        {
            var posting = await context.Set<LedgerPosting>().Include(candidate => candidate.Entries).SingleAsync(candidate => candidate.Kind == LedgerPostingKind.PaymentAllocated);
            posting.DocumentType.ShouldBe(LedgerDocumentType.PaymentAllocation);
            posting.DocumentId.ShouldBe(allocation.Id);
            posting.PayerId.ShouldBe(new PayerId(payer.Id));
            posting.BusinessDate.ShouldBe(Today.AddDays(1));
            posting.Description.ShouldBe($"Payment {payment.Number} allocated to bill {large.Number}");
            posting.CorrelationId.ShouldBe(response.GetCorrelationId());
            posting.Entries.OrderBy(entry => entry.LineNo).Select(entry => (entry.Account, entry.Debit, entry.Credit, entry.SubjectId)).ShouldBe(
            [
                (LedgerAccount.UnappliedCash, Money.Of(500m), Money.Zero, payment.Id),
                (LedgerAccount.Receivable, Money.Zero, Money.Of(500m), large.Id),
            ]);

            // The allocation keeps the posting it was made with.
            var stored = await context.Set<PaymentAllocation>().AsNoTracking().SingleAsync();
            stored.PostingId.ShouldBe(posting.Id);
            stored.PaymentId.ShouldBe(new PaymentId(payment.Id));
        }

        // The caches: both fall by the amount, so what the payer owes net is what it was (docs/database.md, 5.4).
        var balance = await BalanceAsync(cashier, payer);
        balance.Receivable.ShouldBe(Money.Of(342m), "716 + 126 - 500");
        balance.UnappliedCash.ShouldBe(Money.Of(500m));
        balance.NetOutstanding.ShouldBe(Money.Of(-158m), "842 owed less 1000 received, before and after");

        // The audit trail: one record on each of the two documents, in the request of the allocation (BR-701).
        var paymentTrail = await TrailAsync(client, "Payment", payment.Id);
        paymentTrail.Select(record => record.Action).ShouldBe(["Recorded", "Allocated"]);
        paymentTrail[1].DocumentNumber.ShouldBe(payment.Number);
        paymentTrail[1].CorrelationId.ShouldBe(response.GetCorrelationId());
        paymentTrail[1].Before.ShouldNotBeNull().GetProperty("UnappliedAmount").GetDecimal().ShouldBe(1000.00m);
        paymentTrail[1].Before.ShouldNotBeNull().GetProperty("Allocations").GetArrayLength().ShouldBe(0);
        paymentTrail[1].After.GetProperty("UnappliedAmount").GetDecimal().ShouldBe(500.00m);
        paymentTrail[1].After.GetProperty("Allocations").GetArrayLength().ShouldBe(1);
        var billTrail = await TrailAsync(client, "Bill", large.Id);
        billTrail.Select(record => record.Action).ShouldBe(["Drafted", "Posted", "CreditApplied"]);
        billTrail[2].DocumentNumber.ShouldBe(large.Number);
        billTrail[2].CorrelationId.ShouldBe(response.GetCorrelationId());
        billTrail[2].Before.ShouldNotBeNull().GetProperty("Status").GetString().ShouldBe("Posted");
        billTrail[2].After.GetProperty("Status").GetString().ShouldBe("PartiallyPaid");
        billTrail[2].After.GetProperty("BalanceDue").GetDecimal().ShouldBe(216.00m);
        await ShouldHaveNoDriftAsync(cashier);

        // A minute later, the rest of the first bill and all of the second, in one request: both are settled (BR-304).
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await AllocatedAsync(client, first.Payment, (small.Id, 126m), (large.Id, 216m));

        second.Payment.UnappliedAmount.ShouldBe(158.00m);
        second.Allocations.Count.ShouldBe(3);
        second.Allocations[0].ShouldBe(allocation, "the oldest allocation first, unchanged");
        second.Allocations.Skip(1).Select(made => (made.BillId, made.Amount)).ShouldBe([(small.Id, 126.00m), (large.Id, 216.00m)], ignoreOrder: true);
        var settled = await BillAsync(client, large.Id);
        settled.Status.ShouldBe(BillStatus.Settled);
        settled.BalanceDue.ShouldBe(0.00m);
        (await BillAsync(client, small.Id)).Status.ShouldBe(BillStatus.Settled);
        balance = await BalanceAsync(cashier, payer);
        balance.Receivable.ShouldBe(Money.Zero);
        balance.UnappliedCash.ShouldBe(Money.Of(158m));
        balance.NetOutstanding.ShouldBe(Money.Of(-158m));
        (await TrailAsync(client, "Payment", payment.Id)).Select(record => record.Action).ShouldBe(["Recorded", "Allocated", "Allocated", "Allocated"]);
        (await TrailAsync(client, "Bill", large.Id)).Select(record => record.Action).ShouldBe(["Drafted", "Posted", "CreditApplied", "CreditApplied"]);
        await ShouldHaveNoDriftAsync(cashier);

        // Reading the allocations gives what the last request answered.
        using var read = await client.GetAsync(Allocations(payment.Id));
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listed = await read.ReadAsAsync<PaymentAllocationsDto>();
        listed.Payment.ShouldBe(second.Payment);
        listed.Allocations.ShouldBe(second.Allocations);
        read.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(second.Payment.Version));
    }

    [Fact]
    public async Task BR303_an_allocation_that_is_refused_changes_neither_document_and_a_request_is_all_or_nothing()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var (otherPatient, otherPayer) = await client.NewBillPartiesAsync();
        var bill = await PostedBillAsync(client, patient, payer, Dressing);
        var theirs = await PostedBillAsync(client, otherPatient, otherPayer, Dressing);
        var draft = await client.CreateBillAsync(NewBill(patient, payer, Dressing));
        var payment = await RecordedAsync(client, payer, 100m);
        var large = await RecordedAsync(client, payer, 500m);

        // More than the payment has; more than the bill is owed; another payer's bill; a draft; a bill nobody has.
        using var overUnapplied = await AllocateAsync(client, payment, (bill.Id, 100.01m));
        using var overUnappliedInSum = await AllocateAsync(client, payment, (bill.Id, 60m), (theirs.Id, 40.01m));
        using var overBalance = await AllocateAsync(client, large, (bill.Id, 126.01m));
        using var crossPayer = await AllocateAsync(client, large, (theirs.Id, 50m));
        using var notPosted = await AllocateAsync(client, large, (draft.Id, 50m));
        using var unknownBill = await AllocateAsync(client, large, (Guid.NewGuid(), 50m));

        // One good line and one bad line: neither is applied.
        using var mixed = await AllocateAsync(client, large, (bill.Id, 50m), (theirs.Id, 50m));

        await overUnapplied.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.AllocationExceedsUnappliedCode);
        await overUnappliedInSum.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.AllocationExceedsUnappliedCode);
        await overBalance.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.AllocationExceedsBalanceDueCode);
        await crossPayer.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.AllocationPayerMismatchCode);
        await notPosted.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.BillNotPostedCode);
        await unknownBill.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        await mixed.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.AllocationPayerMismatchCode);
        await ShouldBeUntouchedAsync(client, cashier, payer, [payment, large], [bill, theirs]);

        // The whole balance, exactly, is accepted.
        (await AllocatedAsync(client, large, (bill.Id, 126m))).Payment.UnappliedAmount.ShouldBe(374.00m);
        await ShouldHaveNoDriftAsync(cashier);
    }

    [Fact]
    public async Task An_allocation_needs_a_well_formed_body_an_idempotency_key_and_the_version_the_client_read()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var bill = await PostedBillAsync(client, patient, payer, Dressing);
        var payment = await RecordedAsync(client, payer, 100m);
        var etag = PayersApi.ETag(payment.Version);

        using var empty = await AllocateAsync(client, payment);
        using var twice = await AllocateAsync(client, payment, (bill.Id, 10m), (bill.Id, 10m));
        using var zero = await AllocateAsync(client, payment, (bill.Id, 0m));
        using var threeDecimals = await AllocateAsync(client, payment, (bill.Id, 10.005m));
        using var noBill = await client.SendWithIfMatchAsync(HttpMethod.Post, Allocations(payment.Id), new AllocatePaymentRequest { Allocations = [new AllocationRequest { Amount = 10m }] }, etag);
        using var withoutKeyRequest = new HttpRequestMessage(HttpMethod.Post, Allocations(payment.Id)) { Content = PayersApi.RawJson($$"""{"allocations":[{"billId":"{{bill.Id}}","amount":10}]}""") };
        withoutKeyRequest.Headers.TryAddWithoutValidation("If-Match", etag);
        using var withoutKey = await client.SendAsync(withoutKeyRequest);
        using var withoutIfMatch = await client.SendWithIfMatchAsync(HttpMethod.Post, Allocations(payment.Id), Body((bill.Id, 10m)), ifMatch: null);
        using var stale = await client.SendWithIfMatchAsync(HttpMethod.Post, Allocations(payment.Id), Body((bill.Id, 10m)), PayersApi.ETag("AAAAAAAAAAE="));
        using var unknown = await client.SendWithIfMatchAsync(HttpMethod.Post, Allocations(Guid.NewGuid()), Body((bill.Id, 10m)), etag);
        using var unknownRead = await client.GetAsync(Allocations(Guid.NewGuid()));

        (await empty.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode)).GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(["allocations"]);
        (await twice.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode)).GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(["allocations"]);
        (await zero.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode)).GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(["allocations[0].amount"]);
        (await threeDecimals.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode)).GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(["allocations[0].amount"]);
        (await noBill.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode)).GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(["allocations[0].billId"]);
        await withoutKey.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.IdempotencyKeyMissingCode);
        await withoutIfMatch.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
        await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.PaymentNotFoundCode);
        await unknownRead.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.PaymentNotFoundCode);
        await ShouldBeUntouchedAsync(client, cashier, payer, [payment], [bill]);
    }

    [Fact]
    public async Task BR804_two_cashiers_allocating_the_last_rupee_of_one_bill_at_once_give_one_success_and_one_conflict()
    {
        // The first request is held inside its transaction, after its ledger posting has locked the balances of the
        // payer. The second has read the bill by then, as it still was, and waits for that lock.
        var gate = new Gate();
        using var factory = new DatabaseApiFactory(SqlServer, configure: services =>
        {
            services.AddSingleton(gate);
            services.AddScoped<ReceivablesLedger>();
            services.AddScoped<IReceivablesLedger>(provider => new GatedLedger(provider.GetRequiredService<ReceivablesLedger>(), gate));
        });
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var bill = await PostedBillAsync(client, patient, payer, Dressing);
        var one = await RecordedAsync(client, payer, 126m);
        var two = await RecordedAsync(client, payer, 126m);
        gate.Armed = true;

        var first = AllocateAsync(client, one, (bill.Id, 126m));
        await gate.FirstHoldsTheLock.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var second = AllocateAsync(client, two, (bill.Id, 126m));
        await gate.SecondHasReadTheBill.Task.WaitAsync(TimeSpan.FromSeconds(30));
        gate.Release.SetResult();
        using var winner = await first;
        using var loser = await second;

        winner.StatusCode.ShouldBe(HttpStatusCode.OK, await winner.Content.ReadAsStringAsync());
        await loser.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);

        // One allocation exists; the loser's payment is whole, and so is its version.
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.PaymentAllocations")).ShouldBe(1);
        (await BillAsync(client, bill.Id)).Status.ShouldBe(BillStatus.Settled);
        var untouched = await PaymentAsync(client, two.Id);
        untouched.UnappliedAmount.ShouldBe(126.00m);
        untouched.Version.ShouldBe(two.Version);
        var balance = await BalanceAsync(cashier, payer);
        balance.Receivable.ShouldBe(Money.Zero);
        balance.UnappliedCash.ShouldBe(Money.Of(126m));
        await ShouldHaveNoDriftAsync(cashier);

        // The loser reads again and tries again: now the bill itself says it is owed nothing.
        using var again = await AllocateAsync(client, untouched, (bill.Id, 126m));
        await again.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.AllocationExceedsBalanceDueCode);
        await ShouldHaveNoDriftAsync(cashier);
    }

    [Fact]
    public async Task BR804_without_a_gate_the_last_rupee_goes_to_exactly_one_of_two_cashiers_and_the_other_is_refused()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var bill = await PostedBillAsync(client, patient, payer, Dressing);
        var one = await RecordedAsync(client, payer, 126m);
        var two = await RecordedAsync(client, payer, 126m);

        var responses = await Task.WhenAll(
            Task.Run(() => AllocateAsync(client, one, (bill.Id, 126m))),
            Task.Run(() => AllocateAsync(client, two, (bill.Id, 126m))));

        try
        {
            // Which refusal the loser gets depends on when it read the bill: before the winner committed (409, from
            // the save or from the ledger under its lock) or after (422). Both are correct; a second success never is.
            responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
            var loser = responses.Single(response => response.StatusCode != HttpStatusCode.OK);
            if (loser.StatusCode == HttpStatusCode.Conflict)
            {
                await loser.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
            }
            else
            {
                await loser.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.AllocationExceedsBalanceDueCode);
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.PaymentAllocations")).ShouldBe(1);
        (await BillAsync(client, bill.Id)).BalanceDue.ShouldBe(0.00m);
        (await BalanceAsync(cashier, payer)).UnappliedCash.ShouldBe(Money.Of(126m));
        await ShouldHaveNoDriftAsync(cashier);
    }

    [Fact]
    public async Task Ten_payments_allocated_at_once_across_overlapping_bills_of_one_payer_neither_deadlock_nor_drift()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var bills = new List<BillDto>();
        var payments = new List<PaymentDto>();
        for (var index = 0; index < 10; index++)
        {
            bills.Add(await PostedBillAsync(client, patient, payer, Consultation));
            payments.Add(await RecordedAsync(client, payer, 100m));
        }

        // Payment i pays 30 each to bill i and bill i + 1, the bills named in opposite orders by neighbours.
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(index => Task.Run(() =>
        {
            var own = (bills[index].Id, 30m);
            var next = (bills[(index + 1) % 10].Id, 30m);

            return index % 2 == 0 ? AllocateAsync(client, payments[index], own, next) : AllocateAsync(client, payments[index], next, own);
        })));

        int succeeded;
        try
        {
            // A request that read a bill its neighbour changed first is a conflict; nothing is ever a fault.
            responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Conflict);
            succeeded = responses.Count(response => response.StatusCode == HttpStatusCode.OK);
            succeeded.ShouldBeGreaterThanOrEqualTo(1);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.PaymentAllocations")).ShouldBe(succeeded * 2);
        var balance = await BalanceAsync(cashier, payer);
        balance.UnappliedCash.ShouldBe(Money.Of(1000m - (succeeded * 60m)));
        balance.Receivable.ShouldBe(Money.Of(5900m - (succeeded * 60m)));
        await ShouldHaveNoDriftAsync(cashier);
    }

    [Theory]
    [InlineData("the ledger, after the posting is entered")]
    [InlineData("the audit trail, after the record is written")]
    [InlineData("the save itself")]
    public async Task US25_a_failure_at_any_step_leaves_no_allocation_no_ledger_and_no_audit_rows_and_both_documents_as_they_were(string step)
    {
        var cashier = await NewApiTenantAsync();
        PayerDto payer;
        BillDto bill;
        PaymentDto payment;
        var key = Guid.NewGuid();

        using (var failing = new DatabaseApiFactory(SqlServer, configure: services => FailAt(step, services)))
        {
            using var client = failing.CreateClientFor(cashier, "Cashier", "BillingClerk");
            (var patient, payer) = await client.NewBillPartiesAsync();
            bill = await PostedBillAsync(client, patient, payer, Dressing);
            payment = await RecordedAsync(client, payer, 100m);

            using var failed = await AllocateAsync(client, payment, key, (bill.Id, 100m));

            await failed.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
            await ShouldBeUntouchedAsync(client, cashier, payer, [payment], [bill]);
        }

        // The same request again, with the same key, on an API that works.
        using var working = new DatabaseApiFactory(SqlServer);
        using var retrying = working.CreateClientFor(cashier, "Cashier", "BillingClerk");
        using var retried = await AllocateAsync(retrying, payment, key, (bill.Id, 100m));

        retried.StatusCode.ShouldBe(HttpStatusCode.OK, await retried.Content.ReadAsStringAsync());
        retried.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        (await BillAsync(retrying, bill.Id)).BalanceDue.ShouldBe(26.00m);
        (await BalanceAsync(cashier, payer)).UnappliedCash.ShouldBe(Money.Zero);
        await ShouldHaveNoDriftAsync(cashier);
    }

    [Fact]
    public async Task BR803_a_replayed_allocation_returns_the_same_response_and_allocates_once()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var bill = await PostedBillAsync(client, patient, payer, Dressing);
        var payment = await RecordedAsync(client, payer, 100m);
        var key = Guid.NewGuid();

        using var first = await AllocateAsync(client, payment, key, (bill.Id, 40m));
        using var replay = await AllocateAsync(client, payment, key, (bill.Id, 40m));

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.Headers.Contains("Idempotent-Replayed").ShouldBeTrue();
        (await replay.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        replay.Headers.ETag.ShouldBe(first.Headers.ETag);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.PaymentAllocations")).ShouldBe(1);
        (await BillAsync(client, bill.Id)).BalanceDue.ShouldBe(86.00m);

        // The same lines under a new key with the version the client first read: stale, and nothing is allocated twice.
        using var stale = await AllocateAsync(client, payment, (bill.Id, 40m));
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
        (await PaymentAsync(client, payment.Id)).UnappliedAmount.ShouldBe(60.00m);
        await ShouldHaveNoDriftAsync(cashier);
    }

    [Fact]
    public async Task A_payment_of_a_payer_who_was_deactivated_since_can_still_be_allocated()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var bill = await PostedBillAsync(client, patient, payer, Dressing);
        var payment = await RecordedAsync(client, payer, 126m);
        using var deactivation = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Payer(payer.Id, "deactivate"), body: null, PayersApi.ETag(payer.Version));
        deactivation.StatusCode.ShouldBe(HttpStatusCode.OK);

        // BR-309 stops new money for an inactive payer; an allocation moves none (ADR 0024).
        (await AllocatedAsync(client, payment, (bill.Id, 126m))).Payment.UnappliedAmount.ShouldBe(0.00m);
        (await BillAsync(client, bill.Id)).Status.ShouldBe(BillStatus.Settled);
        await ShouldHaveNoDriftAsync(cashier);
    }

    [Fact]
    public async Task BR002_a_payment_and_a_bill_of_another_tenant_can_neither_be_allocated_nor_read()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        var other = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        using var stranger = factory.CreateClientFor(other, "Cashier", "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var (theirPatient, theirPayer) = await stranger.NewBillPartiesAsync();
        var bill = await PostedBillAsync(client, patient, payer, Dressing);
        var payment = await RecordedAsync(client, payer, 100m);
        var theirBill = await PostedBillAsync(stranger, theirPatient, theirPayer, Dressing);
        var theirPayment = await RecordedAsync(stranger, theirPayer, 100m);

        using var forgedPayment = await AllocateAsync(stranger, payment, (theirBill.Id, 10m));
        using var forgedBill = await AllocateAsync(stranger, theirPayment, (bill.Id, 10m));
        using var read = await stranger.GetAsync(Allocations(payment.Id));

        await forgedPayment.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.PaymentNotFoundCode);
        await forgedBill.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        await read.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.PaymentNotFoundCode);
        await ShouldBeUntouchedAsync(client, cashier, payer, [payment], [bill]);

        (await AllocatedAsync(client, payment, (bill.Id, 10m))).Allocations.ShouldHaveSingleItem();
        await using var theirContext = ApiContext(other);
        (await theirContext.Set<PaymentAllocation>().CountAsync()).ShouldBe(0, "the allocation is the other tenant's");
        await ShouldHaveNoDriftAsync(cashier);
        await ShouldHaveNoDriftAsync(other);
    }

    [Fact]
    public async Task BR404_an_allocation_that_no_longer_matches_its_payment_its_posting_or_its_payer_is_reported()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var (otherPatient, otherPayer) = await client.NewBillPartiesAsync();
        var bill = await PostedBillAsync(client, patient, payer, Dressing);
        var theirs = await PostedBillAsync(client, otherPatient, otherPayer, Dressing);
        var payment = await RecordedAsync(client, payer, 100m);
        var allocation = (await AllocatedAsync(client, payment, (bill.Id, 40m))).Allocations.ShouldHaveSingleItem();
        await ShouldHaveNoDriftAsync(cashier);

        // The drift is seeded with SQL: nothing in the application can produce it.
        await ExecuteAsync($"UPDATE dbo.PaymentAllocations SET Amount = 39 WHERE Id = '{allocation.Id}'");

        (await DriftAsync(cashier)).ShouldHaveSingleItem().ShouldBe(new DriftRow(DriftIdentity.PaymentAllocations, payment.Id, payment.Number, 60.00m, 61.00m));

        await ExecuteAsync($"UPDATE dbo.PaymentAllocations SET Amount = 40, BillId = '{theirs.Id}' WHERE Id = '{allocation.Id}'");

        (await DriftAsync(cashier)).ShouldHaveSingleItem().ShouldBe(new DriftRow(DriftIdentity.AllocationPayerMismatch, allocation.Id, null, 40.00m, 0m));

        await ExecuteAsync($"UPDATE dbo.PaymentAllocations SET BillId = '{bill.Id}' WHERE Id = '{allocation.Id}'");
        await ExecuteAsync("UPDATE dbo.LedgerPostings SET DocumentId = NEWID() WHERE Kind = 'PaymentAllocated'");

        (await DriftAsync(cashier)).Select(row => row.Identity).ShouldBe([DriftIdentity.AllocationWithoutPosting, DriftIdentity.PostingWithoutAllocation], ignoreOrder: true);
    }

    // The registrations that make one step of an allocation fail after it has done its work inside the transaction.
    private static void FailAt(string step, IServiceCollection services)
    {
        switch (step)
        {
            case "the ledger, after the posting is entered":
                services.AddScoped<ReceivablesLedger>();
                services.AddScoped<IReceivablesLedger>(provider => new AllocationThenFault(provider.GetRequiredService<ReceivablesLedger>()));
                break;
            case "the audit trail, after the record is written":
                // Registered after the audit subscriber, so it runs when the audit records are already in the unit of work.
                services.AddScoped<IDomainEventSubscriber, FaultOnPaymentAllocated>();
                break;
            case "the save itself":
                services.AddScoped<IDomainEventSubscriber, RowTheDatabaseRefuses>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step), step, "No such step.");
        }
    }

    private static Uri Allocations(Guid paymentId) => new($"/api/v1/payments/{paymentId}/allocations", UriKind.Relative);

    private static AllocatePaymentRequest Body(params (Guid BillId, decimal Amount)[] lines)
        => new() { Allocations = [.. lines.Select(line => new AllocationRequest { BillId = line.BillId, Amount = line.Amount })] };

    private static Task<HttpResponseMessage> AllocateAsync(HttpClient client, PaymentDto payment, params (Guid BillId, decimal Amount)[] lines)
        => client.SendWithIfMatchAsync(HttpMethod.Post, Allocations(payment.Id), Body(lines), PayersApi.ETag(payment.Version));

    private static Task<HttpResponseMessage> AllocateAsync(HttpClient client, PaymentDto payment, Guid idempotencyKey, params (Guid BillId, decimal Amount)[] lines)
        => client.SendWithIfMatchAsync(HttpMethod.Post, Allocations(payment.Id), Body(lines), PayersApi.ETag(payment.Version), idempotencyKey);

    private static async Task<PaymentAllocationsDto> AllocatedAsync(HttpClient client, PaymentDto payment, params (Guid BillId, decimal Amount)[] lines)
    {
        using var response = await AllocateAsync(client, payment, lines);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PaymentAllocationsDto>();
    }

    private static async Task<PaymentDto> RecordedAsync(HttpClient client, PayerDto payer, decimal amount)
    {
        using var response = await client.PostJsonAsync(Payments, new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.UPI, Reference = "412345678901", Amount = amount });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PaymentDto>();
    }

    private static CreateBillRequest NewBill(PatientDto patient, PayerDto payer, params BillLineRequest[] lines)
        => new() { PatientId = patient.Id, PayerId = payer.Id, BillDate = Today, DueDate = Today.AddDays(15), Lines = lines };

    private static async Task<BillDto> PostedBillAsync(HttpClient client, PatientDto patient, PayerDto payer, params BillLineRequest[] lines)
    {
        var draft = await client.CreateBillAsync(NewBill(patient, payer, lines));
        using var response = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<BillDto>();
    }

    private static async Task<BillDto> BillAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(BillingApi.Bill(id));

        return await response.ReadAsAsync<BillDto>();
    }

    private static async Task<PaymentDto> PaymentAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/payments/{id}", UriKind.Relative));

        return await response.ReadAsAsync<PaymentDto>();
    }

    private static async Task<IReadOnlyList<AuditRecordDto>> TrailAsync(HttpClient client, string entityType, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/audit-records?entityType={entityType}&entityId={id}", UriKind.Relative));

        return (await response.ReadAsAsync<PageOf<AuditRecordDto>>()).Items;
    }

    // What US-25 promises after an allocation that did not happen: both documents are as they were read, with their
    // versions, and nothing of an allocation exists.
    private async Task ShouldBeUntouchedAsync(HttpClient client, FixedIdentity cashier, PayerDto payer, PaymentDto[] payments, BillDto[] bills)
    {
        foreach (var payment in payments)
        {
            (await PaymentAsync(client, payment.Id)).ShouldBe(payment);
        }

        foreach (var bill in bills)
        {
            var read = await BillAsync(client, bill.Id);
            read.Status.ShouldBe(BillStatus.Posted);
            read.BalanceDue.ShouldBe(bill.Total);
            read.Version.ShouldBe(bill.Version);
        }

        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.PaymentAllocations")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'PaymentAllocated'")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.AuditRecords WHERE [Action] IN ('Allocated', 'CreditApplied')")).ShouldBe(0);
        (await BalanceAsync(cashier, payer)).UnappliedCash.ShouldBe(Money.Of(payments.Where(payment => payment.PayerId == payer.Id).Sum(payment => payment.Amount)));
        await ShouldHaveNoDriftAsync(cashier);
    }

    private async Task ShouldHaveNoDriftAsync(FixedIdentity tenant) => (await DriftAsync(tenant)).ShouldBeEmpty();

    private async Task<IReadOnlyList<DriftRow>> DriftAsync(FixedIdentity tenant)
    {
        await using var context = ApiContext(tenant);

        return await new ReconciliationQuery(context, tenant).FindDriftAsync(CancellationToken.None);
    }

    private async Task<PayerBalance> BalanceAsync(FixedIdentity cashier, PayerDto payer)
    {
        await using var context = ApiContext(cashier);
        var id = new PayerId(payer.Id);

        return await context.Set<PayerBalance>().AsNoTracking().SingleAsync(balance => balance.PayerId == id);
    }

    private async Task<int> CountAsync(string sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQueryRaw<int>(sql).SingleAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        // A defect seeded on purpose, which the application's login may not cause (ADR 0034).
        await using var context = AdministratorApiContext(SystemIdentity.Instance);

        await context.Database.ExecuteSqlRawAsync(sql);
    }

    // Lets the test put two allocations in the order that makes the second one stale. Not armed while the test sets
    // up its bills and payments, whose postings pass straight through.
    private sealed class Gate
    {
        private int _allocations;

        public bool Armed { get; set; }

        public TaskCompletionSource FirstHoldsTheLock { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondHasReadTheBill { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int NextAllocation() => Interlocked.Increment(ref _allocations);
    }

    // The first allocation posts, which locks the balances of the payer, and then waits. The second says it has
    // arrived, which it does only after its handler has read the bill, and then waits for that lock inside the ledger.
    private sealed class GatedLedger(IReceivablesLedger inner, Gate gate) : IReceivablesLedger
    {
        public async Task<Result<LedgerPostingId>> PostAsync(PostingTemplate template, CancellationToken ct)
        {
            if (!gate.Armed || template.Kind != LedgerPostingKind.PaymentAllocated)
            {
                return await inner.PostAsync(template, ct);
            }

            if (gate.NextAllocation() == 1)
            {
                var entered = await inner.PostAsync(template, ct);
                gate.FirstHoldsTheLock.TrySetResult();
                await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

                return entered;
            }

            gate.SecondHasReadTheBill.TrySetResult();

            return await inner.PostAsync(template, ct);
        }
    }

    private sealed class AllocationThenFault(IReceivablesLedger inner) : IReceivablesLedger
    {
        public async Task<Result<LedgerPostingId>> PostAsync(PostingTemplate template, CancellationToken ct)
        {
            var entered = await inner.PostAsync(template, ct);

            return template.Kind == LedgerPostingKind.PaymentAllocated
                ? throw new InvalidOperationException("Injected: the ledger failed after it entered the posting.")
                : entered;
        }
    }

    private sealed class FaultOnPaymentAllocated : IDomainEventSubscriber
    {
        public Task HandleAsync(DomainEventOccurrence occurrence, CancellationToken ct)
            => occurrence.Event is PaymentAllocated
                ? throw new InvalidOperationException("Injected: a subscriber failed after the audit record was written.")
                : Task.CompletedTask;
    }

    // Adds, with the allocation, a payer that names a patient nobody has: the foreign key refuses the whole save.
    private sealed class RowTheDatabaseRefuses(Infrastructure.Common.Persistence.BillingReconDbContext context) : IDomainEventSubscriber
    {
        public Task HandleAsync(DomainEventOccurrence occurrence, CancellationToken ct)
        {
            if (occurrence.Event is PaymentAllocated)
            {
                context.Add(Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "Nobody" }, new PatientId(Guid.NewGuid())).Value);
            }

            return Task.CompletedTask;
        }
    }
}
