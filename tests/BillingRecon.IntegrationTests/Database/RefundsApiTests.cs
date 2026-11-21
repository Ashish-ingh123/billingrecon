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
/// Refunds end to end, on the pipeline that ships and a real SQL Server (US-34, US-25; BR-007, BR-306 to BR-309,
/// BR-603 to BR-606, BR-801, BR-803, BR-804; ADR 0029): a cashier drafts a refund of a payment's unapplied cash or a
/// credit note's available credit, a second person approves, and the approval writes the number, the lower balance of
/// the source, the ledger posting and the audit records in one transaction or not at all. Every scenario ends with no
/// drift.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class RefundsApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Uri Refunds = new("/api/v1/refunds", UriKind.Relative);
    private static readonly Uri Payments = new("/api/v1/payments", UriKind.Relative);
    private static readonly Uri CreditNotes = new("/api/v1/credit-notes", UriKind.Relative);

    // 17 October 2026 in India: fiscal year 2026-27.
    private static readonly DateOnly Today = new(2026, 10, 17);

    // 2 x 60.00 at 5%: 126.00.
    private static readonly BillLineRequest Dressing = BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m);

    // 1 x 500.00 at 18%: 590.00.
    private static readonly BillLineRequest Consultation = BillingApi.FreeTextLine("OPD consultation", 1m, 500m, 18m);

    [Fact]
    public async Task US34_a_refund_of_unapplied_cash_waits_for_a_second_person_whose_approval_posts_it()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await cashier.NewBillPartiesAsync();
        var bill = await PostedBillAsync(cashier, patient, payer, Consultation);
        var payment = await RecordedAsync(cashier, payer, 1000m);
        await AllocatedAsync(cashier, payment, bill.Id, 590m);

        // The draft takes the payer of its payment, and nothing financial happens.
        using var created = await cashier.PostJsonAsync(Refunds, new CreateRefundRequest { PaymentId = payment.Id, Amount = 300m, Method = PaymentMethod.UPI, Reference = "412345678901", Reason = "Paid twice" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var draft = await created.ReadAsAsync<RefundDto>();
        created.Headers.Location.ShouldNotBeNull().ToString().ShouldEndWith($"/api/v1/refunds/{draft.Id}");
        (draft.Status, draft.PayerId, draft.SourceKind, draft.PaymentId, draft.CreditNoteId).ShouldBe((RefundStatus.Draft, payer.Id, RefundSourceKind.Payment, payment.Id, null));
        (draft.RefundDate, draft.Amount, draft.Method, draft.Reference, draft.Number).ShouldBe((Today, 300.00m, PaymentMethod.UPI, "412345678901", null));

        // The maker asks: 202, and still nothing financial has happened (BR-604).
        using var submitted = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Refund(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        submitted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await submitted.Content.ReadAsStringAsync());
        var waiting = await submitted.ReadAsAsync<RefundDto>();
        (waiting.Status, waiting.Number).ShouldBe((RefundStatus.PendingApproval, null));
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'RefundPosted'")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries WHERE [DocumentType] = 'Refund'")).ShouldBe(0);
        (await PaymentAsync(cashier, payment.Id)).UnappliedAmount.ShouldBe(410.00m);
        await ShouldHaveNoDriftAsync(maker);

        // The checker approves the next day: that is the posting (BR-606).
        factory.Clock.Advance(TimeSpan.FromDays(1));
        using var approved = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Refund(draft.Id, "approve"), new ApproveRefundRequest { Comment = "Bank details checked" }, PayersApi.ETag(waiting.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var posted = await approved.ReadAsAsync<RefundDto>();
        approved.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(posted.Version));
        (posted.Status, posted.Number, posted.FiscalYear, posted.PostedAtUtc).ShouldBe((RefundStatus.Posted, "REF/26-27/000001", "2026-27", DatabaseApiFactory.InitialInstant.AddDays(1)));
        (await PaymentAsync(cashier, payment.Id)).UnappliedAmount.ShouldBe(110.00m);

        // The ledger: Dr UnappliedCash(payment) / Cr Cash, dated the refund date (BR-307).
        await using (var context = ApiContext(maker))
        {
            var entry = await context.Set<LedgerPosting>().Include(candidate => candidate.Entries).SingleAsync(candidate => candidate.Kind == LedgerPostingKind.RefundPosted);
            (entry.DocumentType, entry.DocumentId, entry.BusinessDate).ShouldBe((LedgerDocumentType.Refund, posted.Id, Today));
            entry.Description.ShouldBe($"Refund REF/26-27/000001 from payment {payment.Number}");
            entry.Entries.OrderBy(line => line.LineNo).Select(line => (line.Account, line.Debit, line.Credit, line.SubjectId)).ShouldBe(
            [
                (LedgerAccount.UnappliedCash, Money.Of(300m), Money.Zero, payment.Id),
                (LedgerAccount.Cash, Money.Zero, Money.Of(300m), null),
            ]);
            (await context.Set<Refund>().AsNoTracking().SingleAsync()).PostingId.ShouldBe(entry.Id);
        }

        var balance = await BalanceAsync(maker, payer);
        (balance.Receivable, balance.UnappliedCash).ShouldBe((Money.Zero, Money.Of(110m)));

        // The audit trail of the refund and of its payment, in the request of the approval (BR-701).
        var trail = await TrailAsync(cashier, "Refund", posted.Id);
        trail.Select(record => record.Action).ShouldBe(["Drafted", "SubmittedForApproval", "Posted"]);
        (trail[2].DocumentNumber, trail[2].Reason, trail[2].CorrelationId).ShouldBe(("REF/26-27/000001", "Paid twice", approved.GetCorrelationId()));
        var paymentTrail = await TrailAsync(cashier, "Payment", payment.Id);
        paymentTrail.Select(record => record.Action).ShouldBe(["Recorded", "Allocated", "Refunded"]);
        paymentTrail[2].CorrelationId.ShouldBe(approved.GetCorrelationId());
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [DocumentType] = 'Refund' AND [Status] = 'Approved'")).ShouldBe(1);
        await ShouldHaveNoDriftAsync(maker);

        // Reading it gives what the approval answered; the list finds it by its payment.
        (await RefundAsync(cashier, posted.Id)).ShouldBeEquivalentTo(posted);
        using var listed = await cashier.GetAsync(Refunds.WithQuery($"paymentId={payment.Id}&status=Posted"));
        (await listed.ReadAsAsync<PageOf<RefundDto>>()).Items.ShouldHaveSingleItem().Number.ShouldBe("REF/26-27/000001");

        // Posted for good (BR-308), and its payment is never reversed (BR-306).
        using var edit = await cashier.SendWithIfMatchAsync(HttpMethod.Put, Refund(posted.Id), new UpdateRefundRequest { RefundDate = Today, Amount = 1m, Method = PaymentMethod.Cash, Reason = "x" }, PayersApi.ETag(posted.Version));
        using var reverse = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Payment(payment.Id, "reverse"), new RequestReversalRequest { Reason = "Cheque dishonoured" }, PayersApi.ETag((await PaymentAsync(cashier, payment.Id)).Version));
        await edit.ShouldBeProblemAsync(HttpStatusCode.Conflict, PaymentsErrors.RefundAlreadyPostedCode);
        await reverse.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.PaymentRefundedCode);
        (await PaymentAsync(cashier, payment.Id)).Status.ShouldBe(PaymentStatus.Posted);
    }

    [Fact]
    public async Task BR213_a_refund_of_available_credit_lowers_the_credit_note_and_the_payers_credit()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);

        // 100 of the 126 are paid, so the whole bill credited leaves 100 available after 26 went to the bill (BR-212).
        await AllocatedAsync(clerk, await RecordedAsync(clerk, payer, 100m), bill.Id, 100m);
        var creditNote = await ApprovedCreditNoteAsync(clerk, supervisor, await DraftedCreditNoteAsync(clerk, bill));
        creditNote.AvailableAmount.ShouldBe(100.00m);

        // A credit note that is not posted has nothing to refund.
        var unposted = await DraftedCreditNoteAsync(clerk, bill);
        using var fromDraft = await clerk.PostJsonAsync(Refunds, new CreateRefundRequest { CreditNoteId = unposted.Id, Amount = 1m, Method = PaymentMethod.Cash, Reason = "x" });
        await fromDraft.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditNoteNotPostedCode);

        var draft = await DraftedAsync(clerk, new CreateRefundRequest { CreditNoteId = creditNote.Id, Amount = 100m, Method = PaymentMethod.Cash, Reason = "Patient discharged early" });
        (draft.PayerId, draft.SourceKind, draft.CreditNoteId, draft.Reference).ShouldBe((payer.Id, RefundSourceKind.CreditNote, creditNote.Id, null));
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var posted = await ApprovedAsync(clerk, supervisor, draft);

        posted.Number.ShouldBe("REF/26-27/000001");
        (await CreditNoteAsync(clerk, creditNote.Id)).AvailableAmount.ShouldBe(0.00m);
        await using (var context = ApiContext(maker))
        {
            var entry = await context.Set<LedgerPosting>().Include(candidate => candidate.Entries).SingleAsync(candidate => candidate.Kind == LedgerPostingKind.RefundPosted);
            entry.Description.ShouldBe($"Refund REF/26-27/000001 from credit note {creditNote.Number}");
            entry.Entries.OrderBy(line => line.LineNo).Select(line => (line.Account, line.Debit, line.Credit, line.SubjectId)).ShouldBe(
            [
                (LedgerAccount.CreditAvailable, Money.Of(100m), Money.Zero, creditNote.Id),
                (LedgerAccount.Cash, Money.Zero, Money.Of(100m), null),
            ]);
        }

        (await BalanceAsync(maker, payer)).CreditAvailable.ShouldBe(Money.Zero);
        (await TrailAsync(clerk, "CreditNote", creditNote.Id)).Single(record => record.Action == "Refunded").DocumentNumber.ShouldBe(creditNote.Number);
        await ShouldHaveNoDriftAsync(maker);

        // Nothing is left: neither a refund nor an application of the credit fits any more.
        var more = await DraftedAsync(clerk, new CreateRefundRequest { CreditNoteId = creditNote.Id, Amount = 1m, Method = PaymentMethod.Cash, Reason = "x" });
        using var tooMuch = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Refund(more.Id, "post"), body: null, PayersApi.ETag(more.Version));
        await tooMuch.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.RefundExceedsAvailableCode);
    }

    [Fact]
    public async Task BR307_the_cap_is_asked_when_the_maker_submits_and_again_when_the_checker_approves()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await cashier.NewBillPartiesAsync();
        var bill = await PostedBillAsync(cashier, patient, payer, Consultation);
        var payment = await RecordedAsync(cashier, payer, 200m);

        // The refund fits when it is handed over; the payment is allocated while it waits, which a refund does not stop.
        var waiting = await SubmittedAsync(cashier, await DraftedAsync(cashier, new CreateRefundRequest { PaymentId = payment.Id, Amount = 150m, Method = PaymentMethod.Cash, Reason = "Paid twice" }));
        await AllocatedAsync(cashier, payment, bill.Id, 100m);

        using var refused = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Refund(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));
        await refused.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.RefundExceedsAvailableCode);
        (await RefundAsync(cashier, waiting.Id)).ShouldBeEquivalentTo(waiting);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries WHERE [DocumentType] = 'Refund'")).ShouldBe(0, "every refusal comes before the number (BR-801)");
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'RefundPosted'")).ShouldBe(0);

        // Rejected, corrected to what is left, handed over again and approved.
        using var rejected = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Refund(waiting.Id, "reject"), new RejectRefundRequest { Comment = "Only 100.00 is left" }, PayersApi.ETag(waiting.Version));
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        var returned = await rejected.ReadAsAsync<RefundDto>();
        returned.Status.ShouldBe(RefundStatus.Draft);
        using var corrected = await cashier.SendWithIfMatchAsync(HttpMethod.Put, Refund(waiting.Id), new UpdateRefundRequest { RefundDate = Today, Amount = 100m, Method = PaymentMethod.Cheque, Reference = "000777", Reason = "Paid twice" }, PayersApi.ETag(returned.Version));
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK, await corrected.Content.ReadAsStringAsync());
        var replaced = await corrected.ReadAsAsync<RefundDto>();
        (replaced.Amount, replaced.Method, replaced.Reference, replaced.PaymentId).ShouldBe((100.00m, PaymentMethod.Cheque, "000777", payment.Id));
        var posted = await ApprovedAsync(cashier, supervisor, replaced);
        posted.Number.ShouldBe("REF/26-27/000001", "the refused approval took no number");
        (await PaymentAsync(cashier, payment.Id)).UnappliedAmount.ShouldBe(0.00m);

        // Nothing left: the maker's post of another refund is refused at once.
        var another = await DraftedAsync(cashier, new CreateRefundRequest { PaymentId = payment.Id, Amount = 0.01m, Method = PaymentMethod.Cash, Reason = "x" });
        using var early = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Refund(another.Id, "post"), body: null, PayersApi.ETag(another.Version));
        await early.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.RefundExceedsAvailableCode);
        (await TrailAsync(cashier, "Refund", waiting.Id)).Select(record => record.Action).ShouldBe(
            ["Drafted", "SubmittedForApproval", "ReturnedToDraft", "Updated", "SubmittedForApproval", "Posted"]);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR301_BR307_a_draft_needs_one_usable_source_a_payout_and_a_date_not_before_its_source()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, _) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier");
        var (_, payer) = await cashier.NewBillPartiesAsync();
        var payment = await RecordedAsync(cashier, payer, 200m);
        var reversing = await RecordedAsync(cashier, payer, 50m);
        using var asked = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Payment(reversing.Id, "reverse"), new RequestReversalRequest { Reason = "Cheque dishonoured" }, PayersApi.ETag(reversing.Version));
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());

        using var noSource = await cashier.PostJsonAsync(Refunds, new CreateRefundRequest { Amount = 1m, Method = PaymentMethod.Cash, Reason = "x" });
        using var twoSources = await cashier.PostJsonAsync(Refunds, new CreateRefundRequest { PaymentId = payment.Id, CreditNoteId = Guid.NewGuid(), Amount = 1m, Method = PaymentMethod.Cash, Reason = "x" });
        using var noReference = await cashier.PostJsonAsync(Refunds, new CreateRefundRequest { PaymentId = payment.Id, Amount = 1m, Method = PaymentMethod.UPI, Reason = "x" });
        using var nothing = await cashier.PostJsonAsync(Refunds, new CreateRefundRequest { PaymentId = payment.Id, Amount = 0m, Method = PaymentMethod.Cash, Reason = "x" });
        using var unknownPayment = await cashier.PostJsonAsync(Refunds, new CreateRefundRequest { PaymentId = Guid.NewGuid(), Amount = 1m, Method = PaymentMethod.Cash, Reason = "x" });
        using var unknownCreditNote = await cashier.PostJsonAsync(Refunds, new CreateRefundRequest { CreditNoteId = Guid.NewGuid(), Amount = 1m, Method = PaymentMethod.Cash, Reason = "x" });
        using var beforePayment = await cashier.PostJsonAsync(Refunds, new CreateRefundRequest { PaymentId = payment.Id, RefundDate = Today.AddDays(-1), Amount = 1m, Method = PaymentMethod.Cash, Reason = "x" });
        using var future = await cashier.PostJsonAsync(Refunds, new CreateRefundRequest { PaymentId = payment.Id, RefundDate = Today.AddDays(1), Amount = 1m, Method = PaymentMethod.Cash, Reason = "x" });
        using var fromReversing = await cashier.PostJsonAsync(Refunds, new CreateRefundRequest { PaymentId = reversing.Id, Amount = 1m, Method = PaymentMethod.Cash, Reason = "x" });

        await noSource.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await twoSources.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await noReference.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await nothing.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await unknownPayment.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.PaymentNotFoundCode);
        await unknownCreditNote.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.CreditNoteNotFoundCode);
        await beforePayment.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.RefundDateBeforeSourceDateCode);
        await future.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.RefundDateInFutureCode);
        await fromReversing.ShouldBeProblemAsync(HttpStatusCode.Conflict, PaymentsErrors.PaymentPendingReversalCode);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Refunds")).ShouldBe(0);
    }

    [Fact]
    public async Task BR603_BR605_the_maker_cannot_decide_a_waiting_refund_is_locked_and_a_voided_draft_stays_void()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (_, payer) = await cashier.NewBillPartiesAsync();
        var payment = await RecordedAsync(cashier, payer, 200m);
        var draft = await DraftedAsync(cashier, new CreateRefundRequest { PaymentId = payment.Id, Amount = 50m, Method = PaymentMethod.Cash, Reason = "Paid twice" });

        using var early = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Refund(draft.Id, "approve"), body: null, PayersApi.ETag(draft.Version));
        await early.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.RefundNotPendingApprovalCode);

        var waiting = await SubmittedAsync(cashier, draft);
        using var own = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Refund(draft.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));
        using var ownRejection = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Refund(draft.Id, "reject"), new RejectRefundRequest { Comment = "Mine" }, PayersApi.ETag(waiting.Version));
        using var edit = await cashier.SendWithIfMatchAsync(HttpMethod.Put, Refund(draft.Id), new UpdateRefundRequest { RefundDate = Today, Amount = 1m, Method = PaymentMethod.Cash, Reason = "x" }, PayersApi.ETag(waiting.Version));
        using var voiding = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Refund(draft.Id, "void"), new VoidRefundRequest { Reason = "x" }, PayersApi.ETag(waiting.Version));
        using var noComment = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Refund(draft.Id, "reject"), new RejectRefundRequest(), PayersApi.ETag(waiting.Version));
        using var noVersion = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Refund(draft.Id, "approve"), body: null, ifMatch: null);
        await own.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        await ownRejection.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        await edit.ShouldBeProblemAsync(HttpStatusCode.Conflict, PaymentsErrors.RefundPendingApprovalCode);
        await voiding.ShouldBeProblemAsync(HttpStatusCode.Conflict, PaymentsErrors.RefundPendingApprovalCode);
        await noComment.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await noVersion.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");

        // Rejected with the reason; then given up.
        using var rejected = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Refund(draft.Id, "reject"), new RejectRefundRequest { Comment = "Pay it by cheque" }, PayersApi.ETag(waiting.Version));
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        var returned = await rejected.ReadAsAsync<RefundDto>();
        using var voided = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Refund(draft.Id, "void"), new VoidRefundRequest { Reason = "Raised twice" }, PayersApi.ETag(returned.Version));
        voided.StatusCode.ShouldBe(HttpStatusCode.OK, await voided.Content.ReadAsStringAsync());
        var gone = await voided.ReadAsAsync<RefundDto>();
        (gone.Status, gone.VoidReason, gone.VoidedAtUtc).ShouldBe((RefundStatus.Voided, "Raised twice", DatabaseApiFactory.InitialInstant));
        using var afterVoid = await cashier.SendWithIfMatchAsync(HttpMethod.Post, Refund(draft.Id, "post"), body: null, PayersApi.ETag(gone.Version));
        await afterVoid.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.RefundNotDraftCode);

        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [DocumentType] = 'Refund' AND [Status] = 'Rejected' AND [CheckerComment] = 'Pay it by cheque'")).ShouldBe(1);
        (await PaymentAsync(cashier, payment.Id)).UnappliedAmount.ShouldBe(200.00m);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR309_money_goes_back_only_to_an_active_payer_when_it_is_drafted_and_when_it_is_approved()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (_, payer) = await cashier.NewBillPartiesAsync();
        var payment = await RecordedAsync(cashier, payer, 200m);
        var waiting = await SubmittedAsync(cashier, await DraftedAsync(cashier, new CreateRefundRequest { PaymentId = payment.Id, Amount = 50m, Method = PaymentMethod.Cash, Reason = "Paid twice" }));

        // The payer is deactivated while the refund waits.
        using var read = await cashier.GetAsync(PayersApi.Payer(payer.Id));
        var current = await read.ReadAsAsync<PayerDto>();
        using var deactivated = await cashier.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Payer(payer.Id, "deactivate"), body: null, PayersApi.ETag(current.Version));
        deactivated.StatusCode.ShouldBe(HttpStatusCode.OK, await deactivated.Content.ReadAsStringAsync());

        using var approval = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Refund(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));
        using var another = await cashier.PostJsonAsync(Refunds, new CreateRefundRequest { PaymentId = payment.Id, Amount = 1m, Method = PaymentMethod.Cash, Reason = "x" });

        await approval.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PayersErrors.PayerInactiveCode);
        await another.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PayersErrors.PayerInactiveCode);
        (await RefundAsync(cashier, waiting.Id)).Status.ShouldBe(RefundStatus.PendingApproval);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries WHERE [DocumentType] = 'Refund'")).ShouldBe(0);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR804_two_refunds_of_one_payment_approved_at_once_that_do_not_both_fit_give_exactly_one_posting()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier");
        using var first = factory.CreateClientFor(checker, "Supervisor");
        using var second = factory.CreateClientFor(new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid())), "TenantAdmin");
        var (_, payer) = await cashier.NewBillPartiesAsync();
        var payment = await RecordedAsync(cashier, payer, 300m);
        var one = await SubmittedAsync(cashier, await DraftedAsync(cashier, new CreateRefundRequest { PaymentId = payment.Id, Amount = 200m, Method = PaymentMethod.Cash, Reason = "One" }));
        var two = await SubmittedAsync(cashier, await DraftedAsync(cashier, new CreateRefundRequest { PaymentId = payment.Id, Amount = 200m, Method = PaymentMethod.Cash, Reason = "Two" }));

        var responses = await Task.WhenAll(
            Task.Run(() => first.SendWithIfMatchAsync(HttpMethod.Post, Refund(one.Id, "approve"), body: null, PayersApi.ETag(one.Version))),
            Task.Run(() => second.SendWithIfMatchAsync(HttpMethod.Post, Refund(two.Id, "approve"), body: null, PayersApi.ETag(two.Version))));

        try
        {
            // The loser read the payment before the winner committed (409) or after it (422). A second posting never.
            var bodies = string.Join(" | ", await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync())));
            responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1, bodies);
            responses.Single(response => response.StatusCode != HttpStatusCode.OK).StatusCode.ShouldBeOneOf([HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity], bodies);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        // One refund is posted, with the first number; the other still waits and took none (BR-801).
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Refunds WHERE [Status] = 'Posted' AND [Number] = 'REF/26-27/000001'")).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Refunds WHERE [Status] = 'PendingApproval' AND [Number] IS NULL")).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries WHERE [DocumentType] = 'Refund' AND [NextNumber] = 2")).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'RefundPosted'")).ShouldBe(1);
        (await PaymentAsync(cashier, payment.Id)).UnappliedAmount.ShouldBe(100.00m);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR804_a_refund_approved_while_the_same_payment_is_allocated_and_only_one_fits_never_fails()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await cashier.NewBillPartiesAsync();
        var bill = await PostedBillAsync(cashier, patient, payer, Consultation);
        var payment = await RecordedAsync(cashier, payer, 300m);
        var waiting = await SubmittedAsync(cashier, await DraftedAsync(cashier, new CreateRefundRequest { PaymentId = payment.Id, Amount = 200m, Method = PaymentMethod.Cash, Reason = "Paid twice" }));

        var racing = await Task.WhenAll(
            Task.Run(() => supervisor.SendWithIfMatchAsync(HttpMethod.Post, Refund(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version))),
            Task.Run(() => AllocateAsync(cashier, payment, bill.Id, 200m)));

        bool refunded;
        try
        {
            // Whichever commits second either read the payment before the first (409) or after it (422): never a fault.
            var bodies = string.Join(" | ", await Task.WhenAll(racing.Select(response => response.Content.ReadAsStringAsync())));
            racing.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1, bodies);
            racing.Single(response => response.StatusCode != HttpStatusCode.OK).StatusCode.ShouldBeOneOf([HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity], bodies);
            refunded = racing[0].StatusCode == HttpStatusCode.OK;
        }
        finally
        {
            foreach (var response in racing)
            {
                response.Dispose();
            }
        }

        (await PaymentAsync(cashier, payment.Id)).UnappliedAmount.ShouldBe(100.00m);
        (await RefundAsync(cashier, waiting.Id)).Status.ShouldBe(refunded ? RefundStatus.Posted : RefundStatus.PendingApproval);
        (await BillAsync(cashier, bill.Id)).BalanceDue.ShouldBe(refunded ? 590.00m : 390.00m);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Theory]
    [InlineData("the ledger, after the refund posting is entered")]
    [InlineData("the audit trail, after the record is written")]
    public async Task US25_an_approval_that_fails_at_any_step_leaves_no_number_no_ledger_and_no_audit_rows_and_both_documents_as_they_were(string step)
    {
        using var factory = new DatabaseApiFactory(SqlServer, configure: services =>
        {
            switch (step)
            {
                case "the ledger, after the refund posting is entered":
                    services.AddScoped<ReceivablesLedger>();
                    services.AddScoped<IReceivablesLedger>(provider => new RefundPostedThenFault(provider.GetRequiredService<ReceivablesLedger>()));
                    break;
                default:
                    // Registered after the audit subscriber, so it runs when the audit records are in the unit of work.
                    services.AddScoped<IDomainEventSubscriber, FaultOnRefundPosted>();
                    break;
            }
        });
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (_, payer) = await cashier.NewBillPartiesAsync();
        var payment = await RecordedAsync(cashier, payer, 200m);
        var waiting = await SubmittedAsync(cashier, await DraftedAsync(cashier, new CreateRefundRequest { PaymentId = payment.Id, Amount = 50m, Method = PaymentMethod.Cash, Reason = "Paid twice" }));
        var before = await PaymentAsync(cashier, payment.Id);

        using var failed = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Refund(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));

        await failed.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
        (await RefundAsync(cashier, waiting.Id)).ShouldBeEquivalentTo(waiting);
        (await PaymentAsync(cashier, payment.Id)).ShouldBeEquivalentTo(before);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries WHERE [DocumentType] = 'Refund'")).ShouldBe(0, "the number went back with the rest (BR-801)");
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'RefundPosted'")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.AuditRecords WHERE ([EntityType] = 'Refund' AND [Action] = 'Posted') OR [Action] = 'Refunded'")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [DocumentType] = 'Refund' AND [Status] = 'Pending'")).ShouldBe(1);
        (await BalanceAsync(maker, payer)).UnappliedCash.ShouldBe(Money.Of(200m));
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR803_a_replayed_approval_returns_the_same_response_and_posts_once()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (_, payer) = await cashier.NewBillPartiesAsync();
        var payment = await RecordedAsync(cashier, payer, 200m);
        var waiting = await SubmittedAsync(cashier, await DraftedAsync(cashier, new CreateRefundRequest { PaymentId = payment.Id, Amount = 50m, Method = PaymentMethod.Cash, Reason = "Paid twice" }));
        var key = Guid.NewGuid();

        using var original = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Refund(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version), key);
        using var replay = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Refund(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version), key);

        original.StatusCode.ShouldBe(HttpStatusCode.OK, await original.Content.ReadAsStringAsync());
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).ShouldBe(await original.Content.ReadAsStringAsync());
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'RefundPosted'")).ShouldBe(1);
        (await PaymentAsync(cashier, payment.Id)).UnappliedAmount.ShouldBe(150.00m);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR002_a_refund_and_a_payment_of_another_tenant_can_neither_be_read_nor_used()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, _) = await NewMakerAndCheckerAsync();
        var stranger = await NewApiTenantAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier");
        using var outsider = factory.CreateClientFor(stranger, "TenantAdmin");
        var (_, payer) = await cashier.NewBillPartiesAsync();
        var payment = await RecordedAsync(cashier, payer, 200m);
        var waiting = await SubmittedAsync(cashier, await DraftedAsync(cashier, new CreateRefundRequest { PaymentId = payment.Id, Amount = 50m, Method = PaymentMethod.Cash, Reason = "Paid twice" }));

        using var read = await outsider.GetAsync(Refund(waiting.Id));
        using var approve = await outsider.SendWithIfMatchAsync(HttpMethod.Post, Refund(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));
        using var across = await outsider.PostJsonAsync(Refunds, new CreateRefundRequest { PaymentId = payment.Id, Amount = 1m, Method = PaymentMethod.Cash, Reason = "x" });
        using var list = await outsider.GetAsync(Refunds);

        await read.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.RefundNotFoundCode);
        await approve.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.RefundNotFoundCode);
        await across.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.PaymentNotFoundCode);
        (await list.ReadAsAsync<PageOf<RefundDto>>()).Items.ShouldBeEmpty();
        (await RefundAsync(cashier, waiting.Id)).ShouldBeEquivalentTo(waiting);
        await ShouldHaveNoDriftAsync(maker);
        await ShouldHaveNoDriftAsync(stranger);
    }

    [Fact]
    public async Task BR404_a_refund_without_its_posting_a_posting_without_its_refund_and_a_refund_to_another_payer_are_reported()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (_, payer) = await cashier.NewBillPartiesAsync();
        var (_, otherPayer) = await cashier.NewBillPartiesAsync();
        var payment = await RecordedAsync(cashier, payer, 200m);
        var posted = await ApprovedAsync(cashier, supervisor, await DraftedAsync(cashier, new CreateRefundRequest { PaymentId = payment.Id, Amount = 50m, Method = PaymentMethod.Cash, Reason = "Paid twice" }));
        await ShouldHaveNoDriftAsync(maker);

        // The posting is made to name another document: both directions of identity 2 report it.
        Guid postingId;
        await using (var context = ApiContext(maker))
        {
            postingId = (await context.Set<LedgerPosting>().AsNoTracking().SingleAsync(candidate => candidate.Kind == LedgerPostingKind.RefundPosted)).Id.Value;
        }

        await ExecuteAsync($"UPDATE dbo.LedgerPostings SET [DocumentId] = NEWID() WHERE [Id] = '{postingId}'");
        (await DriftAsync(maker)).ShouldBe(
            [
                new DriftRow(DriftIdentity.RefundWithoutPosting, posted.Id, "REF/26-27/000001", 50.00m, 0.00m),
                new DriftRow(DriftIdentity.PostingWithoutRefund, postingId, null, 0.00m, 0.00m),
            ],
            ignoreOrder: true);
        await ExecuteAsync($"UPDATE dbo.LedgerPostings SET [DocumentId] = '{posted.Id}' WHERE [Id] = '{postingId}'");
        await ShouldHaveNoDriftAsync(maker);

        // The refund is made to belong to another payer than its payment.
        await ExecuteAsync($"UPDATE dbo.Refunds SET [PayerId] = '{otherPayer.Id}' WHERE [Id] = '{posted.Id}'");
        (await DriftAsync(maker)).ShouldBe([new DriftRow(DriftIdentity.RefundPayerMismatch, posted.Id, "REF/26-27/000001", 50.00m, 0.00m)]);
    }

    private static Uri Refund(Guid id) => new($"/api/v1/refunds/{id}", UriKind.Relative);

    private static Uri Refund(Guid id, string action) => new($"/api/v1/refunds/{id}/{action}", UriKind.Relative);

    private static Uri Payment(Guid id, string action) => new($"/api/v1/payments/{id}/{action}", UriKind.Relative);

    private static Uri CreditNote(Guid id, string action) => new($"/api/v1/credit-notes/{id}/{action}", UriKind.Relative);

    private static async Task<RefundDto> DraftedAsync(HttpClient client, CreateRefundRequest request)
    {
        using var response = await client.PostJsonAsync(Refunds, request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<RefundDto>();
    }

    private static async Task<RefundDto> SubmittedAsync(HttpClient maker, RefundDto draft)
    {
        using var response = await maker.SendWithIfMatchAsync(HttpMethod.Post, Refund(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<RefundDto>();
    }

    private static async Task<RefundDto> ApprovedAsync(HttpClient maker, HttpClient checker, RefundDto draft)
    {
        var waiting = await SubmittedAsync(maker, draft);
        using var response = await checker.SendWithIfMatchAsync(HttpMethod.Post, Refund(draft.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<RefundDto>();
    }

    private static async Task<RefundDto> RefundAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(Refund(id));

        return await response.ReadAsAsync<RefundDto>();
    }

    private static async Task<PaymentDto> RecordedAsync(HttpClient client, PayerDto payer, decimal amount)
    {
        using var response = await client.PostJsonAsync(Payments, new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.Cheque, Reference = "000123", Amount = amount });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PaymentDto>();
    }

    private static async Task<PaymentDto> PaymentAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/payments/{id}", UriKind.Relative));

        return await response.ReadAsAsync<PaymentDto>();
    }

    private static async Task<HttpResponseMessage> AllocateAsync(HttpClient client, PaymentDto payment, Guid billId, decimal amount)
    {
        var current = await PaymentAsync(client, payment.Id);

        return await client.SendWithIfMatchAsync(
            HttpMethod.Post,
            new Uri($"/api/v1/payments/{payment.Id}/allocations", UriKind.Relative),
            new AllocatePaymentRequest { Allocations = [new AllocationRequest { BillId = billId, Amount = amount }] },
            PayersApi.ETag(current.Version));
    }

    private static async Task AllocatedAsync(HttpClient client, PaymentDto payment, Guid billId, decimal amount)
    {
        using var response = await AllocateAsync(client, payment, billId, amount);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    // A draft that credits the whole dressing line of the bill.
    private static async Task<CreditNoteDto> DraftedCreditNoteAsync(HttpClient client, BillDto bill)
    {
        var line = bill.Lines.Single(candidate => candidate.Description == "Dressing");
        using var response = await client.PostJsonAsync(CreditNotes, new CreateCreditNoteRequest { BillId = bill.Id, CreditNoteDate = Today, Reason = "Dressing not done", Lines = [new CreditNoteLineRequest { BillLineId = line.Id, Quantity = line.Quantity }] });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<CreditNoteDto>();
    }

    private static async Task<CreditNoteDto> ApprovedCreditNoteAsync(HttpClient maker, HttpClient checker, CreditNoteDto draft)
    {
        using var submitted = await maker.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        submitted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await submitted.Content.ReadAsStringAsync());
        var waiting = await submitted.ReadAsAsync<CreditNoteDto>();

        using var response = await checker.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<CreditNoteDto>();
    }

    private static async Task<CreditNoteDto> CreditNoteAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/credit-notes/{id}", UriKind.Relative));

        return await response.ReadAsAsync<CreditNoteDto>();
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
        await using var context = ApiContext(SystemIdentity.Instance);

        await context.Database.ExecuteSqlRawAsync(sql);
    }

    private sealed class RefundPostedThenFault(IReceivablesLedger inner) : IReceivablesLedger
    {
        public async Task<Result<LedgerPostingId>> PostAsync(PostingTemplate template, CancellationToken ct)
        {
            var entered = await inner.PostAsync(template, ct);

            return template.Kind == LedgerPostingKind.RefundPosted
                ? throw new InvalidOperationException("Injected: the ledger failed after it entered the posting.")
                : entered;
        }
    }

    private sealed class FaultOnRefundPosted : IDomainEventSubscriber
    {
        public Task HandleAsync(DomainEventOccurrence occurrence, CancellationToken ct)
            => occurrence.Event is RefundPosted
                ? throw new InvalidOperationException("Injected: a subscriber failed after the audit record was written.")
                : Task.CompletedTask;
    }
}
