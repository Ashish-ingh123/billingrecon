using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Domain.Tests.Payers;

/// <summary>The Payer aggregate (BR-103 to BR-105): self-pay only in R1, linked to a patient or a guarantor, never deleted.</summary>
public sealed class PayerTests
{
    private static readonly PatientId SomePatient = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301"));

    [Fact]
    public void BR104_a_self_pay_payer_for_a_patient_is_created_active_and_linked_to_them()
    {
        var payer = Payer.Create(PayerKind.SelfPay, Details(), SomePatient).Value;

        payer.Kind.ShouldBe(PayerKind.SelfPay);
        payer.Name.ShouldBe("Priya Sharma");
        payer.PatientId.ShouldBe(SomePatient);
        payer.Phone.ShouldBe("+91 98765 43210");
        payer.Email.ShouldBe("priya.sharma@example.com");
        payer.Status.ShouldBe(PayerStatus.Active);
    }

    [Fact]
    public void BR104_a_guarantor_is_a_self_pay_payer_without_a_patient()
    {
        var payer = Payer.Create(PayerKind.SelfPay, Details() with { Name = "Rahul Sharma" }, patientId: null).Value;

        payer.PatientId.ShouldBeNull();
        payer.Name.ShouldBe("Rahul Sharma");
    }

    [Theory]
    [InlineData(PayerKind.Insurer)]
    [InlineData(PayerKind.Corporate)]
    public void BR103_insurers_and_corporate_accounts_are_refused_in_R1(PayerKind kind)
    {
        var result = Payer.Create(kind, Details(), patientId: null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PayersErrors.PayerKindNotSupportedCode);
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
    }

    [Fact]
    public void Creating_raises_PayerCreated_with_the_kind_the_name_and_the_link()
    {
        var payer = Payer.Create(PayerKind.SelfPay, Details(), SomePatient).Value;

        payer.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PayerCreated(PayerKind.SelfPay, "Priya Sharma", SomePatient));
    }

    [Fact]
    public void A_new_payer_has_no_id_no_tenant_and_no_row_version_until_it_is_saved()
    {
        var payer = Payer.Create(PayerKind.SelfPay, Details(), patientId: null).Value;

        payer.Id.ShouldBe(default);
        payer.TenantId.ShouldBe(default);
        payer.RowVersion.ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_patient_id_is_a_bug_and_throws()
    {
        Should.Throw<ArgumentException>(() => Payer.Create(PayerKind.SelfPay, Details(), new PatientId(Guid.Empty))).ParamName.ShouldBe("patientId");
    }

    [Fact]
    public void A_kind_the_enum_does_not_define_is_a_bug_and_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Payer.Create((PayerKind)42, Details(), patientId: null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_payer_without_a_name_is_a_bug_and_throws(string name)
    {
        Should.Throw<ArgumentException>(() => Payer.Create(PayerKind.SelfPay, Details() with { Name = name }, patientId: null));
    }

    [Fact]
    public void Text_is_stored_without_surrounding_white_space_and_a_blank_contact_detail_as_null()
    {
        var payer = Payer.Create(PayerKind.SelfPay, new PayerDetails { Name = "  Priya Sharma ", Phone = " ", Email = null }, patientId: null).Value;

        payer.Name.ShouldBe("Priya Sharma");
        payer.Phone.ShouldBeNull();
        payer.Email.ShouldBeNull();
    }

    [Fact]
    public void Updating_replaces_the_name_and_contact_details_and_keeps_the_kind_the_link_and_the_status()
    {
        var payer = Payer.Create(PayerKind.SelfPay, Details(), SomePatient).Value;
        payer.Deactivate();

        payer.Update(new PayerDetails { Name = "Priya S. Rao", Email = "priya.rao@example.com" });

        payer.Name.ShouldBe("Priya S. Rao");
        payer.Phone.ShouldBeNull();
        payer.Email.ShouldBe("priya.rao@example.com");
        payer.Kind.ShouldBe(PayerKind.SelfPay);
        payer.PatientId.ShouldBe(SomePatient);
        payer.Status.ShouldBe(PayerStatus.Inactive);
    }

    [Fact]
    public void Updating_raises_PayerUpdated_when_a_value_changed_and_nothing_when_none_did()
    {
        var payer = Payer.Create(PayerKind.SelfPay, Details(), patientId: null).Value;
        payer.ClearDomainEvents();

        payer.Update(Details());
        payer.DomainEvents.ShouldBeEmpty("the same details change nothing");

        payer.Update(Details() with { Name = "Priya S. Rao" });
        payer.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PayerUpdated(payer.Id, PayerKind.SelfPay, "Priya S. Rao"));
    }

    [Fact]
    public void BR105_deactivating_a_payer_makes_it_inactive_and_raises_PayerDeactivated()
    {
        var payer = Payer.Create(PayerKind.SelfPay, Details(), patientId: null).Value;
        payer.ClearDomainEvents();

        payer.Deactivate().IsSuccess.ShouldBeTrue();

        payer.Status.ShouldBe(PayerStatus.Inactive);
        payer.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PayerDeactivated(payer.Id, PayerKind.SelfPay, "Priya Sharma"));
    }

    [Fact]
    public void BR105_deactivating_an_inactive_payer_fails_and_raises_nothing()
    {
        var payer = Payer.Create(PayerKind.SelfPay, Details(), patientId: null).Value;
        payer.Deactivate();
        payer.ClearDomainEvents();

        var result = payer.Deactivate();

        result.Error.Code.ShouldBe(PayersErrors.PayerAlreadyInactiveCode);
        payer.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Reactivating_an_inactive_payer_makes_it_active_and_raises_PayerReactivated()
    {
        var payer = Payer.Create(PayerKind.SelfPay, Details(), patientId: null).Value;
        payer.Deactivate();
        payer.ClearDomainEvents();

        payer.Reactivate().IsSuccess.ShouldBeTrue();

        payer.Status.ShouldBe(PayerStatus.Active);
        payer.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PayerReactivated(payer.Id, PayerKind.SelfPay, "Priya Sharma"));
    }

    [Fact]
    public void Reactivating_an_active_payer_fails_and_raises_nothing()
    {
        var payer = Payer.Create(PayerKind.SelfPay, Details(), patientId: null).Value;
        payer.ClearDomainEvents();

        payer.Reactivate().Error.Code.ShouldBe(PayersErrors.PayerAlreadyActiveCode);
        payer.DomainEvents.ShouldBeEmpty();
    }

    private static PayerDetails Details() => new()
    {
        Name = "Priya Sharma",
        Phone = "+91 98765 43210",
        Email = "priya.sharma@example.com",
    };
}
