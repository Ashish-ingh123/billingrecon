using System.Text;
using BillingRecon.Api.Common.Idempotency;
using Microsoft.Extensions.Primitives;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// The two small rules the idempotency filter is built on (BR-803, ADR 0006), without a host: what counts as a key,
/// and what makes two requests the same request.
/// </summary>
public sealed class IdempotencyContractTests
{
    private const string Uuid = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"amount":5000.00}""");

    [Theory]
    [InlineData(Uuid)]
    [InlineData("3F2504E0-4F89-11D3-9A0C-0305E82C3301")] // capitals
    [InlineData("  " + Uuid + " ")] // white space around it
    public void One_uuid_is_a_key(string header)
    {
        IdempotencyHeaders.TryParseKey(new StringValues(header), out var key).ShouldBeTrue();

        key.ShouldBe(Guid.Parse(Uuid));
    }

    [Theory]
    [InlineData("")]
    [InlineData("payment-42")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("3f2504e04f8911d39a0c0305e82c3301")]
    [InlineData("{" + Uuid + "}")]
    [InlineData("(" + Uuid + ")")]
    [InlineData(Uuid + "," + Uuid)]
    public void Anything_else_is_not_a_key(string header)
    {
        IdempotencyHeaders.TryParseKey(new StringValues(header), out var key).ShouldBeFalse();

        key.ShouldBeNull();
    }

    [Fact]
    public void No_header_and_two_headers_are_not_a_key()
    {
        IdempotencyHeaders.TryParseKey(StringValues.Empty, out _).ShouldBeFalse();
        IdempotencyHeaders.TryParseKey(new StringValues([Uuid, Uuid]), out _).ShouldBeFalse();
        IdempotencyHeaders.TryParseKey(new StringValues((string?)null), out _).ShouldBeFalse();
    }

    [Fact]
    public void The_fingerprint_is_a_sha256_hash_and_the_same_request_always_has_the_same_one()
    {
        var first = RequestFingerprint.Compute("POST", "/api/v1/payments", ifMatch: null, Body);
        var second = RequestFingerprint.Compute("POST", "/api/v1/payments", ifMatch: null, [.. Body]);

        first.Length.ShouldBe(32);
        second.ShouldBe(first);
        RequestFingerprint.Compute("post", "/api/v1/payments", ifMatch: string.Empty, Body).ShouldBe(first, "the case of the method and an absent If-Match do not matter");
    }

    [Fact]
    public void Another_method_path_query_version_or_body_is_another_request()
    {
        var original = RequestFingerprint.Compute("POST", "/api/v1/bills/1/void", "\"AAAAAAAAB9E=\"", Body);

        byte[][] others =
        [
            RequestFingerprint.Compute("PUT", "/api/v1/bills/1/void", "\"AAAAAAAAB9E=\"", Body),
            RequestFingerprint.Compute("POST", "/api/v1/bills/2/void", "\"AAAAAAAAB9E=\"", Body),
            RequestFingerprint.Compute("POST", "/api/v1/bills/1/void?force=true", "\"AAAAAAAAB9E=\"", Body),
            RequestFingerprint.Compute("POST", "/api/v1/bills/1/void", "\"AAAAAAAAB9I=\"", Body),
            RequestFingerprint.Compute("POST", "/api/v1/bills/1/void", ifMatch: null, Body),
            RequestFingerprint.Compute("POST", "/api/v1/bills/1/void", "\"AAAAAAAAB9E=\"", Encoding.UTF8.GetBytes("""{"amount":5000.01}""")),
            RequestFingerprint.Compute("POST", "/api/v1/bills/1/void", "\"AAAAAAAAB9E=\"", []),
        ];

        foreach (var other in others)
        {
            other.ShouldNotBe(original);
        }

        others.Select(Convert.ToHexString).Distinct().Count().ShouldBe(others.Length);
    }

    [Fact]
    public void The_parts_cannot_be_shifted_into_one_another()
    {
        // The same characters, divided differently between the path, the version and the body.
        var one = RequestFingerprint.Compute("POST", "/a", "b", Encoding.UTF8.GetBytes("c"));
        var two = RequestFingerprint.Compute("POST", "/ab", string.Empty, Encoding.UTF8.GetBytes("c"));
        var three = RequestFingerprint.Compute("POST", "/a", string.Empty, Encoding.UTF8.GetBytes("bc"));

        one.ShouldNotBe(two);
        one.ShouldNotBe(three);
        two.ShouldNotBe(three);
    }

    [Fact]
    public void Whitespace_in_a_json_body_makes_another_request_because_the_bytes_are_what_is_compared()
    {
        var compact = RequestFingerprint.Compute("POST", "/api/v1/payments", null, Encoding.UTF8.GetBytes("""{"amount":5000.00}"""));
        var spaced = RequestFingerprint.Compute("POST", "/api/v1/payments", null, Encoding.UTF8.GetBytes("""{ "amount": 5000.00 }"""));

        // A client retries by sending the same bytes again; it does not re-serialise (docs/api.md, section 7).
        spaced.ShouldNotBe(compact);
    }
}
