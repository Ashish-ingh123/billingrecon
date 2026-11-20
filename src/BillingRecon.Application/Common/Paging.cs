namespace BillingRecon.Application.Common;

/// <summary>The limits of a list query (docs/api.md): pages are numbered from 1 and hold at most 100 items.</summary>
public static class Paging
{
    /// <summary>The page a list starts at when the client asks for none.</summary>
    public const int FirstPage = 1;

    /// <summary>The page size when the client asks for none.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>The largest page size a client may ask for.</summary>
    public const int MaxPageSize = 100;

    /// <summary>The highest page number a client may ask for; it keeps the number of skipped rows within an int.</summary>
    public const int MaxPage = 1_000_000;
}
