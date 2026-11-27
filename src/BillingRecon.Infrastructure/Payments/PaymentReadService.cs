using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Payments;

/// <summary>
/// Reads payments without tracking them, filtered by the tenant of the request's context. The list is ordered by the
/// payment date, newest first, then by the number within a day, newest first.
/// </summary>
public sealed class PaymentReadService(BillingReconDbContext context, IPartySearch parties) : IPaymentReadService
{
    /// <inheritdoc />
    public Task<UserId?> FindRecorderAsync(PaymentId id, CancellationToken ct)
        => context.Set<Payment>().Where(payment => payment.Id == id).Select(payment => payment.CreatedByUserId).SingleOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<PaymentDto?> GetAsync(PaymentId id, CancellationToken ct)
    {
        var payment = await Payments().SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        return payment?.ToDto();
    }

    /// <inheritdoc />
    public async Task<PaymentAllocationsDto?> GetWithAllocationsAsync(PaymentId id, CancellationToken ct)
    {
        var payment = await Payments().Include(candidate => candidate.Allocations).SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        return payment?.ToDtoWithAllocations();
    }

    /// <inheritdoc />
    public async Task<PageOf<PaymentDto>> ListAsync(ListPaymentsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var payments = Payments();

        if (query.PayerId is { } payerId)
        {
            payments = payments.Where(payment => payment.PayerId == payerId);
        }

        if (query.Method is { } method)
        {
            payments = payments.Where(payment => payment.Method == method);
        }

        if (query.Status is { } status)
        {
            payments = payments.Where(payment => payment.Status == status);
        }

        if (query.PaymentDateFrom is { } from)
        {
            payments = payments.Where(payment => payment.PaymentDate >= from);
        }

        if (query.PaymentDateTo is { } to)
        {
            payments = payments.Where(payment => payment.PaymentDate <= to);
        }

        // The filters of Prompt 12.2 (ADR 0063): each narrows the rows the search and the page are taken from.
        if (query.AmountFrom is { } amountFrom)
        {
            payments = payments.Where(payment => payment.Amount >= Money.Of(amountFrom));
        }

        if (query.AmountTo is { } amountTo)
        {
            payments = payments.Where(payment => payment.Amount <= Money.Of(amountTo));
        }

        if (query.CreatedBy is { } createdBy)
        {
            payments = payments.Where(payment => payment.CreatedByUserId == createdBy);
        }

        if (query.Origin is { } origin)
        {
            payments = payments.Where(payment => payment.Origin == origin);
        }

        // One box (Prompt 11.1; ADR 0036, amended 2026-11-26). Each part of the search is a query of its own, so that
        // each is answered by its own index, and the documents are those of any part: an OR over them reads every row.
        int? fewFound = null;
        if (DocumentSearchFilter.For(query.Search, parties.PatientsNamed, parties.PayersNamed) is { } search)
        {
            var filtered = payments;
            var found = filtered.Where(payment => EF.Functions.Like(payment.Number!, search.Pattern, LikePattern.Escape)).Select(payment => payment.Id)
                .Union(filtered.Where(payment => EF.Functions.Like(payment.LegacyNumber!, search.Pattern, LikePattern.Escape)).Select(payment => payment.Id))
                .Union(filtered.Where(payment => search.Payers.Contains(payment.PayerId)).Select(payment => payment.Id));
            if (search.Amount is { } amount)
            {
                found = found.Union(filtered.Where(payment => payment.Amount == amount).Select(payment => payment.Id));
            }

            (payments, fewFound) = await DocumentSearchFilter.NarrowAsync(Payments(), found, filtered.Where(payment => found.Contains(payment.Id)), payment => payment.Id, ct);
        }

        var totalCount = fewFound ?? await payments.CountAsync(ct);
        // The order asked for by name, or the list's own; each ends on a key that is one of a kind (ADR 0063).
        IOrderedQueryable<Payment> ordered = query.Order switch
        {
            { Column: "number", Descending: var down } => payments.By(payment => payment.FiscalYear, down).Then(payment => payment.SequenceNumber, down).Then(payment => payment.Id, down),
            { Column: "paymentDate", Descending: var down } => payments.By(payment => payment.PaymentDate, down).Then(payment => payment.Id, down),
            { Column: "amount", Descending: var down } => payments.By(payment => payment.Amount, down).Then(payment => payment.Id, down),
            { Column: "unappliedAmount", Descending: var down } => payments.By(payment => payment.UnappliedAmount, down).Then(payment => payment.Id, down),
            null => payments
                .OrderByDescending(payment => payment.PaymentDate)
                .ThenByDescending(payment => payment.FiscalYear)
                .ThenByDescending(payment => payment.SequenceNumber),
            _ => throw new InvalidOperationException($"The order {query.Order} is not one of this list; its validator should have refused it."),
        };
        var page = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<PaymentDto>([.. page.Select(payment => payment.ToDto())], query.Page, query.PageSize, totalCount);
    }

    private IQueryable<Payment> Payments() => context.Set<Payment>().AsNoTracking();
}
