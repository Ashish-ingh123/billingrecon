using BillingRecon.Infrastructure.Common.Logging;

namespace BillingRecon.IntegrationTests.Logging;

public sealed class PiiMaskerTests
{
    [Theory]
    [InlineData("priya.sharma@example.com", "p***@example.com")]
    [InlineData("a@b.co", "a***@b.co")]
    [InlineData("first.last+tag@sub.example.co.in", "f***@sub.example.co.in")]
    [InlineData("Contact priya.sharma@example.com today", "Contact p***@example.com today")]
    [InlineData("<john@example.com>", "<j***@example.com>")]
    [InlineData("mailto:john@example.com.", "mailto:j***@example.com.")]
    [InlineData("to=a@x.org;cc=b@y.org", "to=a***@x.org;cc=b***@y.org")]
    [InlineData("PRIYA@EXAMPLE.COM", "P***@EXAMPLE.COM")]
    [InlineData("_billing.desk@example.com", "_***@example.com")]
    [InlineData("user name@example.com", "user n***@example.com")]
    public void Email_addresses_keep_the_first_character_and_the_domain(string value, string expected)
    {
        PiiMasker.Mask(value).ShouldBe(expected);
    }

    [Theory]
    [InlineData("9876543210")]
    [InlineData("+919876543210")]
    [InlineData("+91 98765 43210")]
    [InlineData("+91-98765-43210")]
    [InlineData("919876543210")]
    [InlineData("0091 9876543210")]
    [InlineData("09876543210")]
    [InlineData("98765 43210")]
    [InlineData("98765-43210")]
    public void Indian_mobile_numbers_keep_only_the_last_four_digits(string value)
    {
        PiiMasker.Mask(value).ShouldBe("******3210");
    }

    [Theory]
    [InlineData("call 9876543210 now", "call ******3210 now")]
    [InlineData("phones: 9876543210, 6123456789.", "phones: ******3210, ******6789.")]
    [InlineData("MRN-9876543210", "MRN-******3210")]
    [InlineData("call 9876543210 or write to priya@example.com", "call ******3210 or write to p***@example.com")]
    [InlineData("9876543210@ybl", "******3210@ybl")]
    [InlineData("phone_9876543210_home", "phone_******3210_home")]
    [InlineData("ref-919876543210", "ref-******3210")]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301 919876543210", "3f2504e0-4f89-11d3-9a0c-0305e82c3301 ******3210")]
    public void Numbers_and_addresses_are_masked_inside_longer_text(string value, string expected)
    {
        PiiMasker.Mask(value).ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("The bill is already posted.")]
    [InlineData("user@localhost")]
    [InlineData("priya.sharma@okaxis")]
    [InlineData("@handle")]
    [InlineData("price @ 10.50")]
    [InlineData("1234567890")]
    [InlineData("5876543210")]
    [InlineData("98765432")]
    [InlineData("98765432101")]
    [InlineData("ORD9876543210")]
    [InlineData("9876543210X")]
    [InlineData("BIL/26-27/000123")]
    [InlineData("2026-09-29T14:03:00.123Z")]
    [InlineData("1234567.89")]
    [InlineData("/api/v1/bills/3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("/api/v1/bills/3f2504e0-4f89-11d3-9a0c-919876543210")]
    [InlineData("/api/v1/bills/3F2504E0-4F89-11D3-9A0C-919876543210/allocations")]
    [InlineData("bill {3f2504e0-4f89-11d3-9a0c-919876543210} posted")]
    public void Text_without_an_email_address_or_mobile_number_is_returned_unchanged(string value)
    {
        PiiMasker.Mask(value).ShouldBeSameAs(value);
    }

    [Theory]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("3f2504e0-4f89-11d3-9a0c-919876543210")]
    [InlineData("00000000-0000-0000-0000-009876543210")]
    [InlineData("3f2504e04f8911d39a0c919876543210")]
    public void A_guid_is_never_masked_even_when_part_of_it_looks_like_a_phone_number(string value)
    {
        PiiMasker.Mask(value).ShouldBeSameAs(value);
    }

    [Theory]
    [InlineData("Email")]
    [InlineData("email")]
    [InlineData("ContactEmailAddress")]
    [InlineData("Phone")]
    [InlineData("PhoneNumber")]
    [InlineData("guarantorMobile")]
    [InlineData("MOBILE_NO")]
    public void Names_that_say_contact_data_are_sensitive(string name)
    {
        PiiMasker.IsSensitiveName(name).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("RequestPath")]
    [InlineData("CorrelationId")]
    [InlineData("Amount")]
    public void Other_names_are_not_sensitive(string name)
    {
        PiiMasker.IsSensitiveName(name).ShouldBeFalse();
    }

    [Theory]
    [InlineData("priya.sharma@example.com", "p***@example.com")]
    [InlineData("+91 98765 43210", "******3210")]
    [InlineData("not shaped like an address", "***")]
    [InlineData("080-2345-6789", "***")]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301", "***")]
    [InlineData("", "")]
    public void A_value_under_a_sensitive_name_is_masked_whatever_its_shape(string value, string expected)
    {
        PiiMasker.MaskSensitive(value).ShouldBe(expected);
    }

    [Theory]
    [InlineData('a', '9')]
    [InlineData('9', 'a')]
    [InlineData('.', '-')]
    public void A_very_long_value_is_scanned_without_hitting_the_match_timeout(char beforeAt, char afterAt)
    {
        // Nothing here is an address (no dot after the @) or a mobile number (the digit run is far too long), so the
        // value must come back untouched. If either pattern backtracked badly, the 250 ms match timeout would turn
        // the result into "***" and fail this test.
        var value = new string(beforeAt, 200_000) + "@" + new string(afterAt, 200_000);

        PiiMasker.Mask(value).ShouldBeSameAs(value);
    }
}
