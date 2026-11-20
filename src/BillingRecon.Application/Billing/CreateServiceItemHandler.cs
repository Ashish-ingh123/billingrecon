using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Adds an entry to the tenant's service catalogue. The code must be free in the tenant, whatever the case of its
/// letters and whether the item that has it is active or not. The unique index on the code decides the race between
/// two items with one code, whose loser gets the same answer as the check here.
/// </summary>
public sealed class CreateServiceItemHandler(
    IValidator<CreateServiceItemCommand> validator,
    IServiceItemReadService items,
    IServiceItemRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateServiceItemCommand, ServiceItemDto>
{
    /// <inheritdoc />
    public async Task<Result<ServiceItemDto>> HandleAsync(CreateServiceItemCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var code = ServiceItemCode.Of(command.Code);
        if (await items.CodeExistsAsync(code, ct))
        {
            return BillingErrors.ServiceItemCodeDuplicate();
        }

        var item = ServiceItem.Create(code, command.ToDetails());
        repository.Add(item);

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            // The only unique index a new item can hit is the code's: another item with the code won the race.
            return committed.Error.Code == CommonErrors.DuplicateKeyCode ? BillingErrors.ServiceItemCodeDuplicate() : committed.Error;
        }

        return item.ToDto();
    }
}
