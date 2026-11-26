using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Common.Numbering;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.Infrastructure.Receivables;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Maker-checker for a bill at or above the tenant's threshold, end to end on the pipeline that ships and a real SQL
/// Server (US-22; BR-207, BR-603 to BR-606): the maker's posting only hands the bill over, another user's approval is
/// the posting, a rejection makes the bill a draft again, and the maker can decide nothing. Until users and roles
/// exist (Phase 5) the two people are two user ids of one tenant; that the checker is a supervisor is asked then.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class BillApprovalApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly Uri ApprovalRequests = new("/api/v1/approval-requests", UriKind.Relative);

    [Fact]
    public async Task US22_a_bill_at_the_threshold_waits_without_a_number_and_a_second_users_approval_posts_it()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewTenantWithThresholdAsync(126.00m);
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var draft = await clerk.CreateBillAsync(NewBill(patient, payer));

        // The maker posts: the bill is handed over, and nothing financial has happened (BR-207).
        using var submitted = await clerk.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        submitted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await submitted.Content.ReadAsStringAsync());
        var waiting = await submitted.ReadAsAsync<BillDto>();
        waiting.Status.ShouldBe(BillStatus.PendingApproval);
        waiting.Number.ShouldBeNull();
        waiting.PostedAtUtc.ShouldBeNull();
        waiting.Version.ShouldNotBe(draft.Version);
        submitted.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(waiting.Version));
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(0);
        (await BalanceAsync(maker, payer)).Receivable.ShouldBe(Money.Zero);

        // The checker finds it in the inbox.
        var inbox = await ListAsync(supervisor, string.Empty);
        var request = inbox.Items.ShouldHaveSingleItem();
        request.DocumentType.ShouldBe(ApprovalDocumentType.Bill);
        request.DocumentId.ShouldBe(draft.Id);
        request.Action.ShouldBe(ApprovalAction.Post);
        request.Status.ShouldBe(ApprovalStatus.Pending);
        request.MakerUserId.ShouldBe(maker.UserId!.Value.Value);
        request.CheckerUserId.ShouldBeNull();
        request.RequestedAtUtc.ShouldBe(DatabaseApiFactory.InitialInstant);

        // The checker approves, and that is the posting (BR-606).
        factory.Clock.Advance(TimeSpan.FromMinutes(30));
        using var approved = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "approve"), new ApproveBillRequest { Comment = "Checked against the estimate" }, PayersApi.ETag(waiting.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var posted = await approved.ReadAsAsync<BillDto>();
        posted.Status.ShouldBe(BillStatus.Posted);
        posted.Number.ShouldBe("BIL/26-27/000001");
        posted.PostedAtUtc.ShouldBe(DatabaseApiFactory.InitialInstant.AddMinutes(30));
        posted.BalanceDue.ShouldBe(126.00m);

        await using var context = ApiContext(maker);
        var bill = await context.Set<Bill>().SingleAsync(candidate => candidate.Id == new BillId(draft.Id));
        bill.PostedByUserId.ShouldBe(checker.UserId, "the checker is who posted");
        (await context.Set<LedgerPosting>().SingleAsync()).PostedByUserId.ShouldBe(checker.UserId);
        (await BalanceAsync(maker, payer)).Receivable.ShouldBe(Money.Of(126m));

        (await ListAsync(supervisor, string.Empty)).Items.ShouldBeEmpty("nothing waits any more");
        var decided = (await ListAsync(supervisor, "?status=Approved")).Items.ShouldHaveSingleItem();
        decided.CheckerUserId.ShouldBe(checker.UserId!.Value.Value);
        decided.CheckerComment.ShouldBe("Checked against the estimate");
        decided.DecidedAtUtc.ShouldBe(DatabaseApiFactory.InitialInstant.AddMinutes(30));

        // Both steps are audited, on the bill and on the request (BR-701).
        (await TrailAsync(clerk, "Bill", draft.Id)).Select(record => (record.Action, record.UserId)).ShouldBe(
        [
            ("Drafted", maker.UserId!.Value.Value),
            ("SubmittedForApproval", maker.UserId!.Value.Value),
            ("Posted", checker.UserId!.Value.Value),
        ]);
        // Since 2026-11-26 (review finding R7) the trail of an approval request is read by who may decide, not by its maker.
        (await TrailAsync(supervisor, "ApprovalRequest", request.Id)).Select(record => record.Action).ShouldBe(["Opened", "Approved"]);
        using (var makers = await clerk.GetAsync(new Uri($"/api/v1/audit-records?entityType=ApprovalRequest&entityId={request.Id}", UriKind.Relative)))
        {
            makers.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        await ShouldHaveNoDriftAndAGaplessSeriesAsync(maker);
    }

    [Fact]
    public async Task BR603_who_wrote_a_bill_cannot_approve_it_though_another_user_asked_and_a_second_checker_can()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewTenantWithThresholdAsync(100m);
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var writer = factory.CreateClientFor(checker, "Supervisor", "BillingClerk");
        using var second = factory.CreateClientFor(new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid())), "TenantAdmin");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var waiting = await SubmittedAsync(clerk, await writer.CreateBillAsync(NewBill(patient, payer)));

        // Review 2026-11, finding R9: the supervisor wrote the bill, a clerk posted it over the threshold. The maker is
        // the clerk, and the supervisor is still not a second person.
        using var own = await writer.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));
        var problem = await own.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("You wrote this document");

        using var approved = await second.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        (await approved.ReadAsAsync<BillDto>()).Status.ShouldBe(BillStatus.Posted);
    }

    [Fact]
    public async Task BR603_the_maker_can_neither_approve_nor_reject_their_own_bill()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, _) = await NewTenantWithThresholdAsync(100m);
        using var clerk = factory.CreateClientFor(maker, "TenantAdmin");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var waiting = await SubmittedAsync(clerk, await clerk.CreateBillAsync(NewBill(patient, payer)));

        using var approve = await clerk.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));
        using var reject = await clerk.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "reject"), new RejectBillRequest { Comment = "Changed my mind" }, PayersApi.ETag(waiting.Version));

        // Whatever the role: the checker is always another user (BR-603).
        await approve.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        await reject.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        using var read = await clerk.GetAsync(BillingApi.Bill(waiting.Id));
        var unchanged = await read.ReadAsAsync<BillDto>();
        unchanged.Status.ShouldBe(BillStatus.PendingApproval);
        unchanged.Version.ShouldBe(waiting.Version);
        (await ListAsync(clerk, string.Empty)).Items.ShouldHaveSingleItem().Status.ShouldBe(ApprovalStatus.Pending);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries")).ShouldBe(0);
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(maker);
    }

    [Fact]
    public async Task BR605_a_rejection_needs_a_comment_returns_the_bill_to_draft_and_the_maker_can_correct_and_post_it_again()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewTenantWithThresholdAsync(100m);
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var waiting = await SubmittedAsync(clerk, await clerk.CreateBillAsync(NewBill(patient, payer)));
        var etag = PayersApi.ETag(waiting.Version);

        using var withoutComment = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "reject"), new RejectBillRequest { Comment = "  " }, etag);
        using var rejected = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "reject"), new RejectBillRequest { Comment = " Wrong consultant fee " }, etag);

        (await withoutComment.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode))
            .GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(["comment"]);
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        var draft = await rejected.ReadAsAsync<BillDto>();
        draft.Status.ShouldBe(BillStatus.Draft);
        draft.Number.ShouldBeNull();
        var closed = (await ListAsync(supervisor, "?status=Rejected")).Items.ShouldHaveSingleItem();
        closed.CheckerComment.ShouldBe("Wrong consultant fee");
        closed.CheckerUserId.ShouldBe(checker.UserId!.Value.Value);
        (await TrailAsync(clerk, "Bill", draft.Id)).Select(record => (record.Action, record.Reason)).ShouldBe(
            [("Drafted", null), ("SubmittedForApproval", null), ("ReturnedToDraft", "Wrong consultant fee")]);

        // A draft again: it can be corrected. Below the threshold now, it posts directly.
        using var corrected = await clerk.SendWithIfMatchAsync(HttpMethod.Put, BillingApi.Bill(draft.Id, "lines"), new ReplaceBillLinesRequest { Lines = [BillingApi.FreeTextLine("Dressing", 1m, 60m, 5m)] }, PayersApi.ETag(draft.Version));
        var smaller = await corrected.ReadAsAsync<BillDto>();
        using var posted = await clerk.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(smaller.Version));

        posted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await posted.ReadAsAsync<BillDto>()).Number.ShouldBe("BIL/26-27/000001");
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(maker);
    }

    [Fact]
    public async Task BR605_a_rejected_bill_that_is_posted_again_at_the_threshold_gets_a_new_request_and_the_old_one_stays()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewTenantWithThresholdAsync(100m);
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var waiting = await SubmittedAsync(clerk, await clerk.CreateBillAsync(NewBill(patient, payer)));
        using var rejected = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "reject"), new RejectBillRequest { Comment = "Not yet" }, PayersApi.ETag(waiting.Version));
        var draft = await rejected.ReadAsAsync<BillDto>();

        var again = await SubmittedAsync(clerk, draft);

        again.Status.ShouldBe(BillStatus.PendingApproval);
        (await ListAsync(supervisor, string.Empty)).TotalCount.ShouldBe(1);
        (await ListAsync(supervisor, "?status=Rejected")).TotalCount.ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests")).ShouldBe(2);
    }

    [Fact]
    public async Task BR207_a_bill_that_waits_is_locked_for_everyone()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewTenantWithThresholdAsync(100m);
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var waiting = await SubmittedAsync(clerk, await clerk.CreateBillAsync(NewBill(patient, payer)));
        var etag = PayersApi.ETag(waiting.Version);

        foreach (var client in new[] { clerk, supervisor })
        {
            using var header = await client.SendWithIfMatchAsync(HttpMethod.Patch, BillingApi.Bill(waiting.Id), new UpdateBillHeaderRequest { BillDate = Today, DueDate = Today, Notes = "Changed" }, etag);
            using var lines = await client.SendWithIfMatchAsync(HttpMethod.Put, BillingApi.Bill(waiting.Id, "lines"), new ReplaceBillLinesRequest { Lines = [] }, etag);
            using var voided = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "void"), new VoidBillRequest { Reason = "Changed my mind" }, etag);
            using var postedAgain = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "post"), body: null, etag);

            foreach (var refused in new[] { header, lines, voided, postedAgain })
            {
                await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, BillingErrors.BillPendingApprovalCode);
            }
        }

        using var read = await clerk.GetAsync(BillingApi.Bill(waiting.Id));
        (await read.ReadAsAsync<BillDto>()).Version.ShouldBe(waiting.Version);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests")).ShouldBe(1, "a second posting opened no second request (BR-605)");
    }

    [Fact]
    public async Task A_draft_and_a_posted_bill_have_nothing_to_approve_or_reject()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewTenantWithThresholdAsync(1000m);
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var draft = await clerk.CreateBillAsync(NewBill(patient, payer));

        using var approveDraft = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "approve"), body: null, PayersApi.ETag(draft.Version));
        using var rejectDraft = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "reject"), new RejectBillRequest { Comment = "No" }, PayersApi.ETag(draft.Version));

        // Below the threshold the bill posts directly, as before.
        using var postedResponse = await clerk.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        var posted = await postedResponse.ReadAsAsync<BillDto>();
        using var approvePosted = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "approve"), body: null, PayersApi.ETag(posted.Version));

        await approveDraft.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.BillNotPendingApprovalCode);
        await rejectDraft.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.BillNotPendingApprovalCode);
        postedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        posted.Status.ShouldBe(BillStatus.Posted);
        await approvePosted.ShouldBeProblemAsync(HttpStatusCode.Conflict, BillingErrors.BillAlreadyPostedCode);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests")).ShouldBe(0);
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(maker);
    }

    [Fact]
    public async Task Approving_and_rejecting_need_the_version_the_checker_read_and_an_idempotency_key()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewTenantWithThresholdAsync(100m);
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var draft = await clerk.CreateBillAsync(NewBill(patient, payer));
        var waiting = await SubmittedAsync(clerk, draft);

        using var withoutIfMatch = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "approve"), body: null, ifMatch: null);
        using var stale = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "approve"), body: null, PayersApi.ETag(draft.Version));
        using var unknown = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(Guid.NewGuid(), "reject"), new RejectBillRequest { Comment = "No" }, PayersApi.ETag(waiting.Version));
        using var withoutKeyRequest = new HttpRequestMessage(HttpMethod.Post, BillingApi.Bill(waiting.Id, "approve"));
        withoutKeyRequest.Headers.TryAddWithoutValidation("If-Match", PayersApi.ETag(waiting.Version));
        using var withoutKey = await supervisor.SendAsync(withoutKeyRequest);

        await withoutIfMatch.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
        await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        await withoutKey.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.IdempotencyKeyMissingCode);
        (await ListAsync(supervisor, string.Empty)).Items.ShouldHaveSingleItem().Status.ShouldBe(ApprovalStatus.Pending);
    }

    [Fact]
    public async Task BR804_two_checkers_at_once_give_one_decision_and_one_conflict_whether_both_approve_or_one_rejects()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewTenantWithThresholdAsync(100m);
        var otherChecker = new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid()));
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var first = factory.CreateClientFor(checker, "Supervisor");
        using var second = factory.CreateClientFor(otherChecker, "TenantAdmin");
        var (patient, payer) = await clerk.NewBillPartiesAsync();

        // Both approve.
        var waiting = await SubmittedAsync(clerk, await clerk.CreateBillAsync(NewBill(patient, payer)));
        var approvals = await Task.WhenAll(
            first.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version)),
            second.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version)));

        approvals.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict], ignoreOrder: true);
        Array.ForEach(approvals, response => response.Dispose());
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(1);

        // One approves, one rejects: whichever wins, the bill is in one state and the other got a conflict.
        var contested = await SubmittedAsync(clerk, await clerk.CreateBillAsync(NewBill(patient, payer)));
        var decisions = await Task.WhenAll(
            first.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(contested.Id, "approve"), body: null, PayersApi.ETag(contested.Version)),
            second.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(contested.Id, "reject"), new RejectBillRequest { Comment = "No" }, PayersApi.ETag(contested.Version)));

        decisions.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict], ignoreOrder: true);
        var approvedWon = decisions[0].StatusCode == HttpStatusCode.OK;
        Array.ForEach(decisions, response => response.Dispose());
        using var read = await clerk.GetAsync(BillingApi.Bill(contested.Id));
        (await read.ReadAsAsync<BillDto>()).Status.ShouldBe(approvedWon ? BillStatus.Posted : BillStatus.Draft);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(approvedWon ? 2 : 1);
        (await ListAsync(first, string.Empty)).Items.ShouldBeEmpty();
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(maker);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task US25_an_approval_that_fails_leaves_the_bill_waiting_the_request_pending_and_no_number_consumed(bool atTheLedger)
    {
        var (maker, checker) = await NewTenantWithThresholdAsync(100m);
        BillDto waiting;
        PayerDto payer;
        var key = Guid.NewGuid();

        using (var failing = new DatabaseApiFactory(SqlServer, configure: services => FailPosting(services, atTheLedger)))
        {
            using var clerk = failing.CreateClientFor(maker, "BillingClerk");
            using var supervisor = failing.CreateClientFor(checker, "Supervisor");
            (var patient, payer) = await clerk.NewBillPartiesAsync();
            waiting = await SubmittedAsync(clerk, await clerk.CreateBillAsync(NewBill(patient, payer)));

            using var failed = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version), key);

            await failed.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
            using var read = await clerk.GetAsync(BillingApi.Bill(waiting.Id));
            var bill = await read.ReadAsAsync<BillDto>();
            bill.Status.ShouldBe(BillStatus.PendingApproval);
            bill.Version.ShouldBe(waiting.Version);
            var request = (await ListAsync(supervisor, string.Empty)).Items.ShouldHaveSingleItem();
            request.Status.ShouldBe(ApprovalStatus.Pending);
            request.CheckerUserId.ShouldBeNull();
            (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries")).ShouldBe(0, "no number was consumed (BR-801)");
            (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(0);
            (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.AuditRecords WHERE [Action] IN ('Posted', 'Approved')")).ShouldBe(0);
            (await BalanceAsync(maker, payer)).Receivable.ShouldBe(Money.Zero);
        }

        // The same approval again, with the same key, on an API that works.
        using var working = new DatabaseApiFactory(SqlServer);
        using var retrying = working.CreateClientFor(checker, "Supervisor");
        using var retried = await retrying.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version), key);

        retried.StatusCode.ShouldBe(HttpStatusCode.OK, await retried.Content.ReadAsStringAsync());
        (await retried.ReadAsAsync<BillDto>()).Number.ShouldBe("BIL/26-27/000001");
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(maker);
    }

    [Fact]
    public async Task BR803_a_replayed_approval_returns_the_same_response_and_posts_once()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewTenantWithThresholdAsync(100m);
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var waiting = await SubmittedAsync(clerk, await clerk.CreateBillAsync(NewBill(patient, payer)));
        var key = Guid.NewGuid();

        using var first = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version), key);
        using var replay = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version), key);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.Headers.Contains("Idempotent-Replayed").ShouldBeTrue();
        (await replay.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(1);
        (await BalanceAsync(maker, payer)).Receivable.ShouldBe(Money.Of(126m));
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(maker);
    }

    [Fact]
    public async Task BR603_BR605_the_table_itself_refuses_a_checker_who_is_the_maker_and_a_second_pending_request()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, _) = await NewTenantWithThresholdAsync(100m);
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var waiting = await SubmittedAsync(clerk, await clerk.CreateBillAsync(NewBill(patient, payer)));
        await using var system = ApiContext(SystemIdentity.Instance);

        // The rules of the aggregate, again as constraints: SQL that goes around the application meets them too.
        var ownApproval = await Should.ThrowAsync<SqlException>(() => system.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.ApprovalRequests SET [Status] = 'Approved', [CheckerUserId] = [MakerUserId], [DecidedAtUtc] = SYSUTCDATETIME()"));
        var secondPending = await Should.ThrowAsync<SqlException>(() => system.Database.ExecuteSqlRawAsync(
            """
            INSERT dbo.ApprovalRequests ([Id], [TenantId], [DocumentType], [DocumentId], [Action], [MakerUserId], [Status], [RequestedAtUtc], [CorrelationId])
            SELECT NEWID(), [TenantId], [DocumentType], [DocumentId], [Action], [MakerUserId], 'Pending', [RequestedAtUtc], [CorrelationId] FROM dbo.ApprovalRequests
            """));
        var halfDecided = await Should.ThrowAsync<SqlException>(() => system.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.ApprovalRequests SET [Status] = 'Approved'"));

        // 547: a CHECK constraint; 2601: a unique index.
        ownApproval.Number.ShouldBe(547);
        ownApproval.Message.ShouldContain("CK_ApprovalRequests_CheckerIsNotMaker");
        secondPending.Number.ShouldBe(2601);
        secondPending.Message.ShouldContain("UX_ApprovalRequests_TenantId_DocumentType_DocumentId");
        halfDecided.Number.ShouldBe(547);
        halfDecided.Message.ShouldContain("CK_ApprovalRequests_Decided");
        waiting.Status.ShouldBe(BillStatus.PendingApproval);
    }

    [Fact]
    public async Task BR002_a_tenant_sees_and_decides_only_its_own_requests()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewTenantWithThresholdAsync(100m);
        var (otherMaker, otherChecker) = await NewTenantWithThresholdAsync(100m);
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        using var stranger = factory.CreateClientFor(otherChecker, "Supervisor");
        using var theirClerk = factory.CreateClientFor(otherMaker, "BillingClerk");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var waiting = await SubmittedAsync(clerk, await clerk.CreateBillAsync(NewBill(patient, payer)));
        var (theirPatient, theirPayer) = await theirClerk.NewBillPartiesAsync();
        await SubmittedAsync(theirClerk, await theirClerk.CreateBillAsync(NewBill(theirPatient, theirPayer)));

        using var forged = await stranger.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));

        await forged.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        (await ListAsync(supervisor, string.Empty)).Items.ShouldHaveSingleItem().DocumentId.ShouldBe(waiting.Id);
        (await ListAsync(stranger, string.Empty)).Items.ShouldHaveSingleItem().DocumentId.ShouldNotBe(waiting.Id);
    }

    [Theory]
    [InlineData("?status=Sideways", "status")]
    [InlineData("?documentType=Invoice", "documentType")]
    [InlineData("?pageSize=101", "pageSize")]
    public async Task A_malformed_query_of_the_inbox_answers_400(string query, string field)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "Supervisor");

        using var response = await client.GetAsync(new Uri(ApprovalRequests + query, UriKind.Relative));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);
        problem.GetProperty("errors").EnumerateObject().Select(error => error.Name.ToUpperInvariant()).ShouldContain(field.ToUpperInvariant());
    }

    // The registrations that make the approval fail after the number is taken, or after the posting is entered.
    private static void FailPosting(IServiceCollection services, bool atTheLedger)
    {
        if (atTheLedger)
        {
            services.AddScoped<ReceivablesLedger>();
            services.AddScoped<Application.Receivables.IReceivablesLedger>(provider => new PostingThenFault(provider.GetRequiredService<ReceivablesLedger>()));
        }
        else
        {
            services.AddScoped<NumberSeriesAllocator>();
            services.AddScoped<INumberSeries>(provider => new NumberThenFault(provider.GetRequiredService<NumberSeriesAllocator>()));
        }
    }

    private static async Task<BillDto> SubmittedAsync(HttpClient client, BillDto draft)
    {
        using var response = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<BillDto>();
    }

    private static async Task<PageOf<ApprovalRequestDto>> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(new Uri(ApprovalRequests + query, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PageOf<ApprovalRequestDto>>();
    }

    private static async Task<IReadOnlyList<AuditRecordDto>> TrailAsync(HttpClient client, string entityType, Guid entityId)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/audit-records?entityType={entityType}&entityId={entityId}", UriKind.Relative));

        return (await response.ReadAsAsync<PageOf<AuditRecordDto>>()).Items;
    }

    // A tenant whose bills need approval from the threshold on, and two of its users: who posts, and who decides.
    [Fact]
    public async Task BR804_a_decision_that_read_the_bill_before_another_decision_committed_and_finds_no_pending_request_is_a_conflict()
    {
        // The rejection reads the bill while it waits, and is then held just before it looks for the approval request.
        // The approval runs to its commit in between, so the rejection finds no pending request any more. It read a
        // stale bill, and that is a conflict, not a fault: this interleaving once answered 500.
        var gate = new LateDecisionGate();
        using var factory = new DatabaseApiFactory(SqlServer, configure: services =>
        {
            services.AddSingleton(gate);
            services.AddScoped<Infrastructure.Common.Approvals.ApprovalRequestRepository>();
            services.AddScoped<IApprovalRequestRepository>(provider => new HeldApprovals(
                provider.GetRequiredService<Infrastructure.Common.Approvals.ApprovalRequestRepository>(), gate));
        });
        var (maker, checker) = await NewTenantWithThresholdAsync(100m);
        var otherChecker = new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid()));
        using var clerk = factory.CreateClientFor(maker, "BillingClerk");
        using var first = factory.CreateClientFor(checker, "Supervisor");
        using var second = factory.CreateClientFor(otherChecker, "TenantAdmin");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var waiting = await SubmittedAsync(clerk, await clerk.CreateBillAsync(NewBill(patient, payer)));
        gate.Armed = true;

        var rejection = second.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "reject"), new RejectBillRequest { Comment = "No" }, PayersApi.ETag(waiting.Version));
        await gate.FirstIsAboutToLook.Task.WaitAsync(TimeSpan.FromSeconds(30));
        using var approved = await first.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(waiting.Id, "approve"), body: null, PayersApi.ETag(waiting.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        gate.Release.SetResult();
        using var late = await rejection;

        await late.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
        using var read = await clerk.GetAsync(BillingApi.Bill(waiting.Id));
        (await read.ReadAsAsync<BillDto>()).Status.ShouldBe(BillStatus.Posted, "the approval stands");
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [Status] = 'Approved'")).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.ApprovalRequests WHERE [Status] <> 'Approved'")).ShouldBe(0);
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(maker);
    }

    // Holds the first request that looks for a pending approval request, until the test lets it go.
    private sealed class LateDecisionGate
    {
        private int _lookups;

        public bool Armed { get; set; }

        public TaskCompletionSource FirstIsAboutToLook { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsFirst() => Interlocked.Increment(ref _lookups) == 1;
    }

    private sealed class HeldApprovals(IApprovalRequestRepository inner, LateDecisionGate gate) : IApprovalRequestRepository
    {
        public async Task<ApprovalRequest?> GetPendingAsync(ApprovalDocumentType documentType, Guid documentId, CancellationToken ct)
        {
            if (gate.Armed && gate.IsFirst())
            {
                gate.FirstIsAboutToLook.TrySetResult();
                await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            }

            return await inner.GetPendingAsync(documentType, documentId, ct);
        }

        public void Add(ApprovalRequest request) => inner.Add(request);
    }

    private async Task<(FixedIdentity Maker, FixedIdentity Checker)> NewTenantWithThresholdAsync(decimal threshold)
    {
        var maker = await NewApiTenantAsync();

        // Tenant settings have no use case yet (Prompt 5.3); the row is changed as system work.
        await using var system = ApiContext(SystemIdentity.Instance);
        var row = await system.Set<Tenant>().SingleAsync(candidate => candidate.Id == maker.TenantId!.Value);
        system.Entry(row).Property(candidate => candidate.PostingApprovalThreshold).CurrentValue = Money.Of(threshold);
        await system.SaveChangesAsync();

        return (maker, new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid())));
    }

    private async Task ShouldHaveNoDriftAndAGaplessSeriesAsync(FixedIdentity tenant)
    {
        await using var context = ApiContext(tenant);
        (await new ReconciliationQuery(context, tenant).FindDriftAsync(CancellationToken.None)).ShouldBeEmpty();

        var numbered = await context.Set<Bill>().AsNoTracking().Where(bill => bill.Number != null).ToListAsync();
        numbered.Select(bill => bill.SequenceNumber!.Value).Order().ShouldBe(Enumerable.Range(1, numbered.Count), "the bills have a gap");
        var waiting = await context.Set<Bill>().AsNoTracking().Where(bill => bill.Status == BillStatus.PendingApproval).ToListAsync();
        waiting.ShouldAllBe(bill => bill.Number == null && bill.PostedAtUtc == null);
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

    // 2 x 60.00 at 5%: 126.00.
    private static CreateBillRequest NewBill(PatientDto patient, PayerDto payer)
        => new() { PatientId = patient.Id, PayerId = payer.Id, BillDate = Today, DueDate = Today.AddDays(15), Lines = [BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m)] };

    private sealed class NumberThenFault(INumberSeries inner) : INumberSeries
    {
        public Task HoldAsync(DocumentType type, FiscalYear fiscalYear, CancellationToken ct) => inner.HoldAsync(type, fiscalYear, ct);

        public async Task<DocumentNumber> NextAsync(DocumentType type, FiscalYear fiscalYear, CancellationToken ct)
        {
            await inner.NextAsync(type, fiscalYear, ct);

            throw new InvalidOperationException("Injected: the number series failed after it took the number.");
        }
    }

    private sealed class PostingThenFault(Application.Receivables.IReceivablesLedger inner) : Application.Receivables.IReceivablesLedger
    {
        public async Task<Result<LedgerPostingId>> PostAsync(Application.Receivables.PostingTemplate template, CancellationToken ct)
        {
            await inner.PostAsync(template, ct);

            throw new InvalidOperationException("Injected: the ledger failed after it entered the posting.");
        }
    }
}
