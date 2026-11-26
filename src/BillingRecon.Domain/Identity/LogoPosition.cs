namespace BillingRecon.Domain.Identity;

/// <summary>Where the logo stands at the head of a printed document (ADR 0059).</summary>
public enum LogoPosition
{
    /// <summary>At the left, the hospital's name beside it.</summary>
    Left = 1,

    /// <summary>In the middle, the hospital's name under it.</summary>
    Centre = 2,

    /// <summary>At the right, the hospital's name before it.</summary>
    Right = 3,
}
