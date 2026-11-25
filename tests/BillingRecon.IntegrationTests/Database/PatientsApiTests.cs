using System.Net;
using System.Text.Json;
using BillingRecon.Api.Payers;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Payers;
using BillingRecon.IntegrationTests.Support;
using Serilog.Events;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The patient endpoints end to end, on the pipeline that ships and a real SQL Server (US-10, US-12; BR-101, BR-102,
/// BR-804): status codes, error codes, ETags and If-Match, paging and search.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PatientsApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    [Fact]
    public async Task US10_registering_a_patient_answers_201_with_the_patient_its_address_and_its_version()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var request = TestData.NewPatient();

        using var response = await client.PostJsonAsync(PayersApi.Patients, request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var patient = await response.ReadAsAsync<PatientDto>();
        patient.Id.ShouldNotBe(Guid.Empty);
        patient.Mrn.ShouldBe(request.Mrn);
        patient.FullName.ShouldBe(request.FullName);
        patient.DateOfBirth.ShouldBe(request.DateOfBirth);
        patient.Sex.ShouldBe(request.Sex);
        patient.PinCode.ShouldBe(request.PinCode);
        patient.Status.ShouldBe(PatientStatus.Active);
        patient.CreatedAtUtc.ShouldBe(DatabaseApiFactory.InitialInstant);
        Convert.FromBase64String(patient.Version).Length.ShouldBe(8);
        response.Headers.Location.ShouldNotBeNull().ToString().ShouldEndWith($"/api/v1/patients/{patient.Id}");
        response.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(patient.Version));
    }

    [Fact]
    public async Task Enums_travel_by_name()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");

        using var response = await client.PostJsonAsync(PayersApi.Patients, TestData.NewPatient() with { Sex = Sex.Female });

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("sex").GetString().ShouldBe("Female");
        body.GetProperty("status").GetString().ShouldBe("Active");
    }

    [Fact]
    public async Task Reading_a_patient_returns_it_with_its_version_as_the_etag()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var registered = await client.RegisterPatientAsync();

        using var response = await client.GetAsync(PayersApi.Patient(registered.Id));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.ReadAsAsync<PatientDto>()).ShouldBe(registered);
        response.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(registered.Version));
    }

    [Fact]
    public async Task Reading_a_patient_that_does_not_exist_answers_404_patient_not_found()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "Viewer");

        using var response = await client.GetAsync(PayersApi.Patient(Guid.NewGuid()));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PatientNotFoundCode);
    }

    [Fact]
    public async Task A_registration_with_invalid_fields_answers_400_with_the_messages_per_field()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");

        using var response = await client.PostJsonAsync(
            PayersApi.Patients,
            new RegisterPatientRequest { Mrn = " ", Email = "nope", PinCode = "४११००१", DateOfBirth = new DateOnly(2027, 1, 1) });

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var fields = problem.GetProperty("errors").EnumerateObject().Select(field => field.Name).ToArray();
        fields.ShouldBe(["mrn", "fullName", "email", "pinCode", "dateOfBirth"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("""{"mrn":"MRN-1","fullName":"Priya Sharma","nickname":"Pri"}""")]
    [InlineData("""{"mrn":"MRN-1","fullName":"Priya Sharma","sex":1}""")]
    [InlineData("""{"mrn":"MRN-1","fullName":"Priya Sharma","sex":"Banana"}""")]
    public async Task A_body_with_an_unknown_field_or_an_enum_that_is_not_a_known_name_answers_400(string json)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        using var content = PayersApi.RawJson(json);

        using var response = await client.PostAsync(PayersApi.Patients, content);

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task BR101_registering_an_mrn_the_tenant_already_has_answers_409_whatever_the_case_of_its_letters()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        await client.RegisterPatientAsync(TestData.NewPatient() with { Mrn = "MRN-AB-12" });

        using var same = await client.PostJsonAsync(PayersApi.Patients, TestData.NewPatient() with { Mrn = "MRN-AB-12" });
        using var otherCase = await client.PostJsonAsync(PayersApi.Patients, TestData.NewPatient() with { Mrn = "  mrn-ab-12 " });

        await same.ShouldBeProblemAsync(HttpStatusCode.Conflict, PayersErrors.PatientMrnDuplicateCode);
        await otherCase.ShouldBeProblemAsync(HttpStatusCode.Conflict, PayersErrors.PatientMrnDuplicateCode);
    }

    [Fact]
    public async Task BR101_of_ten_simultaneous_registrations_of_one_mrn_exactly_one_succeeds_and_none_is_logged_as_an_error()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        const string mrn = "MRN-RACE-1";

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.PostJsonAsync(PayersApi.Patients, TestData.NewPatient() with { Mrn = mrn })));

        try
        {
            responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
            foreach (var refused in responses.Where(response => response.StatusCode != HttpStatusCode.Created))
            {
                // Refused by the check in the use case or, for a request that passed it, by the unique index: one answer.
                await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, PayersErrors.PatientMrnDuplicateCode);
            }

            factory.Logs.Events.ShouldNotContain(logEvent => logEvent.Level >= LogEventLevel.Error);
            factory.Logs.Events.ShouldNotContain(logEvent => logEvent.ToJson().Contains(mrn, StringComparison.OrdinalIgnoreCase));
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
    public async Task Correcting_a_patient_with_the_etag_it_read_answers_200_with_the_new_version()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var registered = await client.RegisterPatientAsync();
        factory.Clock.Advance(TimeSpan.FromHours(1));

        using var response = await client.SendWithIfMatchAsync(
            HttpMethod.Patch,
            PayersApi.Patient(registered.Id),
            new UpdatePatientRequest { FullName = "Priya S. Rao", City = "Mumbai" },
            PayersApi.ETag(registered.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var corrected = await response.ReadAsAsync<PatientDto>();
        corrected.FullName.ShouldBe("Priya S. Rao");
        corrected.City.ShouldBe("Mumbai");
        corrected.Phone.ShouldBeNull("a field the correction leaves out is cleared");
        corrected.Mrn.ShouldBe(registered.Mrn);
        corrected.CreatedAtUtc.ShouldBe(registered.CreatedAtUtc);
        corrected.UpdatedAtUtc.ShouldBe(DatabaseApiFactory.InitialInstant.AddHours(1));
        corrected.Version.ShouldNotBe(registered.Version);
        response.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(corrected.Version));
    }

    [Fact]
    public async Task BR804_a_correction_without_if_match_answers_428_and_changes_nothing()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var registered = await client.RegisterPatientAsync();

        using var response = await client.SendWithIfMatchAsync(HttpMethod.Patch, PayersApi.Patient(registered.Id), new UpdatePatientRequest { FullName = "Changed" }, ifMatch: null);

        await response.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        (await ReadAsync(client, registered.Id)).ShouldBe(registered);
    }

    [Fact]
    public async Task BR804_a_correction_from_a_stale_version_answers_409_concurrency_conflict_and_changes_nothing()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var registered = await client.RegisterPatientAsync();
        using var first = await client.SendWithIfMatchAsync(HttpMethod.Patch, PayersApi.Patient(registered.Id), new UpdatePatientRequest { FullName = "First" }, PayersApi.ETag(registered.Version));
        var afterFirst = await first.ReadAsAsync<PatientDto>();

        using var second = await client.SendWithIfMatchAsync(HttpMethod.Patch, PayersApi.Patient(registered.Id), new UpdatePatientRequest { FullName = "Second" }, PayersApi.ETag(registered.Version));

        await second.ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");
        (await ReadAsync(client, registered.Id)).ShouldBe(afterFirst);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"AAAAAAAAB9E=\"")]
    [InlineData("AAAAAAAAB9E=")]
    [InlineData("\"AAAAAAAAB9E=\", \"AAAAAAAAB9I=\"")]
    [InlineData("\"bm90IGVpZ2h0IGJ5dGVz\"")]
    public async Task An_if_match_that_is_not_one_strong_etag_of_this_api_answers_400(string ifMatch)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var registered = await client.RegisterPatientAsync();

        using var response = await client.SendWithIfMatchAsync(HttpMethod.Patch, PayersApi.Patient(registered.Id), new UpdatePatientRequest { FullName = "Changed" }, ifMatch);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.GetProperty("errors").TryGetProperty("If-Match", out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"fullName":"Changed","mrn":"MRN-OTHER"}""")]
    [InlineData("""{"fullName":"Changed","status":"Inactive"}""")]
    public async Task A_correction_cannot_change_the_mrn_or_the_status(string json)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var registered = await client.RegisterPatientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Patch, PayersApi.Patient(registered.Id)) { Content = PayersApi.RawJson(json) };
        request.Headers.TryAddWithoutValidation("If-Match", PayersApi.ETag(registered.Version));

        using var response = await client.SendAsync(request);

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        (await ReadAsync(client, registered.Id)).ShouldBe(registered);
    }

    [Fact]
    public async Task US12_a_patient_is_deactivated_and_reactivated_with_the_version_it_was_read_at()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var registered = await client.RegisterPatientAsync();

        using var deactivated = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Patient(registered.Id, "deactivate"), body: null, PayersApi.ETag(registered.Version));
        var inactive = await deactivated.ReadAsAsync<PatientDto>();
        using var again = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Patient(registered.Id, "deactivate"), body: null, PayersApi.ETag(inactive.Version));
        using var reactivated = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Patient(registered.Id, "reactivate"), body: null, PayersApi.ETag(inactive.Version));

        deactivated.StatusCode.ShouldBe(HttpStatusCode.OK);
        inactive.Status.ShouldBe(PatientStatus.Inactive);
        await again.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PayersErrors.PatientAlreadyInactiveCode);
        reactivated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reactivated.ReadAsAsync<PatientDto>()).Status.ShouldBe(PatientStatus.Active);
    }

    [Fact]
    public async Task BR804_deactivating_without_if_match_answers_428()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var registered = await client.RegisterPatientAsync();

        using var response = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Patient(registered.Id, "deactivate"), body: null, ifMatch: null);

        await response.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
    }

    [Fact]
    public async Task Listing_pages_through_the_patients_by_name_with_the_total_count()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        foreach (var name in new[] { "Esha Iyer", "Asha Nair", "Deepa Rao", "Bina Shah", "Chetan Das" })
        {
            await client.RegisterPatientAsync(TestData.NewPatient() with { FullName = name });
        }

        using var response = await client.GetAsync(PayersApi.Patients.WithQuery("page=2&pageSize=2"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.ReadAsAsync<PageOf<PatientDto>>();
        page.Items.Select(patient => patient.FullName).ShouldBe(["Chetan Das", "Deepa Rao"]);
        page.Page.ShouldBe(2);
        page.PageSize.ShouldBe(2);
        page.TotalCount.ShouldBe(5);
    }

    [Fact]
    public async Task A_list_without_paging_returns_the_first_twenty()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        await client.RegisterPatientAsync();

        using var response = await client.GetAsync(PayersApi.Patients);

        var page = await response.ReadAsAsync<PageOf<PatientDto>>();
        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(20);
        page.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Searching_finds_a_patient_by_a_part_of_the_mrn_or_of_the_name_without_regard_to_case()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        await client.RegisterPatientAsync(TestData.NewPatient() with { Mrn = "MRN-AB-100", FullName = "Priya Sharma" });
        await client.RegisterPatientAsync(TestData.NewPatient() with { Mrn = "MRN-XY-200", FullName = "Rahul Verma" });

        (await SearchAsync(client, "sharma")).ShouldBe(["Priya Sharma"]);
        (await SearchAsync(client, "mrn-xy-200")).ShouldBe(["Rahul Verma"]);
        (await SearchAsync(client, "xy-2")).ShouldBe(["Rahul Verma"], "a part of an MRN finds it (Prompt 10.2b)");
        (await SearchAsync(client, "MRN-")).ShouldBe(["Priya Sharma", "Rahul Verma"]);
        (await SearchAsync(client, "MRN-ZZ")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_search_matches_the_wildcards_of_like_literally()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        await client.RegisterPatientAsync(TestData.NewPatient() with { FullName = "Ram 100% Kumar" });
        await client.RegisterPatientAsync(TestData.NewPatient() with { FullName = "Ram 1000 Kumar" });
        await client.RegisterPatientAsync(TestData.NewPatient() with { FullName = "A_B Shah" });
        await client.RegisterPatientAsync(TestData.NewPatient() with { FullName = "AXB Shah" });

        (await SearchAsync(client, "100%")).ShouldBe(["Ram 100% Kumar"]);
        (await SearchAsync(client, "A_B")).ShouldBe(["A_B Shah"]);
    }

    [Fact]
    public async Task A_search_matches_the_wildcards_of_like_literally_in_an_mrn()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        await client.RegisterPatientAsync(TestData.NewPatient() with { Mrn = "OP_100%A", FullName = "Asha Rao" });
        await client.RegisterPatientAsync(TestData.NewPatient() with { Mrn = "OPX1000A", FullName = "Bina Rao" });
        await client.RegisterPatientAsync(TestData.NewPatient() with { Mrn = "IP[7]", FullName = "Chitra Rao" });
        await client.RegisterPatientAsync(TestData.NewPatient() with { Mrn = "IP7", FullName = "Divya Rao" });

        (await SearchAsync(client, "P_1")).ShouldBe(["Asha Rao"]);
        (await SearchAsync(client, "00%")).ShouldBe(["Asha Rao"]);
        (await SearchAsync(client, "[7]")).ShouldBe(["Chitra Rao"]);
        (await SearchAsync(client, "%")).ShouldBe(["Asha Rao"]);
    }

    [Fact]
    public async Task Listing_by_status_returns_the_active_or_the_inactive_patients()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var active = await client.RegisterPatientAsync();
        var inactive = await client.RegisterPatientAsync();
        using var deactivation = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Patient(inactive.Id, "deactivate"), body: null, PayersApi.ETag(inactive.Version));
        deactivation.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var activeOnly = await client.GetAsync(PayersApi.Patients.WithQuery("status=Active"));
        using var inactiveOnly = await client.GetAsync(PayersApi.Patients.WithQuery("status=Inactive"));

        (await activeOnly.ReadAsAsync<PageOf<PatientDto>>()).Items.Select(patient => patient.Id).ShouldBe([active.Id]);
        (await inactiveOnly.ReadAsAsync<PageOf<PatientDto>>()).Items.Select(patient => patient.Id).ShouldBe([inactive.Id]);
    }

    [Theory]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("page=0", "page")]
    [InlineData("status=7", "status")]
    public async Task A_list_query_outside_the_limits_answers_400(string query, string field)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "Viewer");

        using var response = await client.GetAsync(PayersApi.Patients.WithQuery(query));

        // A value the binder refuses is reported under the property name, one the validator refuses under the JSON name.
        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.GetProperty("errors").EnumerateObject()
            .ShouldContain(error => string.Equals(error.Name, field, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<PatientDto> ReadAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(PayersApi.Patient(id));

        return await response.ReadAsAsync<PatientDto>();
    }

    private static async Task<string[]> SearchAsync(HttpClient client, string search)
    {
        using var response = await client.GetAsync(PayersApi.Patients.WithQuery($"search={Uri.EscapeDataString(search)}"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return [.. (await response.ReadAsAsync<PageOf<PatientDto>>()).Items.Select(patient => patient.FullName)];
    }
}
