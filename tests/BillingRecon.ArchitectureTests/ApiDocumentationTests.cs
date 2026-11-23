using System.Reflection;
using System.Text.RegularExpressions;
using BillingRecon.Api.Common.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace BillingRecon.ArchitectureTests;

/// <summary>
/// docs/api.md is complete (Prompt 6.2): every endpoint of every controller appears there as its method and path (route
/// constraints left out, <c>GET /api/v1/bills/{id}</c>), and every error code the API can answer appears there in
/// backticks: the business codes of every <c>Errors</c> class of the Domain and the codes the API makes itself. A new
/// endpoint or code fails here until it is documented.
/// </summary>
public sealed partial class ApiDocumentationTests
{
    private static readonly string Documentation = File.ReadAllText(RepositoryFiles.PathOf("docs/api.md"));

    [Fact]
    public void Every_endpoint_of_the_api_is_documented_by_its_method_and_path()
    {
        var endpoints = Endpoints().ToList();
        endpoints.ShouldNotBeEmpty();

        endpoints.Where(endpoint => !Documentation.Contains(endpoint, StringComparison.Ordinal)).ShouldBeEmpty("endpoints missing from docs/api.md");
    }

    [Fact]
    public void Every_error_code_the_api_can_answer_is_in_the_catalogue()
    {
        var codes = ErrorCodesOfTheApi().ToList();
        codes.Count.ShouldBeGreaterThan(50);

        codes.Where(code => !Documentation.Contains($"`{code}`", StringComparison.Ordinal)).ShouldBeEmpty("error codes missing from docs/api.md");
    }

    // "GET /api/v1/bills/{id}" for every action, from the route of its controller and its own template.
    private static IEnumerable<string> Endpoints()
    {
        foreach (var controller in Assemblies.Api.GetTypes().Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract))
        {
            var prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var http in action.GetCustomAttributes<HttpMethodAttribute>())
                {
                    var template = string.IsNullOrEmpty(http.Template) ? prefix : $"{prefix}/{http.Template}";
                    foreach (var method in http.HttpMethods)
                    {
                        yield return $"{method} /{Constraint().Replace(template, "{$1}")}";
                    }
                }
            }
        }
    }

    // The constants of every Domain class named ...Errors whose name ends in Code, and those of the API's own ErrorCodes.
    private static IEnumerable<string> ErrorCodesOfTheApi()
    {
        var domain = Assemblies.Domain.GetTypes()
            .Where(type => type.IsClass && type.IsAbstract && type.IsSealed && type.Name.EndsWith("Errors", StringComparison.Ordinal))
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string) && field.Name.EndsWith("Code", StringComparison.Ordinal));
        var api = typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string));

        return domain.Concat(api).Select(field => (string)field.GetRawConstantValue()!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
    }

    // "{id:guid}" becomes "{id}".
    [GeneratedRegex(@"\{(\w+):[^}]+\}")]
    private static partial Regex Constraint();
}
