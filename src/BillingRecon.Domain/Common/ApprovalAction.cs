namespace BillingRecon.Domain.Common;

/// <summary>What the maker asks to be done to the document, once a checker approves (BR-604, BR-606).</summary>
public enum ApprovalAction
{
    /// <summary>Post the document.</summary>
    Post = 0,

    /// <summary>Reverse the document.</summary>
    Reverse = 1,

    /// <summary>Change the prefix of a number series.</summary>
    ChangePrefix = 2,
}
