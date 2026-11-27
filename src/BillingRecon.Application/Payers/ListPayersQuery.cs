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
    /// <summary>The columns this list is sorted by on request (ADR 0063); each stands for an expression of the query service.</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["name", "kind"];

    /// <summary>Longest allowed <see cref="Search"/>.</summary>
    public const int SearchMaxLength = 200;

    /// <summary>Part of a name; null or blank lists every payer.</summary>
    public string? Search { get; init; }

    /// <summary>Only payers with this status; null lists both.</summary>
    public PayerStatus? Status { get; init; }

    /// <summary>Only the payer linked to this patient; null lists payers whatever their link.</summary>
    public PatientId? PatientId { get; init; }

    /// <summary>Only the payers of this kind.</summary>
    public PayerKind? Kind { get; init; }

    /// <summary>The order asked for, one of <see cref="Sorts"/>; null for the list's own order.</summary>
    public ListSort? Order { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
