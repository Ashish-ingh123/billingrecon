using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using BillingRecon.Api.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The permission matrix of docs/requirements.md section 1, walked over HTTP on the pipeline that ships (ADR 0032): every
/// endpoint of the API, with each of the five roles alone, gets 403 <c>forbidden</c> exactly where the matrix says no,
/// and anything but a refusal of authorisation where it says yes. The table below is this test's own copy of the matrix,
/// written from the requirements and ADR 0032, not the policies of the API, so a policy that admits the wrong role fails
/// it; and it must name every endpoint the API maps, so a new endpoint fails it until it has a row. Also the one rule the
/// matrix cannot express per endpoint: only a TenantAdmin reads the audit trail of a user.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed partial class PermissionMatrixApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private const Role TenantAdmin = Role.TenantAdmin;
    private const Role Supervisor = Role.Supervisor;
    private const Role BillingClerk = Role.BillingClerk;
    private const Role Cashier = Role.Cashier;
    private const Role Viewer = Role.Viewer;

    private static readonly Role[] Everyone = [TenantAdmin, Supervisor, BillingClerk, Cashier, Viewer];

    // The rows of the matrix, with the decisions of ADR 0032: a blank cell is no access, except that a Cashier reads
    // credit notes (point 3); applying credit is the credit notes' row (point 4); the inbox is the checkers' (point 5);
    // and, with ADR 0033, the checkers read the prefix changes they decide.
    private static readonly Dictionary<string, Role[]> Matrix = new(StringComparer.Ordinal)
    {
        ["users, settings, prefixes"] = [TenantAdmin],
        ["prefix changes: read"] = [TenantAdmin, Supervisor],
        ["reference data: change"] = [TenantAdmin, Supervisor, BillingClerk],
        ["reference data: read"] = Everyone,
        ["bills: change"] = [TenantAdmin, Supervisor, BillingClerk],
        ["bills: read"] = Everyone,
        ["credit notes and adjustments: change"] = [TenantAdmin, Supervisor, BillingClerk],
        ["credit notes: read"] = Everyone,
        ["adjustments: read"] = [TenantAdmin, Supervisor, BillingClerk, Viewer],
        ["payments: record, allocate"] = [TenantAdmin, Supervisor, BillingClerk, Cashier],
        ["payments: read"] = Everyone,
        ["refunds: change"] = [TenantAdmin, Supervisor, Cashier],
        ["refunds: read"] = [TenantAdmin, Supervisor, Cashier, Viewer],
        ["reversals: request"] = [TenantAdmin, Supervisor, BillingClerk, Cashier],
        ["approve or reject"] = [TenantAdmin, Supervisor],
        ["audit trail"] = Everyone,
        ["reports"] = Everyone,
        ["dashboard"] = [TenantAdmin, Supervisor, Viewer],
        ["shifts: open, close"] = [TenantAdmin, Supervisor, BillingClerk, Cashier],
        ["shifts: read"] = Everyone,
        ["day end"] = [TenantAdmin, Supervisor, Viewer],
        ["accounting export"] = [TenantAdmin, Supervisor, Viewer],
        ["signed in"] = Everyone,
    };

    // Every endpoint of the API but the sign-in, by method and route template, with its row of the matrix.
    private static readonly (string Method, string Route, string Row)[] Endpoints =
    [
        ("POST", "api/v1/users", "users, settings, prefixes"),
        ("GET", "api/v1/users", "users, settings, prefixes"),
        ("GET", "api/v1/users/{id:guid}", "users, settings, prefixes"),
        ("PATCH", "api/v1/users/{id:guid}", "users, settings, prefixes"),
        ("PUT", "api/v1/users/{id:guid}/roles", "users, settings, prefixes"),
        ("POST", "api/v1/users/{id:guid}/disable", "users, settings, prefixes"),
        ("POST", "api/v1/users/{id:guid}/enable", "users, settings, prefixes"),
        ("POST", "api/v1/users/{id:guid}/password", "users, settings, prefixes"),

        ("GET", "api/v1/settings", "users, settings, prefixes"),
        ("PATCH", "api/v1/settings", "users, settings, prefixes"),
        ("POST", "api/v1/settings/prefix-changes", "users, settings, prefixes"),
        ("GET", "api/v1/settings/prefix-changes", "prefix changes: read"),
        ("GET", "api/v1/settings/prefix-changes/{id:guid}", "prefix changes: read"),
        ("POST", "api/v1/settings/prefix-changes/{id:guid}/approve", "approve or reject"),
        ("POST", "api/v1/settings/prefix-changes/{id:guid}/reject", "approve or reject"),

        ("POST", "api/v1/patients", "reference data: change"),
        ("GET", "api/v1/patients", "reference data: read"),
        ("GET", "api/v1/patients/{id:guid}", "reference data: read"),
        ("PATCH", "api/v1/patients/{id:guid}", "reference data: change"),
        ("POST", "api/v1/patients/{id:guid}/deactivate", "reference data: change"),
        ("POST", "api/v1/patients/{id:guid}/reactivate", "reference data: change"),
        ("POST", "api/v1/payers", "reference data: change"),
        ("GET", "api/v1/payers", "reference data: read"),
        ("GET", "api/v1/payers/{id:guid}", "reference data: read"),
        ("PATCH", "api/v1/payers/{id:guid}", "reference data: change"),
        ("POST", "api/v1/payers/{id:guid}/deactivate", "reference data: change"),
        ("POST", "api/v1/payers/{id:guid}/reactivate", "reference data: change"),
        ("POST", "api/v1/service-items", "reference data: change"),
        ("GET", "api/v1/service-items", "reference data: read"),
        ("GET", "api/v1/service-items/{id:guid}", "reference data: read"),
        ("PATCH", "api/v1/service-items/{id:guid}", "reference data: change"),
        ("POST", "api/v1/service-items/{id:guid}/deactivate", "reference data: change"),
        ("POST", "api/v1/service-items/{id:guid}/reactivate", "reference data: change"),

        ("POST", "api/v1/bills", "bills: change"),
        ("GET", "api/v1/bills", "bills: read"),
        ("GET", "api/v1/bills/{id:guid}", "bills: read"),
        ("PATCH", "api/v1/bills/{id:guid}", "bills: change"),
        ("PUT", "api/v1/bills/{id:guid}/lines", "bills: change"),
        ("POST", "api/v1/bills/{id:guid}/void", "bills: change"),
        ("POST", "api/v1/bills/{id:guid}/post", "bills: change"),
        ("POST", "api/v1/bills/{id:guid}/approve", "approve or reject"),
        ("POST", "api/v1/bills/{id:guid}/reject", "approve or reject"),

        ("POST", "api/v1/credit-notes", "credit notes and adjustments: change"),
        ("GET", "api/v1/credit-notes", "credit notes: read"),
        ("GET", "api/v1/credit-notes/{id:guid}", "credit notes: read"),
        ("PATCH", "api/v1/credit-notes/{id:guid}", "credit notes and adjustments: change"),
        ("PUT", "api/v1/credit-notes/{id:guid}/lines", "credit notes and adjustments: change"),
        ("POST", "api/v1/credit-notes/{id:guid}/void", "credit notes and adjustments: change"),
        ("POST", "api/v1/credit-notes/{id:guid}/post", "credit notes and adjustments: change"),
        ("POST", "api/v1/credit-notes/{id:guid}/approve", "approve or reject"),
        ("POST", "api/v1/credit-notes/{id:guid}/reject", "approve or reject"),
        ("POST", "api/v1/credit-notes/{id:guid}/applications", "credit notes and adjustments: change"),
        ("POST", "api/v1/credit-notes/{id:guid}/applications/{applicationId:guid}/reverse", "reversals: request"),
        ("POST", "api/v1/credit-notes/{id:guid}/applications/{applicationId:guid}/approve-reversal", "approve or reject"),
        ("POST", "api/v1/credit-notes/{id:guid}/applications/{applicationId:guid}/reject-reversal", "approve or reject"),

        ("POST", "api/v1/adjustments", "credit notes and adjustments: change"),
        ("GET", "api/v1/adjustments", "adjustments: read"),
        ("GET", "api/v1/adjustments/{id:guid}", "adjustments: read"),
        ("PUT", "api/v1/adjustments/{id:guid}", "credit notes and adjustments: change"),
        ("POST", "api/v1/adjustments/{id:guid}/void", "credit notes and adjustments: change"),
        ("POST", "api/v1/adjustments/{id:guid}/post", "credit notes and adjustments: change"),
        ("POST", "api/v1/adjustments/{id:guid}/approve", "approve or reject"),
        ("POST", "api/v1/adjustments/{id:guid}/reject", "approve or reject"),

        ("POST", "api/v1/payments", "payments: record, allocate"),
        ("GET", "api/v1/payments", "payments: read"),
        ("GET", "api/v1/payments/{id:guid}", "payments: read"),
        ("POST", "api/v1/payments/{id:guid}/allocations", "payments: record, allocate"),
        ("GET", "api/v1/payments/{id:guid}/allocations", "payments: read"),
        ("POST", "api/v1/payments/{id:guid}/reverse", "reversals: request"),
        ("POST", "api/v1/payments/{id:guid}/approve-reversal", "approve or reject"),
        ("POST", "api/v1/payments/{id:guid}/reject-reversal", "approve or reject"),
        ("POST", "api/v1/payments/{id:guid}/allocations/{allocationId:guid}/reverse", "reversals: request"),
        ("POST", "api/v1/payments/{id:guid}/allocations/{allocationId:guid}/approve-reversal", "approve or reject"),
        ("POST", "api/v1/payments/{id:guid}/allocations/{allocationId:guid}/reject-reversal", "approve or reject"),

        ("POST", "api/v1/refunds", "refunds: change"),
        ("GET", "api/v1/refunds", "refunds: read"),
        ("GET", "api/v1/refunds/{id:guid}", "refunds: read"),
        ("PUT", "api/v1/refunds/{id:guid}", "refunds: change"),
        ("POST", "api/v1/refunds/{id:guid}/void", "refunds: change"),
        ("POST", "api/v1/refunds/{id:guid}/post", "refunds: change"),
        ("POST", "api/v1/refunds/{id:guid}/approve", "approve or reject"),
        ("POST", "api/v1/refunds/{id:guid}/reject", "approve or reject"),

        // Since Prompt 11.2 (ADR 0060): the shifts of the cash counter and the day-end report.
        ("POST", "api/v1/cashier-shifts", "shifts: open, close"),
        ("GET", "api/v1/cashier-shifts", "shifts: read"),
        ("GET", "api/v1/cashier-shifts/{id:guid}", "shifts: read"),
        ("POST", "api/v1/cashier-shifts/{id:guid}/close", "shifts: open, close"),
        ("POST", "api/v1/cashier-shifts/{id:guid}/approve", "approve or reject"),
        ("POST", "api/v1/cashier-shifts/{id:guid}/reject", "approve or reject"),
        ("GET", "api/v1/reports/day-end", "day end"),

        // Since Prompt 11.4 (ADR 0062): the accountant's export, and the names of the accountant's ledgers.
        ("GET", "api/v1/reports/accounting-export", "accounting export"),
        ("GET", "api/v1/reports/accounting-export/summary", "accounting export"),
        ("GET", "api/v1/settings/accounting-ledgers", "users, settings, prefixes"),
        ("PUT", "api/v1/settings/accounting-ledgers", "users, settings, prefixes"),

        ("GET", "api/v1/approval-requests", "approve or reject"),
        ("GET", "api/v1/audit-records", "audit trail"),
        ("GET", "api/v1/system/info", "signed in"),
        ("GET", "api/v1/system/business-date", "signed in"),
        ("POST", "api/v1/auth/password", "signed in"),

        // Since Prompt 11.3a (ADR 0061): the second step of a sign-in, and an administrator's reset of a user's.
        ("POST", "api/v1/auth/second-step", "signed in"),
        ("POST", "api/v1/auth/second-step/enrolment", "signed in"),
        ("POST", "api/v1/auth/second-step/enrolment/confirm", "signed in"),
        ("POST", "api/v1/users/{id:guid}/second-step/reset", "users, settings, prefixes"),

        ("GET", "api/v1/reports/bill-register", "reports"),
        ("GET", "api/v1/reports/payment-register", "reports"),
        ("GET", "api/v1/reports/daily-collection", "reports"),
        ("GET", "api/v1/reports/outstanding-by-payer", "reports"),
        ("GET", "api/v1/reports/payers/{payerId:guid}/balance", "reports"),
        ("GET", "api/v1/reports/unapplied-cash", "reports"),
        ("GET", "api/v1/reports/available-credit", "reports"),
        ("GET", "api/v1/reports/pending-approvals", "reports"),
        ("GET", "api/v1/reports/reconciliation", "reports"),

        ("GET", "api/v1/dashboard/summary", "dashboard"),
        ("GET", "api/v1/dashboard/trend", "dashboard"),
        ("GET", "api/v1/dashboard/ageing", "dashboard"),
        ("GET", "api/v1/dashboard/top-payers", "dashboard"),
        ("GET", "api/v1/dashboard/recent-payments", "dashboard"),
        ("GET", "api/v1/dashboard/pending-approvals", "dashboard"),
        ("GET", "api/v1/dashboard/drift", "dashboard"),

        ("POST", "api/v1/imports/parties", "users, settings, prefixes"),
        ("POST", "api/v1/opening-batches/dry-run", "users, settings, prefixes"),
        ("POST", "api/v1/opening-batches", "users, settings, prefixes"),
        ("GET", "api/v1/opening-batches", "approve or reject"),
        ("GET", "api/v1/opening-batches/{id:guid}", "approve or reject"),
        ("POST", "api/v1/opening-batches/{id:guid}/approve", "approve or reject"),
        ("POST", "api/v1/opening-batches/{id:guid}/reject", "approve or reject"),

        ("GET", "api/v1/settings/letterhead", "signed in"),
        ("PUT", "api/v1/settings/letterhead", "users, settings, prefixes"),
        ("GET", "api/v1/settings/letterhead/logo", "signed in"),
        ("GET", "api/v1/settings/letterhead/signature", "signed in"),
        ("PUT", "api/v1/settings/letterhead/logo", "users, settings, prefixes"),
        ("PUT", "api/v1/settings/letterhead/signature", "users, settings, prefixes"),
        ("POST", "api/v1/settings/letterhead/logo/remove", "users, settings, prefixes"),
        ("POST", "api/v1/settings/letterhead/signature/remove", "users, settings, prefixes"),
        ("GET", "api/v1/settings/print", "signed in"),
        ("GET", "api/v1/payments/{id:guid}/recorder", "payments: read"),
        ("GET", "api/v1/refunds/{id:guid}/recorder", "refunds: read"),
        ("PUT", "api/v1/settings/print/{documentType}", "users, settings, prefixes"),
        ("POST", "api/v1/bills/{id:guid}/prints", "bills: read"),
        ("POST", "api/v1/payments/{id:guid}/prints", "payments: read"),
        ("POST", "api/v1/refunds/{id:guid}/prints", "refunds: read"),

        // The last row on purpose: the walk below sends every row as every role, and a sign-out ends the sessions of the
        // user who sent it (ADR 0057), so nothing may follow it.
        ("POST", "api/v1/auth/logout", "signed in"),
    ];

    [Fact]
    public void The_table_names_every_endpoint_of_the_api_and_nothing_else()
    {
        using var factory = new DatabaseApiFactory(SqlServer);

        var mapped = ApiEndpoints(factory).Select(endpoint => $"{endpoint.Method} {endpoint.Route}").Order(StringComparer.Ordinal);
        var tabled = Endpoints.Select(endpoint => $"{endpoint.Method} {endpoint.Route}").Order(StringComparer.Ordinal);

        mapped.ShouldBe(tabled);
        Endpoints.Select(endpoint => endpoint.Row).Distinct().ShouldBe(Matrix.Keys, ignoreOrder: true);
    }

    [Fact]
    public async Task ADR0032_every_endpoint_answers_each_role_as_the_matrix_says()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        var clients = Everyone.ToDictionary(role => role, role => factory.CreateClientFor(new FixedIdentity(tenant.TenantId, new UserId(Guid.NewGuid())), role.ToString()));
        var wrong = new List<string>();

        try
        {
            foreach (var (method, route, row) in Endpoints)
            {
                foreach (var role in Everyone)
                {
                    using var response = await clients[role].SendAsync(Request(method, route));
                    var refused = response.StatusCode == HttpStatusCode.Forbidden && await ErrorCodeAsync(response) == "forbidden";
                    var allowed = Matrix[row].Contains(role);

                    if (allowed == refused || response.StatusCode == HttpStatusCode.Unauthorized || (int)response.StatusCode >= 500)
                    {
                        wrong.Add($"{method} {route} as {role}: {(int)response.StatusCode}, the matrix says {(allowed ? "yes" : "no")}");
                    }
                }
            }
        }
        finally
        {
            foreach (var client in clients.Values)
            {
                client.Dispose();
            }
        }

        wrong.ShouldBeEmpty();
    }

    [Fact]
    public async Task US43_every_role_reads_the_trail_of_a_patient_but_only_a_tenant_admin_reads_the_trail_of_a_user()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var administrator = factory.CreateClientFor(tenant, "TenantAdmin");
        var patient = await administrator.RegisterPatientAsync();
        using var created = await administrator.PostJsonAsync(new Uri("/api/v1/users", UriKind.Relative), new Api.Identity.CreateUserRequest { Email = "asha.rao@example.in", DisplayName = "Asha Rao", Password = "correct horse battery staple", Roles = [Role.Cashier] });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var user = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        foreach (var role in Everyone)
        {
            using var client = factory.CreateClientFor(new FixedIdentity(tenant.TenantId, new UserId(Guid.NewGuid())), role.ToString());

            using var patientTrail = await client.GetAsync(Trail("Patient", patient.Id));
            patientTrail.StatusCode.ShouldBe(HttpStatusCode.OK, $"{role} reads a patient's trail");

            foreach (var kind in new[] { "User", "user", " USER " })
            {
                using var userTrail = await client.GetAsync(Trail(kind, user));
                if (role == TenantAdmin)
                {
                    userTrail.StatusCode.ShouldBe(HttpStatusCode.OK);
                    (await userTrail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);
                }
                else
                {
                    await userTrail.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
                }
            }
        }
    }

    [Fact]
    public async Task US43_the_trail_of_a_record_is_read_by_who_may_read_the_record_and_by_no_one_else()
    {
        // Review 2026-11, finding R7: until 2026-11-26 every role read the trail, with its whole before and after
        // images, of any kind of record but four. Each line is a role, a kind, and whether that role reads that kind
        // of record by the permission matrix (docs/requirements.md, section 1). The ids are nobody's: who is let in
        // reads an empty trail, so the answer tells the permission and nothing else.
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        var expected = new (Role Role, string Kind, bool Reads)[]
        {
            // The three sequences of the finding.
            (Role.Cashier, "Adjustment", false),
            (Role.BillingClerk, "Refund", false),
            (Role.Viewer, "OpeningBatch", false),

            // What each of them still reads, because its role reads the record.
            (Role.Cashier, "Payment", true),
            (Role.Cashier, "Refund", true),
            (Role.Cashier, "Bill", true),
            (Role.Cashier, "CreditNote", true),
            (Role.BillingClerk, "Adjustment", true),
            (Role.BillingClerk, "Bill", true),
            (Role.Viewer, "Adjustment", true),
            (Role.Viewer, "Refund", true),
            (Role.Viewer, "Patient", true),

            // What a checker decides is read by who decides.
            (Role.Supervisor, "OpeningBatch", true),
            (Role.Supervisor, "ApprovalRequest", true),
            (Role.TenantAdmin, "ApprovalRequest", true),
            (Role.BillingClerk, "ApprovalRequest", false),
            (Role.Cashier, "ApprovalRequest", false),
            (Role.Viewer, "ApprovalRequest", false),
            (Role.BillingClerk, "OpeningBatch", false),

            // The settings are the administrator's, the print settings with them.
            (Role.TenantAdmin, "PrintSettings", true),
            (Role.Supervisor, "PrintSettings", false),
            (Role.Viewer, "PrintSettings", false),
            (Role.TenantAdmin, "AccountingLedgers", true),
            (Role.Supervisor, "AccountingLedgers", false),
            (Role.Supervisor, "Tenant", false),
            (Role.Supervisor, "NumberSeries", false),

            // Whatever the case and the spaces, as the database compares the kind.
            (Role.Cashier, " adjustment ", false),
            (Role.Viewer, "ADJUSTMENT", true),
        };

        var wrong = new List<string>();
        foreach (var (role, kind, reads) in expected)
        {
            using var client = factory.CreateClientFor(new FixedIdentity(tenant.TenantId, new UserId(Guid.NewGuid())), role.ToString());
            using var answer = await client.GetAsync(Trail(kind, Guid.NewGuid()));

            var want = reads ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            if (answer.StatusCode != want)
            {
                wrong.Add($"{role} asking for the trail of '{kind}': {(int)answer.StatusCode}, expected {(int)want}");
            }
            else if (!reads)
            {
                (await ErrorCodeAsync(answer)).ShouldBe("forbidden");
            }
        }

        wrong.ShouldBeEmpty();

        // A kind that is not audited has no reader, the administrator included; a request with no kind is malformed.
        foreach (var role in Everyone)
        {
            using var client = factory.CreateClientFor(new FixedIdentity(tenant.TenantId, new UserId(Guid.NewGuid())), role.ToString());
            using var unknown = await client.GetAsync(Trail("Widget", Guid.NewGuid()));
            unknown.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{role} asking for the trail of a kind nobody audits");
            using var noKind = await client.GetAsync(new Uri($"/api/v1/audit-records?entityId={Guid.NewGuid()}", UriKind.Relative));
            noKind.StatusCode.ShouldBe(HttpStatusCode.BadRequest, $"{role} asking for a trail without a kind");
        }
    }

    private static Uri Trail(string entityType, Guid id)
        => new($"/api/v1/audit-records?entityType={Uri.EscapeDataString(entityType)}&entityId={id}", UriKind.Relative);

    // The endpoints the API maps for its own controllers, but the anonymous sign-in.
    private static IEnumerable<(string Method, string Route)> ApiEndpoints(DatabaseApiFactory factory)
        => factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Assembly == typeof(ApiControllerBase).Assembly)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(method => (method, endpoint.RoutePattern.RawText!.TrimStart('/'))));

    // The request of a row: an id for every parameter, and an empty JSON body for a change. It is refused or answered
    // before anything is written: the body has no field, and no change carries an If-Match or an Idempotency-Key.
    private static HttpRequestMessage Request(string method, string route)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), new Uri("/" + RouteParameter().Replace(route, _ => Guid.NewGuid().ToString()), UriKind.Relative));
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = PayersApi.RawJson("{}");
        }

        return request;
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
        => response.Content.Headers.ContentLength is 0 ? null : (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString();

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();
}
