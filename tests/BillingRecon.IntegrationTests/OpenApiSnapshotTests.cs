using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// The OpenAPI document the frontend's typed client is generated from (ADR 0038, point 4) is the API's own: the copy
/// committed as <c>frontend/src/api/openapi.json</c> must equal the document the API serves in Development, so a change of
/// an endpoint fails this test until the copy is rewritten and the frontend's types are generated again. With the
/// environment variable <see cref="WriteSetting"/> set to <c>1</c>, the test rewrites the copy first (docs/runbook.md).
/// </summary>
public sealed class OpenApiSnapshotTests(OpenApiSnapshotTests.ShippedApiFactory factory) : IClassFixture<OpenApiSnapshotTests.ShippedApiFactory>
{
    /// <summary>The environment variable that makes the test rewrite the committed copy.</summary>
    public const string WriteSetting = "BILLINGRECON_WRITE_OPENAPI_SNAPSHOT";

    private static readonly string[] SnapshotPath = ["frontend", "src", "api", "openapi.json"];

    private static readonly JsonSerializerOptions Written = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task The_committed_OpenAPI_document_of_the_frontend_is_the_one_the_API_serves()
    {
        using var client = factory.CreateClient();
        var document = Normalised(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)));
        var path = Path.Combine([RepositoryRoot(), .. SnapshotPath]);

        if (Environment.GetEnvironmentVariable(WriteSetting) == "1")
        {
            await File.WriteAllTextAsync(path, document);
        }

        var committed = File.Exists(path) ? (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n") : null;

        (committed == document).ShouldBeTrue(
            $"frontend/src/api/openapi.json is not the API's OpenAPI document. Rewrite it with {WriteSetting}=1 and this test, "
            + "then run 'npm run api:types' in frontend/ (docs/runbook.md section 12).");
    }

    [Fact]
    public async Task The_document_names_no_test_only_endpoint_and_carries_enums_by_name()
    {
        using var client = factory.CreateClient();
        var document = JsonNode.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)))!;

        var paths = document["paths"]!.AsObject().Select(path => path.Key).ToList();
        paths.ShouldContain("/api/v1/auth/login");
        paths.ShouldNotContain(path => path.Contains("probe", StringComparison.OrdinalIgnoreCase));

        // Enums travel by name (docs/api.md section 1), and the frontend's types must say so.
        var schemas = document["components"]!["schemas"]!.AsObject();
        var paymentMethod = schemas.Single(schema => schema.Key == "PaymentMethod").Value!;
        paymentMethod["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ShouldContain("Cash");
    }

    // The document without what depends on the machine that served it, written as the frontend keeps it: no server address,
    // and the line breaks inside its descriptions as LF. The descriptions come from the XML comments of the source files,
    // with the line endings those files have in the checkout: a checkout with CRLF (as from a zip on Windows) and one with
    // LF (CI) must give the same document.
    private static string Normalised(string json)
    {
        var document = JsonNode.Parse(json.Replace("\\r\\n", "\\n", StringComparison.Ordinal))!.AsObject();
        document.Remove("servers");

        return document.ToJsonString(Written) + "\n";
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BillingRecon.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"BillingRecon.sln was not found above {AppContext.BaseDirectory}. This test reads and writes a file of the frontend and must run inside the repository.");
    }

    /// <summary>The API in Development as it ships: none of the test-only controllers the other hosts add.</summary>
    public sealed class ShippedApiFactory : ApiFactory
    {
        protected override string EnvironmentName => Environments.Development;

        protected override void ConfigureTestServices(IServiceCollection services)
        {
            // Nothing added: the document must be the shipped API's.
        }
    }
}
