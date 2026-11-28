using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BillingRecon.Api.Reconciliation;
using BillingRecon.Application.Common;
using BillingRecon.Application.Reconciliation;
using BillingRecon.Domain.Reconciliation;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Settlement channels and the import of a bank's statement end to end, on the pipeline that ships and a real SQL Server
/// (US-60, US-61; BR-1001 to BR-1005, BR-1024; ADR 0065): a TenantAdmin sets up the channels; an Accountant runs the dry
/// run, imports, and sees every line once; the same file twice, an overlapping file, a changed file and an inactive
/// channel; another hospital sees nothing; and the largest file allowed, timed.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class StatementImportsApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Uri Channels = new("/api/v1/settlement-channels", UriKind.Relative);
    private static readonly Uri DryRun = new("/api/v1/statement-imports/dry-run", UriKind.Relative);
    private static readonly Uri Imports = new("/api/v1/statement-imports", UriKind.Relative);
    private static readonly Uri Lines = new("/api/v1/statement-lines", UriKind.Relative);

    private const string Header = "Txn Date,Narration,Ref No,Debit,Credit,Balance\n";

    private const string October16 =
        Header +
        "16/10/2026,NEFT CR RAVI KUMAR,N3341,,\"12,000.00\",\"1,52,300.50\"\n" +
        "16/10/2026,UPI CONSULT,UPI001,,500.00,152800.50\n" +
        "16/10/2026,UPI CONSULT,UPI001,,500.00,152800.50\n" +
        "16/10/2026,BANK CHARGES,,59.00,,152741.50\n" +
        "16/10/2026,BROKEN ROW,,abc,,\n";

    private static ColumnMappingDto Mapping() => new()
    {
        HeaderRow = 1,
        DateColumn = "Txn Date",
        DateFormat = "dd/MM/yyyy",
        DescriptionColumn = "Narration",
        ReferenceColumn = "Ref No",
        AmountLayout = AmountLayout.TwoColumns,
        DebitColumn = "Debit",
        CreditColumn = "Credit",
        BalanceColumn = "Balance",
        DecimalStyle = DecimalStyle.Point,
    };

    [Fact]
    public async Task US60_a_tenant_administrator_sets_up_a_bank_and_a_gateway_and_an_accountant_reads_them()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        using var accountant = factory.CreateClientFor(admin, "Accountant");

        var bank = await CreateBankAsync(administrator, "HDFC current");
        (bank.Kind, bank.Name, bank.AccountLabel, bank.DateWindowDays, bank.Status).ShouldBe((SettlementChannelKind.Bank, "HDFC current", "4521", 3, SettlementChannelStatus.Active));
        bank.Mapping.ShouldBe(Mapping());

        // A gateway settles into an active bank of the hospital, and the name is unique whatever its case (BR-1001).
        using (var gateway = await administrator.PostJsonAsync(Channels, new CreateSettlementChannelRequest { Kind = SettlementChannelKind.Gateway, Name = "Razorpay", Gateway = PaymentGateway.Razorpay, BankChannelId = bank.Id, DateWindowDays = 2 }))
        {
            gateway.StatusCode.ShouldBe(HttpStatusCode.Created, await gateway.Content.ReadAsStringAsync());
            var created = await gateway.ReadAsAsync<SettlementChannelDto>();
            (created.Gateway, created.BankChannelId, created.Mapping).ShouldBe((PaymentGateway.Razorpay, bank.Id, (ColumnMappingDto?)null));
        }

        using (var duplicate = await administrator.PostJsonAsync(Channels, Bank("hdfc CURRENT")))
        {
            await duplicate.ShouldBeProblemAsync(HttpStatusCode.Conflict, ReconciliationErrors.SettlementChannelNameDuplicateCode);
        }

        using (var nowhere = await administrator.PostJsonAsync(Channels, new CreateSettlementChannelRequest { Kind = SettlementChannelKind.Gateway, Name = "PayU", Gateway = PaymentGateway.Razorpay, BankChannelId = Guid.NewGuid() }))
        {
            await nowhere.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, ReconciliationErrors.SettlementChannelBankInvalidCode);
        }

        using (var fullNumber = await administrator.PostJsonAsync(Channels, Bank("ICICI") with { AccountLabel = "50100234524521" }))
        {
            (await fullNumber.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed")).GetRawText().ShouldContain("never the full number");
        }

        // The Accountant reads the channels and changes none of them.
        var list = await GetAsAsync<PageOf<SettlementChannelDto>>(accountant, Channels);
        list.ShouldNotBeNull().Items.Select(channel => channel.Name).ShouldBe(["HDFC current", "Razorpay"]);
        using (var refused = await accountant.PostJsonAsync(Channels, Bank("SBI")))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        // A change of the mapping, under If-Match, and the channel's trail (BR-701).
        using (var changed = await administrator.SendWithIfMatchAsync(HttpMethod.Patch, Channel(bank.Id), new UpdateSettlementChannelRequest { Name = "HDFC current", AccountLabel = "4521", DateWindowDays = 5, Mapping = Mapping() with { DateFormat = "dd-MM-yyyy" } }, PayersApi.ETag(bank.Version)))
        {
            changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
            var after = await changed.ReadAsAsync<SettlementChannelDto>();
            (after.DateWindowDays, after.Mapping!.DateFormat).ShouldBe((5, "dd-MM-yyyy"));
        }

        var trail = await TrailAsync(accountant, "SettlementChannel", bank.Id);
        trail.ShouldBe(["Created", "Changed", "MappingChanged"]);
    }

    [Fact]
    public async Task US61_an_accountant_runs_the_dry_run_then_imports_and_sees_every_line_once()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        using var accountant = factory.CreateClientFor(admin, "Accountant");
        var bank = await CreateBankAsync(administrator, "HDFC current");

        // The dry run stores nothing and says what the import would do, with the broken row and its reason (BR-1003).
        var dryRun = await DryRunAsync(accountant, bank.Id, "hdfc-16-oct.csv", October16);
        (dryRun.RowsRead, dryRun.LinesToAdd, dryRun.LinesAlreadyHeld, dryRun.RowsRefused, dryRun.AlreadyImported).ShouldBe((5, 4, 0, 1, false));
        (dryRun.MoneyIn, dryRun.MoneyOut).ShouldBe((13000m, 59m));
        dryRun.Refusals.ShouldHaveSingleItem().ShouldBe(new StatementRefusal(6, "the debit \"abc\" is not a number."));
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.StatementLines")).ShouldBe(0, "a dry run stores nothing");

        // The import stores the four lines, the two identical consultations both (BR-1004).
        var import = await ImportAsync(accountant, bank.Id, "hdfc-16-oct.csv", October16, dryRun.FileHash);
        (import.RowsRead, import.LinesAdded, import.LinesAlreadyHeld, import.RowsRefused).ShouldBe((5, 4, 0, 1));
        (import.FirstDate, import.LastDate).ShouldBe((new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 16)));
        import.CreatedByUserId.ShouldBe(admin.UserId?.Value);

        var lines = await LinesAsync(accountant, $"channelId={bank.Id}&sort=amount");
        lines.Select(line => (line.Direction, line.Amount, line.Reference, line.Status)).ShouldBe(
        [
            (StatementDirection.Out, 59m, null, StatementLineStatus.Unmatched),
            (StatementDirection.In, 500m, "UPI001", StatementLineStatus.Unmatched),
            (StatementDirection.In, 500m, "UPI001", StatementLineStatus.Unmatched),
            (StatementDirection.In, 12000m, "N3341", StatementLineStatus.Unmatched),
        ]);
        lines[3].Balance.ShouldBe(152300.50m);
        lines[3].RawText.ShouldBe("16/10/2026,NEFT CR RAVI KUMAR,N3341,,\"12,000.00\",\"1,52,300.50\"");
        (await LinesAsync(accountant, "search=consult")).Count.ShouldBe(2);
        (await LinesAsync(accountant, "direction=Out")).ShouldHaveSingleItem().Description.ShouldBe("BANK CHARGES");

        // The same file again adds nothing (BR-1004), and its dry run says so.
        (await DryRunAsync(accountant, bank.Id, "again.csv", October16)).ShouldSatisfyAllConditions(
            again => again.AlreadyImported.ShouldBeTrue(),
            again => again.LinesToAdd.ShouldBe(0),
            again => again.LinesAlreadyHeld.ShouldBe(4));
        using (var twice = await accountant.PostJsonAsync(Imports, File(bank.Id, "again.csv", October16, dryRun.FileHash)))
        {
            await twice.ShouldBeProblemAsync(HttpStatusCode.Conflict, ReconciliationErrors.StatementFileAlreadyImportedCode);
        }

        // A later file that overlaps: the consultations it repeats are held, a third identical one and the new day are added.
        var overlapping = Header +
            "16/10/2026,UPI CONSULT,UPI001,,500.00,152800.50\n" +
            "16/10/2026,UPI CONSULT,UPI001,,500.00,152800.50\n" +
            "16/10/2026,UPI CONSULT,UPI001,,500.00,152800.50\n" +
            "17/10/2026,NEFT CR ASHA RAO,N3399,,\"2,000.00\",154741.50\n";
        var second = await ImportAsync(accountant, bank.Id, "hdfc-17-oct.csv", overlapping, (await DryRunAsync(accountant, bank.Id, "hdfc-17-oct.csv", overlapping)).FileHash);
        (second.LinesAdded, second.LinesAlreadyHeld).ShouldBe((2, 2));
        (await LinesAsync(accountant, $"channelId={bank.Id}")).Count.ShouldBe(6);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.StatementLines WHERE [Reference] = 'UPI001' AND [Occurrence] = 3")).ShouldBe(1);

        // Another file than the dry run's is refused whole (BR-1003).
        using (var changed = await accountant.PostJsonAsync(Imports, File(bank.Id, "edited.csv", October16 + "16/10/2026,EXTRA,,,1.00,\n", dryRun.FileHash)))
        {
            await changed.ShouldBeProblemAsync(HttpStatusCode.Conflict, ReconciliationErrors.StatementFileChangedCode);
        }

        // The imports, the latest first, and one import's trail: one record for the whole file.
        var imports = await GetAsAsync<PageOf<StatementImportDto>>(accountant, Imports);
        imports.ShouldNotBeNull().Items.Select(item => item.FileName).ShouldBe(["hdfc-17-oct.csv", "hdfc-16-oct.csv"], ignoreOrder: true);
        (await TrailAsync(accountant, "StatementImport", import.Id)).ShouldBe(["Recorded"]);
    }

    [Fact]
    public async Task BR803_a_retried_import_with_the_same_key_answers_the_first_import_and_stores_nothing_twice()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin", "Accountant");
        var bank = await CreateBankAsync(client, "HDFC current");
        var hash = (await DryRunAsync(client, bank.Id, "a.csv", October16)).FileHash;
        var key = Guid.NewGuid();

        using var first = await client.PostJsonAsync(Imports, File(bank.Id, "a.csv", October16, hash), key);
        using var retry = await client.PostJsonAsync(Imports, File(bank.Id, "a.csv", October16, hash), key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        retry.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await retry.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.StatementImports")).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.StatementLines")).ShouldBe(4);
    }

    [Fact]
    public async Task BR1001_an_inactive_channel_and_a_gateway_take_no_statement_and_an_unreadable_file_says_why()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin", "Accountant");
        var bank = await CreateBankAsync(client, "HDFC current");

        using (var gatewayResponse = await client.PostJsonAsync(Channels, new CreateSettlementChannelRequest { Kind = SettlementChannelKind.Gateway, Name = "Razorpay", Gateway = PaymentGateway.Razorpay, BankChannelId = bank.Id }))
        {
            var gateway = await gatewayResponse.ReadAsAsync<SettlementChannelDto>();
            using var refused = await client.PostJsonAsync(DryRun, File(gateway.Id, "x.csv", October16));
            await refused.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, ReconciliationErrors.SettlementChannelNotBankCode);
        }

        using (var unreadable = await client.PostJsonAsync(DryRun, File(bank.Id, "x.csv", "Date,Amount\n16/10/2026,5\n")))
        {
            (await unreadable.ShouldBeProblemAsync(HttpStatusCode.BadRequest, ReconciliationErrors.StatementFileUnreadableCode)).GetRawText().ShouldContain("Txn Date");
        }

        using (var deactivated = await client.SendWithIfMatchAsync(HttpMethod.Post, new Uri($"/api/v1/settlement-channels/{bank.Id}/deactivate", UriKind.Relative), null, PayersApi.ETag(bank.Version)))
        {
            deactivated.StatusCode.ShouldBe(HttpStatusCode.OK, await deactivated.Content.ReadAsStringAsync());
        }

        using var inactive = await client.PostJsonAsync(DryRun, File(bank.Id, "x.csv", October16));
        await inactive.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, ReconciliationErrors.SettlementChannelInactiveCode);
    }

    [Fact]
    public async Task BR1024_another_hospital_never_sees_a_channel_an_import_or_a_line()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var ours = await NewApiTenantAsync();
        var theirs = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(ours, "TenantAdmin", "Accountant");
        using var other = factory.CreateClientFor(theirs, "TenantAdmin", "Accountant");
        var bank = await CreateBankAsync(client, "HDFC current");
        var import = await ImportAsync(client, bank.Id, "a.csv", October16, (await DryRunAsync(client, bank.Id, "a.csv", October16)).FileHash);

        using (var channel = await other.GetAsync(Channel(bank.Id)))
        {
            await channel.ShouldBeProblemAsync(HttpStatusCode.NotFound, ReconciliationErrors.SettlementChannelNotFoundCode);
        }

        using (var found = await other.GetAsync(new Uri($"/api/v1/statement-imports/{import.Id}", UriKind.Relative)))
        {
            await found.ShouldBeProblemAsync(HttpStatusCode.NotFound, ReconciliationErrors.StatementImportNotFoundCode);
        }

        using (var dryRun = await other.PostJsonAsync(DryRun, File(bank.Id, "a.csv", October16)))
        {
            await dryRun.ShouldBeProblemAsync(HttpStatusCode.NotFound, ReconciliationErrors.SettlementChannelNotFoundCode);
        }

        (await LinesAsync(other, $"channelId={bank.Id}")).ShouldBeEmpty();
        (await GetAsAsync<PageOf<StatementImportDto>>(other, Imports)).ShouldNotBeNull().Items.ShouldBeEmpty();
        (await GetAsAsync<PageOf<SettlementChannelDto>>(other, Channels)).ShouldNotBeNull().Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task BR1003_a_file_of_1_MB_is_read_and_imported_in_well_under_30_seconds()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin", "Accountant");
        var bank = await CreateBankAsync(client, "HDFC current");

        // As many distinct lines as fit 900,000 characters, about 6,000 rows of a real statement's width.
        var text = new StringBuilder(Header);
        var row = 0;
        while (true)
        {
            var line = string.Create(CultureInfo.InvariantCulture, $"{1 + (row % 16):00}/10/2026,NEFT CR PATIENT {row:000000} HOSPITAL BILL PAYMENT,N{row:0000000},,{100 + row}.00,{1_000_000 + row}.00\n");
            if (text.Length + line.Length > StatementReader.MaxFileLength)
            {
                break;
            }

            text.Append(line);
            row++;
        }

        var watch = Stopwatch.StartNew();
        var dryRun = await DryRunAsync(client, bank.Id, "big.csv", text.ToString());
        var dryRunTime = watch.Elapsed;
        var import = await ImportAsync(client, bank.Id, "big.csv", text.ToString(), dryRun.FileHash);
        watch.Stop();

        import.LinesAdded.ShouldBe(row);
        row.ShouldBeGreaterThan(5000);
        Console.WriteLine($"BR-1003: {row} lines, {text.Length:N0} characters: dry run {dryRunTime.TotalSeconds:0.0} s, dry run and import {watch.Elapsed.TotalSeconds:0.0} s.");
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
    }

    private static CreateSettlementChannelRequest Bank(string name) => new()
    {
        Kind = SettlementChannelKind.Bank,
        Name = name,
        AccountLabel = "4521",
        Mapping = Mapping(),
    };

    private static StatementFileRequest File(Guid channelId, string fileName, string text, string? fileHash = null)
        => new() { ChannelId = channelId, FileName = fileName, Text = text, FileHash = fileHash };

    private static Uri Channel(Guid id) => new($"/api/v1/settlement-channels/{id}", UriKind.Relative);

    private static async Task<SettlementChannelDto> CreateBankAsync(HttpClient client, string name)
    {
        using var response = await client.PostJsonAsync(Channels, Bank(name));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsAsync<SettlementChannelDto>();
    }

    private static async Task<StatementDryRunDto> DryRunAsync(HttpClient client, Guid channelId, string fileName, string text)
    {
        using var response = await client.PostJsonAsync(DryRun, File(channelId, fileName, text));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsAsync<StatementDryRunDto>();
    }

    private static async Task<StatementImportDto> ImportAsync(HttpClient client, Guid channelId, string fileName, string text, string fileHash)
    {
        using var response = await client.PostJsonAsync(Imports, File(channelId, fileName, text, fileHash));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        response.Headers.Location.ShouldNotBeNull();
        return await response.ReadAsAsync<StatementImportDto>();
    }

    private static async Task<IReadOnlyList<StatementLineDto>> LinesAsync(HttpClient client, string query)
    {
        var page = await GetAsAsync<PageOf<StatementLineDto>>(client, Lines.WithQuery(query + "&pageSize=100"));
        return page.ShouldNotBeNull().Items;
    }

    private static async Task<T> GetAsAsync<T>(HttpClient client, Uri uri)
        where T : class
    {
        using var response = await client.GetAsync(uri);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsAsync<T>();
    }

    private async Task<int> CountAsync(string sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQueryRaw<int>(sql).SingleAsync();
    }

    private static async Task<IReadOnlyList<string>> TrailAsync(HttpClient client, string entityType, Guid id)
    {
        var trail = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/audit-records?entityType={entityType}&entityId={id}", UriKind.Relative));
        return [.. trail.GetProperty("items").EnumerateArray().Select(record => record.GetProperty("action").GetString()!)];
    }
}
