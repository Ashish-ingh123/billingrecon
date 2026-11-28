namespace BillingRecon.Application.Reconciliation;

/// <summary>What the dry run and the import of a bank's statement both carry.</summary>
public interface IStatementFileInput
{
    /// <summary>The bank channel the file is for.</summary>
    public Guid ChannelId { get; }

    /// <summary>The name of the file, as the user chose it.</summary>
    public string FileName { get; }

    /// <summary>The text of the file, read as UTF-8.</summary>
    public string Text { get; }
}
