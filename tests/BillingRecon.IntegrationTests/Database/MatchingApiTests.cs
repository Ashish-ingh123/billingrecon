using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using BillingRecon.Api.Payments;
using BillingRecon.Api.Reconciliation;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Application.Receivables;
using BillingRecon.Application.Reconciliation;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.Domain.Reconciliation;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.Infrastructure.Receivables;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Automatic matching end to end, on the pipeline that ships and a real SQL Server (US-62, US-70; BR-1008 to BR-1011,
/// BR-1029; ADR 0066): payments recorded through the API, a statement imported, a run that confirms the certain lines and
/// proposes the rest; never a cash payment or a reversed one; another hospital's payment never a candidate; two runs at
/// once match nothing twice; a match posts nothing; and 10,000 lines against 10,000 payments, timed.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class MatchingApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly Uri Channels = new("/api/v1/settlement-channels", UriKind.Relative);
    private static readonly Uri Payments = new("/api/v1/payments", UriKind.Relative);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static ColumnMappingDto Mapping() => new()
    {
        HeaderRow = 1,
        DateColumn = "Date",
        DateFormat = "dd/MM/yyyy",
        DescriptionColumn = "Narration",
        ReferenceColumn = "Ref",
        AmountLayout = AmountLayout.TwoColumns,
        DebitColumn = "Debit",
        CreditColumn = "Credit",
        DecimalStyle = DecimalStyle.Point,
    };

    [Fact]
    public async Task US62_a_run_confirms_the_certain_lines_proposes_the_rest_and_posts_nothing()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (maker, checker) = await NewMakerAndCheckerAsync();
        using var client = factory.CreateClientFor(maker, "TenantAdmin", "Cashier", "BillingClerk", "Accountant");
        using var supervisor = factory.CreateClientFor(checker, "Supervisor");
        var (_, payer) = await client.NewBillPartiesAsync();

        var neft = await RecordAsync(client, payer, PaymentMethod.BankTransfer, "N3341", 12000m, Today.AddDays(-1));
        await RecordAsync(client, payer, PaymentMethod.UPI, "UPI88123", 500m, Today.AddDays(-1));
        await RecordAsync(client, payer, PaymentMethod.Cheque, "004512", 1800m, Today.AddDays(-2));
        await RecordAsync(client, payer, PaymentMethod.BankTransfer, "N8888", 999m, Today.AddDays(-1));
        await client.EnsureShiftAsync();
        await RecordAsync(client, payer, PaymentMethod.Cash, null, 700m, Today);
        var bounced = await RecordAsync(client, payer, PaymentMethod.BankTransfer, "N7777", 2500m, Today.AddDays(-1));
        await ReverseAsync(client, supervisor, bounced);

        var bank = await CreateBankAsync(client);
        const string statement =
            "Date,Narration,Ref,Debit,Credit\n" +
            "16/10/2026,NEFT CR RAVI KUMAR,N3341,,\"12,000.00\"\n" +
            "16/10/2026,UPI/CR/88123,UPI88123,,500.00\n" +
            "17/10/2026,CHQ DEP,4512,,\"1,800.00\"\n" +
            "17/10/2026,CASH DEPOSIT,,,700.00\n" +
            "16/10/2026,NEFT CR RETURNED LATER,N7777,,\"2,500.00\"\n" +
            "16/10/2026,IMPS CR,,,999.00\n";
        await ImportAsync(client, bank.Id, statement);
        var entriesBefore = await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerEntries");

        var run = await RunAsync(client, bank.Id);

        run.ShouldBe(new MatchingRunDto(6, 3, 1, 2, 0));
        var lines = (await LinesAsync(client, bank.Id)).ToDictionary(line => line.Description);
        lines["NEFT CR RAVI KUMAR"].Status.ShouldBe(StatementLineStatus.Matched);
        lines["NEFT CR RAVI KUMAR"].MatchReason.ShouldBe($"The reference N3341 and the amount ₹12,000.00 agree with {neft.Number} of 16 Oct 2026.");
        lines["UPI/CR/88123"].Status.ShouldBe(StatementLineStatus.Matched, "a bank no gateway settles into takes UPI too");
        lines["CHQ DEP"].Status.ShouldBe(StatementLineStatus.Matched, "4512 is 004512 without its leading zeros");
        lines["CASH DEPOSIT"].ShouldSatisfyAllConditions(
            line => line.Status.ShouldBe(StatementLineStatus.Unmatched),
            line => line.ProposalCount.ShouldBe(0, "a cash payment is never matched to a line (BR-1008)"));
        lines["NEFT CR RETURNED LATER"].ShouldSatisfyAllConditions(
            line => line.Status.ShouldBe(StatementLineStatus.Unmatched),
            line => line.ProposalCount.ShouldBe(0, "a reversed payment is never matched (BR-1008)"));
        lines["IMPS CR"].ShouldSatisfyAllConditions(
            line => line.Status.ShouldBe(StatementLineStatus.Unmatched),
            line => (line.ProposalScore, line.ProposalCount).ShouldBe((70, 1)),
            line => line.ProposalReason.ShouldNotBeNull().ShouldStartWith("The amount ₹999.00 agrees with"));

        // A match posts nothing: the ledger is as it was, and nothing drifts (BR-1007, BR-404).
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.LedgerEntries")).ShouldBe(entriesBefore);
        await ShouldHaveNoDriftAsync(maker);

        // A match is audited (BR-701); a second run reads the three lines left, and puts a fresh proposal in the old one's place.
        var match = await SingleIdAsync($"SELECT TOP (1) l.[MatchId] AS [Value] FROM dbo.MatchLines l JOIN dbo.StatementLines s ON s.[Id] = l.[StatementLineId] WHERE s.[Reference] = 'N3341'");
        (await TrailAsync(client, "Match", match)).ShouldBe(["Made"]);
        (await RunAsync(client, bank.Id)).ShouldBe(new MatchingRunDto(3, 0, 1, 2, 0));
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.MatchProposals WHERE [Status] = 'Open'")).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.MatchProposals WHERE [Status] = 'Withdrawn'")).ShouldBe(1);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Matches")).ShouldBe(3);
    }

    [Fact]
    public async Task BR1029_two_runs_at_once_match_each_line_and_each_payment_once()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "TenantAdmin", "Cashier", "BillingClerk", "Accountant");
        using var other = factory.CreateClientFor(tenant, "Accountant");
        var (_, payer) = await client.NewBillPartiesAsync();
        var csv = new StringBuilder("Date,Narration,Ref,Debit,Credit\n");
        for (var index = 1; index <= 40; index++)
        {
            await RecordAsync(client, payer, PaymentMethod.BankTransfer, $"UTR{index:000000}", 100m + index, Today);
            csv.Append(CultureInfo.InvariantCulture, $"17/10/2026,NEFT CR,UTR{index:000000},,{100 + index}.00\n");
        }

        var bank = await CreateBankAsync(client);
        await ImportAsync(client, bank.Id, csv.ToString());

        // One run takes the channel and the other answers 409, or they run one after the other: either way nothing twice.
        var answers = await Task.WhenAll(PostRunAsync(client, bank.Id), PostRunAsync(other, bank.Id));

        answers.ShouldAllBe(answer => answer.Status == HttpStatusCode.OK || answer.Code == ReconciliationErrors.MatchingRunInProgressCode);
        answers.Where(answer => answer.Run is not null).Sum(answer => answer.Run!.Matched).ShouldBe(40, "every line is matched by one run, never by both");
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.MatchingRuns WHERE [Status] = 'Running'")).ShouldBe(0, "a run never keeps the channel after it ends");
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Matches")).ShouldBe(40);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.MatchItems WHERE [IsActive] = 1")).ShouldBe(40);
        (await CountAsync("SELECT COUNT(DISTINCT [ItemId]) AS [Value] FROM dbo.MatchItems WHERE [IsActive] = 1")).ShouldBe(40);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.StatementLines WHERE [Status] = 'Matched'")).ShouldBe(40);
    }

    [Fact]
    public async Task BR1024_another_hospitals_payment_is_never_a_candidate()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var ours = await NewApiTenantAsync();
        var theirs = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(ours, "TenantAdmin", "Accountant");
        using var them = factory.CreateClientFor(theirs, "Cashier", "BillingClerk");
        var (_, payer) = await them.NewBillPartiesAsync();
        await RecordAsync(them, payer, PaymentMethod.BankTransfer, "N3341", 12000m, Today);

        var bank = await CreateBankAsync(client);
        await ImportAsync(client, bank.Id, "Date,Narration,Ref,Debit,Credit\n17/10/2026,NEFT CR,N3341,,\"12,000.00\"\n");

        (await RunAsync(client, bank.Id)).ShouldBe(new MatchingRunDto(1, 0, 0, 1, 0));
    }

    [Fact]
    public async Task BR1010_ten_thousand_lines_against_ten_thousand_payments_are_matched_in_under_a_minute()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "TenantAdmin", "Cashier", "BillingClerk", "Accountant");
        var (_, payer) = await client.NewBillPartiesAsync();
        var seed = await RecordAsync(client, payer, PaymentMethod.BankTransfer, "SEED", 1m, Today);

        // 10,000 payments copied in SQL from the one recorded: their own numbers, references and amounts. They have no
        // postings of their own, so this test measures matching and says nothing about the ledger.
        // The reconciliation after the test would find them, so the test says so: unapplied cash with no ledger entry, a
        // payment with no posting, and the numbers 2 to 100 of the series never taken.
        ExpectDrift(DriftIdentity.PaymentUnapplied, DriftIdentity.PaymentWithoutPosting, DriftIdentity.NumberSeriesGap);
        await using (var administrator = AdministratorApiContext(SystemIdentity.Instance))
        {
            administrator.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
            var columns = await administrator.Database.SqlQueryRaw<string>(
                "SELECT c.[name] AS [Value] FROM sys.columns c WHERE c.[object_id] = OBJECT_ID(N'dbo.Payments') AND c.[is_computed] = 0 AND TYPE_NAME(c.[system_type_id]) <> 'timestamp' ORDER BY c.[column_id]").ToListAsync();
            string Value(string column) => column switch
            {
                "Id" => "NEWID()",
                "Number" => "CONCAT('PAY/26-27/', RIGHT(CONCAT('000000', n.[n] + 100), 6))",
                "SequenceNumber" => "n.[n] + 100",
                "Reference" => "CONCAT('UTR', RIGHT(CONCAT('0000000', n.[n]), 7))",
                "Amount" or "UnappliedAmount" => "CAST(n.[n] + 100 AS decimal(18, 2))",
                _ => $"p.[{column}]",
            };
            var list = string.Join(", ", columns.Select(column => $"[{column}]"));
            var values = string.Join(", ", columns.Select(Value));
#pragma warning disable EF1002 // Column names read from the catalogue; the id is a GUID from the API.
            await administrator.Database.ExecuteSqlRawAsync(
                $"""
                WITH [n] AS (SELECT TOP (10000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS [n] FROM sys.all_objects a CROSS JOIN sys.all_objects b)
                INSERT INTO dbo.Payments ({list}) SELECT {values} FROM dbo.Payments p CROSS JOIN [n] WHERE p.[Id] = '{seed.Id}'
                """);
#pragma warning restore EF1002
        }

        // A statement of 10,000 lines: half carry the reference (confirmed), half only the amount (proposed).
        var csv = new StringBuilder("Date,Narration,Ref,Debit,Credit\n");
        for (var index = 1; index <= 10_000; index++)
        {
            csv.Append(CultureInfo.InvariantCulture, $"17/10/2026,NEFT CR {index},{(index % 2 == 0 ? $"UTR{index:0000000}" : string.Empty)},,{index + 100}.00\n");
        }

        var bank = await CreateBankAsync(client);
        await ImportAsync(client, bank.Id, csv.ToString());

        var watch = Stopwatch.StartNew();
        var run = await RunAsync(client, bank.Id);
        watch.Stop();

        Console.WriteLine($"BR-1010: a run over 10,000 lines against 10,001 payments: {watch.Elapsed.TotalSeconds:0.0} s ({run.Matched} matched, {run.Proposed} proposed).");
        (run.LinesRead, run.Matched, run.Proposed).ShouldBe((10_000, 5_000, 5_000));
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(60));
    }

    private async Task<(FixedIdentity Maker, FixedIdentity Checker)> NewMakerAndCheckerAsync()
    {
        var maker = await NewApiTenantAsync();

        return (maker, new FixedIdentity(maker.TenantId, new UserId(Guid.NewGuid())));
    }

    private static async Task<PaymentDto> RecordAsync(HttpClient client, PayerDto payer, PaymentMethod method, string? reference, decimal amount, DateOnly date)
    {
        using var response = await client.PostJsonAsync(Payments, new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = date, Method = method, Reference = reference, Amount = amount });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsAsync<PaymentDto>();
    }

    private static async Task ReverseAsync(HttpClient maker, HttpClient checker, PaymentDto payment)
    {
        using var asked = await maker.SendWithIfMatchAsync(HttpMethod.Post, new Uri($"/api/v1/payments/{payment.Id}/reverse", UriKind.Relative), new RequestReversalRequest { Reason = "Returned by the bank" }, PayersApi.ETag(payment.Version));
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());
        var waiting = await asked.ReadAsAsync<PaymentAllocationsDto>();
        using var approved = await checker.SendWithIfMatchAsync(HttpMethod.Post, new Uri($"/api/v1/payments/{payment.Id}/approve-reversal", UriKind.Relative), body: null, PayersApi.ETag(waiting.Payment.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
    }

    private static async Task<SettlementChannelDto> CreateBankAsync(HttpClient client)
    {
        using var response = await client.PostJsonAsync(Channels, new CreateSettlementChannelRequest { Kind = SettlementChannelKind.Bank, Name = "HDFC current", Mapping = Mapping() });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsAsync<SettlementChannelDto>();
    }

    private static async Task ImportAsync(HttpClient client, Guid channelId, string text)
    {
        using var dryRun = await client.PostJsonAsync(new Uri("/api/v1/statement-imports/dry-run", UriKind.Relative), new StatementFileRequest { ChannelId = channelId, FileName = "s.csv", Text = text });
        dryRun.StatusCode.ShouldBe(HttpStatusCode.OK, await dryRun.Content.ReadAsStringAsync());
        var hash = (await dryRun.ReadAsAsync<StatementDryRunDto>()).FileHash;
        using var import = await client.PostJsonAsync(new Uri("/api/v1/statement-imports", UriKind.Relative), new StatementFileRequest { ChannelId = channelId, FileName = "s.csv", Text = text, FileHash = hash });
        import.StatusCode.ShouldBe(HttpStatusCode.Created, await import.Content.ReadAsStringAsync());
    }

    private static async Task<MatchingRunDto> RunAsync(HttpClient client, Guid channelId)
    {
        using var response = await client.PostAsync(new Uri($"/api/v1/settlement-channels/{channelId}/matching-runs", UriKind.Relative), content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsAsync<MatchingRunDto>();
    }

    private static async Task<(HttpStatusCode Status, string? Code, MatchingRunDto? Run)> PostRunAsync(HttpClient client, Guid channelId)
    {
        using var response = await client.PostAsync(new Uri($"/api/v1/settlement-channels/{channelId}/matching-runs", UriKind.Relative), content: null);
        var body = await response.Content.ReadAsStringAsync();
        return response.StatusCode == HttpStatusCode.OK
            ? (response.StatusCode, null, JsonSerializer.Deserialize<MatchingRunDto>(body, Web))
            : (response.StatusCode, JsonDocument.Parse(body).RootElement.GetProperty("errorCode").GetString(), null);
    }

    private static async Task<IReadOnlyList<StatementLineDto>> LinesAsync(HttpClient client, Guid channelId)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/statement-lines?channelId={channelId}&pageSize=100", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsAsync<PageOf<StatementLineDto>>()).Items;
    }

    private static async Task<IReadOnlyList<string>> TrailAsync(HttpClient client, string entityType, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/audit-records?entityType={entityType}&entityId={id}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var trail = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return [.. trail.GetProperty("items").EnumerateArray().Select(record => record.GetProperty("action").GetString()!)];
    }

    private async Task<int> CountAsync(string sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);
        return await context.Database.SqlQueryRaw<int>(sql).SingleAsync();
    }

    private async Task<Guid> SingleIdAsync(string sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);
        return await context.Database.SqlQueryRaw<Guid>(sql).SingleAsync();
    }

    private async Task ShouldHaveNoDriftAsync(FixedIdentity tenant)
    {
        await using var context = ApiContext(tenant);
        (await new ReconciliationQuery(context, tenant).FindDriftAsync(CancellationToken.None)).ShouldBeEmpty();
    }
}
