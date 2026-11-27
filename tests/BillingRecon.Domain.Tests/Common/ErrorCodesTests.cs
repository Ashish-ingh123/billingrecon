using System.Reflection;
using System.Text.RegularExpressions;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Domain.Tests.Common;

/// <summary>
/// Every module keeps its error codes as constants in a static <c>&lt;Module&gt;Errors</c> class in its own namespace,
/// one factory per failure (CLAUDE.md section 5). These tests keep the codes snake_case, unique across modules, and
/// each one backed by a factory that produces an error with that code.
/// </summary>
public sealed class ErrorCodesTests
{
    private static readonly Regex SnakeCase = new("^[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    private static readonly Type[] ErrorClasses =
    [
        .. typeof(Domain.AssemblyMarker).Assembly.GetTypes()
            .Where(type => type.IsClass && type.IsAbstract && type.IsSealed && type.Name.EndsWith("Errors", StringComparison.Ordinal)),
    ];

    [Fact]
    public void Every_errors_class_is_the_static_class_of_one_module()
    {
        ErrorClasses.ShouldNotBeEmpty();

        foreach (var errorClass in ErrorClasses)
        {
            var match = Regex.Match(errorClass.Namespace ?? string.Empty, @"^BillingRecon\.Domain\.(\w+)$");
            match.Success.ShouldBeTrue($"{errorClass.FullName} is not directly in a module namespace");
            errorClass.Name.ShouldBe(match.Groups[1].Value + "Errors");
        }
    }

    [Fact]
    public void Every_code_constant_ends_with_Code_and_holds_a_snake_case_value()
    {
        var codes = Codes().ToArray();

        codes.ShouldNotBeEmpty();
        foreach (var (owner, field) in codes)
        {
            field.Name.ShouldEndWith("Code", customMessage: $"{owner.Name}.{field.Name}");
            SnakeCase.IsMatch((string)field.GetRawConstantValue()!).ShouldBeTrue($"{owner.Name}.{field.Name} is not snake_case");
        }
    }

    [Fact]
    public void Codes_are_unique_across_modules()
    {
        var values = Codes().Select(code => (string)code.Field.GetRawConstantValue()!).ToArray();

        values.Distinct(StringComparer.Ordinal).Count().ShouldBe(values.Length);
    }

    [Fact]
    public void Every_code_has_a_factory_of_the_same_name_that_produces_an_error_with_that_code()
    {
        foreach (var (owner, field) in Codes())
        {
            var factoryName = field.Name[..^"Code".Length];
            var factory = owner.GetMethod(factoryName, BindingFlags.Public | BindingFlags.Static).ShouldNotBeNull($"{owner.Name} has no factory {factoryName}");
            factory.ReturnType.ShouldBe(typeof(Error), $"{owner.Name}.{factoryName} does not return an Error");

            var error = (Error)factory.Invoke(null, [.. factory.GetParameters().Select(SampleArgument)])!;

            error.Code.ShouldBe((string)field.GetRawConstantValue()!);
        }
    }

    [Fact]
    public void TenantInvalid_is_a_validation_error_that_carries_the_field_errors()
    {
        var error = IdentityErrors.TenantInvalid(new Dictionary<string, string[]> { ["code"] = ["Too long."] });

        error.Code.ShouldBe(IdentityErrors.TenantInvalidCode);
        error.Kind.ShouldBe(ErrorKind.Validation);
        error.FieldErrors["code"].ShouldBe(["Too long."]);
    }

    private static IEnumerable<(Type Owner, FieldInfo Field)> Codes()
        => ErrorClasses.SelectMany(owner => owner
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (owner, field)));

    // A value of each parameter type a factory takes today; a new kind of parameter extends this.
    private static object SampleArgument(ParameterInfo parameter)
        => parameter.ParameterType switch
        {
            var type when type == typeof(string) => "sample",
            var type when type == typeof(IReadOnlyDictionary<string, string[]>) => new Dictionary<string, string[]>(),
            var type when type == typeof(decimal) => 0m,
            var type when type == typeof(int) => 0,
            var type when type == typeof(bool) => false,
            var type when type == typeof(DateOnly) => new DateOnly(2026, 11, 20),
            var type when type == typeof(DateTimeOffset) => new DateTimeOffset(2026, 11, 28, 6, 30, 0, TimeSpan.Zero),
            var type when type == typeof(Money) => Money.Of(100m),
            var type when type == typeof(BillingRecon.Domain.Identity.TenantStatus) => BillingRecon.Domain.Identity.TenantStatus.Suspended,
            var type => throw new NotSupportedException($"No sample value for a {type.Name} parameter; add one here."),
        };
}
