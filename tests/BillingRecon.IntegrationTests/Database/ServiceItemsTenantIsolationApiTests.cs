using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Tenant isolation of the service catalogue at the API (BR-002, ADR 0009): a tenant cannot see or change another
/// tenant's items, even with their ids and ETags, and each tenant has its own codes. Every attempt answers 404, exactly
/// as for an id nobody has: a 403 would confirm to the other tenant that the item exists (docs/api.md).
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ServiceItemsTenantIsolationApiTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    [Fact]
    public async Task Tenant_B_reading_tenant_As_item_gets_404_as_for_an_id_nobody_has()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (asA, asB) = await TwoTenantsAsync(factory);
        var item = await asA.CreateServiceItemAsync();

        using var theirs = await asB.GetAsync(BillingApi.ServiceItem(item.Id));
        using var nobodys = await asB.GetAsync(BillingApi.ServiceItem(Guid.NewGuid()));

        var theirProblem = await theirs.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.ServiceItemNotFoundCode);
        var nobodysProblem = await nobodys.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.ServiceItemNotFoundCode);
        theirProblem.GetProperty("detail").GetString().ShouldBe(nobodysProblem.GetProperty("detail").GetString());
        theirProblem.GetProperty("title").GetString().ShouldBe(nobodysProblem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Tenant_B_cannot_correct_deactivate_or_reactivate_tenant_As_item_even_with_its_etag()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (asA, asB) = await TwoTenantsAsync(factory);
        var item = await asA.CreateServiceItemAsync();
        var etag = PayersApi.ETag(item.Version);
        var correction = new UpdateServiceItemRequest { Name = "Hijacked", DefaultUnitPrice = 0m, TaxRatePercent = 0m };

        using var patch = await asB.SendWithIfMatchAsync(HttpMethod.Patch, BillingApi.ServiceItem(item.Id), correction, etag);
        using var deactivate = await asB.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.ServiceItem(item.Id, "deactivate"), body: null, etag);
        using var reactivate = await asB.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.ServiceItem(item.Id, "reactivate"), body: null, etag);

        await patch.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.ServiceItemNotFoundCode);
        await deactivate.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.ServiceItemNotFoundCode);
        await reactivate.ShouldBeProblemAsync(HttpStatusCode.NotFound, BillingErrors.ServiceItemNotFoundCode);

        using var after = await asA.GetAsync(BillingApi.ServiceItem(item.Id));
        (await after.ReadAsAsync<ServiceItemDto>()).ShouldBe(item);
    }

    [Fact]
    public async Task Tenant_B_lists_none_of_tenant_As_items_even_when_it_searches_for_them()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (asA, asB) = await TwoTenantsAsync(factory);
        await asA.CreateServiceItemAsync(TestData.NewServiceItem() with { Code = "SECRET-1", Name = "Cardiac package" });

        using var all = await asB.GetAsync(BillingApi.ServiceItems);
        using var byCode = await asB.GetAsync(BillingApi.ServiceItems.WithQuery("search=SECRET-1"));
        using var byName = await asB.GetAsync(BillingApi.ServiceItems.WithQuery("search=Cardiac"));

        (await all.ReadAsAsync<PageOf<ServiceItemDto>>()).TotalCount.ShouldBe(0);
        (await byCode.ReadAsAsync<PageOf<ServiceItemDto>>()).TotalCount.ShouldBe(0);
        (await byName.ReadAsAsync<PageOf<ServiceItemDto>>()).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Two_tenants_may_each_have_an_item_with_the_same_code()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var (asA, asB) = await TwoTenantsAsync(factory);

        var inA = await asA.CreateServiceItemAsync(TestData.NewServiceItem() with { Code = "CONS-OPD" });
        var inB = await asB.CreateServiceItemAsync(TestData.NewServiceItem() with { Code = "CONS-OPD" });

        inB.Id.ShouldNotBe(inA.Id);
        using var list = await asB.GetAsync(BillingApi.ServiceItems);
        (await list.ReadAsAsync<PageOf<ServiceItemDto>>()).Items.ShouldHaveSingleItem().Id.ShouldBe(inB.Id);
    }

    private async Task<(HttpClient AsA, HttpClient AsB)> TwoTenantsAsync(DatabaseApiFactory factory)
        => (factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk"), factory.CreateClientFor(await NewApiTenantAsync(), "BillingClerk"));
}
