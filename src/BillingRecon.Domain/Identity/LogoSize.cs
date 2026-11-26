namespace BillingRecon.Domain.Identity;

/// <summary>How large the logo is printed (ADR 0059): the size of the box it stands in.</summary>
public enum LogoSize
{
    /// <summary>Small.</summary>
    Small = 1,

    /// <summary>Medium: the size of the sheet before there were settings.</summary>
    Medium = 2,

    /// <summary>Large.</summary>
    Large = 3,
}
