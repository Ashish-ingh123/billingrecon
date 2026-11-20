using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Deactivates a service item. The client must have read the current version (BR-804); a stale version is answered
/// before the status rule, so the client re-reads and sees why.
/// </summary>
public sealed class DeactivateServiceItemHandler(IServiceItemRepository repository, IUnitOfWork unitOfWork)
    : ICommandHandler<DeactivateServiceItemCommand, ServiceItemDto>
{
    /// <inheritdoc />
    public async Task<Result<ServiceItemDto>> HandleAsync(DeactivateServiceItemCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await repository.GetAsync(command.Id, ct);
        if (item is null)
        {
            return BillingErrors.ServiceItemNotFound();
        }

        if (RowVersionText.Of(item) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var deactivated = item.Deactivate();
        if (deactivated.IsFailure)
        {
            return deactivated.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? item.ToDto() : committed.Error;
    }
}
