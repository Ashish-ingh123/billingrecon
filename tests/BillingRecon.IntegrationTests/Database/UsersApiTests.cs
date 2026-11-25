using System.Net;
using BillingRecon.Api.Identity;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Users end to end, on the pipeline that ships and a real SQL Server (US-01; BR-601, BR-701, BR-002; ADR 0030): a
/// tenant administrator creates users with roles, the address is unique in the tenant without regard to case, the
/// password is kept only as a slow salted hash that no answer and no audit image ever holds, and the tenant always
/// keeps an active administrator. Since this prompt every column that names a user is a foreign key to one.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class UsersApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private const string Password = "correct horse battery staple";

    private static readonly Uri Users = new("/api/v1/users", UriKind.Relative);

    [Fact]
    public async Task US01_a_tenant_administrator_creates_a_user_with_roles_and_reads_it_back_without_any_password()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");

        using var created = await client.PostJsonAsync(Users, new CreateUserRequest { Email = "  Asha.Rao@Example.IN ", DisplayName = " Asha Rao ", Password = Password, Roles = [Role.Cashier, Role.Supervisor] });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var body = await created.Content.ReadAsStringAsync();
        var user = await created.ReadAsAsync<UserDto>();
        created.Headers.Location.ShouldNotBeNull().ToString().ShouldEndWith($"/api/v1/users/{user.Id}");
        created.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(user.Version));
        (user.Email, user.DisplayName, user.Status, user.LastSignInAtUtc).ShouldBe(("Asha.Rao@Example.IN", "Asha Rao", UserStatus.Active, null));
        user.Roles.ShouldBe([Role.Supervisor, Role.Cashier]);
        body.ShouldNotContain("password", Case.Insensitive, "no answer carries the password or its hash");
        body.ShouldNotContain("pbkdf2", Case.Insensitive);

        // In the database: the hash, the address as the unique index compares it, and the roles granted by the administrator.
        await using (var context = ApiContext(admin))
        {
            var row = await context.Set<User>().Include(candidate => candidate.Roles).AsNoTracking().SingleAsync(candidate => candidate.Id == new UserId(user.Id));
            row.NormalizedEmail.ShouldBe("ASHA.RAO@EXAMPLE.IN");
            row.PasswordHash.ShouldStartWith("pbkdf2-sha256$600000$");
            new Pbkdf2PasswordHasher().Verify(Password, row.PasswordHash).ShouldBeTrue();
            row.Roles.ShouldAllBe(role => role.GrantedByUserId == admin.UserId && role.GrantedAtUtc == DatabaseApiFactory.InitialInstant);
            row.CreatedByUserId.ShouldBe(admin.UserId);
        }

        // Read and listed, by role and by a part of the address without regard to case (Prompt 10.2b).
        using (var read = await client.GetAsync(User(user.Id)))
        {
            (await read.ReadAsAsync<UserDto>()).ShouldBeEquivalentTo(user);
        }

        (await ListAsync(client, "role=Cashier")).ShouldHaveSingleItem().Id.ShouldBe(user.Id);
        (await ListAsync(client, "email=asha")).ShouldHaveSingleItem().Id.ShouldBe(user.Id);
        (await ListAsync(client, "email=RAO%40example")).ShouldHaveSingleItem().Id.ShouldBe(user.Id);
        (await ListAsync(client, "email=rao%40nowhere")).ShouldBeEmpty();
        (await ListAsync(client, "role=Viewer")).ShouldBeEmpty();

        // Audited (BR-701), and neither image holds the hash.
        var trail = await TrailAsync(client, user.Id);
        trail.Select(record => record.Action).ShouldBe(["Created"]);
        trail[0].After.GetRawText().ShouldNotContain("PasswordHash", Case.Sensitive);
        trail[0].After.GetRawText().ShouldContain("ASHA.RAO@EXAMPLE.IN", Case.Sensitive);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.AuditRecords WHERE [AfterJson] LIKE '%pbkdf2%' OR [BeforeJson] LIKE '%pbkdf2%'")).ShouldBe(0);
    }

    [Fact]
    public async Task BR601_an_address_is_unique_in_the_tenant_without_regard_to_case_and_free_in_another_tenant()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        var stranger = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");
        using var outsider = factory.CreateClientFor(stranger, "TenantAdmin");
        var asha = await CreatedAsync(client, "asha.rao@example.in", Role.Cashier);
        var ravi = await CreatedAsync(client, "ravi@example.in", Role.Viewer);

        using var again = await client.PostJsonAsync(Users, NewUser(" ASHA.RAO@example.IN ", Role.Viewer));
        using var elsewhere = await outsider.PostJsonAsync(Users, NewUser("asha.rao@example.in", Role.Viewer));
        using var moved = await client.SendWithIfMatchAsync(HttpMethod.Patch, User(ravi.Id), new UpdateUserRequest { Email = "Asha.Rao@Example.in", DisplayName = "Ravi" }, PayersApi.ETag(ravi.Version));
        using var renamed = await client.SendWithIfMatchAsync(HttpMethod.Patch, User(asha.Id), new UpdateUserRequest { Email = "ASHA.RAO@EXAMPLE.IN", DisplayName = "Dr Asha Rao" }, PayersApi.ETag(asha.Version));

        await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, IdentityErrors.UserEmailTakenCode);
        elsewhere.StatusCode.ShouldBe(HttpStatusCode.Created, "another tenant's users are another set (BR-601)");
        await moved.ShouldBeProblemAsync(HttpStatusCode.Conflict, IdentityErrors.UserEmailTakenCode);
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK, "a user keeps its own address in another case");
        (await renamed.ReadAsAsync<UserDto>()).Email.ShouldBe("ASHA.RAO@EXAMPLE.IN");

        // Two creations of one address at the same moment: one user, and the other request is told why.
        var racing = await Task.WhenAll(
            Task.Run(() => client.PostJsonAsync(Users, NewUser("meera@example.in", Role.Viewer))),
            Task.Run(() => client.PostJsonAsync(Users, NewUser("MEERA@example.in", Role.Viewer))));
        try
        {
            racing.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
            await racing.Single(response => response.StatusCode != HttpStatusCode.Created).ShouldBeProblemAsync(HttpStatusCode.Conflict, IdentityErrors.UserEmailTakenCode);
        }
        finally
        {
            foreach (var response in racing)
            {
                response.Dispose();
            }
        }

        (await ListAsync(client, "email=meera")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task ADR0030_roles_are_replaced_and_the_tenant_always_keeps_an_active_administrator()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");
        var second = await CreatedAsync(client, "second.admin@example.in", Role.TenantAdmin);

        // Two administrators: the second can stop being one.
        using var demoted = await client.SendWithIfMatchAsync(HttpMethod.Put, User(second.Id, "roles"), new ReplaceUserRolesRequest { Roles = [Role.Viewer, Role.Supervisor] }, PayersApi.ETag(second.Version));
        demoted.StatusCode.ShouldBe(HttpStatusCode.OK, await demoted.Content.ReadAsStringAsync());
        var viewer = await demoted.ReadAsAsync<UserDto>();
        viewer.Roles.ShouldBe([Role.Supervisor, Role.Viewer]);

        // Now the first is the last: it keeps the role, cannot disable itself, and cannot be disabled by anyone else.
        var self = await UserAsync(client, admin.UserId!.Value.Value);
        using var lastRole = await client.SendWithIfMatchAsync(HttpMethod.Put, User(self.Id, "roles"), new ReplaceUserRolesRequest { Roles = [Role.Supervisor] }, PayersApi.ETag(self.Version));
        using var ownDisable = await client.SendWithIfMatchAsync(HttpMethod.Post, User(self.Id, "disable"), body: null, PayersApi.ETag(self.Version));
        // The second user holds a token from before the change, which still says TenantAdmin. Until ADR 0057 the endpoint
        // let it in and the guard of the last administrator refused; now the change of roles ended that session, so the
        // token is refused before any guard. The guard itself is held by UserHandlerTests, with the ports replaced.
        using var asSecond = factory.CreateClientFor(new FixedIdentity(admin.TenantId, new UserId(second.Id)), "TenantAdmin");
        using var lastDisable = await asSecond.SendWithIfMatchAsync(HttpMethod.Post, User(self.Id, "disable"), body: null, PayersApi.ETag(self.Version));
        using var none = await client.SendWithIfMatchAsync(HttpMethod.Put, User(viewer.Id, "roles"), new ReplaceUserRolesRequest { Roles = [] }, PayersApi.ETag(viewer.Version));

        await lastRole.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.UserLastTenantAdminCode);
        await ownDisable.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.UserCannotDisableSelfCode);
        await lastDisable.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.SessionEndedCode);
        await none.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        (await UserAsync(client, self.Id)).ShouldBeEquivalentTo(self);

        (await TrailAsync(client, second.Id)).Select(record => record.Action).ShouldBe(["Created", "RolesReplaced"]);
        (await CountAsync($"SELECT COUNT(*) AS [Value] FROM dbo.UserRoles WHERE [UserId] = '{second.Id}'")).ShouldBe(2, "the role taken away is gone; the trail keeps it");
    }

    [Fact]
    public async Task BR601_a_user_is_disabled_and_enabled_under_the_version_it_was_read_with()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");
        var user = await CreatedAsync(client, "ravi@example.in", Role.Cashier);

        using var noVersion = await client.SendWithIfMatchAsync(HttpMethod.Post, User(user.Id, "disable"), body: null, ifMatch: null);
        using var disabled = await client.SendWithIfMatchAsync(HttpMethod.Post, User(user.Id, "disable"), body: null, PayersApi.ETag(user.Version));
        disabled.StatusCode.ShouldBe(HttpStatusCode.OK, await disabled.Content.ReadAsStringAsync());
        var off = await disabled.ReadAsAsync<UserDto>();
        using var stale = await client.SendWithIfMatchAsync(HttpMethod.Post, User(user.Id, "enable"), body: null, PayersApi.ETag(user.Version));
        using var twice = await client.SendWithIfMatchAsync(HttpMethod.Post, User(user.Id, "disable"), body: null, PayersApi.ETag(off.Version));
        using var enabled = await client.SendWithIfMatchAsync(HttpMethod.Post, User(user.Id, "enable"), body: null, PayersApi.ETag(off.Version));
        var on = await enabled.ReadAsAsync<UserDto>();
        using var already = await client.SendWithIfMatchAsync(HttpMethod.Post, User(user.Id, "enable"), body: null, PayersApi.ETag(on.Version));

        await noVersion.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        off.Status.ShouldBe(UserStatus.Disabled);
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
        await twice.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.UserAlreadyDisabledCode);
        on.Status.ShouldBe(UserStatus.Active);
        await already.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdentityErrors.UserAlreadyActiveCode);
        (await ListAsync(client, "status=Disabled")).ShouldBeEmpty();
        (await TrailAsync(client, user.Id)).Select(record => record.Action).ShouldBe(["Created", "Disabled", "Enabled"]);
    }

    [Fact]
    public async Task ADR0030_an_administrator_resets_a_password_and_only_its_new_hash_is_kept()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");
        var user = await CreatedAsync(client, "ravi@example.in", Role.Cashier);
        const string newPassword = "a different long passphrase";

        using var tooShort = await client.SendWithIfMatchAsync(HttpMethod.Post, User(user.Id, "password"), new ResetUserPasswordRequest { Password = "elevenchars" }, PayersApi.ETag(user.Version));
        using var reset = await client.SendWithIfMatchAsync(HttpMethod.Post, User(user.Id, "password"), new ResetUserPasswordRequest { Password = newPassword }, PayersApi.ETag(user.Version));

        await tooShort.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        reset.StatusCode.ShouldBe(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync());
        (await reset.Content.ReadAsStringAsync()).ShouldNotContain("password", Case.Insensitive);
        await using (var context = ApiContext(admin))
        {
            var hash = (await context.Set<User>().AsNoTracking().SingleAsync(candidate => candidate.Id == new UserId(user.Id))).PasswordHash;
            var hasher = new Pbkdf2PasswordHasher();
            hasher.Verify(newPassword, hash).ShouldBeTrue();
            hasher.Verify(Password, hash).ShouldBeFalse("the old password no longer matches");
        }

        var trail = await TrailAsync(client, user.Id);
        trail.Select(record => record.Action).ShouldBe(["Created", "PasswordReset"]);
        trail[1].Before.ShouldNotBeNull().GetRawText().ShouldNotContain("PasswordHash", Case.Sensitive);
        trail[1].After.GetRawText().ShouldNotContain("PasswordHash", Case.Sensitive);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.AuditRecords WHERE [AfterJson] LIKE '%pbkdf2%' OR [BeforeJson] LIKE '%pbkdf2%'")).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.IdempotencyKeys")).ShouldBe(0, "no fingerprint of a body with a password is stored");
    }

    [Fact]
    public async Task BR002_a_user_of_another_tenant_can_neither_be_read_nor_changed()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        var stranger = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");
        using var outsider = factory.CreateClientFor(stranger, "TenantAdmin");
        var user = await CreatedAsync(client, "ravi@example.in", Role.Cashier);

        using var read = await outsider.GetAsync(User(user.Id));
        using var change = await outsider.SendWithIfMatchAsync(HttpMethod.Patch, User(user.Id), new UpdateUserRequest { Email = "x@example.in", DisplayName = "x" }, PayersApi.ETag(user.Version));
        using var disable = await outsider.SendWithIfMatchAsync(HttpMethod.Post, User(user.Id, "disable"), body: null, PayersApi.ETag(user.Version));
        using var reset = await outsider.SendWithIfMatchAsync(HttpMethod.Post, User(user.Id, "password"), new ResetUserPasswordRequest { Password = Password }, PayersApi.ETag(user.Version));

        await read.ShouldBeProblemAsync(HttpStatusCode.NotFound, IdentityErrors.UserNotFoundCode);
        await change.ShouldBeProblemAsync(HttpStatusCode.NotFound, IdentityErrors.UserNotFoundCode);
        await disable.ShouldBeProblemAsync(HttpStatusCode.NotFound, IdentityErrors.UserNotFoundCode);
        await reset.ShouldBeProblemAsync(HttpStatusCode.NotFound, IdentityErrors.UserNotFoundCode);
        (await ListAsync(outsider, "email=ravi")).ShouldBeEmpty();
        (await UserAsync(client, user.Id)).ShouldBeEquivalentTo(user);
    }

    [Fact]
    public async Task ADR0030_a_column_that_names_a_user_names_one_that_exists()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");
        var (_, payer) = await client.NewBillPartiesAsync();
        await using var system = ApiContext(SystemIdentity.Instance);

        // SQL that goes around the application meets the keys too. 547: a foreign key.
        var adminId = admin.UserId!.Value.Value;
        var stamp = await Should.ThrowAsync<SqlException>(() => system.Database.ExecuteSqlAsync($"UPDATE dbo.Payers SET [UpdatedByUserId] = NEWID() WHERE [Id] = {payer.Id}"));
        var role = await Should.ThrowAsync<SqlException>(() => system.Database.ExecuteSqlAsync($"UPDATE dbo.UserRoles SET [RoleCode] = 'Auditor' WHERE [UserId] = {adminId}"));

        stamp.Number.ShouldBe(547);
        stamp.Message.ShouldContain("FK_Payers_UpdatedByUserId");
        role.Number.ShouldBe(547);
        (await CountAsync("SELECT COUNT(*) AS [Value] FROM dbo.Roles")).ShouldBe(5, "the reset of the tests leaves the roles alone");
    }

    [Fact]
    public async Task The_email_filter_finds_a_part_of_the_address_and_takes_the_wildcards_of_like_as_text()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "TenantAdmin");
        var underscore = await CreatedAsync(client, "a_b.wild@example.in", Role.Viewer);
        var letter = await CreatedAsync(client, "axb.wild@example.in", Role.Viewer);

        (await ListAsync(client, "email=b.WILD")).Select(user => user.Id).ShouldBe([underscore.Id, letter.Id]);
        (await ListAsync(client, "email=wild%40example.in")).Select(user => user.Id).ShouldBe([underscore.Id, letter.Id]);
        (await ListAsync(client, "email=a_b")).Select(user => user.Id).ShouldBe([underscore.Id]);
        (await ListAsync(client, "email=%25")).ShouldBeEmpty();
        (await ListAsync(client, "email=wild%25")).ShouldBeEmpty();
    }

    private static Uri User(Guid id) => new($"/api/v1/users/{id}", UriKind.Relative);

    private static Uri User(Guid id, string action) => new($"/api/v1/users/{id}/{action}", UriKind.Relative);

    private static CreateUserRequest NewUser(string email, params Role[] roles) => new() { Email = email, DisplayName = "Test user", Password = Password, Roles = roles };

    private static async Task<UserDto> CreatedAsync(HttpClient client, string email, params Role[] roles)
    {
        using var response = await client.PostJsonAsync(Users, NewUser(email, roles));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<UserDto>();
    }

    private static async Task<UserDto> UserAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(User(id));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<UserDto>();
    }

    private static async Task<IReadOnlyList<UserDto>> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(Users.WithQuery(query));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.ReadAsAsync<PageOf<UserDto>>()).Items;
    }

    private static async Task<IReadOnlyList<AuditRecordDto>> TrailAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/audit-records?entityType=User&entityId={id}", UriKind.Relative));

        return (await response.ReadAsAsync<PageOf<AuditRecordDto>>()).Items;
    }

    private async Task<int> CountAsync(string sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQueryRaw<int>(sql).SingleAsync();
    }
}
