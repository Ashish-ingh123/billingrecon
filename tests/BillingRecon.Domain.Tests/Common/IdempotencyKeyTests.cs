using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Common;

/// <summary>The record of an idempotency key (BR-803, ADR 0006): reserved in progress, completed once, kept for thirty days.</summary>
public sealed class IdempotencyKeyTests
{
    private static readonly Guid Key = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
    private static readonly UserId Cashier = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3302"));
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 6, 30, 0, TimeSpan.Zero);
    private static readonly byte[] Hash = [.. Enumerable.Range(1, 32).Select(value => (byte)value)];

    [Fact]
    public void A_reservation_is_in_progress_has_no_answer_and_expires_thirty_days_later()
    {
        var key = IdempotencyKey.Reserve(Key, Cashier, "POST", "/api/v1/payments", Hash, Now);

        key.Key.ShouldBe(Key);
        key.UserId.ShouldBe(Cashier);
        key.Method.ShouldBe("POST");
        key.Path.ShouldBe("/api/v1/payments");
        key.RequestHash.ShouldBe(Hash);
        key.Status.ShouldBe(IdempotencyStatus.InProgress);
        key.StatusCode.ShouldBeNull();
        key.ResponseBody.ShouldBeNull();
        key.CompletedAtUtc.ShouldBeNull();
        key.CreatedAtUtc.ShouldBe(Now);
        key.ExpiresAtUtc.ShouldBe(Now.AddDays(30));
        key.TenantId.ShouldBe(default, "the tenant is set when the row is saved, never by the caller");
    }

    [Fact]
    public void The_fingerprint_is_copied_and_compared_byte_by_byte()
    {
        var mine = (byte[])Hash.Clone();
        var key = IdempotencyKey.Reserve(Key, userId: null, "POST", "/api/v1/payments", mine, Now);
        mine[0] = 99;

        key.IsFor(Hash).ShouldBeTrue("changing the caller's array afterwards does not change the stored fingerprint");
        key.IsFor(mine).ShouldBeFalse();
        key.IsFor(Hash.AsSpan(0, 31)).ShouldBeFalse();
        key.UserId.ShouldBeNull();
    }

    [Fact]
    public void Completing_stores_the_answer_once_and_a_second_answer_is_a_bug()
    {
        var key = IdempotencyKey.Reserve(Key, Cashier, "POST", "/api/v1/payments", Hash, Now);

        key.Complete(201, "application/json; charset=utf-8", """{"Location":"/api/v1/payments/1"}""", """{"id":1}""", Now.AddSeconds(2));

        key.Status.ShouldBe(IdempotencyStatus.Completed);
        key.StatusCode.ShouldBe(201);
        key.ResponseContentType.ShouldBe("application/json; charset=utf-8");
        key.ResponseHeaders.ShouldBe("""{"Location":"/api/v1/payments/1"}""");
        key.ResponseBody.ShouldBe("""{"id":1}""");
        key.CompletedAtUtc.ShouldBe(Now.AddSeconds(2));
        key.ExpiresAtUtc.ShouldBe(Now.AddDays(30), "completing does not extend the life of the key");

        Should.Throw<InvalidOperationException>(() => key.Complete(200, null, null, "other", Now.AddSeconds(3)));
        key.ResponseBody.ShouldBe("""{"id":1}""");
    }

    [Fact]
    public void An_answer_without_a_body_is_stored_as_an_empty_one()
    {
        var key = IdempotencyKey.Reserve(Key, Cashier, "POST", "/api/v1/payments", Hash, Now);

        key.Complete(204, contentType: null, headers: null, body: string.Empty, Now);

        key.ResponseBody.ShouldBe(string.Empty);
        key.ResponseContentType.ShouldBeNull();
    }

    [Fact]
    public void A_long_path_and_content_type_are_cut_for_storage_and_headers_that_do_not_fit_are_a_bug()
    {
        var key = IdempotencyKey.Reserve(Key, Cashier, "POST", "/" + new string('p', 500), Hash, Now);

        key.Path.Length.ShouldBe(IdempotencyKey.PathMaxLength);
        Should.Throw<ArgumentException>(() => key.Complete(200, null, new string('h', IdempotencyKey.ResponseHeadersMaxLength + 1), "body", Now));
        key.Status.ShouldBe(IdempotencyStatus.InProgress);

        key.Complete(200, new string('c', 150), null, "body", Now);
        key.ResponseContentType.ShouldNotBeNull().Length.ShouldBe(IdempotencyKey.ResponseContentTypeMaxLength);
    }

    [Fact]
    public void The_empty_uuid_a_blank_method_and_a_hash_that_is_not_sha256_are_bugs_and_throw()
    {
        Should.Throw<ArgumentException>(() => IdempotencyKey.Reserve(Guid.Empty, Cashier, "POST", "/x", Hash, Now)).ParamName.ShouldBe("key");
        Should.Throw<ArgumentException>(() => IdempotencyKey.Reserve(Key, Cashier, " ", "/x", Hash, Now));
        Should.Throw<ArgumentException>(() => IdempotencyKey.Reserve(Key, Cashier, "POST", "/x", [1, 2, 3], Now)).ParamName.ShouldBe("requestHash");
    }
}
