using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>
/// One of the hospital's bank accounts or payment-gateway accounts, the place a statement comes from (BR-1001, BR-1002;
/// ADR 0065; docs/domain-model.md section 11). It keeps a name unique in the hospital and at most the last four digits of
/// the account, never the full number. A bank channel has the column mapping its statement is read with; a gateway
/// channel names its gateway and the bank channel it settles into. A channel is deactivated, never deleted. That the
/// name is free and that the bank channel is an active bank channel of the hospital are the use case's questions.
/// </summary>
public sealed class SettlementChannel : AggregateRoot<SettlementChannelId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    /// <summary>Longest allowed <see cref="Name"/>.</summary>
    public const int NameMaxLength = 100;

    /// <summary>Longest allowed <see cref="AccountLabel"/>: the last four digits.</summary>
    public const int AccountLabelMaxLength = 4;

    /// <summary>The largest date window, in days (BR-1009).</summary>
    public const int MaxDateWindowDays = 10;

    /// <summary>The date window a new channel takes when none is given (BR-1009).</summary>
    public const int DefaultDateWindowDays = 3;

    // Also the constructor Entity Framework uses to load a channel; it then sets the other properties.
    private SettlementChannel(SettlementChannelKind kind, string name)
    {
        Kind = kind;
        Name = name;
        Status = SettlementChannelStatus.Active;
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>A bank account or a gateway account. It never changes.</summary>
    public SettlementChannelKind Kind { get; private set; }

    /// <summary>How people name the account, unique in the hospital ignoring the case of letters.</summary>
    public string Name { get; private set; }

    /// <summary>At most the last four digits of the account or merchant number; null when none was given.</summary>
    public string? AccountLabel { get; private set; }

    /// <summary>The gateway of a gateway channel; null for a bank. It never changes.</summary>
    public PaymentGateway? Gateway { get; private set; }

    /// <summary>The bank channel a gateway channel settles into; null for a bank.</summary>
    public SettlementChannelId? BankChannelId { get; private set; }

    /// <summary>How many days before a line an item may be dated and still match it (BR-1009).</summary>
    public int DateWindowDays { get; private set; }

    /// <summary>How the bank's CSV is read; null for a gateway.</summary>
    public ColumnMapping? Mapping { get; private set; }

    /// <summary>Whether the channel takes imports.</summary>
    public SettlementChannelStatus Status { get; private set; }

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

    /// <summary>Creates an active bank channel read with <paramref name="mapping"/>.</summary>
    /// <exception cref="ArgumentException">A value breaks a limit the validator checks.</exception>
    public static SettlementChannel CreateBank(string name, string? accountLabel, int dateWindowDays, ColumnMapping mapping)
    {
        CheckMapping(mapping);
        var channel = new SettlementChannel(SettlementChannelKind.Bank, Field.Required(name, NameMaxLength));
        channel.Apply(name, accountLabel, dateWindowDays);
        channel.Mapping = mapping;
        channel.AddDomainEvent(new SettlementChannelCreated(channel.Kind, channel.Name, null));

        return channel;
    }

    /// <summary>Creates an active gateway channel of <paramref name="gateway"/> that settles into <paramref name="bankChannelId"/>.</summary>
    /// <exception cref="ArgumentException">A value breaks a limit the validator checks.</exception>
    public static SettlementChannel CreateGateway(string name, string? accountLabel, int dateWindowDays, PaymentGateway gateway, SettlementChannelId bankChannelId)
    {
        if (!Enum.IsDefined(gateway))
        {
            throw new ArgumentException("The gateway is not one BillingRecon reads; the validator should have refused it.", nameof(gateway));
        }

        var channel = new SettlementChannel(SettlementChannelKind.Gateway, Field.Required(name, NameMaxLength));
        channel.Apply(name, accountLabel, dateWindowDays);
        channel.Gateway = gateway;
        channel.BankChannelId = bankChannelId;
        channel.AddDomainEvent(new SettlementChannelCreated(channel.Kind, channel.Name, gateway));

        return channel;
    }

    /// <summary>
    /// Replaces the name, the label, the window and, for a gateway, the bank channel it settles into. The kind and the
    /// gateway stay. Raises <see cref="SettlementChannelChanged"/> when a value changed.
    /// </summary>
    /// <exception cref="ArgumentException">A value breaks a limit the validator checks, or a bank channel names a bank.</exception>
    public void Change(string name, string? accountLabel, int dateWindowDays, SettlementChannelId? bankChannelId)
    {
        if ((Kind == SettlementChannelKind.Gateway) != bankChannelId.HasValue)
        {
            throw new ArgumentException("A gateway names the bank it settles into and a bank names none; the use case should have refused it.", nameof(bankChannelId));
        }

        var before = (Name, AccountLabel, DateWindowDays, BankChannelId);
        Apply(name, accountLabel, dateWindowDays);
        BankChannelId = bankChannelId;

        if (before != (Name, AccountLabel, DateWindowDays, BankChannelId))
        {
            AddDomainEvent(new SettlementChannelChanged(Id, Name));
        }
    }

    /// <summary>Replaces the column mapping of a bank channel; lines imported before keep what was read.</summary>
    /// <exception cref="ArgumentException">The mapping cannot be used; the validator should have refused it.</exception>
    public Result ChangeMapping(ColumnMapping mapping)
    {
        if (Kind != SettlementChannelKind.Bank)
        {
            return ReconciliationErrors.SettlementChannelNotBank();
        }

        CheckMapping(mapping);
        if (Mapping != mapping)
        {
            Mapping = mapping;
            AddDomainEvent(new SettlementChannelMappingChanged(Id, Name));
        }

        return Result.Success();
    }

    /// <summary>Deactivates the channel: it takes no import; what was imported stays.</summary>
    public Result Deactivate()
    {
        if (Status == SettlementChannelStatus.Inactive)
        {
            return ReconciliationErrors.SettlementChannelAlreadyInactive();
        }

        Status = SettlementChannelStatus.Inactive;
        AddDomainEvent(new SettlementChannelDeactivated(Id, Name));

        return Result.Success();
    }

    /// <summary>Makes an inactive channel active again.</summary>
    public Result Reactivate()
    {
        if (Status == SettlementChannelStatus.Active)
        {
            return ReconciliationErrors.SettlementChannelAlreadyActive();
        }

        Status = SettlementChannelStatus.Active;
        AddDomainEvent(new SettlementChannelReactivated(Id, Name));

        return Result.Success();
    }

    /// <summary>Whether <paramref name="value"/> may be a channel's label: one to four digits.</summary>
    public static bool IsAccountLabel(string? value) => value is { Length: > 0 and <= AccountLabelMaxLength } && value.All(char.IsAsciiDigit);

    // Every value is checked before any is assigned, so values that are refused leave the channel as it was.
    private void Apply(string name, string? accountLabel, int dateWindowDays)
    {
        var checkedName = Field.Required(name, NameMaxLength);
        var label = Field.Optional(accountLabel, AccountLabelMaxLength);
        if (label is not null && !IsAccountLabel(label))
        {
            throw new ArgumentException("A label is one to four digits; the validator should have refused it.", nameof(accountLabel));
        }

        if (dateWindowDays is < 0 or > MaxDateWindowDays)
        {
            throw new ArgumentException($"The window is 0 to {MaxDateWindowDays} days; the validator should have refused it.", nameof(dateWindowDays));
        }

        Name = checkedName;
        AccountLabel = label;
        DateWindowDays = dateWindowDays;
    }

    private static void CheckMapping(ColumnMapping mapping)
    {
        if (ColumnMapping.Problem(mapping) is { } problem)
        {
            throw new ArgumentException($"{problem} The validator should have refused it.", nameof(mapping));
        }
    }
}
