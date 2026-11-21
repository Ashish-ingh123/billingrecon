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
/// Credit notes end to end, on the pipeline that ships and a real SQL Server (US-24, US-25; BR-007, BR-208 to BR-214,
/// BR-603 to BR-606, BR-803, BR-804): a draft against a posted bill, a second person's approval that posts it, the
/// credit applied to its bill as far as the bill is owed, the rest left available and applied to another bill. The
/// number, the two ledger postings, the cached balances and the audit records are written by one transaction or not
/// at all, and every scenario ends with no drift between the caches and the ledger.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class CreditNotesApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Uri CreditNotes = new("/api/v1/credit-notes", UriKind.Relative);
    private static readonly Uri Payments = new("/api/v1/payments", UriKind.Relative);

    // 17 October 2026 in India: fiscal year 2026-27.
    private static readonly DateOnly Today = new(2026, 10, 17);

    // 2 x 60.00 at 5%: 126.00.
    private static readonly BillLineRequest Dressing = BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m);

    // 1 x 500.00 at 18%: 590.00.
    private static readonly BillLineRequest Consultation = BillingApi.FreeTextLine("OPD consultation", 1m, 500m, 18m);

    [Fact]
    public async Task US24_a_credit_note_smaller_than_the_balance_waits_for_a_second_person_whose_approval_posts_it_and_applies_it_to_the_bill()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Consultation, Dressing);
        var dressing = bill.Lines.Single(line => line.Description == "Dressing");

        // The draft: the line names the line of the bill and takes its description, price and tax rate from it.
        var draft = await DraftedAsync(clerk, bill, new CreditNoteLineRequest { BillLineId = dressing.Id, Quantity = 2m });
        draft.Status.ShouldBe(CreditNoteStatus.Draft);
        draft.PayerId.ShouldBe(payer.Id);
        draft.PatientId.ShouldBe(patient.Id);
        draft.Number.ShouldBeNull();
        draft.Total.ShouldBe(126.00m);
        draft.AvailableAmount.ShouldBe(126.00m);
        var line = draft.Lines.ShouldHaveSingleItem();
        (line.BillLineId, line.Description, line.UnitPrice, line.TaxRatePercent, line.LineTotal).ShouldBe((dressing.Id, "Dressing", 60m, 5m, 126.00m));

        // The maker asks: 202, and nothing financial has happened (BR-212).
        using var submitted = await clerk.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        submitted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await submitted.Content.ReadAsStringAsync());
        var waiting = await submitted.ReadAsAsync<CreditNoteDto>();
        waiting.Status.ShouldBe(CreditNoteStatus.PendingApproval);
        waiting.Number.ShouldBeNull();
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] IN ('CreditNotePosted', 'CreditApplied')")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries WHERE [DocumentType] = 'CreditNote'")).ShouldBe(0);
        (await BillAsync(clerk, bill.Id)).ShouldBeEquivalentTo(bill);
        await ShouldHaveNoDriftAsync(maker);

        // The checker approves the next day: that is the posting (BR-606).
        factory.Clock.Advance(TimeSpan.FromDays(1));
        using var approved = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "approve"), new ApproveCreditNoteRequest { Comment = "Dressing was not done" }, PayersApi.ETag(waiting.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var posted = await approved.ReadAsAsync<CreditNoteDto>();
        posted.Status.ShouldBe(CreditNoteStatus.Posted);
        posted.Number.ShouldBe("CN/26-27/000001");
        posted.FiscalYear.ShouldBe("2026-27");
        posted.PostedAtUtc.ShouldBe(DatabaseApiFactory.InitialInstant.AddDays(1));
        posted.AvailableAmount.ShouldBe(0.00m, "all of it went to the bill");
        var application = posted.Applications.ShouldHaveSingleItem();
        (application.BillId, application.Amount, application.Status).ShouldBe((bill.Id, 126.00m, CreditNoteAllocationStatus.Active));
        application.AllocatedByUserId.ShouldBe(checker.UserId!.Value.Value);
        approved.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(posted.Version));

        // The bill: owed less, partially paid (BR-208).
        var credited = await BillAsync(clerk, bill.Id);
        credited.Status.ShouldBe(BillStatus.PartiallyPaid);
        credited.BalanceDue.ShouldBe(590.00m);
        credited.Total.ShouldBe(716.00m, "a posted bill keeps its total (BR-006)");

        // The ledger: Dr Revenue / Cr CreditAvailable(credit note), then Dr CreditAvailable / Cr Receivable(bill).
        await using (var context = ApiContext(maker))
        {
            var postings = await context.Set<LedgerPosting>().Include(candidate => candidate.Entries)
                .Where(candidate => candidate.Kind == LedgerPostingKind.CreditNotePosted || candidate.Kind == LedgerPostingKind.CreditApplied)
                .ToListAsync();
            var note = postings.Single(candidate => candidate.Kind == LedgerPostingKind.CreditNotePosted);
            note.DocumentType.ShouldBe(LedgerDocumentType.CreditNote);
            note.DocumentId.ShouldBe(posted.Id);
            note.BusinessDate.ShouldBe(Today, "the date of the credit note");
            note.Description.ShouldBe("Credit note CN/26-27/000001 posted");
            note.Entries.OrderBy(entry => entry.LineNo).Select(entry => (entry.Account, entry.Debit, entry.Credit, entry.SubjectId)).ShouldBe(
            [
                (LedgerAccount.Revenue, Money.Of(126m), Money.Zero, null),
                (LedgerAccount.CreditAvailable, Money.Zero, Money.Of(126m), posted.Id),
            ]);
            var applied = postings.Single(candidate => candidate.Kind == LedgerPostingKind.CreditApplied);
            applied.DocumentType.ShouldBe(LedgerDocumentType.CreditNoteAllocation);
            applied.DocumentId.ShouldBe(application.Id);
            applied.BusinessDate.ShouldBe(Today.AddDays(1), "the day of the application");
            applied.Description.ShouldBe($"Credit note CN/26-27/000001 applied to bill {bill.Number}");
            applied.Entries.OrderBy(entry => entry.LineNo).Select(entry => (entry.Account, entry.Debit, entry.Credit, entry.SubjectId)).ShouldBe(
            [
                (LedgerAccount.CreditAvailable, Money.Of(126m), Money.Zero, posted.Id),
                (LedgerAccount.Receivable, Money.Zero, Money.Of(126m), bill.Id),
            ]);
            (await context.Set<CreditNoteAllocation>().AsNoTracking().SingleAsync()).PostingId.ShouldBe(applied.Id);
        }

        // The caches of the payer.
        var balance = await BalanceAsync(maker, payer);
        balance.Receivable.ShouldBe(Money.Of(590m));
        balance.CreditAvailable.ShouldBe(Money.Zero);

        // The audit trail: the credit note, its request and the bill, in the request of the approval (BR-701).
        var trail = await TrailAsync(clerk, "CreditNote", posted.Id);
        trail.Select(record => record.Action).ShouldBe(["Drafted", "SubmittedForApproval", "Posted", "Applied"], ignoreOrder: true);
        trail.Single(record => record.Action == "Posted").DocumentNumber.ShouldBe("CN/26-27/000001");
        trail.Single(record => record.Action == "Posted").CorrelationId.ShouldBe(approved.GetCorrelationId());
        (await TrailAsync(clerk, "Bill", bill.Id)).Select(record => record.Action).ShouldBe(["Drafted", "Posted", "CreditApplied"]);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [DocumentType] = 'CreditNote' AND [Status] = 'Approved'")).ShouldBe(1);
        await ShouldHaveNoDriftAsync(maker);

        // Reading it gives what the approval answered; the list finds it by its bill.
        (await CreditNoteAsync(clerk, posted.Id)).ShouldBeEquivalentTo(posted);
        using var listed = await clerk.GetAsync(CreditNotes.WithQuery($"billId={bill.Id}&status=Posted"));
        (await listed.ReadAsAsync<PageOf<CreditNoteSummaryDto>>()).Items.ShouldHaveSingleItem().Number.ShouldBe("CN/26-27/000001");
    }

    [Fact]
    public async Task US24_a_credit_note_larger_than_the_balance_due_settles_the_bill_and_leaves_the_rest_available_for_another_bill()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);
        var other = await PostedBillAsync(clerk, patient, payer, Consultation);

        // 100 of the 126 are paid, so 26 are owed when the whole bill is credited.
        await PaidAsync(clerk, payer, bill, 100m);
        var posted = await ApprovedAsync(clerk, supervisor, await DraftedAsync(clerk, bill, WholeLine(bill, "Dressing")));

        posted.Total.ShouldBe(126.00m);
        posted.AvailableAmount.ShouldBe(100.00m, "min(Total, BalanceDue) went to the bill (BR-212)");
        posted.Applications.ShouldHaveSingleItem().Amount.ShouldBe(26.00m);
        var settled = await BillAsync(clerk, bill.Id);
        (settled.Status, settled.BalanceDue).ShouldBe((BillStatus.Settled, 0.00m));
        var balance = await BalanceAsync(maker, payer);
        balance.Receivable.ShouldBe(Money.Of(590m));
        balance.CreditAvailable.ShouldBe(Money.Of(100m));
        await ShouldHaveNoDriftAsync(maker);

        // A minute later the rest is applied to another bill of the payer, by the clerk alone (BR-213).
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        using var response = await ApplyAsync(clerk, posted, (other.Id, 60m));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var applied = await response.ReadAsAsync<CreditNoteDto>();
        applied.AvailableAmount.ShouldBe(40.00m);
        applied.Applications.Select(application => (application.BillId, application.Amount)).ShouldBe([(bill.Id, 26.00m), (other.Id, 60.00m)]);
        applied.Version.ShouldNotBe(posted.Version);
        var partlyPaid = await BillAsync(clerk, other.Id);
        (partlyPaid.Status, partlyPaid.BalanceDue).ShouldBe((BillStatus.PartiallyPaid, 530.00m));
        balance = await BalanceAsync(maker, payer);
        balance.Receivable.ShouldBe(Money.Of(530m));
        balance.CreditAvailable.ShouldBe(Money.Of(40m));
        (await TrailAsync(clerk, "CreditNote", posted.Id)).Count(record => record.Action == "Applied").ShouldBe(2);
        await ShouldHaveNoDriftAsync(maker);

        // More than is left, a bill of another payer, and a bill that is not posted are refused, and nothing changes.
        var (strangerPatient, stranger) = await clerk.NewBillPartiesAsync();
        var strangersBill = await PostedBillAsync(clerk, strangerPatient, stranger, Dressing);
        var unposted = await clerk.CreateBillAsync(NewBill(patient, payer, Dressing));
        using var tooMuch = await ApplyAsync(clerk, applied, (other.Id, 40.01m));
        using var mixed = await ApplyAsync(clerk, applied, (other.Id, 10m), (strangersBill.Id, 10m));
        using var toDraft = await ApplyAsync(clerk, applied, (unposted.Id, 10m));
        using var stale = await ApplyAsync(clerk, posted, (other.Id, 10m));
        await tooMuch.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditExceedsAvailableCode);
        await mixed.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.AllocationPayerMismatchCode);
        await toDraft.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.BillNotPostedCode);
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
        (await CreditNoteAsync(clerk, posted.Id)).ShouldBeEquivalentTo(applied);
        (await BillAsync(clerk, other.Id)).ShouldBeEquivalentTo(partlyPaid);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task US24_a_credit_note_equal_to_the_balance_due_settles_the_bill_and_leaves_nothing_available()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);

        var posted = await ApprovedAsync(clerk, supervisor, await DraftedAsync(clerk, bill, WholeLine(bill, "Dressing")));

        posted.AvailableAmount.ShouldBe(0.00m);
        var settled = await BillAsync(clerk, bill.Id);
        (settled.Status, settled.BalanceDue).ShouldBe((BillStatus.Settled, 0.00m));
        var balance = await BalanceAsync(maker, payer);
        (balance.Receivable, balance.CreditAvailable).ShouldBe((Money.Zero, Money.Zero));
        await ShouldHaveNoDriftAsync(maker);

        // Nothing is left to apply.
        using var nothingLeft = await ApplyAsync(clerk, posted, (bill.Id, 1m));
        await nothingLeft.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditExceedsAvailableCode);
    }

    [Fact]
    public async Task BR210_BR211_the_credits_of_a_bill_and_of_each_of_its_lines_never_exceed_them()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Consultation, Dressing);
        var dressing = bill.Lines.Single(line => line.Description == "Dressing");

        // One unit of the dressing is credited and posted: 63 of the line's 126.
        await ApprovedAsync(clerk, supervisor, await DraftedAsync(clerk, bill, new CreditNoteLineRequest { BillLineId = dressing.Id, Quantity = 1m }));

        // Two more units would take the line to 189: refused when the maker asks (BR-211).
        var overLine = await DraftedAsync(clerk, bill, new CreditNoteLineRequest { BillLineId = dressing.Id, Quantity = 2m });
        using var lineRefused = await clerk.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(overLine.Id, "post"), body: null, PayersApi.ETag(overLine.Version));
        await lineRefused.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditNoteLineExceedsBillLineCode);

        // A free-text credit of 700 with the 63 would take the bill of 716 to 763 (BR-210).
        var overBill = await DraftedAsync(clerk, bill, new CreditNoteLineRequest { Description = "Goodwill", Quantity = 1m, UnitPrice = 700m, TaxRatePercent = 0m });
        using var billRefused = await clerk.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(overBill.Id, "post"), body: null, PayersApi.ETag(overBill.Version));
        await billRefused.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditNoteExceedsBillTotalCode);

        // Exactly what is left of the line, and exactly what is left of the bill, are accepted.
        var restOfLine = await ApprovedAsync(clerk, supervisor, await DraftedAsync(clerk, bill, new CreditNoteLineRequest { BillLineId = dressing.Id, Quantity = 1m }));
        restOfLine.Number.ShouldBe("CN/26-27/000002");
        var restOfBill = await ApprovedAsync(clerk, supervisor, await DraftedAsync(clerk, bill, new CreditNoteLineRequest { Description = "Goodwill", Quantity = 1m, UnitPrice = 590m, TaxRatePercent = 0m }));
        restOfBill.Number.ShouldBe("CN/26-27/000003");
        var settled = await BillAsync(clerk, bill.Id);
        (settled.Status, settled.BalanceDue).ShouldBe((BillStatus.Settled, 0.00m));

        // The two refused drafts are drafts still, and took no number.
        (await CreditNoteAsync(clerk, overLine.Id)).Status.ShouldBe(CreditNoteStatus.Draft);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.CreditNotes WHERE [Number] IS NOT NULL")).ShouldBe(3);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR603_BR605_the_maker_cannot_decide_and_a_rejection_with_its_reason_makes_the_credit_note_a_draft_again()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);
        var draft = await DraftedAsync(clerk, bill, WholeLine(bill, "Dressing"));

        // A draft has nothing to decide.
        using var early = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "approve"), body: null, PayersApi.ETag(draft.Version));
        await early.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditNoteNotPendingApprovalCode);

        var waiting = await SubmittedAsync(clerk, draft);

        // The maker can neither approve nor reject; nobody can edit, void or ask again while it waits.
        using var ownApproval = await clerk.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));
        using var ownRejection = await clerk.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "reject"), new RejectCreditNoteRequest { Comment = "Mine" }, PayersApi.ETag(waiting.Version));
        using var edit = await clerk.SendWithIfMatchAsync(HttpMethod.Put, CreditNote(draft.Id, "lines"), new ReplaceCreditNoteLinesRequest { Lines = [] }, PayersApi.ETag(waiting.Version));
        using var voiding = await clerk.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "void"), new VoidCreditNoteRequest { Reason = "Changed my mind" }, PayersApi.ETag(waiting.Version));
        using var again = await clerk.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "post"), body: null, PayersApi.ETag(waiting.Version));
        using var noReason = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "reject"), new RejectCreditNoteRequest(), PayersApi.ETag(waiting.Version));
        await ownApproval.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        await ownRejection.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        await edit.ShouldBeProblemAsync(HttpStatusCode.Conflict, BillingErrors.CreditNotePendingApprovalCode);
        await voiding.ShouldBeProblemAsync(HttpStatusCode.Conflict, BillingErrors.CreditNotePendingApprovalCode);
        await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, BillingErrors.CreditNotePendingApprovalCode);
        await noReason.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");

        // The checker rejects: a draft again, with nothing financial (BR-605).
        using var rejected = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "reject"), new RejectCreditNoteRequest { Comment = "Only one dressing was wrong" }, PayersApi.ETag(waiting.Version));
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        var returned = await rejected.ReadAsAsync<CreditNoteDto>();
        returned.Status.ShouldBe(CreditNoteStatus.Draft);
        returned.Number.ShouldBeNull();
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] IN ('CreditNotePosted', 'CreditApplied')")).ShouldBe(0);
        (await BillAsync(clerk, bill.Id)).ShouldBeEquivalentTo(bill);

        // The maker corrects it and asks again, which opens a second request; this one is approved.
        var dressing = bill.Lines.Single();
        using var corrected = await clerk.SendWithIfMatchAsync(HttpMethod.Put, CreditNote(draft.Id, "lines"), new ReplaceCreditNoteLinesRequest { Lines = [new CreditNoteLineRequest { BillLineId = dressing.Id, Quantity = 1m }] }, PayersApi.ETag(returned.Version));
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK, await corrected.Content.ReadAsStringAsync());
        var posted = await ApprovedAsync(clerk, supervisor, await corrected.ReadAsAsync<CreditNoteDto>());
        posted.Total.ShouldBe(63.00m);
        posted.Number.ShouldBe("CN/26-27/000001", "the rejected attempt took no number (BR-801)");
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [DocumentType] = 'CreditNote'")).ShouldBe(2);

        // Posted: frozen for everybody (BR-006).
        using var late = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "approve"), body: null, PayersApi.ETag(posted.Version));
        using var lateEdit = await clerk.SendWithIfMatchAsync(HttpMethod.Patch, CreditNote(draft.Id), new UpdateCreditNoteHeaderRequest { CreditNoteDate = Today, Reason = "Other" }, PayersApi.ETag(posted.Version));
        await late.ShouldBeProblemAsync(HttpStatusCode.Conflict, BillingErrors.CreditNoteAlreadyPostedCode);
        await lateEdit.ShouldBeProblemAsync(HttpStatusCode.Conflict, BillingErrors.CreditNoteAlreadyPostedCode);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task A_draft_is_written_against_a_posted_bill_only_and_can_be_corrected_and_voided()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var maker = await NewApiTenantAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);
        var unposted = await clerk.CreateBillAsync(NewBill(patient, payer, Dressing));

        // What cannot be drafted.
        using var unknownBill = await clerk.PostJsonAsync(CreditNotes, new CreateCreditNoteRequest { BillId = Guid.NewGuid(), Reason = "x" });
        using var draftBill = await clerk.PostJsonAsync(CreditNotes, new CreateCreditNoteRequest { BillId = unposted.Id, Reason = "x" });
        using var noReason = await clerk.PostJsonAsync(CreditNotes, new CreateCreditNoteRequest { BillId = bill.Id });
        using var beforeBill = await clerk.PostJsonAsync(CreditNotes, new CreateCreditNoteRequest { BillId = bill.Id, Reason = "x", CreditNoteDate = Today.AddDays(-1) });
        using var future = await clerk.PostJsonAsync(CreditNotes, new CreateCreditNoteRequest { BillId = bill.Id, Reason = "x", CreditNoteDate = Today.AddDays(1) });
        using var strangeLine = await clerk.PostJsonAsync(CreditNotes, new CreateCreditNoteRequest { BillId = bill.Id, Reason = "x", Lines = [new CreditNoteLineRequest { BillLineId = Guid.NewGuid(), Quantity = 1m }] });
        using var freeTextWithoutPrice = await clerk.PostJsonAsync(CreditNotes, new CreateCreditNoteRequest { BillId = bill.Id, Reason = "x", Lines = [new CreditNoteLineRequest { Description = "Goodwill", Quantity = 1m }] });
        await unknownBill.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        await draftBill.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.BillNotPostedCode);
        await noReason.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await beforeBill.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditNoteDateBeforeBillDateCode);
        await future.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditNoteDateInFutureCode);
        await strangeLine.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditNoteBillLineNotFoundCode);
        await freeTextWithoutPrice.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.CreditNotes")).ShouldBe(0);

        // A draft without lines, which cannot be handed over; nor can one that comes to nothing.
        using var created = await clerk.PostJsonAsync(CreditNotes, new CreateCreditNoteRequest { BillId = bill.Id, Reason = "Dressing not done" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var draft = await created.ReadAsAsync<CreditNoteDto>();
        created.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe($"/api/v1/credit-notes/{draft.Id}");
        draft.CreditNoteDate.ShouldBe(Today, "today in India when left out");
        using var empty = await clerk.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        await empty.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditNoteHasNoLinesCode);
        using var free = await clerk.SendWithIfMatchAsync(HttpMethod.Put, CreditNote(draft.Id, "lines"), new ReplaceCreditNoteLinesRequest { Lines = [new CreditNoteLineRequest { Description = "Nothing", Quantity = 1m, UnitPrice = 0m, TaxRatePercent = 0m }] }, PayersApi.ETag(draft.Version));
        var worthless = await free.ReadAsAsync<CreditNoteDto>();
        using var zero = await clerk.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "post"), body: null, PayersApi.ETag(worthless.Version));
        await zero.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditNoteTotalZeroCode);

        // Lines and header are replaced under the version of the credit note (BR-804).
        using var withoutVersion = await clerk.SendWithIfMatchAsync(HttpMethod.Put, CreditNote(draft.Id, "lines"), new ReplaceCreditNoteLinesRequest { Lines = [] }, ifMatch: null);
        using var staleVersion = await clerk.SendWithIfMatchAsync(HttpMethod.Put, CreditNote(draft.Id, "lines"), new ReplaceCreditNoteLinesRequest { Lines = [] }, PayersApi.ETag(draft.Version));
        await withoutVersion.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        await staleVersion.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
        using var replaced = await clerk.SendWithIfMatchAsync(HttpMethod.Put, CreditNote(draft.Id, "lines"), new ReplaceCreditNoteLinesRequest { Lines = [WholeLine(bill, "Dressing"), new CreditNoteLineRequest { Description = "Extra", Quantity = 1m, UnitPrice = 10m, TaxRatePercent = 0m, DiscountAmount = 20m }] }, PayersApi.ETag(worthless.Version));
        await replaced.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditNoteLineDiscountExceedsAmountCode);
        using var twoLines = await clerk.SendWithIfMatchAsync(HttpMethod.Put, CreditNote(draft.Id, "lines"), new ReplaceCreditNoteLinesRequest { Lines = [WholeLine(bill, "Dressing"), new CreditNoteLineRequest { Description = "Extra", Quantity = 1m, UnitPrice = 10m, TaxRatePercent = 0m }] }, PayersApi.ETag(worthless.Version));
        var edited = await twoLines.ReadAsAsync<CreditNoteDto>();
        edited.Lines.Select(line => (line.LineNo, line.LineTotal)).ShouldBe([(1, 126.00m), (2, 10.00m)]);
        edited.Total.ShouldBe(136.00m);
        using var oneLine = await clerk.SendWithIfMatchAsync(HttpMethod.Put, CreditNote(draft.Id, "lines"), new ReplaceCreditNoteLinesRequest { Lines = [WholeLine(bill, "Dressing")] }, PayersApi.ETag(edited.Version));
        var shorter = await oneLine.ReadAsAsync<CreditNoteDto>();
        shorter.Total.ShouldBe(126.00m);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.CreditNoteLines")).ShouldBe(1, "the line taken out of the draft is gone");
        using var header = await clerk.SendWithIfMatchAsync(HttpMethod.Patch, CreditNote(draft.Id), new UpdateCreditNoteHeaderRequest { CreditNoteDate = Today, Reason = "Dressing billed twice" }, PayersApi.ETag(shorter.Version));
        var renamed = await header.ReadAsAsync<CreditNoteDto>();
        renamed.Reason.ShouldBe("Dressing billed twice");

        // Voided: it keeps its content and never changes again.
        using var voided = await clerk.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "void"), new VoidCreditNoteRequest { Reason = "Raised on the wrong bill" }, PayersApi.ETag(renamed.Version));
        voided.StatusCode.ShouldBe(HttpStatusCode.OK, await voided.Content.ReadAsStringAsync());
        var gone = await voided.ReadAsAsync<CreditNoteDto>();
        (gone.Status, gone.VoidReason, gone.Total).ShouldBe((CreditNoteStatus.Voided, "Raised on the wrong bill", 126.00m));
        using var afterVoid = await clerk.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "post"), body: null, PayersApi.ETag(gone.Version));
        await afterVoid.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditNoteNotDraftCode);
        (await TrailAsync(clerk, "CreditNote", draft.Id)).Select(record => record.Action).ShouldBe(["Drafted", "LinesReplaced", "LinesReplaced", "LinesReplaced", "HeaderUpdated", "Voided"]);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] IN ('CreditNotePosted', 'CreditApplied')")).ShouldBe(0);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR210_two_credit_notes_approved_at_once_against_a_bill_that_only_one_fits_give_exactly_one_posting()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        var otherChecker = new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid()));
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var first = factory.CreateClientFor(checker, "Supervisor");
        using var second = factory.CreateClientFor(otherChecker, "TenantAdmin");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);

        // The bill is paid in full, so an approval changes nothing on its row: the row version of the bill cannot be
        // what stops the second credit note. Both drafts fit while neither is posted.
        await PaidAsync(clerk, payer, bill, 126m);
        var one = await SubmittedAsync(clerk, await DraftedAsync(clerk, bill, WholeLine(bill, "Dressing")));
        var two = await SubmittedAsync(clerk, await DraftedAsync(clerk, bill, WholeLine(bill, "Dressing")));

        var responses = await Task.WhenAll(
            Task.Run(() => first.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(one.Id, "approve"), body: null, PayersApi.ETag(one.Version))),
            Task.Run(() => second.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(two.Id, "approve"), body: null, PayersApi.ETag(two.Version))));

        try
        {
            // The loser is told 409 when it had read the caps before the winner committed, 422 when after. Both are
            // correct; a second posting never is.
            responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
            var loser = responses.Single(response => response.StatusCode != HttpStatusCode.OK);
            if (loser.StatusCode == HttpStatusCode.Conflict)
            {
                await loser.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
            }
            else
            {
                await loser.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditNoteExceedsBillTotalCode);
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        // One credit note is posted, with the first number; the other still waits and took none (BR-801).
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.CreditNotes WHERE [Status] = 'Posted'")).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.CreditNotes WHERE [Status] = 'PendingApproval' AND [Number] IS NULL")).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.CreditNotes WHERE [Number] = 'CN/26-27/000001'")).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'CreditNotePosted'")).ShouldBe(1);
        (await BalanceAsync(maker, payer)).CreditAvailable.ShouldBe(Money.Of(126m), "the bill was owed nothing, so all of the one credit note is available");
        await ShouldHaveNoDriftAsync(maker);
    }

    [Theory]
    [InlineData("the ledger, after the second posting is entered")]
    [InlineData("the audit trail, after the record is written")]
    public async Task US25_an_approval_that_fails_at_any_step_leaves_no_number_no_ledger_and_no_audit_rows_and_both_documents_as_they_were(string step)
    {
        using var factory = new DatabaseApiFactory(SqlServer, configure: services =>
        {
            switch (step)
            {
                case "the ledger, after the second posting is entered":
                    services.AddScoped<ReceivablesLedger>();
                    services.AddScoped<IReceivablesLedger>(provider => new CreditAppliedThenFault(provider.GetRequiredService<ReceivablesLedger>()));
                    break;
                default:
                    // Registered after the audit subscriber, so it runs when the audit records are in the unit of work.
                    services.AddScoped<IDomainEventSubscriber, FaultOnCreditNotePosted>();
                    break;
            }
        });
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);
        var waiting = await SubmittedAsync(clerk, await DraftedAsync(clerk, bill, WholeLine(bill, "Dressing")));

        using var failed = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));

        await failed.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
        (await CreditNoteAsync(clerk, waiting.Id)).ShouldBeEquivalentTo(waiting);
        (await BillAsync(clerk, bill.Id)).ShouldBeEquivalentTo(bill);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries WHERE [DocumentType] = 'CreditNote'")).ShouldBe(0, "the number went back with the rest (BR-801)");
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] IN ('CreditNotePosted', 'CreditApplied')")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.CreditNoteAllocations")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.AuditRecords WHERE [Action] IN ('Posted', 'Applied', 'CreditApplied', 'Approved') AND [EntityType] <> 'Bill'")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [Status] = 'Pending'")).ShouldBe(1);
        var balance = await BalanceAsync(maker, payer);
        (balance.Receivable, balance.CreditAvailable).ShouldBe((Money.Of(126m), Money.Zero));
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
        var waiting = await SubmittedAsync(clerk, await DraftedAsync(clerk, bill, WholeLine(bill, "Dressing")));
        var key = Guid.NewGuid();

        using var original = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version), key);
        using var replay = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version), key);

        original.StatusCode.ShouldBe(HttpStatusCode.OK, await original.Content.ReadAsStringAsync());
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).ShouldBe(await original.Content.ReadAsStringAsync());
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'CreditNotePosted'")).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.CreditNoteAllocations")).ShouldBe(1);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR002_a_credit_note_and_a_bill_of_another_tenant_can_neither_be_read_nor_used()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        var stranger = await NewApiTenantAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        using var outsider = factory.CreateClientFor(stranger, "TenantAdmin");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);
        var (theirPatient, theirPayer) = await outsider.NewBillPartiesAsync();
        var theirBill = await PostedBillAsync(outsider, theirPatient, theirPayer, Dressing);
        var draft = await DraftedAsync(clerk, bill, WholeLine(bill, "Dressing"));

        using var read = await outsider.GetAsync(CreditNote(draft.Id));
        using var post = await outsider.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        using var against = await outsider.PostJsonAsync(CreditNotes, new CreateCreditNoteRequest { BillId = bill.Id, Reason = "x" });
        using var list = await outsider.GetAsync(CreditNotes);
        await read.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.CreditNoteNotFoundCode);
        await post.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.CreditNoteNotFoundCode);
        await against.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        (await list.ReadAsAsync<PageOf<CreditNoteSummaryDto>>()).Items.ShouldBeEmpty();

        // Credit cannot be applied to a bill of another tenant either: for this tenant it does not exist.
        await PaidAsync(clerk, payer, bill, 126m);
        var posted = await ApprovedAsync(clerk, supervisor, draft);
        using var across = await ApplyAsync(clerk, posted, (theirBill.Id, 10m));
        await across.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        (await BillAsync(outsider, theirBill.Id)).ShouldBeEquivalentTo(theirBill);
        await ShouldHaveNoDriftAsync(maker);
        await ShouldHaveNoDriftAsync(stranger);
    }

    [Fact]
    public async Task BR404_a_credit_note_that_no_longer_matches_its_ledger_its_applications_or_its_bill_is_reported()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var bill = await PostedBillAsync(clerk, patient, payer, Dressing);
        await PaidAsync(clerk, payer, bill, 100m);
        var posted = await ApprovedAsync(clerk, supervisor, await DraftedAsync(clerk, bill, WholeLine(bill, "Dressing")));
        await ShouldHaveNoDriftAsync(maker);

        // The cache of the credit note is changed behind the application: the ledger and the applications both say 100.
        await ExecuteAsync($"UPDATE dbo.CreditNotes SET [AvailableAmount] = 90 WHERE [Id] = '{posted.Id}'");
        (await DriftAsync(maker)).ShouldBe(
            [
                new DriftRow(DriftIdentity.CreditNoteAvailable, posted.Id, "CN/26-27/000001", 90.00m, 100.00m),
                new DriftRow(DriftIdentity.CreditNoteApplications, posted.Id, "CN/26-27/000001", 90.00m, 100.00m),
            ],
            ignoreOrder: true);
        await ExecuteAsync($"UPDATE dbo.CreditNotes SET [AvailableAmount] = 100 WHERE [Id] = '{posted.Id}'");
        await ShouldHaveNoDriftAsync(maker);

        // The bill is made smaller than its credit notes (BR-210).
        await ExecuteAsync($"ALTER TABLE dbo.Bills NOCHECK CONSTRAINT ALL; UPDATE dbo.Bills SET [Total] = 120, [SubTotal] = 120, [TaxTotal] = 0 WHERE [Id] = '{bill.Id}'; ALTER TABLE dbo.Bills CHECK CONSTRAINT ALL;");
        (await DriftAsync(maker)).ShouldContain(new DriftRow(DriftIdentity.CreditNotesExceedBillTotal, bill.Id, bill.Number, 120.00m, 126.00m));
    }

    private static Uri CreditNote(Guid id) => new($"/api/v1/credit-notes/{id}", UriKind.Relative);

    private static Uri CreditNote(Guid id, string action) => new($"/api/v1/credit-notes/{id}/{action}", UriKind.Relative);

    // A line that credits the whole of the line of the bill with that description.
    private static CreditNoteLineRequest WholeLine(BillDto bill, string description)
    {
        var line = bill.Lines.Single(candidate => candidate.Description == description);

        return new CreditNoteLineRequest { BillLineId = line.Id, Quantity = line.Quantity };
    }

    private static async Task<CreditNoteDto> DraftedAsync(HttpClient client, BillDto bill, params CreditNoteLineRequest[] lines)
    {
        using var response = await client.PostJsonAsync(CreditNotes, new CreateCreditNoteRequest { BillId = bill.Id, CreditNoteDate = Today, Reason = "Dressing not done", Lines = lines });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<CreditNoteDto>();
    }

    private static async Task<CreditNoteDto> SubmittedAsync(HttpClient maker, CreditNoteDto draft)
    {
        using var response = await maker.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<CreditNoteDto>();
    }

    private static async Task<CreditNoteDto> ApprovedAsync(HttpClient maker, HttpClient checker, CreditNoteDto draft)
    {
        var waiting = await SubmittedAsync(maker, draft);
        using var response = await checker.SendWithIfMatchAsync(HttpMethod.Post, CreditNote(draft.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<CreditNoteDto>();
    }

    private static Task<HttpResponseMessage> ApplyAsync(HttpClient client, CreditNoteDto creditNote, params (Guid BillId, decimal Amount)[] lines)
        => client.SendWithIfMatchAsync(
            HttpMethod.Post,
            CreditNote(creditNote.Id, "applications"),
            new ApplyCreditNoteRequest { Applications = [.. lines.Select(line => new CreditApplicationRequest { BillId = line.BillId, Amount = line.Amount })] },
            PayersApi.ETag(creditNote.Version));

    private static async Task<CreditNoteDto> CreditNoteAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(CreditNote(id));

        return await response.ReadAsAsync<CreditNoteDto>();
    }

    // Records a payment of the payer and allocates all of it to the bill.
    private static async Task PaidAsync(HttpClient client, PayerDto payer, BillDto bill, decimal amount)
    {
        using var recorded = await client.PostJsonAsync(Payments, new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.UPI, Reference = "412345678901", Amount = amount });
        recorded.StatusCode.ShouldBe(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync());
        var payment = await recorded.ReadAsAsync<PaymentDto>();

        using var allocated = await client.SendWithIfMatchAsync(
            HttpMethod.Post,
            new Uri($"/api/v1/payments/{payment.Id}/allocations", UriKind.Relative),
            new AllocatePaymentRequest { Allocations = [new AllocationRequest { BillId = bill.Id, Amount = amount }] },
            PayersApi.ETag(payment.Version));
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
        await using var context = ApiContext(SystemIdentity.Instance);

        await context.Database.ExecuteSqlRawAsync(sql);
    }

    private sealed class CreditAppliedThenFault(IReceivablesLedger inner) : IReceivablesLedger
    {
        public async Task<Result<LedgerPostingId>> PostAsync(PostingTemplate template, CancellationToken ct)
        {
            var entered = await inner.PostAsync(template, ct);

            return template.Kind == LedgerPostingKind.CreditApplied
                ? throw new InvalidOperationException("Injected: the ledger failed after it entered the posting.")
                : entered;
        }
    }

    private sealed class FaultOnCreditNotePosted : IDomainEventSubscriber
    {
        public Task HandleAsync(DomainEventOccurrence occurrence, CancellationToken ct)
            => occurrence.Event is CreditNotePosted
                ? throw new InvalidOperationException("Injected: a subscriber failed after the audit record was written.")
                : Task.CompletedTask;
    }
}
