using System.Net;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Payments;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Common.Numbering;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.Infrastructure.Receivables;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Recording a payment end to end, on the pipeline that ships and a real SQL Server (US-30, US-25; BR-006, BR-007,
/// BR-205, BR-301, BR-302, BR-309, BR-801, BR-803): the number, the payment, the ledger entry, the payer's unapplied
/// cash and the audit record are written by one transaction or not at all. Every scenario ends with no drift between
/// the caches and the ledger and with a gapless series.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PaymentsApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Uri Payments = new("/api/v1/payments", UriKind.Relative);

    // 17 October 2026 in India: fiscal year 2026-27.
    private static readonly DateOnly Today = new(2026, 10, 17);

    [Fact]
    public async Task US30_recording_a_payment_assigns_the_number_enters_the_ledger_raises_the_unapplied_cash_and_writes_the_audit_record()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (_, payer) = await client.NewBillPartiesAsync();
        factory.Clock.Advance(TimeSpan.FromMinutes(10));

        using var response = await client.PostJsonAsync(Payments, Upi(payer, 3000m) with { Reference = " 412345678901 ", Notes = "Paid at the counter" });

        // The answer: the payment under its number, with all of its amount unapplied (BR-302).
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var payment = await response.ReadAsAsync<PaymentDto>();
        payment.Number.ShouldBe("PAY/26-27/000001");
        payment.FiscalYear.ShouldBe("2026-27");
        payment.PayerId.ShouldBe(payer.Id);
        payment.PaymentDate.ShouldBe(Today);
        payment.Method.ShouldBe(PaymentMethod.UPI);
        payment.Reference.ShouldBe("412345678901");
        payment.Amount.ShouldBe(3000.00m);
        payment.UnappliedAmount.ShouldBe(3000.00m);
        payment.Status.ShouldBe(PaymentStatus.Posted);
        payment.Notes.ShouldBe("Paid at the counter");
        payment.CreatedAtUtc.ShouldBe(DatabaseApiFactory.InitialInstant.AddMinutes(10));
        response.Headers.Location.ShouldNotBeNull().ToString().ShouldEndWith($"/api/v1/payments/{payment.Id}");
        response.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(payment.Version));

        // Reading it again gives the same payment; the list shows it.
        using var read = await client.GetAsync(Payment(payment.Id));
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await read.ReadAsAsync<PaymentDto>()).ShouldBe(payment, "the stored payment is what the recording answered");
        read.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(payment.Version));
        using var list = await client.GetAsync(Payments);
        (await list.ReadAsAsync<PageOf<PaymentDto>>()).Items.ShouldHaveSingleItem().ShouldBe(payment);

        // The ledger: Dr Cash / Cr UnappliedCash(payment) for the amount, on the payment date (BR-302).
        await using var context = ApiContext(cashier);
        var posting = await context.Set<LedgerPosting>().Include(candidate => candidate.Entries).SingleAsync();
        posting.Kind.ShouldBe(LedgerPostingKind.PaymentRecorded);
        posting.DocumentType.ShouldBe(LedgerDocumentType.Payment);
        posting.DocumentId.ShouldBe(payment.Id);
        posting.PayerId.ShouldBe(new PayerId(payer.Id));
        posting.BusinessDate.ShouldBe(Today);
        posting.Description.ShouldBe("Payment PAY/26-27/000001 recorded");
        posting.PostedByUserId.ShouldBe(cashier.UserId);
        posting.CorrelationId.ShouldBe(response.GetCorrelationId());
        posting.Entries.OrderBy(entry => entry.LineNo).Select(entry => (entry.Account, entry.Debit, entry.Credit, entry.SubjectId)).ShouldBe(
        [
            (LedgerAccount.Cash, Money.Of(3000m), Money.Zero, null),
            (LedgerAccount.UnappliedCash, Money.Zero, Money.Of(3000m), payment.Id),
        ]);

        // The payment keeps the posting it was recorded with.
        var stored = await context.Set<Payment>().AsNoTracking().SingleAsync();
        stored.PostingId.ShouldBe(posting.Id);
        stored.SequenceNumber.ShouldBe(1);
        stored.CreatedByUserId.ShouldBe(cashier.UserId);

        // The caches: the hospital holds the amount for the payer; nothing is owed less yet (BR-404).
        var balance = await BalanceAsync(cashier, payer);
        balance.UnappliedCash.ShouldBe(Money.Of(3000m));
        balance.Receivable.ShouldBe(Money.Zero);
        balance.NetOutstanding.ShouldBe(Money.Of(-3000m));

        // The audit record: Recorded, with the number, in the request of the recording (BR-701, BR-703).
        var record = (await TrailAsync(client, payment.Id)).ShouldHaveSingleItem();
        record.Action.ShouldBe("Recorded");
        record.DocumentNumber.ShouldBe("PAY/26-27/000001");
        record.CorrelationId.ShouldBe(response.GetCorrelationId());
        record.OccurredAtUtc.ShouldBe(DatabaseApiFactory.InitialInstant.AddMinutes(10));
        record.Before.ShouldBeNull();
        record.After.GetProperty("Number").GetString().ShouldBe("PAY/26-27/000001");
        record.After.GetProperty("Status").GetString().ShouldBe("Posted");
        record.After.GetProperty("Method").GetString().ShouldBe("UPI");
        record.After.GetProperty("Amount").GetDecimal().ShouldBe(3000.00m);
        record.After.GetProperty("UnappliedAmount").GetDecimal().ShouldBe(3000.00m);

        await ShouldHaveNoDriftAndAGaplessSeriesAsync(cashier);
    }

    [Fact]
    public async Task BR301_cash_needs_no_reference_and_a_payment_without_a_date_is_dated_today_in_India()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (_, payer) = await client.NewBillPartiesAsync();

        // 18:45 UTC on the 17th is 00:15 on the 18th in India (BR-003).
        factory.Clock.Set(new DateTimeOffset(2026, 10, 17, 18, 45, 0, TimeSpan.Zero));
        await client.EnsureShiftAsync();
        var payment = await RecordedAsync(client, new RecordPaymentRequest { PayerId = payer.Id, Method = PaymentMethod.Cash, Amount = 0.01m });

        payment.PaymentDate.ShouldBe(new DateOnly(2026, 10, 18));
        payment.Reference.ShouldBeNull();
        payment.Notes.ShouldBeNull();
        payment.Amount.ShouldBe(0.01m);
        (await BalanceAsync(cashier, payer)).UnappliedCash.ShouldBe(Money.Of(0.01m));
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(cashier);
    }

    [Fact]
    public async Task BR801_payments_are_numbered_in_the_order_they_are_recorded_one_series_per_fiscal_year_of_the_payment_date()
    {
        // 2 April 2027: money received on 31 March belongs to 2026-27, money received on 1 April to 2027-28 (BR-004).
        using var factory = new DatabaseApiFactory(SqlServer);
        factory.Clock.Set(new DateTimeOffset(2027, 4, 2, 6, 30, 0, TimeSpan.Zero));
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (_, payer) = await client.NewBillPartiesAsync();
        var march = new DateOnly(2027, 3, 31);
        var april = new DateOnly(2027, 4, 1);

        var first = await RecordedAsync(client, Upi(payer, 100m) with { PaymentDate = march });
        var second = await RecordedAsync(client, Upi(payer, 200m) with { PaymentDate = april });
        var third = await RecordedAsync(client, Upi(payer, 300m) with { PaymentDate = march });

        first.Number.ShouldBe("PAY/26-27/000001");
        second.Number.ShouldBe("PAY/27-28/000001");
        third.Number.ShouldBe("PAY/26-27/000002");
        (await BalanceAsync(cashier, payer)).UnappliedCash.ShouldBe(Money.Of(600m));
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(cashier);
    }

    [Fact]
    public async Task BR301_a_malformed_payment_is_refused_with_400_and_writes_nothing()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (_, payer) = await client.NewBillPartiesAsync();

        var refused = new (RecordPaymentRequest Request, string Field)[]
        {
            (Upi(payer, 100m) with { Reference = null }, "reference"),
            (Upi(payer, 100m) with { Method = PaymentMethod.Cheque, Reference = "  " }, "reference"),
            (Upi(payer, 0m), "amount"),
            (Upi(payer, -5m), "amount"),
            (Upi(payer, 100.005m), "amount"),
            (Upi(payer, 10_000_000_000m), "amount"),
            (Upi(payer, 100m) with { Amount = null }, "amount"),
            (Upi(payer, 100m) with { Method = null }, "method"),
            (Upi(payer, 100m) with { PayerId = null }, "payerId"),
            (Upi(payer, 100m) with { Reference = new string('r', 101) }, "reference"),
            (Upi(payer, 100m) with { Notes = new string('n', 501) }, "notes"),
        };

        foreach (var (request, field) in refused)
        {
            using var response = await client.PostJsonAsync(Payments, request);

            var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);
            problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe([field]);
        }

        using var unknownMethod = await client.PostAsync(Payments, PayersApi.RawJson($$"""{"payerId":"{{payer.Id}}","method":"Barter","amount":100}"""));
        unknownMethod.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var withoutKeyRequest = new HttpRequestMessage(HttpMethod.Post, Payments) { Content = PayersApi.RawJson($$"""{"payerId":"{{payer.Id}}","method":"Cash","amount":100}""") };
        using var withoutKey = await client.SendAsync(withoutKeyRequest);
        await withoutKey.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.IdempotencyKeyMissingCode);

        await ShouldHaveWrittenNothingAsync(cashier, payer);
    }

    [Fact]
    public async Task A_payment_that_is_refused_takes_no_number_and_writes_nothing()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (_, payer) = await client.NewBillPartiesAsync();
        var (_, inactivePayer) = await client.NewBillPartiesAsync();
        using var deactivation = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Payer(inactivePayer.Id, "deactivate"), body: null, PayersApi.ETag(inactivePayer.Version));
        deactivation.StatusCode.ShouldBe(HttpStatusCode.OK);

        // BR-205: not in the future, and not older than the hospital allows (seven days for a new tenant).
        using var tomorrow = await client.PostJsonAsync(Payments, Upi(payer, 100m) with { PaymentDate = Today.AddDays(1) });
        using var tooOld = await client.PostJsonAsync(Payments, Upi(payer, 100m) with { PaymentDate = Today.AddDays(-8) });

        // BR-309: only an active payer of this tenant.
        using var payerInactive = await client.PostJsonAsync(Payments, Upi(inactivePayer, 100m));
        using var payerUnknown = await client.PostJsonAsync(Payments, Upi(payer, 100m) with { PayerId = Guid.NewGuid() });

        await tomorrow.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.PaymentDateInFutureCode);
        await tooOld.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.PaymentDateTooOldCode);
        await payerInactive.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PayersErrors.PayerInactiveCode);
        await payerUnknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PayerNotFoundCode);
        await ShouldHaveWrittenNothingAsync(cashier, payer);

        // The oldest date allowed is accepted, and gets the first number: no refusal reached the series.
        (await RecordedAsync(client, Upi(payer, 100m) with { PaymentDate = Today.AddDays(-7) })).Number.ShouldBe("PAY/26-27/000001");
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(cashier);
    }

    [Theory]
    [InlineData("the number series, after the number is taken")]
    [InlineData("the ledger, after the posting is entered")]
    [InlineData("the audit trail, after the record is written")]
    [InlineData("the save itself")]
    public async Task US25_a_failure_at_any_step_leaves_no_number_no_payment_no_ledger_and_no_audit_rows_and_the_next_payment_gets_the_number(string step)
    {
        var cashier = await NewApiTenantAsync();
        PayerDto payer;
        var key = Guid.NewGuid();

        using (var failing = new DatabaseApiFactory(SqlServer, configure: services => FailAt(step, services)))
        {
            using var client = failing.CreateClientFor(cashier, "Cashier", "BillingClerk");
            (_, payer) = await client.NewBillPartiesAsync();

            using var failed = await client.PostJsonAsync(Payments, Upi(payer, 126m), key);

            await failed.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
            await ShouldHaveWrittenNothingAsync(cashier, payer);
        }

        // The same request again, with the same key, on an API that works: the key was freed by the fault, and the
        // payment gets the number the failed recording had taken and given back.
        using var working = new DatabaseApiFactory(SqlServer);
        using var retrying = working.CreateClientFor(cashier, "Cashier", "BillingClerk");
        using var retried = await retrying.PostJsonAsync(Payments, Upi(payer, 126m), key);

        retried.StatusCode.ShouldBe(HttpStatusCode.Created, await retried.Content.ReadAsStringAsync());
        retried.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        (await retried.ReadAsAsync<PaymentDto>()).Number.ShouldBe("PAY/26-27/000001");
        (await BalanceAsync(cashier, payer)).UnappliedCash.ShouldBe(Money.Of(126m));
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(cashier);
    }

    [Fact]
    public async Task BR801_twenty_payments_recorded_at_once_for_one_payer_get_twenty_consecutive_numbers_and_the_payer_holds_their_sum()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (_, payer) = await client.NewBillPartiesAsync();

        var recorded = await Task.WhenAll(Enumerable.Range(1, 20).Select(number => Task.Run(() => RecordedAsync(client, Upi(payer, number * 100m)))));

        recorded.Select(payment => payment.Number).Order().ShouldBe(Enumerable.Range(1, 20).Select(number => $"PAY/26-27/{number:D6}"));
        recorded.Select(payment => payment.Id).Distinct().Count().ShouldBe(20);
        (await BalanceAsync(cashier, payer)).UnappliedCash.ShouldBe(Money.Of(21000m), "100 x (1 + 2 + ... + 20)");
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(20);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.AuditRecords WHERE [EntityType] = 'Payment'")).ShouldBe(20);
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(cashier);
    }

    [Fact]
    public async Task BR803_a_replayed_recording_returns_the_same_response_and_records_once()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (_, payer) = await client.NewBillPartiesAsync();
        var key = Guid.NewGuid();

        var request = Upi(payer, 126m);
        using var first = await client.PostJsonAsync(Payments, request, key);
        using var replay = await client.PostJsonAsync(Payments, request, key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.Headers.Contains("Idempotent-Replayed").ShouldBeTrue();
        (await replay.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        replay.Headers.ETag.ShouldBe(first.Headers.ETag);
        replay.Headers.Location.ShouldBe(first.Headers.Location);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Payments")).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(1);
        (await BalanceAsync(cashier, payer)).UnappliedCash.ShouldBe(Money.Of(126m));

        // EXPECTED BEHAVIOUR CHANGED on 2026-11-26 (review finding R2, approved by the owner; BR-301). Until then this
        // test held that the same payment sent under a new key was a second payment ("the key, not the content, makes a
        // retry"). The key still makes the retry (BR-803); but the same reference for the same payer and method under a
        // new key is now refused, because that is what a receipt typed again after a lost answer looks like.
        using (var again = await client.PostJsonAsync(Payments, request))
        {
            await again.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.PaymentReferenceAlreadyRecordedCode);
        }

        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Payments")).ShouldBe(1);

        // Other money, with a reference of its own, is a second payment under its own key, with the next number.
        (await RecordedAsync(client, Upi(payer, 126m))).Number.ShouldBe("PAY/26-27/000002");
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(cashier);
    }

    [Fact]
    public async Task BR803_the_same_key_while_the_first_recording_is_still_running_answers_409_idempotency_in_progress()
    {
        // The first request is held inside the transaction, after it has taken the number; the second arrives then.
        var gate = new Gate();
        using var factory = new DatabaseApiFactory(SqlServer, configure: services =>
        {
            services.AddSingleton(gate);
            services.AddScoped<NumberSeriesAllocator>();
            services.AddScoped<INumberSeries>(provider => new GatedNumberSeries(provider.GetRequiredService<NumberSeriesAllocator>(), gate));
        });
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (_, payer) = await client.NewBillPartiesAsync();
        var key = Guid.NewGuid();

        // One request, sent twice: since 2026-11-26 each call of Upi carries a reference of its own (BR-301).
        var request = Upi(payer, 126m);
        var first = client.PostJsonAsync(Payments, request, key);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        using var second = await client.PostJsonAsync(Payments, request, key);
        gate.Release.SetResult();
        using var firstResponse = await first;

        await second.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.IdempotencyInProgressCode);
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await firstResponse.Content.ReadAsStringAsync());
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Payments")).ShouldBe(1);
        (await BalanceAsync(cashier, payer)).UnappliedCash.ShouldBe(Money.Of(126m));
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(cashier);
    }

    [Fact]
    public async Task The_list_is_newest_first_and_filters_by_payer_method_status_and_date()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (_, payer) = await client.NewBillPartiesAsync();
        var (_, otherPayer) = await client.NewBillPartiesAsync();
        var older = await RecordedAsync(client, Upi(payer, 100m) with { PaymentDate = Today.AddDays(-2) });
        await client.EnsureShiftAsync();
        var cash = await RecordedAsync(client, new RecordPaymentRequest { PayerId = payer.Id, Method = PaymentMethod.Cash, Amount = 200m });
        var theirs = await RecordedAsync(client, Upi(otherPayer, 300m));

        (await ListAsync(client, string.Empty)).Items.Select(payment => payment.Number).ShouldBe([theirs.Number, cash.Number, older.Number]);
        (await ListAsync(client, $"payerId={payer.Id}")).Items.Select(payment => payment.Number).ShouldBe([cash.Number, older.Number]);
        (await ListAsync(client, "method=Cash")).Items.ShouldHaveSingleItem().ShouldBe(cash);
        (await ListAsync(client, "status=Posted")).TotalCount.ShouldBe(3);
        (await ListAsync(client, $"paymentDateTo={Today.AddDays(-1):yyyy-MM-dd}")).Items.ShouldHaveSingleItem().ShouldBe(older);
        (await ListAsync(client, $"paymentDateFrom={Today:yyyy-MM-dd}&paymentDateTo={Today:yyyy-MM-dd}&payerId={otherPayer.Id}")).Items.ShouldHaveSingleItem().ShouldBe(theirs);

        var secondPage = await ListAsync(client, "page=2&pageSize=2");
        secondPage.Items.ShouldHaveSingleItem().ShouldBe(older);
        secondPage.TotalCount.ShouldBe(3);

        using var backwards = await client.GetAsync(Payments.WithQuery($"paymentDateFrom={Today:yyyy-MM-dd}&paymentDateTo={Today.AddDays(-1):yyyy-MM-dd}"));
        await backwards.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);
    }

    [Fact]
    public async Task BR006_a_payment_has_no_endpoint_that_edits_or_deletes_it()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (_, payer) = await client.NewBillPartiesAsync();
        var payment = await RecordedAsync(client, Upi(payer, 126m));

        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            using var response = await client.SendWithIfMatchAsync(method, Payment(payment.Id), method == HttpMethod.Delete ? null : Upi(payer, 1m), PayersApi.ETag(payment.Version));

            response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        }

        using var read = await client.GetAsync(Payment(payment.Id));
        (await read.ReadAsAsync<PaymentDto>()).ShouldBe(payment);
    }

    [Fact]
    public async Task BR002_a_payment_is_invisible_to_another_tenant_and_each_tenant_has_its_own_series()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        var other = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        using var stranger = factory.CreateClientFor(other, "Cashier", "BillingClerk");
        var (_, payer) = await client.NewBillPartiesAsync();
        var (_, theirPayer) = await stranger.NewBillPartiesAsync();
        var payment = await RecordedAsync(client, Upi(payer, 126m));

        using var read = await stranger.GetAsync(Payment(payment.Id));
        using var forged = await stranger.PostJsonAsync(Payments, Upi(payer, 50m));

        await read.ShouldBeProblemAsync(HttpStatusCode.NotFound, PaymentsErrors.PaymentNotFoundCode);
        await forged.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PayerNotFoundCode);
        (await ListAsync(stranger, string.Empty)).Items.ShouldBeEmpty();
        (await RecordedAsync(stranger, Upi(theirPayer, 70m))).Number.ShouldBe("PAY/26-27/000001");
        (await ListAsync(client, string.Empty)).Items.ShouldHaveSingleItem().ShouldBe(payment);
        (await BalanceAsync(cashier, payer)).UnappliedCash.ShouldBe(Money.Of(126m));
        (await BalanceAsync(other, theirPayer)).UnappliedCash.ShouldBe(Money.Of(70m));
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(cashier);
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(other);
    }

    [Fact]
    public async Task BR404_a_payment_whose_unapplied_amount_differs_from_the_ledger_and_one_without_its_posting_are_reported()
    {
        ExpectDrift(DriftIdentity.PaymentWithoutPosting, DriftIdentity.PostingWithoutPayment);

        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (_, payer) = await client.NewBillPartiesAsync();
        var payment = await RecordedAsync(client, Upi(payer, 126m));
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(cashier);

        // The drift is seeded with SQL: nothing in the application can produce it.
        await ExecuteAsync($"UPDATE dbo.Payments SET UnappliedAmount = 100 WHERE Id = '{payment.Id}'");

        // Two identities see it: the ledger, and since Prompt 4.2 the amount less the allocations.
        (await DriftAsync(cashier)).ShouldBe(
            [
                new DriftRow(DriftIdentity.PaymentUnapplied, payment.Id, "PAY/26-27/000001", 100.00m, 126.00m),
                new DriftRow(DriftIdentity.PaymentAllocations, payment.Id, "PAY/26-27/000001", 100.00m, 126.00m),
            ],
            ignoreOrder: true);

        await ExecuteAsync($"UPDATE dbo.Payments SET UnappliedAmount = 126 WHERE Id = '{payment.Id}'");
        await ExecuteAsync("UPDATE dbo.LedgerPostings SET DocumentId = NEWID()");

        (await DriftAsync(cashier)).Select(row => (row.Identity, row.Reference)).ShouldBe(
            [(DriftIdentity.PaymentWithoutPosting, "PAY/26-27/000001"), (DriftIdentity.PostingWithoutPayment, null)],
            ignoreOrder: true);
    }

    // The registrations that make one step of a recording fail after it has done its work inside the transaction.
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
                services.AddScoped<IDomainEventSubscriber, FaultOnPaymentRecorded>();
                break;
            case "the save itself":
                services.AddScoped<IDomainEventSubscriber, RowTheDatabaseRefuses>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step), step, "No such step.");
        }
    }

    private static Uri Payment(Guid id) => new($"/api/v1/payments/{id}", UriKind.Relative);

    // Since 2026-11-26 a payer's reference is recorded once (BR-301, review finding R2): each payment these tests record
    // is other money, and so carries a reference of its own.
    private static int _references;

    private static string NextReference() => (400_000_000_000L + Interlocked.Increment(ref _references)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static RecordPaymentRequest Upi(PayerDto payer, decimal amount)
        => new() { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.UPI, Reference = NextReference(), Amount = amount };

    private static async Task<PaymentDto> RecordedAsync(HttpClient client, RecordPaymentRequest request)
    {
        using var response = await client.PostJsonAsync(Payments, request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PaymentDto>();
    }

    private static async Task<PageOf<PaymentDto>> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(query.Length == 0 ? Payments : Payments.WithQuery(query));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PageOf<PaymentDto>>();
    }

    private static async Task<IReadOnlyList<AuditRecordDto>> TrailAsync(HttpClient client, Guid paymentId)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/audit-records?entityType=Payment&entityId={paymentId}", UriKind.Relative));

        return (await response.ReadAsAsync<PageOf<AuditRecordDto>>()).Items;
    }

    // What US-25 promises after a recording that did not happen: nothing of it exists.
    private async Task ShouldHaveWrittenNothingAsync(FixedIdentity cashier, PayerDto payer)
    {
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.NumberSeries")).ShouldBe(0, "no number was consumed (BR-801)");
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Payments")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerPostings")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerEntries")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.AuditRecords WHERE [EntityType] = 'Payment'")).ShouldBe(0);
        (await BalanceAsync(cashier, payer)).UnappliedCash.ShouldBe(Money.Zero);
        await ShouldHaveNoDriftAndAGaplessSeriesAsync(cashier);
    }

    // The two things every scenario ends with: the caches equal the ledger (BR-404), and the payment numbers of every
    // fiscal year run from 1 without a gap, with the series pointing at the next one (BR-801).
    private async Task ShouldHaveNoDriftAndAGaplessSeriesAsync(FixedIdentity tenant)
    {
        (await DriftAsync(tenant)).ShouldBeEmpty();

        await using var context = ApiContext(tenant);
        var payments = await context.Set<Payment>().AsNoTracking().ToListAsync();
        foreach (var year in payments.GroupBy(payment => payment.FiscalYear))
        {
            year.Select(payment => payment.SequenceNumber).Order().ShouldBe(Enumerable.Range(1, year.Count()), $"the payments of {year.Key} have a gap");
            var series = await context.Set<NumberSeries>().AsNoTracking().SingleAsync(candidate => candidate.DocumentType == DocumentType.Payment && candidate.FiscalYear == year.Key);
            series.NextNumber.ShouldBe(year.Count() + 1, $"the series of {year.Key} does not point at the next number");
        }

        (await context.Set<NumberSeries>().CountAsync()).ShouldBe(payments.Select(payment => payment.FiscalYear).Distinct().Count(), "a series exists without a payment");
    }

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

    // Lets a test hold a recording inside its transaction: Entered is set when the number is taken, and the recording
    // goes on when the test sets Release.
    private sealed class Gate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class GatedNumberSeries(INumberSeries inner, Gate gate) : INumberSeries
    {
        public Task HoldAsync(DocumentType type, FiscalYear fiscalYear, CancellationToken ct) => inner.HoldAsync(type, fiscalYear, ct);

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
        public Task HoldAsync(DocumentType type, FiscalYear fiscalYear, CancellationToken ct) => inner.HoldAsync(type, fiscalYear, ct);

        public async Task<DocumentNumber> NextAsync(DocumentType type, FiscalYear fiscalYear, CancellationToken ct)
        {
            await inner.NextAsync(type, fiscalYear, ct);

            throw new InvalidOperationException("Injected: the number series failed after it took the number.");
        }
    }

    private sealed class PostingThenFault(IReceivablesLedger inner) : IReceivablesLedger
    {
        public async Task<Result<LedgerPostingId>> PostAsync(PostingTemplate template, CancellationToken ct)
        {
            await inner.PostAsync(template, ct);

            throw new InvalidOperationException("Injected: the ledger failed after it entered the posting.");
        }
    }

    private sealed class FaultOnPaymentRecorded : IDomainEventSubscriber
    {
        public Task HandleAsync(DomainEventOccurrence occurrence, CancellationToken ct)
            => occurrence.Event is PaymentRecorded
                ? throw new InvalidOperationException("Injected: a subscriber failed after the audit record was written.")
                : Task.CompletedTask;
    }

    // Adds, with the payment, a payer that names a patient nobody has: the foreign key refuses the whole save.
    private sealed class RowTheDatabaseRefuses(Infrastructure.Common.Persistence.BillingReconDbContext context) : IDomainEventSubscriber
    {
        public Task HandleAsync(DomainEventOccurrence occurrence, CancellationToken ct)
        {
            if (occurrence.Event is PaymentRecorded)
            {
                context.Add(Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "Nobody" }, new PatientId(Guid.NewGuid())).Value);
            }

            return Task.CompletedTask;
        }
    }
}
