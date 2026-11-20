using BillingRecon.Application.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>
/// One page of the tenant's patients, by name. <see cref="Search"/> finds a patient by the whole MRN or by part of the
/// name, without regard to case; <see cref="Status"/> narrows the list to active or inactive patients.
/// </summary>
public sealed record ListPatientsQuery : IPagedQuery
{
    /// <summary>Longest allowed <see cref="Search"/>.</summary>
    public const int SearchMaxLength = 200;

    /// <summary>A whole MRN or part of a name; null or blank lists every patient.</summary>
    public string? Search { get; init; }

    /// <summary>Only patients with this status; null lists both.</summary>
    public PatientStatus? Status { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
