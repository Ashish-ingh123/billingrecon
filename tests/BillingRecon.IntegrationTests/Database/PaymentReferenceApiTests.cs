using System.Net;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The same receipt is recorded once, end to end on the pipeline that ships and a real SQL Server (review 2026-11,
/// finding R2; BR-301 as amended 2026-11-26): a payment of a payer by a method with a reference that is recorded
/// already, and was not reversed, is refused under any idempotency key; cash is never compared; another payer, another
/// method and another hospital are other money; and two requests at the same instant record exactly one.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PaymentReferenceApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Uri Payments = new("/api/v1/payments", UriKind.Relative);
    private static readonly DateOnly Today = new(2026, 10, 17);

    private static RecordPaymentRequest Upi(PayerDto payer, string reference, decimal amount = 5000m)
        => new() { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.UPI, Reference = reference, Amount = amount };

    [Fact]
    public async Task BR301_a_receipt_typed_again_under_a_new_key_is_refused_and_names_the_payment_that_holds_it()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (_, payer) = await client.NewBillPartiesAsync();

        // The finding's sequence: recorded under one key, the answer lost, typed again under another.
        using var first = await client.PostJsonAsync(Payments, Upi(payer, "UTR123"), Guid.NewGuid());
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var recorded = await first.ReadAsAsync<PaymentDto>();

        using var again = await client.PostJsonAsync(Payments, Upi(payer, "UTR123"), Guid.NewGuid());
        var problem = await again.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.PaymentReferenceAlreadyRecordedCode);
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain(recorded.Number);

        // Typed otherwise, with spaces, in another case, for another amount or date: it is the same reference.
        foreach (var request in new[] { Upi(payer, "  UTR123 "), Upi(payer, "utr123"), Upi(payer, "UTR123", 1m), Upi(payer, "UTR123") with { PaymentDate = Today.AddDays(-1) } })
        {
            using var refused = await client.PostJsonAsync(Payments, request);
            await refused.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.PaymentReferenceAlreadyRecordedCode);
        }

        // One payment, one number, 5,000.00 of unapplied cash: nothing of the refusals was kept.
        await using var context = ApiContext(cashier);
        var payments = await context.Set<Payment>().AsNoTracking().ToListAsync();
        payments.ShouldHaveSingleItem().Number.ShouldBe(recorded.Number);
        using var next = await client.PostJsonAsync(Payments, Upi(payer, "UTR124"));
        (await next.ReadAsAsync<PaymentDto>()).Number.ShouldBe("PAY/26-27/000002", "a refused recording takes no number");
    }

    [Fact]
    public async Task BR301_another_method_another_payer_cash_and_another_hospital_are_other_money()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var cashier = await NewApiTenantAsync();
        var elsewhere = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        using var theirClient = factory.CreateClientFor(elsewhere, "Cashier", "BillingClerk");
        var (_, payer) = await client.NewBillPartiesAsync();
        var (_, otherPayer) = await client.NewBillPartiesAsync();
        var (_, theirPayer) = await theirClient.NewBillPartiesAsync();

        async Task ShouldRecordAsync(HttpClient sender, RecordPaymentRequest request)
        {
            using var response = await sender.PostJsonAsync(Payments, request);
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        }

        await ShouldRecordAsync(client, Upi(payer, "777001"));

        // The same text as a cheque number, for another payer, and in another hospital.
        await ShouldRecordAsync(client, Upi(payer, "777001") with { Method = PaymentMethod.Cheque });
        await ShouldRecordAsync(client, Upi(otherPayer, "777001"));
        await ShouldRecordAsync(theirClient, Upi(theirPayer, "777001"));

        // Cash is never compared, with or without a note in its reference: two equal cash payments are ordinary.
        await client.EnsureShiftAsync();
        var cash = new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.Cash, Amount = 200m };
        await ShouldRecordAsync(client, cash);
        await ShouldRecordAsync(client, cash);
        await ShouldRecordAsync(client, cash with { Reference = "777001" });
        await ShouldRecordAsync(client, cash with { Reference = "777001" });

        await using var context = ApiContext(cashier);
        (await context.Set<Payment>().CountAsync()).ShouldBe(7);
    }

    [Fact]
    public async Task BR301_two_recordings_of_one_reference_at_the_same_instant_record_exactly_one()
    {
        var gate = new RaceGate();
        using var factory = new DatabaseApiFactory(SqlServer, configure: services => services.GateNumbersOf(gate, DocumentType.Payment));
        var cashier = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(cashier, "Cashier", "BillingClerk");
        var (_, payer) = await client.NewBillPartiesAsync();

        // Held at the gate (review 2026-11, R11): the second recording has looked for the reference and found none
        // while the first holds the series and has not committed. Only the unique index can refuse it, and it is told
        // what the read would have told it.
        var answers = await gate.RaceAsync(
            () => client.PostJsonAsync(Payments, Upi(payer, "RACE-1"), Guid.NewGuid()),
            () => client.PostJsonAsync(Payments, Upi(payer, "RACE-1"), Guid.NewGuid()));

        try
        {
            answers[0].StatusCode.ShouldBe(HttpStatusCode.Created, await answers[0].Content.ReadAsStringAsync());
            await answers[1].ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.PaymentReferenceAlreadyRecordedCode);
        }
        finally
        {
            foreach (var answer in answers)
            {
                answer.Dispose();
            }
        }

        // One payment; the number the loser had taken went back with it.
        await using var context = ApiContext(cashier);
        (await context.Set<Payment>().AsNoTracking().Select(payment => payment.SequenceNumber).ToListAsync()).ShouldBe([1]);
        using var next = await client.PostJsonAsync(Payments, Upi(payer, "RACE-2"));
        (await next.ReadAsAsync<PaymentDto>()).Number.ShouldBe("PAY/26-27/000002", "a refused recording leaves no gap");
    }
}
