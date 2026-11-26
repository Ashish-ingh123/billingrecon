namespace BillingRecon.Domain.Identity;

/// <summary>
/// What a hospital prints at the head and the foot of its documents (ADR 0054): the name it trades under in law, where
/// it is, how it is reached, its GST registration if it has one, and the line under which a document is signed. Every
/// part may be left out; a printed document shows what there is. The state is the place of supply of every document.
/// </summary>
public sealed record TenantLetterhead
{
    /// <summary>The legal name, when it differs from the name the hospital is known by.</summary>
    public string? LegalName { get; init; }

    /// <summary>The first line of the address.</summary>
    public string? AddressLine1 { get; init; }

    /// <summary>The second line of the address.</summary>
    public string? AddressLine2 { get; init; }

    /// <summary>The city or town.</summary>
    public string? City { get; init; }

    /// <summary>The state or union territory: the place of supply.</summary>
    public string? State { get; init; }

    /// <summary>The PIN code.</summary>
    public string? PinCode { get; init; }

    /// <summary>The phone number a patient may call about a bill.</summary>
    public string? Phone { get; init; }

    /// <summary>The e-mail address a patient may write to about a bill.</summary>
    public string? Email { get; init; }

    /// <summary>The GSTIN, fifteen characters, when the hospital is registered.</summary>
    public string? Gstin { get; init; }

    /// <summary>The line under the place of a signature, such as "Authorised signatory".</summary>
    public string? Signatory { get; init; }

    /// <summary>The signatory's designation, printed under the name (ADR 0059).</summary>
    public string? SignatoryDesignation { get; init; }
}
