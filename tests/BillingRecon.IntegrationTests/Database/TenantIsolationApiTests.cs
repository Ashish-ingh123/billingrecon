using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BillingRecon.Api.Adjustments;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Identity;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Adjustments;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Payments;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Requests never cross tenants (US-04; BR-002, BR-602; ADR 0031), tested as a defect would be, now that the tenant of
/// a request comes from its token alone: with a valid token of tenant B, no record of tenant A can be read by its id,
/// found in a list, or changed with its ETag; each answers as an id nobody has would. The patients, payers, service
/// items and bills have their own tests of this; here are the users and every financial document.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class TenantIsolationApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    // 17 October 2026 in India: fiscal year 2026-27.
    private static readonly DateOnly Today = new(2026, 10, 17);

    // 2 x 60.00 at 5%: 126.00.
    private static readonly BillLineRequest Dressing = BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m);

    [Fact]
    public async Task US04_a_token_of_tenant_B_reads_none_of_tenant_As_records_by_id()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (a, records) = await TenantWithRecordsAsync(factory);
        using var tenantA = factory.CreateClientFor(a, "TenantAdmin");
        using var tenantB = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");

        foreach (var (resource, id) in records)
        {
            (await tenantA.GetAsync(Path(resource, id))).StatusCode.ShouldBe(HttpStatusCode.OK, $"tenant A reads its own {resource}");

            using var theirs = await tenantB.GetAsync(Path(resource, id));
            using var nobodys = await tenantB.GetAsync(Path(resource, Guid.NewGuid()));

            theirs.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"tenant B read tenant A's {resource}");
            (await ErrorCodeAsync(theirs)).ShouldBe(await ErrorCodeAsync(nobodys), $"tenant A's {resource} must look like one nobody has");
        }

        // The allocations of a payment are read through the payment.
        var payment = records.Single(record => record.Resource == "payments").Id;
        (await tenantB.GetAsync(new Uri($"/api/v1/payments/{payment}/allocations", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task US04_a_token_of_tenant_B_finds_none_of_tenant_As_records_in_a_list_or_in_the_audit_trail()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (a, records) = await TenantWithRecordsAsync(factory);
        using var tenantA = factory.CreateClientFor(a, "TenantAdmin");
        using var tenantB = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");

        foreach (var (resource, id) in records)
        {
            (await IdsAsync(tenantA, resource)).ShouldContain(id, $"tenant A lists its own {resource}");
            (await IdsAsync(tenantB, resource)).ShouldNotContain(id, $"tenant B listed tenant A's {resource}");
        }

        var user = records.Single(record => record.Resource == "users").Id;
        (await IdsAsync(tenantA, $"audit-records?entityType=User&entityId={user}")).ShouldNotBeEmpty();
        (await IdsAsync(tenantB, $"audit-records?entityType=User&entityId={user}")).ShouldBeEmpty();
    }

    [Fact]
    public async Task US04_a_token_of_tenant_B_cannot_change_tenant_As_user_even_with_its_etag()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var a = await NewApiTenantAsync();
        using var tenantA = factory.CreateClientFor(a, "TenantAdmin");
        using var tenantB = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");
        var user = await UserAsync(tenantA);
        var etag = PayersApi.ETag(user.Version);

        using var update = await tenantB.SendWithIfMatchAsync(HttpMethod.Patch, Path("users", user.Id), new UpdateUserRequest { Email = "someone@else.in", DisplayName = "Someone" }, etag);
        using var roles = await tenantB.SendWithIfMatchAsync(HttpMethod.Put, new Uri($"/api/v1/users/{user.Id}/roles", UriKind.Relative), new ReplaceUserRolesRequest { Roles = [Role.TenantAdmin] }, etag);
        using var disable = await tenantB.SendWithIfMatchAsync(HttpMethod.Post, new Uri($"/api/v1/users/{user.Id}/disable", UriKind.Relative), body: null, etag);
        using var password = await tenantB.SendWithIfMatchAsync(HttpMethod.Post, new Uri($"/api/v1/users/{user.Id}/password", UriKind.Relative), new ResetUserPasswordRequest { Password = "a password of tenant B" }, etag);

        foreach (var response in new[] { update, roles, disable, password })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{response.RequestMessage!.Method} {response.RequestMessage.RequestUri}");
        }

        using var unchanged = await tenantA.GetAsync(Path("users", user.Id));
        var after = await unchanged.ReadAsAsync<UserDto>();
        (after.Email, after.Status, after.Version).ShouldBe((user.Email, user.Status, user.Version));
        after.Roles.ShouldBe(user.Roles);
    }

    // A tenant with one record of each kind: a user, a payment, a refund of it, and a posted bill with a credit note
    // and an adjustment on it, the documents as drafts. Returns the tenant's administrator and the records by resource.
    private async Task<(FixedIdentity Admin, (string Resource, Guid Id)[] Records)> TenantWithRecordsAsync(DatabaseApiFactory factory)
    {
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");

        var user = await UserAsync(client);
        var (patient, payer) = await client.NewBillPartiesAsync();
        var item = await client.CreateServiceItemAsync();
        var bill = await PostedBillAsync(client, patient, payer);
        var creditNote = await CreatedAsync<CreditNoteDto>(client, "credit-notes", new CreateCreditNoteRequest
        {
            BillId = bill.Id,
            CreditNoteDate = Today,
            Reason = "Dressing not done",
            Lines = [new CreditNoteLineRequest { BillLineId = bill.Lines.Single().Id, Quantity = 1m }],
        });
        var adjustment = await CreatedAsync<AdjustmentDto>(client, "adjustments", new CreateAdjustmentRequest { BillId = bill.Id, Kind = AdjustmentKind.WriteOff, Amount = 6m, Reason = "Rounding" });
        var payment = await CreatedAsync<PaymentDto>(client, "payments", new RecordPaymentRequest { PayerId = payer.Id, PaymentDate = Today, Method = PaymentMethod.Cheque, Reference = "000123", Amount = 500m });
        await client.EnsureShiftAsync();
        var refund = await CreatedAsync<RefundDto>(client, "refunds", new CreateRefundRequest { PaymentId = payment.Id, Amount = 100m, Method = PaymentMethod.Cash, Reason = "Paid twice" });

        return (admin,
        [
            ("users", user.Id),
            ("patients", patient.Id),
            ("payers", payer.Id),
            ("service-items", item.Id),
            ("bills", bill.Id),
            ("credit-notes", creditNote.Id),
            ("adjustments", adjustment.Id),
            ("payments", payment.Id),
            ("refunds", refund.Id),
        ]);
    }

    private static async Task<UserDto> UserAsync(HttpClient client)
        => await CreatedAsync<UserDto>(client, "users", new CreateUserRequest { Email = $"{Guid.NewGuid():N}@users.test", DisplayName = "Asha Rao", Password = "correct horse battery staple", Roles = [Role.Cashier] });

    private static async Task<T> CreatedAsync<T>(HttpClient client, string resource, object request)
        where T : class
    {
        using var response = await client.PostJsonAsync(new Uri($"/api/v1/{resource}", UriKind.Relative), request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<T>();
    }

    private static async Task<BillDto> PostedBillAsync(HttpClient client, Application.Payers.PatientDto patient, Application.Payers.PayerDto payer)
    {
        var draft = await client.CreateBillAsync(new CreateBillRequest { PatientId = patient.Id, PayerId = payer.Id, BillDate = Today, DueDate = Today.AddDays(15), Lines = [Dressing] });
        using var response = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<BillDto>();
    }

    private static Uri Path(string resource, Guid id) => new($"/api/v1/{resource}/{id}", UriKind.Relative);

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString();

    // The ids of the first page of a list, whatever the kind of record.
    private static async Task<IReadOnlyList<Guid>> IdsAsync(HttpClient client, string resource)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/{resource}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();

        return [.. page.GetProperty("items").EnumerateArray().Select(item => item.TryGetProperty("entityId", out var entity) ? entity.GetGuid() : item.GetProperty("id").GetGuid())];
    }
}
