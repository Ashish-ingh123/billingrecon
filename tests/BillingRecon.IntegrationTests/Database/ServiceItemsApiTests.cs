using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.IntegrationTests.Support;
using Serilog.Events;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The service catalogue endpoints end to end, on the pipeline that ships and a real SQL Server (docs/domain-model.md
/// section 4.3; BR-201, BR-804): a clerk maintains the catalogue through the API.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ServiceItemsApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    [Fact]
    public async Task A_new_item_answers_201_active_with_its_address_and_version_and_reads_back_the_same()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");

        using var response = await client.PostJsonAsync(BillingApi.ServiceItems, Consultation());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var item = await response.ReadAsAsync<ServiceItemDto>();
        item.Code.ShouldBe("CONS-OPD");
        item.Name.ShouldBe("OPD consultation");
        item.Category.ShouldBe("Consultation");
        item.DefaultUnitPrice.ShouldBe(500m);
        item.TaxRatePercent.ShouldBe(18m);
        item.Status.ShouldBe(ServiceItemStatus.Active);
        response.Headers.Location.ShouldNotBeNull().ToString().ShouldEndWith($"/api/v1/service-items/{item.Id}");
        response.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(item.Version));

        using var read = await client.GetAsync(BillingApi.ServiceItem(item.Id));
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(item.Version));
        (await read.ReadAsAsync<ServiceItemDto>()).ShouldBe(item);
    }

    [Fact]
    public async Task BR201_a_price_with_four_decimals_and_a_tax_rate_with_two_are_stored_exactly()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");

        var created = await client.CreateServiceItemAsync(Consultation() with { DefaultUnitPrice = 333.3333m, TaxRatePercent = 2.25m });

        using var read = await client.GetAsync(BillingApi.ServiceItem(created.Id));
        var stored = await read.ReadAsAsync<ServiceItemDto>();
        stored.DefaultUnitPrice.ShouldBe(333.3333m);
        stored.TaxRatePercent.ShouldBe(2.25m);
    }

    [Fact]
    public async Task A_free_untaxed_service_is_a_valid_item()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");

        var item = await client.CreateServiceItemAsync(Consultation() with { DefaultUnitPrice = 0m, TaxRatePercent = 0m });

        item.DefaultUnitPrice.ShouldBe(0m);
        item.TaxRatePercent.ShouldBe(0m);
    }

    [Theory]
    [InlineData("CONS-OPD")]
    [InlineData("cons-opd")]
    [InlineData(" Cons-Opd ")]
    public async Task A_code_the_tenant_already_has_answers_409_whatever_its_case(string code)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        await client.CreateServiceItemAsync(Consultation());

        using var response = await client.PostJsonAsync(BillingApi.ServiceItems, Consultation() with { Code = code, Name = "Another service" });

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, BillingErrors.ServiceItemCodeDuplicateCode);
    }

    [Fact]
    public async Task The_code_of_an_inactive_item_stays_taken()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var item = await client.CreateServiceItemAsync(Consultation());
        using var deactivation = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.ServiceItem(item.Id, "deactivate"), body: null, PayersApi.ETag(item.Version));
        deactivation.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var response = await client.PostJsonAsync(BillingApi.ServiceItems, Consultation());

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, BillingErrors.ServiceItemCodeDuplicateCode);
    }

    [Fact]
    public async Task Of_ten_simultaneous_items_with_one_code_exactly_one_is_created()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.PostJsonAsync(BillingApi.ServiceItems, Consultation())));

        try
        {
            responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
            foreach (var refused in responses.Where(response => response.StatusCode != HttpStatusCode.Created))
            {
                await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, BillingErrors.ServiceItemCodeDuplicateCode);
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

    [Fact]
    public async Task An_item_without_a_code_a_name_a_price_and_a_tax_rate_answers_400_with_each_field()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");

        using var response = await client.PostJsonAsync(BillingApi.ServiceItems, new CreateServiceItemRequest());

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        var errors = problem.GetProperty("errors");
        errors.TryGetProperty("code", out _).ShouldBeTrue();
        errors.TryGetProperty("name", out _).ShouldBeTrue();
        errors.TryGetProperty("defaultUnitPrice", out _).ShouldBeTrue();
        errors.TryGetProperty("taxRatePercent", out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"code":"X-1","name":"X","defaultUnitPrice":-1,"taxRatePercent":0}""", "defaultUnitPrice")]
    [InlineData("""{"code":"X-1","name":"X","defaultUnitPrice":1.00001,"taxRatePercent":0}""", "defaultUnitPrice")]
    [InlineData("""{"code":"X-1","name":"X","defaultUnitPrice":1,"taxRatePercent":100.01}""", "taxRatePercent")]
    [InlineData("""{"code":"X-1","name":"X","defaultUnitPrice":1,"taxRatePercent":12.345}""", "taxRatePercent")]
    [InlineData("""{"code":"X 1","name":"X","defaultUnitPrice":1,"taxRatePercent":0}""", "code")]
    public async Task BR201_a_number_or_a_code_out_of_its_rules_answers_400_naming_the_field(string json, string field)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        using var content = PayersApi.RawJson(json);

        using var response = await client.PostAsync(BillingApi.ServiceItems, content);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        problem.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Correcting_an_item_with_its_etag_replaces_its_details_and_without_one_or_from_a_stale_one_changes_nothing()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var item = await client.CreateServiceItemAsync(Consultation());
        var correction = new UpdateServiceItemRequest { Name = "OPD consultation (senior)", DefaultUnitPrice = 750.5m, TaxRatePercent = 5m };

        using var withoutIfMatch = await client.SendWithIfMatchAsync(HttpMethod.Patch, BillingApi.ServiceItem(item.Id), correction, ifMatch: null);
        using var corrected = await client.SendWithIfMatchAsync(HttpMethod.Patch, BillingApi.ServiceItem(item.Id), correction, PayersApi.ETag(item.Version));
        using var stale = await client.SendWithIfMatchAsync(HttpMethod.Patch, BillingApi.ServiceItem(item.Id), correction with { Name = "Too late" }, PayersApi.ETag(item.Version));

        await withoutIfMatch.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await corrected.ReadAsAsync<ServiceItemDto>();
        after.Name.ShouldBe("OPD consultation (senior)");
        after.DefaultUnitPrice.ShouldBe(750.5m);
        after.TaxRatePercent.ShouldBe(5m);
        after.Category.ShouldBeNull("a category the correction leaves out is cleared");
        after.Code.ShouldBe("CONS-OPD");
        after.Version.ShouldNotBe(item.Version);
        corrected.Headers.ETag.ShouldNotBeNull().Tag.ShouldBe(PayersApi.ETag(after.Version));
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");

        using var read = await client.GetAsync(BillingApi.ServiceItem(item.Id));
        (await read.ReadAsAsync<ServiceItemDto>()).ShouldBe(after);
    }

    [Theory]
    [InlineData("""{"name":"Changed","defaultUnitPrice":1,"taxRatePercent":0,"code":"OTHER"}""")]
    [InlineData("""{"name":"Changed","defaultUnitPrice":1,"taxRatePercent":0,"status":"Inactive"}""")]
    [InlineData("""{"name":"Changed","defaultUnitPrice":1,"taxRatePercent":0,"id":"3f2504e0-4f89-11d3-9a0c-0305e82c3301"}""")]
    public async Task A_correction_cannot_change_the_code_the_status_or_the_id(string json)
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var item = await client.CreateServiceItemAsync(Consultation());
        using var request = new HttpRequestMessage(HttpMethod.Patch, BillingApi.ServiceItem(item.Id)) { Content = PayersApi.RawJson(json) };
        request.Headers.TryAddWithoutValidation("If-Match", PayersApi.ETag(item.Version));

        using var response = await client.SendAsync(request);

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        using var read = await client.GetAsync(BillingApi.ServiceItem(item.Id));
        (await read.ReadAsAsync<ServiceItemDto>()).ShouldBe(item);
    }

    [Fact]
    public async Task An_item_is_deactivated_and_reactivated_with_the_version_it_was_read_at()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var item = await client.CreateServiceItemAsync(Consultation());

        using var withoutIfMatch = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.ServiceItem(item.Id, "deactivate"), body: null, ifMatch: null);
        using var deactivated = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.ServiceItem(item.Id, "deactivate"), body: null, PayersApi.ETag(item.Version));
        var inactive = await deactivated.ReadAsAsync<ServiceItemDto>();
        using var stale = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.ServiceItem(item.Id, "reactivate"), body: null, PayersApi.ETag(item.Version));
        using var again = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.ServiceItem(item.Id, "deactivate"), body: null, PayersApi.ETag(inactive.Version));
        using var reactivated = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.ServiceItem(item.Id, "reactivate"), body: null, PayersApi.ETag(inactive.Version));
        var active = await reactivated.ReadAsAsync<ServiceItemDto>();
        using var onceMore = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.ServiceItem(item.Id, "reactivate"), body: null, PayersApi.ETag(active.Version));

        await withoutIfMatch.ShouldBeProblemAsync(HttpStatusCode.PreconditionRequired, "precondition_required");
        inactive.Status.ShouldBe(ServiceItemStatus.Inactive);
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");
        await again.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.ServiceItemAlreadyInactiveCode);
        active.Status.ShouldBe(ServiceItemStatus.Active);
        await onceMore.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, BillingErrors.ServiceItemAlreadyActiveCode);
    }

    [Fact]
    public async Task An_item_cannot_be_deleted()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk");
        var item = await client.CreateServiceItemAsync(Consultation());

        using var response = await client.DeleteAsync(BillingApi.ServiceItem(item.Id));

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        using var read = await client.GetAsync(BillingApi.ServiceItem(item.Id));
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_item_nobody_has_answers_404_service_item_not_found()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "Viewer");

        using var response = await client.GetAsync(BillingApi.ServiceItem(Guid.NewGuid()));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.ServiceItemNotFoundCode);
    }

    [Fact]
    public async Task Listing_the_catalogue_by_code_with_filters_for_the_status_and_a_part_of_the_code_or_the_name()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "Viewer");
        var xray = await client.CreateServiceItemAsync(Item("RAD/XRAY-CHEST", "Chest X-ray"));
        var consultation = await client.CreateServiceItemAsync(Item("CONS-OPD", "OPD consultation"));
        var bloodCount = await client.CreateServiceItemAsync(Item("LAB/CBC", "Complete blood count"));
        using var deactivation = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.ServiceItem(bloodCount.Id, "deactivate"), body: null, PayersApi.ETag(bloodCount.Version));
        deactivation.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ListAsync(client, string.Empty)).ShouldBe([consultation.Id, bloodCount.Id, xray.Id]);
        (await ListAsync(client, "status=Active")).ShouldBe([consultation.Id, xray.Id]);
        (await ListAsync(client, "status=Inactive")).ShouldBe([bloodCount.Id]);
        (await ListAsync(client, "search=lab/")).ShouldBe([bloodCount.Id]);
        (await ListAsync(client, "search=CONSULT")).ShouldBe([consultation.Id]);
        (await ListAsync(client, "search=c")).ShouldBe([consultation.Id, bloodCount.Id, xray.Id]);
        (await ListAsync(client, "search=x-ray&status=Inactive")).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_search_text_is_data_and_never_a_pattern()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "Viewer");
        var percent = await client.CreateServiceItemAsync(Item("OXY-1", "Oxygen 100% per hour"));
        await client.CreateServiceItemAsync(Item("OXY-2", "Oxygen 1000 litres"));
        var underscore = await client.CreateServiceItemAsync(Item("BED_1", "Bed charge"));
        await client.CreateServiceItemAsync(Item("BEDA1", "Bed charge, annex"));
        var bracket = await client.CreateServiceItemAsync(Item("MISC-1", "Dressing [large]"));

        (await ListAsync(client, "search=100%25")).ShouldBe([percent.Id]);
        (await ListAsync(client, "search=BED_1")).ShouldBe([underscore.Id]);
        (await ListAsync(client, "search=%5Blarge%5D")).ShouldBe([bracket.Id]);
        (await ListAsync(client, "search=%25")).ShouldBe([percent.Id]);
    }

    [Fact]
    public async Task The_catalogue_is_listed_one_page_at_a_time_with_the_total_count()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClientFor(await NewApiTenantAsync(), "Viewer");
        var created = new List<ServiceItemDto>();
        foreach (var number in Enumerable.Range(1, 5))
        {
            created.Add(await client.CreateServiceItemAsync(Item($"P-{number}", $"Procedure {number}")));
        }

        using var second = await client.GetAsync(BillingApi.ServiceItems.WithQuery("page=2&pageSize=2"));
        using var tooLarge = await client.GetAsync(BillingApi.ServiceItems.WithQuery("pageSize=101"));

        var page = await second.ReadAsAsync<PageOf<ServiceItemDto>>();
        page.Items.Select(item => item.Code).ShouldBe(["P-3", "P-4"]);
        page.Page.ShouldBe(2);
        page.PageSize.ShouldBe(2);
        page.TotalCount.ShouldBe(created.Count);
        await tooLarge.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    private static async Task<Guid[]> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(query.Length == 0 ? BillingApi.ServiceItems : BillingApi.ServiceItems.WithQuery(query));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return [.. (await response.ReadAsAsync<PageOf<ServiceItemDto>>()).Items.Select(item => item.Id)];
    }

    private static CreateServiceItemRequest Consultation() => new()
    {
        Code = "CONS-OPD",
        Name = "OPD consultation",
        Category = "Consultation",
        DefaultUnitPrice = 500m,
        TaxRatePercent = 18m,
    };

    private static CreateServiceItemRequest Item(string code, string name) => new()
    {
        Code = code,
        Name = name,
        DefaultUnitPrice = 100m,
        TaxRatePercent = 5m,
    };
}
