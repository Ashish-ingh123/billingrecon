namespace BillingRecon.Domain.Identity;

/// <summary>Where a sign-in stands with its second step (ADR 0061): what the token it was given is good for.</summary>
public enum SecondStepStage
{
    /// <summary>The user has no second step and their roles need none: the token is a full one.</summary>
    None = 0,

    /// <summary>The password was right and the user has a second step: the token is good for giving its code, and nothing else.</summary>
    Pending = 1,

    /// <summary>The user's roles need a second step and the user has none yet: the token is good for enrolling, and nothing else.</summary>
    Enrolment = 2,

    /// <summary>The second step was given: the token is a full one, and says so.</summary>
    Done = 3,
}
