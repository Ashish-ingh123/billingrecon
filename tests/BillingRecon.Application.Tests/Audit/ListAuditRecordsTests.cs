using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Audit;
using BillingRecon.Domain.Common;
using NSubstitute;

namespace BillingRecon.Application.Tests.Audit;

/// <summary>
/// Reading an audit trail (US-43): the shape of the query, the handler that refuses a malformed one before it reads,
/// and the mapping of a record to what the API returns.
/// </summary>
public sealed class ListAuditRecordsTests
{
    private static readonly Guid Entity = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3306");

    private readonly ListAuditRecordsQueryValidator _validator = new();

    [Fact]
    public async Task A_query_for_the_trail_of_one_record_is_valid()
    {
        (await _validator.GetErrorAsync(Query(), CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AnEntityTypeThatIsLongerThanTheFiftyCharactersOfItsColumn")]
    public async Task A_missing_or_too_long_entity_type_is_refused(string entityType)
    {
        var error = (await _validator.GetErrorAsync(Query() with { EntityType = entityType }, CancellationToken.None)).ShouldNotBeNull();

        error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        error.FieldErrors.Keys.ShouldBe(["entityType"]);
    }

    [Fact]
    public async Task A_missing_entity_id_and_a_page_outside_the_limits_are_refused()
    {
        var error = (await _validator.GetErrorAsync(Query() with { EntityId = Guid.Empty, PageSize = Paging.MaxPageSize + 1 }, CancellationToken.None)).ShouldNotBeNull();

        error.FieldErrors.Keys.ShouldBe(["pageSize", "entityId"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_handler_refuses_a_malformed_query_before_it_reads_anything()
    {
        var audit = Substitute.For<IAuditReadService>();
        var handler = new ListAuditRecordsHandler(_validator, audit);

        var result = await handler.HandleAsync(Query() with { EntityId = Guid.Empty }, CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        await audit.DidNotReceiveWithAnyArgs().ListAsync(default!, default);
    }

    [Fact]
    public async Task The_handler_returns_the_page_the_read_service_answers()
    {
        var page = new PageOf<AuditRecordDto>([], 1, 20, 0);
        var audit = Substitute.For<IAuditReadService>();
        audit.ListAsync(Arg.Any<ListAuditRecordsQuery>(), Arg.Any<CancellationToken>()).Returns(page);

        var result = await new ListAuditRecordsHandler(_validator, audit).HandleAsync(Query(), CancellationToken.None);

        result.Value.ShouldBeSameAs(page);
    }

    [Fact]
    public void A_record_is_mapped_with_its_images_as_json_objects()
    {
        var at = new DateTimeOffset(2026, 11, 20, 6, 30, 0, TimeSpan.Zero);
        var user = new UserId(Guid.NewGuid());
        var correlation = Guid.NewGuid();
        var record = AuditRecord.Create(at, user, correlation, "Bill", Entity, "Voided", """{"Status":"Draft"}""", """{"Status":"Voided","Lines":[{"LineNo":1}]}""", reason: "Duplicate");

        var dto = record.ToDto();

        dto.OccurredAtUtc.ShouldBe(at);
        dto.UserId.ShouldBe(user.Value);
        dto.CorrelationId.ShouldBe(correlation);
        dto.EntityType.ShouldBe("Bill");
        dto.EntityId.ShouldBe(Entity);
        dto.Action.ShouldBe("Voided");
        dto.Reason.ShouldBe("Duplicate");
        dto.Before.ShouldNotBeNull().GetProperty("Status").GetString().ShouldBe("Draft");
        dto.After.GetProperty("Lines")[0].GetProperty("LineNo").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void A_record_of_a_creation_is_mapped_without_a_before_image()
    {
        var record = AuditRecord.Create(DateTimeOffset.UnixEpoch, userId: null, Guid.NewGuid(), "Patient", Entity, "Registered", beforeJson: null, "{}");

        var dto = record.ToDto();

        dto.Before.ShouldBeNull();
        dto.UserId.ShouldBeNull();
    }

    private static ListAuditRecordsQuery Query() => new() { EntityType = "Patient", EntityId = Entity };
}
