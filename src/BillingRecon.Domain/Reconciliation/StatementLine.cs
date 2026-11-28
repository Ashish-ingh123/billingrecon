using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>
/// A line of a bank's statement, as it was read (BR-1004, BR-1005; ADR 0065). Its read fields and the raw text of its
/// row never change: they have no setter outside this type and no method changes them. Only its status follows its
/// matches (Prompts 13.2 and 13.3), which is why it carries a row version. It is never deleted. A line is created by an
/// import, which raises one event for all of its lines, so creating a line raises none of its own.
/// </summary>
public sealed class StatementLine : AggregateRoot<StatementLineId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>Longest allowed <see cref="Reference"/>.</summary>
    public const int ReferenceMaxLength = 100;

    /// <summary>Longest allowed <see cref="Description"/>.</summary>
    public const int DescriptionMaxLength = 500;

    /// <summary>Longest allowed <see cref="RawText"/>: the row as the file held it.</summary>
    public const int RawTextMaxLength = 2000;

    /// <summary>The length of <see cref="Fingerprint"/>: a SHA-256 in hexadecimal.</summary>
    public const int FingerprintLength = 64;

    // Also the constructor Entity Framework uses to load a line; it then sets the other properties.
    private StatementLine(string description, string rawText, string fingerprint)
    {
        Description = description;
        RawText = rawText;
        Fingerprint = fingerprint;
        Status = StatementLineStatus.Unmatched;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The bank channel whose statement held the line.</summary>
    public SettlementChannelId ChannelId { get; private set; }

    /// <summary>The import that stored the line.</summary>
    public StatementImportId ImportId { get; private set; }

    /// <summary>The line of the file the row started on, as the dry run named it.</summary>
    public int RowNumber { get; private set; }

    /// <summary>The date of the transaction, as the bank wrote it.</summary>
    public DateOnly TransactionDate { get; private set; }

    /// <summary>The value date, when the bank gave one.</summary>
    public DateOnly? ValueDate { get; private set; }

    /// <summary>Money in or money out.</summary>
    public StatementDirection Direction { get; private set; }

    /// <summary>The amount, above zero, in rupees (BR-1005).</summary>
    public Money Amount { get; private set; }

    /// <summary>The reference the bank gave (UTR, cheque number); null when the file has none.</summary>
    public string? Reference { get; private set; }

    /// <summary>The bank's description of the line (the narration).</summary>
    public string Description { get; private set; }

    /// <summary>The running balance after the line, when the bank gave one; it may be below zero (an overdraft).</summary>
    public Money? Balance { get; private set; }

    /// <summary>The row as the file held it, its fields joined by commas.</summary>
    public string RawText { get; private set; }

    /// <summary>The SHA-256 of what identifies the line (BR-1004), in upper-case hexadecimal.</summary>
    public string Fingerprint { get; private set; }

    /// <summary>Which of the lines with this fingerprint this one is, from 1: identical lines are kept apart by it.</summary>
    public int Occurrence { get; private set; }

    /// <summary>Where the line stands: the one thing about it that changes.</summary>
    public StatementLineStatus Status { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? CreatedByUserId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? UpdatedByUserId { get; private set; }

    /// <inheritdoc />
    public byte[] RowVersion { get; private set; }

    /// <summary>A line read from a row of a statement, unmatched, the <paramref name="occurrence"/>-th with its fingerprint.</summary>
    /// <exception cref="ArgumentException">A value breaks a limit the reader checks.</exception>
    public static StatementLine Read(
        SettlementChannelId channelId,
        StatementImportId importId,
        int rowNumber,
        StatementLineValues values,
        string rawText,
        int occurrence)
    {
        if (values.Amount <= Money.Zero)
        {
            throw new ArgumentException("A line's amount is above zero (BR-1005).", nameof(values));
        }

        if (!Enum.IsDefined(values.Direction) || rowNumber < 1 || occurrence < 1)
        {
            throw new ArgumentException("A line has a direction, a row from 1 and an occurrence from 1.", nameof(values));
        }

        var line = new StatementLine(
            Field.Required(values.Description, DescriptionMaxLength),
            Field.Required(rawText, RawTextMaxLength),
            FingerprintOf(channelId, values))
        {
            ChannelId = channelId,
            ImportId = importId,
            RowNumber = rowNumber,
            TransactionDate = values.TransactionDate,
            ValueDate = values.ValueDate,
            Direction = values.Direction,
            Amount = values.Amount,
            Reference = Field.Optional(values.Reference, ReferenceMaxLength),
            Balance = values.Balance,
            Occurrence = occurrence,
        };

        return line;
    }

    /// <summary>
    /// The line joins a match (BR-1006): the one change a line accepts, of its status only. The match is audited; the
    /// status is its cache on the line, kept in the match's transaction under the line's row version.
    /// </summary>
    /// <exception cref="InvalidOperationException">The line is not unmatched; the use case should have left it alone.</exception>
    public void MarkMatched()
    {
        if (Status != StatementLineStatus.Unmatched)
        {
            throw new InvalidOperationException($"Only an unmatched line joins a match; this one is {Status}.");
        }

        Status = StatementLineStatus.Matched;
    }

    /// <summary>
    /// The fingerprint of a line of <paramref name="channelId"/> (BR-1004): a SHA-256 of the channel, the date, the
    /// direction, the amount, the reference, the balance and the description, in upper-case hexadecimal. Two rows with
    /// the same values have the same fingerprint, whatever file they came from.
    /// </summary>
    public static string FingerprintOf(SettlementChannelId channelId, StatementLineValues values)
    {
        var text = string.Join(
            '\u001F',
            channelId.Value.ToString("N"),
            values.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            values.Direction.ToString(),
            values.Amount.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            values.Reference?.Trim() ?? string.Empty,
            values.Balance?.Amount.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty,
            values.Description.Trim());

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
