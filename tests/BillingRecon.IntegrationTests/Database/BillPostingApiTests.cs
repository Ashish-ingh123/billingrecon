using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Common.Numbering;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.Infrastructure.Receivables;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Posting a bill end to end, on the pipeline that ships and a real SQL Server (US-21, US-25; BR-006, BR-007, BR-206,
/// BR-208, BR-801, BR-803, BR-804): the number, the ledger entry, the caches and the audit record are written by one
/// transaction or not at all. Every scenario ends with no drift between the caches and the ledger and with a gapless
/// series.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class BillPostingApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    // 17 October 2026 in India: fiscal year 2026-27.
    private static readonly DateOnly Today = new(2026, 10, 17);

    [Fact]
    public async Task US21_posting_a_draft_assigns_the_number_enters_the_ledger_moves_the_caches_and_writes_the_audit_record()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var draft = await client.CreateBillAsync(NewBill(patient, payer, BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m), BillingApi.FreeTextLine("OPD consultation", 1m, 500m, 18m)));
        factory.Clock.Advance(TimeSpan.FromMinutes(10));

        using var response = await PostAsync(client, draft);

        // The answer: the bill, frozen under its number, with a new version.
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var posted = await response.ReadAsAsync<BillDto>();
        posted.Status.ShouldBe(BillStatus.Posted);
        posted.Number.ShouldBe("BIL/26-27/000001");
        posted.FiscalYear.ShouldBe("2026-27");
        posted.PostedAtUtc.ShouldBe(DatabaseApiFactory.InitialInstant.AddMinutes(10));
        posted.Total.ShouldBe(716.00m);
        posted.BalanceDue.ShouldBe(716.00m);
        posted.Lines.Count.ShouldBe(2);
        posted.Version.ShouldNotBe(draft.Version);
        response.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(posted.Version));

        // Reading it again gives the same bill; the list shows the number.
        using var read = await client.GetAsync(BillingApi.Bill(draft.Id));
        var stored = await read.ReadAsAsync<BillDto>();
        (stored with { Lines = [] }).ShouldBe(posted with { Lines = [] }, "the stored bill is what the posting answered");
        stored.Lines.ShouldBe(posted.Lines);
        using var list = await client.GetAsync(BillingApi.Bills);
        (await list.ReadAsAsync<PageOf<BillSummaryDto>>()).Items.ShouldHaveSingleItem().Number.ShouldBe("BIL/26-27/000001");

        // The ledger: Dr Receivable(bill) / Cr Revenue for the total, on the bill date (BR-206, BR-406).
        await using var context = ApiContext(clerk);
        var posting = await context.Set<LedgerPosting>().Include(candidate => candidate.Entries).SingleAsync();
        posting.Kind.ShouldBe(LedgerPostingKind.BillPosted);
        posting.DocumentType.ShouldBe(LedgerDocumentType.Bill);
        posting.DocumentId.ShouldBe(draft.Id);
        posting.PayerId.ShouldBe(new PayerId(payer.Id));
        posting.BusinessDate.ShouldBe(Today);
        posting.Description.ShouldBe("Bill BIL/26-27/000001 posted");
        posting.PostedByUserId.ShouldBe(clerk.UserId);
        posting.CorrelationId.ShouldBe(response.GetCorrelationId());
        posting.Entries.OrderBy(entry => entry.LineNo).Select(entry => (entry.Account, entry.Debit, entry.Credit, entry.SubjectId)).ShouldBe(
        [
            (LedgerAccount.Receivable, Money.Of(716m), Money.Zero, draft.Id),
            (LedgerAccount.Revenue, Money.Zero, Money.Of(716m), null),
        ]);

        // The caches: the payer owes the total (BR-404).
        var balance = await context.Set<PayerBalance>().SingleAsync(candidate => candidate.PayerId == new PayerId(payer.Id));
        balance.Receivable.ShouldBe(Money.Of(716m));
        balance.NetOutstanding.ShouldBe(Money.Of(716m));

        // The audit record: Posted, with the number, in the request of the posting (BR-701, BR-703).
        var trail = await TrailAsync(client, draft.Id);
        trail.Select(record => record.Action).ShouldBe(["Drafted", "Posted"]);
        trail[1].DocumentNumber.ShouldBe("BIL/26-27/000001");
        trail[1].CorrelationId.ShouldBe(response.GetCorrelationId());
        trail[1].OccurredAtUtc.ShouldBe(DatabaseApiFactory.InitialInstant.AddMinutes(10));
        trail[1].Before.ShouldNotBeNull().GetProperty("Status").GetString().ShouldBe("Draft");
        trail[1].Before.ShouldNotBeNull().GetProperty("Number").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        trail[1].After.GetProperty("Status").GetString().ShouldBe("Posted");
        trail[1].After.GetProperty("Number").GetString().ShouldBe("BIL/26-27/000001");
        trail[1].After.GetProperty("SequenceNumber").GetInt32().ShouldBe(1);
        trail[1].After.GetProperty("Lines").GetArrayLength().ShouldBe(2);

        await ShouldHaveNoDriftAndAGaplessSeriesAsync(clerk);
    }

    [Fact]
    public async Task BR801_bills_are_numbered_in_the_order_they_are_posted_one_series_per_fiscal_year_of_the_bill_date()
    {
        // 2 April 2027: a bill dated 31 March belongs to 2026-27, one dated 1 April to 2027-28 (BR-004).
        using var factory = new DatabaseApiFactory(SqlServer);
        factory.Clock.Set(new DateTimeOffset(2027, 4, 2, 6, 30, 0, TimeSpan.Zero));
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var line = BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m);
        var march = new DateOnly(2027, 3, 31);
        var april = new DateOnly(2027, 4, 1);

        var first = await PostedAsync(client, await client.CreateBillAsync(NewBill(patient, payer, line) with { BillDate = march, DueDate = march }));
        var second = await PostedAsync(client, await client.CreateBillAsync(NewBill(patient, payer, line) with { BillDate = april, DueDate = april }));
        var third = await PostedAsync(client, await client.CreateBillAsync(NewBill(patient, payer, line) with { BillDate = march, DueDate = march }));

        first.Number.ShouldBe("BIL/26-27/000001");
        second.Number.ShouldBe("BIL/27-28/000001");
        third.Number.ShouldBe("BIL/26-27/000002");
        (await BalanceAsync(clerk, payer)).Receivable.ShouldBe(Money.Of(378m));
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(clerk);
    }

    [Fact]
    public async Task BR206_a_bill_that_comes_to_nothing_is_settled_under_its_number_and_has_no_ledger_posting()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var free = BillingApi.FreeTextLine("Follow-up visit", 1m, 0m, 0m);

        var settled = await PostedAsync(client, await client.CreateBillAsync(NewBill(patient, payer, free)));

        settled.Status.ShouldBe(BillStatus.Settled);
        settled.Number.ShouldBe("BIL/26-27/000001");
        settled.Total.ShouldBe(0m);
        settled.BalanceDue.ShouldBe(0m);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(0, "a line of zero is not an entry");
        (await BalanceAsync(clerk, payer)).Receivable.ShouldBe(Money.Zero);
        (await TrailAsync(client, settled.Id)).Select(record => record.Action).ShouldBe(["Drafted", "Posted"]);
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(clerk);
    }

    [Fact]
    public async Task BR006_a_posted_bill_refuses_edit_void_and_a_second_posting()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var posted = await PostedAsync(client, await client.CreateBillAsync(NewBill(patient, payer, BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m))));
        var etag = PayersApi.ETag(posted.Version);

        using var header = await client.SendWithIfMatchAsync(HttpMethod.Patch, BillingApi.Bill(posted.Id), new UpdateBillHeaderRequest { BillDate = Today, DueDate = Today, Notes = "Changed" }, etag);
        using var lines = await client.SendWithIfMatchAsync(HttpMethod.Put, BillingApi.Bill(posted.Id, "lines"), new ReplaceBillLinesRequest { Lines = [] }, etag);
        using var voided = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(posted.Id, "void"), new VoidBillRequest { Reason = "Changed my mind" }, etag);
        using var again = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(posted.Id, "post"), body: null, etag);

        foreach (var refused in new[] { header, lines, voided, again })
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, BillingErrors.BillAlreadyPostedCode);
        }

        using var read = await client.GetAsync(BillingApi.Bill(posted.Id));
        var unchanged = await read.ReadAsAsync<BillDto>();
        unchanged.Version.ShouldBe(posted.Version);
        unchanged.Number.ShouldBe("BIL/26-27/000001");
        unchanged.Notes.ShouldBeNull();
        unchanged.Lines.ShouldHaveSingleItem();
        (await TrailAsync(client, posted.Id)).Select(record => record.Action).ShouldBe(["Drafted", "Posted"]);
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(clerk);
    }

    [Fact]
    public async Task Posting_needs_an_idempotency_key_and_the_version_the_client_read()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var draft = await client.CreateBillAsync(NewBill(patient, payer, BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m)));

        using var withoutKeyRequest = new HttpRequestMessage(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"));
        withoutKeyRequest.Headers.TryAddWithoutValidation("If-Match", PayersApi.ETag(draft.Version));
        using var withoutKey = await client.SendAsync(withoutKeyRequest);
        using var withoutIfMatch = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, ifMatch: null);
        using var stale = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag("AAAAAAAAAAE="));
        using var unknown = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(Guid.NewGuid(), "post"), body: null, PayersApi.ETag(draft.Version));

        await withoutKey.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.IdempotencyKeyMissingCode);
        await withoutIfMatch.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
        await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        await ShouldBeAnUntouchedDraftAsync(client, clerk, draft, payer);
    }

    [Fact]
    public async Task A_posting_that_is_refused_takes_no_number_and_writes_nothing()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var line = BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m);
        var empty = await client.CreateBillAsync(NewBill(patient, payer));
        var (_, inactivePayer) = await client.NewBillPartiesAsync();
        var ofInactivePayer = await client.CreateBillAsync(NewBill(patient, inactivePayer, line));
        using var deactivation = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Payer(inactivePayer.Id, "deactivate"), body: null, PayersApi.ETag(inactivePayer.Version));
        deactivation.StatusCode.ShouldBe(HttpStatusCode.OK);
        var grownOld = await client.CreateBillAsync(NewBill(patient, payer, line));

        using var noLines = await PostAsync(client, empty);
        using var payerInactive = await PostAsync(client, ofInactivePayer);

        // BR-205: eight days later the draft is older than the hospital allows a bill to be back-dated.
        factory.Clock.Advance(TimeSpan.FromDays(8));
        using var tooOld = await PostAsync(client, grownOld);

        await noLines.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.BillHasNoLinesCode);
        await payerInactive.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PayersErrors.PayerInactiveCode);
        await tooOld.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.BillDateTooOldCode);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries")).ShouldBe(0, "no refusal reached the series");
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.AuditRecords WHERE [Action] = 'Posted'")).ShouldBe(0);
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(clerk);
    }

    [Fact]
    public async Task BR105_a_bill_of_an_inactive_patient_whose_payer_is_active_can_still_be_posted()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var draft = await client.CreateBillAsync(NewBill(patient, payer, BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m)));
        using var deactivation = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Patient(patient.Id, "deactivate"), body: null, PayersApi.ETag(patient.Version));
        deactivation.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The draft exists already; an inactive patient only stops new bills (BR-102).
        (await PostedAsync(client, draft)).Status.ShouldBe(BillStatus.Posted);
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(clerk);
    }

    [Theory]
    [InlineData(126.00, false)]
    [InlineData(126.01, true)]
    public async Task BR207_a_bill_at_or_above_the_approval_threshold_waits_for_a_second_person_and_one_below_is_posted(decimal threshold, bool posted)
    {
        // Until Prompt 3.6 the bill at the threshold was refused with 422 bill_requires_approval.
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        await SetApprovalThresholdAsync(clerk, threshold);
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var draft = await client.CreateBillAsync(NewBill(patient, payer, BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m)));

        using var response = await PostAsync(client, draft);
        var bill = await response.ReadAsAsync<BillDto>();

        if (posted)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            bill.Status.ShouldBe(BillStatus.Posted);
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            bill.Status.ShouldBe(BillStatus.PendingApproval);
            bill.Number.ShouldBeNull();
            (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries")).ShouldBe(0, "a bill that waits has taken no number (BR-207)");
            (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(0);
            (await BalanceAsync(clerk, payer)).Receivable.ShouldBe(Money.Zero);
        }

        await ShouldHaveNoDriftAndAGaplessSeriesAsync(clerk);
    }

    [Theory]
    [InlineData("the number series, after the number is taken")]
    [InlineData("the ledger, after the posting is entered")]
    [InlineData("the audit trail, after the record is written")]
    [InlineData("the save itself")]
    public async Task US25_a_failure_at_any_step_leaves_no_number_no_ledger_and_no_audit_rows_and_the_next_posting_gets_the_number(string step)
    {
        var clerk = await NewApiTenantAsync();
        BillDto draft;
        PayerDto payer;
        var key = Guid.NewGuid();

        using (var failing = new DatabaseApiFactory(SqlServer, configure: services => FailAt(step, services)))
        {
            using var client = failing.CreateClientFor(clerk, "BillingClerk");
            (var patient, payer) = await client.NewBillPartiesAsync();
            draft = await client.CreateBillAsync(NewBill(patient, payer, BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m)));

            using var failed = await PostAsync(client, draft, key);

            await failed.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
            await ShouldBeAnUntouchedDraftAsync(client, clerk, draft, payer);
        }

        // The same request again, with the same key, on an API that works: the key was freed by the fault, and the
        // bill gets the number the failed posting had taken and given back.
        using var working = new DatabaseApiFactory(SqlServer);
        using var retrying = working.CreateClientFor(clerk, "BillingClerk");
        using var retried = await PostAsync(retrying, draft, key);

        retried.StatusCode.ShouldBe(HttpStatusCode.OK, await retried.Content.ReadAsStringAsync());
        retried.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        (await retried.ReadAsAsync<BillDto>()).Number.ShouldBe("BIL/26-27/000001");
        (await BalanceAsync(clerk, payer)).Receivable.ShouldBe(Money.Of(126m));
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(clerk);
    }

    [Fact]
    public async Task BR804_two_simultaneous_posts_of_the_same_bill_give_one_success_and_one_conflict_and_use_one_number()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var line = BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m);
        var draft = await client.CreateBillAsync(NewBill(patient, payer, line));

        // Two clerks, two keys, the same version.
        var responses = await Task.WhenAll(PostAsync(client, draft), PostAsync(client, draft));

        try
        {
            responses.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict], ignoreOrder: true);
            var loser = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            await loser.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
            (await responses.Single(response => response.StatusCode == HttpStatusCode.OK).ReadAsAsync<BillDto>()).Number.ShouldBe("BIL/26-27/000001");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(1);
        (await BalanceAsync(clerk, payer)).Receivable.ShouldBe(Money.Of(126m));
        (await TrailAsync(client, draft.Id)).Select(record => record.Action).ShouldBe(["Drafted", "Posted"]);

        // The loser gave its number back: the next bill gets the second number, not the third.
        (await PostedAsync(client, await client.CreateBillAsync(NewBill(patient, payer, line)))).Number.ShouldBe("BIL/26-27/000002");
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(clerk);
    }

    [Fact]
    public async Task BR801_twenty_bills_posted_at_once_get_twenty_consecutive_numbers_and_the_payer_owes_their_sum()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var drafts = new List<BillDto>();
        for (var number = 1; number <= 20; number++)
        {
            drafts.Add(await client.CreateBillAsync(NewBill(patient, payer, BillingApi.FreeTextLine("Dressing", number, 100m, 0m))));
        }

        var posted = await Task.WhenAll(drafts.Select(draft => PostedAsync(client, draft)));

        posted.Select(bill => bill.Number).Order().ShouldBe(Enumerable.Range(1, 20).Select(number => $"BIL/26-27/{number:D6}"));
        (await BalanceAsync(clerk, payer)).Receivable.ShouldBe(Money.Of(21000m), "100 x (1 + 2 + ... + 20)");
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(clerk);
    }

    [Fact]
    public async Task BR803_a_replayed_posting_returns_the_same_response_and_posts_once()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var draft = await client.CreateBillAsync(NewBill(patient, payer, BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m)));
        var key = Guid.NewGuid();

        using var first = await PostAsync(client, draft, key);
        using var replay = await PostAsync(client, draft, key);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.Headers.Contains("Idempotent-Replayed").ShouldBeTrue();
        (await replay.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        replay.Headers.ETag.ShouldBe(first.Headers.ETag);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(1);
        (await BalanceAsync(clerk, payer)).Receivable.ShouldBe(Money.Of(126m));
        (await TrailAsync(client, draft.Id)).Select(record => record.Action).ShouldBe(["Drafted", "Posted"]);
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(clerk);
    }

    [Fact]
    public async Task BR803_the_same_key_while_the_first_posting_is_still_running_answers_409_idempotency_in_progress()
    {
        // The first request is held inside the transaction, after it has taken the number; the second arrives then.
        var gate = new Gate();
        using var factory = new DatabaseApiFactory(SqlServer, configure: services =>
        {
            services.AddSingleton(gate);
            services.AddScoped<NumberSeriesAllocator>();
            services.AddScoped<INumberSeries>(provider => new GatedNumberSeries(provider.GetRequiredService<NumberSeriesAllocator>(), gate));
        });
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var draft = await client.CreateBillAsync(NewBill(patient, payer, BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m)));
        var key = Guid.NewGuid();

        var first = PostAsync(client, draft, key);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        using var second = await PostAsync(client, draft, key);
        gate.Release.SetResult();
        using var firstResponse = await first;

        await second.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.IdempotencyInProgressCode);
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await firstResponse.Content.ReadAsStringAsync());
        (await firstResponse.ReadAsAsync<BillDto>()).Number.ShouldBe("BIL/26-27/000001");

        // Once the first has finished, the same key is a replay of its answer.
        using var replay = await PostAsync(client, draft, key);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.Headers.Contains("Idempotent-Replayed").ShouldBeTrue();
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(1);
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(clerk);
    }

    [Fact]
    public async Task A_second_posting_with_another_key_after_the_first_committed_is_a_conflict_whichever_version_it_sends()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var draft = await client.CreateBillAsync(NewBill(patient, payer, BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m)));
        var posted = await PostedAsync(client, draft);

        // The version the client read before the posting: stale, like any other stale version (BR-804).
        using var withOldVersion = await PostAsync(client, draft);

        // The version of the posted bill: nothing is stale, the bill is simply posted already (BR-006).
        using var withCurrentVersion = await PostAsync(client, posted);

        await withOldVersion.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
        await withCurrentVersion.ShouldBeProblemAsync(HttpStatusCode.Conflict, BillingErrors.BillAlreadyPostedCode);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(1);
        (await BalanceAsync(clerk, payer)).Receivable.ShouldBe(Money.Of(126m));
        (await TrailAsync(client, draft.Id)).Select(record => record.Action).ShouldBe(["Drafted", "Posted"]);

        // Neither refusal reached the series: the next bill gets the second number.
        (await PostedAsync(client, await client.CreateBillAsync(NewBill(patient, payer, BillingApi.FreeTextLine("Dressing", 1m, 60m, 5m))))).Number.ShouldBe("BIL/26-27/000002");
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(clerk);
    }

    [Fact]
    public async Task BR207_a_tenant_without_a_threshold_needs_no_approval_however_large_the_bill()
    {
        // The threshold of a new tenant is null: never (BR-207).
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var large = await client.CreateBillAsync(NewBill(patient, payer, BillingApi.FreeTextLine("Transplant package", 1m, 9_000_000m, 18m)));

        var posted = await PostedAsync(client, large);

        posted.Status.ShouldBe(BillStatus.Posted);
        posted.Total.ShouldBe(10_620_000.00m);
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(clerk);
    }

    [Fact]
    public async Task BR002_another_tenants_bill_cannot_be_posted_and_each_tenant_has_its_own_series()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        var other = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        using var stranger = factory.CreateClientFor(other, "BillingClerk");
        var line = BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m);
        var (patient, payer) = await client.NewBillPartiesAsync();
        var (theirPatient, theirPayer) = await stranger.NewBillPartiesAsync();
        var draft = await client.CreateBillAsync(NewBill(patient, payer, line));

        using var forged = await PostAsync(stranger, draft);

        await forged.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.BillNotFoundCode);
        (await PostedAsync(client, draft)).Number.ShouldBe("BIL/26-27/000001");
        (await PostedAsync(stranger, await stranger.CreateBillAsync(NewBill(theirPatient, theirPayer, line)))).Number.ShouldBe("BIL/26-27/000001");
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(clerk);
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(other);
    }

    // The registrations that make one step of a posting fail after it has done its work inside the transaction.
    private static void FailAt(string step, IServiceCollection services)
    {
        switch (step)
        {
            case "the number series, after the number is taken":
                services.AddScoped<NumberSeriesAllocator>();
                services.AddScoped<INumberSeries>(provider => new NumberThenFault(provider.GetRequiredService<NumberSeriesAllocator>()));
                break;
            case "the ledger, after the posting is entered":
                services.AddScoped<ReceivablesLedger>();
                services.AddScoped<IReceivablesLedger>(provider => new PostingThenFault(provider.GetRequiredService<ReceivablesLedger>()));
                break;
            case "the audit trail, after the record is written":
                // Registered after the audit subscriber, so it runs when the audit record is already in the unit of work.
                services.AddScoped<IDomainEventSubscriber, FaultOnBillPosted>();
                break;
            case "the save itself":
                services.AddScoped<IDomainEventSubscriber, RowTheDatabaseRefuses>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step), step, "No such step.");
        }
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, BillDto bill, Guid? idempotencyKey = null)
        => client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "post"), body: null, PayersApi.ETag(bill.Version), idempotencyKey);

    private static async Task<BillDto> PostedAsync(HttpClient client, BillDto draft)
    {
        using var response = await PostAsync(client, draft);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<BillDto>();
    }

    private static async Task<IReadOnlyList<AuditRecordDto>> TrailAsync(HttpClient client, Guid billId)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/audit-records?entityType=Bill&entityId={billId}", UriKind.Relative));

        return (await response.ReadAsAsync<PageOf<AuditRecordDto>>()).Items;
    }

    // What US-25 promises after a posting that did not happen: the bill is the draft it was, and nothing else exists.
    private async Task ShouldBeAnUntouchedDraftAsync(HttpClient client, FixedIdentity clerk, BillDto draft, PayerDto payer)
    {
        using var read = await client.GetAsync(BillingApi.Bill(draft.Id));
        var bill = await read.ReadAsAsync<BillDto>();
        bill.Status.ShouldBe(BillStatus.Draft);
        bill.Number.ShouldBeNull();
        bill.PostedAtUtc.ShouldBeNull();
        bill.Version.ShouldBe(draft.Version);

        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries")).ShouldBe(0, "no number was consumed (BR-801)");
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerEntries")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.AuditRecords WHERE [Action] = 'Posted'")).ShouldBe(0);
        (await BalanceAsync(clerk, payer)).Receivable.ShouldBe(Money.Zero);
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(clerk);
    }

    // The two things every scenario ends with: the caches equal the ledger (BR-404), and the bill numbers of every
    // fiscal year run from 1 without a gap, with the series pointing at the next one (BR-801).
    private async Task ShouldHaveNoDriftAndAGaplessSeriesAsync(FixedIdentity tenant)
    {
        await using var context = ApiContext(tenant);
        (await new ReconciliationQuery(context, tenant).FindDriftAsync(CancellationToken.None)).ShouldBeEmpty();

        var numbered = await context.Set<Bill>().AsNoTracking().Where(bill => bill.Number != null).ToListAsync();
        foreach (var year in numbered.GroupBy(bill => bill.FiscalYear!.Value))
        {
            year.Select(bill => bill.SequenceNumber!.Value).Order().ShouldBe(Enumerable.Range(1, year.Count()), $"the bills of {year.Key} have a gap");
            var series = await context.Set<NumberSeries>().AsNoTracking().SingleAsync(candidate => candidate.DocumentType == DocumentType.Bill && candidate.FiscalYear == year.Key);
            series.NextNumber.ShouldBe(year.Count() + 1, $"the series of {year.Key} does not point at the next number");
        }

        (await context.Set<NumberSeries>().CountAsync()).ShouldBe(numbered.Select(bill => bill.FiscalYear).Distinct().Count(), "a series exists without a posted bill");
    }

    private async Task<PayerBalance> BalanceAsync(FixedIdentity clerk, PayerDto payer)
    {
        await using var context = ApiContext(clerk);
        var id = new PayerId(payer.Id);

        return await context.Set<PayerBalance>().AsNoTracking().SingleAsync(balance => balance.PayerId == id);
    }

    private async Task SetApprovalThresholdAsync(FixedIdentity tenant, decimal threshold)
    {
        // Tenant settings have no use case yet (Prompt 5.3); the row is changed as system work.
        await using var system = ApiContext(SystemIdentity.Instance);
        var row = await system.Set<Tenant>().SingleAsync(candidate => candidate.Id == tenant.TenantId!.Value);
        system.Entry(row).Property(candidate => candidate.PostingApprovalThreshold).CurrentValue = Money.Of(threshold);
        await system.SaveChangesAsync();
    }

    private async Task<int> CountAsync(string sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQueryRaw<int>(sql).SingleAsync();
    }

    private static CreateBillRequest NewBill(PatientDto patient, PayerDto payer, params BillLineRequest[] lines)
        => new() { PatientId = patient.Id, PayerId = payer.Id, BillDate = Today, DueDate = Today.AddDays(15), Lines = lines };

    // Lets a test hold a posting inside its transaction: Entered is set when the number is taken, and the posting
    // goes on when the test sets Release.
    private sealed class Gate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class GatedNumberSeries(INumberSeries inner, Gate gate) : INumberSeries
    {
        public async Task<DocumentNumber> NextAsync(DocumentType type, FiscalYear fiscalYear, CancellationToken ct)
        {
            var number = await inner.NextAsync(type, fiscalYear, ct);
            gate.Entered.TrySetResult();
            await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

            return number;
        }
    }

    private sealed class NumberThenFault(INumberSeries inner) : INumberSeries
    {
        public async Task<DocumentNumber> NextAsync(DocumentType type, FiscalYear fiscalYear, CancellationToken ct)
        {
            await inner.NextAsync(type, fiscalYear, ct);

            throw new InvalidOperationException("Injected: the number series failed after it took the number.");
        }
    }

    private sealed class PostingThenFault(IReceivablesLedger inner) : IReceivablesLedger
    {
        public async Task<Result> PostAsync(PostingTemplate template, CancellationToken ct)
        {
            await inner.PostAsync(template, ct);

            throw new InvalidOperationException("Injected: the ledger failed after it entered the posting.");
        }
    }

    private sealed class FaultOnBillPosted : IDomainEventSubscriber
    {
        public Task HandleAsync(DomainEventOccurrence occurrence, CancellationToken ct)
            => occurrence.Event is BillPosted
                ? throw new InvalidOperationException("Injected: a subscriber failed after the audit record was written.")
                : Task.CompletedTask;
    }

    // Adds, with the posting, a payer that names a patient nobody has: the foreign key refuses the whole save.
    private sealed class RowTheDatabaseRefuses(Infrastructure.Common.Persistence.BillingReconDbContext context) : IDomainEventSubscriber
    {
        public Task HandleAsync(DomainEventOccurrence occurrence, CancellationToken ct)
        {
            if (occurrence.Event is BillPosted)
            {
                context.Add(Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "Nobody" }, new PatientId(Guid.NewGuid())).Value);
            }

            return Task.CompletedTask;
        }
    }
}
