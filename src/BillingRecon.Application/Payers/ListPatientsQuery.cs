using BillingRecon.Application.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>
/// One page of the tenant's patients, by name. <see cref="Search"/> finds a patient by a part of the MRN or of the
/// name, without regard to case; <see cref="Status"/> narrows the list to active or inactive patients.
/// </summary>
public sealed record ListPatientsQuery : IPagedQuery
{
    /// <summary>The columns this list is sorted by on request (ADR 0063); each stands for an expression of the query service.</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["mrn", "fullName", "dateOfBirth"];

    /// <summary>Longest allowed <see cref="Search"/>.</summary>
    public const int SearchMaxLength = 200;

    /// <summary>A part of an MRN or of a name; null or blank lists every patient.</summary>
    public string? Search { get; init; }

    /// <summary>Only patients with this status; null lists both.</summary>
    public PatientStatus? Status { get; init; }

    /// <summary>The order asked for, one of <see cref="Sorts"/>; null for the list's own order.</summary>
    public ListSort? Order { get; init; }

    /// <inheritdoc />
    public int Page { get; init; } = Paging.FirstPage;

    /// <inheritdoc />
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}
