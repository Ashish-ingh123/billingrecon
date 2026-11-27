using System.Net;
using BillingRecon.Api.Identity;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// A hospital's names for its accountant's ledgers, end to end on the pipeline that ships and a real SQL Server
/// (ADR 0062): none until an administrator saves them; a first save and a change under a version, audited with what
/// the names were and are; a name the file could not carry refused; only the administrator; and another hospital
/// reading and changing nothing of this one.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class AccountingLedgersApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Uri Ledgers = new("/api/v1/settings/accounting-ledgers", UriKind.Relative);
    private static readonly string None = PayersApi.ETag(AccountingLedgersMapping.NoVersion);

    [Fact]
    public async Task ADR0062_the_administrator_names_the_ledgers_under_a_version_and_each_change_is_audited()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");

        // Nothing saved: no name, and the version of a row that is not there.
        using (var empty = await administrator.GetAsync(Ledgers))
        {
            empty.StatusCode.ShouldBe(HttpStatusCode.OK, await empty.Content.ReadAsStringAsync());
            empty.Headers.ETag!.Tag.ShouldBe(None);
            (await empty.ReadAsAsync<AccountingLedgersDto>()).ShouldBe(AccountingLedgersMapping.NoneDto());
        }

        var names = new UpdateAccountingLedgersRequest { Receivable = "  Patient Receivables ", Revenue = "Hospital Income", Card = "HDFC Current A/c", Upi = "HDFC Current A/c", Cheque = "   " };

        using (var noVersion = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Ledgers, names, ifMatch: null))
        {
            noVersion.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        }

        using var saved = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Ledgers, names, None);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var first = await saved.ReadAsAsync<AccountingLedgersDto>();
        first.Version.ShouldNotBe(AccountingLedgersMapping.NoVersion);
        saved.Headers.ETag!.Tag.ShouldBe(PayersApi.ETag(first.Version));
        (first.Receivable, first.Revenue, first.Card, first.Upi, first.Cheque, first.TaxOutput).ShouldBe(("Patient Receivables", "Hospital Income", "HDFC Current A/c", "HDFC Current A/c", null, null));

        // The version of none no longer opens it; the version it answered does, and a name left out is taken away.
        using (var stale = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Ledgers, names with { TaxOutput = "GST Output" }, None))
        {
            await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
        }

        using var changed = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Ledgers, names with { TaxOutput = "GST Output", Upi = null }, PayersApi.ETag(first.Version));
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        var second = await changed.ReadAsAsync<AccountingLedgersDto>();
        (second.TaxOutput, second.Upi, second.Version == first.Version).ShouldBe(("GST Output", null, false));

        // The same names again change nothing: the same version, and no audit record more.
        using (var same = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Ledgers, names with { TaxOutput = "GST Output", Upi = null }, PayersApi.ETag(second.Version)))
        {
            (await same.ReadAsAsync<AccountingLedgersDto>()).Version.ShouldBe(second.Version);
        }

        using (var read = await administrator.GetAsync(Ledgers))
        {
            (await read.ReadAsAsync<AccountingLedgersDto>()).ShouldBe(second);
        }

        await using var context = ApiContext(admin);
        var row = await context.Set<AccountingLedgers>().AsNoTracking().SingleAsync();
        using var trail = await administrator.GetAsync(new Uri($"/api/v1/audit-records?entityType=AccountingLedgers&entityId={row.Id.Value}", UriKind.Relative));
        trail.StatusCode.ShouldBe(HttpStatusCode.OK, await trail.Content.ReadAsStringAsync());
        var records = (await trail.ReadAsAsync<PageOf<AuditRecordDto>>()).Items.OrderBy(record => record.Id).ToList();
        records.Select(record => record.Action).ShouldBe(["Changed", "Changed"]);
        records[0].Before.ShouldBeNull("the first save made the row");
        records[0].After.GetProperty("Upi").GetString().ShouldBe("HDFC Current A/c");
        records[1].Before.ShouldNotBeNull().GetProperty("Upi").GetString().ShouldBe("HDFC Current A/c");
        records[1].After.GetProperty("TaxOutput").GetString().ShouldBe("GST Output");
    }

    [Fact]
    public async Task ADR0062_a_name_too_long_or_on_two_lines_or_a_member_that_is_none_is_refused_by_its_field()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");

        using var tooLong = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Ledgers, new UpdateAccountingLedgersRequest { Cash = new string('a', 101), Card = "Bank\nA/c", Upi = new string('a', 100) }, None);
        var problem = await tooLong.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);
        problem.GetProperty("errors").EnumerateObject().Select(field => field.Name).ShouldBe(["cash", "card"], ignoreOrder: true);

        using var unknown = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Ledgers, new { cash = "Cash", sales = "Sales" }, None);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var context = ApiContext(admin);
        (await context.Set<AccountingLedgers>().AsNoTracking().CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("Supervisor")]
    [InlineData("BillingClerk")]
    [InlineData("Cashier")]
    [InlineData("Viewer")]
    public async Task ADR0062_only_the_administrator_reads_and_sets_the_names(string role)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var other = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), role);

        using var read = await other.GetAsync(Ledgers);
        using var written = await other.SendWithIfMatchAsync(HttpMethod.Put, Ledgers, new UpdateAccountingLedgersRequest { Cash = "Cash" }, None);

        await read.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        await written.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task BR002_another_hospital_reads_and_changes_nothing_of_these_names()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        var stranger = await NewApiTenantAsync();
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        using var other = factory.CreateClientFor(stranger, "TenantAdmin");

        using var saved = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Ledgers, new UpdateAccountingLedgersRequest { Cash = "Cash" }, None);
        var mine = await saved.ReadAsAsync<AccountingLedgersDto>();

        // The other hospital sees none, cannot use this one's version, and saves a row of its own.
        using (var read = await other.GetAsync(Ledgers))
        {
            (await read.ReadAsAsync<AccountingLedgersDto>()).ShouldBe(AccountingLedgersMapping.NoneDto());
        }

        using (var withMine = await other.SendWithIfMatchAsync(HttpMethod.Put, Ledgers, new UpdateAccountingLedgersRequest { Cash = "Petty Cash" }, PayersApi.ETag(mine.Version)))
        {
            await withMine.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
        }

        using (var theirs = await other.SendWithIfMatchAsync(HttpMethod.Put, Ledgers, new UpdateAccountingLedgersRequest { Cash = "Petty Cash" }, None))
        {
            theirs.StatusCode.ShouldBe(HttpStatusCode.OK, await theirs.Content.ReadAsStringAsync());
        }

        using var after = await administrator.GetAsync(Ledgers);
        (await after.ReadAsAsync<AccountingLedgersDto>()).ShouldBe(mine);
    }
}
