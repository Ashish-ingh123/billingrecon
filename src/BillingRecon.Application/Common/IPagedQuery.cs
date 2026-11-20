namespace BillingRecon.Application.Common;

/// <summary>A query that answers with one page of a list (<see cref="PageOf{T}"/>).</summary>
public interface IPagedQuery
{
    /// <summary>The page asked for, from 1.</summary>
    public int Page { get; }

    /// <summary>How many items a page holds, 1 to <see cref="Paging.MaxPageSize"/>.</summary>
    public int PageSize { get; }
}
