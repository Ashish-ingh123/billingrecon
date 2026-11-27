using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Application.Receivables;
using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Payments;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Serilog.Events;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The reconciliation report (US-41; BR-404; docs/database.md section 5.6; ADR 0036): an auditor, a Viewer, runs it and
/// finds no drift after the documents the application posted; a balance changed behind the application's back, and a
/// number skipped in a series, are each reported with the two figures that disagree, and written to the log as an error;
/// another hospital's drift is never in the report. The report corrects nothing.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ReconciliationApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly Uri Reconciliation = new("/api/v1/reports/reconciliation", UriKind.Relative);

    [Fact]
    public async Task US41_after_what_the_application_posted_the_reconciliation_finds_no_drift()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (tenant, _) = await PostedBillAndPaymentAsync(factory);
        using var viewer = Viewer(factory, tenant);

        var report = await ReconciliationAsync(viewer);

        (report.Items.Count, report.TotalCount, report.Totals.Rows).ShouldBe((0, 0, 0));
        report.Totals.ByIdentity.ShouldBeEmpty();
        report.Totals.CheckedAtUtc.ShouldBe(factory.Clock.UtcNow);
        report.Period.ShouldBeNull();
        factory.Logs.Events.ShouldNotContain(logEvent => logEvent.MessageTemplate.Text.StartsWith("The reconciliation found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task US41_a_balance_changed_behind_the_application_and_a_skipped_number_are_reported_and_logged_as_an_error()
    {
        ExpectDrift(DriftIdentity.BillBalance, DriftIdentity.NumberSeriesGap);
        using var factory = new DatabaseApiFactory(SqlServer);
        var (tenant, bill) = await PostedBillAndPaymentAsync(factory);
        using var viewer = Viewer(factory, tenant);
        var tenantId = tenant.TenantId!.Value.Value;

        // The seeded defects: what the application's login may not do, done as the administrator (ADR 0034).
        await using (var administrator = AdministratorApiContext(tenant))
        {
            (await administrator.Database.ExecuteSqlAsync($"UPDATE dbo.Bills SET [BalanceDue] = [BalanceDue] + 1.00 WHERE [TenantId] = {tenantId} AND [Id] = {bill.Id}")).ShouldBe(1);
            (await administrator.Database.ExecuteSqlAsync($"UPDATE dbo.NumberSeries SET [NextNumber] = [NextNumber] + 1 WHERE [TenantId] = {tenantId} AND [DocumentType] = 'Payment'")).ShouldBe(1);
        }

        using var response = await viewer.GetAsync(Reconciliation);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var report = await response.ReadAsAsync<ReportPage<DriftRowDto, ReconciliationTotalsDto>>();

        report.Items.Select(row => (row.Identity, row.Reference, row.Cached, row.Ledger)).ShouldBe(
        [
            (DriftIdentity.BillBalance, bill.Number, 217.00m, 216.00m),
            (DriftIdentity.NumberSeriesGap, "Payment 2026-27", 1.00m, 2.00m),
        ]);
        report.Items[0].SubjectId.ShouldBe(bill.Id);
        report.Totals.ByIdentity.ShouldBe([new DriftCountDto(DriftIdentity.BillBalance, 1), new DriftCountDto(DriftIdentity.NumberSeriesGap, 1)]);
        (report.TotalCount, report.Totals.Rows).ShouldBe((2, 2));

        // Loudly: an error in the log, with the count and the identities, and the tenant of the request.
        var error = factory.Logs.ForCorrelationId(response.GetCorrelationId())
            .Where(logEvent => logEvent.MessageTemplate.Text.StartsWith("The reconciliation found", StringComparison.Ordinal))
            .ShouldHaveSingleItem();
        error.Level.ShouldBe(LogEventLevel.Error);
        error.Scalar("DriftRows").ShouldBe(2);
        error.Scalar("TenantId")?.ToString().ShouldBe(tenantId.ToString());

        // It reports and corrects nothing: the next run finds the same; one page holds one row, the totals all of them.
        var page = await ReconciliationAsync(viewer, "?page=2&pageSize=1");
        page.Items.ShouldHaveSingleItem().Identity.ShouldBe(DriftIdentity.NumberSeriesGap);
        (page.TotalCount, page.Totals.Rows).ShouldBe((2, 2));
    }

    [Fact]
    public async Task US41_another_hospital_s_drift_is_never_in_the_report()
    {
        ExpectDrift(DriftIdentity.BillBalance);
        using var factory = new DatabaseApiFactory(SqlServer);
        var (first, _) = await PostedBillAndPaymentAsync(factory);
        var (other, otherBill) = await PostedBillAndPaymentAsync(factory);
        using var viewer = Viewer(factory, first);
        using var otherViewer = Viewer(factory, other);

        await using (var administrator = AdministratorApiContext(other))
        {
            (await administrator.Database.ExecuteSqlAsync($"UPDATE dbo.Bills SET [BalanceDue] = [BalanceDue] - 1.00 WHERE [TenantId] = {other.TenantId!.Value.Value} AND [Id] = {otherBill.Id}")).ShouldBe(1);
        }

        (await ReconciliationAsync(viewer)).TotalCount.ShouldBe(0);
        (await ReconciliationAsync(otherViewer)).Items.ShouldHaveSingleItem().SubjectId.ShouldBe(otherBill.Id);
    }

    private static async Task<ReportPage<DriftRowDto, ReconciliationTotalsDto>> ReconciliationAsync(HttpClient client, string query = "")
    {
        using var response = await client.GetAsync(new Uri(Reconciliation + query, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<ReportPage<DriftRowDto, ReconciliationTotalsDto>>();
    }

    // An auditor of the tenant: a Viewer, another user than the clerk.
    private static HttpClient Viewer(DatabaseApiFactory factory, FixedIdentity tenant)
        => factory.CreateClientFor(new FixedIdentity(tenant.TenantId, new Domain.Common.UserId(Guid.NewGuid())), "Viewer");

    // A tenant with a posted bill of 716.00 and a payment of 500.00 allocated to it, which leaves it owing 216.00.
    private async Task<(FixedIdentity Tenant, BillDto Bill)> PostedBillAndPaymentAsync(DatabaseApiFactory factory)
    {
        var tenant = await NewApiTenantAsync();
        using var clerk = factory.CreateClientFor(tenant, "BillingClerk", "Cashier");
        var (patient, payer) = await clerk.NewBillPartiesAsync();

        var draft = await clerk.CreateBillAsync(new CreateBillRequest
        {
            PatientId = patient.Id,
            PayerId = payer.Id,
            BillDate = Today,
            DueDate = Today.AddDays(15),
            Lines = [BillingApi.FreeTextLine("OPD consultation", 1m, 500m, 18m), BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m)],
        });
        using var posted = await clerk.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());
        var bill = await posted.ReadAsAsync<BillDto>();

        var payment = await RecordedAsync(clerk, payer);
        using var allocated = await clerk.SendWithIfMatchAsync(
            HttpMethod.Post,
            new Uri($"/api/v1/payments/{payment.Id}/allocations", UriKind.Relative),
            new AllocatePaymentRequest { Allocations = [new AllocationRequest { BillId = bill.Id, Amount = 500m }] },
            PayersApi.ETag(payment.Version));
        allocated.StatusCode.ShouldBe(HttpStatusCode.OK, await allocated.Content.ReadAsStringAsync());

        using var read = await clerk.GetAsync(BillingApi.Bill(bill.Id));

        return (tenant, await read.ReadAsAsync<BillDto>());
    }

    private static async Task<PaymentDto> RecordedAsync(HttpClient client, PayerDto payer)
    {
        await client.EnsureShiftAsync();
        using var response = await client.PostJsonAsync(
            new Uri("/api/v1/payments", UriKind.Relative),
            new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.Cash, Amount = 500m });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<PaymentDto>();
    }
}
