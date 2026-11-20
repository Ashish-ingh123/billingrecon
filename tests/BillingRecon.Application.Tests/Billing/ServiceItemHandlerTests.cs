using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using NSubstitute;

namespace BillingRecon.Application.Tests.Billing;

/// <summary>
/// The service item use cases with their ports replaced (BR-201, BR-804): what they check, in which order, and that
/// nothing is written when a check fails.
/// </summary>
public sealed class ServiceItemHandlerTests
{
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 7, 209];
    private static readonly byte[] OlderVersion = [0, 0, 0, 0, 0, 0, 7, 208];

    private readonly IServiceItemReadService _items = Substitute.For<IServiceItemReadService>();
    private readonly IServiceItemRepository _repository = Substitute.For<IServiceItemRepository>();
    private IUnitOfWork _unitOfWork = TestDoubles.UnitOfWork();

    [Fact]
    public async Task An_item_with_a_free_code_is_created_active_and_committed_once()
    {
        var result = await Create().HandleAsync(NewItem(), CancellationToken.None);

        result.Value.Code.ShouldBe("CONS-OPD");
        result.Value.Name.ShouldBe("OPD consultation");
        result.Value.DefaultUnitPrice.ShouldBe(500m);
        result.Value.TaxRatePercent.ShouldBe(18m);
        result.Value.Status.ShouldBe(ServiceItemStatus.Active);
        _repository.Received(1).Add(Arg.Is<ServiceItem>(item => item.Code == ServiceItemCode.Of("CONS-OPD")));
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_invalid_item_is_refused_with_400_before_anything_is_read()
    {
        var result = await Create().HandleAsync(NewItem() with { Code = "CONS OPD", TaxRatePercent = 101m }, CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        result.Error.FieldErrors.Keys.ShouldBe(["code", "taxRatePercent"], ignoreOrder: true);
        await _items.DidNotReceiveWithAnyArgs().CodeExistsAsync(default, default);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task A_code_the_tenant_already_has_is_refused_with_409_before_anything_is_written()
    {
        _items.CodeExistsAsync(ServiceItemCode.Of("cons-opd"), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Create().HandleAsync(NewItem(), CancellationToken.None);

        result.Error.Code.ShouldBe(BillingErrors.ServiceItemCodeDuplicateCode);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task An_item_that_loses_the_race_on_the_unique_index_gets_the_same_answer()
    {
        _unitOfWork = TestDoubles.UnitOfWork(CommonErrors.DuplicateKey());

        var result = await Create().HandleAsync(NewItem(), CancellationToken.None);

        result.Error.Code.ShouldBe(BillingErrors.ServiceItemCodeDuplicateCode);
    }

    [Fact]
    public async Task BR804_correcting_an_item_from_a_stale_version_is_a_conflict_and_changes_nothing()
    {
        var item = Existing(CurrentVersion);

        var result = await Update().HandleAsync(Correction(item.Id, OlderVersion) with { DefaultUnitPrice = 999m }, CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        item.DefaultUnitPrice.ShouldBe(UnitPrice.Of(500m));
        await _unitOfWork.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task Correcting_an_item_from_the_current_version_replaces_its_details_keeps_its_code_and_commits()
    {
        var item = Existing(CurrentVersion);

        var result = await Update().HandleAsync(
            Correction(item.Id, CurrentVersion) with { Name = "OPD consultation (senior)", DefaultUnitPrice = 750.5m, TaxRatePercent = 5m },
            CancellationToken.None);

        result.Value.Name.ShouldBe("OPD consultation (senior)");
        result.Value.DefaultUnitPrice.ShouldBe(750.5m);
        result.Value.TaxRatePercent.ShouldBe(5m);
        result.Value.Code.ShouldBe("CONS-OPD");
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_invalid_correction_is_refused_with_400_before_the_item_is_loaded()
    {
        var item = Existing(CurrentVersion);

        var result = await Update().HandleAsync(Correction(item.Id, CurrentVersion) with { DefaultUnitPrice = null }, CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        await _repository.DidNotReceiveWithAnyArgs().GetAsync(default, default);
    }

    [Fact]
    public async Task Correcting_an_unknown_item_is_not_found()
    {
        var result = await Update().HandleAsync(Correction(new ServiceItemId(Guid.NewGuid()), CurrentVersion), CancellationToken.None);

        result.Error.Code.ShouldBe(BillingErrors.ServiceItemNotFoundCode);
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Deactivating_an_item_and_deactivating_it_again()
    {
        var item = Existing(CurrentVersion);
        var handler = new DeactivateServiceItemHandler(_repository, _unitOfWork);

        (await handler.HandleAsync(new DeactivateServiceItemCommand(item.Id, Text(CurrentVersion)), CancellationToken.None)).Value.Status.ShouldBe(ServiceItemStatus.Inactive);
        (await handler.HandleAsync(new DeactivateServiceItemCommand(item.Id, Text(CurrentVersion)), CancellationToken.None)).Error.Code.ShouldBe(BillingErrors.ServiceItemAlreadyInactiveCode);
        (await handler.HandleAsync(new DeactivateServiceItemCommand(item.Id, Text(OlderVersion)), CancellationToken.None)).Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        (await handler.HandleAsync(new DeactivateServiceItemCommand(new ServiceItemId(Guid.NewGuid()), Text(CurrentVersion)), CancellationToken.None)).Error.Code.ShouldBe(BillingErrors.ServiceItemNotFoundCode);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reactivating_an_inactive_item_and_reactivating_it_again()
    {
        var item = Existing(CurrentVersion);
        item.Deactivate();
        var handler = new ReactivateServiceItemHandler(_repository, _unitOfWork);

        (await handler.HandleAsync(new ReactivateServiceItemCommand(item.Id, Text(CurrentVersion)), CancellationToken.None)).Value.Status.ShouldBe(ServiceItemStatus.Active);
        (await handler.HandleAsync(new ReactivateServiceItemCommand(item.Id, Text(CurrentVersion)), CancellationToken.None)).Error.Code.ShouldBe(BillingErrors.ServiceItemAlreadyActiveCode);
        (await handler.HandleAsync(new ReactivateServiceItemCommand(item.Id, Text(OlderVersion)), CancellationToken.None)).Error.Code.ShouldBe(CommonErrors.ConcurrencyConflictCode);
        (await handler.HandleAsync(new ReactivateServiceItemCommand(new ServiceItemId(Guid.NewGuid()), Text(CurrentVersion)), CancellationToken.None)).Error.Code.ShouldBe(BillingErrors.ServiceItemNotFoundCode);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reading_and_listing_items()
    {
        var known = Existing(CurrentVersion).ToDto();
        _items.GetAsync(new ServiceItemId(known.Id), Arg.Any<CancellationToken>()).Returns(known);
        var page = new PageOf<ServiceItemDto>([known], 1, 20, 1);
        _items.ListAsync(Arg.Any<ListServiceItemsQuery>(), Arg.Any<CancellationToken>()).Returns(page);

        (await new GetServiceItemHandler(_items).HandleAsync(new GetServiceItemQuery(new ServiceItemId(known.Id)), CancellationToken.None)).Value.ShouldBe(known);
        (await new GetServiceItemHandler(_items).HandleAsync(new GetServiceItemQuery(new ServiceItemId(Guid.NewGuid())), CancellationToken.None)).Error.Code.ShouldBe(BillingErrors.ServiceItemNotFoundCode);
        (await new ListServiceItemsHandler(new ListServiceItemsQueryValidator(), _items).HandleAsync(new ListServiceItemsQuery(), CancellationToken.None)).Value.ShouldBeSameAs(page);
        (await new ListServiceItemsHandler(new ListServiceItemsQueryValidator(), _items).HandleAsync(new ListServiceItemsQuery { Page = 0 }, CancellationToken.None)).Error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
    }

    [Fact]
    public void Mapping_a_command_that_was_not_validated_is_a_bug_and_throws()
    {
        Should.Throw<ArgumentException>(() => (NewItem() with { DefaultUnitPrice = null }).ToDetails());
        Should.Throw<ArgumentException>(() => (NewItem() with { TaxRatePercent = null }).ToDetails());
    }

    private CreateServiceItemHandler Create() => new(new CreateServiceItemCommandValidator(), _items, _repository, _unitOfWork);

    private UpdateServiceItemHandler Update() => new(new UpdateServiceItemCommandValidator(), _repository, _unitOfWork);

    // An item as the repository would load it: saved, with an id and a row version.
    private ServiceItem Existing(byte[] version)
    {
        var item = ServiceItem.Create(ServiceItemCode.Of("CONS-OPD"), NewItem().ToDetails()).Saved(version);
        var id = new ServiceItemId(Guid.NewGuid());
        typeof(Entity<ServiceItemId>).GetProperty(nameof(Entity<ServiceItemId>.Id))!.SetValue(item, id);
        _repository.GetAsync(id, Arg.Any<CancellationToken>()).Returns(item);

        return item;
    }

    private static string Text(byte[] version) => Convert.ToBase64String(version);

    private static CreateServiceItemCommand NewItem() => new()
    {
        Code = "CONS-OPD",
        Name = "OPD consultation",
        Category = "Consultation",
        DefaultUnitPrice = 500m,
        TaxRatePercent = 18m,
    };

    private static UpdateServiceItemCommand Correction(ServiceItemId id, byte[] version) => new()
    {
        Id = id,
        ExpectedVersion = Text(version),
        Name = "OPD consultation",
        Category = "Consultation",
        DefaultUnitPrice = 500m,
        TaxRatePercent = 18m,
    };
}
