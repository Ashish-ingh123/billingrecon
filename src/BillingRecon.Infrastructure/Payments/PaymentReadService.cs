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
        var page = await payments
            .OrderByDescending(payment => payment.PaymentDate)
            .ThenByDescending(payment => payment.FiscalYear)
            .ThenByDescending(payment => payment.SequenceNumber)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<PaymentDto>([.. page.Select(payment => payment.ToDto())], query.Page, query.PageSize, totalCount);
    }

    private IQueryable<Payment> Payments() => context.Set<Payment>().AsNoTracking();
}
