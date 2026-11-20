using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.Infrastructure.Receivables;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The receivables ledger on SQL Server, through its port and without the API around it (ADR 0005; BR-401 to BR-406):
/// a posting and the balances of its payer are written by one commit and taken back together, a balance never goes
/// below zero, ledger rows cannot be changed, and the reconciliation is empty when the caches equal the ledger and
/// says where they do not.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ReceivablesOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Guid Correlation = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3305");
    private static readonly DateOnly Today = new(2026, 10, 17);

    [Fact]
    public async Task The_tables_have_the_documented_constraints_and_indexes_each_led_by_the_tenant()
    {
        (await NamesAsync("SELECT [name] AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.LedgerEntries')"))
            .ShouldBe(
                [
                    "CK_LedgerEntries_LineNo", "CK_LedgerEntries_Debit", "CK_LedgerEntries_Credit", "CK_LedgerEntries_OneSide",
                    "CK_LedgerEntries_Subject", "CK_LedgerEntries_Account", "CK_LedgerEntries_SubjectType",
                ],
                ignoreOrder: true);
        (await NamesAsync("SELECT [name] AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.LedgerPostings')"))
            .ShouldBe(["CK_LedgerPostings_Kind", "CK_LedgerPostings_DocumentType"], ignoreOrder: true);
        (await NamesAsync("SELECT [name] AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.PayerBalances')"))
            .ShouldBe(["CK_PayerBalances_Receivable", "CK_PayerBalances_UnappliedCash", "CK_PayerBalances_CreditAvailable"], ignoreOrder: true);

        var indexes = await NamesAsync(
            """
            SELECT i.[name] + ':' + c.[name] + ':' + CAST(i.is_unique AS varchar(1)) AS [Value]
            FROM sys.indexes AS i
            JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1
            JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id IN (OBJECT_ID(N'dbo.LedgerPostings'), OBJECT_ID(N'dbo.LedgerEntries'), OBJECT_ID(N'dbo.PayerBalances')) AND i.is_primary_key = 0
            """);

        indexes.ShouldBe(
            [
                "IX_LedgerPostings_TenantId_BusinessDate:TenantId:0",
                "IX_LedgerPostings_TenantId_DocumentType_DocumentId:TenantId:0",
                "IX_LedgerPostings_TenantId_PayerId:TenantId:0",
                "UX_LedgerEntries_TenantId_PostingId_LineNo:TenantId:1",
                "IX_LedgerEntries_TenantId_SubjectType_SubjectId:TenantId:0",
                "IX_LedgerEntries_TenantId_PayerId_Account:TenantId:0",
            ],
            ignoreOrder: true);

        (await NamesAsync("SELECT [name] + ':' + CAST(delete_referential_action AS varchar(1)) AS [Value] FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID(N'dbo.LedgerPostings'), OBJECT_ID(N'dbo.LedgerEntries'), OBJECT_ID(N'dbo.PayerBalances'))"))
            .ShouldBe(
                [
                    "FK_LedgerPostings_TenantId:0", "FK_LedgerPostings_PayerId:0", "FK_LedgerPostings_ReversalOfPostingId:0",
                    "FK_LedgerEntries_TenantId:0", "FK_LedgerEntries_PostingId:0", "FK_LedgerEntries_PayerId:0",
                    "FK_PayerBalances_TenantId:0", "FK_PayerBalances_PayerId:0",
                ],
                ignoreOrder: true);
        (await NamesAsync("SELECT [name] AS [Value] FROM sys.computed_columns WHERE object_id = OBJECT_ID(N'dbo.PayerBalances') AND is_persisted = 1"))
            .ShouldBe(["NetOutstanding"]);
    }

    [Fact]
    public async Task BR404_a_new_payer_has_its_balances_at_zero_and_the_reconciliation_is_empty()
    {
        var clerk = await NewApiTenantAsync();

        var payer = await NewPayerAsync(clerk);

        var balance = await BalanceAsync(clerk, payer);
        balance.Receivable.ShouldBe(Money.Zero);
        balance.UnappliedCash.ShouldBe(Money.Zero);
        balance.CreditAvailable.ShouldBe(Money.Zero);
        balance.NetOutstanding.ShouldBe(Money.Zero);
        balance.TenantId.ShouldBe(clerk.TenantId!.Value);
        balance.UpdatedAtUtc.ShouldBe(Now);
        (await DriftAsync(clerk)).ShouldBeEmpty();
    }

    [Fact]
    public async Task BR401_a_posting_written_through_the_port_is_saved_with_its_entries_moves_the_payer_and_reconciles_to_zero_drift()
    {
        var clerk = await NewApiTenantAsync();
        var payer = await NewPayerAsync(clerk);
        var payment = Guid.NewGuid();

        await PostAsync(clerk, PaymentRecorded(payer, payment, 1000m));

        await using var context = ApiContext(clerk);
        var posting = await context.Set<LedgerPosting>().Include(candidate => candidate.Entries).SingleAsync();
        posting.Kind.ShouldBe(LedgerPostingKind.PaymentRecorded);
        posting.DocumentType.ShouldBe(LedgerDocumentType.Payment);
        posting.DocumentId.ShouldBe(payment);
        posting.PayerId.ShouldBe(payer);
        posting.BusinessDate.ShouldBe(Today);
        posting.PostedAtUtc.ShouldBe(Now);
        posting.PostedByUserId.ShouldBe(clerk.UserId);
        posting.CorrelationId.ShouldBe(Correlation);
        posting.TenantId.ShouldBe(clerk.TenantId!.Value);
        var entries = posting.Entries.OrderBy(entry => entry.LineNo).ToArray();
        entries.Select(entry => (entry.Account, entry.Debit, entry.Credit)).ShouldBe(
            [(LedgerAccount.Cash, Money.Of(1000m), Money.Zero), (LedgerAccount.UnappliedCash, Money.Zero, Money.Of(1000m))]);
        entries[0].PayerId.ShouldBeNull();
        entries[1].PayerId.ShouldBe(payer);
        entries[1].SubjectId.ShouldBe(payment);
        entries.ShouldAllBe(entry => entry.Id > 0 && entry.TenantId == clerk.TenantId!.Value);

        var balance = await BalanceAsync(clerk, payer);
        balance.UnappliedCash.ShouldBe(Money.Of(1000m));
        balance.Receivable.ShouldBe(Money.Zero);
        balance.NetOutstanding.ShouldBe(Money.Of(-1000m), "money held for the payer lowers what they owe net");
        (await DriftAsync(clerk)).ShouldBeEmpty();
    }

    [Fact]
    public async Task BR007_a_posting_that_is_not_committed_leaves_no_rows_and_gives_the_balance_back()
    {
        var clerk = await NewApiTenantAsync();
        var payer = await NewPayerAsync(clerk);

        await using (var context = ApiContext(clerk))
        {
            (await Ledger(context, clerk).PostAsync(PaymentRecorded(payer, Guid.NewGuid(), 1000m), CancellationToken.None)).IsSuccess.ShouldBeTrue();

            // Inside the transaction the balance has moved already; nobody else can see it yet.
            context.Database.CurrentTransaction.ShouldNotBeNull("the ledger opened the transaction of the use case");
        }

        (await BalanceAsync(clerk, payer)).UnappliedCash.ShouldBe(Money.Zero);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerEntries")).ShouldBe(0);
        (await DriftAsync(clerk)).ShouldBeEmpty();
    }

    [Fact]
    public async Task BR405_a_posting_that_would_take_a_subject_balance_below_zero_is_refused_and_writes_nothing()
    {
        var clerk = await NewApiTenantAsync();
        var payer = await NewPayerAsync(clerk);
        var payment = Guid.NewGuid();
        await PostAsync(clerk, PaymentRecorded(payer, payment, 1000m));

        await using var context = ApiContext(clerk);
        var refused = await Ledger(context, clerk).PostAsync(Refund(payer, payment, 1000.01m), CancellationToken.None);

        refused.Error.Code.ShouldBe(ReceivablesErrors.LedgerBalanceNegativeCode);
        refused.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        context.ChangeTracker.Entries<LedgerPosting>().ShouldBeEmpty("nothing joined the unit of work");
        context.Database.CurrentTransaction.ShouldBeNull("the balance was not touched");
        (await BalanceAsync(clerk, payer)).UnappliedCash.ShouldBe(Money.Of(1000m));
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(1);
    }

    [Fact]
    public async Task BR405_the_whole_balance_may_be_taken_and_postings_of_one_request_count_before_they_are_saved()
    {
        var clerk = await NewApiTenantAsync();
        var payer = await NewPayerAsync(clerk);
        var payment = Guid.NewGuid();
        await PostAsync(clerk, PaymentRecorded(payer, payment, 1000m));

        await using var context = ApiContext(clerk);
        var ledger = Ledger(context, clerk);
        (await ledger.PostAsync(Refund(payer, payment, 400m), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await ledger.PostAsync(Refund(payer, payment, 600m), CancellationToken.None)).IsSuccess.ShouldBeTrue("600 is exactly what the first left");
        (await ledger.PostAsync(Refund(payer, payment, 0.01m), CancellationToken.None)).Error.Code.ShouldBe(ReceivablesErrors.LedgerBalanceNegativeCode);
        (await new UnitOfWork(context).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        (await BalanceAsync(clerk, payer)).UnappliedCash.ShouldBe(Money.Zero);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(3);
        (await DriftAsync(clerk)).ShouldBeEmpty();
    }

    [Fact]
    public async Task BR401_an_unbalanced_template_is_a_bug_and_throws_before_anything_is_written()
    {
        var clerk = await NewApiTenantAsync();
        var payer = await NewPayerAsync(clerk);
        var payment = Guid.NewGuid();
        var unbalanced = new PostingTemplate(
            LedgerPostingKind.PaymentRecorded,
            LedgerDocumentType.Payment,
            payment,
            payer,
            Today,
            "Payment recorded",
            [LedgerLine.Dr(LedgerAccount.Cash, Money.Of(1000m)), LedgerLine.Cr(LedgerAccount.UnappliedCash, Money.Of(999.99m), new LedgerSubject(LedgerSubjectType.Payment, payment))]);

        await using var context = ApiContext(clerk);
        await Should.ThrowAsync<ArgumentException>(() => Ledger(context, clerk).PostAsync(unbalanced, CancellationToken.None));

        context.Database.CurrentTransaction.ShouldBeNull();
        (await BalanceAsync(clerk, payer)).UnappliedCash.ShouldBe(Money.Zero);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(0);
    }

    [Fact]
    public async Task A_posting_for_a_payer_without_balances_is_a_fault_and_nothing_is_written()
    {
        // A payer saved without the Receivables port, which no use case does: the ledger has no row to move.
        var clerk = await NewApiTenantAsync();
        PayerId payer;
        await using (var context = ApiContext(clerk))
        {
            var orphan = Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "Rahul Sharma" }, patientId: null).Value;
            context.Add(orphan);
            await context.SaveChangesAsync();
            payer = orphan.Id;
        }

        await using (var context = ApiContext(clerk))
        {
            var exception = await Should.ThrowAsync<InvalidOperationException>(() => Ledger(context, clerk).PostAsync(PaymentRecorded(payer, Guid.NewGuid(), 1000m), CancellationToken.None));
            exception.Message.ShouldContain("no row of cached balances");
        }

        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BR402_a_ledger_row_cannot_be_updated_or_deleted_through_the_context(bool entry, bool delete)
    {
        var clerk = await NewApiTenantAsync();
        var payer = await NewPayerAsync(clerk);
        await PostAsync(clerk, PaymentRecorded(payer, Guid.NewGuid(), 1000m));
        await using var context = ApiContext(clerk);

        // Each row is read on its own: with its entries loaded, Entity Framework itself refuses to remove a posting
        // (the required relationship would be severed) before the guard is asked.
        var posting = await context.Set<LedgerPosting>().SingleAsync();
        var firstEntry = await context.Set<LedgerEntry>().OrderBy(candidate => candidate.LineNo).FirstAsync();
        if (!entry)
        {
            context.Entry(firstEntry).State = EntityState.Detached;
        }

        object row = entry ? firstEntry : posting;

        if (delete)
        {
            context.Remove(row);
        }
        else if (entry)
        {
            context.Entry(firstEntry).Property(candidate => candidate.Debit).CurrentValue = Money.Of(1m);
        }
        else
        {
            context.Entry(posting).Property(candidate => candidate.Description).CurrentValue = "Rewritten";
        }

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        exception.Message.ShouldContain("append-only");
        (await DriftAsync(clerk)).ShouldBeEmpty("the ledger is as it was");
    }

    [Fact]
    public async Task BR404_twenty_simultaneous_postings_for_one_payer_all_land_and_the_balance_is_their_sum()
    {
        // No row version on the balances: each posting adds to the row in the database, so none overwrites another.
        var clerk = await NewApiTenantAsync();
        var payer = await NewPayerAsync(clerk);

        await Task.WhenAll(Enumerable.Range(1, 20).Select(number => Task.Run(() => PostAsync(clerk, PaymentRecorded(payer, Guid.NewGuid(), number)))));

        (await BalanceAsync(clerk, payer)).UnappliedCash.ShouldBe(Money.Of(210m));
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(20);
        (await DriftAsync(clerk)).ShouldBeEmpty();
    }

    [Fact]
    public async Task BR404_a_cached_balance_that_differs_from_the_ledger_is_reported_with_both_amounts()
    {
        var clerk = await NewApiTenantAsync();
        var payer = await NewPayerAsync(clerk);
        await PostAsync(clerk, PaymentRecorded(payer, Guid.NewGuid(), 1000m));

        // The drift is seeded with SQL: nothing in the application can produce it.
        await ExecuteAsync($"UPDATE dbo.PayerBalances SET UnappliedCash = UnappliedCash + 0.01, Receivable = 5 WHERE PayerId = '{payer.Value}'");

        var drift = await DriftAsync(clerk);

        drift.ShouldBe(
            [
                new DriftRow(DriftIdentity.PayerUnappliedCash, payer.Value, null, 1000.01m, 1000.00m),
                new DriftRow(DriftIdentity.PayerReceivable, payer.Value, null, 5.00m, 0.00m),
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task BR401_a_posting_that_no_longer_balances_and_a_balance_below_zero_are_reported()
    {
        var clerk = await NewApiTenantAsync();
        var payer = await NewPayerAsync(clerk);
        var payment = Guid.NewGuid();
        await PostAsync(clerk, PaymentRecorded(payer, payment, 590m));
        var postingId = (await NamesAsync("SELECT CAST([Id] AS varchar(36)) AS [Value] FROM dbo.LedgerPostings")).ShouldHaveSingleItem();
        (await DriftAsync(clerk)).ShouldBeEmpty();

        // One entry changed behind the application's back: the posting no longer balances. The control account has
        // no payer, so no balance of the payer drifts with it.
        await ExecuteAsync("UPDATE dbo.LedgerEntries SET Debit = 589.99 WHERE Account = 'Cash'");

        (await DriftAsync(clerk)).ShouldHaveSingleItem().ShouldBe(new DriftRow(DriftIdentity.UnbalancedPosting, Guid.Parse(postingId), null, 589.99m, 590.00m));

        // The other entry turned round: the unapplied cash of the payment is below zero, and the payer's cache is off.
        await ExecuteAsync("UPDATE dbo.LedgerEntries SET Debit = 590, Credit = 0 WHERE Account = 'UnappliedCash'");

        var drift = await DriftAsync(clerk);
        drift.ShouldContain(new DriftRow(DriftIdentity.NegativeSubjectBalance, payment, null, 0m, -590.00m));
        drift.ShouldContain(new DriftRow(DriftIdentity.PayerUnappliedCash, payer.Value, null, 590.00m, -590.00m));
    }

    [Fact]
    public async Task A_payer_with_ledger_entries_and_no_balances_is_reported()
    {
        var clerk = await NewApiTenantAsync();
        var payer = await NewPayerAsync(clerk);
        await PostAsync(clerk, PaymentRecorded(payer, Guid.NewGuid(), 590m));

        await ExecuteAsync($"DELETE FROM dbo.PayerBalances WHERE PayerId = '{payer.Value}'");

        (await DriftAsync(clerk)).ShouldHaveSingleItem().ShouldBe(new DriftRow(DriftIdentity.PayerWithoutBalances, payer.Value, null, 0m, -590.00m));
    }

    [Fact]
    public async Task BR404_creating_a_payer_through_the_api_opens_its_balances_in_the_same_commit()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");

        var guarantor = await client.CreatePayerAsync(new BillingRecon.Api.Payers.CreatePayerRequest { Kind = PayerKind.SelfPay, Name = "Rahul Sharma" });
        var patient = await client.RegisterPatientAsync();
        var self = await client.CreatePayerAsync(new BillingRecon.Api.Payers.CreatePayerRequest { Kind = PayerKind.SelfPay, PatientId = patient.Id, Name = patient.FullName });

        foreach (var payer in new[] { guarantor, self })
        {
            var balance = await BalanceAsync(clerk, new PayerId(payer.Id));
            balance.Receivable.ShouldBe(Money.Zero);
            balance.UnappliedCash.ShouldBe(Money.Zero);
            balance.CreditAvailable.ShouldBe(Money.Zero);
            balance.UpdatedAtUtc.ShouldBe(payer.CreatedAtUtc, "opened by the commit that created the payer");
        }

        // A payer that was refused has none: the second payer for the same patient loses, and nothing of it is saved.
        using var refused = await client.PostJsonAsync(PayersApi.Payers, new BillingRecon.Api.Payers.CreatePayerRequest { Kind = PayerKind.SelfPay, PatientId = patient.Id, Name = "Again" });
        refused.StatusCode.ShouldBe(System.Net.HttpStatusCode.Conflict);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.PayerBalances")).ShouldBe(2);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Payers")).ShouldBe(2);
        (await DriftAsync(clerk)).ShouldBeEmpty();
    }

    [Fact]
    public async Task BR401_a_bill_posting_whose_bill_is_not_posted_is_reported()
    {
        var clerk = await NewApiTenantAsync();
        var payer = await NewPayerAsync(clerk);
        var number = DocumentNumber.Create(DocumentType.Bill, FiscalYear.FromDate(Today), "BIL", 1);

        // A bill posting for a bill that does not exist: the ledger accepts it, the reconciliation finds it.
        await PostAsync(clerk, PostingTemplate.BillPosted(new BillId(Guid.NewGuid()), payer, Money.Of(590m), Today, number));
        var postingId = (await NamesAsync("SELECT CAST([Id] AS varchar(36)) AS [Value] FROM dbo.LedgerPostings")).ShouldHaveSingleItem();

        (await DriftAsync(clerk)).ShouldHaveSingleItem().ShouldBe(new DriftRow(DriftIdentity.PostingWithoutBill, Guid.Parse(postingId), null, 0m, 0m));
        (await BalanceAsync(clerk, payer)).Receivable.ShouldBe(Money.Of(590m));
    }

    [Fact]
    public void The_template_of_a_bill_posting_debits_the_receivable_of_the_bill_and_credits_revenue()
    {
        var bill = new BillId(Guid.NewGuid());
        var payer = new PayerId(Guid.NewGuid());
        var number = DocumentNumber.Create(DocumentType.Bill, FiscalYear.FromDate(Today), "BIL", 42);

        var template = PostingTemplate.BillPosted(bill, payer, Money.Of(590m), Today, number);

        template.Kind.ShouldBe(LedgerPostingKind.BillPosted);
        template.DocumentType.ShouldBe(LedgerDocumentType.Bill);
        template.DocumentId.ShouldBe(bill.Value);
        template.PayerId.ShouldBe(payer);
        template.BusinessDate.ShouldBe(Today);
        template.Description.ShouldBe("Bill BIL/26-27/000042 posted");
        template.Lines.ShouldBe(
        [
            LedgerLine.Dr(LedgerAccount.Receivable, Money.Of(590m), new LedgerSubject(LedgerSubjectType.Bill, bill.Value)),
            LedgerLine.Cr(LedgerAccount.Revenue, Money.Of(590m)),
        ]);

        // A bill that comes to nothing has no posting: a line of zero is not an entry (BR-401 as amended).
        Should.Throw<ArgumentException>(() => PostingTemplate.BillPosted(bill, payer, Money.Zero, Today, number));
    }

    [Fact]
    public async Task BR002_the_ledger_and_the_reconciliation_of_one_tenant_never_show_another_tenants_rows()
    {
        var clerk = await NewApiTenantAsync();
        var other = await NewApiTenantAsync();
        var payer = await NewPayerAsync(clerk);
        var theirPayer = await NewPayerAsync(other);
        await PostAsync(clerk, PaymentRecorded(payer, Guid.NewGuid(), 1000m));
        await PostAsync(other, PaymentRecorded(theirPayer, Guid.NewGuid(), 70m));
        await ExecuteAsync($"UPDATE dbo.PayerBalances SET UnappliedCash = 71 WHERE PayerId = '{theirPayer.Value}'");

        (await DriftAsync(clerk)).ShouldBeEmpty("the drift is the other tenant's");
        (await DriftAsync(other)).ShouldHaveSingleItem().SubjectId.ShouldBe(theirPayer.Value);

        await using var context = ApiContext(clerk);
        (await context.Set<LedgerPosting>().CountAsync()).ShouldBe(1);
        (await context.Set<LedgerEntry>().CountAsync()).ShouldBe(2);
        (await context.Set<PayerBalance>().Select(balance => balance.PayerId).ToListAsync()).ShouldBe([payer]);

        // A posting for another tenant's payer finds no row of balances in this tenant: the statement carries the tenant.
        await Should.ThrowAsync<InvalidOperationException>(() => Ledger(context, clerk).PostAsync(PaymentRecorded(theirPayer, Guid.NewGuid(), 1m), CancellationToken.None));
    }

    [Fact]
    public async Task The_reconciliation_needs_a_tenant()
    {
        await using var system = ApiContext(SystemIdentity.Instance);

        await Should.ThrowAsync<InvalidOperationException>(() => new ReconciliationQuery(system, SystemIdentity.Instance).FindDriftAsync(CancellationToken.None));
    }

    private static PostingTemplate PaymentRecorded(PayerId payer, Guid payment, decimal amount) => new(
        LedgerPostingKind.PaymentRecorded,
        LedgerDocumentType.Payment,
        payment,
        payer,
        Today,
        "Payment recorded",
        [LedgerLine.Dr(LedgerAccount.Cash, Money.Of(amount)), LedgerLine.Cr(LedgerAccount.UnappliedCash, Money.Of(amount), new LedgerSubject(LedgerSubjectType.Payment, payment))]);

    private static PostingTemplate Refund(PayerId payer, Guid payment, decimal amount) => new(
        LedgerPostingKind.RefundPosted,
        LedgerDocumentType.Refund,
        Guid.NewGuid(),
        payer,
        Today,
        "Refund posted",
        [LedgerLine.Dr(LedgerAccount.UnappliedCash, Money.Of(amount), new LedgerSubject(LedgerSubjectType.Payment, payment)), LedgerLine.Cr(LedgerAccount.Cash, Money.Of(amount))]);

    private static ReceivablesLedger Ledger(BillingReconDbContext context, FixedIdentity identity)
        => new(context, identity, identity, new FixedCorrelation(Correlation), new FakeClock(Now));

    // One use case: a posting, and the commit that saves it and ends the transaction the ledger opened.
    private async Task PostAsync(FixedIdentity clerk, PostingTemplate template)
    {
        await using var context = ApiContext(clerk);

        (await Ledger(context, clerk).PostAsync(template, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await new UnitOfWork(context).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    // A guarantor with its balances opened, as the use case that creates a payer saves them.
    private async Task<PayerId> NewPayerAsync(FixedIdentity clerk)
    {
        await using var context = ApiContext(clerk);
        var payer = Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "Rahul Sharma" }, patientId: null).Value;
        context.Add(payer);
        new PayerAccounts(context, new FakeClock(Now)).Open(payer.Id);
        (await new UnitOfWork(context).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        return payer.Id;
    }

    private async Task<PayerBalance> BalanceAsync(FixedIdentity clerk, PayerId payer)
    {
        await using var context = ApiContext(clerk);

        return await context.Set<PayerBalance>().AsNoTracking().SingleAsync(balance => balance.PayerId == payer);
    }

    private async Task<IReadOnlyList<DriftRow>> DriftAsync(FixedIdentity tenant)
    {
        await using var context = ApiContext(tenant);

        return await new ReconciliationQuery(context, tenant).FindDriftAsync(CancellationToken.None);
    }

    private async Task<List<string>> NamesAsync(string sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQueryRaw<string>(sql).ToListAsync();
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
}
