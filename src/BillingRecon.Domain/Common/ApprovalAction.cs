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

    /// <summary>Bring in the opening balances of an opening batch (ADR 0053).</summary>
    BringForward = 3,

    /// <summary>Close a cashier's shift with a difference (BR-314, ADR 0060).</summary>
    Close = 4,
}
