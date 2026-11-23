using System.Net;
using BillingRecon.Api.Adjustments;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Adjustments;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Adjustments;
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
/// Adjustments end to end, on the pipeline that ships and a real SQL Server (US-25; BR-007, BR-208, BR-501 to BR-503,
/// BR-603 to BR-606, BR-803, BR-804): a draft against a posted bill, a second person's approval that posts it, the
/// bill lowered by its amount. The number, the ledger posting, the cached balances and the audit records are written
/// by one transaction or not at all, and every scenario ends with no drift between the caches and the ledger.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class AdjustmentsApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Uri Adjustments = new("/api/v1/adjustments", UriKind.Relative);
    private static readonly Uri Payments = new("/api/v1/payments", UriKind.Relative);

    // 17 October 2026 in India: fiscal year 2026-27.
    private static readonly DateOnly Today = new(2026, 10, 17);

    // 2 x 60.00 at 5%: 126.00.
    private static readonly BillLineRequest Dressing = BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m);

    // 1 x 500.00 at 18%: 590.00.
    private static readonly BillLineRequest Consultation = BillingApi.FreeTextLine("OPD consultation", 1m, 500m, 18m);

    [Fact]
    public async Task BR502_an_approved_write_off_settles_the_bill_with_its_number_its_ledger_entry_and_no_drift()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);

        // The draft.
        using var created = await clerk.PostJsonAsync(Adjustments, new CreateAdjustmentRequest { BillId = bill.Id, Kind = AdjustmentKind.WriteOff, Amount = 126m, Reason = "Patient cannot pay" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var draft = await created.ReadAsAsync<AdjustmentDto>();
        created.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe($"/api/v1/adjustments/{draft.Id}");
        (draft.Status, draft.Kind, draft.Amount, draft.PayerId, draft.Number).ShouldBe((AdjustmentStatus.Draft, AdjustmentKind.WriteOff, 126.00m, payer.Id, null));
        draft.AdjustmentDate.ShouldBe(Today, "today in India when left out");

        // The maker asks: 202, and nothing financial has happened (BR-502).
        using var submitted = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        submitted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await submitted.Content.ReadAsStringAsync());
        var waiting = await submitted.ReadAsAsync<AdjustmentDto>();
        waiting.Status.ShouldBe(AdjustmentStatus.PendingApproval);
        waiting.Number.ShouldBeNull();
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'AdjustmentPosted'")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries WHERE [DocumentType] = 'Adjustment'")).ShouldBe(0);
        (await BillAsync(clerk, bill.Id)).ShouldBeEquivalentTo(bill);

        // The checker approves the next day: that is the posting (BR-606).
        factory.Clock.Advance(TimeSpan.FromDays(1));
        using var approved = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(draft.Id, "approve"), new ApproveAdjustmentRequest { Comment = "Agreed by the director" }, PayersApi.ETag(waiting.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var posted = await approved.ReadAsAsync<AdjustmentDto>();
        posted.Status.ShouldBe(AdjustmentStatus.Posted);
        posted.Number.ShouldBe("ADJ/26-27/000001");
        posted.FiscalYear.ShouldBe("2026-27");
        posted.PostedAtUtc.ShouldBe(DatabaseApiFactory.InitialInstant.AddDays(1));
        approved.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(posted.Version));

        // The bill: settled, with its total as posted (BR-208, BR-006).
        var settled = await BillAsync(clerk, bill.Id);
        (settled.Status, settled.BalanceDue, settled.Total).ShouldBe((BillStatus.Settled, 0.00m, 126.00m));

        // The ledger: Dr Adjustments / Cr Receivable(bill), dated the day of the adjustment.
        await using (var context = ApiContext(maker))
        {
            var posting = await context.Set<LedgerPosting>().Include(candidate => candidate.Entries).SingleAsync(candidate => candidate.Kind == LedgerPostingKind.AdjustmentPosted);
            posting.DocumentType.ShouldBe(LedgerDocumentType.Adjustment);
            posting.DocumentId.ShouldBe(posted.Id);
            posting.PayerId.ShouldBe(new PayerId(payer.Id));
            posting.BusinessDate.ShouldBe(Today);
            posting.Description.ShouldBe($"Adjustment ADJ/26-27/000001 posted against bill {bill.Number}");
            posting.Entries.OrderBy(entry => entry.LineNo).Select(entry => (entry.Account, entry.Debit, entry.Credit, entry.SubjectId)).ShouldBe(
            [
                (LedgerAccount.Adjustments, Money.Of(126m), Money.Zero, null),
                (LedgerAccount.Receivable, Money.Zero, Money.Of(126m), bill.Id),
            ]);
            (await context.Set<Adjustment>().AsNoTracking().SingleAsync()).PostingId.ShouldBe(posting.Id);
        }

        (await BalanceAsync(maker, payer)).Receivable.ShouldBe(Money.Zero);

        // The audit trail: the adjustment, with its reason, and the bill, in the request of the approval (BR-701).
        var trail = await TrailAsync(clerk, "Adjustment", posted.Id);
        trail.Select(record => record.Action).ShouldBe(["Drafted", "SubmittedForApproval", "Posted"]);
        trail[2].DocumentNumber.ShouldBe("ADJ/26-27/000001");
        trail[2].Reason.ShouldBe("Patient cannot pay");
        trail[2].CorrelationId.ShouldBe(approved.GetCorrelationId());
        (await TrailAsync(clerk, "Bill", bill.Id)).Select(record => record.Action).ShouldBe(["Drafted", "Posted", "CreditApplied"]);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [DocumentType] = 'Adjustment' AND [Status] = 'Approved'")).ShouldBe(1);
        await ShouldHaveNoDriftAsync(maker);

        // Reading it gives what the approval answered; the list finds it by its bill and its kind.
        (await AdjustmentAsync(clerk, posted.Id)).ShouldBe(posted);
        using var listed = await clerk.GetAsync(Adjustments.WithQuery($"billId={bill.Id}&kind=WriteOff&status=Posted"));
        (await listed.ReadAsAsync<PageOf<AdjustmentDto>>()).Items.ShouldHaveSingleItem().ShouldBe(posted);

        // Posted: frozen for everybody (BR-006).
        using var late = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(draft.Id, "approve"), body: null, PayersApi.ETag(posted.Version));
        using var lateEdit = await clerk.SendWithIfMatchAsync(HttpMethod.Put, Adjustment(draft.Id), Update(AdjustmentKind.Discount, 1m), PayersApi.ETag(posted.Version));
        await late.ShouldBeProblemAsync(HttpStatusCode.Conflict, AdjustmentsErrors.AdjustmentAlreadyPostedCode);
        await lateEdit.ShouldBeProblemAsync(HttpStatusCode.Conflict, AdjustmentsErrors.AdjustmentAlreadyPostedCode);
    }

    [Fact]
    public async Task BR501_a_discount_leaves_the_bill_partially_paid_and_a_rounding_of_paise_closes_what_a_payment_left()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Consultation);

        var discount = await ApprovedAsync(clerk, supervisor, await DraftedAsync(clerk, bill, AdjustmentKind.Discount, 90m));
        discount.Number.ShouldBe("ADJ/26-27/000001");
        var partlyPaid = await BillAsync(clerk, bill.Id);
        (partlyPaid.Status, partlyPaid.BalanceDue).ShouldBe((BillStatus.PartiallyPaid, 500.00m));
        await ShouldHaveNoDriftAsync(maker);

        // The payer pays 499.60; forty paise are left, and a rounding closes the bill. It, too, needs the checker.
        await PaidAsync(clerk, payer, bill, 499.60m);
        var rounding = await DraftedAsync(clerk, bill, AdjustmentKind.Rounding, 0.40m);
        var waiting = await SubmittedAsync(clerk, rounding);
        waiting.Status.ShouldBe(AdjustmentStatus.PendingApproval, "BR-502 has no exception for a rounding");
        (await BillAsync(clerk, bill.Id)).BalanceDue.ShouldBe(0.40m);
        using var approved = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(rounding.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        (await approved.ReadAsAsync<AdjustmentDto>()).Number.ShouldBe("ADJ/26-27/000002");

        var settled = await BillAsync(clerk, bill.Id);
        (settled.Status, settled.BalanceDue).ShouldBe((BillStatus.Settled, 0.00m));
        var balance = await BalanceAsync(maker, payer);
        (balance.Receivable, balance.UnappliedCash).ShouldBe((Money.Zero, Money.Zero));
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR501_what_cannot_be_drafted_or_submitted_is_refused_and_writes_nothing()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var maker = await NewApiTenantAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);
        var unposted = await clerk.CreateBillAsync(NewBill(patient, payer, Dressing));

        using var unknownBill = await clerk.PostJsonAsync(Adjustments, Create(Guid.NewGuid(), AdjustmentKind.Discount, 1m));
        using var draftBill = await clerk.PostJsonAsync(Adjustments, Create(unposted.Id, AdjustmentKind.Discount, 1m));
        using var rupee = await clerk.PostJsonAsync(Adjustments, Create(bill.Id, AdjustmentKind.Rounding, 1.00m));
        using var nothing = await clerk.PostJsonAsync(Adjustments, Create(bill.Id, AdjustmentKind.Discount, 0m));
        using var threeDecimals = await clerk.PostJsonAsync(Adjustments, Create(bill.Id, AdjustmentKind.Discount, 1.005m));
        using var noKind = await clerk.PostJsonAsync(Adjustments, new CreateAdjustmentRequest { BillId = bill.Id, Amount = 1m, Reason = "x" });
        using var noReason = await clerk.PostJsonAsync(Adjustments, new CreateAdjustmentRequest { BillId = bill.Id, Kind = AdjustmentKind.Discount, Amount = 1m });
        using var beforeBill = await clerk.PostJsonAsync(Adjustments, new CreateAdjustmentRequest { BillId = bill.Id, Kind = AdjustmentKind.Discount, Amount = 1m, Reason = "x", AdjustmentDate = Today.AddDays(-1) });
        using var future = await clerk.PostJsonAsync(Adjustments, new CreateAdjustmentRequest { BillId = bill.Id, Kind = AdjustmentKind.Discount, Amount = 1m, Reason = "x", AdjustmentDate = Today.AddDays(1) });
        await unknownBill.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        await draftBill.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.BillNotPostedCode);
        await rupee.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, AdjustmentsErrors.AdjustmentRoundingTooLargeCode);
        await nothing.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await threeDecimals.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await noKind.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await noReason.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await beforeBill.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, AdjustmentsErrors.AdjustmentDateBeforeBillDateCode);
        await future.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, AdjustmentsErrors.AdjustmentDateInFutureCode);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Adjustments")).ShouldBe(0);

        // More than the bill is owed can be drafted, and is refused when the maker asks (BR-501).
        var tooMuch = await DraftedAsync(clerk, bill, AdjustmentKind.WriteOff, 126.01m);
        using var refused = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(tooMuch.Id, "post"), body: null, PayersApi.ETag(tooMuch.Version));
        await refused.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, AdjustmentsErrors.AdjustmentExceedsBalanceDueCode);

        // The draft is corrected under its version (BR-804), then voided, and never changes again.
        using var withoutVersion = await clerk.SendWithIfMatchAsync(HttpMethod.Put, Adjustment(tooMuch.Id), Update(AdjustmentKind.WriteOff, 126m), ifMatch: null);
        await withoutVersion.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        using var corrected = await clerk.SendWithIfMatchAsync(HttpMethod.Put, Adjustment(tooMuch.Id), Update(AdjustmentKind.WriteOff, 126m), PayersApi.ETag(tooMuch.Version));
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK, await corrected.Content.ReadAsStringAsync());
        var fitting = await corrected.ReadAsAsync<AdjustmentDto>();
        fitting.Amount.ShouldBe(126.00m);
        using var stale = await clerk.SendWithIfMatchAsync(HttpMethod.Put, Adjustment(tooMuch.Id), Update(AdjustmentKind.WriteOff, 1m), PayersApi.ETag(tooMuch.Version));
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
        using var voided = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(tooMuch.Id, "void"), new VoidAdjustmentRequest { Reason = "Raised on the wrong bill" }, PayersApi.ETag(fitting.Version));
        voided.StatusCode.ShouldBe(HttpStatusCode.OK, await voided.Content.ReadAsStringAsync());
        var gone = await voided.ReadAsAsync<AdjustmentDto>();
        (gone.Status, gone.VoidReason).ShouldBe((AdjustmentStatus.Voided, "Raised on the wrong bill"));
        using var afterVoid = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(tooMuch.Id, "post"), body: null, PayersApi.ETag(gone.Version));
        await afterVoid.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, AdjustmentsErrors.AdjustmentNotDraftCode);
        (await TrailAsync(clerk, "Adjustment", tooMuch.Id)).Select(record => record.Action).ShouldBe(["Drafted", "Updated", "Voided"]);
        (await BillAsync(clerk, bill.Id)).ShouldBeEquivalentTo(bill);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'AdjustmentPosted'")).ShouldBe(0);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR603_BR605_the_maker_cannot_decide_and_a_rejection_with_its_reason_makes_the_adjustment_a_draft_again()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");

        // The maker with a checker's role too is refused as the maker (BR-603); as a clerk, for the role (ADR 0032).
        using var makerAsChecker = factory.CreateClientFor(maker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);
        var draft = await DraftedAsync(clerk, bill, AdjustmentKind.Discount, 26m);

        // A draft has nothing to decide.
        using var early = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(draft.Id, "approve"), body: null, PayersApi.ETag(draft.Version));
        await early.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, AdjustmentsErrors.AdjustmentNotPendingApprovalCode);

        var waiting = await SubmittedAsync(clerk, draft);

        // The maker can neither approve nor reject; nobody can correct, void or ask again while it waits.
        using var ownApproval = await makerAsChecker.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(draft.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));
        using var ownRejection = await makerAsChecker.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(draft.Id, "reject"), new RejectAdjustmentRequest { Comment = "Mine" }, PayersApi.ETag(waiting.Version));
        using var asClerk = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(draft.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));
        await asClerk.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        using var edit = await clerk.SendWithIfMatchAsync(HttpMethod.Put, Adjustment(draft.Id), Update(AdjustmentKind.Discount, 1m), PayersApi.ETag(waiting.Version));
        using var voiding = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(draft.Id, "void"), new VoidAdjustmentRequest { Reason = "Changed my mind" }, PayersApi.ETag(waiting.Version));
        using var again = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(draft.Id, "post"), body: null, PayersApi.ETag(waiting.Version));
        using var noReason = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(draft.Id, "reject"), new RejectAdjustmentRequest(), PayersApi.ETag(waiting.Version));
        await ownApproval.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        await ownRejection.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        await edit.ShouldBeProblemAsync(HttpStatusCode.Conflict, AdjustmentsErrors.AdjustmentPendingApprovalCode);
        await voiding.ShouldBeProblemAsync(HttpStatusCode.Conflict, AdjustmentsErrors.AdjustmentPendingApprovalCode);
        await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, AdjustmentsErrors.AdjustmentPendingApprovalCode);
        await noReason.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");

        // The checker rejects: a draft again, with nothing financial (BR-605).
        using var rejected = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(draft.Id, "reject"), new RejectAdjustmentRequest { Comment = "Ten is the staff discount" }, PayersApi.ETag(waiting.Version));
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        var returned = await rejected.ReadAsAsync<AdjustmentDto>();
        (returned.Status, returned.Number).ShouldBe((AdjustmentStatus.Draft, null));
        (await BillAsync(clerk, bill.Id)).ShouldBeEquivalentTo(bill);

        // The maker corrects it and asks again, which opens a second request; this one is approved.
        using var corrected = await clerk.SendWithIfMatchAsync(HttpMethod.Put, Adjustment(draft.Id), Update(AdjustmentKind.Discount, 10m), PayersApi.ETag(returned.Version));
        var posted = await ApprovedAsync(clerk, supervisor, await corrected.ReadAsAsync<AdjustmentDto>());
        posted.Number.ShouldBe("ADJ/26-27/000001", "the rejected attempt took no number (BR-801)");
        (await BillAsync(clerk, bill.Id)).BalanceDue.ShouldBe(116.00m);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [DocumentType] = 'Adjustment'")).ShouldBe(2);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR501_an_adjustment_that_no_longer_fits_when_the_checker_decides_is_refused_and_takes_no_number()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);

        // The write-off of the whole bill fitted when the maker asked; then the payer paid 100.
        var waiting = await SubmittedAsync(clerk, await DraftedAsync(clerk, bill, AdjustmentKind.WriteOff, 126m));
        await PaidAsync(clerk, payer, bill, 100m);

        using var refused = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));

        await refused.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, AdjustmentsErrors.AdjustmentExceedsBalanceDueCode);
        (await AdjustmentAsync(clerk, waiting.Id)).ShouldBe(waiting);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries WHERE [DocumentType] = 'Adjustment'")).ShouldBe(0);
        (await BillAsync(clerk, bill.Id)).BalanceDue.ShouldBe(26.00m);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR804_an_adjustment_and_a_payment_for_the_last_rupees_of_one_bill_at_once_give_exactly_one_success()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "Cashier", "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);
        var waiting = await SubmittedAsync(clerk, await DraftedAsync(clerk, bill, AdjustmentKind.WriteOff, 126m));
        var payment = await RecordedAsync(clerk, payer, 126m);

        var responses = await Task.WhenAll(
            Task.Run(() => supervisor.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version))),
            Task.Run(() => AllocateAsync(clerk, payment, bill, 126m)));

        try
        {
            // The loser read the bill before the winner committed (409) or after (422). Both are correct; a second
            // success would settle the bill twice.
            responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
            responses.Single(response => response.StatusCode != HttpStatusCode.OK).StatusCode
                .ShouldBeOneOf(HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        var settled = await BillAsync(clerk, bill.Id);
        (settled.Status, settled.BalanceDue).ShouldBe((BillStatus.Settled, 0.00m));
        var adjusted = await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Adjustments WHERE [Status] = 'Posted'");
        var allocated = await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.PaymentAllocations");
        (adjusted + allocated).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries WHERE [DocumentType] = 'Adjustment' AND [NextNumber] > 1")).ShouldBe(adjusted, "a lost approval gave its number back (BR-801)");
        await ShouldHaveNoDriftAsync(maker);
    }

    [Theory]
    [InlineData("the ledger, after the posting is entered")]
    [InlineData("the audit trail, after the record is written")]
    public async Task US25_an_approval_that_fails_at_any_step_leaves_no_number_no_ledger_and_no_audit_rows_and_both_documents_as_they_were(string step)
    {
        using var factory = new DatabaseApiFactory(SqlServer, configure: services =>
        {
            switch (step)
            {
                case "the ledger, after the posting is entered":
                    services.AddScoped<ReceivablesLedger>();
                    services.AddScoped<IReceivablesLedger>(provider => new AdjustmentThenFault(provider.GetRequiredService<ReceivablesLedger>()));
                    break;
                default:
                    // Registered after the audit subscriber, so it runs when the audit records are in the unit of work.
                    services.AddScoped<IDomainEventSubscriber, FaultOnAdjustmentPosted>();
                    break;
            }
        });
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);
        var waiting = await SubmittedAsync(clerk, await DraftedAsync(clerk, bill, AdjustmentKind.WriteOff, 126m));

        using var failed = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));

        await failed.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
        (await AdjustmentAsync(clerk, waiting.Id)).ShouldBe(waiting);
        (await BillAsync(clerk, bill.Id)).ShouldBeEquivalentTo(bill);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries WHERE [DocumentType] = 'Adjustment'")).ShouldBe(0, "the number went back with the rest (BR-801)");
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'AdjustmentPosted'")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.AuditRecords WHERE [Action] IN ('Posted', 'CreditApplied', 'Approved') AND [EntityType] <> 'Bill'")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.AuditRecords WHERE [Action] = 'CreditApplied'")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [Status] = 'Pending'")).ShouldBe(1);
        (await BalanceAsync(maker, payer)).Receivable.ShouldBe(Money.Of(126m));
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR803_a_replayed_approval_returns_the_same_response_and_posts_once()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);
        var waiting = await SubmittedAsync(clerk, await DraftedAsync(clerk, bill, AdjustmentKind.Discount, 26m));
        var key = Guid.NewGuid();

        using var original = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version), key);
        using var replay = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version), key);

        original.StatusCode.ShouldBe(HttpStatusCode.OK, await original.Content.ReadAsStringAsync());
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).ShouldBe(await original.Content.ReadAsStringAsync());
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'AdjustmentPosted'")).ShouldBe(1);
        (await BillAsync(clerk, bill.Id)).BalanceDue.ShouldBe(100.00m);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR002_an_adjustment_and_a_bill_of_another_tenant_can_neither_be_read_nor_used()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var maker = await NewApiTenantAsync();
        var stranger = await NewApiTenantAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var outsider = factory.CreateClientFor(stranger, "TenantAdmin");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);
        var draft = await DraftedAsync(clerk, bill, AdjustmentKind.Discount, 26m);

        using var read = await outsider.GetAsync(Adjustment(draft.Id));
        using var post = await outsider.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        using var against = await outsider.PostJsonAsync(Adjustments, Create(bill.Id, AdjustmentKind.Discount, 1m));
        using var list = await outsider.GetAsync(Adjustments);

        await read.ShouldBeProblemAsync(HttpStatusCode.NotFound, AdjustmentsErrors.AdjustmentNotFoundCode);
        await post.ShouldBeProblemAsync(HttpStatusCode.NotFound, AdjustmentsErrors.AdjustmentNotFoundCode);
        await against.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        (await list.ReadAsAsync<PageOf<AdjustmentDto>>()).Items.ShouldBeEmpty();
        (await AdjustmentAsync(clerk, draft.Id)).ShouldBe(draft);
    }

    [Fact]
    public async Task BR404_an_adjustment_that_lost_its_posting_or_names_the_wrong_payer_is_reported()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var (_, otherPayer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);
        var posted = await ApprovedAsync(clerk, supervisor, await DraftedAsync(clerk, bill, AdjustmentKind.Discount, 26m));
        await ShouldHaveNoDriftAsync(maker);

        // The adjustment is given another payer behind the application (BR-501).
        await ExecuteAsync($"UPDATE dbo.Adjustments SET [PayerId] = '{otherPayer.Id}' WHERE [Id] = '{posted.Id}'");
        (await DriftAsync(maker)).ShouldBe([new DriftRow(DriftIdentity.AdjustmentPayerMismatch, posted.Id, "ADJ/26-27/000001", 26.00m, 0.00m)]);
        await ExecuteAsync($"UPDATE dbo.Adjustments SET [PayerId] = '{payer.Id}' WHERE [Id] = '{posted.Id}'");
        await ShouldHaveNoDriftAsync(maker);

        // The posting is pointed at a document nobody has: the adjustment has no posting, and the posting no adjustment.
        await ExecuteAsync($"ALTER TABLE dbo.LedgerPostings NOCHECK CONSTRAINT ALL; UPDATE dbo.LedgerPostings SET [DocumentId] = NEWID() WHERE [Kind] = 'AdjustmentPosted'; ALTER TABLE dbo.LedgerPostings CHECK CONSTRAINT ALL;");
        var drift = await DriftAsync(maker);
        drift.Select(row => row.Identity).ShouldBe([DriftIdentity.AdjustmentWithoutPosting, DriftIdentity.PostingWithoutAdjustment], ignoreOrder: true);
        drift.Single(row => row.Identity == DriftIdentity.AdjustmentWithoutPosting).SubjectId.ShouldBe(posted.Id);
    }

    private static Uri Adjustment(Guid id) => new($"/api/v1/adjustments/{id}", UriKind.Relative);

    private static Uri Adjustment(Guid id, string action) => new($"/api/v1/adjustments/{id}/{action}", UriKind.Relative);

    private static CreateAdjustmentRequest Create(Guid billId, AdjustmentKind kind, decimal amount)
        => new() { BillId = billId, AdjustmentDate = Today, Kind = kind, Amount = amount, Reason = "Agreed with the patient" };

    private static UpdateAdjustmentRequest Update(AdjustmentKind kind, decimal amount)
        => new() { AdjustmentDate = Today, Kind = kind, Amount = amount, Reason = "Agreed with the patient" };

    private static async Task<AdjustmentDto> DraftedAsync(HttpClient client, BillDto bill, AdjustmentKind kind, decimal amount)
    {
        using var response = await client.PostJsonAsync(Adjustments, Create(bill.Id, kind, amount));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<AdjustmentDto>();
    }

    private static async Task<AdjustmentDto> SubmittedAsync(HttpClient maker, AdjustmentDto draft)
    {
        using var response = await maker.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<AdjustmentDto>();
    }

    private static async Task<AdjustmentDto> ApprovedAsync(HttpClient maker, HttpClient checker, AdjustmentDto draft)
    {
        var waiting = await SubmittedAsync(maker, draft);
        using var response = await checker.SendWithIfMatchAsync(HttpMethod.Post, Adjustment(draft.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<AdjustmentDto>();
    }

    private static async Task<AdjustmentDto> AdjustmentAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(Adjustment(id));

        return await response.ReadAsAsync<AdjustmentDto>();
    }

    private static async Task<PaymentDto> RecordedAsync(HttpClient client, PayerDto payer, decimal amount)
    {
        using var response = await client.PostJsonAsync(Payments, new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.UPI, Reference = "412345678901", Amount = amount });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PaymentDto>();
    }

    private static Task<HttpResponseMessage> AllocateAsync(HttpClient client, PaymentDto payment, BillDto bill, decimal amount)
        => client.SendWithIfMatchAsync(
            HttpMethod.Post,
            new Uri($"/api/v1/payments/{payment.Id}/allocations", UriKind.Relative),
            new AllocatePaymentRequest { Allocations = [new AllocationRequest { BillId = bill.Id, Amount = amount }] },
            PayersApi.ETag(payment.Version));

    // Records a payment of the payer and allocates all of it to the bill.
    private static async Task PaidAsync(HttpClient client, PayerDto payer, BillDto bill, decimal amount)
    {
        var payment = await RecordedAsync(client, payer, amount);
        using var allocated = await AllocateAsync(client, payment, bill, amount);

        allocated.StatusCode.ShouldBe(HttpStatusCode.OK, await allocated.Content.ReadAsStringAsync());
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

    private sealed class AdjustmentThenFault(IReceivablesLedger inner) : IReceivablesLedger
    {
        public async Task<Result<LedgerPostingId>> PostAsync(PostingTemplate template, CancellationToken ct)
        {
            var entered = await inner.PostAsync(template, ct);

            return template.Kind == LedgerPostingKind.AdjustmentPosted
                ? throw new InvalidOperationException("Injected: the ledger failed after it entered the posting.")
                : entered;
        }
    }

    private sealed class FaultOnAdjustmentPosted : IDomainEventSubscriber
    {
        public Task HandleAsync(DomainEventOccurrence occurrence, CancellationToken ct)
            => occurrence.Event is AdjustmentPosted
                ? throw new InvalidOperationException("Injected: a subscriber failed after the audit record was written.")
                : Task.CompletedTask;
    }
}
