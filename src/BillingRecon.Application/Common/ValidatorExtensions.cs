using System.Text.Json;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Common;

/// <summary>Turns the outcome of a FluentValidation validator into the error a use case returns (ADR 0018).</summary>
public static class ValidatorExtensions
{
    /// <summary>
    /// Validates <paramref name="instance"/>. Null when it is valid; otherwise <c>validation_failed</c> with the messages
    /// per field. Field names are written as the API's JSON writes property names (<c>fullName</c>, <c>pageSize</c>),
    /// so a client finds each message under the name of the field it sent.
    /// </summary>
    public static async Task<Error?> GetErrorAsync<T>(this IValidator<T> validator, T instance, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var result = await validator.ValidateAsync(instance, ct);
        if (result.IsValid)
        {
            return null;
        }

        var fieldErrors = result.Errors
            .GroupBy(failure => FieldName(failure.PropertyName), StringComparer.Ordinal)
            .ToDictionary(
                field => field.Key,
                field => field.Select(failure => failure.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        return CommonErrors.ValidationFailed(fieldErrors);
    }

    // "Lines[0].UnitPrice" becomes "lines[0].unitPrice": each segment as the camel-case policy of the JSON writer has it.
    private static string FieldName(string propertyName)
        => string.Join('.', propertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}
