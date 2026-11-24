using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Domain.Tests.Identity;

public sealed class TenantTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 3, 6, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_tenant_is_active_and_has_the_initial_settings()
    {
        var result = Tenant.Create("city-hospital", "City Hospital", CreatedAt);

        result.IsSuccess.ShouldBeTrue();
        var tenant = result.Value;
        tenant.Code.ShouldBe("city-hospital");
        tenant.Name.ShouldBe("City Hospital");
        tenant.Status.ShouldBe(TenantStatus.Active);
        tenant.PostingApprovalThreshold.ShouldBeNull();
        tenant.MaxBackdateDays.ShouldBe(7);
        tenant.DefaultDueDays.ShouldBe(0);
        tenant.BillPrefix.ShouldBe("BIL");
        tenant.CreditNotePrefix.ShouldBe("CN");
        tenant.PaymentPrefix.ShouldBe("PAY");
        tenant.AdjustmentPrefix.ShouldBe("ADJ");
        tenant.RefundPrefix.ShouldBe("REF");
        tenant.CreatedAtUtc.ShouldBe(CreatedAt);
    }

    [Fact]
    public void A_new_tenant_has_no_id_no_row_version_and_no_events_until_it_is_added_and_saved()
    {
        var tenant = Tenant.Create("city-hospital", "City Hospital", CreatedAt).Value;

        tenant.Id.ShouldBe(default);
        tenant.Id.Value.ShouldBe(Guid.Empty);
        tenant.RowVersion.ShouldBeEmpty();
        tenant.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("7")]
    [InlineData("c1")]
    [InlineData("city-hospital")]
    [InlineData("a-b-c")]
    [InlineData("12345678901234567890")]
    public void A_code_of_lower_case_letters_digits_and_single_hyphens_is_accepted(string code)
    {
        var result = Tenant.Create(code, "City Hospital", CreatedAt);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Code.ShouldBe(code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("City")]
    [InlineData("city_hospital")]
    [InlineData("city hospital")]
    [InlineData(" city")]
    [InlineData("-city")]
    [InlineData("city-")]
    [InlineData("city--hospital")]
    [InlineData("-")]
    [InlineData("citÿ")]
    [InlineData("city.hospital")]
    [InlineData("123456789012345678901")]
    public void Any_other_code_is_a_validation_failure_on_the_code_field(string? code)
    {
        var result = Tenant.Create(code!, "City Hospital", CreatedAt);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(IdentityErrors.TenantInvalidCode);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.FieldErrors.Keys.ShouldBe(["code"]);
    }

    [Fact]
    public void The_name_is_stored_without_surrounding_white_space()
    {
        var result = Tenant.Create("city-hospital", "  City Hospital \t", CreatedAt);

        result.Value.Name.ShouldBe("City Hospital");
    }

    [Fact]
    public void A_name_of_200_characters_is_accepted()
    {
        var name = new string('n', 200);

        var result = Tenant.Create("city-hospital", name, CreatedAt);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe(name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(201)]
    public void A_missing_blank_or_too_long_name_is_a_validation_failure_on_the_name_field(object? name)
    {
        // A number stands for a name of that many characters.
        var text = name is int length ? new string('n', length) : (string?)name;

        var result = Tenant.Create("city-hospital", text!, CreatedAt);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(IdentityErrors.TenantInvalidCode);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.FieldErrors.Keys.ShouldBe(["name"]);
    }

    [Fact]
    public void Every_invalid_field_is_reported_at_once()
    {
        var result = Tenant.Create("Not A Code", " ", CreatedAt);

        result.IsFailure.ShouldBeTrue();
        result.Error.FieldErrors.Keys.OrderBy(key => key, StringComparer.Ordinal).ShouldBe(["code", "name"]);
    }

    [Fact]
    public void An_instant_that_is_not_utc_is_a_bug_and_throws()
    {
        var indiaTime = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.FromMinutes(330));

        var exception = Should.Throw<ArgumentException>(() => Tenant.Create("city-hospital", "City Hospital", indiaTime));

        exception.ParamName.ShouldBe("createdAtUtc");
    }

    [Fact]
    public void US03_the_settings_change_together_and_only_a_change_is_an_event()
    {
        var tenant = Tenant.Create("city-hospital", "City Hospital", CreatedAt).Value;

        tenant.ChangeSettings(null, 7, 0).ShouldBeFalse("nothing changed");
        tenant.DomainEvents.ShouldBeEmpty();

        tenant.ChangeSettings(Money.Of(50_000m), 3, 15).ShouldBeTrue();

        (tenant.PostingApprovalThreshold, tenant.MaxBackdateDays, tenant.DefaultDueDays).ShouldBe((Money.Of(50_000m), 3, 15));
        tenant.DomainEvents.ShouldHaveSingleItem().ShouldBe(new TenantSettingsChanged(Money.Of(50_000m), 3, 15));

        tenant.ChangeSettings(null, 0, 365).ShouldBeTrue("the threshold goes back to none; the days reach both limits");
        tenant.PostingApprovalThreshold.ShouldBeNull();
    }

    [Fact]
    public void BR607_settings_outside_their_limits_are_a_bug_in_the_caller()
    {
        var tenant = Tenant.Create("city-hospital", "City Hospital", CreatedAt).Value;

        Should.Throw<ArgumentOutOfRangeException>(() => tenant.ChangeSettings(Money.Zero, 7, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => tenant.ChangeSettings(null, -1, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => tenant.ChangeSettings(null, 7, Tenant.MaxDays + 1));
    }

    [Fact]
    public void Each_document_type_has_its_default_prefix()
    {
        var tenant = Tenant.Create("city-hospital", "City Hospital", CreatedAt).Value;

        Enum.GetValues<DocumentType>().Select(tenant.PrefixOf).ShouldBe(["BIL", "CN", "PAY", "ADJ", "REF"]);
        tenant.Prefixes.Keys.ShouldBe(Enum.GetValues<DocumentType>());
    }

    [Fact]
    public void BR802_an_approved_change_gives_one_type_a_new_default_prefix_and_says_so()
    {
        var tenant = Tenant.Create("city-hospital", "City Hospital", CreatedAt).Value;

        tenant.ChangePrefix(DocumentType.Bill, "INV");

        tenant.BillPrefix.ShouldBe("INV");
        tenant.PrefixOf(DocumentType.CreditNote).ShouldBe("CN", "the other types keep theirs");
        tenant.DomainEvents.ShouldHaveSingleItem().ShouldBe(new TenantPrefixChanged(DocumentType.Bill, "BIL", "INV"));
    }

    [Fact]
    public void ADR0033_a_default_prefix_never_belongs_to_two_types_and_a_change_that_changes_nothing_is_a_bug()
    {
        var tenant = Tenant.Create("city-hospital", "City Hospital", CreatedAt).Value;

        Should.Throw<InvalidOperationException>(() => tenant.ChangePrefix(DocumentType.Refund, "PAY"));
        Should.Throw<ArgumentException>(() => tenant.ChangePrefix(DocumentType.Bill, "BIL"));
        Should.Throw<ArgumentException>(() => tenant.ChangePrefix(DocumentType.Bill, "inv"));
        Should.Throw<ArgumentException>(() => tenant.ChangePrefix(DocumentType.Bill, "INVO"));
        tenant.RefundPrefix.ShouldBe("REF");
        tenant.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ADR0052_an_onboarded_tenant_records_its_onboarding()
    {
        var tenant = Tenant.Onboard("city-hospital", " City Hospital ", CreatedAt).Value;

        tenant.Status.ShouldBe(TenantStatus.Active);
        tenant.DomainEvents.ShouldHaveSingleItem().ShouldBe(new TenantOnboarded("city-hospital", "City Hospital"));
    }

    [Fact]
    public void ADR0052_an_onboarding_with_a_wrong_code_records_nothing_and_says_why()
    {
        var result = Tenant.Onboard("City Hospital", "City Hospital", CreatedAt);

        result.Error.Code.ShouldBe(IdentityErrors.TenantInvalidCode);
        result.Error.FieldErrors.Keys.ShouldBe(["code"]);
    }

    [Fact]
    public void ADR0052_a_suspension_and_its_lifting_are_recorded_with_their_reasons()
    {
        var tenant = Tenant.Create("city-hospital", "City Hospital", CreatedAt).Value;

        tenant.Suspend(" Contract ended ").ShouldBeTrue();
        tenant.Status.ShouldBe(TenantStatus.Suspended);
        tenant.Resume("Contract renewed").ShouldBeTrue();

        tenant.Status.ShouldBe(TenantStatus.Active);
        tenant.DomainEvents.ShouldBe([new TenantSuspended("Contract ended"), new TenantResumed("Contract renewed")]);
    }

    [Fact]
    public void ADR0052_suspending_a_suspended_tenant_or_resuming_an_active_one_records_nothing()
    {
        var tenant = Tenant.Create("city-hospital", "City Hospital", CreatedAt).Value;

        tenant.Resume("Nothing to lift").ShouldBeFalse();
        tenant.Suspend("Contract ended").ShouldBeTrue();
        tenant.Suspend("Again").ShouldBeFalse();

        tenant.DomainEvents.ShouldHaveSingleItem().ShouldBe(new TenantSuspended("Contract ended"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ADR0052_a_suspension_needs_a_reason(string reason)
    {
        var tenant = Tenant.Create("city-hospital", "City Hospital", CreatedAt).Value;

        Should.Throw<ArgumentException>(() => tenant.Suspend(reason));
        tenant.Status.ShouldBe(TenantStatus.Active);
    }
}
