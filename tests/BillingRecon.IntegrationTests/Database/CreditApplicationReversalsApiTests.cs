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
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Reversing an application of credit end to end, on the pipeline that ships and a real SQL Server (ADR 0028; US-25;
/// BR-007, BR-208, BR-212, BR-213, BR-603 to BR-606, BR-803, BR-804): a clerk asks, a second person decides, and an
/// approval writes the mirror ledger posting, the restored balances and the audit records in one transaction or not
/// at all. The application a credit note made to its own bill at posting is never reversed. Nothing is deleted, and
/// every scenario ends with no drift.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class CreditApplicationReversalsApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
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
    public async Task ADR0028_a_later_application_is_reversed_by_a_second_person_and_the_credit_and_the_bill_are_as_before_it_was_made()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var scene = await AppliedAsync(factory, clerk, supervisor);
        var right = await PostedBillAsync(clerk, scene.Patient, scene.Payer, Dressing);
        await ShouldHaveNoDriftAsync(maker);

        // The clerk asks: 202, the application waits, and nothing financial has happened.
        using var asked = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Application(scene.CreditNote.Id, scene.Later.Id, "reverse"), new ReverseCreditApplicationRequest { Reason = "Applied to the wrong bill" }, PayersApi.ETag(scene.CreditNote.Version));
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());
        var waiting = await asked.ReadAsAsync<CreditNoteDto>();
        asked.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(waiting.Version));
        waiting.Applications.Single(application => application.Id == scene.Later.Id).Status.ShouldBe(CreditNoteAllocationStatus.PendingReversal);
        waiting.AvailableAmount.ShouldBe(40.00m);
        (await BillAsync(clerk, scene.Other.Id)).BalanceDue.ShouldBe(530.00m);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'CreditApplicationReversed'")).ShouldBe(0);
        await ShouldHaveNoDriftAsync(maker);

        // The checker approves the next day: that is the reversal (BR-606).
        factory.Clock.Advance(TimeSpan.FromDays(1));
        var reversedAt = factory.Clock.UtcNow;
        using var approved = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Application(scene.CreditNote.Id, scene.Later.Id, "approve-reversal"), new ApproveCreditApplicationReversalRequest { Comment = "Confirmed with the ward" }, PayersApi.ETag(waiting.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var reversed = await approved.ReadAsAsync<CreditNoteDto>();
        approved.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(reversed.Version));
        reversed.AvailableAmount.ShouldBe(100.00m);
        reversed.Total.ShouldBe(126.00m, "a posted credit note keeps its total (BR-006)");
        reversed.Applications.Count.ShouldBe(2, "nothing is deleted (BR-005)");
        var taken = reversed.Applications.Single(application => application.Id == scene.Later.Id);
        (taken.Status, taken.Amount, taken.BillId, taken.ReversalReason, taken.ReversedAtUtc).ShouldBe((CreditNoteAllocationStatus.Reversed, 60.00m, scene.Other.Id, "Applied to the wrong bill", reversedAt));
        reversed.Applications.Single(application => application.Id == scene.AtPosting.Id).Status.ShouldBe(CreditNoteAllocationStatus.Active);

        // The bill is owed again and posted again (BR-208); the credit note's own bill is untouched.
        var owed = await BillAsync(clerk, scene.Other.Id);
        (owed.Status, owed.BalanceDue).ShouldBe((BillStatus.Posted, 590.00m));
        (await BillAsync(clerk, scene.Own.Id)).Status.ShouldBe(BillStatus.Settled);

        // The ledger: the mirror pair, naming the posting it reverses; the original posting is as it was.
        await using (var context = ApiContext(maker))
        {
            var mirror = await context.Set<LedgerPosting>().Include(candidate => candidate.Entries).SingleAsync(candidate => candidate.Kind == LedgerPostingKind.CreditApplicationReversed);
            var row = await context.Set<CreditNoteAllocation>().AsNoTracking().SingleAsync(application => application.Id == new CreditNoteAllocationId(scene.Later.Id));
            mirror.DocumentType.ShouldBe(LedgerDocumentType.CreditNoteAllocation);
            mirror.DocumentId.ShouldBe(scene.Later.Id);
            mirror.ReversalOfPostingId.ShouldBe(row.PostingId);
            mirror.BusinessDate.ShouldBe(Today.AddDays(1), "the day of the reversal");
            mirror.Description.ShouldBe($"Application of credit note CN/26-27/000001 to bill {scene.Other.Number} reversed");
            mirror.Entries.OrderBy(entry => entry.LineNo).Select(entry => (entry.Account, entry.Debit, entry.Credit, entry.SubjectId)).ShouldBe(
            [
                (LedgerAccount.Receivable, Money.Of(60m), Money.Zero, scene.Other.Id),
                (LedgerAccount.CreditAvailable, Money.Zero, Money.Of(60m), scene.CreditNote.Id),
            ]);
            (row.ReversalPostingId, row.ReversedByUserId, row.AppliedAtPosting).ShouldBe((mirror.Id, checker.UserId, false));
            (await context.Set<LedgerPosting>().CountAsync(candidate => candidate.Kind == LedgerPostingKind.CreditApplied)).ShouldBe(2);
        }

        var balance = await BalanceAsync(maker, scene.Payer);
        (balance.Receivable, balance.CreditAvailable).ShouldBe((Money.Of(716m), Money.Of(100m)));

        // The audit trail of both documents, with the reason (BR-701).
        var trail = await TrailAsync(clerk, "CreditNote", scene.CreditNote.Id);
        trail.Select(record => record.Action).ShouldBe(
            ["Drafted", "SubmittedForApproval", "Posted", "Applied", "Applied", "ApplicationReversalRequested", "ApplicationReversed"], ignoreOrder: true);
        var last = trail.Single(record => record.Action == "ApplicationReversed");
        (last.Reason, last.DocumentNumber, last.CorrelationId).ShouldBe(("Applied to the wrong bill", "CN/26-27/000001", approved.GetCorrelationId()));
        (await TrailAsync(clerk, "Bill", scene.Other.Id)).Select(record => record.Action).ShouldBe(["Drafted", "Posted", "CreditApplied", "CreditReversed"]);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [DocumentType] = 'CreditNoteAllocation' AND [Action] = 'Reverse' AND [Status] = 'Approved'")).ShouldBe(1);
        await ShouldHaveNoDriftAsync(maker);

        // The credit is free again and goes where it should have gone; the reversed application stays reversed.
        var again = await AppliedToAsync(clerk, reversed, right.Id, 100m);
        again.AvailableAmount.ShouldBe(0.00m);
        again.Applications.Count.ShouldBe(3);
        (await BillAsync(clerk, right.Id)).BalanceDue.ShouldBe(26.00m);
        using var twice = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Application(scene.CreditNote.Id, scene.Later.Id, "reverse"), new ReverseCreditApplicationRequest { Reason = "Again" }, PayersApi.ETag(again.Version));
        await twice.ShouldBeProblemAsync(HttpStatusCode.Conflict, BillingErrors.CreditApplicationReversedCode);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task ADR0028_the_application_made_at_posting_is_marked_and_cannot_be_reversed()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var scene = await AppliedAsync(factory, clerk, supervisor);

        scene.AtPosting.AppliedAtPosting.ShouldBeTrue();
        scene.Later.AppliedAtPosting.ShouldBeFalse();
        (await CountAsync($"SELECT COUNT(*) AS [Value] FROM dbo.CreditNoteAllocations WHERE [AppliedAtPosting] = 1 AND [Id] = '{scene.AtPosting.Id}'")).ShouldBe(1);

        using var refused = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Application(scene.CreditNote.Id, scene.AtPosting.Id, "reverse"), new ReverseCreditApplicationRequest { Reason = "Raised on the wrong bill" }, PayersApi.ETag(scene.CreditNote.Version));

        await refused.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditApplicationMadeAtPostingCode);
        (await CreditNoteAsync(clerk, scene.CreditNote.Id)).ShouldBeEquivalentTo(scene.CreditNote);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [DocumentType] = 'CreditNoteAllocation'")).ShouldBe(0);

        // The database holds the same line: the application made at posting is never anything but active. 547: a CHECK.
        var around = await Should.ThrowAsync<SqlException>(() => ExecuteAsync($"UPDATE dbo.CreditNoteAllocations SET [Status] = 'PendingReversal' WHERE [Id] = '{scene.AtPosting.Id}'"));
        around.Number.ShouldBe(547);
        around.Message.ShouldContain("CK_CreditNoteAllocations_AtPostingStaysActive");
    }

    [Fact]
    public async Task BR603_BR605_the_clerk_who_asked_cannot_decide_one_decision_waits_at_a_time_and_a_rejection_leaves_the_application_in_force()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");

        // The maker with a checker's role too is refused as the maker (BR-603); as a clerk, for the role (ADR 0032).
        using var makerAsChecker = factory.CreateClientFor(maker, "Supervisor");
        var scene = await AppliedAsync(factory, clerk, supervisor);
        var creditNote = scene.CreditNote;

        // Nothing waits yet, and a reversal needs a reason, the version and an application of this credit note (BR-804).
        using var early = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Application(creditNote.Id, scene.Later.Id, "approve-reversal"), body: null, PayersApi.ETag(creditNote.Version));
        using var earlyRejection = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Application(creditNote.Id, scene.Later.Id, "reject-reversal"), new RejectCreditApplicationReversalRequest { Comment = "x" }, PayersApi.ETag(creditNote.Version));
        using var noReason = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Application(creditNote.Id, scene.Later.Id, "reverse"), new ReverseCreditApplicationRequest(), PayersApi.ETag(creditNote.Version));
        using var noVersion = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Application(creditNote.Id, scene.Later.Id, "reverse"), new ReverseCreditApplicationRequest { Reason = "x" }, ifMatch: null);
        using var stale = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Application(creditNote.Id, scene.Later.Id, "reverse"), new ReverseCreditApplicationRequest { Reason = "x" }, PayersApi.ETag(scene.Posted.Version));
        using var unknown = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Application(creditNote.Id, Guid.NewGuid(), "reverse"), new ReverseCreditApplicationRequest { Reason = "x" }, PayersApi.ETag(creditNote.Version));
        await early.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditApplicationNotPendingReversalCode);
        await earlyRejection.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.CreditApplicationNotPendingReversalCode);
        await noReason.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        await noVersion.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
        await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.CreditApplicationNotFoundCode);

        // The application waits: the clerk cannot decide, and it cannot be asked for twice.
        var waiting = await RequestedAsync(clerk, creditNote, scene.Later.Id, "Applied to the wrong bill");
        using var own = await makerAsChecker.SendWithIfMatchAsync(HttpMethod.Post, Application(creditNote.Id, scene.Later.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Version));
        using var ownRejection = await makerAsChecker.SendWithIfMatchAsync(HttpMethod.Post, Application(creditNote.Id, scene.Later.Id, "reject-reversal"), new RejectCreditApplicationReversalRequest { Comment = "Mine" }, PayersApi.ETag(waiting.Version));
        using var asClerk = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Application(creditNote.Id, scene.Later.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Version));
        await asClerk.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        using var secondRequest = await clerk.SendWithIfMatchAsync(HttpMethod.Post, Application(creditNote.Id, scene.Later.Id, "reverse"), new ReverseCreditApplicationRequest { Reason = "Again" }, PayersApi.ETag(waiting.Version));
        using var noComment = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Application(creditNote.Id, scene.Later.Id, "reject-reversal"), new RejectCreditApplicationReversalRequest(), PayersApi.ETag(waiting.Version));
        await own.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        await ownRejection.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        await secondRequest.ShouldBeProblemAsync(HttpStatusCode.Conflict, BillingErrors.CreditApplicationPendingReversalCode);
        await noComment.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");

        // More of the credit can still be applied meanwhile; the decision then needs the version that followed.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var meanwhile = await AppliedToAsync(clerk, waiting, scene.Other.Id, 10m);
        meanwhile.AvailableAmount.ShouldBe(30.00m);
        using var outdated = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Application(creditNote.Id, scene.Later.Id, "reject-reversal"), new RejectCreditApplicationReversalRequest { Comment = "x" }, PayersApi.ETag(waiting.Version));
        await outdated.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);

        // The checker rejects: the application is in force again, and nothing financial happened (BR-605).
        using var rejected = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Application(creditNote.Id, scene.Later.Id, "reject-reversal"), new RejectCreditApplicationReversalRequest { Comment = "The bill is right" }, PayersApi.ETag(meanwhile.Version));
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        var kept = await rejected.ReadAsAsync<CreditNoteDto>();
        kept.Applications.Single(application => application.Id == scene.Later.Id).ShouldBe(scene.Later);
        kept.AvailableAmount.ShouldBe(30.00m);
        (await BillAsync(clerk, scene.Other.Id)).BalanceDue.ShouldBe(520.00m);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'CreditApplicationReversed'")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [DocumentType] = 'CreditNoteAllocation' AND [Status] = 'Rejected' AND [CheckerComment] = 'The bill is right'")).ShouldBe(1);
        (await TrailAsync(clerk, "CreditNote", creditNote.Id)).Select(record => record.Action).ShouldBe(
            ["Drafted", "SubmittedForApproval", "Posted", "Applied", "Applied", "ApplicationReversalRequested", "Applied", "ApplicationReversalRejected"], ignoreOrder: true);
        await ShouldHaveNoDriftAsync(maker);

        // It can be asked for again.
        (await RequestedAsync(clerk, kept, scene.Later.Id, "Asked again")).Applications.Single(application => application.Id == scene.Later.Id).Status.ShouldBe(CreditNoteAllocationStatus.PendingReversal);
    }

    [Fact]
    public async Task BR804_two_checkers_deciding_the_same_reversal_at_once_give_exactly_one_decision()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        using var second = factory.CreateClientFor(new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid())), "TenantAdmin");
        var scene = await AppliedAsync(factory, clerk, supervisor);
        var waiting = await RequestedAsync(clerk, scene.CreditNote, scene.Later.Id, "Applied to the wrong bill");

        var decisions = await Task.WhenAll(
            Task.Run(() => supervisor.SendWithIfMatchAsync(HttpMethod.Post, Application(waiting.Id, scene.Later.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Version))),
            Task.Run(() => second.SendWithIfMatchAsync(HttpMethod.Post, Application(waiting.Id, scene.Later.Id, "reject-reversal"), new RejectCreditApplicationReversalRequest { Comment = "No" }, PayersApi.ETag(waiting.Version))));

        bool approvalWon;
        try
        {
            // Exactly one decision stands. The other read the credit note before the winner committed (409 from the
            // version, or from the request that is no longer pending) or after it (the application no longer waits).
            decisions.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1, string.Join(" | ", await Task.WhenAll(decisions.Select(response => response.Content.ReadAsStringAsync()))));
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

        var read = await CreditNoteAsync(clerk, waiting.Id);
        read.Applications.Single(application => application.Id == scene.Later.Id).Status.ShouldBe(approvalWon ? CreditNoteAllocationStatus.Reversed : CreditNoteAllocationStatus.Active);
        read.AvailableAmount.ShouldBe(approvalWon ? 100.00m : 40.00m);
        (await BillAsync(clerk, scene.Other.Id)).BalanceDue.ShouldBe(approvalWon ? 590.00m : 530.00m);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'CreditApplicationReversed'")).ShouldBe(approvalWon ? 1 : 0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [Status] = 'Pending'")).ShouldBe(0);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR804_a_reversal_approved_while_a_payment_is_allocated_to_the_same_bill_never_fails_and_leaves_no_drift()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var cashier = factory.CreateClientFor(new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid())), "Cashier");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var scene = await AppliedAsync(factory, clerk, supervisor);
        var waiting = await RequestedAsync(clerk, scene.CreditNote, scene.Later.Id, "Applied to the wrong bill");
        var payment = await RecordedAsync(cashier, scene.Payer, 100m);

        // Both change the same bill: the approval gives it 60.00 back, the allocation takes 100.00 off it.
        var racing = await Task.WhenAll(
            Task.Run(() => supervisor.SendWithIfMatchAsync(HttpMethod.Post, Application(waiting.Id, scene.Later.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Version))),
            Task.Run(() => cashier.SendWithIfMatchAsync(
                HttpMethod.Post,
                new Uri($"/api/v1/payments/{payment.Id}/allocations", UriKind.Relative),
                new AllocatePaymentRequest { Allocations = [new AllocationRequest { BillId = scene.Other.Id, Amount = 100m }] },
                PayersApi.ETag(payment.Version))));

        bool reversed;
        bool allocated;
        try
        {
            // Whichever commits second either went after the first (200) or read the bill before it (409, read again).
            // Never a fault, and at least one goes through.
            var bodies = string.Join(" | ", await Task.WhenAll(racing.Select(response => response.Content.ReadAsStringAsync())));
            racing.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Conflict, bodies);
            racing.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBeGreaterThanOrEqualTo(1, bodies);
            reversed = racing[0].StatusCode == HttpStatusCode.OK;
            allocated = racing[1].StatusCode == HttpStatusCode.OK;
        }
        finally
        {
            foreach (var response in racing)
            {
                response.Dispose();
            }
        }

        var bill = await BillAsync(clerk, scene.Other.Id);
        bill.BalanceDue.ShouldBe(530.00m + (reversed ? 60.00m : 0m) - (allocated ? 100.00m : 0m));
        (await CreditNoteAsync(clerk, waiting.Id)).AvailableAmount.ShouldBe(reversed ? 100.00m : 40.00m);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [Status] = 'Pending'")).ShouldBe(reversed ? 0 : 1);
        (await CountAsync($"SELECT COUNT(*) AS [Value] FROM dbo.PaymentAllocations WHERE [BillId] = '{scene.Other.Id}'")).ShouldBe(allocated ? 1 : 0);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Theory]
    [InlineData("the ledger, after the reversal posting is entered")]
    [InlineData("the audit trail, after the record is written")]
    public async Task US25_a_reversal_that_fails_at_any_step_leaves_no_ledger_and_no_audit_rows_and_both_documents_as_they_were(string step)
    {
        using var factory = new DatabaseApiFactory(SqlServer, configure: services =>
        {
            switch (step)
            {
                case "the ledger, after the reversal posting is entered":
                    services.AddScoped<ReceivablesLedger>();
                    services.AddScoped<IReceivablesLedger>(provider => new ApplicationReversedThenFault(provider.GetRequiredService<ReceivablesLedger>()));
                    break;
                default:
                    // Registered after the audit subscriber, so it runs when the audit records are in the unit of work.
                    services.AddScoped<IDomainEventSubscriber, FaultOnApplicationReversed>();
                    break;
            }
        });
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var scene = await AppliedAsync(factory, clerk, supervisor);
        var waiting = await RequestedAsync(clerk, scene.CreditNote, scene.Later.Id, "Applied to the wrong bill");
        var bill = await BillAsync(clerk, scene.Other.Id);

        using var failed = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Application(waiting.Id, scene.Later.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Version));

        await failed.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
        (await CreditNoteAsync(clerk, waiting.Id)).ShouldBeEquivalentTo(waiting);
        (await BillAsync(clerk, scene.Other.Id)).ShouldBeEquivalentTo(bill);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'CreditApplicationReversed'")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.AuditRecords WHERE [Action] IN ('ApplicationReversed', 'CreditReversed')")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [DocumentType] = 'CreditNoteAllocation' AND [Status] = 'Pending'")).ShouldBe(1);
        var balance = await BalanceAsync(maker, scene.Payer);
        (balance.Receivable, balance.CreditAvailable).ShouldBe((Money.Of(530m), Money.Of(40m)));
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR803_a_replayed_approval_returns_the_same_response_and_reverses_once()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var scene = await AppliedAsync(factory, clerk, supervisor);
        var waiting = await RequestedAsync(clerk, scene.CreditNote, scene.Later.Id, "Applied to the wrong bill");
        var key = Guid.NewGuid();

        using var original = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Application(waiting.Id, scene.Later.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Version), key);
        using var replay = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Application(waiting.Id, scene.Later.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Version), key);

        original.StatusCode.ShouldBe(HttpStatusCode.OK, await original.Content.ReadAsStringAsync());
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).ShouldBe(await original.Content.ReadAsStringAsync());
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings WHERE [Kind] = 'CreditApplicationReversed'")).ShouldBe(1);
        (await BillAsync(clerk, scene.Other.Id)).BalanceDue.ShouldBe(590.00m);
        await ShouldHaveNoDriftAsync(maker);
    }

    [Fact]
    public async Task BR002_an_application_of_another_tenant_can_neither_be_reversed_nor_decided()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        var stranger = await NewApiTenantAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        using var outsider = factory.CreateClientFor(stranger, "TenantAdmin");
        var scene = await AppliedAsync(factory, clerk, supervisor);
        var waiting = await RequestedAsync(clerk, scene.CreditNote, scene.Later.Id, "Applied to the wrong bill");

        using var ask = await outsider.SendWithIfMatchAsync(HttpMethod.Post, Application(waiting.Id, scene.AtPosting.Id, "reverse"), new ReverseCreditApplicationRequest { Reason = "x" }, PayersApi.ETag(waiting.Version));
        using var approve = await outsider.SendWithIfMatchAsync(HttpMethod.Post, Application(waiting.Id, scene.Later.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Version));
        using var reject = await outsider.SendWithIfMatchAsync(HttpMethod.Post, Application(waiting.Id, scene.Later.Id, "reject-reversal"), new RejectCreditApplicationReversalRequest { Comment = "x" }, PayersApi.ETag(waiting.Version));

        await ask.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.CreditNoteNotFoundCode);
        await approve.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.CreditNoteNotFoundCode);
        await reject.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.CreditNoteNotFoundCode);
        (await CreditNoteAsync(clerk, waiting.Id)).ShouldBeEquivalentTo(waiting);
        await ShouldHaveNoDriftAsync(maker);
        await ShouldHaveNoDriftAsync(stranger);
    }

    [Fact]
    public async Task BR404_a_reversal_that_lost_its_posting_or_a_row_that_no_longer_says_reversed_is_reported()
    {
        ExpectDrift(DriftIdentity.ReversalPostingNoDocument, DriftIdentity.CreditNoteApplications);

        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var scene = await AppliedAsync(factory, clerk, supervisor);
        var waiting = await RequestedAsync(clerk, scene.CreditNote, scene.Later.Id, "Applied to the wrong bill");
        using var approved = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, Application(waiting.Id, scene.Later.Id, "approve-reversal"), body: null, PayersApi.ETag(waiting.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        await ShouldHaveNoDriftAsync(maker);

        // The mirror posting is made to name no posting: the reversal has lost it.
        await ExecuteAsync("UPDATE dbo.LedgerPostings SET [ReversalOfPostingId] = NULL WHERE [Kind] = 'CreditApplicationReversed'");
        (await DriftAsync(maker)).ShouldBe([new DriftRow(DriftIdentity.ReversedApplicationNoPosting, scene.Later.Id, null, 60.00m, 0.00m)]);
        await ExecuteAsync(
            "UPDATE lp SET [ReversalOfPostingId] = a.[PostingId] FROM dbo.LedgerPostings AS lp JOIN dbo.CreditNoteAllocations AS a ON a.[Id] = lp.[DocumentId] WHERE lp.[Kind] = 'CreditApplicationReversed'");
        await ShouldHaveNoDriftAsync(maker);

        // The row is made to say it is in force again: the posting has no reversed application, and the credit note's
        // total less its applications no longer gives its available amount.
        await ExecuteAsync($"ALTER TABLE dbo.CreditNoteAllocations NOCHECK CONSTRAINT ALL; UPDATE dbo.CreditNoteAllocations SET [Status] = 'Active' WHERE [Id] = '{scene.Later.Id}'; ALTER TABLE dbo.CreditNoteAllocations CHECK CONSTRAINT ALL;");
        var drift = await DriftAsync(maker);
        drift.Select(row => row.Identity).ShouldBe([DriftIdentity.ReversalPostingNoDocument, DriftIdentity.CreditNoteApplications], ignoreOrder: true);
        drift.Single(row => row.Identity == DriftIdentity.CreditNoteApplications).ShouldBe(new DriftRow(DriftIdentity.CreditNoteApplications, scene.CreditNote.Id, "CN/26-27/000001", 100.00m, 40.00m));
    }

    private static Uri CreditNote(Guid id) => new($"/api/v1/credit-notes/{id}", UriKind.Relative);

    private static Uri CreditNote(Guid id, string action) => new($"/api/v1/credit-notes/{id}/{action}", UriKind.Relative);

    private static Uri Application(Guid creditNoteId, Guid applicationId, string action) => new($"/api/v1/credit-notes/{creditNoteId}/applications/{applicationId}/{action}", UriKind.Relative);

    // A posted credit note for the whole of a bill of 126.00 of which 100.00 was paid: 26.00 went to its own bill at
    // posting, and a minute later 60.00 of the 100.00 left went to another bill of the payer (590.00, then 530.00 owed).
    private static async Task<Scene> AppliedAsync(DatabaseApiFactory factory, HttpClient clerk, HttpClient supervisor)
    {
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var own = await PostedBillAsync(clerk, patient, payer, Dressing);
        var other = await PostedBillAsync(clerk, patient, payer, Consultation);
        await PaidAsync(clerk, payer, own, 100m);
        var posted = await ApprovedAsync(clerk, supervisor, await DraftedAsync(clerk, own));
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var applied = await AppliedToAsync(clerk, posted, other.Id, 60m);

        applied.AvailableAmount.ShouldBe(40.00m);

        return new Scene(patient, payer, own, other, posted, applied, applied.Applications.Single(application => application.BillId == own.Id), applied.Applications.Single(application => application.BillId == other.Id));
    }

    // A request to reverse, which must be accepted: the credit note as the application waits.
    private static async Task<CreditNoteDto> RequestedAsync(HttpClient client, CreditNoteDto creditNote, Guid applicationId, string reason)
    {
        using var response = await client.SendWithIfMatchAsync(HttpMethod.Post, Application(creditNote.Id, applicationId, "reverse"), new ReverseCreditApplicationRequest { Reason = reason }, PayersApi.ETag(creditNote.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<CreditNoteDto>();
    }

    private static async Task<CreditNoteDto> AppliedToAsync(HttpClient client, CreditNoteDto creditNote, Guid billId, decimal amount)
    {
        using var response = await client.SendWithIfMatchAsync(
            HttpMethod.Post,
            CreditNote(creditNote.Id, "applications"),
            new ApplyCreditNoteRequest { Applications = [new CreditApplicationRequest { BillId = billId, Amount = amount }] },
            PayersApi.ETag(creditNote.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<CreditNoteDto>();
    }

    // A draft that credits the whole dressing line of the bill.
    private static async Task<CreditNoteDto> DraftedAsync(HttpClient client, BillDto bill)
    {
        var line = bill.Lines.Single(candidate => candidate.Description == "Dressing");
        using var response = await client.PostJsonAsync(CreditNotes, new CreateCreditNoteRequest { BillId = bill.Id, CreditNoteDate = Today, Reason = "Dressing not done", Lines = [new CreditNoteLineRequest { BillLineId = line.Id, Quantity = line.Quantity }] });

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

    private static async Task<CreditNoteDto> CreditNoteAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(CreditNote(id));

        return await response.ReadAsAsync<CreditNoteDto>();
    }

    private static async Task<PaymentDto> RecordedAsync(HttpClient client, PayerDto payer, decimal amount)
    {
        using var response = await client.PostJsonAsync(Payments, new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.Cheque, Reference = "000456", Amount = amount });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PaymentDto>();
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
        // A defect seeded on purpose, which the application's login may not cause (ADR 0034).
        await using var context = AdministratorApiContext(SystemIdentity.Instance);

        await context.Database.ExecuteSqlRawAsync(sql);
    }

    // The documents of a scenario: the parties, the credit note's own bill and the other bill, the credit note as it was
    // posted and as it is after the later application, and its two applications.
    private sealed record Scene(
        PatientDto Patient,
        PayerDto Payer,
        BillDto Own,
        BillDto Other,
        CreditNoteDto Posted,
        CreditNoteDto CreditNote,
        CreditNoteApplicationDto AtPosting,
        CreditNoteApplicationDto Later);

    private sealed class ApplicationReversedThenFault(IReceivablesLedger inner) : IReceivablesLedger
    {
        public async Task<Result<LedgerPostingId>> PostAsync(PostingTemplate template, CancellationToken ct)
        {
            var entered = await inner.PostAsync(template, ct);

            return template.Kind == LedgerPostingKind.CreditApplicationReversed
                ? throw new InvalidOperationException("Injected: the ledger failed after it entered the posting.")
                : entered;
        }
    }

    private sealed class FaultOnApplicationReversed : IDomainEventSubscriber
    {
        public Task HandleAsync(DomainEventOccurrence occurrence, CancellationToken ct)
            => occurrence.Event is CreditNoteApplicationReversed
                ? throw new InvalidOperationException("Injected: a subscriber failed after the audit record was written.")
                : Task.CompletedTask;
    }
}
