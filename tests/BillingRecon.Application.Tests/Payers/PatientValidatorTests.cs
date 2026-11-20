using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Tests.Support;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Tests.Payers;

/// <summary>
/// The shape of the patient commands and queries: what a request must hold before a use case looks at the database.
/// Each case names the field the client finds the message under.
/// </summary>
public sealed class PatientValidatorTests
{
    private readonly RegisterPatientCommandValidator _register = new(TestDoubles.ClockAt(TestDoubles.Today));
    private readonly UpdatePatientCommandValidator _update = new(TestDoubles.ClockAt(TestDoubles.Today));
    private readonly ListPatientsQueryValidator _list = new();

    [Fact]
    public async Task A_complete_registration_is_valid()
    {
        (await _register.GetErrorAsync(Complete(), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Only_the_mrn_and_the_full_name_are_required()
    {
        var minimal = new RegisterPatientCommand { Mrn = "MRN-1", FullName = "Priya Sharma" };

        (await _register.GetErrorAsync(minimal, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("MRN\t1")]
    [InlineData("1234567890123456789012345678901")]
    public async Task BR101_an_mrn_that_is_missing_too_long_or_has_a_control_character_is_refused(string mrn)
    {
        await ShouldRefuseAsync(Complete() with { Mrn = mrn }, "mrn");
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    public async Task A_missing_full_name_is_refused(string fullName)
    {
        await ShouldRefuseAsync(Complete() with { FullName = fullName }, "fullName");
    }

    [Fact]
    public async Task A_full_name_is_measured_without_its_surrounding_white_space()
    {
        var exactly200 = new string('a', Patient.FullNameMaxLength);

        (await _register.GetErrorAsync(Complete() with { FullName = $"  {exactly200}  " }, CancellationToken.None)).ShouldBeNull();
        await ShouldRefuseAsync(Complete() with { FullName = exactly200 + "a" }, "fullName");
    }

    [Fact]
    public async Task A_date_of_birth_may_be_today_but_not_tomorrow()
    {
        (await _register.GetErrorAsync(Complete() with { DateOfBirth = TestDoubles.Today }, CancellationToken.None)).ShouldBeNull();
        await ShouldRefuseAsync(Complete() with { DateOfBirth = TestDoubles.Today.AddDays(1) }, "dateOfBirth");
    }

    [Fact]
    public async Task A_date_of_birth_before_1900_is_refused()
    {
        (await _register.GetErrorAsync(Complete() with { DateOfBirth = new DateOnly(1900, 1, 1) }, CancellationToken.None)).ShouldBeNull();
        await ShouldRefuseAsync(Complete() with { DateOfBirth = new DateOnly(1899, 12, 31) }, "dateOfBirth");
    }

    [Fact]
    public async Task A_sex_the_enum_does_not_define_is_refused()
    {
        await ShouldRefuseAsync(Complete() with { Sex = (Sex)42 }, "sex");
    }

    [Theory]
    [InlineData("+91 98765 43210")]
    [InlineData("9876543210")]
    [InlineData("020-2612 3456")]
    [InlineData("(022) 2345 6789")]
    [InlineData("  +91 98765 43210  ")]
    public async Task A_phone_number_of_seven_to_fifteen_digits_with_the_usual_separators_is_accepted(string phone)
    {
        (await _register.GetErrorAsync(Complete() with { Phone = phone }, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567890123456")]
    [InlineData("98765x43210")]
    [InlineData("91+9876543210")]
    [InlineData("+91 98765 43210 1234")]
    public async Task A_phone_number_with_too_few_or_too_many_digits_or_other_characters_is_refused(string phone)
    {
        await ShouldRefuseAsync(Complete() with { Phone = phone }, "phone");
    }

    [Theory]
    [InlineData("priya@example.com")]
    [InlineData("p.sharma+billing@hospital.co.in")]
    [InlineData(" priya@example.com ")]
    public async Task An_email_address_is_accepted(string email)
    {
        (await _register.GetErrorAsync(Complete() with { Email = email }, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData("priya")]
    [InlineData("priya@")]
    [InlineData("@example.com")]
    [InlineData("priya@example")]
    [InlineData("priya@.example.com")]
    [InlineData("pri ya@example.com")]
    [InlineData("a@b@example.com")]
    public async Task Something_that_is_not_an_email_address_is_refused(string email)
    {
        await ShouldRefuseAsync(Complete() with { Email = email }, "email");
    }

    [Fact]
    public async Task An_email_address_longer_than_the_column_is_refused()
    {
        var email = new string('p', Patient.EmailMaxLength) + "@example.com";

        await ShouldRefuseAsync(Complete() with { Email = email }, "email");
    }

    [Theory]
    [InlineData("411001")]
    [InlineData("SW1A 1AA")]
    [InlineData("12345-6789")]
    public async Task An_indian_pin_code_or_a_foreign_postal_code_is_accepted(string pinCode)
    {
        (await _register.GetErrorAsync(Complete() with { PinCode = pinCode }, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData("४११००१")]
    [InlineData("41100@")]
    [InlineData("12345678901")]
    [InlineData("--")]
    public async Task A_pin_code_outside_ascii_letters_and_digits_or_too_long_is_refused(string pinCode)
    {
        await ShouldRefuseAsync(Complete() with { PinCode = pinCode }, "pinCode");
    }

    [Theory]
    [InlineData(nameof(RegisterPatientCommand.AddressLine1), "addressLine1", Patient.AddressLineMaxLength)]
    [InlineData(nameof(RegisterPatientCommand.AddressLine2), "addressLine2", Patient.AddressLineMaxLength)]
    [InlineData(nameof(RegisterPatientCommand.City), "city", Patient.CityMaxLength)]
    [InlineData(nameof(RegisterPatientCommand.State), "state", Patient.StateMaxLength)]
    public async Task An_optional_text_longer_than_its_column_is_refused(string property, string field, int maxLength)
    {
        var tooLong = new string('x', maxLength + 1);
        var command = property switch
        {
            nameof(RegisterPatientCommand.AddressLine1) => Complete() with { AddressLine1 = tooLong },
            nameof(RegisterPatientCommand.AddressLine2) => Complete() with { AddressLine2 = tooLong },
            nameof(RegisterPatientCommand.City) => Complete() with { City = tooLong },
            _ => Complete() with { State = tooLong },
        };

        await ShouldRefuseAsync(command, field);
    }

    [Fact]
    public async Task Every_invalid_field_is_reported_at_once()
    {
        var error = await _register.GetErrorAsync(new RegisterPatientCommand { Mrn = string.Empty, FullName = string.Empty, Email = "nope" }, CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["mrn", "fullName", "email"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_correction_needs_the_version_the_client_read_and_follows_the_same_rules_as_a_registration()
    {
        var update = new UpdatePatientCommand { Id = new PatientId(Guid.NewGuid()), ExpectedVersion = "AAAAAAAAB9E=", FullName = "Priya Sharma" };

        (await _update.GetErrorAsync(update, CancellationToken.None)).ShouldBeNull();

        var error = await _update.GetErrorAsync(update with { ExpectedVersion = string.Empty, FullName = " ", Phone = "123" }, CancellationToken.None);
        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["expectedVersion", "fullName", "phone"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_list_query_with_the_defaults_is_valid()
    {
        (await _list.GetErrorAsync(new ListPatientsQuery(), CancellationToken.None)).ShouldBeNull();
        (await _list.GetErrorAsync(new ListPatientsQuery { PageSize = Paging.MaxPageSize, Page = Paging.MaxPage }, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(1_000_001, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public async Task A_page_outside_the_limits_is_refused(int page, int pageSize, string field)
    {
        var error = await _list.GetErrorAsync(new ListPatientsQuery { Page = page, PageSize = pageSize }, CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe([field]);
    }

    [Fact]
    public async Task A_search_longer_than_two_hundred_characters_or_an_unknown_status_is_refused()
    {
        var error = await _list.GetErrorAsync(
            new ListPatientsQuery { Search = new string('s', ListPatientsQuery.SearchMaxLength + 1), Status = (PatientStatus)7 },
            CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["search", "status"], ignoreOrder: true);
    }

    private async Task ShouldRefuseAsync(RegisterPatientCommand command, string field)
    {
        var error = await _register.GetErrorAsync(command, CancellationToken.None);

        error.ShouldNotBeNull($"the command should have been refused on {field}");
        error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        error.FieldErrors.Keys.ShouldBe([field]);
    }

    private static RegisterPatientCommand Complete() => new()
    {
        Mrn = "MRN-000123",
        FullName = "Priya Sharma",
        DateOfBirth = new DateOnly(1990, 5, 12),
        Sex = Sex.Female,
        Phone = "+91 98765 43210",
        Email = "priya.sharma@example.com",
        AddressLine1 = "12 MG Road",
        AddressLine2 = "Camp",
        City = "Pune",
        State = "Maharashtra",
        PinCode = "411001",
    };
}
