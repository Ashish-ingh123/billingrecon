using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Makes an inactive service item active again. The client must have read the current version (BR-804); a stale
/// version is answered before the status rule, so the client re-reads and sees why.
/// </summary>
public sealed class ReactivateServiceItemHandler(IServiceItemRepository repository, IUnitOfWork unitOfWork)
    : ICommandHandler<ReactivateServiceItemCommand, ServiceItemDto>
{
    /// <inheritdoc />
    public async Task<Result<ServiceItemDto>> HandleAsync(ReactivateServiceItemCommand command, CancellationToken ct)
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

        var reactivated = item.Reactivate();
        if (reactivated.IsFailure)
        {
            return reactivated.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? item.ToDto() : committed.Error;
    }
}
