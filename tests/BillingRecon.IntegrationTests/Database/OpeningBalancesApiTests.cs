using System.Globalization;
using System.Net;
using BillingRecon.Api.Identity;
using BillingRecon.Api.Payers;
using BillingRecon.Api.Payments;
using BillingRecon.Api.Receivables;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Application.Receivables;
using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Payments;
using BillingRecon.Domain.Receivables;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Bringing in a hospital's records and opening balances end to end, on the pipeline that ships and a real SQL Server
/// (Prompt 10.3, ADR 0053): the dry runs write nothing and name every rejected row; patients are matched by MRN and
/// never merged; a batch is submitted by one person and posted by the approval of another, in one transaction, against
/// the account OpeningBalances, under numbers of its own series; the same batch is not brought in twice; an opening
/// receivable is settled and an opening advance applied like the documents they stand for; and the export of the
/// reference hospital imports whole, to the totals of its trial balance. Every test ends with the reconciliation of
/// every tenant, as every test of the collection does.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class OpeningBalancesApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly DateOnly CutOver = new(2026, 10, 1);
    private static readonly Uri Parties = new("/api/v1/imports/parties", UriKind.Relative);
    private static readonly Uri Batches = new("/api/v1/opening-batches", UriKind.Relative);
    private static readonly Uri DryRun = new("/api/v1/opening-batches/dry-run", UriKind.Relative);

    private const string Patients =
        """
        mrn,full_name,date_of_birth,sex,phone,city
        NF-1,Asha Rao,1984-03-12,Female,+91 00000 00001,Pune
        NF-2,Ravi Iyer,,,,
        """;

    private const string Guarantors =
        """
        guarantor_ref,name,phone
        G-1,Mohan Rao,+91 00000 90001
        """;

    private const string Bills =
        """
        legacy_number,mrn,guarantor_ref,bill_date,due_date,outstanding_amount
        OLD-1,NF-1,,2026-08-10,2026-08-25,1000.00
        OLD-2,NF-2,G-1,2026-09-01,,250.50
        """;

    private const string Advances =
        """
        legacy_reference,mrn,guarantor_ref,received_date,amount,method,kind
        ADV-1,NF-1,,2026-09-20,300.00,UPI,
        CR-1,,G-1,2026-09-28,75.25,Cash,Credit
        """;

    private const string TrialBalance =
        """
        account,amount
        Receivable,1250.50
        Advance,300.00
        Credit,75.25
        """;

    private static OpeningBatchFilesRequest Files(string bills = Bills, string? advances = Advances, string trialBalance = TrialBalance, DateOnly? cutOver = null)
        => new() { CutOverDate = cutOver ?? CutOver, BillsCsv = bills, AdvancesCsv = advances, TrialBalanceCsv = trialBalance };

    private static Uri Batch(Guid id, string? action = null) => new($"/api/v1/opening-batches/{id}{(action is null ? string.Empty : "/" + action)}", UriKind.Relative);

    [Fact]
    public async Task ADR0053_the_dry_runs_write_nothing_and_name_every_rejected_row_with_its_line_and_reason()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");

        // A patient twice, a row without a name, and a date that is no date: three rows, three findings, nothing written.
        const string faulty =
            """
            mrn,full_name,date_of_birth
            NF-1,Asha Rao,1984-03-12
            nf-1,Asha Rao,
            NF-3,,
            NF-4,Meera Nair,12/03/1984
            """;
        var checkedParties = await ImportAsync(client, faulty, Guarantors, dryRun: true);
        (checkedParties.DryRun, checkedParties.Written, checkedParties.PatientsCreated, checkedParties.GuarantorsCreated).ShouldBe((true, false, 1, 1));
        checkedParties.Rejected.Select(finding => $"{finding.File}:{finding.Line}").ShouldBe(["patients.csv:3", "patients.csv:4", "patients.csv:5"]);
        checkedParties.Rejected[0].Reason.ShouldContain("is on line 2 too (BR-101)");
        checkedParties.Rejected[2].Reason.ShouldContain("yyyy-MM-dd");

        // Without the dry run the same files write nothing either: one rejected row stops them all.
        var refused = await ImportAsync(client, faulty, Guarantors, dryRun: false);
        (refused.Written, refused.Rejected.Count).ShouldBe((false, 3));
        await CountsShouldBeAsync(admin, patients: 0, payers: 0, bills: 0, payments: 0, batches: 0, postings: 0);

        (await ImportAsync(client, Patients, Guarantors, dryRun: false)).Written.ShouldBeTrue();

        // The opening batch: a bill of a patient who is not here, and an amount with three decimals.
        var faultyBills = Bills + "\nOLD-3,NF-9,,2026-09-02,,10.00\nOLD-4,NF-1,,2026-09-02,,10.005";
        var found = await CheckAsync(client, Files(bills: faultyBills));
        found.Accepted.ShouldBeFalse();
        found.Rejected.Select(finding => $"{finding.File}:{finding.Line}").ShouldBe(["bills.csv:4", "bills.csv:5"]);
        found.Rejected[0].Reason.ShouldContain("NF-9 is not a patient here");

        var accepted = await CheckAsync(client, Files());
        (accepted.Accepted, accepted.Receivables, accepted.ReceivableTotal, accepted.Advances, accepted.AdvanceTotal, accepted.CreditBalanceTotal)
            .ShouldBe((true, 2, 1250.50m, 2, 375.25m, 75.25m));
        accepted.Rejected.ShouldBeEmpty();

        using var submitted = await client.PostJsonAsync(Batches, Files(bills: faultyBills));
        await submitted.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, ReceivablesErrors.OpeningBatchHasRejectionsCode);
        await CountsShouldBeAsync(admin, patients: 2, payers: 3, bills: 0, payments: 0, batches: 0, postings: 0);
    }

    [Fact]
    public async Task ADR0053_patients_are_matched_by_their_mrn_and_never_merged_and_the_same_files_can_be_sent_again()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");

        var first = await ImportAsync(client, Patients, Guarantors, dryRun: false);
        (first.Written, first.PatientsCreated, first.PatientsMatched, first.PayersCreated, first.GuarantorsCreated, first.GuarantorsMatched)
            .ShouldBe((true, 2, 0, 2, 1, 0));

        await using (var context = ApiContext(admin))
        {
            var payers = await context.Set<Payer>().AsNoTracking().ToListAsync();
            payers.Count(payer => payer.PatientId is not null).ShouldBe(2, "each patient is the self-pay payer it is (BR-104)");
            payers.Single(payer => payer.PatientId is null).LegacyReference.ShouldBe("G-1");
            (await context.Set<PayerBalance>().CountAsync()).ShouldBe(3, "every payer has its row of balances (BR-404)");
        }

        var again = await ImportAsync(client, Patients, Guarantors, dryRun: false);
        (again.Written, again.PatientsCreated, again.PatientsMatched, again.PayersCreated, again.GuarantorsCreated, again.GuarantorsMatched)
            .ShouldBe((true, 0, 2, 0, 0, 1));
        await CountsShouldBeAsync(admin, patients: 2, payers: 3, bills: 0, payments: 0, batches: 0, postings: 0);

        // The MRN of a patient under another name is rejected, and the new patient beside it is not written.
        var other = await ImportAsync(client, "mrn,full_name\nNF-1,Asha Kulkarni\nNF-7,Dev Shah", guarantorsCsv: null, dryRun: false);
        other.Written.ShouldBeFalse();
        var finding = other.Rejected.ShouldHaveSingleItem();
        (finding.File, finding.Line).ShouldBe(("patients.csv", 2));
        finding.Reason.ShouldContain("under another name");
        finding.Reason.ShouldNotContain("Rao");
        await CountsShouldBeAsync(admin, patients: 2, payers: 3, bills: 0, payments: 0, batches: 0, postings: 0);
    }

    [Fact]
    public async Task ADR0053_a_batch_is_submitted_by_one_person_and_posted_whole_by_the_approval_of_another()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var maker = factory.CreateClientFor(admin, "TenantAdmin");
        using var checker = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "Supervisor");
        await ImportAsync(maker, Patients, Guarantors, dryRun: false);

        using var submitted = await maker.PostJsonAsync(Batches, Files());
        submitted.StatusCode.ShouldBe(HttpStatusCode.Created, await submitted.Content.ReadAsStringAsync());
        var batch = await submitted.ReadAsAsync<OpeningBatchDto>();
        (batch.Status, batch.CutOverDate, batch.Receivables, batch.ReceivableTotal, batch.Advances, batch.AdvanceTotal, batch.SubmittedByUserId)
            .ShouldBe((OpeningBatchStatus.PendingApproval, CutOver, 2, 1250.50m, 2, 375.25m, admin.UserId!.Value.Value));
        (await submitted.Content.ReadAsStringAsync()).ShouldNotContain("OLD-1", Case.Sensitive, "the files are never answered back");

        // Nothing is posted while the batch waits, and its maker cannot approve it (BR-603).
        await CountsShouldBeAsync(admin, patients: 2, payers: 3, bills: 0, payments: 0, batches: 1, postings: 0);
        using (var own = await maker.SendWithIfMatchAsync(HttpMethod.Post, Batch(batch.Id, "approve"), body: null, PayersApi.ETag(batch.Version)))
        {
            await own.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        }

        using (var noVersion = await checker.SendWithIfMatchAsync(HttpMethod.Post, Batch(batch.Id, "approve"), body: null, ifMatch: null))
        {
            noVersion.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        }

        using var approved = await checker.SendWithIfMatchAsync(HttpMethod.Post, Batch(batch.Id, "approve"), new DecideOpeningBatchRequest { Comment = "Agreed with the old system's ledger" }, PayersApi.ETag(batch.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var done = await approved.ReadAsAsync<OpeningBatchDto>();
        (done.Status, done.DecidedAtUtc).ShouldBe((OpeningBatchStatus.Approved, DatabaseApiFactory.InitialInstant));

        await using var context = ApiContext(admin);
        var bills = await context.Set<Bill>().AsNoTracking().OrderBy(bill => bill.SequenceNumber).ToListAsync();
        bills.Select(bill => (bill.Number, bill.LegacyNumber, bill.Origin, bill.Status, bill.Total.Amount, bill.BalanceDue.Amount, bill.BillDate, bill.DueDate)).ShouldBe(
        [
            ("OBR/26-27/000001", "OLD-1", DocumentOrigin.Opening, BillStatus.Posted, 1000.00m, 1000.00m, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 25)),
            ("OBR/26-27/000002", "OLD-2", DocumentOrigin.Opening, BillStatus.Posted, 250.50m, 250.50m, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1)),
        ]);
        var guarantor = await context.Set<Payer>().AsNoTracking().SingleAsync(payer => payer.LegacyReference == "G-1");
        bills[1].PayerId.ShouldBe(guarantor.Id, "the guarantor owes the second bill, not the patient");

        var payments = await context.Set<Payment>().AsNoTracking().OrderBy(payment => payment.SequenceNumber).ToListAsync();
        payments.Select(payment => (payment.Number, payment.LegacyNumber, payment.Reference, payment.Origin, payment.Status, payment.Amount.Amount, payment.UnappliedAmount.Amount, payment.Method, payment.PaymentDate)).ShouldBe(
        [
            ("OBA/26-27/000001", "ADV-1", "ADV-1", DocumentOrigin.Opening, PaymentStatus.Posted, 300.00m, 300.00m, PaymentMethod.UPI, new DateOnly(2026, 9, 20)),
            ("OBA/26-27/000002", "CR-1", "CR-1", DocumentOrigin.Opening, PaymentStatus.Posted, 75.25m, 75.25m, PaymentMethod.Cash, new DateOnly(2026, 9, 28)),
        ]);
        payments[1].Notes.ShouldNotBeNull().ShouldContain("credit balance");

        // The ledger: every opening posting is dated the day before the cut-over and has OpeningBalances as its other
        // side; nothing is revenue and nothing is cash received.
        var postings = await context.Set<LedgerPosting>().AsNoTracking().ToListAsync();
        postings.Count.ShouldBe(4);
        postings.ShouldAllBe(posting => posting.BusinessDate == CutOver.AddDays(-1));
        postings.Count(posting => posting.Kind == LedgerPostingKind.OpeningReceivablePosted).ShouldBe(2);
        postings.Count(posting => posting.Kind == LedgerPostingKind.OpeningAdvancePosted).ShouldBe(2);
        var entries = await context.Set<LedgerEntry>().AsNoTracking().ToListAsync();
        entries.Select(entry => entry.Account).Distinct().ShouldBe([LedgerAccount.Receivable, LedgerAccount.OpeningBalances, LedgerAccount.UnappliedCash], ignoreOrder: true);
        entries.Where(entry => entry.Account == LedgerAccount.OpeningBalances).Sum(entry => entry.Credit.Amount - entry.Debit.Amount).ShouldBe(1250.50m - 375.25m);

        // The two opening series, each gapless from 1 in the fiscal year of the cut-over, beside no series of bills.
        var series = await context.Set<NumberSeries>().AsNoTracking().OrderBy(row => row.DocumentType).ToListAsync();
        series.Select(row => (row.DocumentType, row.Prefix, row.NextNumber)).ShouldBe(
            [(DocumentType.OpeningReceivable, "OBR", 3), (DocumentType.OpeningAdvance, "OBA", 3)], ignoreOrder: true);

        // What each payer owes and holds is what the old system said.
        var outstanding = await OutstandingAsync(maker);
        (outstanding.Totals.Receivable, outstanding.Totals.UnappliedCash).ShouldBe((1250.50m, 375.25m));
        outstanding.Items.Single(row => row.PayerId == guarantor.Id.Value).ShouldSatisfyAllConditions(
            row => row.Receivable.ShouldBe(250.50m),
            row => row.UnappliedCash.ShouldBe(75.25m));

        // The answers say where the documents come from, and the trail what was done and by whom.
        using var read = await maker.GetAsync(BillingApi.Bill(bills[0].Id.Value));
        var answered = await read.ReadAsAsync<BillDto>();
        (answered.Origin, answered.LegacyNumber, answered.Number).ShouldBe((DocumentOrigin.Opening, "OLD-1", "OBR/26-27/000001"));
        (await TrailAsync(maker, "OpeningBatch", batch.Id)).Select(record => record.Action).ShouldBe(["Submitted", "Approved"]);
        (await TrailAsync(maker, "OpeningBatch", batch.Id)).ShouldAllBe(record => !record.After.GetRawText().Contains("OLD-1", StringComparison.Ordinal));
        var billTrail = (await TrailAsync(maker, "Bill", bills[0].Id.Value)).ShouldHaveSingleItem();
        (billTrail.Action, billTrail.DocumentNumber).ShouldBe(("BroughtForward", "OBR/26-27/000001"));
        billTrail.UserId.ShouldNotBe(admin.UserId!.Value.Value, "the checker brought it in, not the maker");

        // Decided once.
        using var twice = await checker.SendWithIfMatchAsync(HttpMethod.Post, Batch(batch.Id, "approve"), body: null, PayersApi.ETag(done.Version));
        await twice.ShouldBeProblemAsync(HttpStatusCode.Conflict, ReceivablesErrors.OpeningBatchNotPendingCode);
    }

    [Fact]
    public async Task ADR0053_the_same_batch_is_not_brought_in_twice_and_a_rejected_one_can_be_submitted_again()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var maker = factory.CreateClientFor(admin, "TenantAdmin");
        using var checker = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "TenantAdmin");
        await ImportAsync(maker, Patients, Guarantors, dryRun: false);

        var first = await SubmitAsync(maker, Files());
        using (var whileItWaits = await maker.PostJsonAsync(Batches, Files()))
        {
            await whileItWaits.ShouldBeProblemAsync(HttpStatusCode.Conflict, ReceivablesErrors.OpeningBatchDuplicateCode);
        }

        // A rejection needs its reason, posts nothing, and frees the files.
        using (var noReason = await checker.SendWithIfMatchAsync(HttpMethod.Post, Batch(first.Id, "reject"), new DecideOpeningBatchRequest(), PayersApi.ETag(first.Version)))
        {
            await noReason.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        }

        using (var rejected = await checker.SendWithIfMatchAsync(HttpMethod.Post, Batch(first.Id, "reject"), new DecideOpeningBatchRequest { Comment = "Wait for the March closing" }, PayersApi.ETag(first.Version)))
        {
            (await rejected.ReadAsAsync<OpeningBatchDto>()).Status.ShouldBe(OpeningBatchStatus.Rejected);
        }

        await CountsShouldBeAsync(admin, patients: 2, payers: 3, bills: 0, payments: 0, batches: 1, postings: 0);

        var second = await SubmitAsync(maker, Files());
        second.Id.ShouldNotBe(first.Id);
        using (var approved = await checker.SendWithIfMatchAsync(HttpMethod.Post, Batch(second.Id, "approve"), body: null, PayersApi.ETag(second.Version)))
        {
            approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        }

        // Once brought in: the same files are a duplicate, and any other file with one of their numbers has that row rejected.
        using (var afterwards = await maker.PostJsonAsync(Batches, Files()))
        {
            await afterwards.ShouldBeProblemAsync(HttpStatusCode.Conflict, ReceivablesErrors.OpeningBatchDuplicateCode);
        }

        var partly = await CheckAsync(maker, Files(bills: "legacy_number,mrn,bill_date,outstanding_amount\nOLD-1,NF-1,2026-08-10,1000.00\nOLD-9,NF-1,2026-08-11,5.00", advances: null, trialBalance: "account,amount\nReceivable,1005.00"));
        partly.Rejected.ShouldHaveSingleItem().Reason.ShouldContain("OLD-1 was brought in before");
        await CountsShouldBeAsync(admin, patients: 2, payers: 3, bills: 2, payments: 2, batches: 2, postings: 4);

        var list = await ListAsync(checker, "status=Approved");
        list.Items.ShouldHaveSingleItem().Id.ShouldBe(second.Id);
        (await ListAsync(checker, string.Empty)).TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task ADR0053_old_numbers_and_guarantor_references_are_the_same_whatever_their_case_as_the_database_holds_them()
    {
        // Review 2026-11, finding R10: the checks compared by case and the unique indexes do not. Two numbers that
        // differ in case passed the dry run and the submission, and the batch could then never be approved (409
        // duplicate_key, no row named); a guarantor written in another case was "not a guarantor here".
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var maker = factory.CreateClientFor(admin, "TenantAdmin");
        using var checker = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "TenantAdmin");
        await ImportAsync(maker, Patients, Guarantors, dryRun: false);

        // A guarantor file that names one guarantor twice, in two cases, is told so.
        var twice = await ImportAsync(maker, Patients, "guarantor_ref,name,phone\nG-7,Mohan Rao,\ng-7,Mohan Rao,", dryRun: true);
        twice.Rejected.ShouldHaveSingleItem().Reason.ShouldContain("is on line 2 too");

        // The guarantor of the files in small letters is the guarantor that was imported.
        var small = Files(bills: Bills.Replace("G-1", "g-1", StringComparison.Ordinal), advances: Advances.Replace("G-1", "g-1", StringComparison.Ordinal));
        var check = await CheckAsync(maker, small);
        check.Rejected.ShouldBeEmpty();
        check.Accepted.ShouldBeTrue();

        // Two rows of one number in two cases are named by the dry run, each file for itself.
        var doubled = await CheckAsync(maker, Files(
            bills: "legacy_number,mrn,bill_date,outstanding_amount\nOB-1,NF-1,2026-08-10,1000.00\nob-1,NF-1,2026-08-11,5.00",
            advances: "legacy_reference,mrn,guarantor_ref,received_date,amount,method,kind\nADV-1,NF-1,,2026-09-20,300.00,UPI,\nadv-1,NF-1,,2026-09-21,1.00,UPI,",
            trialBalance: "account,amount\nReceivable,1005.00\nAdvance,301.00\nCredit,0.00"));
        doubled.Rejected.Select(finding => $"{finding.File}:{finding.Line}").ShouldBe(["bills.csv:3", "advances.csv:3"], ignoreOrder: true);
        doubled.Rejected.ShouldAllBe(finding => finding.Reason.Contains("is on line 2 too", StringComparison.Ordinal));

        // And what was brought in is found again in any case: the batch in small letters posts, and its numbers in
        // capitals are then "brought in before".
        var batch = await SubmitAsync(maker, small);
        using (var approved = await checker.SendWithIfMatchAsync(HttpMethod.Post, Batch(batch.Id, "approve"), body: null, PayersApi.ETag(batch.Version)))
        {
            approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        }

        var again = await CheckAsync(maker, Files(
            bills: "legacy_number,mrn,bill_date,outstanding_amount\nold-1,NF-1,2026-08-10,1000.00",
            advances: "legacy_reference,mrn,guarantor_ref,received_date,amount,method,kind\nadv-1,NF-1,,2026-09-20,300.00,UPI,",
            trialBalance: "account,amount\nReceivable,1000.00\nAdvance,300.00\nCredit,0.00"));
        again.Rejected.Count.ShouldBe(2);
        again.Rejected.ShouldAllBe(finding => finding.Reason.Contains("was brought in before", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADR0053_an_opening_receivable_is_settled_by_a_payment_and_an_opening_advance_is_applied_but_never_reversed()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var maker = factory.CreateClientFor(admin, "TenantAdmin");
        using var checker = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "Supervisor");
        await ImportAsync(maker, Patients, Guarantors, dryRun: false);
        var batch = await SubmitAsync(maker, Files());
        using (var approved = await checker.SendWithIfMatchAsync(HttpMethod.Post, Batch(batch.Id, "approve"), body: null, PayersApi.ETag(batch.Version)))
        {
            approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        }

        Bill first, second;
        Payment credit;
        await using (var context = ApiContext(admin))
        {
            first = await context.Set<Bill>().AsNoTracking().SingleAsync(bill => bill.LegacyNumber == "OLD-1");
            second = await context.Set<Bill>().AsNoTracking().SingleAsync(bill => bill.LegacyNumber == "OLD-2");
            credit = await context.Set<Payment>().AsNoTracking().SingleAsync(payment => payment.LegacyNumber == "CR-1");
        }

        // A payment recorded here settles the opening receivable of its payer.
        var today = DateOnly.FromDateTime(DatabaseApiFactory.InitialInstant.UtcDateTime.AddHours(5.5));
        await maker.EnsureShiftAsync();
        using var recorded = await maker.PostJsonAsync(new Uri("/api/v1/payments", UriKind.Relative), new RecordPaymentRequest { PayerId = first.PayerId.Value, PaymentDate = today, Method = PaymentMethod.Cash, Amount = 1000.00m });
        recorded.StatusCode.ShouldBe(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync());
        var payment = await recorded.ReadAsAsync<PaymentDto>();
        (payment.Origin, payment.Number).ShouldBe((DocumentOrigin.Standard, "PAY/26-27/000001"));
        using (var allocated = await AllocateAsync(maker, payment.Id, payment.Version, first.Id.Value, 1000.00m))
        {
            allocated.StatusCode.ShouldBe(HttpStatusCode.OK, await allocated.Content.ReadAsStringAsync());
        }

        // The opening credit balance of the guarantor is applied to the opening receivable the guarantor owes.
        using var readCredit = await maker.GetAsync(new Uri($"/api/v1/payments/{credit.Id.Value}", UriKind.Relative));
        var opening = await readCredit.ReadAsAsync<PaymentDto>();
        (opening.Origin, opening.LegacyNumber, opening.Number).ShouldBe((DocumentOrigin.Opening, "CR-1", "OBA/26-27/000002"));
        using (var applied = await AllocateAsync(maker, opening.Id, opening.Version, second.Id.Value, 75.25m))
        {
            applied.StatusCode.ShouldBe(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        }

        await using (var context = ApiContext(admin))
        {
            var bills = await context.Set<Bill>().AsNoTracking().OrderBy(bill => bill.SequenceNumber).ToListAsync();
            bills.Select(bill => (bill.Status, bill.BalanceDue.Amount)).ShouldBe([(BillStatus.Settled, 0m), (BillStatus.PartiallyPaid, 175.25m)]);
        }

        // No money came in here with an opening advance, so there is none to take back: it is refunded, not reversed.
        using var readAdvance = await maker.GetAsync(new Uri($"/api/v1/payments/{opening.Id}", UriKind.Relative));
        var current = await readAdvance.ReadAsAsync<PaymentDto>();
        using var reversal = await maker.SendWithIfMatchAsync(HttpMethod.Post, new Uri($"/api/v1/payments/{opening.Id}/reverse", UriKind.Relative), new RequestReversalRequest { Reason = "Brought in by mistake" }, PayersApi.ETag(current.Version));
        await reversal.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.OpeningAdvanceNotReversibleCode);
    }

    [Fact]
    public async Task ADR0053_the_approval_checks_the_files_again_and_writes_nothing_when_a_row_no_longer_holds()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var maker = factory.CreateClientFor(admin, "TenantAdmin");
        using var checker = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "Supervisor");
        await ImportAsync(maker, Patients, Guarantors, dryRun: false);
        var batch = await SubmitAsync(maker, Files());

        // While the batch waits, a patient it names is deactivated (BR-102).
        using var patients = await maker.GetAsync(PayersApi.Patients.WithQuery("search=NF-1"));
        var asha = (await patients.ReadAsAsync<PageOf<PatientDto>>()).Items.ShouldHaveSingleItem();
        using (var deactivated = await maker.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Patient(asha.Id, "deactivate"), body: null, PayersApi.ETag(asha.Version)))
        {
            deactivated.StatusCode.ShouldBe(HttpStatusCode.OK, await deactivated.Content.ReadAsStringAsync());
        }

        using var approval = await checker.SendWithIfMatchAsync(HttpMethod.Post, Batch(batch.Id, "approve"), body: null, PayersApi.ETag(batch.Version));
        await approval.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, ReceivablesErrors.OpeningBatchHasRejectionsCode);

        await CountsShouldBeAsync(admin, patients: 2, payers: 3, bills: 0, payments: 0, batches: 1, postings: 0);
        using var read = await checker.GetAsync(Batch(batch.Id));
        (await read.ReadAsAsync<OpeningBatchDto>()).Status.ShouldBe(OpeningBatchStatus.PendingApproval, "the refused approval decided nothing");
    }

    [Fact]
    public async Task ADR0053_a_batch_is_its_tenants_alone_and_no_hospital_takes_an_opening_prefix()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        var elsewhere = await NewApiTenantAsync();
        using var maker = factory.CreateClientFor(admin, "TenantAdmin");
        using var other = factory.CreateClientFor(elsewhere, "TenantAdmin");
        await ImportAsync(maker, Patients, Guarantors, dryRun: false);
        var batch = await SubmitAsync(maker, Files());

        using var foreign = await other.GetAsync(Batch(batch.Id));
        await foreign.ShouldBeProblemAsync(HttpStatusCode.NotFound, ReceivablesErrors.OpeningBatchNotFoundCode);
        (await ListAsync(other, string.Empty)).TotalCount.ShouldBe(0);

        // The other hospital has no such patients: the same files hold nothing it can bring in.
        (await CheckAsync(other, Files())).Accepted.ShouldBeFalse();

        var prefixChanges = new Uri("/api/v1/settings/prefix-changes", UriKind.Relative);
        using var taken = await maker.PostJsonAsync(prefixChanges, new RequestPrefixChangeRequest { DocumentType = DocumentType.Bill, Prefix = "OBR" });
        await taken.ShouldBeProblemAsync(HttpStatusCode.Conflict, IdentityErrors.PrefixInUseCode);
        using var fixedType = await maker.PostJsonAsync(prefixChanges, new RequestPrefixChangeRequest { DocumentType = DocumentType.OpeningAdvance, Prefix = "OPN" });
        await fixedType.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task ADR0058_the_export_of_the_reference_hospital_imports_whole_to_the_totals_of_its_trial_balance()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var maker = factory.CreateClientFor(admin, "TenantAdmin");
        using var checker = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "Supervisor");

        // The day after the reference hospital's cut-over.
        var cutOver = new DateOnly(2027, 4, 1);
        factory.Clock.Set(new DateTimeOffset(2027, 4, 2, 5, 0, 0, TimeSpan.Zero));
        string File(string name) => System.IO.File.ReadAllText(Path.Combine(RepositoryRoot(), "deploy", "reference", "export", name));

        var parties = await ImportAsync(maker, File("patients.csv"), File("guarantors.csv"), dryRun: false);
        (parties.Written, parties.PatientsCreated, parties.PayersCreated, parties.GuarantorsCreated, parties.Rejected.Count).ShouldBe((true, 2000, 2000, 12, 0));

        var files = Files(File("bills.csv"), File("advances.csv"), File("trial_balance.csv"), cutOver);
        var found = await CheckAsync(maker, files);
        found.Rejected.ShouldBeEmpty();
        (found.Receivables, found.Advances, found.ReceivableTotal, found.AdvanceTotal, found.CreditBalanceTotal).ShouldBe((300, 55, 6584094.91m, 540270.61m, 174519.00m));

        var batch = await SubmitAsync(maker, files);
        using (var approved = await checker.SendWithIfMatchAsync(HttpMethod.Post, Batch(batch.Id, "approve"), body: null, PayersApi.ETag(batch.Version)))
        {
            approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        }

        await CountsShouldBeAsync(admin, patients: 2000, payers: 2012, bills: 300, payments: 55, batches: 1, postings: 355);

        // The imported totals are the trial balance of the export.
        var outstanding = await OutstandingAsync(maker);
        (outstanding.Totals.Receivable, outstanding.Totals.UnappliedCash, outstanding.Totals.CreditAvailable).ShouldBe((6584094.91m, 540270.61m, 0m));

        // And payer by payer: what each owes and holds here is what its rows of the export come to.
        var expected = ExpectedByPayer(File("bills.csv"), File("advances.csv"));
        var statements = new List<(decimal Receivable, decimal Unapplied)>();
        for (var page = 1; statements.Count < outstanding.TotalCount; page++)
        {
            using var response = await maker.GetAsync(new Uri($"/api/v1/reports/outstanding-by-payer?page={page}&pageSize=100", UriKind.Relative));
            var rows = (await response.ReadAsAsync<ReportPage<PayerOutstandingDto, OutstandingTotalsDto>>()).Items;
            rows.ShouldNotBeEmpty();
            statements.AddRange(rows.Select(row => (row.Receivable, row.UnappliedCash)));
        }

        statements.Order().ShouldBe(expected.Order());

        await using var context = ApiContext(admin);
        (await context.Set<Bill>().AsNoTracking().MaxAsync(bill => bill.SequenceNumber)).ShouldBe(300);
        (await context.Set<Bill>().AsNoTracking().Select(bill => bill.Number!).MinAsync()).ShouldBe("OBR/27-28/000001");
        (await context.Set<Payment>().AsNoTracking().Select(payment => payment.Number).MaxAsync()).ShouldBe("OBA/27-28/000055");
    }

    // What each payer of the export owes and holds: its rows of the two files summed, a payer being the guarantor a row
    // names or else the patient.
    private static List<(decimal Receivable, decimal Unapplied)> ExpectedByPayer(string bills, string advances)
    {
        var byPayer = new Dictionary<string, (decimal Receivable, decimal Unapplied)>(StringComparer.Ordinal);
        void Add(string text, string amountColumn, bool receivable)
        {
            var table = CsvTable.Parse(text);
            foreach (var row in table.Rows)
            {
                var payer = table.Field(row, "guarantor_ref") ?? table.Field(row, "mrn")!;
                var amount = decimal.Parse(table.Field(row, amountColumn)!, CultureInfo.InvariantCulture);
                var sums = byPayer.GetValueOrDefault(payer);
                byPayer[payer] = receivable ? (sums.Receivable + amount, sums.Unapplied) : (sums.Receivable, sums.Unapplied + amount);
            }
        }

        Add(bills, "outstanding_amount", receivable: true);
        Add(advances, "amount", receivable: false);

        return [.. byPayer.Values];
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BillingRecon.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root, where BillingRecon.sln is, was not found above the test.");
    }

    private static async Task<PartyImportDto> ImportAsync(HttpClient client, string patientsCsv, string? guarantorsCsv, bool dryRun)
    {
        using var response = await client.PostJsonAsync(Parties, new ImportPartiesRequest { PatientsCsv = patientsCsv, GuarantorsCsv = guarantorsCsv, DryRun = dryRun });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PartyImportDto>();
    }

    private static async Task<OpeningBatchCheckDto> CheckAsync(HttpClient client, OpeningBatchFilesRequest files)
    {
        using var response = await client.PostJsonAsync(DryRun, files);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<OpeningBatchCheckDto>();
    }

    private static async Task<OpeningBatchDto> SubmitAsync(HttpClient client, OpeningBatchFilesRequest files)
    {
        using var response = await client.PostJsonAsync(Batches, files);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<OpeningBatchDto>();
    }

    private static async Task<PageOf<OpeningBatchDto>> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(query.Length == 0 ? Batches : Batches.WithQuery(query));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PageOf<OpeningBatchDto>>();
    }

    private static Task<HttpResponseMessage> AllocateAsync(HttpClient client, Guid paymentId, string version, Guid billId, decimal amount)
        => client.SendWithIfMatchAsync(
            HttpMethod.Post,
            new Uri($"/api/v1/payments/{paymentId}/allocations", UriKind.Relative),
            new AllocatePaymentRequest { Allocations = [new AllocationRequest { BillId = billId, Amount = amount }] },
            PayersApi.ETag(version));

    private static async Task<ReportPage<PayerOutstandingDto, OutstandingTotalsDto>> OutstandingAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/api/v1/reports/outstanding-by-payer?pageSize=100", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<ReportPage<PayerOutstandingDto, OutstandingTotalsDto>>();
    }

    private static async Task<IReadOnlyList<AuditRecordDto>> TrailAsync(HttpClient client, string entityType, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/audit-records?entityType={entityType}&entityId={id}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.ReadAsAsync<PageOf<AuditRecordDto>>()).Items;
    }

    private async Task CountsShouldBeAsync(FixedIdentity identity, int patients, int payers, int bills, int payments, int batches, int postings)
    {
        await using var context = ApiContext(identity);

        (
            await context.Set<Patient>().CountAsync(),
            await context.Set<Payer>().CountAsync(),
            await context.Set<Bill>().CountAsync(),
            await context.Set<Payment>().CountAsync(),
            await context.Set<OpeningBatch>().CountAsync(),
            await context.Set<LedgerPosting>().CountAsync())
            .ShouldBe((patients, payers, bills, payments, batches, postings));
    }
}
