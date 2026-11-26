using System.Net;
using System.Net.Http.Json;
using BillingRecon.Api.Billing;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Infrastructure.Common.Idempotency;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Idempotency of financial POSTs (BR-803, US-35; ADR 0006) on the pipeline that ships and a real SQL Server, shown on
/// a test-only probe endpoint that behaves like a posting: it takes a gapless number and saves a record in one unit
/// of work. The four outcomes of BR-803 (replay, reuse, in progress, missing), what becomes of the work after a
/// refusal and after a fault, and then the two real endpoints that carry the attribute so far.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class IdempotencyApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    // The probe endpoint takes a document number and saves a probe record, not a document: its series has no documents.
    protected override IReadOnlyCollection<string> DriftLeftOnPurpose => [DriftIdentity.NumberSeriesGap];

    private const string Replayed = "Idempotent-Replayed";

    private static readonly Uri Records = new("/api/v1/probe/idempotent/records", UriKind.Relative);
    private static readonly Uri Other = new("/api/v1/probe/idempotent/other", UriKind.Relative);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task US35_a_retry_with_the_same_key_and_payload_gets_the_original_response_unchanged_and_nothing_runs_twice()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "Cashier");
        var key = Guid.NewGuid();

        using var first = await client.PostJsonAsync(Records, new IdempotencyProbeRequest("MRN-1"), key);
        var firstBody = await first.Content.ReadAsStringAsync();
        using var retry = await client.PostJsonAsync(Records, new IdempotencyProbeRequest("MRN-1"), key);
        using var again = await client.PostJsonAsync(Records, new IdempotencyProbeRequest("MRN-1"), key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        first.Headers.Contains(Replayed).ShouldBeFalse("the first response is not a replay");
        var record = (await first.Content.ReadFromJsonAsync<IdempotencyProbeResponse>(TestJson.Options)).ShouldNotBeNull();
        record.Number.ShouldBe("PAY/26-27/000001");

        foreach (var replay in new[] { retry, again })
        {
            replay.StatusCode.ShouldBe(HttpStatusCode.Created);
            (await replay.Content.ReadAsStringAsync()).ShouldBe(firstBody, "a replay returns the body byte for byte");
            replay.Content.Headers.ContentType.ShouldBe(first.Content.Headers.ContentType);
            replay.Headers.Location.ShouldBe(first.Headers.Location);
            replay.Headers.ETag.ShouldBe(first.Headers.ETag);
            replay.Headers.GetValues(Replayed).ShouldBe(["true"]);
        }

        first.Headers.Location.ShouldNotBeNull().ToString().ShouldEndWith($"/api/v1/probe/idempotent/records/{record.Id}");
        first.Headers.ETag.ShouldNotBeNull();
        factory.IdempotencyProbe.Executions.ShouldBe(1);

        await using var reader = ApiContext(tenant);
        (await reader.Set<Patient>().CountAsync()).ShouldBe(1);
        (await reader.Set<NumberSeries>().SingleAsync()).NextNumber.ShouldBe(2);
    }

    [Fact]
    public async Task The_key_is_stored_completed_with_its_fingerprint_its_answer_and_thirty_days_to_live()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "Cashier");
        var key = Guid.NewGuid();

        using var response = await client.PostJsonAsync(Records, new IdempotencyProbeRequest("MRN-1"), key);
        var body = await response.Content.ReadAsStringAsync();

        await using var reader = ApiContext(tenant);
        var row = await reader.Set<IdempotencyKey>().SingleAsync();
        row.Key.ShouldBe(key);
        row.TenantId.ShouldBe(tenant.TenantId!.Value);
        row.UserId.ShouldBe(tenant.UserId);
        row.Method.ShouldBe("POST");
        row.Path.ShouldBe("/api/v1/probe/idempotent/records");
        row.RequestHash.Length.ShouldBe(32);
        row.Status.ShouldBe(IdempotencyStatus.Completed);
        row.StatusCode.ShouldBe(201);
        row.ResponseBody.ShouldBe(body);
        row.ResponseContentType.ShouldNotBeNull().ShouldStartWith("application/json");
        row.ResponseHeaders.ShouldNotBeNull().ShouldContain("/api/v1/probe/idempotent/records/");
        row.CreatedAtUtc.ShouldBe(DatabaseApiFactory.InitialInstant);
        row.CompletedAtUtc.ShouldBe(DatabaseApiFactory.InitialInstant);
        row.ExpiresAtUtc.ShouldBe(DatabaseApiFactory.InitialInstant.AddDays(30));
    }

    [Fact]
    public async Task BR803_the_same_key_with_another_payload_query_version_or_path_answers_422_and_the_original_stays()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "Cashier");
        var key = Guid.NewGuid();
        using var original = await client.PostJsonAsync(Records, new IdempotencyProbeRequest("MRN-1"), key);
        var originalBody = await original.Content.ReadAsStringAsync();

        using var otherBody = await client.PostJsonAsync(Records, new IdempotencyProbeRequest("MRN-2"), key);
        using var otherQuery = await client.PostJsonAsync(Records.WithQuery("tag=second"), new IdempotencyProbeRequest("MRN-1"), key);
        using var otherVersion = await client.SendWithIfMatchAsync(HttpMethod.Post, Records, new IdempotencyProbeRequest("MRN-1"), "\"AAAAAAAAB9E=\"", key);
        using var otherPath = await client.PostJsonAsync(Other, new IdempotencyProbeRequest("MRN-1"), key);
        using var replay = await client.PostJsonAsync(Records, new IdempotencyProbeRequest("MRN-1"), key);

        original.StatusCode.ShouldBe(HttpStatusCode.Created);
        foreach (var reuse in new[] { otherBody, otherQuery, otherVersion, otherPath })
        {
            await reuse.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, CommonErrors.IdempotencyKeyReuseCode);
            reuse.Headers.Contains(Replayed).ShouldBeFalse();
        }

        (await replay.Content.ReadAsStringAsync()).ShouldBe(originalBody, "the original answer is never overwritten");
        factory.IdempotencyProbe.Executions.ShouldBe(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("12345")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("3f2504e04f8911d39a0c0305e82c3301")] // a UUID without its hyphens
    [InlineData("{3f2504e0-4f89-11d3-9a0c-0305e82c3301}")]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301, 3f2504e0-4f89-11d3-9a0c-0305e82c3302")] // a list
    public async Task BR803_a_missing_or_malformed_key_answers_400_and_nothing_runs(string? header)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "Cashier");
        using var request = new HttpRequestMessage(HttpMethod.Post, Records)
        {
            Content = JsonContent.Create(new IdempotencyProbeRequest("MRN-1"), options: TestJson.Options),
        };
        if (header is not null)
        {
            request.Headers.TryAddWithoutValidation(PayersApi.IdempotencyKeyHeader, header).ShouldBeTrue();
        }

        using var response = await client.SendAsync(request);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.IdempotencyKeyMissingCode);
        problem.GetProperty("errors").TryGetProperty(PayersApi.IdempotencyKeyHeader, out _).ShouldBeTrue();
        factory.IdempotencyProbe.Executions.ShouldBe(0);

        await using var reader = ApiContext(tenant);
        (await reader.Set<IdempotencyKey>().CountAsync()).ShouldBe(0);
        (await reader.Set<Patient>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task BR803_two_headers_with_a_key_each_are_refused_like_none()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "Cashier");
        using var request = new HttpRequestMessage(HttpMethod.Post, Records)
        {
            Content = JsonContent.Create(new IdempotencyProbeRequest("MRN-1"), options: TestJson.Options),
        };
        request.Headers.Add(PayersApi.IdempotencyKeyHeader, [Guid.NewGuid().ToString(), Guid.NewGuid().ToString()]);

        using var response = await client.SendAsync(request);

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.IdempotencyKeyMissingCode);
        factory.IdempotencyProbe.Executions.ShouldBe(0);
    }

    [Fact]
    public async Task BR803_a_second_request_while_the_first_is_still_running_answers_409_and_the_first_completes()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "Cashier");
        var key = Guid.NewGuid();
        var waiting = new IdempotencyProbeRequest("MRN-1", IdempotencyProbeOutcome.Wait);

        var first = client.PostJsonAsync(Records, waiting, key);
        await factory.IdempotencyProbe.Entered.WaitAsync(Patience);

        using var second = await client.PostJsonAsync(Records, waiting, key);
        await second.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.IdempotencyInProgressCode);

        factory.IdempotencyProbe.OpenGate();
        using var completed = await first.WaitAsync(Patience);
        using var replay = await client.PostJsonAsync(Records, waiting, key);

        completed.StatusCode.ShouldBe(HttpStatusCode.Created);
        completed.Headers.Contains(Replayed).ShouldBeFalse();
        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.Headers.GetValues(Replayed).ShouldBe(["true"]);
        (await replay.Content.ReadAsStringAsync()).ShouldBe(await completed.Content.ReadAsStringAsync());
        factory.IdempotencyProbe.Executions.ShouldBe(1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task BR803_of_ten_simultaneous_requests_with_one_key_exactly_one_runs(int round)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "Cashier");
        var key = Guid.NewGuid();
        var payload = new IdempotencyProbeRequest($"MRN-{round}");

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => client.PostJsonAsync(Records, payload, key))));

        try
        {
            // Each of the ten is the one that ran, a replay of it, or was told to wait: never a second execution.
            var created = responses.Where(response => response.StatusCode == HttpStatusCode.Created).ToArray();
            created.Count(response => !response.Headers.Contains(Replayed)).ShouldBe(1);
            foreach (var other in responses.Except(created))
            {
                await other.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.IdempotencyInProgressCode);
            }

            var bodies = await Task.WhenAll(created.Select(response => response.Content.ReadAsStringAsync()));
            bodies.Distinct().Count().ShouldBe(1);
            factory.IdempotencyProbe.Executions.ShouldBe(1);
            factory.Logs.Events.ShouldNotContain(logEvent => logEvent.Level >= LogEventLevel.Error);

            using var afterwards = await client.PostJsonAsync(Records, payload, key);
            (await afterwards.Content.ReadAsStringAsync()).ShouldBe(bodies[0]);

            await using var reader = ApiContext(tenant);
            (await reader.Set<Patient>().CountAsync()).ShouldBe(1);
            (await reader.Set<NumberSeries>().SingleAsync()).NextNumber.ShouldBe(2);
            (await reader.Set<IdempotencyKey>().CountAsync()).ShouldBe(1);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_refusal_is_replayed_unchanged_and_everything_the_request_did_is_undone_number_included()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "Cashier");
        var key = Guid.NewGuid();
        var refused = new IdempotencyProbeRequest("MRN-1", IdempotencyProbeOutcome.Refuse);

        // The probe takes a number, saves its record, commits its unit of work, and only then refuses.
        using var first = await client.PostJsonAsync(Records, refused, key);
        var firstBody = await first.Content.ReadAsStringAsync();
        using var retry = await client.PostJsonAsync(Records, refused, key);

        await first.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, IdempotencyProbeController.RefusedCode);
        retry.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await retry.Content.ReadAsStringAsync()).ShouldBe(firstBody, "the same refusal, with the trace and correlation ids of the first");
        retry.Headers.GetValues(Replayed).ShouldBe(["true"]);
        factory.IdempotencyProbe.Executions.ShouldBe(1);

        await using (var reader = ApiContext(tenant))
        {
            (await reader.Set<Patient>().CountAsync()).ShouldBe(0, "the record the refused request saved was rolled back");
            (await reader.Set<NumberSeries>().CountAsync()).ShouldBe(0, "and so was the number series it created");
            var row = await reader.Set<IdempotencyKey>().SingleAsync();
            row.Status.ShouldBe(IdempotencyStatus.Completed);
            row.StatusCode.ShouldBe(422);
        }

        // The number the refused request took is handed out to the next one: no gap (BR-801).
        using var next = await client.PostJsonAsync(Records, new IdempotencyProbeRequest("MRN-1"));
        (await next.Content.ReadFromJsonAsync<IdempotencyProbeResponse>(TestJson.Options)).ShouldNotBeNull().Number.ShouldBe("PAY/26-27/000001");
    }

    [Fact]
    public async Task A_request_the_model_binder_refuses_is_stored_and_replayed_like_any_other_refusal()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "Cashier");
        var key = Guid.NewGuid();

        using var first = await SendRawAsync(client, key, """{"mrn":"MRN-1","then":"NoSuchOutcome"}""");
        var firstBody = await first.Content.ReadAsStringAsync();
        using var retry = await SendRawAsync(client, key, """{"mrn":"MRN-1","then":"NoSuchOutcome"}""");

        await first.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        retry.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await retry.Content.ReadAsStringAsync()).ShouldBe(firstBody);
        retry.Headers.GetValues(Replayed).ShouldBe(["true"]);
        factory.IdempotencyProbe.Executions.ShouldBe(0);
    }

    [Fact]
    public async Task A_fault_stores_nothing_undoes_everything_and_frees_the_key_for_a_retry_that_then_runs()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "Cashier");
        var key = Guid.NewGuid();
        var payload = new IdempotencyProbeRequest("MRN-1", IdempotencyProbeOutcome.FaultOnce);
        factory.IdempotencyProbe.FaultNext(1);

        // The probe takes a number, saves its record, commits its unit of work, and then throws.
        using var faulted = await client.PostJsonAsync(Records, payload, key);
        await faulted.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "internal_error");

        await using (var reader = ApiContext(tenant))
        {
            (await reader.Set<IdempotencyKey>().CountAsync()).ShouldBe(0, "a fault leaves no key behind");
            (await reader.Set<Patient>().CountAsync()).ShouldBe(0);
            (await reader.Set<NumberSeries>().CountAsync()).ShouldBe(0);
        }

        using var retry = await client.PostJsonAsync(Records, payload, key);
        using var replay = await client.PostJsonAsync(Records, payload, key);

        retry.StatusCode.ShouldBe(HttpStatusCode.Created);
        retry.Headers.Contains(Replayed).ShouldBeFalse("the retry after a fault is a first execution, not a replay");
        var record = (await retry.Content.ReadFromJsonAsync<IdempotencyProbeResponse>(TestJson.Options)).ShouldNotBeNull();
        record.Number.ShouldBe("PAY/26-27/000001", "the number the faulted request took was given back");
        replay.Headers.GetValues(Replayed).ShouldBe(["true"]);
        factory.IdempotencyProbe.Executions.ShouldBe(2);

        await using var after = ApiContext(tenant);
        (await after.Set<Patient>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_key_left_in_progress_by_a_crash_answers_409_until_it_is_cleared_and_then_the_request_runs()
    {
        // What a process that died between reserving and answering leaves behind (docs/runbook.md has the procedure).
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "Cashier");
        var key = Guid.NewGuid();
        var payload = new IdempotencyProbeRequest("MRN-1");
        using (var crashed = await client.PostJsonAsync(Records, payload, key))
        {
            crashed.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        await using (var system = ApiContext(SystemIdentity.Instance))
        {
            await system.Database.ExecuteSqlAsync(
                $"UPDATE [dbo].[IdempotencyKeys] SET [Status] = 'InProgress', [StatusCode] = NULL, [CompletedAtUtc] = NULL, [ResponseBody] = NULL, [ResponseHeaders] = NULL, [ResponseContentType] = NULL WHERE [Key] = {key}");
        }

        using var stuck = await client.PostJsonAsync(Records, payload, key);
        await stuck.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.IdempotencyInProgressCode);

        await using (var system = ApiContext(SystemIdentity.Instance))
        {
            (await system.Database.ExecuteSqlAsync($"DELETE FROM [dbo].[IdempotencyKeys] WHERE [Key] = {key} AND [Status] = 'InProgress'")).ShouldBe(1);
        }

        using var cleared = await client.PostJsonAsync(Records, new IdempotencyProbeRequest("MRN-2"), key);
        cleared.StatusCode.ShouldBe(HttpStatusCode.Created);
        cleared.Headers.Contains(Replayed).ShouldBeFalse();
    }

    [Fact]
    public async Task A_key_left_in_progress_by_a_crash_is_taken_over_by_a_retry_after_five_minutes_and_not_before()
    {
        // Review 2026-11, finding R3: until 2026-11-26 such a key answered 409 until somebody deleted it by hand.
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "Cashier");
        var key = Guid.NewGuid();
        var payload = new IdempotencyProbeRequest("MRN-1");
        using (var crashed = await client.PostJsonAsync(Records, payload, key))
        {
            crashed.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        await using (var system = ApiContext(SystemIdentity.Instance))
        {
            await system.Database.ExecuteSqlAsync(
                $"UPDATE [dbo].[IdempotencyKeys] SET [Status] = 'InProgress', [StatusCode] = NULL, [CompletedAtUtc] = NULL, [ResponseBody] = NULL, [ResponseHeaders] = NULL, [ResponseContentType] = NULL WHERE [Key] = {key}");
        }

        // What the dead request did was rolled back with its connection: the patient it registered is not there. The
        // application may not delete a patient, so the administrator of the test does.
        await using (var administrator = AdministratorApiContext(SystemIdentity.Instance))
        {
            await administrator.Database.ExecuteSqlAsync($"DELETE FROM [dbo].[Patients] WHERE [TenantId] = {tenant.TenantId!.Value.Value} AND [Mrn] = 'MRN-1'");
        }

        // Five minutes exactly is not yet longer than five minutes.
        factory.Clock.Advance(IdempotencyKey.AbandonedAfter);
        using (var waiting = await client.PostJsonAsync(Records, payload, key))
        {
            await waiting.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.IdempotencyInProgressCode);
        }

        // Another request under the abandoned key is still another request: the key is not its to take.
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var other = await client.PostJsonAsync(Records, new IdempotencyProbeRequest("MRN-2"), key))
        {
            await other.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, CommonErrors.IdempotencyKeyReuseCode);
        }

        // The same request takes it over, runs, and is replayed from then on.
        using var retried = await client.PostJsonAsync(Records, payload, key);
        retried.StatusCode.ShouldBe(HttpStatusCode.Created, await retried.Content.ReadAsStringAsync());
        retried.Headers.Contains(Replayed).ShouldBeFalse("the retry ran: there was no answer to replay");
        var body = await retried.Content.ReadAsStringAsync();

        using var replay = await client.PostJsonAsync(Records, payload, key);
        replay.Headers.GetValues(Replayed).ShouldBe(["true"]);
        (await replay.Content.ReadAsStringAsync()).ShouldBe(body);

        await using var reader = ApiContext(tenant);
        var row = await reader.Set<IdempotencyKey>().SingleAsync();
        row.Status.ShouldBe(IdempotencyStatus.Completed);
        row.CreatedAtUtc.ShouldBe(factory.Clock.UtcNow, "the reservation is the retry's own");
    }

    [Fact]
    public async Task The_sweep_deletes_the_keys_of_every_tenant_that_have_expired_and_no_other()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        var elsewhere = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "Cashier");
        using var theirClient = factory.CreateClientFor(elsewhere, "Cashier");

        async Task<Guid> RecordedAsync(HttpClient sender, string mrn)
        {
            var key = Guid.NewGuid();
            using var response = await sender.PostJsonAsync(Records, new IdempotencyProbeRequest(mrn), key);
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

            return key;
        }

        async Task<int> SweptAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();

            return await scope.ServiceProvider.GetRequiredService<IdempotencyKeySweep>().RunAsync(CancellationToken.None);
        }

        await RecordedAsync(client, "MRN-1");
        await RecordedAsync(client, "MRN-2");
        await RecordedAsync(theirClient, "MRN-1");

        // On the day a key expires it is still kept.
        factory.Clock.Advance(IdempotencyKey.Retention);
        (await SweptAsync()).ShouldBe(0);

        var young = await RecordedAsync(client, "MRN-3");
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        (await SweptAsync()).ShouldBe(3);
        (await SweptAsync()).ShouldBe(0);

        await using var reader = ApiContext(tenant);
        (await reader.Set<IdempotencyKey>().SingleAsync()).Key.ShouldBe(young);
        await using var theirs = ApiContext(elsewhere);
        (await theirs.Set<IdempotencyKey>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task BR002_the_key_is_scoped_to_the_tenant_two_tenants_may_use_the_same_one()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenantA = await NewApiTenantAsync();
        var tenantB = await NewApiTenantAsync();
        using var asA = factory.CreateClientFor(tenantA, "Cashier");
        using var asB = factory.CreateClientFor(tenantB, "Cashier");
        var key = Guid.NewGuid();

        using var inA = await asA.PostJsonAsync(Records, new IdempotencyProbeRequest("MRN-1"), key);
        using var inB = await asB.PostJsonAsync(Records, new IdempotencyProbeRequest("MRN-OTHER"), key);
        using var replayInB = await asB.PostJsonAsync(Records, new IdempotencyProbeRequest("MRN-OTHER"), key);

        inA.StatusCode.ShouldBe(HttpStatusCode.Created);
        inB.StatusCode.ShouldBe(HttpStatusCode.Created, "another tenant's key is no key of this tenant: not a reuse, not a replay");
        inB.Headers.Contains(Replayed).ShouldBeFalse();
        (await replayInB.Content.ReadAsStringAsync()).ShouldBe(await inB.Content.ReadAsStringAsync());
        (await inB.Content.ReadAsStringAsync()).ShouldNotBe(await inA.Content.ReadAsStringAsync());
        factory.IdempotencyProbe.Executions.ShouldBe(2);

        await using var readerB = ApiContext(tenantB);
        (await readerB.Set<IdempotencyKey>().SingleAsync()).TenantId.ShouldBe(tenantB.TenantId!.Value);
    }

    [Fact]
    public async Task Two_users_of_one_tenant_cannot_use_one_key_for_two_operations()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var clerk = await NewApiTenantAsync();
        var colleague = new FixedIdentity(clerk.TenantId, new UserId(Guid.NewGuid()));
        using var asClerk = factory.CreateClientFor(clerk, "Cashier");
        using var asColleague = factory.CreateClientFor(colleague, "Cashier");
        var key = Guid.NewGuid();

        using var first = await asClerk.PostJsonAsync(Records, new IdempotencyProbeRequest("MRN-1"), key);
        using var sameOperation = await asColleague.PostJsonAsync(Records, new IdempotencyProbeRequest("MRN-1"), key);
        using var otherOperation = await asColleague.PostJsonAsync(Records, new IdempotencyProbeRequest("MRN-2"), key);

        // The key names the operation, not the user (ADR 0006).
        sameOperation.Headers.GetValues(Replayed).ShouldBe(["true"]);
        (await sameOperation.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        await otherOperation.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, CommonErrors.IdempotencyKeyReuseCode);
    }

    [Fact]
    public async Task An_endpoint_without_the_attribute_ignores_the_header_and_stores_no_key()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "BillingClerk");
        var key = Guid.NewGuid();

        using var first = await client.PostJsonAsync(PayersApi.Patients, TestData.NewPatient() with { Mrn = "MRN-1" }, key);
        using var second = await client.PostJsonAsync(PayersApi.Patients, TestData.NewPatient() with { Mrn = "MRN-2" }, key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created, "registering a patient is not a financial POST");

        await using var reader = ApiContext(tenant);
        (await reader.Set<IdempotencyKey>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Rule5_creating_a_draft_bill_needs_a_key_and_a_retry_creates_no_second_draft()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var tenant = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(tenant, "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var draft = new CreateBillRequest { PatientId = patient.Id, PayerId = payer.Id, Lines = [BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m)] };
        var key = Guid.NewGuid();
        using var withoutKey = new HttpRequestMessage(HttpMethod.Post, BillingApi.Bills) { Content = JsonContent.Create(draft, options: TestJson.Options) };

        using var refused = await client.SendAsync(withoutKey);
        using var created = await client.PostJsonAsync(BillingApi.Bills, draft, key);
        using var retry = await client.PostJsonAsync(BillingApi.Bills, draft, key);
        using var changed = await client.PostJsonAsync(BillingApi.Bills, draft with { Notes = "Changed" }, key);

        await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.IdempotencyKeyMissingCode);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var bill = await created.ReadAsAsync<BillDto>();
        retry.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await retry.ReadAsAsync<BillDto>()).ShouldBeEquivalentTo(bill);
        retry.Headers.Location.ShouldBe(created.Headers.Location);
        retry.Headers.ETag.ShouldBe(created.Headers.ETag);
        retry.Headers.GetValues(Replayed).ShouldBe(["true"]);
        await changed.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, CommonErrors.IdempotencyKeyReuseCode);

        await using var reader = ApiContext(tenant);
        (await reader.Set<Bill>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Rule5_voiding_a_bill_needs_a_key_and_a_retry_gets_the_voided_bill_not_a_refusal()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var bill = await client.CreateBillAsync(new CreateBillRequest { PatientId = patient.Id, PayerId = payer.Id });
        var reason = new VoidBillRequest { Reason = "Entered twice" };
        var etag = PayersApi.ETag(bill.Version);
        var key = Guid.NewGuid();
        using var withoutKey = new HttpRequestMessage(HttpMethod.Post, BillingApi.Bill(bill.Id, "void")) { Content = JsonContent.Create(reason, options: TestJson.Options) };
        withoutKey.Headers.TryAddWithoutValidation("If-Match", etag);

        using var refused = await client.SendAsync(withoutKey);
        using var voided = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "void"), reason, etag, key);
        using var retry = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "void"), reason, etag, key);
        using var newAttempt = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "void"), reason, etag);

        await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.IdempotencyKeyMissingCode);
        voided.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await voided.ReadAsAsync<BillDto>();
        after.Status.ShouldBe(BillStatus.Voided);

        // Without the key the retry would find a voided bill and a stale version, and be told 409.
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await retry.ReadAsAsync<BillDto>()).ShouldBeEquivalentTo(after);
        retry.Headers.GetValues(Replayed).ShouldBe(["true"]);
        await newAttempt.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);
    }

    [Fact]
    public async Task A_conflict_is_stored_too_and_a_new_attempt_with_the_new_version_needs_a_new_key()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var bill = await client.CreateBillAsync(new CreateBillRequest { PatientId = patient.Id, PayerId = payer.Id });
        using var edit = await client.SendWithIfMatchAsync(HttpMethod.Patch, BillingApi.Bill(bill.Id), new UpdateBillHeaderRequest { BillDate = bill.BillDate, DueDate = bill.DueDate, Notes = "Edited" }, PayersApi.ETag(bill.Version));
        var current = await edit.ReadAsAsync<BillDto>();
        var reason = new VoidBillRequest { Reason = "Entered twice" };
        var key = Guid.NewGuid();

        using var stale = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "void"), reason, PayersApi.ETag(bill.Version), key);
        using var sameKeyNewVersion = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "void"), reason, PayersApi.ETag(current.Version), key);
        using var newKeyNewVersion = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(bill.Id, "void"), reason, PayersApi.ETag(current.Version));

        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, CommonErrors.ConcurrencyConflictCode);

        // The version is part of the request: sending the new one under the old key is a different request, and the
        // client is told so instead of being handed the old 409 again.
        await sameKeyNewVersion.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, CommonErrors.IdempotencyKeyReuseCode);
        newKeyNewVersion.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> SendRawAsync(HttpClient client, Guid key, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Records) { Content = PayersApi.RawJson(json) };
        request.Headers.Add(PayersApi.IdempotencyKeyHeader, key.ToString());

        return await client.SendAsync(request);
    }
}
