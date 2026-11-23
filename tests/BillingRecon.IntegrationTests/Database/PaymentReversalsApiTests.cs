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
using BillingRecon.Domain.Payments;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.Infrastructure.Receivables;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Reversing an allocation and reversing a payment end to end, on the pipeline that ships and a real SQL Server
/// (US-32, US-33, US-25; BR-007, BR-208, BR-305, BR-306, BR-603 to BR-606, BR-803, BR-804): a cashier asks, a second
/// person decides, and an approval writes the mirror ledger postings, the restored balances and the audit records in
/// one transaction or not at all. Nothing is deleted, and every scenario ends with no drift.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PaymentReversalsApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Uri Payments = new("/api/v1/payments", UriKind.Relative);

    // 17 October 2026 in India: fiscal year 2026-27.
    private static readonly DateOnly Today = new(2026, 10, 17);

    // 2 x 60.00 at 5%: 126.00.
    private static readonly BillLineRequest Dressing = BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m);

    // 1 x 500.00 at 18%: 590.00.
    private static readonly BillLineRequest Consultation = BillingApi.FreeTextLine("OPD consultation", 1m, 500m, 18m);

    [Fact]
    public async Task US32_an_allocation_is_reversed_by_a_second_person_and_the_payment_and_the_bill_are_as_before_it_was_made()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier", "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await cashier.NewBillPartiesAsync();
        var large = await PostedBillAsync(cashier, patient, payer, Consultation);
        var small = await PostedBillAsync(cashier, patient, payer, Dressing);
        var payment = await RecordedAsync(cashier, payer, 716m);
        var allocated = await AllocatedAsync(cashier, payment, (large.Id, 590m), (small.Id, 126m));
        var toLarge = allocated.Allocations.Single(allocation => allocation.BillId == large.Id);
        await ShouldHaveNoDriftAsync(maker);

        // The cashier asks: 202, the allocation waits, and nothing financial has happened (BR-305).
        using var asked = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Allocation(payment.Id, toLarge.Id, "reverse"), new RequestReversalRequest { Reason = "Applied to the wrong bill" }, PayersApi.ETag(allocated.Payment.Version));
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());
        var waiting = await asked.ReadAsAsync<PaymentAllocationsDto>();
        asked.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(waiting.Payment.Version));
        waiting.Allocations.Single(allocation => allocation.Id == toLarge.Id).Status.ShouldBe(PaymentAllocationStatus.PendingReversal);
        waiting.Payment.UnappliedAmount.ShouldBe(0.00m);
        (await BillAsync(cashier, large.Id)).Status.ShouldBe(BillStatus.Settled);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'AllocationReversed'")).ShouldBe(0);
        await ShouldHaveNoDriftAsync(maker);

        // The checker approves the next day: that is the reversal (BR-606).
        factory.Clock.Advance(TimeSpan.FromDays(1));
        using var approved = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Allocation(payment.Id, toLarge.Id, "approve-reversal"), new ApproveReversalRequest { Comment = "Confirmed with the ward" }, PayersApi.ETag(waiting.Payment.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var reversed = await approved.ReadAsAsync<PaymentAllocationsDto>();
        reversed.Payment.UnappliedAmount.ShouldBe(590.00m);
        reversed.Payment.Status.ShouldBe(PaymentStatus.Posted);
        reversed.Allocations.Count.ShouldBe(2, "nothing is deleted (BR-005)");
        var taken = reversed.Allocations.Single(allocation => allocation.Id == toLarge.Id);
        (taken.Status, taken.Amount, taken.ReversalReason, taken.ReversedAtUtc).ShouldBe((PaymentAllocationStatus.Reversed, 590.00m, "Applied to the wrong bill", DatabaseApiFactory.InitialInstant.AddDays(1)));
        reversed.Allocations.Single(allocation => allocation.BillId == small.Id).Status.ShouldBe(PaymentAllocationStatus.Active);

        // The bill is owed again and posted again (BR-208); the other bill is untouched.
        var owed = await BillAsync(cashier, large.Id);
        (owed.Status, owed.BalanceDue).ShouldBe((BillStatus.Posted, 590.00m));
        (await BillAsync(cashier, small.Id)).Status.ShouldBe(BillStatus.Settled);

        // The ledger: the mirror pair, naming the posting it reverses; the original posting is as it was.
        await using (var context = ApiContext(maker))
        {
            var mirror = await context.Set<LedgerPosting>().Include(candidate => candidate.Entries).SingleAsync(candidate => candidate.Kind == LedgerPostingKind.AllocationReversed);
            var row = await context.Set<PaymentAllocation>().AsNoTracking().SingleAsync(allocation => allocation.Id == new PaymentAllocationId(toLarge.Id));
            mirror.DocumentType.ShouldBe(LedgerDocumentType.PaymentAllocation);
            mirror.DocumentId.ShouldBe(toLarge.Id);
            mirror.ReversalOfPostingId.ShouldBe(row.PostingId);
            mirror.BusinessDate.ShouldBe(Today.AddDays(1), "the day of the reversal");
            mirror.Description.ShouldBe($"Allocation of payment {payment.Number} to bill {large.Number} reversed");
            mirror.Entries.OrderBy(entry => entry.LineNo).Select(entry => (entry.Account, entry.Debit, entry.Credit, entry.SubjectId)).ShouldBe(
            [
                (LedgerAccount.Receivable, Money.Of(590m), Money.Zero, large.Id),
                (LedgerAccount.UnappliedCash, Money.Zero, Money.Of(590m), payment.Id),
            ]);
            row.ReversalPostingId.ShouldBe(mirror.Id);
            row.ReversedByUserId.ShouldBe(checker.UserId);
            (await context.Set<LedgerPosting>().CountAsync(candidate => candidate.Kind == LedgerPostingKind.PaymentAllocated)).ShouldBe(2);
        }

        var balance = await BalanceAsync(maker, payer);
        (balance.Receivable, balance.UnappliedCash).ShouldBe((Money.Of(590m), Money.Of(590m)));

        // The audit trail of both documents, with the reason (BR-701).
        var trail = await TrailAsync(cashier, "Payment", payment.Id);
        trail.Select(record => record.Action).ShouldBe(["Recorded", "Allocated", "Allocated", "AllocationReversalRequested", "AllocationReversed"]);
        trail[4].Reason.ShouldBe("Applied to the wrong bill");
        trail[4].CorrelationId.ShouldBe(approved.GetCorrelationId());
        (await TrailAsync(cashier, "Bill", large.Id)).Select(record => record.Action).ShouldBe(["Drafted", "Posted", "CreditApplied", "CreditReversed"]);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [DocumentType] = 'PaymentAllocation' AND [Action] = 'Reverse' AND [Status] = 'Approved'")).ShouldBe(1);
        await ShouldHaveNoDriftAsync(maker);

        // The money is free again and goes where it should have gone; the reversed allocation stays reversed.
        var again = await AllocatedAsync(cashier, reversed.Payment, (large.Id, 590m));
        again.Allocations.Count.ShouldBe(3);
        using var twice = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Allocation(payment.Id, toLarge.Id, "reverse"), new RequestReversalRequest { Reason = "Again" }, PayersApi.ETag(again.Payment.Version));
        await twice.ShouldBeProblemAsync(HttpStatusCode.Conflict, PaymentsErrors.AllocationReversedCode);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task US33_a_payment_allocated_to_two_bills_is_reversed_and_every_balance_is_where_it_began()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier", "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await cashier.NewBillPartiesAsync();
        var large = await PostedBillAsync(cashier, patient, payer, Consultation);
        var small = await PostedBillAsync(cashier, patient, payer, Dressing);
        var before = await BalanceAsync(maker, payer);
        (before.Receivable, before.UnappliedCash).ShouldBe((Money.Of(716m), Money.Zero));

        // A cheque of 1,000: 500 to the large bill, all of the small one, 374 left unapplied.
        var payment = await RecordedAsync(cashier, payer, 1000m);
        var allocated = await AllocatedAsync(cashier, payment, (large.Id, 500m), (small.Id, 126m));
        (await BillAsync(cashier, large.Id)).Status.ShouldBe(BillStatus.PartiallyPaid);

        // The cashier asks: the payment waits, and nothing can be allocated from it meanwhile (BR-306).
        using var asked = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "reverse"), new RequestReversalRequest { Reason = "Cheque dishonoured" }, PayersApi.ETag(allocated.Payment.Version));
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());
        var waiting = await asked.ReadAsAsync<PaymentAllocationsDto>();
        waiting.Payment.Status.ShouldBe(PaymentStatus.PendingReversal);
        waiting.Payment.UnappliedAmount.ShouldBe(374.00m);
        using var meanwhile = await AllocateAsync(cashier, waiting.Payment, (large.Id, 90m));
        await meanwhile.ShouldBeProblemAsync(HttpStatusCode.Conflict, PaymentsErrors.PaymentPendingReversalCode);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] IN ('AllocationReversed', 'PaymentReversed')")).ShouldBe(0);
        await ShouldHaveNoDriftAsync(maker);

        // The checker approves: both allocations are reversed first, then the payment.
        factory.Clock.Advance(TimeSpan.FromDays(1));
        using var approved = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Payment.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var reversed = await approved.ReadAsAsync<PaymentAllocationsDto>();
        reversed.Payment.Status.ShouldBe(PaymentStatus.Reversed);
        reversed.Payment.UnappliedAmount.ShouldBe(0.00m, "a reversed payment holds nothing");
        reversed.Payment.Amount.ShouldBe(1000.00m);
        reversed.Payment.Number.ShouldBe(payment.Number, "it keeps its number: the series has no gap (BR-801)");
        (reversed.Payment.ReversalReason, reversed.Payment.ReversedAtUtc).ShouldBe(("Cheque dishonoured", DatabaseApiFactory.InitialInstant.AddDays(1)));
        reversed.Allocations.Count.ShouldBe(2);
        reversed.Allocations.ShouldAllBe(allocation => allocation.Status == PaymentAllocationStatus.Reversed && allocation.ReversalReason == "Cheque dishonoured");

        // Every balance is where it began: both bills posted and owed in full, the payer owes 716 and holds nothing.
        var largeAgain = await BillAsync(cashier, large.Id);
        var smallAgain = await BillAsync(cashier, small.Id);
        (largeAgain.Status, largeAgain.BalanceDue).ShouldBe((BillStatus.Posted, 590.00m));
        (smallAgain.Status, smallAgain.BalanceDue).ShouldBe((BillStatus.Posted, 126.00m));
        var after = await BalanceAsync(maker, payer);
        (after.Receivable, after.UnappliedCash, after.CreditAvailable).ShouldBe((before.Receivable, before.UnappliedCash, before.CreditAvailable));

        // The ledger: two mirror pairs, then Dr UnappliedCash(payment) / Cr Cash for the whole amount.
        await using (var context = ApiContext(maker))
        {
            (await context.Set<LedgerPosting>().CountAsync(candidate => candidate.Kind == LedgerPostingKind.AllocationReversed)).ShouldBe(2);
            var last = await context.Set<LedgerPosting>().Include(candidate => candidate.Entries).SingleAsync(candidate => candidate.Kind == LedgerPostingKind.PaymentReversed);
            var row = await context.Set<Payment>().AsNoTracking().SingleAsync();
            last.DocumentType.ShouldBe(LedgerDocumentType.Payment);
            last.DocumentId.ShouldBe(payment.Id);
            last.ReversalOfPostingId.ShouldBe(row.PostingId);
            last.BusinessDate.ShouldBe(Today.AddDays(1));
            last.Description.ShouldBe($"Payment {payment.Number} reversed");
            last.Entries.OrderBy(entry => entry.LineNo).Select(entry => (entry.Account, entry.Debit, entry.Credit, entry.SubjectId)).ShouldBe(
            [
                (LedgerAccount.UnappliedCash, Money.Of(1000m), Money.Zero, payment.Id),
                (LedgerAccount.Cash, Money.Zero, Money.Of(1000m), null),
            ]);
            row.ReversalPostingId.ShouldBe(last.Id);
            row.ReversedByUserId.ShouldBe(checker.UserId);
        }

        var trail = await TrailAsync(cashier, "Payment", payment.Id);
        trail.Select(record => record.Action).ShouldBe(
            ["Recorded", "Allocated", "Allocated", "ReversalRequested", "AllocationReversed", "AllocationReversed", "Reversed"], ignoreOrder: true);
        trail.Single(record => record.Action == "Reversed").Reason.ShouldBe("Cheque dishonoured");
        (await TrailAsync(cashier, "Bill", small.Id)).Select(record => record.Action).ShouldBe(["Drafted", "Posted", "CreditApplied", "CreditReversed"]);
        await ShouldHaveNoDriftAsync(maker);

        // Reversed for good: nothing is allocated from it, and it is not reversed twice.
        using var late = await AllocateAsync(cashier, reversed.Payment, (large.Id, 1m));
        using var twice = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "reverse"), new RequestReversalRequest { Reason = "Again" }, PayersApi.ETag(reversed.Payment.Version));
        await late.ShouldBeProblemAsync(HttpStatusCode.Conflict, PaymentsErrors.PaymentReversedCode);
        await twice.ShouldBeProblemAsync(HttpStatusCode.Conflict, PaymentsErrors.PaymentReversedCode);

        // The list finds it by its status.
        using var listed = await cashier.GetAsync(Payments.WithQuery("status=Reversed"));
        (await listed.ReadAsAsync<PageOf<PaymentDto>>()).Items.ShouldHaveSingleItem().Number.ShouldBe(payment.Number);
    }

    [Fact]
    public async Task BR603_BR605_the_cashier_who_asked_cannot_decide_and_a_rejection_leaves_everything_as_it_was()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier", "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");

        // The maker with a checker's role too is refused as the maker (BR-603); as a cashier, for the role (ADR 0032).
        using var makerAsChecker = factory.CreateClientFor(maker, "Supervisor");
        var (patient, payer) = await cashier.NewBillPartiesAsync();
        var bill = await PostedBillAsync(cashier, patient, payer, Dressing);
        var payment = await RecordedAsync(cashier, payer, 200m);
        var allocated = await AllocatedAsync(cashier, payment, (bill.Id, 126m));
        var allocation = allocated.Allocations.ShouldHaveSingleItem();

        // Nothing waits yet, and a reversal needs a reason and the version (BR-804).
        using var early = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "approve-reversal"), body: null, PayersApi.ETag(allocated.Payment.Version));
        using var earlyAllocation = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Allocation(payment.Id, allocation.Id, "approve-reversal"), body: null, PayersApi.ETag(allocated.Payment.Version));
        using var noReason = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "reverse"), new RequestReversalRequest(), PayersApi.ETag(allocated.Payment.Version));
        using var noVersion = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "reverse"), new RequestReversalRequest { Reason = "x" }, ifMatch: null);
        using var stale = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "reverse"), new RequestReversalRequest { Reason = "x" }, PayersApi.ETag(payment.Version));
        using var unknown = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Allocation(payment.Id, Guid.NewGuid(), "reverse"), new RequestReversalRequest { Reason = "x" }, PayersApi.ETag(allocated.Payment.Version));
        await early.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.PaymentNotPendingReversalCode);
        await earlyAllocation.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.AllocationNotPendingReversalCode);
        await noReason.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await noVersion.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
        await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.AllocationNotFoundCode);

        // The allocation waits: the cashier cannot decide, and the payment cannot be asked for meanwhile.
        var waiting = await RequestedAsync(cashier, Allocation(payment.Id, allocation.Id, "reverse"), allocated.Payment, "Wrong bill");
        using var own = await makerAsChecker.SendWithIfMatchAsync(HttpMethod.Post, Allocation(payment.Id, allocation.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Payment.Version));
        using var ownRejection = await makerAsChecker.SendWithIfMatchAsync(HttpMethod.Post, Allocation(payment.Id, allocation.Id, "reject-reversal"), new RejectReversalRequest { Comment = "Mine" }, PayersApi.ETag(waiting.Payment.Version));
        using var asCashier = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Allocation(payment.Id, allocation.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Payment.Version));
        await asCashier.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        using var wholePayment = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "reverse"), new RequestReversalRequest { Reason = "Cheque dishonoured" }, PayersApi.ETag(waiting.Payment.Version));
        using var noComment = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Allocation(payment.Id, allocation.Id, "reject-reversal"), new RejectReversalRequest(), PayersApi.ETag(waiting.Payment.Version));
        await own.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        await ownRejection.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        await wholePayment.ShouldBeProblemAsync(HttpStatusCode.Conflict, PaymentsErrors.AllocationPendingReversalCode);
        await noComment.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");

        // The checker rejects: the allocation is in force again, and nothing financial happened (BR-605).
        using var rejected = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Allocation(payment.Id, allocation.Id, "reject-reversal"), new RejectReversalRequest { Comment = "The bill is right" }, PayersApi.ETag(waiting.Payment.Version));
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        var kept = await rejected.ReadAsAsync<PaymentAllocationsDto>();
        kept.Allocations.ShouldHaveSingleItem().ShouldBe(allocation);
        kept.Payment.UnappliedAmount.ShouldBe(74.00m);

        // The same for the payment: asked, then rejected, it is posted again and can be used.
        var paymentWaiting = await RequestedAsync(cashier, Payment(payment.Id, "reverse"), kept.Payment, "Cheque dishonoured");
        using var ownPayment = await makerAsChecker.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "approve-reversal"), body: null, PayersApi.ETag(paymentWaiting.Payment.Version));
        using var allocationMeanwhile = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Allocation(payment.Id, allocation.Id, "reverse"), new RequestReversalRequest { Reason = "x" }, PayersApi.ETag(paymentWaiting.Payment.Version));
        await ownPayment.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        await allocationMeanwhile.ShouldBeProblemAsync(HttpStatusCode.Conflict, PaymentsErrors.PaymentPendingReversalCode);
        using var paymentRejected = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "reject-reversal"), new RejectReversalRequest { Comment = "The bank cleared it" }, PayersApi.ETag(paymentWaiting.Payment.Version));
        paymentRejected.StatusCode.ShouldBe(HttpStatusCode.OK, await paymentRejected.Content.ReadAsStringAsync());
        var posted = await paymentRejected.ReadAsAsync<PaymentAllocationsDto>();
        (posted.Payment.Status, posted.Payment.ReversalReason, posted.Payment.UnappliedAmount).ShouldBe((PaymentStatus.Posted, null, 74.00m));

        (await BillAsync(cashier, bill.Id)).Status.ShouldBe(BillStatus.Settled);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] IN ('AllocationReversed', 'PaymentReversed')")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [Action] = 'Reverse' AND [Status] = 'Rejected'")).ShouldBe(2);
        (await TrailAsync(cashier, "Payment", payment.Id)).Select(record => record.Action).ShouldBe(
            ["Recorded", "Allocated", "AllocationReversalRequested", "AllocationReversalRejected", "ReversalRequested", "ReversalRejected"]);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR804_a_payment_reversal_approved_while_another_allocation_of_the_payment_arrives_gives_exactly_one_success()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier", "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await cashier.NewBillPartiesAsync();
        var bill = await PostedBillAsync(cashier, patient, payer, Consultation);
        var payment = await RecordedAsync(cashier, payer, 590m);
        var allocated = await AllocatedAsync(cashier, payment, (bill.Id, 300m));
        var allocation = allocated.Allocations.ShouldHaveSingleItem();

        // The reversal of the allocation waits. Two checkers decide at once, one approving and one rejecting, both
        // with the version they read.
        var waiting = await RequestedAsync(cashier, Allocation(payment.Id, allocation.Id, "reverse"), allocated.Payment, "Wrong bill");
        var otherChecker = new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid()));
        using var second = factory.CreateClientFor(otherChecker, "TenantAdmin");

        var decisions = await Task.WhenAll(
            Task.Run(() => supervisor.SendWithIfMatchAsync(HttpMethod.Post, Allocation(payment.Id, allocation.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Payment.Version))),
            Task.Run(() => second.SendWithIfMatchAsync(HttpMethod.Post, Allocation(payment.Id, allocation.Id, "reject-reversal"), new RejectReversalRequest { Comment = "No" }, PayersApi.ETag(waiting.Payment.Version))));

        bool approvalWon;
        try
        {
            // Exactly one decision stands. The other read the payment before the winner committed (409 from the
            // version) or after it (the allocation no longer waits: 409 or 422).
            decisions.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
            decisions.Single(response => response.StatusCode != HttpStatusCode.OK).StatusCode.ShouldBeOneOf(HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity);
            approvalWon = decisions[0].StatusCode == HttpStatusCode.OK;
        }
        finally
        {
            foreach (var response in decisions)
            {
                response.Dispose();
            }
        }

        var read = await AllocationsAsync(cashier, payment.Id);
        read.Allocations.ShouldHaveSingleItem().Status.ShouldBe(approvalWon ? PaymentAllocationStatus.Reversed : PaymentAllocationStatus.Active);
        read.Payment.UnappliedAmount.ShouldBe(approvalWon ? 590.00m : 290.00m);
        (await BillAsync(cashier, bill.Id)).BalanceDue.ShouldBe(approvalWon ? 590.00m : 290.00m);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'AllocationReversed'")).ShouldBe(approvalWon ? 1 : 0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [Status] = 'Pending'")).ShouldBe(0);
        await ShouldHaveNoDriftAsync(maker);

        // Then the payment itself: its reversal is approved while the cashier allocates the rest of it. The payment
        // waits, so the allocation is refused whichever arrives first, and the reversal goes through.
        var paymentWaiting = await RequestedAsync(cashier, Payment(payment.Id, "reverse"), read.Payment, "Cheque dishonoured");
        var racing = await Task.WhenAll(
            Task.Run(() => supervisor.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "approve-reversal"), body: null, PayersApi.ETag(paymentWaiting.Payment.Version))),
            Task.Run(() => AllocateAsync(cashier, paymentWaiting.Payment, (bill.Id, 100m))));

        try
        {
            racing[0].StatusCode.ShouldBe(HttpStatusCode.OK, await racing[0].Content.ReadAsStringAsync());
            racing[1].StatusCode.ShouldBe(HttpStatusCode.Conflict, "nothing is allocated from a payment that waits for its reversal or is reversed");
        }
        finally
        {
            foreach (var response in racing)
            {
                response.Dispose();
            }
        }

        var end = await AllocationsAsync(cashier, payment.Id);
        (end.Payment.Status, end.Payment.UnappliedAmount).ShouldBe((PaymentStatus.Reversed, 0.00m));
        end.Allocations.ShouldAllBe(made => made.Status == PaymentAllocationStatus.Reversed);
        var owed = await BillAsync(cashier, bill.Id);
        (owed.Status, owed.BalanceDue).ShouldBe((BillStatus.Posted, 590.00m));
        var balance = await BalanceAsync(maker, payer);
        (balance.Receivable, balance.UnappliedCash).ShouldBe((Money.Of(590m), Money.Zero));
        await ShouldHaveNoDriftAsync(maker);
    }

    [Theory]
    [InlineData("the ledger, after the last posting is entered")]
    [InlineData("the audit trail, after the record is written")]
    public async Task US25_a_reversal_that_fails_at_any_step_leaves_no_ledger_and_no_audit_rows_and_every_document_as_it_was(string step)
    {
        using var factory = new DatabaseApiFactory(SqlServer, configure: services =>
        {
            switch (step)
            {
                case "the ledger, after the last posting is entered":
                    services.AddScoped<ReceivablesLedger>();
                    services.AddScoped<IReceivablesLedger>(provider => new PaymentReversedThenFault(provider.GetRequiredService<ReceivablesLedger>()));
                    break;
                default:
                    // Registered after the audit subscriber, so it runs when the audit records are in the unit of work.
                    services.AddScoped<IDomainEventSubscriber, FaultOnPaymentReversed>();
                    break;
            }
        });
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier", "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await cashier.NewBillPartiesAsync();
        var bill = await PostedBillAsync(cashier, patient, payer, Dressing);
        var payment = await RecordedAsync(cashier, payer, 200m);
        var allocated = await AllocatedAsync(cashier, payment, (bill.Id, 126m));
        var waiting = await RequestedAsync(cashier, Payment(payment.Id, "reverse"), allocated.Payment, "Cheque dishonoured");

        using var failed = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Payment.Version));

        await failed.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
        (await AllocationsAsync(cashier, payment.Id)).ShouldBeEquivalentTo(waiting);
        var settled = await BillAsync(cashier, bill.Id);
        (settled.Status, settled.BalanceDue).ShouldBe((BillStatus.Settled, 0.00m));
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] IN ('AllocationReversed', 'PaymentReversed')")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.AuditRecords WHERE [Action] IN ('Reversed', 'AllocationReversed', 'CreditReversed', 'Approved')")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [Status] = 'Pending'")).ShouldBe(1);
        var balance = await BalanceAsync(maker, payer);
        (balance.Receivable, balance.UnappliedCash).ShouldBe((Money.Zero, Money.Of(74m)));
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR803_a_replayed_approval_returns_the_same_response_and_reverses_once()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier", "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await cashier.NewBillPartiesAsync();
        var bill = await PostedBillAsync(cashier, patient, payer, Dressing);
        var payment = await RecordedAsync(cashier, payer, 126m);
        var allocated = await AllocatedAsync(cashier, payment, (bill.Id, 126m));
        var waiting = await RequestedAsync(cashier, Payment(payment.Id, "reverse"), allocated.Payment, "Cheque dishonoured");
        var key = Guid.NewGuid();

        using var original = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Payment.Version), key);
        using var replay = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Payment.Version), key);

        original.StatusCode.ShouldBe(HttpStatusCode.OK, await original.Content.ReadAsStringAsync());
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).ShouldBe(await original.Content.ReadAsStringAsync());
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'PaymentReversed'")).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'AllocationReversed'")).ShouldBe(1);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR002_a_payment_of_another_tenant_can_neither_be_reversed_nor_decided()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, _) = await NewMakerAndCheckerAsync();
        var stranger = await NewApiTenantAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier", "BillingClerk");
        using var outsider = factory.CreateClientFor(stranger, "TenantAdmin");
        var (patient, payer) = await cashier.NewBillPartiesAsync();
        var bill = await PostedBillAsync(cashier, patient, payer, Dressing);
        var payment = await RecordedAsync(cashier, payer, 126m);
        var allocated = await AllocatedAsync(cashier, payment, (bill.Id, 126m));
        var allocation = allocated.Allocations.ShouldHaveSingleItem();
        var waiting = await RequestedAsync(cashier, Payment(payment.Id, "reverse"), allocated.Payment, "Cheque dishonoured");

        using var ask = await outsider.SendWithIfMatchAsync(HttpMethod.Post, Allocation(payment.Id, allocation.Id, "reverse"), new RequestReversalRequest { Reason = "x" }, PayersApi.ETag(waiting.Payment.Version));
        using var approve = await outsider.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Payment.Version));
        using var reject = await outsider.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "reject-reversal"), new RejectReversalRequest { Comment = "x" }, PayersApi.ETag(waiting.Payment.Version));

        await ask.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.PaymentNotFoundCode);
        await approve.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.PaymentNotFoundCode);
        await reject.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.PaymentNotFoundCode);
        (await AllocationsAsync(cashier, payment.Id)).ShouldBeEquivalentTo(waiting);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR404_a_reversal_that_lost_its_posting_or_a_row_that_no_longer_says_reversed_is_reported()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier", "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await cashier.NewBillPartiesAsync();
        var bill = await PostedBillAsync(cashier, patient, payer, Dressing);
        var payment = await RecordedAsync(cashier, payer, 126m);
        var allocated = await AllocatedAsync(cashier, payment, (bill.Id, 126m));
        var allocation = allocated.Allocations.ShouldHaveSingleItem();
        var waiting = await RequestedAsync(cashier, Payment(payment.Id, "reverse"), allocated.Payment, "Cheque dishonoured");
        using var approved = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Payment.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        await ShouldHaveNoDriftAsync(maker);

        // The mirror postings are made to name other postings: both reversals have lost theirs.
        await ExecuteAsync("ALTER TABLE dbo.LedgerPostings NOCHECK CONSTRAINT ALL; UPDATE dbo.LedgerPostings SET [ReversalOfPostingId] = NULL WHERE [Kind] IN ('AllocationReversed', 'PaymentReversed'); ALTER TABLE dbo.LedgerPostings CHECK CONSTRAINT ALL;");
        (await DriftAsync(maker)).ShouldBe(
            [
                new DriftRow(DriftIdentity.ReversedAllocationNoPosting, allocation.Id, null, 126.00m, 0.00m),
                new DriftRow(DriftIdentity.ReversedPaymentNoPosting, payment.Id, payment.Number, 126.00m, 0.00m),
            ],
            ignoreOrder: true);
    }

    private static Uri Payment(Guid id, string action) => new($"/api/v1/payments/{id}/{action}", UriKind.Relative);

    private static Uri Allocation(Guid paymentId, Guid allocationId, string action) => new($"/api/v1/payments/{paymentId}/allocations/{allocationId}/{action}", UriKind.Relative);

    // A request to reverse, which must be accepted: the payment with its allocations as it waits.
    private static async Task<PaymentAllocationsDto> RequestedAsync(HttpClient client, Uri uri, PaymentDto payment, string reason)
    {
        using var response = await client.SendWithIfMatchAsync(HttpMethod.Post, uri, new RequestReversalRequest { Reason = reason }, PayersApi.ETag(payment.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PaymentAllocationsDto>();
    }

    private static Task<HttpResponseMessage> AllocateAsync(HttpClient client, PaymentDto payment, params (Guid BillId, decimal Amount)[] lines)
        => client.SendWithIfMatchAsync(
            HttpMethod.Post,
            new Uri($"/api/v1/payments/{payment.Id}/allocations", UriKind.Relative),
            new AllocatePaymentRequest { Allocations = [.. lines.Select(line => new AllocationRequest { BillId = line.BillId, Amount = line.Amount })] },
            PayersApi.ETag(payment.Version));

    private static async Task<PaymentAllocationsDto> AllocatedAsync(HttpClient client, PaymentDto payment, params (Guid BillId, decimal Amount)[] lines)
    {
        using var response = await AllocateAsync(client, payment, lines);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PaymentAllocationsDto>();
    }

    private static async Task<PaymentAllocationsDto> AllocationsAsync(HttpClient client, Guid paymentId)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/payments/{paymentId}/allocations", UriKind.Relative));

        return await response.ReadAsAsync<PaymentAllocationsDto>();
    }

    private static async Task<PaymentDto> RecordedAsync(HttpClient client, PayerDto payer, decimal amount)
    {
        using var response = await client.PostJsonAsync(Payments, new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.Cheque, Reference = "000123", Amount = amount });

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

    private static async Task<IReadOnlyList<AuditRecordDto>> TrailAsync(HttpClient client, string entityType, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/audit-records?entityType={entityType}&entityId={id}", UriKind.Relative));

        return (await response.ReadAsAsync<PageOf<AuditRecordDto>>()).Items;
    }

    // A tenant without an approval threshold, so its bills post at once, and two of its users.
    private async Task<(FixedIdentity Maker, FixedIdentity Checker)> NewMakerAndCheckerAsync()
    {
        var maker = await NewApiTenantAsync();

        return (maker, new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid())));
    }

    private async Task ShouldHaveNoDriftAsync(FixedIdentity tenant) => (await DriftAsync(tenant)).ShouldBeEmpty();

    private async Task<IReadOnlyList<DriftRow>> DriftAsync(FixedIdentity tenant)
    {
        await using var context = ApiContext(tenant);

        return await new ReconciliationQuery(context, tenant).FindDriftAsync(CancellationToken.None);
    }

    private async Task<PayerBalance> BalanceAsync(FixedIdentity tenant, PayerDto payer)
    {
        await using var context = ApiContext(tenant);
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

    private sealed class PaymentReversedThenFault(IReceivablesLedger inner) : IReceivablesLedger
    {
        public async Task<Result<LedgerPostingId>> PostAsync(PostingTemplate template, CancellationToken ct)
        {
            var entered = await inner.PostAsync(template, ct);

            return template.Kind == LedgerPostingKind.PaymentReversed
                ? throw new InvalidOperationException("Injected: the ledger failed after it entered the posting.")
                : entered;
        }
    }

    private sealed class FaultOnPaymentReversed : IDomainEventSubscriber
    {
        public Task HandleAsync(DomainEventOccurrence occurrence, CancellationToken ct)
            => occurrence.Event is PaymentReversed
                ? throw new InvalidOperationException("Injected: a subscriber failed after the audit record was written.")
                : Task.CompletedTask;
    }
}
