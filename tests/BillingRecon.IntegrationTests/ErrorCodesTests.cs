using BillingRecon.Api.Common.Errors;
using BillingRecon.Domain.Common;

namespace BillingRecon.IntegrationTests;

/// <summary>The default error codes are API contract (ADR 0011), so the whole table is pinned here.</summary>
public sealed class ErrorCodesTests
{
    [Theory]
    [InlineData(400, false, "bad_request")]
    [InlineData(400, true, "validation_failed")]
    [InlineData(401, false, "unauthorized")]
    [InlineData(403, false, "forbidden")]
    [InlineData(404, false, "not_found")]
    [InlineData(405, false, "method_not_allowed")]
    [InlineData(406, false, "not_acceptable")]
    [InlineData(408, false, "request_timeout")]
    [InlineData(409, false, "conflict")]
    [InlineData(413, false, "payload_too_large")]
    [InlineData(415, false, "unsupported_media_type")]
    [InlineData(422, false, "unprocessable_entity")]
    [InlineData(422, true, "unprocessable_entity")]
    [InlineData(428, false, "precondition_required")]
    [InlineData(429, false, "too_many_requests")]
    [InlineData(500, false, "internal_error")]
    [InlineData(503, false, "service_unavailable")]
    [InlineData(418, false, "http_418")]
    [InlineData(504, false, "http_504")]
    public void Each_status_has_its_code(int statusCode, bool hasFieldErrors, string expected)
    {
        var code = ErrorCodes.ForStatus(statusCode, hasFieldErrors);

        code.ShouldBe(expected);

        // Every default code obeys the same snake_case rule as a business error code.
        Should.NotThrow(() => new Error(code, "Any message.", ErrorKind.Conflict));
    }
}
