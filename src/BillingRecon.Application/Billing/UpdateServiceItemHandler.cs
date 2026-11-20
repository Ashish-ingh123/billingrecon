using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Replaces the name, category, price and tax rate of a service item. The client must have read the current version
/// (BR-804): a stale one is refused before anything changes, and the commit checks the row version once more, against
/// the row itself. Bill lines priced from the item keep the price and tax rate they copied.
/// </summary>
public sealed class UpdateServiceItemHandler(
    IValidator<UpdateServiceItemCommand> validator,
    IServiceItemRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateServiceItemCommand, ServiceItemDto>
{
    /// <inheritdoc />
    public async Task<Result<ServiceItemDto>> HandleAsync(UpdateServiceItemCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var item = await repository.GetAsync(command.Id, ct);
        if (item is null)
        {
            return BillingErrors.ServiceItemNotFound();
        }

        if (RowVersionText.Of(item) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        item.Update(command.ToDetails());

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? item.ToDto() : committed.Error;
    }
}
