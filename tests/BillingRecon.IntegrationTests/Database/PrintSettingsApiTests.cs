using System.Net;
using BillingRecon.Api.Identity;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Payments;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The print settings, end to end on the pipeline that ships and a real SQL Server (Prompt 10.4c, ADR 0059): three
/// answers with the defaults until a kind is saved; a first save and a change by the administrator under a version,
/// audited with what it was and is; what a bill may never switch off refused by the API and by the table; another
/// hospital reading and changing nothing of this one; and who recorded a payment and a refund, by name.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PrintSettingsApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly Uri Print = new("/api/v1/settings/print", UriKind.Relative);

    private static readonly UpdatePrintSettingsRequest Defaults = new()
    {
        Paper = PaperSize.A4,
        MarginTopMm = 14,
        MarginRightMm = 12,
        MarginBottomMm = 14,
        MarginLeftMm = 12,
        LogoPosition = LogoPosition.Left,
        LogoSize = LogoSize.Medium,
        PrePrintedStationery = false,
        StationeryTopMm = 0,
        ShowPatientContact = false,
        ShowPayer = true,
        ShowSacColumn = true,
        ShowTaxSplit = true,
        ShowAmountInWords = true,
        ShowSignatureBlock = true,
        ShowCashierName = false,
    };

    private static Uri Of(string documentType) => new($"/api/v1/settings/print/{documentType}", UriKind.Relative);

    [Fact]
    public async Task ADR0059_the_administrator_sets_how_a_receipt_is_printed_and_anyone_signed_in_reads_it()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        using var cashier = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(Guid.NewGuid())), "Cashier");

        // Nothing saved: three answers, the defaults, each with the version of none.
        var before = await ReadAsync(cashier);
        before.Select(answer => answer.DocumentType).ShouldBe([PrintedDocumentType.Bill, PrintedDocumentType.Payment, PrintedDocumentType.Refund]);
        before.ShouldAllBe(answer => answer.Version == PrintSettingsMapping.NoVersion);
        before[1].ShouldBe(PrintSettingsMapping.DefaultDto(PrintedDocumentType.Payment));

        var roll = Defaults with { Paper = PaperSize.Roll80, MarginLeftMm = 5, MarginRightMm = 5, ShowCashierName = true, FooterLine = "  Thank you  ", Terms = "Refunds within 7 days.\nKeep this receipt." };

        using (var noVersion = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Of("Payment"), roll, ifMatch: null))
        {
            noVersion.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        }

        using (var notTheirs = await cashier.SendWithIfMatchAsync(HttpMethod.Put, Of("Payment"), roll, PayersApi.ETag(PrintSettingsMapping.NoVersion)))
        {
            await notTheirs.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        using (var unknown = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Of("CreditNote"), roll, PayersApi.ETag(PrintSettingsMapping.NoVersion)))
        {
            unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "only a bill, a receipt and a refund voucher are printed");
        }

        using var saved = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Of("Payment"), roll, PayersApi.ETag(PrintSettingsMapping.NoVersion));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var first = await saved.ReadAsAsync<PrintSettingsDto>();
        first.Version.ShouldNotBe(PrintSettingsMapping.NoVersion);
        saved.Headers.ETag!.Tag.ShouldBe(PayersApi.ETag(first.Version));
        (first.DocumentType, first.Paper, first.MarginLeftMm, first.ShowCashierName, first.FooterLine, first.Terms)
            .ShouldBe((PrintedDocumentType.Payment, PaperSize.Roll80, 5, true, "Thank you", "Refunds within 7 days.\nKeep this receipt."));

        // The version of none no longer opens it; the version it answered does.
        using (var stale = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Of("Payment"), roll with { Paper = PaperSize.A5 }, PayersApi.ETag(PrintSettingsMapping.NoVersion)))
        {
            await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");
        }

        using var changed = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Of("Payment"), roll with { Paper = PaperSize.A5 }, PayersApi.ETag(first.Version));
        var second = await changed.ReadAsAsync<PrintSettingsDto>();
        (second.Paper, second.Version == first.Version).ShouldBe((PaperSize.A5, false));

        // The cashier reads what was set; the bill and the voucher still answer their defaults.
        var after = await ReadAsync(cashier);
        after[1].ShouldBe(second);
        after[0].ShouldBe(before[0]);
        after[2].ShouldBe(before[2]);

        // Two changes, each audited with what the settings were and are.
        await using var context = ApiContext(admin);
        var row = await context.Set<PrintSettings>().AsNoTracking().SingleAsync();
        using var trail = await administrator.GetAsync(new Uri($"/api/v1/audit-records?entityType=PrintSettings&entityId={row.Id.Value}", UriKind.Relative));
        var records = (await trail.ReadAsAsync<PageOf<AuditRecordDto>>()).Items.OrderBy(record => record.Id).ToList();
        records.Select(record => record.Action).ShouldBe(["Changed", "Changed"]);
        records[0].Before.ShouldBeNull("the first save made the row");
        records[0].After.GetProperty("Paper").GetString().ShouldBe("Roll80");
        records[1].Before.ShouldNotBeNull().GetProperty("Paper").GetString().ShouldBe("Roll80");
        records[1].After.GetProperty("Paper").GetString().ShouldBe("A5");
    }

    [Fact]
    public async Task ADR0059_a_bill_cannot_switch_off_its_sac_column_or_its_tax_split_nor_be_printed_on_a_roll()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var administrator = factory.CreateClientFor(admin, "TenantAdmin");
        var none = PayersApi.ETag(PrintSettingsMapping.NoVersion);

        var refusals = new (UpdatePrintSettingsRequest Request, string Field, string Reason)[]
        {
            (Defaults with { ShowSacColumn = false }, "showSacColumn", "cannot be switched off for a bill"),
            (Defaults with { ShowTaxSplit = false }, "showTaxSplit", "cannot be switched off for a bill"),
            (Defaults with { Paper = PaperSize.Roll80 }, "paper", "A bill is printed on A4 or A5"),
            (Defaults with { MarginBottomMm = 26 }, "marginBottomMm", "between 5 and 25 mm"),
            (Defaults with { PrePrintedStationery = true, StationeryTopMm = 81 }, "stationeryTopMm", "between 0 and 80 mm"),
        };

        foreach (var (request, field, reason) in refusals)
        {
            using var refused = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Of("Bill"), request, none);

            var problem = await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
            problem.GetProperty("errors").EnumerateObject().Select(member => member.Name).ShouldBe([field], reason);
            problem.GetProperty("errors").GetProperty(field)[0].GetString().ShouldNotBeNull().ShouldContain(reason);
        }

        await using (var context = ApiContext(admin))
        {
            (await context.Set<PrintSettings>().CountAsync()).ShouldBe(0);
        }

        // Every other switch of a bill is the hospital's to turn, and the same two mean nothing on a voucher.
        using var bill = await administrator.SendWithIfMatchAsync(
            HttpMethod.Put, Of("Bill"), Defaults with { Paper = PaperSize.A5, ShowPayer = false, ShowAmountInWords = false, ShowSignatureBlock = false, ShowPatientContact = true, LogoPosition = LogoPosition.Centre, LogoSize = LogoSize.Large }, none);
        bill.StatusCode.ShouldBe(HttpStatusCode.OK, await bill.Content.ReadAsStringAsync());
        using var voucher = await administrator.SendWithIfMatchAsync(HttpMethod.Put, Of("Refund"), Defaults with { Paper = PaperSize.Roll80, ShowSacColumn = false, ShowTaxSplit = false }, none);
        voucher.StatusCode.ShouldBe(HttpStatusCode.OK, await voucher.Content.ReadAsStringAsync());

        // The table holds the same two rules for whatever writes to it.
        await using var direct = AdministratorApiContext(admin);
        var rowsChanged = async (string sql) => await Should.ThrowAsync<Exception>(() => direct.Database.ExecuteSqlRawAsync(sql));
        (await rowsChanged("UPDATE [PrintSettings] SET [ShowTaxSplit] = 0 WHERE [DocumentType] = 'Bill'")).ToString().ShouldContain("CK_PrintSettings_BillShowsTax");
        (await rowsChanged("UPDATE [PrintSettings] SET [Paper] = 'Roll80' WHERE [DocumentType] = 'Bill'")).ToString().ShouldContain("CK_PrintSettings_BillPaper");
    }

    [Fact]
    public async Task ADR0059_another_hospital_never_reads_or_changes_the_print_settings_of_this_one()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var ours = await NewApiTenantAsync();
        var theirs = await NewApiTenantAsync();
        using var ourAdministrator = factory.CreateClientFor(ours, "TenantAdmin");
        using var theirAdministrator = factory.CreateClientFor(theirs, "TenantAdmin");

        using var saved = await ourAdministrator.SendWithIfMatchAsync(
            HttpMethod.Put, Of("Bill"), Defaults with { Paper = PaperSize.A5, FooterLine = "Ours alone" }, PayersApi.ETag(PrintSettingsMapping.NoVersion));
        var ourBill = await saved.ReadAsAsync<PrintSettingsDto>();

        // They read their own defaults, and our version opens nothing of theirs.
        (await ReadAsync(theirAdministrator)).ShouldAllBe(answer => answer.Version == PrintSettingsMapping.NoVersion && answer.Paper == PaperSize.A4 && answer.FooterLine == null);
        using (var crossed = await theirAdministrator.SendWithIfMatchAsync(HttpMethod.Put, Of("Bill"), Defaults with { FooterLine = "Theirs" }, PayersApi.ETag(ourBill.Version)))
        {
            await crossed.ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");
        }

        // Their own first save is their own row, and ours is as it was.
        using var theirSave = await theirAdministrator.SendWithIfMatchAsync(HttpMethod.Put, Of("Bill"), Defaults with { FooterLine = "Theirs" }, PayersApi.ETag(PrintSettingsMapping.NoVersion));
        theirSave.StatusCode.ShouldBe(HttpStatusCode.OK, await theirSave.Content.ReadAsStringAsync());
        (await ReadAsync(ourAdministrator))[0].ShouldBe(ourBill);

        await using var ourContext = ApiContext(ours);
        (await ourContext.Set<PrintSettings>().AsNoTracking().SingleAsync()).FooterLine.ShouldBe("Ours alone");
    }

    [Fact]
    public async Task ADR0059_who_recorded_a_payment_and_a_refund_is_read_by_name_and_never_of_another_hospital()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var maker = await NewApiTenantAsync();
        var stranger = await NewApiTenantAsync();
        using var cashier = factory.CreateClientFor(maker, "Cashier", "BillingClerk");
        using var theirCashier = factory.CreateClientFor(stranger, "Cashier", "BillingClerk");
        var (_, payer) = await cashier.NewBillPartiesAsync();

        using var recorded = await cashier.PostJsonAsync(new Uri("/api/v1/payments", UriKind.Relative), new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.Cash, Amount = 1000m });
        recorded.StatusCode.ShouldBe(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync());
        var payment = await recorded.ReadAsAsync<PaymentDto>();

        using var drafted = await cashier.PostJsonAsync(new Uri("/api/v1/refunds", UriKind.Relative), new CreateRefundRequest { PaymentId = payment.Id, Amount = 300m, Method = PaymentMethod.Cash, Reason = "Paid twice" });
        drafted.StatusCode.ShouldBe(HttpStatusCode.Created, await drafted.Content.ReadAsStringAsync());
        var refund = await drafted.ReadAsAsync<RefundDto>();

        string name;
        await using (var context = ApiContext(maker))
        {
            name = await context.Set<User>().Where(user => user.Id == maker.UserId!.Value).Select(user => user.DisplayName).SingleAsync();
        }

        name.ShouldNotBeNullOrWhiteSpace();
        var paymentRecorder = new Uri($"/api/v1/payments/{payment.Id}/recorder", UriKind.Relative);
        var refundRecorder = new Uri($"/api/v1/refunds/{refund.Id}/recorder", UriKind.Relative);

        using (var ofPayment = await cashier.GetAsync(paymentRecorder))
        {
            (await ofPayment.ReadAsAsync<DocumentRecorderDto>()).ShouldBe(new DocumentRecorderDto(name));
        }

        using (var ofRefund = await cashier.GetAsync(refundRecorder))
        {
            (await ofRefund.ReadAsAsync<DocumentRecorderDto>()).ShouldBe(new DocumentRecorderDto(name));
        }

        // Another hospital's cashier finds neither document, and so no name of this hospital's staff.
        using (var theirs = await theirCashier.GetAsync(paymentRecorder))
        {
            await theirs.ShouldBeProblemAsync(HttpStatusCode.NotFound, "payment_not_found");
        }

        using (var theirs = await theirCashier.GetAsync(refundRecorder))
        {
            await theirs.ShouldBeProblemAsync(HttpStatusCode.NotFound, "refund_not_found");
        }

        using (var missing = await cashier.GetAsync(new Uri($"/api/v1/payments/{Guid.NewGuid()}/recorder", UriKind.Relative)))
        {
            await missing.ShouldBeProblemAsync(HttpStatusCode.NotFound, "payment_not_found");
        }

        // The payment itself answers as it did, wherever it is read: no name is added to it.
        using var withAllocations = await cashier.GetAsync(new Uri($"/api/v1/payments/{payment.Id}/allocations", UriKind.Relative));
        (await withAllocations.ReadAsAsync<PaymentAllocationsDto>()).Payment.ShouldBe(payment);
    }

    private static async Task<IReadOnlyList<PrintSettingsDto>> ReadAsync(HttpClient client)
    {
        using var read = await client.GetAsync(Print);
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());

        return await read.ReadAsAsync<List<PrintSettingsDto>>();
    }
}
