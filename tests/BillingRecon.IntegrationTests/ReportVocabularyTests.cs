using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Payments;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// Reporting has words of its own for a bill's state, a payment's state and a payment method, since the module rule keeps
/// its Application and Api layers from naming the Billing and Payments enums (ADR 0036). The words must stay the ones
/// those modules use, by name and by number, so that a new method or state fails here before a report meets it.
/// </summary>
public sealed class ReportVocabularyTests
{
    [Fact]
    public void A_payment_method_of_a_report_is_a_payment_method_by_the_same_name_and_number()
    {
        Members<ReportPaymentMethod>().ShouldBe(Members<PaymentMethod>());
    }

    [Fact]
    public void A_payment_state_of_a_report_is_a_payment_state_by_the_same_name_and_number()
    {
        Members<ReportPaymentStatus>().ShouldBe(Members<PaymentStatus>());
    }

    [Fact]
    public void A_bill_state_of_the_register_is_one_of_the_states_of_a_posted_bill_by_the_same_name()
    {
        Enum.GetNames<ReportBillStatus>().ShouldBe([nameof(BillStatus.Posted), nameof(BillStatus.PartiallyPaid), nameof(BillStatus.Settled)]);
    }

    private static (string Name, int Value)[] Members<TEnum>()
        where TEnum : struct, Enum
        => [.. Enum.GetValues<TEnum>().Select(value => (value.ToString(), Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)))];
}
