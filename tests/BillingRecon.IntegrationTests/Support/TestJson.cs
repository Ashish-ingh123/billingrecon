using System.Text.Json;
using System.Text.Json.Serialization;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>The JSON conventions of the API, for the bodies the tests send and read: web defaults (camel case) and enums by name.</summary>
internal static class TestJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
