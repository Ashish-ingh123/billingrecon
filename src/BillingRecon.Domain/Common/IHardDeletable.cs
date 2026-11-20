namespace BillingRecon.Domain.Common;

/// <summary>
/// Marks the rare entity whose rows may be deleted. Nothing else is ever deleted (CLAUDE.md section 4, rule 10): the
/// persistence layer refuses to delete a row of any entity that does not carry this marker. The one production use is
/// a line of a draft bill, which is not yet a financial record: replacing the lines of a draft removes the ones that
/// are no longer wanted (BR-204), and the aggregate allows that only while the bill is a draft.
/// </summary>
public interface IHardDeletable
{
}
