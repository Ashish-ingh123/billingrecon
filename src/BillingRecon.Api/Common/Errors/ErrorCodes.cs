using System.Globalization;

namespace BillingRecon.Api.Common.Errors;

/// <summary>
/// The <c>errorCode</c> values the API itself produces for failures that no use case reported: routing, model binding,
/// content negotiation and unhandled faults. A business failure carries its own code
/// (<see cref="BillingRecon.Domain.Common.Error.Code"/>).
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string BadRequest = "bad_request";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string NotAcceptable = "not_acceptable";
    public const string RequestTimeout = "request_timeout";
    public const string Conflict = "conflict";
    public const string PayloadTooLarge = "payload_too_large";
    public const string UnsupportedMediaType = "unsupported_media_type";
    public const string UnprocessableEntity = "unprocessable_entity";
    public const string PreconditionRequired = "precondition_required";
    public const string TooManyRequests = "too_many_requests";
    public const string InternalError = "internal_error";
    public const string ServiceUnavailable = "service_unavailable";

    /// <summary>The code for a response that has only an HTTP status to go by.</summary>
    /// <param name="statusCode">The HTTP status of the response.</param>
    /// <param name="hasFieldErrors">True when the response lists errors per field.</param>
    public static string ForStatus(int statusCode, bool hasFieldErrors) => statusCode switch
    {
        StatusCodes.Status400BadRequest when hasFieldErrors => ValidationFailed,
        StatusCodes.Status400BadRequest => BadRequest,
        StatusCodes.Status401Unauthorized => Unauthorized,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status406NotAcceptable => NotAcceptable,
        StatusCodes.Status408RequestTimeout => RequestTimeout,
        StatusCodes.Status409Conflict => Conflict,
        StatusCodes.Status413PayloadTooLarge => PayloadTooLarge,
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaType,
        StatusCodes.Status422UnprocessableEntity => UnprocessableEntity,
        StatusCodes.Status428PreconditionRequired => PreconditionRequired,
        StatusCodes.Status429TooManyRequests => TooManyRequests,
        StatusCodes.Status500InternalServerError => InternalError,
        StatusCodes.Status503ServiceUnavailable => ServiceUnavailable,
        _ => string.Create(CultureInfo.InvariantCulture, $"http_{statusCode}"),
    };
}
