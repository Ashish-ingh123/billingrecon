using System.Net;
using System.Text.Json;
using BillingRecon.Api.Adjustments;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Payers;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Adjustments;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The filters and the named orders of the lists of documents, end to end on the pipeline that ships and a real SQL
/// Server (Prompt 12.2; ADR 0063): each filter alone and with others, each order both ways with its tie-breaker across
/// two pages, an order the list does not have and a filter that cannot be read refused in their own words, and no row
/// of another hospital ever found.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ListFiltersApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly DateOnly Today = new(2026, 10, 17);

    [Fact]
    public async Task ADR0063_bills_are_narrowed_by_total_balance_maker_and_origin_alone_and_together()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "TenantAdmin");
        var scene = await SceneAsync(client);
        var (small, middle, large) = (scene.Bills[0].Id, scene.Bills[1].Id, scene.Bills[2].Id);

        (await IdsAsync(client, "bills", "totalFrom=590")).ShouldBe([large, middle], ignoreOrder: true);
        (await IdsAsync(client, "bills", "totalTo=590")).ShouldBe([small, middle], ignoreOrder: true);
        (await IdsAsync(client, "bills", "totalFrom=126.01&totalTo=1999.99")).ShouldBe([middle]);
        (await IdsAsync(client, "bills", "totalFrom=2000.00&totalTo=2000")).ShouldBe([large]);

        // The large bill was paid 500.00: it owes 1,500.00.
        (await IdsAsync(client, "bills", "balanceDueFrom=1500&balanceDueTo=1500")).ShouldBe([large]);
        (await IdsAsync(client, "bills", "balanceDueTo=126")).ShouldBe([small]);

        // Who made it, and where it came from.
        (await IdsAsync(client, "bills", $"createdBy={tenant.UserId!.Value.Value}")).Count.ShouldBe(3);
        (await IdsAsync(client, "bills", $"createdBy={Guid.NewGuid()}")).ShouldBeEmpty();
        (await IdsAsync(client, "bills", "origin=Standard")).Count.ShouldBe(3);
        (await IdsAsync(client, "bills", "origin=opening")).ShouldBeEmpty();

        // With the filters a list had before, and with the search.
        (await IdsAsync(client, "bills", $"totalFrom=500&billDateTo={Today.AddDays(-2):yyyy-MM-dd}")).ShouldBe([large]);
        (await IdsAsync(client, "bills", "totalFrom=500&status=PartiallyPaid")).ShouldBe([large]);
        (await IdsAsync(client, "bills", "totalTo=1000&search=deshmu")).ShouldBe([small, middle], ignoreOrder: true);
        (await IdsAsync(client, "bills", "totalFrom=5000")).ShouldBeEmpty();

        // An empty filter filters nothing.
        (await IdsAsync(client, "bills", "totalFrom=&totalTo=&createdBy=&origin=&sort=")).Count.ShouldBe(3);
    }

    [Fact]
    public async Task ADR0063_bills_are_ordered_by_each_named_column_both_ways()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");
        var scene = await SceneAsync(client);
        var (small, middle, large) = (scene.Bills[0].Id, scene.Bills[1].Id, scene.Bills[2].Id);

        // Posted in the order small, middle, large; dated today, yesterday, the day before.
        (await IdsAsync(client, "bills", "sort=number")).ShouldBe([small, middle, large]);
        (await IdsAsync(client, "bills", "sort=-number")).ShouldBe([large, middle, small]);
        (await IdsAsync(client, "bills", "sort=billDate")).ShouldBe([large, middle, small]);
        (await IdsAsync(client, "bills", "sort=-billDate")).ShouldBe([small, middle, large]);
        (await IdsAsync(client, "bills", "sort=total")).ShouldBe([small, middle, large]);
        (await IdsAsync(client, "bills", "sort=-total")).ShouldBe([large, middle, small]);
        (await IdsAsync(client, "bills", "sort=balanceDue")).ShouldBe([small, middle, large]);
        (await IdsAsync(client, "bills", "sort=-BalanceDue")).ShouldBe([large, middle, small]);

        // No order asked for: the list's own, the newest bill date first, as before.
        (await IdsAsync(client, "bills", string.Empty)).ShouldBe([small, middle, large]);
    }

    [Fact]
    public async Task ADR0063_payments_credit_notes_adjustments_and_refunds_are_narrowed_and_ordered_by_their_new_columns()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "TenantAdmin");
        var scene = await SceneAsync(client);
        var me = tenant.UserId!.Value.Value;

        // Payments: a second one of 1,000.00, unapplied in full; the first holds nothing, all of it on the large bill.
        var second = await CreatedAsync<PaymentDto>(client, "payments", new RecordPaymentRequest { PayerId = scene.Payer.Id, PaymentDate = Today.AddDays(-1), Method = PaymentMethod.Card, Reference = "AUTH-2", Amount = 1000m });
        (await IdsAsync(client, "payments", "amountFrom=600")).ShouldBe([second.Id]);
        (await IdsAsync(client, "payments", "amountTo=500.00")).ShouldBe([scene.Payment.Id]);
        (await IdsAsync(client, "payments", $"createdBy={me}&origin=Standard")).Count.ShouldBe(2);
        (await IdsAsync(client, "payments", "sort=unappliedAmount")).ShouldBe([scene.Payment.Id, second.Id]);
        (await IdsAsync(client, "payments", "sort=-unappliedAmount")).ShouldBe([second.Id, scene.Payment.Id]);
        (await IdsAsync(client, "payments", "sort=paymentDate")).ShouldBe([second.Id, scene.Payment.Id]);
        (await IdsAsync(client, "payments", "sort=-amount&method=Card")).ShouldBe([second.Id]);

        // A credit note of the small bill's dressing, an adjustment of the middle bill, a refund of the second payment.
        var line = scene.Bills[0].Lines.Single();
        var creditNote = await CreatedAsync<CreditNoteDto>(client, "credit-notes", new CreateCreditNoteRequest { BillId = scene.Bills[0].Id, CreditNoteDate = Today, Reason = "Not done", Lines = [new CreditNoteLineRequest { BillLineId = line.Id, Quantity = line.Quantity }] });
        var adjustment = await CreatedAsync<AdjustmentDto>(client, "adjustments", new CreateAdjustmentRequest { BillId = scene.Bills[1].Id, AdjustmentDate = Today.AddDays(-1), Kind = AdjustmentKind.Discount, Amount = 90m, Reason = "Staff" });
        var refund = await CreatedAsync<RefundDto>(client, "refunds", new CreateRefundRequest { PaymentId = second.Id, Amount = 250m, Method = PaymentMethod.BankTransfer, Reference = "UTR-9", Reason = "Paid twice" });

        (await IdsAsync(client, "credit-notes", $"creditNoteDateFrom={Today:yyyy-MM-dd}&creditNoteDateTo={Today:yyyy-MM-dd}&totalFrom=126&totalTo=126")).ShouldBe([creditNote.Id]);
        (await IdsAsync(client, "credit-notes", $"creditNoteDateTo={Today.AddDays(-1):yyyy-MM-dd}")).ShouldBeEmpty();
        (await IdsAsync(client, "credit-notes", $"createdBy={me}&sort=-total")).ShouldBe([creditNote.Id]);
        (await IdsAsync(client, "adjustments", $"adjustmentDateFrom={Today.AddDays(-1):yyyy-MM-dd}&amountFrom=90&amountTo=90&sort=adjustmentDate")).ShouldBe([adjustment.Id]);
        (await IdsAsync(client, "adjustments", "amountFrom=90.01")).ShouldBeEmpty();
        (await IdsAsync(client, "refunds", $"refundDateFrom={Today:yyyy-MM-dd}&method=BankTransfer&amountTo=250&sort=-amount")).ShouldBe([refund.Id]);
        (await IdsAsync(client, "refunds", "method=Cash")).ShouldBeEmpty();
        (await IdsAsync(client, "refunds", $"createdBy={Guid.NewGuid()}")).ShouldBeEmpty();
    }

    [Fact]
    public async Task ADR0063_an_order_ends_on_a_tie_breaker_so_two_pages_never_share_or_skip_a_row()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");
        var scene = await SceneAsync(client);

        // Five more bills of the same total and the same date: the order of equal rows is the tie-breaker's alone.
        var equal = new List<Guid>();
        for (var index = 0; index < 5; index++)
        {
            equal.Add((await PostedBillAsync(client, scene.Patient, scene.Payer, Today, BillingApi.FreeTextLine("Same", 1m, 300m, 0m))).Id);
        }

        foreach (var sort in new[] { "total", "-total", "billDate", "-billDate", "balanceDue", "-balanceDue", "number", "-number" })
        {
            var pages = new List<Guid>();
            for (var page = 1; page <= 4; page++)
            {
                pages.AddRange(await IdsAsync(client, "bills", $"sort={sort}&page={page}&pageSize=2", paged: false));
            }

            pages.Count.ShouldBe(8, sort);
            pages.Distinct().Count().ShouldBe(8, sort);
            pages.ShouldBe(await IdsAsync(client, "bills", $"sort={sort}"), sort);
        }
    }

    [Fact]
    public async Task ADR0063_an_order_the_list_does_not_have_and_a_filter_that_cannot_be_read_are_refused_in_their_own_words()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");

        foreach (var (list, sort) in new[] { ("bills", "patientName"), ("payments", "Amount; DROP TABLE Payments"), ("refunds", "-"), ("users", "passwordHash"), ("reports/bill-register", "total desc") })
        {
            using var response = await client.GetAsync(new Uri($"/api/v1/{list}?sort={Uri.EscapeDataString(sort)}", UriKind.Relative));

            var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.SortNotSupportedCode);
            problem.GetProperty("errors").EnumerateObject().Select(field => field.Name).ShouldBe(["sort"], list);
        }

        using var many = await client.GetAsync(new Uri("/api/v1/bills?totalFrom=ten&balanceDueTo=1.234&createdBy=nobody&origin=1&totalTo=5", UriKind.Relative));
        var invalid = await many.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.FilterInvalidCode);
        invalid.GetProperty("errors").EnumerateObject().Select(field => field.Name).ShouldBe(["totalFrom", "balanceDueTo", "createdBy", "origin"], ignoreOrder: true);

        using var backwards = await client.GetAsync(new Uri("/api/v1/payments?amountFrom=500&amountTo=100", UriKind.Relative));
        var reversed = await backwards.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.FilterInvalidCode);
        reversed.GetProperty("errors").EnumerateObject().Select(field => field.Name).ShouldBe(["amountTo"]);

        using var negative = await client.GetAsync(new Uri("/api/v1/refunds?amountFrom=-1&refundDateFrom=17-10-2026", UriKind.Relative));
        (await negative.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.FilterInvalidCode)).GetProperty("errors").EnumerateObject().Count().ShouldBe(2);
    }

    [Fact]
    public async Task BR002_no_filter_or_order_finds_a_row_of_another_hospital()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var mine = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(mine, "TenantAdmin");
        using var other = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");
        var scene = await SceneAsync(client);
        await SceneAsync(other);

        (await IdsAsync(client, "bills", "totalFrom=0&sort=-total")).ShouldBe([.. scene.Bills.Select(bill => bill.Id).Reverse()]);
        (await IdsAsync(client, "payments", "amountFrom=0&sort=amount")).ShouldBe([scene.Payment.Id]);
        (await IdsAsync(other, "bills", $"createdBy={mine.UserId!.Value.Value}")).ShouldBeEmpty();
        using var report = await client.GetAsync(new Uri($"/api/v1/reports/bill-register?from={Today.AddDays(-30):yyyy-MM-dd}&to={Today:yyyy-MM-dd}&totalFrom=0&sort=total", UriKind.Relative));
        using var json = JsonDocument.Parse(await report.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("totals").GetProperty("bills").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task ADR0063_the_totals_of_a_register_follow_its_filters_and_equal_the_ledger_of_the_rows_it_keeps()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "TenantAdmin");
        var scene = await SceneAsync(client);
        var period = $"from={Today.AddDays(-30):yyyy-MM-dd}&to={Today:yyyy-MM-dd}";

        using var response = await client.GetAsync(new Uri($"/api/v1/reports/bill-register?{period}&totalFrom=500&sort=-total", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        var totals = json.RootElement.GetProperty("totals");

        items.Select(item => item.GetProperty("billId").GetGuid()).ShouldBe([scene.Bills[2].Id, scene.Bills[1].Id]);
        totals.GetProperty("bills").GetInt32().ShouldBe(2);
        totals.GetProperty("total").GetDecimal().ShouldBe(2590.00m);
        totals.GetProperty("balanceDue").GetDecimal().ShouldBe(2090.00m);

        // The ledger, summed here in SQL: what was posted to Receivable for those two bills, less what was taken off.
        await using var context = ApiContext(SystemIdentity.Instance);
        var ids = string.Join(",", items.Select(item => $"'{item.GetProperty("billId").GetGuid()}'"));
        var tenantId = tenant.TenantId!.Value.Value;
#pragma warning disable EF1002 // The ids are GUIDs this test read from the API, never text from outside.
        var ledger = await context.Database.SqlQueryRaw<decimal>(
            $"SELECT ISNULL(SUM(e.[Debit] - e.[Credit]), 0) AS [Value] FROM dbo.LedgerEntries e WHERE e.[TenantId] = '{tenantId}' AND e.[Account] = 'Receivable' AND e.[SubjectId] IN ({ids})").SingleAsync();
#pragma warning restore EF1002
        ledger.ShouldBe(totals.GetProperty("balanceDue").GetDecimal());

        using var payments = await client.GetAsync(new Uri($"/api/v1/reports/payment-register?{period}&amountFrom=600", UriKind.Relative));
        using var paymentJson = JsonDocument.Parse(await payments.Content.ReadAsStringAsync());
        paymentJson.RootElement.GetProperty("totals").GetProperty("payments").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task ADR0063_the_business_date_is_the_day_in_india_and_a_date_filter_takes_its_days_whole()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");

        // 23:59 in India on the 16th, then 00:00 on the 17th: the server's day turns at midnight in India, not in UTC.
        factory.Clock.Set(new DateTimeOffset(2026, 10, 16, 18, 29, 0, TimeSpan.Zero));
        (await BusinessDateAsync(client)).ShouldBe(new DateOnly(2026, 10, 16));
        factory.Clock.Set(new DateTimeOffset(2026, 10, 16, 18, 30, 0, TimeSpan.Zero));
        (await BusinessDateAsync(client)).ShouldBe(new DateOnly(2026, 10, 17));

        // A bill dated the 17th is in a filter that ends or begins on the 17th, and in no filter that stops on the 16th.
        var scene = await SceneAsync(client);
        var small = scene.Bills[0].Id;
        (await IdsAsync(client, "bills", "billDateFrom=2026-10-17&billDateTo=2026-10-17")).ShouldBe([small]);
        (await IdsAsync(client, "bills", "billDateTo=2026-10-16")).ShouldNotContain(small);
        (await IdsAsync(client, "bills", "billDateFrom=2026-10-18")).ShouldBeEmpty();
    }

    [Fact]
    public async Task ADR0063_the_master_lists_the_approvals_and_the_shifts_take_their_new_orders_and_filters()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "TenantAdmin");

        var older = await client.RegisterPatientAsync(TestData.NewPatient() with { FullName = "Asha Older", DateOfBirth = new DateOnly(1960, 1, 1) });
        var younger = await client.RegisterPatientAsync(TestData.NewPatient() with { FullName = "Asha Younger", DateOfBirth = new DateOnly(1990, 1, 1) });
        (await IdsAsync(client, "patients", "search=Asha&sort=dateOfBirth")).ShouldBe([older.Id, younger.Id]);
        (await IdsAsync(client, "patients", "search=Asha&sort=-dateOfBirth")).ShouldBe([younger.Id, older.Id]);

        // A category is matched whole and as text: % and _ are not patterns.
        var odd = await client.CreateServiceItemAsync(TestData.NewServiceItem() with { Category = "50%_off" });
        await client.CreateServiceItemAsync(TestData.NewServiceItem() with { Category = "50Xoff" });
        (await IdsAsync(client, "service-items", $"category={Uri.EscapeDataString("50%_off")}")).ShouldBe([odd.Id]);
        (await IdsAsync(client, "service-items", "category=50")).ShouldBeEmpty();

        // Only self-pay payers exist in this release (BR-103): the kind finds them all, and another kind none.
        var payer = await client.CreatePayerAsync(new CreatePayerRequest { Kind = PayerKind.SelfPay, PatientId = older.Id, Name = older.FullName });
        (await IdsAsync(client, "payers", "kind=SelfPay&sort=-name")).ShouldBe([payer.Id]);
        (await IdsAsync(client, "payers", "kind=Corporate")).ShouldBeEmpty();

        // Shifts by business date; the approvals by who asked.
        await client.EnsureShiftAsync();
        (await IdsAsync(client, "cashier-shifts", $"dateFrom={Today:yyyy-MM-dd}&dateTo={Today:yyyy-MM-dd}&sort=-businessDate")).Count.ShouldBe(1);
        (await IdsAsync(client, "cashier-shifts", $"dateTo={Today.AddDays(-1):yyyy-MM-dd}")).ShouldBeEmpty();
        (await IdsAsync(client, "approval-requests", $"requestedBy={Guid.NewGuid()}&sort=-requestedAtUtc")).ShouldBeEmpty();
        (await IdsAsync(client, "users", "sort=-lastSignInAtUtc")).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ADR0063_on_100000_bills_every_filter_and_order_answers_within_a_second_and_a_half()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "TenantAdmin");
        var scene = await SceneAsync(client);

        // 100,000 drafts copied in SQL from a draft of the scene, with totals and dates spread out. Drafts have no
        // posting, so the ledger is not touched and the reconciliation after the test finds nothing.
        var draft = await client.CreateBillAsync(new CreateBillRequest { PatientId = scene.Patient.Id, PayerId = scene.Payer.Id, BillDate = Today, DueDate = Today.AddDays(15), Lines = [BillingApi.FreeTextLine("Seed", 1m, 100m, 0m)] });
        await using (var administrator = AdministratorApiContext(SystemIdentity.Instance))
        {
            administrator.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
            var columns = await administrator.Database.SqlQueryRaw<string>(
                "SELECT c.[name] AS [Value] FROM sys.columns c WHERE c.[object_id] = OBJECT_ID(N'dbo.Bills') AND c.[is_computed] = 0 AND TYPE_NAME(c.[system_type_id]) <> 'timestamp' ORDER BY c.[column_id]").ToListAsync();
            string Value(string column) => column switch
            {
                "Id" => "NEWID()",
                "BillDate" => "DATEADD(DAY, -(n.[n] % 365), b.[BillDate])",
                "DueDate" => "DATEADD(DAY, 15 - (n.[n] % 365), b.[BillDate])",
                "SubTotal" or "Total" => "CAST((n.[n] % 50000) + 1 AS decimal(18, 2))",
                "BalanceDue" => "CASE WHEN b.[BalanceDue] = 0 THEN 0 ELSE CAST((n.[n] % 50000) + 1 AS decimal(18, 2)) END",
                _ => $"b.[{column}]",
            };
            var list = string.Join(", ", columns.Select(column => $"[{column}]"));
            var values = string.Join(", ", columns.Select(Value));
#pragma warning disable EF1002 // Column names read from the catalogue; the id is a GUID from the API.
            await administrator.Database.ExecuteSqlRawAsync(
                $"""
                WITH [n] AS (SELECT TOP (100000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS [n] FROM sys.all_objects a CROSS JOIN sys.all_objects b)
                INSERT INTO dbo.Bills ({list}) SELECT {values} FROM dbo.Bills b CROSS JOIN [n] WHERE b.[Id] = '{draft.Id}'
                """);
#pragma warning restore EF1002
        }

        string[] queries =
        [
            string.Empty, "sort=total", "sort=-total", "sort=billDate", "sort=-billDate", "sort=balanceDue", "sort=-balanceDue", "sort=number",
            "totalFrom=40000", "totalTo=10", "totalFrom=100&totalTo=200&sort=-total", "balanceDueFrom=1", "status=Draft&sort=total",
            "billDateFrom=2026-09-01&billDateTo=2026-09-30&sort=total", $"createdBy={tenant.UserId!.Value.Value}&sort=-total", "origin=Standard&totalFrom=49000",
            "search=deshmu&sort=-total", "page=2500&sort=total",
        ];

        var slowest = TimeSpan.Zero;
        var times = new List<string>();
        foreach (var query in queries)
        {
            // The first call warms the plan; the second is measured.
            await IdsAsync(client, "bills", query + "&pageSize=20", paged: false);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await IdsAsync(client, "bills", query + "&pageSize=20", paged: false);
            watch.Stop();
            times.Add($"{(query == string.Empty ? "(none)" : query)}: {watch.ElapsedMilliseconds} ms");
            slowest = watch.Elapsed > slowest ? watch.Elapsed : slowest;
        }

        Console.WriteLine("ADR 0063, 100,000 bills on this machine:\n" + string.Join('\n', times));
        slowest.ShouldBeLessThan(TimeSpan.FromSeconds(1.5), string.Join("; ", times));
    }

    private static async Task<DateOnly> BusinessDateAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/api/v1/system/business-date", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return DateOnly.Parse(json.RootElement.GetProperty("today").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
    }

    // The ids a list answers for a query string, in its order.
    private static async Task<List<Guid>> IdsAsync(HttpClient client, string list, string query, bool paged = true)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/{list}?{(paged ? "pageSize=100&" : string.Empty)}{query}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return [.. json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
    }

    // One patient who pays for herself: three bills of 126.00, 590.00 and 2,000.00, dated today, yesterday and the day
    // before and posted in that order; a payment of 500.00 on the last.
    private static async Task<Scene> SceneAsync(HttpClient client)
    {
        var kavita = await client.RegisterPatientAsync(TestData.NewPatient() with { FullName = "Kavita Deshmukh" });
        var self = await client.CreatePayerAsync(new CreatePayerRequest { Kind = PayerKind.SelfPay, PatientId = kavita.Id, Name = kavita.FullName });

        var small = await PostedBillAsync(client, kavita, self, Today, BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m));
        var middle = await PostedBillAsync(client, kavita, self, Today.AddDays(-1), BillingApi.FreeTextLine("OPD consultation", 1m, 500m, 18m));
        var large = await PostedBillAsync(client, kavita, self, Today.AddDays(-2), BillingApi.FreeTextLine("Room", 1m, 2000m, 0m));

        var payment = await CreatedAsync<PaymentDto>(client, "payments", new RecordPaymentRequest { PayerId = self.Id, PaymentDate = Today, Method = PaymentMethod.UPI, Reference = "UTR-1", Amount = 500m });
        using var allocated = await client.SendWithIfMatchAsync(
            HttpMethod.Post,
            new Uri($"/api/v1/payments/{payment.Id}/allocations", UriKind.Relative),
            new AllocatePaymentRequest { Allocations = [new AllocationRequest { BillId = large.Id, Amount = 500m }] },
            PayersApi.ETag(payment.Version));
        allocated.StatusCode.ShouldBe(HttpStatusCode.OK, await allocated.Content.ReadAsStringAsync());

        return new Scene(kavita, self, [small, middle, large], payment);
    }

    private static async Task<BillDto> PostedBillAsync(HttpClient client, PatientDto patient, PayerDto payer, DateOnly billDate, params BillLineRequest[] lines)
    {
        var draft = await client.CreateBillAsync(new CreateBillRequest { PatientId = patient.Id, PayerId = payer.Id, BillDate = billDate, DueDate = billDate.AddDays(15), Lines = lines });
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

    private sealed record Scene(PatientDto Patient, PayerDto Payer, IReadOnlyList<BillDto> Bills, PaymentDto Payment);
}
