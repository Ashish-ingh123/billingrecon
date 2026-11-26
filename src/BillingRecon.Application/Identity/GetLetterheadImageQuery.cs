using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>The logo or the signature the current hospital prints now (ADR 0059).</summary>
public sealed record GetLetterheadImageQuery(LetterheadImageKind Kind);
