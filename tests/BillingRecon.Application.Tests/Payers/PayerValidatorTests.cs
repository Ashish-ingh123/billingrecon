using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Tests.Payers;

/// <summary>The shape of the payer commands and queries. That only self-pay payers exist is a business rule, not a shape (BR-103).</summary>
public sealed class PayerValidatorTests
{
    private readonly CreatePayerCommandValidator _create = new();
    private readonly UpdatePayerCommandValidator _update = new();
    private readonly ListPayersQueryValidator _list = new();

    [Fact]
    public async Task A_payer_for_a_patient_and_a_guarantor_are_both_valid()
    {
        (await _create.GetErrorAsync(Create(), CancellationToken.None)).ShouldBeNull();
        (await _create.GetErrorAsync(Create() with { PatientId = null }, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_payer_without_a_kind_is_refused_rather_than_defaulted()
    {
        var error = await _create.GetErrorAsync(Create() with { Kind = null }, CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["kind"]);
    }

    [Fact]
    public async Task BR103_the_reserved_kinds_pass_the_shape_check_and_are_left_to_the_business_rule()
    {
        // Insurer and Corporate are names the API knows; refusing them is BR-103, which answers 422, not 400.
        (await _create.GetErrorAsync(Create() with { Kind = PayerKind.Insurer }, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_kind_the_enum_does_not_define_is_refused()
    {
        var error = await _create.GetErrorAsync(Create() with { Kind = (PayerKind)42 }, CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["kind"]);
    }

    [Fact]
    public async Task An_empty_patient_id_is_refused()
    {
        var error = await _create.GetErrorAsync(Create() with { PatientId = new PatientId(Guid.Empty) }, CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["patientId"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_payer_without_a_name_is_refused(string name)
    {
        var error = await _create.GetErrorAsync(Create() with { Name = name }, CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["name"]);
    }

    [Fact]
    public async Task The_contact_details_follow_the_same_rules_as_a_patients()
    {
        var error = await _create.GetErrorAsync(Create() with { Phone = "12", Email = "nope" }, CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["phone", "email"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_correction_needs_the_version_the_client_read_and_a_name()
    {
        var update = new UpdatePayerCommand { Id = new PayerId(Guid.NewGuid()), ExpectedVersion = "AAAAAAAAB9E=", Name = "Priya Sharma" };

        (await _update.GetErrorAsync(update, CancellationToken.None)).ShouldBeNull();

        var error = await _update.GetErrorAsync(update with { ExpectedVersion = string.Empty, Name = string.Empty }, CancellationToken.None);
        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["expectedVersion", "name"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_list_query_checks_the_page_the_search_the_status_and_the_patient()
    {
        (await _list.GetErrorAsync(new ListPayersQuery(), CancellationToken.None)).ShouldBeNull();

        var error = await _list.GetErrorAsync(
            new ListPayersQuery
            {
                Page = 0,
                PageSize = Paging.MaxPageSize + 1,
                Search = new string('s', ListPayersQuery.SearchMaxLength + 1),
                Status = (PayerStatus)7,
                PatientId = new PatientId(Guid.Empty),
            },
            CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["page", "pageSize", "search", "status", "patientId"], ignoreOrder: true);
    }

    private static CreatePayerCommand Create() => new()
    {
        Kind = PayerKind.SelfPay,
        PatientId = new PatientId(Guid.NewGuid()),
        Name = "Priya Sharma",
        Phone = "+91 98765 43210",
        Email = "priya.sharma@example.com",
    };
}
