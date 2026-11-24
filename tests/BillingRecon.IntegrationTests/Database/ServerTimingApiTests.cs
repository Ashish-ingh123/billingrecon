using System.Globalization;
using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Common.Diagnostics;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Common.Numbering;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The Server-Timing of the number series (ADR 0046; docs/requirements.md section 5): with <c>Diagnostics:ServerTiming</c>
/// on, the answer to a request that took a document number says how long the request waited for the series and how
/// long it held it, which is what the load test reports; off, which is the default, no answer says it; and a request
/// that takes no number has nothing to say.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ServerTimingApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    // 17 October 2026 in India: fiscal year 2026-27.
    private static readonly DateOnly Today = new(2026, 10, 17);

    private static readonly Uri Payments = new("/api/v1/payments", UriKind.Relative);

    private static readonly BillLineRequest Dressing = BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m);

    [Fact]
    public async Task With_the_setting_on_posting_a_bill_and_recording_a_payment_answer_the_wait_and_the_hold_of_their_series()
    {
        using var factory = new DatabaseApiFactory(SqlServer, configure: ServerTimingOn);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk", "Cashier");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var draft = await client.CreateBillAsync(NewBill(patient, payer, Dressing));

        using var posted = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        using var recorded = await client.PostJsonAsync(Payments, new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.Cash, Amount = 126m });

        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());
        recorded.StatusCode.ShouldBe(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync());
        foreach (var answer in new[] { posted, recorded })
        {
            var timings = Timings(answer);
            timings.Select(timing => timing.Name).ShouldBe([NumberSeriesTimer.WaitName, NumberSeriesTimer.HoldName]);
            timings.ShouldAllBe(timing => timing.Milliseconds >= 0);
        }
    }

    [Fact]
    public async Task With_the_setting_on_a_request_that_takes_no_number_answers_no_timing()
    {
        using var factory = new DatabaseApiFactory(SqlServer, configure: ServerTimingOn);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();

        // A draft takes no number; nor does reading it; nor does a posting refused before its number (BR-801).
        using var created = await client.PostJsonAsync(BillingApi.Bills, NewBill(patient, payer));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var empty = await created.ReadAsAsync<BillDto>();
        using var read = await client.GetAsync(BillingApi.Bill(empty.Id));
        using var refused = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(empty.Id, "post"), body: null, PayersApi.ETag(empty.Version));

        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        await refused.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.BillHasNoLinesCode);
        foreach (var answer in new[] { created, read, refused })
        {
            answer.Headers.Contains(ServerTimingMiddleware.HeaderName).ShouldBeFalse($"{answer.RequestMessage?.RequestUri} took no number");
        }
    }

    [Fact]
    public async Task By_default_a_posting_answers_no_timing()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(clerk, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var draft = await client.CreateBillAsync(NewBill(patient, payer, Dressing));

        using var posted = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));

        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());
        posted.Headers.Contains(ServerTimingMiddleware.HeaderName).ShouldBeFalse();
    }

    private static void ServerTimingOn(IServiceCollection services)
        => services.Configure<DiagnosticsOptions>(options => options.ServerTiming = true);

    // "number-wait;dur=1.25, number-hold;dur=6.8" as names and milliseconds, in their order.
    private static (string Name, double Milliseconds)[] Timings(HttpResponseMessage response)
    {
        response.Headers.TryGetValues(ServerTimingMiddleware.HeaderName, out var values).ShouldBeTrue("the answer has a Server-Timing header");

        return
        [
            .. string.Join(", ", values!)
                .Split(", ")
                .Select(entry => entry.Split(";dur="))
                .Select(parts => (parts[0], double.Parse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture))),
        ];
    }

    private static CreateBillRequest NewBill(PatientDto patient, PayerDto payer, params BillLineRequest[] lines)
        => new() { PatientId = patient.Id, PayerId = payer.Id, BillDate = Today, DueDate = Today.AddDays(15), Lines = lines };
}
