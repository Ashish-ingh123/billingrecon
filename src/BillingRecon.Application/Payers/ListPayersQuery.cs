using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>
/// One page of the tenant's payers, by name. <see cref="Search"/> finds a payer by part of its name, without regard to
/// case; <see cref="Status"/> and <c>PatientId</c> narrow the list.
/// </summary>
public sealed record ListPayersQuery : IPagedQuery
{
    /// <summary>Longest allowed <see cref="Search"/>.</summary>
    public const int SearchMaxLength = 200;

    /// <summary>Part of a name; null or blank lists every payer.</summary>
    public string? Search { get; init; }

    /// <summary>Only payers with this status; null lists both.</summary>
    public PayerStatus? Status { get; init; }

    /// <summary>Only the payer linked to this patient; null lists payers whatever their link.</summary>
    public PatientId? PatientId { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
