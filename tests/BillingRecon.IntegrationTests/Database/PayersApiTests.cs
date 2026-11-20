using System.Net;
using BillingRecon.Api.Payers;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Payers;
using BillingRecon.IntegrationTests.Support;
using Serilog.Events;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The payer endpoints end to end, on the pipeline that ships and a real SQL Server (US-11, US-12; BR-103 to BR-105,
/// BR-804).
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PayersApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    [Fact]
    public async Task US11_a_payer_for_a_patient_answers_201_linked_to_the_patient_with_its_address_and_version()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var patient = await client.RegisterPatientAsync();

        using var response = await client.PostJsonAsync(PayersApi.Payers, ForPatient(patient));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var payer = await response.ReadAsAsync<PayerDto>();
        payer.Kind.ShouldBe(PayerKind.SelfPay);
        payer.PatientId.ShouldBe(patient.Id);
        payer.Name.ShouldBe(patient.FullName);
        payer.Status.ShouldBe(PayerStatus.Active);
        response.Headers.Location.ShouldNotBeNull().ToString().ShouldEndWith($"/api/v1/payers/{payer.Id}");
        response.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(payer.Version));
    }

    [Fact]
    public async Task BR104_guarantors_have_no_patient_and_any_number_of_them_may_exist()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");

        var first = await client.CreatePayerAsync(Guarantor("Rahul Sharma"));
        var second = await client.CreatePayerAsync(Guarantor("Rahul Sharma"));

        first.PatientId.ShouldBeNull();
        second.PatientId.ShouldBeNull();
        second.Id.ShouldNotBe(first.Id);
    }

    [Fact]
    public async Task BR104_a_second_payer_for_the_same_patient_answers_409_patient_already_linked()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var patient = await client.RegisterPatientAsync();
        await client.CreatePayerAsync(ForPatient(patient));

        using var response = await client.PostJsonAsync(PayersApi.Payers, ForPatient(patient));

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, PayersErrors.PatientAlreadyLinkedCode);
    }

    [Fact]
    public async Task BR104_of_ten_simultaneous_payers_for_one_patient_exactly_one_is_created()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var patient = await client.RegisterPatientAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.PostJsonAsync(PayersApi.Payers, ForPatient(patient))));

        try
        {
            responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
            foreach (var refused in responses.Where(response => response.StatusCode != HttpStatusCode.Created))
            {
                await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, PayersErrors.PatientAlreadyLinkedCode);
            }

            factory.Logs.Events.ShouldNotContain(logEvent => logEvent.Level >= LogEventLevel.Error);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Theory]
    [InlineData(PayerKind.Insurer)]
    [InlineData(PayerKind.Corporate)]
    public async Task BR103_an_insurer_or_a_corporate_account_answers_422_payer_kind_not_supported(PayerKind kind)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");

        using var response = await client.PostJsonAsync(PayersApi.Payers, Guarantor("Star Health") with { Kind = kind });

        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PayersErrors.PayerKindNotSupportedCode);
    }

    [Fact]
    public async Task A_payer_without_a_kind_answers_400_rather_than_becoming_self_pay_by_default()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");

        using var response = await client.PostJsonAsync(PayersApi.Payers, Guarantor("Rahul Sharma") with { Kind = null });

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.GetProperty("errors").TryGetProperty("kind", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task A_payer_for_a_patient_that_does_not_exist_answers_404_patient_not_found()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");

        using var response = await client.PostJsonAsync(PayersApi.Payers, Guarantor("Priya Sharma") with { PatientId = Guid.NewGuid() });

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, PayersErrors.PatientNotFoundCode);
    }

    [Fact]
    public async Task A_payer_for_an_inactive_patient_answers_422_patient_inactive()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var patient = await client.RegisterPatientAsync();
        using var deactivation = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Patient(patient.Id, "deactivate"), body: null, PayersApi.ETag(patient.Version));
        deactivation.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var response = await client.PostJsonAsync(PayersApi.Payers, ForPatient(patient));

        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PayersErrors.PatientInactiveCode);
    }

    [Fact]
    public async Task Correcting_a_payer_with_its_etag_replaces_its_details_and_without_one_or_from_a_stale_one_changes_nothing()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var payer = await client.CreatePayerAsync(Guarantor("Rahul Sharma") with { Phone = "+91 98765 43210" });
        var correction = new UpdatePayerRequest { Name = "Rahul K. Sharma", Email = "rahul@example.com" };

        using var withoutIfMatch = await client.SendWithIfMatchAsync(HttpMethod.Patch, PayersApi.Payer(payer.Id), correction, ifMatch: null);
        using var corrected = await client.SendWithIfMatchAsync(HttpMethod.Patch, PayersApi.Payer(payer.Id), correction, PayersApi.ETag(payer.Version));
        using var stale = await client.SendWithIfMatchAsync(HttpMethod.Patch, PayersApi.Payer(payer.Id), correction with { Name = "Too Late" }, PayersApi.ETag(payer.Version));

        await withoutIfMatch.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await corrected.ReadAsAsync<PayerDto>();
        after.Name.ShouldBe("Rahul K. Sharma");
        after.Email.ShouldBe("rahul@example.com");
        after.Phone.ShouldBeNull("a contact detail the correction leaves out is cleared");
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");

        using var read = await client.GetAsync(PayersApi.Payer(payer.Id));
        (await read.ReadAsAsync<PayerDto>()).ShouldBe(after);
    }

    [Theory]
    [InlineData("""{"name":"Changed","kind":"Insurer"}""")]
    [InlineData("""{"name":"Changed","patientId":"3f2504e0-4f89-11d3-9a0c-0305e82c3301"}""")]
    [InlineData("""{"name":"Changed","status":"Inactive"}""")]
    public async Task A_correction_cannot_change_the_kind_the_patient_link_or_the_status(string json)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var payer = await client.CreatePayerAsync(Guarantor("Rahul Sharma"));
        using var request = new HttpRequestMessage(HttpMethod.Patch, PayersApi.Payer(payer.Id)) { Content = PayersApi.RawJson(json) };
        request.Headers.TryAddWithoutValidation("If-Match", PayersApi.ETag(payer.Version));

        using var response = await client.SendAsync(request);

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task US12_a_payer_is_deactivated_and_reactivated_with_the_version_it_was_read_at()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var payer = await client.CreatePayerAsync(Guarantor("Rahul Sharma"));

        using var deactivated = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Payer(payer.Id, "deactivate"), body: null, PayersApi.ETag(payer.Version));
        var inactive = await deactivated.ReadAsAsync<PayerDto>();
        using var again = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Payer(payer.Id, "deactivate"), body: null, PayersApi.ETag(inactive.Version));
        using var reactivated = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Payer(payer.Id, "reactivate"), body: null, PayersApi.ETag(inactive.Version));

        inactive.Status.ShouldBe(PayerStatus.Inactive);
        await again.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PayersErrors.PayerAlreadyInactiveCode);
        (await reactivated.ReadAsAsync<PayerDto>()).Status.ShouldBe(PayerStatus.Active);
    }

    [Fact]
    public async Task Listing_payers_by_name_with_filters_for_the_patient_the_status_and_a_part_of_the_name()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "Viewer");
        var patient = await client.RegisterPatientAsync(TestData.NewPatient() with { FullName = "Priya Sharma" });
        var linked = await client.CreatePayerAsync(ForPatient(patient));
        var guarantor = await client.CreatePayerAsync(Guarantor("Anil Kapoor"));
        var other = await client.CreatePayerAsync(Guarantor("Meena Iyer"));
        using var deactivation = await client.SendWithIfMatchAsync(HttpMethod.Post, PayersApi.Payer(other.Id, "deactivate"), body: null, PayersApi.ETag(other.Version));
        deactivation.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ListAsync(client, string.Empty)).ShouldBe([guarantor.Id, other.Id, linked.Id]);
        (await ListAsync(client, $"patientId={patient.Id}")).ShouldBe([linked.Id]);
        (await ListAsync(client, "status=Inactive")).ShouldBe([other.Id]);
        (await ListAsync(client, "search=KAPOOR")).ShouldBe([guarantor.Id]);
    }

    private static async Task<Guid[]> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(query.Length == 0 ? PayersApi.Payers : PayersApi.Payers.WithQuery(query));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return [.. (await response.ReadAsAsync<PageOf<PayerDto>>()).Items.Select(payer => payer.Id)];
    }

    private static CreatePayerRequest ForPatient(PatientDto patient) => new()
    {
        Kind = PayerKind.SelfPay,
        PatientId = patient.Id,
        Name = patient.FullName,
        Phone = patient.Phone,
        Email = patient.Email,
    };

    private static CreatePayerRequest Guarantor(string name) => new() { Kind = PayerKind.SelfPay, Name = name };
}
