using System.Net;
using System.Text.Json;
using BillingRecon.Api.Adjustments;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Payers;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Adjustments;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Payments;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The one search box of the five lists of documents, end to end on the pipeline that ships and a real SQL Server
/// (Prompt 11.1; ADR 0036, amended 2026-11-26): a part of the number, of the patient's name or MRN, of the payer's
/// name, and an exact amount; the text is never a pattern; the search combines with the other filters and the paging;
/// and it never leaves the tenant.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class DocumentSearchApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private const string Bills = "bills";
    private const string Payments = "payments";
    private const string CreditNotes = "credit-notes";
    private const string Adjustments = "adjustments";
    private const string Refunds = "refunds";

    private static readonly DateOnly Today = new(2026, 10, 17);

    [Fact]
    public async Task US11_1_each_list_finds_a_document_by_a_part_of_its_number_its_patient_its_payer_or_its_exact_amount()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");
        var scene = await SceneAsync(client);

        // A part of the patient's name, in any case, on every list: bills and credit notes have the patient, the
        // others have the payer of the patient.
        foreach (var term in new[] { "deshmu", "DESHMUKH", "  Kavita Desh " })
        {
            (await FoundAsync(client, Bills, term)).ShouldBe([scene.Bill.Id]);
            (await FoundAsync(client, Payments, term)).ShouldBe([scene.Payment.Id]);
            (await FoundAsync(client, CreditNotes, term)).ShouldBe([scene.CreditNote.Id]);
            (await FoundAsync(client, Adjustments, term)).ShouldBe([scene.Adjustment.Id]);
            (await FoundAsync(client, Refunds, term)).ShouldBe([scene.Refund.Id]);
        }

        // A part of the MRN. The other patient's bill is found by his; the payment of his guarantor is not his.
        (await FoundAsync(client, Bills, "77_1")).ShouldBe([scene.Bill.Id]);
        (await FoundAsync(client, Refunds, "kd-77")).ShouldBe([scene.Refund.Id]);
        (await FoundAsync(client, Bills, "77X1")).ShouldBe([scene.InsuredBill.Id]);
        (await FoundAsync(client, Payments, "77X1")).ShouldBeEmpty();

        // A part of the payer's name.
        (await FoundAsync(client, Bills, "assur")).ShouldBe([scene.InsuredBill.Id]);
        (await FoundAsync(client, Payments, "Acme Ass")).ShouldBe([scene.InsurerPayment.Id]);

        // A part of the number.
        scene.Bill.Number.ShouldBe("BIL/26-27/000001");
        (await FoundAsync(client, Bills, "000001")).ShouldBe([scene.Bill.Id]);
        (await FoundAsync(client, Bills, "26-27/000002")).ShouldBe([scene.InsuredBill.Id]);
        (await FoundAsync(client, Payments, scene.InsurerPayment.Number[4..])).ShouldBe([scene.InsurerPayment.Id]);
        (await FoundAsync(client, Bills, "bil/")).ShouldBe([scene.Bill.Id, scene.InsuredBill.Id], ignoreOrder: true);

        // The amount, exactly, however it is written; a part of an amount is not searched for.
        foreach (var amount in new[] { "590", "590.00", "590.0" })
        {
            (await FoundAsync(client, Bills, amount)).ShouldBe([scene.InsuredBill.Id]);
        }

        (await FoundAsync(client, Payments, "777.25")).ShouldBe([scene.InsurerPayment.Id]);
        (await FoundAsync(client, Payments, "5,000")).ShouldBe([scene.Payment.Id]);
        (await FoundAsync(client, Payments, "777")).ShouldBeEmpty();
        (await FoundAsync(client, CreditNotes, "126")).ShouldBe([scene.CreditNote.Id]);
        (await FoundAsync(client, Adjustments, "26.00")).ShouldBe([scene.Adjustment.Id]);
        (await FoundAsync(client, Refunds, "50")).ShouldBe([scene.Refund.Id]);
    }

    [Fact]
    public async Task ADR0018_the_text_is_never_a_pattern_and_a_text_too_short_or_too_long_is_told_apart()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");
        var scene = await SceneAsync(client);

        // As patterns each of these would find Kavita, or everything.
        foreach (var term in new[] { "Kav_ta", "Kav%ta", "%%", "__", "[K]avita", "Kavita%" })
        {
            foreach (var list in new[] { Bills, Payments, CreditNotes, Adjustments, Refunds })
            {
                (await FoundAsync(client, list, term)).ShouldBeEmpty($"{term} on {list}");
            }
        }

        // The underscore of her MRN is an underscore: the other MRN has an X there.
        (await FoundAsync(client, Bills, "7_1")).ShouldBe([scene.Bill.Id]);

        // One character filters nothing, like no search at all.
        (await FoundAsync(client, Bills, "d")).Count.ShouldBe(2);
        (await FoundAsync(client, Bills, " ")).Count.ShouldBe(2);

        // More than the box takes is a validation failure, on every list.
        foreach (var list in new[] { Bills, Payments, CreditNotes, Adjustments, Refunds })
        {
            using var tooLong = await client.GetAsync(new Uri($"/api/v1/{list}?search={new string('a', DocumentSearch.MaxLength + 1)}", UriKind.Relative));
            await tooLong.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        }
    }

    [Fact]
    public async Task US11_1_the_search_combines_with_the_other_filters_and_the_paging()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");
        var scene = await SceneAsync(client);

        (await FoundAsync(client, Bills, "deshmu", "status=Posted")).ShouldBe([scene.Bill.Id]);
        (await FoundAsync(client, Bills, "deshmu", "status=Draft")).ShouldBeEmpty();
        (await FoundAsync(client, Bills, "bil/", $"payerId={scene.Insurer.Id}")).ShouldBe([scene.InsuredBill.Id]);
        (await FoundAsync(client, Payments, "deshmu", "method=UPI")).ShouldBe([scene.Payment.Id]);
        (await FoundAsync(client, Payments, "deshmu", "method=Cash")).ShouldBeEmpty();
        (await FoundAsync(client, Payments, "pay/", $"paymentDateFrom={Today.AddDays(1):yyyy-MM-dd}")).ShouldBeEmpty();
        (await FoundAsync(client, Refunds, "deshmu", $"paymentId={scene.Payment.Id}")).ShouldBe([scene.Refund.Id]);
        (await FoundAsync(client, Adjustments, "deshmu", "kind=WriteOff")).ShouldBeEmpty();

        // The count is of what the search finds, not of the list.
        using var paged = await client.GetAsync(new Uri("/api/v1/bills?search=bil/&pageSize=1&page=2", UriKind.Relative));
        using var json = JsonDocument.Parse(await paged.Content.ReadAsStringAsync());
        var page = json.RootElement;
        (page.GetProperty("totalCount").GetInt32(), page.GetProperty("items").GetArrayLength()).ShouldBe((2, 1));
    }

    [Fact]
    public async Task BR002_a_search_never_finds_the_document_of_another_hospital()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");
        using var theirClient = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");
        var ours = await SceneAsync(client);
        var theirs = await SceneAsync(theirClient);

        // The same names, numbers and amounts in both hospitals: each finds its own, by every kind of term.
        foreach (var term in new[] { "deshmu", "KD-77", "assur", "000001", "590" })
        {
            var found = await FoundAsync(client, Bills, term);
            found.ShouldNotBeEmpty(term);
            found.ShouldAllBe(id => id == ours.Bill.Id || id == ours.InsuredBill.Id, term);
            (await FoundAsync(theirClient, Bills, term)).ShouldAllBe(id => id == theirs.Bill.Id || id == theirs.InsuredBill.Id, term);
        }

        (await FoundAsync(client, Payments, "deshmu")).ShouldBe([ours.Payment.Id]);
        (await FoundAsync(client, CreditNotes, "deshmu")).ShouldBe([ours.CreditNote.Id]);
        (await FoundAsync(client, Adjustments, "deshmu")).ShouldBe([ours.Adjustment.Id]);
        (await FoundAsync(client, Refunds, "deshmu")).ShouldBe([ours.Refund.Id]);
    }

    [Fact]
    public async Task US11_1_a_search_that_finds_more_documents_than_are_read_by_their_ids_counts_and_pages_them_all()
    {
        // Up to DocumentSearchFilter.FewEnoughToName documents are read by their ids; one more goes the other way.
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");
        var kavita = await client.RegisterPatientAsync(TestData.NewPatient() with { FullName = "Kavita Deshmukh", Mrn = "KD-77_1" });
        var self = await client.CreatePayerAsync(new CreatePayerRequest { Kind = PayerKind.SelfPay, PatientId = kavita.Id, Name = kavita.FullName });
        const int Many = 501;
        for (var number = 1; number <= Many; number++)
        {
            await client.EnsureShiftAsync();
            await CreatedAsync<PaymentDto>(client, Payments, new RecordPaymentRequest { PayerId = self.Id, PaymentDate = Today, Method = PaymentMethod.Cash, Amount = number });
        }

        foreach (var term in new[] { "deshmu", "pay/" })
        {
            using var first = await client.GetAsync(new Uri($"/api/v1/payments?search={Uri.EscapeDataString(term)}&pageSize=100", UriKind.Relative));
            using var last = await client.GetAsync(new Uri($"/api/v1/payments?search={Uri.EscapeDataString(term)}&pageSize=100&page=6", UriKind.Relative));
            using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
            using var lastJson = JsonDocument.Parse(await last.Content.ReadAsStringAsync());

            (firstJson.RootElement.GetProperty("totalCount").GetInt32(), firstJson.RootElement.GetProperty("items").GetArrayLength()).ShouldBe((Many, 100), term);
            (lastJson.RootElement.GetProperty("totalCount").GetInt32(), lastJson.RootElement.GetProperty("items").GetArrayLength()).ShouldBe((Many, 1), term);
        }

        // With another filter the search finds few again, and the same answer comes the short way.
        (await FoundAsync(client, Payments, "deshmu", $"paymentDateFrom={Today.AddDays(1):yyyy-MM-dd}")).ShouldBeEmpty();
        (await FoundAsync(client, Payments, "250")).Count.ShouldBe(1);
    }

    // Kavita Deshmukh pays for herself: a posted bill of 126.00, a payment of 5,000.00, and a credit note, an adjustment
    // and a refund in draft. Rohan Mehta is billed 590.00 to a guarantor that is no patient (only self-pay payers exist in this release), which has paid 777.25.
    private static async Task<Scene> SceneAsync(HttpClient client)
    {
        var kavita = await client.RegisterPatientAsync(TestData.NewPatient() with { FullName = "Kavita Deshmukh", Mrn = "KD-77_1" });
        var self = await client.CreatePayerAsync(new CreatePayerRequest { Kind = PayerKind.SelfPay, PatientId = kavita.Id, Name = kavita.FullName });
        var rohan = await client.RegisterPatientAsync(TestData.NewPatient() with { FullName = "Rohan Mehta", Mrn = "RM-77X1" });
        var insurer = await client.CreatePayerAsync(new CreatePayerRequest { Kind = PayerKind.SelfPay, Name = "Acme Assurance" });

        var bill = await PostedBillAsync(client, kavita, self, BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m));
        var insured = await PostedBillAsync(client, rohan, insurer, BillingApi.FreeTextLine("OPD consultation", 1m, 500m, 18m));
        var payment = await CreatedAsync<PaymentDto>(client, Payments, new RecordPaymentRequest { PayerId = self.Id, PaymentDate = Today, Method = PaymentMethod.UPI, Reference = "UTR-1", Amount = 5000m });
        var insurerPayment = await CreatedAsync<PaymentDto>(client, Payments, new RecordPaymentRequest { PayerId = insurer.Id, PaymentDate = Today, Method = PaymentMethod.Cheque, Reference = "CHQ-1", Amount = 777.25m });

        var line = bill.Lines.Single();
        var creditNote = await CreatedAsync<CreditNoteDto>(client, CreditNotes, new CreateCreditNoteRequest
        {
            BillId = bill.Id,
            CreditNoteDate = Today,
            Reason = "Dressing not done",
            Lines = [new CreditNoteLineRequest { BillLineId = line.Id, Quantity = line.Quantity }],
        });
        var adjustment = await CreatedAsync<AdjustmentDto>(client, Adjustments, new CreateAdjustmentRequest { BillId = bill.Id, AdjustmentDate = Today, Kind = AdjustmentKind.Discount, Amount = 26m, Reason = "Agreed with the patient" });
        await client.EnsureShiftAsync();
        var refund = await CreatedAsync<RefundDto>(client, Refunds, new CreateRefundRequest { PaymentId = payment.Id, Amount = 50m, Method = PaymentMethod.Cash, Reason = "Paid twice" });

        return new Scene(insurer, bill, insured, payment, insurerPayment, creditNote, adjustment, refund);
    }

    private static async Task<BillDto> PostedBillAsync(HttpClient client, PatientDto patient, PayerDto payer, params BillLineRequest[] lines)
    {
        var draft = await client.CreateBillAsync(new CreateBillRequest { PatientId = patient.Id, PayerId = payer.Id, BillDate = Today, DueDate = Today.AddDays(15), Lines = lines });
        using var response = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<BillDto>();
    }

    private static async Task<T> CreatedAsync<T>(HttpClient client, string list, object request)
        where T : class
    {
        using var response = await client.PostJsonAsync(new Uri($"/api/v1/{list}", UriKind.Relative), request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<T>();
    }

    // The ids of the documents a list answers for a search, with any other filter.
    private static async Task<List<Guid>> FoundAsync(HttpClient client, string list, string search, string? filter = null)
    {
        var query = $"search={Uri.EscapeDataString(search)}&pageSize=100{(filter is null ? string.Empty : "&" + filter)}";
        using var response = await client.GetAsync(new Uri($"/api/v1/{list}?{query}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var page = json.RootElement;

        return [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
    }

    private sealed record Scene(
        PayerDto Insurer,
        BillDto Bill,
        BillDto InsuredBill,
        PaymentDto Payment,
        PaymentDto InsurerPayment,
        CreditNoteDto CreditNote,
        AdjustmentDto Adjustment,
        RefundDto Refund);
}
