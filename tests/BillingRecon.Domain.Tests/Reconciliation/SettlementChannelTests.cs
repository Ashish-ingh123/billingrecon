using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Domain.Tests.Reconciliation;

/// <summary>
/// The SettlementChannel aggregate and its column mapping (BR-1001, BR-1002; ADR 0065; docs/domain-model.md section 11):
/// a bank with its mapping or a gateway with its bank, a label of at most four digits, a window of 0 to 10 days, never
/// deleted.
/// </summary>
public sealed class SettlementChannelTests
{
    internal static ColumnMapping Mapping() => new()
    {
        HeaderRow = 1,
        DateColumn = "Txn Date",
        DateFormat = "dd/MM/yyyy",
        DescriptionColumn = "Description",
        ReferenceColumn = "Ref No",
        AmountLayout = AmountLayout.TwoColumns,
        DebitColumn = "Debit",
        CreditColumn = "Credit",
        BalanceColumn = "Balance",
        DecimalStyle = DecimalStyle.Point,
    };

    [Fact]
    public void BR1001_a_bank_channel_keeps_its_mapping_and_has_no_gateway()
    {
        var channel = SettlementChannel.CreateBank("HDFC current", "4521", 3, Mapping());

        (channel.Kind, channel.Name, channel.AccountLabel, channel.DateWindowDays, channel.Status).ShouldBe(
            (SettlementChannelKind.Bank, "HDFC current", "4521", 3, SettlementChannelStatus.Active));
        channel.Mapping.ShouldBe(Mapping());
        channel.Gateway.ShouldBeNull();
        channel.BankChannelId.ShouldBeNull();
        channel.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<SettlementChannelCreated>().Kind.ShouldBe(SettlementChannelKind.Bank);
    }

    [Fact]
    public void BR1001_a_gateway_channel_names_its_gateway_and_its_bank_and_has_no_mapping()
    {
        var bank = new SettlementChannelId(Guid.NewGuid());

        var channel = SettlementChannel.CreateGateway("Razorpay", null, 2, PaymentGateway.Razorpay, bank);

        (channel.Kind, channel.Gateway, channel.BankChannelId, channel.Mapping).ShouldBe((SettlementChannelKind.Gateway, PaymentGateway.Razorpay, bank, (ColumnMapping?)null));
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("12a4")]
    [InlineData("XXXX")]
    public void BR1001_a_label_is_one_to_four_digits_never_a_full_account_number(string label)
    {
        SettlementChannel.IsAccountLabel(label).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => SettlementChannel.CreateBank("Bank", label, 3, Mapping()));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void BR1009_the_window_is_zero_to_ten_days(int days)
        => Should.Throw<ArgumentException>(() => SettlementChannel.CreateBank("Bank", null, days, Mapping()));

    [Fact]
    public void A_change_that_changes_nothing_raises_no_event_and_one_that_does_raises_one()
    {
        var channel = SettlementChannel.CreateBank("HDFC current", "4521", 3, Mapping());
        channel.ClearDomainEvents();

        channel.Change("HDFC current", "4521", 3, null);
        channel.ChangeMapping(Mapping()).IsSuccess.ShouldBeTrue();
        channel.DomainEvents.ShouldBeEmpty();

        channel.Change("HDFC current account", "4521", 5, null);
        channel.ChangeMapping(Mapping() with { DateFormat = "dd-MM-yyyy" }).IsSuccess.ShouldBeTrue();
        channel.DomainEvents.Select(raised => raised.GetType()).ShouldBe([typeof(SettlementChannelChanged), typeof(SettlementChannelMappingChanged)]);
        (channel.Name, channel.DateWindowDays, channel.Mapping!.Value.DateFormat).ShouldBe(("HDFC current account", 5, "dd-MM-yyyy"));
    }

    [Fact]
    public void A_gateway_has_no_mapping_to_change_and_a_bank_names_no_bank()
    {
        var gateway = SettlementChannel.CreateGateway("Razorpay", null, 2, PaymentGateway.Razorpay, new SettlementChannelId(Guid.NewGuid()));
        gateway.ChangeMapping(Mapping()).Error.Code.ShouldBe(ReconciliationErrors.SettlementChannelNotBankCode);

        var bank = SettlementChannel.CreateBank("Bank", null, 3, Mapping());
        Should.Throw<ArgumentException>(() => bank.Change("Bank", null, 3, new SettlementChannelId(Guid.NewGuid())));
    }

    [Fact]
    public void A_channel_is_deactivated_and_reactivated_once_each()
    {
        var channel = SettlementChannel.CreateBank("Bank", null, 3, Mapping());

        channel.Deactivate().IsSuccess.ShouldBeTrue();
        channel.Deactivate().Error.Code.ShouldBe(ReconciliationErrors.SettlementChannelAlreadyInactiveCode);
        channel.Reactivate().IsSuccess.ShouldBeTrue();
        channel.Reactivate().Error.Code.ShouldBe(ReconciliationErrors.SettlementChannelAlreadyActiveCode);
    }

    [Fact]
    public void BR1002_a_mapping_names_each_value_once_and_the_columns_of_its_layout_only()
    {
        ColumnMapping.Problem(Mapping()).ShouldBeNull();
        ColumnMapping.Problem(Mapping() with { AmountLayout = AmountLayout.OneColumn, AmountColumn = "Amount", DebitColumn = null, CreditColumn = null }).ShouldBeNull();

        ColumnMapping.Problem(Mapping() with { HeaderRow = 0 }).ShouldNotBeNull();
        ColumnMapping.Problem(Mapping() with { DateColumn = " " }).ShouldNotBeNull();
        ColumnMapping.Problem(Mapping() with { DateFormat = "MM/dd/yyyy" }).ShouldNotBeNull("a US date is not one the mapping offers");
        ColumnMapping.Problem(Mapping() with { AmountColumn = "Amount" }).ShouldNotBeNull("two columns name no amount column");
        ColumnMapping.Problem(Mapping() with { CreditColumn = null }).ShouldNotBeNull();
        ColumnMapping.Problem(Mapping() with { BalanceColumn = "debit" }).ShouldNotBeNull("each value has a column of its own, whatever the case");
        Should.Throw<ArgumentException>(() => SettlementChannel.CreateBank("Bank", null, 3, Mapping() with { HeaderRow = 51 }));
    }
}
