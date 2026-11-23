using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Identity;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Payments;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The tenant's settings and its prefix changes end to end, on the pipeline that ships and a real SQL Server (US-03;
/// BR-205, BR-207, BR-603, BR-607, BR-802; ADR 0033): the settings change at the version read, apply from the next
/// request and are audited; a prefix change waits for a second person, whose approval numbers the next document with the
/// new prefix while the numbers issued keep theirs; a prefix belongs to one kind of document for good; and the trails of
/// the settings are the TenantAdmin's alone.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class SettingsApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Uri Settings = new("/api/v1/settings", UriKind.Relative);
    private static readonly Uri PrefixChanges = new("/api/v1/settings/prefix-changes", UriKind.Relative);

    // 17 October 2026 in India: fiscal year 2026-27.
    private static readonly DateOnly Today = new(2026, 10, 17);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    // 2 x 60.00 at 5%: 126.00.
    private static readonly BillLineRequest Dressing = BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m);

    [Fact]
    public async Task US03_a_tenant_admin_changes_the_settings_and_the_next_request_applies_them()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");
        var before = await SettingsAsync(client);
        (before.Settings.PostingApprovalThreshold, before.Settings.MaxBackdateDays, before.Settings.DefaultDueDays).ShouldBe(((decimal?)null, 7, 0));
        before.Settings.Prefixes.ShouldBe(new DocumentPrefixesDto("BIL", "CN", "PAY", "ADJ", "REF"));

        using var changed = await client.SendWithIfMatchAsync(HttpMethod.Patch, Settings, new UpdateSettingsRequest { PostingApprovalThreshold = 100m, MaxBackdateDays = 0, DefaultDueDays = 15 }, before.ETag);

        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        var after = await changed.ReadAsAsync<TenantSettingsDto>();
        (after.PostingApprovalThreshold, after.MaxBackdateDays, after.DefaultDueDays).ShouldBe(((decimal?)100m, 0, 15));
        changed.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(after.Version));
        after.Version.ShouldNotBe(before.Settings.Version);

        // The next requests: a bill of 126.00 waits for a second person, a bill without a due date is due in 15 days,
        // and a payment dated yesterday is refused.
        var (patient, payer) = await client.NewBillPartiesAsync();
        var draft = await client.CreateBillAsync(new CreateBillRequest { PatientId = patient.Id, PayerId = payer.Id, BillDate = Today, Lines = [Dressing] });
        draft.DueDate.ShouldBe(Today.AddDays(15));
        using var posted = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        posted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await posted.Content.ReadAsStringAsync());
        (await posted.ReadAsAsync<BillDto>()).Status.ShouldBe(BillStatus.PendingApproval);
        using var yesterday = await client.PostJsonAsync(new Uri("/api/v1/payments", UriKind.Relative), new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = Today.AddDays(-1), Method = PaymentMethod.Cash, Amount = 10m });
        await yesterday.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PaymentsErrors.PaymentDateTooOldCode);

        // BR-607: audited as the administrator's change, the row before and after.
        var record = (await TrailAsync(client, "Tenant", admin.TenantId!.Value.Value)).ShouldHaveSingleItem();
        (record.Action, record.UserId).ShouldBe(("SettingsChanged", (Guid?)admin.UserId!.Value.Value));
        record.Before.ShouldNotBeNull().GetProperty("MaxBackdateDays").GetInt32().ShouldBe(7);
        record.After.GetProperty("MaxBackdateDays").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task US03_a_stale_version_is_refused_a_threshold_left_out_means_none_and_only_a_tenant_admin_may()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");
        using var supervisor = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "Supervisor");
        var before = await SettingsAsync(client);

        using var first = await client.SendWithIfMatchAsync(HttpMethod.Patch, Settings, new UpdateSettingsRequest { PostingApprovalThreshold = 500m, MaxBackdateDays = 7, DefaultDueDays = 0 }, before.ETag);
        using var stale = await client.SendWithIfMatchAsync(HttpMethod.Patch, Settings, new UpdateSettingsRequest { MaxBackdateDays = 7, DefaultDueDays = 0 }, before.ETag);
        var current = await SettingsAsync(client);
        using var noThreshold = await client.SendWithIfMatchAsync(HttpMethod.Patch, Settings, new UpdateSettingsRequest { MaxBackdateDays = 7, DefaultDueDays = 0 }, current.ETag);
        using var invalid = await client.SendWithIfMatchAsync(HttpMethod.Patch, Settings, new UpdateSettingsRequest { PostingApprovalThreshold = 0m, MaxBackdateDays = 366 }, current.ETag);
        using var noVersion = await client.SendWithIfMatchAsync(HttpMethod.Patch, Settings, new UpdateSettingsRequest { MaxBackdateDays = 7, DefaultDueDays = 0 }, ifMatch: null);
        using var bySupervisor = await supervisor.GetAsync(Settings);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
        noThreshold.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await noThreshold.ReadAsAsync<TenantSettingsDto>()).PostingApprovalThreshold.ShouldBeNull();
        var errors = (await invalid.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode)).GetProperty("errors");
        errors.EnumerateObject().Select(field => field.Name).ShouldBe(["postingApprovalThreshold", "maxBackdateDays", "defaultDueDays"], ignoreOrder: true);
        await noVersion.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        await bySupervisor.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task BR802_a_prefix_change_waits_for_a_second_person_whose_approval_numbers_the_next_bill_with_it()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");
        using var supervisor = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "Supervisor");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var first = await PostedBillAsync(client, patient.Id, payer.Id);
        first.Number.ShouldBe("BIL/26-27/000001");

        // Asked for: the change waits, in the checkers' inbox, and nothing changes yet.
        var change = await RequestedAsync(client, DocumentType.Bill, " inv ");
        (change.FromPrefix, change.ToPrefix, change.Status, change.RequestedByUserId).ShouldBe(("BIL", "INV", PrefixChangeStatus.Pending, (Guid?)admin.UserId!.Value.Value));
        using var inbox = await supervisor.GetAsync(new Uri("/api/v1/approval-requests?documentType=PrefixChange", UriKind.Relative));
        (await inbox.ReadAsAsync<PageOf<ApprovalRequestDto>>()).Items.ShouldHaveSingleItem().DocumentId.ShouldBe(change.Id);
        (await PostedBillAsync(client, patient.Id, payer.Id)).Number.ShouldBe("BIL/26-27/000002");

        // Approved by the Supervisor, who reads the change first: the next number carries the new prefix.
        using var read = await supervisor.GetAsync(PrefixChange(change.Id));
        var waiting = await read.ReadAsAsync<PrefixChangeDto>();
        using var approved = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, PrefixChange(change.Id, "approve"), new ApprovePrefixChangeRequest { Comment = "New stationery" }, PayersApi.ETag(waiting.Version));

        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        (await approved.ReadAsAsync<PrefixChangeDto>()).Status.ShouldBe(PrefixChangeStatus.Approved);
        (await SettingsAsync(client)).Settings.Prefixes.Bill.ShouldBe("INV");
        (await PostedBillAsync(client, patient.Id, payer.Id)).Number.ShouldBe("INV/26-27/000003");
        (await BillAsync(client, first.Id)).Number.ShouldBe("BIL/26-27/000001", "a number already issued keeps its prefix");

        // Audited: the request and its approval, the default on the tenant, the prefix of the series.
        (await TrailAsync(client, "PrefixChange", change.Id)).Select(record => record.Action).ShouldBe(["Requested", "Approved"]);
        (await TrailAsync(client, "Tenant", admin.TenantId!.Value.Value)).Select(record => record.Action).ShouldBe(["PrefixChanged"]);
        var series = await SeriesIdAsync(admin, DocumentType.Bill);
        (await TrailAsync(client, "NumberSeries", series)).ShouldHaveSingleItem().Action.ShouldBe("PrefixChanged");

        // ADR 0033 point 9: those trails are the TenantAdmin's alone.
        foreach (var (kind, id) in new[] { ("Tenant", admin.TenantId.Value.Value), ("PrefixChange", change.Id), ("NumberSeries", series) })
        {
            using var refused = await supervisor.GetAsync(Trail(kind, id));
            await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }
    }

    [Fact]
    public async Task BR603_the_maker_cannot_decide_their_own_change_and_a_rejection_says_why_and_changes_nothing()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");
        using var otherAdmin = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "TenantAdmin");
        var change = await RequestedAsync(client, DocumentType.Refund, "RFD");

        using var own = await client.SendWithIfMatchAsync(HttpMethod.Post, PrefixChange(change.Id, "approve"), body: null, PayersApi.ETag(change.Version));
        using var noReason = await otherAdmin.SendWithIfMatchAsync(HttpMethod.Post, PrefixChange(change.Id, "reject"), new RejectPrefixChangeRequest(), PayersApi.ETag(change.Version));
        using var rejected = await otherAdmin.SendWithIfMatchAsync(HttpMethod.Post, PrefixChange(change.Id, "reject"), new RejectPrefixChangeRequest { Comment = "The printed forms say REF" }, PayersApi.ETag(change.Version));

        await own.ShouldBeProblemAsync(HttpStatusCode.Forbidden, CommonErrors.ApprovalCheckerIsMakerCode);
        await noReason.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.ValidationFailedCode);
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        var decided = await rejected.ReadAsAsync<PrefixChangeDto>();
        decided.Status.ShouldBe(PrefixChangeStatus.Rejected);
        (await SettingsAsync(client)).Settings.Prefixes.Refund.ShouldBe("REF");
        using var again = await otherAdmin.SendWithIfMatchAsync(HttpMethod.Post, PrefixChange(change.Id, "approve"), body: null, PayersApi.ETag(decided.Version));
        await again.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.PrefixChangeNotPendingCode);

        // A rejected prefix was never used: another type may ask for it.
        (await RequestedAsync(client, DocumentType.Adjustment, "RFD")).Status.ShouldBe(PrefixChangeStatus.Pending);
    }

    [Fact]
    public async Task ADR0033_a_prefix_belongs_to_one_type_for_good_and_one_change_of_a_type_waits_at_a_time()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");
        using var supervisor = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "Supervisor");
        var (patient, payer) = await client.NewBillPartiesAsync();
        await PostedBillAsync(client, patient.Id, payer.Id);

        // Bills leave BIL, which their series used, for INV.
        var change = await RequestedAsync(client, DocumentType.Bill, "INV");
        using var waitingTwice = await client.PostJsonAsync(PrefixChanges, new RequestPrefixChangeRequest { DocumentType = DocumentType.Bill, Prefix = "INX" });
        await waitingTwice.ShouldBeProblemAsync(HttpStatusCode.Conflict, IdentityErrors.PrefixChangePendingCode);
        using var reserved = await client.PostJsonAsync(PrefixChanges, new RequestPrefixChangeRequest { DocumentType = DocumentType.Payment, Prefix = "INV" });
        await reserved.ShouldBeProblemAsync(HttpStatusCode.Conflict, IdentityErrors.PrefixInUseCode);
        using var approved = await supervisor.SendWithIfMatchAsync(HttpMethod.Post, PrefixChange(change.Id, "approve"), body: null, PayersApi.ETag(change.Version));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());

        // Now BIL, which a bill carries, can never be a credit note's; CN is the credit notes' default; INV is the bills'.
        foreach (var (type, prefix) in new[] { (DocumentType.CreditNote, "BIL"), (DocumentType.Payment, "CN"), (DocumentType.Refund, "INV") })
        {
            using var taken = await client.PostJsonAsync(PrefixChanges, new RequestPrefixChangeRequest { DocumentType = type, Prefix = prefix });
            await taken.ShouldBeProblemAsync(HttpStatusCode.Conflict, IdentityErrors.PrefixInUseCode);
        }

        using var unchanged = await client.PostJsonAsync(PrefixChanges, new RequestPrefixChangeRequest { DocumentType = DocumentType.Bill, Prefix = "INV" });
        await unchanged.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.PrefixUnchangedCode);

        // Its own former prefix may come back to a type; a minute later, so that the list has an order.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        (await RequestedAsync(client, DocumentType.Bill, "BIL")).FromPrefix.ShouldBe("INV");
        using var list = await supervisor.GetAsync(PrefixChanges.WithQuery("documentType=Bill"));
        (await list.ReadAsAsync<PageOf<PrefixChangeDto>>()).Items.Select(item => (item.ToPrefix, item.Status)).ShouldBe([("BIL", PrefixChangeStatus.Pending), ("INV", PrefixChangeStatus.Approved)]);
    }

    [Fact]
    public async Task ADR0033_an_approval_in_the_instant_of_the_first_number_of_the_year_waits_for_it_and_then_changes_the_series_it_created()
    {
        ExpectDrift(DriftIdentity.NumberSeriesGap);

        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");
        using var supervisor = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "Supervisor");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var change = await RequestedAsync(client, DocumentType.Bill, "INV");
        var tenantId = admin.TenantId!.Value.Value;

        // The first bill number of the year, stopped where built note point 5 found the gap: the series' place in the
        // index is locked and the tenant's default read, as the allocator does (docs/database.md section 4.1); the row
        // is not written yet.
        await using var numbering = new SqlConnection(SqlServer.ApiConnectionString);
        await numbering.OpenAsync();
        await using var transaction = (SqlTransaction)await numbering.BeginTransactionAsync();
        await SeriesSqlAsync(numbering, transaction, "SELECT COUNT(*) FROM [dbo].[NumberSeries] WITH (UPDLOCK, HOLDLOCK) WHERE [TenantId] = @tenant AND [DocumentType] = 'Bill' AND [FiscalYear] = @fiscalYear", tenantId);
        var defaultRead = (string?)await SeriesSqlAsync(numbering, transaction, "SELECT [BillPrefix] FROM [dbo].[Tenants] WHERE [Id] = @tenant", tenantId);
        defaultRead.ShouldBe("BIL");

        // The checker approves in that instant: the approval must wait for the number being taken.
        var approval = Task.Run(() => ApprovedAsync(supervisor, change));
        var early = await Task.WhenAny(approval, Task.Delay(TimeSpan.FromMilliseconds(750)));
        early.ShouldNotBeSameAs(approval, "the approval must hold the series, and so wait for the number being taken");

        // The number goes on as the allocator does: the series created from the default it read, number 1 taken.
        await SeriesSqlAsync(
            numbering,
            transaction,
            "INSERT [dbo].[NumberSeries] ([Id], [TenantId], [DocumentType], [FiscalYear], [Prefix], [NextNumber], [CreatedAtUtc], [UpdatedAtUtc]) VALUES (NEWID(), @tenant, 'Bill', @fiscalYear, @prefix, 2, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())",
            tenantId,
            defaultRead);
        await transaction.CommitAsync();

        // The approval then finds the series and gives it the new prefix: no series is left behind with the old one.
        var (status, body) = await approval.WaitAsync(Patience);
        status.ShouldBe(HttpStatusCode.OK, body);
        (await SettingsAsync(client)).Settings.Prefixes.Bill.ShouldBe("INV");
        await using (var context = ApiContext(admin))
        {
            (await context.Set<NumberSeries>().AsNoTracking().SingleAsync()).Prefix.ShouldBe("INV");
        }

        (await PostedBillAsync(client, patient.Id, payer.Id)).Number.ShouldBe("INV/26-27/000002");
    }

    [Fact]
    public async Task ADR0033_a_first_bill_of_the_year_and_an_approval_at_the_same_instant_both_succeed_and_leave_the_series_with_the_new_prefix()
    {
        using var factory = new DatabaseApiFactory(SqlServer);

        for (var round = 0; round < 10; round++)
        {
            var admin = await NewApiTenantAsync();
            using var client = factory.CreateClientFor(admin, "TenantAdmin");
            using var supervisor = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "Supervisor");
            var (patient, payer) = await client.NewBillPartiesAsync();
            var draft = await client.CreateBillAsync(new CreateBillRequest { PatientId = patient.Id, PayerId = payer.Id, BillDate = Today, DueDate = Today, Lines = [Dressing] });
            var change = await RequestedAsync(client, DocumentType.Bill, "INV");

            // No series of the year exists: the first number and the approval race for it.
            using var start = new SemaphoreSlim(0, 2);
            var posting = Task.Run(async () =>
            {
                await start.WaitAsync();
                using var response = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));

                return response.IsSuccessStatusCode
                    ? (response.StatusCode, (await response.ReadAsAsync<BillDto>()).Number!)
                    : (response.StatusCode, await response.Content.ReadAsStringAsync());
            });
            var approval = Task.Run(async () =>
            {
                await start.WaitAsync();

                return await ApprovedAsync(supervisor, change);
            });
            start.Release(2);

            var (postStatus, number) = await posting.WaitAsync(Patience);
            var (approveStatus, approveBody) = await approval.WaitAsync(Patience);

            postStatus.ShouldBe(HttpStatusCode.OK, number);
            approveStatus.ShouldBe(HttpStatusCode.OK, approveBody);
            number.ShouldBeOneOf("BIL/26-27/000001", "INV/26-27/000001");
            await using var context = ApiContext(admin);
            (await context.Set<NumberSeries>().AsNoTracking().SingleAsync()).Prefix.ShouldBe("INV", $"round {round}: the series of the year must carry the approved prefix");
            (await context.Set<Tenant>().AsNoTracking().SingleAsync()).BillPrefix.ShouldBe("INV");
        }
    }

    [Fact]
    public async Task BR002_a_prefix_change_of_another_tenant_is_answered_as_one_nobody_has()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var tenantA = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");
        using var tenantB = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");
        var change = await RequestedAsync(tenantA, DocumentType.Bill, "INV");

        using var read = await tenantB.GetAsync(PrefixChange(change.Id));
        using var decided = await tenantB.SendWithIfMatchAsync(HttpMethod.Post, PrefixChange(change.Id, "approve"), body: null, PayersApi.ETag(change.Version));
        using var list = await tenantB.GetAsync(PrefixChanges);

        await read.ShouldBeProblemAsync(HttpStatusCode.NotFound, IdentityErrors.PrefixChangeNotFoundCode);
        await decided.ShouldBeProblemAsync(HttpStatusCode.NotFound, IdentityErrors.PrefixChangeNotFoundCode);
        (await list.ReadAsAsync<PageOf<PrefixChangeDto>>()).Items.ShouldBeEmpty();
        (await SettingsAsync(tenantB)).Settings.Prefixes.Bill.ShouldBe("BIL");
    }

    private static async Task<(TenantSettingsDto Settings, string ETag)> SettingsAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Settings);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.ReadAsAsync<TenantSettingsDto>(), response.Headers.ETag.ShouldNotBeNull().Tag);
    }

    private static async Task<PrefixChangeDto> RequestedAsync(HttpClient client, DocumentType type, string prefix)
    {
        using var response = await client.PostJsonAsync(PrefixChanges, new RequestPrefixChangeRequest { DocumentType = type, Prefix = prefix });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        // The body can be read once.
        var change = await response.ReadAsAsync<PrefixChangeDto>();
        response.Headers.Location.ShouldNotBeNull().ToString().ShouldEndWith($"/api/v1/settings/prefix-changes/{change.Id}");

        return change;
    }

    private static async Task<BillDto> PostedBillAsync(HttpClient client, Guid patientId, Guid payerId)
    {
        var draft = await client.CreateBillAsync(new CreateBillRequest { PatientId = patientId, PayerId = payerId, BillDate = Today, DueDate = Today, Lines = [Dressing] });
        using var response = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<BillDto>();
    }

    // An approval of a change at the version it was asked for at; the answer's status and body, read once.
    private static async Task<(HttpStatusCode Status, string Body)> ApprovedAsync(HttpClient checker, PrefixChangeDto change)
    {
        using var response = await checker.SendWithIfMatchAsync(HttpMethod.Post, PrefixChange(change.Id, "approve"), body: null, PayersApi.ETag(change.Version));

        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // One step of a number being taken, sent by the test on its own connection inside its own transaction.
    private static async Task<object?> SeriesSqlAsync(SqlConnection connection, SqlTransaction transaction, string sql, Guid tenantId, string? prefix = null)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@tenant", tenantId));
        command.Parameters.Add(new SqlParameter("@fiscalYear", System.Data.SqlDbType.Char, FiscalYear.LabelLength) { Value = FiscalYear.FromDate(Today).Label });
        command.Parameters.Add(new SqlParameter("@prefix", System.Data.SqlDbType.VarChar, 3) { Value = (object?)prefix ?? DBNull.Value });

        return await command.ExecuteScalarAsync();
    }

    private static async Task<BillDto> BillAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(BillingApi.Bill(id));

        return await response.ReadAsAsync<BillDto>();
    }

    private static async Task<IReadOnlyList<AuditRecordDto>> TrailAsync(HttpClient client, string entityType, Guid id)
    {
        using var response = await client.GetAsync(Trail(entityType, id));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.ReadAsAsync<PageOf<AuditRecordDto>>()).Items;
    }

    private async Task<Guid> SeriesIdAsync(FixedIdentity tenant, DocumentType type)
    {
        await using var context = ApiContext(tenant);

        return (await context.Set<NumberSeries>().AsNoTracking().SingleAsync(series => series.DocumentType == type)).Id.Value;
    }

    private static Uri Trail(string entityType, Guid id) => new($"/api/v1/audit-records?entityType={entityType}&entityId={id}", UriKind.Relative);

    private static Uri PrefixChange(Guid id) => new($"/api/v1/settings/prefix-changes/{id}", UriKind.Relative);

    private static Uri PrefixChange(Guid id, string action) => new($"/api/v1/settings/prefix-changes/{id}/{action}", UriKind.Relative);
}
