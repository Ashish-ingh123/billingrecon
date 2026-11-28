using System.Net.Http.Json;
using System.Xml.Linq;
using BillingRecon.Api.Common;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests;

public sealed class SystemEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task System_info_returns_product_name_version_and_environment()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());

        var info = await client.GetFromJsonAsync<SystemInfoResponse>(new Uri("/api/v1/system/info", UriKind.Relative));

        var body = info.ShouldNotBeNull();
        body.Application.ShouldBe("BillingRecon");
        body.Version.ShouldNotBeNullOrWhiteSpace();
        body.Environment.ShouldBe("Production");
    }

    /// <summary>
    /// B26: the version is the release set in Directory.Build.props, not the SDK's default 1.0.0. The build may add
    /// "+" and the commit after it.
    /// </summary>
    [Fact]
    public async Task System_info_reports_the_release_set_in_the_build()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());

        var info = await client.GetFromJsonAsync<SystemInfoResponse>(new Uri("/api/v1/system/info", UriKind.Relative));

        var reported = info.ShouldNotBeNull().Version.Split('+')[0];
        reported.ShouldBe(ReleaseInBuildProps());
    }

    private static string ReleaseInBuildProps()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BillingRecon.sln")))
        {
            directory = directory.Parent;
        }

        var props = Path.Combine(directory.ShouldNotBeNull().FullName, "Directory.Build.props");
        var version = XDocument.Load(props).Descendants("Version").Single().Value.Trim();
        version.ShouldNotBe("1.0.0");
        return version;
    }
}
