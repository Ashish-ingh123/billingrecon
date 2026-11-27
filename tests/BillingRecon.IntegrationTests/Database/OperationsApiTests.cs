using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Idempotency;
using BillingRecon.Infrastructure.Common.Operations;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.Infrastructure.Receivables;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The operator's alerts on a real SQL Server (ADR 0056), with the mail server replaced by one that keeps the mails:
/// the nightly reconciliation of every hospital sends one alert that names the hospital with drift and not the one
/// without, and nothing on a clean night; a sweep or a reconciliation that cannot run at all is an alert too.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class OperationsApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private const string Operator = "oncall@billingrecon.example";
    private static readonly DateOnly Today = new(2026, 10, 17);

    [Fact]
    public async Task ADR0056_the_nightly_reconciliation_alerts_once_naming_the_hospital_with_drift_and_not_the_other()
    {
        ExpectDrift(DriftIdentity.PayerReceivable);
        var mails = new CapturedMails();
        using var factory = WithAlerts(SqlServerFixture.ApiDatabase, mails);
        var drifting = await NewApiTenantAsync();
        var clean = await NewApiTenantAsync();
        var bill = await PostedBillAsync(factory, drifting);
        await PostedBillAsync(factory, clean);

        // A clean night: nothing is mailed.
        var first = await RunAsync(factory);
        (first.Hospitals, first.Drift.Count, first.Failed.Count).ShouldBe((2, 0, 0));
        await Should.ThrowAsync<TimeoutException>(() => mails.NextAsync(TimeSpan.FromMilliseconds(500)));

        // The drift is seeded with SQL: nothing in the application can produce it.
        await using (var administrator = AdministratorApiContext(SystemIdentity.Instance))
        {
            await administrator.Database.ExecuteSqlAsync($"UPDATE dbo.PayerBalances SET Receivable = Receivable + 1 WHERE PayerId = {bill.PayerId}");
        }

        var night = await RunAsync(factory);

        var (driftingCode, cleanCode) = (await CodeAsync(drifting), await CodeAsync(clean));
        night.Drift.Keys.ShouldBe([driftingCode]);
        night.Drift[driftingCode].Select(row => row.Identity).ShouldBe([DriftIdentity.PayerReceivable]);
        var mail = await mails.NextAsync();
        (mail.To, mail.Subject).ShouldBe((Operator, OperationsAlerts.SubjectPrefix + NightlyReconciliation.DriftSubject));
        mail.Body.ShouldContain($"Hospital {driftingCode}: 1 rows of drift.");
        mail.Body.ShouldContain("PayerReceivable ");
        mail.Body.ShouldContain("cached 501.00, ledger 500.00");
        mail.Body.ShouldContain("docs/runbook.md section 16.1");
        mail.Body.ShouldNotContain(cleanCode);

        // One mail for the night, whatever the number of hospitals.
        await Should.ThrowAsync<TimeoutException>(() => mails.NextAsync(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task ADR0056_a_sweep_or_a_reconciliation_that_cannot_run_is_an_alert()
    {
        var mails = new CapturedMails();
        using var factory = WithAlerts("BillingRecon_NoSuchDatabase", mails);
        var hosted = factory.Services.GetServices<IHostedService>().ToList();

        await hosted.OfType<IdempotencyKeySweeper>().Single().RunOnceAsync(CancellationToken.None);
        await hosted.OfType<NightlyReconciler>().Single().RunOnceAsync(CancellationToken.None);

        var subjects = new[] { (await mails.NextAsync()).Subject, (await mails.NextAsync()).Subject };
        subjects.ShouldBe(
            [
                OperationsAlerts.SubjectPrefix + "The sweep of expired idempotency keys failed",
                OperationsAlerts.SubjectPrefix + "The nightly reconciliation did not run",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task ADR0056_without_an_address_or_a_mail_server_an_alert_is_logged_and_not_mailed()
    {
        var mails = new CapturedMails();
        using var factory = new DatabaseApiFactory(SqlServer, configure: services => services.AddSingleton<IMailTransport>(mails));
        await using var scope = factory.Services.CreateAsyncScope();
        var alerts = scope.ServiceProvider.GetRequiredService<IOperationsAlerts>();

        alerts.Delivers.ShouldBeFalse();
        alerts.Raise("A test", "Nothing is sent.");

        await Should.ThrowAsync<TimeoutException>(() => mails.NextAsync(TimeSpan.FromMilliseconds(500)));
    }

    private DatabaseApiFactory WithAlerts(string database, CapturedMails mails)
        => new(SqlServer, database, configure: services =>
        {
            services.Configure<MailOptions>(options =>
            {
                options.Host = "smtp.billingrecon.example";
                options.From = "alerts@billingrecon.example";
                options.PublicBaseUrl = "https://billing.example";
            });
            services.Configure<AlertOptions>(options => options.MailTo = Operator);
            services.AddSingleton<IMailTransport>(mails);
        });

    private static async Task<ReconciliationNight> RunAsync(DatabaseApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<NightlyReconciliation>().RunAsync(CancellationToken.None);
    }

    private async Task<string> CodeAsync(FixedIdentity tenant)
    {
        await using var context = ApiContext(tenant);

        return (await context.Set<Tenant>().AsNoTracking().SingleAsync()).Code;
    }

    private static async Task<BillDto> PostedBillAsync(DatabaseApiFactory factory, FixedIdentity tenant)
    {
        using var clerk = factory.CreateClientFor(tenant, "BillingClerk");
        var (patient, payer) = await clerk.NewBillPartiesAsync();
        var draft = await clerk.CreateBillAsync(new CreateBillRequest
        {
            PatientId = patient.Id,
            PayerId = payer.Id,
            BillDate = Today,
            DueDate = Today.AddDays(15),
            Lines = [BillingApi.FreeTextLine("OPD consultation", 1m, 500m, 0m)],
        });
        using var posted = await clerk.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());

        return await posted.ReadAsAsync<BillDto>();
    }
}
